// The sources keyed-rows.mjs records by hash, and the source checks over them: at write time for a run into
// docs/research/arms-match/, and in the Arms.Match budget gate, which pools only the committed runs whose recorded
// hashes match the tree (plan 2026-10-24-issue-157.md, Decision P9).
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { join, relative, sep } from "node:path";

/**
 * The paths keyed-rows.mjs records by hash and the pool compares, relative to examples/Fable.Ranvier.Playground: the
 * benchmark, its page and build config, the keyed-rows fixtures, and every input of the Arms.Match lowering, the Fable
 * plugin project and each file it compiles included. A committed run is compared on these paths alone, whatever else
 * it records. A core or view-library change leaves the pool unchanged.
 */
export const hashedSources = [
  "benchmarks/frameworks/keyed-rows.mjs", "benchmarks/frameworks/keyed-page.mjs", "benchmarks/frameworks/keyed-solid.jsx", "benchmarks/frameworks/vite.config.mjs",
  "tests/KeyedRowsPerf.fs", "tests/KeyedRowsSugarPerf.fs", "tests/KeyedRowsArmsMatchPerf.fs", ...["Arms.fs", "Cells.fs", "Site.fs", "Errors.fs", "Diagnostics.fs"].map(path => `../../experiments/Ranvier.View.Arms/${path}`),
  ...["Ranvier.View.Arms.FablePlugin.fsproj", "Shape.fs", "Lower.fs", "Plugin.fs"].map(path => `../../experiments/Ranvier.View.Arms.FablePlugin/${path}`)
];

const playground = new URL("../../", import.meta.url);
const sha = bytes => createHash("sha256").update(bytes).digest("hex");

/** The sha256 of `bytes` as given, with LF line endings and with CRLF line endings. */
export const lineEndingHashes = bytes => {
  const lf = Buffer.from(bytes.toString("latin1").replace(/\r\n/g, "\n"), "latin1");
  return [sha(bytes), sha(lf), sha(Buffer.from(lf.toString("latin1").replace(/\n/g, "\r\n"), "latin1"))];
};

/**
 * Each hashed source that `recorded` omits, or records with a hash other than one of `lineEndingHashes` of the source
 * `read` returns. A run with an empty result was measured on the sources of the tree.
 */
export const mismatchedSources = (recorded, read = path => readFileSync(new URL(path, playground))) =>
  hashedSources.filter(path => !Object.hasOwn(recorded ?? {}, path) || !lineEndingHashes(read(path)).includes(recorded[path]));

/**
 * The hashed sources a committed run may not record: each path whose hash at the start of the run differs from its hash
 * at the end, and each path in `uncommitted`.
 */
export const staleSources = ({ before, after, uncommitted }) =>
  Object.keys(after).filter(path => before[path] !== after[path] || uncommitted.includes(path));

/** The paths of `files` that differ from HEAD in the working tree or the index, untracked paths included. */
export const uncommittedOf = (cwd, files) =>
  files.filter(path => execFileSync("git", ["status", "--porcelain", "--untracked-files=all", "--", path], { cwd, encoding: "utf8" }).trim() !== "");

/** The sha256 over the relative path and bytes of every file under `dir`, in path order. */
export const bundleHash = dir => {
  const hash = createHash("sha256");
  const files = readdirSync(dir, { recursive: true, withFileTypes: true }).filter(entry => entry.isFile())
    .map(entry => join(entry.parentPath, entry.name)).map(path => [relative(dir, path).split(sep).join("/"), path]).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0);
  for (const [name, path] of files) hash.update(`${name}\0`).update(readFileSync(path)).update("\0");
  return hash.digest("hex");
};

/**
 * The reasons a run into docs/research/arms-match/ may not be written: a pooled run is built with `--build` and serves
 * the bundle it built, so `bundleBefore`, hashed after the build, equals `bundleAfter`, hashed at the write.
 */
export const pooledRunProblems = ({ pooled, build, bundleBefore, bundleAfter }) =>
  !pooled ? [] : !build ? ["a pooled run builds its own bundle: pass --build"]
    : bundleBefore !== bundleAfter ? ["the bundle changed during the run"] : [];

const alive = pid => { try { process.kill(pid, 0); return true; } catch (error) { return error.code === "EPERM"; } };

/**
 * Runs `body` while holding the lock directory `path`, waiting `pollMs` between attempts while another live process
 * holds it. A lock left by an exited process is taken over.
 */
export const withLock = async (path, body, pollMs = 1000) => {
  for (;;) {
    try { mkdirSync(path); break; } catch (error) {
      if (error.code !== "EEXIST") throw error;
      let holder;
      try { holder = Number(readFileSync(join(path, "pid"), "utf8")); } catch {}
      if (holder && holder !== process.pid && !alive(holder)) { rmSync(path, { recursive: true, force: true }); continue; }
      await new Promise(resolve => setTimeout(resolve, pollMs));
    }
  }
  writeFileSync(join(path, "pid"), String(process.pid));
  try { return await body(); } finally { rmSync(path, { recursive: true, force: true }); }
};