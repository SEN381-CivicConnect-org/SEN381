using System.ComponentModel.DataAnnotations;
using CivicConnect.Web.Components.Pages.Auth;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace CivicConnect.Tests;

public sealed class UserRegistrationTests
{
    [Fact]
    public void SignUpPage_HasExpectedRouteAndAllowAnonymousAttribute()
    {
        var routeAttr = typeof(SignUp)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .SingleOrDefault();

        Assert.NotNull(routeAttr);
        Assert.Equal("/signup", routeAttr.Template);

        var allowAnonymous = typeof(SignUp)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .Any();

        Assert.True(allowAnonymous);
    }

    [Fact]
    public void SignUpFormModel_ValidInput_PassesDataAnnotationsValidation()
    {
        var model = new SignUpFormModel
        {
            FullName = "Nomsa Khumalo",
            Email = "nomsa.khumalo@civicconnect.demo",
            Password = "SecurePassword#2026",
            ConfirmPassword = "SecurePassword#2026"
        };

        var results = ValidateModel(model);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("", "valid@example.com", "ValidPass123!", "ValidPass123!", nameof(SignUpFormModel.FullName))]
    [InlineData("A", "valid@example.com", "ValidPass123!", "ValidPass123!", nameof(SignUpFormModel.FullName))]
    [InlineData("Nomsa Khumalo", "not-an-email", "ValidPass123!", "ValidPass123!", nameof(SignUpFormModel.Email))]
    [InlineData("Nomsa Khumalo", "valid@example.com", "short", "short", nameof(SignUpFormModel.Password))]
    [InlineData("Nomsa Khumalo", "valid@example.com", "ValidPass123!", "DifferentPass123!", nameof(SignUpFormModel.ConfirmPassword))]
    public void SignUpFormModel_InvalidInput_FailsDataAnnotationsValidation(
        string fullName,
        string email,
        string password,
        string confirmPassword,
        string expectedMemberName)
    {
        var model = new SignUpFormModel
        {
            FullName = fullName,
            Email = email,
            Password = password,
            ConfirmPassword = confirmPassword
        };

        var results = ValidateModel(model);
        Assert.Contains(results, r => r.MemberNames.Contains(expectedMemberName));
    }

    [Theory]
    [InlineData("", "valid@example.com", "ValidPass123!", "ValidPass123!")]
    [InlineData("   ", "valid@example.com", "ValidPass123!", "ValidPass123!")]
    [InlineData("A", "valid@example.com", "ValidPass123!", "ValidPass123!")]
    [InlineData("Nomsa Khumalo", "", "ValidPass123!", "ValidPass123!")]
    [InlineData("Nomsa Khumalo", "   ", "ValidPass123!", "ValidPass123!")]
    [InlineData("Nomsa Khumalo", "invalid-email", "ValidPass123!", "ValidPass123!")]
    [InlineData("Nomsa Khumalo", "user@nodot", "ValidPass123!", "ValidPass123!")]
    [InlineData("Nomsa Khumalo", "valid@example.com", "", "")]
    [InlineData("Nomsa Khumalo", "valid@example.com", "short", "short")]
    [InlineData("Nomsa Khumalo", "valid@example.com", "ValidPass123!", "")]
    [InlineData("Nomsa Khumalo", "valid@example.com", "ValidPass123!", "Mismatch123!")]
    public async Task RegisterAsync_RejectsInvalidInputBeforeDatabaseCall(
        string fullName,
        string email,
        string password,
        string confirmPassword)
    {
        var service = new UserRegistrationService(new DummyScopeFactory());

        var result = await service.RegisterAsync(fullName, email, password, confirmPassword);

        Assert.False(result.Succeeded);
        Assert.False(result.IsDuplicateEmail);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void AppAuthenticationStateProvider_DoesNotExposeRedundantRegisterAsync()
    {
        var registerMethods = typeof(AppAuthenticationStateProvider)
            .GetMethods()
            .Where(m => m.Name == "RegisterAsync")
            .ToArray();

        Assert.Empty(registerMethods);
    }

    [Fact]
    public void DefaultLowestRoleName_IsReporter()
    {
        Assert.Equal("reporter", UserRegistrationService.DefaultLowestRoleName);
        Assert.Equal(
            CivicConnect.Data.Security.Argon2idPasswordHasher.DefaultLowestRoleName,
            UserRegistrationService.DefaultLowestRoleName);
    }

    private static List<ValidationResult> ValidateModel(SignUpFormModel model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    private sealed class DummyScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() =>
            throw new InvalidOperationException("Database scope should not be created when input validation fails.");
    }
}
