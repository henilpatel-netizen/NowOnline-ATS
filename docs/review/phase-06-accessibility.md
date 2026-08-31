# Phase 6 — Accessibility (WCAG 2.1 AA)

The design system is tasteful but currently desktop-mouse-first. These items make it keyboard- and
screen-reader-operable and safe against tenant-chosen colours. Target: WCAG 2.1 AA.

Raises: Frontend, UI/UX. Verified against `c7f4614`.

> **Status (2026-08-27):** all six items implemented.
>
> **A11Y-3 fixed a live failure, not a hypothetical one.** Tenant Acme has accent `#9C8BDA` saved.
> With the previously hard-coded white button text that was **2.95:1** (AA needs 4.5:1) and the focus
> ring was also 2.95:1 against a white surface (needs 3:1, so effectively invisible). Text is now
> paired from WCAG luminance maths: **5.35:1**, and the ring falls back to dark ink at 15.79:1.
> `--ats-on-accent` and `--ats-focus-ring` are derived server-side by `BrandColor` and emitted by the
> Branding component; the editor shows the resulting ratio and warns below 4.5:1. Auto-adjust was
> chosen over rejecting colours, so no tenant's saved branding breaks.
>
> **Finding for the product owner:** the *default* NowOnline Sky Blue `#0085CA` reaches only
> **4.03:1** with white text. That satisfies AA for large/bold text and UI components (3:1) but not the
> 4.5:1 required for normal text, and button labels are 14px. Darkening it ~7% to `#007CBC` clears it
> (4.56:1). The code reports the shortfall rather than silently altering the brand palette, because
> changing the accent is a brand decision. Pinned by a test so it cannot regress unnoticed.
>
> **A11Y-4** is enforced by a tag helper on `span.ms`, so new markup gets `aria-hidden` automatically
> instead of relying on memory. An icon that carries meaning opts out by setting `role`/`aria-label`.
> The helper renders the `class` attribute properly rather than calling `ToString()` on it — on a
> validation-error postback MVC replaces that value with an `IHtmlContent` and `ToString()` would
> return the type name.
>
> **A11Y-5:** the Dutch career-site CTA is marked `lang="nl"` rather than translated — fixing the
> pronunciation defect without changing public-facing copy, which is a product decision (real
> bilingual support is FEAT-5).
>
> **A11Y-1** also removes the last `onclick="location.href"` navigation. Note the rows are real links
> but are **not** boosted: the NAV-1 config is scoped to the sidebar `nav.ats-nav`, and row links live
> in `#ats-content`. Clicking a row is still a full page load. Boosting in-content links would mean
> putting `hx-target`/`hx-select` on a container inside the content, which other htmx elements would
> then inherit — the exact trap NAV-1 hit. Worth doing as its own change, with the Candidates
> add-to-job form checked first.

---

### [x] A11Y-1 · Keyboard-operable list rows — Priority: High · Effort: M
**Files:** `Views/Jobs/Index.cshtml:51-52`, `Views/Candidates/Index.cshtml:29-30`
(`<div class="ats-trow--link" onclick="location.href=…">` — no `href`, `tabindex`, role, or key
handler).
**Problem:** The two primary list screens can't be reached or activated by keyboard/AT (WCAG 2.1.1).
**Fix:** Make the primary title cell a real `<a>` and stretch it over the row with the "stretched-link"
pattern (`::after` overlay); drop the div `onclick`; keep `event.stopPropagation()` on the actions
cell so its menu still works.
**Acceptance:** Rows are Tab-focusable, Enter-activatable, and announce as links.
**Verify:** Tab through Jobs/Candidates and open a row with Enter; a screen reader announces the link.

---

### [x] A11Y-2 · Candidate drawer: focus management + keyboard open — Priority: High · Effort: M
**Files:** `src/Ats.Web/wwwroot/js/site.js:43-62` (wrapper is `role="dialog" aria-modal="true"` but no
focus handling), `_CandidateDrawer.cshtml:14`; board open is a mouse-only `click` on a non-focusable
`<form>` (`Views/Board/Index.cshtml:96-102`).
**Problem:** Focus never enters the "modal", isn't trapped, and isn't restored on close; no
`aria-label`. Keyboard users can't open the drawer from the board at all (WCAG 2.4.3 / 4.1.2).
**Fix:** On `afterSwap`, move focus to the close button/first heading; trap Tab within the drawer;
remember `document.activeElement` before open and restore on close; add `aria-labelledby` → candidate
name. Make the card title a focusable `<button>`/`<a>` that fires the same `htmx.ajax`.
**Acceptance:** Drawer opens via keyboard, traps focus, labels itself, and restores focus on close.
**Verify:** Open/close the drawer keyboard-only with a screen reader; focus behaves as a modal.

---

### [x] A11Y-3 · Contrast-safe tenant accent — Priority: High · Effort: M
**Files:** `Views/Shared/Components/Branding/Default.cshtml:9-15`; consumed by `ats-tokens.css:141-154`
(`.btn-primary` forces white text) and `:283-286` (focus ring). `BrandColor.Normalize` validates hex
**format only**.
**Problem:** A light tenant accent (yellow/lime/pale) yields white-on-light text and an invisible focus
ring on the most prominent controls — tenant-driven WCAG 1.4.3 failures outside author control.
**Fix:** Compute the accent's relative luminance and emit a paired `--ats-on-accent` (black/white)
token used for button/chip text and the focus ring; optionally reject accents in the Branding editor
that can't reach 4.5:1 against both black and white.
**Acceptance:** Primary controls keep ≥4.5:1 text contrast for any saved accent; focus ring is visible.
**Verify:** Set accent to `#F5E663`; button text auto-flips to dark and stays readable; focus ring
visible.

---

### [x] A11Y-4 · Hide decorative icons from assistive tech — Priority: Medium · Effort: S
**Files:** pervasive `<span class="ms">icon_name</span>` (e.g. `_CandidateDrawer.cshtml:28`,
`Careers/.../Detail.cshtml:67`, `Dashboard/Index.cshtml:80,99`, `Jobs/Index.cshtml:31`).
**Problem:** The ligature text node is read by screen readers, polluting accessible names
("Submit application arrow_forward") — WCAG 1.1.1 / 4.1.2.
**Fix:** Add `aria-hidden="true"` to the `.ms` span by default (bake it into a small icon partial/tag
helper so it's automatic); use `role="img"` + label only where an icon carries standalone meaning.
**Acceptance:** Icon glyphs are not announced; labelled controls read their label only.
**Verify:** Screen-reader a labelled icon button; only the label is announced.

---

### [x] A11Y-5 · Honest search semantics + skip link + lang — Priority: Medium · Effort: S
**Files:** `TopBar/Default.cshtml:21` (`role="listbox"` with plain `<a>` children, no keyboard nav);
`_Layout.cshtml:22-46` (no skip link, long sidebar before `<main>`); `_Layout.cshtml`/
`_CareersLayout.cshtml` hardcode `lang="en"`; `Careers/.../Jobs/Index.cshtml:57` renders Dutch
"Bekijk vacature".
**Problem:** A listbox role with no options/keyboard nav is misleading (worse than none); no
skip-to-content; a Dutch string under `lang="en"` mispronounces (WCAG 2.4.1 / 3.1.1 / 4.1.2).
**Fix:** Drop the bogus `role="listbox"` (or implement the full combobox pattern later); add a
visually-hidden-until-focused "Skip to content" link + `id="main"` on `<main>`; fix the CTA to English
or wrap it `lang="nl"` (full i18n is FEAT-5).
**Acceptance:** Search panel has honest semantics; keyboard users can skip nav; no language mismatch.
**Verify:** Screen-reader the search panel and the skip link; the CTA pronunciation matches `lang`.

---

### [x] A11Y-6 · Form + control label audit — Priority: Low · Effort: S
**Files:** all forms (login/register, job/candidate/pipeline/department/location, careers apply,
branding, integration settings).
**Problem:** Confirm every input has a programmatic label, every icon-only button an `aria-label`, and
error summaries are associated (`aria-describedby`). Most are labelled, but no systematic pass has run.
**Fix:** Audit each form; add missing `aria-label`/`for`/`aria-describedby`; ensure the validation
summary is announced (`role="alert"`).
**Acceptance:** Automated a11y scan (axe) reports no label/name violations on any form.
**Verify:** Run axe DevTools on each screen; zero critical/serious violations.

---

## Exit criteria
- [ ] Lists and the drawer are fully keyboard/AT operable; focus is managed.
- [ ] Tenant accent can't break contrast; decorative icons are silent to AT.
- [ ] Skip link present; search semantics honest; no lang mismatch.
- [ ] axe scan clean on key screens; `dotnet build` clean, `dotnet test` green.

---

## Superseded by Phase 9

This phase was signed off without ever running a scanner. The first axe run (Phase 9) failed
**11 of 11 back-office screens** — contrast on the sidebar labels, breadcrumbs, eyebrows, table
heads, `.ats-kbd` and every link, plus a critical unlabelled-input violation on the pipeline stage
grid. All are fixed and now enforced by `tests/e2e/a11y.spec.ts`.

See `docs/review/phase-09-e2e-verification.md`. The lesson worth keeping: manual contrast checks
covered only the tokens that were edited, and missed every token that was not.
