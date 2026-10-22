import test, { after } from "node:test";
import { readFileSync } from "node:fs";
import { JSDOM } from "jsdom";
import { hydrations } from "../generated-tests/ArmsChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
const names = ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event", "EventTarget"];
for (const name of names) globalThis[name] = name === "window" ? dom.window : dom.window[name];
after(() => { dom.window.close(); for (const name of names) delete globalThis[name]; });

const golden = readFileSync(new URL("../../../experiments/Ranvier.View.Arms/Checks/arms-server.golden.txt", import.meta.url), "utf8")
  .replace(/\r\n/g, "\n").replace(/\n+$/, "").split("\n").map(line => line.split("|"));

for (const [name, check] of hydrations(golden.map(([fixture, state, , ...html]) => [fixture, state, html.join("|")])))
  test(name, check);
