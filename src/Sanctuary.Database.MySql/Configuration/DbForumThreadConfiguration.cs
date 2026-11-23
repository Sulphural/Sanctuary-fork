using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbForumThreadConfiguration : IEntityTypeConfiguration<DbForumThread>
{
    public void Configure(EntityTypeBuilder<DbForumThread> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.Title).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AuthorName).IsRequired().HasMaxLength(64);
        builder.Property(t => t.LastPostName).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Created).IsRequired().HasDefaultValueSql("NOW()");
        builder.Property(t => t.LastPostAt).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasIndex(t => new { t.ForumId, t.IsSticky, t.LastPostId });

        builder.HasOne(t => t.Forum)
            .WithMany()
            .HasForeignKey(t => t.ForumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.AuthorUser)
            .WithMany()
            .HasForeignKey(t => t.AuthorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.AuthorCharacter)
            .WithMany()
            .HasForeignKey(t => t.AuthorCharacterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
