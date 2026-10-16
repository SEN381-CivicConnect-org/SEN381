# CI Workflow — What Was Done

Scope: the two GitHub Actions workflows that run automated checks against every pull
request and every push to the project's controlled branches. This is the project's CI
pipeline evidence for SEN381 Milestone 3, Assessment Area C.

## Pipeline overview

| Workflow | File | Triggers | What it runs |
| --- | --- | --- | --- |
| CI Quality & Security Gate | `.github/workflows/ci-quality-gate.yml` | `push` and `pull_request` on `dev`, `staging`, `main` | Secret scan, dependency-vulnerability check, build with warnings as errors |
| Database migrations | `.github/workflows/db-migrations.yml` | `push` on `staging`/`main`, `pull_request` (any target) | Migrate up → down → up against a real PostgreSQL service container, then run the full automated test suite with code-coverage collection |

Both are standard GitHub-hosted Actions runners (`ubuntu-latest`) — no self-hosted runner
or external CI product, matching the project's cost/team-size constraints (PED §2.5).

## `ci-quality-gate.yml` — job `code-quality-and-security`

1. `actions/checkout@v4` (`fetch-depth: 0`, full history for the secret scanner).
2. `gitleaks/gitleaks-action@v2` — secret scanning (see
   `docs/security/security-dependency-secrets-V1.0.md` for its real failure/fix history).
3. `actions/setup-dotnet@v4` (`.NET 10.0.x`).
4. `dotnet restore`.
5. `dotnet list package --vulnerable` — dependency vulnerability report.
6. `dotnet build --no-restore -warnaserror` — compiles the whole solution with the
   default Roslyn analyzers promoted to errors.

This workflow does not run the test suites or touch a database — it is the fast,
no-infrastructure check that runs on every push/PR regardless of branch.

## `db-migrations.yml` — job `migrate-up-down-up`

Runs a real `postgres:16` service container (not SQLite or an in-memory provider),
health-checked with `pg_isready` before the job proceeds:

1. Checkout, `.NET 10.0.x` setup, `dotnet restore`, `dotnet build --no-restore`.
2. `./scripts/migrate-up.sh` — applies every EF Core migration to an empty database.
3. `./scripts/migrate-down.sh` — reverses every migration back to empty.
4. `./scripts/migrate-up.sh` again — re-applies from empty, proving the down-migrations
   are genuinely reversible and not merely present.
5. `dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage"` — runs the
   entire solution's test suites (`CivicConnect.Tests` and `CivicConnect.Data.Tests`, the
   unit/component and API/integration suites documented in `docs/testing/`) against the
   now-current schema, collecting a Cobertura coverage report as a build artefact. Per the
   M3 brief's coverage rule, no coverage percentage is asserted or gated on here — the
   collected report is available for inspection, not treated as proof of correctness.

The up→down→up sequence is itself a meaningful check, independent of the tests that
follow it: it fails if any migration's `Down` doesn't actually undo its `Up`, which a
test suite that only ever runs against an already-migrated database would never catch.

## Real execution evidence

Most recent run of each workflow at the time of writing (2026-10-16, PR #140 "Staging
prep", branch `Staging_Prep`):

- `CI Quality & Security Gate` — run
  [38888423618](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38888423618):
  **success**, 39s.
- `Database migrations` — run
  [38888423708](https://github.com/SEN381-CivicConnect-org/SEN381/actions/runs/38888423708):
  **success**, 1m13s. The `Run tests` step's own output: `CivicConnect.Tests` — Total
  tests: 66, Passed: 66; `CivicConnect.Data.Tests` — Total tests: 32, Passed: 32.

Both workflows have real, non-manufactured failure history from earlier in the project
(compile errors, a missing license secret, a failing secrets test — see
`docs/ci/quality-gate-V1.0.md` for the specific runs and what they demonstrate), followed
by a genuine fix commit and a subsequent green run. This history is in the repository's
own Actions log (`gh run list`), not reconstructed for this document.

## What this pipeline does not cover

- No staging/production deployment step — this repository's CI validates the build,
  schema and test suite; it does not deploy anywhere.
- No dedicated black-box/E2E test execution job separate from the general `dotnet test`
  step — black-box and E2E cases run as part of the same test suite invocation as the
  unit/component and API/integration cases (see `docs/testing/` for which test classes
  are which).
- No required-status-check enforcement at the branch level — see
  `docs/ci/quality-gate-V1.0.md`.

## Traceability

PED v2.1 §6.4 (ADR-004, Docker/CI direction), §7 (GitHub and Team Governance), NFR-01,
NFR-10. Full check-level detail: `docs/security/security-dependency-secrets-V1.0.md`.
Gate/blocking-behaviour detail: `docs/ci/quality-gate-V1.0.md`.
