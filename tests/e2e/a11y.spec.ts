import { type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { test, expect, acceptConfirm } from './confirm';

// Closes the Phase 6 exit criterion that could not be verified without a scanner.
const SCREENS = [
  '/',
  '/Jobs',
  '/Jobs/Create',
  '/Candidates',
  '/Pipelines',
  '/Pipelines/Create',
  '/Departments',
  '/Locations',
  '/Integration',
  '/Integration/Deliveries',
  '/Audit',
];

async function expectNoViolations(page: Page, label: string) {
  const { violations } = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();

  if (violations.length) {
    console.log(
      `\n--- ${label} ---\n` +
        violations
          .map(
            v =>
              `[${v.impact}] ${v.id}: ${v.help}\n` +
              v.nodes.slice(0, 3).map(n => `    ${n.target.join(' ')}`).join('\n')
          )
          .join('\n')
    );
  }

  expect(violations.map(v => `${v.impact}:${v.id}`)).toEqual([]);
}

for (const url of SCREENS) {
  test(`a11y: ${url}`, async ({ page }) => {
    await page.goto(url);
    await page.waitForLoadState('networkidle');
    await expectNoViolations(page, url);
  });
}

test.describe('a11y: board', () => {
  // A fresh draft job, so every column is empty and its placeholder is scanned on the tinted column
  // surface. Drafts are never pushed to ReferralTool.
  const TITLE = `a11y-board-${Date.now()}`;

  test.beforeAll(async ({ browser }) => {
    const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
    await page.goto('/Jobs/Create');
    await page.locator('#Title').fill(TITLE);
    await page.locator('#Description').fill('Created by the a11y e2e spec.');
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

  test('a11y: /Board with empty columns', async ({ page }) => {
    await page.goto(`/Jobs?q=${encodeURIComponent(TITLE)}`);
    await page.locator('.ats-row-link', { hasText: TITLE }).click();
    await expect(page).toHaveURL(/\/Board\?jobId=/);
    await expect(page.locator('.ats-board-cards > .ats-board-empty').first()).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expectNoViolations(page, '/Board');
  });
});

test('a11y: the open confirm modal is a labelled dialog that traps focus', async ({ page }) => {
  // Sync vacancies always renders, and the modal is only dismissed, so nothing is sent.
  await page.goto('/Integration');
  const form = page.locator('#ats-content form[hx-confirm]').first();
  await form.locator('button[type="submit"]').click();

  const dialog = page.getByRole('dialog', { name: (await form.getAttribute('data-confirm-title'))! });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAttribute('aria-modal', 'true');
  await expect(dialog).toHaveAccessibleDescription((await form.getAttribute('hx-confirm'))!);
  await expect(dialog.locator(':focus')).toHaveCount(1);

  for (const key of ['Tab', 'Tab', 'Tab', 'Shift+Tab', 'Shift+Tab', 'Shift+Tab']) {
    await page.keyboard.press(key);
    expect(await dialog.evaluate((d) => d.contains(document.activeElement)), `focus left the dialog on ${key}`)
      .toBe(true);
  }

  const { violations } = await new AxeBuilder({ page })
    .include('#ats-confirm')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  expect(violations.map(v => `${v.impact}:${v.id}`)).toEqual([]);

  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(form.locator('button[type="submit"]')).toBeFocused();
});
