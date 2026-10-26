import { test, expect } from "@playwright/test";
import { openPage, expectNoErrors } from "./support.mjs";

const points = async page => {
  const path = await page.locator(".chart-line").getAttribute("d");
  const numbers = path.match(/-?\d+(?:\.\d+)?/g).map(Number);
  return Array.from({ length: numbers.length / 2 }, (_, i) => numbers.slice(i * 2, i * 2 + 2));
};

test.afterEach(async ({ page }) => expectNoErrors(page));

test("samples scroll continuously while the fixed right edge eases to the new value", async ({ page }) => {
  await openPage(page, "/dashboard/");
  await page.clock.runFor(25_450);
  await page.getByRole("button", { name: "Pause", exact: true }).click();
  await page.clock.runFor(200);
  const before = await points(page);
  await page.evaluate(() => window.__motion.push());
  await page.clock.runFor(100);
  const growing = await points(page);
  expect(growing[10][0]).toBeLessThan(before[10][0]);
  expect(growing[10][1]).toBeCloseTo(before[10][1], 1);
  expect(growing.at(-1)[0]).toBe(600);
  expect(growing.at(-1)[1]).not.toBe(before.at(-1)[1]);
  await page.clock.runFor(350);
  const sliding = await points(page);
  expect(sliding[10][0]).toBeLessThan(before[10][0]);
  expect(sliding[10][1]).toBeCloseTo(before[10][1], 1);
  await page.clock.runFor(400);
  const late = await points(page);
  const displacement = before[10][0] - late[10][0];
  expect(displacement).toBeGreaterThan(600 / 23 * 0.8);
  expect(displacement).toBeLessThan(600 / 23 * 0.9);
  expect(late[10][1]).toBeCloseTo(before[10][1], 1);
  await page.clock.runFor(200);
  const settled = await points(page);
  expect(settled[9][0]).toBeCloseTo(before[10][0] - 600 / 23, 0);
  expect(settled[9][1]).toBeCloseTo(before[10][1], 1);
  expect(settled.at(-1)[0]).toBe(600);
});

test("reconnect retains chart history and resumes with one subscription", async ({ page }) => {
  await openPage(page, "/dashboard/");
  await page.clock.runFor(8_550);
  await page.getByRole("button", { name: "Pause", exact: true }).click();
  const history = await page.evaluate(() => window.__motion.samples().map(s => s.Tick));
  await page.getByRole("button", { name: "Reconnect", exact: true }).click();
  await expect(page.locator(".metrics")).toHaveAttribute("data-refreshing", "true");
  await page.evaluate(() => window.__motion.push());
  await page.clock.runFor(950);
  const resumed = await page.evaluate(() => window.__motion.samples().map(s => s.Tick));
  expect(resumed.slice(0, -1)).toEqual(history);
  await expect(page.locator(".metrics")).not.toHaveAttribute("data-refreshing", "true");
  expect(await page.evaluate(() => window.__motion.subscribers())).toBe(1);
});
