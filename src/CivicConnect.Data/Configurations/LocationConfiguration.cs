using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("location", table =>
        {
            table.HasCheckConstraint("location_parent_not_self_ck", "id <> parent_location_id");
            table.HasCheckConstraint("location_impact_level_range_ck", "impact_level BETWEEN 1 AND 4");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).UseIdentityByDefaultColumn();

        builder.Property(l => l.Name).IsRequired();
        builder.Property(l => l.IsActive).HasDefaultValue(true);
        builder.Property(l => l.ImpactLevel).IsRequired();

        builder.Property(l => l.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(l => l.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(l => l.LocationKind)
            .WithMany(k => k.Locations)
            .HasForeignKey(l => l.LocationKindId)
            .OnDelete(DeleteBehavior.Restrict);

        // A parent cannot be deleted while it still has children.
        builder.HasOne(l => l.ParentLocation)
            .WithMany(l => l.ChildLocations)
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
