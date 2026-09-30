# Themed confirm dialogs and native-UI clean-up

> Run with `/ats-ship docs/plans/2026-09-29-themed-dialogs-and-native-ui.md`. Implementers never commit.

**Goal:** no browser-native or default-Bootstrap UI left where the NowOnline design system should apply.

**Library decision:** no new dependency. Bootstrap 5 modal (already loaded) + htmx's documented
`htmx:confirm` hook (`evt.preventDefault()` then `evt.detail.issueRequest(true)`). SweetAlert2 was
considered and rejected (extra ~16 KB, second modal system, own theme to override).

## Task 1: One themed confirm modal for every `hx-confirm`
- [x] A single Bootstrap modal in `_Layout.cshtml` (outside `#ats-content`, so boosted swaps never remove
  it), styled with design tokens only: title, message, Cancel (secondary) and confirm button.
- [x] `site.js`: handle `htmx:confirm` for elements that carry `hx-confirm`: prevent the native dialog,
  fill the modal, and on confirm call `evt.detail.issueRequest(true)`; on cancel/Escape/backdrop do
  nothing. Only one pending request at a time. Focus goes to the safe (Cancel) button for destructive
  actions and returns to the triggering control on close.
- [x] Optional per-form attributes: `data-confirm-title`, `data-confirm-ok` (button text, e.g. "Delete",
  "Publish") and `data-confirm-variant="danger"` (danger-styled confirm). Apply them to all 11 existing
  `hx-confirm` forms (Delete/Remove = danger; Publish/Close/Sync = primary) with clear button labels.
- [x] Works with the submit-button disabling in `site.js` (button disables only once the request goes out,
  never on cancel) and inside the candidate drawer.
- [x] Playwright: no native `dialog` event fires anywhere; the modal appears with the right text; Cancel
  leaves the page unchanged; Confirm completes the action. Update every existing spec that accepted a
  native dialog (`page.on('dialog')`) to use the modal instead.
- [x] a11y spec covers the open modal (role dialog, labelled, focus trapped).

## Task 2: Theme dropdown menus
- [x] Map `--bs-dropdown-*` variables to tokens (surface, border, radius, shadow, item padding, hover and
  active/focus background, text) in `ats-tokens.css`; danger items keep readable contrast on hover and
  keyboard focus (WCAG AA). No Bootstrap blue anywhere.
- [x] Playwright/axe check on an open Jobs row menu with the Delete item focused.

## Task 3: Themed form validation (no native bubbles)
- [x] Replace the browser's native validation bubble with inline, themed messages: keep HTML5 constraints
  (`required`, `type=email`, `accept` ...), but a small shared script listens for `invalid`, prevents the
  bubble and shows the message under the field (`.invalid-feedback` style from tokens, `aria-invalid`,
  `aria-describedby`), clearing it on input. Server-side ModelState messages keep working.
- [x] Must also run on the public career site (its layout does not load `site.js`; load a small shared
  script there too, without the back-office drawer/boost code).
- [x] Playwright: submit an empty required form on login, a back-office form and the career-site apply
  form; no native bubble, themed message visible, focus on the first invalid field.

## Task 4: Map Bootstrap status colours and native controls to tokens
- [x] Map `--bs-danger`, `--bs-danger-rgb`, `--bs-success`, `--bs-warning`, `--bs-info` (and their
  `-text-emphasis`, `-bg-subtle`, `-border-subtle` variants) and `--bs-form-invalid-*` /
  `--bs-form-valid-*` to NowOnline tokens, so `text-danger`, `alert-*` and invalid inputs use the design
  system colours.
- [x] Theme the file input's `::file-selector-button` (career-site CV upload).
- [x] Branding colour input: replace the inline `style` with a class.
- [x] Sweep views for any remaining inline `style` colours or Bootstrap default colours and move them to
  tokens/classes. Screenshots spec stays green.

## Done
Build 0 warnings, tests green, format clean, conventions PASS, e2e PASS (full suite), a11y green. Update
the UI skill (confirm modal usage and attributes, validation script, dropdown theming).
