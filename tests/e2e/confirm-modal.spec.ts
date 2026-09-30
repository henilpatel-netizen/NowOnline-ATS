import { type Page } from '@playwright/test';
import { test, expect, dismissConfirm, openedConfirm, type Dismissal } from './confirm';

/**
 * Every hx-confirm form opens the themed modal with its own text, and closing the modal any way other
 * than its confirm button sends nothing. Only dismissals happen here, so no data changes; confirming
 * is covered by the delete/remove specs. The `test` fixture fails on any native browser dialog.
 */
const PAGES = ['/Jobs', '/Candidates', '/Pipelines', '/Organisation', '/Integration'];
const WAYS: Dismissal[] = ['cancel', 'escape', 'backdrop'];

function countPosts(page: Page): () => number {
  let posts = 0;
  const onRequest = (r: { method(): string }) => { if (r.method() === 'POST') posts++; };
  page.on('request', onRequest);
  return () => { page.off('request', onRequest); return posts; };
}

test('every hx-confirm opens the themed modal, and dismissing it sends nothing', async ({ page }) => {
  let checked = 0;
  for (const url of PAGES) {
    await page.goto(url);
    const forms = page.locator('#ats-content form[hx-confirm]');
    const seen = new Set<string>();
    for (let i = 0; i < (await forms.count()); i++) {
      const form = forms.nth(i);
      const question = (await form.getAttribute('hx-confirm'))!;
      // A list repeats the same form per row; one of each kind is enough.
      if (seen.has(question)) continue;
      seen.add(question);
      const danger = (await form.getAttribute('data-confirm-variant')) === 'danger';
      const button = form.locator('button[type="submit"]');
      const posts = countPosts(page);

      // Menu items are hidden until their dropdown opens; the click is what matters here.
      await button.dispatchEvent('click');
      const dialog = await openedConfirm(page, question);
      await expect(page.getByRole('dialog', { name: (await form.getAttribute('data-confirm-title'))! })).toBeVisible();
      const ok = dialog.locator('[data-confirm-ok]');
      await expect(ok).toHaveText((await form.getAttribute('data-confirm-ok'))!);
      await expect(ok).toHaveClass(danger ? /btn-danger/ : /btn-primary/);
      // The safe choice has focus when the action is destructive.
      await expect(danger ? dialog.getByRole('button', { name: 'Cancel' }) : ok).toBeFocused();

      await dismissConfirm(page, undefined, WAYS[checked % WAYS.length]);
      await page.waitForTimeout(300);
      expect(posts(), `dismissing "${question}" must not post`).toBe(0);
      await expect(page).toHaveURL(new RegExp(`${url}$`));
      await expect(button).toBeEnabled();
      checked++;
    }
  }
  expect(checked, 'no hx-confirm form was found').toBeGreaterThan(0);
});

test('a second confirm while one is open is dropped, not queued', async ({ page }) => {
  await page.goto('/Integration');
  const form = page.locator('#ats-content form[hx-confirm]').first();
  const question = (await form.getAttribute('hx-confirm'))!;
  const posts = countPosts(page);
  await form.locator('button[type="submit"]').dispatchEvent('click');
  await openedConfirm(page, question);

  // Submitted behind the open modal with a different question: site.js must drop it, so the modal
  // keeps the first question and the first request.
  await form.evaluate((f) => {
    f.setAttribute('hx-confirm', 'A second question');
    (f as HTMLFormElement).requestSubmit();
  });
  await page.waitForTimeout(300);
  await dismissConfirm(page, question);
  await page.waitForTimeout(600);
  await expect(page.locator('#ats-confirm')).toBeHidden();
  expect(posts(), 'neither submit may post').toBe(0);
});

test('a confirm that arrives while the modal fades out is shown once it has closed', async ({ page }) => {
  await page.goto('/Integration');
  const form = page.locator('#ats-content form[hx-confirm]').first();
  const question = (await form.getAttribute('hx-confirm'))!;
  const posts = countPosts(page);
  await form.locator('button[type="submit"]').dispatchEvent('click');
  const dialog = await openedConfirm(page, question);

  // Cancel starts the fade-out; the second submit lands in the same task, before it can finish.
  await dialog.evaluate((d, f) => {
    (d.querySelector('[data-confirm-cancel]') as HTMLButtonElement).click();
    const el = document.querySelector(f) as HTMLFormElement;
    el.setAttribute('hx-confirm', 'A second question');
    el.requestSubmit();
  }, '#ats-content form[hx-confirm]');

  await openedConfirm(page, 'A second question');
  await dismissConfirm(page, 'A second question');
  await page.waitForTimeout(300);
  expect(posts(), 'neither dismissal may post').toBe(0);
});
