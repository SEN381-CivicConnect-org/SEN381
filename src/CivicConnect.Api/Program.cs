using CivicConnect.Data;
using CivicConnect.Domain.Entities;
using CivicConnect.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);


// 1. Service Registration & Dependency Injection


// Swagger / OpenAPI documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "CivicConnect API",
        Version = "v1",
        Description = "Community Service Request Management Platform API - SEN381 Standards"
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

// Configure PostgreSQL Relational Persistence via CivicConnectDbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=changeme";

builder.Services.AddDbContext<CivicConnectDbContext>(options =>
{
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString);
});

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


// 3. Basic System Endpoints


// Platform Root Info
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


// Get all active request categories
app.MapGet("/api/categories", async (CivicConnectDbContext dbContext) =>
{
    var categories = await dbContext.Categories
        .AsNoTracking()
        .Where(c => c.IsActive)
        .Include(c => c.PriorityTarget)
        .Include(c => c.DefaultServiceTeam)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.Description,
            c.DefaultUrgency,
            DefaultUrgencyName = c.DefaultUrgency.ToString(),
            c.PriorityTargetId,
            PriorityTargetName = c.PriorityTarget.Name,
            c.DefaultServiceTeamId,
            DefaultServiceTeamName = c.DefaultServiceTeam.Name
        })
        .ToListAsync();

    return Results.Ok(categories);
})
.WithName("GetCategories")
.WithTags("ReferenceData");

// Get all baseline priority targets (SLA targets)
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


// 5. Service Request Management & Priority Calculation (FR-05 DDD)


// Dynamic calculation endpoint: fetches category DefaultUrgency from PostgreSQL and evaluates via ServiceRequest.SetImpactAndUrgency
app.MapPost("/api/requests/calculate-priority", async (CalculatePriorityRequest req, CivicConnectDbContext dbContext) =>
{
    UrgencyLevel urgency;
    string categoryName = "Manual";

    if (req.CategoryId.HasValue)
    {
        var category = await dbContext.Categories.FindAsync(req.CategoryId.Value);
        if (category is null)
        {
            return Results.NotFound(new { message = $"Category ID {req.CategoryId.Value} not found in database." });
        }
        urgency = category.DefaultUrgency;
        categoryName = category.Name;
    }
    else if (req.Urgency.HasValue)
    {
        urgency = req.Urgency.Value;
    }
    else
    {
        return Results.BadRequest(new { message = "Either CategoryId or Urgency must be specified." });
    }

    var domainRequest = new ServiceRequest();
    domainRequest.SetImpactAndUrgency(req.Impact, urgency);

    return Results.Ok(new CalculatePriorityResponse(
        req.CategoryId,
        categoryName,
        domainRequest.Impact,
        domainRequest.Impact.ToString(),
        domainRequest.Urgency,
        domainRequest.Urgency.ToString(),
        domainRequest.Priority,
        domainRequest.Priority.ToString()
    ));
})
.WithName("CalculatePriority")
.WithTags("ServiceRequests")
.WithSummary("Calculates priority by combining database category urgency and reported impact (FR-05 DDD)");

// Create Service Request adhering to DDD: fetches category from PostgreSQL and encapsulates calculation in domain model
app.MapPost("/api/requests", async (CreateServiceRequestDto dto, CivicConnectDbContext dbContext) =>
{
    var category = await dbContext.Categories.FindAsync(dto.CategoryId);
    if (category is null)
    {
        return Results.NotFound(new { message = $"Category ID {dto.CategoryId} not found." });
    }

    var request = new ServiceRequest
    {
        Title = dto.Title,
        Description = dto.Description,
        CategoryId = category.Id,
        RequesterId = dto.RequesterId
    };

    // Encapsulated domain calculation (Step 4 workflow)
    request.SetImpactAndUrgency(dto.Impact, category.DefaultUrgency);

    dbContext.ServiceRequests.Add(request);
    await dbContext.SaveChangesAsync();

    return Results.Created($"/api/requests/{request.Id}", new
    {
        request.Id,
        request.Title,
        request.Description,
        request.CategoryId,
        CategoryName = category.Name,
        request.Impact,
        ImpactName = request.Impact.ToString(),
        request.Urgency,
        UrgencyName = request.Urgency.ToString(),
        request.Priority,
        PriorityName = request.Priority.ToString(),
        request.Status,
        request.CreatedAt
    });
})
.WithName("CreateServiceRequest")
.WithTags("ServiceRequests")
.WithSummary("Creates a new ServiceRequest with priority calculated via rich domain model (FR-05 DDD)");

// Get Service Request by ID
app.MapGet("/api/requests/{id:guid}", async (Guid id, CivicConnectDbContext dbContext) =>
{
    var request = await dbContext.ServiceRequests
        .AsNoTracking()
        .Include(r => r.Category)
        .Include(r => r.Requester)
        .Include(r => r.AssignedTo)
        .FirstOrDefaultAsync(r => r.Id == id);

    if (request is null) return Results.NotFound();

    return Results.Ok(new
    {
        request.Id,
        request.Title,
        request.Description,
        request.CategoryId,
        CategoryName = request.Category?.Name,
        request.Impact,
        ImpactName = request.Impact.ToString(),
        request.Urgency,
        UrgencyName = request.Urgency.ToString(),
        request.Priority,
        PriorityName = request.Priority.ToString(),
        request.Status,
        request.IsSupervisorOverridden,
        request.SupervisorOverrideReason,
        request.CreatedAt,
        request.UpdatedAt
    });
})
.WithName("GetServiceRequestById")
.WithTags("ServiceRequests");

// List Service Requests
app.MapGet("/api/requests", async (CivicConnectDbContext dbContext) =>
{
    var requests = await dbContext.ServiceRequests
        .AsNoTracking()
        .Include(r => r.Category)
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new
        {
            r.Id,
            r.Title,
            r.CategoryId,
            CategoryName = r.Category != null ? r.Category.Name : null,
            r.Impact,
            ImpactName = r.Impact.ToString(),
            r.Urgency,
            UrgencyName = r.Urgency.ToString(),
            r.Priority,
            PriorityName = r.Priority.ToString(),
            r.Status,
            r.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(requests);
})
.WithName("GetServiceRequests")
.WithTags("ServiceRequests");

app.Run();

public record CalculatePriorityRequest(ImpactLevel Impact, int? CategoryId, UrgencyLevel? Urgency);
public record CalculatePriorityResponse(int? CategoryId, string CategoryName, ImpactLevel Impact, string ImpactName, UrgencyLevel Urgency, string UrgencyName, PriorityLevel Priority, string PriorityName);
public record CreateServiceRequestDto(string Title, string Description, int CategoryId, ImpactLevel Impact, Guid? RequesterId);
