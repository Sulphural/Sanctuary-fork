using System;

namespace Sanctuary.Database.Entities;

public class DbAccountToken
{
    public required string TokenHash { get; set; }

    public ulong UserId { get; set; }
    public DbUser User { get; set; } = null!;

    public required string Purpose { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
