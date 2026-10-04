using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace CivicConnect.Web.Services;

// Context belongs to one interactive circuit and is cleared when its account changes.
public sealed class WorkspaceNavigation : IDisposable
{
    private readonly NavigationManager _navigation;
    private readonly DemoAuthenticationStateProvider _auth;
    private string? _lastWorkspace;
    private string? _account;

    public string RequesterFilter { get; set; } = "All";

    public WorkspaceNavigation(NavigationManager navigation, DemoAuthenticationStateProvider auth)
    {
        _navigation = navigation;
        _auth = auth;
        _account = auth.CurrentUser?.Email;
        navigation.LocationChanged += LocationChanged;
        auth.AuthenticationStateChanged += AccountChanged;
        Remember(navigation.Uri);
    }

    public string Home => _auth.CurrentUser is null ? "/login" : _auth.HomeForCurrentRole();
    public string ReturnToWorkspace => IsAllowed(_lastWorkspace) ? _lastWorkspace! : Home;

    // Allow only known local routes for this account, never an arbitrary return URL.
    public bool IsAllowed(string? destination)
    {
        if (_auth.CurrentUser is null || string.IsNullOrWhiteSpace(destination) ||
            !destination.StartsWith('/') || destination.StartsWith("//") ||
            destination.Contains('\\') || destination.Any(char.IsControl)) return false;

        var path = destination.Split('?', '#')[0];
        if (path is "/notifications" or "/settings") return true;
        if (path.StartsWith("/tickets/", StringComparison.Ordinal) &&
            path.Length > "/tickets/".Length && !path["/tickets/".Length..].Contains('/')) return true;

        return _auth.CurrentUser.Role switch
        {
            "Requester" => path is "/requester/tickets" or "/requester/new",
            "Staff" => path is "/staff" or "/staff/queue" or "/staff/assigned",
            "Management" => path is "/management" or "/management/tickets" or "/management/users" or "/management/categories",
            _ => false
        };
    }

    public string AfterSignIn(string? requested) => IsAllowed(requested) ? requested! : Home;
    public void GoBack() => _navigation.NavigateTo(ReturnToWorkspace);

    private void LocationChanged(object? sender, LocationChangedEventArgs e) => Remember(e.Location);
    private void Remember(string absolute)
    {
        var relative = "/" + _navigation.ToBaseRelativePath(absolute);
        if (!IsAllowed(relative) || relative.StartsWith("/tickets/", StringComparison.Ordinal)) return;
        // Completing a new request returns to its list, never to a fresh submission form.
        _lastWorkspace = relative.Split('?', '#')[0] == "/requester/new" ? "/requester/tickets" : relative;
    }

    private void AccountChanged(Task<AuthenticationState> state)
    {
        if (_account == _auth.CurrentUser?.Email) return;
        _account = _auth.CurrentUser?.Email;
        _lastWorkspace = null;
        RequesterFilter = "All";
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= LocationChanged;
        _auth.AuthenticationStateChanged -= AccountChanged;
    }
}
