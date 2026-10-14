using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace CivicConnect.Data.Configuration;

/// <summary>
/// Strongly-typed container for system secrets loaded exclusively from environment variables (NFR-01, IT-CFG-01).
/// </summary>
public sealed record SystemSecrets(string ConnectionString, string JwtSecret);

/// <summary>
/// Centralized startup loader and validator for application secrets (NFR-01: Protection of System Secrets).
/// Enforces RTM test <c>IT-CFG-01 StartupFailsIfSecretsHardcoded</c> by rejecting secrets hardcoded in
/// static configuration files (<c>appsettings*.json</c>) and requiring <c>CONNECTION_STRING</c> and
/// <c>JWT_SECRET</c> to be supplied via environment variables.
/// </summary>
public static class SystemSecretsConfiguration
{
    public const string ConnectionStringEnvVar = "CONNECTION_STRING";
    public const string JwtSecretEnvVar = "JWT_SECRET";
    public const string JwtSecretAltEnvVar = "Jwt__Secret";
    public const int MinimumJwtSecretLength = 16;

    private static readonly string[] ForbiddenConfigConnectionKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "DefaultConnection",
        "CONNECTION_STRING"
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

        ValidateConnectionString(connectionString);

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
            (jwtSecret ?? string.Empty).Trim());
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

            if (replaceDockerDbHost &&
                string.Equals(key, ConnectionStringEnvVar, StringComparison.Ordinal) &&
                value.Contains("Host=db;", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Replace("Host=db;", "Host=localhost;", StringComparison.OrdinalIgnoreCase);
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

    private static void ValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"NFR-01: '{ConnectionStringEnvVar}' environment variable is not set or is empty. Copy .env.example to .env and fill it in.");
        }

        var trimmed = connectionString.Trim();
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
