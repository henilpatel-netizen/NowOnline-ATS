// PreToolUse guard for .claude/rules/restrictions.md. Catches the forms that permissions.deny
// cannot (docs: a deny rule "isn't a security boundary"): `git -C x push`, `git -c k=v commit`,
// `/usr/bin/git`, `& git.exe push` in PowerShell, `sh -c 'git push'`, EF options before `database`.
// Exit 2 blocks the call; stderr is shown to Claude so it can hand the command to the developer.
// Self-check: node .claude/hooks/guard-restricted.mjs --test

// Start of a command: line start, a shell separator, or the body of `-c '...'` / `-Command "..."`.
const START = String.raw`(?:^|[;&|(){}\n` + '`' + String.raw`]|(?:-c|-Command)\s+["'])\s*`;
const PREFIX = String.raw`(?:[A-Za-z_]\w*=\S*\s+)*(?:&\s*)?["']?(?:[\w.:~\\/-]*[\\/])?`;
const GIT = String.raw`git(?:\.exe)?["']?(?:\s+(?:-C\s+\S+|-c\s+\S+|--[\w-]+(?:=\S+)?|-[pP]))*\s+`;
const tool = (name) => START + PREFIX + name + String.raw`(?:\.exe)?["']?\s+`;
const ARGS = String.raw`[^;&|\n]*`;

const RULES = [
  ['git write/history', START + PREFIX + GIT + String.raw`(?:commit|push|pull|fetch|merge|rebase|reset|revert|stash|tag|cherry-pick|am)\b`],
  ['git discard working tree', START + PREFIX + GIT + String.raw`(?:restore|clean|checkout\s+(?:\S+\s+)*(?:--|\.)(?:\s|$))`],
  ['git branch create/delete', START + PREFIX + GIT + String.raw`(?:branch\s+(?:-[dDmMcCf]\b|--(?:delete|move|copy|force)\b|[^-\s])|checkout\s+(?:-[bB]\b|--orphan\b)|switch\s+(?:-[cC]\b|--(?:create|force-create|orphan)\b))`],
  ['EF database', tool('dotnet') + String.raw`ef\b` + ARGS + String.raw`\b(?:database\s+(?:update|drop)|migrations\s+remove)\b`],
  ['dotnet publish', tool('dotnet') + String.raw`publish\b`],
  ['DevOps CLI', START + PREFIX + String.raw`(?:az|azd|docker|kubectl|helm|sqlcmd)(?:\.exe)?["']?(?:\s|$)`],
  ['raw SQL', String.raw`\bInvoke-Sqlcmd\b`],
  ['pipeline trigger / merge', tool('gh') + String.raw`(?:workflow\s+run|run\s+rerun|pr\s+merge)\b`],
].map(([name, src]) => [name, new RegExp(src, 'i')]);

export function check(command) {
  for (const [name, re] of RULES) if (re.test(command)) return name;
  return null;
}

if (process.argv.includes('--test')) {
  const blocked = [
    'git commit -m x', 'git -C . push origin main', 'git -c push.default=current push', 'cd src && git push',
    '/usr/bin/git reset --hard', '& git.exe commit -m x', "sh -c 'git push'", 'FOO=1 git stash',
    'git --no-pager fetch', 'git branch feature-x', 'git branch -D old', 'git checkout -b new',
    'git switch -c new', 'git tag v1', 'dotnet ef database update', 'dotnet ef --project src/Ats.Infrastructure database update',
    'dotnet ef migrations remove', 'dotnet publish -c Release', 'az login', 'docker compose up', 'kubectl get pods',
    'sqlcmd -S .', 'Invoke-Sqlcmd -Query "x"', 'gh workflow run ci.yml', 'gh pr merge 12', 'echo ok; git pull', 'echo "$(git push)"',
    'git checkout -- src/Ats.Web/Program.cs', 'git checkout .', 'git checkout HEAD -- x.cs', 'git restore x.cs',
    'git clean -fd',
  ];
  const allowed = [
    'git status', 'git diff --stat', 'git log --oneline -5', 'git show HEAD', 'git branch', 'git branch -a',
    'git branch --show-current', 'git -C . status', 'dotnet build Ats.slnx', 'dotnet test Ats.slnx',
    'dotnet format Ats.slnx --verify-no-changes', 'dotnet ef migrations add AddX --project src/Ats.Infrastructure',
    'dotnet ef migrations list', 'npx playwright test tests/e2e/smoke.spec.ts', 'grep -rn "commit" docs',
    'ls azure', 'echo dockerfile', 'gh pr view 12', 'git log --grep=push', 'git checkout nowonline_theme_redesign',
  ];
  const fails = [
    ...blocked.filter((c) => !check(c)).map((c) => `should block: ${c}`),
    ...allowed.filter((c) => check(c)).map((c) => `should allow: ${c} (matched ${check(c)})`),
  ];
  console.log(fails.length ? fails.join('\n') : `ok: ${blocked.length} blocked, ${allowed.length} allowed`);
  process.exit(fails.length ? 1 : 0);
}

let input = '';
for await (const chunk of process.stdin) input += chunk;
const command = JSON.parse(input || '{}').tool_input?.command ?? '';
const hit = check(command);
if (hit) {
  process.stderr.write(
    `Blocked by .claude/rules/restrictions.md (${hit}). This is a manual developer operation. ` +
      `Do not retry or rephrase it; give the developer this command to run themselves:\n${command}\n`,
  );
  process.exit(2);
}
