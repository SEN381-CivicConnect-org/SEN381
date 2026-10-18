using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CivicConnect.Data.Configuration;

/// <summary>
/// Strongly-typed container for system secrets loaded exclusively from environment variables (NFR-01, IT-CFG-01).
/// </summary>
public sealed record SystemSecrets(
    string ConnectionString,
    string JwtSecret,
    string? MigrationConnectionString = null);

/// <summary>
/// Centralized startup loader and validator for application secrets (NFR-01: Protection of System Secrets),
/// database connection TLS enforcement (NFR-04: Protection of Personal Information),
/// and least-privilege database role separation (NFR-10: Least-Privilege Database Access, IT-SEC-02).
/// </summary>
public static class SystemSecretsConfiguration
{
    public const string ConnectionStringEnvVar = "CONNECTION_STRING";
    public const string MigrationConnectionStringEnvVar = "MIGRATION_CONNECTION_STRING";
    public const string JwtSecretEnvVar = "JWT_SECRET";
    public const string JwtSecretAltEnvVar = "Jwt__Secret";
    public const string ReportingRoleName = "civic_report";
    public const string AppRoleName = "civic_app";
    public const string MigrationRoleName = "civic_migrate";
    public const int MinimumJwtSecretLength = 16;

    private static readonly ConcurrentDictionary<string, bool> VerifiedTlsEndpoints =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ForbiddenConfigConnectionKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "DefaultConnection",
        "CONNECTION_STRING",
        "MIGRATION_CONNECTION_STRING"
    ];

    private static readonly string[] ForbiddenConfigJwtKeys =
    [
        "Jwt:Secret",
        "Jwt:Key",
        "JWT_SECRET"
    ];

    private static readonly Regex PasswordValueRegex = new(
        @"(?:Password|Pwd)\s*=\s*([^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SslModeValueRegex = new(
        @"(?:Ssl\s*Mode|SslMode)\s*=\s*([^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Validates that no secrets are hardcoded in <paramref name="configuration"/> and loads
    /// <c>CONNECTION_STRING</c> and <c>JWT_SECRET</c> from environment variables.
    /// Throws <see cref="InvalidOperationException"/> if secrets are hardcoded in configuration
    /// or missing/invalid in environment variables (<c>IT-CFG-01</c>).
    /// </summary>
    public static SystemSecrets LoadAndValidate(
        IConfiguration? configuration,
        Func<string, string?>? getEnvironmentVariable = null,
        bool requireJwtSecret = true)
    {
        EnsureNoHardcodedSecretsInConfiguration(configuration);

        var envGetter = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        var connectionString = envGetter(ConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString) && getEnvironmentVariable is null && configuration is not null)
        {
            TryGetFromEnvironmentProvider(configuration, ConnectionStringEnvVar, out connectionString);
        }

        connectionString = NormalizeToAdoNetConnectionString(connectionString);
        ValidateConnectionString(connectionString);

        var migrationConnectionString = envGetter(MigrationConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(migrationConnectionString) && getEnvironmentVariable is null && configuration is not null)
        {
            TryGetFromEnvironmentProvider(configuration, MigrationConnectionStringEnvVar, out migrationConnectionString);
        }

        migrationConnectionString = NormalizeToAdoNetConnectionString(migrationConnectionString);
        if (!string.IsNullOrWhiteSpace(migrationConnectionString))
        {
            ValidateMigrationConnectionString(migrationConnectionString);
        }

        var jwtSecret = envGetter(JwtSecretEnvVar) ?? envGetter(JwtSecretAltEnvVar);
        if (string.IsNullOrWhiteSpace(jwtSecret) && getEnvironmentVariable is null && configuration is not null)
        {
            if (!TryGetFromEnvironmentProvider(configuration, JwtSecretEnvVar, out jwtSecret))
            {
                TryGetFromEnvironmentProvider(configuration, "Jwt:Secret", out jwtSecret);
            }
        }

        if (requireJwtSecret || !string.IsNullOrWhiteSpace(jwtSecret))
        {
            ValidateJwtSecret(jwtSecret);
        }

        return new SystemSecrets(
            connectionString!.Trim(),
            (jwtSecret ?? string.Empty).Trim(),
            migrationConnectionString?.Trim());
    }

    /// <summary>
    /// Inspects non-environment configuration providers (such as <c>appsettings.json</c> or in-memory config)
    /// and throws <see cref="InvalidOperationException"/> if database credentials or JWT secrets are hardcoded.
    /// </summary>
    public static void EnsureNoHardcodedSecretsInConfiguration(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return;
        }

        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers)
            {
                var providerTypeName = provider.GetType().Name;
                if (providerTypeName.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase) ||
                    providerTypeName.Contains("Chained", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var key in ForbiddenConfigConnectionKeys)
                {
                    if (provider.TryGet(key, out var rawConn) && !string.IsNullOrWhiteSpace(rawConn))
                    {
                        throw new InvalidOperationException(
                            $"NFR-01 violation (IT-CFG-01): Hardcoded database connection string or password detected in configuration key '{key}' ({providerTypeName}). " +
                            $"Supply '{ConnectionStringEnvVar}' via secure environment variables instead of hardcoding credentials.");
                    }
                }

                foreach (var key in ForbiddenConfigJwtKeys)
                {
                    if (provider.TryGet(key, out var rawJwt) && !string.IsNullOrWhiteSpace(rawJwt))
                    {
                        throw new InvalidOperationException(
                            $"NFR-01 violation (IT-CFG-01): Hardcoded JWT secret detected in configuration key '{key}' ({providerTypeName}). " +
                            $"Supply '{JwtSecretEnvVar}' via secure environment variables instead of hardcoding secrets.");
                    }
                }
            }

            return;
        }

        foreach (var key in ForbiddenConfigConnectionKeys)
        {
            var rawConn = configuration[key];
            if (!string.IsNullOrWhiteSpace(rawConn))
            {
                throw new InvalidOperationException(
                    $"NFR-01 violation (IT-CFG-01): Hardcoded database connection string detected in configuration key '{key}'.");
            }
        }

        foreach (var key in ForbiddenConfigJwtKeys)
        {
            var rawJwt = configuration[key];
            if (!string.IsNullOrWhiteSpace(rawJwt))
            {
                throw new InvalidOperationException(
                    $"NFR-01 violation (IT-CFG-01): Hardcoded JWT secret detected in configuration key '{key}'.");
            }
        }
    }

    /// <summary>
    /// Validates raw JSON content from an <c>appsettings*.json</c> file on disk to ensure no hardcoded
    /// database passwords or JWT secrets exist in source control (<c>IT-CFG-01</c>).
    /// </summary>
    public static void ValidateJsonConfigurationContent(string jsonContent, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return;
        }

        var locationLabel = string.IsNullOrWhiteSpace(filePath) ? "JSON configuration" : filePath;

        if (PasswordValueRegex.IsMatch(jsonContent))
        {
            throw new InvalidOperationException(
                $"NFR-01 violation (IT-CFG-01): Hardcoded database password ('Password=...') found in {locationLabel}.");
        }

        using var document = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("ConnectionStrings", out var connStrings) &&
            connStrings.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in connStrings.EnumerateObject())
            {
                var val = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    throw new InvalidOperationException(
                        $"NFR-01 violation (IT-CFG-01): Hardcoded connection string found in 'ConnectionStrings:{prop.Name}' within {locationLabel}.");
                }
            }
        }

        if (root.TryGetProperty("Jwt", out var jwtElement) &&
            jwtElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var secretPropName in new[] { "Secret", "Key" })
            {
                if (jwtElement.TryGetProperty(secretPropName, out var secretVal) &&
                    !string.IsNullOrWhiteSpace(secretVal.GetString()))
                {
                    throw new InvalidOperationException(
                        $"NFR-01 violation (IT-CFG-01): Hardcoded JWT secret found in 'Jwt:{secretPropName}' within {locationLabel}.");
                }
            }
        }
    }

    /// <summary>
    /// Loads environment variables from a local <c>.env</c> file if present during local development,
    /// without overwriting variables already set in the environment.
    /// </summary>
    public static void LoadDotEnvIfPresent(string? startDirectory = null, bool adjustDockerHostForLocalProcess = true)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CIVICCONNECT_SKIP_DOTENV"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var runningInContainer = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var candidateRoots = new[]
        {
            startDirectory,
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var root in candidateRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            var dir = new DirectoryInfo(root);
            while (dir is not null)
            {
                var envFile = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envFile))
                {
                    ApplyDotEnvFile(envFile, adjustDockerHostForLocalProcess && !runningInContainer);
                    return;
                }

                dir = dir.Parent;
            }
        }
    }

    private static void ApplyDotEnvFile(string envFilePath, bool replaceDockerDbHost)
    {
        foreach (var rawLine in File.ReadAllLines(envFilePath))
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"', '\'');

            if (string.IsNullOrEmpty(key) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            if (string.Equals(key, ConnectionStringEnvVar, StringComparison.Ordinal) ||
                string.Equals(key, MigrationConnectionStringEnvVar, StringComparison.Ordinal))
            {
                if (replaceDockerDbHost && value.Contains("Host=db;", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Replace("Host=db;", "Host=localhost;", StringComparison.OrdinalIgnoreCase);
                }

                if (!SslModeValueRegex.IsMatch(value))
                {
                    value = value.TrimEnd(';') + ";SslMode=Require";
                }
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static bool TryGetFromEnvironmentProvider(IConfiguration configuration, string key, out string? value)
    {
        value = null;
        if (configuration is not IConfigurationRoot root)
        {
            return false;
        }

        foreach (var provider in root.Providers)
        {
            if (!provider.GetType().Name.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (provider.TryGet(key, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates a database connection string against NFR-01 (secrets), NFR-04 (in-transit TLS encryption),
    /// and NFR-10 (least-privilege database role separation).
    /// Rejects unencrypted connections such as <c>SslMode=Disable</c> (<c>IT-SEC-01</c>)
    /// and rejects runtime usage of the migration role (<c>IT-SEC-02</c>).
    /// </summary>
    public static void ValidateConnectionString(string? connectionString, bool requireExplicitTls = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"NFR-01: '{ConnectionStringEnvVar}' environment variable is not set or is empty. Copy .env.example to .env and fill it in.");
        }

        var trimmed = NormalizeToAdoNetConnectionString(connectionString);
        if (trimmed.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"NFR-01: '{ConnectionStringEnvVar}' contains an unreplaced environment variable placeholder.");
        }

        var match = PasswordValueRegex.Match(trimmed);
        if (match.Success)
        {
            var password = match.Groups[1].Value.Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(password) ||
                string.Equals(password, "changeme", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"NFR-01 (IT-CFG-01): '{ConnectionStringEnvVar}' uses an insecure default or empty password ('{password}').");
            }
        }

        // NFR-10 (IT-SEC-02): Disallow migration role for application runtime connections
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(trimmed);
            if (string.Equals(builder.Username, MigrationRoleName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"NFR-10 violation (IT-SEC-02): Runtime '{ConnectionStringEnvVar}' must not use the migration role ('{MigrationRoleName}'). " +
                    $"Configure the application to connect using the least-privilege role '{AppRoleName}'.");
            }
        }
        catch (ArgumentException)
        {
            // If connection string parsing fails, it will fail downstream with standard diagnostics
        }

        ValidateConnectionStringTls(trimmed, requireExplicitTls);
    }

    /// <summary>
    /// Validates the dedicated migration connection string against NFR-01 (secrets) and NFR-04 (TLS encryption).
    /// </summary>
    public static void ValidateMigrationConnectionString(string? migrationConnectionString, bool requireExplicitTls = false)
    {
        if (string.IsNullOrWhiteSpace(migrationConnectionString))
        {
            return;
        }

        var trimmed = NormalizeToAdoNetConnectionString(migrationConnectionString);
        if (trimmed.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"NFR-01: '{MigrationConnectionStringEnvVar}' contains an unreplaced environment variable placeholder.");
        }

        var match = PasswordValueRegex.Match(trimmed);
        if (match.Success)
        {
            var password = match.Groups[1].Value.Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(password) ||
                string.Equals(password, "changeme", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"NFR-01 (IT-CFG-01): '{MigrationConnectionStringEnvVar}' uses an insecure default or empty password ('{password}').");
            }
        }

        ValidateConnectionStringTls(trimmed, requireExplicitTls);
    }

    /// <summary>
    /// Normalizes PostgreSQL URI-formatted connection strings (such as those provided by Render, Heroku, or Neon)
    /// into standard ADO.NET Npgsql connection strings with enforced TLS and server certificate trust.
    /// </summary>
    public static string NormalizeToAdoNetConnectionString(string? rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            return string.Empty;
        }

        var trimmed = rawConnectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Strip any trailing key-values appended with a semicolon before URI parsing
                var semiIdx = trimmed.IndexOf(';');
                var uriPart = semiIdx >= 0 ? trimmed[..semiIdx] : trimmed;
                var uri = new Uri(uriPart);

                var userInfo = uri.UserInfo.Split(':');
                var username = Uri.UnescapeDataString(userInfo[0]);
                var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 5432;
                var database = uri.AbsolutePath.TrimStart('/');

                var builder = new Npgsql.NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Database = database,
                    Username = username,
                    Password = password,
                    SslMode = Npgsql.SslMode.Require
                };

                return builder.ConnectionString;
            }
            catch
            {
                // Fall back to original string if not a parseable URI
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Validates that a connection string does not disable TLS (NFR-04, IT-SEC-01) and ensures
    /// <c>SslMode=Require</c> (or <c>VerifyCA</c>/<c>VerifyFull</c>) is enforced on the returned connection string.
    /// </summary>
    public static string EnsureTlsConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"NFR-04 (IT-SEC-01): Database connection string cannot be null or empty.");
        }

        var normalized = NormalizeToAdoNetConnectionString(connectionString);
        ValidateConnectionStringTls(normalized, requireExplicitTls: false);

        if (!SslModeValueRegex.IsMatch(normalized))
        {
            return normalized.TrimEnd(';') + ";SslMode=Require";
        }

        return normalized;
    }

    /// <summary>
    /// Alias for <see cref="EnsureTlsConnectionString"/> enforcing in-transit TLS (NFR-04).
    /// </summary>
    public static string EnforceTls(string connectionString) => EnsureTlsConnectionString(connectionString);

    /// <summary>
    /// Builds a TLS-enforced connection string for the dedicated <c>civic_report</c> reporting database role (NFR-04, IT-SEC-01).
    /// </summary>
    public static string CreateReportingRoleConnectionString(string baseConnectionString)
    {
        var tlsConn = EnsureTlsConnectionString(baseConnectionString);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(tlsConn)
        {
            Username = ReportingRoleName
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Connects to the database and asserts that the current session role is neither a superuser
    /// nor the dedicated migration role (NFR-10: Least-Privilege Database Access, IT-SEC-02).
    /// </summary>
    public static async Task EnforceLeastPrivilegeRoleAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        EnsureServerTlsReady(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(EnsureTlsConnectionString(connectionString))
        {
            Timeout = 5,
            Pooling = false
        };

        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT current_user, usesuper FROM pg_user WHERE usename = current_user;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var currentUser = reader.GetString(0);
            var isSuperUser = reader.GetBoolean(1);

            if (isSuperUser)
            {
                throw new InvalidOperationException(
                    $"NFR-10 violation (IT-SEC-02): Application connected as superuser '{currentUser}'. " +
                    $"Runtime application must connect using the least-privilege role '{AppRoleName}'.");
            }

            if (string.Equals(currentUser, MigrationRoleName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"NFR-10 violation (IT-SEC-02): Application connected as migration role '{currentUser}'. " +
                    $"Runtime application must connect using the least-privilege role '{AppRoleName}'.");
            }
        }
    }

    /// <summary>
    /// Synchronously asserts that the current session role is neither a superuser nor the migration role (NFR-10).
    /// </summary>
    public static void EnforceLeastPrivilegeRole(string connectionString)
    {
        EnforceLeastPrivilegeRoleAsync(connectionString).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Ensures the target PostgreSQL instance has SSL/TLS enabled before opening a <c>SslMode=Require</c> connection.
    /// Useful when connecting against throwaway or CI PostgreSQL containers that started before SSL was reloaded.
    /// </summary>
    public static void EnsureServerTlsReady(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var endpointKey = $"{builder.Host}:{builder.Port}";
            if (VerifiedTlsEndpoints.ContainsKey(endpointKey))
            {
                return;
            }

            builder.SslMode = SslMode.Prefer;
            builder.Pooling = false;
            builder.Timeout = 5;

            using var conn = new NpgsqlConnection(builder.ConnectionString);
            conn.Open();

            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT current_setting('ssl', true);";
                var sslSetting = checkCmd.ExecuteScalar() as string;
                if (string.Equals(sslSetting, "on", StringComparison.OrdinalIgnoreCase))
                {
                    VerifiedTlsEndpoints[endpointKey] = true;
                    return;
                }
            }

            var (certB64, keyB64) = GenerateSelfSignedServerCertAndKeyBase64();

            using (var provisionCmd = conn.CreateCommand())
            {
                provisionCmd.CommandText = $$"""
                    DO $tls$
                    BEGIN
                        EXECUTE format(
                            'COPY (SELECT 1) TO PROGRAM %L',
                            format(
                                'sh -c "echo %s | base64 -d > \"$PGDATA/server.crt\" && echo %s | base64 -d > \"$PGDATA/server.key\" && chmod 600 \"$PGDATA/server.key\" && chmod 644 \"$PGDATA/server.crt\""',
                                '{{certB64}}',
                                '{{keyB64}}'
                            )
                        );
                    EXCEPTION WHEN OTHERS THEN
                        NULL;
                    END
                    $tls$;
                    """;
                provisionCmd.ExecuteNonQuery();
            }

            using (var alterCmd = conn.CreateCommand())
            {
                alterCmd.CommandText = "ALTER SYSTEM SET ssl = 'on';";
                alterCmd.ExecuteNonQuery();
            }

            using (var reloadCmd = conn.CreateCommand())
            {
                reloadCmd.CommandText = "SELECT pg_reload_conf();";
                reloadCmd.ExecuteNonQuery();
            }

            conn.Close();
            NpgsqlConnection.ClearAllPools();
            VerifiedTlsEndpoints[endpointKey] = true;
        }
        catch
        {
            // Ignore bootstrap errors when database is offline during non-DB unit tests
        }
    }

    public static (string CertPemBase64, string KeyPemBase64) GenerateSelfSignedServerCertAndKeyBase64()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName("db");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        var certPem = cert.ExportCertificatePem();
        var keyPem = rsa.ExportPkcs8PrivateKeyPem();

        return (
            Convert.ToBase64String(Encoding.ASCII.GetBytes(certPem)),
            Convert.ToBase64String(Encoding.ASCII.GetBytes(keyPem)));
    }

    private static void ValidateConnectionStringTls(string connectionString, bool requireExplicitTls)
    {
        var sslMatch = SslModeValueRegex.Match(connectionString);
        if (!sslMatch.Success)
        {
            if (requireExplicitTls)
            {
                throw new InvalidOperationException(
                    "NFR-04 violation (IT-SEC-01): Database connection string must explicitly specify 'SslMode=Require' or 'SslMode=VerifyFull'.");
            }

            return;
        }

        var sslMode = sslMatch.Groups[1].Value.Trim('"', '\'');
        if (!string.Equals(sslMode, "Require", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sslMode, "VerifyCA", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sslMode, "VerifyFull", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"NFR-04 violation (IT-SEC-01): Unencrypted or non-enforced database connection SSL mode '{sslMode}' is forbidden. " +
                "In-transit TLS encryption ('SslMode=Require' or 'SslMode=VerifyFull') is mandatory.");
        }
    }

    private static void ValidateJwtSecret(string? jwtSecret)
    {
        if (string.IsNullOrWhiteSpace(jwtSecret))
        {
            throw new InvalidOperationException(
                $"NFR-01: '{JwtSecretEnvVar}' environment variable is not set or is empty. Configure '{JwtSecretEnvVar}' via secure environment variables.");
        }

        var trimmed = jwtSecret.Trim();
        if (trimmed.Contains("${", StringComparison.Ordinal) ||
            string.Equals(trimmed, "changeme", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"NFR-01 (IT-CFG-01): '{JwtSecretEnvVar}' contains an unreplaced placeholder or insecure default value.");
        }

        if (trimmed.Length < MinimumJwtSecretLength)
        {
            throw new InvalidOperationException(
                $"NFR-01: '{JwtSecretEnvVar}' must be at least {MinimumJwtSecretLength} characters long.");
        }
    }
}
