using System.Security.Claims;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;

namespace CivicConnect.Tests;

/// <summary>
/// FR-07 and docs/decisions/0004-status-transitions.md.
/// Exercise the public action used by TicketDetail, then read public results.
/// No private methods, lifecycle-table introspection, or state mutation during actions.
/// </summary>
public sealed class RequestStatusStateTransitionTests
{
    [Theory]
    [InlineData("Open", "ASSIGNED", "Assigned", "NEW", "ASSIGNED")]
    [InlineData("Assigned", "IN_PROGRESS", "In Progress", "ASSIGNED", "IN_PROGRESS")]
    [InlineData("In Progress", "RESOLVED", "Resolved", "IN_PROGRESS", "RESOLVED")]
    [InlineData("Resolved", "CLOSED", "Closed", "RESOLVED", "CLOSED")]
    public void ST_ForwardLifecycle_StaffAction_TransitionsToDocumentedNextState(
        string initial, string action, string expected, string previousHistory, string nextHistory)
    {
        var service = Arrange(initial);
        var before = DateTime.UtcNow;

        var result = service.ChangeStatus("ST-REQUEST", action, User("Staff"));

        Assert.True(result.Success, result.Message);
        var ticket = Assert.IsType<Ticket>(service.Find("ST-REQUEST"));
        Assert.Equal(expected, ticket.Status);
        var entry = Assert.Single(ticket.StatusHistory);
        Assert.Equal(previousHistory, entry.PreviousStatus);
        Assert.Equal(nextHistory, entry.NewStatus);
        Assert.Equal("Test Staff", entry.Actor);
        Assert.Equal(DateTimeKind.Utc, entry.ChangedAt.Kind);
        Assert.InRange(entry.ChangedAt, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Open", "ASSIGNED", "Assigned")]
    [InlineData("Assigned", "IN_PROGRESS", "In Progress")]
    [InlineData("In Progress", "RESOLVED", "Resolved")]
    [InlineData("Resolved", "CLOSED", "Closed")]
    public void ST_AcceptedTransition_ReadAgain_RetainsNextState(
        string initial, string action, string expected)
    {
        var service = Arrange(initial);
        var result = service.ChangeStatus("ST-REQUEST", action, User("Staff"));
        Assert.True(result.Success, result.Message);
        Assert.Equal(expected, service.Find("ST-REQUEST")?.Status);

        // Re-read through the public query after the action; no second mutation.
        // This is in-memory state retention, not database/restart persistence.
        var reloaded = Assert.IsType<Ticket>(service.Find("ST-REQUEST"));
        Assert.Equal(expected, reloaded.Status);
        Assert.Single(reloaded.StatusHistory);
    }

    [Fact]
    public void ST_OpenRequest_DirectResolveAction_IsRejectedAndRemainsOpen()
    {
        var service = Arrange("Open");

        var result = service.ChangeStatus("ST-REQUEST", "RESOLVED", User("Staff"));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        var ticket = Assert.IsType<Ticket>(service.Find("ST-REQUEST"));
        Assert.Equal("Open", ticket.Status);
        Assert.Empty(ticket.StatusHistory);
    }

    [Theory]
    [InlineData("Requester")]
    [InlineData("Management")]
    [InlineData(null)]
    public void ST_OpenRequest_RestrictedActorAssignAction_IsRejectedAndRemainsOpen(string? role)
    {
        var service = Arrange("Open");
        var actor = role is null ? new ClaimsPrincipal(new ClaimsIdentity()) : User(role);

        var result = service.ChangeStatus("ST-REQUEST", "ASSIGNED", actor);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        var ticket = Assert.IsType<Ticket>(service.Find("ST-REQUEST"));
        Assert.Equal("Open", ticket.Status);
        Assert.Empty(ticket.StatusHistory);
    }

    private static TicketService Arrange(string initial)
    {
        var service = new TicketService();
        service.Tickets.Clear();
        // Set the starting state only in setup. All tested changes use the public action.
        service.Tickets.Add(new Ticket
        {
            Id = "ST-REQUEST", Title = "State transition request", Status = initial,
            Requester = "Test Requester", Assignee = "Test Staff", Category = "IT Support"
        });
        return service;
    }

    private static ClaimsPrincipal User(string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Name, $"Test {role}"),
        new Claim(ClaimTypes.Role, role)
    }, "Test"));
}
