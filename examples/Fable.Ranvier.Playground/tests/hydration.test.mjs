import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests/HydrationChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
for (const name of ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event"]) globalThis[name] = name === "window" ? dom.window : dom.window[name];
globalThis.document = dom.window.document;
after(() => { dom.window.close(); for (const name of ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event"]) delete globalThis[name]; });
for (const [name, check] of cases) test(name, () => check());
