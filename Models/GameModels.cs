namespace NotMyGiftLAN.Models;

public sealed class Player
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string? ConnectionId { get; set; }
    public int Penalties { get; set; }
    public int? CardId { get; set; }
    public bool Connected => ConnectionId != null;
}
public sealed class GiftCard
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Emoji { get; init; } = "";
}
public sealed class Offer
{
    public string FromId { get; init; } = "";
    public string ToId { get; init; } = "";
    public int CardId { get; init; }
    public string ClaimedName { get; init; } = "";
}
public sealed class GameRoom
{
    public string Code { get; init; } = "";
    public string HostId { get; set; } = "";
    public List<Player> Players { get; } = new();
    public List<GiftCard> Deck { get; set; } = new();
    public Dictionary<int, GiftCard> Cards { get; } = new();
    public List<string> Log { get; } = new();
    public string Phase { get; set; } = "lobby"; // lobby, memorize, play, finished
    public int TurnIndex { get; set; }
    public Offer? Pending { get; set; }
    public string? WinnerId { get; set; }
    public int Round { get; set; }
    public DateTime LastTouched { get; set; } = DateTime.UtcNow;
}
