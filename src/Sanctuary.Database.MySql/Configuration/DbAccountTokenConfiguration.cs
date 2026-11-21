using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbAccountTokenConfiguration : IEntityTypeConfiguration<DbAccountToken>
{
    public void Configure(EntityTypeBuilder<DbAccountToken> builder)
    {
        builder.HasKey(t => t.TokenHash);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);

        builder.Property(t => t.Purpose).IsRequired().HasMaxLength(16);
        builder.Property(t => t.Email).HasMaxLength(254);

        builder.Property(t => t.Created).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasIndex(t => new { t.UserId, t.Purpose });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
