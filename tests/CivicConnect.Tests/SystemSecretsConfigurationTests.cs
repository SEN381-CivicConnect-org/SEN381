using System.Text;
using CivicConnect.Data.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CivicConnect.Tests;

/// <summary>
/// Automated verification for NFR-01 (Protection of System Secrets) and
/// RTM test ID <c>IT-CFG-01 StartupFailsIfSecretsHardcoded</c>.
/// </summary>
public sealed class SystemSecretsConfigurationTests
{
    private const string ValidConnectionString =
        "Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=env_supplied_secure_db_pass_2026";

    private const string ValidJwtSecret =
        "env_supplied_jwt_signing_secret_key_min_32_bytes_long_2026";

    [Fact]
    public void Startup_LoadsConnectionStringAndJwtSecretFromEnvironmentVariables()
    {
        // Given a clean appsettings.json with empty placeholders and secrets provided via environment variables
        const string cleanAppSettingsJson = """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information"
                }
              },
              "ConnectionStrings": {
                "DefaultConnection": ""
              },
              "Jwt": {
                "Secret": ""
              }
            }
            """;

        using var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(cleanAppSettingsJson));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();

        var envVars = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SystemSecretsConfiguration.ConnectionStringEnvVar] = ValidConnectionString,
            [SystemSecretsConfiguration.JwtSecretEnvVar] = ValidJwtSecret
        };

        // When SystemSecretsConfiguration.LoadAndValidate runs at startup
        var secrets = SystemSecretsConfiguration.LoadAndValidate(
            configuration,
            key => envVars.GetValueOrDefault(key));

        // Then both secrets are loaded from the environment variables
        Assert.Equal(ValidConnectionString, secrets.ConnectionString);
        Assert.Equal(ValidJwtSecret, secrets.JwtSecret);
    }

    [Theory]
    [InlineData(null, ValidJwtSecret)]
    [InlineData("", ValidJwtSecret)]
    [InlineData("   ", ValidJwtSecret)]
    [InlineData("Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=changeme", ValidJwtSecret)]
    [InlineData("Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=${POSTGRES_PASSWORD}", ValidJwtSecret)]
    [InlineData(ValidConnectionString, null)]
    [InlineData(ValidConnectionString, "")]
    [InlineData(ValidConnectionString, "   ")]
    [InlineData(ValidConnectionString, "too_short")]
    [InlineData(ValidConnectionString, "changeme")]
    [InlineData(ValidConnectionString, "${JWT_SECRET}")]
    public void Startup_FailsWhenConnectionStringOrJwtSecretMissingFromEnvironment(
        string? connectionStringEnv,
        string? jwtSecretEnv)
    {
        var configuration = new ConfigurationBuilder().Build();

        var envVars = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SystemSecretsConfiguration.ConnectionStringEnvVar] = connectionStringEnv,
            [SystemSecretsConfiguration.JwtSecretEnvVar] = jwtSecretEnv
        };

        Assert.Throws<InvalidOperationException>(() =>
            SystemSecretsConfiguration.LoadAndValidate(
                configuration,
                key => envVars.GetValueOrDefault(key)));
    }

    [Theory]
    [InlineData(
        """
        {
          "ConnectionStrings": {
            "DefaultConnection": "Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=changeme"
          }
        }
        """,
        "Hardcoded DefaultConnection in appsettings.json")]
    [InlineData(
        """
        {
          "ConnectionStrings": {
            "DefaultConnection": "Host=localhost;Port=5432;Database=civicconnect;Username=civicconnect;Password=hardcoded_secret_pass"
          }
        }
        """,
        "Hardcoded custom password in appsettings.json")]
    [InlineData(
        """
        {
          "Jwt": {
            "Secret": "hardcoded_jwt_secret_in_appsettings_json_file_12345"
          }
        }
        """,
        "Hardcoded Jwt:Secret in appsettings.json")]
    [InlineData(
        """
        {
          "JWT_SECRET": "hardcoded_top_level_jwt_secret_in_appsettings_json"
        }
        """,
        "Hardcoded top-level JWT_SECRET in appsettings.json")]
    public void Startup_FailsIfSecretsHardcodedInAppsettingsJson(string hardcodedJson, string scenario)
    {
        using var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(hardcodedJson));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();

        var envVars = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SystemSecretsConfiguration.ConnectionStringEnvVar] = ValidConnectionString,
            [SystemSecretsConfiguration.JwtSecretEnvVar] = ValidJwtSecret
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SystemSecretsConfiguration.LoadAndValidate(
                configuration,
                key => envVars.GetValueOrDefault(key)));

        Assert.True(
            ex.Message.Contains("NFR-01", StringComparison.OrdinalIgnoreCase) &&
            ex.Message.Contains("IT-CFG-01", StringComparison.OrdinalIgnoreCase),
            $"Expected NFR-01 / IT-CFG-01 violation message for scenario '{scenario}', but got: {ex.Message}");
    }

    [Fact]
    public void RepositoryConfigFiles_DoNotContainHardcodedCredentials()
    {
        var repoRoot = LocateRepositoryRoot();
        var apiAppSettings = Path.Combine(repoRoot, "src", "CivicConnect.Api", "appsettings.json");
        var apiDevAppSettings = Path.Combine(repoRoot, "src", "CivicConnect.Api", "appsettings.Development.json");

        Assert.True(File.Exists(apiAppSettings), $"Expected '{apiAppSettings}' to exist.");
        Assert.True(File.Exists(apiDevAppSettings), $"Expected '{apiDevAppSettings}' to exist.");

        var configFiles = Directory.GetFiles(
            Path.Combine(repoRoot, "src"),
            "appsettings*.json",
            SearchOption.AllDirectories);

        Assert.NotEmpty(configFiles);

        foreach (var file in configFiles)
        {
            var rawJson = File.ReadAllText(file);

            // Validate raw JSON file has no Password=... or non-empty ConnectionStrings / Jwt:Secret
            SystemSecretsConfiguration.ValidateJsonConfigurationContent(rawJson, file);

            // Also build IConfiguration from the file and verify LoadAndValidate succeeds when env vars are supplied
            var fileConfiguration = new ConfigurationBuilder()
                .AddJsonFile(file, optional: false, reloadOnChange: false)
                .Build();

            var secrets = SystemSecretsConfiguration.LoadAndValidate(
                fileConfiguration,
                key => key switch
                {
                    SystemSecretsConfiguration.ConnectionStringEnvVar => ValidConnectionString,
                    SystemSecretsConfiguration.JwtSecretEnvVar => ValidJwtSecret,
                    _ => null
                });

            Assert.Equal(ValidConnectionString, secrets.ConnectionString);
            Assert.Equal(ValidJwtSecret, secrets.JwtSecret);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CivicConnect.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "GEMINI.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CivicConnect repository root.");
    }
}
