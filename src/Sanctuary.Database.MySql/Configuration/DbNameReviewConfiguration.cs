using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbNameReviewConfiguration : IEntityTypeConfiguration<DbNameReview>
{
    public void Configure(EntityTypeBuilder<DbNameReview> builder)
    {
        builder.HasKey(r => r.CharacterId);

        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.Character)
            .WithMany()
            .HasForeignKey(r => r.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
