using Ats.Api.OpenApi;
using Ats.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddHealthChecks();
builder.Services.AddAtsInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddScoped<Ats.Api.Authentication.FeedApiKeyFilter>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<FeedSecuritySchemeTransformer>();
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");

// Dev-only API docs UI (never enabled in production, per security guidance).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
