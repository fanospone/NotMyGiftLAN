
using System.Collections.Concurrent;
using System.Security.Cryptography;
using NotMyGiftLAN.Models;

namespace NotMyGiftLAN.Services;

public sealed class GameStore
{
    public object Gate { get; } = new();

    public ConcurrentDictionary<string, GameRoom> Rooms { get; } = new();

    public const int TotalCards = 110;

    public static readonly string[] Gifts =
    {
        "Topi|🎩",
        "Payung|☂️",
        "Sepatu|👟",
        "Jam|⌚",
        "Kacamata|🕶️",
        "Boneka|🧸",
        "Buku|📚",
        "Kamera|📷",
        "Gitar|🎸",
        "Piala|🏆",
        "Pizza|🍕",
        "Kunci|🔑",
        "Bola|⚽",
        "Lampu|💡",
        "Cincin|💍",
        "Tas|🎒",
        "Bunga|🌷",
        "Kue|🎂",
        "Telepon|📱",
        "Kado|🎁",
        "Mobil|🚗",
        "Roket|🚀",
        "Permen|🍬",
        "Pisang|🍌"
    };

    private static readonly string[] Directions =
    {
        "Left",
        "Right",
        "Any"
    };

    public static List<GiftCard> NewDeck()
    {
        var cards = new List<GiftCard>();

        for (int i = 0; i < TotalCards; i++)
        {
            var gift = Gifts[i % Gifts.Length].Split('|');

            cards.Add(new GiftCard
            {
                Id = i + 1,
                Name = gift[0],
                Emoji = gift[1],
                Direction = Directions[
                    RandomNumberGenerator.GetInt32(Directions.Length)
                ]
            });
        }

        // Acak urutan kartu.
        return cards
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
            .ToList();
    }

    public static string Code()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        return new string(
            Enumerable.Range(0, 6)
                .Select(_ => chars[
                    RandomNumberGenerator.GetInt32(chars.Length)
                ])
                .ToArray()
        );
    }

    public static object View(GameRoom r, string viewer)
    {
        bool showCards = r.Phase == "memorize";

        var currentPlayer =
            r.Players.Count > 0
                ? r.Players[r.TurnIndex % r.Players.Count]
                : null;

        var players = r.Players.Select(p =>
        {
            GiftCard? card = null;

            if (showCards &&
                p.CardId.HasValue &&
                r.Cards.TryGetValue(p.CardId.Value, out var found))
            {
                card = found;
            }

            return new
            {
                id = p.Id,
                name = p.Name,
                connected = p.Connected,
                penalties = p.Penalties,

                card = card == null
                    ? null
                    : new
                    {
                        name = card.Name,
                        emoji = card.Emoji,
                        direction = card.Direction
                    },

                hasCard = p.CardId.HasValue
            };
        }).ToArray();

        var giftShop = r.Deck.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            emoji = c.Emoji,
            direction = c.Direction
        }).ToArray();

        object? drawnCard = null;

        if (r.DrawnCardId.HasValue &&
            r.Cards.TryGetValue(r.DrawnCardId.Value, out var drawn))
        {
            drawnCard = new
            {
                id = drawn.Id,
                name = drawn.Name,
                emoji = drawn.Emoji,
                direction = drawn.Direction
            };
        }

        object? pending = null;

        if (r.Pending != null &&
            r.Cards.TryGetValue(r.Pending.CardId, out var offered))
        {
            pending = new
            {
                fromId = r.Pending.FromId,
                toId = r.Pending.ToId,
                claim = r.Pending.ClaimedName,
                direction = offered.Direction
            };
        }

        return new
        {
            code = r.Code,
            phase = r.Phase,
            hostId = r.HostId,
            viewerId = viewer,
            round = r.Round,

            currentId = currentPlayer?.Id,

            players,

            pending,

            drawnCard,

            giftShop,

            deckCount = r.Deck.Count,

            winnerId = r.WinnerId,

            log = r.Log
                .TakeLast(15)
                .Reverse()
                .ToArray(),

            choices = Gifts
                .Select(s => s.Split('|')[0])
                .Distinct()
                .ToArray()
        };
    }

    public static void AddLog(GameRoom r, string message)
    {
        r.Log.Add(message);

        if (r.Log.Count > 100)
        {
            r.Log.RemoveAt(0);
        }

        r.LastTouched = DateTime.UtcNow;
    }
}
