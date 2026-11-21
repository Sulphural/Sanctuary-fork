using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbRedeemCodeConfiguration : IEntityTypeConfiguration<DbRedeemCode>
{
    public void Configure(EntityTypeBuilder<DbRedeemCode> builder)
    {
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).IsRequired().HasMaxLength(32);

        builder.Property(c => c.Description).IsRequired().HasMaxLength(100);
        builder.Property(c => c.CreatedBy).IsRequired().HasMaxLength(50);

        builder.Property(c => c.Created).IsRequired().HasDefaultValueSql("NOW()");
    }
}
