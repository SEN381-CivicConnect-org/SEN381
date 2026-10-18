namespace CivicConnect.Api.Services;

/// <summary>
/// Summary DTO describing the records purged during a retention sweep operation (NFR-05, POPIA s14(1), IT-RET-01).
/// </summary>
public sealed record RetentionSweepSummaryDto
{
    /// <summary>
    /// Total count of idempotency records (<c>submission_request</c>) purged.
    /// </summary>
    public int PurgedSubmissionRequests { get; init; }

    /// <summary>
    /// Total count of read notifications (<c>notification</c>) marked read more than 12 months (365 days) ago purged.
    /// </summary>
    public int PurgedNotifications { get; init; }

    /// <summary>
    /// Total count of all records purged across all tables.
    /// </summary>
    public int TotalPurgedRecords => PurgedSubmissionRequests + PurgedNotifications;

    /// <summary>
    /// Timestamp at which the retention sweep executed.
    /// </summary>
    public DateTimeOffset ExecutedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Elapsed duration of the retention sweep operation in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; init; }
}

/// <summary>
/// Service contract for executing scheduled and on-demand retention sweeps of personal information
/// and idempotency tokens per NFR-05 and POPIA s14(1) (IT-RET-01).
/// </summary>
public interface IRetentionSweepService
{
    /// <summary>
    /// Executes a retention sweep within a database transaction:
    /// <list type="bullet">
    ///   <item><description>Purges idempotency records (<c>submission_request</c>) where <c>expires_at &lt; now</c> or created &gt; 7 days ago.</description></item>
    ///   <item><description>Purges notifications (<c>notification</c>) that were marked read (<c>read_at IS NOT NULL</c>) more than 12 months (365 days) ago.</description></item>
    ///   <item><description>Retains unread notifications and any records still within their retention window.</description></item>
    /// </list>
    /// </summary>
    /// <param name="referenceTimeUtc">Optional reference timestamp used as "now" (useful for determinism in integration tests).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary DTO detailing the number of purged rows per entity.</returns>
    Task<RetentionSweepSummaryDto> SweepAsync(
        DateTimeOffset? referenceTimeUtc = null,
        CancellationToken cancellationToken = default);
}
