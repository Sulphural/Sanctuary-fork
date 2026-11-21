using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbPlayerReportConfiguration : IEntityTypeConfiguration<DbPlayerReport>
{
    public void Configure(EntityTypeBuilder<DbPlayerReport> builder)
    {
        builder.HasKey(r => new { r.CharacterId, r.ReporterUserId });

        builder.Property(r => r.Reason).IsRequired().HasMaxLength(500);

        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.Character)
            .WithMany()
            .HasForeignKey(r => r.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ReporterUser)
            .WithMany()
            .HasForeignKey(r => r.ReporterUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
