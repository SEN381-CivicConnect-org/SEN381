using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class LocationKindConfiguration : IEntityTypeConfiguration<LocationKind>
{
    /// Baseline location kinds every environment starts with.
    public static readonly (short Id, string Name)[] BaselineLocationKinds =
    {
        (1, "site"),
        (2, "floor"),
        (3, "room"),
    };

    public void Configure(EntityTypeBuilder<LocationKind> builder)
    {
        builder.ToTable("location_kind");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).UseIdentityByDefaultColumn();

        builder.Property(k => k.Name).IsRequired();
        builder.HasIndex(k => k.Name).IsUnique();

        builder.Property(k => k.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasData(BaselineLocationKinds.Select(kind => new
        {
            kind.Id,
            kind.Name,
            CreatedAt = SeedTimestamps.SeededAt,
        }));
    }
}
