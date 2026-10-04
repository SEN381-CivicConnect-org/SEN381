using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CivicConnect.Web.Services;

// Demo identities live in the Blazor circuit, not in an HTTP cookie. A fresh
// protected-page request must reach sign-in without trying an unregistered scheme.
public sealed class DemoAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded) return next(context);
        if (HttpMethods.IsGet(context.Request.Method))
        {
            var requested = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            var page = result.Forbidden ? "/access-denied" : "/login";
            context.Response.Redirect(context.Request.PathBase + page + "?returnUrl=" + Uri.EscapeDataString(requested));
        }
        else context.Response.StatusCode = result.Forbidden ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
