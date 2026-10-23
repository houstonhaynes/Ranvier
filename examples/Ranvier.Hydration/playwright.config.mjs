import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "tests",
  fullyParallel: true,
  use: { baseURL: "http://127.0.0.1:5080", browserName: "chromium" },
  webServer: [{
    command: "npm start",
    url: "http://127.0.0.1:5080",
    reuseExistingServer: false,
    timeout: 30_000
  }, {
    command: "vite preview --host 127.0.0.1 --port 5081 --strictPort --base /nested/hydration/",
    url: "http://127.0.0.1:5081/nested/hydration/",
    reuseExistingServer: false,
    timeout: 30_000
  }]
});
