using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Operations;

/// <summary>
/// Result of an automated logical database backup execution (<c>NFR-02</c>, <c>OPS-BKP-01</c>).
/// </summary>
public sealed record BackupExecutionResult(
    string ArchivePath,
    string ChecksumPath,
    string Sha256Hash,
    long ArchiveSizeBytes,
    int IncidentCount,
    string LatestIncidentReference,
    DateTimeOffset? LatestIncidentOpenedAtUtc,
    int PrunedArchivesCount,
    TimeSpan ElapsedDuration);

/// <summary>
/// Result of an automated database restore execution (<c>NFR-02</c>, <c>NFR-03</c>, <c>OPS-BKP-01</c>).
/// </summary>
public sealed record RestoreExecutionResult(
    string BackupFilePath,
    string TargetDatabase,
    TimeSpan ElapsedRecoveryDuration,
    int RecoveredIncidentCount,
    int RecoveredTicketCount,
    string LatestIncidentReference,
    DateTimeOffset? LatestIncidentOpenedAtUtc,
    TimeSpan RpoGap,
    bool IsRpoCompliant);

/// <summary>
/// Evaluation of Recovery Point Objective (RPO) compliance after database restoration (<c>NFR-02</c>).
/// </summary>
public sealed record RpoEvaluationResult(
    bool IsCompliant,
    TimeSpan RecoveryPointGap,
    TimeSpan AllowedRpoWindow,
    string? LatestIncidentReference,
    DateTimeOffset? LatestIncidentOpenedAtUtc,
    DateTimeOffset DestructionTimestampUtc,
    TimeSpan ElapsedRecoveryDuration);

/// <summary>
/// Contract for executing logical backups, database restores, checksum verification,
/// 30-day POPIA retention rotation, and RPO compliance verification (<c>NFR-02</c>, <c>OPS-BKP-01</c>).
/// </summary>
public interface IDatabaseBackupManager
{
    Task<BackupExecutionResult> RunBackupAsync(
        string? backupDirectory = null,
        string? databaseName = null,
        int retentionDays = DatabaseBackupManager.DefaultRetentionDays,
        string? containerName = null,
        CancellationToken cancellationToken = default);

    Task<RestoreExecutionResult> RunRestoreAsync(
        string? backupFilePath = null,
        string? targetDatabase = null,
        DateTimeOffset? destructionTimestampUtc = null,
        bool recreateDatabase = false,
        string? containerName = null,
        CancellationToken cancellationToken = default);

    Task<RpoEvaluationResult> EvaluateRpoAsync(
        CivicConnectDbContext dbContext,
        DateTimeOffset destructionTimestampUtc,
        TimeSpan elapsedRecoveryDuration,
        TimeSpan? allowedRpoWindow = null,
        CancellationToken cancellationToken = default);

    bool VerifyArchiveChecksum(string archivePath, string? checksumFilePath = null);

    IReadOnlyList<string> PruneExpiredBackups(
        string backupDirectory,
        int retentionDays = DatabaseBackupManager.DefaultRetentionDays,
        DateTimeOffset? referenceTimeUtc = null);
}

/// <summary>
/// Implements <c>NFR-02: Durability and Recovery Point (RPO)</c> and <c>NFR-05: Retention and Disposal</c>.
/// Orchestrates <c>scripts/backup-db.*</c> and <c>scripts/restore-db.*</c>, validates SHA-256 digests,
/// enforces the 30-day backup retention ceiling, and verifies that the most recent recoverable incident
/// is within the 24-hour Milestone 2/3 RPO window (15-minute production WAL baseline).
/// </summary>
public sealed class DatabaseBackupManager : IDatabaseBackupManager
{
    /// <summary>
    /// Milestones 2 and 3 Recovery Point Objective: no more than 24 hours of committed request data lost (NFR-02).
    /// </summary>
    public static readonly TimeSpan MilestoneRpoWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Production baseline Recovery Point Objective: no more than 15 minutes of committed request data lost (NFR-02).
    /// </summary>
    public static readonly TimeSpan ProductionBaselineRpoWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Recovery Time Objective ceiling per NFR-03: 4 hours.
    /// </summary>
    public static readonly TimeSpan RecoveryTimeObjectiveWindow = TimeSpan.FromHours(4);

    /// <summary>
    /// Continuous WAL archiving timeout in seconds (15 minutes = 900 seconds) for production baseline (NFR-02).
    /// </summary>
    public const int WalArchiveTimeoutSeconds = 900;

    /// <summary>
    /// Maximum backup retention period in days per POPIA s14(1) and NFR-05 (30 days).
    /// </summary>
    public const int DefaultRetentionDays = 30;

    private readonly string _repositoryRoot;

    public DatabaseBackupManager(string? repositoryRoot = null)
    {
        _repositoryRoot = !string.IsNullOrWhiteSpace(repositoryRoot)
            ? Path.GetFullPath(repositoryRoot)
            : ResolveRepositoryRoot();
    }

    public string RepositoryRoot => _repositoryRoot;

    public async Task<BackupExecutionResult> RunBackupAsync(
        string? backupDirectory = null,
        string? databaseName = null,
        int retentionDays = DefaultRetentionDays,
        string? containerName = null,
        CancellationToken cancellationToken = default)
    {
        if (retentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention days must be at least 1.");
        }

        var effectiveBackupDir = !string.IsNullOrWhiteSpace(backupDirectory)
            ? Path.GetFullPath(backupDirectory)
            : Path.Combine(_repositoryRoot, "backups");

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var scriptPath = Path.Combine(_repositoryRoot, "scripts", isWindows ? "backup-db.ps1" : "backup-db.sh");

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Backup script not found at '{scriptPath}'.", scriptPath);
        }

        var args = new List<string>();
        if (isWindows)
        {
            args.AddRange(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath]);
            args.AddRange(["-BackupDir", effectiveBackupDir]);
            args.AddRange(["-RetentionDays", retentionDays.ToString(CultureInfo.InvariantCulture)]);
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                args.AddRange(["-Database", databaseName]);
            }
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                args.AddRange(["-ContainerName", containerName]);
            }
        }
        else
        {
            args.Add(scriptPath);
            args.AddRange(["--backup-dir", effectiveBackupDir]);
            args.AddRange(["--retention-days", retentionDays.ToString(CultureInfo.InvariantCulture)]);
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                args.AddRange(["--database", databaseName]);
            }
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                args.AddRange(["--container", containerName]);
            }
        }

        var (exitCode, stdout, stderr) = await ExecuteProcessAsync(
            isWindows ? ResolvePowerShellExecutable() : "bash",
            args,
            _repositoryRoot,
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Database backup script failed with exit code {exitCode}.\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        }

        var kv = ParseKeyValueOutput(stdout);
        var archivePath = kv.GetValueOrDefault("BACKUP_FILE") ?? string.Empty;
        var checksumPath = kv.GetValueOrDefault("CHECKSUM_FILE") ?? $"{archivePath}.sha256";
        var sha256 = kv.GetValueOrDefault("SHA256") ?? string.Empty;
        long.TryParse(kv.GetValueOrDefault("ARCHIVE_SIZE_BYTES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sizeBytes);
        int.TryParse(kv.GetValueOrDefault("INCIDENT_COUNT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var incidentCount);
        var latestRef = kv.GetValueOrDefault("LATEST_INCIDENT_REF") ?? "NONE";
        var latestOpenedRaw = kv.GetValueOrDefault("LATEST_INCIDENT_OPENED_AT");
        int.TryParse(kv.GetValueOrDefault("PRUNED_COUNT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var prunedCount);
        long.TryParse(kv.GetValueOrDefault("ELAPSED_BACKUP_MS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs);

        DateTimeOffset? latestOpenedAt = null;
        if (!string.IsNullOrWhiteSpace(latestOpenedRaw) &&
            !string.Equals(latestOpenedRaw, "NONE", StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.TryParse(latestOpenedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDto))
        {
            latestOpenedAt = parsedDto.ToUniversalTime();
        }

        return new BackupExecutionResult(
            ArchivePath: archivePath,
            ChecksumPath: checksumPath,
            Sha256Hash: sha256,
            ArchiveSizeBytes: sizeBytes,
            IncidentCount: incidentCount,
            LatestIncidentReference: latestRef,
            LatestIncidentOpenedAtUtc: latestOpenedAt,
            PrunedArchivesCount: prunedCount,
            ElapsedDuration: TimeSpan.FromMilliseconds(elapsedMs));
    }

    public async Task<RestoreExecutionResult> RunRestoreAsync(
        string? backupFilePath = null,
        string? targetDatabase = null,
        DateTimeOffset? destructionTimestampUtc = null,
        bool recreateDatabase = false,
        string? containerName = null,
        CancellationToken cancellationToken = default)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var scriptPath = Path.Combine(_repositoryRoot, "scripts", isWindows ? "restore-db.ps1" : "restore-db.sh");

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Restore script not found at '{scriptPath}'.", scriptPath);
        }

        var effectiveDestructionUtc = (destructionTimestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var isoDestruction = effectiveDestructionUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        var args = new List<string>();
        if (isWindows)
        {
            args.AddRange(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath]);
            if (!string.IsNullOrWhiteSpace(backupFilePath))
            {
                args.AddRange(["-BackupFile", Path.GetFullPath(backupFilePath)]);
            }
            else
            {
                args.Add("-Latest");
            }

            if (!string.IsNullOrWhiteSpace(targetDatabase))
            {
                args.AddRange(["-Database", targetDatabase]);
            }
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                args.AddRange(["-ContainerName", containerName]);
            }
            if (recreateDatabase)
            {
                args.Add("-RecreateDatabase");
            }
            args.AddRange(["-DestructionTimestampUtc", isoDestruction]);
        }
        else
        {
            args.Add(scriptPath);
            if (!string.IsNullOrWhiteSpace(backupFilePath))
            {
                args.AddRange(["--file", Path.GetFullPath(backupFilePath)]);
            }
            else
            {
                args.Add("--latest");
            }

            if (!string.IsNullOrWhiteSpace(targetDatabase))
            {
                args.AddRange(["--database", targetDatabase]);
            }
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                args.AddRange(["--container", containerName]);
            }
            if (recreateDatabase)
            {
                args.Add("--recreate-db");
            }
            args.AddRange(["--destruction-timestamp", isoDestruction]);
        }

        var (exitCode, stdout, stderr) = await ExecuteProcessAsync(
            isWindows ? ResolvePowerShellExecutable() : "bash",
            args,
            _repositoryRoot,
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Database restore script failed with exit code {exitCode}.\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        }

        var kv = ParseKeyValueOutput(stdout);
        var resolvedBackupFile = kv.GetValueOrDefault("BACKUP_FILE") ?? backupFilePath ?? string.Empty;
        var resolvedTargetDb = kv.GetValueOrDefault("TARGET_DATABASE") ?? targetDatabase ?? "civicconnect";
        long.TryParse(kv.GetValueOrDefault("ELAPSED_RECOVERY_MS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs);
        int.TryParse(kv.GetValueOrDefault("RECOVERED_INCIDENT_COUNT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recoveredIncidents);
        int.TryParse(kv.GetValueOrDefault("RECOVERED_TICKET_COUNT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recoveredTickets);
        var latestRef = kv.GetValueOrDefault("LATEST_INCIDENT_REF") ?? "NONE";
        var latestOpenedRaw = kv.GetValueOrDefault("LATEST_INCIDENT_OPENED_AT");
        double.TryParse(kv.GetValueOrDefault("RPO_GAP_HOURS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rpoGapHours);
        var rpoCompliantStr = kv.GetValueOrDefault("RPO_COMPLIANT") ?? "true";
        var isRpoCompliant = string.Equals(rpoCompliantStr, "true", StringComparison.OrdinalIgnoreCase);

        DateTimeOffset? latestOpenedAt = null;
        if (!string.IsNullOrWhiteSpace(latestOpenedRaw) &&
            !string.Equals(latestOpenedRaw, "NONE", StringComparison.OrdinalIgnoreCase) &&
            DateTimeOffset.TryParse(latestOpenedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDto))
        {
            latestOpenedAt = parsedDto.ToUniversalTime();
        }

        return new RestoreExecutionResult(
            BackupFilePath: resolvedBackupFile,
            TargetDatabase: resolvedTargetDb,
            ElapsedRecoveryDuration: TimeSpan.FromMilliseconds(elapsedMs),
            RecoveredIncidentCount: recoveredIncidents,
            RecoveredTicketCount: recoveredTickets,
            LatestIncidentReference: latestRef,
            LatestIncidentOpenedAtUtc: latestOpenedAt,
            RpoGap: TimeSpan.FromHours(rpoGapHours),
            IsRpoCompliant: isRpoCompliant);
    }

    public async Task<RpoEvaluationResult> EvaluateRpoAsync(
        CivicConnectDbContext dbContext,
        DateTimeOffset destructionTimestampUtc,
        TimeSpan elapsedRecoveryDuration,
        TimeSpan? allowedRpoWindow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var maxAllowedRpo = allowedRpoWindow ?? MilestoneRpoWindow;
        var utcDestruction = destructionTimestampUtc.ToUniversalTime();

        var latestIncident = await dbContext.Incidents
            .AsNoTracking()
            .OrderByDescending(i => i.OpenedAt)
            .ThenByDescending(i => i.Id)
            .Select(i => new { i.ReferenceNumber, i.OpenedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestIncident is null)
        {
            return new RpoEvaluationResult(
                IsCompliant: false,
                RecoveryPointGap: TimeSpan.MaxValue,
                AllowedRpoWindow: maxAllowedRpo,
                LatestIncidentReference: null,
                LatestIncidentOpenedAtUtc: null,
                DestructionTimestampUtc: utcDestruction,
                ElapsedRecoveryDuration: elapsedRecoveryDuration);
        }

        var openedAtUtc = latestIncident.OpenedAt.ToUniversalTime();
        var gap = utcDestruction - openedAtUtc;
        if (gap < TimeSpan.Zero)
        {
            gap = TimeSpan.Zero;
        }

        var isCompliant = gap <= maxAllowedRpo;

        return new RpoEvaluationResult(
            IsCompliant: isCompliant,
            RecoveryPointGap: gap,
            AllowedRpoWindow: maxAllowedRpo,
            LatestIncidentReference: latestIncident.ReferenceNumber,
            LatestIncidentOpenedAtUtc: openedAtUtc,
            DestructionTimestampUtc: utcDestruction,
            ElapsedRecoveryDuration: elapsedRecoveryDuration);
    }

    public bool VerifyArchiveChecksum(string archivePath, string? checksumFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return false;
        }

        var effectiveChecksumPath = !string.IsNullOrWhiteSpace(checksumFilePath)
            ? checksumFilePath
            : $"{archivePath}.sha256";

        if (!File.Exists(effectiveChecksumPath))
        {
            return false;
        }

        var rawLine = File.ReadLines(effectiveChecksumPath).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var expectedHash = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0].Trim().ToLowerInvariant();
        using var stream = File.OpenRead(archivePath);
        var hashBytes = SHA256.HashData(stream);
        var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> PruneExpiredBackups(
        string backupDirectory,
        int retentionDays = DefaultRetentionDays,
        DateTimeOffset? referenceTimeUtc = null)
    {
        if (retentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention period must be at least 1 day.");
        }

        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
        {
            return [];
        }

        var cutoffUtc = (referenceTimeUtc ?? DateTimeOffset.UtcNow).UtcDateTime.AddDays(-retentionDays);
        var prunedFiles = new List<string>();

        foreach (var dumpFile in Directory.EnumerateFiles(backupDirectory, "civicconnect_backup_*.dump"))
        {
            var lastWriteUtc = File.GetLastWriteTimeUtc(dumpFile);
            if (lastWriteUtc >= cutoffUtc)
            {
                continue;
            }

            var basePrefix = dumpFile[..^5]; // strip ".dump"
            var companionPaths = new[]
            {
                dumpFile,
                $"{dumpFile}.sha256",
                $"{dumpFile}.meta.json",
                $"{basePrefix}_roles.sql"
            };

            foreach (var path in companionPaths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    prunedFiles.Add(path);
                }
            }
        }

        return prunedFiles;
    }

    private static string ResolveRepositoryRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var candidate in candidates)
        {
            var dir = new DirectoryInfo(candidate);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CivicConnect.sln")) ||
                    Directory.Exists(Path.Combine(dir.FullName, "scripts")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string ResolvePowerShellExecutable()
    {
        var pwsh = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p, "pwsh.exe"))
            .FirstOrDefault(File.Exists);

        return !string.IsNullOrWhiteSpace(pwsh) ? pwsh : "powershell.exe";
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> ExecuteProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return (process.ExitCode, stdOut, stdErr);
    }

    private static Dictionary<string, string> ParseKeyValueOutput(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0)
            {
                continue;
            }

            var key = line[..eqIdx].Trim();
            var val = line[(eqIdx + 1)..].Trim();
            result[key] = val;
        }

        return result;
    }
}
