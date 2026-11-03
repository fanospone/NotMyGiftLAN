
using Microsoft.AspNetCore.SignalR;
using NotMyGiftLAN.Models;
using NotMyGiftLAN.Services;

namespace NotMyGiftLAN.Hubs;

public sealed class GameHub(GameStore store) : Hub
{
    private static HubException Fail(string message) => new(message);

    private static string Clean(string? value, int max)
    {
        var result = (value ?? "").Trim();
        return result[..Math.Min(result.Length, max)];
    }

    private (GameRoom room, Player player) Auth(string code, string token)
    {
        if (!store.Rooms.TryGetValue(code.ToUpperInvariant(), out var room))
            throw Fail("Room tidak ditemukan.");

        var player = room.Players.FirstOrDefault(p =>
            p.Id == token && p.ConnectionId == Context.ConnectionId);

        if (player == null)
            throw Fail("Sesi tidak valid. Silakan join ulang.");

        return (room, player);
    }

    private async Task Broadcast(GameRoom room)
    {
        foreach (var player in room.Players.Where(p => p.ConnectionId != null))
        {
            await Clients.Client(player.ConnectionId!)
                .SendAsync("State", GameStore.View(room, player.Id));
        }
    }

    private static int? Draw(GameRoom room)
    {
        if (room.Deck.Count == 0)
            return null;

        var card = room.Deck[^1];
        room.Deck.RemoveAt(room.Deck.Count - 1);

        return card.Id;
    }

    private static int GetPlayerIndex(GameRoom room, string playerId)
    {
        return room.Players.FindIndex(p => p.Id == playerId);
    }

    // Left = pemain berikutnya dalam urutan kursi.
    // Right = pemain sebelumnya dalam urutan kursi.
    private static Player GetTargetByDirection(
        GameRoom room,
        Player sender,
        string? targetId)
    {
        var count = room.Players.Count;
        var senderIndex = GetPlayerIndex(room, sender.Id);

        var card = room.Cards[room.DrawnCardId!.Value];

        if (card.Direction == "Left")
            return room.Players[(senderIndex + 1) % count];

        if (card.Direction == "Right")
            return room.Players[(senderIndex - 1 + count) % count];

        if (card.Direction == "Any")
        {
            if (string.IsNullOrWhiteSpace(targetId))
                throw Fail("Pilih pemain tujuan untuk kartu Any.");

            var target = room.Players.FirstOrDefault(p => p.Id == targetId);

            if (target == null || target.Id == sender.Id)
                throw Fail("Pemain tujuan tidak valid.");

            return target;
        }

        throw Fail("Arah kartu tidak valid.");
    }

    private static void FinishGame(GameRoom room, string message)
    {
        room.Phase = "finished";

        var minimum = room.Players.Min(p => p.Penalties);
        var winners = room.Players
            .Where(p => p.Penalties == minimum)
            .ToList();

        room.WinnerId = winners.Count == 1
            ? winners[0].Id
            : null;

        GameStore.AddLog(room, message);

        if (winners.Count > 1)
        {
            GameStore.AddLog(room,
                "Hasil seri! Ada lebih dari satu pemain dengan penalti paling sedikit.");
        }
        else
        {
            GameStore.AddLog(room,
                $"Pemenang: {winners[0].Name}!");
        }
    }

    public async Task<object> CreateRoom(string name, string token)
    {
        name = Clean(name, 20);

        if (name.Length < 2 || token.Length < 12)
            throw Fail("Nama minimal 2 karakter.");

        GameRoom room;
        Player player;

        lock (store.Gate)
        {
            var code = GameStore.Code();

            while (store.Rooms.ContainsKey(code))
                code = GameStore.Code();

            player = new Player
            {
                Id = token,
                Name = name,
                ConnectionId = Context.ConnectionId
            };

            room = new GameRoom
            {
                Code = code,
                HostId = token
            };

            room.Players.Add(player);
            store.Rooms[code] = room;

            GameStore.AddLog(room, $"{name} membuat room.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Broadcast(room);

        return new { code = room.Code, id = player.Id };
    }

    public async Task<object> JoinRoom(string code, string name, string token)
    {
        code = Clean(code, 6).ToUpperInvariant();
        name = Clean(name, 20);

        if (name.Length < 2 || token.Length < 12)
            throw Fail("Nama minimal 2 karakter.");

        GameRoom room;
        Player player;

        lock (store.Gate)
        {
            if (!store.Rooms.TryGetValue(code, out var found))
                throw Fail("Kode room tidak ditemukan.");

            room = found;

            player = room.Players.FirstOrDefault(p => p.Id == token)!;

            if (player == null)
            {
                if (room.Phase != "lobby")
                    throw Fail("Permainan sudah dimulai. Hanya pemain lama yang bisa masuk kembali.");

                if (room.Players.Count >= 8)
                    throw Fail("Room sudah penuh (8 pemain).");

                if (room.Players.Any(p =>
                    p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw Fail("Nama sudah dipakai.");

                player = new Player
                {
                    Id = token,
                    Name = name
                };

                room.Players.Add(player);
                GameStore.AddLog(room, $"{name} bergabung.");
            }

            player.ConnectionId = Context.ConnectionId;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await Broadcast(room);

        return new { code = room.Code, id = player.Id };
    }

    public async Task StartGame(string code, string token)
    {
        GameRoom room;

        lock (store.Gate)
        {
            (room, _) = Auth(code, token);

            if (room.HostId != token || room.Phase != "lobby")
                throw Fail("Hanya host yang dapat memulai.");

            if (room.Players.Count < 3)
                throw Fail("Minimal 3 pemain.");

            if (room.Players.Count > 8)
                throw Fail("Maksimal 8 pemain.");

            if (room.Players.Any(p => !p.Connected))
                throw Fail("Semua pemain harus online.");

            room.Deck = GameStore.NewDeck();
            room.Cards.Clear();
            room.DiscardedCardIds.Clear();

            foreach (var card in room.Deck)
                room.Cards[card.Id] = card;

            // Setiap pemain mendapat satu kartu awal.
            foreach (var player in room.Players)
            {
                player.Penalties = 0;
                player.CardId = Draw(room);
            }

            room.TurnIndex = 0;
            room.Round = 1;
            room.Pending = null;
            room.DrawnCardId = null;
            room.WinnerId = null;
            room.Phase = "memorize";

            GameStore.AddLog(room,
                "Permainan dimulai. Hafalkan kartu masing-masing!");
        }

        await Broadcast(room);
    }

    public async Task HideCards(string code, string token)
    {
        GameRoom room;

        lock (store.Gate)
        {
            (room, _) = Auth(code, token);

            if (room.HostId != token || room.Phase != "memorize")
                throw Fail("Hanya host dapat menutup kartu.");

            // Kartu awal tetap dipegang masing-masing pemain.
            // Pemain pertama mengambil kartu baru dari Gift Shop.
            room.DrawnCardId = Draw(room);

            if (!room.DrawnCardId.HasValue)
            {
                FinishGame(room, "Gift Shop kosong. Permainan selesai.");
            }
            else
            {
                room.Phase = "play";

                GameStore.AddLog(room,
                    $"{room.Players[room.TurnIndex].Name} mengambil kartu dari Gift Shop.");
            }
        }

        await Broadcast(room);
    }

    public async Task GiveGift(
        string code,
        string token,
        string claimedName,
        string? targetId = null)
    {
        GameRoom room;

        lock (store.Gate)
        {
            (room, var sender) = Auth(code, token);

            if (room.Phase != "play" ||
                room.Pending != null ||
                room.Players[room.TurnIndex].Id != token)
            {
                throw Fail("Bukan giliran Anda.");
            }

            if (!room.DrawnCardId.HasValue)
                throw Fail("Tidak ada kartu yang harus diberikan.");

            claimedName = Clean(claimedName, 30);

            if (!GameStore.Gifts.Any(g =>
                g.Split('|')[0] == claimedName))
            {
                throw Fail("Pilih nama barang yang tersedia.");
            }

            var target = GetTargetByDirection(room, sender, targetId);

            var cardId = room.DrawnCardId.Value;
            var card = room.Cards[cardId];

            room.Pending = new Offer
            {
                FromId = sender.Id,
                ToId = target.Id,
                CardId = cardId,
                ClaimedName = claimedName
            };

            // Kartu sudah diberikan, tidak lagi berada di tangan pengirim.
            room.DrawnCardId = null;

            GameStore.AddLog(room,
                $"{sender.Name} memberikan kartu kepada {target.Name} " +
                $"dan menyebutnya '{claimedName}'. " +
                $"Arah kartu: {card.Direction}.");
        }

        await Broadcast(room);
    }

    public async Task Respond(string code, string token, bool challenge)
    {
        GameRoom room;

        lock (store.Gate)
        {
            (room, var recipient) = Auth(code, token);

            var offer = room.Pending;

            if (room.Phase != "play" ||
                offer == null ||
                offer.ToId != token)
            {
                throw Fail("Tidak ada hadiah untuk Anda.");
            }

            var sender = room.Players.First(p => p.Id == offer.FromId);
            var offeredCard = room.Cards[offer.CardId];

            room.Pending = null;

            if (!challenge)
            {
                // Penerima menerima kartu yang ditawarkan.
                // Kartu aktif lamanya menjadi kartu yang harus diteruskan.
                var oldCardId = recipient.CardId;

                recipient.CardId = offer.CardId;
                room.DrawnCardId = oldCardId;

                if (!room.DrawnCardId.HasValue)
                {
                    FinishGame(room, "Kartu aktif penerima tidak tersedia.");
                }
                else
                {
                    room.TurnIndex = GetPlayerIndex(room, recipient.Id);

                    GameStore.AddLog(room,
                        $"{recipient.Name} menerima hadiah. " +
                        "Sekarang kartu aktif lamanya harus diteruskan.");
                }
            }
            else
            {
                // Kartu yang ditantang dibuka dan dibuang.
                bool claimCorrect =
                    offeredCard.Name.Equals(
                        offer.ClaimedName,
                        StringComparison.OrdinalIgnoreCase);

                var loser = claimCorrect ? recipient : sender;

                loser.Penalties++;

                room.DiscardedCardIds.Add(offer.CardId);

                GameStore.AddLog(room,
                    $"{recipient.Name} menantang! " +
                    $"Kartu sebenarnya: {offeredCard.Emoji} {offeredCard.Name}. " +
                    $"{loser.Name} mendapat penalti. " +
                    $"Total penalti: {loser.Penalties}/3.");

                // Kartu aktif pemain tidak berubah.
                // Pemain yang kalah mengambil kartu pengganti.
                if (loser.Penalties >= 3)
                {
                    FinishGame(room,
                        $"{loser.Name} mencapai 3 penalti. Permainan selesai.");
                }
                else
                {
                    room.DrawnCardId = Draw(room);
                    room.TurnIndex = GetPlayerIndex(room, loser.Id);

                    if (!room.DrawnCardId.HasValue)
                    {
                        FinishGame(room,
                            "Gift Shop kosong. Permainan selesai.");
                    }
                    else
                    {
                        GameStore.AddLog(room,
                            $"{loser.Name} mengambil kartu pengganti " +
                            "dan harus memberikannya kepada pemain lain.");
                    }
                }
            }

            if (room.Phase == "play" && room.TurnIndex == 0)
                room.Round++;
        }

        await Broadcast(room);
    }

    public async Task CloseRoom(string code, string token)
    {
        GameRoom room;

        lock (store.Gate)
        {
            (room, _) = Auth(code, token);

            if (room.HostId != token)
                throw Fail("Hanya host yang dapat menutup room.");

            // Hapus room agar tidak dapat dimasuki kembali.
            if (!store.Rooms.TryRemove(room.Code, out _))
                throw Fail("Room sudah ditutup atau tidak ditemukan.");

            GameStore.AddLog(room, "Host menutup room.");
        }

        // Beri tahu seluruh pemain yang masih berada di room.
        await Clients.Group(room.Code).SendAsync("RoomClosed");
    }

    public async Task BackToLobby(string code, string token)
    {
        GameRoom room;

        lock (store.Gate)
        {
            (room, _) = Auth(code, token);

            if (room.HostId != token || room.Phase != "finished")
                throw Fail("Hanya host dapat mengulang setelah selesai.");

            room.Phase = "lobby";
            room.Pending = null;
            room.DrawnCardId = null;
            room.WinnerId = null;
            room.Round = 0;
            room.TurnIndex = 0;

            room.Deck.Clear();
            room.Cards.Clear();
            room.DiscardedCardIds.Clear();

            foreach (var player in room.Players)
            {
                player.CardId = null;
                player.Penalties = 0;
            }

            GameStore.AddLog(room, "Kembali ke lobby.");
        }

        await Broadcast(room);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        List<GameRoom> changed = new();

        lock (store.Gate)
        {
            foreach (var room in store.Rooms.Values)
            {
                var player = room.Players.FirstOrDefault(p =>
                    p.ConnectionId == Context.ConnectionId);

                if (player == null)
                    continue;

                player.ConnectionId = null;

                if (room.Phase == "lobby")
                {
                    room.Players.Remove(player);

                    if (room.HostId == player.Id && room.Players.Count > 0)
                        room.HostId = room.Players[0].Id;

                    if (room.Players.Count == 0)
                        store.Rooms.TryRemove(room.Code, out _);
                }

                GameStore.AddLog(room, $"{player.Name} terputus.");
                changed.Add(room);
            }
        }

        foreach (var room in changed)
            await Broadcast(room);

        await base.OnDisconnectedAsync(exception);
    }
}
