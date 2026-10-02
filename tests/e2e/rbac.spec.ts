import { test, expect } from '@playwright/test';

// Non-Owner coverage (viewer, recruiter, forced password change, deactivation) lives in users.spec.ts;
// the role matrix is also covered by tests/Ats.Tests/Authorization. This spec proves the Owner keeps
// full access and that the require-authenticated fallback policy did not break the public surface.

test.describe('owner', () => {
  test('sees admin navigation', async ({ page }) => {
    await page.goto('/Dashboard');
    const nav = page.locator('#ats-sidebar');
    for (const name of ['Pipelines', 'Organisation', 'Integrations', 'Audit log']) {
      await expect(nav.getByRole('link', { name, exact: false })).toBeVisible();
    }
  });

  test('sees manage actions', async ({ page }) => {
    await page.goto('/Jobs');
    await expect(page.getByRole('link', { name: 'New job' })).toBeVisible();
    await page.goto('/Candidates');
    await expect(page.getByRole('link', { name: 'Add candidate' })).toBeVisible();
    await page.goto('/CareerSite');
    await expect(page.getByRole('link', { name: 'Branding' })).toBeVisible();
  });

  test('can open the job form with editable fields', async ({ page }) => {
    await page.goto('/Jobs/Create');
    await expect(page.locator('#Title')).toBeEditable();
    await expect(page.getByRole('button', { name: 'Save' })).toBeVisible();
  });
});

test.describe('anonymous', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('back office redirects to sign-in', async ({ page }) => {
    await page.goto('/Jobs');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('sign-in page loads its stylesheets', async ({ page }) => {
    const failed: string[] = [];
    page.on('response', r => { if (r.url().endsWith('.css') && r.status() >= 400) failed.push(r.url()); });
    await page.goto('/Account/Login');
    await expect(page.locator('#Email')).toBeVisible();
    expect(failed).toEqual([]);
  });

  test('liveness probe is public', async ({ request }) => {
    const res = await request.get('/health/live');
    expect(res.status()).toBe(200);
  });
});
