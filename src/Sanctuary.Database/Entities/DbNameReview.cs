using System;

namespace Sanctuary.Database.Entities;

public class DbNameReview
{
    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public required string RequestedFirstName { get; set; }
    public string RequestedLastName { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
