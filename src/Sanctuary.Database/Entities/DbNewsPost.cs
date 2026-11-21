using System;

namespace Sanctuary.Database.Entities;

public class DbNewsPost
{
    public ulong Id { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public required string Title { get; set; }
    public required string Summary { get; set; }
    public required string Body { get; set; }

    public required string AuthorUsername { get; set; }
}
