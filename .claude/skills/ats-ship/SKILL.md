---
name: ats-ship
description: Run Ats implementation work through the project subagents - implement each plan task with ats-implementer, review in parallel with tenancy-guard, ats-conventions-reviewer and (for UI) e2e-verifier, gate on build/test/format evidence, and stop before git. Use for "/ats-ship <plan-file or review item ID>" or "/ats-ship review" to review the current diff.
argument-hint: <docs/plans/...md | docs/review item ID like PERF-3 | review>
---

# /ats-ship

Superpowers `subagent-driven-development`, adapted to Ats. `CLAUDE.md` and `.claude/rules/*.md` override
superpowers wherever they differ. You are the lead: you dispatch, judge and verify; you do not implement.

## Modes
- `/ats-ship <plan file>`: every unchecked task in that plan, in order.
- `/ats-ship <item ID>` (for example `A11Y-4`): that single item from `docs/review/phase-*.md`.
- `/ats-ship review`: skip to step 3 for the current `git diff` + untracked files (work written by a person).
- No argument, or no plan exists yet: write one first with `superpowers:writing-plans` into
  `docs/plans/YYYY-MM-DD-<topic>.md`, show it to the developer, and wait for approval.

## Test strategy (who runs what, and when)
The full Playwright suite takes minutes and grows with every plan, so it runs where it adds information,
never as a habit. Coverage is not reduced: every task is browser-verified, shared changes get the full
suite at once, and every plan ends with a full-suite gate.

| When | Who | Runs |
|---|---|---|
| During a task | `ats-implementer` | build, unit tests, format, plus the **targeted specs** you name in the dispatch (only when the task touches UI or a browser flow). Never the full suite. |
| After a task (UI true) | `e2e-verifier` | the targeted specs; **new or changed specs twice** (flakiness); an existing failing test re-run once |
| After a task that touched a **shared file** | `e2e-verifier` | the **full suite once**, in addition |
| After the last task (any plan) | `e2e-verifier` | the **full suite once, always** (plan gate, before the whole-change review) |

**Shared files:** `Views/Shared/**` (layouts, partials, `_Pager`, view components), `ViewComponents/**`,
`wwwroot/js/**`, `wwwroot/css/**`, `Program.cs`, `Web/Identity/**`, `Web/Middleware/**`, `Web/Tenancy/**`,
any `*ListQuery`/`Application/Common/**`, and authorization policies. A regression in these can break
screens the task never touched, so it is caught in the task that caused it.

**Picking targeted specs:** use the spec map in `.claude/agents/e2e-verifier.md`, plus every spec that
references a changed page, controller or selector (`grep -l` the route or class in `tests/e2e`). When in
doubt, include the spec; a few extra specs cost seconds, a missed one costs a late fix.

**Environment (saves blocked runs):** before Playwright, make sure nothing stale listens on port 7044
(the suite reuses a running server and would test old code). LocalDB: `sqllocaldb start MSSQLLocalDB`;
if it fails with "SQL Server process failed to start", an instance from another Windows session holds it:
`sqllocaldb stop MSSQLLocalDB -k`, stop any leftover `sqlservr.exe`, start again (developer-approved).
Stop LocalDB again when the plan is finished.

## Scope and pace
- **Model:** follow the rule in step 1. Do not upgrade a UI/docs/tests task to `opus` because the developer
  asked for polish; quality of UI work comes from the brief and the reviews, not the model tier.
- **New scope found mid-run** (polish ideas, unrelated bugs) goes into a follow-up task at the end of the
  plan, or a separate plan, unless it blocks the current task or is a security/tenancy defect. Tell the
  developer what you added and why.
- **Review findings:** blocking findings go through the fix loop (step 4). Minor, non-blocking findings
  are collected and fixed once in a "review follow-ups" task after the last task, not one round per task.

## 0. Prepare
1. Read the plan (or item) once. Extract each task's **full text**; subagents never read the plan file.
2. Note which domain skill each task needs: entities, pipeline, career-site, integration, audit, ui,
   multitenancy, architecture. Note the targeted specs each task needs and whether it touches a shared file.
3. Create a todo per task. Record `git status --short` as the baseline so you can tell the task's changes
   apart from what was already dirty.

## 1. Implement (one task at a time, never in parallel)
Dispatch `ats-implementer` with:
```
Task N: <title>
<full task text, acceptance criteria and verification step, pasted verbatim>
Context: <where it fits, what earlier tasks changed, relevant files if known>
Domain skill to read: .claude/skills/<domain>/SKILL.md
Verify: dotnet build/test/format; Playwright: <targeted spec files, or "none (no UI or browser flow)">.
Do not run the full Playwright suite; the e2e-verifier does that.
```
Model: the agent defaults to `sonnet`. Pass `model: "opus"` on the Agent call when the task touches
authentication or sessions, authorization policies, tenancy (query filter, interceptor, tenant context,
any `IgnoreQueryFilters`), entities/configurations/migrations, `Ats.Worker`, or outbox/retry/HTTP-client
code. When unsure, use `opus`. Keep the same model for that task's fix rounds.
Handle the status it returns:
- `NEEDS_CONTEXT`: answer from the codebase or plan if you can; otherwise ask the developer. Re-dispatch.
- `BLOCKED`: if it is an architectural decision, ask the developer. Do not force a retry with the same input.
- `DONE_WITH_CONCERNS`: read the concerns before review; ask the developer about any that change scope.

## 2. Collect the change
`git diff` + `git status --short` against the baseline gives the task's files. Classify them:
- **UI** if any `.cshtml`, `wwwroot/css`, `wwwroot/js`, controller, or `Areas/Careers` file changed.
- **Data** if any entity, configuration, repository, query, migration, middleware or `Ats.Worker`
  file changed.

## 3. Review (parallel: one message, several Agent calls)
- `tenancy-guard`: always, when **Data** is true, or for any change under `src/` in review mode.
- `ats-conventions-reviewer`: always. Pass the full task text so it can check spec compliance.
- `e2e-verifier`: when **UI** is true, or when any shared file changed. Pass the changed file list, the
  targeted specs, which specs are new or changed (run twice), and whether a shared file changed (then the
  full suite once as well).
- `pr-review-toolkit:silent-failure-hunter`: only when `Ats.Worker`, `Infrastructure/Integration` or any
  outbox/retry/HTTP-client code changed. Tell it to read `.claude/skills/integration/SKILL.md` first.

Give each reviewer the list of changed files and the task text; they read the diff themselves.

## 4. Fix loop (maximum 2 rounds)
Merge the findings. Drop any you can disprove by reading the code. Send the rest back to
`ats-implementer` (same model as the task's first run) as one list with `path:line`, then re-run **only** the
reviewers that failed. A fix round's Playwright check is the targeted specs for what the fix touched (plus the
full suite if the fix touched a shared file). Minor findings go to the end-of-plan follow-ups (see "Scope and
pace"). After 2 rounds still failing: stop and hand the open findings to the developer.

## 5. Definition of done (you run these yourself; never accept a report as evidence)
- `dotnet build Ats.slnx`: 0 errors, no warnings the change introduced
- `dotnet test Ats.slnx`: green
- `dotnet format Ats.slnx --verify-no-changes`: clean
- `TENANCY: PASS` (when run), `CONVENTIONS: PASS`, `E2E: PASS` (when UI or a shared file changed: targeted
  specs, plus the full suite for shared files)
- Migration added: the file exists, and the `database update` command is in the close-out

Anything missing means the task is not done. Say so plainly; never soften a failure.

## 6. Close-out per task
- Tick the task's checkbox in the plan / review file.
- If the task finished a phase, do the `CLAUDE.md` documentation-maintenance step (skill index, domain skill).
- Print:
```
Task N: <title> - READY FOR DEVELOPER REVIEW
Files: <list>
Evidence: build <warnings>, tests <passed/total>, format clean, tenancy PASS, conventions PASS, e2e <targeted n/n [+ full n/n] | n/a>
Agent runs: <count> (implementer <n>, reviews <n>), review rounds: <n>
Implementer model: <sonnet | opus> (<reason, e.g. "touches session validation">)
Suggested commit: <type(scope): summary>
Manual commands: <migration apply / anything the guard blocked, or "none">
```

## After the last task
1. **Review follow-ups:** if minor findings were collected, run them as one more task (steps 1-6).
2. **Full-suite gate (always, any plan size):** dispatch `e2e-verifier` for the full Playwright suite once.
   A failure: re-run that single test once to rule out flakiness; a real failure goes to `ats-implementer`
   with the likely causing task's context, then the full suite runs again. The plan is not done until it is green.
3. For a plan with 3 or more tasks, dispatch `superpowers:code-reviewer` once on the whole change (pass the
   plan path and the list of changed files; there are no commit SHAs because we do not commit). Blocking
   findings get one fix task (then targeted specs, plus the full suite if shared files changed).
4. Give one combined summary, including the full-suite result (`e2e full n/n`). **Stop there.** Do not commit, stage, stash, branch, merge or open a PR, and do not use
`superpowers:finishing-a-development-branch`. The developer takes it from here.

## Never
- Run two `ats-implementer` agents at the same time, or use `isolation: worktree` for implementers.
- Pass the plan file path to a subagent instead of the task text.
- Skip a review because the change "looks trivial".
- Retry or rephrase a command the guard hook blocked; list it under manual commands.
