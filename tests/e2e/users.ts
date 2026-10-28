import { expect, type Browser, type Locator, type Page } from '@playwright/test';
import { acceptConfirm } from './confirm';
import { waitForMail } from './mailbox';

// Same origin the config uses; contexts made with browser.newContext() do not inherit it.
const baseURL = process.env.ATS_BASE_URL ?? 'https://localhost:7044';

export const INVITE_SUBJECT = 'You are invited to join';

/** Invites a user from /Users/Create (no password: they choose one from the emailed link). */
export async function inviteUser(page: Page, u: { name: string; email: string }, role: string): Promise<void> {
  await page.goto('/Users/Create');
  await page.locator('#DisplayName').fill(u.name);
  await page.locator('#Email').fill(u.email);
  await page.locator('#Role').selectOption(role);
  await page.getByRole('button', { name: 'Send invitation' }).click();
  await expect(page.locator('.alert-success')).toContainText(`Invitation sent to ${u.email.toLowerCase()}.`);
}

/**
 * Opens an invite link in a fresh browser context (the Owner's session is left alone), sets `password`
 * and returns the page, signed in as the invited user. The caller closes its context.
 */
export async function acceptInvite(browser: Browser, link: string, password: string): Promise<Page> {
  const ctx = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  const page = await ctx.newPage();
  await page.goto(link);
  await page.locator('#NewPassword').fill(password);
  await page.locator('#ConfirmPassword').fill(password);
  await page.getByRole('button', { name: 'Set password and sign in' }).click();
  await expect(page.locator('#ats-sidebar')).toBeVisible({ timeout: 15_000 });
  return page;
}

/** Invites a user, accepts the emailed invite with a generated password and returns that password. */
export async function addUser(page: Page, u: { name: string; email: string }, role: string): Promise<string> {
  await inviteUser(page, u, role);
  return acceptLatestInvite(page, u.email);
}

async function acceptLatestInvite(page: Page, email: string): Promise<string> {
  const mail = await waitForMail(email, INVITE_SUBJECT);
  const password = `Invite-${Date.now()}-pass`;
  const invited = await acceptInvite(page.context().browser()!, mail.link!, password);
  await invited.context().close();
  return password;
}

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
// when the invite is accepted and only ever exists in the local database.
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
    await addUser(page, u, 'Viewer');
    await page.goto(findUserUrl(u.email));
    await expect(row).toHaveCount(1);
  }
  const href = await row.locator('a.ats-row-link').getAttribute('href');
  if (!href) throw new Error(`No Users row for ${u.email}.`);

  await page.goto(href);
  // A fixture left invite-pending by an interrupted run: send a fresh invite and accept it.
  const resend = page.getByRole('button', { name: 'Resend invite' });
  if (await resend.count()) {
    await resend.click();
    await acceptConfirm(page);
    await expect(page.locator('.alert-success')).toBeVisible();
    await acceptLatestInvite(page, u.email);
    await page.goto(href);
  }
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
      // Never signed in, so the invite is left unaccepted.
      await inviteUser(page, { name: `E2E Pager ${n}`, email }, 'Viewer');
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
