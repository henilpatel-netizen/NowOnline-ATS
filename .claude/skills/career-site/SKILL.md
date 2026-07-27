---
name: career-site
description: The Ats public career site - the Careers area, slug-based tenant resolution, IFileStore resume storage, and the public apply flow. Read before changing public-facing or file-upload behavior.
---

# Ats Career Site (Phase 2)

## Area and routes
Public pages live in the `Careers` MVC area (`Areas/Careers`), anonymous, attribute-routed under
`careers/{slug}`: board (`""`), detail (`jobs/{externalRef}`), apply (`POST jobs/{externalRef}/apply`),
thank-you (`thank-you`). The area has its own `_CareersLayout` (no sidebar) reusing `site.css` tokens.
`app.MapControllers()` in `Program.cs` enables the area's attribute routes alongside the back-office
conventional route.

## Tenant resolution
`TenantResolutionMiddleware` (registered after `UseAuthorization`) reads the `{slug}` route value on
unauthenticated requests, looks up an Active tenant, and sets `HttpContext.Items["TenantId"]`.
`HttpTenantContext` reads the `tenant_id` claim first, then that item. Unknown or suspended slug returns
404. The global query filter then scopes every public query. This is a documented tenant-resolution
source (see `.claude/rules/multi-tenancy.md`).

## File storage
`IFileStore` (Application) with `LocalFileStore` (Infrastructure) writes to `FileStorage:LocalPath`
(outside `wwwroot`), returns an opaque `{guid}{ext}` key (no original filename, no traversal). Resumes
are never web-served; back-office download streams them via `ResumeController` (`[Authorize]`).

## Apply flow
`ICareerService.ApplyAsync` resolves the published job by `ExternalRef`, create-or-matches the candidate
by email (sets `ResumeFileKey`), and either updates the existing application's `SourceCode` (re-apply, no
duplicate, no stage change) or creates a new application at the first stage with the initial
`ApplicationEvent` (`MovedByUserId` null for public apply). The controller validates the resume
(.pdf/.doc/.docx, content-type, 5 MB) before storing; `IFormFile` never reaches the Application layer.

## Referral attribution (the `?ref=` capture)
`JobsController.ResolveReferralCode(slug, codeParam)` captures the tenant's code query parameter (name
from `ICareerService.GetCodeParameterNameAsync`, e.g. `ref`) on any careers page into a per-tenant
cookie (`ats_ref`, `Path=/careers/{slug}`, `HttpOnly`, `Secure` on HTTPS, `SameSite=Lax`, essential,
30-day, last-touch, trimmed and capped to 36 chars). Index captures only; Detail captures and resolves
`Code` (query wins, else cookie); Apply falls back to the cookie when `form.SourceCode` is empty. This
makes the ReferralTool general "all jobs" link attribute: the code survives navigation from the landing
page to the apply form even though job links don't carry `?ref=`. The cookie value is untrusted input,
only ever forwarded as `SourceCode`; ReferralTool validates and rejects unknown codes. Future upgrade:
a dedicated redirect capture route (`/careers/{slug}/r/{code}`) if click analytics are needed.

## Security
Only Published jobs are exposed (404 otherwise); resume validated + stored outside web root; antiforgery
on the apply POST (global filter); tenant strictly from the slug with fail-closed filtering.
