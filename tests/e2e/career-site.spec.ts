import { test, expect } from '@playwright/test';

/**
 * Layer 5: the public career site. This is the only part of the product a candidate ever sees, and
 * the only anonymous write path in the app, so it needs its own coverage.
 *
 * The slug is discovered from the back office rather than hardcoded, so the suite works on any
 * tenant.
 */
let slug: string;

test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  await page.goto('/CareerSite');
  const href = await page.getByRole('link', { name: /open live site/i }).first().getAttribute('href');
  await page.close();
  const match = href?.match(/\/careers\/([^/?#]+)/);
  expect(match, `could not discover the career-site slug from "${href}"`).not.toBeNull();
  slug = match![1];
});

test.describe('public career site', () => {
  // The career site is anonymous by definition.
  test.use({ storageState: { cookies: [], origins: [] } });

  test('the careers index renders published jobs', async ({ page }) => {
    const response = await page.goto(`/careers/${slug}`);
    expect(response?.status()).toBe(200);
    await expect(page.locator('h1').first()).toBeVisible();
  });

  test('a job detail page renders an apply form with a resume field', async ({ page }) => {
    await page.goto(`/careers/${slug}`);
    const job = page.locator('a[href*="/jobs/"]').first();
    test.skip((await job.count()) === 0, 'no published job on this tenant');

    await job.click();
    await expect(page.locator('form[enctype="multipart/form-data"]')).toBeVisible();
    await expect(page.locator('input[name="resume"]')).toHaveAttribute('required', '');
  });

  test('applying without a resume is rejected', async ({ page }) => {
    await page.goto(`/careers/${slug}`);
    const job = page.locator('a[href*="/jobs/"]').first();
    test.skip((await job.count()) === 0, 'no published job on this tenant');
    await job.click();

    const applyUrl = page.url() + '/apply';
    // Post without the file part: the server must not accept it, whatever the browser would do.
    const response = await page.request.post(applyUrl, {
      form: { FirstName: 'No', LastName: 'Resume', Email: 'no-resume@example.com' },
      maxRedirects: 0,
    });
    expect(response.status(), 'a resume-less application must not be accepted').not.toBe(302);
  });

  test('an unknown job on a valid slug returns 404', async ({ page }) => {
    const response = await page.goto(`/careers/${slug}/jobs/not-a-real-external-ref`);
    expect(response?.status()).toBe(404);
  });

  test('the career site does not leak the back-office shell', async ({ page }) => {
    await page.goto(`/careers/${slug}`);
    await expect(page.locator('#ats-sidebar')).toHaveCount(0);
  });

  test('the career site has no accessibility violations', async ({ page }) => {
    const { default: AxeBuilder } = await import('@axe-core/playwright');
    await page.goto(`/careers/${slug}`);
    await page.waitForLoadState('networkidle');
    const { violations } = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();
    if (violations.length) {
      console.log(
        violations
          .map(v => `[${v.impact}] ${v.id}\n` + v.nodes.slice(0, 3).map(n => `    ${n.target.join(' ')} :: ${n.html.slice(0,90)}`).join('\n'))
          .join('\n')
      );
    }
    expect(violations.map(v => `${v.impact}:${v.id}`)).toEqual([]);
  });
});
