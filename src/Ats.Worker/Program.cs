using Ats.Application.Abstractions;
using Ats.Application.Email;
using Ats.Application.Integration;
using Ats.Infrastructure;
using Ats.Infrastructure.Email;
using Ats.Infrastructure.Integration;
using Ats.Infrastructure.Tenancy;
using Ats.Worker;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAtsInfrastructure(builder.Configuration);

// The worker has no HttpContext, so it registers the settable tenant context. Infrastructure no
// longer registers an HTTP one, so there is nothing to remove first (QUAL-6).
builder.Services.AddScoped<WorkerTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());
// A background process acts as no one; audit/application services still require an ICurrentUser.
builder.Services.AddScoped<ICurrentUser, AnonymousCurrentUser>();

builder.Services.AddOptions<IntegrationOptions>()
    .Bind(builder.Configuration.GetSection("Integration"))
    .Validate(o => !o.AllowInsecureReferralToolUrl || builder.Environment.IsDevelopment(),
        "Integration:AllowInsecureReferralToolUrl is only allowed in Development.")
    .ValidateOnStart();
builder.Services.AddScoped<IOutboxClaimStore, OutboxClaimStore>();
builder.Services.AddScoped<IOutboxProcessor, OutboxProcessor>();
builder.Services.AddHostedService<OutboxDrainer>();

// Without user-secrets for Email (see EmailOptions), start-up fails here naming the missing setting.
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection("Email"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EmailOptions>>(new EmailOptionsValidator(builder.Environment.IsDevelopment()));
builder.Services.AddScoped<IEmailClaimStore, EmailClaimStore>();
builder.Services.AddScoped<IEmailProcessor, EmailProcessor>();
builder.Services.AddScoped<IEmailTransport, MailKitEmailTransport>();
builder.Services.AddHostedService<EmailDrainer>();

var host = builder.Build();
host.Run();

internal sealed class EmailOptionsValidator(bool isDevelopment) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options) =>
        EmailOptions.Validate(options, isDevelopment).ToList() is { Count: > 0 } errors
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
}
