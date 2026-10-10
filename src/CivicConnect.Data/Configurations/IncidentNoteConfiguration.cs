using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class IncidentNoteConfiguration : IEntityTypeConfiguration<IncidentNote>
{
    public void Configure(EntityTypeBuilder<IncidentNote> builder)
    {
        builder.ToTable("incident_note");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).UseIdentityByDefaultColumn();

        builder.Property(n => n.Body).IsRequired();

        builder.Property(n => n.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(n => n.Incident)
            .WithMany()
            .HasForeignKey(n => n.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Author)
            .WithMany()
            .HasForeignKey(n => n.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.IncidentId, n.CreatedAt });
    }
}
