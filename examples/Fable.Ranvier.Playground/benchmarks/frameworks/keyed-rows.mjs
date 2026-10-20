// Keyed-list benchmark: js-framework-benchmark operations for ranvier (t.each), ranvier-sugar (forEachFixed) and Solid 2 (<For>). The default modes are all three. `--profile` result files list every sampled allocating function.
// Usage: node benchmarks/frameworks/keyed-rows.mjs [--build|--bundle] [--profile [--repeats=10]] [--ops=a,b] [--modes=a,b] [--pairs=a:b,c:d] [--shuffle] [--pin] [--label=<name>] [--processes=3] [--rounds=15]
import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3) ?? fallback;
// `--out=<dir>` writes the result JSON there; `--save-cpu=<dir>` also writes each raw CPU profile there.
const output = argument("out") ?? fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/poc-rerun-2026-10-15/", import.meta.url));
const saveCpu = argument("save-cpu");
const profile = process.argv.includes("--profile");
const label = argument("label", profile ? "profile" : "timings");
const processes = Number(argument("processes", profile ? 1 : 3));
const rounds = Number(argument("rounds", 15)), warmups = 3;
// `--dist=<dir>` serves an already-built bundle, e.g. one built from another commit.
const outDir = argument("dist") ?? (profile ? "generated-keyed-profile" : "generated-keyed-dist");
const env = { ...process.env, RanvierCounters: "false", RANVIER_PERF_OUTPUT: outDir, ...(profile ? { RANVIER_PERF_PROFILE: "true" } : {}) };
const port = Number(argument("port", 5187));

if (process.argv.includes("--build")) {
  for (const [args, extra] of [
    [["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"], {}],
    [["dotnet", "fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release", "--noCache"], {}]
  ]) execFileSync("rtk", args, { cwd, stdio: "inherit", windowsHide: true, env: { ...env, ...extra } });
}
if (process.argv.includes("--build") || process.argv.includes("--bundle"))
  execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: "inherit", windowsHide: true, env });

const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let serverOutput = "", browser;
server.stdout.on("data", data => serverOutput += data);
server.stderr.on("data", data => serverOutput += data);

const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const shuffle = process.argv.includes("--shuffle");
// `--pin` keeps, for each mode and operation, one list that ran the operation once mounted while the process measures, so
// the object shapes of every adapter stay alive across the forced collections.
const pin = process.argv.includes("--pin");
// A seeded Fisher-Yates permutation, reproducible from `seed`.
function permutation(list, seed) {
  let h = 2166136261;
  for (const c of seed) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  const next = () => ((h = Math.imul(h ^ (h >>> 15), 2246822507) ^ Math.imul(h ^ (h >>> 13), 3266489909)) >>> 0) / 2 ** 32;
  const out = [...list];
  for (let i = out.length - 1; i > 0; i--) { const j = Math.floor(next() * (i + 1)); [out[i], out[j]] = [out[j], out[i]]; }
  return out;
}
const only = (list, flag) => argument(flag) ? list.filter(value => argument(flag).split(",").includes(value)) : list;
const sha = async path => createHash("sha256").update(await readFile(new URL(path, new URL("../../", import.meta.url)))).digest("hex");

async function open() {
  browser = await chromium.launch();
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`);
  await page.waitForFunction(() => window.keyedPerf);
  const cdp = await page.context().newCDPSession(page);
  return { page, cdp, errors, chromium: browser.version() };
}

try {
  let ready = false;
  for (let attempt = 0; attempt < 100 && !ready; attempt++) {
    if (server.exitCode !== null) throw Error(serverOutput);
    try { ready = (await fetch(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`)).ok; } catch {}
    if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(serverOutput);

  const samples = [], heaps = [], domCalls = [], profiles = [], allocations = [];
  let chromiumVersion, modes, names;
  for (let replicate = 0; replicate < processes; replicate++) {
    const { page, cdp, errors, chromium } = await open();
    chromiumVersion = chromium;
    ({ modes, names } = await page.evaluate(() => ({ modes: window.keyedPerf.modes, names: window.keyedPerf.operations })));
    modes = only(modes, "modes"); names = only(names, "ops");
    // MEASUREMENT (measure/keyed-rows-on-core): an operation runs only when every selected mode has an adapter for its fixture.
    const supported = await page.evaluate(({ modes, names }) => names.filter(name => modes.every(mode => window.keyedPerf.supports(mode, name))), { modes, names });
    if (replicate === 0 && supported.length < names.length) console.log(`Skipped ${names.filter(name => !supported.includes(name)).join(", ")}: no adapter for every mode`);
    names = supported;
    // Every adapter must render identical row text after every operation.
    for (const name of names) {
      const texts = await page.evaluate(({ modes, name }) => modes.map(mode => window.keyedPerf.verify(mode, name)), { modes, name });
      if (texts.some(text => text !== texts[0])) throw Error(`Adapters disagree after ${name}`);
    }
    console.log(`Verified ${names.length} operations, process ${replicate + 1}`);
    if (pin) await page.evaluate(({ modes, names }) => {
      window.pinnedSteps = modes.flatMap(mode => names.map(name => { const step = window.keyedPerf.prepared(mode, name); step.run(); return step; }));
    }, { modes, names });

    if (!profile) {
      for (const name of names) {
        let previous = null;
        for (let round = -warmups; round < rounds; round++) {
          // `--shuffle` draws each round's order from a seeded permutation, so no mode always follows the same mode.
          const order = shuffle ? permutation(modes, `${replicate}/${name}/${round}`) : modes.map((_, offset) => modes[(round + warmups + offset + replicate) % modes.length]);
          for (const mode of order) {
            await cdp.send("HeapProfiler.collectGarbage");
            const ms = await page.evaluate(({ mode, name }) => window.keyedPerf.sample(mode, name), { mode, name });
            if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid timing ${mode}/${name}: ${ms}`);
            if (round >= 0) samples.push({ mode, operation: name, replicate, round, previous, ms });
            previous = mode;
          }
        }
        console.log(`Measured ${name}, process ${replicate + 1}`);
      }
      for (const count of [1000, 10000]) for (let offset = 0; offset < modes.length; offset++) {
        const mode = modes[(offset + replicate) % modes.length];
        await cdp.send("HeapProfiler.collectGarbage");
        const before = await cdp.send("Runtime.getHeapUsage");
        await page.evaluate(({ mode, count }) => { window.releaseKeyed = window.keyedPerf.retain(mode, count); }, { mode, count });
        await cdp.send("HeapProfiler.collectGarbage");
        const retained = await cdp.send("Runtime.getHeapUsage");
        await page.evaluate(() => { window.releaseKeyed(); delete window.releaseKeyed; });
        await cdp.send("HeapProfiler.collectGarbage");
        const released = await cdp.send("Runtime.getHeapUsage");
        heaps.push({ mode, count, replicate, retainedBytesPerRow: (retained.usedSize - before.usedSize) / count, releasedDeltaBytes: released.usedSize - before.usedSize });
      }
      if (replicate === 0) for (const name of names) for (const mode of modes)
        domCalls.push({ mode, operation: name, calls: await page.evaluate(({ mode, name }) => window.keyedPerf.domCalls(mode, name), { mode, name }) });
    } else {
      // CPU profiles: the samples inside `measuredRun`, across repeated fresh lists. Allocation sampling: bytes by allocating function.
      const repeats = Number(argument("repeats", 40));
      await cdp.send("Profiler.enable");
      await cdp.send("Profiler.setSamplingInterval", { interval: 50 });
      for (const name of names) for (const mode of modes) {
        await cdp.send("HeapProfiler.collectGarbage");
        await cdp.send("Profiler.start");
        // `--protocol=sample` profiles the timing protocol: a forced GC, then one `sample` per evaluate.
        if (argument("protocol") === "sample") for (let i = 0; i < repeats; i++) {
          await cdp.send("HeapProfiler.collectGarbage");
          await page.evaluate(({ mode, name }) => window.keyedPerf.sample(mode, name), { mode, name });
        }
        else await page.evaluate(({ mode, name, repeats }) => {
          for (let i = 0; i < repeats; i++) { const step = window.keyedPerf.prepared(mode, name); step.run(); step.cleanup(); }
        }, { mode, name, repeats });
        const { profile: cpu } = await cdp.send("Profiler.stop");
        if (saveCpu) { await mkdir(saveCpu, { recursive: true }); await writeFile(`${saveCpu}/${label}-${mode}-${name}.cpuprofile`, JSON.stringify(cpu)); }
        profiles.push({ mode, operation: name, repeats, ...summarizeCpu(cpu) });
        console.log(`Profiled ${mode}/${name}`);
      }
      for (const name of process.argv.includes("--no-alloc") ? [] : only(["create1k", "clear1k", "swap1k", "remove1k", "remove10k"], "ops")) for (const mode of modes) {
        const runs = 10;
        await page.evaluate(({ mode, name, runs }) => { window.keyedSteps = Array.from({ length: runs }, () => window.keyedPerf.prepared(mode, name)); }, { mode, name, runs });
        await cdp.send("HeapProfiler.collectGarbage");
        await cdp.send("HeapProfiler.startSampling", { samplingInterval: 64, includeObjectsCollectedByMajorGC: true, includeObjectsCollectedByMinorGC: true });
        await page.evaluate(() => { for (const step of window.keyedSteps) step.run(); });
        const { profile: heap } = await cdp.send("HeapProfiler.stopSampling");
        await page.evaluate(() => { for (const step of window.keyedSteps) step.cleanup(); delete window.keyedSteps; });
        allocations.push({ mode, operation: name, runs, ...summarizeHeap(heap) });
        console.log(`Allocation-sampled ${mode}/${name}`);
      }
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
  }

  const summaries = profile ? [] : names.flatMap(name => modes.map(mode => {
    const rows = samples.filter(row => row.operation === name && row.mode === mode);
    return { operation: name, mode, medianMs: median(rows.map(row => row.ms)), byProcessMs: [...Array(processes).keys()].map(r => median(rows.filter(row => row.replicate === r).map(row => row.ms))) };
  }));
  // Ratios within a process and round: each Ranvier sample divided by the Solid sample of the same round.
  const ratios = profile || !modes.includes("solid") ? [] : names.flatMap(name => modes.filter(mode => mode !== "solid").map(mode => {
    const pairs = samples.filter(row => row.operation === name && row.mode === mode).map(row => {
      const solid = samples.find(other => other.operation === name && other.mode === "solid" && other.replicate === row.replicate && other.round === row.round);
      return { replicate: row.replicate, ratio: row.ms / solid.ms };
    });
    return { operation: name, mode, medianRatioToSolid: median(pairs.map(pair => pair.ratio)), byProcess: [...Array(processes).keys()].map(r => median(pairs.filter(pair => pair.replicate === r).map(pair => pair.ratio))) };
  }));
  // Ratios between two modes within a process and round, for `--pairs=numerator:denominator,...`.
  const pairRatios = profile || !argument("pairs") ? [] : names.flatMap(name => argument("pairs").split(",").map(pair => pair.split(":")).map(([top, bottom]) => {
    const pairs = samples.filter(row => row.operation === name && row.mode === top).map(row => {
      const other = samples.find(o => o.operation === name && o.mode === bottom && o.replicate === row.replicate && o.round === row.round);
      return { replicate: row.replicate, ratio: row.ms / other.ms };
    });
    const sorted = pairs.map(pair => pair.ratio).sort((a, b) => a - b);
    return { operation: name, numerator: top, denominator: bottom, medianRatio: median(sorted), q1: sorted[Math.floor(sorted.length / 4)], q3: sorted[Math.floor(3 * sorted.length / 4)], byProcess: [...Array(processes).keys()].map(r => median(pairs.filter(pair => pair.replicate === r).map(pair => pair.ratio))) };
  }));
  const files = ["benchmarks/frameworks/keyed-rows.mjs", "benchmarks/frameworks/keyed-page.mjs", "benchmarks/frameworks/keyed-solid.jsx", "benchmarks/frameworks/vite.config.mjs", "tests/KeyedRowsPerf.fs", "package-lock.json",
    ...["Lifetime.fs", "Representation.fs", "Template.fs"].map(path => `../../experiments/Ranvier.View.Portable/${path}`), ...["TemplateDom.fs", "Mounting.fs"].map(path => `../../experiments/Ranvier.View.Foundation/${path}`), "../../src/Ranvier/Core.fs", "../../src/Ranvier/Projections.fs", "../../src/Ranvier/Api.fs", "../../src/Ranvier/Combinators.fs"];
  const result = {
    measuredAt: new Date().toISOString(),
    commit: execFileSync("git", ["rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(),
    dirty: execFileSync("git", ["status", "--porcelain"], { cwd, encoding: "utf8" }).trim().split("\n").filter(Boolean),
    node: process.version, chromium: chromiumVersion, cpu: os.cpus()[0].model, cores: os.cpus().length, memoryGb: Math.round(os.totalmem() / 2 ** 30), platform: `${os.platform()} ${os.release()}`,
    production: true, minified: !profile, fable: "5.18.0", packages: { solid: "2.0.0-rc.14", solidWeb: "2.0.0-rc.14", solidCompiler: "2.0.0-rc.14" },
    processes, warmups, rounds, shuffle, pin, unit: "milliseconds per operation",
    notes: [
      "Each sample mounts a fresh list, runs the untimed setup, forces GC through CDP, then times one synchronous operation including its DOM commit; layout and paint are excluded.",
      "Modes alternate within each round and the starting mode rotates per round and process; ratios divide each Ranvier sample by the Solid sample of the same round.",
      "Rows follow js-framework-benchmark keyed: id text, label signal in an anchor, a remove anchor with a span, an empty cell; two click handlers per row. Solid's For keys by row identity, Ranvier's t.each by row id.",
      "Retained heap is GC-stabilized JavaScript heap per row of a mounted list, excluding DOM node memory outside the V8 heap."
    ],
    sourceHashes: Object.fromEntries(await Promise.all(files.map(async path => [path, await sha(path)]))),
    modes, operations: names, summaries, ratios, pairRatios, heaps, domCalls, samples, profiles, allocations
  };
  await mkdir(output, { recursive: true });
  await writeFile(`${output}/${label}.json`, JSON.stringify(result, null, 2) + "\n");
  console.log(JSON.stringify(profile ? profiles.map(p => ({ mode: p.mode, operation: p.operation, totalMs: p.totalMs, files: p.files.slice(0, 6) })) : { summaries: summaries.map(s => [s.operation, s.mode, s.medianMs.toFixed(3)]), ratios: ratios.map(r => [r.operation, r.mode, r.medianRatioToSolid.toFixed(2)]), pairRatios: pairRatios.map(r => [r.operation, `${r.numerator}/${r.denominator}`, r.medianRatio.toFixed(3), r.byProcess.map(v => v.toFixed(3)).join(" ")]), heaps }, null, 1));
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

function fileOf(url) {
  return url ? url.replace(/^.*\/(generated-tests|node_modules|benchmarks|assets)\//, "$1/").replace(/\?.*$/, "") : "(native)";
}

// Self time per function and per file for the samples whose stack passes through `measuredRun`, plus stackless (GC) samples between them.
function summarizeCpu(cpu) {
  const nodes = new Map(cpu.nodes.map(node => [node.id, node]));
  const parents = new Map();
  for (const node of cpu.nodes) for (const child of node.children || []) parents.set(child, node.id);
  const inside = cpu.samples.map(id => {
    let js = false;
    for (let at = id; at !== undefined; at = parents.get(at)) {
      const frame = nodes.get(at).callFrame;
      if (frame.functionName === "measuredRun") return true;
      if (frame.url) js = true;
    }
    const name = nodes.get(id).callFrame.functionName;
    return name === "(garbage collector)" ? null : js ? false : false;
  });
  for (let i = 0, previous = false; i < inside.length; i++) {
    if (inside[i] !== null) { previous = inside[i]; continue; }
    inside[i] = previous;
  }
  const self = new Map(), files = new Map(), inclusive = new Map();
  let total = 0;
  for (let i = 0; i < cpu.samples.length; i++) {
    if (!inside[i]) continue;
    const delta = cpu.timeDeltas[i] || 0;
    total += delta;
    const leaf = nodes.get(cpu.samples[i]).callFrame;
    const key = `${leaf.functionName || "(anonymous)"} ${fileOf(leaf.url)}:${leaf.lineNumber + 1}`;
    self.set(key, (self.get(key) || 0) + delta);
    files.set(fileOf(leaf.url), (files.get(fileOf(leaf.url)) || 0) + delta);
    const seen = new Set();
    for (let at = cpu.samples[i]; at !== undefined; at = parents.get(at)) {
      const frame = nodes.get(at).callFrame;
      const name = `${frame.functionName || "(anonymous)"} ${fileOf(frame.url)}:${frame.lineNumber + 1}`;
      if (!seen.has(name)) { seen.add(name); inclusive.set(name, (inclusive.get(name) || 0) + delta); }
    }
  }
  const top = (map, n) => [...map].sort((a, b) => b[1] - a[1]).slice(0, n).map(([name, us]) => ({ name, ms: +(us / 1000).toFixed(3) }));
  return { totalMs: +(total / 1000).toFixed(3), files: top(files, 20), self: top(self, 60), inclusive: top(inclusive, 80) };
}

// Sampled allocation bytes by allocating function, attributed to the leaf frame.
function summarizeHeap(heap) {
  const self = new Map();
  let total = 0;
  const visit = (node, inside) => {
    const frame = node.callFrame;
    const here = inside || frame.functionName === "run";
    if (here && node.selfSize) {
      const key = `${frame.functionName || "(anonymous)"} ${fileOf(frame.url)}:${frame.lineNumber + 1}`;
      self.set(key, (self.get(key) || 0) + node.selfSize);
      total += node.selfSize;
    }
    for (const child of node.children || []) visit(child, here);
  };
  visit(heap.head, false);
  return { totalBytes: total, self: [...self].sort((a, b) => b[1] - a[1]).map(([name, bytes]) => ({ name, bytes })) };
}
