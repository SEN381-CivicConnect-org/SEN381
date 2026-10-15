using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

namespace CivicConnect.Tests;

public class RequesterOwnershipTests
{
    private static ClaimsPrincipal User(string name, string id, string role = "Requester") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Role, role)
        }, "Test"));

    [Theory]
    [InlineData("Bernard Small", "A", "CC-014", true)]
    [InlineData("Bernard Small", "A", "CC-010", false)]
    [InlineData("Ayesha Khan", "B", "CC-014", false)]
    [InlineData("Ayesha Khan", "B", "CC-010", true)]
    public void LegacyFixturesAreFilteredBeforeReturningToRequester(string name, string id, string ticketId, bool allowed)
    {
        var service = new TicketService();
        var user = User(name, id);
        Assert.Equal(allowed, service.GetVisibleTickets(user).Any(ticket => ticket.Id == ticketId));
        Assert.Equal(allowed, service.FindVisible(ticketId, user) is not null);
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Management")]
    public void BroaderRolesRetainAllTicketAccess(string role)
    {
        var service = new TicketService();
        var user = User("Other account", "other", role);
        Assert.Equal(service.Tickets, service.GetVisibleTickets(user));
        Assert.NotNull(service.FindVisible("CC-010", user));
        Assert.NotNull(service.FindVisible("CC-014", user));
    }

    [Fact]
    public void AnonymousAndUnknownRolesHaveNoVisibility()
    {
        var service = new TicketService();
        foreach (var user in new[] { new ClaimsPrincipal(), User("Bernard Small", "A", "Unknown") })
        {
            Assert.Empty(service.GetVisibleTickets(user));
            Assert.Null(service.FindVisible("CC-014", user));
        }
        Assert.Null(service.FindVisible("missing", User("Bernard Small", "A")));
    }

    [Theory]
    [InlineData("Bernard Small", "A")]
    [InlineData("Ayesha Khan", "B")]
    public void CreationIgnoresSpoofedOwnershipAndCopiesAuthenticatedIdentity(string name, string id)
    {
        var service = new TicketService();
        var user = User(name, id);
        var input = new Ticket { Title = "Owned request", Requester = "Spoofed owner", RequesterId = "spoofed" };
        var createdId = service.CreateForRequester(input, user);
        var saved = service.Find(createdId)!;
        Assert.Equal(name, saved.Requester);
        Assert.Equal(id, saved.RequesterId);
        Assert.Equal("Open", saved.Status);
        Assert.Same(saved, service.FindVisible(createdId, user));
        Assert.Null(service.FindVisible(createdId, User(name, "different-account-with-same-name")));
        // Ownership, like priority data, belongs to the saved snapshot, not live form state.
        input.Requester = "Changed";
        input.RequesterId = "Changed";
        Assert.Equal(name, saved.Requester);
        Assert.Equal(id, saved.RequesterId);
    }

    [Fact]
    public void StableIdSurvivesRenameAndNeverFallsBackToDisplayName()
    {
        var service = new TicketService();
        var id = service.CreateForRequester(new Ticket { Title = "Rename test" }, User("Original name", "A"));
        Assert.NotNull(service.FindVisible(id, User("Renamed", "A")));
        Assert.Null(service.FindVisible(id, User("Original name", "B")));
        var noId = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "Original name"), new Claim(ClaimTypes.Role, "Requester")
        }, "Test"));
        Assert.Null(service.FindVisible(id, noId));
    }

    [Fact]
    public void CreationRequiresAuthenticatedRequesterWithStableIdAndName()
    {
        var service = new TicketService();
        var missingId = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "Requester"), new Claim(ClaimTypes.Role, "Requester")
        }, "Test"));
        foreach (var user in new[] { new ClaimsPrincipal(), missingId, User("", "A"), User("Staff", "S", "Staff"), User("Manager", "M", "Management") })
            Assert.Throws<UnauthorizedAccessException>(() => service.CreateForRequester(new Ticket(), user));
        Assert.Equal(4, service.Tickets.Count);
    }

    [Fact]
    public void LegacyFallbackDoesNotMatchBlankOrPartialNames()
    {
        var service = new TicketService();
        Assert.Empty(service.GetVisibleTickets(User("", "A")));
        Assert.Empty(service.GetVisibleTickets(User("Bernard", "A")));
        Assert.Empty(service.GetVisibleTickets(User("bernard small", "A")));
    }
}
