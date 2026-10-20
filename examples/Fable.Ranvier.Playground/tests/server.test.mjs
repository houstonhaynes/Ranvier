import test from "node:test";
import { cases } from "../generated-tests/experiments/Ranvier.View.Portable/Checks/ServerChecks.js";

for (const [name, check] of cases) test(name, () => check());
