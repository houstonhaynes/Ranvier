// The dashboard page in Chromium under page.clock: the page pushes samples at 600 ms, 1600 ms, 2600 ms and on, so runFor
// moves both the stream and anime's engine.
import { test, expect } from "@playwright/test";
import { expectNoErrors, opacityOf, openPage, shoot } from "./support.mjs";

const attribute = (page, selector, name) => page.locator(selector).getAttribute(name);

test.beforeEach(async ({ page }) => {
  await openPage(page, "/dashboard/");
});

test.afterEach(async ({ page }) => expectNoErrors(page));

test("the skeleton pulses until the first sample, then the metrics replace it", async ({ page }) => {
  await expect(page.locator(".skeleton-tile")).toHaveCount(4);
  const tile = page.locator(".skeleton-tile").first();
  await page.clock.runFor(200);
  const first = await opacityOf(tile);
  await page.clock.runFor(200);
  const second = await opacityOf(tile);
  expect(first).not.toBe(second);
  await page.clock.runFor(300);
  await expect(page.locator(".skeleton")).toHaveCount(0);
  await expect(page.locator(".metrics")).toHaveCount(1);
  await expect(page.locator(".requests .note")).toHaveText("sample 1");
});

test("the chart's path and the ring's stroke change with every sample", async ({ page }) => {
  await page.clock.runFor(700);
  const line = ".chart-line";
  const ring = ".ring-value";
  const first = [await attribute(page, line, "d"), await attribute(page, ring, "stroke-dasharray")];
  await page.clock.runFor(1000);
  await page.clock.runFor(150);
  const moving = [await attribute(page, line, "d"), await attribute(page, ring, "stroke-dasharray")];
  await page.clock.runFor(650);
  const settled = [await attribute(page, line, "d"), await attribute(page, ring, "stroke-dasharray")];
  await page.clock.runFor(50);
  const still = await attribute(page, line, "d");
  expect(moving[0]).not.toBe(first[0]);
  expect(moving[0]).not.toBe(settled[0]);
  expect(moving[1]).not.toBe(first[1]);
  expect(still).toBe(settled[0]);
  expect((settled[0].match(/[ML]/g) ?? []).length).toBe(24);
  await expect(page.locator(".chart .note")).toHaveText("2 of 24");
});

test("gauges follow each sample and the request rate counts up to it", async ({ page }) => {
  await page.clock.runFor(700);
  await page.clock.runFor(200);
  const counting = Number(await page.locator(".counter").textContent());
  await page.clock.runFor(640);
  const requests = await page.evaluate(() => window.__motion.samples().at(-1).Requests);
  await expect(page.locator(".counter")).toHaveText(String(requests));
  expect(counting).toBeGreaterThan(0);
  expect(counting).toBeLessThan(requests);
  const cpu = await page.evaluate(() => window.__motion.samples().at(-1).Cpu);
  const scale = await page.locator(".gauge-fill").first().evaluate(element => new DOMMatrixReadOnly(getComputedStyle(element).transform).a);
  expect(scale).toBeCloseTo(cpu, 2);
});

test("a spike fades the alert in, and the details panel toggles with a fade", async ({ page }) => {
  await page.clock.runFor(700);
  await expect(page.locator(".alert")).toHaveCount(0);
  await page.evaluate(() => window.__motion.spike());
  const alert = page.locator(".alert");
  await expect(alert).toHaveCount(1);
  await page.clock.runFor(100);
  const fading = await opacityOf(alert);
  await page.clock.runFor(300);
  expect(fading).toBeGreaterThan(0);
  expect(fading).toBeLessThan(1);
  expect(await opacityOf(alert)).toBe(1);
  await page.click("button.details-toggle");
  await expect(page.locator(".details dd").first()).toHaveText("2");
  await page.clock.runFor(400);
  await shoot(page, "dashboard");
  await page.click("button.details-toggle");
  await page.clock.runFor(300);
  await expect(page.locator(".details")).toHaveCount(0);
});

test("pause stops the samples, and reconnect dims the metrics until the next sample", async ({ page }) => {
  await page.clock.runFor(1700);
  await page.click("button.pause");
  await page.clock.runFor(3000);
  expect(await page.evaluate(() => window.__motion.samples().length)).toBe(2);
  await page.click("button.pause");
  await page.click("button.reconnect");
  await expect(page.locator(".metrics")).toHaveAttribute("data-refreshing", "true");
  await page.clock.runFor(1000);
  await expect(page.locator(".metrics")).not.toHaveAttribute("data-refreshing", "true");
  await expect(page.locator(".chart .note")).toHaveText("1 of 24");
  expect(await page.evaluate(() => window.__motion.subscribers())).toBe(1);
});
