import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

/**
 * Bootstrap's status colours (text-danger, alert-*, field errors) are remapped to NowOnline tokens
 * in ats-tokens.css. Unmapped, they fall back to Bootstrap's own palette, e.g. #dc3545.
 */
const BOOTSTRAP_DANGER = 'rgb(220, 53, 69)';

/** Resolves a custom property to the rgb() string the browser computes for it. */
async function tokenColour(page: Page, token: string) {
  return page.evaluate((t) => {
    const probe = document.createElement('span');
    probe.style.color = `var(${t})`;
    document.body.append(probe);
    const colour = getComputedStyle(probe).color;
    probe.remove();
    return colour;
  }, token);
}

test('alerts and text-danger use the token colours', async ({ page }) => {
  await page.goto('/');
  await page.locator('#ats-content').evaluate((main) => {
    main.insertAdjacentHTML('afterbegin', `
      <div id="status-probe">
        ${['danger', 'success', 'warning', 'info'].map((t) =>
          `<div class="alert alert-${t}" role="alert" data-tone="${t}">A ${t} message.</div>`).join('')}
        <span class="text-danger" data-probe="text-danger">Danger text.</span>
      </div>`);
  });

  for (const tone of ['danger', 'success', 'warning', 'info']) {
    const alert = page.locator(`#status-probe .alert-${tone}`);
    await expect(alert).toHaveCSS('color', await tokenColour(page, `--no-${tone}-ink`));
    await expect(alert).toHaveCSS('background-color', await tokenColour(page, `--no-${tone}-soft`));
  }
  const text = page.locator('[data-probe="text-danger"]');
  await expect(text).toHaveCSS('color', await tokenColour(page, '--no-danger-ink'));
  await expect(text).not.toHaveCSS('color', BOOTSTRAP_DANGER);

  const { violations } = await new AxeBuilder({ page })
    .include('#status-probe')
    .withRules(['color-contrast'])
    .analyze();
  expect(violations).toEqual([]);
});

test.describe('anonymous', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('a validation message and the invalid border use the danger ink', async ({ page }) => {
    await page.goto('/Account/Login');
    await page.getByRole('button', { name: 'Sign in' }).click();

    const ink = await tokenColour(page, '--no-danger-ink');
    const message = page.locator('[data-valmsg-for="Email"]');
    await expect(message).toHaveText('Enter an email address.');
    await expect(message).toHaveCSS('color', ink);
    await expect(message).not.toHaveCSS('color', BOOTSTRAP_DANGER);
    await page.locator('#Password').focus();
    await expect(page.locator('#Email')).toHaveCSS('border-top-color', ink);
  });
});
