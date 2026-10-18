using CivicConnect.Api.Services;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Tests;

/// <summary>
/// Integration tests for FR-01 — Submit and Categorise a Service Request:
/// "Given that a user has entered the required issue details and selected a category for
/// their request, when the request is submitted, a new ticket should be generated containing
/// the information from the request and a success confirmation message should be displayed."
/// Each test opens its own transaction and never commits it, so the rollback on disposal is
/// the cleanup — no manual deletes needed, matching <see cref="ServiceCatalogueTests"/>.
/// </summary>
[Collection("Database")]
public sealed class SubmissionServiceTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public SubmissionServiceTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IT_SUB_01_SubmitCreatesTicketAndIncident()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, category, location, reporter) = await CreateScopeAsync(context);
        var service = new SubmissionService(context, new PriorityMatrix());

        var result = await service.SubmitAsync(new SubmitTicketRequestDto
        {
            Title = "Projector not powering on",
            Description = "The lecture hall projector will not switch on.",
            CategoryId = category.Id,
            LocationId = location.Id,
            ReporterId = reporter.Id
        });

        // A new ticket is generated, carrying the submitted information.
        Assert.NotNull(result.TicketId);
        Assert.Matches(@"^TKT-\d{6,}$", result.TicketReferenceNumber!);
        Assert.Equal("Projector not powering on", result.Title);
        Assert.Equal("The lecture hall projector will not switch on.", result.Description);
        Assert.Equal(category.Id, result.CategoryId);
        Assert.Equal(location.Id, result.LocationId);

        var ticket = await context.Tickets.AsNoTracking().SingleAsync(t => t.Id == result.TicketId);
        Assert.Equal(reporter.Id, ticket.ReporterId);
        Assert.Equal("Projector not powering on", ticket.Title);
        Assert.Equal("The lecture hall projector will not switch on.", ticket.Description);
        Assert.Equal(category.Id, ticket.CategoryId);
        Assert.Equal(location.Id, ticket.LocationId);

        // The data a "success confirmation" is built from: a new incident with its own
        // reference number, not a replay of a prior submission.
        Assert.True(result.CreatedNewIncident);
        Assert.False(result.IsIdempotentReplay);
        Assert.StartsWith("INC-", result.ReferenceNumber, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitRequest_SelectedCategoryIsPersistedOnTheGeneratedTicket()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, category, location, reporter) = await CreateScopeAsync(context);
        var service = new SubmissionService(context, new PriorityMatrix());

        var result = await service.SubmitAsync(new SubmitTicketRequestDto
        {
            Title = "Network outage in server room",
            Description = "No network access from any port in the server room.",
            CategoryId = category.Id,
            LocationId = location.Id,
            ReporterId = reporter.Id
        });

        Assert.Equal(category.Id, result.CategoryId);
        Assert.Equal(category.Name, result.CategoryName);

        var ticket = await context.Tickets.AsNoTracking().SingleAsync(t => t.Id == result.TicketId);
        Assert.Equal(category.Id, ticket.CategoryId);
    }

    [Theory]
    [InlineData("", "Valid description")]
    [InlineData("   ", "Valid description")]
    [InlineData("Valid title", "")]
    [InlineData("Valid title", "   ")]
    public async Task SubmitRequest_MissingRequiredIssueDetails_IsRejected(string title, string description)
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, category, location, reporter) = await CreateScopeAsync(context);
        var service = new SubmissionService(context, new PriorityMatrix());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(new SubmitTicketRequestDto
        {
            Title = title,
            Description = description,
            CategoryId = category.Id,
            LocationId = location.Id,
            ReporterId = reporter.Id
        }));
    }

    [Fact]
    public async Task SubmitRequest_WithoutSelectingAnExistingCategory_IsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, _, location, reporter) = await CreateScopeAsync(context);
        var service = new SubmissionService(context, new PriorityMatrix());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SubmitAsync(new SubmitTicketRequestDto
        {
            Title = "Leaking pipe",
            Description = "Water pooling under the sink.",
            CategoryId = -1,
            LocationId = location.Id,
            ReporterId = reporter.Id
        }));
    }

    [Fact]
    public async Task SubmitRequest_WithDeactivatedCategory_IsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, category, location, reporter) = await CreateScopeAsync(context);

        category.IsActive = false;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var service = new SubmissionService(context, new PriorityMatrix());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SubmitAsync(new SubmitTicketRequestDto
        {
            Title = "Leaking pipe",
            Description = "Water pooling under the sink.",
            CategoryId = category.Id,
            LocationId = location.Id,
            ReporterId = reporter.Id
        }));
    }

    [Fact]
    public async Task SubmitRequest_WithNonexistentLocation_IsRejected()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var (_, category, _, reporter) = await CreateScopeAsync(context);
        var service = new SubmissionService(context, new PriorityMatrix());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SubmitAsync(new SubmitTicketRequestDto
        {
            Title = "Leaking pipe",
            Description = "Water pooling under the sink.",
            CategoryId = category.Id,
            LocationId = -1,
            ReporterId = reporter.Id
        }));
    }

    private static async Task<(ServiceTeam Team, Category Category, Location Location, AppUser Reporter)> CreateScopeAsync(
        CivicConnectDbContext context)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var team = new ServiceTeam { Name = $"Submission Team {suffix}" };
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();

        var category = new Category
        {
            Name = $"Submission Category {suffix}",
            Description = "Used by FR-01 submission tests",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
            IsActive = true,
        };
        var location = new Location
        {
            Name = $"Submission Location {suffix}",
            LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
            ImpactLevel = 2,
            IsActive = true,
        };
        var reporter = new AppUser
        {
            Email = $"sub.rep.{suffix}@example.com",
            FullName = "Submission Test Reporter",
            PasswordHash = "hashed-password",
            IsActive = true,
        };

        context.Categories.Add(category);
        context.Locations.Add(location);
        context.AppUsers.Add(reporter);
        await context.SaveChangesAsync();

        return (team, category, location, reporter);
    }
}
