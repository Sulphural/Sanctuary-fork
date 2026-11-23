using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbForumPostConfiguration : IEntityTypeConfiguration<DbForumPost>
{
    public void Configure(EntityTypeBuilder<DbForumPost> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.AuthorName).IsRequired().HasMaxLength(64);
        builder.Property(p => p.EditedBy).HasMaxLength(64);
        builder.Property(p => p.Body).IsRequired().HasMaxLength(10000);
        builder.Property(p => p.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.HasIndex(p => new { p.ThreadId, p.Id });
        builder.HasIndex(p => p.AuthorCharacterId);

        builder.HasOne(p => p.Thread)
            .WithMany()
            .HasForeignKey(p => p.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.AuthorUser)
            .WithMany()
            .HasForeignKey(p => p.AuthorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.AuthorCharacter)
            .WithMany()
            .HasForeignKey(p => p.AuthorCharacterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
