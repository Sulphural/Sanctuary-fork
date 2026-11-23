using System;

namespace Sanctuary.Database.Entities;

public class DbForumPost
{
    public ulong Id { get; set; }

    public ulong ThreadId { get; set; }
    public DbForumThread Thread { get; set; } = null!;

    public ulong? AuthorUserId { get; set; }
    public DbUser? AuthorUser { get; set; }

    public ulong? AuthorCharacterId { get; set; }
    public DbCharacter? AuthorCharacter { get; set; }

    public required string AuthorName { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Edited { get; set; }
    public string? EditedBy { get; set; }

    public required string Body { get; set; }
}
