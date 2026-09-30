import { type Page } from '@playwright/test';
import { test, expect, acceptConfirm } from './confirm';

/**
 * Departments are hard-deleted from the Organisation page, guarded against deletion while a job uses
 * them. The confirm is the themed hx-confirm modal (a native onsubmit confirm is bypassed by the
 * boosted submit), so each delete asserts its text before confirming.
 */
const RUN = `org-${Date.now()}`;
const FREE = `${RUN} Free`;
const USED = `${RUN} Used`;
const JOB = `${RUN} Job`;

test.describe.configure({ mode: 'serial' });

async function createDepartment(page: Page, name: string) {
  await page.goto('/Departments/Create');
  await page.locator('#Name').fill(name);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Department created.')).toBeVisible();
}

async function deleteDepartment(page: Page, name: string) {
  await page.goto('/Organisation');
  await page.getByRole('button', { name: `Delete ${name}`, exact: true }).click();
  await acceptConfirm(page, 'Delete this department?');
}

test('an unused department is deleted from the Organisation page', async ({ page }) => {
  await createDepartment(page, FREE);

  await deleteDepartment(page, FREE);

  await expect(page).toHaveURL(/\/Organisation/);
  await expect(page.getByText('Department deleted.')).toBeVisible();
  await expect(page.getByRole('button', { name: `Delete ${FREE}`, exact: true })).toHaveCount(0);
});

test('a department used by a job is not deleted', async ({ page }) => {
  await createDepartment(page, USED);
  await page.goto('/Jobs/Create');
  await page.locator('#Title').fill(JOB);
  await page.locator('#Description').fill('Created by the organisation-delete e2e spec.');
  await page.locator('#DepartmentId').selectOption({ label: USED });
  await page.locator('#PipelineTemplateId').selectOption({ index: 1 });
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Job created.')).toBeVisible();

  await deleteDepartment(page, USED);

  await expect(page).toHaveURL(/\/Organisation/);
  await expect(page.getByText('This department is used by one or more jobs and cannot be deleted.')).toBeVisible();
  await expect(page.getByRole('button', { name: `Delete ${USED}`, exact: true })).toBeVisible();
});

// The job is deleted first. The soft-deleted job still references the department, and the
// department delete then succeeds because that foreign key is ON DELETE SET NULL.
test.afterAll(async ({ browser }) => {
  const page = await browser.newPage({ storageState: 'tests/e2e/.auth/user.json' });
  const list = `/Jobs?q=${encodeURIComponent(RUN)}`;

  await page.goto(list);
  if ((await page.locator('.ats-trow').count()) > 0) {
    await page.locator('.ats-trow').first().locator('button', { hasText: 'Delete' }).dispatchEvent('click');
    await acceptConfirm(page);
    await expect(page.getByText('Job deleted.')).toBeVisible();
  }
  await page.goto(list);
  await expect(page.locator('.ats-trow')).toHaveCount(0);

  await page.goto('/Organisation');
  for (const name of [USED, FREE]) {
    if ((await page.getByRole('button', { name: `Delete ${name}`, exact: true }).count()) === 0) continue;
    await deleteDepartment(page, name);
    await expect(page.getByText('Department deleted.')).toBeVisible();
    await page.goto('/Organisation');
  }
  for (const name of [USED, FREE])
    await expect(page.getByRole('button', { name: `Delete ${name}`, exact: true })).toHaveCount(0);
  await page.close();
});
