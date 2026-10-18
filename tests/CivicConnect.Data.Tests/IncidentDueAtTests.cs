using CivicConnect.Data;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CivicConnect.Data.Tests;

[Collection("Database")]
public sealed class IncidentDueAtTests(CivicConnectDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-10, true)]
    [InlineData(0, false)]
    [InlineData(10, false)]
    public async Task ManagementQuery_UsesStrictUtcBoundaryInPostgres(long ticksFromNow, bool included)
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var target = await context.PriorityTargets.OrderBy(p => p.Id).FirstAsync();
        // PostgreSQL stores microseconds (10 ticks), unlike the in-memory boundary tests.
        var incident = await CreateIncident(context, Now.AddTicks(ticksFromNow).AddMinutes(-target.ResolveWithinMinutes));
        var results = await GetOverdue(context);
        Assert.Equal(included, results.Any(i => i.Id == incident.Id));
        if (included)
        {
            var dto = Assert.Single(results, i => i.Id == incident.Id);
            Assert.Equal(Now.AddTicks(ticksFromNow), dto.DueAt);
            Assert.True(dto.IsOverdue);
            Assert.Equal(Now - dto.DueAt, dto.Overrun);
        }
    }

    [Fact]
    public async Task ManagementQuery_FiltersFutureIncidentsAndOrdersDueDatesInPostgres()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var target = await context.PriorityTargets.OrderBy(p => p.Id).FirstAsync();
        var openingForDueNow = Now.AddMinutes(-target.ResolveWithinMinutes);
        var later = await CreateIncident(context, openingForDueNow.AddMinutes(-5));
        var future = await CreateIncident(context, openingForDueNow.AddHours(1));
        var earlier = await CreateIncident(context, openingForDueNow.AddHours(-2));
        var results = await GetOverdue(context);
        Assert.DoesNotContain(results, i => i.Id == future.Id);
        Assert.Equal(new[] { earlier.Id, later.Id }, results.Where(i => i.Id == earlier.Id || i.Id == later.Id).Select(i => i.Id));
    }

    [Fact]
    public async Task DueAt_IsGeneratedOnInsertAndOverrideRemainsAnchoredToOriginalOpening()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var incident = await CreateIncident(context);
        var targets = await context.PriorityTargets.OrderBy(p => p.Id).ToListAsync();
        var originalTarget = targets.Single(p => p.Id == incident.Priority);
        Assert.Equal(incident.OpenedAt.AddMinutes(originalTarget.ResolveWithinMinutes), incident.DueAt);
        Assert.Equal(TimeSpan.Zero, incident.DueAt.Offset);

        var supervisor = new AppUser
        {
            Email = $"fr12-{Guid.NewGuid():N}@example.com", FullName = "FR-12 Supervisor", PasswordHash = "test-hash"
        };
        context.AppUsers.Add(supervisor);
        await context.SaveChangesAsync();
        var overrideTarget = targets.First(p => p.Id != originalTarget.Id && p.ResolveWithinMinutes != originalTarget.ResolveWithinMinutes);
        var openedAt = incident.OpenedAt;
        incident.Priority = overrideTarget.Id;
        incident.PriorityOverridden = true;
        incident.OverrideReason = "FR-12 test escalation";
        incident.OverrideBy = supervisor.Id;
        incident.OverrideAt = openedAt.AddHours(12);
        await context.SaveChangesAsync();
        await context.Entry(incident).ReloadAsync();

        Assert.Equal(openedAt, incident.OpenedAt);
        Assert.Equal(openedAt.AddMinutes(overrideTarget.ResolveWithinMinutes), incident.DueAt);
        Assert.NotEqual(incident.OverrideAt.Value.AddMinutes(overrideTarget.ResolveWithinMinutes), incident.DueAt);
        Assert.Equal(originalTarget.Id, incident.ComputedPriority);
    }

    [Fact]
    public async Task MissingDueDate_IsRejectedByPostgres()
    {
        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var incident = await CreateIncident(context);

        // Updating due_at alone does not invoke the priority trigger; NOT NULL still protects it.
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE incident SET due_at = NULL WHERE id = {incident.Id}"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, error.SqlState);
        Assert.Equal("due_at", error.ColumnName);
    }

    private static async Task<Incident> CreateIncident(CivicConnectDbContext context, DateTimeOffset? openedAt = null)
    {
        var team = new ServiceTeam { Name = $"FR12-{Guid.NewGuid():N}" };
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();
        var category = new Category
        {
            Name = $"FR12-{Guid.NewGuid():N}", Description = "FR-12", UrgencyLevel = 2,
            DefaultServiceTeamId = team.Id, IsActive = true
        };
        var location = new Location
        {
            Name = $"FR12-{Guid.NewGuid():N}", ImpactLevel = 2, IsActive = true,
            LocationKindId = await context.LocationKinds.Select(l => l.Id).FirstAsync()
        };
        context.Categories.Add(category);
        context.Locations.Add(location);
        await context.SaveChangesAsync();
        var priority = await context.PriorityTargets.OrderBy(p => p.Id).Select(p => p.Id).FirstAsync();
        var incident = new Incident
        {
            CategoryId = category.Id, LocationId = location.Id, Title = "FR-12 due date", Description = "FR-12",
            ComputedPriority = priority, Priority = priority,
            OpenedAt = openedAt ?? new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();
        return incident;
    }

    private static async Task<List<OverdueIncidentDto>> GetOverdue(CivicConnectDbContext context)
    {
        var controller = new IncidentsController(context, timeProvider: new FixedTimeProvider());
        return Assert.IsType<List<OverdueIncidentDto>>(Assert.IsType<OkObjectResult>(await controller.GetOverdueIncidents()).Value);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
