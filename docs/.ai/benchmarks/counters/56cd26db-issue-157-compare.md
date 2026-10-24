# Counter comparison for #157: fdfaa4c3 against 56cd26db

Primitive counters of the reactive core before and after the payload-direct `Arms.Match` lowering
([#157](https://github.com/shayanhabibi/Ranvier/issues/157), plan `docs/superpowers/plans/2026-10-24-issue-157.md`,
Step 12).

- **Before:** `fdfaa4c3`, the tip of `integration/issues` and the base of `issue/157`, measured from a `git archive`
  export of that commit. The export carries no `.git`, so its JSON names the Ranvier version `unknown`:
  [`fdfaa4c3-nopmc.md`](fdfaa4c3-nopmc.md), [`fdfaa4c3-nopmc.json`](fdfaa4c3-nopmc.json).
- **After:** `56cd26db` on `issue/157`: [`56cd26db-nopmc.md`](56cd26db-nopmc.md),
  [`56cd26db-nopmc.json`](56cd26db-nopmc.json).
- Both runs: `counters.ps1 -NoPmc` (five processes, `--scale 1`), under `gate-lock.ps1 bench` on the shared machine,
  .NET 10.0.13, Node v26.7.0, fable-library-js 5.23.0.

**Instructions are not collected.** `-NoPmc` runs without elevation and records allocations and library counters
only; retired instructions need an administrator shell (`counters.ps1` without `-NoPmc`).

## Result

| Runtime | Rows | Library counters identical | Allocation identical |
| --- | ---: | ---: | ---: |
| .NET | 82 | 82 | 82 (bytes and objects per op) |
| Fable / Node.js | 57 | 57 | 56; `app-table` Ranvier 258,203 -> 258,055 B per op (-0.06 %) |

The library counters (memo recomputes, effect runs, flushes and the rest) are identical in every scenario on both
runtimes. #157 changes `experiments/Ranvier.View.Arms`, its Fable plugin and the Playground fixtures; `src/`,
`bench/` and `fable/` are byte-identical between the two commits.
