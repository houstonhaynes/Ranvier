// The Chromium and heap gates of Arms.Match (spec, Testing 11-12) over the committed results in docs/research/arms-match:
// - for each arm operation, the pooled median paired ratio of ranvier-arms-match over ranvier-sugar (same process and
//   round) over the runs of pooled-interval.mjs, the committed runs whose recorded source hashes match the tree, is at
//   most the operation's entry in `budgets`; its pooled 95 % bootstrap interval is printed as below, above or
//   straddling the budget, and an empty pool fails;
// - the retained JavaScript heap per row after create is within 10 % of ranvier-sugar at 1,000 and 10,000 rows;
// - the retained JavaScript heap per row after one switch of every row is printed against ranvier-sugar at both counts.
// Exits 1 on a breach. Usage: node experiments/Ranvier.View.Arms/Checks/chromium-gate.mjs
import { readFileSync } from "node:fs";
import { committed, outOfDate, pooled, runs } from "./pooled-interval.mjs";

/**
 * The fixed pooled paired median budget per operation (plan 2026-10-24-issue-157.md, Decision P9). A pooled interval
 * that straddles its budget passes on the median and is recorded in the spec, the plan and docs/content/view/arms.md.
 */
export const budgets = { armsCreate1k: 1.05, armsSwitch1k: 1.04, armsPayload1k: 1.07, armsClear1k: 1.05 };
const heapBudget = 1.10;

const results = new URL("../../../docs/research/arms-match/", import.meta.url);
const read = name => JSON.parse(readFileSync(new URL(name, results), "utf8"));

/** The pooled median and 95 % interval of the operation, whether the median is within budget, and where the interval lies. */
export const judge = operation => {
  const { median, ci95, pairs } = pooled(operation);
  const budget = budgets[operation];
  const interval = ci95[1] <= budget ? "below" : ci95[0] > budget ? "above" : "straddles";
  return { median, ci95, pairs, budget, ok: median <= budget, interval };
};

if (import.meta.main) {
  let failed = false;

  for (const operation of Object.keys(budgets)) {
    const { median, ci95, pairs, budget, ok, interval } = judge(operation);
    if (pairs === 0) { console.log(`MISSING ${operation}: no ranvier-arms-match:ranvier-sugar pairs`); failed = true; continue; }
    failed ||= !ok;
    console.log(`${ok ? "PASS" : "FAIL"} ${operation} pooled paired median ${median.toFixed(3)} over ${pairs} pairs (95 % ${ci95.map(x => x.toFixed(3)).join("-")}, ${interval} the budget), budget ${budget}`);
  }

  const heapRows = [1000, 10000];
  const heapOf = (name, after) => read(name).results.filter(r => (r.after ?? "create") === after);
  const medianOf = (heap, rows, mode) => {
    const v = heap.filter(r => r.rows === rows && r.mode === mode).map(r => r.jsBytesPerRow).sort((a, b) => a - b);
    return v[Math.floor(v.length / 2)];
  };
  const heaps = { create: heapOf("arms-match-heap.json", "create"), switch: heapOf("arms-match-heap-switch.json", "switch") };
  for (const [after, heap] of Object.entries(heaps))
    for (const rows of heapRows) {
      const twin = medianOf(heap, rows, "ranvier-sugar"), arms = medianOf(heap, rows, "ranvier-arms-match");
      if (twin === undefined || arms === undefined) { console.log(`MISSING heap after ${after}, ${rows} rows`); failed = true; continue; }
      const ratio = arms / twin;
      const delta = `${arms.toFixed(0)} B per row against ${twin.toFixed(0)}, ${(100 * (ratio - 1)).toFixed(1)} % over ranvier-sugar`;
      if (after === "create") {
        const ok = ratio <= heapBudget;
        failed ||= !ok;
        console.log(`${ok ? "PASS" : "FAIL"} heap after create, ${rows} rows: ${delta}, budget 10 %`);
      } else console.log(`INFO heap after switch, ${rows} rows: ${delta}`);
    }

  for (const run of committed.filter(run => !runs.includes(run))) console.log(`out of the pool: ${run.path}, differs in ${outOfDate(run).join(", ")}`);
  for (const run of runs.map(r => r.read()))
    for (const note of (run.notes ?? []).filter(note => note.startsWith("CPU load"))) console.log(`load ${run.commit.slice(0, 8)}: ${note}`);
  if (failed) process.exitCode = 1;
}
