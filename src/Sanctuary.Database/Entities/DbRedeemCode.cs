using System;

namespace Sanctuary.Database.Entities;

public class DbRedeemCode
{
    public required string Code { get; set; }

    public required string Description { get; set; }

    public int Coins { get; set; }
    public int StationCash { get; set; }

    public int? ItemDefinition { get; set; }
    public int ItemCount { get; set; }

    public int MaxUses { get; set; }
    public int Uses { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public required string CreatedBy { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
