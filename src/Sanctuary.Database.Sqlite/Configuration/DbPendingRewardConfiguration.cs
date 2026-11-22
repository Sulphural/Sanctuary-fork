using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbPendingRewardConfiguration : IEntityTypeConfiguration<DbPendingReward>
{
    public void Configure(EntityTypeBuilder<DbPendingReward> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Source).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Code).HasMaxLength(40);
        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.HasIndex(r => r.UserId);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
