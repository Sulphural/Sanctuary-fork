namespace Sanctuary.Database.Entities;

public class DbForum
{
    public int Id { get; set; }

    public required string Category { get; set; }
    public int CategoryOrder { get; set; }

    public required string Title { get; set; }
    public required string Description { get; set; }
    public int DisplayOrder { get; set; }

    public bool StaffThreadsOnly { get; set; }
    public bool MembersOnly { get; set; }
}
