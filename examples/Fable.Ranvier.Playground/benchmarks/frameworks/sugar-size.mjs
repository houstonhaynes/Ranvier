// Criterion 4: bundle bytes of the explicit fixtures, of each sugared declaration style (free, ofMembers, ofClass) and of all three combined,
// raw, gzip and Brotli, each delta against its explicit twin entry held to budgetGzip (exit code 1 otherwise); whether the explicit and sugared entries hold sugar
// code; and frameCheck: every declaration in the combined bundle's sugar modules is a sugar name, both explicit bundles hold none (exit code 1 otherwise),
// and nameCollisions lists the explicit declarations that a frame without a recorded module would misattribute by name.
// Writes docs/research/framework-performance/investigation/view-sugar-v2/bundle.json; Vite's log goes to stderr.
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { gzipSync, brotliCompressSync, constants } from "node:zlib";
import { bundleSugar, isSugarName } from "./sugar-frames.mjs";

const cwd = new URL("../../", import.meta.url);
const output = new URL("../../docs/research/framework-performance/investigation/view-sugar-v2/bundle.json", cwd);
const size = file => {
  const raw = readFileSync(file);
  return { raw: raw.length, gzip: gzipSync(raw, { level: 9 }).length, brotli: brotliCompressSync(raw, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length };
};
const build = (entry, minify) => {
  execFileSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/sugar-size.config.mjs"], { cwd, stdio: ["ignore", 2, 2], windowsHide: true, env: { ...process.env, SUGAR_SIZE_ENTRY: entry, SUGAR_SIZE_MINIFY: String(minify) } });
  return new URL(`generated-sugar-size/${entry}${minify ? "" : "-unminified"}/bundle.js`, cwd);
};
// Each sugared entry mapped to the explicit entry used as its delta baseline: one-row entries to the one-row explicit entry, and the
// three-row combined entry to explicit-combined, the explicit twins of the same three rows with the same keyed and arm fixtures.
const twins = { sugared: "explicit", members: "explicit", class: "explicit", combined: "explicit-combined" };
const entries = ["explicit", "explicit-combined", ...Object.keys(twins)];
const result = {};
for (const entry of entries) result[entry] = size(build(entry, true));
const deltaOf = (entry, twin) => Object.fromEntries(["raw", "gzip", "brotli"].map(key => [key, result[entry][key] - result[twin][key]]));
result.twins = twins;
result.delta = Object.fromEntries(Object.entries(twins).map(([entry, twin]) => [entry, deltaOf(entry, twin)]));
result.combinedOverOneRowExplicit = deltaOf("combined", "explicit");
result.budgetGzip = 1024;
result.withinBudget = Object.keys(twins).every(entry => result.delta[entry].gzip <= result.budgetGzip);
// Sugar is detected on unminified builds, by the bundle regions of the sugar modules.
const explicitSugar = bundleSugar(readFileSync(build("explicit", false), "utf8"));
const explicitCombinedSugar = bundleSugar(readFileSync(build("explicit-combined", false), "utf8"));
result.explicitHoldsSugar = explicitSugar.inSugar.length + explicitCombinedSugar.inSugar.length > 0;
const unminified = readFileSync(build("sugared", false), "utf8");
result.sugaredHoldsSugar = bundleSugar(unminified).inSugar.length > 0;
const combinedSugar = bundleSugar(readFileSync(build("combined", false), "utf8"));
const uncovered = combinedSugar.inSugar.filter(name => !isSugarName(name));
result.frameCheck = {
  sugarDeclarations: combinedSugar.inSugar.length,
  uncovered,
  nameCollisions: explicitSugar.collisions,
  violations: [
    ...(combinedSugar.inSugar.length ? [] : ["the combined bundle holds no sugar-module declaration"]),
    ...uncovered.map(name => `${name} is declared in a sugar module but is no sugar name`),
    ...[["explicit", explicitSugar], ["explicit-combined", explicitCombinedSugar]].filter(([, sugar]) => sugar.inSugar.length).map(([entry, sugar]) => `the ${entry} bundle declares ${sugar.inSugar.slice(0, 5).join(", ")} in a sugar module`)
  ]
};
if (!result.withinBudget || result.frameCheck.violations.length) process.exitCode = 1;
mkdirSync(new URL("./", output), { recursive: true });
writeFileSync(output, JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
