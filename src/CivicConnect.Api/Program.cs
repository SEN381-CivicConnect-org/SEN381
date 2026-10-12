using CivicConnect.Api.Services;
using CivicConnect.Data;
using CivicConnect.Data.Configuration;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 1. Service Registration & Dependency Injection

// Add Controllers support
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

// Authorization & Authentication
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// Swagger / OpenAPI documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "CivicConnect API",
        Version = "v1",
        Description = "CivicConnect Incident & Ticket Management Platform API - SEN381 Standards"
    });
});

// Configure CORS for web client access
builder.Services.AddCors(options =>
{
    options.AddPolicy("CivicConnectCorsPolicy", policy =>
    {
        policy.WithOrigins(
                "http://localhost:8080",
                "http://127.0.0.1:8080",
                "http://localhost:5000",
                "http://127.0.0.1:5000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// JSON serializer options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// Configure & Validate System Secrets from Environment Variables (NFR-01, IT-CFG-01: No hardcoded credentials)
SystemSecretsConfiguration.LoadDotEnvIfPresent();
var systemSecrets = SystemSecretsConfiguration.LoadAndValidate(builder.Configuration);
builder.Services.AddSingleton(systemSecrets);
var connectionString = systemSecrets.ConnectionString;

builder.Services.AddDbContext<CivicConnectDbContext>(options =>
{
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString);
});

builder.Services.AddSingleton<IPriorityMatrix, PriorityMatrix>();
builder.Services.AddScoped<IResolutionService, ResolutionService>();
builder.Services.AddScoped<ResolutionService>();

var app = builder.Build();

// 2. HTTP Request Pipeline

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CivicConnect API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("CivicConnectCorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

// 3. Basic System Endpoints
app.MapControllers();
app.MapGet("/", () => Results.Ok(new
{
    platform = "CivicConnect API",
    status = "Online",
    version = "1.0.0-net10",
    environment = app.Environment.EnvironmentName,
    swagger = "/swagger",
    health = "/health"
}))
.WithName("RootInfo")
.WithTags("System");

// Health Probe for System Readiness
app.MapGet("/health", async (CivicConnectDbContext dbContext) =>
{
    var checks = new Dictionary<string, string>();
    var isHealthy = true;

    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync();
        checks["database"] = canConnect ? "Healthy" : "Degraded (Cannot connect)";
        if (!canConnect) isHealthy = false;
    }
    catch (Exception ex)
    {
        checks["database"] = $"Unhealthy: {ex.Message}";
        isHealthy = false;
    }

    return Results.Ok(new
    {
        status = isHealthy ? "Healthy" : "Degraded",
        timestamp = DateTimeOffset.UtcNow,
        components = checks
    });
})
.WithName("HealthCheck")
.WithTags("System");

// 4. Reference Data Endpoints

app.MapGet("/api/categories", async (CivicConnectDbContext dbContext) =>
{
    var categories = await dbContext.Categories
        .AsNoTracking()
        .Where(c => c.IsActive)
        .Include(c => c.DefaultServiceTeam)
        .OrderBy(c => c.Name)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.Description,
            c.UrgencyLevel,
            UrgencyLevelName = PriorityMatrix.GetLevelName(c.UrgencyLevel),
            c.DefaultServiceTeamId,
            DefaultServiceTeamName = c.DefaultServiceTeam.Name
        })
        .ToListAsync();

    return Results.Ok(categories);
})
.WithName("GetCategories")
.WithTags("ReferenceData");

app.MapGet("/api/locations", async (CivicConnectDbContext dbContext) =>
{
    var locations = await dbContext.Locations
        .AsNoTracking()
        .Where(l => l.IsActive)
        .Include(l => l.LocationKind)
        .OrderBy(l => l.Name)
        .Select(l => new
        {
            l.Id,
            l.Name,
            l.ImpactLevel,
            ImpactLevelName = PriorityMatrix.GetLevelName(l.ImpactLevel),
            l.LocationKindId,
            LocationKindName = l.LocationKind.Name,
            l.ParentLocationId
        })
        .ToListAsync();

    return Results.Ok(locations);
})
.WithName("GetLocations")
.WithTags("ReferenceData");

app.MapGet("/api/priority-targets", async (CivicConnectDbContext dbContext) =>
{
    var targets = await dbContext.PriorityTargets
        .AsNoTracking()
        .OrderBy(pt => pt.Id)
        .Select(pt => new
        {
            pt.Id,
            pt.Name,
            pt.ResolveWithinMinutes
        })
        .ToListAsync();

    return Results.Ok(targets);
})
.WithName("GetPriorityTargets")
.WithTags("ReferenceData");

// Get active service teams
app.MapGet("/api/service-teams", async (CivicConnectDbContext dbContext) =>
{
    var teams = await dbContext.ServiceTeams
        .AsNoTracking()
        .OrderBy(st => st.Name)
        .Select(st => new
        {
            st.Id,
            st.Name
        })
        .ToListAsync();

    return Results.Ok(teams);
})
.WithName("GetServiceTeams")
.WithTags("ReferenceData");

// 5. Priority Calculation & Incident/Ticket Management (FR-05, DD-02)

static async Task<IResult> HandleCalculatePriorityAsync(
    CalculatePriorityRequest req,
    CivicConnectDbContext dbContext,
    IPriorityMatrix priorityMatrix)
{
    short urgencyLevel;
    string categoryName = "Manual";

    if (req.CategoryId.HasValue)
    {
        var category = await dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CategoryId.Value);

        if (category is null)
        {
            return Results.NotFound(new { message = $"Category ID {req.CategoryId.Value} not found in database." });
        }

        urgencyLevel = category.UrgencyLevel;
        categoryName = category.Name;
    }
    else if (req.UrgencyLevel.HasValue)
    {
        urgencyLevel = req.UrgencyLevel.Value;
    }
    else
    {
        return Results.BadRequest(new { message = "Either CategoryId or UrgencyLevel (1..4) must be specified." });
    }

    short impactLevel;
    string locationName = "Manual";

    if (req.LocationId.HasValue)
    {
        var location = await dbContext.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == req.LocationId.Value);

        if (location is null)
        {
            return Results.NotFound(new { message = $"Location ID {req.LocationId.Value} not found in database." });
        }

        impactLevel = location.ImpactLevel;
        locationName = location.Name;
    }
    else if (req.ImpactLevel.HasValue)
    {
        impactLevel = req.ImpactLevel.Value;
    }
    else
    {
        return Results.BadRequest(new { message = "Either LocationId or ImpactLevel (1..4) must be specified." });
    }

    short computedPriorityId;
    try
    {
        computedPriorityId = priorityMatrix.CalculatePriority(impactLevel, urgencyLevel);
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }

    var priorityTarget = await dbContext.PriorityTargets
        .AsNoTracking()
        .FirstOrDefaultAsync(pt => pt.Id == computedPriorityId);

    return Results.Ok(new CalculatePriorityResponse(
        req.CategoryId,
        categoryName,
        req.LocationId,
        locationName,
        impactLevel,
        priorityMatrix.FormatLevelName(impactLevel),
        urgencyLevel,
        priorityMatrix.FormatLevelName(urgencyLevel),
        computedPriorityId,
        priorityMatrix.FormatPriorityName(computedPriorityId),
        priorityTarget?.ResolveWithinMinutes
    ));
}

app.MapPost("/api/priority/calculate", HandleCalculatePriorityAsync)
    .WithName("CalculatePriority")
    .WithTags("Priority")
    .WithSummary("Calculates priority target ID (1=Critical..4=Low) from Location.ImpactLevel and Category.UrgencyLevel (FR-05, DD-02)");

app.MapPost("/api/requests/calculate-priority", HandleCalculatePriorityAsync)
    .WithName("CalculateIncidentPriority")
    .WithTags("Incidents")
    .WithSummary("Calculates priority target ID (1=Critical..4=Low) from Location.ImpactLevel and Category.UrgencyLevel (FR-05, DD-02)");

app.MapPost("/api/requests", async (
    CreateIncidentRequestDto dto,
    CivicConnectDbContext dbContext,
    IPriorityMatrix priorityMatrix) =>
{
    if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Description))
    {
        return Results.BadRequest(new { message = "Title and Description are required." });
    }

    var category = await dbContext.Categories
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.IsActive);

    if (category is null)
    {
        return Results.NotFound(new { message = $"Active Category ID {dto.CategoryId} not found." });
    }

    var location = await dbContext.Locations
        .AsNoTracking()
        .FirstOrDefaultAsync(l => l.Id == dto.LocationId && l.IsActive);

    if (location is null)
    {
        return Results.NotFound(new { message = $"Active Location ID {dto.LocationId} not found." });
    }

    short priorityId = priorityMatrix.CalculatePriority(location.ImpactLevel, category.UrgencyLevel);

    var incident = new Incident
    {
        CategoryId = category.Id,
        LocationId = location.Id,
        Title = dto.Title.Trim(),
        Description = dto.Description.Trim(),
        Status = IncidentStatus.New,
        ComputedPriority = priorityId,
        Priority = priorityId,
        PriorityOverridden = false,
        AssignedTeamId = category.DefaultServiceTeamId
    };

    dbContext.Incidents.Add(incident);
    await dbContext.SaveChangesAsync();

    Ticket? ticket = null;
    var reporterId = dto.ReporterId ?? dto.RequesterId;

    if (reporterId.HasValue)
    {
        var reporterExists = await dbContext.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == reporterId.Value && u.IsActive);

        if (reporterExists)
        {
            dbContext.IncidentStatusHistories.Add(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                FromStatus = null,
                ToStatus = IncidentStatus.New,
                ChangedBy = reporterId.Value,
                ChangedAt = incident.OpenedAt,
                AssignedTeamId = incident.AssignedTeamId
            });

            ticket = new Ticket
            {
                IncidentId = incident.Id,
                ReporterId = reporterId.Value,
                CategoryId = category.Id,
                LocationId = location.Id,
                Title = incident.Title,
                Description = incident.Description
            };
            dbContext.Tickets.Add(ticket);

            var alreadySubscribed = await dbContext.IncidentSubscriptions
                .AnyAsync(s => s.IncidentId == incident.Id && s.ReporterId == reporterId.Value);

            if (!alreadySubscribed)
            {
                dbContext.IncidentSubscriptions.Add(new IncidentSubscription
                {
                    IncidentId = incident.Id,
                    ReporterId = reporterId.Value
                });
            }

            await dbContext.SaveChangesAsync();
        }
    }

    return Results.Created($"/api/requests/{incident.Id}", new
    {
        incident.Id,
        incident.ReferenceNumber,
        TicketId = ticket?.Id,
        TicketReferenceNumber = ticket?.ReferenceNumber,
        incident.Title,
        incident.Description,
        incident.CategoryId,
        CategoryName = category.Name,
        incident.LocationId,
        LocationName = location.Name,
        ImpactLevel = location.ImpactLevel,
        ImpactName = priorityMatrix.FormatLevelName(location.ImpactLevel),
        UrgencyLevel = category.UrgencyLevel,
        UrgencyName = priorityMatrix.FormatLevelName(category.UrgencyLevel),
        incident.ComputedPriority,
        ComputedPriorityName = priorityMatrix.FormatPriorityName(incident.ComputedPriority),
        incident.Priority,
        PriorityName = priorityMatrix.FormatPriorityName(incident.Priority),
        incident.PriorityOverridden,
        incident.Status,
        incident.AssignedTeamId,
        incident.OpenedAt,
        incident.DueAt
    });
})
.WithName("CreateIncident")
.WithTags("Incidents")
.WithSummary("Creates an Incident (and optional Ticket/Subscription) with priority computed via PriorityMatrix (FR-05)");

app.MapGet("/api/requests/{id:long}", async (
    long id,
    CivicConnectDbContext dbContext,
    IPriorityMatrix priorityMatrix) =>
{
    var incident = await dbContext.Incidents
        .AsNoTracking()
        .Include(i => i.Category)
        .Include(i => i.Location)
        .Include(i => i.PriorityTarget)
        .Include(i => i.ComputedPriorityTarget)
        .Include(i => i.AssignedTeam)
        .Include(i => i.Assignee)
        .FirstOrDefaultAsync(i => i.Id == id);

    if (incident is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        incident.Id,
        incident.ReferenceNumber,
        incident.Title,
        incident.Description,
        incident.CategoryId,
        CategoryName = incident.Category.Name,
        incident.LocationId,
        LocationName = incident.Location.Name,
        ImpactLevel = incident.Location.ImpactLevel,
        ImpactName = priorityMatrix.FormatLevelName(incident.Location.ImpactLevel),
        UrgencyLevel = incident.Category.UrgencyLevel,
        UrgencyName = priorityMatrix.FormatLevelName(incident.Category.UrgencyLevel),
        incident.ComputedPriority,
        ComputedPriorityName = priorityMatrix.FormatPriorityName(incident.ComputedPriority),
        incident.Priority,
        PriorityName = priorityMatrix.FormatPriorityName(incident.Priority),
        incident.PriorityOverridden,
        incident.OverrideReason,
        incident.OverrideBy,
        incident.OverrideAt,
        incident.Status,
        incident.AssignedTeamId,
        AssignedTeamName = incident.AssignedTeam?.Name,
        incident.AssigneeId,
        AssigneeName = incident.Assignee?.FullName,
        incident.OpenedAt,
        incident.DueAt,
        incident.ResolvedAt,
        incident.ClosedAt
    });
})
.WithName("GetIncidentById")
.WithTags("Incidents");

app.MapGet("/api/requests", async (
    CivicConnectDbContext dbContext,
    IPriorityMatrix priorityMatrix) =>
{
    var incidents = await dbContext.Incidents
        .AsNoTracking()
        .Include(i => i.Category)
        .Include(i => i.Location)
        .OrderByDescending(i => i.OpenedAt)
        .ToListAsync();

    var response = incidents.Select(i => new
    {
        i.Id,
        i.ReferenceNumber,
        i.Title,
        i.CategoryId,
        CategoryName = i.Category.Name,
        i.LocationId,
        LocationName = i.Location.Name,
        ImpactLevel = i.Location.ImpactLevel,
        ImpactName = priorityMatrix.FormatLevelName(i.Location.ImpactLevel),
        UrgencyLevel = i.Category.UrgencyLevel,
        UrgencyName = priorityMatrix.FormatLevelName(i.Category.UrgencyLevel),
        i.ComputedPriority,
        ComputedPriorityName = priorityMatrix.FormatPriorityName(i.ComputedPriority),
        i.Priority,
        PriorityName = priorityMatrix.FormatPriorityName(i.Priority),
        i.PriorityOverridden,
        i.Status,
        i.OpenedAt,
        i.DueAt
    });

    return Results.Ok(response);
})
.WithName("GetIncidents")
.WithTags("Incidents");

app.Run();

public partial class Program;

public record CalculatePriorityRequest(
    int? CategoryId = null,
    int? LocationId = null,
    short? ImpactLevel = null,
    short? UrgencyLevel = null);

public record CalculatePriorityResponse(
    int? CategoryId,
    string CategoryName,
    int? LocationId,
    string LocationName,
    short ImpactLevel,
    string ImpactName,
    short UrgencyLevel,
    string UrgencyName,
    short PriorityId,
    string PriorityName,
    int? ResolveWithinMinutes);

public record CreateIncidentRequestDto(
    string Title,
    string Description,
    int CategoryId,
    int LocationId,
    Guid? ReporterId = null,
    Guid? RequesterId = null);