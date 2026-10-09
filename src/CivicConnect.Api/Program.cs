using CivicConnect.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. Service Registration & Dependency Injection
// ==========================================

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

// Configure PostgreSQL Relational Persistence via CivicConnectDbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=changeme";

builder.Services.AddDbContext<CivicConnectDbContext>(options =>
{
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString);
});

var app = builder.Build();

// ==========================================
// 2. HTTP Request Pipeline
// ==========================================

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

app.MapControllers();

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

app.Run();
