import { test, expect } from "@playwright/test";
import { expectNoErrors, openPage } from "./support.mjs";

for (const name of ["kanban", "dashboard", "story", "physics", "routes", "graphs"]) {
  test(`${name} arrives rendered and retains its server root through startup`, async ({ page, request }) => {
    const response = await request.get(`/${name}/`);
    expect(response.ok()).toBe(true);
    const html = await response.text();
    expect(html).toContain('<div id="app"><!--[r-->');
    expect(html).toContain('<div class="frame">');
    expect(html).toMatch(/type="module"[^>]*src="[^"]*generated-pages\//);
    expect(html).not.toContain(`/${name}/main.js`);

    await page.addInitScript(() => {
      const observer = new MutationObserver(() => {
        const root = document.getElementById("app")?.firstElementChild;
        if (root) {
          window.__serverRoot = root;
          observer.disconnect();
        }
      });
      observer.observe(document, { childList: true, subtree: true });
    });
    await openPage(page, `/${name}/`);
    expect(await page.evaluate(() => Boolean(window.__serverRoot) && window.__serverRoot === document.getElementById("app")?.firstElementChild)).toBe(true);
    expectNoErrors(page);
  });
}

test("the landing page is generated without a browser startup module", async ({ request }) => {
  const response = await request.get("/");
  expect(response.ok()).toBe(true);
  const html = await response.text();
  expect(html).toContain("Motion that belongs to the view.");
  expect(html).toContain('href="./story/"');
  expect(html).not.toContain("generated-pages/");
});
