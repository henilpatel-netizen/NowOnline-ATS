# Ats — Claude Code Instructions

Multi-tenant ATS product. .NET 10 MVC + SQL Server + EF Core. Integrates with ReferralTool.

## Restricted actions (manual developer operations only)
Read `.claude/rules/restrictions.md`. Never run git commit/push/merge/rebase/reset, EF
`database update` / apply migrations, or any deploy/CI. Refuse and suggest the manual command instead.

## Project overview
Companies register as tenants, post jobs, run candidates through a configurable pipeline, host a
public career site, and push referral status updates to ReferralTool.
Solution: `Ats.slnx` (modern XML solution format, .NET 10 default; projects under `src/`).

| Project | Purpose |
|---------|---------|
| `Ats.Domain` | Entities, enums, domain rules. No framework/EF dependencies. |
| `Ats.Application` | Use-case services, abstractions (`ITenantContext`, `IIdentityService`), validators. |
| `Ats.Infrastructure` | EF Core `AtsDbContext`, tenancy filters + interceptor, Identity impl, DI. |
| `Ats.Web` | MVC back-office (root: `/Jobs`, `/Candidates`, `/Board`, ...) + public career site (`Careers` area, `/careers/{slug}`). |
| `Ats.Api` | REST API: vacancy feed + integration endpoints. |
| `Ats.Worker` | Background host: outbox delivery, notifications. |
| `Ats.Tests` | xUnit tests for pure Application-layer logic (no database). Under `tests/`. |

## Build / run
```
dotnet build
dotnet test
dotnet run --project src/Ats.Web
```

End-to-end tests (Playwright, `tests/e2e/`) drive a real browser and start the app themselves:
```
npx playwright test
npx playwright test --ui
```
Set the sign-in credentials in `tests/e2e/auth.setup.ts` (or `ATS_TEST_EMAIL` / `ATS_TEST_PASSWORD`).
The suite is the source of truth for navigation cost and accessibility — both were verified by
guesswork before it existed, and both were wrong.

## Front-end
Server-rendered MVC + Bootstrap 5, reskinned with the NowOnline design system (Urbanist / Lexend /
Sometype Mono, Material Symbols icons, four layered `ats-*.css` files). Client libraries are managed
by LibMan (`libman.json`); run `libman restore` to populate `src/Ats.Web/wwwroot/lib`. UI
conventions: `.claude/skills/ui/SKILL.md`.

Back-office navigation is **htmx-boosted**: only `#ats-content` is swapped. Both the sidebar nav and
`<main id="ats-content">` carry the boost config, so in-content links, pagers, filters and form POSTs
are boosted too (measured: 862ms -> 42ms, 17 asset re-fetches -> 0). Three rules you must not break
(details + rationale in the UI skill): `hx-target`/`hx-select` are **inherited**, so anything inside
`#ats-content` that drives its own htmx request must override them (`hx-select="unset"`); shared
libraries go in `<head>` while page `@section Scripts` renders inside `<main>`; the document title
comes from `data-page-title`. Use `hx-confirm`, never `onsubmit="return confirm(...)"` — a boosted
submit bypasses the native handler.
Never set `grid-template-columns` inline, and never put a raw hex colour in a view.

## Architecture (strict layering)
Controllers -> Application services -> repositories -> EF Core (Infrastructure).
`Ats.Domain` has no EF/framework deps. Always `async/await`.

## Multi-tenancy (CRITICAL)
Shared DB + `TenantId` discriminator. Every tenant entity extends `TenantEntity` / implements
`ITenantEntity`. Isolation is enforced automatically by the global query filter + `SaveChanges`
interceptor in `AtsDbContext`; do NOT hand-set `TenantId` except in the documented onboarding path.
Bypass the filter only with `IgnoreQueryFilters()` at sign-in/onboarding (documented). Full rule:
`.claude/rules/multi-tenancy.md`.

## Database migrations
Applied MANUALLY by a developer. See `.claude/rules/migrations.md`.

## ReferralTool integration
Frozen contract in `docs/integration/referraltool-contract.md` — the source of truth. Do not
re-derive it from the ReferralTool repo.

## UI: NowOnline design system
The whole product (back office + public career site) is on the NowOnline design system — tokenised
CSS (`wwwroot/css/ats-*.css`) over Bootstrap, Urbanist/Lexend/Sometype Mono, Material Symbols icons,
per-tenant branding. See `.claude/skills/ui/SKILL.md` before building any view. Design source: the
Claude Design handoff spec `docs/specs/2026-07-30-ats-nowonline-redesign-design.md`; implemented in
four phases (`docs/plans/2026-07-30-ats-redesign-phase-{1..4}-*.md`).

## Conventions
- Auth behind `IIdentityService` (ASP.NET Core Identity impl); swappable later.
- No hardcoded secrets; config per environment, secrets outside source control.
- Code comments only when they say something the code/names cannot.
- **Build must stay warning-clean.** `.editorconfig` + `latest-recommended` analyzers run on build
  (`TreatWarningsAsErrors` is deliberately `false`, but leave no new warnings). `dotnet format
  --verify-no-changes` must pass; CI (`.github/workflows/ci.yml`) gates build + tests + format.
- **User email is globally unique** across tenants (`IX_Users_Email`), so sign-in resolves to exactly
  one tenant. See `.claude/rules/multi-tenancy.md`.
- **Times:** store UTC, never `ToLocalTime()`/`DateTime.Now`. Render with the `<local-time>` tag helper
  (explicit end tag required) so timestamps show in the **viewer's** timezone. Details: UI skill.
- **Explicit transactions must run inside the EF execution strategy** (`EnableRetryOnFailure` is on) —
  use `IApplicationRepository.InTransactionAsync`. Do not adopt `AddDbContextPool` (it would capture
  the scoped tenant context in the interceptor). Details: architecture skill.
- **No server-side data cache by design** (freshness requirement). Read the phase-04 status note before
  adding one.
- **`OperationResult` is in `Ats.Application.Common`**; one search path per screen (the `*ListQuery`
  read models). `ITenantContext`/`ICurrentUser` are registered per host, never by Infrastructure, which
  has no ASP.NET dependency. Details: architecture skill.
- **Business rules need tests.** `tests/Ats.Tests` uses hand-rolled fakes (no database). Mutation-check
  a new suite that passes first time.

## Documentation maintenance (MANDATORY, after each phase)
1. Refresh the skill-index table below and any changed conventions.
2. Add/update the `.claude/skills/<domain>/SKILL.md` for the domain that phase built
   (Phase 1 -> `entities`, `pipeline`; Phase 2 -> `career-site`; Phase 3 -> `integration`;
   Phase 4 -> `notifications`/`audit`).
3. Keep `docs/specs` and `docs/plans` current.

## Skill files — read BEFORE exploring code
| Domain | Skill file | Covers |
|--------|-----------|--------|
| Architecture | `.claude/skills/architecture/SKILL.md` | Solution layout, layering, DI, where code goes |
| Multi-tenancy | `.claude/skills/multitenancy/SKILL.md` | `TenantEntity`, query filter, interceptor, onboarding stamping |
| UI | `.claude/skills/ui/SKILL.md` | Layouts, design tokens, shared components, how to add a page |
| Entities | `.claude/skills/entities/SKILL.md` | Job/Candidate/JobApplication/Event, soft delete, ExternalRef |
| Pipeline | `.claude/skills/pipeline/SKILL.md` | Templates, stages, board moves, history, concurrency |
| Career site | `.claude/skills/career-site/SKILL.md` | Careers area, slug tenancy, IFileStore, public apply |
| Integration | `.claude/skills/integration/SKILL.md` | Feed, outbox, worker, ReferralTool client, settings |
| Audit | `.claude/skills/audit/SKILL.md` | Audit log, dashboard metrics, integration test tools |
