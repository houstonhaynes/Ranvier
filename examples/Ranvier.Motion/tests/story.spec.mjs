// The story page in Chromium: served by Vite as the document the .NET server renders (npm run server writes it),
// hydrated strictly, and scrolled to fixed offsets under page.clock.
import { test, expect } from "@playwright/test";
import { expectNoErrors, opacityOf, openPage, shoot } from "./support.mjs";

// Scrolls to `top` and resolves once the page has dispatched its scroll event, so the observers have seen the scroll
// before the clock runs.
async function scrollTo(page, top) {
  await page.evaluate(top => new Promise(resolve => {
    addEventListener("scroll", resolve, { once: true });
    window.scrollTo(0, top);
  }), top);
  await page.clock.runFor(400);
}

// Scrolls so that the element's centre sits at the viewport's centre, then lets the observers and engine run.
async function centre(page, selector) {
  const top = await page.evaluate(selector => {
    const box = document.querySelector(selector).getBoundingClientRect();
    return window.scrollY + box.top + box.height / 2 - window.innerHeight / 2;
  }, selector);
  await scrollTo(page, top);
}

const progress = page => page.evaluate(() => Array.from(window.__motion.progress()));

test.beforeEach(async ({ page }) => {
  await openPage(page, "/story/");
});

test.afterEach(async ({ page }) => expectNoErrors(page));

test("the page arrives server-rendered and hydrates strictly without a report", async ({ page, request }) => {
  const served = await (await request.get("/story/")).text();
  // Vite adds its client script to the head; the rest is the document Server.renderDocument wrote.
  expect(served.startsWith('<!doctype html><html lang="en"><head>')).toBe(true);
  expect(served).toContain('<title data-ranvier-head="title">Story · Ranvier Motion</title>');
  expect(served).toContain('<body><div id="app"><!--[r--><div class="frame"><header class="site-header">');
  expect(served).toContain("Motion, owned by the view.");
  await expect(page).toHaveTitle("Story · Ranvier Motion");
  expect(await page.evaluate(() => [window.__motion.hydrated, window.__motion.errors()])).toEqual([true, []]);
  await expect(page.locator(".eyebrow")).not.toHaveAttribute("style");
  expect(await progress(page)).toEqual([0, 0, 0, 0]);
});

test("the headline splits into letters that rise in", async ({ page }) => {
  const letters = page.locator(".headline [data-char]");
  await expect(letters).toHaveCount(22);
  const last = letters.last();
  await page.clock.runFor(100);
  const early = await opacityOf(last);
  await page.clock.runFor(1600);
  expect(early).toBeLessThan(1);
  expect(await opacityOf(last)).toBe(1);
});

test("scrolling to each chapter writes its progress into the rail", async ({ page }) => {
  await centre(page, 'section[data-chapter="1"]');
  const middle = await progress(page);
  expect(middle[1]).toBeGreaterThan(0.4);
  expect(middle[1]).toBeLessThan(0.6);
  expect(middle[0]).toBeGreaterThan(middle[1]);
  expect(middle[2]).toBeLessThan(middle[1]);
  await expect(page.locator(".rail-item").nth(1)).toHaveClass(/seen/);
  await expect(page.locator(".rail-item .rail-value").nth(1)).toHaveText(`${Math.round(middle[1] * 100)}%`);
  await shoot(page, "story");
  await scrollTo(page, 0);
  expect((await progress(page))[1]).toBe(0);
  await expect(page.locator(".rail-item .rail-value").nth(1)).toHaveText("0%");
});

test("the hydrated extras button shows the client-created tiles, which appear", async ({ page }) => {
  await expect(page.locator(".extra")).toHaveCount(0);
  await page.click("button.more-toggle");
  await expect(page.locator(".extra")).toHaveCount(4);
  await page.clock.runFor(100);
  const popping = await opacityOf(page.locator(".extra").first());
  await page.clock.runFor(800);
  expect(popping).toBeLessThan(1);
  expect(await opacityOf(page.locator(".extra").first())).toBe(1);
  await expect(page.locator("button.more-toggle")).toHaveText("Hide the extras");
  await page.click("button.more-toggle");
  await expect(page.locator(".extra")).toHaveCount(0);
  expect(await page.evaluate(() => window.__motion.errors())).toEqual([]);
});
