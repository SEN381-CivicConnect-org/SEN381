using System.Security.Claims;
using CivicConnect.Data.Configurations;
using CivicConnect.Web.Components.Pages.Management;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CivicConnect.Tests;

public sealed class RoleDelegationTests
{
    [Fact]
    public void BaselineHierarchy_MatchesDatabaseBaselineRolesInHierarchicalOrder()
    {
        var service = CreateService();

        var expectedNames = RoleConfiguration.BaselineRoles
            .OrderBy(r => r.Id)
            .Select(r => r.Name)
            .ToArray();

        Assert.Equal(expectedNames, service.AllRoles.Select(r => r.Name).ToArray());
        Assert.Equal(1, service.GetRoleLevel("reporter"));
        Assert.Equal(2, service.GetRoleLevel("technician"));
        Assert.Equal(3, service.GetRoleLevel("supervisor"));
        Assert.Equal(4, service.GetRoleLevel("operations manager"));
        Assert.Equal(5, service.GetRoleLevel("administrator"));
    }

    [Theory]
    [InlineData("reporter", new[] { "reporter" })]
    [InlineData("technician", new[] { "reporter", "technician" })]
    [InlineData("supervisor", new[] { "reporter", "technician", "supervisor" })]
    [InlineData("operations manager", new[] { "reporter", "technician", "supervisor", "operations manager" })]
    [InlineData("administrator", new[] { "reporter", "technician", "supervisor", "operations manager", "administrator" })]
    public void GetAssignableRoles_OnlyReturnsRolesUpToAndIncludingActorLevel(
        string actorRole,
        string[] expectedAssignableRoles)
    {
        var service = CreateService();

        var assignable = service.GetAssignableRoles(actorRole)
            .Select(r => r.Name)
            .ToArray();

        Assert.Equal(expectedAssignableRoles, assignable);
    }

    [Theory]
    [InlineData("administrator", "administrator", "operations manager", true)]
    [InlineData("administrator", "reporter", "administrator", true)]
    [InlineData("operations manager", "operations manager", "supervisor", true)]
    [InlineData("operations manager", "supervisor", "reporter", true)]
    [InlineData("operations manager", "administrator", "reporter", false)]
    [InlineData("operations manager", "reporter", "administrator", false)]
    [InlineData("supervisor", "operations manager", "technician", false)]
    [InlineData("technician", "supervisor", "reporter", false)]
    [InlineData("reporter", "technician", "reporter", false)]
    public void CanDelegateRole_EnforcesHierarchicalCeilingAndTargetRank(
        string actorRole,
        string targetRoleToGrant,
        string targetUserCurrentRole,
        bool expectedAllowed)
    {
        var service = CreateService();

        var allowed = service.CanDelegateRole(actorRole, targetRoleToGrant, targetUserCurrentRole);

        Assert.Equal(expectedAllowed, allowed);
    }

    [Fact]
    public async Task DelegateRoleAsync_BlocksLowerTierUserFromGrantingHigherRoleBeforeDatabaseCall()
    {
        var service = CreateService();
        var operationsManagerPrincipal = CreatePrincipal("operations manager", "Management");

        var result = await service.DelegateRoleAsync(
            operationsManagerPrincipal,
            Guid.NewGuid(),
            "administrator");

        Assert.False(result.Succeeded);
        Assert.Contains("Insufficient privileges", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DelegateRoleAsync_RejectsUnauthenticatedActorBeforeDatabaseCall()
    {
        var service = CreateService();
        var anonymousPrincipal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await service.DelegateRoleAsync(
            anonymousPrincipal,
            Guid.NewGuid(),
            "reporter");

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task DelegateRoleAsync_RejectsUnknownRoleBeforeDatabaseCall()
    {
        var service = CreateService();
        var adminPrincipal = CreatePrincipal("administrator", "Management");

        var result = await service.DelegateRoleAsync(
            adminPrincipal,
            Guid.NewGuid(),
            "super-root-role");

        Assert.False(result.Succeeded);
        Assert.Contains("not a valid CivicConnect authorization role", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoleManagementPage_HasExpectedRoutesAndAuthorizeAttribute()
    {
        var routes = typeof(RoleManagement)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Select(r => r.Template)
            .ToArray();

        Assert.Contains("/management/roles", routes);
        Assert.Contains("/role-management", routes);

        var authorizeAttr = typeof(RoleManagement)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(authorizeAttr);
        Assert.Contains("Management", authorizeAttr.Roles, StringComparison.Ordinal);
        Assert.Contains("administrator", authorizeAttr.Roles, StringComparison.Ordinal);
        Assert.Contains("operations manager", authorizeAttr.Roles, StringComparison.Ordinal);
    }

    private static RoleDelegationService CreateService() =>
        new(new NoOpScopeFactory());

    private static ClaimsPrincipal CreatePrincipal(string databaseRole, string portalRole)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Test Actor"),
            new Claim(ClaimTypes.Email, "actor@civicconnect.demo"),
            new Claim(ClaimTypes.Role, portalRole),
            new Claim("DatabaseRole", databaseRole),
            new Claim(ClaimTypes.Role, databaseRole)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "CivicConnect"));
    }

    private sealed class NoOpScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("Database scope should not be created when pre-database validation rejects the request.");
    }
}
