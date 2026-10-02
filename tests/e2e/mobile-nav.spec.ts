import { expect, test, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

/**
 * Phones (< 768px): the sidebar is a compact app bar, and the nav plus the user block sit in an
 * off-canvas panel (#ats-nav-panel). The sidebar is re-rendered out of band on every boosted
 * navigation and restored from a snapshot on Back/Forward, so these tests lock that neither leaves
 * the panel open, a backdrop behind or the page scroll-locked.
 */
test.use({ viewport: { width: 375, height: 812 } });

const menu = (page: Page) => page.getByRole('button', { name: 'Open navigation' });
const panel = (page: Page) => page.locator('#ats-nav-panel');

async function expectClosedCleanly(page: Page) {
  await expect(panel(page)).not.toHaveClass(/\bshow\b/);
  await expect(panel(page)).toBeHidden();
  await expect(page.locator('.offcanvas-backdrop')).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe('');
  await expect(menu(page)).toHaveAttribute('aria-expanded', 'false');
}

async function openPanel(page: Page) {
  await menu(page).click();
  await expect(panel(page)).toBeVisible();
  await expect(panel(page)).toHaveClass(/\bshow\b/);
  await expect(menu(page)).toHaveAttribute('aria-expanded', 'true');
}

test('the page heading is visible without scrolling; the nav waits behind the menu button', async ({ page }) => {
  await page.goto('/Users');
  const h1 = page.locator('#ats-content h1');
  await expect(h1).toBeInViewport();
  const content = await page.locator('#ats-content').boundingBox();
  expect(content!.y, 'content should start right below the app bar and top bar').toBeLessThanOrEqual(130);

  await expect(panel(page)).toBeHidden();
  await expect(page.locator('#ats-sidebar').getByRole('link', { name: 'Jobs' })).toBeHidden();
  await expect(menu(page)).toHaveAttribute('aria-controls', 'ats-nav-panel');
  const box = await menu(page).boundingBox();
  expect(box!.width).toBeGreaterThanOrEqual(44);
  expect(box!.height).toBeGreaterThanOrEqual(44);
});

test('the panel holds the nav and the user block, and focus moves into it', async ({ page }) => {
  await page.goto('/Users');
  await openPanel(page);
  await expect(panel(page).getByRole('link', { name: 'Jobs' })).toBeVisible();
  await expect(panel(page).getByRole('link', { name: 'Change password' })).toBeVisible();
  await expect(panel(page).getByRole('button', { name: 'Sign out' })).toBeVisible();
  expect(await page.evaluate(() => document.getElementById('ats-nav-panel')!.contains(document.activeElement))).toBe(true);
  await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe('hidden');
});

test('a link in the panel navigates boosted and leaves the panel closed with no backdrop', async ({ page }) => {
  await page.goto('/Users');
  const loads: string[] = [];
  page.on('request', r => { if (r.resourceType() === 'document') loads.push(r.url()); });

  await openPanel(page);
  await panel(page).getByRole('link', { name: 'Jobs' }).click();
  await expect(page).toHaveURL(/\/Jobs$/);
  await expect(page.locator('#ats-content h1')).toHaveText('Jobs.');
  expect(loads, 'the navigation should be a boosted swap, not a page load').toEqual([]);
  await expectClosedCleanly(page);
  await expect(page.locator('#ats-content h1')).toBeFocused();

  // The panel opens again from the re-rendered menu button.
  await openPanel(page);
  await page.keyboard.press('Escape');
  await expectClosedCleanly(page);
});

test('Escape and the close button close the panel and return focus to the menu button', async ({ page }) => {
  await page.goto('/Users');
  await openPanel(page);
  await page.keyboard.press('Escape');
  await expectClosedCleanly(page);
  await expect(menu(page)).toBeFocused();

  await openPanel(page);
  await panel(page).getByRole('button', { name: 'Close navigation' }).click();
  await expectClosedCleanly(page);
  await expect(menu(page)).toBeFocused();
});

test('a backdrop click closes the panel', async ({ page }) => {
  await page.goto('/Users');
  await openPanel(page);
  await page.mouse.click(360, 400);
  await expectClosedCleanly(page);
});

test('Back with the panel open, or after navigating from it, never restores it open', async ({ page }) => {
  await page.goto('/Users');
  await openPanel(page);
  await panel(page).getByRole('link', { name: 'Jobs' }).click();
  await expect(page).toHaveURL(/\/Jobs$/);

  await page.goBack();
  await expect(page).toHaveURL(/\/Users$/);
  await expectClosedCleanly(page);

  await openPanel(page);
  await page.goForward();
  await expect(page).toHaveURL(/\/Jobs$/);
  await expectClosedCleanly(page);
  await page.locator('#ats-content h1').click(); // nothing left over blocks the page
});

test('the search field opens from the top bar on phones', async ({ page }) => {
  await page.goto('/Users');
  const search = page.locator('#ats-global-search');
  await expect(search).toBeHidden();
  const toggle = page.getByRole('button', { name: 'Search', exact: true });
  await toggle.click();
  await expect(search).toBeVisible();
  await expect(search).toBeFocused();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
});

test('Escape folds the opened search field on phones and returns focus to its toggle', async ({ page }) => {
  await page.goto('/Users');
  const search = page.locator('#ats-global-search');
  const toggle = page.getByRole('button', { name: 'Search', exact: true });
  await toggle.click();
  await expect(search).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(search).toBeHidden();
  await expect(toggle).toBeFocused();
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
});

test('Ctrl+K opens the folded search field on phones and focuses it', async ({ page }) => {
  await page.goto('/Users');
  const search = page.locator('#ats-global-search');
  await expect(search).toBeHidden();
  await page.locator('#ats-content h1').click();
  await page.keyboard.press('Control+K');
  await expect(search).toBeVisible();
  await expect(search).toBeFocused();
  await expect(page.getByRole('button', { name: 'Search', exact: true })).toHaveAttribute('aria-expanded', 'true');
});

test('widening past 768px with the panel open leaves the desktop sidebar and an unlocked page', async ({ page }) => {
  await page.goto('/Users');
  await openPanel(page);

  // Bootstrap hides an .offcanvas-md on resize once it stops being position: fixed.
  await page.setViewportSize({ width: 1024, height: 812 });
  await expect(page.locator('.offcanvas-backdrop')).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe('');
  await expect(panel(page)).not.toHaveClass(/\bshow\b/);
  await expect(menu(page)).toBeHidden();
  await expect(page.locator('#ats-sidebar').getByRole('link', { name: 'Jobs' })).toBeVisible();
  await page.locator('#ats-content h1').click(); // nothing left over blocks the page

  await page.setViewportSize({ width: 375, height: 812 });
  await expectClosedCleanly(page);
  await openPanel(page);
  await page.keyboard.press('Escape');
  await expectClosedCleanly(page);
});

test('axe passes with the panel open', async ({ page }) => {
  await page.goto('/Users');
  await openPanel(page);
  await page.waitForTimeout(400); // let the slide-in finish so colour contrast is measured on the final paint
  const { violations } = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  expect(violations.map(v => `${v.impact}:${v.id} ${v.nodes.map(n => n.target.join(' ')).slice(0, 3).join(', ')}`)).toEqual([]);
});

test('desktop keeps the full sidebar and no menu button', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/Users');
  await expect(menu(page)).toBeHidden();
  await expect(page.locator('#ats-sidebar').getByRole('link', { name: 'Jobs' })).toBeVisible();
  await expect(page.locator('#ats-global-search')).toBeVisible();
});
