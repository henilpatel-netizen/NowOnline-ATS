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

test.describe('career site for a signed-in back-office user', () => {
  // Default storage state: signed in as the Owner. The slug, not the cookie, decides the tenant.

  test('an unknown slug returns 404 even when signed in', async ({ page }) => {
    const response = await page.goto(`/careers/no-such-tenant-${Date.now()}`);
    expect(response?.status()).toBe(404);
  });

  test("another tenant's career site shows that tenant, not the signed-in one", async ({ page, browser }) => {
    // Each run adds a throwaway tenant (and its owner) to the local dev database.
    const stamp = Date.now();
    const otherSlug = `e2e-${stamp}`;
    const otherName = `E2E Other ${stamp}`;
    const anon = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    const reg = await anon.newPage();
    await reg.goto('/Account/Register');
    await reg.locator('#CompanyName').fill(otherName);
    await reg.locator('#Slug').fill(otherSlug);
    await reg.locator('#OwnerName').fill('E2E Owner');
    await reg.locator('#OwnerEmail').fill(`owner-${stamp}@example.test`);
    await reg.locator('#Password').fill(`E2e!${stamp}Aa`);
    await reg.getByRole('button', { name: /create account/i }).click();
    await expect(reg).not.toHaveURL(/\/Account\/Register/i);
    await anon.close();

    await page.goto(`/careers/${slug}`);
    const tenantName = page.locator('.careers-nav-inner span').nth(1);
    const ownName = (await tenantName.innerText()).trim();

    const response = await page.goto(`/careers/${otherSlug}`);
    expect(response?.status()).toBe(200);
    await expect(tenantName).toHaveText(otherName);
    expect(ownName).not.toBe(otherName);
    // The new tenant has no published jobs; the Owner's jobs must not show under its slug.
    await expect(page.locator('a[href*="/jobs/"]')).toHaveCount(0);
  });
});
