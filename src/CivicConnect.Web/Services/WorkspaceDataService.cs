using System.Security.Claims;
using CivicConnect.Data;
using CivicConnect.Web.Controllers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Web.Services;

public sealed record DirectoryUser(string Name, string Email, bool IsActive, List<string> Roles);
public sealed record DirectoryCategory(string Name, string Description, bool IsActive);

// Each read owns a short-lived scope/context; no DbContext is shared across circuit operations.
public sealed class WorkspaceDataService(IServiceScopeFactory scopes, AuthenticationStateProvider authentication)
{
    private async Task<ClaimsPrincipal> RequireUserAsync(string? role = null)
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || (role is not null && !user.IsInRole(role)))
            throw new UnauthorizedAccessException();
        return user;
    }

    public async Task<List<DirectoryUser>> GetUsersAsync()
    {
        await RequireUserAsync("Management");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await db.AppUsers.AsNoTracking().OrderBy(u => u.FullName).ThenBy(u => u.Email)
            .Select(u => new DirectoryUser(u.FullName, u.Email, u.IsActive,
                u.UserRoles.OrderBy(r => r.Role.Name).Select(r => r.Role.Name).ToList()))
            .ToListAsync();
    }

    public async Task<List<DirectoryCategory>> GetCategoriesAsync()
    {
        await RequireUserAsync("Management");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await db.Categories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new DirectoryCategory(c.Name, c.Description, c.IsActive)).ToListAsync();
    }

    public async Task<List<NotificationDto>> GetUnreadNotificationsAsync()
    {
        var user = await RequireUserAsync();
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var recipientId))
            throw new UnauthorizedAccessException();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicConnectDbContext>();
        return await NotificationsController.ReadUnreadAsync(db, recipientId);
    }
}
