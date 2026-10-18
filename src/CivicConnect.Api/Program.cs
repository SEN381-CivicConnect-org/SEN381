using CivicConnect.Api.Services;
using CivicConnect.Data;
using CivicConnect.Data.Configuration;
using CivicConnect.Data.Entities;
using CivicConnect.Data.Operations;
using CivicConnect.Data.Security;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net.Sockets;
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
builder.Services.AddHealthChecks();

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
                "http://localhost:8085",
                "http://127.0.0.1:8085",
                "http://localhost:8086",
                "http://127.0.0.1:8086",
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
var connectionString = SystemSecretsConfiguration.EnsureTlsConnectionString(systemSecrets.ConnectionString);
SystemSecretsConfiguration.EnsureServerTlsReady(connectionString);

builder.Services.AddDbContext<CivicConnectDbContext>(options =>
{
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString);
});

builder.Services.AddSingleton<IPasswordHashingService, Argon2idPasswordHasher>();
builder.Services.AddSingleton<Argon2idPasswordHasher>();
builder.Services.AddSingleton<IDatabaseBackupManager, DatabaseBackupManager>();
builder.Services.AddSingleton<DatabaseBackupManager>();
builder.Services.AddSingleton<IPriorityMatrix, PriorityMatrix>();
builder.Services.AddScoped<IResolutionService, ResolutionService>();
builder.Services.AddScoped<ResolutionService>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<SubmissionService>();
builder.Services.AddScoped<IIncidentReportingService, IncidentReportingService>();
builder.Services.AddScoped<IRetentionSweepService, RetentionSweepJob>();
builder.Services.AddScoped<RetentionSweepJob>();
builder.Services.AddScoped<IUserDeIdentificationService, UserDeIdentificationService>();
builder.Services.AddScoped<UserDeIdentificationService>();
builder.Services.AddHostedService<RetentionSweepHostedService>();

var app = builder.Build();

// Verify that application connects under least-privilege role (NFR-10, IT-SEC-02)
await SystemSecretsConfiguration.EnforceLeastPrivilegeRoleAsync(connectionString);

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
    health = "/health",
    healthReady = "/health/ready",
    healthLive = "/health/live"
}))
.WithName("RootInfo")
.WithTags("System");

// Liveness Probe (NFR-09: Fast process responsiveness)
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow,
    check = "live",
    message = "CivicConnect Kestrel runtime process is responsive"
}))
.WithName("HealthCheckLive")
.WithTags("System")
.WithSummary("Liveness probe verifying Kestrel runtime process responsiveness");

// Readiness Probe (NFR-09: PostgreSQL and RabbitMQ critical dependencies)
static async Task<IResult> HandleReadinessCheckAsync(
    CivicConnectDbContext dbContext,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var totalSw = Stopwatch.StartNew();
    var components = new Dictionary<string, HealthComponentStatus>();
    var allHealthy = true;

    // 1. PostgreSQL connectivity check (CanConnectAsync)
    var dbSw = Stopwatch.StartNew();
    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        dbSw.Stop();
        if (canConnect)
        {
            components["database"] = new HealthComponentStatus(
                "Healthy",
                "PostgreSQL database connection established",
                Math.Round(dbSw.Elapsed.TotalMilliseconds, 2));
        }
        else
        {
            components["database"] = new HealthComponentStatus(
                "Unhealthy",
                "PostgreSQL database returned false for CanConnectAsync",
                Math.Round(dbSw.Elapsed.TotalMilliseconds, 2));
            allHealthy = false;
        }
    }
    catch (Exception ex)
    {
        dbSw.Stop();
        components["database"] = new HealthComponentStatus(
            "Unhealthy",
            $"PostgreSQL database connection failed: {ex.Message}",
            Math.Round(dbSw.Elapsed.TotalMilliseconds, 2));
        allHealthy = false;
    }

    // 2. RabbitMQ message broker responsiveness check
    var rabbitEnabled = configuration.GetValue<bool?>("RabbitMQ:Enabled") ?? true;
    var rabbitHost = configuration["RabbitMQ:Host"]
        ?? Environment.GetEnvironmentVariable("RabbitMQ__Host")
        ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST");

    if (string.IsNullOrWhiteSpace(rabbitHost))
    {
        rabbitHost = "localhost";
    }

    if (!rabbitEnabled || rabbitHost.Equals("none", StringComparison.OrdinalIgnoreCase))
    {
        components["rabbitmq"] = new HealthComponentStatus(
            "Healthy",
            "RabbitMQ message broker check bypassed (disabled or set to none)",
            0.0);
    }
    else
    {
        var portStr = configuration["RabbitMQ:Port"]
            ?? Environment.GetEnvironmentVariable("RabbitMQ__Port")
            ?? Environment.GetEnvironmentVariable("RABBITMQ_PORT")
            ?? "5672";

        if (!int.TryParse(portStr, out var rabbitPort))
        {
            rabbitPort = 5672;
        }

        var rmqSw = Stopwatch.StartNew();
        try
        {
            using var tcpClient = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await tcpClient.ConnectAsync(rabbitHost, rabbitPort, cts.Token);
            rmqSw.Stop();
            components["rabbitmq"] = new HealthComponentStatus(
                "Healthy",
                $"RabbitMQ message broker reachable at {rabbitHost}:{rabbitPort}",
                Math.Round(rmqSw.Elapsed.TotalMilliseconds, 2));
        }
        catch (Exception ex)
        {
            rmqSw.Stop();
            components["rabbitmq"] = new HealthComponentStatus(
                "Unhealthy",
                $"RabbitMQ message broker unreachable at {rabbitHost}:{rabbitPort}: {ex.Message}",
                Math.Round(rmqSw.Elapsed.TotalMilliseconds, 2));
            allHealthy = false;
        }
    }

    totalSw.Stop();

    var response = new HealthCheckResponse(
        Status: allHealthy ? "Healthy" : "Unhealthy",
        Timestamp: DateTimeOffset.UtcNow,
        DurationMs: Math.Round(totalSw.Elapsed.TotalMilliseconds, 2),
        Components: components);

    return allHealthy
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
}

app.MapGet("/health/ready", HandleReadinessCheckAsync)
.WithName("HealthCheckReady")
.WithTags("System")
.WithSummary("Readiness probe verifying PostgreSQL and RabbitMQ critical dependencies (NFR-09)");

app.MapGet("/health", HandleReadinessCheckAsync)
.WithName("HealthCheck")
.WithTags("System")
.WithSummary("Readiness probe verifying PostgreSQL and RabbitMQ critical dependencies (NFR-09)");

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
    HttpContext httpContext,
    ISubmissionService submissionService,
    CancellationToken cancellationToken) =>
{
    var headerIdempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
    var effectiveIdempotencyKey = !string.IsNullOrWhiteSpace(dto.IdempotencyKey)
        ? dto.IdempotencyKey
        : headerIdempotencyKey;

    try
    {
        var result = await submissionService.SubmitAsync(
            new SubmitTicketRequestDto
            {
                Title = dto.Title,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                LocationId = dto.LocationId,
                ReporterId = dto.ReporterId ?? dto.RequesterId,
                IdempotencyKey = effectiveIdempotencyKey
            },
            cancellationToken: cancellationToken);

        return result.IsIdempotentReplay
            ? Results.Ok(result)
            : Results.Created($"/api/requests/{result.Id}", result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (IdempotencyConflictException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
})
.WithName("CreateIncident")
.WithTags("Incidents")
.WithSummary("Creates an Incident (or clusters onto an existing open Incident) and attaches a Ticket/Subscription with priority computed via PriorityMatrix (FR-03, FR-05, NFR-06)");

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

// NFR-10 (IT-SEC-02): Verify running application database connection is not a superuser and not the migration role
await SystemSecretsConfiguration.EnforceLeastPrivilegeRoleAsync(connectionString);

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
    Guid? RequesterId = null,
    string? IdempotencyKey = null);

public record HealthComponentStatus(
    string Status,
    string Description,
    double DurationMs);

public record HealthCheckResponse(
    string Status,
    DateTimeOffset Timestamp,
    double DurationMs,
    Dictionary<string, HealthComponentStatus> Components);