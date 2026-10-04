using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

var navigation = new TestNavigation();
var auth = new DemoAuthenticationStateProvider();
using var workspace = new WorkspaceNavigation(navigation, auth);
Assert(workspace.ReturnToWorkspace == "/login", "Anonymous recovery must lead to sign-in.");
foreach (var (email, origin, otherRole) in new[] {
    ("requester@civicconnect.demo", "/requester/tickets", "/staff"),
    ("staff@civicconnect.demo", "/staff/assigned", "/requester/tickets"),
    ("manager@civicconnect.demo", "/management/tickets", "/staff/queue") })
{
    Assert(auth.SignIn(email, "password"), "Demo account sign-in.");
    navigation.NavigateTo(origin);
    workspace.RequesterFilter = "In Progress";
    navigation.NavigateTo("/tickets/CC-014");
    Assert(workspace.ReturnToWorkspace == origin, "Ticket must return to its original role list.");
    navigation.NavigateTo("/tickets/missing");
    Assert(workspace.ReturnToWorkspace == origin, "Missing ticket must preserve origin.");
    navigation.NavigateTo(otherRole);
    navigation.NavigateTo("/access-denied");
    Assert(workspace.ReturnToWorkspace == origin, "Denied routes must not replace safe context.");
    Assert(!workspace.IsAllowed(otherRole), "Role-invalid return URL must be rejected.");
    foreach (var bad in new[] { "https://example.com", "//example.com", "/\\example.com", "/unknown", "/login", "/access-denied", "/staff\n", "", "tickets/CC-014" })
        Assert(workspace.AfterSignIn(bad) == workspace.Home, "Unsafe or unknown return must fall back to role home.");
    Assert(workspace.AfterSignIn("/tickets/CC-014") == "/tickets/CC-014", "Sign-in must resume a local shared ticket.");
    workspace.GoBack();
    Assert(navigation.ToBaseRelativePath(navigation.Uri) == origin.TrimStart('/'), "Back destination must be safe.");
    auth.SignOut();
    Assert(workspace.ReturnToWorkspace == "/login", "Sign-out clears role context.");
    Assert(workspace.RequesterFilter == "All", "Account change clears list state.");
}
auth.SignIn("requester@civicconnect.demo", "password");
navigation.NavigateTo("/requester/new");
navigation.NavigateTo("/tickets/CC-015");
Assert(workspace.ReturnToWorkspace == "/requester/tickets", "New ticket must return to list rather than another submission form.");
workspace.RequesterFilter = "Resolved";
navigation.NavigateTo("/requester/tickets");
navigation.NavigateTo("/tickets/CC-012");
workspace.GoBack();
Assert(workspace.RequesterFilter == "Resolved", "Returning from detail preserves requester filter.");

var handler = new DemoAuthorizationResultHandler();
var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
var context = new DefaultHttpContext();
context.Request.Method = "GET";
context.Request.Path = "/staff/assigned";
context.Request.QueryString = new QueryString("?filter=open");
await handler.HandleAsync(_ => throw new Exception("Unauthorized endpoint executed."), context, policy, PolicyAuthorizationResult.Challenge());
Assert(context.Response.StatusCode == 302, "Direct protected GET must redirect rather than throw.");
Assert(context.Response.Headers.Location.ToString() == "/login?returnUrl=%2Fstaff%2Fassigned%3Ffilter%3Dopen", "Challenge must preserve local requested URL.");
context = new DefaultHttpContext();
context.Request.Method = "POST";
await handler.HandleAsync(_ => throw new Exception("Forbidden endpoint executed."), context, policy, PolicyAuthorizationResult.Forbid());
Assert(context.Response.StatusCode == 403, "Forbidden POST must keep authorization enforced.");
Console.WriteLine("All navigation and authorization recovery checks passed.");

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("http://localhost/", "http://localhost/login");
    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Uri = ToAbsoluteUri(uri).AbsoluteUri;
        NotifyLocationChanged(false);
    }
}
