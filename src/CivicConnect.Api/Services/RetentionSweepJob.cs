using System.Diagnostics;
using CivicConnect.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CivicConnect.Api.Services;

/// <summary>
/// Implements NFR-05 (Retention and Disposal of Personal Information) and IT-RET-01 (RetentionSweep)
/// in accordance with POPIA s14(1) and the Requirements Traceability Matrix.
/// Purges expired idempotency records and aged read notifications within a transaction.
/// </summary>
public sealed class RetentionSweepJob : IRetentionSweepService
{
    /// <summary>
    /// Default retention window for idempotency tokens (7 days).
    /// </summary>
    public const int IdempotencyRetentionDays = 7;

    /// <summary>
    /// Default retention window for read notifications (12 months / 365 days).
    /// </summary>
    public const int ReadNotificationRetentionDays = 365;

    private readonly CivicConnectDbContext _dbContext;
    private readonly ILogger<RetentionSweepJob>? _logger;

    public RetentionSweepJob(
        CivicConnectDbContext dbContext,
        ILogger<RetentionSweepJob>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RetentionSweepSummaryDto> SweepAsync(
        DateTimeOffset? referenceTimeUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = referenceTimeUtc ?? DateTimeOffset.UtcNow;
        var idempotencyCutoff = now.AddDays(-IdempotencyRetentionDays);
        var notificationCutoff = now.AddDays(-ReadNotificationRetentionDays);

        var stopwatch = Stopwatch.StartNew();

        var hasExternalTransaction = _dbContext.Database.CurrentTransaction != null;
        await using var transaction = hasExternalTransaction
            ? null
            : await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            int purgedSubmissionRequests;
            int purgedNotifications;

            try
            {
                purgedSubmissionRequests = await _dbContext.SubmissionRequests
                    .Where(sr => sr.ExpiresAt < now || sr.RequestedAt < idempotencyCutoff)
                    .ExecuteDeleteAsync(cancellationToken);

                purgedNotifications = await _dbContext.Notifications
                    .Where(n => n.ReadAt != null && n.ReadAt < notificationCutoff)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Fallback for providers that do not support relational ExecuteDeleteAsync (e.g., InMemory)
                var expiredRequests = await _dbContext.SubmissionRequests
                    .Where(sr => sr.ExpiresAt < now || sr.RequestedAt < idempotencyCutoff)
                    .ToListAsync(cancellationToken);

                _dbContext.SubmissionRequests.RemoveRange(expiredRequests);
                purgedSubmissionRequests = expiredRequests.Count;

                var expiredNotifications = await _dbContext.Notifications
                    .Where(n => n.ReadAt != null && n.ReadAt < notificationCutoff)
                    .ToListAsync(cancellationToken);

                _dbContext.Notifications.RemoveRange(expiredNotifications);
                purgedNotifications = expiredNotifications.Count;

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            stopwatch.Stop();

            _logger?.LogInformation(
                "Retention sweep executed at {ExecutedAt}: purged {PurgedRequests} submission requests (cutoff: {IdemCutoff}) and {PurgedNotifications} read notifications (cutoff: {NotifCutoff}) in {ElapsedMs} ms.",
                now,
                purgedSubmissionRequests,
                idempotencyCutoff,
                purgedNotifications,
                notificationCutoff,
                stopwatch.ElapsedMilliseconds);

            return new RetentionSweepSummaryDto
            {
                PurgedSubmissionRequests = purgedSubmissionRequests,
                PurgedNotifications = purgedNotifications,
                ExecutedAtUtc = now,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger?.LogError(ex, "Retention sweep failed at {ExecutedAt}.", now);
            throw;
        }
    }
}

/// <summary>
/// Background hosted service that executes periodic automated retention sweeps (POPIA s14(1), NFR-05).
/// </summary>
public sealed class RetentionSweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionSweepHostedService> _logger;
    private readonly TimeSpan _period;
    private readonly TimeSpan _initialDelay;

    public RetentionSweepHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<RetentionSweepHostedService> logger,
        IConfiguration? configuration = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var hours = configuration?.GetValue<double>("RetentionSweep:IntervalHours") ?? 24.0;
        _period = TimeSpan.FromHours(Math.Max(1.0, hours));

        var initialDelayMinutes = configuration?.GetValue<double>("RetentionSweep:InitialDelayMinutes") ?? 60.0;
        _initialDelay = TimeSpan.FromMinutes(Math.Max(0.0, initialDelayMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_initialDelay > TimeSpan.Zero)
            {
                await Task.Delay(_initialDelay, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sweepService = scope.ServiceProvider.GetRequiredService<IRetentionSweepService>();
                    var result = await sweepService.SweepAsync(cancellationToken: stoppingToken);

                    _logger.LogInformation(
                        "Periodic retention sweep completed: {PurgedRequests} submission requests, {PurgedNotifications} notifications in {ElapsedMs} ms.",
                        result.PurgedSubmissionRequests,
                        result.PurgedNotifications,
                        result.ElapsedMilliseconds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Periodic retention sweep encountered an unhandled error.");
                }

                await Task.Delay(_period, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal graceful shutdown
        }
    }
}
