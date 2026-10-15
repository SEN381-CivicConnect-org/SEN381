# Unit / Component Tests — What Was Done

Scope: `tests/CivicConnect.Tests/` (xUnit, .NET 10). These tests run fully in-process —
no database connection, no network call, and (with one noted exception) no filesystem
access — against the application's own classes. They are the project's unit/component
test evidence for SEN381 Milestone 3, Assessment Area C/D.

## Environment

- Test runner: xUnit via `dotnet test tests/CivicConnect.Tests/CivicConnect.Tests.csproj`.
- No `CONNECTION_STRING` or other external dependency is required.
- Last executed: 2026-10-15, `dotnet test --nologo` → **43 passed, 0 failed, 0 skipped**
  (43 total, 110 ms, `net10.0`).

## `PriorityMatrixTests.cs` — FR-05, UT-PRI-01

Pure unit tests of `PriorityMatrix`/`IPriorityMatrix` (`src/CivicConnect.Web/Services`),
the impact × urgency → priority calculation behind FR-05.

- `UT_PRI_01_ElectricalUrgencyCritical_GroundFloorImpactMedium_CalculatesPriorityCritical` —
  single worked example lifted directly from the FR-05 acceptance criterion
  (Category "Electrical" urgency 4 + Location "Ground Floor" impact 2 → priority 1 "Critical").
- `UT_PRI_01_PriorityMatrix_CalculatesExpectedPriorityTargetId` (`[Theory]`, 15 cases) —
  equivalence-partitioning/decision-table coverage of every impact (1–4) × urgency (1–4)
  combination that maps to each of the four `PriorityTarget` rows, cross-checked against
  the seeded `PriorityTargetConfiguration.BaselinePriorityTargets` data so the test fails
  if the seed data and the matrix ever disagree.
- `UT_PRI_01_PriorityMatrix_OutOfRangeInputs_ThrowsArgumentOutOfRangeException`
  (`[Theory]`, 6 cases) — boundary-value analysis at the matrix's valid 1–4 range
  (`0`, `-1`, `5` on each axis) asserting the out-of-range guard throws.
- `Incident_SupervisorOverride_UpdatesEffectivePriorityWhilePreservingComputedPriority` —
  FR-05/FR-06 invariant: setting `Incident.Priority` via a supervisor override leaves
  `Incident.ComputedPriority` untouched.

## `TicketSnapshotTests.cs` — FR-05, IT-PRI-02

Despite the `IT-` RTM prefix (integration test), this class exercises `TicketService`
entirely in memory with no database — it is unit/component-level and is kept in this
suite for that reason.

- `TicketCreation_SnapshotsImpactUrgencyAndCalculatedPriority` — a created ticket stores
  its impact level, urgency level and computed priority at submission time.
- `IT_PRI_02_ConfigChangeDoesNotRewriteHistory_OnIncidentAndTicket` — creates an
  incident/ticket under one category/location configuration, then mutates the
  `Category.UrgencyLevel` and `Location.ImpactLevel` objects (including the caller's own
  input object, to rule out a hidden live reference), and asserts the already-created
  ticket/incident keep their original computed priority while a newly created one
  reflects the new configuration.

## `SystemSecretsConfigurationTests.cs` — NFR-01, IT-CFG-01

Tests of `SystemSecretsConfiguration.LoadAndValidate` (`src/CivicConnect.Data/Configuration`),
which loads the DB connection string and JWT secret from environment variables at startup
and refuses to start if either is missing, blank, a placeholder, or hardcoded.

- `Startup_LoadsConnectionStringAndJwtSecretFromEnvironmentVariables` — happy path: both
  secrets are read from environment variables over an `appsettings.json` with empty
  placeholders.
- `Startup_FailsWhenConnectionStringOrJwtSecretMissingFromEnvironment` (`[Theory]`,
  11 cases) — negative testing: `null`/empty/whitespace/default-password/`${...}`-style
  placeholder/too-short values for either secret all throw `InvalidOperationException`.
- `Startup_FailsIfSecretsHardcodedInAppsettingsJson` (`[Theory]`, 4 cases) — negative
  testing against simulated `appsettings.json` content containing a hardcoded connection
  password, a hardcoded `Jwt:Secret`, and a hardcoded top-level `JWT_SECRET` key; each
  must fail with a message naming both NFR-01 and IT-CFG-01.
- `RepositoryConfigFiles_DoNotContainHardcodedCredentials` — reads every real
  `appsettings*.json` under `src/` on disk (the one filesystem access in this suite; no
  network or database) and asserts none contains a hardcoded secret, and that each still
  loads successfully once the required environment variables are supplied.

## Traceability

| RTM ID | Requirement | Covered by |
| --- | --- | --- |
| UT-PRI-01 | FR-05 priority calculation | `PriorityMatrixTests` |
| IT-PRI-02 | FR-05 priority snapshot immutability | `TicketSnapshotTests` |
| IT-CFG-01 | NFR-01 protection of system secrets | `SystemSecretsConfigurationTests` |

See `docs/testing/api-integration/` for the tests that exercise the API controllers and
the real PostgreSQL database.
