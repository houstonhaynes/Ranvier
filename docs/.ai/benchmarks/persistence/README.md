# Persistence counter bench (#210, criterion 1)

Base: `origin/integration/issues` at 201ed534. Head: `issue/210` with the `persist-write` row (step 13).
Machine: the shared lane machine, Windows 11, not elevated, so no processor counters: instruction counts come
from CI's arm64 perf leg. Allocations come from the counter-bench workers; timings from a Stopwatch probe
(`timing.template.fsx`) over the same `Scenarios.all` cases. Both sides were built untraced, without
`RanvierCounters`, and run with tiering, PGO and ReadyToRun off.

`paired.ps1` ran five rounds under `gate-lock.ps1 bench`, each round the base then the head: the full worker
(every scenario, allocations) and the timing probe (create, update, chain, cutoff, update-on, derive-on,
mvu-dispatch, project-edit, persist-write). `analyse.fsx` reports the per-round head/base ratio: median and range.
CPU load (Win32_Processor, all cores): 25% before, 6-93% between runs, 63% after.

## Results

- **Untraced IL of `src/Ranvier` unchanged.** `ildiff.fsx` compares every method's name, signature and IL of the
  base and head `Ranvier.dll` (net10.0, untraced), with each `ldstr` resolved and the source root stripped: 2,385
  methods, 0 differ. The only metadata difference is the user string holding the build's source path, which shifts
  later `ldstr` tokens.
- **No-persistence rows: allocations unchanged.** Every base row allocates exactly the same bytes per operation on
  the head: paired ratio 1.000, range 1.000..1.000, in all 82 rows (`paired-results.md`).
- **No-persistence rows: timings unchanged within noise.** Paired medians 0.79..1.08; every range straddles 1.0.
  The library assembly is identical, so the spread is the busy machine.
- **Per-write cost of a persisted signal** (`persist-write`, a `MemoryStateStore`, one write per flush): 800 bytes
  per write against 0 for the plain signal row; 518 ns against 44 ns median, a per-round ratio of 7.0..20.9 (median
  10.7) within the head process. The bytes are the encoded payload, the `StoredRecord`, the record list and the
  save-chain task.

Instructions per operation for the no-persistence rows are confirmed by the arm64 perf gate on PR #214.
