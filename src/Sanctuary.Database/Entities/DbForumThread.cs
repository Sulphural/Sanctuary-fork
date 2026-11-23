using System;

namespace Sanctuary.Database.Entities;

public class DbForumThread
{
    public ulong Id { get; set; }

    public int ForumId { get; set; }
    public DbForum Forum { get; set; } = null!;

    public required string Title { get; set; }

    public ulong? AuthorUserId { get; set; }
    public DbUser? AuthorUser { get; set; }

    public ulong? AuthorCharacterId { get; set; }
    public DbCharacter? AuthorCharacter { get; set; }

    public required string AuthorName { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastPostAt { get; set; } = DateTimeOffset.UtcNow;
    public ulong LastPostId { get; set; }
    public ulong? LastPostCharacterId { get; set; }
    public required string LastPostName { get; set; }

    public int ReplyCount { get; set; }
    public int ViewCount { get; set; }

    public bool IsSticky { get; set; }
    public bool IsLocked { get; set; }
}
