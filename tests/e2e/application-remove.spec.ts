import { type Locator, type Page } from '@playwright/test';
import { test, expect, acceptConfirm, dismissConfirm, type Dismissal } from './confirm';

/**
 * "Remove from job" soft-deletes one application: the candidate leaves that job's board but stays in
 * the candidate list, and can be added to the same job again. Each confirm is first dismissed, which
 * must leave the button usable and focused (site.js used to disable it before htmx showed the
 * hx-confirm), then confirmed on a second click.
 */
const RUN = `arem-${Date.now()}`;
const JOB = `${RUN} Job`;
const LAST = RUN;
const EMAIL = `${RUN}@example.com`;
const PROMPT = 'Remove this candidate from the job? Their history on this job is hidden.';

test.describe.configure({ mode: 'serial' });

// Each run creates at most a few candidates; more loop turns than this means the cleanup is stuck.
const MAX_CLEANUP = 10;

let jobId = '';
let templateId = '';
let boardUrl = '';

async function addToJob(page: Page) {
  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  const row = page.locator('.ats-trow').first();
  await row.locator('[data-bs-toggle="dropdown"]').click();
  const menu = row.locator('.dropdown-menu.show');
  await menu.locator('select').selectOption(jobId);
  await menu.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByText('Candidate added to job.')).toBeVisible();
}

function card(page: Page) {
  return page.locator('#ats-content .ats-board-card', { hasText: EMAIL });
}

async function removeWithDismissFirst(page: Page, scope: Locator, how: Dismissal) {
  const button = scope.getByRole('button', { name: 'Remove from job' });
  const before = page.url();

  await button.click();
  await dismissConfirm(page, PROMPT, how);
  await expect(button).toBeEnabled();
  await expect(button).toBeFocused();
  expect(page.url()).toBe(before);

  await button.click();
  await acceptConfirm(page, PROMPT);
  await expect(page).toHaveURL(new RegExp(`/Board\\?jobId=${jobId}$`));
  await expect(page.getByText('Candidate removed from this job.')).toBeVisible();
  await expect(card(page)).toHaveCount(0);
}

test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(JOB);
  await page.locator('#Description').fill('Created by the application-remove e2e spec.');
  await page.locator('#PipelineTemplateId').selectOption({ index: 1 });
  templateId = await page.locator('#PipelineTemplateId').inputValue();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job created.')).toBeVisible();

  await page.goto(`/Jobs?q=${encodeURIComponent(JOB)}`);
  await page.locator('.ats-trow').first().locator('button', { hasText: 'Publish' }).dispatchEvent('click');
  await acceptConfirm(page);
  await expect(page.getByText('Job published.')).toBeVisible();

  await page.goto('/Candidates/Create');
  await page.locator('#FirstName').fill('Remove');
  await page.locator('#LastName').fill(LAST);
  await page.locator('#Email').fill(EMAIL);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Candidate created.')).toBeVisible();

  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  const row = page.locator('.ats-trow').first();
  await row.locator('[data-bs-toggle="dropdown"]').click();
  jobId = (await row.locator('.dropdown-menu.show option', { hasText: JOB }).getAttribute('value'))!;
  boardUrl = `/Board?jobId=${jobId}`;
  await page.close();
});

// The dev tenant is connected to ReferralTool, so the published job is deleted again (it goes out
// as Inactive), and so is the candidate.
test.afterAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  // Bounded, so a delete that silently stops working fails here instead of hanging the run.
  for (let i = 0; ; i++) {
    expect(i, 'candidate cleanup did not finish').toBeLessThan(MAX_CLEANUP);
    await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
    const row = page.locator('.ats-trow').first();
    if ((await row.count()) === 0) break;
    await row.locator('button', { hasText: 'Delete' }).dispatchEvent('click');
    await acceptConfirm(page);
    await expect(page.getByText('Candidate deleted.')).toBeVisible();
  }
  await page.goto(`/Jobs?q=${encodeURIComponent(JOB)}`);
  const job = page.locator('.ats-trow').first();
  if ((await job.count()) > 0) {
    await job.locator('button', { hasText: 'Delete' }).dispatchEvent('click');
    await acceptConfirm(page);
    await expect(page.getByText('Job deleted.')).toBeVisible();
  }
  await page.goto(`/Jobs?q=${encodeURIComponent(JOB)}`);
  await expect(page.locator('.ats-trow')).toHaveCount(0);
  await page.close();
});

test('the board drawer removes the candidate from the job, after a cancelled confirm', async ({ page }) => {
  await addToJob(page);
  await page.goto(boardUrl);
  await expect(card(page)).toHaveCount(1);

  await card(page).locator('.ats-card-open').click();
  const drawer = page.locator('#ats-drawer-host .ats-drawer');
  await expect(drawer.getByRole('button', { name: 'Remove from job' })).toBeVisible();

  // Escape closes only the confirm, not the drawer underneath it.
  await removeWithDismissFirst(page, drawer, 'escape');
  await expect(page.locator('#ats-drawer-host')).toBeEmpty();
  // The drawer's trigger was swapped away; focus lands on the new page's heading, not <body>.
  await expect(page.locator('#ats-content h1')).toBeFocused();

  // The job's candidate count drops back to none.
  await page.goto(`/Jobs?q=${encodeURIComponent(JOB)}`);
  await expect(page.locator('.ats-trow').first().locator('.ats-avatar-stack')).toHaveText('—');

  // The candidate stays; only the application is gone.
  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  await expect(page.locator('.ats-trow')).toHaveCount(1);
});

test('the same candidate can be added again and removed from the details page', async ({ page }) => {
  await addToJob(page);
  await page.goto(boardUrl);
  await expect(card(page)).toHaveCount(1);

  const cardUrl = await card(page).getAttribute('hx-get-card');
  await page.goto(cardUrl!.replace('/Card/', '/Details/'));

  await removeWithDismissFirst(page, page.locator('#ats-content'), 'cancel');
});

// The removed application above still sits in the pipeline's first stage. The database refuses the
// stage delete whatever the app does (the foreign key is Restrict), so this is safe to run against
// the tenant's shared pipeline; it checks the refusal is a friendly message, not a 500.
test('a stage that a removed application still points at cannot be deleted', async ({ page }) => {
  await page.goto(`/Pipelines/Edit/${templateId}`);
  const first = page.locator('#stages tbody tr').first();
  const stageName = await first.locator('input[name$=".Name"]').inputValue();
  await first.getByRole('button', { name: 'Remove' }).click();
  await page.getByRole('button', { name: 'Save pipeline' }).click();

  await expect(page.getByText('This stage still has candidates (including removed ones) and cannot be deleted.')).toBeVisible();
  await page.goto(`/Pipelines/Edit/${templateId}`);
  await expect(page.locator('#stages tbody tr').first().locator('input[name$=".Name"]')).toHaveValue(stageName);
});
