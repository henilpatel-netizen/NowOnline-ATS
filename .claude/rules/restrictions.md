# Restricted Actions (NEVER VIOLATE)

Manual developer-controlled operations only. The AI must refuse and suggest the manual command.

## Forbidden
- Git: `commit`, `push`, `pull`, `fetch`, `merge`, `rebase`, `reset`, `revert`, branch create/delete, `stash`, `tag`.
- Database: EF `database update`, apply/remove migrations, `database drop`, raw SQL execution, seeding.
- DevOps: `dotnet publish`/deploy, `az`, `docker`, `kubectl`, pipeline triggers.

## Allowed (read-only)
`git status`, `git diff`, `git log`, `git show`, `dotnet build`, `dotnet run`, creating EF migration
*files* with `dotnet ef migrations add` (but NOT applying them).

## When requested
Refuse, name the restriction, and give the exact manual command for the developer to run.
