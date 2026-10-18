using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicConnect.Data.Security;

/// <summary>
/// Contract for hashing and verifying user passwords using Argon2id in PHC string format (NFR-11),
/// and creating persisted user accounts with the default lowest baseline role (<c>reporter</c>).
/// </summary>
public interface IPasswordHashingService
{
    /// <summary>
    /// Hashes a plaintext password using Argon2id with the documented NFR-11 baseline parameters
    /// (<c>v=19</c>, <c>m=19456</c> KiB, <c>t=2</c>, <c>p=1</c>) and returns a PHC-formatted string.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifies a plaintext password against a stored Argon2id PHC string by parsing the algorithm
    /// and parameters directly from the stored PHC string.
    /// </summary>
    bool VerifyPassword(string password, string phcHash);

    /// <summary>
    /// Verifies a plaintext password against a stored Argon2id PHC string and indicates whether
    /// the stored hash uses outdated parameters and should be transparently upgraded.
    /// </summary>
    bool VerifyPassword(string password, string phcHash, out bool needsRehash);

    /// <summary>
    /// Creates and persists a new <see cref="AppUser"/> with an Argon2id PHC password hash and the default
    /// lowest baseline role (<c>reporter</c>), logging account metadata without ever logging the plaintext
    /// password or raw hash.
    /// </summary>
    Task<AppUser> CreateUserAccountAsync(
        CivicConnectDbContext dbContext,
        string email,
        string fullName,
        string plaintextPassword,
        ILogger? auditLogger = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Parsed components of an Argon2id PHC string (<c>$argon2id$v=19$m=...,t=...,p=...$&lt;salt&gt;$&lt;hash&gt;</c>).
/// </summary>
public sealed record Argon2idPhcParameters(
    string Algorithm,
    int Version,
    int MemoryCostKiB,
    int Iterations,
    int Parallelism,
    byte[] Salt,
    byte[] Hash);

/// <summary>
/// Implements NFR-11 password hashing and verification using Argon2id in PHC string format:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;base64-unpadded-salt&gt;$&lt;base64-unpadded-hash&gt;</c>.
/// </summary>
public sealed partial class Argon2idPasswordHasher : IPasswordHashingService
{
    public const string AlgorithmIdentifier = "argon2id";
    public const int Version = 19; // 0x13 (Argon2 v1.3)
    public const int MemoryCostKiB = 19456; // 19 MiB
    public const int Iterations = 2;
    public const int Parallelism = 1;
    public const int SaltLengthBytes = 16;
    public const int HashLengthBytes = 32;

    public const string DefaultLowestRoleName = "reporter";
    public const int MinFullNameLength = 2;
    public const int MaxFullNameLength = 120;
    public const int MinPasswordLength = 8;

    public const string CheckConstraintName = "app_user_password_hash_argon2id_ck";
    public const string CheckConstraintSql =
        "password_hash ~ '^\\$argon2id\\$v=19\\$m=[0-9]+,t=[0-9]+,p=[0-9]+\\$[A-Za-z0-9+/]+\\$[A-Za-z0-9+/]+$'";

    private static readonly EmailAddressAttribute EmailValidator = new();

    /// <summary>
    /// Shared stateless singleton instance for callers without DI.
    /// </summary>
    public static Argon2idPasswordHasher Instance { get; } = new();

    private readonly ILogger<Argon2idPasswordHasher>? _logger;

    public Argon2idPasswordHasher(ILogger<Argon2idPasswordHasher>? logger = null)
    {
        _logger = logger;
    }

    [GeneratedRegex(@"^\$argon2id\$v=19\$m=[0-9]+,t=[0-9]+,p=[0-9]+\$[A-Za-z0-9+/]+\$[A-Za-z0-9+/]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhcFormatRegex();

    /// <summary>
    /// Returns true if the candidate string matches the Argon2id PHC format required by NFR-11.
    /// </summary>
    public static bool IsValidPhcFormat(string? phcHash) =>
        !string.IsNullOrWhiteSpace(phcHash) && PhcFormatRegex().IsMatch(phcHash);

    /// <inheritdoc />
    public string HashPassword(string password) =>
        HashPasswordWithParameters(password, MemoryCostKiB, Iterations, Parallelism);

    /// <summary>
    /// Computes an Argon2id PHC string with explicit parameters. Used by baseline hashing and
    /// parameter-upgrade verification tests.
    /// </summary>
    public string HashPasswordWithParameters(
        string password,
        int memoryCostKiB,
        int iterations,
        int parallelism,
        byte[]? salt = null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be null, empty, or whitespace.", nameof(password));
        }

        if (memoryCostKiB < 8 * parallelism)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryCostKiB), "Memory cost must be at least 8 KiB per lane.");
        }

        if (iterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be at least 1.");
        }

        if (parallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parallelism), "Parallelism must be at least 1.");
        }

        var saltBytes = salt is { Length: > 0 }
            ? (byte[])salt.Clone()
            : RandomNumberGenerator.GetBytes(SaltLengthBytes);

        var hashBytes = ComputeArgon2idBytes(
            password,
            saltBytes,
            memoryCostKiB,
            iterations,
            parallelism,
            HashLengthBytes);

        var saltB64 = EncodeUnpaddedBase64(saltBytes);
        var hashB64 = EncodeUnpaddedBase64(hashBytes);

        _logger?.LogInformation(
            "Computed Argon2id password hash using v={Version}, m={MemoryCostKiB}, t={Iterations}, p={Parallelism}.",
            Version,
            memoryCostKiB,
            iterations,
            parallelism);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${AlgorithmIdentifier}$v={Version}$m={memoryCostKiB},t={iterations},p={parallelism}${saltB64}${hashB64}");
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string phcHash) =>
        VerifyPassword(password, phcHash, out _);

    /// <inheritdoc />
    public bool VerifyPassword(string password, string phcHash, out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(phcHash))
        {
            return false;
        }

        if (!TryParsePhcString(phcHash, out var parsed) || parsed is null)
        {
            _logger?.LogWarning("Rejected password verification because stored hash is not a valid Argon2id PHC string.");
            return false;
        }

        if (!string.Equals(parsed.Algorithm, AlgorithmIdentifier, StringComparison.Ordinal))
        {
            return false;
        }

        var computedBytes = ComputeArgon2idBytes(
            password,
            parsed.Salt,
            parsed.MemoryCostKiB,
            parsed.Iterations,
            parsed.Parallelism,
            parsed.Hash.Length);

        var verified = CryptographicOperations.FixedTimeEquals(computedBytes, parsed.Hash);
        CryptographicOperations.ZeroMemory(computedBytes);

        if (verified)
        {
            needsRehash =
                parsed.Version != Version ||
                parsed.MemoryCostKiB != MemoryCostKiB ||
                parsed.Iterations != Iterations ||
                parsed.Parallelism != Parallelism ||
                parsed.Salt.Length != SaltLengthBytes ||
                parsed.Hash.Length != HashLengthBytes;

            _logger?.LogInformation(
                "Verified Argon2id password hash (v={Version}, m={MemoryCostKiB}, t={Iterations}, p={Parallelism}, needsRehash={NeedsRehash}).",
                parsed.Version,
                parsed.MemoryCostKiB,
                parsed.Iterations,
                parsed.Parallelism,
                needsRehash);
        }

        return verified;
    }

    /// <summary>
    /// Parses a PHC string into its algorithm, version, memory cost, iterations, parallelism, salt, and hash bytes.
    /// </summary>
    public static Argon2idPhcParameters ParsePhcString(string phcHash)
    {
        if (!TryParsePhcString(phcHash, out var parsed) || parsed is null)
        {
            throw new FormatException("The provided string is not a valid Argon2id PHC hash.");
        }

        return parsed;
    }

    /// <summary>
    /// Attempts to parse an Argon2id PHC string without throwing.
    /// </summary>
    public static bool TryParsePhcString(string? phcHash, out Argon2idPhcParameters? parameters)
    {
        parameters = null;

        if (string.IsNullOrWhiteSpace(phcHash))
        {
            return false;
        }

        // Expected format: $argon2id$v=19$m=19456,t=2,p=1$<saltB64>$<hashB64>
        var segments = phcHash.Split('$');
        if (segments.Length != 6 || segments[0].Length != 0)
        {
            return false;
        }

        var algorithm = segments[1];
        if (!string.Equals(algorithm, AlgorithmIdentifier, StringComparison.Ordinal))
        {
            return false;
        }

        var versionSegment = segments[2];
        if (!versionSegment.StartsWith("v=", StringComparison.Ordinal) ||
            !int.TryParse(versionSegment.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version <= 0)
        {
            return false;
        }

        var paramParts = segments[3].Split(',');
        if (paramParts.Length != 3)
        {
            return false;
        }

        if (!paramParts[0].StartsWith("m=", StringComparison.Ordinal) ||
            !int.TryParse(paramParts[0].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var memoryCostKiB) ||
            memoryCostKiB <= 0)
        {
            return false;
        }

        if (!paramParts[1].StartsWith("t=", StringComparison.Ordinal) ||
            !int.TryParse(paramParts[1].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
            iterations <= 0)
        {
            return false;
        }

        if (!paramParts[2].StartsWith("p=", StringComparison.Ordinal) ||
            !int.TryParse(paramParts[2].AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var parallelism) ||
            parallelism <= 0)
        {
            return false;
        }

        if (memoryCostKiB < 8 * parallelism)
        {
            return false;
        }

        if (!TryDecodeUnpaddedBase64(segments[4], out var salt) || salt.Length == 0)
        {
            return false;
        }

        if (!TryDecodeUnpaddedBase64(segments[5], out var hash) || hash.Length == 0)
        {
            return false;
        }

        parameters = new Argon2idPhcParameters(
            algorithm,
            version,
            memoryCostKiB,
            iterations,
            parallelism,
            salt,
            hash);
        return true;
    }

    /// <inheritdoc />
    public async Task<AppUser> CreateUserAccountAsync(
        CivicConnectDbContext dbContext,
        string email,
        string fullName,
        string plaintextPassword,
        ILogger? auditLogger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var normalizedFullName = fullName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            throw new ArgumentException("Full name is required.", nameof(fullName));
        }

        if (normalizedFullName.Length < MinFullNameLength || normalizedFullName.Length > MaxFullNameLength)
        {
            throw new ArgumentException(
                $"Full name must be between {MinFullNameLength} and {MaxFullNameLength} characters.",
                nameof(fullName));
        }

        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException("Email address is required.", nameof(email));
        }

        if (!IsValidEmailAddress(normalizedEmail))
        {
            throw new ArgumentException("Enter a valid email address.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(plaintextPassword))
        {
            throw new ArgumentException("Password is required.", nameof(plaintextPassword));
        }

        if (plaintextPassword.Length < MinPasswordLength)
        {
            throw new ArgumentException(
                $"Password must be at least {MinPasswordLength} characters long.",
                nameof(plaintextPassword));
        }

        var phcHash = HashPassword(plaintextPassword);

        var defaultRoleId = await dbContext.Roles
            .AsNoTracking()
            .Where(r => r.Name == DefaultLowestRoleName)
            .Select(r => (short?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? RoleConfiguration.BaselineRoles.First(r => r.Name == DefaultLowestRoleName).Id;

        var userId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId,
            Email = normalizedEmail,
            FullName = normalizedFullName,
            PasswordHash = phcHash,
            IsActive = true
        };

        user.UserRoles.Add(new UserRole
        {
            UserId = userId,
            User = user,
            RoleId = defaultRoleId,
            GrantedByUserId = userId,
            GrantedByUser = user
        });

        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var targetLogger = auditLogger ?? _logger;
        targetLogger?.LogInformation(
            "Created user account {UserId} for {Email} ({FullName}) with default role '{RoleName}' and {Algorithm} parameters v={Version}, m={MemoryCostKiB}, t={Iterations}, p={Parallelism}.",
            user.Id,
            user.Email,
            user.FullName,
            DefaultLowestRoleName,
            AlgorithmIdentifier,
            Version,
            MemoryCostKiB,
            Iterations,
            Parallelism);

        return user;
    }

    public static bool IsValidEmailAddress(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        if (trimmed.Any(char.IsWhiteSpace) || !EmailValidator.IsValid(trimmed))
        {
            return false;
        }

        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0 || atIndex != trimmed.LastIndexOf('@') || atIndex == trimmed.Length - 1)
        {
            return false;
        }

        var domain = trimmed[(atIndex + 1)..];
        return domain.Contains('.', StringComparison.Ordinal)
            && !domain.StartsWith('.')
            && !domain.EndsWith('.');
    }

    private static byte[] ComputeArgon2idBytes(
        string password,
        byte[] salt,
        int memoryCostKiB,
        int iterations,
        int parallelism,
        int outputLength)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memoryCostKiB,
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };

            return argon2.GetBytes(outputLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static string EncodeUnpaddedBase64(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryDecodeUnpaddedBase64(string unpaddedBase64, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrEmpty(unpaddedBase64) || unpaddedBase64.Contains('=', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var ch in unpaddedBase64)
        {
            var isValidBase64Char =
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '+' ||
                ch == '/';

            if (!isValidBase64Char)
            {
                return false;
            }
        }

        var padded = (unpaddedBase64.Length % 4) switch
        {
            0 => unpaddedBase64,
            2 => unpaddedBase64 + "==",
            3 => unpaddedBase64 + "=",
            _ => null
        };

        if (padded is null)
        {
            return false;
        }

        var buffer = new byte[(padded.Length * 3) / 4];
        if (!Convert.TryFromBase64String(padded, buffer, out var bytesWritten))
        {
            return false;
        }

        bytes = buffer[..bytesWritten];
        return true;
    }
}
