# ATS — Production-Readiness Improvement Program

A phased backlog to take the ATS from a solid multi-tenant MVP to a **10/10, production-hardened
product** for hundreds of tenants and a 10-year maintenance horizon.

Every item in these files was **verified against the current codebase** (commit `c7f4614`) — file and
line references are real, not assumed. Findings come from a five-dimension architecture/security/
performance/frontend/backend review plus live browser + database testing, and the set was extended
with additional gaps confirmed by direct inspection (password reset, user management, role
enforcement, DataProtection, timezone handling, health checks, CI/tooling, and more).

## How to use this

- Work **phase by phase, top to bottom**. Phases are ordered by dependency and risk: foundations and
  security first, then data/perf, then UX/accessibility, then features, then quality.
- Each phase file is **self-contained and executable** — treat it as an implementation plan. Items
  have a stable ID, evidence (`path:line`), a concrete fix, acceptance criteria, and a verification
  step.
- Tick the checkbox on each item as it lands. Each phase ends with an "Exit criteria" gate.

## Phase map

| Phase | File | Theme | Raises (modules) |
|------:|------|-------|------------------|
| 1 | `phase-01-foundations-and-tooling.md` | CI, analyzers, DataProtection, health, resiliency, API errors | Maintainability, Scalability, Backend |
| 2 | `phase-02-security-and-access-control.md` | Secrets-at-rest, authn hardening, role model, headers | Security |
| 3 | `phase-03-data-layer-and-correctness.md` | Indexes, idempotency, atomic writes, timezone, KPIs | Backend, Scalability |
| 4 | `phase-04-performance-and-scalability.md` | Caching, server-side aggregation, search, pooling, asset caching | Performance, Scalability |
| 5 | `phase-05-navigation-and-frontend-ux.md` | Boosted SPA nav (blink fix), responsive tables, loading states | UI/UX, Frontend |
| 6 | `phase-06-accessibility.md` | Keyboard nav, modal focus, contrast, icons, i18n, skip link | Frontend, UI/UX |
| 7 | `phase-07-feature-completeness-and-workflows.md` | Password reset, user/team mgmt, notifications, role-aware UI | Feature completeness |
| 8 | `phase-08-code-quality-consistency-tests.md` | Business-logic tests, dead code, pattern consistency, logging | Maintainability, Architecture |

## Module scorecard (baseline → target)

| Module | Baseline | Target | Primary phases |
|--------|:--------:|:------:|----------------|
| Architecture | 8.0 | 10 | 8 |
| Backend / API | 7.0 | 10 | 1, 3 |
| Frontend | 7.0 | 10 | 5, 6 |
| UI/UX | 6.5 | 10 | 5, 6 |
| Performance | 6.5 | 10 | 4, 3 |
| Security | 7.5 | 10 | 2, 1 |
| Maintainability | 7.0 | 10 | 1, 8 |
| Scalability | 6.0 | 10 | 4, 1, 3 |
| Feature completeness | — | 10 | 7 |
| **Overall** | **7.0** | **10** | all |

## Legend

- **Priority:** `Critical` (fix before onboarding real tenants) · `High` (before GA) · `Medium`
  (fast-follow) · `Low` (hardening/debt).
- **Effort:** `S` ≈ ≤half a day · `M` ≈ 1–3 days · `L` ≈ multi-day / cross-cutting.
- **ID prefixes:** `FND` foundations · `SEC` security · `DATA` data layer · `PERF` performance ·
  `NAV`/`UX` frontend · `A11Y` accessibility · `FEAT` features · `QUAL` code quality.

## Repo conventions (apply to every item)

- **Migrations are created but applied manually.** Generate the migration file; never run
  `dotnet ef database update` — hand the developer the exact command. See `.claude/rules/migrations.md`.
- **Git is manual.** Never commit/push/merge; land the change, run `dotnet build` + `dotnet test`,
  and stop. See `.claude/rules/restrictions.md`.
- **Do not break** tenancy isolation, the ReferralTool contract (`docs/integration/…`), the outbox, or
  the worker. Verify each item before claiming done — build clean, tests green, and the stated
  verification step observed.

## Definition of done for the program

All checkboxes ticked, every phase Exit-criteria met, `dotnet build` clean with
`TreatWarningsAsErrors=true`, `dotnet test` green with meaningful business-logic coverage, and a
signed-off walkthrough of the User, Admin, and Customer workflows described in
`phase-07-feature-completeness-and-workflows.md`.
