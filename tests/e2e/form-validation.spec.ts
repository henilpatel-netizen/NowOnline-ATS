import { test, expect, type Page } from '@playwright/test';

/**
 * Themed validation (ats-validation.js). Forms keep their HTML5 constraints, but the browser's own
 * bubble is suppressed: every `invalid` event must be cancelled, the message is rendered under the
 * field, the field is marked aria-invalid and linked to it, and focus lands on the first invalid
 * field. Playwright cannot see a native bubble, so "no bubble" is asserted as "every invalid event
 * was cancelled" (the bubble only shows for an uncancelled one).
 */

/** Records, per invalid event, the field name and whether it was cancelled once dispatch ended. */
async function recordInvalidEvents(page: Page) {
  await page.addInitScript(() => {
    const seen: { name: string; cancelled: boolean }[] = [];
    (window as unknown as { __invalid: typeof seen }).__invalid = seen;
    document.addEventListener('invalid', (e) => {
      const el = e.target as HTMLInputElement;
      setTimeout(() => seen.push({ name: el.name, cancelled: e.defaultPrevented }));
    }, true);
  });
}

async function invalidEvents(page: Page) {
  return page.evaluate(() => (window as unknown as { __invalid: { name: string; cancelled: boolean }[] }).__invalid);
}

function countPosts(page: Page) {
  const posts: string[] = [];
  page.on('request', (r) => { if (r.method() === 'POST') posts.push(r.url()); });
  return posts;
}

/** The field is invalid, shows `message` in the element its aria-describedby points at. */
async function expectFieldError(page: Page, selector: string, message: string | RegExp) {
  const field = page.locator(selector);
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  const describedBy = (await field.getAttribute('aria-describedby')) ?? '';
  const ids = describedBy.split(/\s+/).filter(Boolean);
  expect(ids.length, `${selector} has no aria-describedby`).toBeGreaterThan(0);
  const error = page.locator(ids.map((id) => `[id="${id}"]`).join(',')).filter({ hasText: message });
  await expect(error).toHaveCount(1);
  await expect(error).toBeVisible();
  await expect(error).toHaveClass(/field-validation-error/);
}

async function expectAllCancelled(page: Page, names: string[]) {
  await expect.poll(async () => (await invalidEvents(page)).map((e) => e.name)).toEqual(expect.arrayContaining(names));
  for (const e of await invalidEvents(page)) expect(e.cancelled, `native bubble for ${e.name}`).toBe(true);
}

test.describe('anonymous forms', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('login: empty submit shows themed messages, focuses the first field, sends nothing', async ({ page }) => {
    await recordInvalidEvents(page);
    await page.goto('/Account/Login');
    const posts = countPosts(page);

    await page.getByRole('button', { name: 'Sign in' }).click();

    await expectAllCancelled(page, ['Email', 'Password']);
    await expect(page.locator('#Email')).toBeFocused();
    await expectFieldError(page, '#Email', 'Enter an email address.');
    await expectFieldError(page, '#Password', 'Fill in this field.');
    // The client message reuses the server's asp-validation-for span: one message per field.
    await expect(page.locator('[data-valmsg-for="Email"]')).toHaveText('Enter an email address.');
    await expect(page.locator('#Email ~ .field-validation-error')).toHaveCount(1);
    expect(posts).toEqual([]);

    await page.locator('#Email').fill('not-an-email');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expectFieldError(page, '#Email', 'Enter a valid email address.');

    await page.locator('#Email').fill('someone@example.com');
    await expect(page.locator('#Email')).not.toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('[data-valmsg-for="Email"]')).toHaveText('');
    expect(posts).toEqual([]);
  });

  test('server-side messages still render on the login form', async ({ page }) => {
    await page.goto('/Account/Login');
    // Bypass the client constraints to reach ModelState validation.
    await page.evaluate(() => { document.querySelector('form')!.noValidate = true; });
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.locator('[data-valmsg-for="Email"]')).toHaveText(/required/i);
    await expect(page.locator('#Email')).toHaveAttribute('aria-invalid', 'true');
  });
});

test.describe('public career site', () => {
  let slug: string;

  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
    await page.goto('/CareerSite');
    const href = await page.getByRole('link', { name: /open live site/i }).first().getAttribute('href');
    await page.close();
    slug = href!.match(/\/careers\/([^/?#]+)/)![1];
  });

  test.use({ storageState: { cookies: [], origins: [] } });

  test('apply: empty submit shows themed messages including the resume, sends nothing', async ({ page }) => {
    await recordInvalidEvents(page);
    await page.goto(`/careers/${slug}`);
    const job = page.locator('a[href*="/jobs/"]').first();
    test.skip((await job.count()) === 0, 'no published job on this tenant');
    await job.click();
    const posts = countPosts(page);

    await page.getByRole('button', { name: /submit application/i }).click();

    await expectAllCancelled(page, ['FirstName', 'LastName', 'Email', 'resume']);
    await expect(page.locator('#FirstName')).toBeFocused();
    await expectFieldError(page, '#FirstName', 'Fill in this field.');
    await expectFieldError(page, '#Email', 'Enter an email address.');
    await expectFieldError(page, 'input[name="resume"]', 'Choose a file to upload.');
    expect(posts).toEqual([]);
  });
});

test.describe('back office', () => {
  test('a boosted form stays on the page and sends no request when invalid', async ({ page }) => {
    await recordInvalidEvents(page);
    await page.goto('/Organisation');
    // Reach the form through a boosted navigation, so it is swapped-in content, not a page load.
    await page.locator('#ats-content a[href$="/Departments/Create"]').first().click();
    await expect(page).toHaveURL(/\/Departments\/Create/);
    await expect(page.locator('#Name')).toBeVisible();
    const posts = countPosts(page);

    await page.locator('#ats-content form').getByRole('button', { name: 'Save' }).click();

    await expectAllCancelled(page, ['Name']);
    await expect(page.locator('#Name')).toBeFocused();
    await expectFieldError(page, '#Name', 'Fill in this field.');
    expect(posts).toEqual([]);

    await page.locator('#Name').fill('x');
    await expect(page.locator('#Name')).not.toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('[data-valmsg-for="Name"]')).toBeHidden();
  });

  test('a non-nullable bool checkbox is not made required', async ({ page }) => {
    await recordInvalidEvents(page);
    await page.goto('/Pipelines/Create');
    const posts = countPosts(page);

    await page.getByRole('button', { name: 'Save pipeline' }).click();

    await expectAllCancelled(page, ['Name']);
    const names = (await invalidEvents(page)).map((e) => e.name);
    expect(names.some((n) => n.endsWith('.IsTerminal')), 'an unticked checkbox was flagged').toBe(false);
    await expect(page.locator('input[type="checkbox"][name$=".IsTerminal"]').first()).not.toHaveAttribute('required', '');
    expect(posts).toEqual([]);
  });
});
