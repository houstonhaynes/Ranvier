# CI instruction counters: a pull-request gate and a recorded history

Date: 2026-10-16. Branch `feat/ci-perf-counters`, from `master` at `2ce4b7d`.

## Problem

The counter bench (`bench/Ranvier.Counters`, `fable/Ranvier.Counters`, `counters.ps1`) reads processor counters through ETW, so it runs only on an elevated Windows x64 machine with hardware counters. Every report so far was taken by hand. Regressions such as the two in #58 (keyed-remove with fresh items +104%, app-detail +100% under Node after #57) were found only because a run happened to follow the merge.

## Goal

1. Every pull request against `master` is measured against its merge base. A pull request fails when a scenario's instruction count rises past the threshold of [Verdict](#verdict), unless the regression is accepted.
2. Every push to `master` records its figures, so the timeline of instruction counts per scenario builds itself.
3. The figures predict the direction and rough size of a change on users' x64 machines; the local `counters.ps1` on x64 stays the reference for absolute numbers.

## Runner capabilities

Measured on 2026-10-16 with throwaway workflows on branch `spike/ci-pmc-probe` (runs [38841846197](https://github.com/shayanhabibi/Ranvier/actions/runs/38841846197) and [38842531656](https://github.com/shayanhabibi/Ranvier/actions/runs/38842531656)):

| Runner | CPU | Hardware counters |
|---|---|---|
| `ubuntu-latest` | AMD EPYC 7763, Hyper-V guest | none: no `cpu` PMU, `perf stat` reports `<not supported>` |
| `windows-latest` | AMD EPYC 7763, Hyper-V guest, administrator | `Timer` only; `counters.ps1` fails with "Profile source 'InstructionRetired' is not available here" |
| `ubuntu-24.04-arm` | Neoverse-N2 | user-mode instructions, cycles and branch misses through `perf_event_open` |

Valgrind (Cachegrind) under Node differed by 4–27% between repeats with the JIT, and by under 0.5% with `--jitless`, which measures the interpreter instead of the code users run. It is not used.

The stability probe ran the Fable harness under Node in 10 processes per runner, alternating sets A and B at one commit, on three runners of each architecture, 41 scenarios. The A/B difference of the medians:

| Leg | Metric | Median | 90th percentile | Worst |
|---|---|---|---|---|
| arm64 | instructions | 0.02% | 0.18–0.52% | 1.9–13.4% |
| arm64 | cycles | 1.2–1.5% | 4.0–5.0% | 8–19% |
| arm64 | thread CPU time | 1.1–1.7% | 4.1–5.9% | 10–17% |
| x64 | thread CPU time | 2.8–4.4% | 7–16% | 30–2,581% |
| x64 | wall time | 2.5–4.6% | 9–16% | 24–8,197% |
| both | bytes | 0% | 0% | 0% (one x64 runner: 341%) |

The medians of the three arm64 runners agree within 0.02% (median) and 0.42% (90th percentile) across scenarios, so arm64 figures from different machines are comparable. Four scenarios are noisy in instructions on arm64: async-recover (about 300 instructions per operation, ±10%), dispose-on (±3–6%), keyed-remove and keyed-swap with stable items (±2–4%). Each job took about 3 minutes including the Fable build.

The .NET worker was not probed; it marks regions with ETW and has no Linux path yet.

## Design

### Legs

| Leg | Runner | Measures | Role |
|---|---|---|---|
| `arm64-counters` | `ubuntu-24.04-arm` | instructions, cycles and branch misses per region, user mode, measured thread only; bytes; library counters | gate on instructions |
| `x64-timing` | `ubuntu-latest` | thread CPU time per region; bytes; library counters | report; warning annotation only |

Both legs measure the Node harness and the .NET worker, and both run in parallel in one workflow.

### Rounds

Each leg builds the merge base and the head, then runs base and head alternately: five processes each, in the order B H B H B H B H B H, for Node and then for .NET. A scenario's figure for a commit is the median of its five processes, each process figure being `(m(2N) - m(N)) / N` as today. Scenarios present in only one commit are reported as added or removed and never fail.

### Verdict

A scenario regresses on a leg's gating metric when both hold:

1. the head median exceeds the base median by more than **max(2%, 20 instructions per operation)**;
2. **every** head process figure exceeds **every** base process figure (complete separation; with five against five, chance gives this at p < 0.01).

An improvement is the mirror image and is listed, never failing. A region with a garbage collection is measured as is and flagged in the report.

The `arm64-counters` check fails when any scenario regresses in instructions on Node or .NET and the regression is not accepted. The `x64-timing` leg applies the same separation rule to thread CPU time with a threshold of 15% and emits a warning annotation; it never fails.

### Accepting a regression

A regression is accepted by either:

- the `perf-accepted` label on the pull request, which accepts every scenario; or
- a `Perf-Accepted` trailer in any commit message between the merge base and the head:

  ```
  Perf-Accepted: app-detail, keyed-remove — the detail now disposes rows eagerly
  ```

  The value is a comma-separated list of scenario names, or `all`, then ` — ` or ` -- ` and the reason. A name matches a scenario name exactly; a name that matches no scenario of the base or head fails the check with that name, so a typo cannot pass silently.

An accepted regression still appears in the report, with what accepted it and the reason. With squash merges the trailer reaches `master`, and the recorded run carries it.

### Report

One pull-request comment, created on the first run and updated on later runs. It holds the verdict and the override that applied; a table of regressions, improvements and library-counter changes; and the full table per leg and runtime, collapsed. Library counters are exact, so any change in them is listed in its own section: it usually signals a behaviour change.

### Workflows

- **`perf.yml`**, on `pull_request` (types `opened`, `synchronize`, `reopened`, `labeled`, `unlabeled`) and on `push` to `master`, with `contents: read` and `actions: read` (to reuse an earlier run's artifact).
  - The two legs measure and upload their figures as an artifact.
  - A `verdict` job reads the figures, the label and the trailers, and sets the check. On `labeled` and `unlabeled` it reuses the figures of the latest run for the same head commit instead of measuring again; when none exists it measures.
  - On `push` to `master` the legs measure the pushed commit against its first parent, so the recorded run also carries the delta.
- **`perf-report.yml`**, on `workflow_run` of `perf.yml`, with `pull-requests: write` and `contents: write`. It executes no code from the pull request: it posts or updates the comment from the artifact, and on a `master` run it commits the run to the `perf-data` branch.

### History

An orphan branch `perf-data` holds:

- `runs/<sha>.json`: per scenario, leg and runtime, the five process figures and the median for each metric; the runner CPU model, the Node, .NET, Fable and fable-library versions; and the accepted regressions with their reasons.
- `timeline.md`: regenerated on each commit, one table per scenario of instructions by commit, in the shape of the 2026-10-16 timeline in `docs/.ai/benchmarks/counters`.

### Components

The bench tool and its scripts stay where they are. The Python driver of the probe is not kept.

- **`bench/Ranvier.Counters/LinuxPmc.fs` (new).** Opens user-mode `PERF_COUNT_HW_INSTRUCTIONS`, `PERF_COUNT_HW_CPU_CYCLES` and `PERF_COUNT_HW_BRANCH_MISSES` counters for one thread with `perf_event_open` (P/Invoke through `syscall`), with reset, enable, disable and read. It also reads a thread's CPU time from `/proc/<pid>/task/<tid>/schedstat`. When the hardware events are unavailable, as on x64 runners, it reports so and the leg falls back to CPU time.
- **Node on Linux.** `NodeRuns.runHandshake`'s `mark` callback opens the counters on the Node main thread (its thread id is the process id) and resets and enables them on `BEGIN`, then disables and reads them on `END`, before replying. The harness is unchanged.
- **.NET on Linux.** `Worker.fs` opens the counters on its own thread and reads them around each region where it now writes the ETW `Begin` and `End` markers. Both paths produce the existing `Pmc.RegionCounts`, so `Report.fs` and the reports are unchanged.
- **Backend choice.** ETW on Windows, perf on Linux, by OS. `counters.ps1` keeps its behaviour on Windows.
- **`ab` subcommand (new).** Takes the base and head build folders (each with its compiled Fable harness and .NET worker) and writes one JSON of process figures for both commits in the round order above. The head's tool drives both commits.
- **`compare` subcommand (new).** Applies [Verdict](#verdict) and [Accepting a regression](#accepting-a-regression) to the `ab` output, writes `verdict.json` and the Markdown comment, and exits 1 on a regression that is not accepted. Takes `--accept <names|all>`, `--accept-reason`, `--threshold`, `--floor`.
- **`timeline` subcommand (new).** Rebuilds `timeline.md` from `runs/*.json`.
- **`build.fsx` drives the CI steps.** New Partas.Build commands `perf-build <ref> <dir>` (worktree, Fable build, .NET worker build), `perf-ab`, `perf-compare` (collects the label and the trailers and calls `compare`) and `perf-record` (writes `runs/<sha>.json` and calls `timeline`). The workflows call `dotnet fsi build.fsx -- <command>`. In GitHub Actions the commands write the step summary and annotations through Partas.Build 0.8.1.

### Bootstrap

Until the merge base contains the Linux backend and the `ab` subcommand, the pull request that adds them included, the check reports "no baseline" and passes. The first push to `master` after the merge records the first `perf-data` run.

## Tests

`bench/Ranvier.Counters.Tests` (new, Expecto):

- the threshold and the absolute floor at their edges;
- complete separation against one overlapping process, in both directions;
- added and removed scenarios;
- label against trailer, `all`, several trailers in several commits, and a trailer naming no scenario;
- `timeline` over runs with a missing scenario.

`LinuxPmc` runs only on Linux: the `arm64-counters` job asserts that every measured region has non-zero instructions, and the PR that adds the backend records one `ab` run at a single commit (base = head) whose verdict must be clean.

## Out of scope

- Reducing the noise of async-recover, dispose-on and the stable-item keyed-remove and keyed-swap scenarios.
- Gating on x64.
- Charts on the documentation site.
- Absolute comparison between arm64 CI figures and x64 local figures.

## Prerequisite

The user subsequently included the prerequisite upgrade in this work. `build.fsx` now uses Partas.Build 0.8.1 and Partas.Build.Baked 0.3.0, with the public Cloudsmith feed in root `nuget.config`, also included in the solution's `/.environment/` folder. Cold-cache bootstrap and execution under redirected output have been verified. The `perf-*` commands use the package's automatic GitHub Actions stage summaries and warning annotations; detailed verdicts remain in report artifacts and comments. No direct `GITHUB_STEP_SUMMARY` or `::warning::` fallback is used.

The privileged `workflow_run` reporter must exist on the default branch before GitHub triggers it. Measurement and artifact-reuse checks can run on this PR; live comment and history checks follow the user's merge. An existing PR is reused rather than creating another one.
