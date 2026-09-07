import { test } from '@playwright/test';

const SHOTS = [
  { route: '/', name: 'dashboard' },
  { route: '/Jobs', name: 'jobs' },
  { route: '/Organisation', name: 'organisation' },
  { route: '/Integration', name: 'integration' },
  { route: '/Audit', name: 'audit' },
  { route: '/Candidates', name: 'candidates' },
  { route: '/Pipelines/Create', name: 'pipeline-create' },
];

test('capture wide desktop', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 1920, height: 1080 });
  for (const s of SHOTS) {
    await page.goto(s.route);
    await page.waitForLoadState('networkidle');
    await page.screenshot({ path: `artifacts/shots/1920-${s.name}.png`, fullPage: true });
  }
});

test('capture mobile', async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 375, height: 812 });
  for (const s of ['/Jobs', '/', '/Audit']) {
    await page.goto(s);
    await page.waitForLoadState('networkidle');
    await page.screenshot({ path: `artifacts/shots/375-${s.replace(/\W/g,'') || 'dashboard'}.png`, fullPage: true });
  }
});
