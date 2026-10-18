using System.Security.Claims;
using System.Text.Json;
using CivicConnect.Web.Components.Pages.Shared;
using CivicConnect.Web.Models;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicConnect.Tests;

/// <summary>
/// Regression tests verifying cross-feature invariants after the FR-11 requester ownership fix,
/// FR-07 status transition lifecycle, FR-16 public/internal note visibility, FR-05 priority
/// snapshot immutability, and FR-08 structured audit logging were integrated onto dev.
/// </summary>
public sealed class TicketWorkflowRegressionTests
{
    [Theory]
    [InlineData("Bernard Small", "USER-A", "REG-A")]
    [InlineData("Ayesha Khan", "USER-B", "REG-B")]
    public async Task REG_OwningRequesterDetail_RendersPublicNotesAndStatusHistory_WhileWithholdingInternalNotesAndStaffControls(
        string requesterName, string requesterId, string ticketId)
    {
        var tickets = CreateSeededService();
        var html = await RenderTicketDetailAsync(tickets, User(requesterName, requesterId, "Requester"), ticketId);

        // Owning requester still sees public note and status transition activity (FR-11 x FR-16 x FR-07)
        Assert.Contains($"Public update for {ticketId}", html);
        Assert.Contains("NEW → ASSIGNED", html);

        // Internal staff note and staff-only mutation controls are never disclosed to the requester
        Assert.DoesNotContain($"Internal diagnostic for {ticketId}", html);
        Assert.DoesNotContain("Add a note", html);
        Assert.DoesNotContain("Update status", html);
    }

    [Theory]
    [InlineData("Bernard Small", "USER-A", "REG-B")]
    [InlineData("Ayesha Khan", "USER-B", "REG-A")]
    public async Task REG_ForeignRequesterDetail_WithholdsAllNotesAndStatusHistory_WhenOwnershipDeniesTicket(
        string requesterName, string requesterId, string foreignTicketId)
    {
        var tickets = CreateSeededService();
        var html = await RenderTicketDetailAsync(tickets, User(requesterName, requesterId, "Requester"), foreignTicketId);

        // Ownership denial prevents loading or rendering any foreign notes or history (FR-11 x FR-16)
        Assert.Contains("Ticket not found", html);
        Assert.DoesNotContain($"Public update for {foreignTicketId}", html);
        Assert.DoesNotContain($"Internal diagnostic for {foreignTicketId}", html);
        Assert.DoesNotContain("NEW → ASSIGNED", html);
    }

    [Theory]
    [InlineData("Staff", "Daniel Jacobs", "STAFF-1")]
    [InlineData("Management", "Michael Adams", "MGR-1")]
    public async Task REG_PrivilegedRolesDetail_RetainsBothPublicAndInternalNoteVisibility_AfterOwnershipFix(
        string role, string actorName, string actorId)
    {
        var tickets = CreateSeededService();
        var html = await RenderTicketDetailAsync(tickets, User(actorName, actorId, role), "REG-A");

        Assert.Contains("Public update for REG-A", html);
        Assert.Contains("Internal diagnostic for REG-A", html);
        Assert.Contains("Add a note", html);
    }

    [Fact]
    public void REG_RequesterCreateFollowedByNotesAndFullLifecycle_PreservesPrioritySnapshotAndOwnership()
    {
        var audit = new AuditService();
        var service = new TicketService(new PriorityMatrix(), audit);
        var requester = User("Bernard Small", "USER-A", "Requester");
        var staff = User("Daniel Jacobs", "STAFF-1", "Staff");

        var submission = new Ticket
        {
            Title = "Server room cooling failure",
            Description = "Contact +27820001111 or bernard@example.com immediately",
            Category = "Facilities",
            CategoryId = 2,
            Location = "Ground Floor",
            LocationId = 3,
            ImpactLevel = PriorityMatrix.LevelHigh,       // 3
            UrgencyLevel = PriorityMatrix.LevelCritical,  // 4 -> Priority 1 (Critical)
            Requester = "Spoofed Name",
            RequesterId = "SPOOFED-ID"
        };

        var createdId = service.CreateForRequester(submission, requester);

        // Staff records both internal and public notes and advances through the full forward lifecycle
        service.AddNote(createdId, "  Inspecting compressor unit  ", isInternal: true, staff);
        service.AddNote(createdId, "Technician dispatched to Ground Floor.", isInternal: false, staff);

        foreach (var nextStatus in new[] { "ASSIGNED", "IN_PROGRESS", "RESOLVED", "CLOSED" })
        {
            var transition = service.ChangeStatus(createdId, nextStatus, staff);
            Assert.True(transition.Success, transition.Message);
        }

        // Verify FR-11 ownership and FR-05 priority snapshot did not regress across mutations
        var persisted = Assert.IsType<Ticket>(service.FindVisible(createdId, requester));
        Assert.Equal("Bernard Small", persisted.Requester);
        Assert.Equal("USER-A", persisted.RequesterId);
        Assert.Equal(PriorityMatrix.LevelHigh, persisted.ImpactLevel);
        Assert.Equal(PriorityMatrix.LevelCritical, persisted.UrgencyLevel);
        Assert.Equal(PriorityMatrix.PriorityCritical, persisted.ComputedPriorityId);
        Assert.Equal(PriorityMatrix.PriorityCritical, persisted.PriorityId);
        Assert.Equal("Critical", persisted.Priority);
        Assert.Equal("Closed", persisted.Status);
        Assert.Equal(4, persisted.StatusHistory.Count);

        // Verify FR-16 note filtering on the completed ticket
        var requesterNotes = service.GetNotes(createdId, requester);
        var singlePublicNote = Assert.Single(requesterNotes);
        Assert.False(singlePublicNote.IsInternal);
        Assert.Equal("Technician dispatched to Ground Floor.", singlePublicNote.Text);

        var staffNotes = service.GetNotes(createdId, staff);
        Assert.Equal(2, staffNotes.Count);

        // Verify FR-08 audit trail recorded CREATE, 2x ADD_NOTE, and 4x STATUS_CHANGE without leaking free-text PII
        var auditEntries = audit.GetForEntity("Ticket", createdId);
        Assert.Equal(7, auditEntries.Count);
        Assert.Equal(
            new[] { "CREATE", "ADD_NOTE", "ADD_NOTE", "CHANGE_STATUS", "CHANGE_STATUS", "CHANGE_STATUS", "CHANGE_STATUS" },
            auditEntries.Select(e => e.Action));

        foreach (var entry in auditEntries)
        {
            var serialized = $"{entry.PreviousValues}{entry.NewValues}";
            Assert.DoesNotContain("+27820001111", serialized);
            Assert.DoesNotContain("bernard@example.com", serialized);
            Assert.DoesNotContain("Inspecting compressor unit", serialized);
            using var doc = JsonDocument.Parse(entry.NewValues!);
            Assert.False(doc.RootElement.TryGetProperty("Title", out _));
            Assert.False(doc.RootElement.TryGetProperty("Description", out _));
        }
    }

    [Theory]
    [InlineData("Requester")]
    [InlineData("Anonymous")]
    public void REG_UnauthorizedNoteAndStatusMutations_AreRejectedWithoutAlteringTicketOrAuditTrail(string actorKind)
    {
        var audit = new AuditService();
        var service = new TicketService(new PriorityMatrix(), audit);
        var owner = User("Bernard Small", "USER-A", "Requester");
        var ticketId = service.CreateForRequester(new Ticket
        {
            Title = "Projector bulb flickering",
            Description = "Room 204 projector",
            ImpactLevel = PriorityMatrix.LevelLow,
            UrgencyLevel = PriorityMatrix.LevelMedium
        }, owner);

        var actor = actorKind == "Anonymous"
            ? new ClaimsPrincipal()
            : User("Bernard Small", "USER-A", "Requester");

        Assert.Throws<UnauthorizedAccessException>(() =>
            service.AddNote(ticketId, "Attempting unauthorized note", isInternal: false, actor));

        var statusResult = service.ChangeStatus(ticketId, "ASSIGNED", actor);
        Assert.False(statusResult.Success);

        var ticket = Assert.IsType<Ticket>(service.Find(ticketId));
        Assert.Equal("Open", ticket.Status);
        Assert.Empty(ticket.StatusHistory);
        Assert.Empty(service.GetNotes(ticketId, User("Daniel Jacobs", "STAFF-1", "Staff")));

        // Only the initial CREATE audit record exists; rejected mutations append nothing
        var auditEntries = audit.GetForEntity("Ticket", ticketId);
        var createEntry = Assert.Single(auditEntries);
        Assert.Equal("CREATE", createEntry.Action);
    }

    [Fact]
    public void REG_StaffQueueFiltering_IncludesActiveRequesterTickets_AndExcludesOnceResolvedOrClosed()
    {
        var service = new TicketService();
        var requester = User("Ayesha Khan", "USER-B", "Requester");
        var staff = User("Daniel Jacobs", "STAFF-1", "Staff");

        var createdId = service.CreateForRequester(new Ticket
        {
            Title = "VPN token expired",
            Description = "Cannot authenticate to corporate VPN",
            Category = "IT Support",
            Location = "Ground Floor",
            ImpactLevel = PriorityMatrix.LevelMedium,
            UrgencyLevel = PriorityMatrix.LevelHigh
        }, requester);

        // Open, Assigned, and In Progress remain visible in the IT Support staff queue (FR-14 / FR-15)
        Assert.Contains(service.GetStaffQueue("IT Support", "Daniel Jacobs"), t => t.Id == createdId);

        Assert.True(service.ChangeStatus(createdId, "ASSIGNED", staff).Success);
        Assert.Contains(service.GetStaffQueue("IT Support", "Daniel Jacobs"), t => t.Id == createdId);

        Assert.True(service.ChangeStatus(createdId, "IN_PROGRESS", staff).Success);
        Assert.Contains(service.GetStaffQueue("IT Support", "Daniel Jacobs"), t => t.Id == createdId);

        // Once Resolved and Closed, the ticket is excluded from the active staff queue but remains visible to its Requester
        Assert.True(service.ChangeStatus(createdId, "RESOLVED", staff).Success);
        Assert.DoesNotContain(service.GetStaffQueue("IT Support", "Daniel Jacobs"), t => t.Id == createdId);
        Assert.NotNull(service.FindVisible(createdId, requester));

        Assert.True(service.ChangeStatus(createdId, "CLOSED", staff).Success);
        Assert.DoesNotContain(service.GetStaffQueue("IT Support", "Daniel Jacobs"), t => t.Id == createdId);
        Assert.NotNull(service.FindVisible(createdId, requester));
    }

    private static TicketService CreateSeededService()
    {
        var tickets = new TicketService();
        tickets.Tickets.Clear();

        var staff = User("Daniel Jacobs", "STAFF-1", "Staff");

        tickets.Tickets.AddRange(new[]
        {
            new Ticket
            {
                Id = "REG-A",
                Requester = "Bernard Small",
                RequesterId = "USER-A",
                Title = "Requester A ticket title",
                Description = "Requester A ticket description",
                Category = "IT Support",
                Location = "Ground Floor",
                Status = "Open",
                Assignee = "Daniel Jacobs"
            },
            new Ticket
            {
                Id = "REG-B",
                Requester = "Ayesha Khan",
                RequesterId = "USER-B",
                Title = "Requester B ticket title",
                Description = "Requester B ticket description",
                Category = "IT Support",
                Location = "Ground Floor",
                Status = "Open",
                Assignee = "Daniel Jacobs"
            }
        });

        foreach (var id in new[] { "REG-A", "REG-B" })
        {
            tickets.AddNote(id, $"Internal diagnostic for {id}", isInternal: true, staff);
            tickets.AddNote(id, $"Public update for {id}", isInternal: false, staff);
            tickets.ChangeStatus(id, "ASSIGNED", staff);
        }

        return tickets;
    }

    private static ClaimsPrincipal User(string name, string id, string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Email, $"{id.ToLowerInvariant()}@civicconnect.demo"),
            new Claim(ClaimTypes.Role, role),
            new Claim("Team", "IT Support")
        }, "Test"));

    private static async Task<string> RenderTicketDetailAsync(
        TicketService tickets, ClaimsPrincipal user, string ticketId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton<AuthenticationStateProvider>(new SignedInState(user));
        services.AddSingleton(tickets);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(_ => NpgsqlDataSource.Create("Host=localhost;Database=unused"));
        services.AddSingleton<AppAuthenticationStateProvider>();
        services.AddSingleton<WorkspaceNavigation>();

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment content = builder =>
            {
                builder.OpenComponent<TicketDetail>(0);
                builder.AddAttribute(1, "Id", ticketId);
                builder.CloseComponent();
            };
            var root = await renderer.RenderComponentAsync<CascadingAuthenticationState>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ChildContent"] = content }));
            return root.ToHtmlString();
        });
    }

    private sealed class SignedInState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/tickets/REG-A");
    }
}
