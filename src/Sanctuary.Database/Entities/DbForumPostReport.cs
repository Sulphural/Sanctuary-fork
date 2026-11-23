using System;

namespace Sanctuary.Database.Entities;

public class DbForumPostReport
{
    public ulong PostId { get; set; }
    public DbForumPost Post { get; set; } = null!;

    public ulong ReporterUserId { get; set; }
    public DbUser ReporterUser { get; set; } = null!;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
