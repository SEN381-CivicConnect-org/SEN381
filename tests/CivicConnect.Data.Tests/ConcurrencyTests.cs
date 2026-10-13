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
/// Integration tests verifying NFR-06 — Concurrency Correctness Under Simultaneous Submission:
/// - IT-CONC-01: 20 simultaneous submissions for the same (category_id, location_id) produce 1 incident, 20 tickets, and 20 distinct subscriptions.
/// - IT-CLM-02: Simultaneous claims by two technicians result in 1 success (200 OK) and 1 conflict (409 Conflict) naming the winning assignee.
/// - IT-CONC-02: Concurrent/stale-version priority overrides reject the stale update with 409 Conflict without overwriting changes.
/// - IT-TXN-01: Mid-operation submission failures roll back the entire transaction, leaving no orphaned rows.
/// </summary>
[Collection("Database")]
public sealed class ConcurrencyTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public ConcurrencyTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentSubmissions_TwentySimultaneousRequests_ProduceSingleIncidentTwentyTicketsAndTwentySubscribers()
    {
        const int workerCount = 20;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        int teamId = 0;
        int categoryId = 0;
        int locationId = 0;
        var reporterIds = new List<Guid>(workerCount);

        try
        {
            await using (var setupContext = _fixture.CreateContext())
            {
                var team = new ServiceTeam { Name = $"ConcTeam-{suffix}" };
                setupContext.ServiceTeams.Add(team);
                await setupContext.SaveChangesAsync();
                teamId = team.Id;

                var category = new Category
                {
                    Name = $"ConcCat-{suffix}",
                    Description = "Concurrent submission test category",
                    UrgencyLevel = 3,
                    DefaultServiceTeamId = team.Id,
                    IsActive = true
                };
                var location = new Location
                {
                    Name = $"ConcLoc-{suffix}",
                    LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "floor").Id,
                    ImpactLevel = 3,
                    IsActive = true
                };
                setupContext.Categories.Add(category);
                setupContext.Locations.Add(location);

                for (var i = 0; i < workerCount; i++)
                {
                    var reporter = new AppUser
                    {
                        Email = $"conc.rep.{i:D2}.{suffix}@example.com",
                        FullName = $"Reporter {i + 1:D2}",
                        PasswordHash = "hashed-password",
                        IsActive = true
                    };
                    setupContext.AppUsers.Add(reporter);
                }

                await setupContext.SaveChangesAsync();
                categoryId = category.Id;
                locationId = location.Id;

                reporterIds = await setupContext.AppUsers
                    .AsNoTracking()
                    .Where(u => u.Email.EndsWith($".{suffix}@example.com"))
                    .OrderBy(u => u.Email)
                    .Select(u => u.Id)
                    .ToListAsync();
                Assert.Equal(workerCount, reporterIds.Count);
            }

            var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyCount = 0;

            var submissionTasks = reporterIds.Select((reporterId, index) => Task.Run(async () =>
            {
                await using var workerContext = _fixture.CreateContext();
                var submissionService = new SubmissionService(workerContext, new PriorityMatrix());

                if (Interlocked.Increment(ref readyCount) == workerCount)
                {
                    startBarrier.TrySetResult();
                }

                await startBarrier.Task;

                return await submissionService.SubmitAsync(new SubmitTicketRequestDto
                {
                    Title = $"Campus Wi-Fi outage report #{index + 1}",
                    Description = $"Reporter #{index + 1} cannot connect to the wireless network on this floor.",
                    CategoryId = categoryId,
                    LocationId = locationId,
                    ReporterId = reporterId,
                    IdempotencyKey = $"conc-key-{suffix}-{index:D2}"
                });
            })).ToArray();

            var results = await Task.WhenAll(submissionTasks);

            Assert.Equal(workerCount, results.Length);
            Assert.Equal(1, results.Count(r => r.CreatedNewIncident));

            await using var verifyContext = _fixture.CreateContext();

            // 1. Exactly 1 open incident exists for (category_id, location_id).
            var openIncidents = await verifyContext.Incidents
                .AsNoTracking()
                .Where(i => i.CategoryId == categoryId && i.LocationId == locationId)
                .ToListAsync();

            var singleIncident = Assert.Single(openIncidents);
            Assert.Equal(IncidentStatus.New, singleIncident.Status);
            Assert.StartsWith("INC-", singleIncident.ReferenceNumber, StringComparison.Ordinal);
            Assert.All(results, r => Assert.Equal(singleIncident.Id, r.Id));

            // 2. Derived report count (COUNT(*) FROM ticket WHERE incident_id = ...) is 20, with 20 distinct TKT-xxxxxx numbers.
            var tickets = await verifyContext.Tickets
                .AsNoTracking()
                .Where(t => t.IncidentId == singleIncident.Id)
                .ToListAsync();

            Assert.Equal(workerCount, tickets.Count);
            Assert.Equal(
                workerCount,
                tickets.Select(t => t.ReferenceNumber).Distinct(StringComparer.Ordinal).Count());
            Assert.All(tickets, t => Assert.Matches(@"^TKT-\d{6,}$", t.ReferenceNumber));

            // 3. Each distinct reporter appears exactly once in incident_subscription for that incident.
            var subscriptions = await verifyContext.IncidentSubscriptions
                .AsNoTracking()
                .Where(s => s.IncidentId == singleIncident.Id)
                .ToListAsync();

            Assert.Equal(workerCount, subscriptions.Count);
            Assert.Equal(
                reporterIds.OrderBy(id => id),
                subscriptions.Select(s => s.ReporterId).OrderBy(id => id));

            // 4. Exactly 1 initial creation history row (FromStatus = null, ToStatus = NEW) was recorded for the incident.
            var historyRows = await verifyContext.IncidentStatusHistories
                .AsNoTracking()
                .Where(h => h.IncidentId == singleIncident.Id)
                .ToListAsync();

            var creationHistory = Assert.Single(historyRows);
            Assert.Null(creationHistory.FromStatus);
            Assert.Equal(IncidentStatus.New, creationHistory.ToStatus);
        }
        finally
        {
            await CleanupScopeAsync(categoryId, locationId, teamId, reporterIds);
        }
    }

    [Fact]
    public async Task ConcurrentClaim_TwoTechniciansReadUnassignedIncidentSimultaneously_OneSucceedsAndOneGetsConflictNamingAssignee()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        int teamId = 0;
        int categoryId = 0;
        int locationId = 0;
        var userIds = new List<Guid>();

        try
        {
            Guid reporterId;
            Guid techAId;
            Guid techBId;
            long incidentId;

            await using (var setupContext = _fixture.CreateContext())
            {
                var reporter = new AppUser
                {
                    Email = $"claim.rep.{suffix}@example.com",
                    FullName = "Reporter User",
                    PasswordHash = "hashed-password"
                };
                var techA = new AppUser
                {
                    Email = $"claim.techa.{suffix}@example.com",
                    FullName = "Technician A",
                    PasswordHash = "hashed-password"
                };
                var techB = new AppUser
                {
                    Email = $"claim.techb.{suffix}@example.com",
                    FullName = "Technician B",
                    PasswordHash = "hashed-password"
                };
                var team = new ServiceTeam { Name = $"ClaimTeam-{suffix}" };

                setupContext.AppUsers.AddRange(reporter, techA, techB);
                setupContext.ServiceTeams.Add(team);
                await setupContext.SaveChangesAsync();

                reporterId = reporter.Id;
                techAId = techA.Id;
                techBId = techB.Id;
                teamId = team.Id;
                userIds.AddRange([reporterId, techAId, techBId]);

                var techTeamRoleId = TeamRoleConfiguration.BaselineTeamRoles.Single(r => r.Name == "technician").Id;
                setupContext.TeamMembers.AddRange(
                    new TeamMember { UserId = techAId, ServiceTeamId = teamId, TeamRoleId = techTeamRoleId },
                    new TeamMember { UserId = techBId, ServiceTeamId = teamId, TeamRoleId = techTeamRoleId });

                var category = new Category
                {
                    Name = $"ClaimCat-{suffix}",
                    Description = "Claim concurrency test category",
                    UrgencyLevel = 2,
                    DefaultServiceTeamId = teamId,
                    IsActive = true
                };
                var location = new Location
                {
                    Name = $"ClaimLoc-{suffix}",
                    LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
                    ImpactLevel = 2,
                    IsActive = true
                };

                setupContext.Categories.Add(category);
                setupContext.Locations.Add(location);
                await setupContext.SaveChangesAsync();

                categoryId = category.Id;
                locationId = location.Id;

                var submissionService = new SubmissionService(setupContext, new PriorityMatrix());
                var created = await submissionService.SubmitAsync(new SubmitTicketRequestDto
                {
                    Title = "Projector not powering on",
                    Description = "Lecture room projector fails to start",
                    CategoryId = categoryId,
                    LocationId = locationId,
                    ReporterId = reporterId
                });
                incidentId = created.Id;
            }

            await using var contextA = _fixture.CreateContext();
            await using var contextB = _fixture.CreateContext();

            // Both technicians read the same unassigned incident at Version = 1 into their respective DbContexts before either saves.
            var readByA = await contextA.Incidents
                .Include(i => i.Assignee)
                .Include(i => i.Category)
                .SingleAsync(i => i.Id == incidentId);
            var readByB = await contextB.Incidents
                .Include(i => i.Assignee)
                .Include(i => i.Category)
                .SingleAsync(i => i.Id == incidentId);

            Assert.Null(readByA.AssigneeId);
            Assert.Null(readByB.AssigneeId);
            Assert.Equal(1, readByA.Version);
            Assert.Equal(1, readByB.Version);

            var controllerA = CreateControllerWithUser(contextA, techAId, "technician");
            var controllerB = CreateControllerWithUser(contextB, techBId, "technician");

            var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readyCount = 0;

            var claimTasks = new[]
            {
                Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref readyCount) == 2)
                    {
                        startBarrier.TrySetResult();
                    }
                    await startBarrier.Task;
                    return (TechName: "Technician A", TechId: techAId, Result: await controllerA.Claim(incidentId));
                }),
                Task.Run(async () =>
                {
                    if (Interlocked.Increment(ref readyCount) == 2)
                    {
                        startBarrier.TrySetResult();
                    }
                    await startBarrier.Task;
                    return (TechName: "Technician B", TechId: techBId, Result: await controllerB.Claim(incidentId));
                })
            };

            var outcomes = await Task.WhenAll(claimTasks);

            var winners = outcomes.Where(o => o.Result is OkObjectResult).ToList();
            var losers = outcomes.Where(o => o.Result is ConflictObjectResult).ToList();

            var winner = Assert.Single(winners);
            var loser = Assert.Single(losers);

            var conflictResult = Assert.IsType<ConflictObjectResult>(loser.Result);
            Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode ?? StatusCodes.Status409Conflict);
            Assert.Contains(winner.TechName, conflictResult.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);

            await using var verifyContext = _fixture.CreateContext();
            var persistedIncident = await verifyContext.Incidents
                .AsNoTracking()
                .SingleAsync(i => i.Id == incidentId);

            Assert.Equal(winner.TechId, persistedIncident.AssigneeId);
            Assert.Equal(IncidentStatus.Assigned, persistedIncident.Status);
            Assert.Equal(2, persistedIncident.Version);

            var assignedHistory = await verifyContext.IncidentStatusHistories
                .AsNoTracking()
                .Where(h => h.IncidentId == incidentId && h.ToStatus == IncidentStatus.Assigned)
                .ToListAsync();

            var singleAssignHistory = Assert.Single(assignedHistory);
            Assert.Equal(IncidentStatus.New, singleAssignHistory.FromStatus);
            Assert.Equal(winner.TechId, singleAssignHistory.AssigneeId);
        }
        finally
        {
            await CleanupScopeAsync(categoryId, locationId, teamId, userIds);
        }
    }

    [Fact]
    public async Task StaleVersionOverride_ConcurrentPriorityOverrides_SecondFailsWithConflict()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        int teamId = 0;
        int categoryId = 0;
        int locationId = 0;
        var userIds = new List<Guid>();

        try
        {
            Guid reporterId;
            Guid supervisorAId;
            Guid supervisorBId;
            long incidentId;

            await using (var setupContext = _fixture.CreateContext())
            {
                var reporter = new AppUser
                {
                    Email = $"ovr.rep.{suffix}@example.com",
                    FullName = "Reporter User",
                    PasswordHash = "hashed-password"
                };
                var supervisorA = new AppUser
                {
                    Email = $"ovr.supa.{suffix}@example.com",
                    FullName = "Supervisor A",
                    PasswordHash = "hashed-password"
                };
                var supervisorB = new AppUser
                {
                    Email = $"ovr.supb.{suffix}@example.com",
                    FullName = "Supervisor B",
                    PasswordHash = "hashed-password"
                };
                var team = new ServiceTeam { Name = $"OvrTeam-{suffix}" };

                setupContext.AppUsers.AddRange(reporter, supervisorA, supervisorB);
                setupContext.ServiceTeams.Add(team);
                await setupContext.SaveChangesAsync();

                reporterId = reporter.Id;
                supervisorAId = supervisorA.Id;
                supervisorBId = supervisorB.Id;
                teamId = team.Id;
                userIds.AddRange([reporterId, supervisorAId, supervisorBId]);

                var category = new Category
                {
                    Name = $"OvrCat-{suffix}",
                    Description = "Priority override concurrency test category",
                    UrgencyLevel = 2,
                    DefaultServiceTeamId = teamId,
                    IsActive = true
                };
                var location = new Location
                {
                    Name = $"OvrLoc-{suffix}",
                    LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
                    ImpactLevel = 2,
                    IsActive = true
                };

                setupContext.Categories.Add(category);
                setupContext.Locations.Add(location);
                await setupContext.SaveChangesAsync();

                categoryId = category.Id;
                locationId = location.Id;

                var submissionService = new SubmissionService(setupContext, new PriorityMatrix());
                var created = await submissionService.SubmitAsync(new SubmitTicketRequestDto
                {
                    Title = "Intermittent network drop",
                    Description = "Switch port flapping in room",
                    CategoryId = categoryId,
                    LocationId = locationId,
                    ReporterId = reporterId
                });
                incidentId = created.Id;
            }

            await using var contextA = _fixture.CreateContext();
            await using var contextB = _fixture.CreateContext();

            // Both supervisors load the incident while its Version is 1.
            var loadedByA = await contextA.Incidents.SingleAsync(i => i.Id == incidentId);
            var loadedByB = await contextB.Incidents.SingleAsync(i => i.Id == incidentId);
            Assert.Equal(1, loadedByA.Version);
            Assert.Equal(1, loadedByB.Version);

            var controllerA = CreateControllerWithUser(contextA, supervisorAId, "supervisor");
            var controllerB = CreateControllerWithUser(contextB, supervisorBId, "supervisor");

            // Supervisor A overrides priority to 1 (Critical) with Version = 1 -> succeeds and bumps Version to 2.
            var firstResult = await controllerA.OverridePriority(
                incidentId,
                new OverridePriorityRequestDto
                {
                    NewPriority = PriorityMatrix.PriorityCritical,
                    Reason = "Escalated by Supervisor A due to exam venue impact.",
                    Version = 1
                });

            var okResult = Assert.IsType<OkObjectResult>(firstResult);
            Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode ?? StatusCodes.Status200OK);

            // Supervisor B attempts to override priority to 4 (Low) using stale Version = 1 -> rejected with 409 Conflict.
            var secondResult = await controllerB.OverridePriority(
                incidentId,
                new OverridePriorityRequestDto
                {
                    NewPriority = PriorityMatrix.PriorityLow,
                    Reason = "Stale downgrade attempt by Supervisor B.",
                    Version = 1
                });

            var conflictResult = Assert.IsType<ConflictObjectResult>(secondResult);
            Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode ?? StatusCodes.Status409Conflict);

            // Verify Supervisor A's override remains intact and was not overwritten.
            await using var verifyContext = _fixture.CreateContext();
            var persisted = await verifyContext.Incidents
                .AsNoTracking()
                .SingleAsync(i => i.Id == incidentId);

            Assert.Equal(PriorityMatrix.PriorityCritical, persisted.Priority);
            Assert.Equal(PriorityMatrix.PriorityMedium, persisted.ComputedPriority);
            Assert.True(persisted.PriorityOverridden);
            Assert.Equal("Escalated by Supervisor A due to exam venue impact.", persisted.OverrideReason);
            Assert.Equal(supervisorAId, persisted.OverrideBy);
            Assert.Equal(2, persisted.Version);
        }
        finally
        {
            await CleanupScopeAsync(categoryId, locationId, teamId, userIds);
        }
    }

    [Fact]
    public async Task SubmissionFailure_MidTransactionRollback_LeavesNoOrphanedRecords()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        int teamId = 0;
        int categoryId = 0;
        int locationId = 0;
        var userIds = new List<Guid>();

        try
        {
            Guid reporterId;

            await using (var setupContext = _fixture.CreateContext())
            {
                var reporter = new AppUser
                {
                    Email = $"txn.rep.{suffix}@example.com",
                    FullName = "Transactional Test Reporter",
                    PasswordHash = "hashed-password",
                    IsActive = true
                };
                var team = new ServiceTeam { Name = $"TxnTeam-{suffix}" };

                setupContext.AppUsers.Add(reporter);
                setupContext.ServiceTeams.Add(team);
                await setupContext.SaveChangesAsync();

                reporterId = reporter.Id;
                teamId = team.Id;
                userIds.Add(reporterId);

                var category = new Category
                {
                    Name = $"TxnCat-{suffix}",
                    Description = "Transactional rollback test category",
                    UrgencyLevel = 3,
                    DefaultServiceTeamId = teamId,
                    IsActive = true
                };
                var location = new Location
                {
                    Name = $"TxnLoc-{suffix}",
                    LocationKindId = LocationKindConfiguration.BaselineLocationKinds.Single(k => k.Name == "room").Id,
                    ImpactLevel = 2,
                    IsActive = true
                };

                setupContext.Categories.Add(category);
                setupContext.Locations.Add(location);
                await setupContext.SaveChangesAsync();

                categoryId = category.Id;
                locationId = location.Id;
            }

            long stagedIncidentId = 0;

            // Case 1: Simulated failure after incident row is flushed inside the transaction.
            await using (var failingContext = _fixture.CreateContext())
            {
                var submissionService = new SubmissionService(failingContext, new PriorityMatrix());

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    submissionService.SubmitAsync(
                        new SubmitTicketRequestDto
                        {
                            Title = "Water leak in corridor",
                            Description = "Pipe leaking near lecture hall entrance",
                            CategoryId = categoryId,
                            LocationId = locationId,
                            ReporterId = reporterId,
                            IdempotencyKey = $"txn-fail-{suffix}"
                        },
                        onAfterIncidentStaged: (stagedIncident, _) =>
                        {
                            stagedIncidentId = stagedIncident.Id;
                            Assert.True(stagedIncidentId > 0);
                            throw new InvalidOperationException("Simulated mid-transaction failure after incident insert.");
                        }));
            }

            Assert.True(stagedIncidentId > 0);

            // Case 2: Foreign key violation on non-existent ReporterId after incident staging.
            await using (var fkFailingContext = _fixture.CreateContext())
            {
                var submissionService = new SubmissionService(fkFailingContext, new PriorityMatrix());

                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    submissionService.SubmitAsync(
                        new SubmitTicketRequestDto
                        {
                            Title = "Water leak in corridor (FK failure)",
                            Description = "Unattributable reporter ID triggers FK violation mid-transaction",
                            CategoryId = categoryId,
                            LocationId = locationId,
                            ReporterId = Guid.NewGuid()
                        }));
            }

            // Verify 0 incidents, 0 tickets, 0 status history rows, 0 subscriptions, and 0 idempotency records remain.
            await using var verifyContext = _fixture.CreateContext();

            Assert.Equal(
                0,
                await verifyContext.Incidents.AsNoTracking().CountAsync(i => i.CategoryId == categoryId && i.LocationId == locationId));
            Assert.Equal(
                0,
                await verifyContext.Tickets.AsNoTracking().CountAsync(t => t.CategoryId == categoryId && t.LocationId == locationId));
            Assert.Equal(
                0,
                await verifyContext.IncidentStatusHistories.AsNoTracking().CountAsync(h => h.IncidentId == stagedIncidentId));
            Assert.Equal(
                0,
                await verifyContext.IncidentSubscriptions.AsNoTracking().CountAsync(s => s.IncidentId == stagedIncidentId));
            Assert.Equal(
                0,
                await verifyContext.SubmissionRequests.AsNoTracking().CountAsync(sr => sr.ReporterId == reporterId));
        }
        finally
        {
            await CleanupScopeAsync(categoryId, locationId, teamId, userIds);
        }
    }

    private static IncidentsController CreateControllerWithUser(
        CivicConnectDbContext context,
        Guid userId,
        string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return new IncidentsController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private async Task CleanupScopeAsync(
        int categoryId,
        int locationId,
        int teamId,
        IReadOnlyCollection<Guid> userIds)
    {
        await using var cleanupContext = _fixture.CreateContext();
        await using var tx = await cleanupContext.Database.BeginTransactionAsync();

        await cleanupContext.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = 'replica';");

        if (categoryId > 0)
        {
            await cleanupContext.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM notification WHERE incident_id IN (SELECT id FROM incident WHERE category_id = {categoryId});
                DELETE FROM submission_request WHERE ticket_id IN (SELECT id FROM ticket WHERE category_id = {categoryId});
                DELETE FROM incident_subscription WHERE incident_id IN (SELECT id FROM incident WHERE category_id = {categoryId});
                DELETE FROM incident_status_history WHERE incident_id IN (SELECT id FROM incident WHERE category_id = {categoryId});
                DELETE FROM ticket WHERE category_id = {categoryId};
                DELETE FROM incident WHERE category_id = {categoryId};
                DELETE FROM category WHERE id = {categoryId};
            ");
        }

        if (locationId > 0)
        {
            await cleanupContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM location WHERE id = {locationId};");
        }

        if (teamId > 0)
        {
            await cleanupContext.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM team_member WHERE service_team_id = {teamId};
                DELETE FROM service_team WHERE id = {teamId};
            ");
        }

        foreach (var userId in userIds)
        {
            await cleanupContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM app_user WHERE id = {userId};");
        }

        await tx.CommitAsync();
    }
}
