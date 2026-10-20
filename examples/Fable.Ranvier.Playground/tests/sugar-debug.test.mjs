import test, { after } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests-debug/SugarDebugChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
const names = ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event"];
for (const name of names) globalThis[name] = name === "window" ? dom.window : dom.window[name];
after(() => { dom.window.close(); for (const name of names) delete globalThis[name]; });
for (const [name, check] of cases) test(name, () => check());

const template = dir => readFileSync(new URL(`../${dir}/experiments/Ranvier.View.Portable/Template.js`, import.meta.url), "utf8");
test("the Release Template.js has no region-read counter", () => assert.equal(template("generated-tests").includes("RegionReads"), false));
test("the Debug Template.js has the region-read counter", () => assert.equal(template("generated-tests-debug").includes("RegionReads"), true));
