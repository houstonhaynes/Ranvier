// Range comparison of the sugared adapters with their explicit twins.
// Usage: node benchmarks/frameworks/sugar-compare.mjs template|keyed|heap|alloc <result.json>...
// alloc fails on a sugar frame, on fixture-module bytes above the explicit twin's, on a missing positive control and on sugar-alloc.mjs data
// without twin repeats or modules.
import { readFileSync } from "node:fs";
import { isSugarFrame } from "./sugar-frames.mjs";

const [kind, ...files] = process.argv.slice(2);
const load = file => JSON.parse(readFileSync(file, "utf8"));
// The fixture module of each explicit twin and of each sugared row; the byte check sums the bytes they allocate.
const twinModules = { "ranvier-template": "FrameworkPerf.js", ranvier: "KeyedRowsPerf.js" };
const rowModules ={ "ranvier-template-sugar": "FrameworkSugarPerf.js", "ranvier-template-members": "FrameworkMembersPerf.js", "ranvier-template-class": "FrameworkClassPerf.js", "ranvier-sugar": "KeyedRowsSugarPerf.js" };
const median = values => {
  const sorted = [...values].sort((a, b) => a - b), middle = sorted.length >> 1;
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
};
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
    for (const mode of ["ranvier-template-sugar", "ranvier-template-members", "ranvier-template-class"])
      for (const row of data.summaries.filter(row => row.mode === mode)) {
        const twin = data.summaries.find(other => other.mode === "ranvier-template" && other.width === row.width && other.phase === row.phase);
        report(`${mode} ${row.phase}@${row.width}`, range(twin.byProcessUs), range(row.byProcessUs));
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
      // A sugared sample above the explicit median fails; one below it is reported as LOWER.
      const limit = 0.005 * median(explicit);
      const higher = sugar.some(value => value - median(explicit) > limit);
      const lower = sugar.some(value => median(explicit) - value > limit);
      if (higher) failed++;
      console.log(`${higher ? "DIFFER  " : lower ? "LOWER   " : "same    "} heap ${rows} rows: explicit ${explicit.join(", ")} B/row, sugar ${sugar.join(", ")} B/row`);
    }
  if (kind === "alloc") {
    const sugared = /-(sugar|members|class)$/;
    const explicitOf = sample => data.allocations.filter(other => other.mode === sample.mode.replace(sugared, "") && other.operation === sample.operation && other.width === sample.width);
    if (data.allocations.some(sample => sugared.test(sample.mode) && sample.self.some(row => row.module === undefined)))
      console.log("note     frames without a recorded module are classified by name");
    for (const sample of data.allocations.filter(sample => sugared.test(sample.mode))) {
      // Every sampled function is searched; the benchmark adapter entries are excluded.
      const named = sample.self.filter(row => isSugarFrame(row) && !/^mountTemplate(Sugar|Members|Class) /.test(row.name));
      const twin = explicitOf(sample).find(other => other.process === sample.process);
      if (named.length) failed++;
      console.log(`${named.length ? "SUGAR   " : "clean   "} ${sample.mode} ${sample.operation ?? `width ${sample.width}`}${sample.process === undefined ? "" : ` process ${sample.process + 1}`}: ${sample.self.length} functions${twin ? `, ${sample.totalBytes} B against ${twin.totalBytes} B explicit` : ""}${named.length ? ", " + named.map(row => row.name).join(", ") : ""}`);
    }
    // Positive control: a sugar-alloc.mjs file holds a control sample with allocations in a sugar module, in every process.
    const recorded = data.allocations.every(sample => sample.self.every(row => row.module !== undefined));
    const controls = data.allocations.filter(sample => sample.mode === "sugar-alloc-control");
    if (recorded) {
      const processes = new Set(data.allocations.map(sample => sample.process));
      const proven = new Set(controls.filter(sample => sample.self.some(row => isSugarFrame(row))).map(sample => sample.process));
      const missing = [...processes].filter(process => !proven.has(process));
      if (missing.length) failed++;
      console.log(`${missing.length ? "CONTROL " : "control "} ${controls.map(sample => `process ${sample.process + 1}: ${sample.self.filter(row => isSugarFrame(row)).reduce((sum, row) => sum + row.bytes, 0)} B in sugar modules`).join(", ") || "no control sample"}${missing.length ? `; no sugar-module allocation in process ${missing.map(process => process + 1).join(", ")}` : ""}`);
    }
    // Sampled bytes, gated on the fixture modules (the twin's fixture module plus the row's own): the sugared median may exceed the
    // twin's median by at most the range of the twin's own fixture-module samples. Total heap bytes are reported with their detection limit.
    for (const [mode, samples] of Object.entries(Object.groupBy(data.allocations.filter(sample => sugared.test(sample.mode)), sample => `${sample.mode}\u0000${sample.operation ?? sample.width}`))) {
      const [name] = mode.split("\u0000"), twins = explicitOf(samples[0]), twinName = twins[0]?.mode ?? name.replace(sugared, "");
      const rows = samples[0].width && (samples[0].mounts ?? data.mounts) ? samples[0].width * (samples[0].mounts ?? data.mounts) : 1;
      const unit = rows === 1 ? "B" : "B/row", perRow = bytes => bytes / rows, fixed = value => value.toFixed(rows === 1 ? 0 : 1);
      const at = samples[0].operation ?? `width ${samples[0].width}`;
      if (samples[0].operation !== undefined && (twins.length < 2 || !recorded)) {
        console.log(`UNTESTED bytes ${name} ${at}: keyed-rows.mjs profiles record one sample per mode and operation without modules; no twin tolerance`);
        continue;
      }
      if (twins.length < 2 || !recorded) {
        failed++;
        console.log(`UNTESTED bytes ${name} ${at}: ${twins.length} ${twinName} sample(s)${recorded ? "" : ", frames without modules"}; sugar-alloc.mjs data needs 2 or more twin samples with modules`);
        continue;
      }
      const fixtureBytes = (sample, modules) => sample.self.filter(row => modules.some(module => row.module.endsWith(`/${module}`))).reduce((sum, row) => sum + row.bytes, 0);
      const twinFixture = twins.map(sample => perRow(fixtureBytes(sample, [twinModules[twinName]])));
      const ownFixture = samples.map(sample => perRow(fixtureBytes(sample, [twinModules[twinName], rowModules[name]])));
      const tolerance = Math.max(...twinFixture) - Math.min(...twinFixture);
      const excess = median(ownFixture) - median(twinFixture);
      const over = excess > tolerance;
      if (over) failed++;
      console.log(`${over ? "BYTES   " : "bytes ok"} ${name} ${at}: ${twinModules[twinName]} + ${rowModules[name]} allocate ${ownFixture.map(fixed).join(", ")} ${unit} (median ${fixed(median(ownFixture))}) against ${twinModules[twinName]} ${twinFixture.map(fixed).join(", ")} ${unit} (median ${fixed(median(twinFixture))}); excess ${fixed(excess)} ${unit}, tolerance ${fixed(tolerance)} ${unit}`);
      const totals = twins.map(sample => perRow(sample.totalBytes)), limit = Math.max(...totals) - Math.min(...totals);
      console.log(`info     ${name} ${at}: total heap median ${fixed(perRow(median(samples.map(sample => sample.totalBytes))))} against ${fixed(median(totals))} ${unit} explicit; detects excess above ${fixed(limit)} ${unit} only`);
      // Modules whose sugared median exceeds the twin's by more than the twin's own range for that module.
      const moduleBytes = (sample, module) => perRow(sample.self.filter(row => row.module === module).reduce((sum, row) => sum + row.bytes, 0));
      for (const module of new Set([...samples, ...twins].flatMap(sample => sample.self.map(row => row.module)))) {
        const twin = twins.map(sample => moduleBytes(sample, module)), own = median(samples.map(sample => moduleBytes(sample, module)));
        const moduleExcess = own - median(twin), moduleRange = Math.max(...twin) - Math.min(...twin);
        if (moduleExcess > moduleRange && module !== `generated-tests/${rowModules[name]}`)
          console.log(`info     ${name} ${at}: ${module} median ${fixed(own)} against ${fixed(median(twin))} ${unit} explicit, excess ${fixed(moduleExcess)} above its explicit range ${fixed(moduleRange)} ${unit}`);
      }
    }
  }
}
process.exitCode = failed ? 1 : 0;
