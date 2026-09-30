import { type Locator, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { test, expect, acceptConfirm } from './confirm';

/**
 * Row menus are Bootstrap dropdowns themed through --bs-dropdown-* in ats-tokens.css. Unthemed, a
 * focused or pressed item turns Bootstrap blue and the red Delete text on it becomes unreadable.
 */
const TITLE = `dropdown-theme-${Date.now()}`;
const BOOTSTRAP_BLUE = 'rgb(13, 110, 253)';

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => {
  // A draft job: never published, so nothing is pushed to ReferralTool.
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(TITLE);
  await page.locator('#Description').fill('Created by the dropdown-theme e2e spec.');
  await page.locator('#PipelineTemplateId').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job created.')).toBeVisible();
  await page.close();
});

test.afterAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  await page.goto(`/Jobs?q=${encodeURIComponent(TITLE)}`);
  const row = page.locator('.ats-trow').first();
  if ((await row.count()) > 0) {
    await row.locator('button', { hasText: 'Delete' }).dispatchEvent('click');
    await acceptConfirm(page);
    await expect(page.getByText('Job deleted.')).toBeVisible();
  }
  await page.close();
});

async function openDelete(page: Page): Promise<Locator> {
  await page.goto(`/Jobs?q=${encodeURIComponent(TITLE)}`);
  const row = page.locator('.ats-trow').first();
  await row.locator('[data-bs-toggle="dropdown"]').click();
  const menu = row.locator('.dropdown-menu.show');
  await expect(menu).toBeVisible();
  return menu.locator('.dropdown-item.text-danger');
}

async function expectThemedAndReadable(page: Page, item: Locator) {
  const bg = await item.evaluate((el) => getComputedStyle(el).backgroundColor);
  expect(bg, 'the Delete item uses Bootstrap blue').not.toBe(BOOTSTRAP_BLUE);
  const { violations } = await new AxeBuilder({ page })
    .include('.dropdown-menu.show')
    .withRules(['color-contrast'])
    .analyze();
  expect(violations.flatMap((v) => v.nodes.map((n) => n.target.join(' ')))).toEqual([]);
}

test('jobs row menu: the focused Delete item is themed and readable', async ({ page }) => {
  const item = await openDelete(page);
  await item.focus();
  await expect(item).toBeFocused();
  await expectThemedAndReadable(page, item);
});

test('jobs row menu: the hovered Delete item is themed and readable', async ({ page }) => {
  const item = await openDelete(page);
  await item.hover();
  await expectThemedAndReadable(page, item);
});

test('jobs row menu: the pressed Delete item is themed and readable', async ({ page }) => {
  const item = await openDelete(page);
  await item.hover();
  await page.mouse.down();
  await expectThemedAndReadable(page, item);
  // Release off the item so no click (and no confirm) fires.
  await page.mouse.move(1, 1);
  await page.mouse.up();
});
