# CivicConnect — Milestone Restore Drill Runbook (V1.0)

| Document Metadata | Value |
| --- | --- |
| **Document ID** | `OPS-BKP-02-RUNBOOK-V1.0` |
| **Requirement Traceability** | **NFR-03** (Verified Restore / RTO), **NFR-02** (Durability & RPO), **NFR-01** (Protection of System Secrets), **NFR-05** (POPIA Retention & Disposal) |
| **Risk Mitigation** | **RSK-08** (Unverified Backups / Database Recovery Failure), **RSK-01** (Operational Cost & Portability) |
| **RTM Verification Reference** | `OPS-BKP-02 RestoreDrillRecord` |
| **Recovery Time Objective (RTO)** | **$\le 4\text{ hours}$ ($\le 240\text{ minutes}$ / $\le 14,400\text{ seconds}$)** |
| **Target Database Engine** | PostgreSQL 15 / 16 (`postgres:15-alpine`) |

---

## 1. Purpose and Scope

This Standard Operating Procedure (**SOP**) defines the step-by-step runbook for executing a **Milestone Restore Drill** on the CivicConnect platform in fulfillment of **NFR-03 (Verified Restore)**:

> **NFR-03 Statement:** *"The system's backups will be verified by a restore drill in every milestone, restoring into a clean instance and passing the integration test suite within the stated recovery time objective of four hours."*
>
> **NFR-03 Acceptance Criteria:** *"Given the most recent backup and role export, when a team member restores them into an empty PostgreSQL instance and runs the integration test suite, then every test passes, the elapsed wall-clock time from the start of the restore to the last passing test is four hours or less, and a drill record containing that elapsed time is committed to `docs/deployment/`."*

This runbook covers both:
1. **Automated Execution** via [`scripts/run-restore-drill.ps1`](../../scripts/run-restore-drill.ps1) (Windows / PowerShell) and [`scripts/run-restore-drill.sh`](../../scripts/run-restore-drill.sh) (Linux / macOS / CI).
2. **Manual Step-by-Step Execution** for operators performing hands-on disaster recovery verification.

---

## 2. Recovery Objectives & Pass Criteria

| Metric | Requirement | Target Threshold | Verification Mechanism |
| --- | --- | --- | --- |
| **Recovery Time Objective (RTO)** | NFR-03 / NFR-09 | **$\le 4\text{ hours}$** ($14,400\text{ s}$) | Wall-clock timer from initiation of `pg_restore` / role import to completion of the final passing integration test |
| **Recovery Point Objective (RPO)** | NFR-02 | **$\le 24\text{ hours}$** (M2/M3) / **$\le 15\text{ min}$** (Prod) | Timestamp comparison between destruction/drill start and `MAX(incident.opened_at)` |
| **Archive Cryptographic Integrity** | NFR-02 / NFR-03 | **SHA-256 match** | Verification of `.dump` binary against companion `.dump.sha256` digest |
| **Schema & Reference Completeness** | NFR-03 | **22 domain tables + 12 migrations** | Post-restore SQL verification against `information_schema.tables` and `__ef_migrations_history` |
| **Integration Test Pass Rate** | NFR-03 (`OPS-BKP-02`) | **100% pass (`0` failed)** | `dotnet test tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj` against the restored instance |

---

## 3. Pre-Drill Checklist

Before initiating a milestone restore drill, the operator must verify the following prerequisites:

- [ ] **1. Environment Configuration (`NFR-01`)**:
  - Confirm that `.env` exists at the repository root (copied from `.env.example`) or that `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, and `CONNECTION_STRING` are exported in the current shell session.
  - Confirm that no placeholder passwords (`changeme` or unexpanded `${...}` tokens) are in use.
- [ ] **2. Docker Engine & Runtime Tools**:
  - Verify Docker Desktop / Docker Engine is running (`docker info`).
  - Verify the `.NET 10.0 SDK` is installed (`dotnet --version`).
  - Confirm TCP port `5439` (the default isolated drill port) is free on `localhost`, or select an alternate port via `-DrillPort` / `--drill-port`.
- [ ] **3. Backup & Role Export Availability (`NFR-02`)**:
  - Check `backups/` for the most recent backup triad:
    - `civicconnect_backup_<YYYYMMDD_HHMMSSZ>.dump` (PostgreSQL custom-format compressed archive)
    - `civicconnect_backup_<YYYYMMDD_HHMMSSZ>.dump.sha256` (SHA-256 checksum file)
    - `civicconnect_backup_<YYYYMMDD_HHMMSSZ>_roles.sql` (Cluster role export from `pg_dumpall --roles-only`)
  - If no backup exists yet, capture one from the running `civicconnect-db` container using `scripts/backup-db.ps1` / `scripts/backup-db.sh` (or pass `-TakeFreshBackup` / `--take-fresh-backup` to the drill script).

---

## 4. Automated Restore Drill Execution (Recommended)

The automated drill scripts orchestrate all phases, enforce strict error handling, measure millisecond-resolution phase timings, and guarantee teardown of temporary containers/databases via `try / finally` (PowerShell) and `trap ... EXIT` (Bash).

### 4.1 PowerShell (Windows / Cross-Platform PowerShell)

```powershell
# Run the restore drill using the most recent backup in backups/
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-restore-drill.ps1

# Capture a fresh backup from civicconnect-db first, then execute the full restore drill
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-restore-drill.ps1 -TakeFreshBackup
```

### 4.2 Bash (Linux / macOS / GitHub Actions CI)

```bash
# Run the restore drill using the most recent backup in backups/
bash ./scripts/run-restore-drill.sh

# Capture a fresh backup first, then execute the full restore drill
bash ./scripts/run-restore-drill.sh --take-fresh-backup
```

### 4.3 Supported Command-Line Parameters

| PowerShell Parameter | Bash Flag | Default | Description |
| --- | --- | --- | --- |
| `-BackupFile <path>` | `--file <path>` | Latest in `backups/` | Explicit path to the `.dump` archive to restore |
| `-RolesFile <path>` | `--roles-file <path>` | Companion `*_roles.sql` | Explicit path to the `pg_dumpall --roles-only` SQL file |
| `-BackupDir <path>` | `--backup-dir <path>` | `./backups` | Directory containing backup archives and drill logs |
| `-DrillContainerName` | `--drill-container` | `civicconnect-drill-db` | Name of the ephemeral PostgreSQL container |
| `-DrillDatabase` | `--drill-database` | `civicconnect_drill` | Target database name inside the clean instance |
| `-DrillPort` | `--drill-port` | `5439` | Host TCP port mapped to container port `5432` |
| `-PostgresImage` | `--postgres-image` | `postgres:15-alpine` | Container image matching `docker-compose.yml` |
| `-IsolationMode` | `--isolation-mode` | `Auto` (`Container` / `Database`) | `Container` provisions an ephemeral container; `Database` provisions an isolated DB on an existing server |
| `-TakeFreshBackup` | `--take-fresh-backup` | `false` | Captures a fresh backup and role export before starting the drill |
| `-KeepInstance` | `--keep-instance` | `false` | Retains the drill container/database after completion for manual inspection |

---

## 5. Manual Step-by-Step Restore Drill Procedure

When executing a manual verification drill without the automation wrapper, operators must record the UTC timestamp at the start of **Step 3** (`Restore Initiation`) and at the end of **Step 5** (`Last Passing Test`).

### Step 1: Verify Backup Archive Integrity & Role Export

1. Identify the latest `.dump` archive and companion `.sha256` and `*_roles.sql` files in `backups/`:
   ```bash
   ls -lh backups/civicconnect_backup_*
   ```
2. Verify the SHA-256 cryptographic digest:
   ```bash
   # Linux / macOS
   (cd backups && sha256sum -c civicconnect_backup_<TIMESTAMP>.dump.sha256)
   ```
   ```powershell
   # Windows PowerShell
   Get-FileHash .\backups\civicconnect_backup_<TIMESTAMP>.dump -Algorithm SHA256
   Get-Content .\backups\civicconnect_backup_<TIMESTAMP>.dump.sha256
   ```
3. Verify the archive table of contents (TOC) is readable:
   ```bash
   docker exec civicconnect-db pg_restore --list /backups/civicconnect_backup_<TIMESTAMP>.dump | head -n 30
   ```

### Step 2: Provision a Clean, Isolated PostgreSQL Instance

1. Remove any pre-existing drill container to guarantee a zero-state environment:
   ```bash
   docker rm -f civicconnect-drill-db 2>/dev/null || true
   ```
2. Start a clean `postgres:15-alpine` instance on host port `5439`:
   ```bash
   docker run -d \
     --name civicconnect-drill-db \
     -e POSTGRES_DB=civicconnect_drill \
     -e POSTGRES_USER="${POSTGRES_USER}" \
     -e POSTGRES_PASSWORD="${POSTGRES_PASSWORD}" \
     -p 5439:5432 \
     postgres:15-alpine
   ```
3. Wait until `pg_isready` reports accepting connections:
   ```bash
   docker exec civicconnect-drill-db pg_isready -h localhost -p 5432 -U "${POSTGRES_USER}" -d civicconnect_drill
   ```

### Step 3: Restore Role Export & Database Backup Archive (Start RTO Timer)

1. **Record the UTC start time (`T_start`)**:
   ```bash
   date -u +"%Y-%m-%dT%H:%M:%SZ"
   ```
2. **Import the cluster role export (`*_roles.sql`)**:
   ```bash
   docker cp "backups/civicconnect_backup_<TIMESTAMP>_roles.sql" civicconnect-drill-db:/tmp/drill_roles.sql
   docker exec -e PGPASSWORD="${POSTGRES_PASSWORD}" civicconnect-drill-db \
     psql -h localhost -p 5432 -U "${POSTGRES_USER}" -d postgres -f /tmp/drill_roles.sql
   ```
   *(Note: `ERROR: role "civicconnect" already exists` is expected and harmless because the container entrypoint initializes the bootstrap superuser.)*
3. **Restore the custom-format database dump (`.dump`)**:
   ```bash
   docker cp "backups/civicconnect_backup_<TIMESTAMP>.dump" civicconnect-drill-db:/tmp/drill_restore.dump
   docker exec -e PGPASSWORD="${POSTGRES_PASSWORD}" civicconnect-drill-db \
     pg_restore -h localhost -p 5432 -U "${POSTGRES_USER}" -d civicconnect_drill \
     --clean --if-exists --no-owner --no-privileges --exit-on-error \
     /tmp/drill_restore.dump
   ```

### Step 4: Verify Restored Schema & Reference Data

Run the post-restore verification query inside `civicconnect-drill-db` to confirm all 22 domain tables, 12 EF Core migrations, seeded lookup tables, and operational records are intact:

```bash
docker exec -e PGPASSWORD="${POSTGRES_PASSWORD}" civicconnect-drill-db \
  psql -h localhost -p 5432 -U "${POSTGRES_USER}" -d civicconnect_drill -c "
    SELECT
      (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__ef_migrations_history') AS domain_tables,
      (SELECT COUNT(*) FROM __ef_migrations_history) AS migrations,
      (SELECT COUNT(*) FROM role) AS roles,
      (SELECT COUNT(*) FROM priority_target) AS priority_targets,
      (SELECT COUNT(*) FROM location_kind) AS location_kinds,
      (SELECT COUNT(*) FROM resolution_code) AS resolution_codes,
      (SELECT COUNT(*) FROM incident) AS incidents,
      (SELECT COUNT(*) FROM ticket) AS tickets;
  "
```

Expected baseline counts:
- `domain_tables`: `22`
- `migrations`: `12`
- `roles`: `5` (`reporter`, `technician`, `supervisor`, `operations manager`, `administrator`)
- `priority_targets`: `4` (`critical`, `high`, `medium`, `low`)
- `location_kinds`: `3` (`site`, `floor`, `room`)
- `resolution_codes`: `6` (`FIXED`, `DUPLICATE`, `CANNOT_REPRODUCE`, `WORKAROUND`, `HARDWARE_REPLACED`, `BEYOND_REPAIR`)

### Step 5: Execute the Integration Test Suite Against the Restored Instance (Stop RTO Timer)

1. Point `CONNECTION_STRING` at the restored drill instance (`localhost:5439/civicconnect_drill`) and run `CivicConnect.Data.Tests`:
   ```bash
   export CONNECTION_STRING="Host=localhost;Port=5439;Database=civicconnect_drill;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
   dotnet test tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj --verbosity normal
   ```
2. **Record the UTC completion time (`T_end`)** immediately upon completion of the test run:
   ```bash
   date -u +"%Y-%m-%dT%H:%M:%SZ"
   ```
3. Verify that:
   - `Failed: 0`, `Passed: 32`, `Total: 32` (100% pass rate).
   - $\text{Elapsed Time} = T_{\text{end}} - T_{\text{start}} \le 4\text{ hours}$ ($14,400\text{ seconds}$).

### Step 6: Teardown, Cleanup & Evidence Commit

1. Stop and remove the ephemeral drill container and temporary files:
   ```bash
   docker rm -f civicconnect-drill-db
   ```
2. Record the execution metrics in `docs/deployment/restore-drill-record-<MILESTONE>.md` and commit the record to source control.

---

## 6. Failure Escalation & Troubleshooting Matrix

If any phase of the restore drill fails, the drill is marked **`FAIL`** and must be remediated before milestone sign-off:

| Failure Symptom | Root Cause | Immediate Remediation / Escalation |
| --- | --- | --- |
| **SHA-256 Checksum Mismatch** | Corrupted `.dump` archive or incomplete file copy | Quarantine the corrupt archive; select the immediately preceding valid backup from `backups/`; re-run `scripts/backup-db.*` and inspect disk space. |
| **`pg_restore` Foreign Key Violation (`fk_audit_log_...`)** | Orphaned `audit_log` rows created by test teardowns that bypassed triggers via `SET LOCAL session_replication_role = 'replica'` | Purge orphaned rows in the source database prior to dump capture (`run-restore-drill.*` performs this automatically in `Ensure-SourceBaselineData`). |
| **`pg_restore` Exit Code $\ne 0$** | Version mismatch between `pg_dump` and `pg_restore`, or missing `citext` extension privileges | Verify both source and drill containers use `postgres:15-alpine` (or compatible major version) and that the restore user has `CREATE` privilege on the target database. |
| **Integration Test Failure (`Failed > 0`)** | Schema drift between backup state and current EF Core model, or broken trigger/constraint | Inspect `restore-drill-results.trx`; verify `__ef_migrations_history` contains all expected migrations; escalate to Database & Backend Leads. |
| **Elapsed Time $> 4\text{ hours}$ ($\text{RTO Breach}$)** | Excessive backup size, slow storage I/O, or stalled network transfer | Enable parallel restore (`pg_restore --jobs=4`), tune Docker volume mounts, and escalate to Operations Lead under **RSK-08**. |
