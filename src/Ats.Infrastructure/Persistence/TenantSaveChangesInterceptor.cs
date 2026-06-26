using Ats.Application.Abstractions;
using Ats.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ats.Infrastructure.Persistence;

public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;

    public TenantSaveChangesInterceptor(ITenantContext tenant) => _tenant = tenant;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is ITenantEntity te && te.TenantId == 0)
            {
                if (_tenant.CurrentTenantId is not int id)
                    throw new InvalidOperationException(
                        $"Cannot insert {entry.Entity.GetType().Name}: no tenant in context. " +
                        "Tenant-scoped writes require a resolved tenant.");
                te.TenantId = id;
            }

            if (entry.Entity is KeyedEntity ke)
            {
                if (entry.State == EntityState.Added && ke.CreatedAt == default) ke.CreatedAt = now;
                if (entry.State == EntityState.Modified) ke.UpdatedAt = now;
            }
        }
    }
}
