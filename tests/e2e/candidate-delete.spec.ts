import { type Page } from '@playwright/test';
import { test, expect, acceptConfirm } from './confirm';

/**
 * Deleting a candidate soft-deletes them and all their applications, so they vanish from the list,
 * the global search and the board, and their email can be used again. The confirm is the themed
 * hx-confirm modal, so each delete asserts its text before confirming.
 */
const RUN = `cdel-${Date.now()}`;
const JOB = `${RUN} Job`;
const LAST = RUN;
const EMAIL = `${RUN}@example.com`;
const PROMPT = 'Delete this candidate and remove them from all jobs?';

test.describe.configure({ mode: 'serial' });

// Each run creates at most a few candidates; more loop turns than this means the cleanup is stuck.
const MAX_CLEANUP = 10;

let boardUrl = '';

async function createCandidate(page: Page) {
  await page.goto('/Candidates/Create');
  await page.locator('#FirstName').fill('Delete');
  await page.locator('#LastName').fill(LAST);
  await page.locator('#Email').fill(EMAIL);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Candidate created.')).toBeVisible();
}

test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(JOB);
  await page.locator('#Description').fill('Created by the candidate-delete e2e spec.');
  await page.locator('#PipelineTemplateId').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job created.')).toBeVisible();

  await page.goto(`/Jobs?q=${encodeURIComponent(JOB)}`);
  await page.locator('.ats-trow').first().locator('button', { hasText: 'Publish' }).dispatchEvent('click');
  await acceptConfirm(page);
  await expect(page.getByText('Job published.')).toBeVisible();

  await createCandidate(page);
  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  const row = page.locator('.ats-trow').first();
  await row.locator('[data-bs-toggle="dropdown"]').click();
  const menu = row.locator('.dropdown-menu.show');
  const jobId = await menu.locator('option', { hasText: JOB }).getAttribute('value');
  await menu.locator('select').selectOption(jobId!);
  await menu.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByText('Candidate added to job.')).toBeVisible();
  boardUrl = `/Board?jobId=${jobId}`;
  await page.close();
});

// The dev tenant is connected to ReferralTool, so the published job is deleted again (it goes out
// as Inactive), along with any candidate a failed test left behind.
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

test('a candidate is deleted from the list row menu and disappears everywhere', async ({ page }) => {
  await page.goto(boardUrl);
  await expect(page.locator('#ats-content').getByText(`Delete ${LAST}`)).not.toHaveCount(0);

  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  const row = page.locator('.ats-trow');
  await expect(row).toHaveCount(1);
  await row.locator('[data-bs-toggle="dropdown"]').click();
  await row.locator('.dropdown-menu.show').getByRole('button', { name: 'Delete' }).click();
  await acceptConfirm(page, PROMPT);

  await expect(page).toHaveURL(/\/Candidates/);
  await expect(page.getByText('Candidate deleted.')).toBeVisible();

  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  await expect(page.locator('.ats-trow')).toHaveCount(0);

  const search = page.locator('#ats-global-search');
  await search.fill(EMAIL);
  await search.press('Enter');
  await expect(page.locator('#ats-search-results')).toContainText('No matches.');

  await page.goto(boardUrl);
  await expect(page.locator('#ats-content').getByText(`Delete ${LAST}`)).toHaveCount(0);
});

test('the email of a deleted candidate can be used again, and the edit page deletes too', async ({ page }) => {
  await createCandidate(page);

  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  await page.locator('.ats-trow .ats-row-link').first().click();
  await expect(page).toHaveURL(/\/Candidates\/Edit\/\d+/);

  await page.getByRole('button', { name: 'Delete candidate' }).click();
  await acceptConfirm(page, PROMPT);
  await expect(page).toHaveURL(/\/Candidates/);
  await expect(page.getByText('Candidate deleted.')).toBeVisible();

  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  await expect(page.locator('.ats-trow')).toHaveCount(0);
});
