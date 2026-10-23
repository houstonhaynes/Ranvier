// Gates 1 and 2 of the DOM twin spec (§7.1). Requires `npm run compile:tests`; builds into generated-sugar-size/ with
// the sugar-size Vite config.
// --out <file>      writes the SHA-256 of each minified entry's bundle.js
// --compare <file>  exits 1 when an entry's hash differs from the file's, naming each such entry
// --absence         exits 1 when the unminified explicit or combined bundle holds an Inspect or Debug module or a probe case
// --expect-present  with --absence, checks that the inspect entry triggers both detectors
// --inspect-cost    compiles generated-inspect/ with RANVIER_VIEW_INSPECT and prints the minified inspect entry's raw,
//                   gzip and Brotli bytes over the combined entry's
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { inspectionFailures } from "./inspection-presence.mjs";

const cwd = new URL("../../", import.meta.url);
const entries = ["explicit", "explicit-combined", "sugared", "members", "class", "combined"];
const build = (entry, minify) => {
  execFileSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/sugar-size.config.mjs"],
    { cwd, stdio: ["ignore", 2, 2], windowsHide: true, env: { ...process.env, SUGAR_SIZE_ENTRY: entry, SUGAR_SIZE_MINIFY: String(minify) } });
  return new URL(`generated-sugar-size/${entry}${minify ? "" : "-unminified"}/bundle.js`, cwd);
};
const sha = file => createHash("sha256").update(readFileSync(file)).digest("hex");
const option = name => { const i = process.argv.indexOf(name); return i < 0 ? undefined : process.argv[i + 1] ?? ""; };

if (process.argv.includes("--inspect-cost")) {
  const { gzipSync, brotliCompressSync } = await import("node:zlib");
  execFileSync("dotnet", ["fickl", "tests/DomTests.fsproj", "--outDir", "generated-inspect", "--configuration", "Release", "--define", "RANVIER_VIEW_INSPECT"],
    { cwd, stdio: ["ignore", 2, 2], windowsHide: true });
  const sizes = entry => {
    const bytes = readFileSync(build(entry, true));
    return { raw: bytes.length, gzip: gzipSync(bytes, { level: 9 }).length, brotli: brotliCompressSync(bytes).length };
  };
  const combined = sizes("combined"), inspect = sizes("inspect");
  const delta = Object.fromEntries(Object.keys(combined).map(key => [key, inspect[key] - combined[key]]));
  console.log(JSON.stringify({ combined, inspect, delta }, null, 2));
} else if (process.argv.includes("--absence")) {
  const expectPresent = process.argv.includes("--expect-present");
  const failures = [];
  for (const entry of expectPresent ? ["inspect"] : ["explicit", "combined"]) {
    const text = readFileSync(build(entry, false), "utf8");
    for (const failure of inspectionFailures(text, expectPresent)) failures.push(`${entry}: ${failure}`);
  }
  for (const failure of failures) console.error(failure);
  process.exitCode = failures.length ? 1 : 0;
} else {
  const hashes = Object.fromEntries(entries.map(entry => [entry, sha(build(entry, true))]));
  const out = option("--out"), compare = option("--compare");
  if (out !== undefined) {
    mkdirSync(dirname(resolve(out)), { recursive: true });
    writeFileSync(resolve(out), JSON.stringify(hashes, null, 2) + "\n");
  }
  if (compare !== undefined) {
    const expected = JSON.parse(readFileSync(resolve(compare), "utf8"));
    const differing = entries.filter(entry => expected[entry] !== hashes[entry]);
    for (const entry of differing) console.error(`${entry}: ${expected[entry]} at the base, ${hashes[entry]} on the branch`);
    process.exitCode = differing.length ? 1 : 0;
  }
  console.log(JSON.stringify(hashes, null, 2));
}
