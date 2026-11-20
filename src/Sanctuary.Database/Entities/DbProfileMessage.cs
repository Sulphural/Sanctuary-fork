using System;

namespace Sanctuary.Database.Entities;

public class DbProfileMessage
{
    public ulong Id { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public ulong AuthorCharacterId { get; set; }
    public DbCharacter AuthorCharacter { get; set; } = null!;

    public required string Text { get; set; }
}
