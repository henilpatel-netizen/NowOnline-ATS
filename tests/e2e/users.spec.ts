import { type Browser, type Page } from '@playwright/test';
import { test, expect, acceptConfirm } from './confirm';
import { PAGER_FIXTURE_COUNT, PAGER_FIXTURE_PREFIX, ensurePagerFixtures, findUserUrl, otherEditUrl, ownRow } from './users';

// Creates real users in the local dev database (deactivated at the end). Passwords are generated per
// run and exist only in this local database.
test.describe.configure({ mode: 'serial' });

// Same origin the config uses; afterAll cannot rely on test.info().
const baseURL = process.env.ATS_BASE_URL ?? 'https://localhost:7044';
const run = Date.now();
const TEMP = `Temp-${run}-pass`;
const NEW = `New-${run}-password`;
const RESET = `Reset-${run}-pass`;
const viewer = { name: `E2E Viewer ${run}`, email: `viewer-${run}@example.test` };
const recruiter = { name: `E2E Recruiter ${run}`, email: `recruiter-${run}@example.test` };
const editee = { name: `E2E Editee ${run}`, email: `editee-${run}@example.test` };
const edited = { name: `E2E Edited ${run}`, email: `edited-${run}@example.test` };

async function addUser(page: Page, u: { name: string; email: string }, role: string) {
  await page.goto('/Users/Create');
  await page.locator('#DisplayName').fill(u.name);
  await page.locator('#Email').fill(u.email);
  await page.locator('#Role').selectOption(role);
  await page.locator('#TemporaryPassword').fill(TEMP);
  await page.getByRole('button', { name: 'Add user' }).click();
  await expect(page.locator('.alert-success')).toBeVisible();
  await page.goto(findUserUrl(u.email));
  await expect(page.locator('.ats-trow', { hasText: u.email })).toHaveCount(1);
}

async function signInAs(browser: Browser, email: string, password: string) {
  const ctx = await browser.newContext({ baseURL, ignoreHTTPSErrors: true });
  const page = await ctx.newPage();
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(email);
  await page.locator('#Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  return page;
}

async function setOwnPassword(page: Page) {
  await expect(page).toHaveURL(/\/Profile\/ChangePassword/);
  await page.locator('#CurrentPassword').fill(TEMP);
  await page.locator('#NewPassword').fill(NEW);
  await page.locator('#ConfirmPassword').fill(NEW);
  await page.getByRole('button', { name: 'Change password' }).click();
  await expect(page.locator('#ats-sidebar')).toBeVisible();
  await expect(page).not.toHaveURL(/\/Profile\/ChangePassword/);
}

// Every user change lives on the Edit page; the list row only links to it.
async function openEdit(page: Page, u: { name: string; email: string }) {
  await page.goto(findUserUrl(u.email));
  const row = page.locator('.ats-trow', { hasText: u.email });
  await row.getByRole('link', { name: u.name, exact: true }).click();
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
  await expect(page.locator('h1')).toHaveText(/Edit user/);
}

async function deactivate(page: Page, u: { name: string; email: string }) {
  await openEdit(page, u);
  await page.getByRole('button', { name: 'Deactivate' }).click();
  await acceptConfirm(page, `Deactivate ${u.name}? They are signed out at once and cannot sign in until reactivated.`);
  await expect(page.getByText(`${u.name} deactivated and signed out.`)).toBeVisible();
}

const summary = (page: Page) => page.getByRole('region', { name: 'User summary' });
const chip = (page: Page, name: string) => page.locator('.ats-filter-group').getByRole('link', { name, exact: true });
const listCount = (page: Page) => page.locator('.ats-toolbar .ms-auto');

test('the list shows active users by default; deactivated ones only under Deactivated or All', async ({ page }) => {
  await otherEditUrl(page, 'deactivated');
  await otherEditUrl(page, 'active');
  const gone = 'e2e-fixture-deactivated@example.test';

  await page.goto('/Users');
  await expect(chip(page, 'Active')).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('.ats-trow').getByText('Deactivated', { exact: true })).toHaveCount(0);

  await page.goto(`/Users?q=${gone}`);
  await expect(page.locator('.ats-trow', { hasText: gone })).toHaveCount(0);
  await expect(page.getByText('No users match.')).toBeVisible();
  await expect(page.getByText('Try clearing the search or the filter.')).toBeVisible();
  await expect(listCount(page)).toHaveText('0 users');
  // The eyebrow counts the tenant's active users (you, at least), not the filtered rows.
  await expect(page.locator('.ats-eyebrow')).toHaveText(/^[1-9]\d* active:$/);

  await chip(page, 'Deactivated').click();
  await expect(page).toHaveURL(/status=Deactivated/);
  await expect(page.locator('.ats-trow', { hasText: gone })).toHaveCount(1);
  await expect(page.locator('#ats-content input[name="q"]')).toHaveValue(gone);
  await expect(listCount(page)).toHaveText('1 user');
});

test('search matches part of an email, any case, and survives switching filters', async ({ page }) => {
  await otherEditUrl(page, 'active');
  const active = 'e2e-fixture-active@example.test';
  await page.goto('/Users');
  const box = page.locator('#ats-content').getByLabel('Search users');
  await box.fill('  FIXTURE-ACTIVE@EXAMPLE  ');
  await box.press('Enter');
  await expect(page).toHaveURL(/q=/);
  await expect(page.locator('.ats-trow')).toHaveCount(1);
  await expect(page.locator('.ats-trow', { hasText: active })).toHaveCount(1);

  await chip(page, 'All').click();
  await expect(page).toHaveURL(/status=All/);
  await expect(page.locator('.ats-trow', { hasText: active })).toHaveCount(1);
  await expect(box).toHaveValue('  FIXTURE-ACTIVE@EXAMPLE  ');

  // Searching from a filtered view keeps the filter: the form carries the status.
  await box.fill('e2e-fixture-deactivated');
  await box.press('Enter');
  await expect(page).toHaveURL(/status=All/);
  await expect(page.locator('.ats-trow', { hasText: 'e2e-fixture-deactivated@example.test' })).toHaveCount(1);
});

test('the list pages at 20 and the pager keeps the search and the filter', async ({ page }) => {
  test.setTimeout(180_000); // the first run creates the fixtures
  await ensurePagerFixtures(page);

  await page.goto(`/Users?status=Deactivated&q=${PAGER_FIXTURE_PREFIX}`);
  await expect(listCount(page)).toHaveText(`${PAGER_FIXTURE_COUNT} users`);
  await expect(page.locator('.ats-trow')).toHaveCount(20);
  await expect(page.getByText('Page 1 of 2')).toBeVisible();

  await page.getByRole('link', { name: 'Next page' }).click();
  await expect(page.getByText('Page 2 of 2')).toBeVisible();
  await expect(page).toHaveURL(/page=2/);
  await expect(page.locator('.ats-trow')).toHaveCount(PAGER_FIXTURE_COUNT - 20);
  await expect(page.locator('#ats-content input[name="q"]')).toHaveValue(PAGER_FIXTURE_PREFIX);
  await expect(chip(page, 'Deactivated')).toHaveAttribute('aria-current', 'page');

  await page.getByRole('link', { name: 'Previous page' }).click();
  await expect(page.getByText('Page 1 of 2')).toBeVisible();
  await expect(page.locator('.ats-trow')).toHaveCount(20);
});

test('owner adds users; every role is offered', async ({ page }) => {
  await page.goto('/Users/Create');
  await expect(page.locator('#Role option')).toHaveText(['Owner', 'Recruiter', 'Hiring manager', 'Viewer']);
  await expect(page.locator('#Role option[value="HiringManager"]')).toHaveText('Hiring manager');
  await addUser(page, viewer, 'Viewer');
  await addUser(page, recruiter, 'Recruiter');
  await addUser(page, editee, 'Viewer');
});

test('every row, the own one included, links to Edit by name and by the edit icon', async ({ page }) => {
  const own = await ownRow(page);
  await expect(page.locator('.ats-trow .dropdown')).toHaveCount(0);
  await expect(own).toHaveCount(1);
  await expect(own.getByRole('link', { name: /^Edit / })).toHaveCount(1);
  await page.goto(findUserUrl(viewer.email));
  const row = page.locator('.ats-trow', { hasText: viewer.email });
  await row.getByRole('link', { name: `Edit ${viewer.name}` }).click();
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
  await expect(summary(page).getByText(viewer.email)).toBeVisible();
  await expect(summary(page).getByText('Active', { exact: true })).toBeVisible();
});

test('viewer must set a password, then is read-only', async ({ page: owner, browser }) => {
  const otherUserEdit = await otherEditUrl(owner, 'active');
  const page = await signInAs(browser, viewer.email, TEMP);
  await expect(page).toHaveURL(/\/Profile\/ChangePassword/);
  await page.goto('/Jobs'); // held on the change page
  await expect(page).toHaveURL(/\/Profile\/ChangePassword/);

  // While held the shell offers nothing that would bounce back here: no nav, no search, no bell.
  const sidebar = page.locator('#ats-sidebar');
  await expect(sidebar.locator('nav')).toHaveCount(0);
  await expect(sidebar.getByRole('link', { name: 'Jobs' })).toHaveCount(0);
  await expect(page.locator('#ats-global-search')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Notifications' })).toHaveCount(0);
  await expect(sidebar.getByRole('button', { name: 'Sign out' })).toBeVisible();

  // A non-boosted htmx request while held gets an HX-Redirect to the change page, not the page itself.
  const held = await page.request.get('/Search?q=abc', { headers: { 'HX-Request': 'true' }, maxRedirects: 0 });
  expect(held.status()).toBe(204);
  expect(held.headers()['hx-redirect']).toBe('/Profile/ChangePassword');

  // Phones: the navigation panel then holds only the user block, so Sign out stays one tap away.
  await page.setViewportSize({ width: 375, height: 812 });
  await expect(page.locator('#ats-content h1')).toBeInViewport();
  await page.getByRole('button', { name: 'Open navigation' }).click();
  const panel = page.locator('#ats-nav-panel');
  await expect(panel).toHaveClass(/\bshow\b/); // slide-in finished, focus is inside
  await expect(panel.getByRole('button', { name: 'Sign out' })).toBeVisible();
  await expect(panel.locator('nav')).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();
  await expect(page.locator('.offcanvas-backdrop')).toHaveCount(0);
  await page.setViewportSize({ width: 1280, height: 720 });

  await setOwnPassword(page);

  // The boosted redirect re-renders the sidebar and top bar out of band, so the full shell is back.
  await expect(sidebar.getByRole('link', { name: 'Jobs' })).toBeVisible();
  await expect(page.locator('#ats-global-search')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Notifications' })).toBeVisible();

  const nav = page.locator('#ats-sidebar');
  for (const hidden of ['Integrations', 'Audit log', 'Users', 'Pipelines', 'Organisation']) {
    await expect(nav.getByRole('link', { name: hidden })).toHaveCount(0);
  }
  await page.goto('/Jobs');
  await expect(page.getByRole('link', { name: 'New job' })).toHaveCount(0);
  // Without jobs.manage the row menu would hold only Open board, which the title already links to.
  await expect(page.getByRole('button', { name: 'Actions' })).toHaveCount(0);
  await page.goto('/Candidates');
  await expect(page.getByRole('link', { name: 'Add candidate' })).toHaveCount(0);

  for (const url of ['/Integration', '/Audit', '/Users', otherUserEdit, '/Jobs/Create', '/Pipelines']) {
    const res = await page.goto(url);
    expect(res?.status(), url).toBe(403);
  }
  await page.context().close();
});

test('recruiter can manage jobs but not admin screens', async ({ browser }) => {
  const page = await signInAs(browser, recruiter.email, TEMP);
  await setOwnPassword(page);
  await page.goto('/Jobs');
  await expect(page.getByRole('link', { name: 'New job' })).toBeVisible();
  // With jobs.manage every row keeps its menu.
  await expect(page.getByRole('button', { name: 'Actions' })).toHaveCount(await page.locator('.ats-trow').count());
  await expect(page.locator('#ats-sidebar').getByRole('link', { name: 'Pipelines' })).toBeVisible();
  expect((await page.goto('/Integration'))?.status()).toBe(403);
  expect((await page.goto('/Users'))?.status()).toBe(403);
  await page.context().close();
});

test('deactivating signs the user out at once', async ({ page, browser }) => {
  const viewerPage = await signInAs(browser, viewer.email, NEW);
  await expect(viewerPage.locator('#ats-sidebar')).toBeVisible();

  await deactivate(page, viewer);
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
  await expect(summary(page).getByText('Deactivated')).toBeVisible();
  // A deactivated user offers only Reactivate; the details stay editable.
  await expect(page.getByRole('button', { name: 'Reactivate' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reset password' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Deactivate' })).toHaveCount(0);
  await expect(page.locator('#Details_Email')).toBeEditable();

  await viewerPage.goto('/Dashboard');
  await expect(viewerPage).toHaveURL(/\/Account\/Login/);
  await viewerPage.locator('#Email').fill(viewer.email);
  await viewerPage.locator('#Password').fill(NEW);
  await viewerPage.getByRole('button', { name: 'Sign in' }).click();
  await expect(viewerPage.getByText('This account is not available')).toBeVisible();
  await viewerPage.context().close();
});

test('reactivating lets the user sign in again', async ({ page, browser }) => {
  await openEdit(page, viewer);
  await page.getByRole('button', { name: 'Reactivate' }).click();
  await acceptConfirm(page, `Reactivate ${viewer.name}? They can sign in again with their existing password.`);
  await expect(page.getByText(`${viewer.name} reactivated.`)).toBeVisible();
  await expect(summary(page).getByText('Active', { exact: true })).toBeVisible();

  const viewerPage = await signInAs(browser, viewer.email, NEW);
  await expect(viewerPage.locator('#ats-sidebar')).toBeVisible();
  await viewerPage.context().close();

  await deactivate(page, viewer);
});

// A second tab still showing the old state posts a stale form: nothing changes, so no audit entry.
test('repeating a deactivate or reactivate from a stale page is a no-op with an info message', async ({ page }) => {
  const audited = async (summary: string) => {
    const audit = await page.context().newPage();
    await audit.goto(`/Audit?q=${encodeURIComponent(viewer.email)}`);
    const n = await audit.getByText(summary, { exact: true }).count();
    await audit.close();
    return n;
  };
  const other = await page.context().newPage();

  await openEdit(page, viewer); // deactivated: shows Reactivate
  await openEdit(other, viewer);
  await other.getByRole('button', { name: 'Reactivate' }).click();
  await acceptConfirm(other);
  await expect(other.getByText(`${viewer.name} reactivated.`)).toBeVisible();
  const reactivations = await audited(`Reactivated '${viewer.email}'`);
  await page.getByRole('button', { name: 'Reactivate' }).click();
  await acceptConfirm(page);
  await expect(page.locator('.alert-info')).toContainText(`${viewer.name} is already active.`);
  expect(await audited(`Reactivated '${viewer.email}'`)).toBe(reactivations);

  await openEdit(page, viewer); // active: shows Deactivate
  await deactivate(other, viewer);
  const deactivations = await audited(`Deactivated '${viewer.email}'`);
  await page.getByRole('button', { name: 'Deactivate' }).click();
  await acceptConfirm(page);
  await expect(page.locator('.alert-info')).toContainText(`${viewer.name} is already deactivated.`);
  expect(await audited(`Deactivated '${viewer.email}'`)).toBe(deactivations);
  await other.close();
});

test("owner's own Edit page allows the name only, links to Change password and has no user actions", async ({ page }) => {
  const openOwn = async () => {
    const own = await ownRow(page);
    await own.getByRole('link', { name: /^Edit / }).click();
    await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
  };
  await openOwn();
  await expect(summary(page).getByText('You', { exact: true })).toBeVisible();

  const form = page.locator('form[action*="/Users/Edit/"]');
  await expect(form.locator('input:not([type=hidden]), select, textarea')).toHaveCount(1);
  await expect(page.locator('#Details_DisplayName')).toBeEditable();
  await expect(page.getByText('Your role and email can only be changed by another Owner.')).toBeVisible();
  // Read-only email and role are a definition list, so each label is tied to its value.
  await expect(page.locator('#ats-content dl dt')).toHaveText(['Email', 'Role']);
  await expect(page.getByRole('definition').first()).not.toBeEmpty();

  const security = page.getByRole('region', { name: 'Security.' });
  await expect(security.getByRole('heading', { name: 'Password' })).toBeVisible();
  await expect(security.getByRole('link', { name: 'Change password' })).toHaveAttribute('href', '/Profile/ChangePassword');
  await expect(security).not.toHaveClass(/ats-danger-zone/);
  await expect(page.getByText('Changing the email or role signs the user out.')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: /Danger zone|Account access/ })).toHaveCount(0);
  for (const name of ['Reset password', 'Deactivate', 'Reactivate']) {
    await expect(page.getByRole('button', { name })).toHaveCount(0);
  }

  // Saving with nothing changed is a no-op, not an update.
  const original = await page.locator('#Details_DisplayName').inputValue();
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('No changes to save.')).toBeVisible();
  await expect(page.getByText(`${original} updated.`)).toHaveCount(0);

  // A changed own name is re-issued into the cookie, so the sidebar shows it straight away.
  const renamed = `${original} E2E`;
  try {
    await page.locator('#Details_DisplayName').fill(renamed);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText(`${renamed} updated.`, { exact: true })).toBeVisible();
    await expect(page.locator('.ats-sidebar-user-name')).toHaveText(renamed);
    await page.goto('/Dashboard');
    await expect(page.locator('#ats-sidebar')).toBeVisible();
  } finally {
    await openOwn();
    await page.locator('#Details_DisplayName').fill(original);
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.locator('.ats-sidebar-user-name')).toHaveText(original);
  }
});

test('owner edits a name and email: the open session ends and the new email signs in', async ({ page, browser }) => {
  const editeePage = await signInAs(browser, editee.email, TEMP);
  await expect(editeePage).toHaveURL(/\/Profile\/ChangePassword/);

  await openEdit(page, editee);
  await expect(page.getByText('Changing the email or role signs the user out.')).toBeVisible();
  await page.locator('#Details_DisplayName').fill(edited.name);
  await page.locator('#Details_Email').fill(edited.email.toUpperCase());
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText(`${edited.name} updated. They are signed out.`)).toBeVisible();
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
  await expect(summary(page).getByText(edited.name)).toBeVisible();
  await expect(summary(page).getByText(edited.email)).toBeVisible();

  await page.goto(`/Audit?q=${encodeURIComponent(editee.email)}`);
  await expect(page.getByText(`Updated '${editee.email}': email to '${edited.email}', name`)).toBeVisible();

  await editeePage.goto('/Profile/ChangePassword');
  await expect(editeePage).toHaveURL(/\/Account\/Login/);
  await editeePage.context().close();

  const old = await signInAs(browser, editee.email, TEMP);
  await expect(old).toHaveURL(/\/Account\/Login/);
  await old.context().close();
  const renamed = await signInAs(browser, edited.email, TEMP);
  await expect(renamed).toHaveURL(/\/Profile\/ChangePassword/);
  await renamed.context().close();
});

test('a role change signs an open session out at once', async ({ page, browser }) => {
  const recruiterPage = await signInAs(browser, recruiter.email, NEW);
  await expect(recruiterPage.locator('#ats-sidebar')).toBeVisible();

  await openEdit(page, recruiter);
  await page.locator('#Details_Role').selectOption('Viewer');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText(`${recruiter.name} updated. They are signed out.`)).toBeVisible();
  await expect(summary(page).getByText('Viewer', { exact: true })).toBeVisible();

  await recruiterPage.goto('/Dashboard');
  await expect(recruiterPage).toHaveURL(/\/Account\/Login/);
  await recruiterPage.context().close();
});

test('an admin password reset signs an open session out and forces a change at next sign-in', async ({ page, browser }) => {
  const openPage = await signInAs(browser, recruiter.email, NEW);
  await expect(openPage.locator('#ats-sidebar')).toBeVisible();

  await openEdit(page, recruiter);
  await page.locator('#Reset_TemporaryPassword').fill(RESET);
  await page.getByRole('button', { name: 'Reset password' }).click();
  await acceptConfirm(page, `Reset the password for ${recruiter.name}? They are signed out at once.`);
  await expect(page.getByText(`Password reset for ${recruiter.name}. They are signed out and must set a new one at next sign-in.`)).toBeVisible();
  await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);

  await openPage.goto('/Dashboard');
  await expect(openPage).toHaveURL(/\/Account\/Login/);
  await openPage.context().close();

  const recruiterPage = await signInAs(browser, recruiter.email, RESET);
  await expect(recruiterPage).toHaveURL(/\/Profile\/ChangePassword/);
  await expect(recruiterPage.locator('#CurrentPassword')).toBeVisible();
  await recruiterPage.goto('/Jobs');
  await expect(recruiterPage).toHaveURL(/\/Profile\/ChangePassword/);
  await recruiterPage.context().close();
});

test('a rejected reset redirects back to Edit, so a refresh does not re-post', async ({ page }) => {
  await openEdit(page, recruiter);
  const editUrl = page.url();
  // Whitespace passes the browser's required check but not the server's.
  await page.locator('#Reset_TemporaryPassword').fill('   ');
  await page.getByRole('button', { name: 'Reset password' }).click();
  await acceptConfirm(page);
  await expect(page.locator('.alert-danger')).toContainText('Enter a temporary password.');
  expect(page.url()).toBe(editUrl);
  await expect(page.locator('#Reset_TemporaryPassword')).toHaveValue('');

  const res = await page.reload();
  expect(res?.status()).toBe(200);
  await expect(page.locator('h1')).toHaveText(/Edit user/);
  await expect(page.locator('.alert-danger')).toHaveCount(0);
});

test.afterAll(async ({ browser }) => {
  // Deactivate every test user that is still active, so repeated runs do not pile up active users.
  // The editee is listed under both emails: which one it has depends on how far the run got.
  const ctx = await browser.newContext({ baseURL, ignoreHTTPSErrors: true, storageState: 'tests/e2e/.auth/user.json' });
  const page = await ctx.newPage();
  for (const u of [viewer, recruiter, editee, edited]) {
    await page.goto(findUserUrl(u.email));
    const row = page.locator('.ats-trow', { hasText: u.email });
    if (!(await row.count())) continue;
    await row.locator('a.ats-row-link').click();
    await expect(page).toHaveURL(/\/Users\/Edit\/\d+$/);
    if (!(await page.getByRole('button', { name: 'Deactivate' }).count())) continue;
    await page.getByRole('button', { name: 'Deactivate' }).click();
    await acceptConfirm(page);
    await expect(page.getByText(`${u.name} deactivated and signed out.`)).toBeVisible();
  }
  await ctx.close();
});
