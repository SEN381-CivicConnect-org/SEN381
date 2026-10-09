using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("asset");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityByDefaultColumn();

        builder.Property(a => a.AssetTag).IsRequired();
        builder.HasIndex(a => a.AssetTag).IsUnique();

        builder.Property(a => a.Description).IsRequired();
        builder.Property(a => a.Supplier).IsRequired();
        builder.Property(a => a.PurchaseDate).HasColumnType("date").IsRequired();
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(a => a.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        // Deleting a location that still has assets is refused; an asset may instead have no location.
        builder.HasOne(a => a.Location)
            .WithMany(l => l.Assets)
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
