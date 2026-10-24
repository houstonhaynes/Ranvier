// Bundle budget of Arms.Match (spec, "Bundle budget"), over the ArmsSizeEntries functions of the Playground's DomTests,
// compiled by this script in Release with no defines into generated-size-tests (ARMS_SIZE_REUSE=1 reuses that output),
// bundled by the Playground's Vite with minification, gzip level 9 and Brotli quality 11:
// - one: the Arms.Match loader against its explicit twin, at most 400 B gzip;
// - perSite: a second site's delta (two sites minus one) against its twin's, at most 60 B gzip;
// - plain: an entry calling no Arms.Match holds no Ranvier.View.Arms module (Ruling 12);
// - quotation: the unminified Arms.Match entry holds no quotation code;
// - helpers: the unminified Arms.Match entry, from its first Ranvier.View.Arms module to its end (the entry module), holds
//   no curry, uncurry or partialApply.
// - cells: the Fable output holds no Ranvier.View.Arms Cells module, and the unminified Arms.Match entry holds none and no
//   WeakMap (payload-direct sites hold no lookup state).
// Exits 1 on a breach. Writes docs/research/arms-match/bundle.json.
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { gzipSync, brotliCompressSync, constants } from "node:zlib";

const here = new URL("./", import.meta.url);
const playground = new URL("../../../examples/Fable.Ranvier.Playground/", here);
const generated = "generated-size-tests";
const defines = [];
const output = new URL("../../../docs/research/arms-match/bundle.json", here);
const pathOf = url => decodeURIComponent(url.pathname).replace(/^\/([A-Za-z]:)/, "$1");
mkdirSync(new URL("generated-size/", here), { recursive: true });

if (process.env.ARMS_SIZE_REUSE !== "1") {
  const run = args => execFileSync("dotnet", args, { cwd: pathOf(playground), stdio: ["ignore", 2, 2], windowsHide: true });
  run(["build", "../../experiments/Ranvier.View.Arms.FablePlugin/Ranvier.View.Arms.FablePlugin.fsproj", "-c", "Release", "-v", "q"]);
  run(["fickl", "tests/DomTests.fsproj", "--outDir", generated, "--configuration", "Release", "--noCache", ...defines.flatMap(name => ["--define", name])]);
}

const build = (name, minify) => {
  writeFileSync(new URL(`generated-size/entry-${name}.mjs`, here),
    `export { ${name} as mount } from "../../../../examples/Fable.Ranvier.Playground/${generated}/ArmsSizeEntries.js";\n`);
  execFileSync(process.execPath, [pathOf(new URL("node_modules/vite/bin/vite.js", playground)), "build", "--config", "size.config.mjs"], {
    cwd: pathOf(here), stdio: ["ignore", 2, 2], windowsHide: true, env: { ...process.env, ARMS_SIZE_ENTRY: name, ARMS_SIZE_MINIFY: String(minify) }
  });
  return readFileSync(new URL(`generated-size/${name}${minify ? "" : "-unminified"}/bundle.js`, here));
};
const sizeOf = code => ({
  raw: code.length,
  gzip: gzipSync(code, { level: 9 }).length,
  brotli: brotliCompressSync(code, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length
});

const sizes = Object.fromEntries(["explicitOne", "matchOne", "explicitTwo", "matchTwo", "plain"].map(name => [name, sizeOf(build(name, true))]));
const one = sizes.matchOne.gzip - sizes.explicitOne.gzip;
const perSite = sizes.matchTwo.gzip - sizes.explicitTwo.gzip - one;
// Unminified bundles keep a region comment naming each bundled module's path.
const armsModule = /Ranvier\.View\.Arms\//;
const matchUnminified = build("matchOne", false).toString();
const plainHoldsArms = armsModule.test(build("plain", false).toString());
const quotation = /quotation/i.test(matchUnminified);
// The entry module carries no region of its own: the bundle ends with it, after the Ranvier.View.Arms regions.
const firstArms = matchUnminified.search(/\/\/#region [^\n]*Ranvier\.View\.Arms\//);
if (firstArms < 0) throw Error("The unminified Arms.Match entry holds no Ranvier.View.Arms region");
const cells = { compiledModule: existsSync(new URL(`${generated}/experiments/Ranvier.View.Arms/Cells.js`, playground)), cellsModule: /\/\/#region [^\n]*Ranvier\.View\.Arms\/[^\n]*Cells\.js/.test(matchUnminified), weakMap: /\bWeakMap\b/.test(matchUnminified.slice(firstArms)) };
const helpers = { count: (matchUnminified.slice(firstArms).match(/\b(curry\d*|uncurry\d*|partialApply)\b/g) ?? []).length };

const result = {
  defines,
  sizes,
  one: { deltaGzip: one, budgetGzip: 400, withinBudget: one <= 400 },
  perSite: { deltaGzip: perSite, budgetGzip: 60, withinBudget: perSite <= 60 },
  plain: { matchEntryHoldsArmsModule: armsModule.test(matchUnminified), plainHoldsArmsModule: plainHoldsArms, withinBudget: armsModule.test(matchUnminified) && !plainHoldsArms },
  quotation: { present: quotation, withinBudget: !quotation },
  helpers: { ...helpers, withinBudget: helpers.count === 0 },
  cells: { ...cells, withinBudget: !cells.compiledModule && !cells.cellsModule && !cells.weakMap }
};
mkdirSync(new URL("./", output), { recursive: true });
writeFileSync(output, JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
if (["one", "perSite", "plain", "quotation", "helpers", "cells"].some(key => !result[key].withinBudget)) process.exitCode = 1;
