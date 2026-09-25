# Claude Code Multi-Agent Setup for Ats

Date: 25 September 2026
Status: phases A-C done and verified, D3 done, E1/E2 trialled; D1/D2 manual; E3/E4 deferred (25 September 2026)
Scope: `.claude/` configuration, project `CLAUDE.md`, `.gitignore`. No application code changes.

## 1. Goal

Let one lead Claude session run Ats work through specialised subagents (implement, tenancy review,
convention review, e2e verification). It should do so safely, repeatably, and the same way for every
developer on the team. The restrictions in `.claude/rules/restrictions.md` must be enforced by the harness, not just
requested in prose.

## 2. Current state (verified 25 September 2026)

| Item | State | Gap |
|------|-------|-----|
| `CLAUDE.md` + 3 rules + 8 domain skills | Good, current | None |
| `.claude/settings.json` | Missing | Restrictions are prose only. A subagent can still run `git commit` or `dotnet ef database update`. |
| `.claude/agents/` | Missing | No Ats-aware agents; generic plugin agents know nothing about tenancy rules. |
| Hooks | None | No guard for bypass forms such as `git -C . commit`. |
| Plugins | Enabled in `~/.claude/settings.json` only | Teammates do not get superpowers / feature-dev / microsoft-docs. |
| `.claude/worktrees/` | Not gitignored | Worktree-isolated agents would show up as untracked files. |
| `appsettings.Development.json` | Tracked | No `.worktreeinclude` needed. |

## 3. Reference review and verdicts

| Source | What it is | Verdict for Ats |
|--------|-----------|-----------------|
| [obra/superpowers](https://github.com/obra/superpowers) (installed, v5.0.7) | Skills for brainstorm, plan, subagent-driven-development (implementer, then spec reviewer, then quality reviewer), TDD, verification-before-completion | **Adopt as the process backbone.** Override two steps that clash with our restrictions: implementers commit, and `finishing-a-development-branch` merges/opens PRs. Superpowers itself ranks `CLAUDE.md` above its skills, so an override section in `CLAUDE.md` is enough. |
| [kunchenguid/firstmate](https://github.com/kunchenguid/firstmate) | Lead agent plus "crewmates" in tmux panes and throwaway worktrees; opens PRs, can self-merge | **Skip the framework.** It needs tmux (poor fit for Windows), pushes and merges on its own (forbidden here), and has many moving parts. **Borrow 2 ideas:** a written definition of done, and "no `done` without evidence". |
| [anthropics/claude-code plugins](https://github.com/anthropics/claude-code/tree/main/plugins) | `feature-dev` (installed), `code-review` (parallel reviewers + confidence filter), `pr-review-toolkit` (silent-failure-hunter and others) | Keep `feature-dev`. Borrow confidence filtering for our reviewers. Trial `pr-review-toolkit` silent-failure-hunter on outbox/worker code. |
| [dotnet/skills](https://github.com/dotnet/skills) (Microsoft .NET team) | `dotnet-data` (EF Core), `dotnet-aspnetcore`, `dotnet-test` skills | **Trial** `dotnet-data` and `dotnet-aspnetcore`. Best official .NET source. |
| [wshobson/agents](https://github.com/wshobson/agents) | 90+ small plugins, model tiering, `dotnet-contribution` | Borrow model tiering only. Skip bulk install: its generic caching/Dapper advice conflicts with our no-cache and EF-only rules. |
| [VoltAgent/awesome-claude-code-subagents](https://github.com/VoltAgent/awesome-claude-code-subagents) | 160+ agent prompts incl. `csharp-developer`, `sql-pro` | Use as prompt inspiration only; our agents must carry our tenancy/layering rules. |
| [Aaronontheweb/dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) | EF Core patterns, concurrency skills | Optional, selective. |
| SuperClaude, Ruflo (claude-flow) | Heavy frameworks with their own commands, modes and swarms | **Skip.** Invasive, and they overlap what we already have. |
| Claude Code agent teams (experimental) | Lead + teammates, shared task list, messaging | **Defer.** Split panes do not work in Windows Terminal, no `/resume` for in-process teammates, experimental flag. Revisit when stable. |

## 4. Target design

### 4.1 Principles

1. **Parallel reads, sequential writes.** Explore/review agents fan out in parallel. Implementers run one
   at a time in the main checkout. Reason: we may not commit or merge, so parallel worktrees would produce
   diffs only a developer can reconcile by hand, and all worktrees share one SQL Server database.
2. **Enforce, do not ask.** Restrictions live in `permissions.deny` plus one `PreToolUse` guard hook.
3. **The developer owns git and the database.** The lead ends every task with a diff summary and a suggested
   commit message; it never commits.
4. **Reuse before adding.** Superpowers provides the loop, and existing domain skills provide the knowledge.
   New agents exist only where Ats rules add value that generic agents lack.
5. **Model tiering.** `opus` (Opus 5.5) for the lead, implementation and tenancy/security review;
   `sonnet` for convention review and e2e verification. `opus` is pinned rather than `inherit`, so an
   implementer stays on Opus even when a developer runs the lead session on a cheaper model.

### 4.2 Roles

```
                 Developer (commits, applies migrations, deploys)
                                   |
                        Lead session (main checkout)
     superpowers: brainstorming -> writing-plans -> subagent-driven-development
                                   |
      +--------------+-------------+--------------+-----------------+
      |              |             |              |                 |
   Explore /    ats-implementer  tenancy-guard  ats-conventions  e2e-verifier
 feature-dev    (opus, writes,   (opus, read-   -reviewer        (sonnet, runs
 code-explorer   1 at a time)     only)          (sonnet, r/o)    Playwright)
  (parallel)                      \_____ parallel after each task _____/
```

| Agent | File | Model | Tools | Preloaded skills | Job |
|-------|------|-------|-------|------------------|-----|
| `ats-implementer` | `.claude/agents/ats-implementer.md` | opus | Read, Edit, Write, Grep, Glob, Bash | `architecture`, `multitenancy` | Implement one plan task with TDD in `tests/Ats.Tests`, keep the build warning-clean, run `dotnet format`, report `DONE / DONE_WITH_CONCERNS / NEEDS_CONTEXT / BLOCKED` plus the files changed. Never commits. |
| `tenancy-guard` | `.claude/agents/tenancy-guard.md` | opus | Read, Grep, Glob, Bash (`git diff` only) | `multitenancy` | Review the diff for `IgnoreQueryFilters`, hand-set `TenantId`, `Items["TenantId"]` outside the documented spots; new entities missing `ITenantEntity`; raw SQL; cross-tenant IDs from user input; migrations that drop the tenant index. PASS/FAIL with `path:line`. Only report findings it is confident in. |
| `ats-conventions-reviewer` | `.claude/agents/ats-conventions-reviewer.md` | sonnet | Read, Grep, Glob, Bash (`git diff` only) | `architecture`, `ui` | Check layering, `async`, UTC and `<local-time>`, htmx rules (`hx-select="unset"`, `hx-confirm`), no inline `grid-template-columns`, no raw hex, transactions via `InTransactionAsync`, no server-side cache, `OperationResult` location, and that tests exist for business rules. |
| `e2e-verifier` | `.claude/agents/e2e-verifier.md` | sonnet | Read, Grep, Glob, Bash | `ui` | Map changed views/controllers to `tests/e2e/*.spec.ts`, run only those with `npx playwright test <spec>`, report pass/fail with the failure output. Never edits code. |

Existing agents stay in use: `Explore`, `feature-dev:code-explorer`, `feature-dev:code-architect`,
`superpowers:code-reviewer` (final whole-branch review).

### 4.3 Orchestration skill: `/ats-ship`

One project skill, `.claude/skills/ats-ship/SKILL.md`, turns the superpowers loop into Ats specifics so
every developer gets the same pipeline:

1. **Input:** a plan file in `docs/plans/` (write it with `superpowers:writing-plans` if none exists) or a
   `docs/review/phase-*.md` item ID.
2. **Per task (sequential):** dispatch `ats-implementer` with the full task text and the domain skill to read
   (entities / pipeline / career-site / integration / audit / ui).
3. **Review (parallel, one message):** `tenancy-guard` + `ats-conventions-reviewer`, plus `e2e-verifier`
   when `.cshtml`, `.css`, `.js` or controllers changed. Findings go back to the same implementer, then the
   reviews run again. Maximum 2 rounds, then escalate to the developer.
4. **Definition of done (evidence required, borrowed from firstmate):**
   - `dotnet build Ats.slnx` with 0 new warnings
   - `dotnet test Ats.slnx` green
   - `dotnet format Ats.slnx --verify-no-changes` clean
   - relevant Playwright specs green (if UI touched)
   - tenancy-guard PASS
   - if a migration was added: the file exists, and the `database update` command is printed for the developer
5. **Close-out:** tick the plan checkbox, run the docs-maintenance step from `CLAUDE.md`, and print the
   changed files, a suggested Conventional Commit message and any manual commands. **Do not commit.**

A second mode, `/ats-ship review`, runs only step 3 over the current `git diff`, for review of work written
by a person.

### 4.4 Enforcement: `.claude/settings.json` (committed)

```json
{
  "permissions": {
    "deny": [
      "Bash(git commit *)", "Bash(git push *)", "Bash(git pull *)", "Bash(git fetch *)",
      "Bash(git merge *)", "Bash(git rebase *)", "Bash(git reset *)", "Bash(git revert *)",
      "Bash(git stash *)", "Bash(git tag *)", "Bash(git branch -d *)", "Bash(git branch -D *)",
      "Bash(git checkout -b *)", "Bash(git switch -c *)",
      "Bash(dotnet ef database *)", "Bash(dotnet ef migrations remove *)",
      "Bash(dotnet publish *)", "Bash(az *)", "Bash(docker *)", "Bash(kubectl *)",
      "Bash(sqlcmd *)", "Bash(gh workflow run *)", "Bash(gh pr merge *)"
    ],
    "allow": [
      "Bash(dotnet build *)", "Bash(dotnet test *)", "Bash(dotnet format *)",
      "Bash(dotnet ef migrations add *)", "Bash(npx playwright test *)",
      "Bash(git status *)", "Bash(git diff *)", "Bash(git log *)", "Bash(git show *)"
    ]
  },
  "hooks": {
    "PreToolUse": [
      { "matcher": "Bash|PowerShell",
        "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/guard-restricted.mjs\"" }] }
    ]
  },
  "worktree": { "baseRef": "head" },
  "extraKnownMarketplaces": {
    "claude-plugins-official": { "source": { "source": "github", "repo": "anthropics/claude-plugins-official" } }
  },
  "enabledPlugins": {
    "superpowers@claude-plugins-official": true,
    "feature-dev@claude-plugins-official": true,
    "microsoft-docs@claude-plugins-official": true,
    "claude-md-management@claude-plugins-official": true
  }
}
```

Notes:
- `deny` and `ask` apply even before a developer trusts the folder; `allow` waits for trust.
- Personal plugins (caveman, ponytail, firecrawl, frontend-design) stay in each user's own settings.
- `worktree.baseRef: head` matters: `isolation: worktree` otherwise branches from `master`, not the current
  feature branch.

### 4.5 Guard hook: `.claude/hooks/guard-restricted.mjs`

Deny patterns match by prefix, so they miss forms like `git -C src commit`, `cd x; git push`,
PowerShell `& dotnet ef database update`, or `Invoke-Sqlcmd`. A small Node script (Node 24 is already
required for Playwright) reads the hook JSON from stdin and regex-checks `tool_input.command` against the
same list, including `git\s+(-C\s+\S+\s+)?(commit|push|...)`. On a match it exits with code 2 and prints
the manual command to stderr, so Claude relays it to the developer. About 40 lines, with an inline self-check
table run by `node guard-restricted.mjs --test`.

### 4.6 `CLAUDE.md` additions (short)

```
## Multi-agent workflow
Use `/ats-ship <plan>` for implementation work. Process: superpowers subagent-driven-development,
with these overrides (they take precedence over superpowers skills):
- Implementers never commit; they leave a working-tree diff. The lead prints a suggested commit message.
- Do not use `finishing-a-development-branch` merge/PR options; stop at "ready for developer review".
- Implementers run sequentially in the main checkout. Parallelise only read-only agents.
Agents: `.claude/agents/` (ats-implementer, tenancy-guard, ats-conventions-reviewer, e2e-verifier).
```

Plus a new row in the skill-index table for `ats-ship`.

## 5. Implementation phases

Each phase ends with a check the developer can run. Nothing here touches application code.

### Phase A: Enforcement (highest value, about 1 hour)
- [x] A1 Create `.claude/settings.json` (section 4.4). Deny rules duplicated for the `PowerShell` tool.
- [x] A2 Create `.claude/hooks/guard-restricted.mjs` with a `--test` self-check (section 4.5). Hook uses
      exec form (`"command": "node", "args": [...]`) so it runs the same under Git Bash and PowerShell.
- [x] A3 Add `.claude/worktrees/`, `.claude/agent-memory/`, `.claude/settings.local.json` to `.gitignore`.
- [x] A4 Verified 25 September 2026: self-check 32 blocked / 20 allowed; three hook mutations killed; live
      in-session `git -C . commit`, `git tag` and PowerShell `& git.exe -C . commit` all blocked. Rule syntax
      confirmed in the docs: `Bash(git push *)` (`:*` is an equivalent legacy suffix). Pilot finding: an
      implementer ran `git checkout --`; discard commands (`restore`, `clean`, `checkout -- <path>`,
      `checkout .`) were added to the rule file, deny list and hook.

### Phase B: Agents
- [x] B1 Write the 4 agent files (section 4.2).
- [x] B2 `tenancy-guard` references `.claude/rules/multi-tenancy.md` as the single source.
- [x] B3 Seeded-defect check: `IgnoreQueryFilters()` in `JobRepository.GetAsync` -> FAIL with full leak path
      (both emulated and real agent dispatch); clean control diff -> PASS (no false positive); inline hex +
      `DateTime.Now` in a view -> FAIL on both.
- [x] B4 `claude plugin validate` only accepts plugin manifests, so it cannot check project agents. Used
      `claude agents` instead: all 4 load with the expected models. Note: agents added mid-session only become
      dispatchable after the session reloads them.

### Phase C: Orchestration skill
- [x] C1 `.claude/skills/ats-ship/SKILL.md`.
- [x] C2 `CLAUDE.md` section, agent/model table and skill-index row.
- [x] C3 Pilot on SEC-6 (phase 8 had no open items). Result:
      - Implementer: DONE_WITH_CONCERNS, TDD + 2 mutation checks, 38 new tests, 182/182 green, 0 warnings.
      - tenancy-guard PASS, conventions PASS, 0 false positives, 1 review round, 0 fix loops.
      - e2e first BLOCKED (orphaned LocalDB `sqlservr.exe`); after freeing it, real `e2e-verifier` agent:
        PASS 30/30 (smoke + security specs), 41k tokens.
      - Lead done-gate re-run independently: build 0 warnings, 182/182, format clean.
      - Cost (emulated agents, which also re-read CLAUDE.md): implementer 103k tokens / 5.4 min;
        reviewers 71k + 95k + 77k. A real (non-emulated) tenancy-guard run took 23k, so real runs are cheaper.
      - Prompt tuning from the pilot: lead strips personal-plugin comment tags (`ponytail:`) that teammates
        will not recognise.

### Phase D: Team rollout
- [ ] D1 The developer commits the `.claude/` changes (manual).
- [ ] D2 Each developer trusts the folder once and installs any missing pinned plugin (README step 2).
- [x] D3 "Claude Code setup" section in `README.md`.

### Phase E: Trials (optional, time-boxed, one at a time)
- [x] E1 `dotnet/skills` installed 25 September 2026 at **local** scope (`.claude/settings.local.json`,
      gitignored) for the trial. Verdict:
      - `dotnet-data:optimizing-ef-core-queries`: **keep**. Useful, but it recommends
        `ExecuteUpdate/ExecuteDelete`, which skip the interceptor and hard-delete soft-deletable rows.
        Guardrail added to the architecture skill.
      - `dotnet-data:create-datadriven-aspnetcore`: **conflicts** (injects `DbContext` into endpoints/pages).
        Banned in `CLAUDE.md`; it ships in the same plugin, so it cannot be uninstalled on its own.
      - `dotnet-aspnetcore`: **low value** for Ats. `dotnet-webapi` respects existing controllers (fine for
        `Ats.Api`); OpenTelemetry is only relevant if observability work starts; the Blazor and minimal-API
        upload skills do not apply. Recommend uninstalling unless `Ats.Api` grows.
- [x] E2 `pr-review-toolkit` installed (local scope). `silent-failure-hunter` on the outbox path: 6 findings,
      3 verified by the lead against `OutboxProcessor.cs` (delivery log lost if the final save fails; any 4xx
      after a prior attempt recorded as Delivered; disabled settings burn attempts). **High value: keep**, and
      add it to `/ats-ship` review for `Ats.Worker` / integration changes once promoted to project scope.
- [x] E5 Promotion (25 September 2026): `pr-review-toolkit` and `dotnet-data` moved to project scope
      (`enabledPlugins` + `dotnet-agent-skills` marketplace in `.claude/settings.json`); `dotnet-aspnetcore`
      uninstalled. Incident: `claude plugin install --scope project` (CLI 2.1.105) rewrote `settings.json` and
      dropped the hook `args` and `worktree.baseRef`, silently disabling the guard. Restored by hand; the hook
      now uses shell form, which that CLI keeps; README warns against CLI edits of the file.
- [x] E6 First real `/ats-ship` run (phase 10, OUT-1..7), 25 September 2026: 5 tasks, about 30 agent runs;
      silent-failure-hunter found issues in 4 of 5 tasks, including two regressions introduced by earlier
      fixes; every finding was verified by the lead before a fix loop. Lesson: the hunter earns its place
      on integration code; tenancy-guard and conventions passed first time on every task.
- [ ] E3 A saved Workflow (`.claude/workflows/ats-review.js`) for a deterministic multi-dimension review
      fan-out, only if the `/ats-ship review` fan-out proves unreliable.
- [ ] E4 Agent teams: re-evaluate when they leave experimental status and support Windows Terminal.

## 6. Deliberately skipped

| Skipped | Why | Revisit when |
|---------|-----|--------------|
| Parallel implementers in worktrees | Cannot merge (restriction); one shared database; manual reconciliation cost | Git policy relaxes, or a task set is truly file-disjoint and large |
| firstmate / SuperClaude / Ruflo | Framework weight, tmux dependency, self-merge, overlap | Never, unless requirements change |
| Agent `memory:` fields | Adds churn under `.claude/agent-memory/`; skills already hold durable knowledge | Reviewers repeat the same false positives across sessions |
| `SubagentStop` build hook | Definition of done already demands build evidence | Implementers report `DONE` without running the build |
| Dedicated migration/security agents | `tenancy-guard` covers migrations; the built-in `/security-review` covers the rest | Pilot shows gaps |

## 7. Risks

- **Superpowers updates** may change the prompts we adapted. Mitigation: `ats-ship` carries its own copies
  of the templates; review them on a superpowers major version bump.
- **Reviewer noise** makes developers ignore findings. Mitigation: confidence threshold + `path:line`
  evidence required; C3 pilot measures false positives.
- **Token cost:** about 3-4 agent runs per task, 2 of them on Opus (implementer + tenancy-guard).
  Mitigation: `sonnet` for the other reviewers, e2e only when UI changed, maximum of 2 review rounds.
- **Out of scope, noted:** the organisation rule requiring Dutch and English translation keys has no
  implementation in Ats yet (no `.resx` / `IStringLocalizer`). Already tracked as FEAT-5 in
  `docs/review/phase-07-*.md`; it is not a Claude-setup item.
