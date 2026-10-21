// Compiles DomTests with the core counters (RanvierCounters=true defines RANVIER_COUNTERS) into generated-tests-counters; --noCache keeps a build without the flag from being reused.
import { execFileSync } from "node:child_process";

execFileSync("dotnet", ["fickl", "tests/DomTests.fsproj", "--outDir", "generated-tests-counters", "--configuration", "Release", "--noCache", "--define", "RANVIER_VIEW_COUNTERS"], {
  stdio: "inherit", windowsHide: true, shell: process.platform === "win32", env: { ...process.env, RanvierCounters: "true" }
});
