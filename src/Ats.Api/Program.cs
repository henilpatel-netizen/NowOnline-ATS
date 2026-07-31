using Ats.Api.OpenApi;
using Ats.Infrastructure;
using Ats.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtsDbContext>("database", tags: new[] { "ready" });
builder.Services.AddAtsInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddScoped<Ats.Api.Authentication.FeedApiKeyFilter>();

// RFC 7807 problem responses for a partner-facing API.
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<FeedSecuritySchemeTransformer>();
});

var app = builder.Build();

// Structured problem responses for unhandled errors. Development keeps the built-in developer
// exception page (added automatically) for diagnostics.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}
app.UseStatusCodePages();

app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

// Dev-only API docs UI (never enabled in production, per security guidance).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
