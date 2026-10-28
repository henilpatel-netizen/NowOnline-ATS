using System.Security.Claims;
using System.Threading.RateLimiting;
using Ats.Application.Abstractions;
using Ats.Application.Accounts;
using Ats.Application.Integration;
using Ats.Infrastructure;
using Ats.Infrastructure.Persistence;
using Ats.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccountTokens, DataProtectionAccountTokens>();

// Vacancy snapshots carry an absolute career-site URL; fail at startup rather than on the first publish.
builder.Services.AddOptions<IntegrationOptions>()
    .Bind(builder.Configuration.GetSection("Integration"))
    .Validate(o => Uri.TryCreate(o.CareerSiteBaseUrl, UriKind.Absolute, out var u) && u.Scheme is "https" or "http",
        "Integration:CareerSiteBaseUrl must be an absolute http(s) URL.")
    .Validate(o => !o.AllowInsecureReferralToolUrl || builder.Environment.IsDevelopment(),
        "Integration:AllowInsecureReferralToolUrl is only allowed in Development.")
    .ValidateOnStart();
// Application services take the validated options object itself (Application has no Options dependency).
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<IntegrationOptions>>().Value);
// Registered here, not in AddAtsInfrastructure: they need IAccountTokens, which only this host provides.
builder.Services.AddScoped<IAccountRecoveryService, AccountRecoveryService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<IEmailChangeService, EmailChangeService>();

// Signed-out account POSTs, fixed 15-minute window per client IP. "account-email" (default 5) is on Forgot
// password, the only one that mails an address typed by a stranger. "account-token" (default 30) is on the
// POSTs that use an emailed link (reset, accept invite, confirm email), so one office behind a NAT IP can
// onboard a team. The e2e suite raises both, since every test request comes from 127.0.0.1. A rejection is a
// plain-text 429 (no page; the limit is per IP, so it reveals nothing about any address).
var accountEmailPermits = builder.Configuration.GetValue("RateLimits:AccountEmailPermitLimit", 5);
var accountTokenPermits = builder.Configuration.GetValue("RateLimits:AccountTokenPermitLimit", 30);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    void PerIp(string policy, int permits) => o.AddPolicy(policy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(15) }));
    PerIp(Ats.Web.Controllers.AccountController.EmailRateLimit, accountEmailPermits);
    PerIp(Ats.Web.Controllers.AccountController.TokenRateLimit, accountTokenPermits);
    o.OnRejected = (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        return new ValueTask(ctx.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", ct));
    };
});

// Host-owned tenant/user resolution (QUAL-6).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, Ats.Web.Tenancy.HttpTenantContext>();
builder.Services.AddScoped<ICurrentUser, Ats.Web.Identity.CurrentUser>();

// Presentation read services, so controllers stay thin (QUAL-3).
builder.Services.AddScoped<Ats.Web.ViewServices.IBoardViewService, Ats.Web.ViewServices.BoardViewService>();

builder.Services.AddAuthentication(AtsSignIn.Scheme)
    .AddCookie(AtsSignIn.Scheme, o =>
    {
        o.LoginPath = "/Account/Login";
        o.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
        // A signed-in user without the permission gets a 403, rendered by the status-code page
        // (HomeController.Status), instead of being bounced to the login page.
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o => o.AddAtsPermissionPolicies());

// Add services to the container. Antiforgery validation applied to all
// non-GET requests (OWASP CSRF protection on the auth + back-office surface).
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<RequirePasswordChangeFilter>();
});

var app = builder.Build();

app.UseSerilogRequestLogging(opts =>
{
    // Attach tenant + user to the request-completion log for tenant-scoped diagnostics (no PII).
    opts.EnrichDiagnosticContext = (diag, http) =>
    {
        var user = http.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantId = user.FindFirst(AtsSignIn.TenantClaim)?.Value;
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
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();   // default: all checks (kept for backwards compatibility)

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
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<Ats.Web.Middleware.TenantResolutionMiddleware>();

app.MapStaticAssets().Add(b => b.Metadata.Add(new AllowAnonymousAttribute()));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// WithStaticAssets so attribute-routed pages (the Careers area) also resolve the fingerprinted,
// immutable asset URLs rather than the revalidated no-cache ones.
app.MapControllers().WithStaticAssets();

app.Run();
