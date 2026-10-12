using System.Security.Claims;
using CivicConnect.Api.Controllers;
using CivicConnect.Api.Services;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Tests;

/// <summary>
/// Integration tests for FR-10 Asset Linkage on Resolution (IT-RES-01 & IT-RES-02) and AP-10 Finance Asset Replacement Report.
/// </summary>
[Collection("Database")]
public sealed class ResolutionTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public ResolutionTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IT_RES_01_ReplacementWithoutAssetRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (reporter, technician, incident, asset) = await SeedIncidentInProgressAsync(context, "IT-00421");

        var replacedCode = await context.ResolutionCodes
            .AsNoTracking()
            .SingleAsync(c => c.Code == "HARDWARE_REPLACED");
        Assert.True(replacedCode.ImpliesReplacement);

        await transaction.CreateSavepointAsync("before_invalid_resolution");

        context.Resolutions.Add(new Resolution
        {
            IncidentId = incident.Id,
            ResolutionCodeId = replacedCode.Id,
            Summary = "Replaced switch without specifying asset",
            AssetId = null,
            ResolvedBy = technician.Id
        });

        var dbEx = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains("requires an asset link", dbEx.InnerException?.Message ?? dbEx.Message, StringComparison.OrdinalIgnoreCase);

        await transaction.RollbackToSavepointAsync("before_invalid_resolution");
        context.ChangeTracker.Clear();

        // Now resolve with valid asset IT-00421
        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.InProgress,
            ToStatus = IncidentStatus.Resolved,
            ChangedBy = technician.Id,
            Note = "Replaced faulty hardware with spare",
            AssignedTeamId = incident.AssignedTeamId,
            AssigneeId = technician.Id
        });

        var validResolution = new Resolution
        {
            IncidentId = incident.Id,
            ResolutionCodeId = replacedCode.Id,
            Summary = "Replaced faulty hardware with spare",
            AssetId = asset.Id,
            ResolvedBy = technician.Id
        };
        context.Resolutions.Add(validResolution);
        await context.SaveChangesAsync();

        var reloadedIncident = await context.Incidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id);
        Assert.Equal(IncidentStatus.Resolved, reloadedIncident.Status);
        Assert.NotNull(reloadedIncident.ResolvedAt);

        var persistedResolution = await context.Resolutions
            .AsNoTracking()
            .Include(r => r.Asset)
            .SingleAsync(r => r.Id == validResolution.Id);
        Assert.True(persistedResolution.NeedsReplacement);
        Assert.Equal("IT-00421", persistedResolution.Asset!.AssetTag);
        Assert.Equal("Dimension Data SA", persistedResolution.Asset.Supplier);
    }

    [Fact]
    public async Task IT_RES_02_ReopenSupersedesResolution()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, technician, incident, asset) = await SeedIncidentInProgressAsync(context, "IT-00422");

        var fixedCode = await context.ResolutionCodes.AsNoTracking().SingleAsync(c => c.Code == "FIXED");
        var beyondRepairCode = await context.ResolutionCodes.AsNoTracking().SingleAsync(c => c.Code == "BEYOND_REPAIR");

        // 1. Initial resolution
        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.InProgress,
            ToStatus = IncidentStatus.Resolved,
            ChangedBy = technician.Id,
            Note = "Initial fix on site"
        });
        var firstResolution = new Resolution
        {
            IncidentId = incident.Id,
            ResolutionCodeId = fixedCode.Id,
            Summary = "Initial fix on site",
            ResolvedBy = technician.Id
        };
        context.Resolutions.Add(firstResolution);
        await context.SaveChangesAsync();

        await context.Entry(incident).ReloadAsync();
        Assert.Equal(IncidentStatus.Resolved, incident.Status);

        // 2. Reopen incident (RESOLVED -> IN_PROGRESS)
        incident.Status = IncidentStatus.InProgress;
        incident.ResolvedAt = null;
        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.Resolved,
            ToStatus = IncidentStatus.InProgress,
            ChangedBy = technician.Id,
            Note = "Reopened after fault recurred"
        });
        await context.SaveChangesAsync();

        // 3. Second resolution supersedes first resolution
        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.InProgress,
            ToStatus = IncidentStatus.Resolved,
            ChangedBy = technician.Id,
            Note = "Unit beyond repair; flagged for replacement"
        });
        var secondResolution = new Resolution
        {
            IncidentId = incident.Id,
            ResolutionCodeId = beyondRepairCode.Id,
            Summary = "Unit beyond repair; flagged for replacement",
            AssetId = asset.Id,
            ResolvedBy = technician.Id
        };
        context.Resolutions.Add(secondResolution);
        await context.SaveChangesAsync();

        var allResolutions = await context.Resolutions
            .AsNoTracking()
            .Where(r => r.IncidentId == incident.Id)
            .OrderBy(r => r.Id)
            .ToListAsync();

        Assert.Equal(2, allResolutions.Count);
        Assert.NotNull(allResolutions[0].SupersededAt);
        Assert.Null(allResolutions[1].SupersededAt);
        Assert.True(allResolutions[1].NeedsReplacement);
        Assert.Equal(asset.Id, allResolutions[1].AssetId);
    }

    [Fact]
    public async Task IncidentsController_EnforcesReplacementRule_StatusTransition_AndFinanceReport()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (reporter, technician, incident, _) = await SeedIncidentInProgressAsync(context, "IT-00423");
        var resolutionService = new ResolutionService(context);
        var controller = CreateAuthenticatedController(resolutionService, technician.Id, "technician");

        // 1. Replacement code without asset returns 400 Bad Request
        var badRequestResult = await controller.ResolveIncidentAsync(
            incident.Id,
            new ResolveIncidentRequestDto
            {
                ResolutionCode = "HARDWARE_REPLACED",
                Summary = "Attempted replacement without selecting asset"
            });

        Assert.IsType<BadRequestObjectResult>(badRequestResult);

        // 2. Replacement code with valid asset tag "IT-00423" returns 200 OK
        var okResult = await controller.ResolveIncidentAsync(
            incident.Id,
            new ResolveIncidentRequestDto
            {
                ResolutionCode = "HARDWARE_REPLACED",
                Summary = "Replaced switch with spare unit",
                AssetTag = "IT-00423"
            });

        var okObject = Assert.IsType<OkObjectResult>(okResult);
        var dto = Assert.IsType<ResolutionResultDto>(okObject.Value);
        Assert.Equal(IncidentStatus.Resolved, dto.IncidentStatus);
        Assert.Equal("IT-00423", dto.AssetTag);
        Assert.True(dto.NeedsReplacement);

        // 3. Finance Asset Replacement Report endpoint returns the linked asset, supplier, and purchase date
        var reportActionResult = await controller.GetAssetReplacementReportAsync(
            from: DateTimeOffset.UtcNow.AddMinutes(-5),
            to: DateTimeOffset.UtcNow.AddMinutes(5));

        var reportOk = Assert.IsType<OkObjectResult>(reportActionResult.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<FinanceAssetReplacementReportItemDto>>(reportOk.Value);
        var item = Assert.Single(items, i => i.ResolutionId == dto.ResolutionId);
        Assert.Equal("IT-00423", item.AssetTag);
        Assert.Equal("Dimension Data SA", item.Supplier);
        Assert.Equal(new DateOnly(2024, 3, 15), item.PurchaseDate);

        // 4. Verify subscriber notification fan-out (resolution_notify_trg) created a RESOLUTION notification for the reporter
        var resolutionNotification = await context.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(n =>
                n.IncidentId == incident.Id &&
                n.RecipientId == reporter.Id &&
                n.Kind == NotificationKind.Resolution);
        Assert.NotNull(resolutionNotification);
    }

    private static IncidentsController CreateAuthenticatedController(
        IResolutionService resolutionService,
        Guid userId,
        string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return new IncidentsController(resolutionService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal
                }
            }
        };
    }

    private static async Task<(AppUser Reporter, AppUser Technician, Incident Incident, Asset Asset)> SeedIncidentInProgressAsync(
        CivicConnectDbContext context,
        string assetTag)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var reporter = new AppUser
        {
            Email = $"rep.{suffix}@example.com",
            FullName = "Reporter",
            PasswordHash = "hash"
        };
        var technician = new AppUser
        {
            Email = $"tech.{suffix}@example.com",
            FullName = "Technician",
            PasswordHash = "hash"
        };
        var team = new ServiceTeam { Name = $"Team {suffix}" };

        context.AppUsers.AddRange(reporter, technician);
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();

        var category = new Category
        {
            Name = $"Category {suffix}",
            Description = "Hardware",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
            IsActive = true
        };
        var location = new Location
        {
            Name = $"Location {suffix}",
            LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
            ImpactLevel = 3,
            IsActive = true
        };

        context.Categories.Add(category);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var asset = await context.Assets.FirstOrDefaultAsync(a => a.AssetTag == assetTag)
            ?? new Asset
            {
                AssetTag = assetTag,
                Description = "Cisco Catalyst 24-Port Switch",
                Supplier = "Dimension Data SA",
                PurchaseDate = new DateOnly(2024, 3, 15),
                LocationId = location.Id,
                IsActive = true
            };

        if (asset.Id == 0)
        {
            context.Assets.Add(asset);
            await context.SaveChangesAsync();
        }

        var incident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            Title = "Switch failure",
            Description = "Hardware fault",
            Status = IncidentStatus.InProgress,
            ComputedPriority = 1,
            Priority = 1,
            PriorityOverridden = false,
            AssignedTeamId = team.Id,
            AssigneeId = technician.Id
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();

        context.IncidentSubscriptions.Add(new IncidentSubscription
        {
            IncidentId = incident.Id,
            ReporterId = reporter.Id
        });

        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = null,
            ToStatus = IncidentStatus.New,
            ChangedBy = reporter.Id
        });
        await context.SaveChangesAsync();

        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.New,
            ToStatus = IncidentStatus.Assigned,
            ChangedBy = technician.Id
        });
        await context.SaveChangesAsync();

        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.Assigned,
            ToStatus = IncidentStatus.InProgress,
            ChangedBy = technician.Id
        });
        await context.SaveChangesAsync();

        return (reporter, technician, incident, asset);
    }
}
