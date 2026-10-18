using CivicConnect.Api.Controllers;
using CivicConnect.Api.Services;
using CivicConnect.Data;
using CivicConnect.Data.Entities;
using CivicConnect.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Tests;

/// <summary>
/// Integration tests verifying NFR-05: Retention and Disposal of Personal Information
/// in accordance with POPIA s14(1) and the Requirements Traceability Matrix.
/// Covers:
/// - IT-RET-01 RetentionSweep
/// - IT-RET-02 DeIdentifyKeepsIncidents
/// </summary>
[Collection("Database")]
public sealed class RetentionAndDisposalTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public RetentionAndDisposalTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// [IT-RET-01 RetentionSweep]
    /// Given idempotency records older than seven days and notifications read more than twelve months ago,
    /// when the retention job runs, then those rows are absent from the database and no error is raised
    /// for rows still within their period.
    /// </summary>
    [Fact]
    public async Task IT_RET_01_RetentionSweep()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var reporter = await CreateUserAsync(context, "Retention Reporter");
        var team = await CreateTeamAsync(context, "Retention Ops Team");
        var incident = await CreateIncidentAsync(context, team, reporter);
        var ticket = await CreateTicketAsync(context, incident, reporter);

        var now = DateTimeOffset.UtcNow;

        // 1. Seed submission_request records
        // A: Expired (> 7 days old and expires_at < now) -> MUST BE PURGED
        var expiredSubmissionRequest = new SubmissionRequest
        {
            ReporterId = reporter.Id,
            IdempotencyKey = $"exp-{Guid.NewGuid():N}",
            RequestFingerprint = "fingerprint-expired",
            TicketId = ticket.Id,
            RequestedAt = now.AddDays(-10),
            ExpiresAt = now.AddDays(-9)
        };
        context.SubmissionRequests.Add(expiredSubmissionRequest);

        // B: Expired by expires_at (< now, but created within 7 days) -> MUST BE PURGED
        var expiredByTtlSubmissionRequest = new SubmissionRequest
        {
            ReporterId = reporter.Id,
            IdempotencyKey = $"ttl-{Guid.NewGuid():N}",
            RequestFingerprint = "fingerprint-ttl-expired",
            TicketId = ticket.Id,
            RequestedAt = now.AddDays(-2),
            ExpiresAt = now.AddHours(-1)
        };
        context.SubmissionRequests.Add(expiredByTtlSubmissionRequest);

        // C: Active (unexpired: expires in future) -> MUST BE RETAINED
        var activeSubmissionRequest = new SubmissionRequest
        {
            ReporterId = reporter.Id,
            IdempotencyKey = $"act-{Guid.NewGuid():N}",
            RequestFingerprint = "fingerprint-active",
            TicketId = ticket.Id,
            RequestedAt = now.AddHours(-1),
            ExpiresAt = now.AddHours(23)
        };
        context.SubmissionRequests.Add(activeSubmissionRequest);

        // 2. Seed notification records
        // A: Read > 12 months ago (e.g. read 400 days ago) -> MUST BE PURGED
        var agedReadNotification = new Notification
        {
            IncidentId = incident.Id,
            RecipientId = reporter.Id,
            Kind = NotificationKind.StatusChange,
            DedupKey = $"dedup-aged-read-{Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-410),
            ReadAt = now.AddDays(-400)
        };
        context.Notifications.Add(agedReadNotification);

        // B: Read recently (< 12 months ago, e.g. read 30 days ago) -> MUST BE RETAINED
        var recentReadNotification = new Notification
        {
            IncidentId = incident.Id,
            RecipientId = reporter.Id,
            Kind = NotificationKind.StatusChange,
            DedupKey = $"dedup-recent-read-{Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-35),
            ReadAt = now.AddDays(-30)
        };
        context.Notifications.Add(recentReadNotification);

        // C: Unread notification older than 12 months -> MUST BE RETAINED (unread notifications are never purged)
        var agedUnreadNotification = new Notification
        {
            IncidentId = incident.Id,
            RecipientId = reporter.Id,
            Kind = NotificationKind.Assignment,
            DedupKey = $"dedup-aged-unread-{Guid.NewGuid():N}",
            CreatedAt = now.AddDays(-450),
            ReadAt = null
        };
        context.Notifications.Add(agedUnreadNotification);

        // D: Unread notification created recently -> MUST BE RETAINED
        var recentUnreadNotification = new Notification
        {
            IncidentId = incident.Id,
            RecipientId = reporter.Id,
            Kind = NotificationKind.Resolution,
            DedupKey = $"dedup-recent-unread-{Guid.NewGuid():N}",
            CreatedAt = now.AddHours(-2),
            ReadAt = null
        };
        context.Notifications.Add(recentUnreadNotification);

        await context.SaveChangesAsync();

        // Execute the retention sweep job using "now" as the reference anchor
        var sweepJob = new RetentionSweepJob(context);
        var summary = await sweepJob.SweepAsync(now);

        // Verify summary counts
        Assert.True(summary.PurgedSubmissionRequests >= 2, $"Expected at least 2 purged submission requests, got {summary.PurgedSubmissionRequests}");
        Assert.True(summary.PurgedNotifications >= 1, $"Expected at least 1 purged notification, got {summary.PurgedNotifications}");
        Assert.True(summary.TotalPurgedRecords >= 3);

        // Verify database state: expired rows are absent
        var remainingExpiredSr1 = await context.SubmissionRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(sr => sr.ReporterId == reporter.Id && sr.IdempotencyKey == expiredSubmissionRequest.IdempotencyKey);
        Assert.Null(remainingExpiredSr1);

        var remainingExpiredSr2 = await context.SubmissionRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(sr => sr.ReporterId == reporter.Id && sr.IdempotencyKey == expiredByTtlSubmissionRequest.IdempotencyKey);
        Assert.Null(remainingExpiredSr2);

        var remainingAgedNotif = await context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == agedReadNotification.Id);
        Assert.Null(remainingAgedNotif);

        // Verify database state: active rows within period are retained
        var remainingActiveSr = await context.SubmissionRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(sr => sr.ReporterId == reporter.Id && sr.IdempotencyKey == activeSubmissionRequest.IdempotencyKey);
        Assert.NotNull(remainingActiveSr);
        Assert.Equal(activeSubmissionRequest.RequestFingerprint, remainingActiveSr.RequestFingerprint);

        var remainingRecentReadNotif = await context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == recentReadNotification.Id);
        Assert.NotNull(remainingRecentReadNotif);
        Assert.NotNull(remainingRecentReadNotif.ReadAt);

        var remainingAgedUnreadNotif = await context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == agedUnreadNotification.Id);
        Assert.NotNull(remainingAgedUnreadNotif);
        Assert.Null(remainingAgedUnreadNotif.ReadAt);

        var remainingRecentUnreadNotif = await context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == recentUnreadNotification.Id);
        Assert.NotNull(remainingRecentUnreadNotif);
        Assert.Null(remainingRecentUnreadNotif.ReadAt);
    }

    /// <summary>
    /// [IT-RET-02 DeIdentifyKeepsIncidents]
    /// Given a de-identification request for a user, when it is processed, then that user's name
    /// and e-mail no longer appear in the user table, their credential row is absent/scrubbed,
    /// and the incidents they reported remain readable with their reference numbers intact.
    /// </summary>
    [Fact]
    public async Task IT_RET_02_DeIdentifyKeepsIncidents()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var originalEmail = $"subject.citizen.{Guid.NewGuid():N}@capetown.gov.za";
        const string originalFullName = "Nomsa Khumalo";
        const string plaintextPassword = "SecretCitizenPassword#2026";

        var hasher = Argon2idPasswordHasher.Instance;
        var user = new AppUser
        {
            Email = originalEmail,
            FullName = originalFullName,
            PasswordHash = hasher.HashPassword(plaintextPassword),
            IsActive = true
        };
        context.AppUsers.Add(user);
        await context.SaveChangesAsync();

        var team = await CreateTeamAsync(context, "Roads and Infrastructure");
        var incident = await CreateIncidentAsync(context, team, user);
        var ticket = await CreateTicketAsync(context, incident, user);

        // Pre-assertions: user exists with original credentials and names
        Assert.Equal(originalFullName, user.FullName);
        Assert.Equal(originalEmail, user.Email);
        Assert.True(user.IsActive);
        Assert.True(hasher.VerifyPassword(plaintextPassword, user.PasswordHash));
        Assert.StartsWith("INC-", incident.ReferenceNumber, StringComparison.Ordinal);
        Assert.StartsWith("TKT-", ticket.ReferenceNumber, StringComparison.Ordinal);

        // Execute de-identification procedure
        var deIdService = new UserDeIdentificationService(context);
        var result = await deIdService.DeIdentifyUserAsync(user.Id);

        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(UserDeIdentificationService.GenericFullName, result.GenericFullName);
        Assert.Equal($"deidentified-{user.Id:D}@redacted.local", result.AnonymizedEmail);
        Assert.False(result.IsActive);
        Assert.True(result.AssociatedTicketsPreserved >= 1);
        Assert.True(result.AssociatedIncidentsPreserved >= 1);

        // Reload user directly from database without EF tracking
        var reloadedUser = await context.AppUsers
            .AsNoTracking()
            .SingleAsync(u => u.Id == user.Id);

        // 1. User's original name and email no longer appear in the user table
        Assert.NotEqual(originalFullName, reloadedUser.FullName);
        Assert.NotEqual(originalEmail, reloadedUser.Email);
        Assert.Equal(UserDeIdentificationService.GenericFullName, reloadedUser.FullName);
        Assert.Equal($"deidentified-{user.Id:D}@redacted.local", reloadedUser.Email);
        Assert.False(reloadedUser.IsActive);

        // Search the table for the original credentials/names: must not exist
        var countByOriginalEmail = await context.AppUsers
            .AsNoTracking()
            .CountAsync(u => u.Email == originalEmail);
        Assert.Equal(0, countByOriginalEmail);

        var countByOriginalName = await context.AppUsers
            .AsNoTracking()
            .CountAsync(u => u.FullName == originalFullName);
        Assert.Equal(0, countByOriginalName);

        // 2. Credential row is absent / scrubbed with invalid sentinel hash
        Assert.Equal(UserDeIdentificationService.InvalidSentinelPasswordHash, reloadedUser.PasswordHash);
        Assert.False(hasher.VerifyPassword(plaintextPassword, reloadedUser.PasswordHash));
        Assert.False(Argon2idPasswordHasher.IsValidPhcFormat(reloadedUser.PasswordHash));

        // 3. Incidents and tickets they reported remain fully queryable and readable with reference numbers intact
        var reloadedTicket = await context.Tickets
            .AsNoTracking()
            .SingleAsync(t => t.Id == ticket.Id);

        Assert.Equal(ticket.ReferenceNumber, reloadedTicket.ReferenceNumber);
        Assert.Equal(ticket.Title, reloadedTicket.Title);
        Assert.Equal(ticket.Description, reloadedTicket.Description);
        Assert.Equal(user.Id, reloadedTicket.ReporterId);

        var reloadedIncident = await context.Incidents
            .AsNoTracking()
            .SingleAsync(i => i.Id == incident.Id);

        Assert.Equal(incident.ReferenceNumber, reloadedIncident.ReferenceNumber);
        Assert.Equal(incident.Title, reloadedIncident.Title);
        Assert.Equal(incident.Description, reloadedIncident.Description);
        Assert.Equal(IncidentStatus.New, reloadedIncident.Status);
    }

    /// <summary>
    /// Verifies administrative controller endpoints for on-demand retention sweep and user de-identification.
    /// </summary>
    [Fact]
    public async Task ManagementController_Endpoints_ExecuteSuccessfully()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var reporter = await CreateUserAsync(context, "Admin Target User");
        var sweepService = new RetentionSweepJob(context);
        var deIdService = new UserDeIdentificationService(context);
        var reportingMock = new MockIncidentReportingService();

        var controller = new ManagementController(reportingMock, sweepService, deIdService);

        // 1. Retention sweep endpoint
        var sweepActionResult = await controller.RunRetentionSweepAsync();
        var sweepOkResult = Assert.IsType<OkObjectResult>(sweepActionResult.Result);
        var sweepSummary = Assert.IsType<RetentionSweepSummaryDto>(sweepOkResult.Value);
        Assert.NotNull(sweepSummary);

        // 2. User de-identification endpoint
        var deIdActionResult = await controller.DeIdentifyUserAsync(reporter.Id);
        var deIdOkResult = Assert.IsType<OkObjectResult>(deIdActionResult.Result);
        var deIdSummary = Assert.IsType<UserDeIdentificationResultDto>(deIdOkResult.Value);
        Assert.Equal(reporter.Id, deIdSummary.UserId);
        Assert.Equal(UserDeIdentificationService.GenericFullName, deIdSummary.GenericFullName);

        // 3. Non-existent user returns 404 NotFound
        var notFoundActionResult = await controller.DeIdentifyUserAsync(Guid.NewGuid());
        Assert.IsType<NotFoundObjectResult>(notFoundActionResult.Result);
    }

    private static async Task<AppUser> CreateUserAsync(CivicConnectDbContext context, string name)
    {
        var user = new AppUser
        {
            Email = $"{name.ToLowerInvariant().Replace(" ", ".")}.{Guid.NewGuid():N}@example.com",
            FullName = name,
            PasswordHash = Argon2idPasswordHasher.Instance.HashPassword("TestPass#123!"),
            IsActive = true,
        };
        context.AppUsers.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<ServiceTeam> CreateTeamAsync(CivicConnectDbContext context, string name)
    {
        var team = new ServiceTeam
        {
            Name = $"{name}-{Guid.NewGuid():N}",
        };
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();
        return team;
    }

    private static async Task<Incident> CreateIncidentAsync(
        CivicConnectDbContext context,
        ServiceTeam team,
        AppUser reporter)
    {
        var category = new Category
        {
            Name = $"Category-{Guid.NewGuid():N}",
            Description = "Retention test category",
            UrgencyLevel = 2,
            DefaultServiceTeamId = team.Id,
            IsActive = true,
        };
        context.Categories.Add(category);

        var locationKindId = await context.LocationKinds
            .AsNoTracking()
            .Select(lk => lk.Id)
            .FirstAsync();

        var location = new Location
        {
            Name = $"Location-{Guid.NewGuid():N}",
            LocationKindId = locationKindId,
            ImpactLevel = 2,
            IsActive = true,
        };
        context.Locations.Add(location);

        var priorityId = await context.PriorityTargets
            .AsNoTracking()
            .OrderBy(pt => pt.Id)
            .Select(pt => pt.Id)
            .FirstAsync();

        await context.SaveChangesAsync();

        var incident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            Title = "Retention verification incident",
            Description = "Ensuring historical reports are retained after user de-identification",
            Status = IncidentStatus.New,
            ComputedPriority = priorityId,
            Priority = priorityId,
            PriorityOverridden = false
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();

        context.IncidentSubscriptions.Add(new IncidentSubscription
        {
            IncidentId = incident.Id,
            ReporterId = reporter.Id,
        });
        await context.SaveChangesAsync();

        return incident;
    }

    private static async Task<Ticket> CreateTicketAsync(
        CivicConnectDbContext context,
        Incident incident,
        AppUser reporter)
    {
        var ticket = new Ticket
        {
            IncidentId = incident.Id,
            ReporterId = reporter.Id,
            CategoryId = incident.CategoryId,
            LocationId = incident.LocationId,
            Title = "Retention verification ticket",
            Description = "Citizen submitted issue details"
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();
        return ticket;
    }

    private sealed class MockIncidentReportingService : IIncidentReportingService
    {
        public Task<IReadOnlyList<OpenStatusCountDto>> GetOpenStatusCountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OpenStatusCountDto>>([]);

        public Task<IReadOnlyList<OverdueIncidentDto>> GetOverdueIncidentsAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OverdueIncidentDto>>([]);

        public Task<IReadOnlyList<CategoryResolutionBreakdownDto>> GetResolutionBreakdownByCategoryAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategoryResolutionBreakdownDto>>([]);

        public Task<IReadOnlyList<TeamResolutionBreakdownDto>> GetResolutionBreakdownByTeamAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TeamResolutionBreakdownDto>>([]);

        public Task<IReadOnlyList<TurnaroundSummaryDto>> GetTurnaroundSummaryAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TurnaroundSummaryDto>>([]);

        public Task<IReadOnlyList<IncidentListItemDto>> ListIncidentsAsync(
            IncidentStatus? status = null,
            int? categoryId = null,
            int? teamId = null,
            int? locationId = null,
            int page = 1,
            int pageSize = 50,
            bool onlyOpen = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IncidentListItemDto>>([]);

        public Task<IReadOnlyList<IncidentListItemDto>> GetTeamQueueAsync(
            int teamId,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IncidentListItemDto>>([]);

        public Task<IReadOnlyList<ReporterTicketDto>> ListTicketsForReporterAsync(Guid reporterId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReporterTicketDto>>([]);
    }
}
