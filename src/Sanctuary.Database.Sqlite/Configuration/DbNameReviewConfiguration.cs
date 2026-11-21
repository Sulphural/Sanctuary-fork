using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbNameReviewConfiguration : IEntityTypeConfiguration<DbNameReview>
{
    public void Configure(EntityTypeBuilder<DbNameReview> builder)
    {
        builder.HasKey(r => r.CharacterId);

        builder.Property(r => r.RequestedFirstName).IsRequired().HasMaxLength(16);
        builder.Property(r => r.RequestedLastName).IsRequired().HasMaxLength(16);

        builder.HasIndex(r => new { r.RequestedFirstName, r.RequestedLastName });

        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.HasOne(r => r.Character)
            .WithMany()
            .HasForeignKey(r => r.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
