import { JSDOM } from "jsdom";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { makeClock, interceptIn } from "../../tests/conformance-host.mjs";

const dom = new JSDOM("<!doctype html><body></body>", { pretendToBeVisual: true });
for (const name of ["window", "document", "Event", "MouseEvent", "PointerEvent", "KeyboardEvent", "InputEvent", "FocusEvent", "Node", "EventTarget"])
  globalThis[name] = name === "window" ? dom.window : dom.window[name];
const clock = makeClock([globalThis, dom.window]);
const { create } = await import("../../generated-inspect/JsdomHarness.js");
const { keyed, boardHydration } = await import("../../generated-inspect/LoopBench.js");
const host = { post: clock.post, runMicrotasks: clock.runMicrotasks, advanceBy: clock.advanceBy, record: () => {}, errors: () => [], intercept: interceptIn(dom.window) };
const harness = create(dom.window.document, host);
const samples = [];
try {
  for (let i = 0; i < 10; i++) {
    const [mount, reorder] = keyed(dom.window.document, () => performance.now());
    const hydration = boardHydration(harness, () => performance.now());
    samples.push({ mount, reorder, hydration });
  }
} finally {
  clock.restore();
  dom.window.close();
}
const result = { host: "jsdom", unit: "milliseconds", samples };
const outIndex = process.argv.indexOf("--out");
if (outIndex >= 0) {
  const out = resolve(process.argv[outIndex + 1]);
  mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, JSON.stringify(result, null, 2) + "\n");
}
console.log(JSON.stringify(result, null, 2));
