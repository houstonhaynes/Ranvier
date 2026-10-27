# Build and CI performance implementation plan

**Goal:** Reduce verification latency without moving the repository or removing checks.

**Architecture:** Separate the serial CSS workload from core .NET tests into independent specification/binding, extraction and browser jobs. Batch independent negative CSS consumers while checking the diagnostic for every source file. Skip unrelated database service jobs only on pull requests with explicitly understood leaf changes; master and manual runs retain full verification.

**Baseline:** Full master CI run 40505947132 took 32m11s in `.NET tests`. CSS binding verification took 7m11s, CSS browser compilation 7m44s, package compatibility 5m01s. Packages took 16m09s in parallel. These are observations from one run, not promised savings.

**Constraints:** Keep `CI result` as the required aggregate, include every new job in it, preserve exact-revision package artifacts and publishing trust, retain unknown-path fallback to full checks, and preserve normal/traced and browser coverage. No product API or package version changes.

## Tasks

- [x] Add failing workflow contract tests proving CSS binding, extraction and browser work runs in independent jobs and all checks remain represented in `CI result`.
- [x] Split the jobs while keeping each command and its setup; add npm download caching for the pinned CSS lockfiles and NuGet download caching for the four affected jobs.
- [x] Add failing selection tests for CSS/View-only PRs, adapter-specific PRs, shared build/core changes, unknown files, and full master/manual runs. Implement conservative service-job selection and protect compiled documentation examples from docs-only skipping.
- [x] Batch negative CSS compilation, assert expected diagnostics per file, and execute the real positive/negative/fixture harness once. Record invocation count and elapsed time.
- [ ] Run all workflow-script tests and validate workflow syntax. Review the complete diff independently and open a PR; use actual CI timings for the measured result.

## Review focus

Missing setup after moving steps; result checks that accidentally omit a failing job; a failed compiler process counted as a successful negative case; shared source/toolchain edits incorrectly skipping service tests; documentation examples incorrectly treated as prose; cancellations or failed matrix legs hidden by the aggregate.

## Execution record

The existing feature worktree is clean and reused on `codex/ci-build-performance`. The Anime/CSS release remains independently monitored. Two stuck diagnostic commands were stopped; no stale build processes owned by this task remained. IDE and SageFs processes were preserved.

Verification: 97 workflow/helper tests passed; YAML parsed as 25 jobs; actionlint 1.7.7 passed. The complete real binding harness passed, including corpus serialization, reactive values, all 22 exact per-file rejection diagnostics, obsolete API diagnostics and the generated small fixture. Negative builds decreased from 22 to 1, total .NET launches from 27 to 6. The negative compiler invocation took 3.69 seconds locally; the final warm whole harness took approximately 27 seconds. These local timings are not compared as a percentage to the cold hosted-runner baseline.

Independent review found the import round trip's dependency on the generator DLL, previously built by an earlier serial step. The extraction job now builds that generator explicitly; review reported no remaining actionable findings. Master/manual source runs retain all database services; only pull requests with known independent leaf paths can skip them.

PR CI attempt 1: core .NET 8m29s, CSS bindings 6m49s, CSS extraction 6m32s, CSS browser 6m53s, Packages 16m20s. Overall wall time was 34m00s; Packages queued 17m27s before execution. These measurements show shorter verification jobs, not an established overall speedup under shared-runner queueing.

That run exposed an existing KurrentDB contention failure. The adapter immediately retried up to 32 times while the server's readable head can lag its writer-side revision. A deterministic stale-head regression failed against the old hot loop, then passed with an awaited pause. SnapshotRetry now preserves the 32-attempt limit, fresh read/reapply and CAS append, waiting 1–50ms after revision conflicts only. Regression tests cover waiting for an incomplete pause, retry exhaustion and unrelated failure propagation. The original 16-writer integration case remains enabled. KurrentDB normal and traced suites each passed 30 tests against the pinned disposable server; the package advances to preview.2 for this fix.
