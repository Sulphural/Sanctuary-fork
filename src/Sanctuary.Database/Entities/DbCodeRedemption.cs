using System;

namespace Sanctuary.Database.Entities;

public class DbCodeRedemption
{
    public required string Code { get; set; }
    public DbRedeemCode RedeemCode { get; set; } = null!;

    public ulong UserId { get; set; }
    public DbUser User { get; set; } = null!;

    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
