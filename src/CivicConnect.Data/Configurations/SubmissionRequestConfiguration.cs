using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicConnect.Data.Configurations;

public sealed class SubmissionRequestConfiguration : IEntityTypeConfiguration<SubmissionRequest>
{
    public void Configure(EntityTypeBuilder<SubmissionRequest> builder)
    {
        builder.ToTable("submission_request");

        // A reporter can see at most one record per idempotency key; a lookup on this key is the replay check.
        builder.HasKey(sr => new { sr.ReporterId, sr.IdempotencyKey });

        builder.Property(sr => sr.IdempotencyKey).IsRequired();
        builder.Property(sr => sr.RequestFingerprint).IsRequired();

        builder.Property(sr => sr.RequestedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        builder.HasOne(sr => sr.Reporter)
            .WithMany()
            .HasForeignKey(sr => sr.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sr => sr.Ticket)
            .WithMany()
            .HasForeignKey(sr => sr.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lets a cleanup job find expired keys without scanning the whole table.
        builder.HasIndex(sr => sr.ExpiresAt);
    }
}
