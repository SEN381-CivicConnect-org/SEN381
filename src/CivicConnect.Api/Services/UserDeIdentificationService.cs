using CivicConnect.Data;
Using CivicConnect.Data.Entities'
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicConnect.Api.Services;

/// <summary>
/// Implements NFR-05 (Retention and Disposal of Personal Information) and IT-RET-02 (DeIdentifyKeepsIncidents)
/// in accordance with POPIA s14(1) and the Requirements Traceability Matrix.
/// Executes irreversible soft de-identification of user credentials and identity while preserving incident history.
/// </summary>
public sealed class UserDeIdentificationService : IUserDeIdentificationService
{
    /// <summary>
    /// Generic identifier replacing the user's real name.
    /// </summary>
    public const string GenericFullName = "De-identified User";

    /// <summary>
    /// Sentinel password hash that is permanently invalid and impossible to authenticate against.
    /// </summary>
    public const string InvalidSentinelPasswordHash = "*DEIDENTIFIED*";

    private readonly CivicConnectDbContext _dbContext;
    private readonly ILogger<UserDeIdentificationService>? _logger;

    public UserDeIdentificationService(
        CivicConnectDbContext dbContext,
        ILogger<UserDeIdentificationService>? logger = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UserDeIdentificationResultDto> DeIdentifyUserAsync(
        Guid userId,
        Guid ActorId,
        Guid actorId
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        var hasExternalTransaction = _dbContext.Database.CurrentTransaction != null;
        await using var transaction = hasExternalTransaction
            ? null
            : await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var user = await _dbContext.AppUsers
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                throw new KeyNotFoundException($"User with ID '{userId}' was not found.");
            }

            var anonymizedEmail = $"deidentified-{user.Id:D}@redacted.local";

            user.FullName = GenericFullName;
            user.Email = anonymizedEmail;
            user.PasswordHash = InvalidSentinelPasswordHash;
            user.IsActive = false;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            // Close out any active team memberships per soft-delete / tenure tracking conventions
            var activeMemberships = await _dbContext.TeamMembers
                .Where(tm => tm.UserId == userId && tm.LeftAt == null)
                .ToListAsync(cancellationToken);

            foreach (var tm in activeMemberships)
            {
                tm.LeftAt = DateTimeOffset.UtcNow;
                tm.UpdatedAt = DateTimeOffset.UtcNow;
            }
// Erasure is an auditable event ; no personal data in the payload.
            _dbContext.AuditLogs.Add(new AuditLog
            {
                ActorId = actorId,
                EntityType = AuditEntityType.User,
                EntityId = user.Id.ToString("D"),
                Action = AuditAction.Update,
                AfterState = "{\"operation\":\"de-identify\"}",
                OccurredAt = DateTimeOffset.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Tally preserved tickets and incidents to verify data integrity under IT-RET-02
            var ticketsPreserved = await _dbContext.Tickets
                .AsNoTracking()
                .CountAsync(t => t.ReporterId == userId, cancellationToken);

            var incidentsPreserved = await _dbContext.Incidents
                .AsNoTracking()
                .CountAsync(i => _dbContext.Tickets.Any(t => t.IncidentId == i.Id && t.ReporterId == userId), cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger?.LogInformation(
                "Irreversibly de-identified user {UserId}: isActive=false, preservedTickets={Tickets}, preservedIncidents={Incidents}.",

                user.Id,
                anonymizedEmail,
                ticketsPreserved,
                incidentsPreserved);

            return new UserDeIdentificationResultDto
            {
                UserId = user.Id,
                AnonymizedEmail = user.Email,
                GenericFullName = user.FullName,
                IsActive = user.IsActive,
                DeIdentifiedAtUtc = DateTimeOffset.UtcNow,
                AssociatedTicketsPreserved = ticketsPreserved,
                AssociatedIncidentsPreserved = incidentsPreserved
            };
        }
        catch (Exception ex)
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger?.LogError(ex, "Failed to de-identify user {UserId}.", userId);
            throw;
        }
    }
}
