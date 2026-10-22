# Arms.Match: measurements

Measurements of the `Arms.Match` implementation ([spec](../superpowers/specs/2026-10-21-arms-match-design.md),
[plan](../superpowers/plans/2026-10-21-arms-match.md)). Fickl 0.3.0 (Fable 5.22), net10.0, Node 26; the Phase 1 discovery ran under Fable 5.18.0 and again under Fickl.

**Summary.** The plugin is found through a project reference and the `.targets`. On .NET a site lowers in about
1.1-1.4 ms once the process is warm; the first five-target site of a process costs 125-146 ms, 9-12 ms after
`Arms.Prepare ()` and about 7 ms with ReadyToRun as well. Under Fable the first `Arms.Match` site costs 374 B gzip over
its twin (budget 400 B) and each further site 10 B (budget 60 B). The retained heap per row is within 10 % of
`ranvier-sugar` after create and about 13 % above it after a switch. In Chromium every arm operation holds this
release's gate, a paired median of at most 1.12 against the sugared twin (create 1.049, switch 1.075, payload 1.096,
clear 1.041); the payload-direct follow-up measured 1.007-1.031 in a spike.

The user explicitly accepted the 1.12 allowance for this release on 2026-10-22, requiring the cost to remain visible
and a tracked follow-up. Measured overhead is +4.9 % create, +7.5 % switch, +9.6 % payload and +4.1 % clear.
The latest switch and payload medians miss the original 1.05 gate; a passing release check does not erase that
shortfall. The payload-direct follow-up targets the original gate. Merge also awaits the requested primitive
instruction-count comparison. The performance work is tracked in [issue #157](https://github.com/shayanhabibi/Ranvier/issues/157).

These Chromium medians are the recorded pre-#151 measurements, with their original source hashes and tool versions
in `arms-match.json`; they were not remeasured during finalization. Integrating #151 updates Vite from 8.3.3 to 8.3.4
and the Anime size checks' Rolldown from 1.2.12 to 1.2.14. Regenerated bundle records use the new locked tool versions.
The committed-results timing check continues to validate historical evidence; it does not measure the new bundle.

## Plugin discovery

From `experiments/Ranvier.View.Arms/Checks/Discovery`, an app with a project reference to `Ranvier.View.Arms` and an
import of `Ranvier.View.Arms.targets`:

- **Discovery through a project reference and the `.targets`:** the plugin lowers the app's `Arms.Match` call; the
  emitted `Main.js` holds the `armsValues_L…` prologues.
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
`generated-size-tests`, then bundles each entry with Vite 8.3.3 (minified), gzip level 9 and Brotli quality 11; the
numbers are in [`arms-match/bundle.json`](arms-match/bundle.json).

| Entry | raw | gzip | Brotli |
|---|---|---|---|
| explicit loader | 206,791 | 60,147 | 52,825 |
| Arms.Match loader | 207,533 | 60,518 | 53,136 |
| two explicit sites | 207,634 | 60,337 | 52,937 |
| two Arms.Match sites | 208,378 | 60,720 | 53,306 |

The first site costs 374 B gzip over its twin (budget 400 B) and the second 10 B (budget 60 B), after the view's
graduation to `src/Ranvier.View` (#155). The method is a
whole-bundle gzip difference, so it resolves about ±20 B: a build with `RANVIER_VIEW_COUNTERS` moves the first figure by
19 B and the second by 25 B without touching `Ranvier.View.Arms`. An entry that calls no `Arms.Match` holds no
`Ranvier.View.Arms` module, the `Arms.Match` entry holds no quotation code and no curry helper, and `Arms.Prepare ()`
in the entry adds no bytes.

How the first-site cost came down from 771 B:

| Change | First site, gzip |
|---|---|
| F# classes over fable-library arrays (first cells) | 771 |
| cells as JavaScript objects in a `WeakMap`, `Site.create` inline | 460 |
| a target's single live entry held without an array (Task 10, for the heap) | 496 |
| AM006 as a code and docs anchor in Release; no run-time AM005 branch; `enter` and `values` inlined into `Arms.Match` as closures; one `serves` closure; unchecked indexing | 370 |
| live entries linked through the entries; the withdrawn entry reused by the next arm of its target; one withdrawal per entry (for the switch); the short text without the URL scheme | 394 |
| after #155; entries seeded with their mount-time values and refreshed on withdrawal (final review I1); withdrawn entries kept in the chain and rebound in place | 371 |
| after #151 updates the locked bundler; unchanged Arms sources and size budgets | 374 |

The remaining cost is the cells' code: entry registration, rebinding and withdrawal, the read over live and withdrawn
entries with graph tags, and the site's arm table. Dropping graph tags under Fable would save about 36 B and let a
reader under one graph read another graph's arm.

## Chromium

`keyed-rows.mjs --modes=ranvier-sugar,ranvier-arms-match --ops=armsCreate1k,armsSwitch1k,armsPayload1k,armsClear1k
--processes=3 --rounds=15 --pairs=ranvier-arms-match:ranvier-sugar`, under the benchmark and browser locks, at `2667e8e5`
(Fickl 0.3.0, Fable 5.22.0; source hashes in the JSON); results in `arms-match/arms-match.json`. The run started after
three load readings below 20 % (25, 8, 3, 3 %); the load read 53 % right after the smoke build and 97 % at the end, and
the third process ran under other work (its sugar switch median is 10.97 ms against 7.38-7.57 ms in the other two).

The gate for this release (spec, Testing 11; plan Ruling 38) is a paired median of at most 1.12 per operation, the
95 % interval reported; `node experiments/Ranvier.View.Arms/Checks/chromium-gate.mjs` checks the committed results.

| Operation | ranvier-sugar median (process medians), ms | ranvier-arms-match median (process medians), ms | paired median ratio | 95 % interval | arms slower in |
|---|---|---|---|---|---|
| armsCreate1k | 12.04 (11.74, 14.34, 11.64) | 12.60 (12.27, 15.12, 12.09) | 1.049 | 1.034-1.064 | 37/45 |
| armsSwitch1k | 7.63 (7.57, 7.38, 10.97) | 8.13 (8.10, 7.95, 12.17) | 1.075 | 1.048-1.102 | 38/45 |
| armsPayload1k | 1.10 (1.08, 0.88, 2.20) | 1.16 (1.12, 1.01, 2.32) | 1.096 | 1.060-1.144 | 31/45 |
| armsClear1k | 2.40 (2.32, 1.96, 4.90) | 2.38 (2.38, 1.74, 5.28) | 1.041 | 0.978-1.130 | 26/45 |

Every operation holds the 1.12 gate. The quiet spike run below (load 7 % and 3 %) agrees: create 1.063, switch 1.106,
payload 1.075 and clear 1.059. Earlier runs, all under 31-77 % load: before the entry reuse (`945dd4ca`) switch
1.144 (1.111-1.176) and payload 1.112; after it, switch 1.122 (1.103-1.145) and payload 1.126 (1.071-1.201).

The remaining cost is per remount and per read: the selector's two arrays per selection and the arm's
`(instance, values)` props; each reader's `values` lookup (a `WeakMap` lookup, the graph tag and the chain); each
arm's entry registration and cleanup. The payload-direct lowering (spec, Phased implementation 8) removes them.

## Heap

`keyed-heap.mjs --modes=ranvier-sugar,ranvier-arms-match --fixture=arms --counts=1000,10000`, after create and, with
`--after=switch`, after one switch of every row; results in `arms-match/arms-match-heap.json` and
`arms-match/arms-match-heap-switch.json`.

| Rows | ranvier-sugar bytes per row | ranvier-arms-match after create | Δ | ranvier-arms-match after a switch | Δ |
|---|---|---|---|---|---|
| 1,000 | 3,511 | 3,839 | +328 (+9.3 %) | 3,959 | +448 (+12.8 %) |
| 10,000 | 3,528 | 3,869 | +341 (+9.7 %) | 3,989 | +461 (+13.1 %) |

After create the gate holds: both counts stay within 10 % of `ranvier-sugar`. After a switch each row keeps its
withdrawn entry with its last values in the target's chain, where the next arm of the target rebinds it; the entry
releases the disposed arm's props. The sugared twin keeps nothing after a switch.

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
