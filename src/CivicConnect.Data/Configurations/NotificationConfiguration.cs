using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).UseIdentityByDefaultColumn();

        builder.Property(n => n.DedupKey).IsRequired();

        builder.Property(n => n.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(n => n.Incident)
            .WithMany()
            .HasForeignKey(n => n.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Recipient)
            .WithMany()
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => n.DedupKey)
            .IsUnique()
            .HasDatabaseName("notification_dedup_key_uq");

        // Supports "unread notifications for a recipient" without scanning read ones too.
        builder.HasIndex(n => n.RecipientId)
            .HasDatabaseName("ix_notification_recipient_unread")
            .HasFilter("read_at IS NULL");
    }
}
