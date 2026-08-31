import { test, expect } from '@playwright/test';

/**
 * Regression guard for NAV-2. Boosting the content area means hx-target/hx-select are inherited by
 * every descendant, which is exactly how the first attempt broke search, the drawer and the board.
 * These tests fail loudly if that inheritance starts biting again.
 */

test('the global search still returns results into its own panel', async ({ page }) => {
  await page.goto('/Jobs');
  // pressSequentially, not fill: the htmx trigger is "keyup changed delay:250ms", and fill() sets
  // the value without ever firing a keyup, so the search would silently never run.
  await page.locator('#ats-global-search').pressSequentially('an', { delay: 120 });
  const response = await page.waitForResponse(r => r.url().includes('/Search'), { timeout: 8000 });
  expect(response.status()).toBe(200);
  await expect(page.locator('#ats-search-results')).not.toBeEmpty({ timeout: 5000 });
  // The page itself must NOT have been replaced by the search fragment.
  await expect(page.locator('#ats-content')).toBeVisible();
  await expect(page.locator('#ats-sidebar')).toBeVisible();
});

test('the shell survives a boosted navigation', async ({ page }) => {
  await page.goto('/Jobs');
  await page.locator('.ats-row-link').first().click();
  // A boosted swap fires no load event, so networkidle can resolve before the swap lands. Wait for
  // the URL htmx pushes instead.
  await page.waitForURL(/\/Board/);

  // A bad hx-select would swap the fragment in and destroy the shell.
  await expect(page.locator('#ats-sidebar')).toBeVisible();
  await expect(page.locator('#ats-content')).toBeVisible();
  await expect(page.locator('#ats-content')).toHaveCount(1);
});

test('back and forward restore both the content and the document title', async ({ page }) => {
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');
  const jobsTitle = await page.title();

  await page.locator('#ats-sidebar a[href="/Candidates"]').click();
  await page.waitForURL(/\/Candidates/);
  await expect.poll(() => page.title()).not.toBe(jobsTitle);
  const candidatesTitle = await page.title();

  await page.goBack();
  await page.waitForURL(/\/Jobs/);
  await expect.poll(() => page.title(), { message: 'title must be restored on Back' })
    .toBe(jobsTitle);
  await expect(page.locator('#ats-sidebar')).toBeVisible();

  await page.goForward();
  await page.waitForURL(/\/Candidates/);
  await expect.poll(() => page.title(), { message: 'title must be restored on Forward' })
    .toBe(candidatesTitle);
});

test('a destructive action asks for confirmation and cancelling changes nothing', async ({ page }) => {
  await page.goto('/Pipelines');
  await page.waitForLoadState('networkidle');

  const before = await page.locator('.ats-trow').count();
  const remove = page.getByRole('button', { name: /delete|remove/i }).first();
  test.skip((await remove.count()) === 0, 'no deletable pipeline on this tenant');

  // hx-confirm raises a native dialog; dismissing it must abort the request.
  page.once('dialog', d => d.dismiss());
  await remove.click();
  await page.waitForTimeout(500);

  expect(await page.locator('.ats-trow').count(), 'cancelling must not delete').toBe(before);
});

test('a boosted form POST still shows its result message', async ({ page }) => {
  await page.goto('/Candidates/Create');
  await page.locator('#FirstName').fill('Boost');
  await page.locator('#LastName').fill(`Check-${Date.now()}`);
  await page.locator('#Email').fill(`boost-${Date.now()}@example.com`);
  await page.getByRole('button', { name: /save|create/i }).first().click();
  await page.waitForURL(/\/Candidates$/);

  // _Alerts renders inside #ats-content, so the toast must arrive with the swap.
  await expect(page.locator('.alert, .ats-toast').first()).toBeVisible({ timeout: 5000 });
  await expect(page.locator('#ats-sidebar')).toBeVisible();
});

test('page scripts re-run after a boosted swap', async ({ page }) => {
  // Pipelines/Create wires up "Add stage" in a @section Scripts block inside <main>. If boosted
  // swaps did not re-execute it, the button would silently do nothing.
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');
  await page.locator('#ats-sidebar a[href="/Pipelines"]').click();
  await page.waitForURL(/\/Pipelines/);
  await page.getByRole('link', { name: /new pipeline|create/i }).first().click();
  await page.waitForURL(/\/Pipelines\/Create/);
  await expect(page.locator('#stages tbody tr').first()).toBeVisible();

  const rowsBefore = await page.locator('#stages tbody tr').count();
  await page.locator('#add-stage').click();
  expect(await page.locator('#stages tbody tr').count(), 'Add stage must still work after a swap')
    .toBe(rowsBefore + 1);
});

test('keyboard: the skip link reaches the content area', async ({ page }) => {
  await page.goto('/Jobs');
  await page.keyboard.press('Tab');
  const focused = await page.evaluate(() => document.activeElement?.className ?? '');
  expect(focused, 'first tab stop should be the skip link').toContain('ats-skip-link');
});
