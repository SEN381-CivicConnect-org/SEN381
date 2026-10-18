<#
.SYNOPSIS
    Automated Health Check Sampler & Recorder (NFR-09, OPS-AVL-01).

.DESCRIPTION
    Periodically probes the CivicConnect API health check endpoint (/health or /health/ready),
    evaluates client working hours (08:00 - 17:00, Monday through Friday),
    and appends structured JSONL telemetry records into logs/health-samples.jsonl.

.PARAMETER EndpointUrl
    URL of the CivicConnect health check endpoint.
    Default: http://localhost:5060/health (fallback to 5062 if 5060 unreachable).

.PARAMETER IntervalSeconds
    Interval between successive health check samples in seconds.
    Default: 300 (5 minutes, matching NFR-09 specification).

.PARAMETER OutputFile
    Path to the persistent JSONL telemetry log file.
    Default: logs/health-samples.jsonl.

.PARAMETER Continuous
    If specified, runs continuously until cancelled (or until -Count is reached).
    If omitted, records a single sample and exits (ideal for scheduled tasks/cron).

.PARAMETER Count
    Maximum number of samples to take when running continuously.
    Default: unlimited.

.PARAMETER WorkingHoursStart
    Start of working hours (hour of day 0-23). Default: 8 (08:00).

.PARAMETER WorkingHoursEnd
    End of working hours (hour of day 0-23). Default: 17 (17:00).

.EXAMPLE
    .\scripts\sample-health.ps1
    # Samples the health probe once and appends to logs/health-samples.jsonl

.EXAMPLE
    .\scripts\sample-health.ps1 -Continuous -IntervalSeconds 300
    # Runs continuously every 5 minutes recording samples
#>

[CmdletBinding()]
param(
    [string]$EndpointUrl,
    [int]$IntervalSeconds = 300,
    [string]$OutputFile = "logs/health-samples.jsonl",
    [switch]$Continuous,
    [int]$Count = 0,
    [int]$WorkingHoursStart = 8,
    [int]$WorkingHoursEnd = 17,
    [switch]$Quiet
)

$ErrorActionPreference = "Continue"

function Resolve-HealthEndpoint {
    param([string]$SpecifiedUrl)

    if (-not [string]::IsNullOrWhiteSpace($SpecifiedUrl)) {
        return $SpecifiedUrl
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CIVICCONNECT_HEALTH_URL)) {
        return $env:CIVICCONNECT_HEALTH_URL
    }

    # Test standard local dev port 5060 first, then Docker mapped port 5062
    $candidateUrls = @(
        "http://localhost:5060/health",
        "http://localhost:5062/health"
    )

    foreach ($url in $candidateUrls) {
        try {
            $req = [System.Net.WebRequest]::Create($url)
            $req.Timeout = 1000
            $req.Method = "GET"
            $resp = $req.GetResponse()
            $resp.Close()
            return $url
        } catch {
            # Continue checking candidate ports
        }
    }

    # Default fallback to standard port
    return "http://localhost:5060/health"
}

function Test-IsWorkingHour {
    param(
        [datetimeoffset]$Timestamp,
        [int]$StartHour = 8,
        [int]$EndHour = 17
    )

    $localTime = $Timestamp.ToLocalTime()
    $dow = $localTime.DayOfWeek

    # Client working days: Monday through Friday
    if ($dow -eq [DayOfWeek]::Saturday -or $dow -eq [DayOfWeek]::Sunday) {
        return $false
    }

    $hour = $localTime.Hour
    return ($hour -ge $StartHour -and $hour -lt $EndHour)
}

function Invoke-HealthProbeSample {
    param(
        [string]$Url,
        [int]$StartHour,
        [int]$EndHour
    )

    $timestamp = [DateTimeOffset]::UtcNow
    $isWorkingHours = Test-IsWorkingHour -Timestamp $timestamp -StartHour $StartHour -EndHour $EndHour

    $httpStatus = 0
    $dbStatus = "Unknown"
    $rabbitStatus = "Unknown"
    $overallStatus = "Unreachable"
    $success = $false
    $details = $null

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $httpResponse = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 10 -ErrorAction Stop
        $sw.Stop()
        $latencyMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        $httpStatus = 200

        if ($httpResponse) {
            $overallStatus = if ($httpResponse.status) { $httpResponse.status } else { "Healthy" }
            $details = $httpResponse.components

            if ($httpResponse.components) {
                if ($httpResponse.components.database) {
                    $dbStatus = if ($httpResponse.components.database.status) {
                        $httpResponse.components.database.status
                    } else {
                        $httpResponse.components.database
                    }
                }
                if ($httpResponse.components.rabbitmq) {
                    $rabbitStatus = if ($httpResponse.components.rabbitmq.status) {
                        $httpResponse.components.rabbitmq.status
                    } else {
                        $httpResponse.components.rabbitmq
                    }
                }
            }

            $success = ($httpStatus -eq 200 -and $overallStatus -eq "Healthy")
        }
    }
    catch {
        $sw.Stop()
        $latencyMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 2)

        if ($_.Exception.Response) {
            $httpStatus = [int]$_.Exception.Response.StatusCode
            $overallStatus = "Degraded"

            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $bodyText = $reader.ReadToEnd()
                $reader.Close()

                if ($bodyText) {
                    $errObj = ConvertFrom-Json $bodyText -ErrorAction SilentlyContinue
                    if ($errObj) {
                        $details = $errObj.components
                        if ($errObj.status) { $overallStatus = $errObj.status }
                        if ($errObj.components -and $errObj.components.database) {
                            $dbStatus = if ($errObj.components.database.status) { $errObj.components.database.status } else { $errObj.components.database }
                        }
                        if ($errObj.components -and $errObj.components.rabbitmq) {
                            $rabbitStatus = if ($errObj.components.rabbitmq.status) { $errObj.components.rabbitmq.status } else { $errObj.components.rabbitmq }
                        }
                    }
                }
            } catch {
                # Ignore response stream read failure
            }
        }
        else {
            $httpStatus = 0
            $overallStatus = "Unreachable"
            $dbStatus = "Unreachable"
            $rabbitStatus = "Unreachable"
        }

        $success = $false
    }

    $sample = [ordered]@{
        timestamp       = $timestamp.ToString("o")
        httpStatus      = $httpStatus
        databaseStatus  = $dbStatus
        rabbitmqStatus  = $rabbitStatus
        latencyMs       = $latencyMs
        success         = $success
        isWorkingHours  = $isWorkingHours
        overallStatus   = $overallStatus
        components      = $details
    }

    return $sample
}

# 1. Initialize Endpoint and Storage
$resolvedUrl = Resolve-HealthEndpoint -SpecifiedUrl $EndpointUrl
$outDir = Split-Path -Path $OutputFile -Parent
if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path -Path $outDir)) {
    New-Item -Path $outDir -ItemType Directory -Force | Out-Null
}

if (-not $Quiet) {
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " CivicConnect Health Check Sampler (NFR-09 / OPS-AVL-01) " -ForegroundColor Cyan
    Write-Host " Target Endpoint: $resolvedUrl" -ForegroundColor Gray
    Write-Host " Telemetry Log  : $OutputFile" -ForegroundColor Gray
    Write-Host " Working Hours  : $WorkingHoursStart`:00 - $WorkingHoursEnd`:00 (Mon-Fri)" -ForegroundColor Gray
    Write-Host " Sample Interval: $IntervalSeconds s" -ForegroundColor Gray
    Write-Host " Mode           : $(if ($Continuous) { 'Continuous' } else { 'Single Sample' })" -ForegroundColor Gray
    Write-Host "============================================================" -ForegroundColor Cyan
}

$sampleIndex = 0

do {
    $sample = Invoke-HealthProbeSample -Url $resolvedUrl -StartHour $WorkingHoursStart -EndHour $WorkingHoursEnd
    $sampleIndex++

    # Serialize and append to JSONL file
    $jsonLine = ConvertTo-Json -InputObject $sample -Compress -Depth 5
    Add-Content -Path $OutputFile -Value $jsonLine -Encoding UTF8

    if (-not $Quiet) {
        $timeStr = [DateTimeOffset]::Parse($sample.timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        $whTag = if ($sample.isWorkingHours) { "[WORK-HRS]" } else { "[OFF-HRS ]" }
        $statusColor = if ($sample.success) { "Green" } else { "Red" }
        $resTag = if ($sample.success) { "PASS" } else { "FAIL" }

        Write-Host "$timeStr $whTag HTTP $($sample.httpStatus) | DB: $($sample.databaseStatus) | RMQ: $($sample.rabbitmqStatus) | Latency: $($sample.latencyMs)ms | $resTag" -ForegroundColor $statusColor
    }

    if ($Continuous) {
        if ($Count -gt 0 -and $sampleIndex -ge $Count) {
            break
        }
        Start-Sleep -Seconds $IntervalSeconds
    }
} while ($Continuous)

if (-not $Quiet) {
    Write-Host "Health check sampling completed. Total samples recorded: $sampleIndex." -ForegroundColor Cyan
}
