using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class IncidentStatusHistoryConfiguration : IEntityTypeConfiguration<IncidentStatusHistory>
{
    public void Configure(EntityTypeBuilder<IncidentStatusHistory> builder)
    {
        builder.ToTable("incident_status_history", table =>
        {
            // A null from-status row must be to NEW; "it's the first row" is checked by a trigger instead.
            table.HasCheckConstraint("incident_status_history_creation_ck", "from_status IS NOT NULL OR to_status = 'NEW'");
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).UseIdentityByDefaultColumn();

        builder.Property(h => h.ChangedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(h => h.Incident)
            .WithMany()
            .HasForeignKey(h => h.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.ChangedByUser)
            .WithMany()
            .HasForeignKey(h => h.ChangedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.AssignedTeam)
            .WithMany()
            .HasForeignKey(h => h.AssignedTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Assignee)
            .WithMany()
            .HasForeignKey(h => h.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.IncidentId, h.ChangedAt });
    }
}
