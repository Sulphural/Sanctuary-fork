using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Sanctuary.Database.Entities;

namespace Sanctuary.Database.Sqlite.Configuration;

public sealed class DbProfilePrivacyConfiguration : IEntityTypeConfiguration<DbProfilePrivacy>
{
    public void Configure(EntityTypeBuilder<DbProfilePrivacy> builder)
    {
        builder.HasKey(p => p.CharacterId);
        builder.Property(p => p.CharacterId).ValueGeneratedNever();

        builder.Property(p => p.Messages).IsRequired();
        builder.Property(p => p.Gallery).IsRequired();
        builder.Property(p => p.Characters).IsRequired();
        builder.Property(p => p.Friends).IsRequired();
        builder.Property(p => p.News).IsRequired();

        builder.HasOne(p => p.Character)
            .WithOne()
            .HasForeignKey<DbProfilePrivacy>(p => p.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
