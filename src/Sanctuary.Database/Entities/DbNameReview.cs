using System;

namespace Sanctuary.Database.Entities;

public class DbNameReview
{
    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
