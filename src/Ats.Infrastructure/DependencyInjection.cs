using Ats.Application.Abstractions;
using Ats.Application.Applications;
using Ats.Application.Auditing;
using Ats.Application.Branding;
using Ats.Application.Candidates;
using Ats.Application.Career;
using Ats.Application.Dashboard;
using Ats.Application.Departments;
using Ats.Application.Integration;
using Ats.Application.Jobs;
using Ats.Application.Locations;
using Ats.Application.Organisation;
using Ats.Application.Pipelines;
using Ats.Application.Search;
using Ats.Application.Shell;
using Ats.Application.Tenancy;
using Ats.Infrastructure.Applications;
using Ats.Infrastructure.Auditing;
using Ats.Infrastructure.Branding;
using Ats.Infrastructure.Candidates;
using Ats.Infrastructure.Dashboard;
using Ats.Infrastructure.Files;
using Ats.Infrastructure.Identity;
using Ats.Infrastructure.Integration;
using Ats.Infrastructure.Jobs;
using Ats.Infrastructure.Organisation;
using Ats.Infrastructure.Persistence;
using Ats.Infrastructure.Persistence.Repositories;
using Ats.Infrastructure.Search;
using Ats.Infrastructure.Shell;
using Ats.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ats.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // ITenantContext and ICurrentUser are registered by each HOST (QUAL-6): how a tenant and a
        // user are resolved is host-specific (claims in the web app, a feed key in the API, a
        // settable value in the worker). Infrastructure no longer owns an HTTP implementation, so the
        // worker no longer has to remove-and-replace one.
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<AtsDbContext>((sp, options) =>
        {
            options.UseSqlServer(config.GetConnectionString("DefaultConnection"),
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null));
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
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<ICandidateService, CandidateService>();
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IFileStore, LocalFileStore>();
        services.AddScoped<ICareerRepository, CareerRepository>();
        services.AddScoped<ICareerService, CareerService>();
        services.AddScoped<IVacancyFeedRepository, VacancyFeedRepository>();
        services.AddScoped<IOutboxEnqueuer, OutboxEnqueuer>();
        services.AddScoped<IIntegrationSettingsService, IntegrationSettingsService>();
        services.AddScoped<IDeliveryLogService, DeliveryLogService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditQuery, AuditQuery>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Presentation-layer read services. Scoped so their per-request caches work.
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();
        services.AddScoped<IShellSummaryService, ShellSummaryService>();
        services.AddScoped<IGlobalSearchService, GlobalSearchService>();

        // Screen read-model projections (Phase 2).
        services.AddScoped<IJobListQuery, JobListQuery>();
        services.AddScoped<ICandidateListQuery, CandidateListQuery>();
        services.AddScoped<IApplicationCardQuery, ApplicationCardQuery>();
        services.AddScoped<IOrganisationReadService, OrganisationReadService>();

        services.AddHttpClient<IReferralToolClient, ReferralToolClient>();

        return services;
    }
}
