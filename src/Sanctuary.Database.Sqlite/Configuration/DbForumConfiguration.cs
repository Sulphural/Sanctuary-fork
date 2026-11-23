using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbForumConfiguration : IEntityTypeConfiguration<DbForum>
{
    public void Configure(EntityTypeBuilder<DbForum> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Category).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Title).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Description).IsRequired().HasMaxLength(300);

        builder.HasData(ForumSeed.Forums);
    }
}
