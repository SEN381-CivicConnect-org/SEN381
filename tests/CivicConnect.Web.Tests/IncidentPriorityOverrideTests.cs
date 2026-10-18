using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Tests;

/// <summary>FR-06 / #110: controller tests against a unique, disposable in-memory store.</summary>
public sealed class IncidentPriorityOverrideTests : IDisposable
{
    private readonly CivicConnectDbContext _context;
    private readonly IncidentsController _controller;
    private readonly Guid _supervisorId = Guid.NewGuid();
    private readonly Incident _incident;

    public IncidentPriorityOverrideTests()
    {
        _context = new CivicConnectDbContext(new DbContextOptionsBuilder<CivicConnectDbContext>()
            .UseInMemoryDatabase($"fr06-{Guid.NewGuid():N}").Options);
        _context.Database.EnsureCreated();
        _incident = new Incident
        {
            Id = 110,
            ReferenceNumber = "INC-000110",
            Title = "Water leak",
            Description = "Leak at the community centre",
            CategoryId = 1,
            LocationId = 1,
            Priority = 3,
            ComputedPriority = 3,
            Version = 7,
            OpenedAt = DateTimeOffset.UtcNow.AddHours(-1),
            DueAt = DateTimeOffset.UtcNow.AddHours(23)
        };
        _context.Incidents.Add(_incident);
        _context.SaveChanges();
        _controller = new IncidentsController(_context);
        SetIdentity(ClaimTypes.NameIdentifier, _supervisorId.ToString());
    }

    [Theory]
    [InlineData(1, ClaimTypes.NameIdentifier)]
    [InlineData(2, ClaimTypes.NameIdentifier)]
    [InlineData(3, ClaimTypes.NameIdentifier)]
    [InlineData(4, ClaimTypes.NameIdentifier)]
    [InlineData(1, "sub")]
    [InlineData(2, "sub")]
    [InlineData(3, "sub")]
    [InlineData(4, "sub")]
    public async Task OverridePriority_ValidRequest_PersistsPriorityAndTrimmedUtcMetadata(
        short priority, string claimType)
    {
        SetIdentity(claimType, _supervisorId.ToString());
        var before = DateTimeOffset.UtcNow;

        var result = await _controller.OverridePriority(_incident.Id, new OverridePriorityRequestDto
        {
            NewPriority = priority,
            Reason = " \t Escalated due to public safety concerns. \r\n",
            Version = _incident.Version
        });

        var after = DateTimeOffset.UtcNow;
        Assert.IsType<OkObjectResult>(result);
        _context.ChangeTracker.Clear();
        var stored = await _context.Incidents.AsNoTracking().SingleAsync();
        Assert.Equal(priority, stored.Priority);
        Assert.Equal((short)3, stored.ComputedPriority);
        Assert.True(stored.PriorityOverridden);
        Assert.Equal("Escalated due to public safety concerns.", stored.OverrideReason);
        Assert.Equal(_supervisorId, stored.OverrideBy);
        Assert.True(stored.OverrideAt.HasValue);
        Assert.Equal(TimeSpan.Zero, stored.OverrideAt.Value.Offset);
        Assert.InRange(stored.OverrideAt.Value, before, after);
    }

    [Fact]
    public async Task OverridePriority_ReasonExactly300Characters_Succeeds()
    {
        var request = ValidRequest();
        request.Reason = new string('x', 300);

        Assert.IsType<OkObjectResult>(await _controller.OverridePriority(_incident.Id, request));
        Assert.Equal(request.Reason, (await _context.Incidents.AsNoTracking().SingleAsync()).OverrideReason);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        // Repeat every rejection with an existing override to protect previously recorded metadata too.
        foreach (var previouslyOverridden in new[] { false, true })
        {
            foreach (var scenario in new[]
            {
                "null-body", "null-reason", "empty-reason", "whitespace-reason", "long-reason",
                "priority-below-range", "priority-above-range", "missing-identity",
                "malformed-nameidentifier", "malformed-sub", "blank-identity",
                "missing-incident", "stale-version", "missing-priority-target"
            })
            {
                yield return new object[] { scenario, previouslyOverridden };
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task OverridePriority_RejectedRequest_DoesNotMutateTrackedOrStoredIncident(
        string scenario, bool previouslyOverridden)
    {
        if (previouslyOverridden)
        {
            _incident.Priority = 4;
            _incident.PriorityOverridden = true;
            _incident.OverrideReason = "Previous supervisor decision";
            _incident.OverrideBy = Guid.NewGuid();
            _incident.OverrideAt = DateTimeOffset.UtcNow.AddMinutes(-30);
            await _context.SaveChangesAsync();
        }

        OverridePriorityRequestDto? request = ValidRequest();
        var id = _incident.Id;
        var expectedStatus = StatusCodes.Status400BadRequest;
        switch (scenario)
        {
            case "null-body": request = null; break;
            case "null-reason": request.Reason = null!; break;
            case "empty-reason": request.Reason = ""; break;
            case "whitespace-reason": request.Reason = " \t\r\n "; break;
            case "long-reason": request.Reason = new string('x', 301); break;
            case "priority-below-range": request.NewPriority = 0; break;
            case "priority-above-range": request.NewPriority = 5; break;
            case "missing-identity":
                SetIdentity(null, null);
                expectedStatus = StatusCodes.Status401Unauthorized;
                break;
            case "malformed-nameidentifier":
                SetIdentity(ClaimTypes.NameIdentifier, "not-a-uuid");
                expectedStatus = StatusCodes.Status401Unauthorized;
                break;
            case "malformed-sub":
                SetIdentity("sub", "not-a-uuid");
                expectedStatus = StatusCodes.Status401Unauthorized;
                break;
            case "blank-identity":
                SetIdentity(ClaimTypes.NameIdentifier, " ");
                expectedStatus = StatusCodes.Status401Unauthorized;
                break;
            case "missing-incident":
                id = long.MaxValue;
                expectedStatus = StatusCodes.Status404NotFound;
                break;
            case "stale-version":
                request.Version = _incident.Version - 1;
                expectedStatus = StatusCodes.Status409Conflict;
                break;
            case "missing-priority-target":
                _context.PriorityTargets.Remove(await _context.PriorityTargets.SingleAsync(p => p.Id == request.NewPriority));
                await _context.SaveChangesAsync();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var original = ScalarValues(_incident);
        var result = await _controller.OverridePriority(id, request);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Equal(original, ScalarValues(_incident));
        Assert.Equal(EntityState.Unchanged, _context.Entry(_incident).State);
        var stored = await _context.Incidents.AsNoTracking().SingleAsync();
        Assert.Equal(original, ScalarValues(stored));
    }

    [Fact]
    public async Task OverridePriority_WithoutOptionalVersion_Succeeds()
    {
        var request = ValidRequest();
        request.Version = null;

        Assert.IsType<OkObjectResult>(await _controller.OverridePriority(_incident.Id, request));
        var stored = await _context.Incidents.AsNoTracking().SingleAsync();
        Assert.Equal(request.NewPriority, stored.Priority);
        Assert.Equal((short)3, stored.ComputedPriority);
        Assert.True(stored.PriorityOverridden);
    }

    private OverridePriorityRequestDto ValidRequest() => new()
    {
        NewPriority = 1,
        Reason = "Public safety escalation",
        Version = _incident.Version
    };

    private void SetIdentity(string? claimType, string? value)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, "Supervisor") };
        if (claimType is not null && value is not null)
            claims.Add(new Claim(claimType, value));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "FR06-test"))
            }
        };
    }

    // Compare every mapped scalar, including priority, computed priority, version and override metadata.
    private object?[] ScalarValues(Incident incident) => _context.Entry(incident).CurrentValues.Properties
        .Select(property => _context.Entry(incident).CurrentValues[property]).ToArray();

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
