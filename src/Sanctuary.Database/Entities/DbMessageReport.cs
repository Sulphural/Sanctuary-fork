using System;

namespace Sanctuary.Database.Entities;

public class DbMessageReport
{
    public ulong MessageId { get; set; }
    public DbProfileMessage Message { get; set; } = null!;

    public ulong ReporterUserId { get; set; }
    public DbUser ReporterUser { get; set; } = null!;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
