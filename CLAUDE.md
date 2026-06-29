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
| `Ats.Web` | MVC back-office (`/manage`) + public career site (Areas, later phases). |
| `Ats.Api` | REST API: vacancy feed + integration endpoints. |
| `Ats.Worker` | Background host: outbox delivery, notifications. |

## Build / run
```
dotnet build
dotnet run --project src/Ats.Web
```

## Front-end
Server-rendered MVC + Bootstrap 5. Client libraries are managed by LibMan (`libman.json`); run
`libman restore` to populate `src/Ats.Web/wwwroot/lib`. UI conventions: `.claude/skills/ui/SKILL.md`.

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

## Conventions
- Auth behind `IIdentityService` (ASP.NET Core Identity impl); swappable later.
- No hardcoded secrets; config per environment, secrets outside source control.
- Code comments only when they say something the code/names cannot.

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
