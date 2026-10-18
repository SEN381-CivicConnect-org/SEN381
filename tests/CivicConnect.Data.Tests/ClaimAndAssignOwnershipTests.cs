using System.Security.Claims;
using CivicConnect.Api.Controllers;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Tests;

/// Covers FR-04 (Assign or Claim Request Ownership), FR-07 (Status Transition History), and NFR-06 (Double-Claim Prevention).
[Collection("Database")]
public sealed class ClaimAndAssignOwnershipTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public ClaimAndAssignOwnershipTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Claim_UnassignedIncident_SetsAssigneeTransitionsToAssignedAndAppendsHistory()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var reporter = await CreateUserAsync(context, "Reporter User");
        var technician = await CreateUserAsync(context, "Technician A");
        var team = await CreateTeamAsync(context, "IT Support");

        context.TeamMembers.Add(new TeamMember
        {
            UserId = technician.Id,
            ServiceTeamId = team.Id,
            TeamRoleId = TeamRoleId("technician"),
        });

        var incident = await CreateIncidentAsync(context, team, reporter);
        Assert.Equal(IncidentStatus.New, incident.Status);
        Assert.Equal(1, incident.Version);

        var controller = CreateControllerWithUser(context, technician.Id, "technician");
        var actionResult = await controller.Claim(incident.Id);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode ?? StatusCodes.Status200OK);

        var reloaded = await context.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id);
        Assert.Equal(technician.Id, reloaded.AssigneeId);
        Assert.Equal(team.Id, reloaded.AssignedTeamId);
        Assert.Equal(IncidentStatus.Assigned, reloaded.Status);
        Assert.Equal(2, reloaded.Version);

        var history = await context.IncidentStatusHistories
            .AsNoTracking()
            .Where(h => h.IncidentId == incident.Id)
            .OrderBy(h => h.Id)
            .ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.Null(history[0].FromStatus);
        Assert.Equal(IncidentStatus.New, history[0].ToStatus);
        Assert.Equal(IncidentStatus.New, history[1].FromStatus);
        Assert.Equal(IncidentStatus.Assigned, history[1].ToStatus);
        Assert.Equal(technician.Id, history[1].AssigneeId);
        Assert.Equal(team.Id, history[1].AssignedTeamId);

        var notifications = await context.Notifications
            .AsNoTracking()
            .Where(n => n.IncidentId == incident.Id && n.RecipientId == reporter.Id && n.Kind == NotificationKind.Assignment)
            .ToListAsync();
        Assert.Single(notifications);
    }

    [Fact]
    public async Task Claim_AlreadyClaimedIncident_ReturnsConflictNamingCurrentAssignee()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var reporter = await CreateUserAsync(context, "Reporter User");
        var technicianA = await CreateUserAsync(context, "Technician A");
        var technicianB = await CreateUserAsync(context, "Technician B");
        var team = await CreateTeamAsync(context, "Facilities");

        var incident = await CreateIncidentAsync(context, team, reporter);

        var firstController = CreateControllerWithUser(context, technicianA.Id, "technician");
        var firstClaimResult = await firstController.Claim(incident.Id);
        Assert.IsType<OkObjectResult>(firstClaimResult);

        var secondController = CreateControllerWithUser(context, technicianB.Id, "technician");
        var secondClaimResult = await secondController.Claim(incident.Id);

        var conflictResult = Assert.IsType<ConflictObjectResult>(secondClaimResult);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode ?? StatusCodes.Status409Conflict);
        Assert.Contains("Technician A", conflictResult.Value?.ToString() ?? string.Empty);

        var reloaded = await context.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id);
        Assert.Equal(technicianA.Id, reloaded.AssigneeId);
    }

    [Fact]
    public async Task Assign_SupervisorAssignsTechnician_RecordsOwnerAndAuditHistory()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var reporter = await CreateUserAsync(context, "Reporter User");
        var supervisor = await CreateUserAsync(context, "Supervisor Sam");
        var technicianB = await CreateUserAsync(context, "Technician B");
        var team = await CreateTeamAsync(context, "Network Ops");

        var incident = await CreateIncidentAsync(context, team, reporter);

        var controller = CreateControllerWithUser(context, supervisor.Id, "supervisor");
        var result = await controller.Assign(incident.Id, new AssignIncidentRequestDto
        {
            AssigneeId = technicianB.Id,
            AssignedTeamId = team.Id,
            Note = "Assigned to Technician B for immediate investigation."
        });

        Assert.IsType<OkObjectResult>(result);

        var reloaded = await context.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id);
        Assert.Equal(technicianB.Id, reloaded.AssigneeId);
        Assert.Equal(team.Id, reloaded.AssignedTeamId);
        Assert.Equal(IncidentStatus.Assigned, reloaded.Status);
        Assert.Equal(2, reloaded.Version);

        var assignHistory = await context.IncidentStatusHistories
            .AsNoTracking()
            .Where(h => h.IncidentId == incident.Id && h.ToStatus == IncidentStatus.Assigned)
            .SingleAsync();

        Assert.Equal(supervisor.Id, assignHistory.ChangedBy);
        Assert.Equal(technicianB.Id, assignHistory.AssigneeId);
        Assert.Equal(team.Id, assignHistory.AssignedTeamId);
        Assert.Equal("Assigned to Technician B for immediate investigation.", assignHistory.Note);
    }

    private static IncidentsController CreateControllerWithUser(
        CivicConnectDbContext context,
        Guid userId,
        string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return new IncidentsController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static short TeamRoleId(string name) =>
        TeamRoleConfiguration.BaselineTeamRoles.Single(r => r.Name == name).Id;

    private static async Task<AppUser> CreateUserAsync(CivicConnectDbContext context, string fullName)
    {
        var user = new AppUser
        {
            Email = $"user.{Guid.NewGuid():N}@example.com",
            FullName = fullName,
            PasswordHash = "hashed-password",
        };

        context.AppUsers.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<ServiceTeam> CreateTeamAsync(CivicConnectDbContext context, string namePrefix)
    {
        var team = new ServiceTeam
        {
            Name = $"{namePrefix} {Guid.NewGuid():N}",
        };

        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();
        return team;
    }

    private static async Task<Incident> CreateIncidentAsync(
        CivicConnectDbContext context,
        ServiceTeam team,
        AppUser reporter)
    {
        var category = new Category
        {
            Name = $"Cat-{Guid.NewGuid():N}",
            Description = "Test category",
            UrgencyLevel = 2,
            DefaultServiceTeamId = team.Id,
            IsActive = true,
        };
        context.Categories.Add(category);

        var locationKindId = await context.LocationKinds
            .AsNoTracking()
            .Select(lk => lk.Id)
            .FirstAsync();

        var location = new Location
        {
            Name = $"Loc-{Guid.NewGuid():N}",
            LocationKindId = locationKindId,
            ImpactLevel = 2,
            IsActive = true,
        };
        context.Locations.Add(location);

        var priorityId = await context.PriorityTargets
            .AsNoTracking()
            .OrderBy(pt => pt.Id)
            .Select(pt => pt.Id)
            .FirstAsync();

        await context.SaveChangesAsync();

        var incident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            Title = "Wi-Fi connectivity issue",
            Description = "Cannot connect to campus network",
            Status = IncidentStatus.New,
            ComputedPriority = priorityId,
            Priority = priorityId,
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();

        context.IncidentSubscriptions.Add(new IncidentSubscription
        {
            IncidentId = incident.Id,
            ReporterId = reporter.Id,
        });
        await context.SaveChangesAsync();

        return incident;
    }
}
