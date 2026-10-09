using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("ticket");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).UseIdentityByDefaultColumn();

        // Derived solely from id, so it is unique without a separate sequence; distinct from incident.reference_number.
        builder.Property(t => t.ReferenceNumber)
            .HasComputedColumnSql("'TKT-' || lpad(id::text, 6, '0')", stored: true);
        builder.HasIndex(t => t.ReferenceNumber).IsUnique();

        builder.Property(t => t.Title).IsRequired();
        builder.Property(t => t.Description).IsRequired();

        builder.Property(t => t.SubmittedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(t => t.Reporter)
            .WithMany()
            .HasForeignKey(t => t.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Incident)
            .WithMany()
            .HasForeignKey(t => t.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Location)
            .WithMany()
            .HasForeignKey(t => t.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
