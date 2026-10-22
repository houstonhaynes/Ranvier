import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { cases, goldenLines } from "../generated-tests/experiments/Ranvier.View.Arms/Checks/ArmsServerChecks.js";

for (const [name, check] of cases) test(name, () => check());

test("the Fable server HTML of every fixture equals the golden file", () => {
  const golden = new URL("../../../experiments/Ranvier.View.Arms/Checks/arms-server.golden.txt", import.meta.url);
  const committed = readFileSync(golden, "utf8").replace(/\r\n/g, "\n").replace(/\n+$/, "").split("\n");
  assert.deepEqual([...goldenLines()], committed);
});
