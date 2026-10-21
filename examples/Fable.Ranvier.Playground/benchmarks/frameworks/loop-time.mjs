// Edit-to-result loop timings for the DOM twin spec (§7.2). Run behind the bench lock.
// --loop jsdom: edits tests/DelegationChecks.fs (appends, then removes, one exported value), then measures
//   compile:  `dotnet fickl tests/DomTests.fsproj --outDir generated-tests --configuration Release`; cold is the first
//             run, with `--noCache`; primed is the second, which rebuilds the cache; warm is the rest
//   watch:    the time from the edit to the rewrite of generated-tests/DelegationChecks.js under `dotnet fickl watch`;
//             cold is the first edit, warm the rest
//   node:     the delegation cases of `node --test tests/conformance.test.mjs`; cold is the first run, warm the rest
// Writes { loop, runs, steps }, each step { cold: [ms], primed?: [ms], warm: { samples, median, min, max } }. The edited
// file is restored on exit and on Ctrl+C.
import { execFileSync, spawn } from "node:child_process";
import { readFileSync, writeFileSync, statSync, existsSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

const cwd = new URL("../../", import.meta.url);
const option = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const loop = option("--loop", "jsdom"), runs = Number(option("--runs", "10")), out = option("--out");
const fixture = new URL("tests/DelegationChecks.fs", cwd);
const emitted = new URL("generated-tests/DelegationChecks.js", cwd);
const original = readFileSync(fixture, "utf8");
const restore = () => writeFileSync(fixture, original);
process.on("SIGINT", () => { restore(); process.exit(130); });
const edit = i => writeFileSync(fixture, original + `\nlet loopTimeEdit = ${i}\n`);
const time = action => { const start = performance.now(); action(); return performance.now() - start; };
const run = (file, args) => execFileSync(file, args, { cwd, stdio: ["ignore", "ignore", 2], windowsHide: true });
export const summary = samples => {
  const sorted = [...samples].sort((a, b) => a - b);
  return { samples, median: sorted[Math.floor((sorted.length - 1) / 2)], min: sorted[0], max: sorted[sorted.length - 1] };
};
const stop = child => {
  if (process.platform === "win32") {
    try { execFileSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore", windowsHide: true }); } catch { }
  } else child.kill();
};

if (loop !== "jsdom") throw new Error(`Unknown loop ${loop}; this script measures jsdom, Task 23 adds twin-test`);
const samples = { compile: [], watch: [], node: [] };
try {
  for (let i = 0; i < runs; i++) {
    edit(i);
    samples.compile.push(time(() => run("dotnet", ["fickl", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", ...(i === 0 ? ["--noCache"] : [])])));
    samples.node.push(time(() => run(process.execPath, ["--test", "--test-name-pattern=^DelegationChecks: ", "tests/conformance.test.mjs"])));
  }
  const watcher = spawn("dotnet", ["fickl", "watch", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release"], { cwd, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
  let errors = "";
  watcher.stderr.on("data", d => { errors = (errors + String(d)).slice(-4000); });
  const waitForRewrite = async (since, limitMs) => {
    const start = performance.now();
    while (performance.now() - start < limitMs) {
      if (existsSync(emitted) && statSync(emitted).mtimeMs > since) return performance.now() - start;
      await new Promise(r => setTimeout(r, 20));
    }
    throw new Error(`generated-tests/DelegationChecks.js was not rewritten within ${limitMs} ms; fickl watch stderr:\n${errors}`);
  };
  try {
    await new Promise((ok, fail) => { watcher.stdout.on("data", d => { if (/Watching/.test(String(d))) ok(); }); watcher.on("exit", c => fail(new Error(`fickl watch exited ${c}:\n${errors}`))); });
    for (let i = 0; i < runs; i++) {
      const since = statSync(emitted).mtimeMs;
      edit(1000 + i);
      samples.watch.push(await waitForRewrite(since, 120000));
    }
  } finally {
    stop(watcher);
  }
} finally {
  restore();
}
const steps = {
  compile: { cold: samples.compile.slice(0, 1), primed: samples.compile.slice(1, 2), warm: summary(samples.compile.slice(2)) },
  watch: { cold: samples.watch.slice(0, 1), warm: summary(samples.watch.slice(1)) },
  node: { cold: samples.node.slice(0, 1), warm: summary(samples.node.slice(1)) }
};
const result = { loop, runs, steps };
if (out) { mkdirSync(dirname(resolve(out)), { recursive: true }); writeFileSync(resolve(out), JSON.stringify(result, null, 2) + "\n"); }
console.log(JSON.stringify(result, null, 2));
