import { test, expect } from "@playwright/test";

test("the page contains its ordinary demos without JavaScript", async ({ browser, request }) => {
  const response = await request.get("/");
  expect(response.ok()).toBe(true);
  const html = await response.text();
  expect(html).toContain("<!--[r-->");
  expect(html).toMatch(/type="module"[^>]*src="[^"]*generated-pages\/Index.js/);
  expect(html).not.toContain("/main.js");
  const context = await browser.newContext({ javaScriptEnabled: false });
  try {
    const page = await context.newPage();
    await page.goto(response.url());
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Create once.");
    await expect(page.locator("#foundation-row-1")).toHaveText("Alpha");
    await expect(page.locator("#foundation-load-result")).toHaveText("Loaded result");
    await expect(page.locator("#component-first-count")).toHaveText("0");
    await expect(page.locator("#component-second-count")).toHaveText("10");
    await expect(page.locator("#component-label")).toHaveValue("Shared label");
  } finally {
    await context.close();
  }
});

test("strict startup retains the server shell, rows and component nodes", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  let release;
  const ready = new Promise(resolve => { release = resolve; });
  await page.route("**/generated/generated-pages/Index.js", async route => {
    await ready;
    await route.continue();
  });
  await page.goto("/", { waitUntil: "commit" });
  await expect(page.locator("#component-label")).toHaveValue("Shared label");
  await page.evaluate(() => {
    window.__serverNodes = [
      document.querySelector("#app").firstElementChild,
      document.querySelector("#foundation-row-1"),
      document.querySelector("#foundation-load-result"),
      document.querySelector("#component-first-count"),
      document.querySelector("#component-label")
    ];
  });
  release();
  await page.waitForFunction(() => Boolean(window.playground));
  expect(await page.evaluate(() => window.__serverNodes.every(node => node?.isConnected))).toBe(true);
  await page.locator("#component-first-increment").click();
  await expect(page.locator("#component-first-count")).toHaveText("1");
  expect(await page.evaluate(() => window.__serverNodes[3] === document.querySelector("#component-first-count"))).toBe(true);
  expect(errors).toEqual([]);
});
