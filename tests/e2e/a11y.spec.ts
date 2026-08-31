import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

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

for (const url of SCREENS) {
  test(`a11y: ${url}`, async ({ page }) => {
    await page.goto(url);
    await page.waitForLoadState('networkidle');

    const { violations } = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();

    if (violations.length) {
      console.log(
        `\n--- ${url} ---\n` +
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
  });
}
