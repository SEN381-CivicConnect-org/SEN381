# API / Integration Tests — What Was Done (V2.0 — Milestone 4 Baseline)

Scope: `tests/CivicConnect.Data.Tests/` (xUnit, .NET 10). Every test in this project
shares one `[Collection("Database")]` fixture (`CivicConnectDatabaseFixture`) that runs
the real EF Core migrations against an isolated PostgreSQL instance before any test runs, then
issues real SQL through the Npgsql/EF Core stack for every test — there is no mocked
`DbContext` and no in-memory provider. Multiple test classes call the ASP.NET Core API controllers
directly (`IncidentsController`, `ResolutionsController`, `NotificationsController`, `ManagementController`),
exercising the full controller → service → PostgreSQL database path. This is the project's
API/integration test evidence for SEN381 Milestone 3 & Milestone 4, Assessment Area C/D.

## Environment

- Database: Project `postgres:15-alpine` container (`civicconnect-db`), migrated with `CivicConnectDbContext.MigrateAsync()` from the fixture's `InitializeAsync()`.
- Connection string: Read from `CONNECTION_STRING` environment variable (falling back to `.env`).
- Test runner: `dotnet test tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj`.
- Current Status: **56 passed, 0 failed, 0 skipped** (56 total, ~7.0 s, `net10.0`). Compiles cleanly with 0 warnings/errors.

---

## Comprehensive Test Class Inventory

### 1. `SubmissionServiceTests.cs` — FR-01, IT-SUB-01
- `IT_SUB_01_SubmitCreatesTicketAndIncident` — Submitting a valid request creates a new ticket (`TKT-xxxxxx`) and incident (`INC-xxxxxx`) within an atomic transaction. Verifies title, description, category, and location foreign keys.
- `SubmitRequest_SelectedCategoryIsPersistedOnTheGeneratedTicket` — Category linkage persistence.
- `SubmitRequest_MatchingOpenCategoryAndLocation_ClustersUnderExistingIncidentAndSubscribesReporter` — Clustering verification.

### 2. `RtmScopeCompletionTests.cs` — FR-17, FR-13, FR-08, NFR-04, NFR-10
Dedicated RTM scope completion integration suite:
- `IT_REP_01_ManagementReporting_StatusCounts_Overdue_CategoryTeamBreakdowns_AndTurnaround` (FR-17, FR-12) — Verifies open status counts, overdue incident list (`due_at < now`), category resolution breakdown, team resolution breakdown, and SLA turnaround metrics.
- `IT_NOTIF_01_SubscribedReporterReceivesResolutionNotificationWithSummary` (FR-13) — Subscribed reporter receives fan-out notification on assignment, status change, and resolution (with resolution summary). Verifies `dedupe_key` and mark-as-read updates.
- `IT_AUD_01_AuditLogRecordsChangesAndEnforcesAppendOnlyImmutability` (FR-08) — Database trigger logs priority overrides into `audit_log`; confirms PostgreSQL trigger blocks `UPDATE` and `DELETE` on `audit_log`.
- `IT_SEC_01_PersonalInformationProtection_StaffDirectoryAndCivicReportRole` (NFR-04) — Confirms keyless `staff_directory` view omits `email` and `password_hash`. Verifies `civic_report` database role can read `staff_directory` and `incident`, but is denied on `app_user`, `ticket`, `audit_log`, `incident_note`, and `notification`.
- `IT_SEC_02_LeastPrivilegeDatabaseRoles_CivicAppCannotDeleteOrAlterSchemaOrMutateAuditLog` (NFR-10) — Confirms runtime `civic_app` role cannot create tables (DDL), cannot delete incidents/users, and cannot mutate audit logs. Proves `civic_migrate` retains DDL privileges.

### 3. `RetentionAndDisposalTests.cs` — NFR-05, IT-RET-01, IT-RET-02
- `IT_RET_01_RetentionSweep` — Purges expired idempotency records (>7 days) and aged read notifications (>12 months) while preserving active idempotency tokens and unread/recent notifications.
- `IT_RET_02_DeIdentifyKeepsIncidents` — De-identifies personal information (anonymizing email, clearing name and credentials) under POPIA s14(1) while retaining historical incident integrity.

### 4. `IncidentDueAtTests.cs` — FR-12, IT-SLA-01, IT-SLA-02
- `ManagementQuery_UsesStrictUtcBoundaryInPostgres` (`IT-SLA-01`) — Strict UTC boundary evaluation in PostgreSQL.
- `ManagementQuery_FiltersFutureIncidentsAndOrdersDueDatesInPostgres` (`IT-SLA-01`) — Future incidents excluded; active overdue incidents sorted by urgency.
- `DueAt_IsGeneratedOnInsertAndOverrideRemainsAnchoredToOriginalOpening` (`IT-SLA-02`) — Database trigger computes `due_at` on insert and recomputes based on original `opened_at` + target when priority is overridden.
- `MissingDueDate_IsRejectedByPostgres` — Confirms database `NOT NULL` constraint on `due_at`.

### 5. `ConcurrencyTests.cs` — NFR-06, IT-CONC-01, IT-CLM-02, IT-CONC-02, IT-TXN-01
Four high-concurrency races across parallel `Task.Run` worker threads:
- `ConcurrentSubmissions_TwentySimultaneousRequests_ProduceSingleIncidentTwentyTicketsAndTwentySubscribers` (`IT-CONC-01`, FR-03).
- `SimultaneousClaims_TwoTechnicians_ProduceOneSuccessAndOneConflictWithAssigneeName` (`IT-CLM-02`, FR-04).
- `ConcurrentPriorityOverrides_StaleVersion_ProducesConflictAndPreservesWinner` (`IT-CONC-02`, FR-06).
- `MidOperationFailure_DuringSubmission_RollsBackAllChangesLeavingNoOrphans` (`IT-TXN-01`, NFR-06).

### 6. `ClaimAndAssignOwnershipTests.cs` — FR-04, FR-07
- `Claim_UnassignedIncident_SetsAssigneeTransitionsToAssignedAndAppendsHistory` (`IT-CLM-01`).
- `Claim_AlreadyClaimedIncident_ReturnsConflictNamingCurrentAssignee` (`IT-CLM-02`).
- `Assign_BySupervisor_SetsAssigneeAndTeamAndAppendsHistory`.

### 7. `ResolutionTests.cs` — FR-10, IT-RES-01, IT-RES-02
- `IT_RES_01_ReplacementWithoutAssetRejected` — Database constraint `resolution_replacement_needs_asset_ck` rejects replacement code without linked asset.
- `IT_RES_02_ReopenSupersedesResolution` — Reopening a resolved incident sets `superseded_at` on previous resolution.

### 8. `ServiceCatalogueTests.cs` — FR-09, IT-CFG-01, IT-CFG-02
- `DeactivateCategory_SetsIsActiveFalse_ReferencingIncidentUntouched` (`IT-CFG-01`).
- `DeleteCategory_ReferencedByAnIncident_IsRefusedByTheDatabase` (`IT-CFG-02`) — Confirms `ON DELETE RESTRICT`.
- `CategoryName_UniqueOnlyAmongActiveCategories` — Filtered partial unique index `category_active_name_uq`.

### 9. `PasswordStorageTests.cs` — NFR-11, IT-SEC-03
- `CreateUserAccount_StoresArgon2idPhcStringWithDocumentedParameters_AndNeverStoresOrLogsPlaintext` (`IT-SEC-03`).
- `VerifyPassword_RejectsPlaintextOrNonArgon2idPasswordHash` (Theory across 5 invalid formats).
- Parameter upgrade detection.

### 10. `IdentityTests.cs` & `LocationHierarchyTests.cs` — Identity & Configuration
- User creation, role assignment, and active team membership tracking.
- Location tree ancestry path recursion (`WITH RECURSIVE`) and cycle prevention trigger.

---

## RTM Traceability Summary

| RTM ID | Requirement | Implementing Class / Test | Result | Status |
|---|---|---|---|:---:|
| `IT-SUB-01` | FR-01 Submit Service Request | `SubmissionServiceTests` | 3 Passed | **Met** |
| `IT-CONC-01` | FR-03 Incident Clustering | `ConcurrencyTests` | 1 Passed | **Met** |
| `IT-CLM-01` / `02` | FR-04 Claim / Assign Request | `ClaimAndAssignOwnershipTests`, `ConcurrencyTests` | 3 Passed | **Met** |
| `IT-CONC-02` | FR-06 Stale Version Override | `ConcurrencyTests` | 1 Passed | **Met** |
| `IT-AUD-01` | FR-08 Append-Only Audit Trail | `RtmScopeCompletionTests` | 1 Passed | **Met** |
| `IT-CFG-01` / `02` | FR-09 Category Deactivation & History | `ServiceCatalogueTests` | 7 Passed | **Met** |
| `IT-RES-01` / `02` | FR-10 Asset Linkage on Resolution | `ResolutionTests` | 4 Passed | **Met** |
| `IT-SLA-01` / `02` | FR-12 Overdue Incident & Recompute | `IncidentDueAtTests`, `RtmScopeCompletionTests` | 5 Passed | **Met** |
| `IT-NOTIF-01` | FR-13 Automated Notifications | `RtmScopeCompletionTests` | 1 Passed | **Met** |
| `IT-REP-01` | FR-17 Management Reporting | `RtmScopeCompletionTests` | 1 Passed | **Met** |
| `IT-SEC-01` | NFR-04 POPIA Security View & Roles | `RtmScopeCompletionTests` | 1 Passed | **Met** |
| `IT-RET-01` / `02` | NFR-05 Data Retention & Disposal | `RetentionAndDisposalTests` | 4 Passed | **Met** |
| `IT-TXN-01` | NFR-06 Transaction Rollback | `ConcurrencyTests` | 1 Passed | **Met** |
| `IT-SEC-02` | NFR-10 Least Privilege Database Roles | `RtmScopeCompletionTests` | 1 Passed | **Met** |
| `IT-SEC-03` | NFR-11 Argon2id Password Storage | `PasswordStorageTests` | 4 Passed | **Met** |

See `docs/testing/unit-component/unit-component-tests-V2.0.md` for in-process unit, regression, and component test evidence.
