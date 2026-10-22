import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests/ArmsChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
const names = ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event", "EventTarget"];
for (const name of names) globalThis[name] = name === "window" ? dom.window : dom.window[name];
after(() => { dom.window.close(); for (const name of names) delete globalThis[name]; });
for (const [name, check] of cases) test(name, () => check());
