using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CivicConnect.Web.Services;

public sealed record DemoUser(string Name, string Email, string Role, string Initials);

public sealed class DemoAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal _currentUser = Anonymous;

    private static readonly Dictionary<string, (string Password, DemoUser User)> Accounts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["requester@civicconnect.demo"] = ("password", new("Bernard Small", "requester@civicconnect.demo", "Requester", "BS")),
            ["staff@civicconnect.demo"] = ("password", new("Daniel Jacobs", "staff@civicconnect.demo", "Staff", "DJ")),
            ["manager@civicconnect.demo"] = ("password", new("Michael Adams", "manager@civicconnect.demo", "Management", "MA"))
        };

    public DemoUser? CurrentUser { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_currentUser));

    public bool SignIn(string email, string password)
    {
        if (!Accounts.TryGetValue(email.Trim(), out var account) || account.Password != password)
            return false;

        CurrentUser = account.User;
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, account.User.Name),
            new Claim(ClaimTypes.Email, account.User.Email),
            new Claim(ClaimTypes.Role, account.User.Role)
        }, "CivicConnectDemo");

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
}
