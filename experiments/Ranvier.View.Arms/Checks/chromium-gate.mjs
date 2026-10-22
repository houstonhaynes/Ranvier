// The Chromium and heap gates of Arms.Match (spec, Testing 11-12) over the committed results in docs/research/arms-match:
// - for each arm operation, the median paired ratio of ranvier-arms-match over ranvier-sugar (same process and round)
//   is at most 1.12; its 95 % bootstrap interval is printed;
// - the retained JavaScript heap per row after create is within 10 % of ranvier-sugar at every row count.
// Exits 1 on a breach. Usage: node experiments/Ranvier.View.Arms/Checks/chromium-gate.mjs
import { readFileSync } from "node:fs";

const results = new URL("../../../docs/research/arms-match/", import.meta.url);
const read = name => JSON.parse(readFileSync(new URL(name, results), "utf8"));
const operations = ["armsCreate1k", "armsSwitch1k", "armsPayload1k", "armsClear1k"];
const budget = 1.12, heapBudget = 1.10;
let failed = false;

const timings = read("arms-match.json");
for (const operation of operations) {
  const pair = timings.pairRatios.find(r => r.operation === operation && r.numerator === "ranvier-arms-match" && r.denominator === "ranvier-sugar");
  if (!pair) { console.log(`MISSING ${operation}: no ranvier-arms-match:ranvier-sugar pair`); failed = true; continue; }
  const ok = pair.medianRatio <= budget;
  failed ||= !ok;
  console.log(`${ok ? "PASS" : "FAIL"} ${operation} paired median ${pair.medianRatio.toFixed(3)} (95 % ${pair.ci95.map(x => x.toFixed(3)).join("-")}), budget ${budget}`);
}

const heap = read("arms-match-heap.json").results.filter(r => (r.after ?? "create") === "create");
for (const rows of [...new Set(heap.map(r => r.rows))]) {
  const median = mode => { const v = heap.filter(r => r.rows === rows && r.mode === mode).map(r => r.jsBytesPerRow).sort((a, b) => a - b); return v[Math.floor(v.length / 2)]; };
  const ratio = median("ranvier-arms-match") / median("ranvier-sugar");
  const ok = ratio <= heapBudget;
  failed ||= !ok;
  console.log(`${ok ? "PASS" : "FAIL"} heap after create, ${rows} rows: ${(100 * (ratio - 1)).toFixed(1)} % over ranvier-sugar, budget 10 %`);
}

console.log(`load: ${timings.notes.slice(-1)[0]}`);
if (failed) process.exitCode = 1;
