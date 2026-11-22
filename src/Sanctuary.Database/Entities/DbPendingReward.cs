using System;

namespace Sanctuary.Database.Entities;

public class DbPendingReward
{
    public long Id { get; set; }

    public ulong UserId { get; set; }
    public DbUser User { get; set; } = null!;

    public int ItemDefinition { get; set; }
    public int Quantity { get; set; }

    public required string Source { get; set; }
    public string? Code { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClaimedAt { get; set; }
    public ulong? ClaimedByCharacterId { get; set; }
}
