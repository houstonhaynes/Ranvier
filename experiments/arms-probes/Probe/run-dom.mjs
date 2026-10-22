// Runs the Fable build of the probe under jsdom: server HTML, mounted updates and Strict hydration of the .NET HTML.
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
const require = createRequire(new URL("../../../examples/Fable.Ranvier.Playground/package.json", import.meta.url));
const { JSDOM } = require("jsdom");
const dom = new JSDOM("<!doctype html><body></body>");
for (const name of ["window", "document", "Node", "Event", "MutationObserver", "FocusEvent", "HTMLElement", "Element", "Comment", "Text"])
  globalThis[name] = name === "window" ? dom.window : dom.window[name];
globalThis.servedHtml = readFileSync(new URL("./served.html", import.meta.url), "utf8").trim();
globalThis.benchArms = process.argv.includes("--bench");
await import("./out/Main.js");
