import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  testMatch: "**/*.spec.mjs",
  fullyParallel: false,
  workers: 1,
  timeout: 30_000,
  expect: { timeout: 5_000 },
  use: { baseURL: "http://127.0.0.1:5179", browserName: "chromium", viewport: { width: 1180, height: 820 } },
  webServer: {
    command: "npm run serve:test",
    url: "http://127.0.0.1:5179",
    reuseExistingServer: false,
    timeout: 30_000
  }
});
