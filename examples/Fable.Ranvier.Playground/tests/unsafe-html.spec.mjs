import { test, expect } from "@playwright/test";

test("script elements in raw HTML do not run; an onerror attribute does", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.unsafeMarkup);
  const result = await page.evaluate(async () => {
    window.rawScript = 0;
    window.rawHandler = 0;
    const [host, dispose] = window.unsafeMarkup('<script>window.rawScript = 1</script><img src="data:," onerror="window.rawHandler = 1">');
    for (let turn = 0; turn < 50 && !window.rawHandler; turn++) await new Promise(resolve => setTimeout(resolve, 10));
    const outcome = [window.rawScript, window.rawHandler, host.querySelectorAll("script").length];
    dispose();
    return outcome;
  });
  expect(result).toEqual([0, 1, 1]);
  expect(errors).toEqual([]);
});

test("raw HTML in a div drops a tr outside a table section in Chromium", async ({ page }) => {
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.unsafeMarkup);
  const rows = await page.evaluate(() => {
    const [host, dispose] = window.unsafeMarkup("<tr><td>a</td></tr>");
    const parsedInDiv = host.querySelectorAll("tr").length;
    dispose();
    return parsedInDiv;
  });
  expect(rows).toBe(0);
});
