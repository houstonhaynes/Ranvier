# Ranvier analyzers: build-time diagnostics for traps that surface late

> **Status 2026-10-21:** approved, with Decisions 8-17. Written against master at `bcdb561` (#139); citations refreshed
> at `5afc604` (#140). No implementation. Every SDK, CLI and
> Fable fact below was read from the published packages or measured in a scratch probe; the probes are in
> `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\{fableprobe,fcsprobe,anaprobe}`, and phase 0 copies them into
> `docs/research/analyzers/`.

Date: 2026-10-21. Inputs: `src/Ranvier/{Api,Core,Types,Platform}.fs`, `experiments/Ranvier.View.Portable/{Template,
TemplateSugar,Server,Representation}.fs`, `experiments/Ranvier.View.Foundation/Hydration.fs`,
`docs/content/guide/troubleshooting.md`, `docs/content/view/troubleshooting.md`, `docs/content/view/collections.md`,
PR #138 (`2cf84be`), the arms research note (`origin/research/idiomatic-arms`,
`docs/research/2026-10-21-idiomatic-arms.md`), `.github/workflows/ci.yml`, and the FSharp.Analyzers.SDK 0.41.0
documentation and sources.

## Core and view state

**Core primitives used:** none at run time. The analyzers read the F# typed tree of the analyzed project and
recognise core and view symbols by full name.

**View-owned state:** none. The analyzer package ships no runtime code.

## Goal

An opt-in package, `Ranvier.Analyzers`, reports Ranvier traps as F# analyzer diagnostics. Each trap reported today by
a runtime exception, a wrong emitted JavaScript file or a failed Fable precompile becomes a `warning RANnnn` or
`error RANnnn` line in `dotnet build` output, in the `dotnet fsharp-analyzers` CLI and in a SARIF report, at the source
range of the cause. Agents and CI read it from the build; IDEs that host the SDK show it inline. Each diagnostic links
to a docs page with the cause, the fix and a before/after example.

## Non-goals

- **Diagnostics the F# compiler already gives.** The sugar's FS0001/FS0041/FS0193 errors stay where they are
  (`docs/content/view/troubleshooting.md:9-31`).
- **Interprocedural or data-flow proofs.** A rule inspects one expression tree and its lexical lambdas. A trap reached
  only through a helper function in another file is a false negative, not a target.
- **Fable-mode type checking.** Analyzers see the .NET typed tree: code under `#if FABLE_COMPILER` is invisible
  (for example `TemplateSugar.fs:23-24`).
- **Replacing runtime checks.** Every runtime check the library has stays; a rule moves its first report earlier.
- **A Roslyn analyzer for C#.** `Ranvier.CSharp` callers are out of scope.
- **Formatting and style rules.** Every rule names a behaviour the program gets wrong.

## Decisions

1. **FSharp.Analyzers.SDK 0.41.0, CLI `fsharp-analyzers` 0.41.0.** 0.41.0 (2026-10-16) depends on
   FSharp.Compiler.Service `[43.12.402]` and FSharp.Core `[10.1.402]`, targets `net10.0`, and is the first release with
   `--fail-on-type-errors` and per-analyzer crash isolation; wildcard `--treat-as-*` arrived in 0.39.0. *The CI gate
   needs all three.* The repo's SDK (`global.json` 10.0.100, roll-forward to the installed 10.0.402) matches.
2. **One package, two projects under `tools/`.** `tools/Ranvier.Analyzers` holds every rule; `tools/Ranvier.Analyzers.Tests`
   holds the Expecto suite. *A rule is a pure function from a typed tree to messages, and one assembly keeps the
   symbol tables in one place.*
3. **One `[<CliAnalyzer>]` and one `[<EditorAnalyzer>]` entry per rule.** `Message` carries no help link; the
   `HelpUri` of the analyzer attribute does. *One entry per rule gives each code its own SARIF rule and docs link.*
4. **One shared traversal per file.** A memoised index (a `ConditionalWeakTable` keyed on the
   `FSharpImplementationFileContents`) records, in one walk, each call with its lambda-context stack. Every rule reads
   the index. *Eighteen walks of `Template.fs` (2,784 lines) would dominate the budget.*
5. **Rules gate on references.** A rule set runs only when the project's `-r:` options name `Ranvier.dll`,
   `Ranvier.View.Portable.dll` or `Fable.Core.dll`. *A project without Ranvier pays one list scan.*
6. **Diagnostics surface through MSBuild.** The package's `build/` targets set `RunAnalyzersDuringBuild` and
   `FSharpAnalyzersContinueOnError=false` through FSharp.Analyzers.Build 0.5.0. *The probe shows CLI output in the
   canonical `file(line,col): warning CODE: text` form becomes MSBuild warnings, and `ContinueOnError=true` (the
   FSharp.Analyzers.Build default) demotes `error RANnnn` to warnings and passes the build.*
7. **In-repo first.** The view rules target `experiments/` projects that are not packed (`IsPackable=false`,
   `Ranvier.View.Portable.fsproj:5`). The package is published once the core rules are stable (Decision 13).
8. **SDK 0.41.0 for the CLI and the build.** The gate needs `--fail-on-type-errors` and crash isolation, both 0.41.0.
   The Ionide-loadable build is added once FsAutoComplete moves to SDK 0.41; the package never ships two builds,
   because the CLI fails on the mismatched one.
9. **RAN002 covers every union declared in the analyzed assembly, at Warning.** `dotnet fable precompile` (Fable
   5.18.0) on a fixture library fails with `MethodDefNotFound` for a tester of a public, an internal and a private
   non-generic union. Testers of generic unions, struct or reference, precompile; RAN002 still reports them, since
   the `match` costs nothing and a later Fable can change the generic case. The fixture is RAN002's truth test. Since #152 the
   repository compiles with Fickl 0.3.0 (Fable 5.22), whose precompile declares the testers of every shape; the truth
   test pins the Fable 5.18.0 failure through `dotnet dnx fable@5.18.0` and records Fickl's acceptance. Fable 5.19-5.21 fail
   too; 5.22 declares the testers. Every Ranvier project references Fable.Core, so a Fable.Core reference does not
   show that Fable compiles the project. RAN001 and RAN002 run only on a positive signal: a tool manifest (searched
   upward to the first root manifest) that declares `fable` or `fickl`, a `Fable.*` package reference in the project
   file, or `RanvierFableAnalyzers=true`, which the package's build targets turn into the `RANVIER_FABLE_ANALYZERS`
   compile symbol for global, `--tool-path` and `dotnet dnx` installs. A .NET-only project gets neither rule. RAN001,
   whose inlining Fable 5.18-5.22 and Fickl 0.3.0 all perform, runs on any signal. RAN002 also needs a declared `fable`
   before 5.22, or no declared compiler.
10. **Repository builds do not run the analyzers by default.** `RanvierAnalyze=true`, `build.fsx analyze` and the CI
    job are the entry points.
11. **The Fable-only projects are not analyzed in phases 0-2.** Their shared sources are analyzed through
    `tests/Ranvier.Tests`. `--property DefineConstants=FABLE_COMPILER` is revisited once a probe shows the project
    type-checks that way.
12. **Rule pages live in `docs/content/analyzers/`,** one page per code, so the `HelpUri` stays stable when guide
    pages move.
13. **The package is published after phase 1,** with the Fable and core rules. The view rules ship in the same
    package once the view is packed; until then they gate on `Ranvier.View.Portable.dll` and run in the repository
    only.
14. **RAN024 shares the library's nesting rules.** `ContentModel` (`Template.fs:650-1081`) moves to its own file in
    `Ranvier.View.Portable`, and the analyzer compile-links that file, so one implementation decides both the server
    refusal and the diagnostic.
15. **RAN020 sites 4-5 report at Warning,** like sites 1-3. A suppression comment records an intended snapshot.
16. **`FSharpAnalyzersAlwaysRunAfterBuild` stays unset.** Agents run `build.fsx analyze` for a complete report; a
    no-op build matches the compiler, which re-emits nothing.
17. **RAN030-RAN032 land with the `Arms.Match` runtime,** in its PR, with the probe's failure-mode table as fixtures.

## Platform facts (verified)

| Fact | Evidence |
|---|---|
| SDK 0.41.0: `net10.0`, FCS `[43.12.402]`, FSharp.Core `[10.1.402]` | nuget.org nuspec |
| The CLI loads only analyzers built against its own SDK major.minor; a mismatched assembly is skipped and the run exits non-zero | SDK docs, "Upgrading Analyzers" |
| FsAutoComplete 0.85.4 (Ionide) locks FSharp.Analyzers.SDK 0.37.2 (FCS 43.12.201, `net8.0`) | FsAutoComplete `paket.lock` on `main` |
| An analyzer built against 0.41 does not load in Ionide until FsAutoComplete moves to 0.41 | the two rows above |
| Rider and Visual Studio host no FSharp.Analyzers.SDK analyzers; Rider's F# analysis is its own FCS integration | JetBrains Rider F# inspection docs; no host in `JetBrains/resharper-fsharp` |
| `dotnet fsharp-analyzers` under SDK 11.0.100-rc.1 fails project loading with `Could not load ... System.Runtime, Version=11.0.0.0` | probe without a `global.json` |
| `RunAnalyzersDuringBuild=true` runs the CLI after `CoreCompile` and reuses `FscCommandLineArgs` through an `.rsp` file; it is skipped when `CoreCompile` is up to date unless `FSharpAnalyzersAlwaysRunAfterBuild=true` | FSharp.Analyzers.Build 0.5.0 targets; probe |
| CLI wall time on `src/Ranvier/Ranvier.fsproj` with one rule: 8.0 s; on `Ranvier.View.Portable.fsproj`: 13.9 s (its `dotnet build`: 14.5 s) | probe |
| `src/Ranvier` references Fable.Core 5.3.0 on every target, so each project that references Ranvier has `Fable.Core.dll` in its `-r:` options, so RAN001-RAN002 gate on a Fable signal (Decision 9) | `Ranvier.fsproj:41` |
| `dotnet fable precompile` (Fable 5.18.0) fails with `MethodDefNotFound` on a tester of a non-generic union of any accessibility and succeeds on generic unions | fixture probe, Decision 9 |
| `fable/Ranvier.Tests.Fable` does not type-check as a .NET project (FS0740 in shared test sources); `--fail-on-type-errors` exits non-zero | probe |

## Rules

Numbering: `RAN001-009` Fable compilation hazards, `RAN010-019` core, `RAN020-029` view, `RAN030-039` `Arms.Match`.
"Reactive read" below means a call of `Value`, `TryValue` or `Status` on `Ranvier.Signal<'T>` (`Core.fs:2912`),
`AsyncSource<'T>` (`Core.fs:3026`), `Memo<'T>` (`Core.fs:3151`), `AsyncMemo<'T>` (`Core.fs:4294`),
`Boundary<'T>` (`Core.fs:5085`) or `Editable<'T>` (`Api.fs:77`), or of `Get`, `TryGet` or `Keys` on a projection or
lookup. `Peek` (`Core.fs:3010`) is an untracked read and never counts.

### Summary

| Code | Title | Default severity | Scope | Fix | Phase |
|---|---|---|---|---|---|
| RAN001 | Fable inlines a snapshot of a module `mutable` | Warning | `Fable.Core` referenced and a Fable signal (Decision 9) | yes | 0 |
| RAN002 | Generated union case tester in a Fable library | Warning | `Fable.Core` referenced, a Fable signal (Decision 9), union in the analyzed assembly | yes | 0 |
| RAN010 | Owned node created in a pure derivation | Error | `Ranvier` referenced | partial | 1 |
| RAN011 | Async value created and read in the owner that replaces it | Warning | `Ranvier` | no | 1 |
| RAN012 | Reactive read after the first await of an async memo | Warning | `Ranvier` | no | 1 |
| RAN013 | Signal write or `Pump` on a thread-pool callback | Warning | `Ranvier` | no | 1 |
| RAN015 | `CancelPrevious` with a body that ignores its token | Hint | `Ranvier` | no | 3 |
| RAN020 | Reactive read at template build time | Warning | `Ranvier.View.Portable` | partial | 2 |
| RAN021 | Unstable list key | Warning / Info | view | no | 2 |
| RAN022 | Definition created on every region read | Warning | view | no | 2 |
| RAN023 | Invalid content parts on an element | Error | view | no | 2 |
| RAN024 | Markup the server render refuses | Warning | view | no | 2 |
| RAN025 | `hydrateWith` with queued commits | Error | view | no | 2 |
| RAN030 | `Arms.Match` pattern variable outside a reader | Error | `Arms.Match` present | no | 3 |
| RAN031 | `Arms.Match` arms not a function literal | Error | `Arms.Match` present | no | 3 |
| RAN032 | `Arms.Match` in a Fable project without the plugin | Warning | `Arms.Match` present | no | 3 |

### RAN001: Fable inlines a snapshot of a module `mutable`

**Trap.** Fable 5.18 Release replaces a single-use immutable `let` whose right-hand side is a bare read of a
module-level `let mutable` declared in another file with a second read at the use site. The snapshot then sees writes
made between the binding and the use. The playground works around it by hand
(`examples/Fable.Ranvier.Playground/tests/ElmishNestedChecks.fs:29`, reading `Loops.childSetups` declared at
`ElmishNested.fs:31`).

**Probe (Fable 5.18.0, `-c Release --noCache`).** `let setups = Loops.childSetups; Loops.setup (); printfn "%d"
(Loops.childSetups - setups)` emits `setup(); const arg = (childSetups() - childSetups()) | 0;`.

| Shape | Inlined |
|---|---|
| bare read of a `let mutable` in another file, one use | **yes** |
| the same, use inside a closure (`fun () -> Loops.childSetups - setups`) | **yes**, the read moves into the closure |
| tuple pattern `let a, b = Loops.childSetups, 1`, one use of `a` | **yes** |
| `let mutable setups = ...` | no |
| mutable in the same file, also in a nested module | no |
| two uses of the binding | no |
| wrapped read (`box ...`, `... + 0`) | no |
| mutable record field `Loops.state.Count`, static property over `static let mutable` | no |
| `-c Debug` | no |

**Detection.** In the typed tree a module-mutable read is `Call (None, v, [], [], [])` with `v.IsMutable` and
`v.IsModuleValueOrMember` (fcsprobe). Flag `Let ((x, rhs, _), body)` where `x` is immutable, `rhs` is that read (or a
`NewTuple` element bound by a tuple pattern), `v.DeclarationLocation.FileName` differs from the binding's file, and
`x` occurs exactly once in `body` (counting occurrences under lambdas).

**False-positive risk.** Low for correctness: the flagged binding compiles to a read at the use, so a write between
the two changes the value. A binding with no intervening write is harmless and still flagged; the fix costs nothing.

**Message.** ``Fable Release compiles `setups` as a second read of `Loops.childSetups` at its single use (line 42), so
writes between the two lines change it. Declare it `let mutable setups` to keep the snapshot.``

**Fix.** Insert `mutable ` before the binding's identifier. No fix for a tuple pattern.

**Tests.** Positive: the playground shape across two files; the closure shape; the tuple shape. Negative: same-file
mutable; two uses; `let mutable`; wrapped read; record field; a non-Fable project (no `Fable.Core`).

### RAN002: Generated union case tester in a Fable library

**Trap.** F# generates `.IsCase` properties for union cases. The docs site's Fable precompile of the library failed
with `MethodDefNotFound` for `Ranvier.FlightPolicy::get_IsQueue`, because the precompiled assembly does not declare
the generated testers (PR #138, `2cf84be`, `Core.fs:4545` and `:4653`).

**Detection (verified).** A generated tester is a `Call` of a property getter whose declaring entity is an F# union,
whose attributes include `System.Runtime.CompilerServices.CompilerGeneratedAttribute`, and whose
`DeclarationLocation` equals its union case's range. A user-written `member this.IsX` carries no such attribute, and
`Option.IsSome` (`prim-types.fsi`) carries none (fcsprobe). The probe analyzer, run through the CLI on master's
`src/Ranvier`, reports two remaining uses on the internal struct union `Platform.FlightOutcome` (`Platform.fs:67`):
`Core.fs:4506` (`IsCompleted`) and `Core.fs:4563` (`IsCanceled`). On `fable/Ranvier.Tests.Fable` it reports
`tests/Ranvier.Tests/Tracing.fs:1707` and `Streams.fs:369`, both on unions of the referenced library.

**Scope.** Unions declared in the analyzed assembly, in projects that reference `Fable.Core`. Tester uses on unions of
referenced assemblies are out of scope: they compile against the referenced assembly, and the precompile failure
concerned the library's own members.

**False-positive risk.** Low: the replacement `match` is equivalent and allocation-free. The precompile breaks on
internal and private unions too (Decision 9).

**Message.** ```policy.IsQueue` calls a compiler-generated union tester, which a Fable precompiled assembly does not
declare. Write `(match policy with FlightPolicy.Queue -> true | _ -> false)`.``

**Fix.** Replace the receiver-and-property range with `(match <receiver text> with <Union full name>.<Case> _ -> true
| _ -> false)`; a case without fields omits the `_`.

**Tests.** Positive: own union, case with and without fields, struct union, generic union. Negative: user-written
`IsX` member; `Option.IsSome`; tester of a union from a referenced assembly; project without `Fable.Core`.

### RAN010: Owned node created in a pure derivation

**Trap.** A memo, effect, async value, boundary, root, projection, lookup, selector or `onCleanup` created inside
`createMemo`'s body fails every read of the memo with `InvalidOperationException` (`Core.fs:1330-1339`,
`docs/content/guide/troubleshooting.md:183-274`). The run fails even when the body catches the creator's exception and
when the creation is inside `untrack`.

**Pure contexts (the lambda passed as):** `compute` of `createMemo` and `createMemoWithComparer` (`Api.fs:251`,
`:257`), of `Memo (graph, compute)` without the owning argument, of `createAsync` and `createAsyncWithPolicy`
(`Api.fs:362`, `:386`) and of `createEffectOn` (`Api.fs:306`, doc at `:302`); `map` of `createProjection` and
`createIndexProjection` (`Api.fs:650`, `:665`); the reader lambda returned by the factory of `createProjectionWith`
(`Api.fs:590`); `f` and `affected` of `createLookup` (`Api.fs:687`); `seed` of `createEditable` and `createDraft`
(`Api.fs:751`, `:770`); `add`, `subtract` and `folder` of projection aggregates (`Combinators.fs:703`).

**Creators:** `createMemo*`, `createEffect*`, `createAsync*`, `createStream*`, `createSuspense*`,
`createErrorBoundary*`, `createBoundary*`, `createRoot`, `createProjection*`, `createIndexProjection*`,
`createLookup`, `createSelector`, `createOptionMemo`, `onCleanup`, and the `Memo`, `Effect`, `AsyncMemo` and
`Boundary` constructors.

**Detection.** A creator call inside a pure context, at lambda depth 0 relative to the context's body, or inside a
lambda passed to `untrack` or `batch` at that depth. A creator inside any other nested lambda (a stored callback) is
not flagged.

**False-positive risk.** Low. A creator on a branch that never runs is the only false positive.

**Message.** ```createEffect` creates an owned node inside `createMemo`'s body, which fails every read of the memo.
Use `createMemoWith` for a memo that owns the nodes its body creates, or create the node outside the memo.`` The
projection, lookup and aggregate variants use the wording of the runtime messages at `Core.fs:1336-1339` and
`Combinators.fs:703`.

**Fix.** For the memo and async contexts, rename the callee: `createMemo` to `createMemoWith`,
`createMemoWithComparer` to `createOwningMemoWithComparer`, `createAsync` to `createAsyncWith`,
`createAsyncWithPolicy` to `createOwningAsyncWithPolicy`. No fix for the other contexts.

**Tests.** Positive: each context with one creator; creator inside `untrack`; creator inside `try ... with`; the
constructor form. Negative: creator in `createMemoWith`; creator in the projection factory body (not the reader);
creator in a stored callback inside a memo; `createSignal` in a memo.

### RAN011: Async value created and read in the owner that replaces it

**Trap.** An async value created inside the body of a boundary, an owning memo, an effect or `createAsyncWith`, and
read in the same body, is disposed and recreated on every settle. The boundary shows its fallback forever and starts a
flight per settle (`docs/content/guide/troubleshooting.md:550-582`).

**Detection.** Owner bodies: `body` of `createSuspense`, `createErrorBoundary`, `createBoundary` and the `Boundary`
constructors; `compute` of `createMemoWith` and `createOwningMemoWithComparer`; the body of `createEffect` and
`Effect`; `compute` of `createAsyncWith` and `createOwningAsyncWithPolicy`. Flag a `let`-bound value produced by
`createAsync*`, `createAsyncSource`, `createStream*` or `new AsyncMemo` in such a body when a reactive read of that
value occurs in the same body.

**False-positive risk.** Low. An async value created and read once on purpose, in an owner that never re-runs, is the
false positive.

**Message.** ```report` is created inside this boundary's body and read there; each settle re-runs the body, which
disposes `report` and starts a new flight. Create `report` outside the boundary and read it inside.``

**Tests.** Positive: each owner kind. Negative: async value created outside and read inside; created inside and never
read there; created inside a pure `createMemo` (RAN010 reports it).

### RAN012: Reactive read after the first await of an async memo

**Trap.** An async memo tracks reads made while its body runs synchronously, up to the first `await` that suspends.
A read in a continuation is untracked, so a change to that source starts no flight
(`docs/content/guide/troubleshooting.md:506-548`, `docs/content/guide/async-memos.md:364`).

**Detection (shape verified for `task`).** The typed tree keeps the CE desugaring: `task { ...; do! Task.Yield ();
return x + A.counter }` is `TaskBuilder.Run (Delay (fun () -> ... TaskBuilderBase.Bind (builder, awaitable, fun _arg1
-> Return (...))))` (fcsprobe). Inside the `compute` lambda of `createAsync*`, `createOwningAsyncWithPolicy` or
`new AsyncMemo`, flag a reactive read inside the last lambda argument of a builder call whose method name starts with
`Bind` (`Bind`, `BindReturn`, `Bind2`, `MergeSources` covers `and!`). The rule applies to any builder, so
`cancellableTask` and `async` are covered by the same shape.

**False-positive risk.** Medium-low. `let!` on an already-completed task does not suspend and the read stays tracked;
the rule flags it. The message states the condition.

**Message.** ```userId.Value` is read after the first `let!`/`do!` of this async memo; a read after an await that
suspends is untracked, and a change to `userId` starts no flight. Read it before the first await and bind it to a
local.``

**Tests.** Positive: read after `do! Task.Yield ()`; after `let! _ = previous.Settled`; in an `and!` continuation;
`cancellableTask`. Negative: read before the first await; read of a local bound before the await; `Peek` after the
await.

### RAN013: Signal write or `Pump` on a thread-pool callback

**Trap.** A signal write or `Graph.Pump` on a thread other than the graph's owner raises `... ran on thread N, but
this graph is owned by thread M` under the default `Guarded` affinity
(`docs/content/guide/troubleshooting.md:81-118`).

**Detection.** A call of `Signal<'T>.set_Value`, `Signal.update` (`Api.fs:788`) or `Graph.Pump` (`Core.fs:2119`)
inside a lambda or delegate passed to `Task.Run`, `Task.Factory.StartNew`, `ThreadPool.QueueUserWorkItem`,
`ThreadPool.UnsafeQueueUserWorkItem`, `new Thread`, `Parallel.For`, `Parallel.ForEach` or `new Timer`, and not inside a
lambda passed to `Graph.Dispatch` (`Core.fs:2059`). `AsyncSource.Settle` and `Fail` are excluded: an off-thread
settle waits in the graph's inbox by design.

**False-positive risk.** Medium for graphs built with `ThreadAffinity = Unchecked` or `Serialised` hosts where the
caller guarantees one thread. Those sites carry a suppression comment.

**Message.** ```temperature.Value <- 25` runs on a thread-pool thread; a write off the graph's owning thread raises
under `ThreadAffinity.Guarded`. Hand the write to `graph.Dispatch (fun () -> ...)`.``

**Tests.** Positive: `Task.Run` with a write; `Pump` in `ThreadPool.QueueUserWorkItem`; `Signal.update` in
`Parallel.For`. Negative: write inside `graph.Dispatch` inside `Task.Run`; `Settle` inside `Task.Run`; write in an
ordinary lambda.

### RAN015: `CancelPrevious` with a body that ignores its token

**Trap.** `createAsyncWithPolicy CancelPrevious (fun _ _ -> ...)` cancels a token the body never reads, so it behaves
as `KeepLatest` (`Types.fs:65-77`).

**Detection.** The policy argument is the literal case `CancelPrevious` and the body's token parameter is `_` or
unused. **Severity Hint.** **Message.** ``This async memo's body ignores its CancellationToken, so `CancelPrevious`
cancels nothing and acts as `KeepLatest`. Pass the token to the awaited work, or use `KeepLatest`.`` Phase 3; its
value is the smallest of the set.

### RAN020: Reactive read at template build time

**Trap.** `Component.define` builds a template once, when the definition is created (`Template.fs:2770-2777`), and
`setup` and `render`'s props reader run once per instance, untracked (`TemplateSugar.fs:174-176`). A reactive read in
those positions is a snapshot that never updates: "A child does not follow a parent value"
(`docs/content/view/troubleshooting.md:75-76`).

**Flagged sites, each with a reactive read anywhere in the expression and not under a lambda inside it:**

1. a static text yield: `Element.Yield (value: string)` (`TemplateSugar.fs:55`) and `TemplateBuilder.text (value:
   string)` (`Template.fs:2652`); `TemplateParts` is internal (`Template.fs:2237`) and unreachable from user code;
2. a static attribute or property overload: a member of `AttributeBuilder`, `PropertyBuilder` or the generated
   builders whose parameter is named `value` and is neither a function nor a `Source` (for example
   `className (value: string)`, `Generated/HtmlTemplate.fs:585`, against the reader overload at `:591`), and
   `attribute (name, value: string)` (`Template.fs:2324`);
3. the props argument of `renderOf` (`TemplateSugar.fs:179`);
4. the returned expression of a `setup` lambda (`Component.define`, `Component.template`, `ComponentTag`), or a field
   of a record or tuple constructed in that return position, when it is a reactive read;
5. the body of `render`'s props lambda (`TemplateSugar.fs:175`), when it is a reactive read.

A site lexically inside an `arms` function or an items reader is excluded: that code runs per region read (RAN022).

**False-positive risk.** Low for sites 1-3, which run once per definition. Medium for sites 4-5, where a snapshot can
be intended (an initial value).

**Message (site 1).** ```count.Value` is read once, when the template is built, so this text never updates. Yield a
reader: `fun _ -> string count.Value`.`` **(site 5)** ```render`'s props reader runs once per instance, so the child
receives the value of `s.Count.Value` at mount. Pass the signal: `fun s -> s.Count`.``

**Fix.** Sites 1-2: wrap the argument in `fun _ -> ...`. The fix is offered only where a fixture proves the reader
overload resolves for that member (the inference rule, `docs/content/view/sugar.md`). No fix for sites 3-5.

**Tests.** Positive: each site. Negative: reader yields; `Source` (`from`); `Peek` in a static yield; a read inside an
event handler; setup that reads a value to seed `createSignal`.

### RAN021: Unstable list key

**Trap.** `forEach`, `forEachFixed` and `each` keep a row for an item whose key is equal under `=`
(`TemplateSugar.fs:117-139`, `Template.fs:2749-2762`); "a key function must be pure"
(`docs/content/view/collections.md:58-63`). The key function receives the item only, so an index is reachable only
through a search or captured state.

**Diagnostics.**

| Key function | Effect | Severity |
|---|---|---|
| writes a captured `mutable` or `ref`, or calls `Guid.NewGuid`, `Random`, `DateTime.Now`/`UtcNow`, `Stopwatch` or `obj ()` | every pass gives new keys: every row remounts | Warning |
| calls `Array.indexOf`, `Array.findIndex`, `List.findIndex`, `Seq.findIndex` or `IndexOf` | positional key: a move or insert remounts the rows after it | Warning |
| contains a reactive read | the key depends on a tracked value outside the item | Warning |
| `forEach` with `id` or `fun x -> x` and a record or union item type | a replaced item gets a new key, so the row remounts instead of following the item | Info |
| key type compared by reference (a class without custom equality) | rebuilt items get new keys | Info |

**False-positive risk.** Low for the Warning rows. The Info rows carry intent ambiguity: an item list of strings keyed
by `id` is correct and is excluded (primitive and string item types).

**Message (positional).** ``This key is the item's position (`Array.indexOf`), so moving or inserting an item remounts
every row after it. Key by a value unique to the item, such as an id.``

**Tests.** Positive and negative per row; `t.each` (builder member) and the sugar functions.

### RAN022: Definition created on every region read

**Trap.** A definition (`armOf`, `Component.define`, `Component.template`, `Component.ofMembers`,
`Component.ofClass`, and `show`/`showElse`, which define two components per call, `TemplateSugar.fs:142-168`)
created inside an `arms` function or a list's items reader runs on every region read
(`Template.fs:2723`, `:2739`, `:2757`). The arm remounts on every update; Fable Debug builds raise "a template
definition was created while a region was read" (`Template.fs:2771-2774`). A definition read from a non-auto
`static member` property is recreated on every read with the same effect
(`docs/content/view/troubleshooting.md:52-54`, `:68-69`).

**Detection.** A definition creator inside the `arms` lambda of `matchWith`/`branch` (`TemplateSugar.fs:110`, `:114`;
`Template.fs:2717`, `:2732`), or inside the items or key lambda of `each`/`forEach`/`forEachFixed`. A static property
getter returning `Component<_>` read in those lambdas, whose implementation (found through
`CheckProjectResults.AssemblyContents`) calls a creator. The static-property check runs in the CLI only: an editor
context may lack project results.

**False-positive risk.** Low.

**Message.** ```armOf` runs on every read of this region, so the arm is a new definition each time and remounts.
Bind the definition with `let` outside the template.``

**Tests.** Positive: `armOf` in `function` arms; `show` inside arms; definition in an items reader; static property
read in arms. Negative: hoisted `let` definitions; `static member val`; definition in a template build function
outside regions.

### RAN023: Invalid content parts on an element

**Trap.** `TemplateParts.element` raises `ArgumentException` when an element has two content parts (`Unsafe.html`,
`Hooks.widget`), a content part and template children, or raw HTML on a void, raw-text, `template`, `textarea` or
`title` element (`Template.fs:2174`, `:2210`, `:2212-2222`). The exception fires when the definition is created, on
every target (`docs/content/view/troubleshooting.md:62-64`).

**Detection.** The parts of an element are a literal array (`t.div [| ... |] [| ... |]`) or the constructor arguments
of a generated sugar tag; children are the literal array or the CE yields. Flag the three combinations from literal
parts only.

**False-positive risk.** None for literal parts; parts built by a helper are a false negative.

**Message.** ``This element has two content parts (`Unsafe.html` and `Hooks.widget`), which fails when the definition
is created. Give each content part its own element.``

### RAN024: Markup the server render refuses

**Trap.** The server plan raises for raw-text elements (`script`, `style`, `iframe`, `noscript`, `xmp`, `noembed`,
`noframes`, `plaintext`; `Template.fs:298-308`, `:1228-1229`, `Representation.fs:991-1004`) and for `template`
elements (`Template.fs:1231-1237`), and for nesting the HTML parser restructures (`ContentModel`,
`Template.fs:650-1081`; "The HTML parser restructures this markup (table-row) at table/tr",
`docs/content/view/troubleshooting.md:43-45`). Each surfaces at the first server render. Since #140 the client paths
also refuse `script`, a raw-text element with children (or `plaintext`), `template`, and a void element with children
(`refusal`, `Template.fs:330-354`, raised at `:475` and `:2000`), with the script message of
`Representation.fs:285-286`; those fail at the first render on every target.

**Detection.** A generated sugar tag type or a `TemplateBuilder` member for a refused tag inside a definition. For
nesting, the parent and child tags are both literal (CE nesting or literal child arrays) and match a `ContentModel`
rule. The rule calls the library's `ContentModel` check on the literal path instead of re-implementing it
(Decision 14).

**False-positive risk.** Low. A client-only template with a refused tag is the false positive; the message names the
server render.

**Message.** ```script` is refused by every render ("Script elements are not created by templates or typed
views"). Load scripts through the server document shell, for example `Server.Page` scripts.`` The other raw-text
elements: ```style` is a raw-text element, which the server render refuses ("Raw-text elements require an explicit
HTML serialization policy"). Write it outside the template.``

### RAN025: `hydrateWith` with queued commits

**Trap.** `hydrateWith` raises `NotSupportedException "Hydration supports synchronous commits only"` for
`QueuedCommits` (`Hydration.fs:143`, `:265`).

**Detection.** The first argument of `Hydration.hydrateWith` is a `QueuedCommits` union construction or a call of a
function returning a `QueuedCommits` construction in the same file (`Microtask.fs:16`). **Message.** ``Hydration
supports synchronous commits only, so this call raises. Pass `SynchronousCommits`, or call `hydrate`.``

### RAN030-RAN032: `Arms.Match`

`Arms.Match` is specified on the arms work (research note `docs/research/2026-10-21-idiomatic-arms.md`, "What
implementation takes", items 4 and 5). On .NET its `arms` parameter is `[<ReflectedDefinition>] Expr<'u ->
TemplateView<'inst>>`, so the analyzer sees the call site's lambda as an `FSharpExpr` quotation (`Quote`), with the
same decision-tree shapes the Fable plugin walks.

- **RAN030, pattern variable outside a reader (Error).** A value bound by the match (a `Let` of a union-field,
  tuple-item, record-field or option-value get in the decision tree, or a `DecisionTreeSuccess` target binding) used
  anywhere but inside a lambda whose parameter type is the enclosing instance type. This is the Fable plugin's
  compile error and the .NET `BuildTimeRead` exception, at IDE time. **Message.** ``The pattern variable `text` is
  read when the arm is built, where no instance exists. Read it inside a reader: `fun _ -> text`.``
- **RAN031, arms not a function literal (Error).** The second argument is not a lambda or `function` literal.
  **Message.** ```Arms.Match` takes a function literal; a named function cannot be rewritten into arms.``
- **RAN032, missing plugin (Warning).** A project that references `Fable.Core` and calls `Arms.Match` without a
  reference to the plugin assembly. **Message.** ``Fable compiles `Arms.Match` only with the Ranvier.View.Arms
  plugin; without it the site fails when it is built.``

These rules land with the `Arms.Match` runtime; their fixtures are the probe's failure-mode table.

### Considered and not included

| Candidate | Reason |
|---|---|
| `Server.Options.defaults` (`Policy = Reject`, `Server.fs:21-23`) rendering a template with event listeners | needs the definition's template across files and through `render` children; the runtime message names the fix (`Representation.fs:131`) |
| Handler writing several signals without `batch` (`docs/content/view/troubleshooting.md:78-79`) | unbatched writes are often intended; the cost is extra effect runs, not wrong output |
| `setup.CreateId ()` after setup (`Identity.fs:103`) | whether a closure runs during setup is undecidable from the tree |
| Duplicate projection keys, lookup `affected` omissions | properties of data, not of code shape |
| `untrack` misuse | creators under `untrack` in a pure context are RAN010; an `untrack` outside a tracking scope is harmless |
| No ambient graph (`Graph.Current`) | the active graph is per-thread run-time state |

## Package layout

```text
tools/Ranvier.Analyzers/
  Ranvier.Analyzers.fsproj     net10.0; FSharp.Analyzers.SDK 0.41.0 (PrivateAssets all); FSharp.Core 10.1.402 (Update)
  Symbols.fs                   full names of node types, creators, contexts, view builders
  Index.fs                     the shared per-file traversal and lambda-context stack
  Rules/Fable.fs               RAN001, RAN002
  Rules/Core.fs                RAN010-RAN015
  Rules/View.fs                RAN020-RAN025
  Rules/Arms.fs                RAN030-RAN032
  Analyzers.fs                 one CliAnalyzer and one EditorAnalyzer per rule, with HelpUri
  build/Ranvier.Analyzers.props
  build/Ranvier.Analyzers.targets
tools/Ranvier.Analyzers.Tests/
  Ranvier.Analyzers.Tests.fsproj   Expecto; FSharp.Analyzers.SDK.Testing 0.41.0
  Options.fs                   project options with references to the built Ranvier, view and Fable.Core assemblies
  Ran001.fs ... Ran032.fs      one module per rule
```

- **Packing.** `IncludeBuildOutput=false`; `Ranvier.Analyzers.dll` packs to `analyzers/dotnet/fs/`, the SDK's
  convention. The SDK and FCS assemblies are not packed: the host loads them. `DevelopmentDependency=true`.
- **`build/` targets.** Append `--analyzers-path "$(MSBuildThisFileDirectory)../analyzers/dotnet/fs"` to
  `FSharpAnalyzersOtherFlags` in the `.targets` file (the `.props` file is evaluated before `nuget.g.props`), set
  `FSharpAnalyzersContinueOnError=false` unless the project sets it, and set `RunAnalyzersDuringBuild` when
  `RanvierAnalyze=true`.
- **FSharp.Analyzers.Build.** The package depends on FSharp.Analyzers.Build 0.5.0 with `PrivateAssets="none"` on the
  pack project's reference, so its `build/` assets reach the consumer.
- **Solution.** Both projects join `/tools/` in `Ranvier.slnx`; `dotnet fantomas --check` covers them.

## Opting in

**A consumer project:**

```xml
<PackageReference Include="Ranvier.Analyzers" Version="0.1.0" PrivateAssets="all" />
<PropertyGroup>
  <RanvierAnalyze>true</RanvierAnalyze>
</PropertyGroup>
```

plus the CLI in the tool manifest: `dotnet tool install fsharp-analyzers --version 0.41.0`. `dotnet build` then prints
`Foo.fs(12,9): warning RAN020: ...` after each build that runs `CoreCompile`. Without `RanvierAnalyze`, `dotnet
msbuild -t:AnalyzeFSharpProject` runs the analyzers on demand, and `dotnet fsharp-analyzers --project Foo.fsproj
--analyzers-path <package>/analyzers/dotnet/fs` runs them without MSBuild integration.

**This repository:**

- `.config/dotnet-tools.json` gains `fsharp-analyzers` 0.41.0 with `rollForward: false`.
- `Directory.Build.props` adds the FSharp.Analyzers.Build reference under `'$(RanvierAnalyze)' == 'true'`;
  `Directory.Build.targets` points `--analyzers-path` at `tools/Ranvier.Analyzers/bin/Release/net10.0` and adds
  `--code-root` at the repository root.
- `build.fsx` gains `analyze`: build `tools/Ranvier.Analyzers -c Release`, then run the CLI once over the gate's
  project list with `-c Release --fail-on-type-errors --treat-as-error "RAN*" --exclude-files "**/Generated/**"
  --report artifacts/analyzers/ranvier.sarif`, adding `--output-format github` under `GITHUB_ACTIONS`. Agents and CI
  run the same command: `dotnet fsi build.fsx -- analyze`.

**Gate project list (phase 0 confirms each type-checks under the CLI):** `src/Ranvier`, `src/Ranvier.Query`,
`src/Ranvier.Elmish`, `src/Ranvier.Wire`, `src/Fable.Ranvier`, `src/Fable.Ranvier.Anime`,
`experiments/Ranvier.View.Portable`, `experiments/Ranvier.View.Foundation`, `experiments/Ranvier.View.Anime`,
`examples/Fable.Ranvier.Playground/Playground.fsproj`, `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`,
`examples/Ranvier.Query.Dictionary`, `examples/Ranvier.FuncUI.Workload` (both projects),
`docs/samples/view/Ranvier.Docs.ViewSamples.fsproj`, `tests/Ranvier.Tests`, `tests/Ranvier.Wire.Tests`. The
`IsExcludedFromRootBuild` projects (`fable/*`, `docs/maps/Ranvier.Docs.Maps.fsproj`) are left out (Decision 11).

### CI job

```yaml
  # Runs the Ranvier analyzers' unit tests, then the analyzers over the repository's projects with every RAN code
  # promoted to an error: any finding fails the job.
  analyzers:
    name: Analyzers
    needs: changes
    if: needs.changes.outputs.docs != 'true'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - name: Restore tools
        run: dotnet tool restore
      - name: Test the analyzers
        run: dotnet test tools/Ranvier.Analyzers.Tests -c Release
      - name: Build the analyzed projects
        run: dotnet build Ranvier.slnx -c Release
      - name: Run the analyzers over the repository
        run: dotnet fsi build.fsx -- analyze
      - name: Upload the SARIF report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: analyzers-sarif
          path: artifacts/analyzers/
```

`analyzers` joins the `needs` list of `result` (`.github/workflows/ci.yml:320`). The build step is required: the CLI
type-checks against referenced projects' outputs, and `--fail-on-type-errors` turns a missing output into a failure
instead of an empty clean run.

## Diagnostics and docs

- **Code and severity.** Each rule has one code; its default severity is in the summary table. `--treat-as-*`
  overrides it per project.
- **Message.** One or two sentences: what the program does, then the fix, with the flagged symbol's own name
  substituted. Messages carry no "here"/"this line" deixis: the range locates them.
- **Range.** The smallest expression that is the cause: the read, the creator call, the tester property, the binding
  identifier.
- **Docs.** `docs/content/analyzers/index.md` lists the codes; `docs/content/analyzers/ran001.md` and so on hold
  **Cause**, **Fix**, an example that triggers the rule, its fixed form, and **Suppression**, in the troubleshooting
  pages' format. `HelpUri` is `https://ranvier-org.github.io/Ranvier/analyzers/ran001/`. `docs/Site.fs` adds the
  section to the menu; each troubleshooting entry with a rule links to its page. The docs build compiles each page's
  examples, and the analyzer test suite runs every "triggers" example through its rule.

## Suppression

- **In code:** the SDK's comments, `// fsharpanalyzer: ignore-line RAN013`, `ignore-line-next`,
  `ignore-region-start`/`ignore-region-end`, `ignore-file`. The repository requires the reason on the preceding line,
  and the docs page of each rule shows the form.
- **Per project:** `--treat-as-info RAN021` (or `--exclude-analyzers <name>`) in `FSharpAnalyzersOtherFlags`.
- **In build output:** `MSBuildWarningsAsMessages` demotes a code's MSBuild warning (phase 0 verifies it for
  Exec-reported warnings).
- Each rule's `EditorAnalyzer` checks `AnalyzerIgnoreRanges` for a `File` entry before walking, per the SDK's
  "Optimizing Analyzers Using Ignores".

## Performance budget

- **Measured baseline:** the CLI spends 8.0 s on `src/Ranvier` and 13.9 s on `Ranvier.View.Portable`, almost all of it
  project cracking and type-checking (the latter's full `dotnet build` is 14.5 s).
- **Budget:** all rules together add at most 5 % to the CLI wall time of each gate project, and the `analyzers` CI job
  finishes in under 6 minutes including the solution build.
- **Mechanisms:** reference gating (Decision 5); one shared traversal per file (Decision 4); generated files excluded
  from the gate (`--exclude-files "**/Generated/**"`, which still counts their type errors).
- **Check:** the test suite times each rule over `Template.fs` and `Core.fs` and fails above 250 ms per file on the CI
  runner, and the shared traversal (Decision 4) above 500 ms per file: the traversal materialises the typed tree that
  every rule reads, which costs more than any one rule. These per-file limits are the budget CI enforces. The 5 % CLI
  figure is measured by hand on a quiet machine against a run with the rules excluded; CI does not measure it. Both
  analyzer jobs carry `timeout-minutes: 8` as a guard against a runaway run.
- `FSharpAnalyzersAlwaysRunAfterBuild` stays unset: a no-op build re-emits no compiler warnings and no analyzer
  warnings either (Decision 16).

## Testing

1. **Unit tests per rule.** `FSharp.Analyzers.SDK.Testing.mkOptionsFromProject "net10.0" []` gives base options;
   `Options.fs` appends `-r:` for the Release outputs of `src/Ranvier`, `experiments/Ranvier.View.Portable` and
   `Fable.Core`, because the view assemblies are not on NuGet. `getContext options source` builds a `CliContext`.
   RAN001 needs two files: `FSharp.Analyzers.SDK.Testing.getContextFor` type-checks a list of `SourceFile`s and
   returns the context of one of them. `getContext` and `getContextFor` load the analyzers in the working directory,
   so the suite runs from its own output directory, which holds `Ranvier.Analyzers.dll`.
2. **Cases.** Each rule has at least three positive and three negative cases, the cases named in its section, and an
   exact-message assertion for one positive case. A fix is applied to the source and the result re-checked: it
   type-checks and the rule no longer fires.
3. **Regression seeds.** RAN002 runs on a copy of `Core.fs:4545` as it was before `2cf84be` and fires; RAN001 runs on
   `ElmishNestedChecks.fs:29-30` with `let mutable` changed to `let` and fires.
4. **Fable truth tests.** The Fable test job compiles RAN001's positive fixtures with Fable Release and asserts the
   emitted JavaScript reads the mutable at the use site. A Fable upgrade that stops inlining fails this test, which
   is the signal to retire or rescope RAN001. RAN002's truth test runs `dotnet fable precompile` on a fixture whose
   non-generic union tester fails with `MethodDefNotFound` (Decision 9). Both run in the `analyzers` job.
5. **Corpus gate.** `build.fsx analyze` over the gate list with `--treat-as-error "RAN*"` reports zero findings. The
   two current RAN002 findings (`Core.fs:4506`, `:4563`) are fixed in the phase 0 PR. A new finding on master is a
   bug in the rule or in the code; neither is merged.
6. **Typed-tree drift.** The unit tests compile against the CLI's FCS, so an FCS bump that changes a shape fails the
   tests, not the gate.

## Versioning

- `Ranvier.Analyzers` has its own version, starting `0.1.0-preview.1`. Each release note names the SDK, as the SDK
  convention asks: "Built against FSharp.Analyzers.SDK 0.41.0".
- The SDK pins FCS and FSharp.Core exactly; the analyzer project overrides the repo's FSharp.Core 10.1.401 with
  10.1.402.
- An SDK upgrade moves the tool manifest entry and the package reference in one commit, then runs the unit tests and
  the corpus gate. A mismatch fails the CLI run with a non-zero exit, so a half-done upgrade cannot pass CI.
- Symbol names come from `Symbols.fs` only. A renamed library API breaks the fixtures, which compile against the real
  assemblies.

## Risks

| Risk | Mitigation |
|---|---|
| No IDE shows the diagnostics inline today: FsAutoComplete is on SDK 0.37.2, Rider and Visual Studio host none | build output carries them for agents and Rider's build window; Decision 8 |
| Typed-tree shapes change between FCS versions (CE desugaring for RAN012, quotations for RAN030) | per-rule fixtures run against the CLI's FCS |
| RAN001 describes Fable 5.18's optimiser; another Fable version can change it | Fable truth test (Testing 4) |
| Fable-only branches (`#if FABLE_COMPILER`) are invisible | documented non-goal; RAN001/RAN002 code in shared code is still seen |
| Fable-only projects fail `--fail-on-type-errors` | excluded from the gate (Decision 11) |
| The CLI breaks under a newer SDK (`System.Runtime 11.0.0.0` probe failure) | the repo's `global.json` pins SDK 10; consumers need the same |
| False positives on intended snapshots (RAN020 sites 4-5) and affinity-free graphs (RAN013) | Warning severity, suppression comments with reasons, docs pages show both |
| Rules duplicate library invariants and drift from them | each rule cites the runtime check it mirrors; fixtures use the real assemblies |
| View rules bind to `Ranvier.View.Foundation.*` names that change when the view graduates from `experiments/` | `Symbols.fs` is the single place to change |

## Implementation outline

**Phase 0: scaffolding and the Fable rules.**

1. Copy the three probes to `docs/research/analyzers/`.
2. Add `tools/Ranvier.Analyzers` and `tools/Ranvier.Analyzers.Tests`, `Symbols.fs`, `Index.fs`, the test option
   helpers, and the slnx entries.
3. RAN001 and RAN002 with fixes, tests and docs pages; the Fable truth test for RAN001.
4. Fix `Core.fs:4506` and `:4563` with matches; the precompile fixture of Decision 9 becomes RAN002's truth test.
5. Tool manifest entry, `Directory.Build.props`/`.targets` opt-in, `build.fsx analyze`, the CI job; confirm every
   gate project type-checks; verify `MSBuildWarningsAsMessages`.

**Phase 1: core rules.** RAN010-RAN013 with tests, docs pages and links from `docs/content/guide/troubleshooting.md`.
Pack `Ranvier.Analyzers` and check a consumer project picks up the `build/` targets (`tests/check-package-compatibility.ps1`
gains the case). Publish.

**Phase 2: view rules.** RAN020-RAN025; extract `ContentModel` (Decision 14); links from
`docs/content/view/troubleshooting.md`.

**Phase 3: the rest.** RAN030-RAN032 with `Arms.Match`; RAN015; the Ionide-loadable build once FsAutoComplete moves
to SDK 0.41.
