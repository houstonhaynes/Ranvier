// The page checks under jsdom on a mocked clock. anime evaluates `isBrowser` and captures `Date.now` when it is first
// imported, so the DOM globals, the stubs jsdom lacks and the mocked clock are in place before animejs and the compiled
// checks are imported.
import test, { mock } from "node:test";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body></body>", { url: "http://127.0.0.1/" });
const globals = ["Node", "Element", "HTMLElement", "SVGElement", "NodeList", "HTMLCollection", "getComputedStyle", "Event", "FocusEvent", "MutationObserver", "HashChangeEvent", "location", "history"];
globalThis.window = dom.window;
globalThis.document = dom.window.document;
for (const name of globals) globalThis[name] = dom.window[name];
// jsdom has no layout: the frame callback, DOMPoint, DOMMatrix, ResizeObserver and path geometry are stubbed.
globalThis.requestAnimationFrame = dom.window.requestAnimationFrame = () => 0;
globalThis.cancelAnimationFrame = dom.window.cancelAnimationFrame = () => {};
class DOMPoint { constructor(x = 0, y = 0) { this.x = x; this.y = y; } matrixTransform() { return new DOMPoint(this.x, this.y); } }
class DOMMatrix {
  constructor() { Object.assign(this, { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 }); }
  inverse() { return new DOMMatrix(); }
  preMultiplySelf() { return this; }
}
class ResizeObserver { observe() {} unobserve() {} disconnect() {} }
// anime's WAAPI module reads `CSS` as a free identifier; without registerProperty it skips its transform properties.
globalThis.CSS = dom.window.CSS = {};
// jsdom has no Web Animations: an element's animate() returns an inert animation, so the FLIP layout's transforms do not
// move and the checks read its timeline and the anime engine's values only.
dom.window.Element.prototype.animate = function () {
  const inert = () => {};
  return { currentTime: 0, playbackRate: 1, playState: "finished", onfinish: null, oncancel: null, effect: null, finished: Promise.resolve(),
    play: inert, pause: inert, finish: inert, cancel: inert, commitStyles: inert, reverse: inert, persist: inert };
};
for (const [name, value] of Object.entries({ DOMPoint, DOMMatrix, ResizeObserver })) globalThis[name] = dom.window[name] = value;
dom.window.document.fonts = { status: "loaded", ready: Promise.resolve() };
globalThis.matchMedia = dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
// A path's length is 10 and its point at a length sits at that x on the path's first y.
for (const type of [dom.window.HTMLElement, dom.window.SVGElement]) {
  type.prototype.getTotalLength = () => 10;
  type.prototype.getPointAtLength = function (length) {
    const numbers = (this.getAttribute("d") || "").match(/-?\d+(\.\d+)?/g)?.map(Number) ?? [0, 0];
    return { x: length, y: numbers[1] ?? 0 };
  };
}
mock.timers.enable({ apis: ["Date", "setTimeout", "setInterval"], now: 1000 });

// The checks find each element's draggable and the latest scroll observers.
const anime = await import("animejs");
globalThis.motionDraggables = [];
const enable = anime.Draggable.prototype.enable;
anime.Draggable.prototype.enable = function (...args) {
  globalThis.motionDraggables.push(this);
  return enable.apply(this, args);
};
globalThis.motionObservers = [];
const link = anime.ScrollObserver.prototype.link;
anime.ScrollObserver.prototype.link = function (...args) {
  globalThis.motionObservers.push(this);
  return link.apply(this, args);
};

const { cases, prepare } = await import("../generated-tests/Checks.js");
prepare();
const clock = ms => mock.timers.tick(ms);
// A check defers its last steps with Harness.later; they run once the microtasks it queued have drained (setImmediate,
// which the mocked timers leave real, runs after them). Mounts registered with Harness.owned are disposed after every
// check, so a failed check leaves nothing mounted for the next.
globalThis.motionLater = [];
globalThis.motionOpen = [];
globalThis.motionRelease = () => {
  const open = globalThis.motionOpen.splice(0).reverse();
  let failure = null;
  for (const dispose of open) {
    try { dispose(); } catch (error) { failure ??= error; }
  }
  if (failure) throw failure;
};
const run = async check => {
  try {
    check(clock);
    await new Promise(resolve => setImmediate(resolve));
    while (globalThis.motionLater.length > 0) globalThis.motionLater.shift()();
  } finally {
    globalThis.motionLater.length = 0;
    globalThis.motionRelease();
  }
};
for (const { Name, Check } of cases) test(Name, () => run(Check));
