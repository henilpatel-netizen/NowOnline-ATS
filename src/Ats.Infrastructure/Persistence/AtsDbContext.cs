using Ats.Application.Abstractions;
using Ats.Domain.Common;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Ats.Infrastructure.Persistence;

public class AtsDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public AtsDbContext(DbContextOptions<AtsDbContext> options, ITenantContext tenant) : base(options)
        => _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<PipelineTemplate> PipelineTemplates => Set<PipelineTemplate>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtsDbContext).Assembly);

        // Global query filter on every ITenantEntity: e.TenantId == currentTenant
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var param = Expression.Parameter(entityType.ClrType, "e");
                var prop = Expression.Property(param, nameof(ITenantEntity.TenantId));
                // current tenant captured via closure over _tenant
                var current = Expression.Call(
                    Expression.Constant(this), nameof(GetTenantIdOrZero), Type.EmptyTypes);
                Expression body = Expression.Equal(prop, current);

                if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
                {
                    var notDeleted = Expression.Not(
                        Expression.Property(param, nameof(ISoftDeletable.IsDeleted)));
                    body = Expression.AndAlso(body, notDeleted);
                }

                var lambda = Expression.Lambda(body, param);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    // Used by the query filter. Returns 0 when no tenant -> filters everything out (fail closed).
    public int GetTenantIdOrZero() => _tenant.CurrentTenantId ?? 0;
}
