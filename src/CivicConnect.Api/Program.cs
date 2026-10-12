using System.Text.Json.Serialization;
using CivicConnect.Api.Services;
using CivicConnect.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration["CONNECTION_STRING"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("CONNECTION_STRING environment variable is not set. Copy .env.example to .env and fill it in.");

builder.Services.AddDbContext<CivicConnectDbContext>(options =>
{
    CivicConnectDbContext.Configure((DbContextOptionsBuilder<CivicConnectDbContext>)options, connectionString);
});

builder.Services.AddScoped<IResolutionService, ResolutionService>();
builder.Services.AddScoped<ResolutionService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CivicConnect API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
