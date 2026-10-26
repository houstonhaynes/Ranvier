import { test, expect } from "@playwright/test";
import { openPage, expectNoErrors } from "./support.mjs";

test("graphs have their own rendered page and navigation entry", async ({ request }) => {
  const response = await request.get("/graphs/");
  expect(await response.text()).toContain("Explore a signal");
  expect(await (await request.get("/")).text()).toContain('href="./graphs/"');
  const curves = [...(await response.text()).matchAll(/class="(?:signal-line|comparison-line)" d="([^"]+)"/g)];
  expect(curves).toHaveLength(2);
  for (const [, path] of curves) {
    const coordinates = path.match(/-?\d+(?:\.\d+)?(?:e[+-]?\d+)?/gi).map(Number);
    expect(coordinates.every(n => Number.isFinite(n) && n >= 0 && n <= 960)).toBe(true);
  }
});

test("the crosshair and tooltip follow the nearest data point", async ({ page }) => {
  await openPage(page, "/graphs/");
  const plot = page.locator(".explorer-svg");
  const box = await plot.boundingBox();
  await page.mouse.move(box.x + box.width * 0.3, box.y + box.height * 0.5);
  await page.clock.runFor(300);
  const first = Number(await page.locator(".focus-point").getAttribute("cx"));
  await expect(page.locator(".graph-tooltip")).toBeVisible();
  const firstText = await page.locator(".graph-tooltip").textContent();
  await page.mouse.move(box.x + box.width * 0.7, box.y + box.height * 0.5);
  await page.clock.runFor(100);
  const moving = Number(await page.locator(".focus-point").getAttribute("cx"));
  await page.clock.runFor(250);
  const settled = Number(await page.locator(".focus-point").getAttribute("cx"));
  expect(moving).toBeGreaterThan(first);
  expect(moving).toBeLessThan(settled);
  expect(await page.locator(".graph-tooltip").textContent()).not.toBe(firstText);
  expectNoErrors(page);
});

test("zoom and pan animate, stay bounded and reset to the whole day", async ({ page }) => {
  await openPage(page, "/graphs/");
  const plot = page.locator(".explorer-svg");
  const view = async () => (await plot.getAttribute("viewBox")).split(/[ ,]+/).map(Number);
  const original = await view();
  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  await page.clock.runFor(150);
  const moving = await view();
  await page.clock.runFor(650);
  const zoomed = await view();
  expect(moving[2]).toBeLessThan(original[2]);
  expect(moving[2]).toBeGreaterThan(zoomed[2]);
  await page.getByRole("button", { name: "Pan right", exact: true }).click();
  await page.clock.runFor(800);
  expect((await view())[0]).toBeGreaterThan(zoomed[0]);
  const box = await plot.boundingBox();
  await page.mouse.move(box.x + box.width * 0.6, box.y + box.height * 0.5);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.3, box.y + box.height * 0.5, { steps: 8 });
  await page.mouse.up();
  await page.clock.runFor(800);
  const dragged = await view();
  expect(dragged[0]).toBeGreaterThan(zoomed[0]);
  expect(dragged[0] + dragged[2]).toBeLessThanOrEqual(960.1);
  await page.getByRole("button", { name: "Reset view", exact: true }).click();
  await page.clock.runFor(800);
  expect(await view()).toEqual(original);
  expectNoErrors(page);
});

test("ranked bars animate to the next snapshot and retain their elements", async ({ page }) => {
  await openPage(page, "/graphs/");
  await page.clock.runFor(1000);
  await page.locator('[data-region="London"]').evaluate(e => e.dataset.retained = "yes");
  const row = page.locator('[data-region="London"]');
  const y = () => row.evaluate(e => new DOMMatrixReadOnly(getComputedStyle(e).transform).m42);
  const first = await y();
  await page.getByRole("button", { name: "Next snapshot", exact: true }).click();
  await page.clock.runFor(200);
  const moving = await y();
  await page.clock.runFor(900);
  expect(moving).not.toBe(first);
  expect(await y()).toBeCloseTo(0, 0);
  await expect(row).toHaveAttribute("data-retained", "yes");
  await expect(page.locator(".snapshot-time")).toHaveText("12:00");
  expectNoErrors(page);
});

test("wheel input during a zoom keeps the point beneath the pointer anchored", async ({ page }) => {
  await openPage(page, "/graphs/");
  const plot = page.locator(".explorer-svg");
  await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  await page.clock.runFor(150);
  const before = (await plot.getAttribute("viewBox")).split(/[ ,]+/).map(Number);
  const box = await plot.boundingBox();
  const position = 0.3;
  const anchor = before[0] + before[2] * position;
  await page.mouse.move(box.x + box.width * position, box.y + box.height * 0.5);
  await page.mouse.wheel(0, -200);
  await page.clock.runFor(800);
  const after = (await plot.getAttribute("viewBox")).split(/[ ,]+/).map(Number);
  expect(after[2]).toBeLessThan(before[2]);
  expect(after[0] + after[2] * position).toBeCloseTo(anchor, 0);
  expectNoErrors(page);
});

test("keyboard inspection resets cleanly and snapshot playback pauses", async ({ page }) => {
  await openPage(page, "/graphs/");
  const plot = page.locator(".explorer-svg");
  await plot.press("ArrowRight");
  await page.clock.runFor(300);
  await expect(page.locator(".graph-tooltip")).toBeVisible();
  await plot.press("+");
  await page.clock.runFor(800);
  await plot.press("ArrowLeft");
  await page.clock.runFor(300);
  await expect(page.locator(".graph-tooltip")).toBeVisible();
  await plot.press("0");
  await expect(page.locator(".graph-tooltip")).toHaveCount(0);
  await page.clock.runFor(800);
  expect((await plot.getAttribute("viewBox")).split(/[ ,]+/).map(Number)).toEqual([0, 0, 960, 280]);
  await page.getByRole("button", { name: "Play snapshots", exact: true }).click();
  await page.clock.runFor(2600);
  await expect(page.locator(".snapshot-time")).not.toHaveText("09:00");
  await page.getByRole("button", { name: "Pause snapshots", exact: true }).click();
  const paused = await page.locator(".snapshot-time").textContent();
  await page.clock.runFor(5200);
  await expect(page.locator(".snapshot-time")).toHaveText(paused);
  expectNoErrors(page);
});

test("inspection stays inside a narrow chart and the marker keeps its width while zooming", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 850 });
  await openPage(page, "/graphs/");
  const plot = page.locator(".explorer-svg");
  const marker = page.locator(".focus-point");
  const width = (await marker.boundingBox()).width;
  for (let i = 0; i < 3; i++) await page.getByRole("button", { name: "Zoom in", exact: true }).click();
  await page.clock.runFor(800);
  expect((await marker.boundingBox()).width).toBeCloseTo(width, 0);
  const box = await plot.boundingBox();
  await page.mouse.move(box.x + box.width * 0.97, box.y + box.height * 0.5);
  await page.clock.runFor(300);
  const tip = await page.locator(".graph-tooltip").boundingBox();
  expect(tip.x).toBeGreaterThanOrEqual(box.x);
  expect(tip.x + tip.width).toBeLessThanOrEqual(box.x + box.width + 1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(390);
  expectNoErrors(page);
});
