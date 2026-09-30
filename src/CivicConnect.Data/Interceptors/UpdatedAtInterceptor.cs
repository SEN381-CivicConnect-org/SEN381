using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CivicConnect.Data.Interceptors;

/// Sets UpdatedAt to now on every modified IHasTimestamps entity before it is saved.
public sealed class UpdatedAtInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (EntityEntry<IHasTimestamps> entry in context.ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
