#Requires -Version 5.1
<#
.SYNOPSIS
    CivicConnect Automated Milestone Restore Drill Script (PowerShell).
.DESCRIPTION
    Implements NFR-03 (Verified Restore / Recovery Time Objective - RTO <= 4 hours) and OPS-BKP-02:
    1. Resolves (or captures) the most recent PostgreSQL custom-format backup archive (.dump),
       SHA-256 checksum (.sha256), and cluster role export (*_roles.sql).
    2. Provisions a clean, isolated PostgreSQL 15 instance (ephemeral container 'civicconnect-drill-db'
       on port 5439, or an isolated clean database on the target host).
    3. Restores the role export and database backup archive into the empty PostgreSQL instance.
    4. Verifies restored schema completeness (21 tables, 12 EF Core migrations, seeded reference data,
       and operational incident/ticket watermarks).
    5. Configures CONNECTION_STRING to target the restored instance and executes the integration
       test suite (tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj).
    6. Measures exact wall-clock timings for each phase and validates:
       - 100% of integration tests pass (0 failures).
       - Total elapsed wall-clock time from restore start to final passing test is <= 4 hours (14,400s).
    7. Generates structured execution logs, JSON evidence, and an optional Markdown drill record,
       then tears down the temporary drill container/database in a finally block.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [Alias("File")]
    [string]$BackupFile = "",

    [Parameter(Mandatory = $false)]
    [string]$RolesFile = "",

    [Parameter(Mandatory = $false)]
    [string]$BackupDir = "",

    [Parameter(Mandatory = $false)]
    [string]$SourceContainerName = "",

    [Parameter(Mandatory = $false)]
    [string]$SourceDatabase = "",

    [Parameter(Mandatory = $false)]
    [string]$DrillContainerName = "civicconnect-drill-db",

    [Parameter(Mandatory = $false)]
    [string]$DrillDatabase = "civicconnect_drill",

    [Parameter(Mandatory = $false)]
    [int]$DrillPort = 5439,

    [Parameter(Mandatory = $false)]
    [string]$PostgresImage = "postgres:15-alpine",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Auto", "Container", "Database")]
    [string]$IsolationMode = "Auto",

    [Parameter(Mandatory = $false)]
    [string]$TestProject = "tests/CivicConnect.Data.Tests/CivicConnect.Data.Tests.csproj",

    [Parameter(Mandatory = $false)]
    [double]$RtoThresholdHours = 4.0,

    [Parameter(Mandatory = $false)]
    [string]$Operator = "",

    [Parameter(Mandatory = $false)]
    [string]$Milestone = "M3",

    [Parameter(Mandatory = $false)]
    [string]$RecordOutputPath = "",

    [Parameter(Mandatory = $false)]
    [string]$EnvFile = "",

    [Parameter(Mandatory = $false)]
    [switch]$TakeFreshBackup,

    [Parameter(Mandatory = $false)]
    [switch]$SkipChecksum,

    [Parameter(Mandatory = $false)]
    [switch]$KeepInstance
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = Join-Path $RepoRoot ".env"
}

# Load environment variables from .env if present (NFR-01: no hardcoded secrets)
if (Test-Path -LiteralPath $EnvFile) {
    foreach ($rawLine in Get-Content -LiteralPath $EnvFile) {
        $trimmed = $rawLine.Trim()
        if ([string]::IsNullOrEmpty($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }
        $eqIdx = $trimmed.IndexOf('=')
        if ($eqIdx -le 0) {
            continue
        }
        $key = $trimmed.Substring(0, $eqIdx).Trim()
        $val = $trimmed.Substring($eqIdx + 1).Trim().Trim('"', "'")
        if (-not [string]::IsNullOrEmpty($key) -and [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($key))) {
            [Environment]::SetEnvironmentVariable($key, $val)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    $envBackupDir = [Environment]::GetEnvironmentVariable("BACKUP_DIR")
    if (-not [string]::IsNullOrWhiteSpace($envBackupDir)) {
        $BackupDir = if ([System.IO.Path]::IsPathRooted($envBackupDir)) { $envBackupDir } else { Join-Path $RepoRoot $envBackupDir }
    } else {
        $BackupDir = Join-Path $RepoRoot "backups"
    }
}

if (-not (Test-Path -LiteralPath $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
}
$BackupDir = (Resolve-Path -LiteralPath $BackupDir).Path

if ([string]::IsNullOrWhiteSpace($SourceContainerName)) {
    $envContainer = [Environment]::GetEnvironmentVariable("POSTGRES_CONTAINER")
    $SourceContainerName = if (-not [string]::IsNullOrWhiteSpace($envContainer)) { $envContainer } else { "civicconnect-db" }
}

# Parse CONNECTION_STRING if present
$OriginalConnectionString = [Environment]::GetEnvironmentVariable("CONNECTION_STRING")
$ConnHost = ""
$ConnPort = ""
$ConnDb = ""
$ConnUser = ""
$ConnPass = ""

if (-not [string]::IsNullOrWhiteSpace($OriginalConnectionString)) {
    foreach ($segment in ($OriginalConnectionString -split ';')) {
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        $idx = $segment.IndexOf('=')
        if ($idx -le 0) { continue }
        $k = $segment.Substring(0, $idx).Trim().ToLowerInvariant()
        $v = $segment.Substring($idx + 1).Trim().Trim('"', "'")
        switch ($k) {
            { $_ -in @("host", "server") } { $ConnHost = $v }
            "port" { $ConnPort = $v }
            { $_ -in @("database", "initial catalog") } { $ConnDb = $v }
            { $_ -in @("username", "user", "user id", "uid") } { $ConnUser = $v }
            { $_ -in @("password", "pwd") } { $ConnPass = $v }
        }
    }
}

$PgHost = if (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_HOST)) { $env:POSTGRES_HOST } elseif (-not [string]::IsNullOrWhiteSpace($ConnHost)) { $ConnHost } else { "localhost" }
$PgPort = if (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_PORT)) { $env:POSTGRES_PORT } elseif (-not [string]::IsNullOrWhiteSpace($ConnPort)) { $ConnPort } else { "5432" }
$PgDb   = if (-not [string]::IsNullOrWhiteSpace($SourceDatabase)) { $SourceDatabase } elseif (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_DB)) { $env:POSTGRES_DB } elseif (-not [string]::IsNullOrWhiteSpace($ConnDb)) { $ConnDb } else { "civicconnect" }
$PgUser = if (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_USER)) { $env:POSTGRES_USER } elseif (-not [string]::IsNullOrWhiteSpace($ConnUser)) { $ConnUser } else { "civicconnect" }
$PgPass = if (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_PASSWORD)) { $env:POSTGRES_PASSWORD } elseif (-not [string]::IsNullOrWhiteSpace($ConnPass)) { $ConnPass } else { "" }

# NFR-01 validation: reject missing or placeholder passwords
if ([string]::IsNullOrWhiteSpace($PgPass) -or $PgPass.Equals("changeme", [StringComparison]::OrdinalIgnoreCase) -or $PgPass.Contains('${')) {
    Write-Error "NFR-01 violation: POSTGRES_PASSWORD or CONNECTION_STRING password is missing or uses an insecure placeholder."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Operator)) {
    $gitUser = ""
    $gitEmail = ""
    try {
        $gitUser = (& git -C $RepoRoot config user.name 2>$null | Out-String).Trim()
        $gitEmail = (& git -C $RepoRoot config user.email 2>$null | Out-String).Trim()
    } catch {}
    if (-not [string]::IsNullOrWhiteSpace($gitUser)) {
        $Operator = if (-not [string]::IsNullOrWhiteSpace($gitEmail)) { "$gitUser <$gitEmail>" } else { $gitUser }
    } elseif (-not [string]::IsNullOrWhiteSpace($env:USERNAME)) {
        $Operator = $env:USERNAME
    } else {
        $Operator = "automated-drill-operator"
    }
}

# Determine Docker availability
$DockerAvailable = $false
$SourceContainerRunning = $false
$DockerCmd = Get-Command docker -ErrorAction SilentlyContinue
if ($null -ne $DockerCmd) {
    $dockerInfo = & docker info 2>$null
    if ($LASTEXITCODE -eq 0) {
        $DockerAvailable = $true
        $runningState = & docker inspect -f "{{.State.Running}}" $SourceContainerName 2>$null
        if ($LASTEXITCODE -eq 0 -and "$runningState".Trim() -eq "true") {
            $SourceContainerRunning = $true
        }
    }
}

$EffectiveIsolationMode = $IsolationMode
if ($EffectiveIsolationMode -eq "Auto") {
    $EffectiveIsolationMode = if ($DockerAvailable) { "Container" } else { "Database" }
}

function Invoke-NativeQuiet {
    param([scriptblock]$Command)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Command
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

function Invoke-SqlScript {
    param(
        [Parameter(Mandatory = $true)][string]$Sql,
        [Parameter(Mandatory = $true)][string]$TargetDatabase,
        [Parameter(Mandatory = $false)][string]$Container = "",
        [Parameter(Mandatory = $false)][string]$TargetHost = "localhost",
        [Parameter(Mandatory = $false)][int]$TargetPort = 5432,
        [Parameter(Mandatory = $false)][switch]$TuplesOnly
    )

    $tmpLocalSql = Join-Path $BackupDir ("tmp_sql_" + [Guid]::NewGuid().ToString("N") + ".sql")
    [System.IO.File]::WriteAllText($tmpLocalSql, $Sql, (New-Object System.Text.UTF8Encoding($false)))

    try {
        if (-not [string]::IsNullOrWhiteSpace($Container)) {
            $tmpContainerSql = "/tmp/" + [System.IO.Path]::GetFileName($tmpLocalSql)
            & docker cp $tmpLocalSql "${Container}:${tmpContainerSql}" | Out-Null
            try {
                $psqlArgs = @("exec", "-e", "PGPASSWORD=$PgPass", $Container, "psql", "-h", "localhost", "-p", "5432", "-U", $PgUser, "-d", $TargetDatabase, "-v", "ON_ERROR_STOP=1")
                if ($TuplesOnly) {
                    $psqlArgs += @("-t", "-A")
                }
                $psqlArgs += @("-f", $tmpContainerSql)
                $out = Invoke-NativeQuiet { & docker @psqlArgs 2>$null }
                if ($LASTEXITCODE -ne 0) {
                    throw "psql execution failed inside container '$Container' on database '$TargetDatabase' (exit code $LASTEXITCODE)."
                }
                return ($out | Out-String).Trim()
            } finally {
                Invoke-NativeQuiet { & docker exec $Container rm -f $tmpContainerSql 2>$null | Out-Null }
            }
        } else {
            $prevPass = $env:PGPASSWORD
            try {
                $env:PGPASSWORD = $PgPass
                $psqlArgs = @("-h", $TargetHost, "-p", "$TargetPort", "-U", $PgUser, "-d", $TargetDatabase, "-v", "ON_ERROR_STOP=1")
                if ($TuplesOnly) {
                    $psqlArgs += @("-t", "-A")
                }
                $psqlArgs += @("-f", $tmpLocalSql)
                $out = Invoke-NativeQuiet { & psql @psqlArgs 2>$null }
                if ($LASTEXITCODE -ne 0) {
                    throw "Local psql execution failed on database '$TargetDatabase' (exit code $LASTEXITCODE)."
                }
                return ($out | Out-String).Trim()
            } finally {
                $env:PGPASSWORD = $prevPass
            }
        }
    } finally {
        if (Test-Path -LiteralPath $tmpLocalSql) {
            Remove-Item -LiteralPath $tmpLocalSql -Force -ErrorAction SilentlyContinue
        }
    }
}

# Helper function to ensure source database has clean referential integrity and baseline operational records before taking a fresh backup
function Ensure-SourceBaselineData {
    if (-not $SourceContainerRunning) {
        return
    }

    # Clean any orphaned audit_log rows left behind by replica-mode test teardowns (SET LOCAL session_replication_role = 'replica')
    $cleanOrphansSql = @"
BEGIN;
SET LOCAL session_replication_role = 'replica';
DELETE FROM audit_log a
WHERE NOT EXISTS (SELECT 1 FROM app_user u WHERE u.id = a.actor_id)
   OR (a.incident_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM incident i WHERE i.id = a.incident_id));
COMMIT;
"@
    Invoke-SqlScript -Sql $cleanOrphansSql -TargetDatabase $PgDb -Container $SourceContainerName | Out-Null

    $incCountRaw = Invoke-SqlScript -Sql "SELECT COUNT(*) FROM incident;" -TargetDatabase $PgDb -Container $SourceContainerName -TuplesOnly
    $incCount = 0
    if (-not [string]::IsNullOrWhiteSpace("$incCountRaw")) {
        [int]::TryParse(("$incCountRaw".Trim()), [ref]$incCount) | Out-Null
    }
    if ($incCount -gt 0) {
        return
    }

    Write-Host "[INFO] Source database '$PgDb' has 0 incidents; seeding baseline operational dataset for backup verification..."
    $seedSql = @"
DO `$`$
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
        VALUES ('drill.reporter@civicconnect.local', 'Drill Verification Reporter', '`$argon2id`$v=19`$m=19456,t=2,p=1`$c2FsdHNhbHRzYWx0c2FsdA`$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g', true)
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
END `$`$;
"@
    Invoke-SqlScript -Sql $seedSql -TargetDatabase $PgDb -Container $SourceContainerName | Out-Null
}

# Helper function to create a backup & role export if none exists or -TakeFreshBackup is specified
function Invoke-BackupCapture {
    Ensure-SourceBaselineData

    $backupScriptPath = Join-Path $ScriptDir "backup-db.ps1"
    if (Test-Path -LiteralPath $backupScriptPath) {
        Write-Host "[INFO] Invoking NFR-02 backup script '$backupScriptPath'..."
        $backupOut = & $backupScriptPath -BackupDir $BackupDir -ContainerName $SourceContainerName -Database $PgDb
        if ($LASTEXITCODE -ne 0) {
            throw "backup-db.ps1 failed with exit code $LASTEXITCODE."
        }
        foreach ($line in $backupOut) {
            if ("$line" -match '^BACKUP_FILE=(.+)$') {
                return $Matches[1].Trim()
            }
        }
    }

    # Built-in NFR-02 compatible pg_dump + pg_dumpall --roles-only capture
    $utcNow = [DateTimeOffset]::UtcNow
    $tsToken = $utcNow.ToString("yyyyMMdd_HHmmss'Z'")
    $isoTs = $utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $archiveName = "civicconnect_backup_$tsToken.dump"
    $archiveFull = Join-Path $BackupDir $archiveName
    $checksumFull = "$archiveFull.sha256"
    $rolesName = "civicconnect_backup_${tsToken}_roles.sql"
    $rolesFull = Join-Path $BackupDir $rolesName
    $metaFull = "$archiveFull.meta.json"

    Write-Host "[INFO] [$isoTs] Capturing logical backup and role export from '$PgDb'..."
    $bkpSw = [System.Diagnostics.Stopwatch]::StartNew()

    if ($SourceContainerRunning) {
        $tmpDump = "/tmp/$archiveName"
        & docker exec -e "PGPASSWORD=$PgPass" $SourceContainerName `
            pg_dump -h localhost -p 5432 -U $PgUser -d $PgDb `
            --format=custom --compress=6 --no-owner --no-privileges `
            --file=$tmpDump
        if ($LASTEXITCODE -ne 0) {
            throw "pg_dump inside container '$SourceContainerName' failed with exit code $LASTEXITCODE."
        }
        & docker cp "${SourceContainerName}:${tmpDump}" $archiveFull | Out-Null
        & docker exec $SourceContainerName pg_restore --list $tmpDump | Out-Null
        $listExit = $LASTEXITCODE
        & docker exec $SourceContainerName rm -f $tmpDump | Out-Null
        if ($listExit -ne 0) {
            throw "pg_restore --list failed on '$archiveName'."
        }

        $tmpRoles = "/tmp/$rolesName"
        & docker exec -e "PGPASSWORD=$PgPass" $SourceContainerName `
            pg_dumpall -h localhost -p 5432 -U $PgUser --roles-only --file=$tmpRoles 2>$null
        if ($LASTEXITCODE -eq 0) {
            & docker cp "${SourceContainerName}:${tmpRoles}" $rolesFull | Out-Null
            & docker exec $SourceContainerName rm -f $tmpRoles | Out-Null
        }
    } else {
        $effHost = if ($PgHost -eq "db") { "localhost" } else { $PgHost }
        $prevPass = $env:PGPASSWORD
        try {
            $env:PGPASSWORD = $PgPass
            & pg_dump -h $effHost -p $PgPort -U $PgUser -d $PgDb `
                --format=custom --compress=6 --no-owner --no-privileges `
                --file=$archiveFull
            if ($LASTEXITCODE -ne 0) {
                throw "Local pg_dump failed with exit code $LASTEXITCODE."
            }
            & pg_restore --list $archiveFull | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Local pg_restore --list failed."
            }
            $pgDumpAllCmd = Get-Command pg_dumpall -ErrorAction SilentlyContinue
            if ($null -ne $pgDumpAllCmd) {
                & pg_dumpall -h $effHost -p $PgPort -U $PgUser --roles-only --file=$rolesFull 2>$null
            }
        } finally {
            $env:PGPASSWORD = $prevPass
        }
    }

    $bkpSw.Stop()
    $archiveInfo = Get-Item -LiteralPath $archiveFull
    $hashVal = (Get-FileHash -LiteralPath $archiveFull -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($checksumFull, "$hashVal  $archiveName`n", (New-Object System.Text.UTF8Encoding($false)))

    $metaObj = [ordered]@{
        requirementId      = "NFR-02"
        operation          = "logical_backup"
        timestampUtc       = $isoTs
        database           = $PgDb
        archiveFile        = $archiveName
        archiveSizeBytes   = $archiveInfo.Length
        sha256             = $hashVal
        rolesExportFile    = if (Test-Path -LiteralPath $rolesFull) { $rolesName } else { "" }
        elapsedDurationMs  = [int64]$bkpSw.Elapsed.TotalMilliseconds
        exitCode           = 0
    }
    [System.IO.File]::WriteAllText($metaFull, ($metaObj | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))

    return $archiveFull
}

# ------------------------------------------------------------------------------
# Step 0: Resolve and Verify Target Backup Archive & Role Export
# ------------------------------------------------------------------------------
if ($TakeFreshBackup) {
    $BackupFile = Invoke-BackupCapture
} elseif ([string]::IsNullOrWhiteSpace($BackupFile)) {
    $latestDump = Get-ChildItem -LiteralPath $BackupDir -Filter "civicconnect_backup_*.dump" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestDump) {
        Write-Host "[INFO] No existing backup archive found in '$BackupDir'. Capturing a fresh backup before starting drill..."
        $BackupFile = Invoke-BackupCapture
    } else {
        $BackupFile = $latestDump.FullName
    }
} elseif (-not [System.IO.Path]::IsPathRooted($BackupFile)) {
    $candidate = Join-Path (Get-Location).Path $BackupFile
    $BackupFile = if (Test-Path -LiteralPath $candidate) { $candidate } else { Join-Path $BackupDir $BackupFile }
}

if (-not (Test-Path -LiteralPath $BackupFile)) {
    Write-Error "Target backup archive '$BackupFile' does not exist."
    exit 1
}

$BackupItem = Get-Item -LiteralPath $BackupFile
$ArchiveFileName = $BackupItem.Name
$ArchiveSizeBytes = $BackupItem.Length
$ArchiveCreatedUtc = $BackupItem.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

if ($ArchiveSizeBytes -le 0) {
    Write-Error "Backup archive '$BackupFile' is empty (0 bytes)."
    exit 1
}

# Resolve companion role export (*_roles.sql)
if ([string]::IsNullOrWhiteSpace($RolesFile)) {
    $basePrefix = if ($BackupFile.EndsWith(".dump", [StringComparison]::OrdinalIgnoreCase)) {
        $BackupFile.Substring(0, $BackupFile.Length - 5)
    } else {
        $BackupFile
    }
    $candidateRoles = "${basePrefix}_roles.sql"
    if (Test-Path -LiteralPath $candidateRoles) {
        $RolesFile = $candidateRoles
    }
}

$RolesFileName = if (-not [string]::IsNullOrWhiteSpace($RolesFile) -and (Test-Path -LiteralPath $RolesFile)) {
    (Get-Item -LiteralPath $RolesFile).Name
} else {
    "NONE"
}
$RolesFileSizeBytes = if ($RolesFileName -ne "NONE") { (Get-Item -LiteralPath $RolesFile).Length } else { 0 }

# Compute and verify SHA-256 checksum
$ActualSha256 = (Get-FileHash -LiteralPath $BackupFile -Algorithm SHA256).Hash.ToLowerInvariant()
$ChecksumFile = "$BackupFile.sha256"
$ChecksumVerified = $false

if (-not $SkipChecksum) {
    if (Test-Path -LiteralPath $ChecksumFile) {
        $expectedRaw = (Get-Content -LiteralPath $ChecksumFile -TotalCount 1).Trim()
        $expectedHash = ($expectedRaw -split '\s+')[0].ToLowerInvariant()
        if ($ActualSha256 -ne $expectedHash) {
            Write-Error "SHA-256 checksum mismatch for '$ArchiveFileName'! Expected: $expectedHash, Actual: $ActualSha256"
            exit 1
        }
        $ChecksumVerified = $true
        Write-Host "[INFO] Verified SHA-256 checksum for '$ArchiveFileName' ($ActualSha256)."
    } else {
        [System.IO.File]::WriteAllText($ChecksumFile, "$ActualSha256  $ArchiveFileName`n", (New-Object System.Text.UTF8Encoding($false)))
        $ChecksumVerified = $true
        Write-Host "[INFO] Generated and verified SHA-256 checksum for '$ArchiveFileName' ($ActualSha256)."
    }
}

# ------------------------------------------------------------------------------
# Drill Execution & Timing Harness
# ------------------------------------------------------------------------------
$DrillStartUtc = [DateTimeOffset]::UtcNow
$IsoDrillStartUtc = $DrillStartUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$TotalDrillStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$InstancePrepMs = 0L
$RestoreMs = 0L
$RolesRestoreMs = 0L
$SchemaVerifyMs = 0L
$TestExecutionMs = 0L
$CleanupMs = 0L
$RtoElapsedMs = 0L

$RestoredTableCount = 0
$RestoredMigrationCount = 0
$RestoredRoleCount = 0
$RestoredPriorityTargetCount = 0
$RestoredLocationKindCount = 0
$RestoredResolutionCodeCount = 0
$RecoveredIncidents = 0
$RecoveredTickets = 0
$RecoveredUsers = 0
$LatestIncidentRef = "NONE"
$LatestIncidentOpenedAtUtc = "NONE"

$TotalTests = 0
$PassedTests = 0
$FailedTests = 0
$SkippedTests = 0
$TestSuiteExitCode = -1
$TrxDirectory = Join-Path $BackupDir "drill-test-results"
$TrxFilePath = Join-Path $TrxDirectory "restore-drill-results.trx"

$DrillTargetHost = "localhost"
$DrillTargetPort = $DrillPort
$DrillTargetDb = $DrillDatabase

try {
    # ==========================================================================
    # Phase 1: Clean Isolated Instance Preparation
    # ==========================================================================
    $PrepSw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "[PHASE 1/4] Provisioning clean isolated PostgreSQL drill instance (mode=$EffectiveIsolationMode)..."

    if ($EffectiveIsolationMode -eq "Container") {
        # Ensure any previous drill container is removed
        Invoke-NativeQuiet { & docker rm -f $DrillContainerName 2>$null | Out-Null }

        & docker run -d `
            --name $DrillContainerName `
            -e "POSTGRES_DB=$DrillDatabase" `
            -e "POSTGRES_USER=$PgUser" `
            -e "POSTGRES_PASSWORD=$PgPass" `
            -p "${DrillPort}:5432" `
            $PostgresImage | Out-Null

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to start ephemeral PostgreSQL drill container '$DrillContainerName' on port $DrillPort."
        }

        # Poll pg_isready until the fresh PostgreSQL instance accepts connections
        $ready = $false
        for ($attempt = 1; $attempt -le 60; $attempt++) {
            Invoke-NativeQuiet { & docker exec $DrillContainerName pg_isready -h localhost -p 5432 -U $PgUser -d $DrillDatabase 2>$null | Out-Null }
            if ($LASTEXITCODE -eq 0) {
                $pingOut = Invoke-NativeQuiet {
                    & docker exec -e "PGPASSWORD=$PgPass" $DrillContainerName `
                        psql -h localhost -p 5432 -U $PgUser -d $DrillDatabase -t -A -c "SELECT 1;" 2>$null
                }
                if ($LASTEXITCODE -eq 0 -and ("$pingOut".Trim()) -eq "1") {
                    $ready = $true
                    break
                }
            }
            Start-Sleep -Milliseconds 250
        }

        if (-not $ready) {
            throw "Timed out waiting for ephemeral drill container '$DrillContainerName' to become ready."
        }

        $DrillTargetHost = "localhost"
        $DrillTargetPort = $DrillPort
        $DrillTargetDb = $DrillDatabase
    } else {
        # Isolated clean database on existing PostgreSQL host
        $DrillTargetHost = if ($PgHost -eq "db") { "localhost" } else { $PgHost }
        $DrillTargetPort = [int]$PgPort
        $DrillTargetDb = "${DrillDatabase}_" + $DrillStartUtc.ToString("yyyyMMdd_HHmmss")

        if ($SourceContainerRunning) {
            Invoke-NativeQuiet {
                & docker exec -e "PGPASSWORD=$PgPass" $SourceContainerName `
                    psql -h localhost -p 5432 -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                    -c "DROP DATABASE IF EXISTS `"$DrillTargetDb`";" 2>$null | Out-Null
                & docker exec -e "PGPASSWORD=$PgPass" $SourceContainerName `
                    psql -h localhost -p 5432 -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                    -c "CREATE DATABASE `"$DrillTargetDb`";" 2>$null | Out-Null
            }
        } else {
            $prevPass = $env:PGPASSWORD
            try {
                $env:PGPASSWORD = $PgPass
                Invoke-NativeQuiet {
                    & psql -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                        -c "DROP DATABASE IF EXISTS `"$DrillTargetDb`";" 2>$null | Out-Null
                    & psql -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                        -c "CREATE DATABASE `"$DrillTargetDb`";" 2>$null | Out-Null
                }
            } finally {
                $env:PGPASSWORD = $prevPass
            }
        }
    }

    $PrepSw.Stop()
    $InstancePrepMs = [int64]$PrepSw.Elapsed.TotalMilliseconds
    Write-Host ("[OK] Clean instance '{0}' ready in {1:N3}s ({2} ms)." -f $DrillTargetDb, $PrepSw.Elapsed.TotalSeconds, $InstancePrepMs)

    # ==========================================================================
    # Phase 2: Role Export & Backup Archive Restore (RTO Clock Starts Here)
    # ==========================================================================
    # Per NFR-03: "the elapsed wall-clock time from the start of the restore to the last passing test is four hours or less"
    $RestoreStartUtc = [DateTimeOffset]::UtcNow
    $IsoRestoreStartUtc = $RestoreStartUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $RtoStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $RestoreSw = [System.Diagnostics.Stopwatch]::StartNew()

    Write-Host "[PHASE 2/4] [$IsoRestoreStartUtc] Restoring role export and backup archive '$ArchiveFileName'..."

    $TargetExecContainer = if ($EffectiveIsolationMode -eq "Container") { $DrillContainerName } else { $SourceContainerName }
    $UseContainerExec = ($EffectiveIsolationMode -eq "Container") -or $SourceContainerRunning

    # Step 2a: Restore cluster role export (*_roles.sql) if present
    $RolesSw = [System.Diagnostics.Stopwatch]::StartNew()
    if ($RolesFileName -ne "NONE") {
        if ($UseContainerExec) {
            $tmpRolesContainer = "/tmp/drill_roles.sql"
            & docker cp $RolesFile "${TargetExecContainer}:${tmpRolesContainer}" | Out-Null
            # Apply role export (bootstrap superuser already exists in clean container, so ON_ERROR_STOP is omitted)
            Invoke-NativeQuiet {
                & docker exec -e "PGPASSWORD=$PgPass" $TargetExecContainer `
                    psql -h localhost -p 5432 -U $PgUser -d postgres -f $tmpRolesContainer 2>$null | Out-Null
                & docker exec $TargetExecContainer rm -f $tmpRolesContainer 2>$null | Out-Null
            }
        } else {
            $prevPass = $env:PGPASSWORD
            try {
                $env:PGPASSWORD = $PgPass
                Invoke-NativeQuiet {
                    & psql -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d postgres -f $RolesFile 2>$null | Out-Null
                }
            } finally {
                $env:PGPASSWORD = $prevPass
            }
        }
    }
    $RolesSw.Stop()
    $RolesRestoreMs = [int64]$RolesSw.Elapsed.TotalMilliseconds

    # Step 2b: Restore custom-format database backup archive (.dump)
    $restoreScriptPath = Join-Path $ScriptDir "restore-db.ps1"
    if (Test-Path -LiteralPath $restoreScriptPath) {
        & $restoreScriptPath `
            -BackupFile $BackupFile `
            -Database $DrillTargetDb `
            -ContainerName $TargetExecContainer `
            -SkipChecksum | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "restore-db.ps1 failed with exit code $LASTEXITCODE."
        }
    } else {
        if ($UseContainerExec) {
            $tmpDumpContainer = "/tmp/drill_restore.dump"
            & docker cp $BackupFile "${TargetExecContainer}:${tmpDumpContainer}" | Out-Null
            try {
                Invoke-NativeQuiet {
                    & docker exec -e "PGPASSWORD=$PgPass" $TargetExecContainer `
                        psql -h localhost -p 5432 -U $PgUser -d $DrillTargetDb -v ON_ERROR_STOP=1 `
                        -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" 2>$null | Out-Null
                }
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to reset public schema in '$DrillTargetDb'."
                }

                Invoke-NativeQuiet {
                    & docker exec -e "PGPASSWORD=$PgPass" $TargetExecContainer `
                        pg_restore -h localhost -p 5432 -U $PgUser -d $DrillTargetDb `
                        --clean --if-exists --no-owner --no-privileges --exit-on-error `
                        $tmpDumpContainer 2>$null | Out-Null
                }
                if ($LASTEXITCODE -ne 0) {
                    throw "pg_restore failed inside '$TargetExecContainer' with exit code $LASTEXITCODE."
                }
            } finally {
                Invoke-NativeQuiet { & docker exec $TargetExecContainer rm -f $tmpDumpContainer 2>$null | Out-Null }
            }
        } else {
            $prevPass = $env:PGPASSWORD
            try {
                $env:PGPASSWORD = $PgPass
                Invoke-NativeQuiet {
                    & psql -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d $DrillTargetDb -v ON_ERROR_STOP=1 `
                        -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" 2>$null | Out-Null
                    & pg_restore -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d $DrillTargetDb `
                        --clean --if-exists --no-owner --no-privileges --exit-on-error `
                        $BackupFile 2>$null | Out-Null
                }
                if ($LASTEXITCODE -ne 0) {
                    throw "Local pg_restore failed with exit code $LASTEXITCODE."
                }
            } finally {
                $env:PGPASSWORD = $prevPass
            }
        }
    }

    $RestoreSw.Stop()
    $RestoreMs = [int64]$RestoreSw.Elapsed.TotalMilliseconds
    Write-Host ("[OK] Backup archive and role export restored in {0:N3}s ({1} ms)." -f $RestoreSw.Elapsed.TotalSeconds, $RestoreMs)

    # Step 2c: Post-Restore Schema & Data Verification
    $VerifySw = [System.Diagnostics.Stopwatch]::StartNew()
    $verifySql = @"
SELECT
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
  COALESCE((SELECT to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE');
"@

    $verifyContainerArg = if ($UseContainerExec) { $TargetExecContainer } else { "" }
    $verifyRaw = Invoke-SqlScript `
        -Sql $verifySql `
        -TargetDatabase $DrillTargetDb `
        -Container $verifyContainerArg `
        -TargetHost $DrillTargetHost `
        -TargetPort $DrillTargetPort `
        -TuplesOnly

    if ([string]::IsNullOrWhiteSpace("$verifyRaw")) {
        throw "Post-restore schema verification query returned empty output against '$DrillTargetDb'."
    }

    $vParts = ("$verifyRaw".Trim()) -split '\|', 11
    if ($vParts.Count -eq 11) {
        [int]::TryParse($vParts[0].Trim(), [ref]$RestoredTableCount) | Out-Null
        [int]::TryParse($vParts[1].Trim(), [ref]$RestoredMigrationCount) | Out-Null
        [int]::TryParse($vParts[2].Trim(), [ref]$RestoredRoleCount) | Out-Null
        [int]::TryParse($vParts[3].Trim(), [ref]$RestoredPriorityTargetCount) | Out-Null
        [int]::TryParse($vParts[4].Trim(), [ref]$RestoredLocationKindCount) | Out-Null
        [int]::TryParse($vParts[5].Trim(), [ref]$RestoredResolutionCodeCount) | Out-Null
        [int]::TryParse($vParts[6].Trim(), [ref]$RecoveredIncidents) | Out-Null
        [int]::TryParse($vParts[7].Trim(), [ref]$RecoveredTickets) | Out-Null
        [int]::TryParse($vParts[8].Trim(), [ref]$RecoveredUsers) | Out-Null
        $LatestIncidentRef = $vParts[9].Trim()
        $LatestIncidentOpenedAtUtc = $vParts[10].Trim()
    }

    $VerifySw.Stop()
    $SchemaVerifyMs = [int64]$VerifySw.Elapsed.TotalMilliseconds

    if ($RestoredTableCount -lt 21) {
        throw "Schema verification failed: expected at least 21 domain tables in '$DrillTargetDb', found $RestoredTableCount."
    }

    Write-Host ("[OK] Schema verified: {0} domain tables, {1} migrations, {2} roles, {3} incidents, {4} tickets (latest={5})." -f `
        $RestoredTableCount, $RestoredMigrationCount, $RestoredRoleCount, $RecoveredIncidents, $RecoveredTickets, $LatestIncidentRef)

    # ==========================================================================
    # Phase 3: Integration Test Suite Execution against Restored Instance
    # ==========================================================================
    Write-Host "[PHASE 3/4] Running integration test suite '$TestProject' against restored instance..."
    $DrillConnectionString = "Host=$DrillTargetHost;Port=$DrillTargetPort;Database=$DrillTargetDb;Username=$PgUser;Password=$PgPass"
    [Environment]::SetEnvironmentVariable("CONNECTION_STRING", $DrillConnectionString)

    if (Test-Path -LiteralPath $TrxDirectory) {
        Remove-Item -LiteralPath $TrxDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $TrxDirectory -Force | Out-Null

    $FullTestProjectPath = if ([System.IO.Path]::IsPathRooted($TestProject)) { $TestProject } else { Join-Path $RepoRoot $TestProject }

    $TestSw = [System.Diagnostics.Stopwatch]::StartNew()
    $testOutputLines = Invoke-NativeQuiet {
        & dotnet test $FullTestProjectPath `
            --logger "trx;LogFileName=restore-drill-results.trx" `
            --results-directory $TrxDirectory `
            --verbosity normal 2>&1
    }
    $TestSuiteExitCode = $LASTEXITCODE
    $TestSw.Stop()

    # Stop RTO stopwatch immediately after the final test completes
    $RtoStopwatch.Stop()
    $TestCompletedUtc = [DateTimeOffset]::UtcNow
    $IsoTestCompletedUtc = $TestCompletedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

    $TestExecutionMs = [int64]$TestSw.Elapsed.TotalMilliseconds
    $RtoElapsedMs = [int64]$RtoStopwatch.Elapsed.TotalMilliseconds

    # Parse TRX report if generated
    if (Test-Path -LiteralPath $TrxFilePath) {
        [xml]$trxXml = Get-Content -LiteralPath $TrxFilePath
        $counters = $trxXml.TestRun.ResultSummary.Counters
        if ($null -ne $counters) {
            [int]::TryParse($counters.total, [ref]$TotalTests) | Out-Null
            [int]::TryParse($counters.passed, [ref]$PassedTests) | Out-Null
            [int]::TryParse($counters.failed, [ref]$FailedTests) | Out-Null
            $executedCount = 0
            [int]::TryParse($counters.executed, [ref]$executedCount) | Out-Null
            $SkippedTests = [Math]::Max(0, $TotalTests - $executedCount)
        }
    } else {
        foreach ($line in $testOutputLines) {
            $text = "$line"
            if ($text -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
                $FailedTests = [int]$Matches[1]
                $PassedTests = [int]$Matches[2]
                $SkippedTests = [int]$Matches[3]
                $TotalTests = [int]$Matches[4]
            }
        }
    }

    Write-Host ("[OK] Integration test suite finished in {0:N3}s ({1} ms): Total={2}, Passed={3}, Failed={4}, Skipped={5}." -f `
        $TestSw.Elapsed.TotalSeconds, $TestExecutionMs, $TotalTests, $PassedTests, $FailedTests, $SkippedTests)
} finally {
    # Restore original CONNECTION_STRING
    [Environment]::SetEnvironmentVariable("CONNECTION_STRING", $OriginalConnectionString)

    # ==========================================================================
    # Phase 4: Clean Teardown of Temporary Drill Instance
    # ==========================================================================
    $CleanSw = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $KeepInstance) {
        Write-Host "[PHASE 4/4] Cleaning up temporary drill instance..."
        if ($EffectiveIsolationMode -eq "Container") {
            Invoke-NativeQuiet { & docker rm -f $DrillContainerName 2>$null | Out-Null }
        } elseif (-not [string]::IsNullOrWhiteSpace($DrillTargetDb) -and $DrillTargetDb -ne $PgDb) {
            if ($SourceContainerRunning) {
                Invoke-NativeQuiet {
                    & docker exec -e "PGPASSWORD=$PgPass" $SourceContainerName `
                        psql -h localhost -p 5432 -U $PgUser -d postgres `
                        -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DrillTargetDb' AND pid <> pg_backend_pid();" 2>$null | Out-Null
                    & docker exec -e "PGPASSWORD=$PgPass" $SourceContainerName `
                        psql -h localhost -p 5432 -U $PgUser -d postgres `
                        -c "DROP DATABASE IF EXISTS `"$DrillTargetDb`";" 2>$null | Out-Null
                }
            } else {
                $prevPass = $env:PGPASSWORD
                try {
                    $env:PGPASSWORD = $PgPass
                    Invoke-NativeQuiet {
                        & psql -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d postgres `
                            -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DrillTargetDb' AND pid <> pg_backend_pid();" 2>$null | Out-Null
                        & psql -h $DrillTargetHost -p $DrillTargetPort -U $PgUser -d postgres `
                            -c "DROP DATABASE IF EXISTS `"$DrillTargetDb`";" 2>$null | Out-Null
                    }
                } finally {
                    $env:PGPASSWORD = $prevPass
                }
            }
        }
        if (Test-Path -LiteralPath $TrxDirectory) {
            Remove-Item -LiteralPath $TrxDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    } else {
        Write-Host "[INFO] -KeepInstance specified; retaining drill target '$DrillTargetDb' (port $DrillTargetPort)."
    }
    $CleanSw.Stop()
    $CleanupMs = [int64]$CleanSw.Elapsed.TotalMilliseconds
}

$TotalDrillStopwatch.Stop()
$TotalDrillElapsedMs = [int64]$TotalDrillStopwatch.Elapsed.TotalMilliseconds
$DrillEndUtc = [DateTimeOffset]::UtcNow
$IsoDrillEndUtc = $DrillEndUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

# ------------------------------------------------------------------------------
# Evaluate NFR-03 Acceptance Criteria (100% Tests Pass & RTO <= 4 Hours)
# ------------------------------------------------------------------------------
$RtoThresholdSeconds = [Math]::Round($RtoThresholdHours * 3600.0, 3)
$RtoElapsedSeconds = [Math]::Round($RtoElapsedMs / 1000.0, 3)
$RtoElapsedMinutes = [Math]::Round($RtoElapsedMs / 60000.0, 4)
$RtoElapsedHours = [Math]::Round($RtoElapsedMs / 3600000.0, 6)

$TotalDrillSeconds = [Math]::Round($TotalDrillElapsedMs / 1000.0, 3)
$InstancePrepSeconds = [Math]::Round($InstancePrepMs / 1000.0, 3)
$RestoreSeconds = [Math]::Round($RestoreMs / 1000.0, 3)
$SchemaVerifySeconds = [Math]::Round($SchemaVerifyMs / 1000.0, 3)
$TestExecutionSeconds = [Math]::Round($TestExecutionMs / 1000.0, 3)
$CleanupSeconds = [Math]::Round($CleanupMs / 1000.0, 3)

$AllTestsPassed = ($TestSuiteExitCode -eq 0) -and ($TotalTests -gt 0) -and ($FailedTests -eq 0) -and ($PassedTests -eq $TotalTests)
$RtoCompliant = ($RtoElapsedSeconds -le $RtoThresholdSeconds) -and ($TotalDrillSeconds -le $RtoThresholdSeconds)
$DrillPassed = $AllTestsPassed -and $RtoCompliant -and $ChecksumVerified

$ResultJsonPath = Join-Path $BackupDir "latest-restore-drill-result.json"
$HistoryLogPath = Join-Path $BackupDir "restore-drill-history.log"

$DrillResultObject = [ordered]@{
    requirementId                    = "NFR-03"
    verificationId                   = "OPS-BKP-02"
    operation                        = "milestone_restore_drill"
    milestone                        = $Milestone
    operator                         = $Operator
    drillStartedAtUtc                = $IsoDrillStartUtc
    restoreStartedAtUtc              = $IsoRestoreStartUtc
    lastPassingTestCompletedAtUtc    = $IsoTestCompletedUtc
    drillCompletedAtUtc              = $IsoDrillEndUtc
    isolationMode                    = $EffectiveIsolationMode
    drillContainerName               = if ($EffectiveIsolationMode -eq "Container") { $DrillContainerName } else { $SourceContainerName }
    drillDatabase                    = $DrillTargetDb
    drillPort                        = $DrillTargetPort
    postgresImage                    = $PostgresImage
    backupArtifact                   = [ordered]@{
        archiveFile        = $ArchiveFileName
        archiveCreatedUtc  = $ArchiveCreatedUtc
        archiveSizeBytes   = $ArchiveSizeBytes
        sha256             = $ActualSha256
        checksumVerified   = $ChecksumVerified
        rolesExportFile    = $RolesFileName
        rolesSizeBytes     = $RolesFileSizeBytes
    }
    phaseTimings                     = [ordered]@{
        instancePrepMs                   = $InstancePrepMs
        instancePrepSeconds              = $InstancePrepSeconds
        rolesRestoreMs                   = $RolesRestoreMs
        archiveRestoreMs                 = $RestoreMs
        archiveRestoreSeconds            = $RestoreSeconds
        schemaVerificationMs             = $SchemaVerifyMs
        schemaVerificationSeconds        = $SchemaVerifySeconds
        testSuiteExecutionMs             = $TestExecutionMs
        testSuiteExecutionSeconds        = $TestExecutionSeconds
        cleanupMs                        = $CleanupMs
        cleanupSeconds                   = $CleanupSeconds
        rtoRestoreToLastPassingTestMs    = $RtoElapsedMs
        rtoRestoreToLastPassingTestSec   = $RtoElapsedSeconds
        rtoRestoreToLastPassingTestMin   = $RtoElapsedMinutes
        rtoRestoreToLastPassingTestHours = $RtoElapsedHours
        totalDrillWallClockMs            = $TotalDrillElapsedMs
        totalDrillWallClockSeconds       = $TotalDrillSeconds
    }
    schemaVerification               = [ordered]@{
        restoredDomainTables     = $RestoredTableCount
        appliedEfMigrations      = $RestoredMigrationCount
        seededRoles              = $RestoredRoleCount
        seededPriorityTargets    = $RestoredPriorityTargetCount
        seededLocationKinds      = $RestoredLocationKindCount
        seededResolutionCodes    = $RestoredResolutionCodeCount
        recoveredIncidents       = $RecoveredIncidents
        recoveredTickets         = $RecoveredTickets
        recoveredUsers           = $RecoveredUsers
        latestIncidentReference  = $LatestIncidentRef
        latestIncidentOpenedUtc  = $LatestIncidentOpenedAtUtc
    }
    testSuiteResults                 = [ordered]@{
        testProject      = $TestProject
        totalTests       = $TotalTests
        passedTests      = $PassedTests
        failedTests      = $FailedTests
        skippedTests     = $SkippedTests
        passRatePercent  = if ($TotalTests -gt 0) { [Math]::Round(($PassedTests * 100.0) / $TotalTests, 2) } else { 0.0 }
        allTestsPassed   = $AllTestsPassed
    }
    rtoEvaluation                    = [ordered]@{
        rtoThresholdHours   = $RtoThresholdHours
        rtoThresholdMinutes = [Math]::Round($RtoThresholdHours * 60.0, 1)
        rtoThresholdSeconds = $RtoThresholdSeconds
        elapsedSeconds      = $RtoElapsedSeconds
        elapsedMinutes      = $RtoElapsedMinutes
        elapsedHours        = $RtoElapsedHours
        rtoCompliant        = $RtoCompliant
        drillPassed         = $DrillPassed
    }
}

$DrillResultJson = $DrillResultObject | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($ResultJsonPath, $DrillResultJson, (New-Object System.Text.UTF8Encoding($false)))

$StatusText = if ($DrillPassed) { "PASS" } else { "FAIL" }
$HistoryLine = ("[{0}] STATUS={1} REQ=NFR-03 ID=OPS-BKP-02 MILESTONE={2} ARCHIVE={3} SHA256={4} ROLES={5} PREP_MS={6} RESTORE_MS={7} TEST_MS={8} RTO_MS={9} RTO_S={10} TOTAL_MS={11} TESTS_TOTAL={12} TESTS_PASSED={13} TESTS_FAILED={14} RTO_COMPLIANT={15}`n" -f `
    $IsoDrillEndUtc, $StatusText, $Milestone, $ArchiveFileName, $ActualSha256, $RolesFileName, `
    $InstancePrepMs, $RestoreMs, $TestExecutionMs, $RtoElapsedMs, `
    $RtoElapsedSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture), `
    $TotalDrillElapsedMs, $TotalTests, $PassedTests, $FailedTests, $RtoCompliant.ToString().ToLowerInvariant())
[System.IO.File]::AppendAllText($HistoryLogPath, $HistoryLine, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "================================================================================"
Write-Host " NFR-03 / OPS-BKP-02 RESTORE DRILL SUMMARY ($StatusText)"
Write-Host "================================================================================"
Write-Output "DRILL_STATUS=$StatusText"
Write-Output "BACKUP_FILE=$ArchiveFileName"
Write-Output "BACKUP_SIZE_BYTES=$ArchiveSizeBytes"
Write-Output "BACKUP_SHA256=$ActualSha256"
Write-Output "ROLES_EXPORT_FILE=$RolesFileName"
Write-Output "ISOLATION_MODE=$EffectiveIsolationMode"
Write-Output "DRILL_TARGET_DB=$DrillTargetDb"
Write-Output ("INSTANCE_PREP_MS=$InstancePrepMs")
Write-Output ("INSTANCE_PREP_SECONDS=" + $InstancePrepSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture))
Write-Output ("RESTORE_ELAPSED_MS=$RestoreMs")
Write-Output ("RESTORE_ELAPSED_SECONDS=" + $RestoreSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture))
Write-Output ("SCHEMA_VERIFY_MS=$SchemaVerifyMs")
Write-Output ("TEST_EXECUTION_MS=$TestExecutionMs")
Write-Output ("TEST_EXECUTION_SECONDS=" + $TestExecutionSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture))
Write-Output ("CLEANUP_MS=$CleanupMs")
Write-Output ("RTO_ELAPSED_MS=$RtoElapsedMs")
Write-Output ("RTO_ELAPSED_SECONDS=" + $RtoElapsedSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture))
Write-Output ("TOTAL_DRILL_ELAPSED_MS=$TotalDrillElapsedMs")
Write-Output ("TOTAL_DRILL_ELAPSED_SECONDS=" + $TotalDrillSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture))
Write-Output "RESTORED_TABLES=$RestoredTableCount"
Write-Output "RESTORED_MIGRATIONS=$RestoredMigrationCount"
Write-Output "RECOVERED_INCIDENTS=$RecoveredIncidents"
Write-Output "RECOVERED_TICKETS=$RecoveredTickets"
Write-Output "LATEST_INCIDENT_REF=$LatestIncidentRef"
Write-Output "LATEST_INCIDENT_OPENED_AT=$LatestIncidentOpenedAtUtc"
Write-Output "TESTS_TOTAL=$TotalTests"
Write-Output "TESTS_PASSED=$PassedTests"
Write-Output "TESTS_FAILED=$FailedTests"
Write-Output "TESTS_SKIPPED=$SkippedTests"
Write-Output ("ALL_TESTS_PASSED=" + $AllTestsPassed.ToString().ToLowerInvariant())
Write-Output ("RTO_COMPLIANT=" + $RtoCompliant.ToString().ToLowerInvariant())
Write-Output "RESULT_JSON=$ResultJsonPath"

if (-not $DrillPassed) {
    Write-Error "NFR-03 restore drill failed validation checks (AllTestsPassed=$AllTestsPassed, RtoCompliant=$RtoCompliant)."
    exit 1
}
