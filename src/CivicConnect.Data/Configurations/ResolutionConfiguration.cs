using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class ResolutionConfiguration : IEntityTypeConfiguration<Resolution>
{
    public void Configure(EntityTypeBuilder<Resolution> builder)
    {
        builder.ToTable("resolution");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityByDefaultColumn();

        builder.Property(r => r.Summary).IsRequired();

        // Set by a database trigger from the resolution code's ImpliesReplacement at insert time.
        builder.Property(r => r.NeedsReplacement).ValueGeneratedOnAdd();

        builder.Property(r => r.ResolvedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(r => r.Incident)
            .WithMany()
            .HasForeignKey(r => r.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ResolutionCode)
            .WithMany()
            .HasForeignKey(r => r.ResolutionCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Asset)
            .WithMany()
            .HasForeignKey(r => r.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ResolvedByUser)
            .WithMany()
            .HasForeignKey(r => r.ResolvedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one active (non-superseded) resolution per incident.
        builder.HasIndex(r => r.IncidentId)
            .IsUnique()
            .HasDatabaseName("resolution_incident_active_uq")
            .HasFilter("superseded_at IS NULL");

        builder.HasIndex(r => new { r.IncidentId, r.ResolvedAt });
    }
}
