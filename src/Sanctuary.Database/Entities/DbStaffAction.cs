using System;

namespace Sanctuary.Database.Entities;

public class DbStaffAction
{
    public ulong Id { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public ulong StaffUserId { get; set; }
    public required string StaffUsername { get; set; }

    public required string Action { get; set; }

    public ulong? TargetUserId { get; set; }
    public ulong? TargetCharacterId { get; set; }
    public string? TargetName { get; set; }

    public string? Details { get; set; }
}
