// The routes page in Chromium under page.clock: hash navigation through the tabs and the history, arm presence with
// pinned exits, the pages' own loops, and rapid switching.
import { test, expect } from "@playwright/test";
import { expectNoErrors, finishAnimations, opacityOf, openPage, shoot } from "./support.mjs";

const pages = page => page.locator(".page");
const routeOf = page => page.evaluate(() => window.__motion.route());

test.afterEach(async ({ page }) => expectNoErrors(page));

// The indicator's box and the active tab's box, left edge and width.
async function boxes(page, route) {
  const indicator = await page.locator(".indicator").boundingBox();
  const tab = await page.locator(`.tab[data-route="${route}"]`).boundingBox();
  return { indicator: [indicator.x, indicator.width], tab: [tab.x, tab.width] };
}

for (const [label, width] of [["at desktop width", 1180], ["at phone width", 600]]) {
  test(`the hash selects the first page, and the indicator sits on its tab ${label}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 820 });
    await openPage(page, "/routes/#/settings");
    await expect(pages(page)).toHaveCount(1);
    await expect(pages(page).first()).toHaveAttribute("data-route", "settings");
    await page.clock.runFor(600);
    const settings = await boxes(page, "settings");
    expect(settings.indicator[0]).toBeCloseTo(settings.tab[0], 0);
    expect(settings.indicator[1]).toBeCloseTo(settings.tab[1], 0);
    await page.evaluate(() => new Promise(resolve => {
      addEventListener("hashchange", resolve, { once: true });
      document.querySelector('.tab[data-route="about"]').click();
    }));
    await page.clock.runFor(800);
    const about = await boxes(page, "about");
    expect(about.indicator[0]).toBeCloseTo(about.tab[0], 0);
  });
}

test("a tab switch pins the leaving page while it fades, and the next page takes its place", async ({ page }) => {
  await openPage(page, "/routes/");
  await page.click('.tab[data-route="gallery"]');
  await page.clock.runFor(16);
  expect(await routeOf(page)).toBe("gallery");
  const leaving = page.locator('.page[data-route="home"]');
  await expect(pages(page)).toHaveCount(2);
  await expect(leaving).toHaveCSS("position", "absolute");
  await page.clock.runFor(100);
  const fading = await opacityOf(leaving);
  expect(fading).toBeGreaterThan(0);
  expect(fading).toBeLessThan(1);
  await page.clock.runFor(300);
  await expect(pages(page)).toHaveCount(1);
  await expect(pages(page).first()).toHaveAttribute("data-route", "gallery");
  await finishAnimations(page, ".stage");
  await expect(page.locator(".shell-foot")).toHaveText("Gallery · 1 route changes");
  await page.clock.runFor(600);
  await shoot(page, "routes");
});

test("each page runs its own loop: the counter resets on return, the theme reaches the shell", async ({ page }) => {
  await openPage(page, "/routes/");
  await page.click("button.increment");
  await page.click("button.increment");
  await expect(page.locator(".count")).toHaveText("2");
  await page.click('.tab[data-route="settings"]');
  await page.clock.runFor(500);
  await page.click('.chip[data-theme="dusk"]');
  await expect(page.locator(".shell")).toHaveAttribute("data-theme", "dusk");
  await page.click('.tab[data-route="home"]');
  await page.clock.runFor(500);
  await expect(page.locator(".count")).toHaveText("0");
  await expect(page.locator(".shell")).toHaveAttribute("data-theme", "dusk");
  expect(await page.evaluate(() => window.__motion.visits())).toBe(2);
});

test("a gallery filter scales the swatches out that it hides", async ({ page }) => {
  await openPage(page, "/routes/#/gallery");
  await expect(page.locator(".swatch")).toHaveCount(6);
  await page.click('.chip[data-filter="warm"]');
  await expect(page.locator(".swatch")).toHaveCount(6);
  await page.clock.runFor(400);
  await expect(page.locator(".swatch")).toHaveText(["Coral", "Amber", "Rose"]);
  await page.click('.chip[data-filter="all"]');
  await page.clock.runFor(400);
  await expect(page.locator(".swatch")).toHaveCount(6);
  expect(await opacityOf(page.locator(".swatch.mint"))).toBe(1);
});

test("ten rapid switches and a burst of back and forward leave only the last page", async ({ page }) => {
  await openPage(page, "/routes/");
  const order = ["gallery", "settings", "about", "home", "settings", "gallery", "about", "settings", "home", "about"];
  for (const route of order) {
    await page.click(`.tab[data-route="${route}"]`);
    await page.clock.runFor(30);
  }
  expect(await routeOf(page)).toBe("about");
  // Back, back, forward, with no page time between: each step waits only for its hashchange.
  for (const step of ["back", "back", "forward"]) {
    await page.evaluate(step => new Promise(resolve => {
      addEventListener("hashchange", resolve, { once: true });
      history[step]();
    }), step);
  }
  await page.clock.runFor(30);
  expect(await routeOf(page)).toBe("home");
  await page.clock.runFor(1000);
  await expect(pages(page)).toHaveCount(1);
  await expect(pages(page).first()).toHaveAttribute("data-route", "home");
  expect(await opacityOf(pages(page).first())).toBe(1);
});
