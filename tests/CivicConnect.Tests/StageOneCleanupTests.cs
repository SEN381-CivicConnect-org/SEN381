using System.Security.Claims;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CivicConnect.Tests;

public class StageOneCleanupTests
{
    private static ClaimsPrincipal User(string name, string role, string? id = null, bool authenticated = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name), new(ClaimTypes.Role, role) };
        if (id is not null) claims.Add(new(ClaimTypes.NameIdentifier, id));
        return new(new ClaimsIdentity(claims, authenticated ? "Test" : null));
    }

    [Fact]
    public void AssignedTicketsFollowCurrentStaffIdentityAndFailClosed()
    {
        var service = new TicketService();
        Assert.Equal(new[] { "CC-014", "CC-013" },
            service.GetAssignedTickets(User("Daniel Jacobs", "Staff")).Select(t => t.Id));
        Assert.Empty(service.GetAssignedTickets(User("Another technician", "Staff")));
        Assert.Empty(service.GetAssignedTickets(User("Nadia Peters", "Staff"))); // resolved excluded
        Assert.Empty(service.GetAssignedTickets(User("Daniel Jacobs", "Requester")));
        Assert.Empty(service.GetAssignedTickets(User("Daniel Jacobs", "Staff", authenticated: false)));
        Assert.Empty(service.GetAssignedTickets(User("", "Staff")));

        var other = service.Create(new() { Title = "Assigned elsewhere", Assignee = "Another technician" });
        Assert.Equal(other, Assert.Single(service.GetAssignedTickets(User("Another technician", "Staff"))).Id);
        Assert.DoesNotContain(service.GetAssignedTickets(User("Daniel Jacobs", "Staff")), t => t.Id == other);
    }

    [Theory]
    [InlineData("Requester", true)]
    [InlineData("Staff", true)]
    [InlineData("Management", false)]
    public async Task DirectoryReadsRejectUnauthorizedUsersBeforeOpeningDatabase(string role, bool authenticated)
    {
        var data = new WorkspaceDataService(new NoDatabaseScopes(),
            new FixedAuthentication(User("User", role, authenticated: authenticated)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => data.GetUsersAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => data.GetCategoriesAsync());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("invalid", true)]
    [InlineData("d10a1e30-6d01-4fd2-b235-eb7138345603", false)]
    public async Task NotificationsRequireAuthenticatedStableRecipientBeforeOpeningDatabase(string? id, bool authenticated)
    {
        var data = new WorkspaceDataService(new NoDatabaseScopes(),
            new FixedAuthentication(User("User", "Requester", id, authenticated)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => data.GetUnreadNotificationsAsync());
    }

    private sealed class FixedAuthentication(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class NoDatabaseScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Unauthorized read attempted to access the database.");
    }
}
