using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class IncidentSubscriptionConfiguration : IEntityTypeConfiguration<IncidentSubscription>
{
    public void Configure(EntityTypeBuilder<IncidentSubscription> builder)
    {
        builder.ToTable("incident_subscription");

        // A reporter cannot be subscribed to the same incident twice.
        builder.HasKey(s => new { s.IncidentId, s.ReporterId });

        builder.Property(s => s.SubscribedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(s => s.Incident)
            .WithMany()
            .HasForeignKey(s => s.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Reporter)
            .WithMany()
            .HasForeignKey(s => s.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports "which incidents is this reporter subscribed to" without scanning by incident first.
        builder.HasIndex(s => s.ReporterId);
    }
}
