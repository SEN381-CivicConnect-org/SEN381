using System.ComponentModel.DataAnnotations;
using CivicConnect.Data;
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
    [StringLength(
        UserRegistrationService.MaxFullNameLength,
        MinimumLength = UserRegistrationService.MinFullNameLength,
        ErrorMessage = "Full name must be between 2 and 120 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(UserRegistrationService.MinPasswordLength, ErrorMessage = "Password must be at least 8 characters long.")]
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
/// Validates registration inputs and coordinates transactional user creation by delegating
/// account persistence and Argon2id PHC password hashing (NFR-11) to <see cref="IPasswordHashingService.CreateUserAccountAsync"/>.
/// </summary>
public sealed class UserRegistrationService : IUserRegistrationService
{
    public const string DefaultLowestRoleName = Argon2idPasswordHasher.DefaultLowestRoleName;
    public const int MinFullNameLength = Argon2idPasswordHasher.MinFullNameLength;
    public const int MaxFullNameLength = Argon2idPasswordHasher.MaxFullNameLength;
    public const int MinPasswordLength = Argon2idPasswordHasher.MinPasswordLength;

    private readonly CivicConnectDbContext? _dbContext;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ILogger<UserRegistrationService>? _logger;

    private UserRegistrationService(
        IPasswordHashingService? passwordHasher,
        ILogger<UserRegistrationService>? logger)
    {
        _passwordHasher = passwordHasher ?? Argon2idPasswordHasher.Instance;
        _logger = logger;
    }

    public UserRegistrationService(
        IServiceScopeFactory scopeFactory,
        IPasswordHashingService? passwordHasher = null,
        ILogger<UserRegistrationService>? logger = null)
        : this(passwordHasher, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    public UserRegistrationService(
        CivicConnectDbContext dbContext,
        IPasswordHashingService? passwordHasher = null,
        ILogger<UserRegistrationService>? logger = null)
        : this(passwordHasher, logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
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

        var validationFailure = ValidateRegistrationInput(
            normalizedFullName,
            normalizedEmail,
            password,
            confirmPassword);

        if (validationFailure is not null)
        {
            return validationFailure;
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

    private static UserRegistrationResult? ValidateRegistrationInput(
        string normalizedFullName,
        string normalizedEmail,
        string? password,
        string? confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(normalizedFullName))
        {
            return UserRegistrationResult.ValidationFailed("Full name is required.");
        }

        if (normalizedFullName.Length < MinFullNameLength || normalizedFullName.Length > MaxFullNameLength)
        {
            return UserRegistrationResult.ValidationFailed(
                $"Full name must be between {MinFullNameLength} and {MaxFullNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return UserRegistrationResult.ValidationFailed("Email address is required.");
        }

        if (!Argon2idPasswordHasher.IsValidEmailAddress(normalizedEmail))
        {
            return UserRegistrationResult.ValidationFailed("Enter a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return UserRegistrationResult.ValidationFailed("Password is required.");
        }

        if (password.Length < MinPasswordLength)
        {
            return UserRegistrationResult.ValidationFailed(
                $"Password must be at least {MinPasswordLength} characters long.");
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            return UserRegistrationResult.ValidationFailed("Please confirm your password.");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            return UserRegistrationResult.ValidationFailed("Passwords do not match.");
        }

        return null;
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

            var newUser = await _passwordHasher.CreateUserAccountAsync(
                dbContext,
                normalizedEmail,
                normalizedFullName,
                plaintextPassword,
                _logger,
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

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
