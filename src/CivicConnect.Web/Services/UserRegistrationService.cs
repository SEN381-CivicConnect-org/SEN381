using System.ComponentModel.DataAnnotations;
using CivicConnect.Data;
using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using CivicConnect.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicConnect.Web.Services;

/// <summary>
/// Form model for the <c>/signup</c> registration flow with standard DataAnnotations validation.
/// </summary>
public sealed class SignUpFormModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 120 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of a user registration attempt.
/// </summary>
public sealed record UserRegistrationResult(
    bool Succeeded,
    Guid? UserId = null,
    string? AssignedRole = null,
    string? ErrorMessage = null,
    bool IsDuplicateEmail = false)
{
    public static UserRegistrationResult Success(Guid userId, string assignedRole) =>
        new(true, userId, assignedRole);

    public static UserRegistrationResult DuplicateEmail(
        string message = "An account with this email address already exists.") =>
        new(false, ErrorMessage: message, IsDuplicateEmail: true);

    public static UserRegistrationResult ValidationFailed(string message) =>
        new(false, ErrorMessage: message);

    public static UserRegistrationResult Failed(
        string message = "Unable to create your account right now. Please try again.") =>
        new(false, ErrorMessage: message);
}

/// <summary>
/// Contract for registering new CivicConnect user accounts with Argon2id password hashing and
/// automatic assignment of the lowest-privilege baseline role (<c>reporter</c>).
/// </summary>
public interface IUserRegistrationService
{
    Task<UserRegistrationResult> RegisterAsync(
        SignUpFormModel model,
        CancellationToken cancellationToken = default);

    Task<UserRegistrationResult> RegisterAsync(
        string fullName,
        string email,
        string password,
        string confirmPassword,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persists new user registrations to PostgreSQL inside a database transaction, applying Argon2id
/// PHC password hashing (NFR-11) and automatically granting the lowest baseline role (<c>reporter</c>)
/// before saving the record.
/// </summary>
public sealed class UserRegistrationService : IUserRegistrationService
{
    public const string DefaultLowestRoleName = "reporter";
    public const int MaxFullNameLength = 120;
    public const int MinPasswordLength = 8;

    private static readonly EmailAddressAttribute EmailValidator = new();

    private readonly CivicConnectDbContext? _dbContext;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ILogger<UserRegistrationService>? _logger;

    public UserRegistrationService(
        IServiceScopeFactory scopeFactory,
        IPasswordHashingService? passwordHasher = null,
        ILogger<UserRegistrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _passwordHasher = passwordHasher ?? Argon2idPasswordHasher.Instance;
        _logger = logger;
    }

    public UserRegistrationService(
        CivicConnectDbContext dbContext,
        IPasswordHashingService? passwordHasher = null,
        ILogger<UserRegistrationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
        _passwordHasher = passwordHasher ?? Argon2idPasswordHasher.Instance;
        _logger = logger;
    }

    public Task<UserRegistrationResult> RegisterAsync(
        SignUpFormModel model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return RegisterAsync(
            model.FullName,
            model.Email,
            model.Password,
            model.ConfirmPassword,
            cancellationToken);
    }

    public async Task<UserRegistrationResult> RegisterAsync(
        string fullName,
        string email,
        string password,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        var normalizedFullName = fullName?.Trim() ?? string.Empty;
        var normalizedEmail = email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            return UserRegistrationResult.ValidationFailed("Full name is required.");
        }

        if (normalizedFullName.Length < 2 || normalizedFullName.Length > MaxFullNameLength)
        {
            return UserRegistrationResult.ValidationFailed("Full name must be between 2 and 120 characters.");
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail) || !EmailValidator.IsValid(normalizedEmail))
        {
            return UserRegistrationResult.ValidationFailed("Enter a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return UserRegistrationResult.ValidationFailed("Password is required.");
        }

        if (password.Length < MinPasswordLength)
        {
            return UserRegistrationResult.ValidationFailed($"Password must be at least {MinPasswordLength} characters long.");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return UserRegistrationResult.ValidationFailed("Passwords do not match.");
        }

        if (_scopeFactory is not null)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var scopedContext = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
            return await ExecuteRegistrationTransactionAsync(
                scopedContext,
                normalizedFullName,
                normalizedEmail,
                password,
                cancellationToken);
        }

        return await ExecuteRegistrationTransactionAsync(
            _dbContext!,
            normalizedFullName,
            normalizedEmail,
            password,
            cancellationToken);
    }

    private async Task<UserRegistrationResult> ExecuteRegistrationTransactionAsync(
        CivicConnectDbContext dbContext,
        string normalizedFullName,
        string normalizedEmail,
        string plaintextPassword,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var emailAlreadyExists = await dbContext.AppUsers
                .AsNoTracking()
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail.ToLower(), cancellationToken);

            if (emailAlreadyExists)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return UserRegistrationResult.DuplicateEmail();
            }

            // Look up the lowest authorization role ("reporter") from the baseline role table
            var reporterRoleId = await dbContext.Roles
                .AsNoTracking()
                .Where(r => r.Name == DefaultLowestRoleName)
                .Select(r => (short?)r.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? RoleConfiguration.BaselineRoles.First(r => r.Name == DefaultLowestRoleName).Id;

            // Hash password using Argon2id PHC string format (NFR-11: v=19, m=19456, t=2, p=1)
            var phcPasswordHash = _passwordHasher.HashPassword(plaintextPassword);

            var userId = Guid.NewGuid();
            var newUser = new AppUser
            {
                Id = userId,
                Email = normalizedEmail,
                FullName = normalizedFullName,
                PasswordHash = phcPasswordHash,
                IsActive = true
            };

            // Assign the lowest possible authorization role ("reporter") BEFORE saving to PostgreSQL
            newUser.UserRoles.Add(new UserRole
            {
                UserId = userId,
                User = newUser,
                RoleId = reporterRoleId,
                GrantedByUserId = userId,
                GrantedByUser = newUser
            });

            dbContext.AppUsers.Add(newUser);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger?.LogInformation(
                "Registered new user account {UserId} with default role '{RoleName}'.",
                newUser.Id,
                DefaultLowestRoleName);

            return UserRegistrationResult.Success(newUser.Id, DefaultLowestRoleName);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            return UserRegistrationResult.DuplicateEmail();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            return UserRegistrationResult.DuplicateEmail();
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
            _logger?.LogError(ex, "Unexpected error while registering a new user account.");
            return UserRegistrationResult.Failed();
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
