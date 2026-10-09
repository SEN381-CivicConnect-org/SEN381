using CivicConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("service_request");

        builder.HasKey(sr => sr.Id);

        builder.Property(sr => sr.Title).IsRequired().HasMaxLength(200);
        builder.Property(sr => sr.Description).IsRequired();

        // Enums stored natively as integers in PostgreSQL (Step 3)
        builder.Property(sr => sr.Impact).HasConversion<int>().IsRequired();
        builder.Property(sr => sr.Urgency).HasConversion<int>().IsRequired();
        builder.Property(sr => sr.Priority).HasConversion<int>().IsRequired();

        // FR-06 Supervisor override fields
        builder.Property(sr => sr.IsSupervisorOverridden).HasDefaultValue(false);
        builder.Property(sr => sr.SupervisorOverrideReason).HasMaxLength(500);

        builder.Property(sr => sr.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Open");

        builder.Property(sr => sr.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        builder.Property(sr => sr.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(sr => sr.Category)
            .WithMany()
            .HasForeignKey(sr => sr.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(sr => sr.Requester)
            .WithMany()
            .HasForeignKey(sr => sr.RequesterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(sr => sr.AssignedTo)
            .WithMany()
            .HasForeignKey(sr => sr.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(sr => sr.Status);
        builder.HasIndex(sr => sr.Priority);
    }
}
