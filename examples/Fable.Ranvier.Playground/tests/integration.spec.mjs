import { test, expect } from "@playwright/test";

test("widgets see layout and keep focus in Chromium", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.integrationBrowserCases);
  const results = await page.evaluate(() => window.integrationBrowserCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});
