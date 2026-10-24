// The pooled paired interval of ranvier-arms-match over ranvier-sugar across the committed Chromium runs of #157 (plan
// 2026-10-24-issue-157.md, Decision P9) whose recorded source hashes match the tree. chromium-gate.mjs judges each
// operation by the pooled median against its fixed budget; this script prints the pooled interval, every pooled run's
// median and interval, and the hashed sources that keep each other committed run out of the pool.
// Usage: node experiments/Ranvier.View.Arms/Checks/pooled-interval.mjs
import { readFileSync } from "node:fs";
import { mismatchedSources } from "../../../examples/Fable.Ranvier.Playground/benchmarks/frameworks/run-sources.mjs";

const at = path => ({ path, read: () => JSON.parse(readFileSync(new URL(`../../../${path}`, import.meta.url), "utf8")) });

/** Every committed run with ranvier-arms-match and ranvier-sugar samples, oldest first. */
export const committed = [
  at("docs/research/arms-match/arms-spike.json"),
  at("docs/.ai/benchmarks/arms-157-noisy-probe.json"),
  at("docs/research/arms-match/arms-match-299ab276.json"),
  at("docs/research/arms-match/arms-match-shape.json"),
  at("docs/research/arms-match/arms-match-dc8b3d5b.json"),
  at("docs/research/arms-match/arms-match-5369d6a0.json"),
  at("docs/research/arms-match/arms-match.json"),
  at("docs/research/arms-match/arms-match-da6d32e6-1.json"),
  at("docs/research/arms-match/arms-match-da6d32e6-2.json"),
  at("docs/research/arms-match/arms-match-da6d32e6-3.json"),
  at("docs/research/arms-match/arms-match-da6d32e6-4.json"),
  at("docs/research/arms-match/arms-match-da6d32e6-5.json"),
  ...[1, 2, 3, 4, 5, 6].map(n => at(`docs/research/arms-match/arms-match-22d86d44-${n}.json`))
];

/** The hashed sources whose recorded hash in the run differs from the tree's. */
export const outOfDate = run => mismatchedSources(run.read().sourceHashes);

/** The pool: the committed runs whose recorded hashes match every hashed source of the tree. */
export const runs = committed.filter(run => outOfDate(run).length === 0);

const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

/** Each ranvier-arms-match sample of the operation divided by the ranvier-sugar sample of the same process and round. */
export const pairsOf = (run, operation) =>
  run.samples.filter(s => s.operation === operation && s.mode === "ranvier-arms-match").map(s => {
    const other = run.samples.find(o => o.operation === operation && o.mode === "ranvier-sugar" && o.replicate === s.replicate && o.round === s.round);
    return s.ms / other.ms;
  });

/** The median of the ratios and its percentile bootstrap 95 % interval, with the resampling of keyed-rows.mjs. */
export const interval = ratios => {
  const sorted = [...ratios].sort((a, b) => a - b);
  let seed = 0x9e3779b9;
  const random = () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32;
  const medians = Array.from({ length: 10000 }, () => median(Array.from(sorted, () => sorted[Math.floor(random() * sorted.length)]))).sort((a, b) => a - b);
  return { pairs: sorted.length, median: median(sorted), ci95: [medians[249], medians[9750]] };
};

/** The interval over the pairs of every run in `runs`. */
export const pooled = operation => interval(runs.flatMap(run => pairsOf(run.read(), operation)));

if (import.meta.main) {
  const loaded = runs.map(run => ({ ...run, data: run.read() }));
  for (const operation of ["armsCreate1k", "armsSwitch1k", "armsPayload1k", "armsClear1k"]) {
    const each = loaded.map(run => { const r = interval(pairsOf(run.data, operation)); return `${run.path.split("/").pop()} ${r.median.toFixed(3)} (${r.ci95.map(x => x.toFixed(3)).join("-")})`; });
    const all = pooled(operation);
    console.log(`${operation}: pooled ${all.median?.toFixed(3)} (95 % ${all.ci95.map(x => x?.toFixed(3)).join("-")}, ${all.pairs} pairs); ${each.join("; ")}`);
  }
  for (const run of committed.filter(run => !runs.includes(run))) console.log(`out of the pool: ${run.path} (${run.read().commit.slice(0, 8)}), differs in ${outOfDate(run).join(", ")}`);
}
