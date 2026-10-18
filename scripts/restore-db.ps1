#Requires -Version 5.1
<#
.SYNOPSIS
    CivicConnect Automated Database Restore Script (PowerShell).
.DESCRIPTION
    Implements NFR-02 (Durability and Recovery Point - RPO) and NFR-03 (Verified Restore):
    - Restores a specified pg_dump custom-format archive (or the most recent archive in backups/)
      into the target PostgreSQL instance using pg_restore (--clean --if-exists --no-owner --no-privileges --exit-on-error).
    - Verifies SHA-256 checksum integrity before restoring.
    - Recreates the target database automatically if it was dropped during catastrophic loss.
    - Measures and outputs wall-clock recovery duration (ms and seconds) and evaluates 24-hour RPO compliance.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [Alias("File")]
    [string]$BackupFile = "",

    [Parameter(Mandatory = $false)]
    [switch]$Latest,

    [Parameter(Mandatory = $false)]
    [string]$BackupDir = "",

    [Parameter(Mandatory = $false)]
    [Alias("TargetDatabase")]
    [string]$Database = "",

    [Parameter(Mandatory = $false)]
    [string]$ContainerName = "",

    [Parameter(Mandatory = $false)]
    [string]$EnvFile = "",

    [Parameter(Mandatory = $false)]
    [switch]$RecreateDatabase,

    [Parameter(Mandatory = $false)]
    [switch]$SkipChecksum,

    [Parameter(Mandatory = $false)]
    [string]$DestructionTimestampUtc = ""
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

if ([string]::IsNullOrWhiteSpace($ContainerName)) {
    $envContainer = [Environment]::GetEnvironmentVariable("POSTGRES_CONTAINER")
    $ContainerName = if (-not [string]::IsNullOrWhiteSpace($envContainer)) { $envContainer } else { "civicconnect-db" }
}

# Parse CONNECTION_STRING if present
$ConnStr = [Environment]::GetEnvironmentVariable("CONNECTION_STRING")
$ConnHost = ""
$ConnPort = ""
$ConnDb = ""
$ConnUser = ""
$ConnPass = ""

if (-not [string]::IsNullOrWhiteSpace($ConnStr)) {
    foreach ($segment in ($ConnStr -split ';')) {
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
$PgDb   = if (-not [string]::IsNullOrWhiteSpace($Database)) { $Database } elseif (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_DB)) { $env:POSTGRES_DB } elseif (-not [string]::IsNullOrWhiteSpace($ConnDb)) { $ConnDb } else { "civicconnect" }
$PgUser = if (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_USER)) { $env:POSTGRES_USER } elseif (-not [string]::IsNullOrWhiteSpace($ConnUser)) { $ConnUser } else { "civicconnect" }
$PgPass = if (-not [string]::IsNullOrWhiteSpace($env:POSTGRES_PASSWORD)) { $env:POSTGRES_PASSWORD } elseif (-not [string]::IsNullOrWhiteSpace($ConnPass)) { $ConnPass } else { "" }

# NFR-01 validation: reject missing or placeholder passwords
if ([string]::IsNullOrWhiteSpace($PgPass) -or $PgPass.Equals("changeme", [StringComparison]::OrdinalIgnoreCase) -or $PgPass.Contains('${')) {
    Write-Error "NFR-01 violation: POSTGRES_PASSWORD or CONNECTION_STRING password is missing or uses an insecure placeholder."
    exit 1
}

# Resolve backup archive path
if ($Latest -or [string]::IsNullOrWhiteSpace($BackupFile)) {
    if (-not (Test-Path -LiteralPath $BackupDir)) {
        Write-Error "Backup directory '$BackupDir' does not exist."
        exit 1
    }
    $latestDump = Get-ChildItem -LiteralPath $BackupDir -Filter "civicconnect_backup_*.dump" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestDump) {
        Write-Error "No backup archives ('civicconnect_backup_*.dump') found in '$BackupDir'."
        exit 1
    }
    $BackupFile = $latestDump.FullName
} elseif (-not [System.IO.Path]::IsPathRooted($BackupFile)) {
    $candidatePath = Join-Path (Get-Location).Path $BackupFile
    if (Test-Path -LiteralPath $candidatePath) {
        $BackupFile = $candidatePath
    } else {
        $BackupFile = Join-Path $BackupDir $BackupFile
    }
}

if (-not (Test-Path -LiteralPath $BackupFile)) {
    Write-Error "Backup archive '$BackupFile' does not exist."
    exit 1
}
$BackupFile = (Resolve-Path -LiteralPath $BackupFile).Path
$ArchiveFileName = [System.IO.Path]::GetFileName($BackupFile)
$ChecksumPath = "$BackupFile.sha256"

# Verify SHA-256 checksum if present
if (-not $SkipChecksum -and (Test-Path -LiteralPath $ChecksumPath)) {
    $checksumLine = (Get-Content -LiteralPath $ChecksumPath -TotalCount 1).Trim()
    $expectedHash = ($checksumLine -split '\s+')[0].Trim().ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $BackupFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash -ne $actualHash) {
        Write-Error "SHA-256 checksum mismatch for '$ArchiveFileName'. Expected '$expectedHash', got '$actualHash'."
        exit 1
    }
    Write-Host "[INFO] SHA-256 checksum verified ($actualHash)."
}

# Determine execution mode
$UseDocker = $false
$DockerCmd = Get-Command docker -ErrorAction SilentlyContinue
if ($null -ne $DockerCmd) {
    $runningState = & docker inspect -f "{{.State.Running}}" $ContainerName 2>$null
    if ($LASTEXITCODE -eq 0 -and "$runningState".Trim() -eq "true") {
        $UseDocker = $true
    }
}

$ExecMode = if ($UseDocker) { "docker" } else { "direct" }

$StartUtc = [DateTimeOffset]::UtcNow
$IsoStartUtc = $StartUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$DestructionTime = if (-not [string]::IsNullOrWhiteSpace($DestructionTimestampUtc)) {
    [DateTimeOffset]::Parse($DestructionTimestampUtc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
} else {
    $StartUtc
}
$IsoDestructionUtc = $DestructionTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

Write-Host "[INFO] [$IsoStartUtc] Restoring '$ArchiveFileName' into database '$PgDb' (mode=$ExecMode)..."

$Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

if ($UseDocker) {
    $ContainerTmpDump = "/tmp/restore_$ArchiveFileName"
    & docker cp $BackupFile "${ContainerName}:${ContainerTmpDump}"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to copy backup archive '$BackupFile' into container '$ContainerName' (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    try {
        if ($RecreateDatabase) {
            & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
                psql -h localhost -p 5432 -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$PgDb' AND pid <> pg_backend_pid();" | Out-Null

            & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
                psql -h localhost -p 5432 -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                -c "DROP DATABASE IF EXISTS `"$PgDb`";" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to drop database '$PgDb' prior to recreate."
            }

            & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
                psql -h localhost -p 5432 -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                -c "CREATE DATABASE `"$PgDb`";" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create database '$PgDb'."
            }
        } else {
            $dbExistsRaw = & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
                psql -h localhost -p 5432 -U $PgUser -d postgres -t -A `
                -c "SELECT 1 FROM pg_database WHERE datname = '$PgDb';"
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to check whether target database '$PgDb' exists."
            }
            if (("$dbExistsRaw".Trim()) -ne "1") {
                & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
                    psql -h localhost -p 5432 -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                    -c "CREATE DATABASE `"$PgDb`";" | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to create missing target database '$PgDb'."
                }
            }
        }

        # Reset public schema cleanly before pg_restore
        & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
            psql -h localhost -p 5432 -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 `
            -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to reset public schema in '$PgDb'."
        }

        & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
            pg_restore -h localhost -p 5432 -U $PgUser -d $PgDb `
            --clean --if-exists --no-owner --no-privileges --exit-on-error `
            $ContainerTmpDump

        if ($LASTEXITCODE -ne 0) {
            throw "pg_restore failed with exit code $LASTEXITCODE."
        }
    } finally {
        & docker exec $ContainerName rm -f $ContainerTmpDump 2>$null | Out-Null
    }
} else {
    $PgRestoreCmd = Get-Command pg_restore -ErrorAction SilentlyContinue
    if ($null -eq $PgRestoreCmd) {
        Write-Error "Neither running Docker container '$ContainerName' nor local 'pg_restore' command is available."
        exit 1
    }

    $EffectiveHost = if ($PgHost -eq "db") { "localhost" } else { $PgHost }
    $prevPgPass = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $PgPass

        $PsqlCmd = Get-Command psql -ErrorAction SilentlyContinue
        if ($null -ne $PsqlCmd) {
            if ($RecreateDatabase) {
                & psql -h $EffectiveHost -p $PgPort -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                    -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$PgDb' AND pid <> pg_backend_pid();" | Out-Null
                & psql -h $EffectiveHost -p $PgPort -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                    -c "DROP DATABASE IF EXISTS `"$PgDb`";" | Out-Null
                & psql -h $EffectiveHost -p $PgPort -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                    -c "CREATE DATABASE `"$PgDb`";" | Out-Null
            } else {
                $dbExistsRaw = & psql -h $EffectiveHost -p $PgPort -U $PgUser -d postgres -t -A `
                    -c "SELECT 1 FROM pg_database WHERE datname = '$PgDb';"
                if (("$dbExistsRaw".Trim()) -ne "1") {
                    & psql -h $EffectiveHost -p $PgPort -U $PgUser -d postgres -v ON_ERROR_STOP=1 `
                        -c "CREATE DATABASE `"$PgDb`";" | Out-Null
                }
            }

            & psql -h $EffectiveHost -p $PgPort -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 `
                -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO public;" | Out-Null
        }

        & pg_restore -h $EffectiveHost -p $PgPort -U $PgUser -d $PgDb `
            --clean --if-exists --no-owner --no-privileges --exit-on-error `
            $BackupFile

        if ($LASTEXITCODE -ne 0) {
            throw "Local pg_restore failed with exit code $LASTEXITCODE."
        }
    } finally {
        $env:PGPASSWORD = $prevPgPass
    }
}

$Stopwatch.Stop()
$ElapsedMs = [int64]$Stopwatch.Elapsed.TotalMilliseconds
$ElapsedSeconds = [Math]::Round($Stopwatch.Elapsed.TotalSeconds, 3)

# Query recovered incident metrics and compute RPO compliance
$RecoveredIncidents = 0
$RecoveredTickets = 0
$LatestIncidentRef = "NONE"
$LatestIncidentOpenedAt = "NONE"
$RpoGapHours = 0.0
$RpoCompliant = $true

$MetricsSql = "SELECT (SELECT COUNT(*) FROM incident) || '|' || (SELECT COUNT(*) FROM ticket) || '|' || COALESCE((SELECT reference_number FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE') || '|' || COALESCE((SELECT to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE');"

$MetricsRaw = ""
if ($UseDocker) {
    $MetricsRaw = & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
        psql -h localhost -p 5432 -U $PgUser -d $PgDb -t -A -c $MetricsSql 2>$null
} else {
    $PsqlCmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($null -ne $PsqlCmd) {
        $EffectiveHost = if ($PgHost -eq "db") { "localhost" } else { $PgHost }
        $prevPgPass = $env:PGPASSWORD
        try {
            $env:PGPASSWORD = $PgPass
            $MetricsRaw = & psql -h $EffectiveHost -p $PgPort -U $PgUser -d $PgDb -t -A -c $MetricsSql 2>$null
        } finally {
            $env:PGPASSWORD = $prevPgPass
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace("$MetricsRaw")) {
    $parts = ("$MetricsRaw".Trim()) -split '\|', 4
    if ($parts.Count -eq 4) {
        [int]::TryParse($parts[0].Trim(), [ref]$RecoveredIncidents) | Out-Null
        [int]::TryParse($parts[1].Trim(), [ref]$RecoveredTickets) | Out-Null
        $LatestIncidentRef = $parts[2].Trim()
        $LatestIncidentOpenedAt = $parts[3].Trim()

        if ($LatestIncidentOpenedAt -ne "NONE") {
            $openedAtDto = [DateTimeOffset]::Parse($LatestIncidentOpenedAt, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal).ToUniversalTime()
            $gapSpan = $DestructionTime - $openedAtDto
            $RpoGapHours = [Math]::Round([Math]::Max(0.0, $gapSpan.TotalHours), 4)
            $RpoCompliant = ($gapSpan.TotalHours -le 24.0)
        }
    }
}

$EndUtc = [DateTimeOffset]::UtcNow
$IsoEndUtc = $EndUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

if (-not (Test-Path -LiteralPath $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
}
$LogFilePath = Join-Path $BackupDir "restore-history.log"
$ResultJsonPath = Join-Path $BackupDir "latest-restore-result.json"

$ResultObject = [ordered]@{
    requirementId                        = "NFR-02"
    testId                               = "OPS-BKP-01"
    operation                            = "database_restore"
    restoreStartedAtUtc                  = $IsoStartUtc
    restoreCompletedAtUtc                = $IsoEndUtc
    destructionTimestampUtc              = $IsoDestructionUtc
    targetDatabase                       = $PgDb
    backupFile                           = $ArchiveFileName
    elapsedRecoveryMs                    = $ElapsedMs
    elapsedRecoverySeconds               = $ElapsedSeconds
    recoveredIncidentCount               = $RecoveredIncidents
    recoveredTicketCount                 = $RecoveredTickets
    latestRecoverableIncidentRef         = $LatestIncidentRef
    latestRecoverableIncidentOpenedAtUtc = $LatestIncidentOpenedAt
    rpoGapHours                          = $RpoGapHours
    rpoMaxAllowedHours                   = 24.0
    rpoCompliant                         = $RpoCompliant
    exitCode                             = 0
}

$ResultJson = $ResultObject | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($ResultJsonPath, $ResultJson, (New-Object System.Text.UTF8Encoding($false)))

$ElapsedSecondsStr = $ElapsedSeconds.ToString("0.000", [System.Globalization.CultureInfo]::InvariantCulture)
$RpoGapHoursStr = $RpoGapHours.ToString("0.0000", [System.Globalization.CultureInfo]::InvariantCulture)

$LogLine = "[$IsoEndUtc] STATUS=SUCCESS OP=RESTORE DB=$PgDb FILE=$ArchiveFileName ELAPSED_MS=$ElapsedMs ELAPSED_S=$ElapsedSecondsStr INCIDENTS=$RecoveredIncidents TICKETS=$RecoveredTickets LATEST_INCIDENT=$LatestIncidentRef LATEST_OPENED_AT=$LatestIncidentOpenedAt RPO_GAP_HOURS=$RpoGapHoursStr RPO_COMPLIANT=$($RpoCompliant.ToString().ToLowerInvariant())`n"
[System.IO.File]::AppendAllText($LogFilePath, $LogLine, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[OK] Database restore completed in ${ElapsedSecondsStr}s ($ElapsedMs ms)."
Write-Output "BACKUP_FILE=$BackupFile"
Write-Output "TARGET_DATABASE=$PgDb"
Write-Output "ELAPSED_RECOVERY_MS=$ElapsedMs"
Write-Output "ELAPSED_RECOVERY_SECONDS=$ElapsedSecondsStr"
Write-Output "RECOVERED_INCIDENT_COUNT=$RecoveredIncidents"
Write-Output "RECOVERED_TICKET_COUNT=$RecoveredTickets"
Write-Output "LATEST_INCIDENT_REF=$LatestIncidentRef"
Write-Output "LATEST_INCIDENT_OPENED_AT=$LatestIncidentOpenedAt"
Write-Output "RPO_GAP_HOURS=$RpoGapHoursStr"
Write-Output ("RPO_COMPLIANT=" + $RpoCompliant.ToString().ToLowerInvariant())
