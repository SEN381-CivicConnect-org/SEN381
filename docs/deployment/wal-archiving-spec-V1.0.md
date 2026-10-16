# Continuous WAL Archiving & Durability Specification (V1.0)

| Metadata | Value |
|---|---|
| **Document ID** | `OPS-SPEC-WAL-V1.0` |
| **Requirement Traceability** | **NFR-02** (Durability and Recovery Point), **NFR-01** (Secrets Protection), **NFR-03** (Verified Restore / RTO), **NFR-05** (POPIA s14(1) 30-Day Retention) |
| **Risk Mitigation** | **RSK-08** (Unverified Backups / Provider Data Loss) |
| **RTM Verification Reference** | `OPS-BKP-01 RestoreFromNightlyDump`, `OPS-BKP-02 RestoreDrillRecord` |
| **Target Database Engine** | PostgreSQL 15 / 16 (`civicconnect-db`) |

---

## 1. Purpose and Recovery Objectives

This specification defines the two-stage durability and Recovery Point Objective (**RPO**) architecture for the CivicConnect persistence layer (`CivicConnectDbContext` / PostgreSQL):

1. **Milestones 2 & 3 Baseline ($\text{RPO} \le 24\text{ hours}$)**:
   - Enforced via automated daily logical backups (`scripts/backup-db.sh` and `scripts/backup-db.ps1`) and the `db-backup` companion service in `docker-compose.yml`.
   - Uses `pg_dump --format=custom --compress=6 --no-owner --no-privileges` alongside `pg_dumpall --roles-only` to capture a transactionally consistent snapshot (`REPEATABLE READ`) of all 21 CivicConnect schema tables, sequences, enums, triggers, and generated reference numbers (`INC-xxxxxx`, `TKT-xxxxxx`).
   - Every backup archive is paired with a `.sha256` cryptographic digest and `.meta.json` manifest recording the latest committed `incident.opened_at` watermark and recovery metadata.

2. **Production Baseline ($\text{RPO} \le 15\text{ minutes}$, $\text{RTO} \le 4\text{ hours}$)**:
   - Reduces the maximum potential data loss window from 24 hours to **15 minutes (900 seconds)** by combining scheduled physical base backups (`pg_basebackup`) with **Continuous Write-Ahead Log (WAL) Archiving** and Point-in-Time Recovery (**PITR**).

---

## 2. PostgreSQL Continuous WAL Archiving Configuration

To guarantee that no more than 15 minutes of committed `incident`, `ticket`, `resolution`, or `audit_log` transactions can be lost in a catastrophic storage failure, the production PostgreSQL instance must be configured with the following `postgresql.conf` parameters:

```ini
# ------------------------------------------------------------------------------
# WRITE-AHEAD LOG (WAL) & CONTINUOUS ARCHIVING (NFR-02: 15-Minute RPO Baseline)
# ------------------------------------------------------------------------------

# 1. Generate sufficient WAL metadata for physical base backups and PITR replay
wal_level = replica

# 2. Enable WAL segment archiving via archive_command
archive_mode = on

# 3. Copy completed 16 MB WAL segments to the immutable archive volume
#    and verify non-overwrite semantics before acknowledging archival to Postgres
archive_command = 'test ! -f /var/lib/postgresql/wal_archive/%f && cp %p /var/lib/postgresql/wal_archive/%f'

# 4. Enforce the 15-minute (900s) RPO upper bound even during low-traffic windows.
#    CivicConnect averages ~30 submissions/day (~140 MB/year), meaning a 16 MB
#    WAL segment may not fill naturally within 15 minutes. Setting archive_timeout
#    forces PostgreSQL to switch and archive the active WAL segment at least once
#    every 900 seconds whenever any transaction has been committed.
archive_timeout = 900

# 5. Compress full-page writes in WAL to reduce archive storage footprint
wal_compression = on

# 6. Support physical base backup streaming (pg_basebackup)
max_wal_senders = 3
checkpoint_timeout = 15min
```

### 2.1 Docker Compose Command-Line Override (Production Profile)

When running PostgreSQL inside Docker Compose, these parameters are supplied directly to the `postgres` entrypoint command:

```yaml
services:
  db:
    image: postgres:15-alpine
    container_name: civicconnect-db
    command:
      - "postgres"
      - "-c"
      - "wal_level=replica"
      - "-c"
      - "archive_mode=on"
      - "-c"
      - "archive_timeout=${WAL_ARCHIVE_TIMEOUT:-900}"
      - "-c"
      - "archive_command=test ! -f /var/lib/postgresql/wal_archive/%f && cp %p /var/lib/postgresql/wal_archive/%f"
    volumes:
      - db-data:/var/lib/postgresql/data
      - db-wal-archive:/var/lib/postgresql/wal_archive
```

### 2.2 Off-Site Encrypted WAL Shipping (RSK-08 & NFR-01)

For production deployments, archived WAL segments in `/var/lib/postgresql/wal_archive` are synced continuously (or shipped directly via `archive_command` using `pgBackRest` / `wal-g`) to an off-site S3-compatible object store with server-side or client-side AES-256 encryption:
- Credentials (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `WALG_S3_PREFIX`, `WALG_PGP_KEY_PATH`) **must** be injected exclusively via runtime environment variables in accordance with **NFR-01 (Protection of System Secrets)**.
- No storage keys or connection credentials may appear in `postgresql.conf`, `docker-compose.yml`, or source control.

---

## 3. Point-in-Time Recovery (PITR) Runbook

### 3.1 Scheduled Base Backup (`pg_basebackup`)
Alongside nightly logical dumps (`scripts/backup-db.sh`), the production baseline executes a daily physical base backup:

```bash
pg_basebackup -h "${POSTGRES_HOST}" -p "${POSTGRES_PORT}" -U "${POSTGRES_USER}" \
  -D "/backups/base/base_$(date -u +%Y%m%d_%H%M%SZ)" \
  -Ft -z -X fetch -P
```

### 3.2 Recovering from Catastrophic Failure (PITR Replay)
1. **Stop the PostgreSQL service** and clear the damaged data directory (`$PGDATA`).
2. **Extract the most recent physical base backup** into `$PGDATA` and set ownership (`chmod 0700 $PGDATA`).
3. **Configure WAL replay** in `$PGDATA/postgresql.auto.conf`:
   ```ini
   restore_command = 'cp /var/lib/postgresql/wal_archive/%f %p'
   recovery_target_time = '2026-10-16 20:00:00+00'   # Or omit to replay to the latest archived WAL segment
   recovery_target_action = 'promote'
   ```
4. **Create the recovery signal file**:
   ```bash
   touch "$PGDATA/recovery.signal"
   ```
5. **Start PostgreSQL** and monitor container logs until `database system is ready to accept connections` appears.
6. **Verify RPO and record RTO**:
   - Query `SELECT reference_number, opened_at FROM incident ORDER BY opened_at DESC LIMIT 1;` to confirm that the most recent committed incident is within the RPO target window ($\le 15\text{ minutes}$ for production PITR; $\le 24\text{ hours}$ for Milestone 2/3 nightly dump restore).
   - Commit the restore drill evidence record to `docs/deployment/` per **NFR-03**.

---

## 4. Retention & POPIA s14(1) Disposal Compliance (NFR-05)

Under **NFR-05 (Retention and Disposal of Personal Information)** and **POPIA Section 14(1)**, de-identified or disposed personal information must not persist indefinitely in historical backups:
- **Maximum Backup Retention Window**: **30 days** (`BACKUP_RETENTION_DAYS=30`).
- Both `scripts/backup-db.sh` and `scripts/backup-db.ps1` automatically prune `.dump`, `.sha256`, `*_roles.sql`, and `.meta.json` artifacts older than 30 days on every execution.
- Physical base backups and archived WAL segments older than 30 days are purged via `find /var/lib/postgresql/wal_archive -type f -mtime +30 -delete`, ensuring that any user de-identification or retention sweep becomes irreversible across all backup media within 30 days.
