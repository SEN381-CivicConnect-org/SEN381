using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class IncidentStatusTransitionConfiguration : IEntityTypeConfiguration<IncidentStatusTransition>
{
    /// The documented incident lifecycle; creation (no from-status, to NEW) is handled by a trigger instead.
    public static readonly (IncidentStatus FromStatus, IncidentStatus ToStatus)[] PermittedTransitions =
    {
        (IncidentStatus.New, IncidentStatus.Assigned),
        (IncidentStatus.New, IncidentStatus.Rejected),
        (IncidentStatus.New, IncidentStatus.Merged),

        (IncidentStatus.Assigned, IncidentStatus.InProgress),
        (IncidentStatus.Assigned, IncidentStatus.OnHold),
        (IncidentStatus.Assigned, IncidentStatus.Rejected),
        (IncidentStatus.Assigned, IncidentStatus.Merged),

        (IncidentStatus.InProgress, IncidentStatus.OnHold),
        (IncidentStatus.InProgress, IncidentStatus.Resolved),
        (IncidentStatus.InProgress, IncidentStatus.Merged),

        (IncidentStatus.OnHold, IncidentStatus.InProgress),
        (IncidentStatus.OnHold, IncidentStatus.Resolved),
        (IncidentStatus.OnHold, IncidentStatus.Merged),

        (IncidentStatus.Resolved, IncidentStatus.Closed),
        (IncidentStatus.Resolved, IncidentStatus.InProgress),
    };

    public void Configure(EntityTypeBuilder<IncidentStatusTransition> builder)
    {
        builder.ToTable("incident_status_transition");

        builder.HasKey(t => new { t.FromStatus, t.ToStatus });

        builder.HasData(PermittedTransitions.Select(t => new
        {
            t.FromStatus,
            t.ToStatus,
        }));
    }
}
