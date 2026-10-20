// Range comparison of the sugared adapters with their explicit twins.
// Usage: node benchmarks/frameworks/sugar-compare.mjs template|keyed|heap|alloc <result.json>...
import { readFileSync } from "node:fs";

const [kind, ...files] = process.argv.slice(2);
const load = file => JSON.parse(readFileSync(file, "utf8"));
const range = values => [Math.min(...values), Math.max(...values)];
const overlaps = ([a, b], [c, d]) => a <= d && c <= b;
let failed = 0;
const report = (name, explicit, sugar) => {
  const ok = overlaps(explicit, sugar);
  if (!ok) failed++;
  console.log(`${ok ? "overlap " : "DISJOINT"} ${name} explicit=[${explicit.map(v => v.toFixed(3))}] sugar=[${sugar.map(v => v.toFixed(3))}]`);
};
for (const file of files) {
  const data = load(file);
  if (kind === "template")
    for (const row of data.summaries.filter(row => row.mode === "ranvier-template-sugar")) {
      const twin = data.summaries.find(other => other.mode === "ranvier-template" && other.width === row.width && other.phase === row.phase);
      report(`${row.phase}@${row.width}`, range(twin.byProcessUs), range(row.byProcessUs));
    }
  if (kind === "keyed")
    for (const row of data.summaries.filter(row => row.mode === "ranvier-sugar")) {
      const twin = data.summaries.find(other => other.mode === "ranvier" && other.operation === row.operation);
      report(row.operation, range(twin.byProcessMs), range(row.byProcessMs));
    }
  if (kind === "heap")
    for (const rows of [...new Set(data.results.map(row => row.rows))]) {
      const of = mode => data.results.filter(row => row.mode === mode && row.rows === rows).map(row => row.jsBytesPerRow);
      const explicit = of("ranvier"), sugar = of("ranvier-sugar");
      const median = values => {
        const sorted = [...values].sort((a, b) => a - b), middle = sorted.length >> 1;
        return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
      };
      // A sugared sample above the explicit median fails; one below it is reported as LOWER.
      const limit = 0.005 * median(explicit);
      const higher = sugar.some(value => value - median(explicit) > limit);
      const lower = sugar.some(value => median(explicit) - value > limit);
      if (higher) failed++;
      console.log(`${higher ? "DIFFER  " : lower ? "LOWER   " : "same    "} heap ${rows} rows: explicit ${explicit.join(", ")} B/row, sugar ${sugar.join(", ")} B/row`);
    }
  if (kind === "alloc")
    for (const sample of data.allocations.filter(sample => sample.mode.endsWith("sugar"))) {
      // Every sampled function is searched. `mountTemplateSugar` is the benchmark adapter entry and is excluded.
      const named = sample.self.filter(row => /ElementBuilder|Builders_|Sugar|TemplateBuilder\$1_|Delay/.test(row.name) && !row.name.startsWith("mountTemplateSugar "));
      const twin = data.allocations.find(other => other.mode === sample.mode.replace(/-sugar$/, "") && other.operation === sample.operation && other.process === sample.process);
      if (named.length) failed++;
      console.log(`${named.length ? "SUGAR   " : "clean   "} ${sample.operation ?? `width ${sample.width}`}${sample.process === undefined ? "" : ` process ${sample.process + 1}`}: ${sample.self.length} functions${twin ? `, ${sample.totalBytes} B against ${twin.totalBytes} B explicit` : ""}${named.length ? ", " + named.map(row => row.name).join(", ") : ""}`);
    }
}
process.exitCode = failed ? 1 : 0;
