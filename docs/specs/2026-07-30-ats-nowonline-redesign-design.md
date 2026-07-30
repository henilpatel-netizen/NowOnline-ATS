# ATS - NowOnline Redesign (design)

Date: 2026-07-30
Status: approved
Source design: Claude Design project `ats-redesign-and-improvements`, file `ATS - Redesign.dc.html`
Design system: `nowonline-design-system-3bae6b8b-982a-464f-9759-1335719d4108`

## Goal

Replace the back-office and public career-site UI with the NowOnline-branded redesign, without
changing recruiting behaviour, tenancy isolation, or the ReferralTool integration. Backend work is
limited to what the new screens genuinely need: per-tenant branding, an application origin, a feed
pull timestamp, and read-model projections.

## Non-goals

- No change to the ReferralTool wire contract (`docs/integration/referraltool-contract.md` is frozen).
- No change to tenancy enforcement or the five documented filter-bypass spots.
- No notification store. The topbar bell derives from the needs-attention query.
- No JS framework, no Node build step. Server-rendered Razor, Bootstrap 5, LibMan, htmx, SortableJS.
- No new roles or permission rules.

## Source material

The handoff bundle contains the prototype plus its design system. Two files in it are informational
only and are not ported: `support.js` is the prototype's React runtime harness (`x-dc`, `sc-if`,
`DCLogic`), and `vendor/ats/site.css` is a verbatim snapshot of the current `wwwroot/css/site.css`.

The prototype exposes three white-label props, which drive the branding work below:
`accent` (colour), `sidebarTheme` (`dark` | `light`), `tenantName`.

## Design tokens

Ported from `_ds/.../colors_and_type.css`.

| Token | Value | Use |
|---|---|---|
| Oxford Blue | `#0C2340` | sidebar, dark panels, headings, primary text |
| Maastricht Blue | `#08182C` | deepest surface, gradient end |
| Sky Blue | `#0085CA` | primary CTA, accent, active nav rail |
| Sky Blue hover | `#128FCF` | CTA hover |
| Sky Blue soft | `#EBF5FB` | info chip background |
| Medium Aquamarine | `#69CAA7` | secondary accent, hired stage |
| Medium Aqua deep | `#54A185` | hired terminal |
| Charcoal | `#394656` | body copy on light |
| Roman Silver | `#88909A` | muted text, eyebrows, dividers |
| Platinum | `#E1E3E6` | hairlines, borders |
| Cultured | `#F5F6F7` | page background |
| Danger / Warning / Success / Info | `#EC003F` / `#E17100` / `#009966` / `#155DFC` | semantic |

Type: Urbanist (display, 700-800, tracking `-0.01em`), Lexend (body, weight 300 dominant, line-height
1.56), Sometype Mono (eyebrow/kicker, 700, sentence case ending in a colon). Radii ladder
4 / 8 / 12 / 16 / 24 / pill. Shadows are Oxford-Blue tinted, never neutral black:
`0 2px 6px rgba(12,35,64,.03)` for cards, `0 10px 40px rgba(12,35,64,.08)` for floating media,
`0 10px 40px rgba(12,35,64,.16)` for hover lift.

Icons: Material Symbols Outlined, weight 400, `currentColor`, self-hosted. This replaces Bootstrap
Icons everywhere. The design system is explicit that icon systems must not be mixed.

## Architecture

### Stylesheet layering

Five files in `wwwroot/css`, loaded in order by the layout that needs them. No build step; order is
controlled by the `<link>` sequence.

| File | Contents |
|---|---|
| `ats-tokens.css` | NowOnline custom properties, `--ats-*` semantic aliases, Bootstrap variable overrides |
| `ats-base.css` | `@font-face` declarations, typography, base element styles, `.ms` icon class |
| `ats-components.css` | card, stat tile, eyebrow, status pill, source chip, avatar, pipeline bar, table shell, kanban column and card, drawer, timeline, empty state, pager, filter pill group, toggle |
| `ats-shell.css` | back-office shell: sidebar, topbar, content area, drawer host |
| `ats-careers.css` | public career site: hero, blobs, outlined headline, role cards |

- `_Layout` loads tokens, base, components, shell.
- `_AuthLayout` loads tokens, base, components.
- `_CareersLayout` loads tokens, base, components, careers.

Bootstrap 5 is retained for grid, forms, validation display, collapse, modals, dismissible alerts and
the pager, with its CSS variables overridden in `ats-tokens.css` so `.btn`, `.card`, `.table`,
`.form-control`, `.badge` inherit the new look rather than being fought inline. No theme colours in
views; that rule from `.claude/skills/ui/SKILL.md` still holds.

### Vendored assets

| Path | Source | Notes |
|---|---|---|
| `wwwroot/lib/nowonline-fonts/` | handoff bundle `_ds/.../fonts/` | 5 variable TTFs: Urbanist, Urbanist Italic, Lexend, Sometype Mono, Sometype Mono Italic |
| `wwwroot/lib/material-symbols/` | LibMan, `material-symbols` package | outlined woff2 + css; added to `libman.json` |

`bootstrap-icons` is removed from `libman.json` and its vendored files deleted once no view
references `bi-*`. The inline SVG favicon in all three layouts changes from `#4f46e5` to `#0085CA`.

### Razor components

New and changed shared pieces:

| Path | Purpose |
|---|---|
| `ViewComponents/BrandingViewComponent.cs` + `Views/Shared/Components/Branding/Default.cshtml` | emits the resolved tenant branding as CSS custom properties on the shell root |
| `ViewComponents/TopBarViewComponent.cs` + `Views/Shared/Components/TopBar/Default.cshtml` | breadcrumb, global search, bell, contextual primary action |
| `ViewComponents/SidebarNavViewComponent.cs` (extended) | nav groups, per-item badge counts, tenant chip, user footer |
| `Views/Shared/_PageHead.cshtml` | replaces `_PageHeader`: eyebrow line, H1, right-aligned actions slot |
| `Views/Shared/Partials/_Avatar.cshtml` | initials avatar, colour chosen deterministically from the name |
| `Views/Shared/Partials/_StatTile.cshtml` | KPI tile: eyebrow, big number, unit, delta line |
| `Views/Shared/Partials/_StatusPill.cshtml` | dot + label pill for job status, delivery state, stage |
| `Views/Shared/Partials/_SourceChip.cshtml` | origin chip (Career site / Referral / Manual / Unknown) |
| `Views/Shared/Partials/_PipelineBar.cshtml` | segmented stage-distribution bar |
| `Views/Shared/Partials/_EmptyState.cshtml` | icon, headline, body, optional action |
| `Views/Shared/Partials/_Timeline.cshtml` | dotted vertical timeline used by the drawer and audit |

`_PageHead` stays driven by `ViewData`, so no existing view has to be touched to keep a working
header: `ViewData["Title"]` supplies the H1 and the browser title as today, and optional
`ViewData["Eyebrow"]` adds the mono kicker line above it. The design's trailing period on headings
("Jobs.", "Candidates.") is appended by the partial, not stored in `Title`, so the browser title stays
"Jobs - ATS". Pages needing header buttons define a `PageActions` section, which the partial renders
when present and omits when absent.

`_Avatar` maps a name hash onto the five avatar colour pairs the prototype uses
(`#EBF5FB`/`#00679E`, `#E8F6F0`/`#00734D`, `#F0ECFB`/`#5B3FBF`, `#FDF3E7`/`#A85400`,
`#EFF0F2`/`#5A6472`) so the same person is the same colour on every screen.

### Application and Infrastructure layers

New folders in `Ats.Application`, with matching implementation folders in `Ats.Infrastructure`:

| Folder | Contract |
|---|---|
| `Branding/` | `ITenantBrandingService`, `TenantBranding` record |
| `Search/` | `IGlobalSearchService`, `SearchResults` |
| `Organisation/` | `IOrganisationReadService`, `OrganisationOverview` |

Extended in place: `Dashboard/` (summary record and service), `Jobs/` (list projection),
`Candidates/` (list projection), `Applications/` (detail projection), `Auditing/` (filtered paging),
`Integration/` (health read model).

Layering is unchanged: controllers depend only on Application abstractions; EF lives in
Infrastructure; `Ats.Domain` gains only entity fields and one enum, no framework references.

## Backend changes

### Schema

One migration, `AddBrandingAndApplicationOrigin`. Seven additive columns across two tables. The
migration file is created by this work; **applying it is a manual developer action**.

| Table | Column | Type | Rationale |
|---|---|---|---|
| `TenantSettings` | `BrandAccentColor` | `nvarchar(9)` null | prototype `accent` prop |
| | `BrandSidebarTheme` | `int` null | prototype `sidebarTheme` prop (`SidebarTheme.Dark=0`, `Light=1`) |
| | `CareerHeroHeadline` | `nvarchar(160)` null | career hero first line |
| | `CareerHeroHeadlineOutlined` | `nvarchar(160)` null | stroke-outlined second line |
| | `CareerHeroIntro` | `nvarchar(600)` null | hero intro paragraph |
| | `FeedLastPulledAt` | `datetimeoffset` null | "feed pulled N min ago" on dashboard and integrations |
| `Applications` | `Origin` | `int not null default 0` | source chips, Source column, dashboard source split |

**Impact.** Every column is nullable or defaulted, so no backfill runs and no existing query changes
meaning. Branding resolves to NowOnline defaults when null, so existing tenants render identically
until someone sets a value.

`ApplicationOrigin` is a new enum in `Ats.Domain/Enums`: `Unknown = 0`, `CareerSite = 1`,
`Manual = 2`, `Referral = 3`. Existing rows get `Unknown` and the UI renders that as a neutral chip
rather than inventing a source. New applications are stamped at creation:

- public career-site apply -> `Referral` when a referral code is present on the request, else `CareerSite`
- board "Add candidate" and candidates "Add to job" -> `Manual`

`Origin` is presentation-only. It is never read by the outbox, the worker, the feed, or the
ReferralTool client, so a wrong or unknown value cannot affect integration behaviour.

`FeedLastPulledAt` is written from `Ats.Api`'s `FeedController` after `FeedApiKeyFilter` has resolved
the tenant, so the write happens under normal tenant filtering and adds no new bypass spot. It is
debounced to at most one write per minute and wrapped so a failure is logged and swallowed; the feed
response must never fail because of a telemetry write.

### Read models, no schema change

**`DashboardSummary`** keeps its five existing fields and gains:

| Field | Definition |
|---|---|
| `TimeToHireDays` | mean days from `JobApplication.AppliedAt` to the `ApplicationEvent` whose `ToStageId` is a stage with `TerminalOutcome = Hired`, over hires in the last 90 days. Null when there are no hires in the window. |
| `OfferAcceptanceRate` | hires divided by the number of applications that reached an offer-position stage (the last non-terminal stage of their pipeline), last 90 days. Null when the denominator is zero. |
| `SourceBreakdown` | application counts grouped by `Origin`, as percentages |
| `NeedsAttention` | ordered list of actionable items (see below) |
| `IntegrationHealth` | delivered / failed / pending counts for the last 24h, plus last attempt time and `FeedLastPulledAt` |
| `ActivityFeed` | most recent `AuditEntry` rows projected to icon, sentence, entity ref, time |

Nulls render as an em-dash placeholder with the eyebrow label intact, so a fresh tenant shows honest
empty tiles rather than zeros that imply measured performance.

`NeedsAttention` items, each with icon, tone, headline, subline and target URL:

1. applications idle in a non-terminal stage for more than 7 days (count, most-affected job)
2. outbox messages in `Failed` state (count, last attempt time) - links to the delivery log
3. jobs in `Draft` created more than 7 days ago (count or single title)

The topbar bell shows its dot when `NeedsAttention` is non-empty and its popover lists the same
items. No `Notification` entity is introduced.

**Jobs list projection** adds, per job: department and location names, `PublishedAt`, total
application count, active application count grouped by stage (for the mini bar and the
"3 applied · 2 screening" subline), and the first three applicant names for the avatar stack.

**Candidates list projection** adds, per candidate: `Origin` of the most recent application, the most
recent application's job title and current stage name, whether there is more than one application,
and last activity time (latest `ApplicationEvent.OccurredAt`, falling back to `AppliedAt`).

**Application detail projection** adds: days in the current stage (from the latest
`ApplicationEvent`, falling back to `AppliedAt`), origin label, referral code (`SourceCode`), the
delivery state of the most recent `OutboxMessage` for the application, the next stage in the
pipeline, and resume file name plus size. All reads; nothing is written.

**Audit** gains optional `q` (user, entity ref or summary), `action`, and `from`/`to` filters, and
returns `PagedResult<AuditEntry>` at page size 20 using the existing `_Pager` contract.
`AuditController.Index` gains optional parameters; its route and verb are unchanged.

**Organisation** returns departments and locations each with the count of non-deleted jobs
referencing them.

**Integration health** aggregates `OutboxMessage` by `Status` over the last 24 hours and reads the
most recent `WebhookDelivery` for last-attempt detail.

**Global search** matches job title and `ExternalRef`, candidate first/last name and email, and
`JobApplication.SourceCode` for referral codes. Results are capped at five per category. It runs
through the normal repositories, so the global query filter scopes it to the current tenant with no
special handling.

### `IFileStore`

One additive method, needed by the drawer's file card ("PDF · 248 KB"):

```csharp
public sealed record StoredFileInfo(long Length, string ContentType, string FileName);
Task<StoredFileInfo?> StatAsync(string key, CancellationToken ct = default);
```

Returns null for a missing or invalid key. `LocalFileStore` is the only implementation.

### Branding resolution

`ITenantBrandingService.GetAsync()` returns a `TenantBranding` record with accent, accent hover,
sidebar theme, tenant name and slug, and career hero copy, substituting NowOnline defaults for null
columns. The Infrastructure implementation caches per request (scoped) so the shell costs one query
per request at most. Accent hover is computed by lightening the accent, matching the design's
`#0085CA` -> `#128FCF` relationship, so a tenant only has to pick one colour.

The accent value is validated against `^#[0-9A-Fa-f]{6}$` on save and again before it is emitted,
because it is written into a `style` attribute. A value failing validation falls back to the default
rather than being emitted. This is the only place tenant-supplied data reaches CSS.

## Screens

Shell for every authenticated page: 252px sidebar in the tenant's sidebar theme with brand mark,
tenant chip, four nav groups (ungrouped Dashboard, then `Hiring:`, `Setup:`, `Admin:`) with live
counts on Jobs and Candidates and a danger dot on Integrations when deliveries are failing, and a
user footer with avatar, role and a sign-out menu. Above the content, a 60px white bar with
`Root · Leaf` breadcrumb, a global search field focused by `Ctrl`/`Cmd` + `K`, the bell, and a
per-screen primary action.

| Screen | Route | Content |
|---|---|---|
| Dashboard | `/Dashboard` | greeting with date eyebrow; four KPI tiles (open jobs, active applications, time to hire, offer acceptance); pipeline distribution bars with the source split beneath; needs-you list; dark ReferralTool health card; activity feed |
| Jobs | `/Jobs` | eyebrow status counts; search, status filter pills, department filter, sort; table with title + ref/department/location subline, status pill, pipeline mini-bar with stage subline, avatar stack with overflow, published date, row action menu; pill pager |
| Board | `/Board?jobId=` | back link, title with status pill, ref/department/location/type/pipeline meta row, "View public page" and "Add candidate"; four tabs; stats strip (in process, avg days in stage, from ReferralTool, oldest application); kanban with per-stage dot, count and menu, tinted terminal columns, dashed drop hint on empty Hired, cards showing avatar, email, source chip, days-in-stage chip (tone escalating past 7 days) and stage progress dots |
| Candidates | `/Candidates` | search, source and job filters, add action; table with avatar, two-line contact, source chip, latest job with stage dot, last activity; pager |
| Candidate drawer | `/Applications/Card/{id}` | 520px right panel: avatar, name, contact, "Move to `next`", download CV, reject; stage progress; application facts (job, applied, source, referral code, status pushed); CV file card with size; history timeline |
| Pipelines | `/Pipelines` | template cards with stage chips and usage count, active one outlined in accent; inline editor with name field and stage rows (drag handle, name, outcome badge, ReferralTool status, delete), add-stage, save/cancel, consequence note |
| Organisation | `/Organisation` | two cards, departments and locations, each row showing name (and city) plus job count and an edit action |
| Integrations | `/Integration` | dark health banner (connection state, customer id, feed pull age, 24h delivered/failed/pending, test connection); connection form with enable toggle, base URL, customer id, code parameter, masked token and key with replace; feed key card with state, published job count, last pull, regenerate; inline delivery log with All/Failed/Pending filter and failed rows tinted |
| Audit | `/Audit` | search, action filter, date range; icon timeline with actor sentence, entity subline and action pill; pager |
| Career site | `/CareerSite` (back office) | browser-frame preview of the public site, Branding action, open-live-site action |
| Branding | `/CareerSite/Branding` | accent colour, sidebar theme, career hero headline, outlined line, intro |
| Public career site | `/careers/{slug}` | Oxford Blue hero with two blurred blobs, eyebrow, two-line headline with the second line stroke-outlined, intro; department filter pills with open-position count; role cards with type/location/department chips and an arrow CTA; restyled detail page and apply form; restyled thank-you |

Board tabs resolve to real destinations: Pipeline is the board, Job details links to `Jobs/Edit`, All
applicants lists every application for the job including terminal ones, Activity is the audit log
filtered to that job's entity ref.

The drawer is loaded by htmx into a drawer host in the layout, from a partial that
`Applications/Details` also renders. The full page therefore remains a working deep link and a no-JS
fallback; both surfaces stay in sync because they share one partial.

Departments and Locations keep their controllers and all CRUD routes so existing links and posts
continue to work. Only their `Index` actions change, redirecting to `/Organisation`.

The back-office career-site controller is named `CareerSite`, not `Careers`, deliberately. The public
site uses attribute routing `[Route("careers/{slug}")]`, and literal route segments take precedence
over conventional `{controller}/{action}` ones, so a back-office `/Careers/Branding` would be matched
by the public route with `slug = "Branding"` and 404 as an unknown tenant. `/CareerSite/*` cannot
collide.

Sidebar counts and the bell come from one cached per-request read so adding them to every page does
not multiply queries.

### Data the design shows that cannot be backed

| Prototype element | Resolution |
|---|---|
| Candidate subline "Utrecht · 6 yrs .NET" | no such fields exist. Board cards show email; candidate rows show email and phone. No new columns are added for this. |
| Feed key shown as `rtk_live_••••7c41` | only the hash is stored, which the design itself notes. Renders as a configured/not-set state with a fixed mask containing no real characters. |
| "Avg. days in stage" per board | computed from `ApplicationEvent` timestamps across the job's active applications. |
| Tenant switcher chip in the sidebar | a user belongs to exactly one tenant, so the chip displays the tenant and is not a switcher. It keeps the design's layout without implying a capability that does not exist. |

## Preserved behaviour

Untouched by this work: `AtsDbContext` global query filter and `TenantSaveChangesInterceptor`; the
five documented filter-bypass spots; the frozen ReferralTool contract; outbox enqueue and the worker
delivery loop; feed key authentication; stage-move optimistic concurrency via `RowVersion`; the
resume upload size and type validation; global antiforgery validation; role gating on Integration and
Audit.

Every existing controller action keeps its route, HTTP verb and signature except:

- `AuditController.Index` gains optional filter and page parameters
- `DepartmentsController.Index` and `LocationsController.Index` redirect to `/Organisation`

## Security notes

- The only tenant-supplied value that reaches CSS is the accent colour; it is regex-validated on save
  and re-validated before emission, with a fallback to the default.
- Career hero copy is rendered with Razor's default HTML encoding. It is not treated as markup.
- Global search takes a single `q` parameter used in parameterised LINQ predicates; results are
  tenant-scoped by the existing global filter, capped per category, and expose no cross-tenant ids.
- The masked token and key fields keep the existing write-only pattern: a blank submission preserves
  the stored secret and nothing decrypted is ever rendered.
- The Branding screen is owner-gated, matching Integration and Audit.
- No new outbound requests, no CDN references, no inline event handlers beyond the existing
  confirm-on-submit pattern.

## Verification

The solution has no test project, so verification is:

1. `dotnet build` clean.
2. Migration file created and reviewed, then applied manually by a developer.
3. Manual walk of every screen listed above, signed in as Owner and as a non-Owner, confirming the
   role-gated entries are hidden and the pages 403.
4. Board drag-and-drop still moves stages and still surfaces the concurrency warning.
5. Public career site renders for a valid slug and 404s for an unknown or suspended one.
6. Feed endpoint still returns published vacancies for a valid key and 401s otherwise, and
   `FeedLastPulledAt` advances.
7. A stage move still enqueues an outbox message and the delivery log shows the attempt.

## Manual developer steps

```bash
dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext
```

`libman restore` to hydrate `wwwroot/lib` after `libman.json` changes.

Git operations (commit, push, merge) remain manual per `.claude/rules/restrictions.md`.

## Documentation to update after implementation

Per `CLAUDE.md`: refresh `.claude/skills/ui/SKILL.md` for the new token files, component partials and
shell; note the branding fields in `.claude/skills/multitenancy/SKILL.md` and the origin field in
`.claude/skills/entities/SKILL.md`; note `FeedLastPulledAt` in `.claude/skills/integration/SKILL.md`;
note the extended summary and audit filters in `.claude/skills/audit/SKILL.md`; add the implementation
plan under `docs/plans/`.
