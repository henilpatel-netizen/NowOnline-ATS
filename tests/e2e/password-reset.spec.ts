import { test, expect, type Page } from '@playwright/test';
import { STATE, TEST_EMAIL, TEST_PASSWORD } from './credentials';
import { expectNoMail, waitForMail } from './mailbox';

/**
 * Forgot password end to end, through the real Worker writing .eml files (mail-worker.setup.ts).
 * It changes the shared test account's password and always puts it back, then re-saves the session
 * the other specs start from (a password change rotates the security stamp, ending that session).
 */
test.use({ storageState: { cookies: [], origins: [] } });
test.describe.configure({ mode: 'serial' });

const SENT = 'If an account exists for that address, we have sent a link. It expires in 1 hour.';
const SUBJECT = 'Reset your password';

/** Account links are built from Integration:CareerSiteBaseUrl, which the e2e app shares with the suite's base URL. */
const linkPrefix = () =>
  `${(process.env.ATS_CAREER_SITE_BASE_URL ?? test.info().project.use.baseURL ?? '').replace(/\/$/, '')}/Account/ResetPassword?token=`;

async function requestReset(page: Page, email: string) {
  await page.goto('/Account/ForgotPassword');
  await page.locator('#Email').fill(email);
  await page.getByRole('button', { name: 'Send reset link' }).click();
  await expect(page.getByText(SENT)).toBeVisible();
}

async function submitNewPassword(page: Page, link: string, password: string) {
  await page.goto(link);
  await page.locator('#NewPassword').fill(password);
  await page.locator('#ConfirmPassword').fill(password);
  await page.getByRole('button', { name: 'Set new password' }).click();
  await expect(page).toHaveURL(/\/Account\/Login/);
  await expect(page.locator('.alert-success')).toContainText('Your password has been reset.');
}

async function signIn(page: Page, password: string) {
  await page.goto('/Account/Login');
  await page.locator('#Email').fill(TEST_EMAIL);
  await page.locator('#Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.locator('#ats-sidebar')).toBeVisible({ timeout: 15_000 });
}

/** Puts the original password back the same way, and saves a fresh session for the specs that follow. */
async function restorePassword(page: Page) {
  await requestReset(page, TEST_EMAIL);
  const mail = await waitForMail(TEST_EMAIL, SUBJECT);
  await submitNewPassword(page, mail.link!, TEST_PASSWORD);
  await signIn(page, TEST_PASSWORD);
  await page.context().storageState({ path: STATE });
}

// First, so the main test's reset rotates the stamp and the link this one leaves behind is dead.
test('a forged Host header does not change the link host', async ({ page }) => {
  await page.goto('/Account/ForgotPassword');
  const token = await page.locator('input[name="__RequestVerificationToken"]').inputValue();
  const response = await page.request.post('/Account/ForgotPassword', {
    form: { Email: TEST_EMAIL, __RequestVerificationToken: token },
    headers: { Host: 'evil.example', 'X-Forwarded-Host': 'evil.example' },
  });
  expect(response.status()).toBe(200);
  expect(await response.text()).toContain(SENT);

  const mail = await waitForMail(TEST_EMAIL, SUBJECT);
  expect(mail.link).not.toContain('evil.example');
  expect(mail.link?.startsWith(linkPrefix()), `link was ${mail.link}`).toBe(true);
});

test('an unknown address gets the same answer and no mail', async ({ page }) => {
  const unknown = `nobody-${Date.now()}@example.test`;
  await requestReset(page, unknown);
  await expectNoMail(unknown, SUBJECT, 5_000);
});

test('a reset link survives a prefetch, sets a new password and works once', async ({ page }) => {
  test.setTimeout(120_000);
  const temporary = `E2e-reset-${Date.now()}`;
  let submitted = false;
  try {
    await requestReset(page, TEST_EMAIL);
    const mail = await waitForMail(TEST_EMAIL, SUBJECT);
    expect(mail.link?.startsWith(linkPrefix()), `link was ${mail.link}`).toBe(true);
    const link = mail.link!;

    // A mail scanner opening the link must not use it up.
    for (let i = 0; i < 2; i++) {
      await page.goto(link);
      await expect(page.locator('#NewPassword')).toBeVisible();
    }

    submitted = true;
    await submitNewPassword(page, link, temporary);

    await page.goto(link);
    await expect(page.getByText('This link is invalid or has expired. Request a new one.')).toBeVisible();
    await expect(page.locator('#NewPassword')).toHaveCount(0);

    await signIn(page, temporary);
  } finally {
    if (submitted) await restorePassword(page);
  }
});
