import { type Browser, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { randomUUID } from 'node:crypto';
import { test, expect, acceptConfirm } from './confirm';
import { findUserUrl } from './users';

// A HiringManager sees only the jobs they are assigned to. Everything is created through the UI with
// run-unique names in the local dev database; jobs are deleted and the manager is deactivated at the end.
test.describe.configure({ mode: 'serial' });

// Same origin the config uses; afterAll cannot rely on test.info().
const baseURL = process.env.ATS_BASE_URL ?? 'https://localhost:7044';

type Candidate = { first: string; last: string; full: string; email: string };

// Rebuilt by the first test, so --repeat-each gets fresh names for every repetition.
let f: {
  temp: string;
  password: string;
  manager: { name: string; email: string };
  titles: { a: string; b: string; c: string };
  candidates: { a: Candidate; b: Candidate };
  jobA: number;
  jobB: number;
  candidateAId: number;
  candidateBId: number;
  appA: number;
  appB: number;
  stageB: { current: string; other: string; rowVersion: string };
  hm?: Page;
};

function newFixture() {
  const run = `${Date.now()}`;
  const candidate = (tag: string): Candidate => {
    const last = `Cand${tag}${run}`;
    return { first: 'HM', last, full: `HM ${last}`, email: `hm-cand-${tag.toLowerCase()}-${run}@example.test` };
  };
  return {
    temp: `Temp-${randomUUID()}-pass`,
    password: `New-${randomUUID()}-password`,
    manager: { name: `E2E Hiring Manager ${run}`, email: `hiring-manager-${run}@example.test` },
    titles: { a: `HM Job A ${run}`, b: `HM Job B ${run}`, c: `HM Job C ${run}` },
    candidates: { a: candidate('A'), b: candidate('B') },
  } as typeof f;
}

const chip = (page: Page, email: string) => page.locator('label.ats-team-chip', { hasText: email });
const jobRow = (page: Page, title: string) => page.locator('.ats-trow', { hasText: title });
const column = (page: Page, stageId: string) => page.locator(`.ats-board-col[data-stage-id="${stageId}"]`);
const boardCard = (page: Page, c: Candidate) => page.locator('form.ats-board-card', { hasText: c.full });

async function createJob(page: Page, title: string, managerEmails: string[]) {
  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(title);
  await page.locator('#Description').fill('Created by the hiring manager e2e spec.');
  await page.locator('#PipelineTemplateId').selectOption({ index: 1 });
  for (const email of managerEmails) {
    await chip(page, email).click();
    await expect(page.getByRole('checkbox', { name: email })).toBeChecked();
  }
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job created.')).toBeVisible();
}

async function publishJob(page: Page, title: string) {
  await page.goto(`/Jobs?q=${encodeURIComponent(title)}`);
  const row = jobRow(page, title);
  await row.getByRole('button', { name: 'Actions' }).click();
  await row.getByRole('button', { name: 'Publish' }).click();
  await acceptConfirm(page);
  await expect(page.getByText('Job published.')).toBeVisible();
}

async function deleteJob(page: Page, title: string) {
  await page.goto(`/Jobs?q=${encodeURIComponent(title)}`);
  const row = jobRow(page, title);
  if (!(await row.count())) return;
  await row.getByRole('button', { name: 'Actions' }).click();
  await row.getByRole('button', { name: 'Delete' }).click();
  await acceptConfirm(page);
  await expect(page.getByText('Job deleted.')).toBeVisible();
}

async function jobId(page: Page, title: string) {
  await page.goto(`/Jobs?q=${encodeURIComponent(title)}`);
  const href = await jobRow(page, title).locator('a.ats-row-link').getAttribute('href');
  const id = /jobId=(\d+)/.exec(href ?? '')?.[1];
  if (!id) throw new Error(`No job row for ${title}.`);
  return Number(id);
}

async function boardApplicationId(page: Page, c: Candidate) {
  const card = boardCard(page, c);
  await expect(card).toHaveCount(1);
  return Number(await card.locator('input[name="applicationId"]').inputValue());
}

// The public apply form is the only way to give an application a CV.
async function applyWithCv(browser: Browser, owner: Page, title: string, c: Candidate) {
  await owner.goto('/CareerSite');
  const href = await owner.getByRole('link', { name: /open live site/i }).first().getAttribute('href');
  const slug = /\/careers\/([^/?#]+)/.exec(href ?? '')?.[1];
  if (!slug) throw new Error(`Could not discover the career-site slug from "${href}".`);

  const ctx = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  try {
    const anon = await ctx.newPage();
    await anon.goto(`/careers/${slug}`);
    await anon.locator('a.careers-role-card', { hasText: title }).click();
    await anon.locator('#FirstName').fill(c.first);
    await anon.locator('#LastName').fill(c.last);
    await anon.locator('#Email').fill(c.email);
    await anon.locator('input[name="resume"]').setInputFiles({
      name: 'cv.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n'),
    });
    await anon.getByRole('button', { name: /Submit application/ }).click();
    await expect(anon).toHaveURL(/\/thank-you/);
  } finally {
    await ctx.close();
  }
}

async function candidateId(page: Page, c: Candidate) {
  await page.goto(`/Candidates?q=${encodeURIComponent(c.email)}`);
  const href = await page.locator('.ats-trow', { hasText: c.email }).locator('a.ats-row-link').getAttribute('href');
  const id = /\/Candidates\/Edit\/(\d+)/.exec(href ?? '')?.[1];
  if (!id) throw new Error(`No candidate row for ${c.email}.`);
  return Number(id);
}

async function setHiringTeam(page: Page, id: number, email: string, assigned: boolean) {
  await page.goto(`/Jobs/Edit/${id}`);
  const box = page.getByRole('checkbox', { name: email });
  if ((await box.isChecked()) !== assigned) await chip(page, email).click();
  if (assigned) await expect(box).toBeChecked();
  else await expect(box).not.toBeChecked();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job updated.')).toBeVisible();
}

async function signInAs(browser: Browser, email: string, password: string) {
  const ctx = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  const page = await ctx.newPage();
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  return page;
}

async function status(page: Page, url: string) {
  return (await page.goto(url))?.status();
}

test('owner creates a hiring manager, two published jobs and a CV applicant on each', async ({ page, browser }) => {
  test.setTimeout(180_000);
  f = newFixture();

  await page.goto('/Users/Create');
  await page.locator('#DisplayName').fill(f.manager.name);
  await page.locator('#Email').fill(f.manager.email);
  await page.locator('#Role').selectOption('HiringManager');
  await page.locator('#TemporaryPassword').fill(f.temp);
  await page.getByRole('button', { name: 'Add user' }).click();
  await expect(page.locator('.alert-success')).toBeVisible();

  await createJob(page, f.titles.a, []);
  await createJob(page, f.titles.b, []);
  await publishJob(page, f.titles.a);
  await publishJob(page, f.titles.b);
  f.jobA = await jobId(page, f.titles.a);
  f.jobB = await jobId(page, f.titles.b);

  await applyWithCv(browser, page, f.titles.a, f.candidates.a);
  await applyWithCv(browser, page, f.titles.b, f.candidates.b);
  await page.goto(`/Board?jobId=${f.jobA}`);
  f.appA = await boardApplicationId(page, f.candidates.a);
  await page.goto(`/Board?jobId=${f.jobB}`);
  f.appB = await boardApplicationId(page, f.candidates.b);
  // B's CV exists, so the manager's 404 on it later is scoping and not a missing file.
  expect((await page.request.get(`/Resume/Download?applicationId=${f.appB}`)).status()).toBe(200);
  f.candidateAId = await candidateId(page, f.candidates.a);
  f.candidateBId = await candidateId(page, f.candidates.b);

  // What a forged move for B needs: B's card values and a stage other than its current one.
  await page.goto(`/Board?jobId=${f.jobB}`);
  const card = boardCard(page, f.candidates.b);
  const current = await card.locator('xpath=ancestor::div[contains(@class,"ats-board-col")]').getAttribute('data-stage-id');
  const other = await page.locator('.ats-board-col').evaluateAll(
    (cols, cur) => cols.map((c) => c.getAttribute('data-stage-id')).find((id) => id !== cur),
    current,
  );
  f.stageB = { current: current!, other: other!, rowVersion: await card.locator('input[name="rowVersion"]').inputValue() };
  expect(f.stageB.other).toBeTruthy();
});

test('owner assigns the manager to job A on the job form', async ({ page }) => {
  await setHiringTeam(page, f.jobA, f.manager.email, true);

  await page.goto(`/Board?jobId=${f.jobA}`);
  await expect(page.getByRole('list', { name: 'Hiring team' })).toContainText(f.manager.name);
  await page.goto(`/Board?jobId=${f.jobB}`);
  await expect(page.getByText('No hiring team')).toBeVisible();
});

test('the manager sets their own password', async ({ browser }) => {
  f.hm = await signInAs(browser, f.manager.email, f.temp);
  await expect(f.hm).toHaveURL(/\/Profile\/ChangePassword/);
  await f.hm.locator('#CurrentPassword').fill(f.temp);
  await f.hm.locator('#NewPassword').fill(f.password);
  await f.hm.locator('#ConfirmPassword').fill(f.password);
  await f.hm.getByRole('button', { name: 'Change password' }).click();
  await expect(f.hm.locator('#ats-sidebar')).toBeVisible();
  await expect(f.hm).not.toHaveURL(/\/Profile\/ChangePassword/);
});

test('the manager sees only job A in the list and the sidebar count', async () => {
  const hm = f.hm!;
  await hm.goto('/Jobs');
  await expect(hm.locator('.ats-trow')).toHaveCount(1);
  await expect(jobRow(hm, f.titles.a)).toHaveCount(1);
  await expect(hm.locator('#ats-content')).not.toContainText(f.titles.b);
  await expect(hm.getByRole('link', { name: 'New job' })).toHaveCount(0);

  // Job B is also published, so a tenant-wide count would show 2.
  await expect(hm.locator('#ats-sidebar').getByRole('link', { name: /^Jobs\b/ }).locator('.ats-nav-count')).toHaveText('1');
});

test("the manager sees only job A's candidate", async () => {
  const hm = f.hm!;
  await hm.goto('/Candidates');
  await expect(hm.locator('.ats-trow')).toHaveCount(1);
  await expect(hm.locator('.ats-trow', { hasText: f.candidates.a.full })).toHaveCount(1);
  await expect(hm.locator('#ats-content')).not.toContainText(f.candidates.b.full);
  await expect(hm.locator('#ats-sidebar').getByRole('link', { name: /^Candidates\b/ }).locator('.ats-nav-count')).toHaveText('1');
});

test('global search finds job A but nothing of job B', async () => {
  const hm = f.hm!;
  const search = async (q: string) =>
    (await hm.request.get(`/Search?q=${encodeURIComponent(q)}`, { headers: { 'HX-Request': 'true' } })).text();

  // The searches for A prove the endpoint works, so the empty B results are not an empty page.
  expect(await search(f.titles.a)).toContain(f.titles.a);
  expect(await search(f.candidates.a.last)).toContain(f.candidates.a.full);

  for (const q of [f.titles.b, f.candidates.b.last, f.candidates.b.email]) {
    const html = await search(q);
    expect(html, q).toContain('No matches.');
    expect(html, q).not.toContain(f.titles.b);
    expect(html, q).not.toContain(f.candidates.b.last);
  }
});

test('the dashboard counts job A only and has no activity feed', async ({ page: owner }) => {
  const hm = f.hm!;
  await hm.goto('/Dashboard');
  const tile = (label: string) => hm.locator('.ats-stat', { hasText: label });
  await expect(tile('Open jobs:').locator('.ats-stat-value')).toHaveText('1');
  await expect(tile('Active applications:').locator('.ats-stat-value')).toHaveText('1');
  await expect(tile('Active applications:')).toContainText('1 candidates total');
  await expect(hm.getByText('Latest activity:')).toHaveCount(0);
  await expect(hm.locator('#ats-content')).not.toContainText(f.titles.b);
  await expect(hm.locator('#ats-content')).not.toContainText(f.candidates.b.last);

  // The feed is there for the Owner, so its absence above is the scoping and not a broken page.
  await owner.goto('/Dashboard');
  await expect(owner.getByText('Latest activity:')).toBeVisible();
});

test('board A works: drawer, then a stage move', async () => {
  const hm = f.hm!;
  const c = f.candidates.a;
  await hm.goto(`/Board?jobId=${f.jobA}`);
  await expect(hm.locator('#board-container')).toHaveAttribute('data-can-move', 'true');

  await boardCard(hm, c).locator('.ats-card-open').click();
  const drawer = hm.getByRole('dialog', { name: c.full });
  await expect(drawer).toBeVisible();
  await expect(drawer).toContainText(c.email);
  await expect(drawer.getByRole('link', { name: 'Download CV' })).toBeVisible();
  expect((await hm.request.get(`/Resume/Download?applicationId=${f.appA}`)).status()).toBe(200);
  await hm.keyboard.press('Escape');
  await expect(drawer).toBeHidden();

  const select = boardCard(hm, c).locator('.move-select');
  const target = await select.locator('option').nth(1).getAttribute('value');
  await select.selectOption(target!);
  await expect(column(hm, target!).locator('form.ats-board-card', { hasText: c.full })).toHaveCount(1);

  // The move is stored, not only painted.
  await hm.reload();
  await expect(column(hm, target!).locator('form.ats-board-card', { hasText: c.full })).toHaveCount(1);
});

test("everything of job A opens and everything of job B is 404 for the manager", async () => {
  const hm = f.hm!;
  for (const url of [
    `/Board?jobId=${f.jobA}`,
    `/Jobs/Edit/${f.jobA}`,
    `/Applications/Details/${f.appA}`,
    `/Applications/Card/${f.appA}`,
    `/Candidates/Edit/${f.candidateAId}`,
  ]) {
    expect(await status(hm, url), url).toBe(200);
  }

  for (const url of [
    `/Board?jobId=${f.jobB}`,
    `/Jobs/Edit/${f.jobB}`,
    `/Applications/Details/${f.appB}`,
    `/Applications/Card/${f.appB}`,
    `/Candidates/Edit/${f.candidateBId}`,
    // B's CV downloads for the Owner (first test), so this 404 is scope and not a missing file.
    `/Resume/Download?applicationId=${f.appB}`,
  ]) {
    expect(await status(hm, url), url).toBe(404);
  }
});

test("forged moves of job B's application are rejected and leave its stage alone", async ({ page: owner }) => {
  const hm = f.hm!;
  await hm.goto(`/Board?jobId=${f.jobA}`);
  const token = await hm.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const post = (jobId: number) =>
    hm.request.post('/Board/Move', {
      headers: { 'HX-Request': 'true' },
      maxRedirects: 0,
      form: {
        __RequestVerificationToken: token,
        jobId: `${jobId}`,
        applicationId: `${f.appB}`,
        toStageId: f.stageB.other,
        rowVersion: f.stageB.rowVersion,
      },
    });

  // Job B's own route: out of scope, so the board does not exist for this user.
  const forged = await post(f.jobB);
  expect(forged.status()).toBe(404);

  // Job A's route with B's application: MoveStageAsync fails with "Application not found." and A's
  // board is rendered with that error, so the answer is 200 and nothing of B is in it.
  const mismatched = await post(f.jobA);
  expect(mismatched.status()).toBe(200);
  const html = await mismatched.text();
  expect(html).toContain('alert-warning');
  expect(html).not.toContain(f.candidates.b.last);
  expect(html).not.toContain(f.titles.b);

  await owner.goto(`/Board?jobId=${f.jobB}`);
  await expect(column(owner, f.stageB.current).locator('form.ats-board-card', { hasText: f.candidates.b.full })).toHaveCount(1);
  await expect(column(owner, f.stageB.other).locator('form.ats-board-card', { hasText: f.candidates.b.full })).toHaveCount(0);
});

test('the owner sees the manager under Assigned jobs on the Users Edit page', async ({ page }) => {
  await page.goto(findUserUrl(f.manager.email));
  await page.locator('.ats-trow', { hasText: f.manager.email }).locator('a.ats-row-link').click();
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
  const assigned = page.getByRole('region', { name: 'Assigned jobs.' });
  await expect(assigned).toBeVisible();
  await expect(assigned.getByRole('link', { name: f.titles.a })).toBeVisible();
  await expect(assigned).not.toContainText(f.titles.b);
});

test('the job form with a hiring team chip selected has no axe violations', async ({ page }) => {
  await page.goto(`/Jobs/Edit/${f.jobA}`);
  await expect(page.getByRole('checkbox', { name: f.manager.email })).toBeChecked();
  await expect(page.getByRole('button', { name: 'Save' })).toBeVisible();

  const { violations } = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  if (violations.length) {
    console.log(
      violations
        .map((v) => `[${v.impact}] ${v.id}: ${v.help}\n` + v.nodes.slice(0, 3).map((n) => `    ${n.target.join(' ')}`).join('\n'))
        .join('\n'),
    );
  }
  expect(violations.map((v) => `${v.impact}:${v.id}`)).toEqual([]);
});

test('a deleted job disappears from the manager at once', async ({ page: owner }) => {
  const hm = f.hm!;
  await createJob(owner, f.titles.c, [f.manager.email]);
  const jobC = await jobId(owner, f.titles.c);

  await hm.goto(`/Jobs?q=${encodeURIComponent(f.titles.c)}`);
  await expect(jobRow(hm, f.titles.c)).toHaveCount(1);
  expect(await status(hm, `/Board?jobId=${jobC}`)).toBe(200);

  await deleteJob(owner, f.titles.c);

  await hm.goto(`/Jobs?q=${encodeURIComponent(f.titles.c)}`);
  await expect(jobRow(hm, f.titles.c)).toHaveCount(0);
  expect(await status(hm, `/Board?jobId=${jobC}`)).toBe(404);
  expect(await status(hm, `/Jobs/Edit/${jobC}`)).toBe(404);
  await hm.goto('/Jobs');
  await expect(hm.locator('.ats-trow')).toHaveCount(1);
});

test('removing the manager from job A empties their Jobs list on the next request', async ({ page: owner }) => {
  const hm = f.hm!;
  await setHiringTeam(owner, f.jobA, f.manager.email, false);

  await hm.goto('/Jobs');
  await expect(hm.locator('.ats-trow')).toHaveCount(0);
  await expect(hm.getByText('No jobs match.')).toBeVisible();
  expect(await status(hm, `/Board?jobId=${f.jobA}`)).toBe(404);

  await owner.goto(findUserUrl(f.manager.email));
  await owner.locator('.ats-trow', { hasText: f.manager.email }).locator('a.ats-row-link').click();
  await expect(owner.getByRole('region', { name: 'Assigned jobs.' })).toContainText('Not assigned to any jobs yet.');
});

async function openManagerEdit(page: Page) {
  await page.goto(findUserUrl(f.manager.email));
  await page.locator('.ats-trow', { hasText: f.manager.email }).locator('a.ats-row-link').click();
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
}

test('deactivation keeps the team link, shown as no access; a role change removes it', async ({ page: owner }) => {
  const teamChip = owner.getByRole('list', { name: 'Hiring team' }).locator('li', { hasText: f.manager.name });
  await setHiringTeam(owner, f.jobB, f.manager.email, true);

  await openManagerEdit(owner);
  await owner.getByRole('button', { name: 'Deactivate' }).click();
  await acceptConfirm(owner);
  await expect(owner.getByText(`${f.manager.name} deactivated and signed out.`)).toBeVisible();
  await owner.goto(`/Board?jobId=${f.jobB}`);
  await expect(teamChip).toContainText('No access');

  await openManagerEdit(owner);
  await owner.getByRole('button', { name: 'Reactivate' }).click();
  await acceptConfirm(owner);
  await expect(owner.getByText(`${f.manager.name} reactivated.`)).toBeVisible();
  await owner.goto(`/Board?jobId=${f.jobB}`);
  await expect(teamChip).toBeVisible();
  await expect(teamChip).not.toContainText('No access');

  await openManagerEdit(owner);
  await owner.locator('#Details_Role').selectOption('Viewer');
  await owner.getByRole('button', { name: 'Save changes' }).click();
  await expect(owner.locator('.alert-success')).toContainText('removed from 1 hiring team');

  await owner.goto(`/Jobs/Edit/${f.jobB}`);
  await expect(owner.locator('#ats-content')).not.toContainText(f.manager.email);
  await expect(owner.locator('#ats-content')).not.toContainText(f.manager.name);
});

test.afterAll(async ({ browser }) => {
  if (!f) return;
  await f.hm?.context().close();

  const ctx = await browser.newContext({ baseURL, ignoreHTTPSErrors: true, storageState: 'tests/e2e/.auth/user.json' });
  const page = await ctx.newPage();
  try {
    for (const title of [f.titles.a, f.titles.b, f.titles.c]) await deleteJob(page, title);

    await page.goto(findUserUrl(f.manager.email));
    const row = page.locator('.ats-trow', { hasText: f.manager.email });
    if (await row.count()) {
      await row.locator('a.ats-row-link').click();
      await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
      if (await page.getByRole('button', { name: 'Deactivate' }).count()) {
        await page.getByRole('button', { name: 'Deactivate' }).click();
        await acceptConfirm(page);
        await expect(page.getByText(`${f.manager.name} deactivated and signed out.`)).toBeVisible();
        await expect(page.getByRole('region', { name: 'User summary' }).getByText('Deactivated')).toBeVisible();
      }
    }
  } finally {
    await ctx.close();
  }
});
