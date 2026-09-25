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

## 0. Prepare
1. Read the plan (or item) once. Extract each task's **full text**; subagents never read the plan file.
2. Note which domain skill each task needs: entities, pipeline, career-site, integration, audit, ui,
   multitenancy, architecture.
3. Create a todo per task. Record `git status --short` as the baseline so you can tell the task's changes
   apart from what was already dirty.

## 1. Implement (one task at a time, never in parallel)
Dispatch `ats-implementer` with:
```
Task N: <title>
<full task text, acceptance criteria and verification step, pasted verbatim>
Context: <where it fits, what earlier tasks changed, relevant files if known>
Domain skill to read: .claude/skills/<domain>/SKILL.md
```
Handle the status it returns:
- `NEEDS_CONTEXT`: answer from the codebase or plan if you can; otherwise ask the developer. Re-dispatch.
- `BLOCKED`: if it is an architectural decision, ask the developer. Do not force a retry with the same input.
- `DONE_WITH_CONCERNS`: read the concerns before review; ask the developer about any that change scope.

## 2. Collect the change
`git diff` + `git status --short` against the baseline gives the task's files. Classify them:
- **UI** if any `.cshtml`, `wwwroot/css`, `wwwroot/js`, controller, or `Areas/Careers` file changed.
- **Data** if any entity, configuration, repository, query, migration, middleware, `Ats.Api` or `Ats.Worker`
  file changed.

## 3. Review (parallel: one message, several Agent calls)
- `tenancy-guard`: always, when **Data** is true, or for any change under `src/` in review mode.
- `ats-conventions-reviewer`: always. Pass the full task text so it can check spec compliance.
- `e2e-verifier`: only when **UI** is true. Pass the changed file list.
- `pr-review-toolkit:silent-failure-hunter`: only when `Ats.Worker`, `Infrastructure/Integration` or any
  outbox/retry/HTTP-client code changed. Tell it to read `.claude/skills/integration/SKILL.md` first.

Give each reviewer the list of changed files and the task text; they read the diff themselves.

## 4. Fix loop (maximum 2 rounds)
Merge the findings. Drop any you can disprove by reading the code. Send the rest back to
`ats-implementer` as one list with `path:line`, then re-run **only** the reviewers that failed. After
2 rounds still failing: stop and hand the open findings to the developer.

## 5. Definition of done (you run these yourself; never accept a report as evidence)
- `dotnet build Ats.slnx`: 0 errors, no warnings the change introduced
- `dotnet test Ats.slnx`: green
- `dotnet format Ats.slnx --verify-no-changes`: clean
- `TENANCY: PASS` (when run), `CONVENTIONS: PASS`, `E2E: PASS` (when UI)
- Migration added: the file exists, and the `database update` command is in the close-out

Anything missing means the task is not done. Say so plainly; never soften a failure.

## 6. Close-out per task
- Tick the task's checkbox in the plan / review file.
- If the task finished a phase, do the `CLAUDE.md` documentation-maintenance step (skill index, domain skill).
- Print:
```
Task N: <title> - READY FOR DEVELOPER REVIEW
Files: <list>
Evidence: build <warnings>, tests <passed/total>, format clean, tenancy PASS, conventions PASS, e2e <PASS|n/a>
Agent runs: <count> (implementer <n>, reviews <n>), review rounds: <n>
Suggested commit: <type(scope): summary>
Manual commands: <migration apply / anything the guard blocked, or "none">
```

## After the last task
For a plan with 3 or more tasks, dispatch `superpowers:code-reviewer` once on the whole change (pass the
plan path and the list of changed files; there are no commit SHAs because we do not commit). Then give one
combined summary. **Stop there.** Do not commit, stage, stash, branch, merge or open a PR, and do not use
`superpowers:finishing-a-development-branch`. The developer takes it from here.

## Never
- Run two `ats-implementer` agents at the same time, or use `isolation: worktree` for implementers.
- Pass the plan file path to a subagent instead of the task text.
- Skip a review because the change "looks trivial".
- Retry or rephrase a command the guard hook blocked; list it under manual commands.
