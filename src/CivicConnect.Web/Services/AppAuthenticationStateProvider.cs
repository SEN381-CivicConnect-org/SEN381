using System.Security.Claims;
using CivicConnect.Data.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicConnect.Web.Services;

public sealed record AuthenticatedUser(string Name, string Email, string Role, string Initials);

/// Checks a user's credentials against the database and tracks the signed-in identity for this circuit.
public sealed class AppAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly NpgsqlDataSource _dataSource;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ILogger<AppAuthenticationStateProvider>? _logger;
    private ClaimsPrincipal _currentUser = Anonymous;

    public AppAuthenticationStateProvider(
        NpgsqlDataSource dataSource,
        IPasswordHashingService? passwordHasher = null,
        ILogger<AppAuthenticationStateProvider>? logger = null)
    {
        _dataSource = dataSource;
        _passwordHasher = passwordHasher ?? Argon2idPasswordHasher.Instance;
        _logger = logger;
    }

    public AuthenticatedUser? CurrentUser { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_currentUser));

    /// Looks up the user by email, verifies their password hash, and signs them in if both check out.
    public async Task<bool> SignInAsync(string email, string password)
    {
        var trimmedEmail = email.Trim();

        Guid userId;
        string fullName;
        string passwordHash;
        string[] roleNames;
        string[] teamNames;

        await using (var cmd = _dataSource.CreateCommand(
            """
            SELECT u.id, u.full_name, u.password_hash,
                   coalesce(array_agg(DISTINCT r.name) FILTER (WHERE r.name IS NOT NULL), '{}'::text[]) AS role_names,
                   coalesce(array_agg(DISTINCT st.name) FILTER (WHERE st.name IS NOT NULL), '{}'::text[]) AS team_names
            FROM app_user u
            LEFT JOIN user_role ur ON ur.user_id = u.id
            LEFT JOIN role r ON r.id = ur.role_id
            LEFT JOIN team_member tm ON tm.user_id = u.id AND tm.left_at IS NULL
            LEFT JOIN service_team st ON st.id = tm.service_team_id
            WHERE u.email = $1 AND u.is_active
            GROUP BY u.id
            """))
        {
            cmd.Parameters.AddWithValue(trimmedEmail);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return false;

            userId = reader.GetGuid(0);
            fullName = reader.GetString(1);
            passwordHash = reader.GetString(2);
            roleNames = reader.GetFieldValue<string[]>(3);
            teamNames = reader.GetFieldValue<string[]>(4);
        }

        if (!_passwordHasher.VerifyPassword(password, passwordHash, out var needsRehash))
        {
            _logger?.LogWarning("Authentication failed for user {Email}.", trimmedEmail);
            return false;
        }

        if (needsRehash)
        {
            var upgradedHash = _passwordHasher.HashPassword(password);
            await using var upgradeCmd = _dataSource.CreateCommand(
                """
                UPDATE app_user
                SET password_hash = $1, updated_at = now()
                WHERE id = $2
                """);
            upgradeCmd.Parameters.AddWithValue(upgradedHash);
            upgradeCmd.Parameters.AddWithValue(userId);
            await upgradeCmd.ExecuteNonQueryAsync();

            _logger?.LogInformation(
                "Transparently upgraded Argon2id password hash parameters for user {UserId} ({Email}).",
                userId,
                trimmedEmail);
        }

        var portalRole = PortalRoleFor(roleNames);
        var initials = string.Join("", fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => n[0])).ToUpperInvariant();

        CurrentUser = new AuthenticatedUser(fullName, trimmedEmail, portalRole, initials);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Email, trimmedEmail),
            new(ClaimTypes.Role, portalRole)
        };

        foreach (var teamName in teamNames)
        {
            claims.Add(new Claim("Team", teamName));
        }

        var identity = new ClaimsIdentity(claims, "CivicConnect");

        _currentUser = new ClaimsPrincipal(identity);
        _logger?.LogInformation("User {UserId} ({Email}) signed in with portal role {PortalRole}.", userId, trimmedEmail, portalRole);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return true;
    }

    public void SignOut()
    {
        CurrentUser = null;
        _currentUser = Anonymous;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public string HomeForCurrentRole() => CurrentUser?.Role switch
    {
        "Staff" => "/staff",
        "Management" => "/management",
        _ => "/requester/tickets"
    };

    /// Maps a user's database roles onto the one portal they land on, favouring the most privileged role held.
    private static string PortalRoleFor(IEnumerable<string> roleNames)
    {
        var names = new HashSet<string>(roleNames, StringComparer.OrdinalIgnoreCase);
        if (names.Contains("administrator") || names.Contains("operations manager")) return "Management";
        if (names.Contains("technician") || names.Contains("supervisor")) return "Staff";
        return "Requester";
    }
}
