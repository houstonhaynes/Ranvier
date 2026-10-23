import test, { after } from "node:test";
import { JSDOM, VirtualConsole } from "jsdom";
import { cases } from "../generated-tests-counters/SugarChecks.js";
import { create } from "../generated-tests-counters/JsdomHarness.js";
import { makeClock, interceptIn } from "./conformance-host.mjs";

// Listener failures reach the virtual console and the case, as in the conformance runner.
const listenerErrors = [];
const virtualConsole = new VirtualConsole();
virtualConsole.on("jsdomError", e => {
  if (e.type === "not-implemented" || String(e.message).startsWith("Not implemented")) return;
  listenerErrors.push(e.cause?.message ?? e.detail?.message ?? e.message);
});
const dom = new JSDOM("<!doctype html><body></body>", { virtualConsole });
const names = ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event", "EventTarget", "InputEvent"];
for (const name of names) globalThis[name] = name === "window" ? dom.window : dom.window[name];
globalThis.ranvierExpectCounters = true;
after(() => { dom.window.close(); for (const name of names) delete globalThis[name]; delete globalThis.ranvierExpectCounters; });

// The sugar conformance cases with the core counters compiled in, each with the harness clock over the one document.
for (const [name, run] of cases)
  test(name, () => {
    listenerErrors.length = 0;
    const clock = makeClock([globalThis, dom.window]);
    const host = { post: clock.post, runMicrotasks: clock.runMicrotasks, advanceBy: clock.advanceBy, record: () => {}, errors: () => listenerErrors.splice(0), intercept: interceptIn(dom.window) };
    try { run(create(dom.window.document, host)); }
    finally { clock.restore(); }
  });
