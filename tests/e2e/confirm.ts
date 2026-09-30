import { test as base, expect, type Page } from '@playwright/test';

/**
 * Every hx-confirm goes through the themed #ats-confirm modal (site.js). A native browser dialog
 * means that interception broke, so the `test` exported here fails any test during which one fires.
 */
export const test = base.extend({
  page: async ({ page }, use) => {
    const seen = recordNativeDialogs(page);
    await use(page);
    expect(seen, 'a native browser dialog was shown').toEqual([]);
  },
});

export { expect };

/** Dismisses and records any native dialog, for pages created outside the `page` fixture. */
export function recordNativeDialogs(page: Page): string[] {
  const seen: string[] = [];
  page.on('dialog', (d) => {
    seen.push(d.message());
    void d.dismiss();
  });
  return seen;
}

export function confirmDialog(page: Page) {
  return page.locator('#ats-confirm');
}

/** Waits until the modal is fully shown (focus has moved in), checking its message if given. */
export async function openedConfirm(page: Page, message?: string) {
  const dialog = confirmDialog(page);
  await expect(dialog).toBeVisible();
  await expect(dialog.locator(':focus')).toHaveCount(1);
  if (message !== undefined) await expect(dialog.locator('#ats-confirm-message')).toHaveText(message);
  return dialog;
}

/** Waits for the confirm modal, checks its message if given, and clicks its confirm button. */
export async function acceptConfirm(page: Page, message?: string) {
  const dialog = await openedConfirm(page, message);
  await dialog.locator('[data-confirm-ok]').click();
  await expect(dialog).toBeHidden();
}

export type Dismissal = 'cancel' | 'escape' | 'backdrop';

/** Waits for the confirm modal, checks its message if given, and closes it without confirming. */
export async function dismissConfirm(page: Page, message?: string, how: Dismissal = 'cancel') {
  const dialog = await openedConfirm(page, message);
  if (how === 'cancel') await dialog.getByRole('button', { name: 'Cancel' }).click();
  else if (how === 'escape') await page.keyboard.press('Escape');
  // The .modal element covers the viewport around the dialog box; a click there is a backdrop click.
  else await dialog.click({ position: { x: 5, y: 5 } });
  await expect(dialog).toBeHidden();
}
