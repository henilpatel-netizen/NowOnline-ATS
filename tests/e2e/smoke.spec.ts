import { test, expect, type Page } from '@playwright/test';
import { otherEditUrl, ownEditUrl } from './users';

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
  '/Users',
  '/Audit',
];

async function smoke(page: Page, route: string) {
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
}

for (const route of ROUTES) {
  test(`smoke: ${route}`, async ({ page }) => smoke(page, route));
}

test('smoke: own /Users/Edit page', async ({ page }) => smoke(page, await ownEditUrl(page)));

for (const state of ['active', 'deactivated'] as const) {
  test(`smoke: /Users/Edit page of another ${state} user`, async ({ page }) => smoke(page, await otherEditUrl(page, state)));
}

// (page - 1) * pageSize used to overflow to a negative Skip and return 500, and an out-of-range page
// rendered "Page -5 of N" or an empty page. Out-of-range pages are clamped: past the end shows the last
// page, below one shows the first.
const ROWS = '.ats-trow, .ats-audit-row';
const PAGER = 'nav[aria-label="Pagination"] .ats-muted';

for (const list of ['/Jobs', '/Candidates', '/Users', '/Audit', '/Integration/Deliveries']) {
  test(`smoke: ${list}?page=2147483647 renders the last page`, async ({ page }) => {
    await page.goto(list);
    const hasRows = (await page.locator(ROWS).count()) > 0;

    await smoke(page, `${list}?page=2147483647`);
    if (hasRows) await expect(page.locator(ROWS).first()).toBeVisible();
    const pager = page.locator(PAGER);
    if (await pager.count()) {
      const text = await pager.innerText();
      expect(text).toMatch(/^Page (\d+) of \1$/);
    }
  });

  for (const outOfRange of [0, -5]) {
    test(`smoke: ${list}?page=${outOfRange} renders page 1`, async ({ page }) => {
      await smoke(page, `${list}?page=${outOfRange}`);
      const pager = page.locator(PAGER);
      if (await pager.count()) await expect(pager).toHaveText(/^Page 1 of \d+$/);
    });
  }
}

test('404 page is served for an unknown back-office route', async ({ page }) => {
  const response = await page.goto('/Jobs/Details/999999');
  expect(response?.status()).toBeGreaterThanOrEqual(400);
});
