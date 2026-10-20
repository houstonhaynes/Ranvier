import { mountKeyed as ranvierKeyed, mountArms as ranvierArmsMount } from "../../generated-tests/KeyedRowsPerf.js";
import { mountKeyed as sugarKeyed, mountArms as sugarArmsMount } from "../../generated-tests/KeyedRowsSugarPerf.js";
import { mountKeyed as solidKeyed, mountArms as solidArms } from "./keyed-solid.jsx";

// MEASUREMENT (measure/poc-rerun-2026-10-15): production rows and the production arm adapter against Solid 2.
// Ranvier's operations record exposes F# field names; the Ranvier, sugared Ranvier and Solid adapters present the same shape.
const ranvierRows = host => {
  const ops = ranvierKeyed(host);
  return { create: n => ops.Create(n), append: n => ops.Append(n), updateEvery10th: () => ops.UpdateEvery10th(), swap: () => ops.Swap(), remove: i => ops.Remove(i), clear: () => ops.Clear(), dispose: () => ops.Dispose() };
};
const sugarRows = host => {
  const ops = sugarKeyed(host);
  return { create: n => ops.Create(n), append: n => ops.Append(n), updateEvery10th: () => ops.UpdateEvery10th(), swap: () => ops.Swap(), remove: i => ops.Remove(i), clear: () => ops.Clear(), dispose: () => ops.Dispose() };
};
// ranvier-sugar: the same fixtures written with the template sugar (forEachFixed, armOf).
const adapters = { ranvier: ranvierRows, "ranvier-sugar": sugarRows, solid: solidKeyed };
const ranvierArms = host => {
  const ops = ranvierArmsMount(host);
  return { create: n => ops.Create(n), switch: () => ops.Switch(), payload: () => ops.Payload(), clear: () => ops.Clear(), dispose: () => ops.Dispose() };
};
// The arm fixture: every row holds one matchWith region.
const sugarArms = host => {
  const ops = sugarArmsMount(host);
  return { create: n => ops.Create(n), switch: () => ops.Switch(), payload: () => ops.Payload(), clear: () => ops.Clear(), dispose: () => ops.Dispose() };
};
const armAdapters = { ranvier: ranvierArms, "ranvier-sugar": sugarArms, solid: solidArms };
const adapterFor = (operation, mode) => (operation.fixture === "arms" ? armAdapters : adapters)[mode];

// Each operation: untimed setup on a fresh list, then the timed step, then the expected row count.
export const operations = {
  create1k: { setup: () => {}, run: list => list.create(1000), rows: 1000 },
  create10k: { setup: () => {}, run: list => list.create(10000), rows: 10000 },
  replace1k: { setup: list => list.create(1000), run: list => list.create(1000), rows: 1000 },
  update10th1k: { setup: list => list.create(1000), run: list => list.updateEvery10th(), rows: 1000 },
  swap1k: { setup: list => list.create(1000), run: list => list.swap(), rows: 1000 },
  remove1k: { setup: list => list.create(1000), run: list => list.remove(3), rows: 999 },
  remove10k: { setup: list => list.create(10000), run: list => list.remove(3), rows: 9999 },
  clear1k: { setup: list => list.create(1000), run: list => list.clear(), rows: 0 },
  clear10k: { setup: list => list.create(10000), run: list => list.clear(), rows: 0 },
  append1k: { setup: list => list.create(1000), run: list => list.append(1000), rows: 2000 },
  armsCreate1k: { fixture: "arms", setup: () => {}, run: list => list.create(1000), rows: 1000 },
  armsSwitch1k: { fixture: "arms", setup: list => list.create(1000), run: list => list.switch(), rows: 1000 },
  armsPayload1k: { fixture: "arms", setup: list => list.create(1000), run: list => list.payload(), rows: 1000 },
  armsClear1k: { fixture: "arms", setup: list => list.create(1000), run: list => list.clear(), rows: 0 }
};

let hostIndex = 0;
function host() {
  const node = document.createElement("main");
  node.id = `keyed-host-${++hostIndex}`;
  document.body.append(node);
  return node;
}

function rowsOf(node) {
  return [...node.querySelectorAll("tbody > tr")].map(row => row.textContent);
}

// Kept as a named function so CPU profiles can select the samples inside the timed step.
function measuredRun(operation, list) {
  operation.run(list);
}

window.keyedPerf = {
  modes: Object.keys(adapters),
  /// Whether `mode` has an adapter for the fixture of operation `name`.
  supports(mode, name) { return Boolean(adapterFor(operations[name], mode)); },
  operations: Object.keys(operations),
  /// The DOM text of every row after the operation; both adapters number rows from 1.
  verify(mode, name) {
    const node = host(), operation = operations[name];
    const list = adapterFor(operation, mode)(node);
    try {
      operation.setup(list);
      operation.run(list);
      const rows = rowsOf(node);
      if (rows.length !== operation.rows) throw Error(`${mode}/${name}: ${rows.length} rows, expected ${operation.rows}`);
      return rows.join("\n");
    } finally {
      list.dispose();
      if (node.childNodes.length) throw Error(`${mode}/${name}: disposal retained DOM`);
      node.remove();
    }
  },
  sample(mode, name) {
    const node = host(), operation = operations[name];
    const list = adapterFor(operation, mode)(node);
    try {
      operation.setup(list);
      const start = performance.now();
      measuredRun(operation, list);
      const ms = performance.now() - start;
      if (node.querySelectorAll("tbody > tr").length !== operation.rows) throw Error(`${mode}/${name}: wrong row count`);
      return ms;
    } finally {
      list.dispose();
      node.remove();
    }
  },
  /// Mounts a list of `count` rows and keeps it until the returned release runs.
  retain(mode, count) {
    const node = host(), list = adapters[mode](node);
    list.create(count);
    return () => { list.dispose(); node.remove(); };
  },
  /// Mounts an empty list and returns its create and release steps; used by the heap-snapshot diff.
  mounted(mode) {
    const node = host(), list = adapters[mode](node);
    return { create: count => list.create(count), release: () => { list.dispose(); node.remove(); } };
  },
  /// Counts DOM calls made by the timed step.
  domCalls(mode, name) {
    const node = host(), operation = operations[name];
    const list = adapterFor(operation, mode)(node);
    const counts = {}, undo = [];
    const wrap = (proto, method, key = method) => {
      const original = proto[method];
      proto[method] = function (...args) { counts[key] = (counts[key] || 0) + 1; return original.apply(this, args); };
      undo.push(() => proto[method] = original);
    };
    try {
      operation.setup(list);
      wrap(Document.prototype, "createElement");
      wrap(Document.prototype, "createTextNode");
      wrap(Document.prototype, "createComment");
      wrap(Document.prototype, "createDocumentFragment");
      wrap(Document.prototype, "importNode");
      wrap(Node.prototype, "appendChild");
      wrap(Node.prototype, "insertBefore");
      wrap(Node.prototype, "removeChild");
      wrap(Node.prototype, "cloneNode");
      wrap(Node.prototype, "contains");
      wrap(Element.prototype, "remove");
      wrap(CharacterData.prototype, "remove");
      wrap(Element.prototype, "before", "elementBefore");
      wrap(Element.prototype, "replaceWith");
      wrap(WeakSet.prototype, "add", "weakSetAdd");
      wrap(WeakSet.prototype, "delete", "weakSetDelete");
      wrap(WeakSet.prototype, "has", "weakSetHas");
      wrap(EventTarget.prototype, "addEventListener");
      wrap(EventTarget.prototype, "removeEventListener");
      measuredRun(operation, list);
    } finally {
      for (const restore of undo.reverse()) restore();
      list.dispose();
      node.remove();
    }
    return counts;
  },
  /// Prepares a fresh list and returns a step that runs the operation once; used by the profiler. `beforeSetup` runs
  /// after the list mounts and before its setup.
  prepared(mode, name, beforeSetup) {
    const node = host(), operation = operations[name];
    const list = adapterFor(operation, mode)(node);
    beforeSetup?.();
    operation.setup(list);
    return {
      run() { measuredRun(operation, list); },
      cleanup() { list.dispose(); node.remove(); }
    };
  }
};
