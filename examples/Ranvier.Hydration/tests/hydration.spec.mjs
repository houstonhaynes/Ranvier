import { test, expect } from "@playwright/test";

test("Giraffe renders request state without JavaScript", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto("http://127.0.0.1:5080/?greeting=Ada&start=7");
  await expect(page.locator("#greeting")).toHaveText("Ada");
  await expect(page.locator("#count")).toHaveText("7");
  await expect(page.locator("#increment")).toBeVisible();
  await context.close();
});

test("hydration retains server elements and uses their initial state", async ({ page }) => {
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  await page.route("**/assets/client.js", async route => { await gate; await route.continue(); });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/?greeting=Grace&start=12", { waitUntil: "commit" });
  await expect(page.locator("#count")).toHaveText("12");
  await page.evaluate(() => {
    window.serverNodes = ["greeting", "count", "increment"].map(id => document.getElementById(id));
  });
  release();
  await page.waitForLoadState("networkidle");
  expect(await page.evaluate(() => window.serverNodes.every(node => node === document.getElementById(node.id)))).toBe(true);
  await page.locator("#increment").click();
  await expect(page.locator("#count")).toHaveText("13");
  expect(errors).toEqual([]);
});

test("requests have independent state", async ({ page, request }) => {
  const responses = await Promise.all(Array.from({ length: 20 }, async (_, index) => {
    const greeting = `Concurrent ${index}`;
    const start = 100 + index;
    const response = await request.get(`/?${new URLSearchParams({ greeting, start: String(start) })}`);
    const html = await response.text();
    expect(response.status()).toBe(200);
    expect(html).toContain(`id="greeting">${greeting}`);
    expect(html).toContain(`id="count" aria-live="polite">${start}`);
    const encodedState = html.match(/<script[^>]*id="hydration-props"[^>]*>([\s\S]*?)<\/script>/);
    expect(encodedState).not.toBeNull();
    expect(JSON.parse(encodedState[1])).toEqual({ greeting, start });
    return html;
  }));
  expect(new Set(responses).size).toBe(20);
  await page.goto("/?greeting=First&start=21");
  await page.locator("#increment").click();
  await expect(page.locator("#count")).toHaveText("22");
  const response = await request.get("/?greeting=Second&start=4");
  const html = await response.text();
  expect(html).toContain('id="greeting">Second');
  expect(html).toContain('id="count" aria-live="polite">4');
  await page.reload();
  await expect(page.locator("#count")).toHaveText("21");
});

test("untrusted request text stays text through server rendering and hydration", async ({ page, request }) => {
  const greeting = '</script><script>window.injected=true</script><img src=x onerror="window.injected=true"> & " 🦒';
  const url = `/?${new URLSearchParams({ greeting, start: "9" })}`;
  const response = await request.get(url);
  expect(response.headers()["content-type"]).toContain("text/html");
  expect(await response.text()).not.toContain(greeting);
  await page.goto(url);
  await expect(page.locator("#greeting")).toHaveText(greeting);
  await page.locator("#increment").click();
  await expect(page.locator("#count")).toHaveText("10");
  expect(await page.evaluate(() => window.injected)).toBeUndefined();
  await expect(page.locator("#greeting img")).toHaveCount(0);
});

test("invalid query values use safe defaults", async ({ page }) => {
  await page.goto("/?start=2147483648");
  await expect(page.locator("#greeting")).toHaveText("Hello from F#");
  await expect(page.locator("#count")).toHaveText("3");
  await page.goto(`/?greeting=${"a".repeat(250)}&start=-1`);
  await expect(page.locator("#greeting")).toHaveText("a".repeat(200));
  await expect(page.locator("#count")).toHaveText("3");
});

test("greeting truncation preserves Unicode scalar values at the limit", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  for (const [greeting, expected] of [
    ["a".repeat(199) + "\u{1f992}", "a".repeat(199)],
    ["a".repeat(198) + "\u{1f992}", "a".repeat(198) + "\u{1f992}"]
  ]) {
    await page.goto(`/?${new URLSearchParams({ greeting, start: "6" })}`);
    await expect(page.locator("#greeting")).toHaveText(expected);
    await page.locator("#increment").click();
    await expect(page.locator("#count")).toHaveText("7");
  }
  expect(errors).toEqual([]);
});

test("unknown routes return 404 and assets load", async ({ request }) => {
  expect((await request.get("/missing")).status()).toBe(404);
  expect((await request.get("/assets/client.js")).status()).toBe(200);
  expect((await request.get("/style.css")).status()).toBe(200);
});

test("static export hydrates under a nested documentation URL", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("http://127.0.0.1:5081/nested/hydration/?greeting=Ignored&start=99");
  await expect(page.locator("#greeting")).toHaveText("Hello from F#");
  await expect(page.locator("#count")).toHaveText("3");
  await page.locator("#increment").click();
  await expect(page.locator("#count")).toHaveText("4");
  expect(errors).toEqual([]);
});
