import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env.ATS_BASE_URL ?? 'http://localhost:5092';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['html', { outputFolder: 'artifacts/playwright-report', open: 'never' }]],
  outputDir: 'artifacts/playwright-results',
  use: {
    baseURL,
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
    command: 'dotnet run --project src/Ats.Web --launch-profile http',
    url: baseURL,
    reuseExistingServer: true,
    timeout: 180_000,
    stdout: 'pipe',
  },
});
