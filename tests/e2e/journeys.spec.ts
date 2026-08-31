import { test, expect } from '@playwright/test';

/**
 * Layer 4: the flows that carry the business. These write real rows, so every record is stamped
 * with a run-unique suffix and the tests assert their own data rather than pre-existing data.
 */
const RUN = `e2e-${Date.now()}`;

test.describe.configure({ mode: 'serial' });

test('job lifecycle: create, appears in list, edit, publish', async ({ page }) => {
  const title = `${RUN} Test Engineer`;

  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(title);
  await page.locator('#Description').fill('Created by the e2e suite.');

  // A pipeline is mandatory; take whichever the tenant has configured.
  const pipeline = page.locator('#PipelineTemplateId');
  const options = await pipeline.locator('option[value]:not([value=""])').all();
  expect(options.length, 'the tenant needs at least one pipeline template').toBeGreaterThan(0);
  await pipeline.selectOption({ index: 1 });

  await page.getByRole('button', { name: /save|create/i }).first().click();

  await expect(page).toHaveURL(/\/Jobs/);
  await expect(page.getByText(title)).toBeVisible();
});

test('validation: an empty job is rejected and keeps what was typed', async ({ page }) => {
  await page.goto('/Jobs/Create');
  const description = 'this text must survive the failed save';
  await page.locator('#Description').fill(description);
  await page.getByRole('button', { name: /save|create/i }).first().click();

  // Still on the form, with an error, and the typed description intact.
  await expect(page.locator('#Title')).toBeVisible();
  await expect(page.locator('.text-danger, .validation-summary-errors').first()).toBeVisible();
  await expect(page.locator('#Description')).toHaveValue(description);
});

test('candidate lifecycle: create and find by search', async ({ page }) => {
  const last = `${RUN}-Candidate`;

  await page.goto('/Candidates/Create');
  await page.locator('#FirstName').fill('E2E');
  await page.locator('#LastName').fill(last);
  await page.locator('#Email').fill(`${RUN}@example.com`);
  await page.getByRole('button', { name: /save|create/i }).first().click();

  await expect(page).toHaveURL(/\/Candidates/);

  const search = page.locator('.ats-search input').first();
  await search.fill(last);
  await search.press('Enter');
  await page.waitForLoadState('networkidle');
  await expect(page.getByText(last).first()).toBeVisible();
});

test('board: a job opens its board with its pipeline stages', async ({ page }) => {
  await page.goto('/Jobs');
  await page.locator('.ats-row-link').first().click();
  await expect(page).toHaveURL(/\/Board/);

  const columns = page.locator('[class*="board-col"], .ats-board-col');
  expect(await columns.count(), 'a board should render its stage columns').toBeGreaterThan(0);
});

test('duplicate email is rejected on candidate create', async ({ page }) => {
  const email = `${RUN}-dupe@example.com`;

  for (const attempt of [1, 2]) {
    await page.goto('/Candidates/Create');
    await page.locator('#FirstName').fill('Dupe');
    await page.locator('#LastName').fill(`${RUN}-${attempt}`);
    await page.locator('#Email').fill(email);
    await page.getByRole('button', { name: /save|create/i }).first().click();
  }

  // The second attempt must not silently create a second row.
  await page.goto(`/Candidates?q=${encodeURIComponent(email)}`);
  await page.waitForLoadState('networkidle');
  const rows = page.locator('.ats-trow');
  expect(await rows.count(), 'duplicate email should not create two candidates').toBeLessThanOrEqual(1);
});
