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

---

# Phase 9b — Layout and UX audit

A diagnostic sweep (`tests/e2e/layout-audit.spec.ts`) measured clipped text, elements escaping
their parent, uneven sibling cards, underfilled content, undersized tap targets and off-scale
spacing across 15 routes at three viewports, backed by a screenshot sweep for what only eyes catch.

## Fixed

| # | Defect | Cause | Fix |
|---|---|---|---|
| 1 | ~240px dead space on the right of every page | `.ats-content > *` capped at 1400px but left-aligned | cap removed entirely; the content area shares the top bar's padding, so it fills the width, lines up with the breadcrumb and has matching gutters. Centring the cap was tried first and left the content 106px out of line with the breadcrumb at 1920px. Locked at 1280/1440/1920/2560px |
| 2 | Dashboard KPI tiles 108/138/108/108px | card did not fill its grid column | `height: 100%` for cards in `.row > [col]` |
| 3 | Organisation cards 120 vs 142px | as above, plus `align-items-start` | same rule; removed `align-items-start` |
| 4 | Integration cards 513 vs 284px | as above | same |
| 5 | Jobs list rendered `invitedforfirstinterview` | `.ToLowerInvariant()` on a tenant-authored stage name | removed; names render verbatim |
| 6 | Dashboard bar labels ran under the bar (57-61px) | fixed 5.75rem label column | 7.5rem + ellipsis + `title` |
| 8 | Audit log unusable at 375px (4763px tall, ~1 char per line) | inline fixed-width flex columns, no `min-width: 0` on the body | extracted to `.ats-audit-*` classes with a mobile stack; now 3505px and readable |
| 9 | Pipelines/Create overflowed the viewport by 81px | raw six-column `<table>` | wrapped in `.table-responsive` |
| — | **Breadcrumb never updated on a boosted navigation** | the top bar is outside `#ats-content` and was not in `hx-select-oob` | added `#ats-topbar` to the OOB list on both boosted containers |
| — | **No way back from a sub-page** | breadcrumbs were plain `<span>`s, controller-level only | sub-pages now emit a link back to their section; index pages emit none |
| — | breadcrumb link was a 30x20 target | bare text | `min-height: 24px` + padded hit area |

Findings went from 1130 to 284. Of the remainder, 810 were a false positive in the audit itself
(Material Symbols glyphs overflow their line box by 2px by design) and the heuristic now ignores
them; `uneven-cards` went 6 -> 1 and `escapes-parent` 27 -> 6.

## Deliberately not fixed

- **Jobs/Create "escapes parent by 12px"** — that is Bootstrap's intended negative row gutter,
  absorbed by the card's 22px padding. Not visible; "fixing" it would fight the grid.
- **Stage names like `InvitedForFirstInterview`** — `PipelineStage.Name` is tenant-authored data.
  Humanising it in a view would mangle legitimately-named stages. The tenant should rename these on
  the Pipelines page; the same applies to the 26 stages with `IsTerminal = 0`.
- **Off-scale spacing (180 occurrences of 22px/6px/9px)** and the remaining 23px row-link targets —
  real but cosmetic, and a wide CSS sweep for little visible gain.

## Still open

Unchanged from Phase 9: **SEC-1 (Critical)**, the terminal-stage configuration gap, Phase 2
remainder, Phase 7, PERF-4.
