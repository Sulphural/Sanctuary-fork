using System;

namespace Sanctuary.Database.Entities;

public class DbPlayerReport
{
    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public ulong ReporterUserId { get; set; }
    public DbUser ReporterUser { get; set; } = null!;

    public required string Reason { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
