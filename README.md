# Ats

## Claude Code setup

The repo ships its Claude Code configuration in `.claude/` (see `CLAUDE.md` for the rules).

1. Open the folder in Claude Code once and accept the workspace trust prompt. The deny rules in
   `.claude/settings.json` apply immediately; the guard hook, allow rules and marketplace apply after trust.
2. The pinned plugins (`enabledPlugins` in `.claude/settings.json`) are superpowers, feature-dev,
   microsoft-docs, claude-md-management, pr-review-toolkit (Anthropic marketplace) and dotnet-data
   (`dotnet/skills` marketplace). If Claude Code reports one missing, install it for yourself with
   `claude plugin install <plugin>@<marketplace> --scope user`. **Do not use `--scope project`, and do not
   let the CLI edit `.claude/settings.json`:** older CLI versions (seen with 2.1.105) rewrite the file and
   silently drop fields they do not know, which disabled the guard hook. Add project plugins by hand, then
   re-run step 3. Keep the CLI current with `claude update`.
3. Check the guard: `node .claude/hooks/guard-restricted.mjs --test` (requires Node, already needed for Playwright).
4. Use `/ats-ship <plan file | review item ID>` to implement and `/ats-ship review` to review a diff.

Claude never commits, pushes, applies migrations or deploys; the harness blocks those and prints the
command for you to run. Personal plugins belong in your own `~/.claude/settings.json`, not in the repo.
