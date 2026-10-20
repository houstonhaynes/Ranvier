// Hydration benchmark in Chromium: Ranvier's hydrate of server HTML against Ranvier's client mount of the same page and
// against Solid 2's hydrate of its own server HTML, for the component page (1/16/128 rows) and the keyed page
// (1000/10000 rows). Also the time to the first handled click, the retained heap per row, and lenient against Strict
// hydration of the component page at 16 rows and the keyed page at 1000 rows.
// Usage: node benchmarks/frameworks/hydration-bench.mjs [--build] [--verify-only] [--dist=<dir>] [--label=<name>] [--out=<dir>] [--port=<n>] [--rotation=balanced|cyclic] [--rounds=<n>]
import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3) ?? fallback;
const output = argument("out") ?? fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/server-html/", import.meta.url));
const label = argument("label", "hydration");
const outDir = argument("dist") ?? "generated-hydration-dist";
const verifyOnly = process.argv.includes("--verify-only");
const env = { ...process.env, RanvierCounters: "false", RANVIER_PERF_OUTPUT: outDir };
const port = Number(argument("port", 5189));
const modes = ["ranvier-hydrate", "ranvier-mount", "solid-hydrate"], processes = 3, warmups = 3, retained = 32;
const rotation = argument("rotation", "balanced");
if (rotation !== "balanced" && rotation !== "cyclic") throw Error(`Unknown rotation ${rotation}`);
// Measured samples per mode and process: 60 under `balanced` gives each (mode, predecessor) cell 20 samples per process
// and 60 in all; `cyclic` keeps Task 20's 15.
const rounds = Number(argument("rounds", rotation === "balanced" ? 60 : 15));
if (rotation === "balanced" && rounds % 3 !== 0) throw Error("Balanced rounds must be a multiple of 3");
// The sample order of one process: `rounds` measured samples per mode after `warmups` per mode, each entry carrying its
// mode, the mode sampled just before it, and its round (the mode's measured occurrence; negative while warming up).
// `balanced` cycles through a de Bruijn sequence of the three modes, so in every 9 samples each mode follows each mode,
// itself included, once; each process relabels the modes by its index. `cyclic` is Task 20's order: every round runs the
// modes in their cyclic order, starting one mode later per round and per process.
const schedule = replicate => {
  const order = [];
  if (rotation === "balanced") {
    const sequence = [0, 0, 1, 0, 2, 1, 1, 2, 2];
    for (let index = 0; index < (warmups + rounds) * modes.length; index++)
      order.push(modes[(sequence[index % sequence.length] + replicate) % modes.length]);
  } else {
    for (let round = -warmups; round < rounds; round++) for (let offset = 0; offset < modes.length; offset++)
      order.push(modes[(round + warmups + offset + replicate) % modes.length]);
  }
  const seen = Object.fromEntries(modes.map(mode => [mode, -warmups]));
  return order.map((mode, index) => ({ mode, previous: index === 0 ? null : order[index - 1], round: seen[mode]++ }));
};
// `count` calls per sample, each on its own host.
const fixtures = [
  { fixture: "component", rows: 1, count: 64 },
  { fixture: "component", rows: 16, count: 32 },
  { fixture: "component", rows: 128, count: 8 },
  { fixture: "keyed", rows: 1000, count: 2 },
  { fixture: "keyed", rows: 10000, count: 1 }
];
// The lenient block, reported and not gated: Hydration.HydrationOptions.defaults (Strict = false) against Strict
// hydration, in pairs whose order alternates by round.
const lenientFixtures = [{ fixture: "component", rows: 16, count: 32 }, { fixture: "keyed", rows: 1000, count: 2 }];
const lenientModes = ["ranvier-hydrate", "ranvier-hydrate-lenient"], lenientRounds = 30;

if (process.argv.includes("--build")) {
  for (const args of [
    ["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"],
    ["dotnet", "fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release", "--noCache"],
    ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"]
  ]) execFileSync(args[0] === "node" ? process.execPath : args[0], args.slice(1), { cwd, stdio: "inherit", windowsHide: true, env });
}

// Solid 2's server HTML, rendered in Node from server-solid.jsx compiled for the server with hydration keys, and the
// hydration bootstrap markup of @solidjs/web's server build.
{
  const { transform } = await import("@solidjs/compiler");
  const { generateHydrationScript } = await import("@solidjs/web");
  const source = fileURLToPath(new URL("./server-solid.jsx", import.meta.url));
  const compiled = `${cwd}${outDir}/server-solid.mjs`;
  const { code } = transform(await readFile(source, "utf8"), { filename: source, generate: "ssr", hydratable: true });
  await mkdir(`${cwd}${outDir}`, { recursive: true });
  await writeFile(compiled, code);
  const solid = await import(pathToFileURL(compiled).href);
  const html = { component: {}, keyed: {} };
  for (const { fixture, rows } of fixtures) {
    const rendered = (fixture === "component" ? solid.renderComponentPage : solid.renderKeyedPage)(rows);
    const found = (rendered.match(fixture === "component" ? /class="panel"/g : /<tr[\s>]/g) ?? []).length;
    if (found !== rows) throw Error(`Solid ${fixture} at ${rows} rows rendered ${found} rows`);
    html[fixture][rows] = rendered;
  }
  await writeFile(`${cwd}${outDir}/solid-html.json`, JSON.stringify({ bootstrap: generateHydrationScript(), html }));
}

const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let serverOutput = "", browser;
server.stdout.on("data", data => serverOutput += data);
server.stderr.on("data", data => serverOutput += data);
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const url = `http://127.0.0.1:${port}/benchmarks/frameworks/index.html`;
const samples = [], clicks = [], heaps = [], verified = [], lenientSamples = [];
let chromiumVersion;

try {
  let ready = false;
  for (let attempt = 0; attempt < 100 && !ready; attempt++) {
    if (server.exitCode !== null) throw Error(serverOutput);
    try { ready = (await fetch(url)).ok; } catch {}
    if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(serverOutput);
  for (let replicate = 0; replicate < (verifyOnly ? 1 : processes); replicate++) {
    browser = await chromium.launch();
    chromiumVersion = browser.version();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(url);
    await page.waitForFunction(() => window.hydrationBench);
    const cdp = await page.context().newCDPSession(page);
    const collect = () => cdp.send("HeapProfiler.collectGarbage");
    for (const { fixture, rows } of fixtures) for (const mode of modes) {
      const entry = await page.evaluate(({ mode, fixture, rows }) => window.hydrationBench.verify(mode, fixture, rows), { mode, fixture, rows });
      verified.push({ ...entry, replicate, passed: true });
    }
    for (const { fixture, rows } of lenientFixtures) {
      const entry = await page.evaluate(({ fixture, rows }) => window.hydrationBench.verify("ranvier-hydrate-lenient", fixture, rows), { fixture, rows });
      verified.push({ ...entry, replicate, passed: true });
    }
    if (verifyOnly) {
      for (const { fixture, rows } of fixtures) for (const mode of modes)
        console.log(mode, fixture, rows, "first click ms", await page.evaluate(({ mode, fixture, rows }) => window.hydrationBench.firstClick(mode, fixture, rows), { mode, fixture, rows }));
      if (errors.length) throw Error(errors.join("\n"));
      break;
    }
    for (const { fixture, rows, count } of fixtures) {
      for (const { mode, previous, round } of schedule(replicate)) {
        await page.evaluate(({ mode, fixture, rows, count }) => window.hydrationBench.prepare(mode, fixture, rows, count), { mode, fixture, rows, count });
        await collect();
        const ms = await page.evaluate(({ mode, fixture, rows, count }) => window.hydrationBench.sample(mode, fixture, rows, count), { mode, fixture, rows, count });
        if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid timing ${fixture}/${mode}/${rows}: ${ms}`);
        if (round >= 0) samples.push({ fixture, rows, mode, previous, replicate, round, count, ms });
      }
      for (const { mode, previous, round } of schedule(replicate)) {
        await collect();
        const ms = await page.evaluate(({ mode, fixture, rows }) => window.hydrationBench.firstClick(mode, fixture, rows), { mode, fixture, rows });
        if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid first click ${fixture}/${mode}/${rows}: ${ms}`);
        if (round >= 0) clicks.push({ fixture, rows, mode, previous, replicate, round, ms });
      }
      console.log(`Measured ${fixture} at ${rows} rows, process ${replicate + 1}`);
    }
    for (const { fixture, rows } of fixtures) for (const mode of modes) {
      await collect();
      const before = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(async ({ mode, fixture, rows, count }) => { window.releaseHydration = await window.hydrationBench.retain(mode, fixture, rows, count); }, { mode, fixture, rows, count: retained });
      await collect();
      const after = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(() => { window.releaseHydration(); delete window.releaseHydration; });
      heaps.push({ fixture, rows, mode, replicate, count: retained, retainedBytesPerRow: (after.usedSize - before.usedSize) / (retained * rows) });
    }
    for (const { fixture, rows, count } of lenientFixtures) {
      for (let round = -warmups; round < lenientRounds; round++) {
        for (const mode of (round + warmups) % 2 === 0 ? lenientModes : [...lenientModes].reverse()) {
          await page.evaluate(({ mode, fixture, rows, count }) => window.hydrationBench.prepare(mode, fixture, rows, count), { mode, fixture, rows, count });
          await collect();
          const ms = await page.evaluate(({ mode, fixture, rows, count }) => window.hydrationBench.sample(mode, fixture, rows, count), { mode, fixture, rows, count });
          if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid timing ${fixture}/${mode}/${rows}: ${ms}`);
          if (round >= 0) lenientSamples.push({ fixture, rows, mode, replicate, round, count, ms });
        }
      }
      console.log(`Measured lenient ${fixture} at ${rows} rows, process ${replicate + 1}`);
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close();
    browser = undefined;
  }
  if (!verifyOnly) {
    const byProcess = (rows, value) => Array.from({ length: processes }, (_, replicate) => median(rows.filter(row => row.replicate === replicate).map(value)));
    const select = (list, fixture, rows, mode) => list.filter(row => row.fixture === fixture && row.rows === rows && row.mode === mode);
    const summaries = fixtures.flatMap(({ fixture, rows }) => modes.map(mode => {
      const selected = select(samples, fixture, rows, mode);
      return { fixture, rows, mode, medianUs: median(selected.map(row => row.ms)) * 1000, byProcessUs: byProcess(selected, row => row.ms * 1000) };
    }));
    const byPrevious = fixtures.flatMap(({ fixture, rows }) => modes.flatMap(mode => modes.map(previous => {
      const selected = select(samples, fixture, rows, mode).filter(row => row.previous === previous);
      return { fixture, rows, mode, previous, samples: selected.length, medianUs: selected.length ? median(selected.map(row => row.ms)) * 1000 : null };
    })));
    const firstClick = fixtures.flatMap(({ fixture, rows }) => modes.map(mode => {
      const selected = select(clicks, fixture, rows, mode);
      return { fixture, rows, mode, medianUs: median(selected.map(row => row.ms)) * 1000, byProcessUs: byProcess(selected, row => row.ms * 1000) };
    }));
    const heapSummaries = fixtures.flatMap(({ fixture, rows }) => modes.map(mode => {
      const selected = select(heaps, fixture, rows, mode);
      return { fixture, rows, mode, medianBytesPerRow: median(selected.map(row => row.retainedBytesPerRow)), byProcessBytesPerRow: selected.map(row => row.retainedBytesPerRow) };
    }));
    // Within each process and round, hydrate over mount; the summary is the median of those per-round ratios.
    const hydrateVersusMount = fixtures.map(({ fixture, rows }) => {
      const pick = mode => select(samples, fixture, rows, mode);
      const hydrate = pick("ranvier-hydrate"), mount = pick("ranvier-mount");
      const ratios = hydrate.map(row => ({ replicate: row.replicate, ratio: row.ms / mount.find(other => other.replicate === row.replicate && other.round === row.round).ms }));
      const find = mode => summaries.find(s => s.fixture === fixture && s.rows === rows && s.mode === mode);
      const previousOf = (mode, previous) => byPrevious.find(s => s.fixture === fixture && s.rows === rows && s.mode === mode && s.previous === previous);
      // Per predecessor, hydrate's median over mount's; null when either mode has no sample after it.
      const matched = Object.fromEntries(modes.map(previous => {
        const hydrateAfter = previousOf("ranvier-hydrate", previous), mountAfter = previousOf("ranvier-mount", previous);
        return [previous, hydrateAfter.medianUs === null || mountAfter.medianUs === null ? null : hydrateAfter.medianUs / mountAfter.medianUs];
      }));
      // Null, and a failed gate, when no predecessor has a ratio.
      const present = Object.values(matched).filter(value => value !== null);
      const maxMatchedRatio = present.length ? Math.max(...present) : null;
      return {
        fixture, rows,
        ratio: find("ranvier-hydrate").medianUs / find("ranvier-mount").medianUs,
        medianRoundRatio: median(ratios.map(row => row.ratio)),
        byProcessRatio: byProcess(ratios, row => row.ratio),
        matched, maxMatchedRatio,
        atOrBelowMount: maxMatchedRatio !== null && maxMatchedRatio <= 1
      };
    });
    // Per fixture, lenient hydration's median over Strict hydration's, pooled and per process.
    const lenientVersusStrict = lenientFixtures.map(({ fixture, rows }) => {
      const pick = mode => select(lenientSamples, fixture, rows, mode);
      const strict = pick("ranvier-hydrate"), lenient = pick("ranvier-hydrate-lenient");
      const strictByProcess = byProcess(strict, row => row.ms), lenientByProcess = byProcess(lenient, row => row.ms);
      return {
        fixture, rows,
        strictUs: median(strict.map(row => row.ms)) * 1000,
        lenientUs: median(lenient.map(row => row.ms)) * 1000,
        ratio: median(lenient.map(row => row.ms)) / median(strict.map(row => row.ms)),
        byProcessRatio: lenientByProcess.map((value, index) => value / strictByProcess[index])
      };
    });
    const result = {
      measuredAt: new Date().toISOString(),
      baseCommit: execFileSync("git", ["rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(),
      dirty: execFileSync("git", ["status", "--porcelain"], { cwd, encoding: "utf8" }).trim().split("\n").filter(line => line && !line.includes("generated-")),
      node: process.version, chromium: chromiumVersion, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`,
      unit: "milliseconds per call (samples), microseconds (summaries)",
      notes: [
        "Each sample hydrates (or mounts) `count` hosts attached to the body. The hosts are filled with the fixture's server HTML, parsed as a document parse places it, before a forced garbage collection and outside the timed region; `ranvier-mount` hosts are empty.",
        "Ranvier hydrates its own Server.render output (Omit policy) with { IdPrefix = \"r\"; Strict = true }; Solid 2 hydrates its own renderToString output (hydratable). Each Ranvier call creates and its disposal disposes a new Graph.",
        rotation === "balanced"
          ? "Rotation `balanced`: each process runs a de Bruijn sequence of the three modes, so each mode follows each mode, itself included, equally often; `round` is the mode's occurrence, and `medianRoundRatio` pairs a process's k-th hydrate with its k-th mount sample."
          : "Rotation `cyclic` (Task 20's order): modes rotate per round and process in their cyclic order, so mount follows hydrate in two rounds of three and no mode follows itself; `matched` has no ratio after a Ranvier mode.",
        "The spec's gate is `atOrBelowMount`: `maxMatchedRatio`, the largest of the `matched` ratios (hydrate's median over mount's for the same predecessor), is at most 1. `ratio` is the pooled medians' ratio, reported and not gated.",
        "`byPrevious` gives each mode's median by the mode sampled just before it; a sample's time depends on its predecessor.",
        "firstClick is the time from the call to the end of the first click on the first row's button (component) or link (keyed), including the commit of the click's update (Solid: flush()).",
        "retainedBytesPerRow is the GC-stabilised retained JavaScript heap of 32 live hydrated (or mounted) hosts divided by their rows, the hosts' server DOM included; it is reported, not gated.",
        `lenientVersusStrict is reported, not gated: after the gated samples, each process samples Strict and lenient (Hydration.HydrationOptions.defaults) hydration of the same server HTML in ${lenientRounds} measured pairs per fixture after ${warmups} warm-up pairs, the pair's order alternating by round; \`ratio\` is lenient's median over Strict's.`
      ],
      rotation, rounds, verified, samples, clicks, summaries, byPrevious, firstClick, heaps, heapSummaries, hydrateVersusMount,
      lenientRounds, lenientSamples, lenientVersusStrict
    };
    await mkdir(output, { recursive: true });
    await writeFile(`${output}/${label}.json`, JSON.stringify(result, null, 2) + "\n");
    console.table(summaries.map(({ fixture, rows, mode, medianUs, byProcessUs }) => ({ fixture, rows, mode, medianUs: medianUs.toFixed(1), byProcessUs: byProcessUs.map(value => value.toFixed(1)).join(" / ") })));
    console.table(firstClick.map(({ fixture, rows, mode, medianUs, byProcessUs }) => ({ fixture, rows, mode, medianUs: medianUs.toFixed(1), byProcessUs: byProcessUs.map(value => value.toFixed(1)).join(" / ") })));
    console.table(heapSummaries.map(({ fixture, rows, mode, medianBytesPerRow, byProcessBytesPerRow }) => ({ fixture, rows, mode, medianBytesPerRow: medianBytesPerRow.toFixed(0), byProcess: byProcessBytesPerRow.map(value => value.toFixed(0)).join(" / ") })));
    console.table(byPrevious.filter(row => row.samples > 0).map(({ fixture, rows, mode, previous, samples, medianUs }) => ({ fixture, rows, mode, previous, samples, medianUs: medianUs.toFixed(1) })));
    console.table(hydrateVersusMount.map(({ fixture, rows, ratio, medianRoundRatio, byProcessRatio, matched, maxMatchedRatio, atOrBelowMount }) => ({
      fixture, rows,
      ...Object.fromEntries(modes.map(previous => [`after ${previous}`, matched[previous] === null ? "–" : matched[previous].toFixed(3)])),
      maxMatched: maxMatchedRatio === null ? "–" : maxMatchedRatio.toFixed(3), atOrBelowMount, pooledRatio: ratio.toFixed(3), medianRoundRatio: medianRoundRatio.toFixed(3), byProcess: byProcessRatio.map(value => value.toFixed(3)).join(" / ")
    })));
    console.table(lenientVersusStrict.map(({ fixture, rows, strictUs, lenientUs, ratio, byProcessRatio }) => ({
      fixture, rows, strictUs: strictUs.toFixed(1), lenientUs: lenientUs.toFixed(1), lenientOverStrict: ratio.toFixed(3), byProcess: byProcessRatio.map(value => value.toFixed(3)).join(" / ")
    })));
  } else console.table(verified);
} finally {
  if (browser) await browser.close();
  server.kill();
}
