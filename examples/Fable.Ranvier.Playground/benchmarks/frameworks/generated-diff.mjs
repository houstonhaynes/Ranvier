// Gate 3 of the DOM twin spec (§7.1): the Fable output files that differ between a base and a branch directory,
// with carriage returns and lines starting `import ` ignored and `fable_modules/` (package output) skipped. Exits 1
// when a differing file does not match the allowed pattern, by default the Native module of Ranvier.View.
// Usage: node benchmarks/frameworks/generated-diff.mjs <base dir> <branch dir> [--allow <regex>] [--expected-hunk <file> ...]
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const filesUnder = root => {
  const result = [];
  const walk = directory => {
    for (const name of readdirSync(directory)) {
      const path = join(directory, name);
      if (statSync(path).isDirectory()) { if (name !== "fable_modules") walk(path); }
      else if (name.endsWith(".js")) result.push(relative(root, path).split(sep).join("/"));
    }
  };
  walk(root);
  return result;
};

const body = path => readFileSync(path, "utf8").replace(/\r/g, "").split("\n").filter(line => !line.startsWith("import ")).join("\n");

/** The allow pattern of the command line when `--allow` is absent. */
export const defaultAllow = /^src\/Ranvier\.View\/Native\.js$/;

export function expectedChangesFromArgs(args) {
  const paths = [];
  for (let i = 0; i < args.length; i++) {
    if (args[i] !== "--expected-hunk") continue;
    if (!args[i + 1] || args[i + 1].startsWith("--")) throw new Error("--expected-hunk requires a file");
    paths.push(args[++i]);
  }
  if (!paths.length) {
    const directory = new URL("../../../../docs/research/2026-10-21-dom-twin/", import.meta.url);
    const manifest = JSON.parse(readFileSync(new URL("production-changes.json", directory), "utf8"));
    paths.push(...manifest.patches.map(path => new URL(path, directory)));
  }
  const changes = paths.map(path => parseExpectedChange(readFileSync(path, "utf8")));
  if (new Set(changes.map(change => change.name)).size !== changes.length)
    throw new Error("Expected changes must name distinct files");
  return changes;
}

export function parseExpectedChange(patch) {
  const lines = patch.replace(/\r/g, "").trimEnd().split("\n");
  if (!lines[0]?.startsWith("--- a/") || lines[1] !== "+++ b/" + lines[0].slice(6))
    throw new Error("Expected a single-file unified diff with matching paths");
  const hunks = [];
  let before, after;
  const finish = () => {
    if (before) hunks.push({ before: before.join("\n"), after: after.join("\n") });
  };
  for (const line of lines.slice(2)) {
    if (line.startsWith("@@ ")) { finish(); before = []; after = []; }
    else if (!before || ![" ", "+", "-"].includes(line[0])) throw new Error("Invalid expected hunk line");
    else {
      if (line[0] !== "+") before.push(line.slice(1));
      if (line[0] !== "-") after.push(line.slice(1));
    }
  }
  finish();
  if (!hunks.length || hunks.some(h => !h.before || h.before === h.after)) throw new Error("Expected a nonempty change");
  return { name: lines[0].slice(6), hunks };
}

export function diffDirectories(base, branch, allow, expectedChanges = []) {
  const names = new Set([...filesUnder(base), ...filesUnder(branch), ...expectedChanges.map(c => c.name)]);
  const differing = [], disallowed = [];
  for (const name of [...names].sort()) {
    let left = null, right = null;
    try { left = body(join(base, name)); } catch { }
    try { right = body(join(branch, name)); } catch { }
    const expected = expectedChanges.find(change => change.name === name);
    if (expected) {
      let patched = left;
      for (const { before, after } of expected.hunks) {
        if (patched === null || !before || patched.split(before).length !== 2) { patched = null; break; }
        patched = patched.replace(before, () => after);
      }
      if (patched === null || patched !== right) { differing.push(name); disallowed.push(name); }
      else if (left !== right) differing.push(name);
    } else if (left !== right) {
      differing.push(name);
      if (!allow.test(name)) disallowed.push(name);
    }
  }
  return { differing, disallowed };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [base, branch] = process.argv.slice(2);
  const index = process.argv.indexOf("--allow");
  const allow = index < 0 ? defaultAllow : new RegExp(process.argv[index + 1]);
  const expected = expectedChangesFromArgs(process.argv.slice(4));
  const { differing, disallowed } = diffDirectories(base, branch, allow, expected);
  for (const name of differing) console.log(`${disallowed.includes(name) ? "DIFFERS" : "allowed"} ${name}`);
  process.exitCode = disallowed.length ? 1 : 0;
}
