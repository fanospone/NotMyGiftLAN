using Microsoft.AspNetCore.SignalR;
using NotMyGiftLAN.Models;
using NotMyGiftLAN.Services;

namespace NotMyGiftLAN.Hubs;
public sealed class GameHub(GameStore store) : Hub
{
    private static HubException Fail(string message) => new(message);
    private static string Clean(string? s, int max) => (s ?? "").Trim()[..Math.Min((s ?? "").Trim().Length,max)];
    private (GameRoom room,Player player) Auth(string code,string token)
    {
        if(!store.Rooms.TryGetValue(code.ToUpperInvariant(),out var r)) throw Fail("Room tidak ditemukan.");
        var p=r.Players.FirstOrDefault(p=>p.Id==token && p.ConnectionId==Context.ConnectionId);
        if(p==null) throw Fail("Sesi tidak valid. Silakan join ulang.");
        return (r,p);
    }
    private async Task Broadcast(GameRoom r)
    {
        foreach(var p in r.Players.Where(p=>p.ConnectionId!=null))
            await Clients.Client(p.ConnectionId!).SendAsync("State",GameStore.View(r,p.Id));
    }
    public async Task<object> CreateRoom(string name,string token)
    {
        name=Clean(name,20); if(name.Length<2 || token.Length<12) throw Fail("Nama minimal 2 karakter.");
        GameRoom r; Player p;
        lock(store.Gate)
        {
            var code=GameStore.Code(); while(store.Rooms.ContainsKey(code)) code=GameStore.Code();
            p=new Player {Id=token,Name=name,ConnectionId=Context.ConnectionId};
            r=new GameRoom {Code=code,HostId=token};r.Players.Add(p);
            store.Rooms[code]=r;GameStore.AddLog(r,$"{name} membuat room.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId,r.Code);
        await Broadcast(r);
        return new {code=r.Code,id=p.Id};
    }
    public async Task<object> JoinRoom(string code,string name,string token)
    {
        code=Clean(code,6).ToUpperInvariant();name=Clean(name,20);
        if(name.Length<2 || token.Length<12) throw Fail("Nama minimal 2 karakter.");
        GameRoom r; Player p;
        lock(store.Gate)
        {
            if(!store.Rooms.TryGetValue(code,out var found)) throw Fail("Kode room tidak ditemukan.");
            r=found;
            p=r.Players.FirstOrDefault(x=>x.Id==token)!;
            if(p==null)
            {
                if(r.Phase!="lobby") throw Fail("Permainan sudah dimulai. Hanya pemain lama yang bisa masuk kembali.");
                if(r.Players.Count>=8) throw Fail("Room sudah penuh (8 pemain).");
                if(r.Players.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))) throw Fail("Nama sudah dipakai.");
                p=new Player {Id=token,Name=name};r.Players.Add(p);GameStore.AddLog(r,$"{name} bergabung.");
            }
            p.ConnectionId=Context.ConnectionId;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId,code);
        await Broadcast(r);
        return new {code=r.Code,id=p.Id};
    }
    public async Task StartGame(string code,string token)
    {
        GameRoom r;
        lock(store.Gate)
        {
            (r,_)=Auth(code,token);
            if(r.HostId!=token || r.Phase!="lobby") throw Fail("Hanya host yang dapat memulai.");
            if(r.Players.Count<3) throw Fail("Minimal 3 pemain.");
            if(r.Players.Any(p=>!p.Connected)) throw Fail("Semua pemain harus online.");
            r.Deck=GameStore.NewDeck();r.Cards.Clear();foreach(var c in r.Deck)r.Cards[c.Id]=c;
            foreach(var p in r.Players){p.Penalties=0;p.CardId=Draw(r);}
            r.TurnIndex=0;r.Round=1;r.Pending=null;r.WinnerId=null;r.Phase="memorize";
            GameStore.AddLog(r,"Permainan dimulai. Ingat semua kartu!");
        }
        await Broadcast(r);
    }
    private static int? Draw(GameRoom r)
    {
        if(r.Deck.Count==0) return null;
        var c=r.Deck[^1];r.Deck.RemoveAt(r.Deck.Count-1);return c.Id;
    }
    public async Task HideCards(string code,string token)
    {
        GameRoom r;
        lock(store.Gate)
        {
            (r,_)=Auth(code,token);
            if(r.HostId!=token || r.Phase!="memorize") throw Fail("Hanya host dapat menutup kartu.");
            r.Phase="play";GameStore.AddLog(r,$"Giliran {r.Players[r.TurnIndex].Name}.");
        }
        await Broadcast(r);
    }
    public async Task GiveGift(string code,string token,string claimedName)
    {
        GameRoom r;
        lock(store.Gate)
        {
            (r,var p)=Auth(code,token);
            if(r.Phase!="play" || r.Pending!=null || r.Players[r.TurnIndex].Id!=token) throw Fail("Bukan giliran Anda.");
            if(!p.CardId.HasValue) throw Fail("Tidak ada kartu.");
            claimedName=Clean(claimedName,30);
            if(!GameStore.Gifts.Any(x=>x.Split('|')[0]==claimedName)) throw Fail("Pilih nama barang yang tersedia.");
            var next=r.Players[(r.TurnIndex+1)%r.Players.Count];
            r.Pending=new Offer {FromId=token,ToId=next.Id,CardId=p.CardId.Value,ClaimedName=claimedName};
            GameStore.AddLog(r,$"{p.Name} memberikan hadiah '{claimedName}' kepada {next.Name}. Terima atau tantang?");
        }
        await Broadcast(r);
    }
    public async Task Respond(string code,string token,bool challenge)
    {
        GameRoom r;
        lock(store.Gate)
        {
            (r,var recipient)=Auth(code,token);
            var offer=r.Pending;
            if(r.Phase!="play" || offer==null || offer.ToId!=token) throw Fail("Tidak ada hadiah untuk Anda.");
            var sender=r.Players.First(x=>x.Id==offer.FromId);
            if(challenge)
            {
                var actual=r.Cards[offer.CardId];var honest=actual.Name==offer.ClaimedName;
                var loser=honest?recipient:sender;loser.Penalties++;
                GameStore.AddLog(r,$"{recipient.Name} menantang! Kartu sebenarnya {actual.Emoji} {actual.Name}. {(honest?recipient.Name:sender.Name)} mendapat +1 penalti.");
            }
            else GameStore.AddLog(r,$"{recipient.Name} menerima hadiah tanpa memeriksa.");
            // Kartu lama penerima dikembalikan ke tumpukan buangan (tidak digunakan lagi).
            recipient.CardId=offer.CardId;
            sender.CardId=Draw(r);
            r.Pending=null;
            if(r.Players.Any(x=>x.Penalties>=5))
            {
                r.Phase="finished";
                var min=r.Players.Min(x=>x.Penalties);
                var winners=r.Players.Where(x=>x.Penalties==min).ToList();
                r.WinnerId=winners.Count==1?winners[0].Id:null;
                GameStore.AddLog(r,"Permainan selesai! Penalti paling sedikit menang (seri jika lebih dari satu pemain). ");
            }
            else if(sender.CardId==null)
            {
                r.Phase="finished";var min=r.Players.Min(x=>x.Penalties);
                var winners=r.Players.Where(x=>x.Penalties==min).ToList();
                r.WinnerId=winners.Count==1?winners[0].Id:null;
                GameStore.AddLog(r,"Tumpukan kartu habis. Permainan selesai.");
            }
            else
            {
                r.TurnIndex=(r.TurnIndex+1)%r.Players.Count;
                if(r.TurnIndex==0)r.Round++;
                GameStore.AddLog(r,$"Giliran {r.Players[r.TurnIndex].Name}.");
            }
        }
        await Broadcast(r);
    }
    public async Task BackToLobby(string code,string token)
    {
        GameRoom r;
        lock(store.Gate)
        {
            (r,_)=Auth(code,token);
            if(r.HostId!=token || r.Phase!="finished") throw Fail("Hanya host dapat mengulang setelah selesai.");
            r.Phase="lobby";r.Pending=null;r.WinnerId=null;r.Round=0;
            foreach(var p in r.Players){p.CardId=null;p.Penalties=0;}
            GameStore.AddLog(r,"Kembali ke lobby.");
        }
        await Broadcast(r);
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        List<GameRoom> changed=new();
        lock(store.Gate)
        {
            foreach(var r in store.Rooms.Values)
            {
                var p=r.Players.FirstOrDefault(x=>x.ConnectionId==Context.ConnectionId);
                if(p==null)continue;
                p.ConnectionId=null;
                if(r.Phase=="lobby")
                {
                    r.Players.Remove(p);
                    if(r.HostId==p.Id && r.Players.Count>0)r.HostId=r.Players[0].Id;
                    if(r.Players.Count==0)store.Rooms.TryRemove(r.Code,out _);
                }
                GameStore.AddLog(r,$"{p.Name} terputus.");changed.Add(r);
            }
        }
        foreach(var r in changed)await Broadcast(r);
        await base.OnDisconnectedAsync(exception);
    }
}
