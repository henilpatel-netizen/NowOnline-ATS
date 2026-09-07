import { test, expect } from '@playwright/test';

/**
 * Layer 3: layout integrity. A page whose body scrolls sideways is the single most common
 * responsive defect, and it is objectively detectable — no screenshot review needed.
 */
const VIEWPORTS = [
  { name: 'mobile', width: 375, height: 812 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'desktop', width: 1440, height: 900 },
];

const PAGES = ['/', '/Jobs', '/Candidates', '/Pipelines', '/Integration', '/Audit'];

for (const vp of VIEWPORTS) {
  for (const url of PAGES) {
    test(`${vp.name} ${vp.width}px: ${url} does not scroll sideways`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto(url);
      await page.waitForLoadState('networkidle');

      const overflow = await page.evaluate(() => {
        const d = document.documentElement;
        return { scrollWidth: d.scrollWidth, clientWidth: d.clientWidth };
      });

      // A 1px rounding tolerance; anything more is a real horizontal scrollbar.
      expect(
        overflow.scrollWidth - overflow.clientWidth,
        `${url} overflows by ${overflow.scrollWidth - overflow.clientWidth}px`
      ).toBeLessThanOrEqual(1);
    });
  }
}

test('the sidebar stacks above the content on mobile instead of overlapping it', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/Jobs');
  await page.waitForLoadState('networkidle');

  const sidebar = await page.locator('#ats-sidebar').boundingBox();
  const content = await page.locator('#ats-content').boundingBox();
  expect(sidebar, 'sidebar should still be present').not.toBeNull();
  expect(content, 'content should still be present').not.toBeNull();

  // On mobile the sidebar becomes a full-width bar; the content must start below it, not under it.
  expect(content!.y, 'content is overlapped by the sidebar').toBeGreaterThanOrEqual(
    sidebar!.y + sidebar!.height - 1
  );
});

// The content area and the top bar share one horizontal padding, so a page's left edge must line
// up with the breadcrumb and its right gutter must match its left at any width. A width cap broke
// both: left-aligned it wasted the right-hand side, centred it fell out of line with the crumb.
for (const width of [1280, 1440, 1920, 2560]) {
  test(`@${width}px: content aligns with the breadcrumb and both gutters match`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const crumb = await page.locator('.ats-crumbs').boundingBox();
    const head = await page.locator('.ats-pagehead').boundingBox();
    const main = await page.locator('#ats-content').boundingBox();
    expect(crumb).not.toBeNull();
    expect(head).not.toBeNull();
    expect(main).not.toBeNull();

    expect(
      Math.abs(head!.x - crumb!.x),
      `page heading starts ${Math.round(head!.x - crumb!.x)}px from the breadcrumb`
    ).toBeLessThanOrEqual(2);

    const leftGutter = head!.x - main!.x;
    const rightGutter = main!.x + main!.width - (head!.x + head!.width);
    expect(
      Math.abs(leftGutter - rightGutter),
      `gutters differ: ${Math.round(leftGutter)}px left vs ${Math.round(rightGutter)}px right`
    ).toBeLessThanOrEqual(2);
  });
}
