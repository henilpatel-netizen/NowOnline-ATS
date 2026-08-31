import { test, expect, Page } from '@playwright/test';

/**
 * Measures the real cost of every navigation surface in the back office.
 *
 * The metric that matters is `documents`: a top-level HTML document request means the browser
 * threw the page away and rebuilt it (the "blink"). An htmx-boosted navigation issues an
 * xhr/fetch instead, so documents === 0.
 */
type NavCost = {
  label: string;
  documents: number;
  xhr: number;
  assets: number;
  ms: number;
};

const results: NavCost[] = [];

async function measure(page: Page, label: string, action: () => Promise<void>): Promise<NavCost> {
  let documents = 0;
  let xhr = 0;
  let assets = 0;

  const onRequest = (r: import('@playwright/test').Request) => {
    const type = r.resourceType();
    if (type === 'document') documents++;
    else if (type === 'xhr' || type === 'fetch') xhr++;
    else assets++;
  };

  page.on('request', onRequest);
  const started = Date.now();
  await action();
  await page.waitForLoadState('networkidle');
  const ms = Date.now() - started;
  page.off('request', onRequest);

  const cost: NavCost = { label, documents, xhr, assets, ms };
  results.push(cost);
  return cost;
}

test.afterAll(() => {
  const pad = (s: string, n: number) => s.padEnd(n);
  const lines = [
    '',
    '=== NAVIGATION COST ===============================================',
    `${pad('surface', 40)} ${pad('docs', 6)}${pad('xhr', 6)}${pad('assets', 8)}ms`,
    '-------------------------------------------------------------------',
    ...results.map(
      r =>
        `${pad(r.label, 40)} ${pad(String(r.documents), 6)}${pad(String(r.xhr), 6)}` +
        `${pad(String(r.assets), 8)}${r.ms}`
    ),
    '-------------------------------------------------------------------',
    `full reloads: ${results.filter(r => r.documents > 0).length} / ${results.length}`,
    '===================================================================',
    '',
  ];
  console.log(lines.join('\n'));
});

test('sidebar navigation is boosted', async ({ page }) => {
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');

  const links = ['Candidates', 'Jobs'];
  for (const name of links) {
    const cost = await measure(page, `sidebar: ${name}`, async () => {
      await page.locator('#ats-sidebar').getByRole('link', { name, exact: false }).first().click();
    });
    expect(cost.documents, `sidebar "${name}" should not full-reload`).toBe(0);
  }
});

test('in-content navigation cost', async ({ page }) => {
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');

  // Filter tab inside the content area.
  const filter = page.locator('.ats-filter-group a').nth(1);
  if (await filter.count()) {
    await measure(page, 'jobs: filter tab', async () => {
      await filter.click();
    });
  }

  // Row title -> board.
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');
  const row = page.locator('.ats-row-link').first();
  if (await row.count()) {
    await measure(page, 'jobs: row -> board', async () => {
      await row.click();
    });
  }

  // Toolbar action link.
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');
  const newJob = page.getByRole('link', { name: /New job/i }).first();
  if (await newJob.count()) {
    await measure(page, 'jobs: New job button', async () => {
      await newJob.click();
    });
  }

  // Search submit (GET form).
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');
  const search = page.locator('.ats-search input[type="search"], .ats-search input').first();
  if (await search.count()) {
    await measure(page, 'jobs: search submit', async () => {
      await search.fill('a');
      await search.press('Enter');
    });
  }
});

test('cross-section journey cost', async ({ page }) => {
  const targets = ['/Candidates', '/Pipelines', '/Departments', '/Locations', '/Integration', '/Audit'];
  for (const url of targets) {
    await measure(page, `direct load: ${url}`, async () => {
      await page.goto(url);
    });
  }
});
