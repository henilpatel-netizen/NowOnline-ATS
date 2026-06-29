using Ats.Application.Abstractions;
using Ats.Application.Departments;
using Ats.Application.Jobs;
using Ats.Application.Locations;
using Ats.Application.Pipelines;
using Ats.Application.Tenancy;
using Ats.Infrastructure.Identity;
using Ats.Infrastructure.Persistence;
using Ats.Infrastructure.Persistence.Repositories;
using Ats.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ats.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<AtsDbContext>((sp, options) =>
        {
            options.UseSqlServer(config.GetConnectionString("DefaultConnection"));
            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        services.AddScoped<IOnboardingStore, OnboardingStore>();
        services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();

        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IPipelineTemplateRepository, PipelineTemplateRepository>();
        services.AddScoped<IPipelineTemplateService, PipelineTemplateService>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IJobService, JobService>();

        return services;
    }
}
