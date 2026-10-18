using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebTicket = CivicConnect.Web.Models.Ticket;

namespace CivicConnect.Tests;

/// <summary>
/// Unit and service-level verification for:
/// - FR-15: Search, filter, and sort across staff ticket queue.
/// - FR-09: Category creation and history-preserving deactivation.
/// - FR-12 / FR-17 / FR-08: Management dashboard summary aggregation.
/// </summary>
public sealed class TicketQueueFilterAndManagementTests
{
    [Fact]
    public void FR15_TicketQueueFilter_SearchByKeyword_MatchesTitleAndDescription()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var tickets = new List<WebTicket>
        {
            new() { Id = "CC-001", Title = "HVAC leaking water in server room", Description = "Flooding hazard", Category = "Facilities", Status = "Open", Priority = "Critical" },
            new() { Id = "CC-002", Title = "Password reset for finance user", Description = "Domain login locked", Category = "IT Support", Status = "In Progress", Priority = "Medium" },
            new() { Id = "CC-003", Title = "Broken light fixture", Description = "Flickering in hallway", Category = "Facilities", Status = "Open", Priority = "Low" }
        };

        var match = TicketQueueFilter.Apply(tickets, search: "HVAC", category: "", status: "", sort: "date-newest", now).ToList();
        Assert.Single(match);
        Assert.Equal("CC-001", match[0].Id);

        var matchDesc = TicketQueueFilter.Apply(tickets, search: "login locked", category: "", status: "", sort: "date-newest", now).ToList();
        Assert.Single(matchDesc);
        Assert.Equal("CC-002", matchDesc[0].Id);
    }

    [Fact]
    public void FR15_TicketQueueFilter_FiltersByCategoryAndStatus()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var tickets = new List<WebTicket>
        {
            new() { Id = "CC-001", Title = "Air conditioning", Category = "Facilities", Status = "Open", Priority = "Medium" },
            new() { Id = "CC-002", Title = "Plumbing leak", Category = "Facilities", Status = "In Progress", Priority = "High" },
            new() { Id = "CC-003", Title = "VPN disconnected", Category = "IT Support", Status = "Open", Priority = "High" }
        };

        var facilitiesOnly = TicketQueueFilter.Apply(tickets, search: "", category: "Facilities", status: "", sort: "date-newest", now).ToList();
        Assert.Equal(2, facilitiesOnly.Count);
        Assert.All(facilitiesOnly, t => Assert.Equal("Facilities", t.Category));

        var facilitiesOpen = TicketQueueFilter.Apply(tickets, search: "", category: "Facilities", status: "Open", sort: "date-newest", now).ToList();
        Assert.Single(facilitiesOpen);
        Assert.Equal("CC-001", facilitiesOpen[0].Id);
    }

    [Fact]
    public void FR15_TicketQueueFilter_SortsByPriorityHighAndLow()
    {
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var tickets = new List<WebTicket>
        {
            new() { Id = "T1", Priority = "Low", Status = "Open" },
            new() { Id = "T2", Priority = "Critical", Status = "Open" },
            new() { Id = "T3", Priority = "Medium", Status = "Open" },
            new() { Id = "T4", Priority = "High", Status = "Open" }
        };

        var highToLow = TicketQueueFilter.Apply(tickets, "", "", "", "priority-high", now).Select(t => t.Id).ToList();
        Assert.Equal(new[] { "T2", "T4", "T3", "T1" }, highToLow);

        var lowToHigh = TicketQueueFilter.Apply(tickets, "", "", "", "priority-low", now).Select(t => t.Id).ToList();
        Assert.Equal(new[] { "T1", "T3", "T4", "T2" }, lowToHigh);
    }

    [Fact]
    public void FR15_TicketQueueFilter_SortsByDateNewestAndOldest()
    {
        var now = new DateTime(2026, 10, 18, 14, 0, 0, DateTimeKind.Utc);
        var tickets = new List<WebTicket>
        {
            new() { Id = "T-Old", Updated = "10 Sep 2026", Status = "Open" },
            new() { Id = "T-Mid", Updated = "Yesterday", Status = "Open" },
            new() { Id = "T-New", Updated = "Today, 13:00", Status = "Open" },
            new() { Id = "T-Now", Updated = "Just now", Status = "Open" }
        };

        var newestFirst = TicketQueueFilter.Apply(tickets, "", "", "", "date-newest", now).Select(t => t.Id).ToList();
        Assert.Equal("T-Now", newestFirst[0]);
        Assert.Equal("T-New", newestFirst[1]);
        Assert.Equal("T-Mid", newestFirst[2]);
        Assert.Equal("T-Old", newestFirst[3]);

        var oldestFirst = TicketQueueFilter.Apply(tickets, "", "", "", "date-oldest", now).Select(t => t.Id).ToList();
        Assert.Equal("T-Old", oldestFirst[0]);
        Assert.Equal("T-Mid", oldestFirst[1]);
        Assert.Equal("T-New", oldestFirst[2]);
        Assert.Equal("T-Now", oldestFirst[3]);
    }

    [Fact]
    public async Task FR09_WorkspaceDataService_CreateAndDeactivateCategoryPreservesHistory()
    {
        var dbName = "fr09_test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<CivicConnectDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new CivicConnectDbContext(options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        using (var initDb = new CivicConnectDbContext(options))
        {
            initDb.ServiceTeams.Add(new ServiceTeam { Id = 1, Name = "Facilities Operations" });
            await initDb.SaveChangesAsync();
        }

        var manager = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "manager-user-id"),
            new Claim(ClaimTypes.Name, "Manager User"),
            new Claim(ClaimTypes.Role, "Management")
        ], "TestAuth"));

        var audit = new AuditService();
        var data = new WorkspaceDataService(scopeFactory, new FixedAuthState(manager));

        // 1. Create a category
        var created = await data.CreateCategoryAsync(
            name: "Plumbing Support",
            description: "Water fixtures and drainage",
            urgencyLevel: 2,
            defaultServiceTeamId: 1,
            audit: audit);

        Assert.Equal("Plumbing Support", created.Name);
        Assert.True(created.IsActive);
        Assert.Equal(2, created.UrgencyLevel);

        // Verify audit log recorded creation
        var createAudit = Assert.Single(audit.GetForEntity("Category", created.Id.ToString()));
        Assert.Equal("CREATE", createAudit.Action);

        // 2. Deactivate category (FR-09 soft deactivation)
        var deactivated = await data.SetCategoryActiveAsync(created.Id, isActive: false, audit: audit);
        Assert.False(deactivated.IsActive);

        var deactivateAudit = audit.GetForEntity("Category", created.Id.ToString()).Last();
        Assert.Equal("DEACTIVATE", deactivateAudit.Action);

        // Verify category is still in database and historical queries can still read it
        using (var verifyDb = new CivicConnectDbContext(options))
        {
            var entity = await verifyDb.Categories.FindAsync(created.Id);
            Assert.NotNull(entity);
            Assert.False(entity.IsActive);
        }

        // 3. Reactivate category
        var reactivated = await data.SetCategoryActiveAsync(created.Id, isActive: true, audit: audit);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task FR12_FR17_WorkspaceDataService_DashboardSummaryAggregatesMetrics()
    {
        var dbName = "dash_test_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<CivicConnectDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new CivicConnectDbContext(options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var manager = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "manager-id"),
            new Claim(ClaimTypes.Name, "Manager"),
            new Claim(ClaimTypes.Role, "Management")
        ], "TestAuth"));

        var audit = new AuditService();
        var ticketService = new TicketService(audit: audit);
        var data = new WorkspaceDataService(scopeFactory, new FixedAuthState(manager));

        var summary = await data.GetManagementDashboardSummaryAsync(ticketService, audit);

        Assert.NotNull(summary);
        Assert.True(summary.TotalRequests >= 4);
        Assert.True(summary.OpenCount >= 1);
        Assert.NotNull(summary.CategoryBreakdown);
        Assert.NotEmpty(summary.CategoryBreakdown);
        Assert.NotNull(summary.TeamBreakdown);
        Assert.NotEmpty(summary.TeamBreakdown);
        Assert.NotNull(summary.OverdueIncidents);
        Assert.NotNull(summary.RecentAuditEntries);
    }

    private sealed class FixedAuthState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }
}
