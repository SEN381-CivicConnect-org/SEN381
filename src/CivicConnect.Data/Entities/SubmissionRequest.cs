namespace CivicConnect.Data.Entities;

/// Idempotency record for one reporter's submission key; a replay with the same key returns the same ticket.
public sealed class SubmissionRequest
{
    public Guid ReporterId { get; set; }
    public AppUser Reporter { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    /// Fingerprint of the submitted content; a replay with a different fingerprint is a conflict, not a replay.
    public string RequestFingerprint { get; set; } = null!;

    public long TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public DateTimeOffset RequestedAt { get; set; }

    /// Once past, the key may be forgotten; expired-row cleanup is application-driven, not enforced by the database.
    public DateTimeOffset ExpiresAt { get; set; }
}
