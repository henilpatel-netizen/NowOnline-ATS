import { test as setup, expect } from '@playwright/test';
import { STATE, TEST_EMAIL, TEST_PASSWORD } from './credentials';

setup('sign in and save session', async ({ page }) => {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(TEST_EMAIL);
  await page.locator('#Password').fill(TEST_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();

  // The back office renders the boosted sidebar; the login page does not.
  await expect(page.locator('#ats-sidebar')).toBeVisible({ timeout: 15_000 });
  await page.context().storageState({ path: STATE });
});
