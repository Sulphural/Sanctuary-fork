using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbFriendRequestConfiguration : IEntityTypeConfiguration<DbFriendRequest>
{
    public void Configure(EntityTypeBuilder<DbFriendRequest> builder)
    {
        builder.HasKey(r => new { r.CharacterId, r.TargetCharacterId });

        builder.Property(r => r.Created).IsRequired().HasDefaultValueSql("DATE()");

        builder.HasOne(r => r.Character)
            .WithMany()
            .HasForeignKey(r => r.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TargetCharacter)
            .WithMany()
            .HasForeignKey(r => r.TargetCharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
