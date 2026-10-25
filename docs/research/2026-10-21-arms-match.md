# Arms.Match: measurements

Measurements of the `Arms.Match` implementation ([spec](../superpowers/specs/2026-10-21-arms-match-design.md),
[plan](../superpowers/plans/2026-10-21-arms-match.md)). Fickl 0.3.0 (Fable 5.22), net10.0, Node 26; the Phase 1 discovery ran under Fable 5.18.0 and again under Fickl.

**Summary.** The plugin is found through a project reference and the `.targets`. On .NET a site lowers in about
1.1-1.4 ms once the process is warm; the first five-target site of a process costs 125-146 ms, 9-12 ms after
`Arms.Prepare ()` and about 7 ms with ReadyToRun as well. Under Fable, `Arms.Match` lowers to the payload-direct shape
([#157](https://github.com/shayanhabibi/Ranvier/issues/157)): one `armOf` definition per target over the arm's values
thunk, and no cells. The first site costs 189 B gzip over its twin (budget 400 B) and each further site 8 B (budget
60 B). The retained heap per row is 1.2 % above `ranvier-sugar` after create and after a switch, at 1,000 and 10,000
rows. The Chromium gate is a paired median against the sugared twin of at most 1.05 on create and clear, 1.04 on
switch and 1.07 on payload, under the user's concession of 2026-10-24 ("Chromium"); the cells release ran under a 1.12
allowance (plan Ruling 38, superseded), and its committed timings miss 1.05 on switch (1.075) and payload (1.096).
Pooled over the six committed runs of #157 whose source hashes match the tree and which built their own bundle, at
38-96 % load, the payload-direct shape's paired medians are create 1.006, switch 1.015, payload 1.042 and clear 0.991;
the gate judges these pooled medians and passes, with the pooled switch interval (0.997-1.048) straddling 1.04 and the
pooled payload interval (1.009-1.089) straddling 1.07. In a paired
run against the hand-written shape, the plugin's output is indistinguishable from it on every operation.

## Plugin discovery

From `experiments/Ranvier.View.Arms/Checks/Discovery`, an app with a project reference to `Ranvier.View.Arms` and an
import of `Ranvier.View.Arms.targets`:

- **Discovery through a project reference and the `.targets`:** the plugin lowers the app's `Arms.Match` call; the
  emitted `Main.js` held the `armsValues_L…` prologues of the cells release, and holds the site's `armsTag_L…` tag and
  `armsRead_L…` readers of the payload-direct shape.
- **`FABLE_COMPILER` during project cracking:** set; the `.targets` reference is conditional, and a .NET build carries
  no plugin assembly.
- **netstandard2.0 plugin in Fable 5.18 and Fickl 0.3.0:** loads (Fable.AST 5.0.0 compile reference, FSharp.Core 10.1.203).
- **Server HTML:** the three states render byte-identical HTML on .NET (quotation path) and under Fable (plugin path).
- **Fable.AST in the compilation:** the plugin attribute on `Arms.Match` derives from `MemberDeclarationPluginAttribute`,
  so the compilation that holds `Arms.fs` references `Fable.AST.dll` as well; without it Fable reports FS0074 at the
  attribute and skips the plugin assembly ("`ScanForPluginsAttribute` is required here and is unavailable"). The plugin
  project copies `Fable.AST.dll` beside its own assembly, and the `.targets` references both.
- **References and defines of referenced sources:** Fable compiles `Ranvier.View.Arms` with the app's assembly
  references and the app's `DefineConstants`, not those of `Ranvier.View.Arms.fsproj`. With the `.targets` imported by
  the app alone, `RANVIER_ARMS_PLUGIN` reaches `Arms.fs`; an app without the import (`Tests/NoTargets`) fails at its
  call with FS0101 and the AM005 text.
- **MSBuild targets during cracking:** Fable's project cracking runs a target declared
  `BeforeTargets="ResolveAssemblyReferences"`; the `.targets` check for a missing plugin assembly fires under
  `dotnet fickl`.
- **`dotnet fsi build.fsx -- test` cleans the plugin's `bin` folder** with every other; the plugin is rebuilt after it.

## .NET lowering cost

The site's selector and bodies form one LINQ expression. Its construction runs once in the LINQ interpreter
(`Compile(preferInterpretation = true)`). Each lambda inside it that runs per call (the selector, text and
attribute readers, row readers, event handlers) is a `Hot` function: interpreted for its first 64 calls, then compiled.
Delegates (hooks) and lambdas over lambda-bound variables stay interpreted.

**Cold first site.** `experiments/Ranvier.View.Arms/Checks/DotnetCost` lowers the research's five-target site
(or-pattern, literal pattern, guard) as the first `Arms.Match` of a fresh process, then 200 more, and renders a 1,000-row
site against its `armOf` twin. Three processes per row:

| Mode | First site, fresh process (ms) | First site after the view is warm (`--twin-first`, ms) | Mean of 200 (ms) |
|---|---|---|---|
| interpreted, hot lambdas compiled after 64 calls (shipped) | 162, 147, 133 | 109, 118, 143 | 1.07-1.36 |
| all interpreted | 137, 141, 198 | 109, 105, 110 | 1.01-1.04 |
| all compiled (`preferInterpretation = false`) | 132, 134, 163 | 117, 126, 126 | 9.41-10.11 |
| hot lambdas compiled at lowering | | 126, 132, 122 | 6.09-6.44 |

Building the `armOf` twin first, cold, takes 29-32 ms (the view's own JIT). The rest of the first `Arms.Match` is JIT
of FSharp.Core's quotation deserialiser, `LeafExpressionConverter` and the Arms lowering ("Cold start" below). No mode meets the spec's 10 ms for the first site of a process; the mean meets the 4 ms
budget. The mitigation is the spec's start-up advice: bind sites at module level and render once at start-up, before
the first request. `RepresentationChecks` prints `lowering|warm-first=...` (about 1.5-2.5 ms) for a site lowered after
earlier ones.

**Server render of 1,000 rows** (the rows fixture against its `armOf` twin, one `li` reader per row, mean of 200 renders
after 20 warm-up renders, three rounds per process, three processes):

| Mode | Arms.Match (ms) | twin (ms) |
|---|---|---|
| interpreted, hot lambdas compiled after 64 calls (shipped) | 0.40-0.86 | 0.29-0.77 |
| all interpreted | 0.54-1.42 | 0.21-0.73 |
| all compiled | 0.60-0.77 | 0.52-0.81 |

All-interpreted readers cost about 0.3-0.6 µs more per row reader and render, roughly twice the twin's time for a
reader-heavy page. Compiling each per-call lambda after 64 calls matches the twin once warm, and keeps the lowering near
the all-interpreted cost.

The probe compiled one lambda per body and the selector: 9.06 ms first (warm), 8.55 ms mean.

## Cold start

Measured with `Checks/DotnetCost` (FSharp.Core 10.1.401, the app's pin; the library's floor is 8.0.100), Release,
net10.0, a fresh process per run, under the benchmark lock.

**Where the first site's time goes** (the Phase 3 review's phase profiler, each stage warmed separately, in order;
`System.Runtime.JitInfo` deltas, three processes):

| Stage | Wall (ms) | JIT (ms) | Methods |
|---|---|---|---|
| the view's cold start (an `armOf`/`matchWith` site) | 8.7-8.9 | 8.1-8.4 | 98 |
| quotation deserialisation (a quoted `match` of the same shape) | 30-33 | 26-29 | 382 |
| `LeafExpressionConverter.QuotationToExpression` | 50-54 | 46-49 | 335 |
| `System.Linq.Expressions` interpreter | 3.1-3.5 | 0.4-0.5 | 9 |
| the first `Arms.Match` after those (lowering, `Hot` reflection, `Site.create`, F# collection generics) | 37-40 | 32-33 | 358 |

The cost is JIT of IL-only code: FSharp.Core from NuGet is not ReadyToRun, and the shared framework is, so
`DOTNET_ReadyToRun=0` raises the first site only to 192 ms; tiered-compilation settings change nothing.

**`Arms.Prepare` and ReadyToRun** (`dotnet publish -r win-x64 -p:PublishReadyToRun=true`), three processes each; "first
site" is the five-target site, timed after `Prepare` when it runs:

| Build | `Arms.Prepare ()` (ms) | First site (ms) | First site JIT (ms) |
|---|---|---|---|
| JIT | — | 125-146 | 111-128 |
| JIT, after `Prepare` | 167-182 | 9.2-11.9 | 5.7-7.4 |
| ReadyToRun (first run after publish: 113) | — | 56-57 | 30-31 |
| ReadyToRun, after `Prepare` | 68-75 | 6.9-7.4 | 3.3-3.5 |

`Prepare` moves the cost to start-up, on the calling thread or on the thread pool; ReadyToRun removes about two thirds
of it. With both, the first user site costs about 7 ms, within the draft's 10 ms. The remaining JIT is generic
instantiation over the app's own types, which ReadyToRun cannot pregenerate. Removing the rest needs a build-time
lowering (spec Decision 15).

**Hot compiles.** A per-call lambda queues its compilation on the thread pool at its 64th call and keeps running
interpreted until the compiled delegate is published, so no render waits for a compile (the review measured an inline
compile at 8.2-9.1 ms for the first of a process). `Prepare` compiles one lambda, so the pool's first compile starts warm.
A lambda over `let`-bound values of its case body (`let suffix = "!" in pre () { fun _ -> t + suffix }`) is built by
a factory over those values: the factory runs interpreted once per site construction, and the first of its `Hot`
functions to reach 64 calls compiles it once. A failed compile leaves the function interpreted and writes a `Trace`
warning.

## Fickl

Master replaced `dotnet fable` (Fable 5.18.0) with `dotnet fickl` (Fickl 0.3.0, which reports Fable 5.22.0) in #152.
Its tool folder holds the same `Fable.AST` 5.1.0 and FSharp.Core 10.1.203, and the plugin loads unchanged: the
discovery app renders the same HTML on both targets, and the plugin tests pass with the same diagnostics, ranges and
emitted sites.

## Bundle

`experiments/Ranvier.View.Arms/Checks/size.mjs` compiles the Playground's DomTests in Release with no defines into
`generated-size-tests`, then bundles each entry with Vite 8.3.4 (minified), gzip level 9 and Brotli quality 11; the
numbers are in [`arms-match/bundle.json`](arms-match/bundle.json).

| Entry | raw | gzip | Brotli |
|---|---|---|---|
| explicit loader | 206,936 | 60,178 | 52,828 |
| Arms.Match loader | 207,413 | 60,367 | 52,995 |
| two explicit sites | 207,778 | 60,355 | 52,968 |
| two Arms.Match sites | 208,279 | 60,552 | 53,165 |

With the payload-direct lowering the first site costs 189 B gzip over its twin (budget 400 B) and the second 8 B
(budget 60 B), measured after the rebase onto `integration/issues` (183 B and 1 B before it, within the method's
resolution). The method is a whole-bundle gzip difference, so it resolves about ±20 B: a build with
`RANVIER_VIEW_COUNTERS` moves the first figure by 19 B and the second by 25 B without touching `Ranvier.View.Arms`. An
entry that calls no `Arms.Match` holds no `Ranvier.View.Arms` module; the `Arms.Match` entry holds no quotation code,
no curry helper, no cells module and no `WeakMap`, and the Fable output of `Ranvier.View.Arms` holds no `Cells` module.
`Arms.Prepare ()` in the entry adds no bytes.

The 189 B are the definitions array and its selection, the AM006 guard (`Site.values`, a tag at element 1 of each
values array) with its Release text, and the splice adapter. The cells release cost 374 B.

How the first-site cost moved:

| Change | First site, gzip |
|---|---|
| F# classes over fable-library arrays (first cells) | 771 |
| cells as JavaScript objects in a `WeakMap`, `Site.create` inline | 460 |
| a target's single live entry held without an array (Task 10, for the heap) | 496 |
| AM006 as a code and docs anchor in Release; no run-time AM005 branch; `enter` and `values` inlined into `Arms.Match` as closures; one `serves` closure; unchecked indexing | 370 |
| live entries linked through the entries; the withdrawn entry reused by the next arm of its target; one withdrawal per entry (for the switch); the short text without the URL scheme | 394 |
| after #155; entries seeded with their mount-time values and refreshed on withdrawal (final review I1); withdrawn entries kept in the chain and rebound in place | 371 |
| after #151 updates the locked bundler; unchanged Arms sources and size budgets | 374 |
| payload-direct lowering (#157): one `armOf` per target, the selector's array, the AM006 tag guard, the splice adapter; the Fable cells removed | 183 |

## Chromium

The gate (spec, Testing 11) is a paired median of at most the operation's budget, the 95 % interval reported:
`node experiments/Ranvier.View.Arms/Checks/chromium-gate.mjs` pools the committed runs whose recorded source hashes
match the tree. The
budgets are create 1.05, switch 1.04, payload 1.07 and clear 1.05; the cells release ran under a 1.12 allowance (plan
Ruling 38), which the payload-direct lowering supersedes.

**Concession (user ruling, 2026-10-24).** The payload-direct lowering targeted 1.05 on every operation. Create and
clear keep it. Switch takes 1.04 and payload 1.07 (plan `2026-10-24-issue-157.md`, Decision P9), fixed and not
refitted when a run joins the pool, because the plugin's output is
indistinguishable from the hand-written shape the spike measured ("Against the hand-written shape"). Pooling
concatenates each run's per-pair ratios and applies the bootstrap of `keyed-rows.mjs`;
`experiments/Ranvier.View.Arms/Checks/pooled-interval.mjs` prints the result, `chromium-gate.mjs` judges each
operation by the pooled median, and `pooled-interval.test.mjs` checks the fixed budgets and the pooled medians against
them. The pool is every committed run whose recorded hashes match every source `run-sources.mjs` lists, up to line
endings: the Arms.Match lowering (`experiments/Ranvier.View.Arms` and the Fable plugin project with each file it
compiles), the keyed-rows benchmark with its page and build config, and the keyed-rows fixtures. The core, the view
libraries and the Playground lock file are not hashed (user decision, 2026-10-25); a run is compared on the listed
paths alone. A change to any listed path takes every run out of the pool, and the gate fails until runs are recorded
on the new sources. A pooled run builds its own bundle (`--build`) into `generated-keyed-pooled/<label>` under a lock per
checkout, and records `built` and the served bundle's `bundleHash`; `keyed-rows.mjs` refuses the write when the bundle
changed during the run. CI runs the gate only for a change to the lowering, its benchmark, its runs or the workflow
(`changes.cjs`, `armsRuns`). The six runs at `22d86d44` and the six at `2dc2835b` are the pool; each is the Step 11
command with `--build` under `gate-lock.ps1 bench`, two at a time, and the six runs at each commit record the same
`bundleHash`. CPU load before / after in parentheses:

| Run (CPU load, %) | create | switch | payload | clear |
|---|---|---|---|---|
| `arms-match-22d86d44-1` (38, 83, 96 / 54, 43, 72) | 0.957 (0.902-1.010) | 0.975 (0.950-1.027) | 1.040 (0.979-1.121) | 1.000 (0.864-1.116) |
| `arms-match-22d86d44-2` (43, 84, 96 / 96, 93, 79) | 1.087 (1.005-1.121) | 1.015 (0.948-1.141) | 1.074 (1.009-1.146) | 0.985 (0.853-1.010) |
| `arms-match-22d86d44-3` (44, 57, 79 / 73, 85, 64) | 0.999 (0.923-1.050) | 1.050 (0.967-1.100) | 1.149 (1.028-1.263) | 0.964 (0.890-1.059) |
| `arms-match-22d86d44-4` (52, 59, 68 / 57, 82, 76) | 1.024 (0.989-1.057) | 1.003 (0.951-1.048) | 0.994 (0.946-1.084) | 0.955 (0.869-1.030) |
| `arms-match-22d86d44-5` (71, 88, 59 / 66, 71, 63) | 1.002 (0.952-1.053) | 1.018 (0.981-1.077) | 1.013 (0.939-1.128) | 1.035 (0.893-1.156) |
| `arms-match-22d86d44-6` (65, 64, 59 / 81, 68, 68) | 0.977 (0.917-1.041) | 1.051 (0.997-1.101) | 1.017 (0.929-1.144) | 1.015 (0.922-1.107) |
| `arms-match-2dc2835b-1` (54, 48, 38 / 45, 51, 65) | 1.009 (0.979-1.036) | 1.005 (0.969-1.026) | 1.094 (0.979-1.158) | 0.970 (0.880-1.064) |
| `arms-match-2dc2835b-2` (54, 48, 37 / 77, 26, 44) | 0.983 (0.962-1.004) | 1.016 (0.991-1.047) | 1.024 (0.967-1.088) | 1.004 (0.891-1.133) |
| `arms-match-2dc2835b-3` (57, 20, 34 / 46, 46, 62) | 1.014 (0.971-1.057) | 0.975 (0.937-1.041) | 0.955 (0.895-1.090) | 1.119 (0.931-1.192) |
| `arms-match-2dc2835b-4` (48, 32, 58 / 42, 35, 29) | 0.992 (0.975-1.018) | 1.012 (0.981-1.041) | 1.033 (0.972-1.095) | 0.983 (0.902-1.096) |
| `arms-match-2dc2835b-5` (33, 46, 23 / 35, 54, 38) | 0.993 (0.975-1.043) | 1.014 (0.977-1.049) | 0.970 (0.892-1.011) | 0.863 (0.797-1.132) |
| `arms-match-2dc2835b-6` (40, 51, 63 / 18, 12, 28) | 1.046 (1.022-1.058) | 0.998 (0.965-1.014) | 1.048 (0.987-1.096) | 1.040 (0.913-1.141) |
| pooled, 540 pairs | 1.006 (0.993-1.018) | 1.011 (0.999-1.022) | 1.033 (1.010-1.060) | 0.991 (0.957-1.020) |
| budget | 1.05 | 1.04 | 1.07 | 1.05 |

Every pooled median is within its budget, and every pooled interval lies below it. The runs' switch medians spread
from 0.975 to 1.051 and their payload medians from 0.955 to 1.149.

**The `da6d32e6` runs, out of the pool.** The five runs at `da6d32e6`, on the branch rebased onto `integration/issues`
at `aae54f2e`, stay committed as history. They predate the `--build` requirement: only the first built its bundle,
the other four served the shared output, which a concurrent run could rebuild, so a source match cannot show which lowering
they measured. Their hashes differ from the tree in `keyed-rows.mjs`.

| Run (CPU load, %) | create | switch | payload | clear |
|---|---|---|---|---|
| `arms-match-da6d32e6-1` (62, 25, 59 / 47, 79, 56) | 1.022 (0.999-1.045) | 1.048 (1.018-1.101) | 1.050 (0.953-1.134) | 1.088 (0.921-1.164) |
| `arms-match-da6d32e6-2` (59, 37, 55 / 58, 66, 79) | 0.977 (0.957-0.995) | 1.027 (1.008-1.058) | 1.025 (0.972-1.211) | 1.000 (0.874-1.240) |
| `arms-match-da6d32e6-3` (60, 69, 65 / 65, 68, 59) | 1.019 (0.982-1.036) | 0.995 (0.951-1.027) | 1.006 (0.890-1.110) | 0.985 (0.878-1.107) |
| `arms-match-da6d32e6-4` (68, 60, 71 / 83, 68, 60) | 1.006 (0.995-1.025) | 0.983 (0.955-1.011) | 0.904 (0.870-1.087) | 1.026 (0.956-1.114) |
| `arms-match-da6d32e6-5` (51, 66, 68 / 69, 54, 76) | 0.983 (0.951-1.024) | 1.015 (0.965-1.061) | 0.995 (0.925-1.057) | 0.996 (0.875-1.162) |
| pooled, 225 pairs | 1.000 (0.993-1.016) | 1.015 (1.004-1.030) | 1.009 (0.974-1.053) | 1.003 (0.963-1.074) |

**Earlier runs, out of the pool.** The runs below stay committed as history. Their recorded hashes differ from the
tree in `src/Ranvier/Core.fs` and `src/Ranvier/Projections.fs`, which both modes compile, in
`benchmarks/frameworks/keyed-rows.mjs`, and they record no hash for the plugin project, `Shape.fs` or `Plugin.fs`;
`8ee10018`, `299ab276` and `fccc740c` also differ in `Site.fs` and `Lower.fs`.

| Run (CPU load) | create | switch | payload | clear |
|---|---|---|---|---|
| `8ee10018`, probe (96-100 %) | 1.002 (0.977-1.030) | 1.014 (0.967-1.038) | 1.065 (0.985-1.168) | 1.016 (0.897-1.108) |
| `299ab276`, Step 11 (85-100 %) | 1.003 (0.969-1.063) | 1.078 (1.026-1.137) | 1.073 (0.930-1.222) | 1.011 (0.910-1.146) |
| `fccc740c`, shape run (97-100 %) | 1.041 (0.969-1.068) | 0.993 (0.903-1.066) | 1.042 (0.906-1.193) | 1.014 (0.968-1.095) |
| `dc8b3d5b`, refresh (35-68 %) | 1.013 (0.989-1.039) | 1.009 (0.990-1.027) | 1.000 (0.899-1.055) | 0.942 (0.843-1.111) |
| `5369d6a0`, rebased onto `0967cc02` (70-97 %) | 0.992 (0.958-1.045) | 1.022 (0.945-1.066) | 1.040 (0.948-1.108) | 1.119 (0.991-1.175) |
| `4d00179e`, rebased onto `0967cc02` (56-99 %) | 1.004 (0.962-1.055) | 0.969 (0.917-1.035) | 0.971 (0.943-1.053) | 0.996 (0.906-1.093) |

**The `4d00179e` run.** `arms-match.json` is the Step 11 command with `--build` through
`gate-lock.ps1 bench` at `4d00179e`, CPU load 56, 79 and 88 % before the build and 62, 66 and 99 % after the run,
source hashes matching the tree of `4d00179e` (`Lower.fs` `9c7c6613…`, `Site.fs` `644adbf6…`, `Core.fs` `f2023ae6…`);
`keyed-rows.mjs` writes a run into `docs/research/arms-match/` only from committed sources unchanged during the run. Every median of the run is
within its budget; its switch and payload intervals lie below theirs, and its create and clear intervals straddle 1.05.

| Operation | ranvier-sugar median (process medians), ms | ranvier-arms-match median (process medians), ms | paired median ratio | 95 % interval | arms slower in |
|---|---|---|---|---|---|
| armsCreate1k | 16.85 (19.17, 16.76, 16.47) | 17.09 (19.11, 15.66, 17.08) | 1.004 | 0.962-1.055 | 24/45 |
| armsSwitch1k | 9.88 (9.88, 9.56, 10.81) | 9.63 (9.54, 9.00, 10.95) | 0.969 | 0.917-1.035 | 20/45 |
| armsPayload1k | 1.97 (2.21, 1.88, 1.97) | 1.88 (2.00, 1.75, 1.82) | 0.971 | 0.943-1.053 | 20/45 |
| armsClear1k | 4.73 (4.47, 4.40, 5.84) | 4.74 (3.98, 3.91, 6.22) | 0.996 | 0.906-1.093 | 22/45 |

**Payload-direct shape.** `keyed-rows.mjs --bundle --modes=ranvier-sugar,ranvier-arms-match
--ops=armsCreate1k,armsSwitch1k,armsPayload1k,armsClear1k --processes=3 --rounds=15
--pairs=ranvier-arms-match:ranvier-sugar` under `gate-lock.ps1 bench`, at `299ab276` (clean apart from the untracked
probe below), Node v26.7.0, Chromium 156.0.8078.4, Fickl 0.5.3, Fable 5.23.0, Solid 2.0.0-rc.14; results and source
hashes in `arms-match/arms-match-299ab276.json` (`Lower.fs` `51311ec0…`, `Site.fs` `5ce539ae…`). The machine is shared with up to three lanes, and its
load is the baseline (plan `2026-10-24-issue-157.md`, Decision P7): CPU load read 96, 85 and 91 % before the run and 87,
90 and 100 % after it. Each ratio pairs samples of the same process and round.

| Operation | ranvier-sugar median (process medians), ms | ranvier-arms-match median (process medians), ms | paired median ratio | 95 % interval | arms slower in |
|---|---|---|---|---|---|
| armsCreate1k | 17.78 (16.20, 19.56, 17.95) | 18.52 (16.84, 19.93, 17.24) | 1.003 | 0.969-1.063, straddles | 23/45 |
| armsSwitch1k | 9.56 (10.00, 9.86, 8.93) | 10.52 (10.41, 10.62, 10.22) | 1.078 | 1.026-1.137, straddles | 31/45 |
| armsPayload1k | 1.69 (1.41, 2.17, 1.76) | 1.91 (1.32, 2.36, 1.91) | 1.073 | 0.930-1.222, straddles | 27/45 |
| armsClear1k | 4.29 (3.38, 4.38, 4.55) | 4.42 (3.76, 4.42, 4.77) | 1.011 | 0.910-1.146, straddles | 23/45 |

Every interval straddles 1.05: none clearly meets the budget and none clearly misses it. The medians of create and
clear are within it; those of switch and payload are above it, so the uniform 1.05 gate of that time failed on them.
An earlier probe of the same sources at `8ee10018`, under 96-100 % load (`docs/.ai/benchmarks/arms-157-noisy-probe.json`,
superseded by this run), measured create 1.002 (0.977-1.030),
switch 1.014 (0.967-1.038), payload 1.065 (0.985-1.168) and clear 1.016 (0.897-1.108): its switch interval lies below
1.05 and its payload interval straddles it. Across the two runs only payload's median stays above 1.05; at this load the
paired interval is too wide to separate a 5 % budget from no difference.

**Against the hand-written shape.** A second run of the same sources (source hashes equal to `arms-match-299ab276.json`'s) at
`fccc740c` adds the spike's hand-written payload-direct fixture, `ranvier-sugar-array`, to the same processes and
rounds: `keyed-rows.mjs --build --modes=ranvier-sugar,ranvier-arms-match,ranvier-sugar-array
--ops=armsCreate1k,armsSwitch1k,armsPayload1k,armsClear1k --processes=3 --rounds=15
--pairs=ranvier-arms-match:ranvier-sugar,ranvier-sugar-array:ranvier-sugar,ranvier-arms-match:ranvier-sugar-array`
under `gate-lock.ps1 bench`, CPU load 100 % before and 97 % after (one reading each). Results in
`arms-match/arms-match-shape.json`; `arms-match.json` remains the gate input.

| Operation | arms-match : sugar (95 %) | sugar-array : sugar (95 %) | arms-match : sugar-array (95 %) |
|---|---|---|---|
| armsCreate1k | 1.041 (0.969-1.068) | 0.964 (0.917-1.021) | 1.028 (0.975-1.041) |
| armsSwitch1k | 0.993 (0.903-1.066) | 0.989 (0.896-1.115) | 0.955 (0.862-1.060) |
| armsPayload1k | 1.042 (0.906-1.193) | 1.075 (0.905-1.198) | 1.026 (0.924-1.152) |
| armsClear1k | 1.014 (0.968-1.095) | 1.009 (0.919-1.207) | 0.989 (0.852-1.074) |

Every arms-match : sugar-array interval contains 1: on this machine the plugin's output is indistinguishable from the
hand-written shape the spike measured, which itself reads 1.075 on payload in this run. Pooling the arms-match : sugar
pairs of both runs (90 per operation, the same bootstrap) gives create 1.016 (0.981-1.059), switch 1.052
(0.993-1.091), payload 1.061 (0.945-1.170) and clear 1.012 (0.950-1.088). The pooled switch and payload medians stay
above 1.05; this finding is the reason for the concession above.

**Cells release (history).** At `2667e8e5`, before #151 updated the locked bundler (Fickl 0.3.0, Fable 5.22.0; source
hashes in the JSON). The run started after three load readings below 20 % (25, 8, 3, 3 %); the load read 53 % right after the smoke build and 97 % at the
end, and the third process ran under other work (its sugar switch median is 10.97 ms against 7.38-7.57 ms in the other
two).

| Operation | ranvier-sugar median (process medians), ms | ranvier-arms-match median (process medians), ms | paired median ratio | 95 % interval | arms slower in |
|---|---|---|---|---|---|
| armsCreate1k | 12.04 (11.74, 14.34, 11.64) | 12.60 (12.27, 15.12, 12.09) | 1.049 | 1.034-1.064 | 37/45 |
| armsSwitch1k | 7.63 (7.57, 7.38, 10.97) | 8.13 (8.10, 7.95, 12.17) | 1.075 | 1.048-1.102 | 38/45 |
| armsPayload1k | 1.10 (1.08, 0.88, 2.20) | 1.16 (1.12, 1.01, 2.32) | 1.096 | 1.060-1.144 | 31/45 |
| armsClear1k | 2.40 (2.32, 1.96, 4.90) | 2.38 (2.38, 1.74, 5.28) | 1.041 | 0.978-1.130 | 26/45 |

Switch and payload miss 1.05. The quiet spike run below (load 7 % and 3 %) agrees: create 1.063, switch 1.106,
payload 1.075 and clear 1.059. Earlier runs, all under 31-77 % load: before the entry reuse (`945dd4ca`) switch
1.144 (1.111-1.176) and payload 1.112; after it, switch 1.122 (1.103-1.145) and payload 1.126 (1.071-1.201).

The cells release's cost is per remount and per read: the selector's two arrays per selection and the arm's
`(instance, values)` props; each reader's `values` lookup (a `WeakMap` lookup, the graph tag and the chain); each
arm's entry registration and cleanup. The payload-direct lowering replaces them with one array per selection and a
read of the arm's own values thunk; the spike of that shape measured create 1.019, switch 1.031, payload 1.007 and
clear 1.026.

## Heap

`keyed-heap.mjs --modes=ranvier-sugar,ranvier-arms-match --fixture=arms --counts=1000,10000`, after create and, with
`--after=switch`, after one switch of every row; two processes, under the exclusive gate lock, at `28c69104` with the
payload-direct lowering (CPU load 32 % before, 16 % after; `28c69104` predates the rebase onto `0967cc02`); results in `arms-match/arms-match-heap.json` and
`arms-match/arms-match-heap-switch.json`. `chromium-gate.mjs` checks the after-create figure against 10 % and prints the
after-switch figure.

| Rows | ranvier-sugar bytes per row | ranvier-arms-match after create | Δ | ranvier-arms-match after a switch | Δ |
|---|---|---|---|---|---|
| 1,000 | 3,511 | 3,553 | +42 (+1.2 %) | 3,553 | +42 (+1.2 %) |
| 10,000 | 3,528 | 3,570 | +42 (+1.2 %) | 3,570 | +42 (+1.2 %) |

Both processes agree to the byte. A switched row retains its new arm only, as the twin does; the spike's array shape
measured +38 B after a switch. The cells release retained 3,839 and 3,869 B per row after create (+9.3 % and +9.7 %)
and 3,959 and 3,989 after a switch (+12.8 % and +13.1 %): each row's withdrawn entry with its last values.

## Payload-direct spike

A one-day spike of the redesign the final review estimated: each target an `armOf` definition whose instance is the
arm's values thunk, the site selecting with one array `[target, pattern variables..., matched value, instance]`, and
readers reading `p ()[k]`. No plugin work: `tests/KeyedRowsSugarArrayPerf.fs` writes that shape by hand as the
benchmark mode `ranvier-sugar-array`, and `ArmsSizeEntries.arrayOne` writes the loader that way for the bundle. The
shipped design is unchanged.

**Chromium.** `keyed-rows.mjs --modes=ranvier-sugar,ranvier-arms-match,ranvier-sugar-array
--ops=armsCreate1k,armsSwitch1k,armsPayload1k,armsClear1k --processes=3 --rounds=15
--pairs=ranvier-arms-match:ranvier-sugar,ranvier-sugar-array:ranvier-sugar,ranvier-arms-match:ranvier-sugar-array`, at
`53fe1d35` plus the spike files, under the locks, after three load readings below 20 %: the CPU load was 7 % at the
start and 3 % at the end, the first quiet run of this work. Results in `arms-match/arms-spike.json`.

| Operation | sugar median, ms | arms-match | sugar-array | arms-match : sugar (95 %) | sugar-array : sugar (95 %) | arms-match : sugar-array (95 %) |
|---|---|---|---|---|---|---|
| armsCreate1k | 10.82 | 11.53 | 10.93 | 1.063 (1.049-1.081) | 1.019 (1.003-1.030) | 1.046 (1.032-1.069) |
| armsSwitch1k | 5.47 | 6.13 | 5.66 | 1.106 (1.063-1.131) | 1.031 (0.993-1.048) | 1.083 (1.072-1.096) |
| armsPayload1k | 0.76 | 0.83 | 0.78 | 1.075 (1.053-1.103) | 1.007 (1.000-1.033) | 1.055 (1.024-1.090) |
| armsClear1k | 1.47 | 1.55 | 1.47 | 1.059 (0.988-1.175) | 1.026 (0.944-1.190) | 1.087 (1.016-1.127) |

On the quiet machine today's `Arms.Match` misses the 1.05 gate on create, switch and payload, and clear is not settled.
The payload-direct shape holds it on every operation: its switch is 3.1 % over the twin (interval 0.99-1.05) and the
rest at most 2.6 %. The difference is the cells: today's site is 8.3 % slower than the array shape on switch and 5.5 %
on payload.

**Heap after a switch** (`keyed-heap.mjs --fixture=arms --after=switch`, `arms-match/arms-spike-heap-switch.json`): the twin 3,511
and 3,528 B per row at 1,000 and 10,000 rows; the array shape 3,549 and 3,566 (+38 B, +1.1 %); `Arms.Match` 3,959 and
3,989 (+448 and +461 B, +12.8 % and +13.1 %).

**Bundle.** The array-shape loader costs 31 B gzip over its explicit twin (`Arms.Match`: 371 B), with no AM006 guard
and no splice adapter in it; the review estimates 15-25 B for the guard and 30-50 B for an adapter.

**Helper splices** (spec, "helper views typed `TemplateView<'inst>` work unchanged inside a body"). In the redesign a
body's instance is the values thunk, so a helper view of the enclosing instance type needs an adapter: an inlined child
(`render adapter (fun p -> p ()[n])`) whose instance is the enclosing instance again. A .NET probe
(`render` of an adapter `Component.define (fun _ s -> s) (fun _ -> helper)` against the helper as the arm's whole view)
gives the same structural hash (`5f86489d`) and the same server HTML (`arms-match/splice-probe.fsx`), because an inlined child's markup joins the
template's skeleton. A generic helper over a reader (the `helper` fixture's `labelled`) needs no adapter: its reader is
rewritten like any other.
