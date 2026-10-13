using System.Security.Cryptography;
using System.Text;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicConnect.Api.Services;

/// <summary>
/// Exception raised when an idempotency key is replayed by the same reporter with a different request payload (409 Conflict).
/// </summary>
public sealed class IdempotencyConflictException : InvalidOperationException
{
    public IdempotencyConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Request payload for concurrency-safe ticket intake and incident clustering (FR-01, FR-03, FR-05, NFR-06).
/// </summary>
public sealed record SubmitTicketRequestDto
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required int CategoryId { get; init; }
    public required int LocationId { get; init; }
    public Guid? ReporterId { get; init; }
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Result returned after a submission is clustered onto an open incident or creates a new incident (FR-03, FR-05, NFR-06).
/// </summary>
public sealed record SubmissionResultDto(
    long Id,
    string ReferenceNumber,
    long? TicketId,
    string? TicketReferenceNumber,
    string Title,
    string Description,
    int CategoryId,
    string CategoryName,
    int LocationId,
    string LocationName,
    short ImpactLevel,
    string ImpactName,
    short UrgencyLevel,
    string UrgencyName,
    short ComputedPriority,
    string ComputedPriorityName,
    short Priority,
    string PriorityName,
    bool PriorityOverridden,
    IncidentStatus Status,
    int? AssignedTeamId,
    DateTimeOffset OpenedAt,
    DateTimeOffset DueAt,
    int ReportCount,
    bool CreatedNewIncident,
    bool IsIdempotentReplay);

/// <summary>
/// Contract for concurrency-safe ticket submission, idempotency, and open-incident clustering (FR-03, NFR-06).
/// </summary>
public interface ISubmissionService
{
    Task<SubmissionResultDto> SubmitAsync(
        SubmitTicketRequestDto request,
        Func<Incident, CancellationToken, Task>? onAfterIncidentStaged = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements concurrency-safe ticket intake, open-incident clustering via <c>incident_one_open_per_scope_uq</c>,
/// idempotent reporter subscriptions, and transactional rollback guarantees (NFR-06, IT-CONC-01, IT-TXN-01).
/// </summary>
public sealed class SubmissionService : ISubmissionService
{
    private readonly CivicConnectDbContext _dbContext;
    private readonly IPriorityMatrix _priorityMatrix;

    public SubmissionService(CivicConnectDbContext dbContext, IPriorityMatrix? priorityMatrix = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _priorityMatrix = priorityMatrix ?? new PriorityMatrix();
    }

    public async Task<SubmissionResultDto> SubmitAsync(
        SubmitTicketRequestDto request,
        Func<Incident, CancellationToken, Task>? onAfterIncidentStaged = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Title and Description are required.");
        }

        var trimmedTitle = request.Title.Trim();
        var trimmedDescription = request.Description.Trim();
        var trimmedIdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.IsActive, cancellationToken);

        if (category is null)
        {
            throw new KeyNotFoundException($"Active Category ID {request.CategoryId} not found.");
        }

        var location = await _dbContext.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.IsActive, cancellationToken);

        if (location is null)
        {
            throw new KeyNotFoundException($"Active Location ID {request.LocationId} not found.");
        }

        var reporterId = request.ReporterId;
        var requestFingerprint = ComputeFingerprint(category.Id, location.Id, trimmedTitle, trimmedDescription);

        // Fast-path idempotency check when ReporterId and IdempotencyKey are supplied.
        if (reporterId.HasValue && trimmedIdempotencyKey is not null)
        {
            var existingReplay = await TryGetIdempotentReplayAsync(
                reporterId.Value,
                trimmedIdempotencyKey,
                requestFingerprint,
                category,
                location,
                cancellationToken);

            if (existingReplay is not null)
            {
                return existingReplay;
            }
        }

        short priorityId = _priorityMatrix.CalculatePriority(location.ImpactLevel, category.UrgencyLevel);

        var ownedTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var activeTransaction = ownedTransaction ?? _dbContext.Database.CurrentTransaction!;

        try
        {
            // Serialize concurrent open-incident check/create for the same (category_id, location_id) scope at the PostgreSQL transaction level.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({category.Id}, {location.Id});",
                cancellationToken);

            var incident = await FindOpenIncidentAsync(category.Id, location.Id, cancellationToken);
            var createdNewIncident = false;

            if (incident is null)
            {
                incident = new Incident
                {
                    CategoryId = category.Id,
                    LocationId = location.Id,
                    Title = trimmedTitle,
                    Description = trimmedDescription,
                    Status = IncidentStatus.New,
                    ComputedPriority = priorityId,
                    Priority = priorityId,
                    PriorityOverridden = false,
                    AssignedTeamId = category.DefaultServiceTeamId
                };

                _dbContext.Incidents.Add(incident);
                await _dbContext.SaveChangesAsync(cancellationToken);
                createdNewIncident = true;
            }

            if (onAfterIncidentStaged is not null)
            {
                await onAfterIncidentStaged(incident, cancellationToken);
            }

            Ticket? ticket = null;

            if (reporterId.HasValue)
            {
                var reporterIsActive = await _dbContext.AppUsers
                    .AsNoTracking()
                    .Where(u => u.Id == reporterId.Value)
                    .Select(u => (bool?)u.IsActive)
                    .FirstOrDefaultAsync(cancellationToken);

                if (reporterIsActive == false)
                {
                    throw new InvalidOperationException($"Reporter '{reporterId.Value}' is inactive.");
                }

                if (createdNewIncident)
                {
                    _dbContext.IncidentStatusHistories.Add(new IncidentStatusHistory
                    {
                        IncidentId = incident.Id,
                        FromStatus = null,
                        ToStatus = IncidentStatus.New,
                        ChangedBy = reporterId.Value,
                        ChangedAt = incident.OpenedAt != default ? incident.OpenedAt : DateTimeOffset.UtcNow,
                        AssignedTeamId = incident.AssignedTeamId
                    });
                }

                ticket = new Ticket
                {
                    IncidentId = incident.Id,
                    ReporterId = reporterId.Value,
                    CategoryId = category.Id,
                    LocationId = location.Id,
                    Title = trimmedTitle,
                    Description = trimmedDescription
                };
                _dbContext.Tickets.Add(ticket);

                await _dbContext.SaveChangesAsync(cancellationToken);

                // Atomic, idempotent subscription upsert so concurrent submissions by the same reporter never fail or duplicate.
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO incident_subscription (incident_id, reporter_id)
                       VALUES ({incident.Id}, {reporterId.Value})
                       ON CONFLICT (incident_id, reporter_id) DO NOTHING;",
                    cancellationToken);

                if (trimmedIdempotencyKey is not null)
                {
                    const string idempotencySavepoint = "create_submission_request";
                    await activeTransaction.CreateSavepointAsync(idempotencySavepoint, cancellationToken);

                    var submissionRequest = new SubmissionRequest
                    {
                        ReporterId = reporterId.Value,
                        IdempotencyKey = trimmedIdempotencyKey,
                        RequestFingerprint = requestFingerprint,
                        TicketId = ticket.Id,
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
                    };

                    try
                    {
                        _dbContext.SubmissionRequests.Add(submissionRequest);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, "pk_submission_request"))
                    {
                        await activeTransaction.RollbackToSavepointAsync(idempotencySavepoint, cancellationToken);
                        _dbContext.Entry(submissionRequest).State = EntityState.Detached;

                        if (ownedTransaction is not null)
                        {
                            await ownedTransaction.RollbackAsync(cancellationToken);
                        }

                        var replayResult = await TryGetIdempotentReplayAsync(
                            reporterId.Value,
                            trimmedIdempotencyKey,
                            requestFingerprint,
                            category,
                            location,
                            cancellationToken);

                        if (replayResult is not null)
                        {
                            return replayResult;
                        }

                        throw;
                    }
                }
            }

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            var reportCount = await _dbContext.Tickets
                .AsNoTracking()
                .CountAsync(t => t.IncidentId == incident.Id, cancellationToken);

            return BuildResultDto(
                incident,
                ticket,
                category,
                location,
                reportCount,
                createdNewIncident,
                isIdempotentReplay: false);
        }
        catch
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }

    private Task<Incident?> FindOpenIncidentAsync(
        int categoryId,
        int locationId,
        CancellationToken cancellationToken)
    {
        return _dbContext.Incidents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.CategoryId == categoryId
                  && i.LocationId == locationId
                  && (i.Status == IncidentStatus.New
                      || i.Status == IncidentStatus.Assigned
                      || i.Status == IncidentStatus.InProgress
                      || i.Status == IncidentStatus.OnHold),
                cancellationToken);
    }

    private async Task<SubmissionResultDto?> TryGetIdempotentReplayAsync(
        Guid reporterId,
        string idempotencyKey,
        string requestFingerprint,
        Category category,
        Location location,
        CancellationToken cancellationToken)
    {
        var existingSubmission = await _dbContext.SubmissionRequests
            .AsNoTracking()
            .Include(sr => sr.Ticket)
            .FirstOrDefaultAsync(
                sr => sr.ReporterId == reporterId && sr.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existingSubmission is null)
        {
            return null;
        }

        if (!string.Equals(existingSubmission.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException(
                $"Idempotency key '{idempotencyKey}' has already been used by reporter '{reporterId}' with different request parameters.");
        }

        var existingIncident = await _dbContext.Incidents
            .AsNoTracking()
            .FirstAsync(i => i.Id == existingSubmission.Ticket.IncidentId, cancellationToken);

        var reportCount = await _dbContext.Tickets
            .AsNoTracking()
            .CountAsync(t => t.IncidentId == existingIncident.Id, cancellationToken);

        return BuildResultDto(
            existingIncident,
            existingSubmission.Ticket,
            category,
            location,
            reportCount,
            createdNewIncident: false,
            isIdempotentReplay: true);
    }

    private SubmissionResultDto BuildResultDto(
        Incident incident,
        Ticket? ticket,
        Category category,
        Location location,
        int reportCount,
        bool createdNewIncident,
        bool isIdempotentReplay)
    {
        return new SubmissionResultDto(
            Id: incident.Id,
            ReferenceNumber: incident.ReferenceNumber,
            TicketId: ticket?.Id,
            TicketReferenceNumber: ticket?.ReferenceNumber,
            Title: incident.Title,
            Description: incident.Description,
            CategoryId: incident.CategoryId,
            CategoryName: category.Name,
            LocationId: incident.LocationId,
            LocationName: location.Name,
            ImpactLevel: location.ImpactLevel,
            ImpactName: _priorityMatrix.FormatLevelName(location.ImpactLevel),
            UrgencyLevel: category.UrgencyLevel,
            UrgencyName: _priorityMatrix.FormatLevelName(category.UrgencyLevel),
            ComputedPriority: incident.ComputedPriority,
            ComputedPriorityName: _priorityMatrix.FormatPriorityName(incident.ComputedPriority),
            Priority: incident.Priority,
            PriorityName: _priorityMatrix.FormatPriorityName(incident.Priority),
            PriorityOverridden: incident.PriorityOverridden,
            Status: incident.Status,
            AssignedTeamId: incident.AssignedTeamId,
            OpenedAt: incident.OpenedAt,
            DueAt: incident.DueAt,
            ReportCount: reportCount,
            CreatedNewIncident: createdNewIncident,
            IsIdempotentReplay: isIdempotentReplay);
    }

    private static string ComputeFingerprint(int categoryId, int locationId, string title, string description)
    {
        var canonical = $"{categoryId}|{locationId}|{title}|{description}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception, string constraintName)
    {
        if (exception.InnerException is PostgresException pgEx
            && pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return string.IsNullOrEmpty(constraintName)
                || string.Equals(pgEx.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
