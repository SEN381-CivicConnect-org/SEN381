# Unit / Component Tests — What Was Done (V2.0 — Milestone 4 Baseline)

Scope: `tests/CivicConnect.Tests/` (xUnit, .NET 10). These tests run fully in-process —
no database connection, no network call, and (with one noted exception) no filesystem
access — against the application's own classes. They are the project's unit/component
test evidence for SEN381 Milestone 3 & Milestone 4, Assessment Area C/D.

## Environment

- Test runner: xUnit via `dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj`.
- No `CONNECTION_STRING` or other external dependency is required.
- Current Status: **177 passed, 0 failed, 1 skipped** (178 total, ~7.0 s, `net10.0`). Compiles cleanly with 0 warnings/errors (`-warnaserror`).
- Note on single skip: `FR12-BB-03 — No due date` is intentionally skipped because PostgreSQL enforces `due_at` as a `NOT NULL` value-generated column at the database schema level.

## Test Inventory by Requirement

### 1. `PriorityMatrixTests.cs` — FR-05, UT-PRI-01
Pure unit tests of `PriorityMatrix`/`IPriorityMatrix` (`src/CivicConnect.Web/Services`), the impact × urgency → priority calculation behind FR-05 (DD-02).
- `UT_PRI_01_ElectricalUrgencyCritical_GroundFloorImpactMedium_CalculatesPriorityCritical` — Worked example from FR-05 criterion (Urgency 4 + Impact 2 → Priority 1 "Critical").
- `UT_PRI_01_PriorityMatrix_CalculatesExpectedPriorityTargetId` (`[Theory]`, 15 cases) — Equivalence-partitioning/decision-table coverage across all impact (1–4) × urgency (1–4) combinations.
- `UT_PRI_01_PriorityMatrix_OutOfRangeInputs_ThrowsArgumentOutOfRangeException` (`[Theory]`, 6 cases) — Boundary value analysis at 1–4 range.
- `Incident_SupervisorOverride_UpdatesEffectivePriorityWhilePreservingComputedPriority` — Verifies setting effective priority leaves computed priority unchanged.

### 2. `TicketSnapshotTests.cs` — FR-05, IT-PRI-02
Exercises in-memory snapshot immutability on `TicketService`.
- `TicketCreation_SnapshotsImpactUrgencyAndCalculatedPriority` — Stores impact level, urgency level, and computed priority at submission.
- `IT_PRI_02_ConfigChangeDoesNotRewriteHistory_OnIncidentAndTicket` — Verifies mutating live category/location configuration preserves immutable priority snapshots on historical incidents.

### 3. `SystemSecretsConfigurationTests.cs` — NFR-01, IT-CFG-01
Validates startup configuration and secret loading (`src/CivicConnect.Data/Configuration/SystemSecretsConfiguration.cs`).
- `Startup_LoadsConnectionStringAndJwtSecretFromEnvironmentVariables` — Happy path loading from environment variables.
- `Startup_FailsWhenConnectionStringOrJwtSecretMissingFromEnvironment` (`[Theory]`, 11 cases) — Rejects missing, whitespace, and placeholder secrets.
- `Startup_FailsIfSecretsHardcodedInAppsettingsJson` (`[Theory]`, 4 cases) — Rejects simulated hardcoded secrets with explicit `IT-CFG-01` validation.
- `RepositoryConfigFiles_DoNotContainHardcodedCredentials` — Verifies all repo `appsettings*.json` files contain zero hardcoded secrets.

### 4. `RequesterOwnershipTests.cs` & `RequestVisibilityEquivalencePartitionTests.cs` — FR-11
Comprehensive equivalence partitioning and boundary tests enforcing requester request isolation (resolving defects D1 & D2):
- `LegacyFixturesAreFilteredBeforeReturningToRequester` & `BroaderRolesRetainAllTicketAccess`.
- 10 Equivalence Partitioning tests (`EP-01` to `EP-10`) confirming Requester A cannot view or list Requester B's tickets, while Staff and Management retain authorized operational oversight.

### 5. `TicketWorkflowRegressionTests.cs` — FR-11, FR-16, FR-07, FR-08, FR-05, FR-14, FR-15
Ten comprehensive cross-feature regression scenarios:
- `REG-01` to `REG-04`: Note and status history rendering on detail views with strict ownership and internal-note withholding.
- `REG-05` & `REG-06`: Staff/Management note visibility.
- `REG-07`: Full end-to-end lifecycle (`Open` to `Closed`) with 2 notes and 7 PII-free audit records.
- `REG-08` & `REG-09`: Rejection of unauthorized mutations by Requester and Anonymous actors.
- `REG-10`: Staff queue active inclusion and exclusion once resolved or closed.

### 6. `TicketQueueFilterAndManagementTests.cs` — FR-15, FR-09, FR-12, FR-17
Tests queue search, filtering, and sorting:
- `FR15_TicketQueueFilter_SearchByKeyword_MatchesTitleAndDescription`.
- `FR15_TicketQueueFilter_FiltersByCategoryAndStatus`.
- `FR15_TicketQueueFilter_SortsByPriorityHighAndLow` & `SortsByDateNewestAndOldest`.
- `FR09_WorkspaceDataService_CreateAndDeactivateCategoryPreservesHistory`.
- `FR12_FR17_WorkspaceDataService_DashboardSummaryAggregatesMetrics`.

### 7. `OverdueIncidentTests.cs` — FR-12
Black-box boundary value analysis and equivalence partitioning on SLA overruns:
- Strict UTC boundary evaluation (`DueAt_UsesStrictUtcBoundary`).
- Active status filtering (`OverdueList_IncludesOnlyExistingActiveStatuses`).
- Management list sorting by due date (`ManagementList_FiltersAndSortsByDueAtAndSurfacesIncidentDetails`).

### 8. `RequestStatusStateTransitionTests.cs` — FR-07
- `ST_ForwardLifecycle_StaffAction_TransitionsToDocumentedNextState`.
- `ST_AcceptedTransition_ReadAgain_RetainsNextState`.
- `ST_OpenRequest_DirectResolveAction_IsRejectedAndRemainsOpen` (Illegal transition rejection without state corruption).
- `ST_OpenRequest_RestrictedActorAssignAction_IsRejectedAndRemainsOpen`.

### 9. `AuthorizationRecoveryTests.cs`, `RoleDelegationTests.cs` & `UserRegistrationTests.cs` — FR-02, FR-14
- Protected route challenges and return URL redirects (`DenialNeverExecutesProtectedEndpoint`).
- Staff queue policy enforcement (`QueueRetainsItsActualStaffOnlyPolicy`).
- Hierarchical role assignment rules and user registration validation.

---

## Traceability Summary

| RTM ID | Requirement | Primary Test Class / Methods | Result | Status |
|---|---|---|---|:---:|
| `UT-PRI-01` | FR-05 Priority Calculation | `PriorityMatrixTests` (17 tests) | 17 Passed | **Met** |
| `IT-PRI-02` | FR-05 Priority Snapshot Immutability | `TicketSnapshotTests` (2 tests) | 2 Passed | **Met** |
| `IT-CFG-01` | NFR-01 Protection of System Secrets | `SystemSecretsConfigurationTests` (17 tests) | 17 Passed | **Met** |
| `IT-VIS-01` / `02` | FR-11 Reporter Visibility of Own Requests | `RequestVisibilityEquivalencePartitionTests` & `RequesterOwnershipTests` | 18 Passed | **Met** |
| `UT-SM-01` / `IT-SM-02` | FR-07 Controlled Status Transitions | `RequestStatusStateTransitionTests` | 10 Passed | **Met** |
| `IT-SRCH-01` | FR-15 Search, Filter, Sort Requests | `TicketQueueFilterAndManagementTests` | 6 Passed | **Met** |
| `IT-NOTE-01` | FR-16 Action & Note Recording | `TicketWorkflowRegressionTests` (`REG-01..08`) | 10 Passed | **Met** |
| `IT-SLA-01` / `02` | FR-12 Overdue Incident Visibility | `OverdueIncidentTests` (8 tests) | 7 Passed, 1 Skipped | **Met** |
| `IT-QUEUE-01` | FR-14 Staff Queue Isolation | `AuthorizationRecoveryTests` & `TicketWorkflowRegressionTests` | 5 Passed | **Met** |
| `IT-AUTH-01` | FR-02 Role-Based Portal Access | `AuthorizationRecoveryTests`, `RoleDelegationTests`, `UserRegistrationTests` | 32 Passed | **Met** |

See `docs/testing/api-integration/api-integration-tests-V2.0.md` for PostgreSQL database integration test evidence.
