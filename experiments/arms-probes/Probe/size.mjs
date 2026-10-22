// Bundles each entry with Vite (Playground's install) and prints raw, gzip and Brotli bytes with deltas to explicit.
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { gzipSync, brotliCompressSync, constants } from "node:zlib";

const vite = new URL("../../../examples/Fable.Ranvier.Playground/node_modules/vite/bin/vite.js", import.meta.url);
const cwd = new URL("./", import.meta.url);
const result = {};
for (const entry of ["explicit", "lens", "plugin"]) {
  execFileSync(process.execPath, [vite.pathname.replace(/^\/(\w:)/, "$1"), "build", "--config", "size.config.mjs"], { cwd, stdio: ["ignore", 2, 2], env: { ...process.env, PROBE_ENTRY: entry } });
  const raw = readFileSync(new URL(`bundle-${entry}/bundle.js`, cwd));
  result[entry] = { raw: raw.length, gzip: gzipSync(raw, { level: 9 }).length, brotli: brotliCompressSync(raw, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length };
}
for (const entry of ["lens", "plugin"]) result[`${entry}-delta`] = Object.fromEntries(["raw", "gzip", "brotli"].map(k => [k, result[entry][k] - result.explicit[k]]));
console.log(JSON.stringify(result, null, 2));
