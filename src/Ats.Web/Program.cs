using System.Security.Claims;
using Ats.Infrastructure;
using Ats.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)   // sinks (console + rolling file) + enrichers from appsettings
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Health: liveness (process only) and readiness (verifies the database).
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AtsDbContext>("database", tags: new[] { "ready" });

// Persist Data Protection keys so auth cookies (and encrypted data) survive restarts and are shared
// across instances. Defaults to a local folder for dev; override DataProtection:KeyPath in production
// (a shared volume, or move to Azure Blob + Key Vault). Application name pins the key purpose.
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (string.IsNullOrWhiteSpace(keyPath))
    keyPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .SetApplicationName("Ats")
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

builder.Services.AddAtsInfrastructure(builder.Configuration);

builder.Services.AddAuthentication("AtsCookie")
    .AddCookie("AtsCookie", o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Add services to the container. Antiforgery validation applied to all
// non-GET requests (OWASP CSRF protection on the auth + back-office surface).
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

var app = builder.Build();

app.UseSerilogRequestLogging(opts =>
{
    // Attach tenant + user to the request-completion log for tenant-scoped diagnostics (no PII).
    opts.EnrichDiagnosticContext = (diag, http) =>
    {
        var user = http.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantId = user.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrEmpty(tenantId)) diag.Set("TenantId", tenantId);
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId)) diag.Set("UserId", userId);
        }
    };
});

// Liveness = process up; readiness = database reachable (tagged "ready").
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health");   // default: all checks (kept for backwards compatibility)

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Re-execute error pages. The status code is passed as a query parameter (?code=404) so it binds to
// HomeController.Status(int code); the default route's third segment is {id?}, which would NOT bind to
// a parameter named "code" and would leave it 0 (then Response.StatusCode = 0 resets the connection).
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<Ats.Web.Middleware.TenantResolutionMiddleware>();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// WithStaticAssets so attribute-routed pages (the Careers area) also resolve the fingerprinted,
// immutable asset URLs rather than the revalidated no-cache ones.
app.MapControllers().WithStaticAssets();

app.Run();
