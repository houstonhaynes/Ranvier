// Boundary reveal benchmark: rows under one boundary revealed after one async source settles, Ranvier against Solid 2's Loading.
// Scenario "reveal" mounts the boundary at the root and settles its source afterwards; scenario "switch" mounts the boundary
// from a branch switch over an already settled source, so the boundary mounts inside a flush; scenario "plain" is the switch
// workload without a boundary, and switch minus plain is each framework's cost of a boundary with nothing pending.
// Usage: node benchmarks/frameworks/boundary-reveal.mjs [--build] [--dist=<dir>] [--label=<name>] [--out=<dir>] [--port=<n>]
import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3) ?? fallback;
const output = argument("out") ?? fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/view-boundaries/", import.meta.url));
const label = argument("label", "boundary-reveal");
const outDir = argument("dist") ?? "generated-boundary-dist";
const env = { ...process.env, RanvierCounters: "false", RANVIER_PERF_OUTPUT: outDir };
const port = Number(argument("port", 5188));
const scenarios = ["reveal", "switch", "plain"], modes = ["ranvier", "solid"], widths = [1, 16, 128], processes = 3, rounds = 15, warmups = 3;
const counts = { 1: 64, 16: 32, 128: 8 };

if (process.argv.includes("--build")) {
  for (const args of [
    ["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"],
    ["dotnet", "fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release", "--noCache"],
    ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"]
  ]) execFileSync("rtk", args, { cwd, stdio: "inherit", windowsHide: true, env });
}

const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let serverOutput = "", browser;
server.stdout.on("data", data => serverOutput += data);
server.stderr.on("data", data => serverOutput += data);
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const url = `http://127.0.0.1:${port}/benchmarks/frameworks/index.html`;
const samples = [], heaps = [], verified = [];

try {
  let ready = false;
  for (let attempt = 0; attempt < 100 && !ready; attempt++) {
    if (server.exitCode !== null) throw Error(serverOutput);
    try { ready = (await fetch(url)).ok; } catch {}
    if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(serverOutput);
  for (let replicate = 0; replicate < processes; replicate++) {
    browser = await chromium.launch();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(url);
    await page.waitForFunction(() => window.boundaryReveal);
    for (const scenario of scenarios) for (const width of widths) for (const mode of modes)
      verified.push(await page.evaluate(({ mode, width, scenario }) => window.boundaryReveal.verify(mode, width, scenario), { mode, width, scenario }));
    for (const scenario of scenarios) for (const width of widths) {
      samples.push(...await page.evaluate(async ({ modes, width, count, rounds, warmups, replicate, scenario }) => {
        const rows = [];
        for (let round = -warmups; round < rounds; round++) for (let offset = 0; offset < modes.length; offset++) {
          const mode = modes[(round + warmups + offset + replicate) % modes.length];
          const ms = await window.boundaryReveal.sample(mode, width, count, scenario);
          if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid timing ${scenario}/${mode}/${width}: ${ms}`);
          if (round >= 0) rows.push({ scenario, mode, width, replicate, round, count, ms });
        }
        return rows;
      }, { modes, width, count: counts[width], rounds, warmups, replicate, scenario }));
      console.log(`Measured ${scenario} width ${width}, process ${replicate + 1}`);
    }
    const cdp = await page.context().newCDPSession(page);
    for (const scenario of scenarios) for (const width of widths) for (const mode of modes) {
      const count = 32;
      await cdp.send("HeapProfiler.collectGarbage");
      const before = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(async ({ mode, width, count, scenario }) => { window.releaseBoundaries = await window.boundaryReveal.retain(mode, width, count, scenario); }, { mode, width, count, scenario });
      await cdp.send("HeapProfiler.collectGarbage");
      const retained = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(() => { window.releaseBoundaries(); delete window.releaseBoundaries; });
      heaps.push({ scenario, mode, width, replicate, count, retainedBytesPerRow: (retained.usedSize - before.usedSize) / (count * width) });
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close();
    browser = undefined;
  }
  const summaries = scenarios.flatMap(scenario => widths.flatMap(width => modes.map(mode => {
    const rows = samples.filter(row => row.scenario === scenario && row.width === width && row.mode === mode);
    const byProcessUs = [0, 1, 2].map(replicate => median(rows.filter(row => row.replicate === replicate).map(row => row.ms)) * 1000);
    return { scenario, width, mode, medianUs: median(rows.map(row => row.ms)) * 1000, byProcessUs };
  })));
  const boundaryCost = widths.flatMap(width => modes.map(mode => {
    const find = scenario => summaries.find(s => s.scenario === scenario && s.width === width && s.mode === mode);
    const withBoundary = find("switch"), without = find("plain");
    const byProcessUs = withBoundary.byProcessUs.map((value, index) => value - without.byProcessUs[index]);
    const byProcessPercent = byProcessUs.map((value, index) => 100 * value / without.byProcessUs[index]);
    return { width, mode, costUs: withBoundary.medianUs - without.medianUs, costPercent: 100 * (withBoundary.medianUs - without.medianUs) / without.medianUs, ratio: withBoundary.medianUs / without.medianUs, byProcessUs, byProcessPercent };
  }));
  const result = {
    measuredAt: new Date().toISOString(),
    baseCommit: execFileSync("git", ["rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(),
    dirty: execFileSync("git", ["status", "--porcelain"], { cwd, encoding: "utf8" }).trim().split("\n").filter(line => line && !line.includes("generated-")),
    node: process.version, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`,
    unit: "milliseconds per boundary",
    notes: [
      "Each sample mounts `count` boundaries of `width` rows on fresh hosts; a boundary reveals after its one async source settles, and the adapter awaits one promise per mount.",
      "Scenario `reveal` mounts the boundary at the root and settles the source afterwards. Scenario `switch` mounts the boundary from a branch switch over an already settled source, so the boundary mounts inside a flush.",
      "Scenario `plain` mounts the same rows from the same switch without a boundary; `boundaryCost` is switch minus plain per width and mode, as microseconds and as a percentage of plain, with per-process values.",
      "In Solid 2 a settled promise is read through an async memo, so the boundary variant shows its fallback and reveals in a later microtask turn; Ranvier reveals within the flush.",
      "Solid 2 resolves a promise read by an async memo under <Loading>; Ranvier settles an async source synchronously.",
      "Entries of `dirty` that name _lines.tmp.mjs are an untracked scratch file unrelated to the code under test.",
      "retainedBytesPerRow is the GC-stabilised retained JavaScript heap of 32 revealed boundaries divided by their rows; it is reported, not gated."
    ],
    verified, samples, summaries, boundaryCost, heaps
  };
  await mkdir(output, { recursive: true });
  await writeFile(`${output}/${label}.json`, JSON.stringify(result, null, 2) + "\n");
  console.table(summaries.map(({ scenario, width, mode, medianUs, byProcessUs }) => ({ scenario, width, mode, medianUs: medianUs.toFixed(1), byProcessUs: byProcessUs.map(value => value.toFixed(1)).join(" / ") })));
  console.table(heaps);
} finally {
  if (browser) await browser.close();
  server.kill();
}
