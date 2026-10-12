using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incident", table =>
        {
            // An incident cannot be merged into itself.
            table.HasCheckConstraint("incident_merge_not_self_ck", "id <> merged_into_id");

            table.HasCheckConstraint(
                "incident_override_ck",
                @"(NOT priority_overridden
                      AND override_reason IS NULL AND override_by IS NULL AND override_at IS NULL
                      AND priority = computed_priority)
                  OR (priority_overridden
                      AND override_reason IS NOT NULL AND btrim(override_reason) <> ''
                      AND override_by IS NOT NULL AND override_at IS NOT NULL)");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).UseIdentityByDefaultColumn();

        // Derived solely from id, so it is unique without a separate sequence.
        builder.Property(i => i.ReferenceNumber)
            .HasComputedColumnSql("'INC-' || lpad(id::text, 6, '0')", stored: true);
        builder.HasIndex(i => i.ReferenceNumber).IsUnique();

        builder.Property(i => i.Title).IsRequired();
        builder.Property(i => i.Description).IsRequired();

        builder.Property(i => i.Status).IsRequired().HasDefaultValue(IncidentStatus.New);

        builder.Property(i => i.PriorityOverridden).HasDefaultValue(false);

        // Bumped by a database trigger on every update; the concurrency token EF uses to detect lost updates.
        builder.Property(i => i.Version)
            .HasDefaultValue(1)
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(i => i.OpenedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        // Set by a database trigger from the effective priority target's resolve-within-minutes value.
        builder.Property(i => i.DueAt).ValueGeneratedOnAddOrUpdate();

        builder.HasOne(i => i.Category)
            .WithMany()
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Location)
            .WithMany()
            .HasForeignKey(i => i.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ComputedPriorityTarget)
            .WithMany()
            .HasForeignKey(i => i.ComputedPriority)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PriorityTarget)
            .WithMany()
            .HasForeignKey(i => i.Priority)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.OverrideByUser)
            .WithMany()
            .HasForeignKey(i => i.OverrideBy)
            .OnDelete(DeleteBehavior.Restrict);

        // A merged incident keeps pointing at its survivor; the survivor cannot be deleted while absorbed incidents reference it.
        builder.HasOne(i => i.MergedInto)
            .WithMany()
            .HasForeignKey(i => i.MergedIntoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.AssignedTeam)
            .WithMany()
            .HasForeignKey(i => i.AssignedTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Assignee)
            .WithMany()
            .HasForeignKey(i => i.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
