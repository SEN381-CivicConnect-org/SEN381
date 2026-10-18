using System.Security.Claims;
using System.Text.Json;
using CivicConnect.Data;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicConnect.Web.Services;

/// <summary>
/// Metadata describing one baseline role from <see cref="RoleConfiguration.BaselineRoles"/> and its hierarchical rank.
/// </summary>
public sealed record RoleDefinition(
    short Id,
    string Name,
    string DisplayName,
    int Level,
    string PortalRole);

/// <summary>
/// Projection of a user account and their current effective authorization role for the role management grid.
/// </summary>
public sealed record ManagedUserSummary(
    Guid UserId,
    string FullName,
    string Email,
    string CurrentRoleName,
    string CurrentRoleDisplayName,
    int CurrentRoleLevel,
    string PortalRole,
    bool IsActive,
    DateTimeOffset? GrantedAt = null);

/// <summary>
/// Outcome of a hierarchical role delegation request.
/// </summary>
public sealed record RoleDelegationResult(
    bool Succeeded,
    Guid? TargetUserId = null,
    string? PreviousRole = null,
    string? AssignedRole = null,
    string? ErrorMessage = null)
{
    public static RoleDelegationResult Success(Guid targetUserId, string previousRole, string assignedRole) =>
        new(true, targetUserId, previousRole, assignedRole);

    public static RoleDelegationResult Forbidden(string message) =>
        new(false, ErrorMessage: message);

    public static RoleDelegationResult ValidationFailed(string message) =>
        new(false, ErrorMessage: message);

    public static RoleDelegationResult Failed(
        string message = "Unable to update the user's role right now. Please try again.") =>
        new(false, ErrorMessage: message);
}

/// <summary>
/// Contract for hierarchical role lookup, assignable-role filtering, and backend-enforced role delegation.
/// </summary>
public interface IRoleDelegationService
{
    IReadOnlyList<RoleDefinition> AllRoles { get; }

    RoleDefinition? FindRole(string? roleName);

    int GetRoleLevel(string? roleName);

    RoleDefinition GetEffectiveRole(ClaimsPrincipal? actor);

    IReadOnlyList<RoleDefinition> GetAssignableRoles(ClaimsPrincipal? actor);

    IReadOnlyList<RoleDefinition> GetAssignableRoles(string? actorRoleName);

    bool CanDelegateRole(
        string? actorRoleName,
        string? targetRoleName,
        string? targetUserCurrentRoleName = null);

    Task<IReadOnlyList<ManagedUserSummary>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    Task<RoleDelegationResult> DelegateRoleAsync(
        ClaimsPrincipal actor,
        Guid targetUserId,
        string newRoleName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Enforces hierarchical authorization delegation across CivicConnect's five baseline roles
/// (<c>reporter</c> &lt; <c>technician</c> &lt; <c>supervisor</c> &lt; <c>operations manager</c> &lt; <c>administrator</c>),
/// ensuring an actor can only grant roles up to and including their own authorization level before persisting
/// changes to PostgreSQL.
/// </summary>
public sealed class RoleDelegationService : IRoleDelegationService
{
    /// <summary>
    /// Authoritative role hierarchy ordered from lowest privilege (Level 1) to highest privilege (Level 5),
    /// matching <see cref="RoleConfiguration.BaselineRoles"/>.
    /// </summary>
    public static readonly IReadOnlyList<RoleDefinition> BaselineHierarchy =
        RoleConfiguration.BaselineRoles
            .OrderBy(r => r.Id)
            .Select(r => new RoleDefinition(
                r.Id,
                r.Name,
                FormatDisplayName(r.Name),
                Level: r.Id,
                PortalRole: MapToPortalRole(r.Name)))
            .ToArray();

    private readonly CivicConnectDbContext? _dbContext;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly AuditService? _auditService;
    private readonly ILogger<RoleDelegationService>? _logger;

    [ActivatorUtilitiesConstructor]
    public RoleDelegationService(
        IServiceScopeFactory scopeFactory,
        AuditService? auditService = null,
        ILogger<RoleDelegationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _auditService = auditService;
        _logger = logger;
    }

    public RoleDelegationService(
        CivicConnectDbContext dbContext,
        AuditService? auditService = null,
        ILogger<RoleDelegationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    public IReadOnlyList<RoleDefinition> AllRoles => BaselineHierarchy;

    public RoleDefinition? FindRole(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return null;
        }

        var normalized = roleName.Trim();

        var directMatch = BaselineHierarchy.FirstOrDefault(r =>
            string.Equals(r.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));

        if (directMatch is not null)
        {
            return directMatch;
        }

        // Map portal role aliases if supplied by claims without an explicit DatabaseRole claim
        return normalized.ToLowerInvariant() switch
        {
            "requester" => BaselineHierarchy[0],          // reporter (Level 1)
            "staff" => BaselineHierarchy[1],              // technician (Level 2)
            "management" => BaselineHierarchy[3],         // operations manager (Level 4)
            "admin" => BaselineHierarchy[4],              // administrator (Level 5)
            _ => null
        };
    }

    public int GetRoleLevel(string? roleName) => FindRole(roleName)?.Level ?? 0;

    public RoleDefinition GetEffectiveRole(ClaimsPrincipal? actor)
    {
        if (actor?.Identity?.IsAuthenticated != true)
        {
            return BaselineHierarchy[0];
        }

        // Prefer explicit DatabaseRole claims first
        var dbRoleClaims = actor.FindAll("DatabaseRole")
            .Select(c => FindRole(c.Value))
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderByDescending(r => r.Level)
            .FirstOrDefault();

        if (dbRoleClaims is not null)
        {
            return dbRoleClaims;
        }

        // Fall back to standard ClaimTypes.Role claims
        var roleClaims = actor.FindAll(ClaimTypes.Role)
            .Select(c => FindRole(c.Value))
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderByDescending(r => r.Level)
            .FirstOrDefault();

        return roleClaims ?? BaselineHierarchy[0];
    }

    public IReadOnlyList<RoleDefinition> GetAssignableRoles(ClaimsPrincipal? actor)
    {
        if (actor?.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        var effectiveRole = GetEffectiveRole(actor);
        return GetAssignableRoles(effectiveRole.Name);
    }

    public IReadOnlyList<RoleDefinition> GetAssignableRoles(string? actorRoleName)
    {
        var actorLevel = GetRoleLevel(actorRoleName);
        if (actorLevel <= 0)
        {
            return [];
        }

        return BaselineHierarchy
            .Where(r => r.Level <= actorLevel)
            .OrderBy(r => r.Level)
            .ToArray();
    }

    public bool CanDelegateRole(
        string? actorRoleName,
        string? targetRoleName,
        string? targetUserCurrentRoleName = null)
    {
        var actorRole = FindRole(actorRoleName);
        var targetRole = FindRole(targetRoleName);

        if (actorRole is null || targetRole is null)
        {
            return false;
        }

        // Actor can only grant roles up to and including their own role level
        if (targetRole.Level > actorRole.Level)
        {
            return false;
        }

        // Actor cannot modify or demote a user who currently holds a higher role than the actor
        if (!string.IsNullOrWhiteSpace(targetUserCurrentRoleName))
        {
            var currentTargetRole = FindRole(targetUserCurrentRoleName);
            if (currentTargetRole is not null && currentTargetRole.Level > actorRole.Level)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<IReadOnlyList<ManagedUserSummary>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        if (_scopeFactory is not null)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var scopedContext = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
            return await LoadUsersFromDatabaseAsync(scopedContext, cancellationToken);
        }

        return await LoadUsersFromDatabaseAsync(_dbContext!, cancellationToken);
    }

    public async Task<RoleDelegationResult> DelegateRoleAsync(
        ClaimsPrincipal actor,
        Guid targetUserId,
        string newRoleName,
        CancellationToken cancellationToken = default)
    {
        if (actor?.Identity?.IsAuthenticated != true)
        {
            return RoleDelegationResult.Forbidden("You must be signed in to manage user roles.");
        }

        if (targetUserId == Guid.Empty)
        {
            return RoleDelegationResult.ValidationFailed("A valid target user must be selected.");
        }

        var requestedRole = FindRole(newRoleName);
        if (requestedRole is null)
        {
            return RoleDelegationResult.ValidationFailed($"'{newRoleName}' is not a valid CivicConnect authorization role.");
        }

        // First check claims-based role hierarchy before opening a DB transaction
        var claimEffectiveRole = GetEffectiveRole(actor);
        if (requestedRole.Level > claimEffectiveRole.Level)
        {
            return RoleDelegationResult.Forbidden(
                $"Insufficient privileges: your role ('{claimEffectiveRole.DisplayName}', Level {claimEffectiveRole.Level}) " +
                $"cannot grant '{requestedRole.DisplayName}' (Level {requestedRole.Level}).");
        }

        if (_scopeFactory is not null)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var scopedContext = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
            return await ExecuteRoleDelegationTransactionAsync(
                scopedContext,
                actor,
                claimEffectiveRole,
                targetUserId,
                requestedRole,
                cancellationToken);
        }

        return await ExecuteRoleDelegationTransactionAsync(
            _dbContext!,
            actor,
            claimEffectiveRole,
            targetUserId,
            requestedRole,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ManagedUserSummary>> LoadUsersFromDatabaseAsync(
        CivicConnectDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var users = await dbContext.AppUsers
            .AsNoTracking()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        var summaries = new List<ManagedUserSummary>(users.Count);
        foreach (var user in users)
        {
            var highestGrant = user.UserRoles
                .Where(ur => ur.Role is not null)
                .Select(ur => (Grant: ur, Definition: FindRole(ur.Role.Name) ?? BaselineHierarchy[0]))
                .OrderByDescending(x => x.Definition.Level)
                .FirstOrDefault();

            var roleDef = highestGrant.Definition ?? BaselineHierarchy[0];
            DateTimeOffset? grantedAt = highestGrant.Grant?.GrantedAt;

            summaries.Add(new ManagedUserSummary(
                user.Id,
                user.FullName,
                user.Email,
                roleDef.Name,
                roleDef.DisplayName,
                roleDef.Level,
                roleDef.PortalRole,
                user.IsActive,
                grantedAt));
        }

        return summaries;
    }

    private async Task<RoleDelegationResult> ExecuteRoleDelegationTransactionAsync(
        CivicConnectDbContext dbContext,
        ClaimsPrincipal actor,
        RoleDefinition claimEffectiveRole,
        Guid targetUserId,
        RoleDefinition requestedRole,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // 1. Resolve actor's authoritative database identity and role level
            Guid? actorUserId = null;
            var actorIdClaim = actor.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(actorIdClaim, out var parsedActorId))
            {
                actorUserId = parsedActorId;
            }
            else
            {
                var actorEmail = actor.FindFirstValue(ClaimTypes.Email);
                if (!string.IsNullOrWhiteSpace(actorEmail))
                {
                    actorUserId = await dbContext.AppUsers
                        .AsNoTracking()
                        .Where(u => u.Email.ToLower() == actorEmail.ToLower() && u.IsActive)
                        .Select(u => (Guid?)u.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }
            }

            var effectiveActorRole = claimEffectiveRole;
            if (actorUserId.HasValue)
            {
                var actorDbUser = await dbContext.AppUsers
                    .AsNoTracking()
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == actorUserId.Value, cancellationToken);

                if (actorDbUser is null || !actorDbUser.IsActive)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    return RoleDelegationResult.Forbidden("Authenticated actor account is not active in the database.");
                }

                var dbHighestRole = actorDbUser.UserRoles
                    .Where(ur => ur.Role is not null)
                    .Select(ur => FindRole(ur.Role.Name))
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .OrderByDescending(r => r.Level)
                    .FirstOrDefault();

                if (dbHighestRole is not null)
                {
                    effectiveActorRole = dbHighestRole;
                }
            }

            // 2. Backend enforcement: actor can only grant roles up to and including their own role level
            if (requestedRole.Level > effectiveActorRole.Level)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return RoleDelegationResult.Forbidden(
                    $"Insufficient privileges: your role ('{effectiveActorRole.DisplayName}', Level {effectiveActorRole.Level}) " +
                    $"cannot grant '{requestedRole.DisplayName}' (Level {requestedRole.Level}).");
            }

            // 3. Load target user and their current roles from PostgreSQL
            var targetUser = await dbContext.AppUsers
                .AsNoTracking()
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);

            if (targetUser is null)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return RoleDelegationResult.ValidationFailed("The selected user account was not found.");
            }

            var currentHighestRole = targetUser.UserRoles
                .Where(ur => ur.Role is not null)
                .Select(ur => FindRole(ur.Role.Name))
                .Where(r => r is not null)
                .Select(r => r!)
                .OrderByDescending(r => r.Level)
                .FirstOrDefault() ?? BaselineHierarchy[0];

            // Prevent a lower-tier user from modifying or demoting a user who currently outranks them
            if (!CanDelegateRole(effectiveActorRole.Name, requestedRole.Name, currentHighestRole.Name))
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return RoleDelegationResult.Forbidden(
                    $"Insufficient privileges: your role ('{effectiveActorRole.DisplayName}', Level {effectiveActorRole.Level}) " +
                    $"cannot modify a user holding '{currentHighestRole.DisplayName}' (Level {currentHighestRole.Level}).");
            }

            // Resolve the database Role.Id for the requested role
            var dbRoleId = await dbContext.Roles
                .AsNoTracking()
                .Where(r => r.Name == requestedRole.Name)
                .Select(r => (short?)r.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? requestedRole.Id;

            var grantedByUserId = actorUserId ?? targetUser.Id;
            var existingGrants = targetUser.UserRoles.ToList();

            // Update user_role using INSERT / UPDATE only (respecting NFR-10 where DELETE is revoked from civic_app)
            if (existingGrants.Count == 0)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO user_role (user_id, role_id, granted_by_user_id, granted_at)
                    VALUES ({targetUser.Id}, {dbRoleId}, {grantedByUserId}, now())
                    """,
                    cancellationToken);
            }
            else if (existingGrants.Count == 1)
            {
                var previousRoleId = existingGrants[0].RoleId;
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE user_role
                    SET role_id = {dbRoleId},
                        granted_by_user_id = {grantedByUserId},
                        granted_at = now()
                    WHERE user_id = {targetUser.Id} AND role_id = {previousRoleId}
                    """,
                    cancellationToken);
            }
            else
            {
                // If multiple role rows exist, collapse/update existing rows or insert if needed without DELETE
                if (existingGrants.Any(ur => ur.RoleId == dbRoleId))
                {
                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE user_role
                        SET granted_by_user_id = {grantedByUserId},
                            granted_at = now()
                        WHERE user_id = {targetUser.Id} AND role_id = {dbRoleId}
                        """,
                        cancellationToken);
                }
                else
                {
                    var highestExistingRoleId = existingGrants
                        .OrderByDescending(ur => GetRoleLevel(ur.Role?.Name))
                        .First().RoleId;

                    await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE user_role
                        SET role_id = {dbRoleId},
                            granted_by_user_id = {grantedByUserId},
                            granted_at = now()
                        WHERE user_id = {targetUser.Id} AND role_id = {highestExistingRoleId}
                        """,
                        cancellationToken);
                }
            }

            // Record append-only audit_log row in PostgreSQL (strictly omitting email and password_hash per NFR-04 / POPIA s19(1))
            var beforeJson = JsonSerializer.Serialize(new { role = currentHighestRole.Name, level = currentHighestRole.Level });
            var afterJson = JsonSerializer.Serialize(new { role = requestedRole.Name, level = requestedRole.Level });

            dbContext.AuditLogs.Add(new AuditLog
            {
                ActorId = grantedByUserId,
                EntityType = AuditEntityType.RoleGrant,
                EntityId = targetUser.Id.ToString(),
                Action = existingGrants.Count == 0 ? AuditAction.Create : AuditAction.Update,
                BeforeState = beforeJson,
                AfterState = afterJson
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _auditService?.Record(
                grantedByUserId.ToString(),
                "RoleGrant",
                targetUser.Id.ToString(),
                "UPDATE",
                new Dictionary<string, object?> { ["Role"] = currentHighestRole.DisplayName },
                new Dictionary<string, object?> { ["Role"] = requestedRole.DisplayName });

            _logger?.LogInformation(
                "Actor {ActorId} ({ActorRole}) updated role for user {TargetUserId} from '{PreviousRole}' to '{NewRole}'.",
                grantedByUserId,
                effectiveActorRole.Name,
                targetUser.Id,
                currentHighestRole.Name,
                requestedRole.Name);

            return RoleDelegationResult.Success(targetUser.Id, currentHighestRole.Name, requestedRole.Name);
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            _logger?.LogError(ex, "Failed to update role for user {TargetUserId}.", targetUserId);
            return RoleDelegationResult.Failed();
        }
    }

    public static string FormatDisplayName(string roleName) => roleName.Trim().ToLowerInvariant() switch
    {
        "reporter" => "Reporter",
        "technician" => "Technician",
        "supervisor" => "Supervisor",
        "operations manager" => "Operations Manager",
        "administrator" => "Administrator",
        _ => roleName
    };

    public static string MapToPortalRole(string roleName) => roleName.Trim().ToLowerInvariant() switch
    {
        "administrator" or "operations manager" => "Management",
        "technician" or "supervisor" => "Staff",
        _ => "Requester"
    };
}
