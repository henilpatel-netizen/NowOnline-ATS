# Phase 5 — Navigation & Frontend UX

The most visible quality wins: kill the full-page navigation blink, make the data tables responsive,
and give every async interaction a loading/error state. Accessibility gets its own phase (6).

Raises: UI/UX, Frontend. Verified against `c7f4614`.

> **Status (2026-08-27):** all six items done and live-verified.
>
> **NAV-1** uses the existing htmx dependency (no SPA framework). ReferralTool was checked as a
> reference but does plain full-page loads, so its *conventions* were adopted rather than an
> implementation: shared helpers on a namespace with a scope re-apply function (`Ats.*`, mirroring
> `RT.*`), shared libraries loaded once, progressive enhancement. Measured: **1 request per
> navigation, 0 CSS/font/script re-fetches** (previously every asset on every click).
>
> Three implementation traps, all found by live testing and all now guarded in the UI skill:
> 1. `hx-target`/`hx-select` are **inherited**. On `<body>` they silently broke the global search and
>    the candidate drawer (both filtered their own responses for `#ats-content`). The boost config is
>    therefore scoped to the sidebar `<nav>` container, which contains nav links only.
> 2. Page scripts must render **inside** the swapped region to re-run, but `<main>` parses before the
>    end of `<body>` — so the board's init ran before SortableJS existed. Shared libraries moved to
>    `<head>`; they are fingerprinted + immutable, so the blocking cost is first-load only.
> 3. htmx replays cached DOM on Back/Forward with no HTTP response, so the document title went stale.
>    The server now stamps `data-page-title` on `#ats-content`, covering swaps and history restores.
>
> Safety nets: any response that is not a back-office page (login redirect after sign-out or session
> expiry, error/status page, file download) and any non-2xx or network failure falls back to a real
> browser navigation, so a boosted click can never silently do nothing.
>
> **Not changed:** list rows still navigate via `onclick="location.href"`, so a row click is a full
> page load. Converting rows to real anchors is `A11Y-1`'s restructure — doing it here would mean
> editing the same markup twice.
>
> **UX-5:** colours are fully tokenised (verified: computed RGB values identical to the previous hex
> literals, zero inline colour styles left in views). A `prefers-color-scheme` dark palette was
> deliberately **not** built — the tokens now make it a swap-only change, but shipping a dark theme is
> a product decision, not a refactor.

---

### [x] NAV-1 · Boosted SPA-style navigation (fix the full-page blink) — Priority: High · Effort: S–M
**Files:** `src/Ats.Web/Views/Shared/_Layout.cshtml` (nav links are plain `<a>`; htmx loaded at `:54`
but no `hx-boost`); sidebar `Views/Shared/Components/SidebarNav/Default.cshtml`; topbar.
**Problem (confirmed live via network trace):** Every nav click is a full-document GET that
re-downloads/re-parses all CSS, re-runs jQuery/Bootstrap/htmx, re-evaluates fonts, and rebuilds the
whole DOM (sidebar, topbar) → visible flash + lost scroll/state. htmx is present but unused for
navigation.
**Fix (use the existing htmx dependency — do not introduce a SPA framework):**
- Add `hx-boost="true"` on the shell and target the content region only: give `<main class="ats-content">`
  an `id` and set `hx-target`/`hx-select` so boosted links swap just the content while sidebar/topbar/
  CSS/fonts persist; enable `hx-push-url` for real URLs/history.
- Keep full-page views intact (progressive enhancement: works no-JS and on hard refresh).
- Re-run any content-scoped JS after swap via `htmx:afterSettle` (the board already re-inits on
  `htmx:afterSwap` — follow that pattern).
- Add a thin top progress bar via `hx-indicator` (see UX-2).
- Pair with PERF-6 (immutable asset caching) so even non-boosted loads stop re-fetching.
**Acceptance:** Navigating between pages swaps only the content area — no flash, sidebar/topbar and
scroll persist, URL/back-forward work.
**Verify:** Network trace on a nav shows a single partial GET (no CSS/font re-requests); visually no
blink; browser Back/Forward work.

---

### [x] UX-1 · Responsive data tables (remove inline grid templates) — Priority: High · Effort: M
**Files:** `Views/Jobs/Index.cshtml:21,45,51`, `Views/Candidates/Index.cshtml:8,24,29`,
`Views/Integration/_DeliveryRows.cshtml:5,13,21`; base classes `ats-components.css:339-363`
(no media query), shell breakpoint `ats-shell.css:315-342` (doesn't touch tables).
**Problem:** Rows are CSS grids with `grid-template-columns` injected as an **inline style**, which
beats any stylesheet media query — so tables can't be made responsive in CSS as-is. On phone/tablet
the multi-column grid stays wide → overflow/clipping.
**Fix:** Move each screen's column template into a CSS class (e.g. `.ats-table--jobs`); add
`@media (max-width: 767.98px)` to collapse to a stacked/`1fr` layout or hide low-priority columns.
Never inject `grid-template-columns` inline.
**Acceptance:** Lists are readable and non-overflowing at 375px width.
**Verify:** Resize to mobile; Jobs/Candidates/Delivery-log rows stack cleanly, no horizontal scroll.

---

### [x] UX-2 · Loading / pending states on all htmx interactions — Priority: Medium · Effort: M
**Files:** global search `Views/Shared/Components/TopBar/Default.cshtml:12-21`; board move
`Views/Board/_Board.cshtml:29-32`; drawer open `Views/Board/Index.cshtml:99-100`; drawer host empty
during GET `ats-shell.css:288`. No `hx-indicator` exists anywhere.
**Problem:** Search, board moves, and drawer fetches swap with zero in-flight feedback; on a slow link
the UI looks frozen and users re-click → duplicate moves.
**Fix:** Add a shared `.htmx-request` spinner via `hx-indicator`; render a skeleton/"Loading…" into
`#ats-drawer-host` immediately on click before the GET resolves; disable the moved card during its
move request.
**Acceptance:** Every async action shows immediate progress feedback; no double-submits.
**Verify:** Throttle the network; each interaction shows a spinner/skeleton and can't be double-fired.

---

### [x] UX-3 · Board move failure feedback + revert — Priority: Medium · Effort: S
**Files:** `Views/Board/Index.cshtml:75-86` (SortableJS `onEnd` → `htmx.trigger`), `_Board.cshtml:31`
(`hx-target=#board-container`). Concurrency conflicts are handled server-side (re-render with
`Model.Error`, `_Board.cshtml:7-12`) — good — but a network/non-2xx error leaves the card in the
dropped column silently.
**Problem:** Silent divergence between what the user sees and the server state on a failed move.
**Fix:** Add `htmx:responseError`/`htmx:sendError` handlers that show a toast and re-fetch the board
(revert to server truth).
**Acceptance:** A failed move surfaces an error and the board reconciles.
**Verify:** Force a 500 on move; a toast appears and the card returns to its real column.

---

### [x] UX-4 · Empty states on every board column + consistent empties — Priority: Low · Effort: S
**Files:** `Views/Board/_Board.cshtml:65-68` (only the Hired column shows a drop hint).
**Problem:** Empty non-terminal columns render blank — reads as broken/loading.
**Fix:** Show a neutral "No candidates" placeholder in every empty column (reuse the `_EmptyState`
partial or a lighter inline variant).
**Acceptance:** No empty column is visually blank.
**Verify:** A stage with zero cards shows a placeholder.

---

### [x] UX-5 · Tokenise stray colours + add dark mode — Priority: Low · Effort: M
**Files:** raw hex in `ats-components.css:405-406,438-475` (board), `:561` timeline, `:202` dots;
inline hex in `Dashboard/Index.cshtml:92`, `Integration/Index.cshtml:24,33-35`. No
`prefers-color-scheme` support; dark surfaces hardcode `#fff`.
**Problem:** The values that must change for a dark theme / re-port are scattered literals, some inline
in views — undermining the "consume `--ats-*`, never hand-tune" rule.
**Fix:** Promote recurring board/dark-surface colours to `--ats-*` tokens; move inline dark-card text
colours into a `.ats-card-dark`-scoped rule; add a `prefers-color-scheme: dark` token set if dark mode
is on the roadmap.
**Acceptance:** No one-off hex in views; a dark palette can be enabled by swapping tokens only.
**Verify:** Grep views for inline hex → none; toggling the token set restyles consistently.

---

## Exit criteria
- [x] Navigation is boosted — 1 request/nav, 0 asset re-fetches, shell persists, history + titles work.
- [x] Tables are responsive down to 375px; no inline grid templates anywhere (verified: no horizontal
  overflow on Jobs/Candidates/Deliveries/Organisation/Dashboard at 375px; desktop layout unchanged).
- [x] Search, drawer and board moves all show progress; a moved card is disabled in flight; a failed
  move raises a toast and re-fetches the board (verified against a real htmx error path).
- [x] Every empty board column shows a placeholder (0 visually blank); colours flow through tokens.
- [x] `dotnet build` clean, `dotnet test` green.
