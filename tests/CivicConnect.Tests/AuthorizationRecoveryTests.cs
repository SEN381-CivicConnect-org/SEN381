using System.Security.Claims;
using CivicConnect.Web.Components.Pages.Staff;
using CivicConnect.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CivicConnect.Tests;

public class AuthorizationRecoveryTests
{
    [Theory]
    [InlineData("GET", false, 302)]
    [InlineData("GET", true, 302)]
    [InlineData("POST", false, 401)]
    [InlineData("POST", true, 403)]
    public async Task DenialNeverExecutesProtectedEndpoint(string method, bool forbidden, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.PathBase = "/civic";
        context.Request.Path = "/staff/queue";
        context.Request.QueryString = new QueryString("?filter=open");
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        await new DemoAuthorizationResultHandler().HandleAsync(
            _ => throw new Exception("Protected endpoint executed"), context, policy,
            forbidden ? PolicyAuthorizationResult.Forbid() : PolicyAuthorizationResult.Challenge());
        Assert.Equal(status, context.Response.StatusCode);
        var expected = forbidden ? "/access-denied" : "/login";
        Assert.Equal(method == "GET"
            ? $"/civic{expected}?returnUrl=%2Fcivic%2Fstaff%2Fqueue%3Ffilter%3Dopen"
            : "", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task SuccessfulAuthorizationExecutesEndpoint()
    {
        var executed = false;
        await new DemoAuthorizationResultHandler().HandleAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        }, new DefaultHttpContext(), new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Success());
        Assert.True(executed);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Requester", false)]
    [InlineData("Staff", true)]
    [InlineData("Management", false)]
    public async Task QueueRetainsItsActualStaffOnlyPolicy(string? role, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        using var provider = services.BuildServiceProvider();
        var attributes = typeof(Queue).GetCustomAttributes(true).OfType<IAuthorizeData>();
        var policy = await AuthorizationPolicy.CombineAsync(
            provider.GetRequiredService<IAuthorizationPolicyProvider>(), attributes);
        Assert.NotNull(policy);
        var user = role is null ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "CivicConnect"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy);
        Assert.Equal(allowed, result.Succeeded);
    }
}
