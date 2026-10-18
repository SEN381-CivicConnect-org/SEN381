#!/usr/bin/env bash
# ==============================================================================
# CivicConnect Automated Milestone Restore Drill Script (POSIX / Bash)
# Requirement: NFR-03 (Verified Restore / Recovery Time Objective - RTO <= 4h)
# RTM Reference: OPS-BKP-02 RestoreDrillRecord
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

BACKUP_FILE=""
ROLES_FILE=""
BACKUP_DIR="${BACKUP_DIR:-${REPO_ROOT}/backups}"
SOURCE_CONTAINER="${POSTGRES_CONTAINER:-civicconnect-db}"
SOURCE_DATABASE=""
DRILL_CONTAINER="civicconnect-drill-db"
DRILL_DATABASE="civicconnect_drill"
DRILL_PORT="5439"
POSTGRES_IMAGE="postgres:15-alpine"
ISOLATION_MODE="auto"
TEST_PROJECT="tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj"
RTO_THRESHOLD_HOURS="4"
OPERATOR=""
MILESTONE="M3"
ENV_FILE="${REPO_ROOT}/.env"
TAKE_FRESH_BACKUP="false"
SKIP_CHECKSUM="false"
KEEP_INSTANCE="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --file|-f)
      BACKUP_FILE="$2"
      shift 2
      ;;
    --roles-file)
      ROLES_FILE="$2"
      shift 2
      ;;
    --backup-dir|-o)
      BACKUP_DIR="$2"
      shift 2
      ;;
    --source-container)
      SOURCE_CONTAINER="$2"
      shift 2
      ;;
    --source-database)
      SOURCE_DATABASE="$2"
      shift 2
      ;;
    --drill-container)
      DRILL_CONTAINER="$2"
      shift 2
      ;;
    --drill-database)
      DRILL_DATABASE="$2"
      shift 2
      ;;
    --drill-port)
      DRILL_PORT="$2"
      shift 2
      ;;
    --postgres-image)
      POSTGRES_IMAGE="$2"
      shift 2
      ;;
    --isolation-mode)
      ISOLATION_MODE="$2"
      shift 2
      ;;
    --test-project)
      TEST_PROJECT="$2"
      shift 2
      ;;
    --rto-threshold-hours)
      RTO_THRESHOLD_HOURS="$2"
      shift 2
      ;;
    --operator)
      OPERATOR="$2"
      shift 2
      ;;
    --milestone)
      MILESTONE="$2"
      shift 2
      ;;
    --env-file)
      ENV_FILE="$2"
      shift 2
      ;;
    --take-fresh-backup)
      TAKE_FRESH_BACKUP="true"
      shift
      ;;
    --skip-checksum)
      SKIP_CHECKSUM="true"
      shift
      ;;
    --keep-instance)
      KEEP_INSTANCE="true"
      shift
      ;;
    *)
      echo "[ERROR] Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

# Load .env variables without overwriting existing environment variables (NFR-01)
if [[ -f "$ENV_FILE" ]]; then
  while IFS= read -r line || [[ -n "$line" ]]; do
    trimmed="${line#"${line%%[![:space:]]*}"}"
    trimmed="${trimmed%"${trimmed##*[![:space:]]}"}"
    if [[ -z "$trimmed" || "$trimmed" == \#* ]]; then
      continue
    fi
    if [[ "$trimmed" == *=* ]]; then
      key="${trimmed%%=*}"
      val="${trimmed#*=}"
      val="${val%$'\r'}"
      val="${val#\"}"
      val="${val%\"}"
      val="${val#\'}"
      val="${val%\'}"
      if [[ -z "${!key:-}" ]]; then
        export "$key=$val"
      fi
    fi
  done < "$ENV_FILE"
fi

ORIG_CONNECTION_STRING="${CONNECTION_STRING:-}"
CONN_HOST=""
CONN_PORT=""
CONN_DB=""
CONN_USER=""
CONN_PASS=""

if [[ -n "$ORIG_CONNECTION_STRING" ]]; then
  IFS=';' read -ra ADDR <<< "$ORIG_CONNECTION_STRING"
  for segment in "${ADDR[@]}"; do
    if [[ "$segment" == *=* ]]; then
      k="${segment%%=*}"
      v="${segment#*=}"
      k_lower="$(echo "$k" | tr '[:upper:]' '[:lower:]' | xargs)"
      v_clean="$(echo "$v" | xargs)"
      case "$k_lower" in
        host|server) CONN_HOST="$v_clean" ;;
        port) CONN_PORT="$v_clean" ;;
        database|"initial catalog") CONN_DB="$v_clean" ;;
        username|user|"user id"|uid) CONN_USER="$v_clean" ;;
        password|pwd) CONN_PASS="$v_clean" ;;
      esac
    fi
  done
fi

PG_HOST="${POSTGRES_HOST:-${CONN_HOST:-localhost}}"
PG_PORT="${POSTGRES_PORT:-${CONN_PORT:-5432}}"
PG_DB="${SOURCE_DATABASE:-${POSTGRES_DB:-${CONN_DB:-civicconnect}}}"
PG_USER="${POSTGRES_USER:-${CONN_USER:-civicconnect}}"
PG_PASS="${POSTGRES_PASSWORD:-${CONN_PASS:-}}"

if [[ "$PG_HOST" == "db" ]]; then
  PG_HOST="localhost"
fi

# NFR-01 validation
if [[ -z "$PG_PASS" || "${PG_PASS,,}" == "changeme" || "$PG_PASS" == *"\${"* ]]; then
  echo "[ERROR] NFR-01 violation: POSTGRES_PASSWORD or CONNECTION_STRING password is missing or uses an insecure placeholder." >&2
  exit 1
fi

if [[ -z "$OPERATOR" ]]; then
  GIT_NAME="$(git -C "${REPO_ROOT}" config user.name 2>/dev/null || true)"
  GIT_EMAIL="$(git -C "${REPO_ROOT}" config user.email 2>/dev/null || true)"
  if [[ -n "$GIT_NAME" ]]; then
    if [[ -n "$GIT_EMAIL" ]]; then
      OPERATOR="${GIT_NAME} <${GIT_EMAIL}>"
    else
      OPERATOR="${GIT_NAME}"
    fi
  else
    OPERATOR="${USER:-automated-drill-operator}"
  fi
fi

mkdir -p "$BACKUP_DIR"
BACKUP_DIR="$(cd "$BACKUP_DIR" && pwd)"

get_epoch_ms() {
  if [[ -n "${EPOCHREALTIME:-}" ]]; then
    awk -v t="${EPOCHREALTIME}" 'BEGIN { printf "%.0f", t * 1000 }'
  else
    echo "$(($(date +%s) * 1000))"
  fi
}

compute_sha256() {
  local target_file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$target_file" | awk '{print tolower($1)}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$target_file" | awk '{print tolower($1)}'
  elif command -v openssl >/dev/null 2>&1; then
    openssl dgst -sha256 "$target_file" | awk '{print tolower($NF)}'
  else
    echo "[ERROR] No SHA-256 utility available." >&2
    exit 1
  fi
}

DOCKER_AVAILABLE="false"
SOURCE_CONTAINER_RUNNING="false"
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  DOCKER_AVAILABLE="true"
  if docker inspect -f '{{.State.Running}}' "${SOURCE_CONTAINER}" 2>/dev/null | grep -q "true"; then
    SOURCE_CONTAINER_RUNNING="true"
  fi
fi

EFFECTIVE_ISOLATION_MODE="${ISOLATION_MODE,,}"
if [[ "$EFFECTIVE_ISOLATION_MODE" == "auto" ]]; then
  if [[ "$DOCKER_AVAILABLE" == "true" ]]; then
    EFFECTIVE_ISOLATION_MODE="container"
  else
    EFFECTIVE_ISOLATION_MODE="database"
  fi
fi

ensure_source_baseline_data() {
  if [[ "$SOURCE_CONTAINER_RUNNING" != "true" ]]; then
    return 0
  fi

  local clean_sql="BEGIN;
SET LOCAL session_replication_role = 'replica';
DELETE FROM audit_log a
WHERE NOT EXISTS (SELECT 1 FROM app_user u WHERE u.id = a.actor_id)
   OR (a.incident_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM incident i WHERE i.id = a.incident_id));
COMMIT;"

  echo "$clean_sql" | docker exec -i -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -v ON_ERROR_STOP=1 >/dev/null 2>&1 || true

  local inc_count
  inc_count="$(docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -t -A -c "SELECT COUNT(*) FROM incident;" 2>/dev/null | tr -d '\r\n[:space:]' || echo "0")"

  if [[ "${inc_count:-0}" -gt 0 ]]; then
    return 0
  fi

  cat <<'EOF' | docker exec -i -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" psql -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" -v ON_ERROR_STOP=1 >/dev/null
DO $$
DECLARE
    v_reporter_id uuid;
    v_tech_id uuid;
    v_team_id int;
    v_cat_id int;
    v_loc_id int;
    v_asset_id int;
    v_inc_id bigint;
BEGIN
    SELECT id INTO v_reporter_id FROM app_user ORDER BY created_at ASC LIMIT 1;
    IF v_reporter_id IS NULL THEN
        INSERT INTO app_user (email, full_name, password_hash, is_active)
        VALUES ('drill.reporter@civicconnect.local', 'Drill Verification Reporter', '$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g', true)
        RETURNING id INTO v_reporter_id;
    END IF;

    SELECT id INTO v_tech_id FROM app_user WHERE id <> v_reporter_id ORDER BY created_at ASC LIMIT 1;
    IF v_tech_id IS NULL THEN
        v_tech_id := v_reporter_id;
    END IF;

    SELECT id INTO v_team_id FROM service_team WHERE name = 'Campus Facilities & Infrastructure' LIMIT 1;
    IF v_team_id IS NULL THEN
        INSERT INTO service_team (name) VALUES ('Campus Facilities & Infrastructure') RETURNING id INTO v_team_id;
    END IF;

    SELECT id INTO v_cat_id FROM category WHERE name = 'Electrical & Power Distribution' AND is_active = true LIMIT 1;
    IF v_cat_id IS NULL THEN
        INSERT INTO category (name, description, urgency_level, default_service_team_id, is_active)
        VALUES ('Electrical & Power Distribution', 'Power outages, distribution board faults, and backup generator issues', 3, v_team_id, true)
        RETURNING id INTO v_cat_id;
    END IF;

    SELECT id INTO v_loc_id FROM location WHERE name = 'Main Academic Block - Server Room B104' LIMIT 1;
    IF v_loc_id IS NULL THEN
        INSERT INTO location (name, location_kind_id, impact_level, is_active)
        VALUES ('Main Academic Block - Server Room B104', (SELECT id FROM location_kind WHERE name = 'room' LIMIT 1), 3, true)
        RETURNING id INTO v_loc_id;
    END IF;

    SELECT id INTO v_asset_id FROM asset WHERE asset_tag = 'UPS-B104-01' LIMIT 1;
    IF v_asset_id IS NULL THEN
        INSERT INTO asset (asset_tag, description, supplier, purchase_date, location_id, is_active)
        VALUES ('UPS-B104-01', 'APC Smart-UPS 3000VA Rackmount', 'Schneider Electric SA', '2025-02-10', v_loc_id, true)
        RETURNING id INTO v_asset_id;
    END IF;

    INSERT INTO incident (
        category_id, location_id, assigned_team_id,
        title, description, status,
        computed_priority, priority, priority_overridden, opened_at
    ) VALUES (
        v_cat_id, v_loc_id, v_team_id,
        'UPS battery fault alarm in Server Room B104',
        'Rackmount UPS unit UPS-B104-01 triggered an audible battery fault warning during load test.',
        'NEW', 2, 2, false, now()
    ) RETURNING id INTO v_inc_id;

    INSERT INTO ticket (
        incident_id, reporter_id, category_id, location_id, title, description, submitted_at
    ) VALUES (
        v_inc_id, v_reporter_id, v_cat_id, v_loc_id,
        'UPS battery fault alarm in Server Room B104',
        'Rackmount UPS unit UPS-B104-01 triggered an audible battery fault warning during load test.',
        now()
    );

    INSERT INTO incident_subscription (incident_id, reporter_id)
    VALUES (v_inc_id, v_reporter_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO incident_status_history (incident_id, from_status, to_status, changed_by, assigned_team_id, note)
    VALUES
        (v_inc_id, NULL, 'NEW', v_reporter_id, v_team_id, 'Initial incident created from reporter submission.');
END $$;
EOF
}

capture_backup_if_needed() {
  ensure_source_baseline_data

  if [[ -x "${SCRIPT_DIR}/backup-db.sh" ]] || [[ -f "${SCRIPT_DIR}/backup-db.sh" ]]; then
    echo "[INFO] Invoking NFR-02 backup script '${SCRIPT_DIR}/backup-db.sh'..." >&2
    local bkp_out
    bkp_out="$(bash "${SCRIPT_DIR}/backup-db.sh" --backup-dir "${BACKUP_DIR}" --container "${SOURCE_CONTAINER}" --database "${PG_DB}")"
    echo "$bkp_out" | awk -F= '/^BACKUP_FILE=/ {print $2}' | tail -n 1
    return 0
  fi

  local ts_token
  ts_token="$(date -u +"%Y%m%d_%H%M%SZ")"
  local archive_name="civicconnect_backup_${ts_token}.dump"
  local archive_full="${BACKUP_DIR}/${archive_name}"
  local checksum_full="${archive_full}.sha256"
  local roles_name="civicconnect_backup_${ts_token}_roles.sql"
  local roles_full="${BACKUP_DIR}/${roles_name}"

  if [[ "$SOURCE_CONTAINER_RUNNING" == "true" ]]; then
    local tmp_dump="/tmp/${archive_name}"
    docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
      pg_dump -h localhost -p 5432 -U "${PG_USER}" -d "${PG_DB}" \
      --format=custom --compress=6 --no-owner --no-privileges \
      --file="${tmp_dump}" >/dev/null
    docker cp "${SOURCE_CONTAINER}:${tmp_dump}" "${archive_full}"
    docker exec "${SOURCE_CONTAINER}" pg_restore --list "${tmp_dump}" >/dev/null
    docker exec "${SOURCE_CONTAINER}" rm -f "${tmp_dump}"

    local tmp_roles="/tmp/${roles_name}"
    if docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
      pg_dumpall -h localhost -p 5432 -U "${PG_USER}" --roles-only --file="${tmp_roles}" 2>/dev/null; then
      docker cp "${SOURCE_CONTAINER}:${tmp_roles}" "${roles_full}"
      docker exec "${SOURCE_CONTAINER}" rm -f "${tmp_roles}"
    fi
  else
    PGPASSWORD="${PG_PASS}" pg_dump -h "${PG_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d "${PG_DB}" \
      --format=custom --compress=6 --no-owner --no-privileges \
      --file="${archive_full}"
    pg_restore --list "${archive_full}" >/dev/null
    if command -v pg_dumpall >/dev/null 2>&1; then
      PGPASSWORD="${PG_PASS}" pg_dumpall -h "${PG_HOST}" -p "${PG_PORT}" -U "${PG_USER}" \
        --roles-only --file="${roles_full}" 2>/dev/null || true
    fi
  fi

  local sha_val
  sha_val="$(compute_sha256 "${archive_full}")"
  printf "%s  %s\n" "${sha_val}" "${archive_name}" > "${checksum_full}"
  echo "${archive_full}"
}

# Step 0: Resolve backup archive and role export
if [[ "$TAKE_FRESH_BACKUP" == "true" ]]; then
  BACKUP_FILE="$(capture_backup_if_needed)"
elif [[ -z "$BACKUP_FILE" ]]; then
  LATEST_DUMP="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'civicconnect_backup_*.dump' -printf '%T@ %p\n' 2>/dev/null | sort -nr | head -n 1 | cut -d' ' -f2- || true)"
  if [[ -z "$LATEST_DUMP" ]]; then
    LATEST_DUMP="$(ls -1t "${BACKUP_DIR}"/civicconnect_backup_*.dump 2>/dev/null | head -n 1 || true)"
  fi
  if [[ -z "$LATEST_DUMP" ]]; then
    echo "[INFO] No backup archive found in '${BACKUP_DIR}'. Capturing fresh backup..."
    BACKUP_FILE="$(capture_backup_if_needed)"
  else
    BACKUP_FILE="$LATEST_DUMP"
  fi
elif [[ "$BACKUP_FILE" != /* ]]; then
  if [[ -f "$BACKUP_FILE" ]]; then
    BACKUP_FILE="$(cd "$(dirname "$BACKUP_FILE")" && pwd)/$(basename "$BACKUP_FILE")"
  else
    BACKUP_FILE="${BACKUP_DIR}/${BACKUP_FILE}"
  fi
fi

if [[ ! -f "$BACKUP_FILE" ]]; then
  echo "[ERROR] Target backup archive '${BACKUP_FILE}' does not exist." >&2
  exit 1
fi

ARCHIVE_FILENAME="$(basename "$BACKUP_FILE")"
ARCHIVE_SIZE_BYTES="$(wc -c < "$BACKUP_FILE" | tr -d '[:space:]')"
ARCHIVE_CREATED_UTC="$(date -u -r "$BACKUP_FILE" +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")"

if [[ -z "$ROLES_FILE" ]]; then
  BASE_PREFIX="${BACKUP_FILE%.dump}"
  if [[ -f "${BASE_PREFIX}_roles.sql" ]]; then
    ROLES_FILE="${BASE_PREFIX}_roles.sql"
  fi
fi

ROLES_FILENAME="NONE"
ROLES_SIZE_BYTES=0
if [[ -n "$ROLES_FILE" && -f "$ROLES_FILE" ]]; then
  ROLES_FILENAME="$(basename "$ROLES_FILE")"
  ROLES_SIZE_BYTES="$(wc -c < "$ROLES_FILE" | tr -d '[:space:]')"
fi

ACTUAL_SHA256="$(compute_sha256 "$BACKUP_FILE")"
CHECKSUM_FILE="${BACKUP_FILE}.sha256"
CHECKSUM_VERIFIED="false"

if [[ "$SKIP_CHECKSUM" != "true" ]]; then
  if [[ -f "$CHECKSUM_FILE" ]]; then
    EXPECTED_SHA256="$(awk '{print tolower($1)}' "$CHECKSUM_FILE" | head -n 1 | tr -d '\r\n')"
    if [[ "$ACTUAL_SHA256" != "$EXPECTED_SHA256" ]]; then
      echo "[ERROR] SHA-256 mismatch for '${ARCHIVE_FILENAME}'! Expected: ${EXPECTED_SHA256}, Actual: ${ACTUAL_SHA256}" >&2
      exit 1
    fi
    CHECKSUM_VERIFIED="true"
    echo "[INFO] Verified SHA-256 checksum for '${ARCHIVE_FILENAME}' (${ACTUAL_SHA256})."
  else
    printf "%s  %s\n" "${ACTUAL_SHA256}" "${ARCHIVE_FILENAME}" > "${CHECKSUM_FILE}"
    CHECKSUM_VERIFIED="true"
  fi
fi

ISO_DRILL_START_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
DRILL_START_MS="$(get_epoch_ms)"
TRX_DIR="${BACKUP_DIR}/drill-test-results"

DRILL_TARGET_HOST="localhost"
DRILL_TARGET_PORT="${DRILL_PORT}"
DRILL_TARGET_DB="${DRILL_DATABASE}"

cleanup_drill() {
  if [[ -n "$ORIG_CONNECTION_STRING" ]]; then
    export CONNECTION_STRING="$ORIG_CONNECTION_STRING"
  else
    unset CONNECTION_STRING || true
  fi

  if [[ "$KEEP_INSTANCE" != "true" ]]; then
    if [[ "$EFFECTIVE_ISOLATION_MODE" == "container" ]]; then
      docker rm -f "${DRILL_CONTAINER}" >/dev/null 2>&1 || true
    elif [[ -n "$DRILL_TARGET_DB" && "$DRILL_TARGET_DB" != "$PG_DB" ]]; then
      if [[ "$SOURCE_CONTAINER_RUNNING" == "true" ]]; then
        docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
          psql -h localhost -p 5432 -U "${PG_USER}" -d postgres \
          -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${DRILL_TARGET_DB}' AND pid <> pg_backend_pid();" >/dev/null 2>&1 || true
        docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
          psql -h localhost -p 5432 -U "${PG_USER}" -d postgres \
          -c "DROP DATABASE IF EXISTS \"${DRILL_TARGET_DB}\";" >/dev/null 2>&1 || true
      else
        PGPASSWORD="${PG_PASS}" psql -h "${PG_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres \
          -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '${DRILL_TARGET_DB}' AND pid <> pg_backend_pid();" >/dev/null 2>&1 || true
        PGPASSWORD="${PG_PASS}" psql -h "${PG_HOST}" -p "${PG_PORT}" -U "${PG_USER}" -d postgres \
          -c "DROP DATABASE IF EXISTS \"${DRILL_TARGET_DB}\";" >/dev/null 2>&1 || true
      fi
    fi
    rm -rf "${TRX_DIR}" >/dev/null 2>&1 || true
  fi
}
trap cleanup_drill EXIT

# Phase 1: Clean Instance Preparation
PREP_START_MS="$(get_epoch_ms)"
echo "[PHASE 1/4] Provisioning clean isolated PostgreSQL drill instance (mode=${EFFECTIVE_ISOLATION_MODE})..."

if [[ "$EFFECTIVE_ISOLATION_MODE" == "container" ]]; then
  docker rm -f "${DRILL_CONTAINER}" >/dev/null 2>&1 || true
  docker run -d \
    --name "${DRILL_CONTAINER}" \
    -e "POSTGRES_DB=${DRILL_DATABASE}" \
    -e "POSTGRES_USER=${PG_USER}" \
    -e "POSTGRES_PASSWORD=${PG_PASS}" \
    -p "${DRILL_PORT}:5432" \
    "${POSTGRES_IMAGE}" >/dev/null

  READY="false"
  for _ in $(seq 1 60); do
    if docker exec "${DRILL_CONTAINER}" pg_isready -h localhost -p 5432 -U "${PG_USER}" -d "${DRILL_DATABASE}" >/dev/null 2>&1; then
      PING_OUT="$(docker exec -e PGPASSWORD="${PG_PASS}" "${DRILL_CONTAINER}" \
        psql -h localhost -p 5432 -U "${PG_USER}" -d "${DRILL_DATABASE}" -t -A -c "SELECT 1;" 2>/dev/null | tr -d '\r\n[:space:]' || true)"
      if [[ "$PING_OUT" == "1" ]]; then
        READY="true"
        break
      fi
    fi
    sleep 0.25
  done

  if [[ "$READY" != "true" ]]; then
    echo "[ERROR] Timed out waiting for ephemeral drill container '${DRILL_CONTAINER}'." >&2
    exit 1
  fi

  DRILL_TARGET_HOST="localhost"
  DRILL_TARGET_PORT="${DRILL_PORT}"
  DRILL_TARGET_DB="${DRILL_DATABASE}"
else
  DRILL_TARGET_HOST="${PG_HOST}"
  DRILL_TARGET_PORT="${PG_PORT}"
  DRILL_TARGET_DB="${DRILL_DATABASE}_$(date -u +"%Y%m%d_%H%M%S")"

  if [[ "$SOURCE_CONTAINER_RUNNING" == "true" ]]; then
    docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "DROP DATABASE IF EXISTS \"${DRILL_TARGET_DB}\";" >/dev/null
    docker exec -e PGPASSWORD="${PG_PASS}" "${SOURCE_CONTAINER}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "CREATE DATABASE \"${DRILL_TARGET_DB}\";" >/dev/null
  else
    PGPASSWORD="${PG_PASS}" psql -h "${DRILL_TARGET_HOST}" -p "${DRILL_TARGET_PORT}" -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "DROP DATABASE IF EXISTS \"${DRILL_TARGET_DB}\";" >/dev/null
    PGPASSWORD="${PG_PASS}" psql -h "${DRILL_TARGET_HOST}" -p "${DRILL_TARGET_PORT}" -U "${PG_USER}" -d postgres -v ON_ERROR_STOP=1 \
      -c "CREATE DATABASE \"${DRILL_TARGET_DB}\";" >/dev/null
  fi
fi

PREP_END_MS="$(get_epoch_ms)"
INSTANCE_PREP_MS=$((PREP_END_MS - PREP_START_MS))
INSTANCE_PREP_SECONDS="$(awk -v ms="$INSTANCE_PREP_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"

# Phase 2: Role Export & Backup Archive Restore (RTO Clock Starts Here)
ISO_RESTORE_START_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
RTO_START_MS="$(get_epoch_ms)"
RESTORE_START_MS="${RTO_START_MS}"

echo "[PHASE 2/4] [${ISO_RESTORE_START_UTC}] Restoring role export and backup archive '${ARCHIVE_FILENAME}'..."

TARGET_EXEC_CONTAINER="${SOURCE_CONTAINER}"
USE_CONTAINER_EXEC="${SOURCE_CONTAINER_RUNNING}"
if [[ "$EFFECTIVE_ISOLATION_MODE" == "container" ]]; then
  TARGET_EXEC_CONTAINER="${DRILL_CONTAINER}"
  USE_CONTAINER_EXEC="true"
fi

ROLES_START_MS="$(get_epoch_ms)"
if [[ "$ROLES_FILENAME" != "NONE" ]]; then
  if [[ "$USE_CONTAINER_EXEC" == "true" ]]; then
    docker cp "${ROLES_FILE}" "${TARGET_EXEC_CONTAINER}:/tmp/drill_roles.sql"
    docker exec -e PGPASSWORD="${PG_PASS}" "${TARGET_EXEC_CONTAINER}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d postgres -f /tmp/drill_roles.sql >/dev/null 2>&1 || true
    docker exec "${TARGET_EXEC_CONTAINER}" rm -f /tmp/drill_roles.sql >/dev/null 2>&1 || true
  else
    PGPASSWORD="${PG_PASS}" psql -h "${DRILL_TARGET_HOST}" -p "${DRILL_TARGET_PORT}" -U "${PG_USER}" -d postgres \
      -f "${ROLES_FILE}" >/dev/null 2>&1 || true
  fi
fi
ROLES_END_MS="$(get_epoch_ms)"
ROLES_RESTORE_MS=$((ROLES_END_MS - ROLES_START_MS))

if [[ -f "${SCRIPT_DIR}/restore-db.sh" ]]; then
  bash "${SCRIPT_DIR}/restore-db.sh" \
    --file "${BACKUP_FILE}" \
    --database "${DRILL_TARGET_DB}" \
    --container "${TARGET_EXEC_CONTAINER}" \
    --skip-checksum >/dev/null
else
  if [[ "$USE_CONTAINER_EXEC" == "true" ]]; then
    docker cp "${BACKUP_FILE}" "${TARGET_EXEC_CONTAINER}:/tmp/drill_restore.dump"
    docker exec -e PGPASSWORD="${PG_PASS}" "${TARGET_EXEC_CONTAINER}" \
      psql -h localhost -p 5432 -U "${PG_USER}" -d "${DRILL_TARGET_DB}" -v ON_ERROR_STOP=1 \
      -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" >/dev/null
    docker exec -e PGPASSWORD="${PG_PASS}" "${TARGET_EXEC_CONTAINER}" \
      pg_restore -h localhost -p 5432 -U "${PG_USER}" -d "${DRILL_TARGET_DB}" \
      --clean --if-exists --no-owner --no-privileges --exit-on-error \
      /tmp/drill_restore.dump
    docker exec "${TARGET_EXEC_CONTAINER}" rm -f /tmp/drill_restore.dump >/dev/null 2>&1 || true
  else
    PGPASSWORD="${PG_PASS}" psql -h "${DRILL_TARGET_HOST}" -p "${DRILL_TARGET_PORT}" -U "${PG_USER}" -d "${DRILL_TARGET_DB}" -v ON_ERROR_STOP=1 \
      -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" >/dev/null
    PGPASSWORD="${PG_PASS}" pg_restore -h "${DRILL_TARGET_HOST}" -p "${DRILL_TARGET_PORT}" -U "${PG_USER}" -d "${DRILL_TARGET_DB}" \
      --clean --if-exists --no-owner --no-privileges --exit-on-error \
      "${BACKUP_FILE}"
  fi
fi

RESTORE_END_MS="$(get_epoch_ms)"
RESTORE_MS=$((RESTORE_END_MS - RESTORE_START_MS))
RESTORE_SECONDS="$(awk -v ms="$RESTORE_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"

# Post-restore schema verification
VERIFY_START_MS="$(get_epoch_ms)"
VERIFY_SQL="SELECT
  (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name <> '__ef_migrations_history') || '|' ||
  (SELECT COUNT(*) FROM __ef_migrations_history) || '|' ||
  (SELECT COUNT(*) FROM role) || '|' ||
  (SELECT COUNT(*) FROM priority_target) || '|' ||
  (SELECT COUNT(*) FROM location_kind) || '|' ||
  (SELECT COUNT(*) FROM resolution_code) || '|' ||
  (SELECT COUNT(*) FROM incident) || '|' ||
  (SELECT COUNT(*) FROM ticket) || '|' ||
  (SELECT COUNT(*) FROM app_user) || '|' ||
  COALESCE((SELECT reference_number FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE') || '|' ||
  COALESCE((SELECT to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE');"

if [[ "$USE_CONTAINER_EXEC" == "true" ]]; then
  VERIFY_RAW="$(docker exec -e PGPASSWORD="${PG_PASS}" "${TARGET_EXEC_CONTAINER}" \
    psql -h localhost -p 5432 -U "${PG_USER}" -d "${DRILL_TARGET_DB}" -t -A -c "${VERIFY_SQL}" | tr -d '\r\n')"
else
  VERIFY_RAW="$(PGPASSWORD="${PG_PASS}" psql -h "${DRILL_TARGET_HOST}" -p "${DRILL_TARGET_PORT}" -U "${PG_USER}" -d "${DRILL_TARGET_DB}" -t -A -c "${VERIFY_SQL}" | tr -d '\r\n')"
fi

IFS='|' read -r RESTORED_TABLES RESTORED_MIGRATIONS RESTORED_ROLES RESTORED_PRIORITIES RESTORED_LOC_KINDS RESTORED_RES_CODES RECOVERED_INCIDENTS RECOVERED_TICKETS RECOVERED_USERS LATEST_INC_REF LATEST_INC_OPENED_UTC <<< "${VERIFY_RAW}"
VERIFY_END_MS="$(get_epoch_ms)"
SCHEMA_VERIFY_MS=$((VERIFY_END_MS - VERIFY_START_MS))
SCHEMA_VERIFY_SECONDS="$(awk -v ms="$SCHEMA_VERIFY_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"

if [[ "${RESTORED_TABLES:-0}" -lt 21 ]]; then
  echo "[ERROR] Schema verification failed: expected >= 21 tables, found ${RESTORED_TABLES:-0}." >&2
  exit 1
fi

# Phase 3: Integration Test Suite Execution
echo "[PHASE 3/4] Running integration test suite '${TEST_PROJECT}' against restored instance..."
export CONNECTION_STRING="Host=${DRILL_TARGET_HOST};Port=${DRILL_TARGET_PORT};Database=${DRILL_TARGET_DB};Username=${PG_USER};Password=${PG_PASS}"

rm -rf "${TRX_DIR}" >/dev/null 2>&1 || true
mkdir -p "${TRX_DIR}"

TEST_START_MS="$(get_epoch_ms)"
set +e
TEST_OUT="$(dotnet test "${REPO_ROOT}/${TEST_PROJECT}" \
  --logger "trx;LogFileName=restore-drill-results.trx" \
  --results-directory "${TRX_DIR}" \
  --verbosity normal 2>&1)"
TEST_EXIT_CODE=$?
set -e
TEST_END_MS="$(get_epoch_ms)"

RTO_END_MS="${TEST_END_MS}"
ISO_TEST_COMPLETED_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

TEST_EXECUTION_MS=$((TEST_END_MS - TEST_START_MS))
TEST_EXECUTION_SECONDS="$(awk -v ms="$TEST_EXECUTION_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"
RTO_ELAPSED_MS=$((RTO_END_MS - RTO_START_MS))
RTO_ELAPSED_SECONDS="$(awk -v ms="$RTO_ELAPSED_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"
RTO_ELAPSED_MINUTES="$(awk -v ms="$RTO_ELAPSED_MS" 'BEGIN { printf "%.4f", ms / 60000.0 }')"
RTO_ELAPSED_HOURS="$(awk -v ms="$RTO_ELAPSED_MS" 'BEGIN { printf "%.6f", ms / 3600000.0 }')"

TESTS_TOTAL="$(echo "$TEST_OUT" | sed -n 's/.*Total:[[:space:]]*\([0-9]*\).*/\1/p' | tail -n 1)"
TESTS_PASSED="$(echo "$TEST_OUT" | sed -n 's/.*Passed:[[:space:]]*\([0-9]*\).*/\1/p' | tail -n 1)"
TESTS_FAILED="$(echo "$TEST_OUT" | sed -n 's/.*Failed:[[:space:]]*\([0-9]*\).*/\1/p' | tail -n 1)"
TESTS_SKIPPED="$(echo "$TEST_OUT" | sed -n 's/.*Skipped:[[:space:]]*\([0-9]*\).*/\1/p' | tail -n 1)"

TESTS_TOTAL="${TESTS_TOTAL:-0}"
TESTS_PASSED="${TESTS_PASSED:-0}"
TESTS_FAILED="${TESTS_FAILED:-0}"
TESTS_SKIPPED="${TESTS_SKIPPED:-0}"

# Phase 4: Clean Teardown before writing final metrics
CLEANUP_START_MS="$(get_epoch_ms)"
cleanup_drill
trap - EXIT
CLEANUP_END_MS="$(get_epoch_ms)"
CLEANUP_MS=$((CLEANUP_END_MS - CLEANUP_START_MS))
CLEANUP_SECONDS="$(awk -v ms="$CLEANUP_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"

DRILL_END_MS="$(get_epoch_ms)"
TOTAL_DRILL_MS=$((DRILL_END_MS - DRILL_START_MS))
TOTAL_DRILL_SECONDS="$(awk -v ms="$TOTAL_DRILL_MS" 'BEGIN { printf "%.3f", ms / 1000.0 }')"
ISO_DRILL_END_UTC="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

RTO_THRESHOLD_SECONDS="$(awk -v h="$RTO_THRESHOLD_HOURS" 'BEGIN { printf "%.3f", h * 3600.0 }')"
ALL_TESTS_PASSED="false"
if [[ "$TEST_EXIT_CODE" -eq 0 && "$TESTS_TOTAL" -gt 0 && "$TESTS_FAILED" -eq 0 && "$TESTS_PASSED" -eq "$TESTS_TOTAL" ]]; then
  ALL_TESTS_PASSED="true"
fi

RTO_COMPLIANT="$(awk -v e="$RTO_ELAPSED_SECONDS" -v max="$RTO_THRESHOLD_SECONDS" 'BEGIN { if (e <= max) print "true"; else print "false" }')"
DRILL_PASSED="false"
if [[ "$ALL_TESTS_PASSED" == "true" && "$RTO_COMPLIANT" == "true" && "$CHECKSUM_VERIFIED" == "true" ]]; then
  DRILL_PASSED="true"
fi

STATUS_TEXT="FAIL"
if [[ "$DRILL_PASSED" == "true" ]]; then
  STATUS_TEXT="PASS"
fi

RESULT_JSON="${BACKUP_DIR}/latest-restore-drill-result.json"
HISTORY_LOG="${BACKUP_DIR}/restore-drill-history.log"

cat > "${RESULT_JSON}" <<EOF
{
  "requirementId": "NFR-03",
  "verificationId": "OPS-BKP-02",
  "operation": "milestone_restore_drill",
  "milestone": "${MILESTONE}",
  "operator": "${OPERATOR}",
  "drillStartedAtUtc": "${ISO_DRILL_START_UTC}",
  "restoreStartedAtUtc": "${ISO_RESTORE_START_UTC}",
  "lastPassingTestCompletedAtUtc": "${ISO_TEST_COMPLETED_UTC}",
  "drillCompletedAtUtc": "${ISO_DRILL_END_UTC}",
  "isolationMode": "${EFFECTIVE_ISOLATION_MODE}",
  "drillDatabase": "${DRILL_TARGET_DB}",
  "drillPort": ${DRILL_TARGET_PORT},
  "postgresImage": "${POSTGRES_IMAGE}",
  "backupArtifact": {
    "archiveFile": "${ARCHIVE_FILENAME}",
    "archiveCreatedUtc": "${ARCHIVE_CREATED_UTC}",
    "archiveSizeBytes": ${ARCHIVE_SIZE_BYTES},
    "sha256": "${ACTUAL_SHA256}",
    "checksumVerified": ${CHECKSUM_VERIFIED},
    "rolesExportFile": "${ROLES_FILENAME}",
    "rolesSizeBytes": ${ROLES_SIZE_BYTES}
  },
  "phaseTimings": {
    "instancePrepMs": ${INSTANCE_PREP_MS},
    "instancePrepSeconds": ${INSTANCE_PREP_SECONDS},
    "rolesRestoreMs": ${ROLES_RESTORE_MS},
    "archiveRestoreMs": ${RESTORE_MS},
    "archiveRestoreSeconds": ${RESTORE_SECONDS},
    "schemaVerificationMs": ${SCHEMA_VERIFY_MS},
    "schemaVerificationSeconds": ${SCHEMA_VERIFY_SECONDS},
    "testSuiteExecutionMs": ${TEST_EXECUTION_MS},
    "testSuiteExecutionSeconds": ${TEST_EXECUTION_SECONDS},
    "cleanupMs": ${CLEANUP_MS},
    "cleanupSeconds": ${CLEANUP_SECONDS},
    "rtoRestoreToLastPassingTestMs": ${RTO_ELAPSED_MS},
    "rtoRestoreToLastPassingTestSec": ${RTO_ELAPSED_SECONDS},
    "rtoRestoreToLastPassingTestMin": ${RTO_ELAPSED_MINUTES},
    "rtoRestoreToLastPassingTestHours": ${RTO_ELAPSED_HOURS},
    "totalDrillWallClockMs": ${TOTAL_DRILL_MS},
    "totalDrillWallClockSeconds": ${TOTAL_DRILL_SECONDS}
  },
  "schemaVerification": {
    "restoredDomainTables": ${RESTORED_TABLES:-0},
    "appliedEfMigrations": ${RESTORED_MIGRATIONS:-0},
    "seededRoles": ${RESTORED_ROLES:-0},
    "seededPriorityTargets": ${RESTORED_PRIORITIES:-0},
    "seededLocationKinds": ${RESTORED_LOC_KINDS:-0},
    "seededResolutionCodes": ${RESTORED_RES_CODES:-0},
    "recoveredIncidents": ${RECOVERED_INCIDENTS:-0},
    "recoveredTickets": ${RECOVERED_TICKETS:-0},
    "recoveredUsers": ${RECOVERED_USERS:-0},
    "latestIncidentReference": "${LATEST_INC_REF}",
    "latestIncidentOpenedUtc": "${LATEST_INC_OPENED_UTC}"
  },
  "testSuiteResults": {
    "testProject": "${TEST_PROJECT}",
    "totalTests": ${TESTS_TOTAL},
    "passedTests": ${TESTS_PASSED},
    "failedTests": ${TESTS_FAILED},
    "skippedTests": ${TESTS_SKIPPED},
    "allTestsPassed": ${ALL_TESTS_PASSED}
  },
  "rtoEvaluation": {
    "rtoThresholdHours": ${RTO_THRESHOLD_HOURS},
    "rtoThresholdSeconds": ${RTO_THRESHOLD_SECONDS},
    "elapsedSeconds": ${RTO_ELAPSED_SECONDS},
    "elapsedMinutes": ${RTO_ELAPSED_MINUTES},
    "elapsedHours": ${RTO_ELAPSED_HOURS},
    "rtoCompliant": ${RTO_COMPLIANT},
    "drillPassed": ${DRILL_PASSED}
  }
}
EOF

echo "[${ISO_DRILL_END_UTC}] STATUS=${STATUS_TEXT} REQ=NFR-03 ID=OPS-BKP-02 MILESTONE=${MILESTONE} ARCHIVE=${ARCHIVE_FILENAME} SHA256=${ACTUAL_SHA256} ROLES=${ROLES_FILENAME} PREP_MS=${INSTANCE_PREP_MS} RESTORE_MS=${RESTORE_MS} TEST_MS=${TEST_EXECUTION_MS} RTO_MS=${RTO_ELAPSED_MS} RTO_S=${RTO_ELAPSED_SECONDS} TOTAL_MS=${TOTAL_DRILL_MS} TESTS_TOTAL=${TESTS_TOTAL} TESTS_PASSED=${TESTS_PASSED} TESTS_FAILED=${TESTS_FAILED} RTO_COMPLIANT=${RTO_COMPLIANT}" >> "${HISTORY_LOG}"

echo "DRILL_STATUS=${STATUS_TEXT}"
echo "BACKUP_FILE=${ARCHIVE_FILENAME}"
echo "BACKUP_SIZE_BYTES=${ARCHIVE_SIZE_BYTES}"
echo "BACKUP_SHA256=${ACTUAL_SHA256}"
echo "ROLES_EXPORT_FILE=${ROLES_FILENAME}"
echo "ISOLATION_MODE=${EFFECTIVE_ISOLATION_MODE}"
echo "DRILL_TARGET_DB=${DRILL_TARGET_DB}"
echo "INSTANCE_PREP_MS=${INSTANCE_PREP_MS}"
echo "INSTANCE_PREP_SECONDS=${INSTANCE_PREP_SECONDS}"
echo "RESTORE_ELAPSED_MS=${RESTORE_MS}"
echo "RESTORE_ELAPSED_SECONDS=${RESTORE_SECONDS}"
echo "SCHEMA_VERIFY_MS=${SCHEMA_VERIFY_MS}"
echo "TEST_EXECUTION_MS=${TEST_EXECUTION_MS}"
echo "TEST_EXECUTION_SECONDS=${TEST_EXECUTION_SECONDS}"
echo "CLEANUP_MS=${CLEANUP_MS}"
echo "RTO_ELAPSED_MS=${RTO_ELAPSED_MS}"
echo "RTO_ELAPSED_SECONDS=${RTO_ELAPSED_SECONDS}"
echo "TOTAL_DRILL_ELAPSED_MS=${TOTAL_DRILL_MS}"
echo "TOTAL_DRILL_ELAPSED_SECONDS=${TOTAL_DRILL_SECONDS}"
echo "RESTORED_TABLES=${RESTORED_TABLES:-0}"
echo "RESTORED_MIGRATIONS=${RESTORED_MIGRATIONS:-0}"
echo "RECOVERED_INCIDENTS=${RECOVERED_INCIDENTS:-0}"
echo "RECOVERED_TICKETS=${RECOVERED_TICKETS:-0}"
echo "LATEST_INCIDENT_REF=${LATEST_INC_REF}"
echo "LATEST_INCIDENT_OPENED_AT=${LATEST_INC_OPENED_UTC}"
echo "TESTS_TOTAL=${TESTS_TOTAL}"
echo "TESTS_PASSED=${TESTS_PASSED}"
echo "TESTS_FAILED=${TESTS_FAILED}"
echo "TESTS_SKIPPED=${TESTS_SKIPPED}"
echo "ALL_TESTS_PASSED=${ALL_TESTS_PASSED}"
echo "RTO_COMPLIANT=${RTO_COMPLIANT}"
echo "RESULT_JSON=${RESULT_JSON}"

if [[ "$DRILL_PASSED" != "true" ]]; then
  echo "[ERROR] NFR-03 restore drill failed validation checks." >&2
  exit 1
fi
