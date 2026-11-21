using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbNewsPostConfiguration : IEntityTypeConfiguration<DbNewsPost>
{
    public void Configure(EntityTypeBuilder<DbNewsPost> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).IsRequired().ValueGeneratedOnAdd();

        builder.Property(n => n.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.Property(n => n.Title).IsRequired().HasMaxLength(100);
        builder.Property(n => n.Summary).IsRequired().HasMaxLength(300);
        builder.Property(n => n.Body).IsRequired().HasMaxLength(8000);
        builder.Property(n => n.AuthorUsername).IsRequired().HasMaxLength(50);

        builder.HasIndex(n => n.Created);
    }
}
