using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbStaffActionConfiguration : IEntityTypeConfiguration<DbStaffAction>
{
    public void Configure(EntityTypeBuilder<DbStaffAction> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).IsRequired().ValueGeneratedOnAdd();

        builder.Property(a => a.Created).IsRequired().HasDefaultValueSql("NOW()");

        builder.Property(a => a.StaffUsername).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(32);
        builder.Property(a => a.TargetName).HasMaxLength(64);
        builder.Property(a => a.Details).HasMaxLength(500);

        builder.HasIndex(a => a.Created);
        builder.HasIndex(a => a.TargetUserId);
    }
}
