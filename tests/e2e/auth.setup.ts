import { test as setup, expect } from '@playwright/test';

const STATE = 'tests/e2e/.auth/user.json';

// Local test credentials. Do not commit real values.
const EMAIL = 'henil.patel@nowonline.com';
const PASSWORD = 'Tester01!';

setup('sign in and save session', async ({ page }) => {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(process.env.ATS_TEST_EMAIL ?? EMAIL);
  await page.locator('#Password').fill(process.env.ATS_TEST_PASSWORD ?? PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();

  // The back office renders the boosted sidebar; the login page does not.
  await expect(page.locator('#ats-sidebar')).toBeVisible({ timeout: 15_000 });
  await page.context().storageState({ path: STATE });
});
