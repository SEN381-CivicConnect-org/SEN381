using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Services;

/// <summary>
/// Application-layer contract for FR-10 Asset Linkage on Resolution and AP-10 Finance Asset Replacement Reporting.
/// </summary>
public interface IResolutionService
{
    Task<ResolutionResultDto> ResolveIncidentAsync(
        long incidentId,
        Guid resolvedByUserId,
        string summary,
        short? resolutionCodeId = null,
        string? resolutionCode = null,
        int? assetId = null,
        string? assetTag = null,
        CancellationToken cancellationToken = default);

    Task<ResolutionResultDto> ResolveIncidentAsync(
        long incidentId,
        ResolveIncidentRequestDto request,
        Guid resolvedByUserId = default,
        CancellationToken cancellationToken = default);

    Task<Incident> ReopenIncidentAsync(
        long incidentId,
        Guid changedByUserId,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinanceAssetReplacementReportItemDto>> GetFinanceReplacementReportAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResolutionCodeDto>> GetActiveResolutionCodesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements FR-10 (Asset Linkage on Resolution), FR-07 status history coordination,
/// and AP-10 Finance Asset Replacement reporting against the official CivicConnectDbContext schema.
/// </summary>
public sealed class ResolutionService : IResolutionService
{
    public const short CodeIdFixed = 1;
    public const short CodeIdNoFaultFound = 2;
    public const short CodeIdDuplicate = 3;
    public const short CodeIdHardwareReplaced = 4;
    public const short CodeIdBeyondRepair = 5;

    private readonly CivicConnectDbContext _context;

    public ResolutionService(CivicConnectDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<ResolutionResultDto> ResolveIncidentAsync(
        long incidentId,
        ResolveIncidentRequestDto request,
        Guid resolvedByUserId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var effectiveUserId = resolvedByUserId != Guid.Empty
            ? resolvedByUserId
            : request.ResolvedBy ?? Guid.Empty;

        return ResolveIncidentAsync(
            incidentId: incidentId,
            resolvedByUserId: effectiveUserId,
            summary: request.Summary,
            resolutionCodeId: request.ResolutionCodeId,
            resolutionCode: request.ResolutionCode,
            assetId: request.AssetId,
            assetTag: request.AssetTag,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Resolves an incident with a seeded resolution code, concise summary, and mandatory asset linkage
    /// whenever the resolution code implies equipment replacement (FR-10, IT-RES-01, IT-RES-02).
    /// </summary>
    public async Task<ResolutionResultDto> ResolveIncidentAsync(
        long incidentId,
        Guid resolvedByUserId,
        string summary,
        short? resolutionCodeId = null,
        string? resolutionCode = null,
        int? assetId = null,
        string? assetTag = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("A concise resolution summary is required.");
        }

        var trimmedSummary = summary.Trim();
        if (trimmedSummary.Length > 500)
        {
            throw new InvalidOperationException("Resolution summary must not exceed 500 characters.");
        }

        if (resolvedByUserId == Guid.Empty)
        {
            throw new InvalidOperationException("A valid resolving user ID (resolvedByUserId) is required.");
        }

        var incident = await _context.Incidents
            .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

        if (incident is null)
        {
            throw new KeyNotFoundException($"Incident with ID {incidentId} was not found.");
        }

        var previousStatus = incident.Status;
        bool isPermittedTransition = previousStatus is IncidentStatus.InProgress or IncidentStatus.OnHold;

        if (isPermittedTransition && await _context.IncidentStatusTransitions.AnyAsync(cancellationToken))
        {
            isPermittedTransition = await _context.IncidentStatusTransitions
                .AsNoTracking()
                .AnyAsync(
                    t => t.FromStatus == previousStatus && t.ToStatus == IncidentStatus.Resolved,
                    cancellationToken);
        }

        if (!isPermittedTransition)
        {
            throw new InvalidOperationException(
                $"Incident {incident.Id} cannot transition from '{previousStatus}' to '{IncidentStatus.Resolved}'. " +
                "Only incidents in 'InProgress' or 'OnHold' status can be resolved.");
        }

        if (!resolutionCodeId.HasValue && string.IsNullOrWhiteSpace(resolutionCode))
        {
            throw new InvalidOperationException("A resolution code (resolutionCodeId or resolutionCode) is required.");
        }

        ResolutionCode? codeEntity = null;
        if (resolutionCodeId.HasValue)
        {
            codeEntity = await _context.ResolutionCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == resolutionCodeId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(resolutionCode))
        {
            var rawCode = resolutionCode.Trim().ToUpperInvariant();
            var mappedCode = rawCode == "REPLACED" ? "HARDWARE_REPLACED" : rawCode;

            codeEntity = await _context.ResolutionCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.Code.ToUpper() == rawCode || c.Code.ToUpper() == mappedCode,
                    cancellationToken);
        }

        if (codeEntity is null)
        {
            throw new KeyNotFoundException(
                $"Resolution code '{resolutionCode ?? resolutionCodeId?.ToString()}' was not found.");
        }

        if (!codeEntity.IsActive)
        {
            throw new InvalidOperationException(
                $"Resolution code '{codeEntity.Code}' is inactive and cannot be used for new resolutions.");
        }

        bool hasAssetInput = assetId.HasValue || !string.IsNullOrWhiteSpace(assetTag);
        if (codeEntity.ImpliesReplacement && !hasAssetInput)
        {
            throw new InvalidOperationException(
                $"Resolution code '{codeEntity.Code}' implies equipment replacement and requires an asset link (assetId or assetTag).");
        }

        Asset? assetEntity = null;
        if (hasAssetInput)
        {
            var trimmedTag = assetTag?.Trim();
            if (assetId.HasValue && !string.IsNullOrWhiteSpace(trimmedTag))
            {
                assetEntity = await _context.Assets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        a => a.Id == assetId.Value || a.AssetTag == trimmedTag,
                        cancellationToken);
            }
            else if (assetId.HasValue)
            {
                assetEntity = await _context.Assets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == assetId.Value, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(trimmedTag))
            {
                assetEntity = await _context.Assets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.AssetTag == trimmedTag, cancellationToken);
            }

            if (assetEntity is null)
            {
                throw new KeyNotFoundException(
                    $"Asset '{trimmedTag ?? assetId?.ToString()}' was not found.");
            }

            if (!assetEntity.IsActive)
            {
                throw new InvalidOperationException(
                    $"Asset '{assetEntity.AssetTag}' is inactive and cannot be linked to a resolution.");
            }
        }

        var ownedTransaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            if (_context.Database.IsNpgsql() && _context.Entry(incident).State == EntityState.Modified)
            {
                // Keep incident Unchanged in EF Core so resolution_before_insert_trg updates status & resolved_at
                // in PostgreSQL without causing a double-version-bump concurrency conflict.
                _context.Entry(incident).State = EntityState.Unchanged;
            }

            await EnsureStatusHistoryUpToStatusAsync(
                incident,
                previousStatus,
                resolvedByUserId,
                cancellationToken);

            var now = DateTimeOffset.UtcNow;

            var statusHistory = new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = previousStatus,
                ToStatus = IncidentStatus.Resolved,
                ChangedBy = resolvedByUserId,
                ChangedAt = now,
                Note = trimmedSummary,
                AssignedTeamId = incident.AssignedTeamId,
                AssigneeId = incident.AssigneeId
            };
            _context.IncidentStatusHistories.Add(statusHistory);

            var resolution = new Resolution
            {
                IncidentId = incident.Id,
                ResolutionCodeId = codeEntity.Id,
                Summary = trimmedSummary,
                AssetId = assetEntity?.Id,
                NeedsReplacement = codeEntity.ImpliesReplacement,
                ResolvedBy = resolvedByUserId,
                ResolvedAt = now,
                SupersededAt = null
            };

            if (!_context.Database.IsNpgsql())
            {
                var priorActiveResolutions = await _context.Resolutions
                    .Where(r => r.IncidentId == incident.Id && r.SupersededAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var prior in priorActiveResolutions)
                {
                    prior.SupersededAt = now;
                }

                incident.Status = IncidentStatus.Resolved;
                incident.ResolvedAt = now;
            }

            _context.Resolutions.Add(resolution);
            await _context.SaveChangesAsync(cancellationToken);

            if (_context.Database.IsNpgsql())
            {
                await _context.Entry(incident).ReloadAsync(cancellationToken);
                await _context.Entry(resolution).ReloadAsync(cancellationToken);

                foreach (var trackedResolution in _context.ChangeTracker.Entries<Resolution>()
                             .Where(e => e.Entity.IncidentId == incident.Id && e.Entity.Id != resolution.Id)
                             .ToList())
                {
                    await trackedResolution.ReloadAsync(cancellationToken);
                }
            }

            if (ownedTransaction is not null)
            {
                await ownedTransaction.CommitAsync(cancellationToken);
            }

            return new ResolutionResultDto(
                ResolutionId: resolution.Id,
                IncidentId: incident.Id,
                IncidentReferenceNumber: incident.ReferenceNumber ?? $"INC-{incident.Id:D6}",
                IncidentStatus: incident.Status,
                ResolutionCodeId: codeEntity.Id,
                ResolutionCode: codeEntity.Code,
                ResolutionCodeName: codeEntity.Name,
                Summary: resolution.Summary,
                AssetId: assetEntity?.Id,
                AssetTag: assetEntity?.AssetTag,
                Supplier: assetEntity?.Supplier,
                PurchaseDate: assetEntity?.PurchaseDate,
                NeedsReplacement: resolution.NeedsReplacement,
                ResolvedBy: resolution.ResolvedBy,
                ResolvedAt: resolution.ResolvedAt,
                SupersededAt: resolution.SupersededAt);
        }
        finally
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Reopens a resolved incident (RESOLVED -> IN_PROGRESS) and appends the status transition history row (IT-RES-02).
    /// </summary>
    public async Task<Incident> ReopenIncidentAsync(
        long incidentId,
        Guid changedByUserId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (changedByUserId == Guid.Empty)
        {
            throw new InvalidOperationException("A valid user ID (changedByUserId) is required to reopen an incident.");
        }

        var incident = await _context.Incidents
            .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

        if (incident is null)
        {
            throw new KeyNotFoundException($"Incident with ID {incidentId} was not found.");
        }

        if (incident.Status != IncidentStatus.Resolved)
        {
            throw new InvalidOperationException(
                $"Incident {incident.Id} cannot be reopened from status '{incident.Status}'. Only 'Resolved' incidents can be reopened.");
        }

        await EnsureStatusHistoryUpToStatusAsync(
            incident,
            IncidentStatus.Resolved,
            changedByUserId,
            cancellationToken);

        incident.Status = IncidentStatus.InProgress;
        incident.ResolvedAt = null;

        _context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.Resolved,
            ToStatus = IncidentStatus.InProgress,
            ChangedBy = changedByUserId,
            ChangedAt = DateTimeOffset.UtcNow,
            Note = string.IsNullOrWhiteSpace(note) ? "Incident reopened" : note.Trim(),
            AssignedTeamId = incident.AssignedTeamId,
            AssigneeId = incident.AssigneeId
        });

        await _context.SaveChangesAsync(cancellationToken);
        return incident;
    }

    /// <summary>
    /// Queries the Finance Asset Replacement Report over an optional time window (FR-10, AP-10).
    /// </summary>
    public async Task<IReadOnlyList<FinanceAssetReplacementReportItemDto>> GetFinanceReplacementReportAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Resolutions
            .AsNoTracking()
            .Where(r => r.AssetId != null && r.NeedsReplacement);

        if (activeOnly)
        {
            query = query.Where(r => r.SupersededAt == null);
        }

        if (from.HasValue)
        {
            query = query.Where(r => r.ResolvedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(r => r.ResolvedAt <= to.Value);
        }

        return await query
            .OrderByDescending(r => r.ResolvedAt)
            .Select(r => new FinanceAssetReplacementReportItemDto(
                r.Id,
                r.IncidentId,
                r.Incident.ReferenceNumber,
                r.Incident.Title,
                r.ResolutionCode.Code,
                r.ResolutionCode.Name,
                r.Summary,
                r.Asset!.Id,
                r.Asset.AssetTag,
                r.Asset.Description,
                r.Asset.Supplier,
                r.Asset.PurchaseDate,
                r.ResolvedBy,
                r.ResolvedByUser.FullName,
                r.ResolvedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResolutionCodeDto>> GetActiveResolutionCodesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ResolutionCodes
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .Select(c => new ResolutionCodeDto(
                c.Id,
                c.Code,
                c.Name,
                c.ImpliesReplacement,
                c.IsActive))
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureStatusHistoryUpToStatusAsync(
        Incident incident,
        IncidentStatus targetStatus,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var lastHistory = await _context.IncidentStatusHistories
            .AsNoTracking()
            .Where(h => h.IncidentId == incident.Id)
            .OrderByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        IncidentStatus currentHistoryStatus;
        if (lastHistory is null)
        {
            _context.IncidentStatusHistories.Add(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = null,
                ToStatus = IncidentStatus.New,
                ChangedBy = actorUserId,
                ChangedAt = incident.OpenedAt != default ? incident.OpenedAt : DateTimeOffset.UtcNow,
                Note = "Incident created",
                AssignedTeamId = incident.AssignedTeamId,
                AssigneeId = incident.AssigneeId
            });
            await _context.SaveChangesAsync(cancellationToken);
            currentHistoryStatus = IncidentStatus.New;
        }
        else
        {
            currentHistoryStatus = lastHistory.ToStatus;
        }

        foreach (var (fromStatus, toStatus) in GetBridgeTransitions(currentHistoryStatus, targetStatus))
        {
            _context.IncidentStatusHistories.Add(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                ChangedBy = incident.AssigneeId ?? actorUserId,
                ChangedAt = DateTimeOffset.UtcNow,
                AssignedTeamId = incident.AssignedTeamId,
                AssigneeId = incident.AssigneeId
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<(IncidentStatus From, IncidentStatus To)> GetBridgeTransitions(
        IncidentStatus current,
        IncidentStatus target)
    {
        if (current == target)
        {
            return Array.Empty<(IncidentStatus, IncidentStatus)>();
        }

        return (current, target) switch
        {
            (IncidentStatus.New, IncidentStatus.Assigned) => new[]
            {
                (IncidentStatus.New, IncidentStatus.Assigned)
            },
            (IncidentStatus.New, IncidentStatus.InProgress) => new[]
            {
                (IncidentStatus.New, IncidentStatus.Assigned),
                (IncidentStatus.Assigned, IncidentStatus.InProgress)
            },
            (IncidentStatus.New, IncidentStatus.OnHold) => new[]
            {
                (IncidentStatus.New, IncidentStatus.Assigned),
                (IncidentStatus.Assigned, IncidentStatus.OnHold)
            },
            (IncidentStatus.New, IncidentStatus.Resolved) => new[]
            {
                (IncidentStatus.New, IncidentStatus.Assigned),
                (IncidentStatus.Assigned, IncidentStatus.InProgress),
                (IncidentStatus.InProgress, IncidentStatus.Resolved)
            },
            (IncidentStatus.Assigned, IncidentStatus.InProgress) => new[]
            {
                (IncidentStatus.Assigned, IncidentStatus.InProgress)
            },
            (IncidentStatus.Assigned, IncidentStatus.OnHold) => new[]
            {
                (IncidentStatus.Assigned, IncidentStatus.OnHold)
            },
            (IncidentStatus.Assigned, IncidentStatus.Resolved) => new[]
            {
                (IncidentStatus.Assigned, IncidentStatus.InProgress),
                (IncidentStatus.InProgress, IncidentStatus.Resolved)
            },
            (IncidentStatus.InProgress, IncidentStatus.OnHold) => new[]
            {
                (IncidentStatus.InProgress, IncidentStatus.OnHold)
            },
            (IncidentStatus.InProgress, IncidentStatus.Resolved) => new[]
            {
                (IncidentStatus.InProgress, IncidentStatus.Resolved)
            },
            (IncidentStatus.OnHold, IncidentStatus.InProgress) => new[]
            {
                (IncidentStatus.OnHold, IncidentStatus.InProgress)
            },
            (IncidentStatus.OnHold, IncidentStatus.Resolved) => new[]
            {
                (IncidentStatus.OnHold, IncidentStatus.Resolved)
            },
            (IncidentStatus.Resolved, IncidentStatus.InProgress) => new[]
            {
                (IncidentStatus.Resolved, IncidentStatus.InProgress)
            },
            (IncidentStatus.Resolved, IncidentStatus.OnHold) => new[]
            {
                (IncidentStatus.Resolved, IncidentStatus.InProgress),
                (IncidentStatus.InProgress, IncidentStatus.OnHold)
            },
            _ => Array.Empty<(IncidentStatus, IncidentStatus)>()
        };
    }
}

public sealed record ResolveIncidentRequestDto
{
    public short? ResolutionCodeId { get; init; }
    public string? ResolutionCode { get; init; }
    public string Summary { get; init; } = string.Empty;
    public int? AssetId { get; init; }
    public string? AssetTag { get; init; }
    public Guid? ResolvedBy { get; init; }
}

public sealed record ReopenIncidentRequestDto
{
    public string? Note { get; init; }
    public Guid? ChangedBy { get; init; }
}

public sealed record ResolutionResultDto(
    long ResolutionId,
    long IncidentId,
    string IncidentReferenceNumber,
    IncidentStatus IncidentStatus,
    short ResolutionCodeId,
    string ResolutionCode,
    string ResolutionCodeName,
    string Summary,
    int? AssetId,
    string? AssetTag,
    string? Supplier,
    DateOnly? PurchaseDate,
    bool NeedsReplacement,
    Guid ResolvedBy,
    DateTimeOffset ResolvedAt,
    DateTimeOffset? SupersededAt)
{
    public long Id => ResolutionId;
    public IncidentStatus Status => IncidentStatus;
}

public sealed record FinanceAssetReplacementReportItemDto(
    long ResolutionId,
    long IncidentId,
    string IncidentReferenceNumber,
    string IncidentTitle,
    string ResolutionCode,
    string ResolutionCodeName,
    string Summary,
    int AssetId,
    string AssetTag,
    string AssetDescription,
    string Supplier,
    DateOnly PurchaseDate,
    Guid ResolvedBy,
    string ResolvedByName,
    DateTimeOffset ResolvedAt);

public sealed record ResolutionCodeDto(
    short Id,
    string Code,
    string Name,
    bool ImpliesReplacement,
    bool IsActive);
