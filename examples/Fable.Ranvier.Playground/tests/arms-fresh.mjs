// Runs a dotnet fickl compile of DomTests into the output folder, adding --noCache when the folder's Arms.js is missing
// or older than a source it inlines (Cells.fs, Site.fs, Errors.fs, Diagnostics.fs). Fable keeps an Arms.js whose inlined
// Cells and Site code is stale. Usage: node tests/arms-fresh.mjs <outDir> <configuration> [fickl arguments...]
import { execFileSync } from "node:child_process";
import { existsSync, statSync, utimesSync } from "node:fs";

const [outDir, configuration, ...rest] = process.argv.slice(2);
const arms = new URL("../../../experiments/Ranvier.View.Arms/", import.meta.url);
const newest = Math.max(...["Cells.fs", "Site.fs", "Errors.fs", "Diagnostics.fs"].map(name => statSync(new URL(name, arms)).mtimeMs));
const emitted = new URL(`../${outDir}/experiments/Ranvier.View.Arms/Arms.js`, import.meta.url);
const stale = !existsSync(emitted) || statSync(emitted).mtimeMs < newest;
execFileSync("dotnet", ["fickl", "tests/DomTests.fsproj", "--outDir", outDir, "--configuration", configuration, ...rest, ...(stale ? ["--noCache"] : [])],
  { cwd: new URL("../", import.meta.url), stdio: "inherit", windowsHide: true });
// Fable leaves an unchanged output's time as it was; marking Arms.js current lets the next compile use the cache.
if (stale && existsSync(emitted)) utimesSync(emitted, new Date(), new Date());
