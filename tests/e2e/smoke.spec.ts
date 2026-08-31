import { test, expect } from '@playwright/test';

/**
 * Layer 1: every screen renders, has a heading and a document title, and produces no browser
 * console errors or unhandled JS exceptions. Cheapest possible net for "did I break a page".
 */
const ROUTES = [
  '/',
  '/Jobs',
  '/Jobs/Create',
  '/Candidates',
  '/Pipelines',
  '/Pipelines/Create',
  '/Organisation',
  '/Departments',
  '/Locations',
  '/Integration',
  '/Integration/Deliveries',
  '/CareerSite',
  '/CareerSite/Branding',
  '/Audit',
];

for (const route of ROUTES) {
  test(`smoke: ${route}`, async ({ page }) => {
    const consoleErrors: string[] = [];
    const pageErrors: string[] = [];
    page.on('console', m => m.type() === 'error' && consoleErrors.push(m.text()));
    page.on('pageerror', e => pageErrors.push(e.message));

    const response = await page.goto(route);
    expect(response?.status(), `${route} status`).toBeLessThan(400);

    // Every back-office screen sets a title from data-page-title, and renders one h1.
    await expect(page).not.toHaveTitle(/^\s*$/);
    await expect(page.locator('h1')).toHaveCount(1);

    expect(pageErrors, `${route} threw JS errors`).toEqual([]);
    expect(consoleErrors, `${route} logged console errors`).toEqual([]);
  });
}

test('404 page is served for an unknown back-office route', async ({ page }) => {
  const response = await page.goto('/Jobs/Details/999999');
  expect(response?.status()).toBeGreaterThanOrEqual(400);
});
