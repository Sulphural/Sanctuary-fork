using System;

namespace Sanctuary.Database.Entities;

public class DbFriendRequest
{
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public ulong TargetCharacterId { get; set; }
    public DbCharacter TargetCharacter { get; set; } = null!;
}
