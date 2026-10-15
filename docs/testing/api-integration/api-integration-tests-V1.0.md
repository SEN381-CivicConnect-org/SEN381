# API / Integration Tests — What Was Done

Scope: `tests/CivicConnect.Data.Tests/` (xUnit, .NET 10). Every test in this project
shares one `[Collection("Database")]` fixture (`CivicConnectDatabaseFixture`) that runs
the real EF Core migrations against a real PostgreSQL instance before any test runs, then
issues real SQL through the Npgsql/EF Core stack for every test — there is no mocked
`DbContext` and no in-memory provider. Three of the six classes go one layer further and
call the ASP.NET Core API controllers directly (`IncidentsController`,
`ResolutionsController`), so those exercise the controller → service → database path as
a whole. This is the project's API/integration test evidence for SEN381 Milestone 3,
Assessment Area C/D.

## Environment

- Database: the project's own `postgres:15-alpine` container (`civicconnect-db`,
  `docker compose up -d db`), migrated with `CivicConnectDbContext.MigrateAsync()` from
  the fixture's `InitializeAsync()`.
- Connection string: read from the `CONNECTION_STRING` environment variable (falling
  back to the repo's `.env`), per `README.md`.
- Test runner: `dotnet test tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj`.
- Last executed: 2026-10-15, against the running local container →
  **32 passed, 0 failed, 0 skipped** (32 total, 3 s, `net10.0`).

## `ClaimAndAssignOwnershipTests.cs` — FR-04, FR-07, NFR-06

Calls `IncidentsController.Claim` / `.Assign` directly, with a `ClaimsPrincipal` built to
simulate an authenticated technician or supervisor, against the real database.

- `Claim_UnassignedIncident_SetsAssigneeTransitionsToAssignedAndAppendsHistory` — claiming
  an unassigned incident sets the assignee and team, transitions `NEW → ASSIGNED`,
  increments the optimistic-concurrency `Version`, appends exactly the expected
  `incident_status_history` rows, and raises one assignment notification to the reporter.
- `Claim_AlreadyClaimedIncident_ReturnsConflictNamingCurrentAssignee` — a second
  technician's claim on an already-claimed incident is rejected with `409 Conflict`
  naming the current assignee, and the incident's assignee is unchanged.
- `Assign_SupervisorAssignsTechnician_RecordsOwnerAndAuditHistory` — a supervisor
  assigning a named technician records the owner, team, status transition and the
  supervisor's note on the history row.

## `ConcurrencyTests.cs` — NFR-06 (Concurrency Correctness Under Simultaneous Submission)

Four tests that genuinely run concurrent work against the live database — each spins up
multiple `Task.Run` workers synchronised on a shared `TaskCompletionSource` barrier so the
workers race against the database rather than against each other's in-process state.

- `ConcurrentSubmissions_TwentySimultaneousRequests_ProduceSingleIncidentTwentyTicketsAndTwentySubscribers`
  (IT-CONC-01) — 20 reporters submit the same category/location simultaneously through
  `SubmissionService`; asserts exactly one incident is created, with 20 tickets and 20
  distinct subscriptions.
- `ConcurrentClaim_TwoTechniciansReadUnassignedIncidentSimultaneously_OneSucceedsAndOneGetsConflictNamingAssignee`
  (IT-CLM-02) — two technicians claim the same incident at the same instant; asserts
  exactly one `200 OK` and one `409 Conflict` naming the winner.
- `StaleVersionOverride_ConcurrentPriorityOverrides_SecondFailsWithConflict` (IT-CONC-02) —
  two priority overrides race on the same incident's optimistic-concurrency `Version`;
  the stale write is rejected with `409 Conflict` and does not overwrite the winning
  change.
- `SubmissionFailure_MidTransactionRollback_LeavesNoOrphanedRecords` (IT-TXN-01) — a
  forced failure partway through a submission transaction is asserted to roll back
  completely, leaving no orphaned ticket/incident/subscription rows.

## `ResolutionTests.cs` — FR-10, IT-RES-01/02

- `IT_RES_01_ReplacementWithoutAssetRejected` — resolving an incident with a
  replacement-implying resolution code (`HARDWARE_REPLACED`) but no asset reference is
  rejected; the incident's status is unchanged.
- `IT_RES_02_ReopenSupersedesResolution` — re-opening a resolved incident supersedes
  (not deletes) the prior `resolution` row and allows a new current resolution to be
  recorded.
- `IncidentsController_EnforcesReplacementRule_StatusTransition_AndFinanceReport` — an
  end-to-end pass through the controller: a valid replacement resolution with an asset
  reference is accepted, the incident moves to `RESOLVED`, and the resolution appears
  correctly in the finance asset-replacement query (AP-10) against the real asset and
  supplier rows.

## `IdentityTests.cs` — Identity, Roles and Team Membership

Direct `DbContext` tests (no controller) against the real schema and seeded
`role`/`team_role` reference data.

- `CreateUser_StoresEmailFullNameAndPasswordHash`
- `GrantRole_RecordsGrantedByAndGrantedAt`
- `AddUserToTeam_CreatesActiveMembership`
- `RemoveUserFromTeam_SetsLeftAtAndKeepsRow` — confirms team departure is a soft update
  (`LeftAt` set) and the membership row is retained, not deleted.
- `ReadEffectiveRoles_ReturnsAllGrantedRoles` — a user granted two roles reads back both.

## `PasswordStorageTests.cs` — NFR-11, IT-SEC-03

- `CreateUserAccount_StoresArgon2idPhcStringWithDocumentedParameters_AndNeverStoresOrLogsPlaintext`
  / `IT_SEC_03_StoredHashIsArgon2idWithParams` (same scenario, exposed under both names) —
  a created account's stored `password_hash` matches the documented PHC format
  (`$argon2id$v=19$m=19456,t=2,p=1$...`), verifies correctly for the right password and
  fails for a wrong one, and the plaintext password appears in neither the stored row nor
  any log line captured from a test `ILogger`.
- `VerifyPassword_RejectsPlaintextOrNonArgon2idPasswordHash` (`[Theory]`, 5 cases) —
  negative testing: plaintext, a legacy PBKDF2-style hash, a bcrypt hash, and an
  Argon2**i** (not Argon2**id**) hash are all rejected as invalid PHC input.
- `VerifyPassword_DetectsOutdatedParametersForSeamlessUpgrade` — a hash stored with older,
  weaker parameters (`m=4096,t=1,p=1`) still verifies correctly but signals
  `needsRehash = true`; re-hashing on login upgrades it in place to the current
  parameters without a password reset.

## `ServiceCatalogueTests.cs` — FR-09 (Configuration Deactivation Preserves History)

Nine tests against the real `category`/`incident` schema and constraints:

- `CreateCategory_StoresNameDescriptionUrgencyAndDefaultTeam`
- `CreateCategory_WithNonexistentDefaultTeam_IsRejected` — foreign-key enforcement.
- `CategoryName_UniqueOnlyAmongActiveCategories` — the partial unique index allows a
  reused name once the original is deactivated.
- `PriorityTargets_AreSeededForEveryLevelWithAResolveWithinMinutesValue`
- `DeactivateCategory_SetsIsActiveFalse_ReferencingIncidentUntouched`
- `InactiveCategory_CannotBeSelectedForANewIncident_ButStillResolvesForAnExistingOne`
- `RemovingCategory_ThroughEntityFramework_SoftDeactivatesRatherThanDeleting`
- `DeleteCategory_ReferencedByAnIncident_IsRefusedByTheDatabase` — the database-level
  `ON DELETE RESTRICT` rejects the delete, not just application logic.
- `ReactivateCategory_AllowsItToBeSelectedAgain`

## Traceability

| RTM ID | Requirement | Covered by |
| --- | --- | --- |
| IT-CLM-01 / IT-CLM-02 | FR-04 claim/assign, NFR-06 double-claim | `ClaimAndAssignOwnershipTests`, `ConcurrencyTests` |
| IT-CONC-01 / IT-CONC-02 / IT-TXN-01 | NFR-06 concurrency correctness | `ConcurrencyTests` |
| IT-RES-01 / IT-RES-02 | FR-10 asset linkage on resolution | `ResolutionTests` |
| IT-SEC-03 | NFR-11 password storage | `PasswordStorageTests` |
| IT-CFG-01 / IT-CFG-02 | FR-09 configuration deactivation | `ServiceCatalogueTests` |

See `docs/testing/unit-component/` for the tests that run with no database or network
dependency.
