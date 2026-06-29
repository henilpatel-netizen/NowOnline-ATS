using Ats.Application.Abstractions;
using Ats.Application.Integration;
using Ats.Infrastructure;
using Ats.Infrastructure.Integration;
using Ats.Infrastructure.Tenancy;
using Ats.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAtsInfrastructure(builder.Configuration);

// The worker has no HttpContext: replace the HTTP tenant context with a settable one.
builder.Services.RemoveAll<ITenantContext>();
builder.Services.AddScoped<WorkerTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());

builder.Services.Configure<IntegrationOptions>(builder.Configuration.GetSection("Integration"));
builder.Services.AddHttpClient<IReferralToolClient, ReferralToolClient>();
builder.Services.AddScoped<IOutboxClaimStore, OutboxClaimStore>();
builder.Services.AddScoped<IOutboxProcessor, OutboxProcessor>();
builder.Services.AddHostedService<OutboxDrainer>();

var host = builder.Build();
host.Run();
