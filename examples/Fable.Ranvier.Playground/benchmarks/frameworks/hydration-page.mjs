import { serverHtml, hydrateFixture, hydrateFixtureLenient, mountFixture } from "../../generated-tests/HydrationPerf.js";
import { hydrateComponentPage, hydrateKeyedPage, flushSolid } from "./hydration-solid.jsx";

// Solid 2's server HTML for every fixture and row count, with its hydration bootstrap markup, written by
// hydration-bench.mjs into the bundle's root. Loaded on first use, so the other benchmarks never fetch it.
let loaded;
const load = () => loaded ??= (async () => {
  const response = await fetch("/solid-html.json");
  if (!response.ok) throw Error(`solid-html.json: ${response.status}`);
  const solid = await response.json();
  // A contextual fragment's scripts run when inserted.
  document.head.append(document.createRange().createContextualFragment(solid.bootstrap));
  if (!window._$HY) throw Error("Solid's hydration bootstrap did not run");
  return solid;
})();

const ranvierHtml = new Map();
const servedHtml = (fixture, rows) => {
  const key = `${fixture}/${rows}`;
  if (!ranvierHtml.has(key)) ranvierHtml.set(key, serverHtml(fixture, rows));
  return ranvierHtml.get(key);
};
const modes = {
  "ranvier-hydrate": { html: servedHtml, call: (fixture, host, rows) => hydrateFixture(fixture, host, rows), flush() {} },
  // Hydration.HydrationOptions.defaults: Strict = false. Sampled by the bench's lenient block only.
  "ranvier-hydrate-lenient": { html: servedHtml, call: (fixture, host, rows) => hydrateFixtureLenient(fixture, host, rows), flush() {} },
  "ranvier-mount": { html: () => null, call: (fixture, host, rows) => mountFixture(fixture, host, rows), flush() {} },
  "solid-hydrate": {
    html: (fixture, rows, solid) => solid.html[fixture][rows],
    call: (fixture, host, rows) => (fixture === "component" ? hydrateComponentPage : hydrateKeyedPage)(host, rows),
    flush: flushSolid
  }
};

let hostIndex = 0;
// A host attached to the body, holding `html` as a document parse places it.
function freshHost(html) {
  const host = document.createElement("div");
  host.id = `hydration-${hostIndex++}`;
  document.body.append(host);
  if (html !== null) {
    const range = document.createRange();
    range.selectNodeContents(host);
    host.replaceChildren(range.createContextualFragment(html));
  }
  return host;
}

async function hosts(mode, fixture, rows, count) {
  const solid = await load();
  const html = modes[mode].html(fixture, rows, solid);
  return Array.from({ length: count }, () => freshHost(html));
}

const rowsOf = (fixture, host) => host.querySelectorAll(fixture === "component" ? "section > div.panel" : "tbody > tr").length;
const texts = host => {
  const walker = document.createTreeWalker(host, NodeFilter.SHOW_TEXT), found = [];
  while (walker.nextNode()) found.push(walker.currentNode);
  return found;
};
const sameNodes = (before, after) => before.length === after.length && before.every((node, index) => node === after[index]);

let prepared;

window.hydrationBench = {
  modes: Object.keys(modes),
  // Fills `count` hosts for the next `sample` of the same arguments, so the runner can collect garbage in between.
  async prepare(mode, fixture, rows, count) {
    prepared = { key: `${mode}/${fixture}/${rows}/${count}`, hosts: await hosts(mode, fixture, rows, count) };
  },
  // Hydrates (or mounts) `count` hosts filled with the fixture's server HTML outside the timed region; returns
  // milliseconds per call.
  async sample(mode, fixture, rows, count) {
    const key = `${mode}/${fixture}/${rows}/${count}`;
    const filled = prepared?.key === key ? prepared.hosts : await hosts(mode, fixture, rows, count);
    prepared = undefined;
    const { call } = modes[mode], disposals = [];
    const start = performance.now();
    for (const host of filled) disposals.push(call(fixture, host, rows));
    const ms = (performance.now() - start) / count;
    for (const dispose of disposals) dispose();
    for (const host of filled) host.remove();
    return ms;
  },
  // Milliseconds from the call to the end of the first handled click on the first row's button (component) or link
  // (keyed), including the commit of the click's update.
  async firstClick(mode, fixture, rows) {
    const [host] = await hosts(mode, fixture, rows, 1);
    const { call, flush } = modes[mode];
    const start = performance.now();
    const dispose = call(fixture, host, rows);
    host.querySelector(fixture === "component" ? "button" : "a").click();
    flush();
    const ms = performance.now() - start;
    const clicked = fixture === "component" ? host.querySelector("button").textContent : "handled";
    dispose();
    host.remove();
    if (clicked !== "1" && clicked !== "handled") throw Error(`${mode}/${fixture}/${rows}: the first click shows ${clicked}`);
    return ms;
  },
  // Checks the row count, the first row's content and, after hydration, that every element and text node is the
  // server's; a Ranvier host is empty after disposal.
  async verify(mode, fixture, rows) {
    const [host] = await hosts(mode, fixture, rows, 1);
    const elements = Array.from(host.querySelectorAll("*")), served = texts(host);
    const dispose = modes[mode].call(fixture, host, rows);
    const found = rowsOf(fixture, host);
    const elementsKept = sameNodes(elements, Array.from(host.querySelectorAll("*")));
    const textsKept = sameNodes(served, texts(host));
    const first = fixture === "component"
      ? `${host.querySelector("input").value}|${host.querySelector("button").textContent}`
      : `${host.querySelector("td").textContent}|${host.querySelector("a").textContent}`;
    dispose();
    const emptied = host.childNodes.length === 0;
    host.remove();
    const failures = [];
    if (found !== rows) failures.push(`${found} rows`);
    if (first !== (fixture === "component" ? "row 1|0" : "1|row 1")) failures.push(`first row ${first}`);
    if (mode !== "ranvier-mount" && !elementsKept) failures.push("hydration replaced server elements");
    if (mode !== "ranvier-mount" && !textsKept) failures.push("hydration replaced server text nodes");
    if (mode.startsWith("ranvier") && !emptied) failures.push("the host is not empty after disposal");
    if (failures.length) throw Error(`${mode}/${fixture}/${rows}: ${failures.join("; ")}`);
    return { mode, fixture, rows: found, elements: elements.length, elementsKept, textsKept, emptied };
  },
  // Hydrates (or mounts) `count` filled hosts and returns their release.
  async retain(mode, fixture, rows, count) {
    const filled = await hosts(mode, fixture, rows, count);
    const disposals = filled.map(host => modes[mode].call(fixture, host, rows));
    return () => { for (const dispose of disposals) dispose(); for (const host of filled) host.remove(); };
  }
};
