using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbProfileMessageConfiguration : IEntityTypeConfiguration<DbProfileMessage>
{
    public void Configure(EntityTypeBuilder<DbProfileMessage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).IsRequired().ValueGeneratedOnAdd();

        builder.Property(m => m.Text).IsRequired().HasMaxLength(500);

        builder.Property(m => m.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.HasIndex(m => new { m.CharacterId, m.Created });

        builder.HasOne(m => m.Character)
            .WithMany()
            .HasForeignKey(m => m.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.AuthorCharacter)
            .WithMany()
            .HasForeignKey(m => m.AuthorCharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
