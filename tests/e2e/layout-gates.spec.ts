import { test, expect } from '@playwright/test';

/**
 * Locks the layout defects the diagnostic sweep (layout-audit.spec.ts) drove to zero: text clipped
 * without an ellipsis, rows poking out of their parent, uneven card pairs, and phone tap targets
 * under 44x44px. Deliberately small: a handful of list and form pages, no fixtures.
 */
const PAGES = ['/Jobs', '/Candidates', '/Users', '/Jobs/Create', '/Pipelines/Create', '/Integration'];

const defects = () => {
  const out: string[] = [];
  const vis = (el: Element) => {
    const s = getComputedStyle(el);
    const r = el.getBoundingClientRect();
    return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
  };
  const name = (el: Element) => `${el.tagName.toLowerCase()}.${String(el.className).trim().split(/\s+/).slice(0, 2).join('.')} "${(el.textContent ?? '').trim().slice(0, 30)}"`;

  document.querySelectorAll<HTMLElement>('body *').forEach(el => {
    if (!vis(el) || el.children.length > 0 || el.classList.contains('ms')) return;
    const s = getComputedStyle(el);
    if (s.overflow !== 'visible' || s.textOverflow === 'ellipsis') return;
    if (el.scrollWidth - el.clientWidth > 2) out.push(`clipped text: ${name(el)}`);
  });

  document.querySelectorAll<HTMLElement>('main *').forEach(el => {
    const p = el.parentElement;
    if (!p || !vis(el) || !vis(p) || getComputedStyle(p).overflow !== 'visible') return;
    if (getComputedStyle(el).position === 'absolute') return;
    if (el.getBoundingClientRect().right - p.getBoundingClientRect().right > 2) out.push(`escapes parent: ${name(el)}`);
  });

  return out;
};

for (const vp of [{ name: 'mobile', width: 375 }, { name: 'tablet', width: 768 }]) {
  for (const url of PAGES) {
    test(`${vp.name}: ${url} has no clipped text and nothing escaping its parent`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: 900 });
      await page.goto(url);
      await page.waitForLoadState('networkidle');
      expect(await page.evaluate(defects)).toEqual([]);
    });
  }
}

// Measured with the phone navigation panel closed. Content controls are counted on their own, so a
// page whose list or form rendered no control cannot pass on the shell controls alone.
const CONTENT_TARGETS = [
  '.ats-row-link', '.btn', '.ats-filter-group > a', '.ats-pager-btn',
  '.ats-icon-hit', '.ats-check-hit', '.ats-link-hit',
].map(s => `main ${s}`).join(', ');
const SHELL_TARGETS = ['#ats-topbar .ats-icon-btn', '#ats-topbar .ats-crumbs a', '#ats-sidebar .ats-menu-btn'].join(', ');

const measure = (selector: string) => {
  const els = Array.from(document.querySelectorAll<HTMLElement>(selector))
    .filter(el => el.checkVisibility({ checkVisibilityCSS: true }));
  return {
    count: els.length,
    small: els
      .map(el => ({ el, r: el.getBoundingClientRect() }))
      .filter(({ r }) => r.width < 44 || r.height < 44)
      .map(({ el, r }) => `${el.tagName.toLowerCase()}.${el.className} ${Math.round(r.width)}x${Math.round(r.height)}`),
  };
};

test('mobile: list, shell and navigation controls are 44x44px tap targets', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  for (const url of ['/Jobs', '/Candidates', '/Users', '/Pipelines/Create', '/Integration']) {
    await page.goto(url);
    await page.waitForLoadState('networkidle');
    const content = await page.evaluate(measure, CONTENT_TARGETS);
    const shell = await page.evaluate(measure, SHELL_TARGETS);

    // Nav links, Change password, Sign out and the close button live in the panel.
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await expect(page.locator('#ats-nav-panel')).toBeVisible();
    const panel = await page.evaluate(measure, '#ats-nav-panel a, #ats-nav-panel button');

    // A gate that measured nothing would pass vacuously.
    expect(content.count, `${url}: no content control measured`).toBeGreaterThan(0);
    expect(shell.count, `${url}: no shell control measured`).toBeGreaterThan(0);
    expect(panel.count, `${url}: no navigation control measured`).toBeGreaterThan(0);
    expect([...content.small, ...shell.small, ...panel.small], url).toEqual([]);
  }
});

// Phone cards: the facts wrap, and a wrapped line must not start with the middle-dot separator or
// end the card on one; an empty fact ("—") is hidden. The facts are squeezed to force wrapping, so
// the check does not depend on how long the seeded names are.
const metaDefects = () => {
  const out: string[] = [];
  const sep = (el: Element, pseudo: string) => !['none', 'normal'].includes(getComputedStyle(el, pseudo).content);
  let wrapped = 0;
  const groups = document.querySelectorAll<HTMLElement>('main .ats-trow > .ats-meta, main .ats-trow > .ats-card-line');
  groups.forEach(g => {
    const items = Array.from(g.children)
      .filter(el => el.checkVisibility({ checkVisibilityCSS: true }) && !el.classList.contains('ats-meta-full'))
      .map(el => ({ el, r: el.getBoundingClientRect() }));
    // Items of different heights share a line, so group by vertical overlap, not by equal tops.
    const lines: (typeof items)[] = [];
    for (const i of [...items].sort((a, b) => a.r.top - b.r.top)) {
      const line = lines.at(-1);
      const bottom = line ? Math.max(...line.map(x => x.r.bottom)) : -Infinity;
      if (line && i.r.top < bottom - 1) line.push(i);
      else lines.push([i]);
    }
    if (lines.length > 1) wrapped++;
    for (const line of lines) {
      line.sort((a, b) => a.r.left - b.r.left);
      if (sep(line[0].el, '::before')) out.push(`line starts with a separator: "${line[0].el.textContent?.trim()}"`);
    }
    const last = lines.at(-1)?.at(-1);
    if (last && sep(last.el, '::after')) out.push(`card ends with a separator: "${last.el.textContent?.trim()}"`);
    items.filter(i => i.el.textContent?.trim() === '—').forEach(() => out.push('empty value shown'));
  });
  return { groups: groups.length, wrapped, out };
};

test('mobile: card facts never start a line with a separator and hide empty values', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  for (const url of ['/Jobs', '/Candidates', '/Users']) {
    await page.goto(url);
    await page.waitForLoadState('networkidle');
    await page.addStyleTag({ content: 'main .ats-trow > .ats-meta { max-width: 7rem; }' });
    const r = await page.evaluate(metaDefects);
    expect(r.groups, `${url}: no card measured`).toBeGreaterThan(0);
    expect(r.wrapped, `${url}: nothing wrapped`).toBeGreaterThan(0);
    expect(r.out, url).toEqual([]);
  }
});

test('desktop: the dashboard card columns end on the same line', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  await page.waitForLoadState('networkidle');
  const bottoms = await page.evaluate(() => {
    const row = Array.from(document.querySelectorAll<HTMLElement>('main .row')).find(r => r.querySelector('.ats-card .ats-pipebar-row, .ats-card .ats-empty'));
    if (!row) return [];
    return Array.from(row.children).map(col => {
      const cards = col.querySelectorAll<HTMLElement>('.ats-card, .ats-card-dark');
      return Math.round(cards[cards.length - 1].getBoundingClientRect().bottom);
    });
  });
  expect(bottoms.length).toBe(2);
  expect(Math.abs(bottoms[0] - bottoms[1])).toBeLessThanOrEqual(2);
});
