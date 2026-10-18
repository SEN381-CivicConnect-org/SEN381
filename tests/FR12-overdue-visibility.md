# FR-12 overdue visibility regression tests

Issue #116 verifies source requirement #33 and implementation PR #90 against
`dev` commit `9870ae3`.

Inspected implementation:

- `src/CivicConnect.Data/Entities/Incident.cs`: non-nullable `DateTimeOffset DueAt`.
- `src/CivicConnect.Data/Configurations/IncidentConfiguration.cs`: database-generated due date.
- `src/CivicConnect.Data/Configurations/PriorityTargetConfiguration.cs`: resolution targets in minutes.
- `src/CivicConnect.Data/Migrations/20261008090954_AddIncidentCore.cs`: NOT NULL `due_at`; insert and priority-update triggers.
- `src/CivicConnect.Web/Controllers/IncidentsController.cs`: overdue route, authorization metadata, active-status filter, DTO projection and ordering; staff queue also exposes a due-date flag.
- `src/CivicConnect.Web/Program.cs`: controller registration and mapping.
- `src/CivicConnect.Web/Components/Pages/Management/AllTickets.razor` and `Dashboard.razor`: current management UI.
- `src/CivicConnect.Web/Models/Ticket.cs` and `Services/TicketQueueFilter.cs`: demo tickets have no due-date field or overdue filter.
- `src/CivicConnect.Api/Program.cs`: incident responses surface the stored due date; the FR-12 overdue route lives in the Web controller.

## Verified rules

`GET /api/incidents/overdue` includes New, Assigned, InProgress and OnHold
incidents only when `DueAt < now`. Equality and future timestamps are excluded.
Resolved, Closed, Rejected and Merged incidents are excluded even when their due
dates have passed. Results are ordered by due date ascending and expose the
stored due date, `IsOverdue = true`, elapsed overrun and priority override details.
Authorization metadata permits the existing supervisor, manager, administrator
and Management role spellings.

PostgreSQL generates `due_at = opened_at + effective priority target minutes` on
insert and recalculates it when priority changes. An override remains anchored to
the original opening time. Missing dates are unsupported: the model is
non-nullable and the database rejects setting `due_at` to NULL. No sentinel-date
rule or new status rule has been introduced.

## Tests and execution

- `CivicConnect.Tests/OverdueIncidentTests.cs`: 19 fixed-clock xUnit cases using
  EF Core InMemory to exercise the actual controller query and DTO projection.
- `CivicConnect.Data.Tests/IncidentDueAtTests.cs`: 6 PostgreSQL integration cases
  using the existing Database collection, migrations and rollback transactions.
  These cover generation, overrides, missing dates, the management query's
  boundary at microsecond precision and ordering/filtering.

The controller accepts an optional `TimeProvider`, defaulting to
`TimeProvider.System`. Only the FR-12 action uses this test seam. Tests use fixed
UTC instants; the in-memory tests also compare equivalent non-zero-offset instants.

Run `dotnet build`, then `dotnet test --no-build` with `CONNECTION_STRING`
pointing to a disposable PostgreSQL database, as required by the existing fixture.
The fixture applies migrations. For only the database-independent tests, run
`dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj`.

Validation: .NET 10 SDK, PostgreSQL 16, build passed with zero warnings/errors;
62 solution tests passed (45 unit, 17 database), zero failures or skips.

## Existing gaps

The management Blazor pages still use demo `TicketService` data and do not consume
the overdue endpoint or render due-date/overrun fields. This testing change
verifies the existing management query rather than adding UI functionality.
Authorization is checked through endpoint metadata; full HTTP authentication and
browser rendering are outside these tests. The current target calculation uses
elapsed minutes, without a working-day calendar; tests preserve that behavior.
