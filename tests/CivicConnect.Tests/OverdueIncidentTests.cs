using System.Reflection;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Tests;

public sealed class OverdueIncidentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-600000000, true)]
    [InlineData(600000000, false)]
    public async Task DueAt_UsesStrictUtcBoundary(long ticksFromNow, bool expectedOverdue)
    {
        await using var context = CreateContext();
        var incident = CreateIncident(1, Now.AddTicks(ticksFromNow));
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();

        var results = await GetOverdue(context);

        Assert.Equal(expectedOverdue ? 1 : 0, results.Count);
        if (expectedOverdue)
        {
            var result = Assert.Single(results);
            Assert.True(result.IsOverdue);
            Assert.Equal(incident.DueAt, result.DueAt);
            Assert.Equal(Now - incident.DueAt, result.Overrun);
            Assert.Equal((Now - incident.DueAt).TotalMinutes, result.OverrunMinutes);
        }
    }

    [Theory]
    [InlineData(IncidentStatus.New, true)]
    [InlineData(IncidentStatus.Assigned, true)]
    [InlineData(IncidentStatus.InProgress, true)]
    [InlineData(IncidentStatus.OnHold, true)]
    [InlineData(IncidentStatus.Resolved, false)]
    [InlineData(IncidentStatus.Closed, false)]
    [InlineData(IncidentStatus.Rejected, false)]
    [InlineData(IncidentStatus.Merged, false)]
    public async Task OverdueList_IncludesOnlyExistingActiveStatuses(IncidentStatus status, bool included)
    {
        await using var context = CreateContext();
        context.Incidents.Add(CreateIncident(1, Now.AddHours(-1), status));
        await context.SaveChangesAsync();
        Assert.Equal(included ? 1 : 0, (await GetOverdue(context)).Count);
    }

    [Fact]
    public async Task ManagementList_FiltersAndSortsByDueAtAndSurfacesIncidentDetails()
    {
        await using var context = CreateContext();
        var latest = CreateIncident(1, Now.AddMinutes(-5));
        latest.PriorityOverridden = true;
        latest.Priority = 1;
        latest.OverrideReason = "Escalated";
        latest.OverrideBy = Guid.NewGuid();
        latest.OverrideAt = Now.AddMinutes(-10);
        context.Incidents.AddRange(latest,
            CreateIncident(2, Now.AddHours(1)),
            CreateIncident(3, Now.AddHours(-2)),
            CreateIncident(4, Now.AddHours(-3), IncidentStatus.Closed),
            CreateIncident(5, Now.AddMinutes(-30)));
        await context.SaveChangesAsync();

        var results = await GetOverdue(context);

        Assert.Equal(new long[] { 3, 5, 1 }, results.Select(i => i.Id));
        Assert.All(results, i => Assert.True(i.IsOverdue));
        var dto = results.Last();
        Assert.Equal(latest.ReferenceNumber, dto.ReferenceNumber);
        Assert.Equal(latest.Title, dto.Title);
        Assert.Equal(latest.Description, dto.Description);
        Assert.Equal(latest.CategoryId, dto.CategoryId);
        Assert.Equal(latest.LocationId, dto.LocationId);
        Assert.Equal(latest.Status, dto.Status);
        Assert.Equal(latest.OpenedAt, dto.OpenedAt);
        Assert.Equal(latest.Priority, dto.Priority);
        Assert.Equal(latest.ComputedPriority, dto.ComputedPriority);
        Assert.True(dto.PriorityOverridden);
        Assert.Equal(latest.OverrideReason, dto.OverrideReason);
        Assert.Equal(latest.OverrideBy, dto.OverrideBy);
        Assert.Equal(latest.OverrideAt, dto.OverrideAt);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task DueAt_ComparesInstantsAcrossOffsets(int minutesFromNow, bool included)
    {
        await using var context = CreateContext();
        context.Incidents.Add(CreateIncident(1, Now.AddMinutes(minutesFromNow).ToOffset(TimeSpan.FromHours(2))));
        await context.SaveChangesAsync();
        Assert.Equal(included ? 1 : 0, (await GetOverdue(context)).Count);
    }

    [Fact]
    public void MissingDueDate_IsNotSupportedByTheExistingModel()
    {
        // Do not invent null/default-date semantics: PostgreSQL generates a NOT NULL due_at.
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(Incident))!.FindProperty(nameof(Incident.DueAt))!;
        Assert.False(property.IsNullable);
        Assert.Equal(typeof(DateTimeOffset), property.ClrType);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
    }

    [Fact]
    public void ManagementEndpoint_PreservesRouteAndRoleRestrictions()
    {
        var method = typeof(IncidentsController).GetMethod(nameof(IncidentsController.GetOverdueIncidents))!;
        Assert.Equal("api/incidents", typeof(IncidentsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("overdue", method.GetCustomAttribute<HttpGetAttribute>()!.Template);
        var roles = method.GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');
        Assert.Equal(new[] { "Supervisor", "Manager", "Operations Manager", "Administrator", "supervisor", "operations manager", "administrator", "Management" }, roles);
    }

    private static CivicConnectDbContext CreateContext() => new(
        new DbContextOptionsBuilder<CivicConnectDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Incident CreateIncident(long id, DateTimeOffset dueAt, IncidentStatus status = IncidentStatus.New) => new()
    {
        Id = id, ReferenceNumber = $"INC-{id:000000}", Title = "Overdue test", Description = "FR-12",
        CategoryId = 1, LocationId = 1, Priority = 2, ComputedPriority = 2,
        Status = status, OpenedAt = Now.AddDays(-2), DueAt = dueAt
    };

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
