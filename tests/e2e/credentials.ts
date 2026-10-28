// Local test credentials. Do not commit real values; set ATS_TEST_EMAIL / ATS_TEST_PASSWORD instead.
export const TEST_EMAIL = process.env.ATS_TEST_EMAIL ?? 'henil.patel@nowonline.com';
export const TEST_PASSWORD = process.env.ATS_TEST_PASSWORD ?? 'Tester01!';

/** Where auth.setup.ts saves the signed-in session the desktop project starts from. */
export const STATE = 'tests/e2e/.auth/user.json';
