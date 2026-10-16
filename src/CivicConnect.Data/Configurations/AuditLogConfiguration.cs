using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityByDefaultColumn();

        builder.Property(a => a.EntityId).IsRequired();

        builder.Property(a => a.BeforeState).HasColumnType("jsonb");
        builder.Property(a => a.AfterState).HasColumnType("jsonb");

        builder.Property(a => a.OccurredAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(a => a.Actor)
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Incident)
            .WithMany()
            .HasForeignKey(a => a.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.ActorId)
            .HasDatabaseName("ix_audit_log_actor_id");

        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("ix_audit_log_entity_type_entity_id");

        // Supports "this incident's audit trail" without scanning configuration-change rows too.
        builder.HasIndex(a => new { a.IncidentId, a.OccurredAt })
            .HasDatabaseName("ix_audit_log_incident_id_occurred_at")
            .HasFilter("incident_id IS NOT NULL");
    }
}
