// Writes ecma-numbers.txt: one finite double per line as `<IEEE 754 bits, hex>\t<JSON.stringify text>`.
// Deterministic: fixed boundary values, then 2000 xorshift64 bit patterns.
//
//   node tests/Ranvier.Wire.Tests/ecma-numbers.mjs
import { writeFileSync } from "node:fs";
import { join } from "node:path";

const view = new DataView(new ArrayBuffer(8));
const bitsOf = value => { view.setFloat64(0, value); return view.getBigUint64(0).toString(16).padStart(16, "0"); };
const boundaries = [
    0.1, 1 / 3, 1e21, 1e20, 1.5e-7, 1e-6, 1e-7, 5e-324, 2.2250738585072014e-308, 1.7976931348623157e308,
    123456789012345680000, 0.30000000000000004, 100, 1e15, 1e16, 9007199254740993, -1.5, 4, 2.5, -0.000001,
    1.234e-7, 1.2345e-7, 999999999999999900000, 1e300, -2e-300, 1.25, 4294967296.5,
];
const lines = boundaries.map(value => bitsOf(value) + "\t" + JSON.stringify(value));
const mask = (1n << 64n) - 1n;
let state = 0x9E3779B97F4A7C15n;
while (lines.length < boundaries.length + 2000) {
    state ^= (state << 13n) & mask;
    state ^= state >> 7n;
    state ^= (state << 17n) & mask;
    view.setBigUint64(0, state);
    const value = view.getFloat64(0);
    if (Number.isFinite(value) && value !== 0) lines.push(state.toString(16).padStart(16, "0") + "\t" + JSON.stringify(value));
}
writeFileSync(join(import.meta.dirname, "ecma-numbers.txt"), lines.join("\n") + "\n");
