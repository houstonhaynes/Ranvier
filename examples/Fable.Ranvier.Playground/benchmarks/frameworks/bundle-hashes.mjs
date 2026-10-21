// Gates 1 and 2 of the DOM twin spec (§7.1). Requires `npm run compile:tests`; builds into generated-sugar-size/ with
// the sugar-size Vite config.
// --out <file>      writes the SHA-256 of each minified entry's bundle.js
// --compare <file>  exits 1 when an entry's hash differs from the file's, naming each such entry
// --absence         exits 1 when the unminified explicit or combined bundle holds an Inspect or Debug module or a probe case
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

const cwd = new URL("../../", import.meta.url);
const entries = ["explicit", "explicit-combined", "sugared", "members", "class", "combined"];
const build = (entry, minify) => {
  execFileSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/sugar-size.config.mjs"],
    { cwd, stdio: ["ignore", 2, 2], windowsHide: true, env: { ...process.env, SUGAR_SIZE_ENTRY: entry, SUGAR_SIZE_MINIFY: String(minify) } });
  return new URL(`generated-sugar-size/${entry}${minify ? "" : "-unminified"}/bundle.js`, cwd);
};
const sha = file => createHash("sha256").update(readFileSync(file)).digest("hex");
const option = name => { const i = process.argv.indexOf(name); return i < 0 ? undefined : process.argv[i + 1] ?? ""; };

if (process.argv.includes("--absence")) {
  const region = /\/\/#region [^\n]*experiments\/Ranvier\.View\.Foundation\/(Inspect|Debug)\.js/;
  const probe = /\b(ListProbe|ArmProbe|BoundaryProbe|HoleProbe|InstanceProbe|MountProbe)\b/;
  const failures = [];
  for (const entry of ["explicit", "combined"]) {
    const text = readFileSync(build(entry, false), "utf8");
    if (region.test(text)) failures.push(`${entry}: the bundle holds an Inspect or Debug module`);
    if (probe.test(text)) failures.push(`${entry}: the bundle names a probe case`);
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
