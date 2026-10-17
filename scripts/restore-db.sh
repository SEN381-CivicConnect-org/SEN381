#!/usr/bin/env bash
# ==============================================================================
# CivicConnect Automated Database Restore Script (Bash)
# Requirement: NFR-02 (Durability and Recovery Point - RPO) & NFR-03 (Verified Restore)
# Traceability: OPS-BKP-01 RestoreFromNightlyDump / RSK-08
# ==============================================================================
# Restores a specified pg_dump custom-format archive (or the most recent archive
# in backups/) into the target PostgreSQL instance using pg_restore
# (--clean --if-exists --no-owner --no-privileges --exit-on-error), verifies
# SHA-256 checksum integrity, measures wall-clock recovery duration, and
# evaluates RPO compliance (<= 24 hours for Milestones 2 & 3).
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

BACKUP_DIR="${BACKUP_DIR:-${REPO_ROOT}/backups}"
CONTAINER_NAME="${POSTGRES_CONTAINER:-civicconnect-db}"
ENV_FILE="${ENV_FILE:-${REPO_ROOT}/.env}"
BACKUP_FILE=""
TARGET_DB_OVERRIDE=""
RECREATE_DB="false"
SKIP_CHECKSUM="false"
DESTRUCTION_TIMESTAMP_UTC=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --file|-f)
      BACKUP_FILE="$2"
      shift 2
      ;;
    --latest|-l)
      BACKUP_FILE=""
      shift
      ;;
    --backup-dir|-b)
      BACKUP_DIR="$2"
      shift 2
      ;;
    --database|-d)
      TARGET_DB_OVERRIDE="$2"
      shift 2
      ;;
    --container|-c)
      CONTAINER_NAME="$2"
      shift 2
      ;;
    --env-file|-e)
      ENV_FILE="$2"
      shift 2
      ;;
    --recreate-db)
      RECREATE_DB="true"
      shift
      ;;
    --skip-checksum)
      SKIP_CHECKSUM="true"
      shift
      ;;
    --destruction-timestamp)
      DESTRUCTION_TIMESTAMP_UTC="$2"
      shift 2
      ;;
    -*)
      echo "[ERROR] Unknown option: $1" >&2
      exit 1
      ;;
    *)
      if [[ -z "$BACKUP_FILE" ]]; then
        BACKUP_FILE="$1"
        shift
      else
        echo "[ERROR] Unexpected positional argument: $1" >&2
        exit 1
      fi
      ;;
  esac
done

# Load environment variables from .env if present (NFR-01 compliance)
if [[ -f "${ENV_FILE}" ]]; then
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    trimmed="$(echo "$line" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
    if [[ -z "$trimmed" || "$trimmed" == \#* ]]; then
      continue
    fi
    if [[ "$trimmed" == *=* ]]; then
      key="${trimmed%%=*}"
      val="${trimmed#*=}"
      key="$(echo "$key" | sed -e 's/[[:space:]]*$//')"
      val="$(echo "$val" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/^"//' -e 's/"$//' -e "s/^'//" -e "s/'$//")"
      if [[ -z "${!key:-}" ]]; then
        export "$key=$val"
      fi
    fi
  done < "${ENV_FILE}"
fi

CONN_STR="${CONNECTION_STRING:-}"
CONN_HOST=""
CONN_PORT=""
CONN_DB=""
CONN_USER=""
CONN_PASS=""

if [[ -n "$CONN_STR" ]]; then
  IFS=';' read -ra PAIRS <<< "$CONN_STR"
  for pair in "${PAIRS[@]}"; do
    k="${pair%%=*}"
    v="${pair#*=}"
    k_lower="$(echo "$k" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"
    case "$k_lower" in
      host|server) CONN_HOST="$v" ;;
      port) CONN_PORT="$v" ;;
      database|initialcatalog) CONN_DB="$v" ;;
      username|user|userid|uid) CONN_USER="$v" ;;
      password|pwd) CONN_PASS="$v" ;;
    esac
  done
fi

PG_HOST="${POSTGRES_HOST:-${CONN_HOST:-localhost}}"
PG_PORT="${POSTGRES_PORT:-${CONN_PORT:-5432}}"
PG_DB="${TARGET_DB_OVERRIDE:-${POSTGRES_DB:-${CONN_DB:-civicconnect}}}"
PG_USER="${POSTGRES_USER:-${CONN_USER:-civicconnect}}"
PG_PASS="${POSTGRES_PASSWORD:-${CONN_PASS:-}}"

if [[ -z "$PG_PASS" || "${PG_PASS,,}" == "changeme" || "$PG_PASS" == *'${'* ]]; then
  echo "[ERROR] NFR-01 violation: POSTGRES_PASSWORD or CONNECTION_STRING password is missing or uses an insecure placeholder." >&2
  exit 1
fi

# Resolve backup archive path (default to most recent dump in BACKUP_DIR)
if [[ -z "$BACKUP_FILE" ]]; then
  BACKUP_FILE="$(ls -1t "${BACKUP_DIR}"/civicconnect_backup_*.dump 2>/dev/null | head -n 1 || true)"
  if [[ -z "$BACKUP_FILE" ]]; then
    echo "[ERROR] No backup archives found in '${BACKUP_DIR}'." >&2
    exit 1
  fi
fi

if [[ ! -f "$BACKUP_FILE" ]]; then
  echo "[ERROR] Backup archive '${BACKUP_FILE}' does not exist." >&2
  exit 1
fi

ARCHIVE_FILENAME="$(basename "$BACKUP_FILE")"
CHECKSUM_FILE="${BACKUP_FILE}.sha256"

# Verify SHA-256 checksum if present
if [[ "$SKIP_CHECKSUM" != "true" && -f "$CHECKSUM_FILE" ]]; then
  EXPECTED_HASH="$(awk '{print tolower($1)}' "$CHECKSUM_FILE" | tr -d '\r\n[:space:]')"
  if command -v sha256sum >/dev/null 2>&1; then
    ACTUAL_HASH="$(sha256sum "$BACKUP_FILE" | awk '{print tolower($1)}')"
  elif command -v shasum >/dev/null 2>&1; then
    ACTUAL_HASH="$(shasum -a 256 "$BACKUP_FILE" | awk '{print tolower($1)}')"
  else
    ACTUAL_HASH="$(openssl dgst -sha256 "$BACKUP_FILE" | awk '{print tolower($NF)}')"
  fi

  if [[ "$EXPECTED_HASH" != "$ACTUAL_HASH" ]]; then
    echo "[ERROR] SHA-256 checksum mismatch for '${ARCHIVE_FILENAME}'. Expected ${EXPECTED_HASH}, got ${ACTUAL_HASH}." >&2
    exit 1
  fi
  echo "[INFO] SHA-256 checksum verified (${ACTUAL_HASH})."
fi

# Determine execution mode
EXEC_MODE="direct"
if [[ "${USE_DOCKER_EXEC:-auto}" == "true" ]]; then
  EXEC_MODE="docker"
elif [[ "${USE_DOCKER_EXEC:-auto}" == "auto" ]]; then
  if [[ -f "/.dockerenv" ]] || [[ "${DOTNET_RUNNING_IN_CONTAINER:-}" == "true" ]]; then
    EXEC_MODE="direct"
  elif command -v docker >/dev/null 2>&1 && docker inspect -f '{{.State.Running}}' "${CONTAINER_NAME}" 2>/dev/null | grep -q "true"; then
    EXEC_MODE="docker"
  elif ! command -v pg_restore >/dev/null 2>&1; then
    echo "[ERROR] Neither a running Docker container '${CONTAINER_NAME}' nor local 'pg_restore' binary was found." >&2
    exit 1
  fi
fi

get_epoch_ms() {
  if [[ -n "${EPOCHREALTIME:-}" ]]; then
    awk -v t="${EPOCHREALTIME}" 'BEGIN { printf "%.0f", t * 1000 }'
  else
    echo "$(($(date +%s) * 1000))"
  fi
}

ISO_START_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
if [[ -z "$DESTRUCTION_TIMESTAMP_UTC" ]]; then
  DESTRUCTION_TIMESTAMP_UTC="${ISO_START_UTC}"
fi

START_EPOCH_MS="$(get_epoch_ms)"

echo "[INFO] [${ISO_START_UTC}] Restoring '${ARCHIVE_FILENAME}' into database '${PG_DB}' (mode=${EXEC_MODE})..."

if [[ "$EXEC_MODE" == "docker" ]]; then
  CONTAINER_TMP="/tmp/restore_${ARCHIVE_FILENAME}"
  docker cp "${BACKUP_FILE}" "${CONTAINER_NAME}:${CONTAINER_TMP}"

  if [[ "$RECREATE_DB" == "true" ]]; then
    docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${PG_DB}' AND pid <> pg_backend_pid();" >/dev/null
    docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "DROP DATABASE IF EXISTS \"${PG_DB}\";" >/dev/null
    docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "CREATE DATABASE \"${PG_DB}\";" >/dev/null
  else
    DB_EXISTS="$(docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -t -A \
      -c "SELECT 1 FROM pg_database WHERE datname = '${PG_DB}';" | tr -d '\r\n[:space:]')"
    if [[ "$DB_EXISTS" != "1" ]]; then
      docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
        psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
        -c "CREATE DATABASE \"${PG_DB}\";" >/dev/null
    fi
  fi

  # Reset public schema cleanly before pg_restore
  docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -v ON_ERROR_STOP=1 \
    -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" >/dev/null

  docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
    pg_restore -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" \
    --clean --if-exists --no-owner --no-privileges --exit-on-error \
    "${CONTAINER_TMP}"

  docker exec "${CONTAINER_NAME}" rm -f "${CONTAINER_TMP}"
else
  EFFECTIVE_HOST="${PG_HOST}"
  export PGPASSWORD="${PG_PASS}"

  if [[ "$RECREATE_DB" == "true" ]]; then
    psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${PG_DB}' AND pid <> pg_backend_pid();" >/dev/null
    psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "DROP DATABASE IF EXISTS \"${PG_DB}\";" >/dev/null
    psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "CREATE DATABASE \"${PG_DB}\";" >/dev/null
  else
    DB_EXISTS="$(psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres -t -A \
      -c "SELECT 1 FROM pg_database WHERE datname = '${PG_DB}';" | tr -d '\r\n[:space:]')"
    if [[ "$DB_EXISTS" != "1" ]]; then
      psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
        -c "CREATE DATABASE \"${PG_DB}\";" >/dev/null
    fi
  fi

  psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" -v ON_ERROR_STOP=1 \
    -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" >/dev/null

  pg_restore -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" \
    --clean --if-exists --no-owner --no-privileges --exit-on-error \
    "${BACKUP_FILE}"

  unset PGPASSWORD
fi

END_EPOCH_MS="$(get_epoch_ms)"
ELAPSED_MS=$((END_EPOCH_MS - START_EPOCH_MS))
if [[ "$ELAPSED_MS" -lt 0 ]]; then
  ELAPSED_MS=0
fi
ELAPSED_SECONDS="$(awk -v ms="$ELAPSED_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"

# Query post-restore recovery metrics & RPO verification
QUERY_SQL="SELECT
  (SELECT COUNT(*) FROM incident) || '|' ||
  (SELECT COUNT(*) FROM ticket) || '|' ||
  COALESCE((SELECT reference_number FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE') || '|' ||
  COALESCE((SELECT to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE') || '|' ||
  COALESCE((SELECT ROUND((EXTRACT(EPOCH FROM ('${DESTRUCTION_TIMESTAMP_UTC}'::timestamptz - opened_at)) / 3600.0)::numeric, 4)::text FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), '0.0000') || '|' ||
  COALESCE((SELECT CASE WHEN ('${DESTRUCTION_TIMESTAMP_UTC}'::timestamptz - opened_at) <= interval '24 hours' THEN 'true' ELSE 'false' END FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'true');"

if [[ "$EXEC_MODE" == "docker" ]]; then
  METRICS_RAW="$(docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -t -A -c "${QUERY_SQL}" | tr -d '\r\n')"
else
  export PGPASSWORD="${PG_PASS}"
  METRICS_RAW="$(psql -h "${PG_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" -t -A -c "${QUERY_SQL}" | tr -d '\r\n')"
  unset PGPASSWORD
fi

IFS='|' read -r RECOVERED_INCIDENTS RECOVERED_TICKETS LATEST_INC_REF LATEST_INC_OPENED_AT RPO_GAP_HOURS RPO_COMPLIANT <<< "${METRICS_RAW}"

mkdir -p "${BACKUP_DIR}"
LOG_FILE="${BACKUP_DIR}/restore-history.log"
RESULT_JSON="${BACKUP_DIR}/latest-restore-result.json"
ISO_END_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

cat > "${RESULT_JSON}" <<EOF
{
  "requirementId": "NFR-02",
  "testId": "OPS-BKP-01",
  "operation": "database_restore",
  "restoreStartedAtUtc": "${ISO_START_UTC}",
  "restoreCompletedAtUtc": "${ISO_END_UTC}",
  "destructionTimestampUtc": "${DESTRUCTION_TIMESTAMP_UTC}",
  "targetDatabase": "${PG_DB}",
  "backupFile": "${ARCHIVE_FILENAME}",
  "elapsedRecoveryMs": ${ELAPSED_MS},
  "elapsedRecoverySeconds": ${ELAPSED_SECONDS},
  "recoveredIncidentCount": ${RECOVERED_INCIDENTS:-0},
  "recoveredTicketCount": ${RECOVERED_TICKETS:-0},
  "latestRecoverableIncidentRef": "${LATEST_INC_REF}",
  "latestRecoverableIncidentOpenedAtUtc": "${LATEST_INC_OPENED_AT}",
  "rpoGapHours": ${RPO_GAP_HOURS:-0},
  "rpoMaxAllowedHours": 24.0,
  "rpoCompliant": ${RPO_COMPLIANT:-true},
  "exitCode": 0
}
EOF

LOG_LINE="[${ISO_END_UTC}] STATUS=SUCCESS OP=RESTORE DB=${PG_DB} FILE=${ARCHIVE_FILENAME} ELAPSED_MS=${ELAPSED_MS} ELAPSED_S=${ELAPSED_SECONDS} INCIDENTS=${RECOVERED_INCIDENTS:-0} TICKETS=${RECOVERED_TICKETS:-0} LATEST_INCIDENT=${LATEST_INC_REF} LATEST_OPENED_AT=${LATEST_INC_OPENED_AT} RPO_GAP_HOURS=${RPO_GAP_HOURS:-0} RPO_COMPLIANT=${RPO_COMPLIANT:-true}"
echo "${LOG_LINE}" >> "${LOG_FILE}"

echo "[OK] Database restore completed in ${ELAPSED_SECONDS}s (${ELAPSED_MS} ms)."
echo "BACKUP_FILE=${BACKUP_FILE}"
echo "TARGET_DATABASE=${PG_DB}"
echo "ELAPSED_RECOVERY_MS=${ELAPSED_MS}"
echo "ELAPSED_RECOVERY_SECONDS=${ELAPSED_SECONDS}"
echo "RECOVERED_INCIDENT_COUNT=${RECOVERED_INCIDENTS:-0}"
echo "RECOVERED_TICKET_COUNT=${RECOVERED_TICKETS:-0}"
echo "LATEST_INCIDENT_REF=${LATEST_INC_REF}"
echo "LATEST_INCIDENT_OPENED_AT=${LATEST_INC_OPENED_AT}"
echo "RPO_GAP_HOURS=${RPO_GAP_HOURS:-0}"
echo "RPO_COMPLIANT=${RPO_COMPLIANT:-true}"
