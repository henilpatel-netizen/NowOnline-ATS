import { test, expect } from '@playwright/test';
import { ensurePagerFixtures } from './users';

const rows = '.ats-audit-row';

test('search matches the action and the entity type', async ({ page }) => {
  // The pager fixtures are 21 users the Owner created, so at least 21 UserCreated entries exist.
  await ensurePagerFixtures(page);

  await page.goto('/Audit?q=User');
  await expect(page.locator(rows).first()).toBeVisible();
  await expect(page.locator(`${rows} .ats-audit-action`, { hasText: /^User/ }).first()).toBeVisible();

  // "Added 'x' as Viewer" does not contain the action name, so only the Action column can match.
  await page.goto('/Audit?q=UserCreated');
  await expect(page.locator(`${rows} .ats-audit-action`, { hasText: 'UserCreated' }).first()).toBeVisible();
});

test('LIKE wildcards in the search are matched literally', async ({ page }) => {
  await page.goto(`/Audit?q=${encodeURIComponent('%%%')}`);
  await expect(page.locator(rows)).toHaveCount(0);
  await expect(page.getByText('No activity matches.')).toBeVisible();
});

test('the pager goes to page 2 and keeps the search', async ({ page }) => {
  await ensurePagerFixtures(page);
  await page.goto('/Audit?q=UserCreated');
  await expect(page.locator(rows)).toHaveCount(20);

  await page.getByRole('link', { name: 'Next page' }).click();
  await expect(page).toHaveURL(/page=2/);
  await expect(page).toHaveURL(/q=UserCreated/);
  await expect(page.getByText(/^Page 2 of \d+$/)).toBeVisible();
  await expect(page.getByLabel('Search audit log')).toHaveValue('UserCreated');
});
