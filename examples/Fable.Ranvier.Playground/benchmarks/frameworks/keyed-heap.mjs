// PROTOTYPE (research/keyed-row-cost): retained heap per constructor of a mounted keyed list, from CDP heap snapshots.
// MEASUREMENT (measure/poc-rerun-2026-10-15): one unminified bundle running production rows and Solid 2. For each mode and row count: mount an
// empty list, snapshot, create the rows, snapshot, and diff the objects whose snapshot ids are new. The objects held by each
// field of a new row memo are reported separately, and `holders` attributes each new object to the new object that holds it.
// Expects generated-tests from keyed-rows.mjs --build.
// Usage: node benchmarks/frameworks/keyed-heap.mjs [--counts=1000,10000] [--processes=2] [--label=heap-memo-free] [--no-bundle]
import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const output = process.argv.find(arg => arg.startsWith("--out="))?.slice(6) ?? fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/poc-rerun-2026-10-15/", import.meta.url));
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.split("=")[1] ?? fallback;
const counts = argument("counts", "1000,10000").split(",").map(Number);
const processes = Number(argument("processes", 2));
const label = argument("label", "heap");
const port = 5188;
const bundles = { production: ["ranvier", "ranvier-sugar", "solid"] };
const envFor = () => ({ ...process.env, RANVIER_PERF_OUTPUT: "generated-keyed-heap", RANVIER_PERF_PROFILE: "true" });

if (!process.argv.includes("--no-bundle"))
  for (const build of Object.keys(bundles))
    execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: "inherit", windowsHide: true, env: envFor(build) });

async function snapshot(cdp) {
  const chunks = [];
  const listener = ({ chunk }) => chunks.push(chunk);
  cdp.on("HeapProfiler.addHeapSnapshotChunk", listener);
  await cdp.send("HeapProfiler.collectGarbage");
  await cdp.send("HeapProfiler.collectGarbage");
  await cdp.send("HeapProfiler.takeHeapSnapshot", { reportProgress: false, captureNumericValue: false });
  cdp.off("HeapProfiler.addHeapSnapshotChunk", listener);
  return parse(JSON.parse(chunks.join("")));
}

// Flattens a V8 heap snapshot into per-node arrays with an edge index.
function parse(raw) {
  const meta = raw.snapshot.meta, nf = meta.node_fields.length, ef = meta.edge_fields.length;
  const nodeTypes = meta.node_types[0], edgeTypes = meta.edge_types[0], strings = raw.strings;
  const [typeAt, nameAt, idAt, sizeAt, edgesAt] = ["type", "name", "id", "self_size", "edge_count"].map(f => meta.node_fields.indexOf(f));
  const [etypeAt, enameAt, etoAt] = ["type", "name_or_index", "to_node"].map(f => meta.edge_fields.indexOf(f));
  const count = raw.nodes.length / nf;
  const nodes = { count, type: new Array(count), name: new Array(count), id: new Float64Array(count), size: new Float64Array(count), firstEdge: new Uint32Array(count + 1) };
  let edge = 0;
  for (let i = 0; i < count; i++) {
    const at = i * nf;
    nodes.type[i] = nodeTypes[raw.nodes[at + typeAt]];
    nodes.name[i] = strings[raw.nodes[at + nameAt]];
    nodes.id[i] = raw.nodes[at + idAt];
    nodes.size[i] = raw.nodes[at + sizeAt];
    nodes.firstEdge[i] = edge;
    edge += raw.nodes[at + edgesAt];
  }
  nodes.firstEdge[count] = edge;
  const edges = { type: new Array(edge), name: new Array(edge), to: new Uint32Array(edge) };
  for (let e = 0; e < edge; e++) {
    const at = e * ef, type = edgeTypes[raw.edges[at + etypeAt]];
    edges.type[e] = type;
    edges.name[e] = type === "element" || type === "hidden" ? String(raw.edges[at + enameAt]) : strings[raw.edges[at + enameAt]];
    edges.to[e] = raw.edges[at + etoAt] / nf;
  }
  return { nodes, edges };
}

const kindOf = (nodes, i) => {
  const type = nodes.type[i], name = nodes.name[i];
  if (type === "object" && name.startsWith("system / Context")) return "object system / Context";
  if (type === "object" || type === "native") return `${type} ${name.split(" ")[0]}`;
  if (type === "closure") return "closure";
  if (type === "string" || type === "concatenated string" || type === "sliced string") return "string";
  if (type === "array") return `array ${name}`;
  if (type === "hidden" || type === "object shape" || type === "code") return `${type} ${name.split(" ")[0]}`;
  return type;
};
// Compiled code, feedback and shapes grow as code warms; they are reported apart from the rows.
const isCode = kind => kind.startsWith("code ") || kind.startsWith("object shape") || kind.startsWith("hidden ");

// Per-kind count and self size of the nodes of `after` whose ids `before` does not hold, and of the reverse.
function diff(before, after) {
  const old = new Set(before.nodes.id);
  const fresh = new Uint8Array(after.nodes.count);
  const added = new Map();
  for (let i = 0; i < after.nodes.count; i++) {
    if (old.has(after.nodes.id[i])) continue;
    fresh[i] = 1;
    const kind = kindOf(after.nodes, i), entry = added.get(kind) || { count: 0, bytes: 0 };
    entry.count++; entry.bytes += after.nodes.size[i];
    added.set(kind, entry);
  }
  const kept = new Set(after.nodes.id);
  const removed = new Map();
  for (let i = 0; i < before.nodes.count; i++) {
    if (kept.has(before.nodes.id[i])) continue;
    const kind = kindOf(before.nodes, i), entry = removed.get(kind) || { count: 0, bytes: 0 };
    entry.count++; entry.bytes += before.nodes.size[i];
    removed.set(kind, entry);
  }
  return { added, removed, fresh };
}

// New objects held by each field of a new row memo, attributed once each: the field's object, and the arrays, closures and
// contexts directly inside it. `value` holds the row's nodes, which belong to the row.
function memoParts({ nodes, edges }, fresh) {
  const parts = new Map(), claimed = new Uint8Array(nodes.count);
  const add = (label, i) => {
    const entry = parts.get(label) || { count: 0, bytes: 0 };
    entry.count++; entry.bytes += nodes.size[i];
    parts.set(label, entry);
  };
  const followed = e => edges.type[e] !== "weak" && edges.type[e] !== "shortcut" && edges.name[e] !== "map" && edges.name[e] !== "__proto__" && edges.name[e] !== "value";
  const inner = i => nodes.type[i] === "closure" || nodes.type[i] === "array" || (nodes.type[i] === "object" && (nodes.name[i] === "Array" || nodes.name[i].startsWith("system / Context")));
  for (let m = 0; m < nodes.count; m++) {
    if (!fresh[m] || nodes.type[m] !== "object" || nodes.name[m] !== "Memo$1") continue;
    claimed[m] = 1;
    add("Memo$1", m);
    for (let e = nodes.firstEdge[m]; e < nodes.firstEdge[m + 1]; e++) {
      const t = edges.to[e];
      if (!followed(e) || !fresh[t] || claimed[t] || nodes.type[t] === "number") continue;
      claimed[t] = 1;
      const label = `Memo$1.${edges.name[e]}: ${kindOf(nodes, t)}`;
      add(label, t);
      for (let f = nodes.firstEdge[t]; f < nodes.firstEdge[t + 1]; f++) {
        const u = edges.to[f];
        if (!followed(f) || !fresh[u] || claimed[u] || !inner(u)) continue;
        claimed[u] = 1;
        add(`${label} > ${edges.name[f]}: ${kindOf(nodes, u)}`, u);
      }
    }
  }
  return parts;
}


// Holder of each new object: the fresh object retaining it, as "holderKind.edge -> kind"; for Array element stores, the holder of their Array.
function holders({ nodes, edges }, fresh) {
  const by = new Int32Array(nodes.count).fill(-1), byEdge = new Array(nodes.count);
  for (let i = 0; i < nodes.count; i++) { if (!fresh[i]) continue;
    for (let e = nodes.firstEdge[i]; e < nodes.firstEdge[i + 1]; e++) { const to = edges.to[e];
      if (!fresh[to] || by[to] >= 0 || edges.type[e] === "weak" || edges.type[e] === "shortcut" || edges.name[e] === "__proto__" || edges.name[e] === "map") continue;
      by[to] = i; byEdge[to] = edges.name[e]; } }
  const label = i => { const k = kindOf(nodes, i); return k.startsWith("object ") ? k.slice(7) : k; };
  const out = new Map();
  for (let i = 0; i < nodes.count; i++) { if (!fresh[i]) continue; const k = kindOf(nodes, i);
    if (k.startsWith("native ") || k === "number" || isCode(k)) continue;
    let h = by[i], name = h < 0 ? "(none)" : label(h) + "." + byEdge[i];
    if (h >= 0 && (k === "array (object elements)") && by[h] >= 0) name = label(by[h]) + "." + byEdge[h] + " > " + byEdge[i];
    if (h >= 0 && k === "object system / Context" && by[h] >= 0) name = label(by[h]) + "." + byEdge[h] + " > " + byEdge[i];
    const key = name + " -> " + k, entry = out.get(key) || { count: 0, bytes: 0 };
    entry.count++; entry.bytes += nodes.size[i]; out.set(key, entry); }
  return out;
}
const table = (map, rows) => [...map].map(([kind, { count, bytes }]) => ({ kind, perRow: +(count / rows).toFixed(3), bytesPerRow: +(bytes / rows).toFixed(1), count, bytes }))
  .sort((a, b) => b.bytes - a.bytes);

const results = [];
let chromiumVersion, server, browser;
try {
  for (const [build, modes] of Object.entries(bundles)) {
    server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env: envFor(build), windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
    let serverOutput = "";
    server.stdout.on("data", data => serverOutput += data);
    server.stderr.on("data", data => serverOutput += data);
    let ready = false;
    for (let attempt = 0; attempt < 100 && !ready; attempt++) {
      if (server.exitCode !== null) throw Error(serverOutput);
      try { ready = (await fetch(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`)).ok; } catch {}
      if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
    }
    if (!ready) throw Error(serverOutput);

    for (let replicate = 0; replicate < processes; replicate++) {
      browser = await chromium.launch();
      chromiumVersion = browser.version();
      const page = await browser.newPage();
      const errors = [];
      page.on("pageerror", error => errors.push(String(error)));
      await page.goto(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`);
      await page.waitForFunction(() => window.keyedPerf);
      const cdp = await page.context().newCDPSession(page);
      for (const rows of counts) for (let offset = 0; offset < modes.length; offset++) {
        const mode = modes[(offset + replicate) % modes.length];
        // Two mounts and releases warm the mode's code and shared caches out of the diff.
        await page.evaluate(({ mode }) => { for (let i = 0; i < 2; i++) { const list = window.keyedPerf.mounted(mode); list.create(1000); list.release(); } }, { mode });
        await page.evaluate(({ mode }) => { window.heapList = window.keyedPerf.mounted(mode); }, { mode });
        const before = await snapshot(cdp);
        await page.evaluate(({ rows }) => window.heapList.create(rows), { rows });
        const after = await snapshot(cdp);
        await page.evaluate(() => { window.heapList.release(); delete window.heapList; });
        const { added, removed, fresh } = diff(before, after);
        const all = table(added, rows);
        const sum = list => +(list.reduce((total, row) => total + row.bytes, 0) / rows).toFixed(1);
        const js = all.filter(row => !row.kind.startsWith("native ") && row.kind !== "synthetic" && !isCode(row.kind));
        results.push({
          bundle: build, mode, rows, replicate,
          jsBytesPerRow: sum(js),
          codeBytesPerRow: sum(all.filter(row => isCode(row.kind))),
          nativeBytesPerRow: sum(all.filter(row => row.kind.startsWith("native "))),
          removedBytesPerRow: sum(table(removed, rows)),
          js,
          memo: table(memoParts(after, fresh), rows), holders: table(holders(after, fresh), rows).slice(0, 80),
          native: all.filter(row => row.kind.startsWith("native ")).slice(0, 30),
          removed: table(removed, rows).slice(0, 20)
        });
        console.log(`${build} ${mode} ${rows}: ${results.at(-1).jsBytesPerRow} JS bytes per row`);
      }
      if (errors.length) throw Error(errors.join("\n"));
      await browser.close(); browser = undefined;
    }
    const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; server = undefined;
  }
  const result = {
    measuredAt: new Date().toISOString(),
    commit: execFileSync("git", ["rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(),
    dirty: execFileSync("git", ["status", "--porcelain"], { cwd, encoding: "utf8" }).trim().split("\n").filter(Boolean),
    node: process.version, chromium: chromiumVersion, cpu: os.cpus()[0].model, cores: os.cpus().length, memoryGb: Math.round(os.totalmem() / 2 ** 30), platform: `${os.platform()} ${os.release()}`,
    production: true, minified: false,
    notes: [
      "Each entry diffs two heap snapshots of one page: an empty mounted list, then the same list after create(rows). Each snapshot follows two forced GCs; two mounts of 1,000 rows and their release run first.",
      "Objects are matched by snapshot id. `js` lists the V8 objects new in the second snapshot, by node type and constructor name, with self sizes; the kinds sum to jsBytesPerRow.",
      "codeBytesPerRow (compiled code, shapes, feedback) and nativeBytesPerRow (Blink nodes outside the V8 heap) are excluded from jsBytesPerRow.",
      "`memo` attributes the new objects held by each field of a new Memo$1, and the arrays, closures and contexts directly inside them, each object once. Shared objects (an empty array, a comparer, the graph) are not new and do not appear.",
      "One bundle: ranvier is the production keyed list (t.each); ranvier-sugar is the same list written with forEachFixed; solid is Solid 2 <For>."
    ],
    bundles, counts, processes, results
  };
  await mkdir(output, { recursive: true });
  await writeFile(`${output}/${label}.json`, JSON.stringify(result, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server && server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}
