// The kanban page in Chromium under page.clock: anime's engine runs on the page clock, and the FLIP layout's WAAPI
// animations, which the clock does not drive, are read through getAnimations() and finished by hand.
import { test, expect } from "@playwright/test";
import { cardBox, expectNoErrors, finishAnimations, opacityOf, openPage, scaleOf, shoot } from "./support.mjs";

const card = id => `.card-slot[data-id="${id}"]`;

// Records each WAAPI animation the page starts, with its target's card id, before the page's scripts run: the layout's
// animations run on the real clock and can finish before a test reads getAnimations().
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.__animated = [];
    const animate = Element.prototype.animate;
    Element.prototype.animate = function (...args) {
      const animation = animate.apply(this, args);
      window.__animated.push({ id: this.dataset?.id ?? null, animation });
      return animation;
    };
  });
  await openPage(page, "/kanban/");
});

const animatedIds = page => page.evaluate(() => window.__animated.map(entry => entry.id));

test.afterEach(async ({ page }) => expectNoErrors(page));

test("renders three lanes and five cards, and exposes the loop", async ({ page }) => {
  await expect(page.locator(".card-slot")).toHaveCount(5);
  await expect(page.locator(".lane .count")).toHaveText(["2", "2", "1"]);
  const cards = await page.evaluate(() => window.__motion.cards().map(c => [c.Id, c.Column]));
  expect(cards).toEqual([[1, 0], [2, 0], [3, 1], [4, 1], [5, 2]]);
  await shoot(page, "kanban");
});

test("add: the new card fades and scales in with presence on the page clock", async ({ page }) => {
  await page.click("button.add");
  const added = page.locator(card(6));
  await expect(added).toHaveCount(1);
  await page.clock.runFor(16);
  const early = { opacity: await opacityOf(added), scale: await scaleOf(added) };
  await page.clock.runFor(48);
  const middle = { opacity: await opacityOf(added), scale: await scaleOf(added) };
  await page.clock.runFor(800);
  const settled = { opacity: await opacityOf(added), scale: await scaleOf(added) };
  expect(early.opacity).toBeLessThan(0.5);
  expect(early.scale).toBeLessThan(0.95);
  expect(middle.opacity).toBeGreaterThan(early.opacity);
  expect(middle.scale).toBeGreaterThan(early.scale);
  expect(settled).toEqual({ opacity: 1, scale: 1 });
  await expect(page.locator(".lane .count").first()).toHaveText("3");
});

test("delete: the card is pinned, fades, and leaves when its exit completes", async ({ page }) => {
  await page.click(`${card(3)} button.delete`);
  const leaving = page.locator(card(3));
  await expect(leaving).toHaveCSS("position", "absolute");
  await page.clock.runFor(120);
  const middle = await opacityOf(leaving);
  expect(middle).toBeGreaterThan(0);
  expect(middle).toBeLessThan(1);
  await page.clock.runFor(300);
  await expect(leaving).toHaveCount(0);
  await expect(page.locator(".card-slot")).toHaveCount(4);
  await expect(page.locator(".lane .count")).toHaveText(["2", "1", "1"]);
});

test("move: FLIP animates the card across lanes and it settles in the next lane", async ({ page }) => {
  const before = await cardBox(page, card(1));
  await page.click(`${card(1)} button.right`);
  const moved = page.locator(card(1));
  await expect(moved).toHaveAttribute("data-lane", "1");
  expect(await animatedIds(page)).toContain("1");
  await finishAnimations(page, ".board");
  await page.clock.runFor(1000);
  const after = await cardBox(page, card(1));
  const lane = await page.locator(".lane").nth(1).boundingBox();
  expect(after.x).toBeGreaterThan(before.x + 100);
  expect(after.x).toBeGreaterThanOrEqual(lane.x);
  expect(after.x + after.width).toBeLessThanOrEqual(lane.x + lane.width);
});

test("shuffle moves cards with FLIP, and undo restores the board", async ({ page }) => {
  const start = await page.evaluate(() => window.__motion.cards().map(c => [c.Id, c.Column]));
  await page.click("button.shuffle");
  const shuffled = await page.evaluate(() => window.__motion.cards().map(c => [c.Id, c.Column]));
  expect(shuffled).not.toEqual(start);
  expect((await animatedIds(page)).filter(id => id !== null).length).toBeGreaterThan(0);
  await finishAnimations(page, ".board");
  await page.clock.runFor(1000);
  for (const [id, column] of shuffled) await expect(page.locator(card(id))).toHaveAttribute("data-lane", String(column));
  await page.click("button.undo");
  await finishAnimations(page, ".board");
  await page.clock.runFor(1000);
  expect(await page.evaluate(() => window.__motion.cards().map(c => [c.Id, c.Column]))).toEqual(start);
  await expect(page.locator("button.undo")).toBeDisabled();
});

test("drag: a card dragged one lane to the right drops into that lane", async ({ page }) => {
  const box = await cardBox(page, card(2));
  const lane = await page.locator(".lane").first().boundingBox();
  const x = box.x + box.width / 2;
  const y = box.y + 20;
  await page.mouse.move(x, y);
  await page.mouse.down();
  for (let step = 1; step <= 10; step++) {
    await page.mouse.move(x + (lane.width * step) / 10, y + step * 2);
    await page.clock.runFor(16);
  }
  await expect(page.locator(card(2))).toHaveClass(/dragging/);
  await page.mouse.up();
  await page.clock.runFor(32);
  await expect(page.locator(card(2))).toHaveAttribute("data-lane", "1");
  await finishAnimations(page, ".board");
  await page.clock.runFor(1000);
  const dropped = await cardBox(page, card(2));
  const target = await page.locator(".lane").nth(1).boundingBox();
  expect(dropped.x).toBeGreaterThanOrEqual(target.x);
  expect(dropped.x + dropped.width).toBeLessThanOrEqual(target.x + target.width);
});

test("clear holds every card until its exit completes, then shows the empty note", async ({ page }) => {
  await page.click("button.clear");
  await expect(page.locator(".card-slot")).toHaveCount(5);
  await page.clock.runFor(400);
  await expect(page.locator(".card-slot")).toHaveCount(0);
  await expect(page.locator(".empty")).toBeVisible();
  await page.click("button.undo");
  await page.clock.runFor(600);
  await expect(page.locator(".card-slot")).toHaveCount(5);
  await expect(page.locator(".empty")).toHaveCount(0);
});
