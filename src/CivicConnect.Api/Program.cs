using CivicConnect.Data;
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

app.Run();
