#Requires -Version 5.1
<#
.SYNOPSIS
    CivicConnect Automated Logical Database Backup Script (PowerShell).
.DESCRIPTION
    Implements NFR-02 (Durability and Recovery Point - RPO) and NFR-05 (Retention):
    - Executes a consistent PostgreSQL logical backup using pg_dump (-Fc custom compressed format).
    - Exports cluster roles via pg_dumpall --roles-only (RSK-08 / NFR-03).
    - Generates timestamped archives in backups/ with SHA-256 checksums (.sha256).
    - Enforces backup retention rotation (default 30 days per POPIA s14(1) / NFR-05).
    - Validates non-zero exit codes and logs operation metadata (.meta.json & backup-history.log).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$BackupDir = "",

    [Parameter(Mandatory = $false)]
    [string]$ContainerName = "",

    [Parameter(Mandatory = $false)]
    [int]$RetentionDays = 0,

    [Parameter(Mandatory = $false)]
    [string]$Database = "",

    [Parameter(Mandatory = $false)]
    [string]$EnvFile = "",

    [Parameter(Mandatory = $false)]
    [switch]$SkipRoles
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

if ($RetentionDays -le 0) {
    $envRetention = [Environment]::GetEnvironmentVariable("BACKUP_RETENTION_DAYS")
    $parsedRetention = 0
    if (-not [string]::IsNullOrWhiteSpace($envRetention) -and [int]::TryParse($envRetention, [ref]$parsedRetention) -and $parsedRetention -gt 0) {
        $RetentionDays = $parsedRetention
    } else {
        $RetentionDays = 30
    }
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

if (-not (Test-Path -LiteralPath $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
}
$BackupDir = (Resolve-Path -LiteralPath $BackupDir).Path

$Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$UtcNow = [DateTimeOffset]::UtcNow
$TimestampToken = $UtcNow.ToString("yyyyMMdd_HHmmss'Z'")
$IsoTimestamp = $UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

$ArchiveFileName = "civicconnect_backup_$TimestampToken.dump"
$ArchivePath = Join-Path $BackupDir $ArchiveFileName
$ChecksumPath = "$ArchivePath.sha256"
$RolesFileName = "civicconnect_backup_${TimestampToken}_roles.sql"
$RolesPath = Join-Path $BackupDir $RolesFileName
$MetaPath = "$ArchivePath.meta.json"
$LogFilePath = Join-Path $BackupDir "backup-history.log"

# Determine execution mode: docker exec vs local pg_dump
$UseDocker = $false
$DockerCmd = Get-Command docker -ErrorAction SilentlyContinue
if ($null -ne $DockerCmd) {
    $runningState = & docker inspect -f "{{.State.Running}}" $ContainerName 2>$null
    if ($LASTEXITCODE -eq 0 -and "$runningState".Trim() -eq "true") {
        $UseDocker = $true
    }
}

$ExecMode = if ($UseDocker) { "docker" } else { "direct" }
Write-Host "[INFO] [$IsoTimestamp] Starting CivicConnect logical backup (mode=$ExecMode, db=$PgDb)..."

$IncidentCount = 0
$LatestIncidentRef = "NONE"
$LatestIncidentOpenedAt = "NONE"

if ($UseDocker) {
    $ContainerTmpDump = "/tmp/$ArchiveFileName"

    & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
        pg_dump -h localhost -p 5432 -U $PgUser -d $PgDb `
        --format=custom --compress=6 --no-owner --no-privileges `
        --file=$ContainerTmpDump

    if ($LASTEXITCODE -ne 0) {
        Write-Error "pg_dump failed inside container '$ContainerName' with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    & docker cp "${ContainerName}:${ContainerTmpDump}" $ArchivePath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to copy backup archive from container '$ContainerName' (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    # Validate archive table of contents before removing temporary container file
    & docker exec $ContainerName pg_restore --list $ContainerTmpDump | Out-Null
    $listExit = $LASTEXITCODE
    & docker exec $ContainerName rm -f $ContainerTmpDump | Out-Null

    if ($listExit -ne 0) {
        Write-Error "pg_restore --list verification failed with exit code $listExit."
        exit $listExit
    }

    if (-not $SkipRoles) {
        $ContainerTmpRoles = "/tmp/$RolesFileName"
        & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
            pg_dumpall -h localhost -p 5432 -U $PgUser --roles-only --file=$ContainerTmpRoles 2>$null
        if ($LASTEXITCODE -eq 0) {
            & docker cp "${ContainerName}:${ContainerTmpRoles}" $RolesPath | Out-Null
            & docker exec $ContainerName rm -f $ContainerTmpRoles | Out-Null
        }
    }

    $watermarkQuery = "SELECT COALESCE((SELECT reference_number || '|' || to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE|NONE');"
    $watermarkRaw = & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
        psql -h localhost -p 5432 -U $PgUser -d $PgDb -t -A -c $watermarkQuery 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace("$watermarkRaw")) {
        $parts = ("$watermarkRaw".Trim()) -split '\|', 2
        if ($parts.Count -eq 2) {
            $LatestIncidentRef = $parts[0].Trim()
            $LatestIncidentOpenedAt = $parts[1].Trim()
        }
    }

    $countRaw = & docker exec -e "PGPASSWORD=$PgPass" $ContainerName `
        psql -h localhost -p 5432 -U $PgUser -d $PgDb -t -A -c "SELECT COUNT(*) FROM incident;" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace("$countRaw")) {
        [int]::TryParse(("$countRaw".Trim()), [ref]$IncidentCount) | Out-Null
    }
} else {
    $PgDumpCmd = Get-Command pg_dump -ErrorAction SilentlyContinue
    if ($null -eq $PgDumpCmd) {
        Write-Error "Neither running Docker container '$ContainerName' nor local 'pg_dump' command is available."
        exit 1
    }

    $EffectiveHost = if ($PgHost -eq "db") { "localhost" } else { $PgHost }
    $prevPgPass = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $PgPass
        & pg_dump -h $EffectiveHost -p $PgPort -U $PgUser -d $PgDb `
            --format=custom --compress=6 --no-owner --no-privileges `
            --file=$ArchivePath

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Local pg_dump failed with exit code $LASTEXITCODE."
            exit $LASTEXITCODE
        }

        & pg_restore --list $ArchivePath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "pg_restore --list verification failed with exit code $LASTEXITCODE."
            exit $LASTEXITCODE
        }

        if (-not $SkipRoles) {
            $PgDumpAllCmd = Get-Command pg_dumpall -ErrorAction SilentlyContinue
            if ($null -ne $PgDumpAllCmd) {
                & pg_dumpall -h $EffectiveHost -p $PgPort -U $PgUser --roles-only --file=$RolesPath 2>$null
            }
        }

        $PsqlCmd = Get-Command psql -ErrorAction SilentlyContinue
        if ($null -ne $PsqlCmd) {
            $watermarkQuery = "SELECT COALESCE((SELECT reference_number || '|' || to_char(opened_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') || 'T' || to_char(opened_at AT TIME ZONE 'UTC', 'HH24:MI:SS.MS') || 'Z' FROM incident ORDER BY opened_at DESC, id DESC LIMIT 1), 'NONE|NONE');"
            $watermarkRaw = & psql -h $EffectiveHost -p $PgPort -U $PgUser -d $PgDb -t -A -c $watermarkQuery 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace("$watermarkRaw")) {
                $parts = ("$watermarkRaw".Trim()) -split '\|', 2
                if ($parts.Count -eq 2) {
                    $LatestIncidentRef = $parts[0].Trim()
                    $LatestIncidentOpenedAt = $parts[1].Trim()
                }
            }

            $countRaw = & psql -h $EffectiveHost -p $PgPort -U $PgUser -d $PgDb -t -A -c "SELECT COUNT(*) FROM incident;" 2>$null
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace("$countRaw")) {
                [int]::TryParse(("$countRaw".Trim()), [ref]$IncidentCount) | Out-Null
            }
        }
    } finally {
        $env:PGPASSWORD = $prevPgPass
    }
}

if (-not (Test-Path -LiteralPath $ArchivePath)) {
    Write-Error "Backup archive '$ArchivePath' was not created."
    exit 1
}

$ArchiveItem = Get-Item -LiteralPath $ArchivePath
if ($ArchiveItem.Length -le 0) {
    Write-Error "Backup archive '$ArchivePath' is empty (0 bytes)."
    exit 1
}

# Compute SHA-256 checksum
$FileHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$ChecksumContent = "$FileHash  $ArchiveFileName`n"
[System.IO.File]::WriteAllText($ChecksumPath, $ChecksumContent, (New-Object System.Text.UTF8Encoding($false)))

# Enforce backup retention rotation (POPIA s14(1) / NFR-05: bound backup retention to 30 days)
$CutoffUtc = $UtcNow.UtcDateTime.AddDays(-$RetentionDays)
$PrunedCount = 0
$ExistingDumps = Get-ChildItem -LiteralPath $BackupDir -Filter "civicconnect_backup_*.dump" -File -ErrorAction SilentlyContinue
foreach ($dump in $ExistingDumps) {
    if ($dump.FullName -eq $ArchivePath) {
        continue
    }
    if ($dump.LastWriteTimeUtc -lt $CutoffUtc) {
        $basePrefix = $dump.FullName.Substring(0, $dump.FullName.Length - 5)
        $companionFiles = @(
            $dump.FullName,
            "$($dump.FullName).sha256",
            "$($dump.FullName).meta.json",
            "${basePrefix}_roles.sql"
        )
        foreach ($cf in $companionFiles) {
            if (Test-Path -LiteralPath $cf) {
                Remove-Item -LiteralPath $cf -Force
            }
        }
        $PrunedCount++
    }
}

$Stopwatch.Stop()
$ElapsedMs = [int64]$Stopwatch.Elapsed.TotalMilliseconds

$MetaObject = [ordered]@{
    requirementId             = "NFR-02"
    operation                 = "logical_backup"
    timestampUtc              = $IsoTimestamp
    database                  = $PgDb
    executionMode             = $ExecMode
    archiveFile               = $ArchiveFileName
    archiveSizeBytes          = $ArchiveItem.Length
    sha256                    = $FileHash
    rolesExportFile           = if (Test-Path -LiteralPath $RolesPath) { $RolesFileName } else { "" }
    incidentCount             = $IncidentCount
    latestIncidentReference   = $LatestIncidentRef
    latestIncidentOpenedAtUtc = $LatestIncidentOpenedAt
    retentionDays             = $RetentionDays
    prunedArchivesCount       = $PrunedCount
    elapsedDurationMs         = $ElapsedMs
    exitCode                  = 0
}

$MetaJson = $MetaObject | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($MetaPath, $MetaJson, (New-Object System.Text.UTF8Encoding($false)))

$LogLine = "[$IsoTimestamp] STATUS=SUCCESS OP=BACKUP DB=$PgDb FILE=$ArchiveFileName SIZE_BYTES=$($ArchiveItem.Length) SHA256=$FileHash INCIDENTS=$IncidentCount LATEST_INCIDENT=$LatestIncidentRef LATEST_OPENED_AT=$LatestIncidentOpenedAt RETENTION_DAYS=$RetentionDays PRUNED=$PrunedCount ELAPSED_MS=$ElapsedMs`n"
[System.IO.File]::AppendAllText($LogFilePath, $LogLine, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[OK] Backup completed successfully."
Write-Output "BACKUP_FILE=$ArchivePath"
Write-Output "CHECKSUM_FILE=$ChecksumPath"
Write-Output "SHA256=$FileHash"
Write-Output "ARCHIVE_SIZE_BYTES=$($ArchiveItem.Length)"
Write-Output "INCIDENT_COUNT=$IncidentCount"
Write-Output "LATEST_INCIDENT_REF=$LatestIncidentRef"
Write-Output "LATEST_INCIDENT_OPENED_AT=$LatestIncidentOpenedAt"
Write-Output "PRUNED_COUNT=$PrunedCount"
Write-Output "ELAPSED_BACKUP_MS=$ElapsedMs"
