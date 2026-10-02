import { expect, type Locator, type Page } from '@playwright/test';
import { acceptConfirm } from './confirm';

/** The Users list searched for `q` under every status, so one email finds its row whatever the user's state. */
export const findUserUrl = (q: string) => `/Users?status=All&q=${encodeURIComponent(q)}`;

/** The signed-in Owner's own row: always active, but it may sit on a later page, so walk the pager. */
export async function ownRow(page: Page): Promise<Locator> {
  await page.goto('/Users');
  const own = page.locator('.ats-trow').filter({ has: page.getByText('You', { exact: true }) });
  while (!(await own.count())) {
    const link = page.getByRole('link', { name: 'Next page' });
    const next = (await link.count()) ? await link.getAttribute('href') : null;
    if (!next) throw new Error('The Users list has no row marked "You".');
    await page.goto(next);
  }
  return own;
}

/** The signed-in Owner's own /Users/Edit/{id} URL; the id differs per database, so read it from the list. */
export async function ownEditUrl(page: Page): Promise<string> {
  const href = await (await ownRow(page)).locator('a.ats-row-link').getAttribute('href');
  if (!href) throw new Error('The own Users row has no link.');
  return href;
}

// One fixed Viewer per state, reused across runs so the list does not grow. The password is generated
// when the user is first created and only ever exists in the local database.
const FIXTURES = {
  active: { name: 'E2E Fixture Active', email: 'e2e-fixture-active@example.test' },
  deactivated: { name: 'E2E Fixture Deactivated', email: 'e2e-fixture-deactivated@example.test' },
};

/** The /Users/Edit/{id} URL of another user in the given state, creating that user or switching its state if needed. */
export async function otherEditUrl(page: Page, state: 'active' | 'deactivated'): Promise<string> {
  const u = FIXTURES[state];
  await page.goto(findUserUrl(u.email));
  const row = page.locator('.ats-trow', { hasText: u.email });
  if (!(await row.count())) {
    await page.goto('/Users/Create');
    await page.locator('#DisplayName').fill(u.name);
    await page.locator('#Email').fill(u.email);
    await page.locator('#Role').selectOption('Viewer');
    await page.locator('#TemporaryPassword').fill(`Fixture-${Date.now()}-pass`);
    await page.getByRole('button', { name: 'Add user' }).click();
    await expect(page.locator('.alert-success')).toBeVisible();
    await page.goto(findUserUrl(u.email));
    await expect(row).toHaveCount(1);
  }
  const href = await row.locator('a.ats-row-link').getAttribute('href');
  if (!href) throw new Error(`No Users row for ${u.email}.`);

  await page.goto(href);
  const toggle = page.getByRole('button', { name: state === 'active' ? 'Reactivate' : 'Deactivate' });
  if (await toggle.count()) {
    await toggle.click();
    await acceptConfirm(page);
    await expect(page.locator('.alert-success')).toBeVisible();
  }
  return href;
}

// Fixed Viewers that exist only to fill more than one page of the list, all kept deactivated so they
// never crowd the default (Active) view. Created on the first run, reused afterwards.
export const PAGER_FIXTURE_PREFIX = 'e2e-pager-';
export const PAGER_FIXTURE_COUNT = 21;

/** Ensures the pager fixtures exist and are deactivated. */
export async function ensurePagerFixtures(page: Page): Promise<void> {
  const total = page.locator('.ats-toolbar .ms-auto');
  await page.goto(`/Users?status=Deactivated&q=${PAGER_FIXTURE_PREFIX}`);
  if ((await total.textContent())?.trim() === `${PAGER_FIXTURE_COUNT} users`) return;

  for (let i = 1; i <= PAGER_FIXTURE_COUNT; i++) {
    const n = String(i).padStart(2, '0');
    const email = `${PAGER_FIXTURE_PREFIX}${n}@example.test`;
    await page.goto(findUserUrl(email));
    const row = page.locator('.ats-trow', { hasText: email });
    if (!(await row.count())) {
      await page.goto('/Users/Create');
      await page.locator('#DisplayName').fill(`E2E Pager ${n}`);
      await page.locator('#Email').fill(email);
      await page.locator('#Role').selectOption('Viewer');
      await page.locator('#TemporaryPassword').fill(`Fixture-${Date.now()}-pass`);
      await page.getByRole('button', { name: 'Add user' }).click();
      await expect(page.locator('.alert-success')).toBeVisible();
      await page.goto(findUserUrl(email));
    }
    if (!(await row.getByText('Deactivated', { exact: true }).count())) {
      await row.locator('a.ats-row-link').click();
      await page.getByRole('button', { name: 'Deactivate' }).click();
      await acceptConfirm(page);
      await expect(page.locator('.alert-success')).toBeVisible();
    }
  }
}
