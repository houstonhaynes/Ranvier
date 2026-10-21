// Keyed-list operations on the reactive core alone, with no DOM: Ranvier's production projection (Fixture.fs, compiled by
// Fable) against Solid 2's mapArray from @solidjs/signals. Both run in the same Node process; each round times every
// operation once per framework in a seeded shuffled order, and a ratio divides the two times of one round.
// --base=<dir holding Fixture.js> adds a second Ranvier build as framework `base`; --base-commit=<sha> asserts that build's commit.
// Usage: node bench/Ranvier.CoreListBench/run.mjs [--build] [--base=<dir>] [--base-commit=<sha>] [--processes=7] [--warmup=10] [--rounds=31] [--ops=a,b] [--label=core-list-solid]
import { execFileSync } from "node:child_process";
import path from "node:path";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import os from "node:os";

const here = fileURLToPath(new URL("./", import.meta.url));
const root = fileURLToPath(new URL("../../", import.meta.url));
const solidRoot = `${here}node_modules/@solidjs/signals/`;
const output = `${root}docs/research/core-trimmed-pass/`;
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.split("=")[1] ?? fallback;
const run = (command, args) => execFileSync(command, args, { cwd: here, encoding: "utf8", windowsHide: true, maxBuffer: 1 << 26 });

const operations = {
  create1k: [() => {}, f => f.create(1000)],
  create10k: [() => {}, f => f.create(10000)],
  replace1k: [f => f.create(1000), f => f.replace(1000)],
  append1k: [f => f.create(1000), f => f.append(1000)],
  update10th1k: [f => f.create(1000), f => f.update10th()],
  swap1k: [f => f.create(1000), f => f.swap()],
  swap10k: [f => f.create(10000), f => f.swap()],
  remove1k: [f => f.create(1000), f => f.remove(3)],
  remove10k: [f => f.create(10000), f => f.remove(3)],
  clear1k: [f => f.create(1000), f => f.clear()],
  clear10k: [f => f.create(10000), f => f.clear()],
};

const ranvierFixture = async (dir = `${here}out/`) => {
  // Fable emits the members as module functions taking the instance first.
  const R = await import(pathToFileURL(`${dir}Fixture.js`).href);
  return () => {
    const f = R.Fixture_$ctor();
    return {
      create: n => R.Fixture__Create_Z524259A4(f, n), replace: n => R.Fixture__Create_Z524259A4(f, n),
      append: n => R.Fixture__Append_Z524259A4(f, n), update10th: () => R.Fixture__UpdateEvery10th(f),
      swap: () => R.Fixture__Swap(f), remove: i => R.Fixture__Remove_Z524259A4(f, i), clear: () => R.Fixture__Clear(f),
      snapshot: () => R.Fixture__Snapshot(f), dispose: () => R.Fixture__Dispose(f),
    };
  };
};

const solidFixture = async () => {
  const S = await import(pathToFileURL(`${solidRoot}dist/prod/index.js`).href);
  let sink = 0;
  return () => {
    let next = 1, dispose, items, setItems, mapped;
    const build = count => Array.from({ length: count }, () => {
      const id = next++;
      return { id, label: S.createSignal(`row ${id}`) };
    });
    S.createRoot(d => {
      dispose = d;
      [items, setItems] = S.createSignal([]);
      mapped = S.mapArray(items, item => {
        const row = { id: item.id, text: "" };
        S.createRenderEffect(() => item.label[0](), text => { row.text = text; });
        return row;
      });
      S.createRenderEffect(mapped, rows => { for (const row of rows) sink += row.id; });
    });
    S.flush();
    const write = value => { setItems(value); S.flush(); };
    return {
      create: n => write(build(n)), replace: n => write(build(n)), append: n => write([...items(), ...build(n)]),
      update10th: () => {
        const current = items();
        for (let i = 0; i < current.length; i += 10) current[i].label[1](current[i].label[0]() + " !!!");
        S.flush();
      },
      swap: () => {
        const swapped = items().slice(), last = swapped.length - 2, first = swapped[1];
        swapped[1] = swapped[last];
        swapped[last] = first;
        write(swapped);
      },
      remove: i => write(items().toSpliced(i, 1)), clear: () => write([]),
      snapshot: () => S.untrack(mapped).map(row => `${row.id}:${row.text}`).join(","), dispose: () => dispose(),
    };
  };
};

const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const quantile = (values, q) => [...values].sort((a, b) => a - b)[Math.floor(q * (values.length - 1))];

if (process.argv.includes("--child")) {
  const seed = Number(argument("seed", 1)), warmup = Number(argument("warmup", 10)), rounds = Number(argument("rounds", 31));
  const wanted = argument("ops", "").split(",").filter(Boolean);
  const baseDir = argument("base", "");
  const frameworks = { ranvier: await ranvierFixture(), solid: await solidFixture() };
  if (baseDir) frameworks.base = await ranvierFixture(baseDir.endsWith("/") || baseDir.endsWith("\\") ? baseDir : `${baseDir}/`);
  const frameworkNames = Object.keys(frameworks);
  let state = seed >>> 0 || 1;
  const random = () => (state = (Math.imul(state, 1664525) + 1013904223) >>> 0) / 2 ** 32;
  const sample = (make, [setup, timed]) => {
    const fixture = make();
    setup(fixture);
    globalThis.gc();
    const start = performance.now();
    timed(fixture);
    const ms = performance.now() - start;
    fixture.dispose();
    return ms;
  };
  // Both frameworks must leave the same rows, in the same order, with the same text.
  for (const [name, [setup, timed]] of Object.entries(operations)) {
    const snapshots = Object.values(frameworks).map(make => {
      const fixture = make();
      setup(fixture);
      timed(fixture);
      const snapshot = fixture.snapshot();
      fixture.dispose();
      return snapshot.replace(/\d+:/g, ":");
    });
    if (snapshots.some(snapshot => snapshot !== snapshots[0])) throw Error(`${name}: the frameworks' rows differ`);
  }
  const results = {};
  for (const [name, op] of Object.entries(operations).filter(([name]) => wanted.length === 0 || wanted.includes(name))) {
    const ms = Object.fromEntries(frameworkNames.map(framework => [framework, []])), order = [];
    for (let i = 0; i < warmup + rounds; i++) {
      const names = [...frameworkNames];
      for (let k = names.length - 1; k > 0; k--) {
        const j = Math.floor(random() * (k + 1));
        [names[k], names[j]] = [names[j], names[k]];
      }
      const times = Object.fromEntries(names.map(framework => [framework, sample(frameworks[framework], op)]));
      if (i >= warmup) {
        for (const framework of frameworkNames) ms[framework].push(times[framework]);
        order.push(names[0]);
      }
    }
    results[name] = { ...ms, first: order };
  }
  console.log(JSON.stringify(results));
} else {
  const git = (cwd, ...args) => execFileSync("git", args, { cwd, encoding: "utf8" }).trim();
  const status = cwd => git(cwd, "status", "--porcelain").split("\n").filter(Boolean);
  const dirtyAtStart = status(root);
  const baseArgument = argument("base", "");
  const baseDir = baseArgument ? path.resolve(process.cwd(), baseArgument) : "";
  const baseCommit = baseDir ? git(baseDir, "rev-parse", "HEAD") : undefined;
  const baseDirty = baseDir ? status(baseDir) : undefined;
  const expectedBase = argument("base-commit", "");
  if (expectedBase && baseCommit !== expectedBase) throw Error(`The base build is at ${baseCommit}, not ${expectedBase}`);
  if (process.argv.includes("--build")) {
    run("dotnet", ["fickl", "Ranvier.CoreListBench.fsproj", "--outDir", "out", "--configuration", "Release", "--noCache"]);
    await writeFile(`${here}out/package.json`, "{\"type\":\"module\"}\n");
  }
  const processes = Number(argument("processes", 7)), warmup = argument("warmup", "10"), rounds = argument("rounds", "31");
  const ops = argument("ops", ""), label = argument("label", "core-list-solid");
  const runs = [];
  for (let p = 0; p < processes; p++) {
    const line = run(process.execPath, ["--expose-gc", fileURLToPath(import.meta.url), "--child", ...(baseDir ? [`--base=${baseDir}`] : []), `--seed=${p + 1}`, `--warmup=${warmup}`, `--rounds=${rounds}`, `--ops=${ops}`]).trim().split("\n").at(-1);
    runs.push({ process: p + 1, seed: p + 1, results: JSON.parse(line) });
    console.log(`process ${p + 1}/${processes}`);
  }
  const summary = Object.keys(runs[0].results).map(operation => {
    const all = framework => runs.flatMap(r => r.results[operation][framework]);
    const ratios = runs.flatMap(r => r.results[operation].ranvier.map((ms, i) => ms / r.results[operation].solid[i]));
    const perProcess = runs.map(r => median(r.results[operation].ranvier.map((ms, i) => ms / r.results[operation].solid[i])));
    const withBase = runs[0].results[operation].base !== undefined;
    const sameRound = (a, b) => runs.flatMap(r => r.results[operation][a].map((ms, i) => ms / r.results[operation][b][i]));
    const perProcessOf = (a, b) => runs.map(r => median(r.results[operation][a].map((ms, i) => ms / r.results[operation][b][i])));
    const shape = (a, b) => { const values = sameRound(a, b); return { median: median(values), q1: quantile(values, 0.25), q3: quantile(values, 0.75), perProcess: perProcessOf(a, b) }; };
    return {
      operation, solidMs: median(all("solid")), ranvierMs: median(all("ranvier")),
      ratio: { median: median(ratios), q1: quantile(ratios, 0.25), q3: quantile(ratios, 0.75), perProcess },
      ...(withBase ? { baseMs: median(all("base")), vsBase: shape("ranvier", "base"), baseRatio: shape("base", "solid") } : {}),
    };
  });
  const solidVersion = JSON.parse(await readFile(`${solidRoot}package.json`, "utf8")).version;
  const result = {
    measuredAt: new Date().toISOString(),
    commit: git(root, "rev-parse", "HEAD"),
    dirtyAtStart, dirty: status(root),
    ...(baseDir ? { baseDir, baseCommit, baseDirty } : {}),
    node: process.version, solidSignals: solidVersion, fable: run("dotnet", ["fickl", "--help"]).split("\n").find(line => line.includes("F# to JavaScript compiler")).split(":")[0].trim(),
    cpu: os.cpus()[0].model, cores: os.cpus().length, memoryGb: Math.round(os.totalmem() / 2 ** 30), platform: `${os.platform()} ${os.release()}`,
    processes, warmup: Number(warmup), rounds: Number(rounds), unit: "milliseconds per operation",
    notes: [
      "bench/Ranvier.CoreListBench: a keyed list over records with a label signal. Each row creates one effect writing the label to the row; one effect reads the list's rows. No DOM.",
      "Ranvier: createProjectionOnce CreateFirst keyed by id, Fable Release build, writes inside Graph.Run. Solid 2: mapArray keyed by item identity, createRenderEffect, flush() after each write, the prod build of @solidjs/signals.",
      "Each round times the operation once per framework, in a seeded random order, each on a fresh list after a forced GC. A ratio is Ranvier over Solid 2 within one round. Before timing, every operation's rows are checked equal across the frameworks.",
    ],
    summary, runs,
  };
  await mkdir(output, { recursive: true });
  await writeFile(`${output}${label}.json`, JSON.stringify(result, null, 2) + "\n");
  for (const row of summary)
    console.log(`${row.operation.padEnd(13)} solid ${row.solidMs.toFixed(4)}  ranvier ${row.ranvierMs.toFixed(4)}  ratio ${row.ratio.median.toFixed(3)} [${row.ratio.q1.toFixed(3)}, ${row.ratio.q3.toFixed(3)}] per process ${row.ratio.perProcess.map(r => r.toFixed(2)).join(", ")}`);
}
