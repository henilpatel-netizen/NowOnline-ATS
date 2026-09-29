import { test, expect } from '@playwright/test';

/**
 * Layer 2: the guarantees that must never regress silently. These run with NO stored session, so
 * they prove the app rejects an anonymous caller rather than proving a logged-in user can browse.
 */
test.use({ storageState: { cookies: [], origins: [] } });

const PROTECTED = [
  '/',
  '/Jobs',
  '/Jobs/Create',
  '/Candidates',
  '/Pipelines',
  '/Organisation',
  '/Departments',
  '/Locations',
  '/Integration',
  '/Audit',
];

for (const route of PROTECTED) {
  test(`anonymous access to ${route} is refused`, async ({ page }) => {
    await page.goto(route);
    // ASP.NET Core Identity redirects to the login page with a ReturnUrl.
    await expect(page).toHaveURL(/\/Account\/Login/);
  });
}

test('anonymous POST is rejected rather than silently accepted', async ({ request }) => {
  const response = await request.post('/Jobs/Create', {
    form: { Title: 'anonymous write attempt' },
    maxRedirects: 0,
  });
  expect(response.status(), 'expected a redirect to login or a 4xx').not.toBe(200);
});

test('an unknown career-site slug returns 404, not another tenant', async ({ page }) => {
  const response = await page.goto('/careers/definitely-not-a-real-tenant-slug');
  expect(response?.status()).toBe(404);
});
