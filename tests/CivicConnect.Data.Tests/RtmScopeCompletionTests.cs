using System.Security.Claims;
using CivicConnect.Api.Controllers;
using CivicConnect.Api.Services;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicConnect.Data.Tests;

/// <summary>
/// Integration tests completing formal RTM test IDs:
/// - IT-REP-01 (FR-17, FR-12): Management reporting, status counts, overdue list, category/team breakdowns, and SLA turnaround.
/// - IT-NOTIF-01 (FR-13): Subscribed reporter notification fan-out on status change and resolution with summary.
/// - IT-AUD-01 (FR-08): PostgreSQL audit_log recording and append-only immutability triggers.
/// - IT-SEC-01 (NFR-04): POPIA s19(1) staff_directory security view and civic_report least-privilege access.
/// - IT-SEC-02 (NFR-10): Least-privilege civic_app vs civic_migrate role enforcement (no DDL, no DELETE, no audit_log UPDATE/DELETE).
/// </summary>
[Collection("Database")]
public sealed class RtmScopeCompletionTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public RtmScopeCompletionTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IT_REP_01_ManagementReporting_StatusCounts_Overdue_CategoryTeamBreakdowns_AndTurnaround()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;

        var reporter = new AppUser
        {
            Email = $"rep.r01.{suffix}@example.com",
            FullName = "Reporter R01",
            PasswordHash = "hash"
        };
        var tech = new AppUser
        {
            Email = $"tech.r01.{suffix}@example.com",
            FullName = "Tech R01",
            PasswordHash = "hash"
        };
        var team = new ServiceTeam { Name = $"OpsTeam-{suffix}" };

        context.AppUsers.AddRange(reporter, tech);
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();

        var category = new Category
        {
            Name = $"Cat-{suffix}",
            Description = "Reporting test category",
            UrgencyLevel = 3,
            DefaultServiceTeamId = team.Id,
            IsActive = true
        };
        var location = new Location
        {
            Name = $"Loc-{suffix}",
            LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "site").Id,
            ImpactLevel = 3,
            IsActive = true
        };

        context.Categories.Add(category);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        // 1. Open overdue incident (opened 10 hours ago, Priority 1 = 60 min SLA -> overdue)
        var overdueIncident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            AssignedTeamId = team.Id,
            AssigneeId = tech.Id,
            Title = $"Overdue incident {suffix}",
            Description = "Past SLA target",
            Status = IncidentStatus.InProgress,
            ComputedPriority = 1,
            Priority = 1,
            PriorityOverridden = false,
            OpenedAt = now.AddHours(-10)
        };

        // 2. Resolved incident that met SLA (opened 30 mins ago, resolved 10 mins ago, Priority 1 = 60 min SLA -> 20 min turnaround)
        var resolvedMetIncident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            AssignedTeamId = team.Id,
            AssigneeId = tech.Id,
            Title = $"Resolved met SLA {suffix}",
            Description = "Resolved within 20 minutes",
            Status = IncidentStatus.Resolved,
            ComputedPriority = 1,
            Priority = 1,
            PriorityOverridden = false,
            OpenedAt = now.AddMinutes(-30),
            ResolvedAt = now.AddMinutes(-10)
        };

        // 3. Closed incident
        var closedIncident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            AssignedTeamId = team.Id,
            AssigneeId = tech.Id,
            Title = $"Closed incident {suffix}",
            Description = "Closed after verification",
            Status = IncidentStatus.Closed,
            ComputedPriority = 2,
            Priority = 2,
            PriorityOverridden = false,
            OpenedAt = now.AddHours(-3),
            ResolvedAt = now.AddHours(-2),
            ClosedAt = now.AddHours(-1)
        };

        context.Incidents.AddRange(overdueIncident, resolvedMetIncident, closedIncident);
        await context.SaveChangesAsync();

        var reportingService = new IncidentReportingService(context);
        var controller = new ManagementController(reportingService);

        // Verify open status counts (FR-17)
        var openCountsResult = await controller.GetOpenStatusCountsAsync();
        var openCountsOk = Assert.IsType<OkObjectResult>(openCountsResult.Result);
        var openCounts = Assert.IsAssignableFrom<IReadOnlyList<OpenStatusCountDto>>(openCountsOk.Value);
        Assert.Contains(openCounts, c => c.Status == IncidentStatus.InProgress && c.Count >= 1);

        // Verify overdue incidents (FR-12)
        var overdueResult = await controller.GetOverdueIncidentsAsync(page: 1, pageSize: 100);
        var overdueOk = Assert.IsType<OkObjectResult>(overdueResult.Result);
        var overdueList = Assert.IsAssignableFrom<IReadOnlyList<CivicConnect.Api.Services.OverdueIncidentDto>>(overdueOk.Value);
        Assert.Contains(overdueList, i => i.Id == overdueIncident.Id && i.DueAt < now);
        Assert.DoesNotContain(overdueList, i => i.Id == resolvedMetIncident.Id);
        Assert.DoesNotContain(overdueList, i => i.Id == closedIncident.Id);

        // Verify category resolution breakdown (FR-17)
        var catBreakdownResult = await controller.GetResolutionBreakdownByCategoryAsync(now.AddHours(-12), now.AddHours(1));
        var catBreakdownOk = Assert.IsType<OkObjectResult>(catBreakdownResult.Result);
        var catBreakdowns = Assert.IsAssignableFrom<IReadOnlyList<CategoryResolutionBreakdownDto>>(catBreakdownOk.Value);
        var catRow = Assert.Single(catBreakdowns, c => c.CategoryId == category.Id);
        Assert.Equal(1, catRow.ResolvedCount);
        Assert.Equal(1, catRow.ClosedCount);

        // Verify service team resolution breakdown (FR-17)
        var teamBreakdownResult = await controller.GetResolutionBreakdownByTeamAsync(now.AddHours(-12), now.AddHours(1));
        var teamBreakdownOk = Assert.IsType<OkObjectResult>(teamBreakdownResult.Result);
        var teamBreakdowns = Assert.IsAssignableFrom<IReadOnlyList<TeamResolutionBreakdownDto>>(teamBreakdownOk.Value);
        var teamRow = Assert.Single(teamBreakdowns, t => t.TeamId == team.Id);
        Assert.Equal(1, teamRow.ResolvedCount);
        Assert.Equal(1, teamRow.ClosedCount);

        // Verify SLA turnaround summary (FR-17)
        var turnaroundResult = await controller.GetTurnaroundSummaryAsync(now.AddHours(-12), now.AddHours(1));
        var turnaroundOk = Assert.IsType<OkObjectResult>(turnaroundResult.Result);
        var turnaroundRows = Assert.IsAssignableFrom<IReadOnlyList<TurnaroundSummaryDto>>(turnaroundOk.Value);
        Assert.Contains(turnaroundRows, r => r.PriorityId == 1 && r.MetCount >= 1 && r.AvgTurnaroundMinutes > 0);
    }

    [Fact]
    public async Task IT_NOTIF_01_SubscribedReporterReceivesResolutionNotificationWithSummary()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reporter = new AppUser
        {
            Email = $"rep.notif.{suffix}@example.com",
            FullName = "Subscribed Reporter",
            PasswordHash = "hash"
        };
        var tech = new AppUser
        {
            Email = $"tech.notif.{suffix}@example.com",
            FullName = "Resolving Technician",
            PasswordHash = "hash"
        };
        var team = new ServiceTeam { Name = $"NotifTeam-{suffix}" };

        context.AppUsers.AddRange(reporter, tech);
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();

        var category = new Category
        {
            Name = $"NotifCat-{suffix}",
            Description = "Notification test category",
            UrgencyLevel = 2,
            DefaultServiceTeamId = team.Id,
            IsActive = true
        };
        var location = new Location
        {
            Name = $"NotifLoc-{suffix}",
            LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
            ImpactLevel = 2,
            IsActive = true
        };
        context.Categories.Add(category);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var incident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            AssignedTeamId = team.Id,
            AssigneeId = tech.Id,
            Title = "Leaking pipe in room",
            Description = "Water dripping from ceiling valve",
            Status = IncidentStatus.InProgress,
            ComputedPriority = 3,
            Priority = 3
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();

        var ticket = new Ticket
        {
            IncidentId = incident.Id,
            ReporterId = reporter.Id,
            CategoryId = category.Id,
            LocationId = location.Id,
            Title = incident.Title,
            Description = incident.Description
        };
        var subscription = new IncidentSubscription
        {
            IncidentId = incident.Id,
            ReporterId = reporter.Id
        };
        context.Tickets.Add(ticket);
        context.IncidentSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

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
            ChangedBy = tech.Id
        });
        await context.SaveChangesAsync();

        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.Assigned,
            ToStatus = IncidentStatus.InProgress,
            ChangedBy = tech.Id
        });
        await context.SaveChangesAsync();

        var fixedCode = await context.ResolutionCodes.AsNoTracking().SingleAsync(c => c.Code == "FIXED");
        const string expectedSummary = "Replaced worn valve seal and pressure-tested line.";

        context.IncidentStatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            FromStatus = IncidentStatus.InProgress,
            ToStatus = IncidentStatus.Resolved,
            ChangedBy = tech.Id,
            Note = expectedSummary
        });
        context.Resolutions.Add(new Resolution
        {
            IncidentId = incident.Id,
            ResolutionCodeId = fixedCode.Id,
            Summary = expectedSummary,
            ResolvedBy = tech.Id
        });
        await context.SaveChangesAsync();

        var notificationsController = new NotificationsController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, reporter.Id.ToString()),
                        new Claim(ClaimTypes.Role, "Requester")
                    ], "TestAuth"))
                }
            }
        };

        var actionResult = await notificationsController.GetUnreadNotifications();
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var unread = Assert.IsAssignableFrom<IReadOnlyList<NotificationDto>>(okResult.Value);

        var resolutionNotif = Assert.Single(unread, n => n.IncidentId == incident.Id && n.Kind == NotificationKind.Resolution);
        Assert.Equal(expectedSummary, resolutionNotif.ResolutionSummary);
        Assert.Contains(expectedSummary, resolutionNotif.Message, StringComparison.Ordinal);
        Assert.False(resolutionNotif.IsRead);

        // Verify marking notification as read updates ReadAt
        var markReadResult = await notificationsController.ToggleMarkRead(resolutionNotif.Id);
        Assert.IsType<OkObjectResult>(markReadResult);

        var unreadAfterMark = await CivicConnect.Web.Controllers.NotificationsController.ReadUnreadAsync(context, reporter.Id);
        Assert.DoesNotContain(unreadAfterMark, n => n.Id == resolutionNotif.Id);
    }

    [Fact]
    public async Task IT_AUD_01_AuditLogRecordsChangesAndEnforcesAppendOnlyImmutability()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var supervisor = new AppUser
        {
            Email = $"sup.aud.{suffix}@example.com",
            FullName = "Audit Supervisor",
            PasswordHash = "hash"
        };
        var team = new ServiceTeam { Name = $"AuditTeam-{suffix}" };
        context.AppUsers.Add(supervisor);
        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();

        var category = new Category
        {
            Name = $"AuditCat-{suffix}",
            Description = "Audit category",
            UrgencyLevel = 2,
            DefaultServiceTeamId = team.Id,
            IsActive = true
        };
        var location = new Location
        {
            Name = $"AuditLoc-{suffix}",
            LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "floor").Id,
            ImpactLevel = 2,
            IsActive = true
        };
        context.Categories.Add(category);
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        var incident = new Incident
        {
            CategoryId = category.Id,
            LocationId = location.Id,
            Title = "Audit test incident",
            Description = "Verifies database audit trigger and immutability",
            Status = IncidentStatus.New,
            ComputedPriority = 3,
            Priority = 3,
            PriorityOverridden = false
        };
        context.Incidents.Add(incident);
        await context.SaveChangesAsync();

        // Trigger priority override -> incident_priority_override_audit_trg inserts into audit_log
        incident.Priority = 1;
        incident.PriorityOverridden = true;
        incident.OverrideBy = supervisor.Id;
        incident.OverrideReason = "Escalated due to exam venue impact";
        incident.OverrideAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        var auditEntry = await context.AuditLogs
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.IncidentId == incident.Id && a.Action == AuditAction.Override);

        Assert.NotNull(auditEntry);
        Assert.Equal(supervisor.Id, auditEntry.ActorId);
        Assert.Equal(AuditEntityType.Incident, auditEntry.EntityType);
        Assert.Contains("Escalated due to exam venue impact", auditEntry.AfterState ?? string.Empty, StringComparison.Ordinal);

        // Verify append-only trigger blocks UPDATE on audit_log
        await transaction.CreateSavepointAsync("before_audit_update");
        var updateEx = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "UPDATE audit_log SET entity_id = 'tampered' WHERE id = {0}",
                auditEntry.Id));
        Assert.Contains("append-only", updateEx.Message, StringComparison.OrdinalIgnoreCase);
        await transaction.RollbackToSavepointAsync("before_audit_update");

        // Verify append-only trigger blocks DELETE on audit_log
        await transaction.CreateSavepointAsync("before_audit_delete");
        var deleteEx = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "DELETE FROM audit_log WHERE id = {0}",
                auditEntry.Id));
        Assert.Contains("append-only", deleteEx.Message, StringComparison.OrdinalIgnoreCase);
        await transaction.RollbackToSavepointAsync("before_audit_delete");
    }

    [Fact]
    public async Task IT_SEC_01_PersonalInformationProtection_StaffDirectoryAndCivicReportRole()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staffUser = new AppUser
        {
            Email = $"staff.sec01.{suffix}@example.com",
            FullName = $"Staff Member {suffix}",
            PasswordHash = "$argon2id$v=19$m=65536,t=3,p=1$secret$hash"
        };
        context.AppUsers.Add(staffUser);
        await context.SaveChangesAsync();

        // 1. Verify staff_directory view exposes safe projection without email or password_hash columns
        var directoryEntry = await context.StaffDirectory
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == staffUser.Id);
        Assert.NotNull(directoryEntry);
        Assert.Equal(staffUser.FullName, directoryEntry.FullName);

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using (var colCmd = connection.CreateCommand())
        {
            colCmd.CommandText = """
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'staff_directory'
                """;
            var columns = new List<string>();
            await using var reader = await colCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            Assert.Contains("full_name", columns);
            Assert.DoesNotContain("email", columns);
            Assert.DoesNotContain("password_hash", columns);
        }

        // 2. Verify civic_report role grants: allowed on staff_directory and incident, denied on app_user, ticket, audit_log, incident_note, notification
        await using (var privCmd = connection.CreateCommand())
        {
            privCmd.CommandText = """
                SELECT
                    has_table_privilege('civic_report', 'public.staff_directory', 'SELECT') AS can_read_dir,
                    has_table_privilege('civic_report', 'public.incident', 'SELECT') AS can_read_incident,
                    has_table_privilege('civic_report', 'public.app_user', 'SELECT') AS can_read_user,
                    has_table_privilege('civic_report', 'public.ticket', 'SELECT') AS can_read_ticket,
                    has_table_privilege('civic_report', 'public.incident_note', 'SELECT') AS can_read_note,
                    has_table_privilege('civic_report', 'public.notification', 'SELECT') AS can_read_notif,
                    has_table_privilege('civic_report', 'public.audit_log', 'SELECT') AS can_read_audit
                """;
            await using var reader = await privCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0), "civic_report must have SELECT on staff_directory.");
            Assert.True(reader.GetBoolean(1), "civic_report must have SELECT on incident.");
            Assert.False(reader.GetBoolean(2), "civic_report must NOT have SELECT on app_user.");
            Assert.False(reader.GetBoolean(3), "civic_report must NOT have SELECT on ticket.");
            Assert.False(reader.GetBoolean(4), "civic_report must NOT have SELECT on incident_note.");
            Assert.False(reader.GetBoolean(5), "civic_report must NOT have SELECT on notification.");
            Assert.False(reader.GetBoolean(6), "civic_report must NOT have SELECT on audit_log.");
        }
    }

    [Fact]
    public async Task IT_SEC_02_LeastPrivilegeDatabaseRoles_CivicAppCannotDeleteOrAlterSchemaOrMutateAuditLog()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                (SELECT rolsuper FROM pg_roles WHERE rolname = 'civic_app') AS app_is_super,
                has_schema_privilege('civic_app', 'public', 'CREATE') AS app_can_create_ddl,
                has_schema_privilege('civic_migrate', 'public', 'CREATE') AS migrate_can_create_ddl,
                has_table_privilege('civic_app', 'public.incident', 'SELECT') AS app_can_select_incident,
                has_table_privilege('civic_app', 'public.incident', 'INSERT') AS app_can_insert_incident,
                has_table_privilege('civic_app', 'public.incident', 'UPDATE') AS app_can_update_incident,
                has_table_privilege('civic_app', 'public.incident', 'DELETE') AS app_can_delete_incident,
                has_table_privilege('civic_app', 'public.app_user', 'DELETE') AS app_can_delete_user,
                has_table_privilege('civic_app', 'public.audit_log', 'INSERT') AS app_can_insert_audit,
                has_table_privilege('civic_app', 'public.audit_log', 'UPDATE') AS app_can_update_audit,
                has_table_privilege('civic_app', 'public.audit_log', 'DELETE') AS app_can_delete_audit,
                has_table_privilege('civic_app', 'public.incident_status_history', 'UPDATE') AS app_can_update_history,
                has_table_privilege('civic_app', 'public.incident_status_history', 'DELETE') AS app_can_delete_history
            """;

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0), "civic_app must not be a superuser.");
            Assert.False(reader.GetBoolean(1), "civic_app must not have DDL CREATE privilege on schema public.");
            Assert.True(reader.GetBoolean(2), "civic_migrate must have DDL CREATE privilege on schema public.");
            Assert.True(reader.GetBoolean(3), "civic_app must have SELECT on incident.");
            Assert.True(reader.GetBoolean(4), "civic_app must have INSERT on incident.");
            Assert.True(reader.GetBoolean(5), "civic_app must have UPDATE on incident.");
            Assert.False(reader.GetBoolean(6), "civic_app must NOT have DELETE on incident.");
            Assert.False(reader.GetBoolean(7), "civic_app must NOT have DELETE on app_user.");
            Assert.True(reader.GetBoolean(8), "civic_app must have INSERT on audit_log.");
            Assert.False(reader.GetBoolean(9), "civic_app must NOT have UPDATE on audit_log.");
            Assert.False(reader.GetBoolean(10), "civic_app must NOT have DELETE on audit_log.");
            Assert.False(reader.GetBoolean(11), "civic_app must NOT have UPDATE on incident_status_history.");
            Assert.False(reader.GetBoolean(12), "civic_app must NOT have DELETE on incident_status_history.");
        }

        // Also verify runtime enforcement by switching session role to civic_app inside a savepoint
        await context.Database.ExecuteSqlRawAsync("DO $$ BEGIN EXECUTE format('GRANT civic_app TO %I', current_user); END $$;");

        await transaction.CreateSavepointAsync("before_civic_app_ddl");
        await context.Database.ExecuteSqlRawAsync("SET LOCAL ROLE civic_app");
        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync("CREATE TABLE unauthorized_ddl_test (id int)"));
        await transaction.RollbackToSavepointAsync("before_civic_app_ddl");

        await transaction.CreateSavepointAsync("before_civic_app_delete");
        await context.Database.ExecuteSqlRawAsync("SET LOCAL ROLE civic_app");
        await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM incident WHERE 1 = 0"));
        await transaction.RollbackToSavepointAsync("before_civic_app_delete");
    }
}
