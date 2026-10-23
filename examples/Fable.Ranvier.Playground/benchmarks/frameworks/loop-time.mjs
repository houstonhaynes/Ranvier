// Edit-to-result loop timings for the DOM twin spec (§7.2). Run behind the bench lock.
// --loop jsdom: edits tests/DelegationChecks.fs (appends, then removes, one exported value), then measures
//   compile:  `dotnet fickl tests/DomTests.fsproj --outDir generated-tests --configuration Release`; cold is the first
//             run, with `--noCache`; primed is the second, which rebuilds the cache; warm is the rest
//   watch:    the time from the edit to the rewrite of generated-tests/DelegationChecks.js under `dotnet fickl watch`;
//             cold is the first edit, warm the rest
//   node:     the delegation cases of `node --test tests/conformance.test.mjs`; cold is the first run, warm the rest
// --loop twin-test: edits tests/Ranvier.View.Twin.Tests/Fixtures.fs and runs the Session tests in Release.
//   test:     cold follows build-server shutdown; warm is the remaining edit/build/test runs
// Writes { loop, runs, steps }, each step { cold: [ms], primed?: [ms], warm: { samples, median, min, max } }. The edited
// file is restored on exit and on Ctrl+C.
import { execFileSync, spawn } from "node:child_process";
import { readFileSync, writeFileSync, statSync, existsSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

const cwd = new URL("../../", import.meta.url);
const option = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const loop = option("--loop", "jsdom"), runs = Number(option("--runs", "10")), out = option("--out");
if (!["jsdom", "twin-test"].includes(loop)) throw new Error(`Unknown loop ${loop}; expected jsdom or twin-test`);
if (!Number.isInteger(runs) || runs < 1) throw new Error("--runs must be a positive integer");
const fixture = new URL(loop === "twin-test" ? "../../tests/Ranvier.View.Twin.Tests/Fixtures.fs" : "tests/DelegationChecks.fs", cwd);
const emitted = new URL("generated-tests/DelegationChecks.js", cwd);
const original = readFileSync(fixture, "utf8");
const restore = () => writeFileSync(fixture, original);
let ownedWatcher;
process.on("SIGINT", () => { if (ownedWatcher) stop(ownedWatcher); restore(); process.exit(130); });
const edit = i => writeFileSync(fixture, original + `\nlet loopTimeEdit = ${i}\n`);
const time = action => { const start = performance.now(); action(); return performance.now() - start; };
const run = (file, args) => execFileSync(file, args, { cwd, stdio: ["ignore", "pipe", 2], encoding: "utf8", windowsHide: true });
export const summary = samples => {
  const sorted = [...samples].sort((a, b) => a - b);
  return { samples, median: sorted[Math.floor((sorted.length - 1) / 2)] ?? null, min: sorted[0] ?? null, max: sorted[sorted.length - 1] ?? null };
};
const stop = child => {
  if (process.platform === "win32") {
    try { execFileSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore", windowsHide: true }); } catch { }
  } else child.kill();
};

const samples = { compile: [], watch: [], node: [], test: [] };
try {
  if (loop === "twin-test") {
    run("dotnet", ["build-server", "shutdown"]);
    for (let i = 0; i < runs; i++) {
      edit(i);
      samples.test.push(time(() => {
        const output = run("dotnet", ["test", "../../tests/Ranvier.View.Twin.Tests", "-c", "Release", "--filter", "FullyQualifiedName~Session"]);
        if (!/Passed!.*Total:\s*[1-9]\d*/.test(output)) throw new Error(`Session filter did not report passing tests:\n${output}`);
      }));
    }
  } else {
  run("dotnet", ["build", "../../experiments/Ranvier.View.Arms.FablePlugin/Ranvier.View.Arms.FablePlugin.fsproj", "-c", "Release", "-v", "q"]);
  for (let i = 0; i < runs; i++) {
    edit(i);
    samples.compile.push(time(() => run("dotnet", ["fickl", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", ...(i === 0 ? ["--noCache"] : [])])));
    samples.node.push(time(() => {
      const output = run(process.execPath, ["--test", "--test-name-pattern=^DelegationChecks: ", "tests/conformance.test.mjs"]);
      if (!/(?:^|\n)(?:#|ℹ)\s*pass\s+[1-9]\d*/.test(output)) throw new Error(`Delegation filter did not report passing tests:\n${output}`);
    }));
  }
  const watcher = spawn("dotnet", ["fickl", "watch", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release"], { cwd, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
  ownedWatcher = watcher;
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
    await new Promise((ok, fail) => {
      let output = "";
      const timeout = setTimeout(() => fail(new Error(`fickl watch did not become ready:\n${output}\n${errors}`)), 120000);
      watcher.stdout.on("data", d => {
        output = (output + String(d)).slice(-4000);
        if (/Watching/.test(output)) { clearTimeout(timeout); ok(); }
      });
      watcher.on("error", error => { clearTimeout(timeout); fail(error); });
      watcher.on("exit", c => { clearTimeout(timeout); fail(new Error(`fickl watch exited ${c}:\n${errors}`)); });
    });
    for (let i = 0; i < runs; i++) {
      const since = statSync(emitted).mtimeMs;
      edit(1000 + i);
      samples.watch.push(await waitForRewrite(since, 120000));
    }
  } finally {
    stop(watcher);
    ownedWatcher = undefined;
  }
  }
} finally {
  restore();
}
const steps = loop === "twin-test" ? {
  test: { cold: samples.test.slice(0, 1), warm: summary(samples.test.slice(1)) }
} : {
  compile: { cold: samples.compile.slice(0, 1), primed: samples.compile.slice(1, 2), warm: summary(samples.compile.slice(2)) },
  watch: { cold: samples.watch.slice(0, 1), warm: summary(samples.watch.slice(1)) },
  node: { cold: samples.node.slice(0, 1), warm: summary(samples.node.slice(1)) }
};
const result = { loop, runs, steps };
if (out) { mkdirSync(dirname(resolve(out)), { recursive: true }); writeFileSync(resolve(out), JSON.stringify(result, null, 2) + "\n"); }
console.log(JSON.stringify(result, null, 2));
