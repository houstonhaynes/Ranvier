import { test as base, expect } from "@playwright/test";

/** A page that records console errors and uncaught exceptions; each test ends by expecting none. */
export const test = base.extend({
  problems: async ({ page }, use) => {
    const problems = [];
    page.on("pageerror", error => problems.push(`pageerror: ${error.stack ?? error.message}`));
    page.on("console", message => { if (message.type() === "error") problems.push(`console: ${message.text()}`); });
    await use(problems);
    expect(problems).toEqual([]);
  }
});

export { expect };

/** The map cards of a page, in page order, once every map has run its scenario. */
export async function openMaps(page, path) {
  await page.goto(path);
  const maps = page.locator(".rv-map");
  await expect(maps.first()).toBeVisible();
  await expect.poll(() => page.locator(".rv-map .rv-map-node").count()).toBeGreaterThan(0);
  for (const map of await maps.all()) {
    await expect(map).toHaveAttribute("aria-busy", "false");
    await expect(map.locator(".rv-map-node").first()).toBeAttached();
  }
  return maps;
}
