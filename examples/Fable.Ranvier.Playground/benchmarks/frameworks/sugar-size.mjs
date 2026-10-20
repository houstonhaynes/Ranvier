// Criterion 4: bundle bytes of the explicit and the sugared benchmark fixtures, raw, gzip and Brotli; and whether the explicit entry holds sugar code.
// Writes docs/research/framework-performance/investigation/view-sugar/bundle.json; Vite's log goes to stderr.
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { gzipSync, brotliCompressSync, constants } from "node:zlib";

const cwd = new URL("../../", import.meta.url);
const output = new URL("../../docs/research/framework-performance/investigation/view-sugar/bundle.json", cwd);
const size = file => {
  const raw = readFileSync(file);
  return { raw: raw.length, gzip: gzipSync(raw, { level: 9 }).length, brotli: brotliCompressSync(raw, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length };
};
const build = (entry, minify) => {
  execFileSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/sugar-size.config.mjs"], { cwd, stdio: ["ignore", 2, 2], windowsHide: true, env: { ...process.env, SUGAR_SIZE_ENTRY: entry, SUGAR_SIZE_MINIFY: String(minify) } });
  return new URL(`generated-sugar-size/${entry}${minify ? "" : "-unminified"}/bundle.js`, cwd);
};
// Sugar is detected on unminified builds.
const sugarNames = /Ranvier\.View\.Foundation\.Sugar|ElementBuilder|TemplateBuilder\$1_[A-Z]/;
const result = {};
for (const entry of ["explicit", "sugared"]) result[entry] = size(build(entry, true));
result.delta = Object.fromEntries(["raw", "gzip", "brotli"].map(key => [key, result.sugared[key] - result.explicit[key]]));
result.explicitHoldsSugar = sugarNames.test(readFileSync(build("explicit", false), "utf8"));
result.sugaredHoldsSugar = sugarNames.test(readFileSync(build("sugared", false), "utf8"));
mkdirSync(new URL("./", output), { recursive: true });
writeFileSync(output, JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
