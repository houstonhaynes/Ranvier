// Shared steps of the browser tests: opening a page on the page clock, reading animated values, finishing WAAPI
// animations, and saving screenshots.
import { expect } from "@playwright/test";
import { mkdirSync } from "node:fs";

const shots = process.env.MOTION_SHOTS;

// Installs the page clock, paused, before the page's scripts run, so anime captures the controlled Date and frame
// callback and time moves only through runFor; records page errors for expectNoErrors.
export async function openPage(page, path) {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.errors = errors;
  const start = new Date("2026-10-21T09:00:00Z").getTime();
  await page.clock.install({ time: start });
  await page.clock.pauseAt(start + 1);
  await page.goto(path);
  await page.waitForFunction(() => window.__motion);
  await page.clock.runFor(16);
  return errors;
}

export async function opacityOf(locator) {
  return locator.evaluate(element => Number(getComputedStyle(element).opacity));
}

// The horizontal scale of the element's computed transform, 1 for none.
export async function scaleOf(locator) {
  return locator.evaluate(element => {
    const transform = getComputedStyle(element).transform;
    return transform === "none" ? 1 : Math.round(new DOMMatrixReadOnly(transform).a * 1000) / 1000;
  });
}

export async function cardBox(page, selector) {
  return page.locator(selector).boundingBox();
}

// Finishes every WAAPI animation under `selector`, the FLIP layout's among them.
export async function finishAnimations(page, selector) {
  await page.evaluate(selector => {
    for (const animation of document.querySelector(selector).getAnimations({ subtree: true })) animation.finish();
  }, selector);
}

// Saves a full-page screenshot to MOTION_SHOTS/<name>.png when the variable is set.
export async function shoot(page, name) {
  if (!shots) return;
  mkdirSync(shots, { recursive: true });
  await page.screenshot({ path: `${shots}/${name}.png`, fullPage: true });
}

export function expectNoErrors(page) {
  expect(page.errors).toEqual([]);
}
