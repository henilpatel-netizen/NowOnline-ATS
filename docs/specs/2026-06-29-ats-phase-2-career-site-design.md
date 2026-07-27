# Design Spec: ATS Phase 2 - Career Site

- Date: 2026-06-29
- Status: Proposed (awaiting review)
- Author: Henil Patel (with Claude)
- Related: `2026-06-26-ats-product-design.md` (Sections 5, 9, 11, 12 Phase 2; Appendix C),
  `2026-06-26-ats-phase-1-core-ats-design.md` (entities and ApplicationService reused).

---

## 1. Purpose and scope

Add the public, path-based career site so candidates can browse a tenant's published jobs, open a job
via a referral link, and apply with a resume. Applying captures the referral code on
`Application.SourceCode` and the resume on `Candidate.ResumeFileKey`, finally populating the two fields
Phase 1 left empty. This is the candidate-facing half of the product and the data source for the
Phase 3 ReferralTool loop.

### In scope
A `Careers` MVC area serving `/careers/{slug}` (board), `/careers/{slug}/jobs/{externalRef}` (detail),
and the apply POST; public tenant resolution from the slug; an `IFileStore` abstraction with a local
disk implementation; resume upload with type and size validation; candidate create/match by email;
application create or update (re-apply rule) with `SourceCode` capture and the initial
`ApplicationEvent`; an authenticated back-office "Download resume" link.

### Out of scope (deferred)
- ReferralTool emission, outbox, worker, and the CatsOne vacancy feed (Phase 3).
- Azure Blob storage (the `IFileStore` Azure implementation is Phase 3+; dev uses local disk).
- Subdomains or custom domains (path-based only, per the locked decisions).
- Email notifications and dashboards (Phase 4).

---

## 2. Locked decisions

| Decision | Choice |
|---|---|
| Public tenant resolution | `TenantResolutionMiddleware` maps `{slug}` to `TenantId` into `HttpContext.Items`; `HttpTenantContext` reads claim, then item. Unknown/suspended slug = 404 |
| Resume constraints | Allow `.pdf`, `.doc`, `.docx`; max 5 MB; validate extension and content-type |
| Resume storage (dev) | `IFileStore` + `LocalFileStore` writing outside `wwwroot`; opaque GUID key; not web-served. Azure Blob later |
| Re-apply (same email, same job) | Update the existing application (refresh `SourceCode` if a new non-empty code is present) + resume; no duplicate, no stage change, no event |
| Area structure | New `Careers` area for the public site; back-office stays at root |
| Resume download | Authenticated back-office link on the Application Details page |

---

## 3. Tenant resolution for public requests

`HttpTenantContext.CurrentTenantId` currently reads only the `tenant_id` claim. It is extended to:
1. the `tenant_id` claim (back-office/API), else
2. `HttpContext.Items["TenantId"]` (set by the middleware for career-site requests), else
3. null (fail closed).

`TenantResolutionMiddleware` runs after routing. When the matched route has a `slug` route value and no
authenticated tenant, it looks up an Active `Tenant` by slug (the `Tenants` table is not tenant-scoped,
so this is a plain query) and either sets `HttpContext.Items["TenantId"]` or short-circuits with 404 for
an unknown or suspended slug. Once the item is set, the existing global query filter scopes every
tenant-scoped query automatically, so the public board and detail pages cannot leak across tenants.

`ITenantContext` gains no new members; only `HttpTenantContext`'s resolution order changes. The
documented multi-tenancy rule is updated to list `HttpContext.Items["TenantId"]` (set only by the
slug middleware) as an additional, documented resolution source.

---

## 4. File storage

New abstraction in `Ats.Application/Abstractions/IFileStore.cs`:

```csharp
public interface IFileStore
{
    Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default);
    Task<FileDownload?> OpenAsync(string key, CancellationToken ct = default);
}

public sealed record FileDownload(Stream Content, string ContentType, string DownloadName);
```

`LocalFileStore` (Infrastructure) writes to a configured root (`FileStorage:LocalPath` in config, created
if missing, outside `wwwroot`). `SaveAsync` generates `key = "{guid:N}{ext}"` from the original
extension, writes the bytes, and returns the key (the original filename is never used as a path, avoiding
traversal). `OpenAsync` resolves the key under the root (rejecting any key containing path separators),
opens the file, and infers content-type from the extension. The dev config points at a local folder
(for example `App_Data/uploads` under the content root).

`IFormFile` stays in the Web layer: the career controller validates the upload and calls
`IFileStore.SaveAsync(stream, fileName)`; the apply service only ever sees the returned key string.

---

## 5. Apply flow

New `CareerApplyService` (Application) behind `ICareerService`, plus a repository for public reads:

- `ICareerService.GetPublishedJobsAsync()` - Published, non-deleted jobs for the current tenant.
- `ICareerService.GetPublishedJobAsync(externalRef)` - one Published job by `ExternalRef`, or null.
- `ICareerService.ApplyAsync(ApplyInput)` where
  `ApplyInput(string ExternalRef, string FirstName, string LastName, string Email, string? Phone,
  string? SourceCode, string? ResumeFileKey)`:
  1. Resolve the job by `ExternalRef` (Published); if missing return a failure.
  2. Trim and lowercase the email; create-or-match the `Candidate`. Update name/phone, and set
     `ResumeFileKey` when a new key is provided.
  3. Find the first pipeline stage (lowest `Order`).
  4. If an application for `(candidate, job)` exists: update `SourceCode` only when the incoming code is
     non-empty; keep the current stage; add no event; no duplicate.
  5. Else create the `Application` at the first stage with `SourceCode` (trimmed, max 36; empty = none)
     and write the initial `ApplicationEvent` (`FromStageId = null`).

`SourceCode` is stored verbatim from the query parameter, trimmed, capped at 36 (per the frozen contract,
Appendix C). The `CodeParameterName` to read comes from `TenantSettings`.

This reuses the Phase 1 stage-first logic; where practical the shared application-creation helper from
`ApplicationService` is extracted so both back-office add and public apply create applications
identically. The public apply path stamps `TenantId` via the interceptor (tenant is in context from the
slug), so no manual tenant handling is needed.

---

## 6. Web (Careers area)

`Areas/Careers/` with anonymous controllers and a public `_CareersLayout` (company name header, the
shared `site.css` tokens, Bootstrap; no sidebar). The area route template is
`careers/{slug}/{controller=Jobs}/{action=Index}` with explicit routes for detail and apply.

- `CareersController` (or `JobsController` in the area):
  - `Index(slug)` - the board of published jobs (title, location, employment type).
  - `Detail(slug, externalRef)` - job detail; reads the referral code from the configured query param
    into a hidden field on the apply form. 404 if the job is not Published.
  - `Apply(slug, externalRef, ...)` [HttpPost] - binds the form + `IFormFile resume`, validates the
    resume, stores it via `IFileStore`, calls `ICareerService.ApplyAsync`, then redirects to a
    thank-you page. Validation failures redisplay the form with messages.
- A thank-you view confirming the application.

Resume validation (controller): reject when missing (resume is required to apply), when the extension is
not in the allowlist, when the content-type does not match, or when larger than 5 MB, with a friendly
message.

Antiforgery: the apply form includes the token; global `AutoValidateAntiforgeryToken` validates it.

---

## 7. Back-office resume download

On the existing `Applications/Details` page, add a "Download resume" link shown when the candidate has a
`ResumeFileKey`. A new authenticated `ResumeController.Download(applicationId)` (root, `[Authorize]`)
loads the application (tenant-filtered), resolves the candidate's `ResumeFileKey`, and streams it via
`IFileStore.OpenAsync`. Returns 404 when there is no key or the file is missing.

---

## 8. Security (OWASP-aligned)

- Resume upload: allowlist extension + content-type check + 5 MB cap before storing; generated GUID key
  (no original filename in the path); stored outside `wwwroot`; never directly web-served.
- Public inputs are untrusted: Razor auto-encodes output; email format validated; `SourceCode` stored
  verbatim but length-capped and only ever rendered encoded.
- Tenant strictly from the slug; fail-closed filtering means a wrong or missing tenant yields nothing.
- Resume retrieval is authenticated and tenant-scoped (back-office only).
- Antiforgery on the public apply POST.

---

## 9. Verification

Build + run (no test project). Manual checks:
- Browse `/careers/{slug}` for a tenant with a published job; confirm only Published jobs show and a
  Draft/Closed job does not.
- Open a job detail with `?ref=1RR123456`; apply with a valid PDF; confirm the application appears on the
  back-office board in the first stage and that `SourceCode` and the candidate's `ResumeFileKey` are set.
- Re-apply with the same email to the same job with a different `?ref=`; confirm no duplicate and the
  `SourceCode` updates; the stage does not change.
- Upload an oversized or disallowed file; confirm rejection with a message.
- Hit `/careers/{unknown-slug}` and a non-published job; confirm 404.
- In the back-office, download the resume from the Application Details page.
- Cross-tenant: confirm tenant A's career site never shows tenant B's jobs.

---

## 10. Documentation maintenance (spec Section 16)

After Phase 2 lands:
- Add `.claude/skills/career-site/SKILL.md` (the area, slug tenant resolution, `IFileStore`, apply flow,
  re-apply rule, security).
- Update `.claude/rules/multi-tenancy.md` to note `HttpContext.Items["TenantId"]` (set only by the slug
  middleware) as a documented resolution source.
- Refresh the `CLAUDE.md` skill-index and keep `docs/specs` and `docs/plans` current.

---

## 11. Notes

- Restrictions unchanged: the AI does not commit, apply migrations, or deploy. Phase 2 likely needs no
  schema change (the `ResumeFileKey` and `SourceCode` columns already exist from Phase 1); if any column
  is added, a migration is created by the AI and applied by the developer.
- Em dashes and emoji are avoided in all generated content per the working conventions.
