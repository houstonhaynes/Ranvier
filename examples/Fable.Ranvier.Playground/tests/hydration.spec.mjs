import { test, expect } from "@playwright/test";

for (const [label, key] of [["hydration checks in Chromium", "hydrationCases"], ["hydration browser checks in Chromium", "hydrationBrowserCases"]]) {
  test(label, async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.goto("/tests/hydration.html");
    await page.waitForFunction(key => window[key], key);
    const results = await page.evaluate(key => window[key].map(([name, check]) => {
      try { check(); return { name, passed: true }; }
      catch (error) { return { name, passed: false, error: String(error) }; }
    }), key);
    expect(results.length).toBeGreaterThan(0);
    expect(results.filter(result => !result.passed)).toEqual([]);
    expect(errors).toEqual([]);
  });
}

// Each case mounts or hydrates in one task; its check runs in a later task, after a full garbage collection.
test("hydration retention checks in Chromium", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/hydration.html");
  await page.waitForFunction(() => window.hydrationRetentionCases);
  const count = await page.evaluate(() => {
    window.retentionChecks = window.hydrationRetentionCases.map(([name, setUp]) => [name, setUp()]);
    return window.retentionChecks.length;
  });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("HeapProfiler.collectGarbage");
  const results = await page.evaluate(() => window.retentionChecks.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(count).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});
