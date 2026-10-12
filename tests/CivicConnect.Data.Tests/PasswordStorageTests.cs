using CivicConnect.Data.Entities;
using CivicConnect.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicConnect.Data.Tests;

/// <summary>
/// Integration and unit verification tests for NFR-11 — Password Storage (IT-SEC-03 StoredHashIsArgon2idWithParams).
/// </summary>
[Collection("Database")]
public sealed class PasswordStorageTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public PasswordStorageTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUserAccount_StoresArgon2idPhcStringWithDocumentedParameters_AndNeverStoresOrLogsPlaintext()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var logSink = new List<string>();
        var hasherLogger = new CapturingLogger<Argon2idPasswordHasher>(logSink);
        var hasher = new Argon2idPasswordHasher(hasherLogger);

        const string plaintextPassword = "C1v!cC0nn3ct#PlaintextSecret_9842";
        var email = $"nfr11.user.{Guid.NewGuid():N}@example.com";
        const string fullName = "NFR-11 Verification User";

        var createdUser = await hasher.CreateUserAccountAsync(
            context,
            email,
            fullName,
            plaintextPassword,
            hasherLogger);

        Assert.NotEqual(Guid.Empty, createdUser.Id);

        // Reload directly from PostgreSQL without tracking
        var reloaded = await context.AppUsers
            .AsNoTracking()
            .SingleAsync(u => u.Id == createdUser.Id);

        // 1. Stored password_hash is a PHC-format string with v=19, m=19456, t=2, p=1
        Assert.Matches(
            @"^\$argon2id\$v=19\$m=19456,t=2,p=1\$[A-Za-z0-9+/]+\$[A-Za-z0-9+/]+$",
            reloaded.PasswordHash);

        var parsed = Argon2idPasswordHasher.ParsePhcString(reloaded.PasswordHash);
        Assert.Equal("argon2id", parsed.Algorithm);
        Assert.Equal(19, parsed.Version);
        Assert.Equal(19456, parsed.MemoryCostKiB);
        Assert.Equal(2, parsed.Iterations);
        Assert.Equal(1, parsed.Parallelism);
        Assert.Equal(16, parsed.Salt.Length);
        Assert.Equal(32, parsed.Hash.Length);

        // 2. Verification succeeds for the correct password using parameters parsed from the PHC string
        Assert.True(hasher.VerifyPassword(plaintextPassword, reloaded.PasswordHash, out var needsRehash));
        Assert.False(needsRehash);
        Assert.False(hasher.VerifyPassword("WrongPassword!123", reloaded.PasswordHash));

        // 3. Plaintext password appears nowhere in the database row or in any application log
        Assert.DoesNotContain(plaintextPassword, reloaded.PasswordHash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(plaintextPassword, reloaded.Email, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(plaintextPassword, reloaded.FullName, StringComparison.OrdinalIgnoreCase);

        Assert.NotEmpty(logSink);
        Assert.All(logSink, message =>
        {
            Assert.DoesNotContain(plaintextPassword, message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(reloaded.PasswordHash, message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public Task IT_SEC_03_StoredHashIsArgon2idWithParams() =>
        CreateUserAccount_StoresArgon2idPhcStringWithDocumentedParameters_AndNeverStoresOrLogsPlaintext();

    [Theory]
    [InlineData("PlaintextPassword123!")]
    [InlineData("hashed-password")]
    [InlineData("AQAAAAIAAYagAAAAELegacyPbkdf2HashValue==")]
    [InlineData("$2a$12$R9h/cIPz0gi.URNNX3kh2OPST9/PgBkqquzi.Ss7KIUgO2t0jWMUW")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    public void VerifyPassword_RejectsPlaintextOrNonArgon2idPasswordHash(string invalidPasswordHash)
    {
        var hasher = Argon2idPasswordHasher.Instance;

        Assert.False(Argon2idPasswordHasher.IsValidPhcFormat(invalidPasswordHash));
        Assert.False(Argon2idPasswordHasher.TryParsePhcString(invalidPasswordHash, out _));
        Assert.False(hasher.VerifyPassword("PlaintextPassword123!", invalidPasswordHash, out var needsRehash));
        Assert.False(needsRehash);
    }

    [Fact]
    public async Task VerifyPassword_DetectsOutdatedParametersForSeamlessUpgrade()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var hasher = Argon2idPasswordHasher.Instance;
        const string plaintextPassword = "UpgradableUser#2026!";

        // Simulate an older Argon2id PHC hash stored with lower parameters (m=4096, t=1, p=1)
        var legacyPhcHash = hasher.HashPasswordWithParameters(
            plaintextPassword,
            memoryCostKiB: 4096,
            iterations: 1,
            parallelism: 1);

        Assert.StartsWith("$argon2id$v=19$m=4096,t=1,p=1$", legacyPhcHash, StringComparison.Ordinal);

        var user = new AppUser
        {
            Email = $"upgrade.user.{Guid.NewGuid():N}@example.com",
            FullName = "Parameter Upgrade User",
            PasswordHash = legacyPhcHash,
            IsActive = true
        };

        context.AppUsers.Add(user);
        await context.SaveChangesAsync();

        // Verification parses m=4096,t=1,p=1 from the stored PHC string, succeeds, and signals needsRehash = true
        var verifiedLegacy = hasher.VerifyPassword(plaintextPassword, user.PasswordHash, out var needsRehash);
        Assert.True(verifiedLegacy);
        Assert.True(needsRehash);

        // Seamless re-hash on login upgrades the stored PHC string to m=19456,t=2,p=1 without a password reset
        user.PasswordHash = hasher.HashPassword(plaintextPassword);
        await context.SaveChangesAsync();

        var reloaded = await context.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", reloaded.PasswordHash, StringComparison.Ordinal);

        var verifiedUpgraded = hasher.VerifyPassword(plaintextPassword, reloaded.PasswordHash, out var needsRehashAfterUpgrade);
        Assert.True(verifiedUpgraded);
        Assert.False(needsRehashAfterUpgrade);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _sink;

        public CapturingLogger(List<string> sink) => _sink = sink;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Add(formatter(state, exception));
        }
    }
}
