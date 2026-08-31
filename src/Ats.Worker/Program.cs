using Ats.Application.Abstractions;
using Ats.Application.Integration;
using Ats.Infrastructure;
using Ats.Infrastructure.Integration;
using Ats.Infrastructure.Tenancy;
using Ats.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAtsInfrastructure(builder.Configuration);

// The worker has no HttpContext, so it registers the settable tenant context. Infrastructure no
// longer registers an HTTP one, so there is nothing to remove first (QUAL-6).
builder.Services.AddScoped<WorkerTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());
// A background process acts as no one; audit/application services still require an ICurrentUser.
builder.Services.AddScoped<ICurrentUser, AnonymousCurrentUser>();

builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection("Integration"));
builder.Services.AddScoped<IOutboxClaimStore, OutboxClaimStore>();
builder.Services.AddScoped<IOutboxProcessor, OutboxProcessor>();
builder.Services.AddHostedService<OutboxDrainer>();

var host = builder.Build();
host.Run();
