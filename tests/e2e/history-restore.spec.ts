import { type Page } from '@playwright/test';
import { test, expect, acceptConfirm, dismissConfirm, openedConfirm } from './confirm';

/**
 * htmx snapshots the history element on every boosted navigation and puts it back on Back/Forward.
 * With no hx-history-elt that element is <body>, so a restore re-ran site.js (listeners bound twice)
 * and brought back a second #ats-confirm and any .modal-backdrop that was fading out when the
 * snapshot was taken. The pipeline delete below is always answered by a mocked redirect: no
 * pipeline is ever deleted.
 */
const DELETE = /\/Pipelines\/Delete\//;

async function mockDelete(page: Page): Promise<() => number> {
  let posts = 0;
  await page.route(DELETE, async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    posts++;
    await route.fulfill({ status: 302, headers: { Location: '/Pipelines' } });
  });
  return () => posts;
}

function deleteButton(page: Page) {
  return page.locator('#ats-content form[action*="/Pipelines/Delete/"] button[type="submit"]').first();
}

async function expectPageUsable(page: Page) {
  await expect(page.locator('.modal-backdrop')).toHaveCount(0);
  await expect(page.locator('body')).not.toHaveClass(/modal-open/);
  // trial: runs the actionability checks only, which fail if anything covers the link.
  await page.locator('#ats-sidebar a[href="/Jobs"]').click({ trial: true, timeout: 3000 });
}

async function boostTo(page: Page, href: string) {
  await page.locator(`#ats-sidebar a[href="${href}"]`).click();
  await page.waitForURL(new RegExp(`${href}$`));
  await expect(page.locator('#ats-content')).toHaveCount(1);
}

test('a page script inside #ats-content runs again when Back restores the page', async ({ page }) => {
  await page.goto('/Jobs');
  await page.locator('.ats-row-link').first().click();
  await page.waitForURL(/\/Board/);
  await boostTo(page, '/Candidates');
  await page.goBack();
  await page.waitForURL(/\/Board/);
  // The board's @section Scripts attaches Sortable to every column; restored markup alone has none.
  await expect.poll(() => page.evaluate(() => {
    const cols = Array.from(document.querySelectorAll('.ats-board-cards'));
    const S = (window as unknown as { Sortable: { get(el: Element): unknown } }).Sortable;
    return cols.length > 0 && cols.every((c) => !!S.get(c));
  })).toBe(true);
});

test('after Back there is still exactly one confirm dialog, and dismissing it leaves the page usable', async ({ page }) => {
  await page.goto('/Pipelines');
  await boostTo(page, '/Candidates');
  await page.goBack();
  await page.waitForURL(/\/Pipelines$/);

  await deleteButton(page).click();
  await openedConfirm(page, 'Delete this pipeline?');
  await expect(page.locator('#ats-confirm')).toHaveCount(1);
  await expect(page.locator('.modal.show')).toHaveCount(1);

  await dismissConfirm(page, undefined, 'escape');
  await expectPageUsable(page);
});

test('Back after confirming does not restore a modal backdrop', async ({ page }) => {
  const posts = await mockDelete(page);
  await page.goto('/Candidates');
  await boostTo(page, '/Pipelines');

  await deleteButton(page).click();
  await acceptConfirm(page, 'Delete this pipeline?');
  await expect.poll(posts).toBe(1);
  await expect(page.locator('#ats-content')).toHaveCount(1);

  // The boosted POST pushed /Pipelines again, and that snapshot was taken while the modal was still
  // fading out. Back restores exactly that entry.
  await page.evaluate(() => document.body.addEventListener('htmx:historyRestore',
    () => { (window as unknown as { restored: boolean }).restored = true; }, { once: true }));
  await page.goBack();
  await page.waitForFunction(() => (window as unknown as { restored?: boolean }).restored === true);
  await expect(page.locator('#ats-confirm')).toHaveCount(1);
  await expectPageUsable(page);
});

test('after several Back/Forward cycles one confirm sends exactly one request', async ({ page }) => {
  const posts = await mockDelete(page);
  await page.goto('/Pipelines');
  await boostTo(page, '/Candidates');
  for (let i = 0; i < 3; i++) {
    await page.goBack();
    await page.waitForURL(/\/Pipelines$/);
    await page.goForward();
    await page.waitForURL(/\/Candidates$/);
  }
  await page.goBack();
  await page.waitForURL(/\/Pipelines$/);

  await deleteButton(page).click();
  await acceptConfirm(page, 'Delete this pipeline?');
  await expect.poll(posts).toBe(1);
  await page.waitForTimeout(800);
  expect(posts(), 'one confirm must issue one request').toBe(1);
  await expect(page.locator('#ats-confirm')).toHaveCount(1);
  await expectPageUsable(page);
});
