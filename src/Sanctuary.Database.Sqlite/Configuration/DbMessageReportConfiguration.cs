using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbMessageReportConfiguration : IEntityTypeConfiguration<DbMessageReport>
{
    public void Configure(EntityTypeBuilder<DbMessageReport> builder)
    {
        builder.HasKey(r => new { r.MessageId, r.ReporterUserId });

        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.HasOne(r => r.Message)
            .WithMany()
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ReporterUser)
            .WithMany()
            .HasForeignKey(r => r.ReporterUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
