namespace Sanctuary.Database.Entities;

public class DbProfilePrivacy
{
    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public int Messages { get; set; }
    public int Gallery { get; set; }
    public int Characters { get; set; }
    public int Friends { get; set; } = 1;
    public int News { get; set; }
}
