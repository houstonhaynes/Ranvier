import test from "node:test";
import { cases } from "../generated-tests/experiments/Ranvier.View.Checks/SugarServerChecks.js";

for (const [name, check] of cases) test(name, () => check());
