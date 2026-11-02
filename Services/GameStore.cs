using System.Collections.Concurrent;
using System.Security.Cryptography;
using NotMyGiftLAN.Models;

namespace NotMyGiftLAN.Services;
public sealed class GameStore
{
    public object Gate { get; } = new();
    public ConcurrentDictionary<string, GameRoom> Rooms { get; } = new();
    public static readonly string[] Gifts = {"Topi|🎩","Payung|☂️","Sepatu|👟","Jam|⌚","Kacamata|🕶️","Boneka|🧸","Buku|📚","Kamera|📷","Gitar|🎸","Piala|🏆","Pizza|🍕","Kunci|🔑","Bola|⚽","Lampu|💡","Cincin|💍","Tas|🎒","Bunga|🌷","Kue|🎂","Telepon|📱","Kado|🎁","Mobil|🚗","Roket|🚀","Permen|🍬","Pisang|🍌"};
    public static List<GiftCard> NewDeck()
    {
        var cards = Gifts.Select((s,i) => new GiftCard {Id=i+1, Name=s.Split('|')[0], Emoji=s.Split('|')[1]}).ToList();
        return cards.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();
    }
    public static string Code() => new string(Enumerable.Range(0,6).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[RandomNumberGenerator.GetInt32(32)]).ToArray());
    public static object View(GameRoom r, string viewer)
    {
        var show = r.Phase == "memorize";
        return new {
            code=r.Code, phase=r.Phase, hostId=r.HostId, viewerId=viewer, round=r.Round,
            currentId=r.Players.Count>0 ? r.Players[r.TurnIndex % r.Players.Count].Id : null,
            players=r.Players.Select(p => new {id=p.Id,name=p.Name,connected=p.Connected,penalties=p.Penalties,
                card=(show && p.CardId.HasValue && r.Cards.TryGetValue(p.CardId.Value,out var c)) ? new {name=c.Name,emoji=c.Emoji} : null,
                hasCard=p.CardId.HasValue}).ToArray(),
            pending=r.Pending is null ? null : new {fromId=r.Pending.FromId,toId=r.Pending.ToId,claim=r.Pending.ClaimedName},
            winnerId=r.WinnerId, log=r.Log.TakeLast(15).Reverse().ToArray(),
            choices=Gifts.Select(s=>s.Split('|')[0]).ToArray()
        };
    }
    public static void AddLog(GameRoom r, string message)
    {
        r.Log.Add(message);
        if(r.Log.Count>100) r.Log.RemoveAt(0);
        r.LastTouched=DateTime.UtcNow;
    }
}
