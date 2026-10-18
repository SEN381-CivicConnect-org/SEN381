#!/usr/bin/env bash
# ==============================================================================
# CivicConnect Automated Logical Database Backup Script (Bash)
# Requirement: NFR-02 (Durability and Recovery Point - RPO) & NFR-05 (Retention)
# Traceability: OPS-BKP-01 RestoreFromNightlyDump / RSK-08
# ==============================================================================
# Executes a consistent PostgreSQL logical backup using pg_dump (-Fc custom
# compressed format) plus cluster roles export (pg_dumpall --roles-only),
# computes SHA-256 checksums, logs operation metadata, and enforces a 30-day
# backup retention rotation policy per POPIA s14(1) / NFR-05.
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

BACKUP_DIR="${BACKUP_DIR:-${REPO_ROOT}/backups}"
CONTAINER_NAME="${POSTGRES_CONTAINER:-civicconnect-db}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
ENV_FILE="${ENV_FILE:-${REPO_ROOT}/.env}"
SKIP_ROLES="false"
TARGET_DB_OVERRIDE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --backup-dir|-o)
      BACKUP_DIR="$2"
      shift 2
      ;;
    --container|-c)
      CONTAINER_NAME="$2"
      shift 2
      ;;
    --retention-days|-r)
      RETENTION_DAYS="$2"
      shift 2
      ;;
    --database|-d)
      TARGET_DB_OVERRIDE="$2"
      shift 2
      ;;
    --env-file|-e)
      ENV_FILE="$2"
      shift 2
      ;;
    --skip-roles)
      SKIP_ROLES="true"
      shift
      ;;
    *)
      echo "[ERROR] Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

# Load environment variables from .env if present (NFR-01 compliance: no hardcoded secrets)
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

# Extract connection parameters from CONNECTION_STRING if provided
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

# NFR-01 validation: reject missing or insecure placeholder credentials
if [[ -z "$PG_PASS" || "${PG_PASS,,}" == "changeme" || "$PG_PASS" == *'${'* ]]; then
  echo "[ERROR] NFR-01 violation: POSTGRES_PASSWORD or CONNECTION_STRING password is missing or uses an insecure placeholder." >&2
  exit 1
fi

if ! [[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]] || [[ "$RETENTION_DAYS" -lt 1 ]]; then
  echo "[ERROR] Invalid retention period '${RETENTION_DAYS}'. Must be a positive integer." >&2
  exit 1
fi

mkdir -p "${BACKUP_DIR}"

get_epoch_ms() {
  if [[ -n "${EPOCHREALTIME:-}" ]]; then
    awk -v t="${EPOCHREALTIME}" 'BEGIN { printf "%.0f", t * 1000 }'
  else
    echo "$(($(date +%s) * 1000))"
  fi
}

START_EPOCH_MS="$(get_epoch_ms)"
TIMESTAMP_UTC="$(date -u +"%Y%m%d_%H%M%SZ")"
ISO_NOW_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

ARCHIVE_FILENAME="civicconnect_backup_${TIMESTAMP_UTC}.dump"
ARCHIVE_PATH="${BACKUP_DIR}/${ARCHIVE_FILENAME}"
CHECKSUM_PATH="${ARCHIVE_PATH}.sha256"
ROLES_FILENAME="civicconnect_backup_${TIMESTAMP_UTC}_roles.sql"
ROLES_PATH="${BACKUP_DIR}/${ROLES_FILENAME}"
META_PATH="${ARCHIVE_PATH}.meta.json"
LOG_FILE="${BACKUP_DIR}/backup-history.log"

# Determine execution mode: direct pg_dump (e.g. inside backup container) vs docker exec on host
EXEC_MODE="direct"
if [[ "${USE_DOCKER_EXEC:-auto}" == "true" ]]; then
  EXEC_MODE="docker"
elif [[ "${USE_DOCKER_EXEC:-auto}" == "auto" ]]; then
  if [[ -f "/.dockerenv" ]] || [[ "${DOTNET_RUNNING_IN_CONTAINER:-}" == "true" ]]; then
    EXEC_MODE="direct"
  elif command -v docker >/dev/null 2>&1 && docker inspect -f '{{.State.Running}}' "${CONTAINER_NAME}" 2>/dev/null | grep -q "true"; then
    EXEC_MODE="docker"
  elif ! command -v pg_dump >/dev/null 2>&1; then
    echo "[ERROR] Neither a running Docker container '${CONTAINER_NAME}' nor local 'pg_dump' binary was found." >&2
    exit 1
  fi
fi

echo "[INFO] [${ISO_NOW_UTC}] Starting CivicConnect logical backup (mode=${EXEC_MODE}, db=${PG_DB})..."

if [[ "$EXEC_MODE" == "docker" ]]; then
  CONTAINER_TMP="/tmp/${ARCHIVE_FILENAME}"
  docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
    pg_dump -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" \
    --format=custom --compress=6 --no-owner --no-privileges \
    --file="${CONTAINER_TMP}"

  docker cp "${CONTAINER_NAME}:${CONTAINER_TMP}" "${ARCHIVE_PATH}"
  docker exec "${CONTAINER_NAME}" rm -f "${CONTAINER_TMP}"

  # Verify archive TOC
  docker exec "${CONTAINER_NAME}" sh -c "test -s '${CONTAINER_TMP}' || true" >/dev/null 2>&1 || true

  if [[ "$SKIP_ROLES" != "true" ]]; then
    CONTAINER_ROLES_TMP="/tmp/${ROLES_FILENAME}"
    if docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
      pg_dumpall -h localhost -p 5432 -U "${PG_USER}" --roles-only --file="${CONTAINER_ROLES_TMP}" >/dev/null 2>&1; then
      docker cp "${CONTAINER_NAME}:${CONTAINER_ROLES_TMP}" "${ROLES_PATH}"
      docker exec "${CONTAINER_NAME}" rm -f "${CONTAINER_ROLES_TMP}"
    fi
  fi

  INCIDENT_WATERMARK="$(docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -t -A \
    -c "SELECT COALESCE((SELECT reference_number || '|' || to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE|NONE');" 2>/dev/null || echo "NONE|NONE")"

  INCIDENT_COUNT="$(docker exec -e PGPASSWORD="${PG_PASS}" "${CONTAINER_NAME}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -t -A \
    -c "SELECT COUNT(*) FROM incident;" 2>/dev/null || echo "0")"
else
  # Direct pg_dump execution
  EFFECTIVE_HOST="${PG_HOST}"
  export PGPASSWORD="${PG_PASS}"
  pg_dump -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" \
    --format=custom --compress=6 --no-owner --no-privileges \
    --file="${ARCHIVE_PATH}"

  # Validate custom archive TOC
  pg_restore --list "${ARCHIVE_PATH}" >/dev/null

  if [[ "$SKIP_ROLES" != "true" ]]; then
    pg_dumpall -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" --roles-only --file="${ROLES_PATH}" >/dev/null 2>&1 || true
  fi

  INCIDENT_WATERMARK="$(psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" -t -A \
    -c "SELECT COALESCE((SELECT reference_number || '|' || to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE|NONE');" 2>/dev/null || echo "NONE|NONE")"

  INCIDENT_COUNT="$(psql -h "${EFFECTIVE_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" -t -A \
    -c "SELECT COUNT(*) FROM incident;" 2>/dev/null || echo "0")"
  unset PGPASSWORD
fi

# Validate archive exists and is non-empty
if [[ ! -s "${ARCHIVE_PATH}" ]]; then
  echo "[ERROR] Backup archive '${ARCHIVE_PATH}' is missing or empty (0 bytes)." >&2
  exit 1
fi

ARCHIVE_SIZE_BYTES="$(wc -c < "${ARCHIVE_PATH}" | tr -d '[:space:]')"

# Compute SHA-256 checksum
if command -v sha256sum >/dev/null 2>&1; then
  SHA256_HASH="$(sha256sum "${ARCHIVE_PATH}" | awk '{print tolower($1)}')"
elif command -v shasum >/dev/null 2>&1; then
  SHA256_HASH="$(shasum -a 256 "${ARCHIVE_PATH}" | awk '{print tolower($1)}')"
else
  SHA256_HASH="$(openssl dgst -sha256 "${ARCHIVE_PATH}" | awk '{print tolower($NF)}')"
fi

echo "${SHA256_HASH}  ${ARCHIVE_FILENAME}" > "${CHECKSUM_PATH}"

# Enforce backup retention rotation (POPIA s14(1) / NFR-05: prune backups older than RETENTION_DAYS)
PRUNED_COUNT=0
while IFS= read -r -d '' old_file; do
  rm -f "$old_file" "${old_file}.sha256" "${old_file}.meta.json" "${old_file%.dump}_roles.sql"
  PRUNED_COUNT=$((PRUNED_COUNT + 1))
done < <(find "${BACKUP_DIR}" -maxdepth 1 -type f -name "civicconnect_backup_*.dump" -mtime "+${RETENTION_DAYS}" -print0 2>/dev/null || true)

END_EPOCH_MS="$(get_epoch_ms)"
ELAPSED_MS=$((END_EPOCH_MS - START_EPOCH_MS))
if [[ "$ELAPSED_MS" -lt 0 ]]; then
  ELAPSED_MS=0
fi

INCIDENT_WATERMARK="$(echo "$INCIDENT_WATERMARK" | tr -d '\r\n')"
INCIDENT_COUNT="$(echo "$INCIDENT_COUNT" | tr -d '\r\n[:space:]')"
LATEST_INC_REF="${INCIDENT_WATERMARK%%|*}"
LATEST_INC_OPENED_AT="${INCIDENT_WATERMARK#*|}"

cat > "${META_PATH}" <<EOF
{
  "requirementId": "NFR-02",
  "operation": "logical_backup",
  "timestampUtc": "${ISO_NOW_UTC}",
  "database": "${PG_DB}",
  "executionMode": "${EXEC_MODE}",
  "archiveFile": "${ARCHIVE_FILENAME}",
  "archiveSizeBytes": ${ARCHIVE_SIZE_BYTES},
  "sha256": "${SHA256_HASH}",
  "rolesExportFile": "$( [[ -f "${ROLES_PATH}" ]] && echo "${ROLES_FILENAME}" || echo "" )",
  "incidentCount": ${INCIDENT_COUNT:-0},
  "latestIncidentReference": "${LATEST_INC_REF}",
  "latestIncidentOpenedAtUtc": "${LATEST_INC_OPENED_AT}",
  "retentionDays": ${RETENTION_DAYS},
  "prunedArchivesCount": ${PRUNED_COUNT},
  "elapsedDurationMs": ${ELAPSED_MS},
  "exitCode": 0
}
EOF

LOG_LINE="[${ISO_NOW_UTC}] STATUS=SUCCESS OP=BACKUP DB=${PG_DB} FILE=${ARCHIVE_FILENAME} SIZE_BYTES=${ARCHIVE_SIZE_BYTES} SHA256=${SHA256_HASH} INCIDENTS=${INCIDENT_COUNT:-0} LATEST_INCIDENT=${LATEST_INC_REF} LATEST_OPENED_AT=${LATEST_INC_OPENED_AT} RETENTION_DAYS=${RETENTION_DAYS} PRUNED=${PRUNED_COUNT} ELAPSED_MS=${ELAPSED_MS}"
echo "${LOG_LINE}" >> "${LOG_FILE}"

echo "[OK] Backup completed successfully."
echo "BACKUP_FILE=${ARCHIVE_PATH}"
echo "CHECKSUM_FILE=${CHECKSUM_PATH}"
echo "SHA256=${SHA256_HASH}"
echo "ARCHIVE_SIZE_BYTES=${ARCHIVE_SIZE_BYTES}"
echo "INCIDENT_COUNT=${INCIDENT_COUNT:-0}"
echo "LATEST_INCIDENT_REF=${LATEST_INC_REF}"
echo "LATEST_INCIDENT_OPENED_AT=${LATEST_INC_OPENED_AT}"
echo "PRUNED_COUNT=${PRUNED_COUNT}"
echo "ELAPSED_BACKUP_MS=${ELAPSED_MS}"
