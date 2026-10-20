// First-content benchmark under Node: one server render of the component and keyed pages, Ranvier's Server.render
// against Solid 2's renderToString. Each process is a child `node --expose-gc` running this file with --worker=<replicate>.
// Usage: node benchmarks/frameworks/server-render.mjs [--build] [--label=<name>] [--out=<dir>]
import { execFileSync } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const self = fileURLToPath(import.meta.url);
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3) ?? fallback;
const output = argument("out") ?? fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/server-html/", import.meta.url));
const label = argument("label", "node-first-content");
const worker = argument("worker");
const solidDist = fileURLToPath(new URL("../../generated-server-dist/", import.meta.url));
const solidOut = `${solidDist}server-solid.mjs`;
const modes = ["ranvier", "solid"], processes = 3, rounds = 15, warmups = 3;
// Before a fixture's rounds, each mode renders for at least warmMs and at least warmRenders times. A sample repeats
// batches of `count` renders until it has run for at least sampleMs.
const warmMs = 500, warmRenders = 32, sampleMs = 25;
const fixtures = [
  { fixture: "component", rows: 1, count: 256 },
  { fixture: "component", rows: 16, count: 64 },
  { fixture: "component", rows: 128, count: 16 },
  { fixture: "keyed", rows: 1000, count: 4 },
  { fixture: "keyed", rows: 10000, count: 1 }
];
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

// A component row carries `class="panel"`; a keyed row is a `<tr>` element, with or without attributes.
const countRows = (fixture, html) => (html.match(fixture === "component" ? /class="panel"/g : /<tr[\s>]/g) ?? []).length;

async function runWorker(replicate) {
  if (typeof globalThis.gc !== "function") throw Error("The worker needs --expose-gc.");
  const ranvier = await import("../../generated-tests/experiments/Ranvier.View.Portable/Checks/ServerFixtures.js");
  const solid = await import(pathToFileURL(solidOut).href);
  const renderers = {
    ranvier: { component: ranvier.renderComponentPage, keyed: ranvier.renderKeyedPage },
    solid: { component: solid.renderComponentPage, keyed: solid.renderKeyedPage }
  };
  const verified = [], samples = [];
  for (const { fixture, rows } of fixtures) for (const mode of modes) {
    const html = renderers[mode][fixture](rows);
    const found = countRows(fixture, html);
    if (found !== rows) throw Error(`${mode} ${fixture} at ${rows} rows rendered ${found} rows`);
    verified.push({ fixture, rows, mode, count: found, bytes: Buffer.byteLength(html, "utf8") });
  }
  for (const { fixture, rows, count } of fixtures) {
    for (let offset = 0; offset < modes.length; offset++) {
      const render = renderers[modes[(offset + replicate) % modes.length]][fixture];
      const start = performance.now();
      for (let done = 0; done < warmRenders || performance.now() - start < warmMs; done++) render(rows);
    }
    for (let round = -warmups; round < rounds; round++) for (let offset = 0; offset < modes.length; offset++) {
      const mode = modes[(round + warmups + offset + replicate) % modes.length];
      const render = renderers[mode][fixture];
      globalThis.gc();
      let renders = 0, elapsed = 0;
      const start = performance.now();
      while (elapsed < sampleMs) {
        for (let index = 0; index < count; index++) render(rows);
        renders += count;
        elapsed = performance.now() - start;
      }
      const us = elapsed * 1000 / renders;
      if (!Number.isFinite(us) || us <= 0) throw Error(`Invalid timing ${fixture}/${mode}/${rows}: ${us}`);
      if (round >= 0) samples.push({ fixture, rows, mode, replicate, round, renders, us });
    }
  }
  process.stdout.write(JSON.stringify({ verified, samples }));
}

async function runMain() {
  if (process.argv.includes("--build"))
    execFileSync("dotnet", ["fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"], { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RanvierCounters: "false" } });
  const { transform } = await import("@solidjs/compiler");
  const source = fileURLToPath(new URL("./server-solid.jsx", import.meta.url));
  const { code } = transform(await readFile(source, "utf8"), { filename: source, generate: "ssr", hydratable: true });
  await mkdir(solidDist, { recursive: true });
  await writeFile(solidOut, code);

  const samples = [];
  let verified;
  for (let replicate = 0; replicate < processes; replicate++) {
    const result = JSON.parse(execFileSync(process.execPath, ["--expose-gc", self, `--worker=${replicate}`], { cwd, encoding: "utf8", maxBuffer: 1 << 28, windowsHide: true }));
    if (verified && JSON.stringify(verified) !== JSON.stringify(result.verified)) throw Error(`Process ${replicate + 1} rendered different output`);
    verified = result.verified;
    samples.push(...result.samples);
    console.log(`Measured process ${replicate + 1}`);
  }
  const summaries = fixtures.flatMap(({ fixture, rows }) => modes.map(mode => {
    const selected = samples.filter(row => row.fixture === fixture && row.rows === rows && row.mode === mode);
    const byProcessUs = Array.from({ length: processes }, (_, replicate) => median(selected.filter(row => row.replicate === replicate).map(row => row.us)));
    const { bytes } = verified.find(entry => entry.fixture === fixture && entry.rows === rows && entry.mode === mode);
    return { fixture, rows, mode, medianUs: median(selected.map(row => row.us)), byProcessUs, bytesPerRow: bytes / rows };
  }));
  const result = {
    measuredAt: new Date().toISOString(),
    baseCommit: execFileSync("git", ["rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(),
    dirty: execFileSync("git", ["status", "--porcelain"], { cwd, encoding: "utf8" }).trim().split("\n").filter(line => line && !line.includes("generated-")),
    node: process.version, nodeEnv: process.env.NODE_ENV ?? null, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`,
    unit: "microseconds per render",
    verified, samples, summaries
  };
  await mkdir(output, { recursive: true });
  await writeFile(`${output}/${label}.json`, JSON.stringify(result, null, 2) + "\n");
  console.table(summaries.map(({ fixture, rows, mode, medianUs, byProcessUs, bytesPerRow }) => ({ fixture, rows, mode, medianUs: medianUs.toFixed(2), byProcessUs: byProcessUs.map(value => value.toFixed(2)).join(" / "), bytesPerRow: bytesPerRow.toFixed(1) })));
}

if (worker === undefined) await runMain();
else await runWorker(Number(worker));
