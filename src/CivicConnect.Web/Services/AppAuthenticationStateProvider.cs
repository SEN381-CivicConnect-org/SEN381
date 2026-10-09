using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace CivicConnect.Web.Services;

public sealed record AuthenticatedUser(string Name, string Email, string Role, string Initials);

/// Checks a user's credentials against the database and tracks the signed-in identity for this circuit.
public sealed class AppAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private static readonly PasswordHasher<object> Hasher = new();

    private readonly NpgsqlDataSource _dataSource;
    private ClaimsPrincipal _currentUser = Anonymous;

    public AppAuthenticationStateProvider(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public AuthenticatedUser? CurrentUser { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_currentUser));

    /// Looks up the user by email, verifies their password hash, and signs them in if both check out.
    public async Task<bool> SignInAsync(string email, string password)
    {
        var trimmedEmail = email.Trim();

        await using var cmd = _dataSource.CreateCommand(
            """
            SELECT u.full_name, u.password_hash,
                   coalesce(array_agg(r.name) FILTER (WHERE r.name IS NOT NULL), '{}'::text[]) AS role_names
            FROM app_user u
            LEFT JOIN user_role ur ON ur.user_id = u.id
            LEFT JOIN role r ON r.id = ur.role_id
            WHERE u.email = $1 AND u.is_active
            GROUP BY u.id
            """);
        cmd.Parameters.AddWithValue(trimmedEmail);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return false;

        var fullName = reader.GetString(0);
        var passwordHash = reader.GetString(1);
        var roleNames = reader.GetFieldValue<string[]>(2);

        if (Hasher.VerifyHashedPassword(null!, passwordHash, password) == PasswordVerificationResult.Failed)
            return false;

        var portalRole = PortalRoleFor(roleNames);
        var initials = string.Join("", fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => n[0])).ToUpperInvariant();

        CurrentUser = new AuthenticatedUser(fullName, trimmedEmail, portalRole, initials);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, fullName),
            new Claim(ClaimTypes.Email, trimmedEmail),
            new Claim(ClaimTypes.Role, portalRole)
        }, "CivicConnect");

        _currentUser = new ClaimsPrincipal(identity);
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
