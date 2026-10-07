using System.Reflection;
using System.Security.Claims;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Npgsql;

// Simulates post-database-login state without a live database or production bypass.
// Reads, role navigation, and sign-out use the real AppAuthenticationStateProvider.
sealed class AuthFixture : IDisposable
{
    private readonly NpgsqlDataSource source = NpgsqlDataSource.Create("Host=localhost;Database=unused;Username=unused");
    public AppAuthenticationStateProvider Provider { get; }
    public AuthFixture() => Provider = new(source);

    public bool SignIn(string email, string password, string? role = null, bool includeEmail = true)
    {
        role ??= email.StartsWith("staff") ? "Staff" : email.StartsWith("manager") ? "Management" : "Requester";
        var claims = new List<Claim> { new(ClaimTypes.Name, "Same display name"), new(ClaimTypes.Role, role) };
        if (includeEmail) claims.Add(new(ClaimTypes.Email, email));
        typeof(AppAuthenticationStateProvider).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Provider, new ClaimsPrincipal(new ClaimsIdentity(claims, "CivicConnect")));
        typeof(AppAuthenticationStateProvider).GetProperty("CurrentUser")!.SetValue(Provider, new AuthenticatedUser("Same display name", email, role, "SD"));
        typeof(AuthenticationStateProvider).GetMethod("NotifyAuthenticationStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Provider, new object[] { Provider.GetAuthenticationStateAsync() });
        return true;
    }

    public void SignOut() => Provider.SignOut();
    public void Dispose() => source.Dispose();
}
