# Restricted Actions (NEVER VIOLATE)

Manual developer-controlled operations only. The AI must refuse and suggest the manual command.

## Forbidden
- Git: `commit`, `push`, `pull`, `fetch`, `merge`, `rebase`, `reset`, `revert`, branch create/delete, `stash`, `tag`,
  `cherry-pick`, and anything that discards working-tree changes (`restore`, `clean`, `checkout -- <path>`,
  `checkout .`). To undo your own edit, edit the file back.
- Database: EF `database update`, apply/remove migrations, `database drop`, raw SQL execution, seeding.
- DevOps: `dotnet publish`/deploy, `az`, `docker`, `kubectl`, pipeline triggers.

## Allowed (read-only)
`git status`, `git diff`, `git log`, `git show`, `dotnet build`, `dotnet run`, creating EF migration
*files* with `dotnet ef migrations add` (but NOT applying them).

## Enforcement
`permissions.deny` in `.claude/settings.json` blocks the plain forms; `.claude/hooks/guard-restricted.mjs`
(PreToolUse, Bash + PowerShell) blocks the variants (`git -C`, full paths, `sh -c`, `& git.exe`). Keep the
three in sync when this list changes, and add a case to the hook's `--test` table.

## When requested
Refuse, name the restriction, and give the exact manual command for the developer to run.
