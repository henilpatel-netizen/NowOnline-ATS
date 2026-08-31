# Phase 9 — End-to-end verification (Playwright)

Status: **done**. Build clean, 144 unit tests, 81 e2e tests, `dotnet format` clean.

Phases 1–8 were verified by reading code and clicking around. This phase replaced that with
measurement, and two things that had been reported as done turned out not to be.

## What was added

`tests/e2e/`, driven by `@playwright/test` with `@axe-core/playwright`. The runner starts
`Ats.Web` itself (or reuses a running instance) and signs in once, saving the session.

| Spec | Tests | What it proves |
|------|-------|----------------|
| `smoke.spec.ts` | 15 | Every route renders, one `<h1>`, a title, no console errors, no JS exceptions |
| `security.spec.ts` | 15 | Anonymous access refused on 10 routes; anonymous POST refused; unknown slug 404s; feed rejects missing and wrong keys |
| `nav-cost.spec.ts` | 4 | Counts document/xhr/asset requests per navigation |
| `a11y.spec.ts` | 12 | axe-core, WCAG 2.0/2.1 A + AA |
| `responsive.spec.ts` | 19 | No horizontal overflow at 375 / 768 / 1440; sidebar stacks on mobile |
| `journeys.spec.ts` | 5 | Job and candidate lifecycles, validation keeps typed input, duplicate email rejected |
| `boosted-nav.spec.ts` | 8 | Regression guard for NAV-2 inheritance: search, shell, Back/Forward, hx-confirm, toasts, page scripts, skip link |
| `career-site.spec.ts` | 7 | Public site renders, apply form, resume-less POST rejected, no shell leak, axe clean |

## Finding 1 — NAV-1 only ever covered the sidebar

Phase 5 boosted `<nav class="ats-nav">` only. Everything else full-reloaded: 71 in-content links,
20 POST forms, the pager and 4 filter groups.

Measured before:

| Surface | documents | assets | time |
|---|---|---|---|
| Sidebar | 0 | 0 | 54–71ms |
| In-content link | 1 | 17 | 612–862ms |

**NAV-2** moved the same boost contract onto `<main id="ats-content">`. After:

| Surface | documents | assets | time |
|---|---|---|---|
| Sidebar | 0 | 0 | 60–68ms |
| In-content link | 0 | 0 | 39–43ms |

Three things had to be handled for that to be safe:
- The board move button now sets `hx-select="unset" hx-select-oob="unset"`, because
  `hx-target`/`hx-select` are inherited. It is the only htmx element inside the content area; the
  global search lives in the top bar, outside it.
- Five destructive forms moved from `onsubmit="return confirm(...)"` to `hx-confirm`. A boosted
  submit is driven by htmx and never consults the native handler, so the native confirm would have
  silently stopped gating delete/publish/close/regenerate-key.
- The "Open live site" link and the resume downloads are `hx-boost="false"`.

Remaining `document` requests in the table are `page.goto()` calls — a typed URL or hard refresh is
supposed to be a full load.

## Finding 2 — Phase 6 shipped with accessibility failures on every screen

The first axe run failed **11 of 11** back-office screens. Phase 6 claimed AA and had never been
scanned.

| Issue | Cause | Fix |
|---|---|---|
| `.ats-nav-label`, `.ats-nav-count` 3.23:1 | Light-theme sidebar emitted `#88909A` on white | Branding component now emits `#6B7280` (4.83:1) |
| Breadcrumbs, eyebrows, table heads, `.ats-muted` 2.98–3.22:1 | `--ats-ink-subtle` resolved to the brand `--no-roman-silver` `#88909A` | `--ats-ink-subtle: #667080`, `--ats-ink-faint: #6B7280`; the brand palette token is untouched and is no longer used for text |
| `.ats-kbd` 2.24:1 | `--ats-ink-faint` `#A6AEB8` | as above |
| Pipeline stage inputs unlabelled (**critical**) | A `<th>` column header is not a label | explicit `aria-label` per input |
| `.btn-link` / links 2.97:1 | Raw tenant accent used as text | new `--ats-accent-text` from `BrandColor.AccentText`, which darkens until 4.5:1 and preserves hue |
| Career-site `.careers-role-cta` | same | uses `--ats-accent-text` |

`BrandColor.AccentText` has 6 unit tests, including that a purple accent stays purple rather than
falling back to navy.

Note this supersedes the Phase 6 note about the default accent `#0085CA`: it still fails 4.5:1 as a
*fill* behind white button text (a product decision, unchanged), but it is no longer used as link
text — `--ats-accent-text` darkens it to `#0071AC` (4.9:1).

## Still not covered

- Board drag-and-drop moves and the stage-move concurrency conflict path
- Outbox delivery and the worker (needs a stub ReferralTool endpoint)
- Cross-tenant isolation as a live assertion (needs a second seeded tenant)
- Screen-reader behaviour; axe checks machine-detectable rules only
- Backend integration tests against real EF and the global query filter — everything in
  `tests/Ats.Tests` uses hand-rolled fakes

## Carried over, still open

- **SEC-1 (Critical)** — ReferralTool auth token and API key stored in plaintext. Highest-severity
  open item in the backlog; untouched by this phase.
- Tenant 1 has 26 pipeline stages with `IsTerminal = 0`, so applications never reach Hired/Rejected
  and hire metrics never register. Configuration, not code.
- Phase 2 remainder (SEC-2..9), Phase 7 (FEAT-1..7), PERF-4.
