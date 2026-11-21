using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbCodeRedemptionConfiguration : IEntityTypeConfiguration<DbCodeRedemption>
{
    public void Configure(EntityTypeBuilder<DbCodeRedemption> builder)
    {
        builder.HasKey(r => new { r.Code, r.UserId });

        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.RedeemCode)
            .WithMany()
            .HasForeignKey(r => r.Code)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Character)
            .WithMany()
            .HasForeignKey(r => r.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
