import { test } from '@playwright/test';

/**
 * Diagnostic sweep, not a pass/fail gate. It reports measurable layout defects across every screen
 * and viewport so they can be ranked and fixed; the fixes then get locked by real assertions.
 *
 * Run: npx playwright test layout-audit --reporter=line
 */
const ROUTES = [
  '/', '/Jobs', '/Jobs/Create', '/Candidates', '/Candidates/Create',
  '/Pipelines', '/Pipelines/Create', '/Organisation', '/Departments', '/Locations',
  '/Integration', '/Integration/Deliveries', '/CareerSite', '/CareerSite/Branding', '/Audit',
];

const VIEWPORTS = [
  { name: 'mobile', width: 375, height: 812 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'desktop', width: 1440, height: 900 },
];

type Finding = { route: string; viewport: string; kind: string; detail: string };
const findings: Finding[] = [];

const audit = () => {
  const out: { kind: string; detail: string }[] = [];
  const vis = (el: Element) => {
    const s = getComputedStyle(el);
    const r = el.getBoundingClientRect();
    return s.display !== 'none' && s.visibility !== 'hidden' && s.opacity !== '0' && r.width > 0 && r.height > 0;
  };
  const label = (el: Element) => {
    const cls = (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.') : '');
    const txt = (el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 45);
    return `${el.tagName.toLowerCase()}${cls} "${txt}"`;
  };

  // 1. Text clipped by its own box, with no scroll and no ellipsis to signal it is deliberate.
  document.querySelectorAll<HTMLElement>('body *').forEach(el => {
    if (!vis(el) || el.children.length > 0) return;
    const s = getComputedStyle(el);
    if (s.overflow !== 'visible' && s.overflow !== '') return;
    if (s.textOverflow === 'ellipsis') return;
    const overflowX = el.scrollWidth - el.clientWidth;
    const overflowY = el.scrollHeight - el.clientHeight;
    // Ligature icons render a few px taller than their line box by design; that is not clipping.
    if (el.classList.contains('ms')) return;
    if (overflowX > 2) out.push({ kind: 'text-clipped-x', detail: `${label(el)} overflows ${overflowX}px` });
    else if (overflowY > 3 && s.whiteSpace === 'nowrap') out.push({ kind: 'text-clipped-y', detail: `${label(el)} overflows ${overflowY}px` });
  });

  // 2. Elements whose text visually escapes their parent's right edge.
  document.querySelectorAll<HTMLElement>('main *').forEach(el => {
    if (!vis(el)) return;
    const p = el.parentElement;
    if (!p || !vis(p)) return;
    const ps = getComputedStyle(p);
    if (ps.overflow !== 'visible' && ps.overflow !== '') return;
    if (getComputedStyle(el).position === 'absolute') return;
    const a = el.getBoundingClientRect(), b = p.getBoundingClientRect();
    if (a.right - b.right > 2) out.push({ kind: 'escapes-parent', detail: `${label(el)} by ${Math.round(a.right - b.right)}px` });
  });

  // 3. Sibling cards in the same row that do not share a top edge or a height.
  document.querySelectorAll<HTMLElement>('.row').forEach(row => {
    const cols = Array.from(row.children).filter(c => vis(c)) as HTMLElement[];
    if (cols.length < 2) return;
    const boxes = cols.map(c => c.getBoundingClientRect());
    const sameLine = boxes.every(b => Math.abs(b.top - boxes[0].top) < 2);
    if (!sameLine) return;
    const heights = boxes.map(b => Math.round(b.height));
    if (Math.max(...heights) - Math.min(...heights) > 8) {
      out.push({ kind: 'uneven-columns', detail: `.row children heights ${heights.join(' vs ')}` });
    }
    cols.forEach((c, i) => {
      const card = c.querySelector<HTMLElement>('.ats-card, .ats-card-flush, .ats-card-dark');
      if (!card) return;
      const h = Math.round(card.getBoundingClientRect().height);
      const other = cols.map(o => o.querySelector<HTMLElement>('.ats-card, .ats-card-flush, .ats-card-dark'))
        .filter(Boolean).map(o => Math.round(o!.getBoundingClientRect().height));
      if (i === 0 && other.length > 1 && Math.max(...other) - Math.min(...other) > 8) {
        out.push({ kind: 'uneven-cards', detail: `sibling card heights ${other.join(' vs ')}` });
      }
    });
  });

  // 4. Wasted horizontal space: the widest thing in the content area vs the space available.
  const main = document.getElementById('ats-content');
  if (main) {
    const avail = main.clientWidth;
    let widest = 0;
    Array.from(main.children).forEach(c => {
      if (!vis(c)) return;
      widest = Math.max(widest, (c as HTMLElement).getBoundingClientRect().width);
    });
    const ratio = avail > 0 ? widest / avail : 1;
    if (ratio < 0.7 && avail > 600) {
      out.push({ kind: 'content-underfills', detail: `widest child is ${Math.round(ratio * 100)}% of ${Math.round(avail)}px` });
    }
  }

  // 5. Interactive targets below the WCAG 2.2 minimum (24x24).
  const seen = new Set<string>();
  document.querySelectorAll<HTMLElement>('a, button, input[type=checkbox], input[type=radio], [role=button]').forEach(el => {
    if (!vis(el)) return;
    const r = el.getBoundingClientRect();
    if (r.width >= 24 && r.height >= 24) return;
    const k = label(el);
    if (seen.has(k)) return;
    seen.add(k);
    out.push({ kind: 'tap-target', detail: `${k} is ${Math.round(r.width)}x${Math.round(r.height)}` });
  });

  // 6. Spacing values that are not on the 4px scale the design system uses.
  const offScale = new Map<string, number>();
  document.querySelectorAll<HTMLElement>('main *').forEach(el => {
    if (!vis(el)) return;
    const s = getComputedStyle(el);
    (['paddingTop', 'paddingBottom', 'paddingLeft', 'paddingRight', 'marginTop', 'marginBottom'] as const)
      .forEach(prop => {
        const v = parseFloat(s[prop]);
        if (!v || v % 1 !== 0) return;          // ignore rem-derived fractions
        if (v % 4 === 0) return;
        const k = `${prop}:${v}px`;
        offScale.set(k, (offScale.get(k) ?? 0) + 1);
      });
  });
  Array.from(offScale.entries()).sort((a, b) => b[1] - a[1]).slice(0, 4)
    .forEach(([k, n]) => out.push({ kind: 'off-scale-spacing', detail: `${k} used ${n}x` }));

  return out;
};

for (const vp of VIEWPORTS) {
  test(`layout audit @ ${vp.name}`, async ({ page }) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width: vp.width, height: vp.height });
    for (const route of ROUTES) {
      await page.goto(route);
      await page.waitForLoadState('networkidle');
      const found = await page.evaluate(audit);
      found.forEach(f => findings.push({ route, viewport: vp.name, ...f }));
    }
  });
}

test.afterAll(() => {
  const byKind = new Map<string, Finding[]>();
  findings.forEach(f => byKind.set(f.kind, [...(byKind.get(f.kind) ?? []), f]));

  const lines: string[] = ['', '=== LAYOUT AUDIT ================================================='];
  for (const [kind, items] of [...byKind.entries()].sort((a, b) => b[1].length - a[1].length)) {
    lines.push('', `## ${kind} (${items.length})`);
    const seen = new Set<string>();
    for (const i of items) {
      const key = `${i.route}|${i.detail}`;
      if (seen.has(key)) continue;
      seen.add(key);
      if (seen.size > 12) { lines.push(`  ... ${items.length - 12} more`); break; }
      lines.push(`  [${i.viewport}] ${i.route} -> ${i.detail}`);
    }
  }
  lines.push('', `total findings: ${findings.length}`, '==================================================================', '');
  console.log(lines.join('\n'));
});
