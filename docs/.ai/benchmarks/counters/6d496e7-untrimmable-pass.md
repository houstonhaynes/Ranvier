# Counter bench, untrimmable keyed pass (against 86d812f)

- Date: 2026-10-15
- Machine: AMD64 Family 26 Model 68 Stepping 0, AuthenticAMD, 24 logical processors, Microsoft Windows 10.0.26200, .NET 10.0.13, Node.js v26.7.0
- Change: `9edfe21`, which is `6d496e7` (perf(core): take the full visit when a keyed pass cannot trim) with the trimmed pass's changed positions read by index
- Base: `86d812f`, which is `bef1521` (master) with only the keyed-pass scenarios added; the engine is identical to `bef1521`
- Command, run from each worktree: `./counters.ps1 -Repeat 5 -Scale 8 -Only keyed-`, elevated, untraced
- Scale 8: N = 400 operations, 2N = 800
- Order: base, change, base, change. Each figure is the mean of the two rounds' medians; each round's median is over 5 processes
- Node.js instructions and cycles are main-thread figures
- Every figure is (m(2N) - m(N)) / N. Differences under 5 % are within run-to-run noise. Cycles vary more than instructions between runs
- `*` in bytes/op: a collection ran inside the region, so the figure understates allocation

## Scenarios

A projection of 1,000 rows keyed by `Id`, mapped to `Value`, with one effect per row and one effect on `Keys`. An operation writes the next list; operations alternate between the edited list and the initial list.

- `keyed-remove`: remove the middle row, then restore it
- `keyed-swap`: swap the second and second-to-last rows, then swap them back
- `keyed-insert`: prepend a row with a new key, then remove it

**fresh items**: every write holds new item records (`{ item with Id = item.Id }`), so under the default `===` policy every position differs from the previous pass. **stable items**: every write is a new array of the initial records, so the prefix and suffix trim.

## Results

| Runtime | Scenario | Items | instr/op base | instr/op change | ratio | cycles/op base | cycles/op change | ratio | br-miss/op base | br-miss/op change | bytes/op base | bytes/op change |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| .NET | keyed-remove | fresh | 1,936,756 | 1,561,578 | 0.81 | 635,730 | 548,005 | 0.86 | 91.8 | 69.6 | 8,516 | 8,516 |
| .NET | keyed-remove | stable | 69,014 | 69,066 | 1.00 | 21,168 | 20,455 | 0.97 | 6.7 | 7.4 | 8,516 | 8,516 |
| .NET | keyed-swap | fresh | 1,979,397 | 1,610,796 | 0.81 | 659,280 | 577,266 | 0.88 | 67.5 | 85.0 | 8,216 | 8,216 |
| .NET | keyed-swap | stable | 71,448 | 66,753 | 0.93 | 21,993 | 20,075 | 0.91 | 6.9 | 5.9 | 8,216 | 8,216 |
| .NET | keyed-insert | fresh | 1,938,449 | 1,562,647 | 0.81 | 646,720 | 557,807 | 0.86 | 83.8 | 70.3 | 8,524 | 8,524 |
| .NET | keyed-insert | stable | 79,244 | 78,496 | 0.99 | 23,694 | 22,456 | 0.95 | 7.0 | 6.2 | 8,524 | 8,524 |
| Node | keyed-remove | fresh | 5,196,670 | 3,615,105 | 0.70 | 1,347,784 | 992,836 | 0.74 | 3,426.2 | 458.8 | 154,914* | 6,230* |
| Node | keyed-remove | stable | 512,890 | 417,087 | 0.81 | 252,016 | 169,291 | 0.67 | 2,785.9 | 1,593.7 | 19,122 | 18,473 |
| Node | keyed-swap | fresh | 5,382,639 | 3,738,697 | 0.69 | 1,408,234 | 975,545 | 0.69 | 3,721.2 | 833.6 | 82,599* | 34,851* |
| Node | keyed-swap | stable | 178,192 | 169,580 | 0.95 | 30,454 | 47,290 | 1.55 | 6.6 | 117.9 | 19,243 | 18,597 |
| Node | keyed-insert | fresh | 5,190,894 | 3,643,788 | 0.70 | 1,343,223 | 927,005 | 0.69 | 3,458.3 | 661.4 | 154,520* | 6,236* |
| Node | keyed-insert | stable | 294,092 | 221,003 | 0.75 | 147,610 | 48,053 | 0.33 | 1,484.4 | 56.9 | 19,122 | 18,473 |

## Reading

- Fresh items: instructions fall by 19 % on .NET and by 30-31 % on Node.js. The pass runs the full visit and skips the trim's per-position hash-set bookkeeping.
- Stable items, swap: instructions fall by about 6 % on both runtimes (.NET 0.93, Node.js 0.95). This fits the aligned compare loop running outside the `try`/`finally`, without the per-iteration `not aligned` test.
- Stable items, remove and insert: .NET is unchanged. Node.js reads 0.75-0.81, and those figures still include warm-up compiles. V8 compiles and deoptimises on the measured main thread under `--single-threaded`, and m(2N) - 2 m(N) in these regions is +24..+104 M instructions on the base and about -77 M (remove) and -5 M (insert) on the change. Read these rows as "no regression", not as a measured speed-up.
- Node.js figures at small scale include warm-up compiles: at the default scale (N = 50) the stable swap read 0.25x, an artefact of the transient landing in the 2N region of the base only.
- Library counters (MemoRecomputes, EffectRuns, edges) are identical between base and change in every scenario.

The tool's JSON output for round 1: [change](6d496e7-untrimmable-pass.json), [base](6d496e7-untrimmable-pass-base.json).
