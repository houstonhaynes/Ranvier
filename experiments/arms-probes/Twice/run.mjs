// Runs the Fable build of the twice-shown probe under jsdom.
import { createRequire } from "node:module";
const require = createRequire(new URL("../../../examples/Fable.Ranvier.Playground/package.json", import.meta.url));
const { JSDOM } = require("jsdom");
const dom = new JSDOM("<!doctype html><body></body>");
for (const name of ["window", "document", "Node", "Event", "MutationObserver", "FocusEvent", "HTMLElement", "Element", "Comment", "Text"])
  globalThis[name] = name === "window" ? dom.window : dom.window[name];
await import("./out/Main.js");
