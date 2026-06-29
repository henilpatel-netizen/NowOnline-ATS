using Ats.Infrastructure;
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

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
