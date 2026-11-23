using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.MySql.Configuration;

public sealed class DbProgressiveQuestClaimConfiguration : IEntityTypeConfiguration<DbProgressiveQuestClaim>
{
    public void Configure(EntityTypeBuilder<DbProgressiveQuestClaim> builder)
    {
        builder.HasKey(claim => new { claim.CharacterId, claim.ProgressiveQuestId, claim.Year, claim.IsPrize, claim.SlotId });

        builder.Property(claim => claim.ProgressiveQuestId).IsRequired();
        builder.Property(claim => claim.Year).IsRequired();
        builder.Property(claim => claim.SlotId).IsRequired();
        builder.Property(claim => claim.IsPrize).IsRequired();
        builder.Property(claim => claim.RewardIndex).IsRequired();
        builder.Property(claim => claim.Claimed).IsRequired().HasDefaultValueSql("NOW()");
    }
}
