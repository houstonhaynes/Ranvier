// Prints, for each Arms.Match prologue call (armsValues_L<line>C<column>(instance, target)) of an emitted file, the
// site's ordinal in the file, the target and the F# source line the source map gives it:
// "S<ordinal> <target> <line>". PluginTests.fsx compares the listing with expected/prologues.txt. Exits 1 when the file
// holds no prologue.
// Usage: node sourcemap.mjs <emitted .js>   (the .js.map beside the .js; Fable --sourceMaps writes it)
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";

const require = createRequire(new URL("../../../examples/Fable.Ranvier.Playground/package.json", import.meta.url));
const { TraceMap, originalPositionFor } = require("@jridgewell/trace-mapping");
const [emitted] = process.argv.slice(2);
const map = new TraceMap(readFileSync(emitted + ".map", "utf8"));
const ordinals = new Map();
const prologue = /armsValues_(L\d+C\d+)\([^,()]+, (\d+)\)/g;
const listing = [];

readFileSync(emitted, "utf8").split("\n").forEach((text, index) => {
  for (const found of text.matchAll(prologue)) {
    if (!ordinals.has(found[1])) ordinals.set(found[1], ordinals.size + 1);
    const original = originalPositionFor(map, { line: index + 1, column: found.index });
    listing.push(`S${ordinals.get(found[1])} ${found[2]} ${original.line}`);
  }
});

console.log(listing.join("\n"));
if (listing.length === 0) process.exit(1);
