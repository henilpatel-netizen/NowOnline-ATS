import { defineConfig, devices } from '@playwright/test';

// The app redirects http -> https, and the dev certificate is self-signed, so the suite talks
// to the https origin directly and tolerates that certificate.
const baseURL = process.env.ATS_BASE_URL ?? 'https://localhost:7044';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['html', { outputFolder: 'artifacts/playwright-report', open: 'never' }]],
  outputDir: 'artifacts/playwright-results',
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/ },
    {
      name: 'desktop',
      dependencies: ['setup'],
      testIgnore: /auth\.setup\.ts/,
      use: { ...devices['Desktop Chrome'], storageState: 'tests/e2e/.auth/user.json' },
    },
  ],
  webServer: {
    command: 'dotnet run --project src/Ats.Web --launch-profile https',
    url: baseURL,
    // The readiness probe needs this too, or it never sees the running app and tries to start a
    // second one on a port that is already taken.
    ignoreHTTPSErrors: true,
    reuseExistingServer: true,
    timeout: 180_000,
    stdout: 'pipe',
  },
});
