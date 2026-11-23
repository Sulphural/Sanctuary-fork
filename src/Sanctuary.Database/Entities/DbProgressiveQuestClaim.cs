using System;

namespace Sanctuary.Database.Entities;

public sealed class DbProgressiveQuestClaim
{
    public int ProgressiveQuestId { get; set; }
    public int Year { get; set; }
    public int SlotId { get; set; }
    public bool IsPrize { get; set; }

    public int RewardIndex { get; set; }

    public DateTimeOffset Claimed { get; set; } = DateTimeOffset.UtcNow;

    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;
}
