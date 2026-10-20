// Allocation sample of the mount of the 128-row template benchmark row, explicit and sugared.
// Usage: node benchmarks/frameworks/sugar-alloc.mjs [--build] [--processes=3] [--mounts=10] [--width=128] [--label=alloc-template-row] [--out=<dir>]
// Writes <out>/<label>.json with every allocating function of each sample.
import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3) ?? fallback;
const output = argument("out", fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/view-sugar/", import.meta.url)));
const label = argument("label", "alloc-template-row");
const processes = Number(argument("processes", 3)), mounts = Number(argument("mounts", 10)), width = Number(argument("width", 128));
const modes = ["ranvier-template", "ranvier-template-sugar"];
const outDir = "generated-keyed-profile";
const env = { ...process.env, RanvierCounters: "false", RANVIER_PERF_OUTPUT: outDir, RANVIER_PERF_PROFILE: "true" };
const port = Number(argument("port", 5188));

if (process.argv.includes("--build")) {
  execFileSync("dotnet", ["fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"], { cwd, stdio: "inherit", windowsHide: true, env });
  execFileSync("dotnet", ["fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release", "--noCache"], { cwd, stdio: "inherit", windowsHide: true, env });
}
execFileSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: ["ignore", 2, 2], windowsHide: true, env });

const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let serverOutput = "", browser;
server.stdout.on("data", data => serverOutput += data);
server.stderr.on("data", data => serverOutput += data);

const fileOf = url => url ? url.replace(/^.*\/(generated-tests|node_modules|benchmarks|assets)\//, "$1/").replace(/\?.*$/, "") : "(native)";

// Sampled allocation bytes by allocating function, attributed to the leaf frame, for the samples under `run`.
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

try {
  let ready = false;
  for (let attempt = 0; attempt < 100 && !ready; attempt++) {
    if (server.exitCode !== null) throw Error(serverOutput);
    try { ready = (await fetch(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`)).ok; } catch {}
    if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(serverOutput);

  const allocations = [];
  let chromiumVersion;
  for (let replicate = 0; replicate < processes; replicate++) {
    browser = await chromium.launch();
    chromiumVersion = browser.version();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`);
    await page.waitForFunction(() => window.frameworkPerf);
    const cdp = await page.context().newCDPSession(page);
    // Odd processes sample the sugared mode first.
    for (const mode of replicate % 2 ? [...modes].reverse() : modes) {
      // One mount first, so one-time definition compilation is outside the sample.
      await page.evaluate(({ mode, width }) => { const warm = window.frameworkPerf.prepare(mode, width, "mount", 1); warm.run(); warm.cleanup(); }, { mode, width });
      await page.evaluate(({ mode, width, mounts }) => { window.allocationStep = window.frameworkPerf.prepare(mode, width, "mount", mounts); }, { mode, width, mounts });
      await cdp.send("HeapProfiler.collectGarbage");
      await cdp.send("HeapProfiler.startSampling", { samplingInterval: 64, includeObjectsCollectedByMajorGC: true, includeObjectsCollectedByMinorGC: true });
      await page.evaluate(() => window.allocationStep.run());
      const { profile: heap } = await cdp.send("HeapProfiler.stopSampling");
      await page.evaluate(() => { window.allocationStep.cleanup(); delete window.allocationStep; });
      allocations.push({ process: replicate, mode, width, mounts, ...summarizeHeap(heap) });
      console.log(`Allocation-sampled ${mode}, width ${width}, process ${replicate + 1}`);
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
  }
  await mkdir(output, { recursive: true });
  await writeFile(`${output}/${label}.json`, JSON.stringify({ measuredAt: new Date().toISOString(), chromium: chromiumVersion, width, mounts, processes, allocations }, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}
