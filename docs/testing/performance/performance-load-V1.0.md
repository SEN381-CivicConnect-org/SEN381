# Performance / Load — What Was Done

Scope: one small, justified performance exercise against the management/staff/reporter
read-path queries, run with `tools/seed-benchmark` (a .NET console tool) against a
realistic-scale PostgreSQL dataset. This is the project's performance/load evidence for
SEN381 Milestone 3, Assessment Area C/F — it targets NFR-07 (staff queue query
performance) and NFR-08 (management dashboard query performance), and exercises the
indexes designed in `docs/access patterns/access-patterns-V1.0.html` (AP-04, AP-07,
AP-07b, AP-07c) and `docs/data-persistence/`.

**Repository status at the time of writing:** `tools/seed-benchmark/` is present on disk
and has been run and verified (see below), but is not yet committed — `git status` shows
it untracked. The benchmark output lives under `.scratch/database-schema/`, which is
listed in `.gitignore` ("EF Core scaffolding scratch"), so the report itself is also not
committed. This document is the committed, authoritative record of what the tool does and
what it measured; see the "Repository evidence" note at the end for what would need to
change for this to be first-class, committed CI evidence rather than a local artefact.

## Why this operation

The staff queue (AP-04, NFR-07) and the management dashboard's status/category/overdue
breakdowns (AP-07/07b/07c, NFR-08) are the two read paths the PED calls out with
measurable performance acceptance criteria — "the most frequent read in the system" for
the former, and the basis for management's accountability reporting for the latter. Both
depend entirely on the partial/composite indexes designed for them; a seq scan on
`incident` on either path would be the specific failure this exercise is built to catch.

## Workload and environment

- **Tool:** `tools/seed-benchmark/Program.cs` (`dotnet run --project tools/seed-benchmark
  [incidentCount]`), built on the project's own `CivicConnectDbContext`,
  `IncidentReportingService` and `PriorityMatrix` — not a third-party load-testing
  product.
- **Dataset:** seeds (idempotently — re-running tops the dataset up rather than
  duplicating it) 250 reporters, 30 technicians across 6 service teams, 10 categories, 40
  locations, and incidents/tickets up to the requested count. The default and the count
  used here is **6,000 incidents**, matching ASM-07's own one-year volume estimate (PED
  v2.1 §2.8) — a deliberately representative size, not an inflated one. Incident status is
  distributed NEW 10% / ASSIGNED 10% / IN_PROGRESS 15% / ON_HOLD 5% / RESOLVED 15% /
  CLOSED 40% / REJECTED 5%, with resolved/closed timestamps split roughly 70% inside and
  30% outside their priority's SLA target, so the overdue and resolution-breakdown queries
  have a realistic mix to work against.
- **Environment:** the project's own local `postgres:15-alpine` container
  (`civicconnect-db`), migrated to the current schema, `VACUUM ANALYZE`d immediately
  before benchmarking so the planner's statistics are current — the same honest step a
  production deployment would need after a bulk load.
- **Last executed:** 2026-10-16, against a database already holding the full 6,000-row
  dataset (`Incidents already present: 6000. Creating 0 more.`), report regenerated to
  `.scratch/database-schema/benchmarks/11-management-read-path.md`.

## What was measured

Seven queries behind the real `IncidentReportingService` methods the staff/management UI
calls, each run once through EF Core (application round-trip) and once as a raw
`EXPLAIN (ANALYZE, BUFFERS)` (Postgres execution time and query plan):

| Query | App round-trip | Postgres execution time | Seq scan on `incident`? |
| --- | --- | --- | --- |
| Open incidents by status | 393.84 ms | 0.394 ms | No |
| Overdue incidents (page 1) | 43.90 ms | 0.100 ms | No |
| Resolution breakdown by category | 95.38 ms | 2.129 ms | No |
| Resolution breakdown by team | 19.90 ms | 1.587 ms | No |
| Turnaround vs. priority target | 41.62 ms | 0.602 ms | No |
| Staff combined filter (status + category + team + location) | 106.63 ms | 0.193 ms | No |
| Reporter's own tickets | 11.85 ms | 0.162 ms | No |

Every query plan used an index (`ix_incident_status_priority`, `ix_incident_due_at_open`,
`ix_incident_status_category_id`, `ix_incident_status_assigned_team_id`, and equivalents)
— none fell back to a sequential scan on `incident`, which is what the tool checks for and
flags (`YES — FAIL`) in its own report if it happens.

## Interpretation — what this does and does not show

- **Against NFR-07 and NFR-08:** NFR-07 requires the team queue at ≤500 ms (95th
  percentile, 20,000-incident dataset, 200 open); NFR-08 requires the dashboard summaries
  at ≤2 s (95th percentile, same dataset). Every Postgres execution time measured here is
  under 2.2 ms — comfortably inside both targets, with margin to spare even allowing for a
  larger dataset.
- **What this run does NOT demonstrate:** it is a single run against 6,000 incidents, not
  the 20,000-incident dataset NFR-07/08 name, and it reports one measured time per query
  rather than a 95th percentile over repeated executions. The gap is in methodology
  (single sample, smaller dataset), not in the result — the query plans already show index
  usage, and an index-backed query's cost grows with the log of table size, not linearly,
  so the headroom at 6,000 rows is a reasonable basis for confidence, not proof, at 20,000.
- **App round-trip vs. Postgres execution time:** the app-level timings (12–394 ms) are
  one to two orders of magnitude higher than the database's own execution time. This gap
  is EF Core's one-time cost per distinct query shape in a freshly started process —
  connection-pool setup and LINQ-to-SQL translation/compilation on first use — not a
  representative per-request cost in a warmed, long-running API process. The Postgres
  `EXPLAIN ANALYZE` numbers and the absence of sequential scans are the evidence this
  exercise actually stands behind; the app round-trip column is reported for transparency,
  not relied on as the performance claim.
- **What it does not prove about production/municipal scale:** this is a single local
  container, not a representative host, with no concurrent load applied during the
  measurement (concurrent-write behaviour under load is covered separately by
  `ConcurrencyTests` — see `docs/testing/api-integration/`). It says nothing about network
  latency, connection-pool exhaustion under real concurrent read traffic, or behaviour at
  ten times the seeded volume.

## Repository evidence

`tools/seed-benchmark` and its report currently exist only on the machine that ran this
exercise — they are not committed (the tool is untracked; the report's directory is
gitignored). For this to stand as authentic, repository-traceable evidence rather than a
locally-reproduced claim, `tools/seed-benchmark/Program.cs` (and its `.csproj`, excluding
the `bin`/`obj` output) would need to be committed, and the report moved to a
non-gitignored path (e.g. alongside this document) or regenerated and attached as CI
artefact output. That action was out of scope for this documentation pass and is left for
the team to decide and action.
