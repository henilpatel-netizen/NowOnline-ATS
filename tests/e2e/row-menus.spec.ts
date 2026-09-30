import { type Locator, type Page } from '@playwright/test';
import { test, expect, acceptConfirm } from './confirm';

/**
 * Row action menus must never be clipped by the card or covered by a later row. Both bugs pass a
 * plain toBeVisible(), so every control is checked with elementFromPoint at its centre: that is
 * what a mouse click would actually hit.
 */
const RUN = `menu-${Date.now()}`;
const JOBS = [`${RUN} Alpha`, `${RUN} Beta`];

test.describe.configure({ mode: 'serial' });

// Each run creates at most a few candidates; more loop turns than this means the cleanup is stuck.
const MAX_CLEANUP = 10;

async function createJob(page: Page, title: string) {
  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(title);
  await page.locator('#Description').fill('Created by the row-menu e2e spec.');
  await page.locator('#PipelineTemplateId').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job created.')).toBeVisible();
}

async function createCandidate(page: Page, last: string) {
  await page.goto('/Candidates/Create');
  await page.locator('#FirstName').fill('Menu');
  await page.locator('#LastName').fill(last);
  await page.locator('#Email').fill(`${last}@example.com`);
  await page.getByRole('button', { name: /save|create/i }).first().click();
  await expect(page.getByText('Candidate created.')).toBeVisible();
}

async function openMenu(row: Locator): Promise<Locator> {
  await row.locator('[data-bs-toggle="dropdown"]').click();
  const menu = row.locator('.dropdown-menu.show');
  await expect(menu).toBeVisible();
  return menu;
}

async function expectTopmost(controls: Locator) {
  const count = await controls.count();
  expect(count, 'the open menu has no controls').toBeGreaterThan(0);
  for (let i = 0; i < count; i++) {
    const control = controls.nth(i);
    await expect(control).toBeVisible();
    // The centre plus both ends: the next row's "..." button sits under the right-hand end of a
    // right-aligned menu, where a centre-only probe never looks.
    const hit = await control.evaluate((el) => {
      const r = el.getBoundingClientRect();
      const y = r.top + r.height / 2;
      const label = (el.textContent ?? '').trim() || el.getAttribute('aria-label');
      for (const x of [r.left + r.width / 2, r.left + 3, r.right - 3]) {
        const top = document.elementFromPoint(x, y);
        if (top === null || (top !== el && !el.contains(top))) {
          return { ok: false, label, covered: top ? `${top.tagName}.${top.className}` : 'nothing (outside the viewport)' };
        }
      }
      return { ok: true, label, covered: '' };
    });
    expect(hit.ok, `"${hit.label}" is covered by ${hit.covered}`).toBe(true);
  }
}

test.beforeAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  for (const title of JOBS) await createJob(page, title);

  // "Add to job" only renders when a published job exists. Publishing is setup here, not the
  // behaviour under test, so the button is triggered directly rather than through the menu.
  await page.goto(`/Jobs?q=${encodeURIComponent(JOBS[0])}`);
  await page.locator('.ats-trow').first().locator('button', { hasText: 'Publish' }).dispatchEvent('click');
  await acceptConfirm(page);
  await expect(page.getByText('Job published.')).toBeVisible();

  for (const n of [1, 2]) await createCandidate(page, `${RUN}-${n}`);
  await page.close();
});

// The published job is pushed to ReferralTool and listed on the career site, so every job this spec
// created is deleted again (a published one goes out as Inactive), and so are its candidates.
test.afterAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  const people = `/Candidates?q=${encodeURIComponent(RUN)}`;
  // Bounded, so a delete that silently stops working fails here instead of hanging the run.
  for (let i = 0; ; i++) {
    expect(i, 'candidate cleanup did not finish').toBeLessThan(MAX_CLEANUP);
    await page.goto(people);
    const row = page.locator('.ats-trow').first();
    if ((await row.count()) === 0) break;
    await row.locator('button', { hasText: 'Delete' }).dispatchEvent('click');
    await acceptConfirm(page);
    await expect(page.getByText('Candidate deleted.')).toBeVisible();
  }
  const list = `/Jobs?q=${encodeURIComponent(RUN)}`;
  for (let i = 0; i < JOBS.length; i++) {
    await page.goto(list);
    const row = page.locator('.ats-trow').first();
    if ((await row.count()) === 0) break;
    await row.locator('button', { hasText: 'Delete' }).dispatchEvent('click');
    await acceptConfirm(page);
    await expect(page.getByText('Job deleted.')).toBeVisible();
  }
  await page.goto(list);
  await expect(page.locator('.ats-trow')).toHaveCount(0);
  await page.close();
});

test('jobs: first and last row menus sit above every row and outside the card', async ({ page }) => {
  await page.goto(`/Jobs?q=${encodeURIComponent(RUN)}`);
  const rows = page.locator('.ats-trow');
  await expect(rows).toHaveCount(2);

  const first = await openMenu(rows.first());
  await expectTopmost(first.locator('.dropdown-item'));
  await page.keyboard.press('Escape');
  await expect(first).toBeHidden();

  const lastTitle = (await rows.last().locator('.ats-row-link').textContent())!.trim();
  const last = await openMenu(rows.last());
  await expectTopmost(last.locator('.dropdown-item'));
  await last.getByRole('link', { name: 'Edit' }).click();
  await expect(page).toHaveURL(/\/Jobs\/Edit\/\d+/);
  await expect(page.locator('#Title')).toHaveValue(lastTitle);
});

test('jobs: the last row of the unfiltered list is not clipped', async ({ page }) => {
  await page.goto('/Jobs');
  const menu = await openMenu(page.locator('.ats-trow').last());
  await expectTopmost(menu.locator('.dropdown-item'));
});

test('jobs: the row menu still works from the keyboard', async ({ page }) => {
  await page.goto(`/Jobs?q=${encodeURIComponent(RUN)}`);
  const row = page.locator('.ats-trow').last();
  await row.locator('[data-bs-toggle="dropdown"]').focus();
  await page.keyboard.press('Enter');
  await page.keyboard.press('ArrowDown');
  await expect(row.locator('.dropdown-menu.show .dropdown-item').first()).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(row.locator('.dropdown-menu.show')).toHaveCount(0);
});

test('candidates: a single row "Add to job" menu is not clipped', async ({ page }) => {
  // One row leaves no room inside the card above or below, so a flip cannot hide clipping.
  await page.goto(`/Candidates?q=${encodeURIComponent(`${RUN}-1`)}`);
  const rows = page.locator('.ats-trow');
  await expect(rows).toHaveCount(1);
  const menu = await openMenu(rows.first());
  await expectTopmost(menu.locator('select, button'));
});

test('candidates: the last row "Add to job" menu is usable', async ({ page }) => {
  await page.goto(`/Candidates?q=${encodeURIComponent(RUN)}`);
  const rows = page.locator('.ats-trow');
  await expect(rows).toHaveCount(2);

  const menu = await openMenu(rows.last());
  await expectTopmost(menu.locator('select, button'));

  const jobId = await menu.locator('option', { hasText: JOBS[0] }).getAttribute('value');
  await menu.locator('select').selectOption(jobId!);
  await expect(menu).toBeVisible();
  await menu.getByRole('button', { name: 'Add' }).click();
  await expect(page).toHaveURL(/\/Board/);
  await expect(page.getByText('Candidate added to job.')).toBeVisible();
});
