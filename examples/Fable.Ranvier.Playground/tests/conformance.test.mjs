// The shared conformance cases under jsdom. Writes each case's artefacts to generated-conformance/jsdom/<module>/<case>/.
// Each case starts from an empty document and the harness clock of conformance-host.mjs, as each twin case starts from a
// fresh twin. One jsdom serves the run, as one document serves a page: Ranvier.View.Dom keeps a definition's compiled
// template on the definition, so a template compiled in one window's document would reach the next case's nodes
// with the earlier window's prototypes.
import test, { after, mock } from "node:test";
import { JSDOM, VirtualConsole } from "jsdom";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { create } from "../generated-tests/JsdomHarness.js";
import { modules } from "../generated-tests/ConformanceCases.js";
import { skippedOn } from "../generated-tests/experiments/Ranvier.View.Testing/Conformance.js";
import { makeClock, interceptIn, trackShared } from "./conformance-host.mjs";

const globals = ["window", "document", "Event", "MouseEvent", "PointerEvent", "KeyboardEvent", "InputEvent", "FocusEvent", "MutationObserver", "Node", "EventTarget"];

const root = new URL("../generated-conformance/jsdom/", import.meta.url);
rmSync(root, { recursive: true, force: true });
// FNV-1a over the name's UTF-16 code units, as hex: TwinHarness.slug computes the same.
const nameHash = name => {
  let hash = 2166136261;
  for (let i = 0; i < name.length; i++) hash = Math.imul(hash ^ name.charCodeAt(i), 16777619) >>> 0;
  return hash.toString(16).padStart(8, "0");
};
// A case's artefact directory: its name in lower-case words, cut to 71 characters plus the name's hash when longer
// than 80.
export const slug = name => {
  const text = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  return text.length > 80 ? text.slice(0, 71).replace(/-+$/, "") + "-" + nameHash(name) : text;
};

// Listener failures reach the virtual console, as they would a browser's; jsdom's "Not implemented" notices (form
// submission, navigation) have no twin counterpart and stay out of the listener failures.
const listenerErrors = [];
const virtualConsole = new VirtualConsole();
virtualConsole.on("jsdomError", e => {
  if (e.type === "not-implemented" || String(e.message).startsWith("Not implemented")) return;
  // jsdom wraps an uncaught listener exception; `cause` is the thrown value.
  listenerErrors.push(e.cause?.message ?? e.detail?.message ?? e.message);
});
const dom = new JSDOM("<!doctype html><html><head></head><body></body></html>", { virtualConsole, pretendToBeVisual: true });
globalThis.window = dom.window;
for (const name of globals.slice(1)) globalThis[name] = name === "document" ? dom.window.document : dom.window[name];
after(() => { dom.window.close(); for (const name of globals) delete globalThis[name]; });

// The listeners and expandos a case leaves on the shared window, document, html, head and body fail it.
const { begin, leftovers } = trackShared(dom.window);

// The document as a new one has it: empty head and body without attributes, and nothing focused.
function resetDocument() {
  const document = dom.window.document;
  document.activeElement?.blur?.();
  for (const element of [document.head, document.body]) {
    element.replaceChildren();
    for (const attribute of [...element.attributes]) element.removeAttribute(attribute.name);
  }
  for (const attribute of [...document.documentElement.attributes]) document.documentElement.removeAttribute(attribute.name);
}

// Two case names of a module have distinct slugs.
for (const [moduleName, cases] of modules) {
  const slugs = new Map();
  for (const [name] of cases) {
    const directory = slug(name);
    if (slugs.has(directory)) throw new Error(`${moduleName}: "${name}" and "${slugs.get(directory)}" share the artefact directory ${directory}`);
    slugs.set(directory, name);
  }
}

for (const [moduleName, cases] of modules) {
  for (const [name, run] of cases) {
    if (skippedOn("jsdom", name)) { test(`${moduleName}: ${name}`, { skip: "another host's case" }, () => {}); continue; }
    test(`${moduleName}: ${name}`, async () => {
      const directory = new URL(`${moduleName}/${slug(name)}/`, root);
      let count = 0;
      resetDocument();
      listenerErrors.length = 0;
      begin();
      mock.timers.enable({ apis: ["Date"] });
      const clock = makeClock([globalThis, dom.window], milliseconds => { if (milliseconds > 0) mock.timers.tick(milliseconds); });
      const host = {
        post: clock.post,
        runMicrotasks: clock.runMicrotasks,
        advanceBy: clock.advanceBy,
        record: (artefact, text) => {
          mkdirSync(directory, { recursive: true });
          writeFileSync(new URL(`${String(++count).padStart(2, "0")}-${slug(artefact)}.txt`, directory), text);
        },
        errors: () => listenerErrors.splice(0),
        intercept: (target, member, wrap) => {
          const restore = intercept(target, member, wrap);
          let restored = false;
          const once = () => { if (!restored) { restored = true; restore(); } };
          restores.push(once);
          return once;
        },
      };
      const intercept = interceptIn(dom.window);
      const restores = [];
      let left = [];
      let pending = [];
      try {
        const result = run(create(dom.window.document, host));
        const awaits = result && typeof result.then === "function";
        if (!awaits) pending = clock.pending();
        // A case that returns a promise awaits JavaScript promise delivery: the real timer and microtask functions
        // return, with the work queued so far, before the await.
        if (awaits) {
          mock.timers.reset();
          clock.handOff();
          await result;
        }
      }
      finally {
        for (const restore of restores.reverse()) restore();
        clock.restore();
        mock.timers.reset();
        left = leftovers();
      }
      if (left.length > 0) throw new Error(`The case left state on the shared document: ${left.join("; ")}`);
      if (pending.length > 0) throw new Error(`The case left work queued on the harness clock: ${pending.join("; ")}`);
    });
  }
}
