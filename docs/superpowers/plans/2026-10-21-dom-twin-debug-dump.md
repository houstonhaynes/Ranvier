# .NET DOM twin and `Debug.dump` implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the real Foundation and Portable mount code on .NET against a DOM twin with a scripted interaction API and a deterministic clock, verified by one conformance suite shared with jsdom, and add `Debug.dump`, a text and JSON dump of a mounted view, at 0 B for client bundles that do not call it.

**Architecture:** Every Emit helper, `emitJsExpr` value and `[<Global>]` value without a .NET body moves into `Native.fs`, a new first file of Portable that keeps the JavaScript text verbatim under `FABLE_COMPILER` and holds F# bodies over the `Fable.Core.TS.Dom` interfaces on .NET. A new .NET-only project, `Ranvier.View.Twin`, implements those interfaces with one `DispatchProxy` bridge over a .NET node tree, a subset HTML parser, jsdom-equal serialisers, an event model and a clock; `Ranvier.View.Testing` holds the selector engine, queries, interactions, expectations and the conformance case format for both hosts. `Debug.dump` reads region state through an inspection registry compiled under `RANVIER_VIEW_INSPECT || !FABLE_COMPILER` and reads core nodes only through `Peek` and `Status`.

**Tech Stack:** F# on .NET 10 and Fable 5.18.0 (`.config/dotnet-tools.json`); `Xantham.Fable.Core.TS` 0.2.1 DOM interfaces; `System.Reflection.DispatchProxy`; Expecto 11.0.0-alpha8 with YoloDev.Expecto.TestSdk 0.15.5; jsdom 30.1.1 under `node --test` (Node 26); Vite 8.3.2 for the bundle gates; Fantomas 8.0.5; Ranvier core (`src/Ranvier`, read-only here).

**Spec:** [`docs/superpowers/specs/2026-10-21-dom-twin-debug-dump-design.md`](../specs/2026-10-21-dom-twin-debug-dump-design.md), approved 2026-10-21 with the ten decisions and the planning rulings of its §9. Read it before any task. Section numbers below (§3.2, §7.1) are the spec's.

## Core and view state

Required by `.claude/rules/view-uses-core.md`; the spec's section of the same name is the contract. In short:

**Core primitives used:** `Graph`, `Owner`, `createRoot`, `onCleanup` (unchanged, through the real mount code); `ManualDispatcher` and `Graph.Pump` (the twin's `Settle`); `TimedClock.ofTimeProvider` (the twin clock's `TimeProvider`); `Signal.Peek`, `Memo.Peek` and `INode.Status` (the only reads `Debug.dump` makes); `Graph.IsOnGraphThread` (the dump's thread check); `Trace.snapshot` under `RANVIER_TRACE` (node paths on dump lines).

**View-owned state (new):** the twin document (`Ranvier.View.Twin`); the twin clock's microtask, timer and frame queues; the .NET expando and collection tables in `Native.fs`; the inspection registry in `Inspect.fs`, which holds closures over region variables and no copies. No view-side reconciler, key matcher, selection cache or commit queue is added.

## Global Constraints

Copied verbatim from the spec:

- "The twin reimplements the DOM only; no mount, region, hydration or departure logic is duplicated (§3.1)."
- "Client bundles that do not call `Debug.dump` are byte-identical before and after the change: the `sugar-size.mjs` minified entries hash equal at the merge base and on the branch (§7.1)."
- "A dump changes no core counter, observer count or trace event (§6.5)."
- "The same view and actions give byte-equal dump text on .NET and under Fable, and across runs."
- "All registration code is inside `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`."
- "`Native` has no public surface."
- "No Fable client project references `Ranvier.View.Twin`."
- "Both are `IsPackable=false` until then."
- "No view-side reconciler, key matcher, selection cache or commit queue is added."
- "Real time never advances a twin document."
- "A twin document belongs to the thread that creates it, as a graph does."
- "The twin avoids `use` inside `task { }` so it runs under the SDK's `FSharp.Core` in a session (`AGENTS.md:74-78`, SageFs#141), and it reads no path from `AppContext.BaseDirectory` (SageFs#142)."
- "`Html.outer` / `Html.inner`: the HTML fragment serialisation algorithm, as jsdom 30.1.1 produces it through parse5 8.0.1 and `entities` (text escapes `& < >` and U+00A0; attribute values escape `& "` and U+00A0"
- "A call to any other member raises `NotSupportedException "The DOM twin does not implement HTMLElement.offsetWidth"` with the interface and member."

Repository constraints:

- Work in `C:\Users\shaya\RiderProjects\Ranvier-dom-twin` on branch `feat/dom-twin`. Merge `origin/master` (`git fetch origin && git merge origin/master`); never rebase, amend a pushed commit or force-push. Push with `git push origin HEAD:feat/dom-twin`.
- View-side only: no file under `src/` changes. A task that finds a core change necessary stops and raises it with the user (`.claude/rules/view-uses-core.md`).
- Every existing test passes unchanged, except the check modules that a task moves onto the conformance harness (Tasks 13, 15, 16, 17, 18, 19), whose runner and document access change as that task says, and `experiments/Ranvier.View.Portable/Checks/PackagingChecks.fs`, which since Task 3 exempts `Native.fs` from its document-Emit rule and checks `Native.fs`'s structure instead (every Emit inside a `#if FABLE_COMPILER` block whose `#else` defines the same names; Amendment A1), pending the user's objection. Any other test that has to change is a plan conflict: stop and report it.
- Comments follow `.claude/rules/comments.md`; `///` docs follow the `fsharp-xml-docs` skill. No `FOR-REVIEW` comment is committed.
- Commits: one conventional line, a blank line, then exactly these two lines:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
  ```
  Write the message to a file under `C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/` and commit with `git commit -F <file>`. Before each commit: `dotnet fantomas <each changed .fs file>`, `git add` of named paths only, `git diff --cached --check` empty.
- Prefix shell commands with `rtk` (passthrough when it has no filter) except `node`, `npm`, `npx`, `dotnet`, `gh` and `git commit`.

### Gate commands

Each task names the gate sets it runs. All exit 0. Paths are from the worktree root `C:/Users/shaya/RiderProjects/Ranvier-dom-twin` unless a `cd` says otherwise.

- **G-NET** (every task):
  ```bash
  dotnet fsi build.fsx -- test
  ```
  Runs `dotnet test` over every project in `Ranvier.slnx` whose path starts with `tests` (which includes `tests/Ranvier.View.Twin.Tests` from Task 4 on) and the traced maps tests.
- **G-FORMAT** (every task): `dotnet fantomas --check $(git ls-files '*.fs' ':(exclude)src/Ranvier.Wire/Vendor/**')`, as CI's Formatting job runs it.
- **G-DOM** (every task that changes a Foundation, Portable, Testing or Playground source):
  ```bash
  cd examples/Fable.Ranvier.Playground
  npm ci
  npm run test:dom
  npm run test:sugar-debug
  npm run test:sugar-counters
  ```
  `npm ci` once per worktree; never junction or symlink `node_modules` from another worktree.
- **G-BROWSER** (the tasks that name it): the Chromium suite behind the browser lock:
  ```bash
  cd examples/Fable.Ranvier.Playground
  mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/browser.lock
  npm run test:browser
  rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/browser.lock
  ```
  If `mkdir` fails, another run holds the lock: wait and retry. Never remove a lock you did not create.
- **G-VIEW-NET** (every task that changes a Portable or Foundation source):
  ```bash
  dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj -c Release
  dotnet run --project docs/samples/view/Ranvier.Docs.ViewSamples.fsproj -c Release
  dotnet fsi docs/samples/view/Generate.fsx --check
  ```
- **G-SUGAR** (every task that changes `DomTests.fsproj`'s compile list or a linked source): the generator's three script gates, which build `DomTests.fsproj` on .NET:
  ```bash
  cd tools/Ranvier.Dom.Generate && npm ci && cd ../..
  dotnet fsi tools/Ranvier.Dom.Generate/GeneratorTests.fsx
  dotnet fsi tools/Ranvier.Dom.Generate/CheckDocs.fsx
  dotnet fsi tools/Ranvier.Dom.Generate/CheckTypes.fsx
  ```
- **G-BUNDLE** (spec §7.1 gates 1 and 3; every task that changes a Portable or Foundation source):
  ```bash
  cd examples/Fable.Ranvier.Playground
  npm run compile:tests
  node benchmarks/frameworks/bundle-hashes.mjs --compare ../../docs/research/2026-10-21-dom-twin/bundle-base.json
  node benchmarks/frameworks/generated-diff.mjs generated-tests-base generated-tests
  ```
  `generated-tests-base` and `bundle-base.json` are the merge-base outputs recorded by Task 1. After every merge of `origin/master` that changes a file under `experiments/` or `examples/Fable.Ranvier.Playground/`, re-record both with Task 1 Step 6 before the next G-BUNDLE run.
- **G-SIZE** (Tasks 20 and 22): the sugar bundle budgets, whose JSON output is tracked:
  ```bash
  cd examples/Fable.Ranvier.Playground
  npm run compile:tests
  node benchmarks/frameworks/sugar-size.mjs
  git checkout -- ../../docs/research/framework-performance/investigation/view-sugar-v2/bundle.json
  ```
- **Benchmarks and loop timings** run one at a time behind the bench lock: `mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` before, `rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` after, with the same retry rule.

### Repository procedures

**Check-module migration procedure** (decision 5; Tasks 13, 15-19). For each Playground check module moved onto the conformance harness, in order, with the jsdom run green after each step:

1. Move every module-level `[<Global>]` value and every `jsNative` Emit helper under `#if FABLE_COMPILER` (the test executable initialises every linked file). Give each Emit helper a `#else` body over the DOM interfaces when one exists; when none exists, tag the cases that use it `Conformance.platformOnly "jsdom" "<reason>"`.
2. Replace `globalThis.document` and `[<Global "document">]` uses with `Conformance.document ()`.
3. Export `let cases: Conformance.Case array = Conformance.ofChecks [| ... |]`, keeping every case name.
4. Add the module to `ConformanceCases.modules`, delete its old `*.test.mjs`, and edit `test:dom` in `package.json`.
5. Run `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/conformance.test.mjs` from `examples/Fable.Ranvier.Playground`: every case passes under jsdom.
6. Link the module into the twin test project (`<Compile Include="../../examples/Fable.Ranvier.Playground/tests/<Module>.fs" Link="Checks/<Module>.fs" />` before the linked `ConformanceCases.fs`) and run `dotnet test tests/Ranvier.View.Twin.Tests -c Release --filter "FullyQualifiedName~Conformance"`.

On a twin failure, the rules apply in this order, each fix with a twin unit test:

- `NotSupportedException` naming a member: register the member in `Bridge.fs` or `Events.fs`, with a `BridgeTests` case.
- A twin behaviour that differs from jsdom (serialisation, event order, form state): fix the twin to jsdom's behaviour.
- A `Native` .NET body that differs from its JavaScript: fix the body, and run G-BUNDLE to show the Fable side did not move.
- A difference rooted in .NET semantics outside the view (number formatting in a fixture, `string` of a float): tag the case `[jsdom]` and list it in the research note's "Platform-specific cases".
- Anything else, including a change needed in Mounting, TemplateDom or the core: stop and report it.

### Repository hazards

- **Fable 5.18 inlines a single-use immutable `let` snapshot of a module `mutable` at its use site.** `let current = state` followed by one read of `current` reads `state` at the later point, not the snapshot. Write `let mutable current = state` when the snapshot matters, or check the emitted JavaScript under `generated-tests/`.
- **Union `.IsCase` properties break Fable precompile** (#138 fixed `policy.IsFinishCurrent` in core). In every file Fable compiles (`Native.fs`, `Inspect.fs`, `Debug.fs`, everything in `Ranvier.View.Testing`, the check modules), test a case with `match`.
- **An F# executable runs every file's top-level bindings at startup.** `Ranvier.View.Twin.Tests` is an Expecto executable, so a linked check module with a module-level `jsNative` value (`[<Global>] let private globalThis: GlobalThis = jsNative`) aborts the whole test run. Every such value in a linked file goes under `#if FABLE_COMPILER`.
- **Emit text is the bundle.** A moved Emit string must be the exact text of the declaration it replaces; copy it, do not retype it. Gate 3 (`generated-diff.mjs`) shows any difference in emitted JavaScript.
- **A JavaScript array splice changes the array's length; a .NET array cannot.** `spliceRows` (`Mounting.fs:209-210`) has no .NET body. Task 4 gives its call site a local `#if`.
- **`[<Global>]` module values throw at module initialisation on .NET.** A Foundation library file initialises on first access to any of its values, so one such value makes the whole file unusable on .NET.
- **Citations move.** PR #140 (`feat/motion`: client element refusals and Motion parts) and PR #139 (server document shell) are about to merge into `master`. At the start of each phase (Tasks 4, 7, 14, 17, 20, 23), merge `origin/master`, re-run `rtk grep -c "Emit(" experiments/Ranvier.View.Foundation/*.fs experiments/Ranvier.View.Portable/*.fs`, and rebase this plan's `file:line` citations and helper lists on what `master` holds then. A new Emit helper, `emitJsExpr` value or `[<Global>]` value without a .NET body that a merge brings in joins Task 5's or Task 6's list in the phase that finds it.

## Review Focus

The five inputs the spec implies and no task's spec-derived tests exercise, most likely to bite first:

1. **Keyed rows whose keys are F# options, chars or `int64`.** Fable erases `Some 1` to `1`, writes a `char` as a string and an `int64` as a `BigInt`, so a JavaScript `Map` keys them by value; a reasonable person expects the same rows reused on .NET. Pinned by Task 4's `SameValueZero keys erased Fable primitives by value` test.
2. **A keyed pass that removes rows from the middle of a list** takes the `spliceRows` path (`Mounting.fs:3510-3515`), which shortens a JavaScript array in place. A person expects the remaining rows, in order, with their nodes kept. Pinned by Task 12's `removing two middle rows keeps the other three` test.
3. **Text and attribute values holding U+00A0, `&`, `<`, `>`, `"` and a carriage return.** A person expects `Html.inner` to match jsdom's `innerHTML` and `Html.server (parse s)` to return `s`. Pinned by Task 8's `serialisers escape the jsdom and server sets` test.
4. **Two twin sessions on two threads at once**, as Expecto runs tests in parallel. A person expects each session to see only its own document and a cross-thread use to raise. Pinned by Task 12's `sessions on two threads are isolated` test.
5. **A dump of an instance holding a cyclic value, a 10 000-character string and 200 levels of nesting.** A person expects the dump to terminate, cut the string at `MaxText` and stop at `MaxDepth`. Pinned by Task 21's `dump bounds cyclic, long and deep values` test.

## Amendments after the phase 0 review

These rulings override the task text they name. Each task they touch carries a one-line pointer here.

**A1. The packaging rule for `Native.fs`** (review I1, I2). `PackagingChecks.fs` exempts `Native.fs` from the rule that
no Portable Emit touches `document`, `window` or `addEventListener`, and checks `Native.fs`'s structure instead: every
Emit attribute lies inside a `#if FABLE_COMPILER` block, each such block has an `#else` defining the same top-level
names, every guard naming `FABLE_COMPILER` is `#if FABLE_COMPILER` or `#if !FABLE_COMPILER`, and no Import or Global
attribute appears. A self-test runs the checker over an unguarded Emit, a missing .NET body, a block without `#else` and
another guard, and expects each to fail. The change to an existing test stands pending the user's objection.

**A2. No inline aliases; helpers used many times keep their declaration under a local `#if`** (review I3,
ruling at Task 4). An inline alias changes the emitted JavaScript (Task 3). Two forms keep it unchanged:

- A helper with few uses, or one already moved (Hydration's), is deleted and its uses call `Native.<name>`.
- A helper with many uses (`at`, `item`, `setItem`, `isNullish` and the table helpers in `Mounting.fs`, whose
  names are also common variable names) keeps its `[<Emit>]` declaration verbatim under `#if FABLE_COMPILER`; its
  `#else` declaration has the same name and signature and calls the `Native` .NET primitive. The Fable side is then
  textually unchanged. The tables of Tasks 4 and 5 list the `Native` primitive each `#else` body calls; "delete; each
  use calls `Native.x`" in a row means either form, chosen by the use count.
- A public Emit helper that other files inline (`DocumentHead.documentOf`, `TemplateDom.attachState`) always takes the
  second form.
- `experiments/Ranvier.View.Foundation` is not scanned by `PackagingChecks`; Task 6's A3 rule applies to Portable.

**A3. Portable Emits outside `Native.fs`** (review M6). At the end of Task 6, when every host primitive without a .NET
body has moved, `PackagingChecks`' `browserEmits` changes for Portable files other than `Native.fs`: an Emit attribute
or `emitJsExpr` call is allowed only inside a `#if FABLE_COMPILER` block with an `#else` (the block logic of
`nativeStructure`). `Departures.fs`'s `setTimeout` Emit, which the old rule passed, is covered by this. Add a self-test
case for an unguarded `emitJsExpr`.

**A4. Listener failures are collected on both hosts** (review I4). A listener that raises does not stop the dispatch
and does not raise from it by default, as under jsdom.

- Task 7: `TwinDocument` gains `member val ListenerErrors = ResizeArray<exn> ()` and
  `member val RaiseListenerErrors = false with get, set`.
- Task 10: `Events.dispatch` adds each listener failure to `target.Document.ListenerErrors`. When the outermost dispatch
  ends and `RaiseListenerErrors` is true, it raises `TwinListenerException` for the first failure of that dispatch.
  The test "a listener's exception surfaces after the dispatch ends" sets `RaiseListenerErrors <- true` first. Add a
  test "a listener's exception is collected by default" that dispatches, returns normally, and finds one entry in
  `ListenerErrors`.
- Task 12: under `Reports.Raise` the session sets `RaiseListenerErrors <- true`. Under `Reports.Collect`, after each
  dispatch the session moves `ListenerErrors` into `Session.Reports`, one `[ TwinListenerException (...) ]` entry per
  failure. The Collect test asserts exactly one report.
- Task 13: `Harness` gains `abstract ListenerErrors: unit -> string list`, which returns and clears the messages of
  the listener failures since the last call. The jsdom harness reads them from a `VirtualConsole` passed to `JSDOM`
  (`virtualConsole.on("jsdomError", e => errors.push(e.detail?.message ?? e.message))`, handed to `create` as a third
  callback). The twin harness reads `ListenerErrors` and maps each to its inner exception's `Message`.
- Task 14: after recording `innerHTML`, each `Interact` function records a `listener-errors` artefact when the list is
  not empty, one message per line, so failure paths compare across hosts like any other step.
- A case asserts its listener failures with `Conformance.harness().ListenerErrors ()`. The `[twin]` and `[jsdom]` tags
  are not used for listener failures.

**A5. The dump's flush check covers every view pass** (review I5).

- Task 20: `MountState` keeps `member val internal PassDepth = 0 with get, set` under
  `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`. Every region pass increments it in `try`/`finally`: the keyed list's
  reconcile, the arm switch, the boundary's reveal and teardown effect, and the hole's render, as does the queue's
  flush. The mount probe's `flushing` closure reads `PassDepth > 0`.
- `Debug.capture` checks the flag of every mount probe it meets on its walk.
- The list probe's closure stops at `min keys.Length rows.Length` and skips a null row.
- Task 20 adds the test "a dump from a placed hook during a synchronous keyed pass raises": a placed hook on a row
  calls `Debug.capture`, and the test expects `InvalidOperationException "Debug.dump ran inside a flush"`.
- Spec §4.5 and §5 state that a dump called from inside a core computation that is not a view pass is not detected.

**A6. The non-interference test observes interference** (review I6). Task 21 replaces
"a dump changes no observer count and leaves a stale memo stale" with this test:

- **Setup.** The fixture `probe` is a component whose instance value is `{ Source: Signal<int>; Doubled: Memo<int> }`.
  `Doubled` counts its runs and is not bound to any node; `Source` is bound through a `t.text`.
- **Steps.** The test reads `Doubled.Value` once inside `graph.Run`, writes `Source`, which makes `Doubled` stale, and
  calls `session.Dump ()`.
- **Assertions.**
  - The run count is unchanged by the dump.
  - The dump text shows `Doubled`'s previous value.
  - Reading `Doubled.Value` afterwards runs it once, which proves it was still stale.
- **Counter and trace legs.** G-NET for Tasks 21 and 22 adds two legs, run sequenced so that no other test moves the
  process-wide counters:

  ```bash
  dotnet run --project tests/Ranvier.View.Twin.Tests -c Release -p:RanvierCounters=true -- --sequenced --filter-test-list Dump
  dotnet run --project tests/Ranvier.View.Twin.Tests -c Release -p:RanvierTrace=true -- --sequenced --filter-test-list Dump
  ```

  - Under `RANVIER_COUNTERS`, the test compares `Counters` snapshots before and after `capture`; read its public
    snapshot member from `src/Ranvier/Counters.fs`.
  - Under `RANVIER_TRACE`, it asserts that the trace log's event count is unchanged and that `Trace.reconcile graph`
    is empty.

**A7. `NodeIds`, trace paths, `stale` and `nodeAt`** (review I7). Task 21 Step 3 adds the following.

- **`NodeIds = true`.** A field line ends with `  node=<INode.Id>`, and under `RANVIER_TRACE` also with
  `  trace=<the node's path in Trace.snapshot>`, for example `  columns  memo  [|…|] (3)  node=17  trace=r-0/columns`.
- **The `stale` suffix.** A region whose source changed after its last pass shows `  stale` at the end of its line: a
  list whose projection version is ahead of `previousVersion`, or an arm whose selection memo is stale.
- **Tests.**
  - The `NodeIds` line form.
  - `Debug.nodeAt (session.Mounted, "<instance path>.Count")` returns the counter's signal, compared by reference.
  - A list whose source was written inside `graph.Run` and dumped before the pass ran shows `stale`. Under synchronous
    commits that means dumping from inside the write's `batch`; under queued commits, before `RunMicrotasks`.

**A8. Smaller corrections** (review M5, M9, M10).

- `Twin.hydrateHtml` raises `NotSupportedException` for `Commits.Queued` (Task 18 code amended).
- Task 5's `ModuleInitTests` adds "disposing an installation on another thread or out of order raises", against
  `Native.install`'s checks.
- `Selector.fs` tests options with `match` (Task 11 code amended).


**A9. Phase 1 execution rulings.**

- Task 5 ports the two module-level `emitJsExpr` values, the delegated listener (`Mounting.fs`) and `keepHead`
  (`DocumentHead.fs`), because Foundation's module initialisation on .NET, which Task 5 tests, evaluates them. Task 6
  keeps `overlaps`, `deleteBetween`, the rollback snapshots, `listenDirect` and the seam.
- `document.createX (...)` calls go through `Native.createDocumentFragment`, `createComment`, `createTextNode` and
  `createElement`, whose Emit text is `document.createX($0)`; `(Native.document ()).createX` would emit
  `(document).createX`. The one call in a conditional arm (`Mounting.fs`, element creation without an SVG seam) keeps a
  Fable-only `[<Global "document">]` value, because Fable parenthesises an Emit there.
- The Task 6 divergence case adds its native listener through a check-local Emit (`stopClicks`), because the Xantham
  `addEventListener` overloads take `Event -> obj`.
- `Native.setData` and the attribute writers convert values with `Native.jsString`, which follows JavaScript's `String`
  for the values Fable writes as primitives. `Template.numberText`'s .NET body moved into `Native.numberText`.

**A10. Master at phase 2: Fickl, public seam, net8.0** (#147, #152).

- #152 replaced Fable 5.18.0 with Fickl 0.3.0 (`dotnet fickl`, `.config/dotnet-tools.json`). Every `dotnet fable` command
  in this plan is `dotnet fickl` with the same arguments; the npm scripts already call it. `FABLE_COMPILER` is still
  the define. The Fable hazards (snapshot inlining, `.IsCase`) are kept as hazards for Fickl until shown otherwise.
  The bundle and generated-JavaScript baselines were re-recorded with Fickl at the merge base.
- #147 dropped Portable's `InternalsVisibleTo` for Foundation and made the declarations Foundation uses public, marked
  as the view runtime seam. `Native` is therefore a public module documented as part of that seam (spec §3.2's "no
  public surface" now reads "no application surface"); the twin and its tests keep their `InternalsVisibleTo` entries.
- Portable and Foundation target `net10.0;net8.0` with FSharp.Core 8.0.100. `Native`'s ordered table is its own
  insertion-ordered dictionary (`OrderedDictionary<,>` is .NET 9+), and `Native.valueOrNull` replaces `Option.toObj` on
  DOM interface types, which FSharp.Core 8 types as not nullable.

**A11. Phase 1 review rulings** (`dom-twin-phase1-review.md`).

- `Native` stays `internal`. Portable regains `InternalsVisibleTo Ranvier.View.Foundation` for it; this overrides
  A10's public-module bullet.
- The A3 rule for Portable files other than `Native.fs`: every Emit and `emitJsExpr` sits in a Fable branch, and a
  name declared in a Fable branch without `#else` may be used only inside Fable branches (SvgSugar's registration and
  `Typed.fs`'s helpers are Fable-only).
- `Departures.createSeam ()` builds a fresh seam; tests use it instead of the process seam, whose `Enabled` and
  `ReflowEnabled` are process-wide and one-way on .NET.
- `TemplateDom.dispatchFor` reads the event type through `Event.type` on .NET (spec §2.1's `event?type`); Task 13's
  delegation cases cover it.
- Task 10 asserts that every listener of one dispatch receives the same event proxy, which the delegated walk's
  `$$ranvierHost` expando relies on.
- Gate 3 ignores carriage returns, and gate runs compile the branch with `--noCache`.

---

## File structure

| Path | Task | Responsibility |
|---|---|---|
| `examples/Fable.Ranvier.Playground/benchmarks/frameworks/bundle-hashes.mjs` | 1, 20 | SHA-256 of the minified sugar-size entries (gate 1); absence check (gate 2) |
| `examples/Fable.Ranvier.Playground/benchmarks/frameworks/generated-diff.mjs` | 1 | Differing Fable output files, import lines ignored (gate 3) |
| `examples/Fable.Ranvier.Playground/benchmarks/frameworks/gates.test.mjs` | 1 | Unit tests of the two gate scripts |
| `examples/Fable.Ranvier.Playground/benchmarks/frameworks/loop-time.mjs` | 2, 23 | Edit-to-result loop timings |
| `docs/research/2026-10-21-dom-twin.md`, `docs/research/2026-10-21-dom-twin/` | 1, 2, 22, 23 | Probes, baselines, inspect cost, loop results |
| `experiments/Ranvier.View.Portable/Native.fs` | 3-6 | Host primitives, both hosts |
| `tests/Ranvier.View.Twin.Tests/` | 4-22 | Expecto: Native, twin unit tests, conformance on the twin, dumps |
| `experiments/Ranvier.View.Twin/Tree.fs` | 7 | Node tree, attributes, form-control state, thread affinity |
| `experiments/Ranvier.View.Twin/Bridge.fs` | 9 | `NodeProxy`, member table, served-member record |
| `experiments/Ranvier.View.Twin/Events.fs` | 10 | Event objects, dispatch phases, activation, focus |
| `experiments/Ranvier.View.Twin/Html.fs` | 8 | Subset parser, `Html.outer`/`inner`/`server` |
| `experiments/Ranvier.View.Testing/Selector.fs` | 11 | Selector engine over the DOM interfaces |
| `experiments/Ranvier.View.Twin/Clock.fs`, `Host.fs`, `Session.fs` | 12 | Clock, `Native.IHost` implementation, session API |
| `experiments/Ranvier.View.Testing/Conformance.fs` | 13, 15 | Harness, case format, artefact recording |
| `examples/Fable.Ranvier.Playground/tests/JsdomHarness.fs`, `ConformanceCases.fs`, `conformance.test.mjs`, `conformance-diff.mjs` | 13, 15 | jsdom runner and the cross-host diff |
| `experiments/Ranvier.View.Testing/Query.fs`, `Interact.fs`, `Expect.fs` | 14 | Testing API, both hosts |
| `experiments/Ranvier.View.Foundation/Inspect.fs` | 20 | Inspection registry |
| `experiments/Ranvier.View.Foundation/Debug.fs` | 21 | `Debug.capture`, `text`, `json`, `dump`, `nodeAt` |
| `docs/content/view/testing.md`, `docs/content/guide/tracing.md` | 23 | User docs |

---

## Phase 0: baseline and probes

### Task 1: Bundle-identity and generated-JavaScript gates, with the merge-base baseline

**Files:**
- Create: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/bundle-hashes.mjs`
- Create: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/generated-diff.mjs`
- Create: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/gates.test.mjs`
- Create: `docs/research/2026-10-21-dom-twin/bundle-base.json` (generated)
- Modify: `examples/Fable.Ranvier.Playground/.gitignore` (append three lines)

**Interfaces:**
- Produces: `node benchmarks/frameworks/bundle-hashes.mjs --out <file> | --compare <file> | --absence`; `node benchmarks/frameworks/generated-diff.mjs <base> <branch> [--allow <regex>]`; `export function diffDirectories(base, branch, allow): { differing: string[], disallowed: string[] }` from `generated-diff.mjs`. Every later G-BUNDLE run uses these.

- [ ] **Step 1: Write the failing test**

`examples/Fable.Ranvier.Playground/benchmarks/frameworks/gates.test.mjs`:

```js
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { diffDirectories } from "./generated-diff.mjs";

const tree = files => {
  const root = mkdtempSync(join(tmpdir(), "gates-"));
  for (const [path, text] of Object.entries(files)) {
    mkdirSync(join(root, path, ".."), { recursive: true });
    writeFileSync(join(root, path), text);
  }
  return root;
};

test("identical trees differ nowhere", () => {
  const files = { "a/Mounting.js": "export const x = 1;\n" };
  assert.deepEqual(diffDirectories(tree(files), tree(files), /(^|\/)Native\.js$/), { differing: [], disallowed: [] });
});

test("import lines are ignored", () => {
  const base = tree({ "Mounting.js": 'import { a } from "./A.js";\nexport const x = 1;\n' });
  const branch = tree({ "Mounting.js": 'import { a } from "./A.js";\nimport { b } from "./Native.js";\nexport const x = 1;\n' });
  assert.deepEqual(diffDirectories(base, branch, /(^|\/)Native\.js$/).differing, []);
});

test("a changed body is disallowed and a new Native.js is allowed", () => {
  const base = tree({ "Mounting.js": "export const x = 1;\n" });
  const branch = tree({ "Mounting.js": "export const x = 2;\n", "experiments/Ranvier.View.Portable/Native.js": "export {};\n" });
  const result = diffDirectories(base, branch, /(^|\/)Native\.js$/);
  assert.deepEqual(result.differing.sort(), ["Mounting.js", "experiments/Ranvier.View.Portable/Native.js"]);
  assert.deepEqual(result.disallowed, ["Mounting.js"]);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd examples/Fable.Ranvier.Playground && node --test benchmarks/frameworks/gates.test.mjs`
Expected: FAIL, `Cannot find module '.../generated-diff.mjs'`.

- [ ] **Step 3: Write the two gate scripts**

`examples/Fable.Ranvier.Playground/benchmarks/frameworks/generated-diff.mjs`:

```js
// Gate 3 of the DOM twin spec (§7.1): the Fable output files that differ between a base and a branch directory, with
// lines starting `import ` ignored. Exits 1 when a differing file does not match the allowed pattern.
// Usage: node benchmarks/frameworks/generated-diff.mjs <base dir> <branch dir> [--allow <regex>]
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const filesUnder = root => {
  const result = [];
  const walk = directory => {
    for (const name of readdirSync(directory)) {
      const path = join(directory, name);
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith(".js")) result.push(relative(root, path).split(sep).join("/"));
    }
  };
  walk(root);
  return result;
};

const body = path => readFileSync(path, "utf8").split("\n").filter(line => !line.startsWith("import ")).join("\n");

export function diffDirectories(base, branch, allow) {
  const names = new Set([...filesUnder(base), ...filesUnder(branch)]);
  const differing = [];
  for (const name of [...names].sort()) {
    let left = null, right = null;
    try { left = body(join(base, name)); } catch { }
    try { right = body(join(branch, name)); } catch { }
    if (left !== right) differing.push(name);
  }
  return { differing, disallowed: differing.filter(name => !allow.test(name)) };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [base, branch] = process.argv.slice(2);
  const index = process.argv.indexOf("--allow");
  const allow = new RegExp(index < 0 ? "(^|/)Native\\.js$" : process.argv[index + 1]);
  const { differing, disallowed } = diffDirectories(base, branch, allow);
  for (const name of differing) console.log(`${disallowed.includes(name) ? "DIFFERS" : "allowed"} ${name}`);
  process.exitCode = disallowed.length ? 1 : 0;
}
```

`examples/Fable.Ranvier.Playground/benchmarks/frameworks/bundle-hashes.mjs`:

```js
// Gates 1 and 2 of the DOM twin spec (§7.1). Requires `npm run compile:tests`; builds into generated-sugar-size/ with
// the sugar-size Vite config.
// --out <file>      writes the SHA-256 of each minified entry's bundle.js
// --compare <file>  exits 1 when an entry's hash differs from the file's, naming each such entry
// --absence         exits 1 when the unminified explicit or combined bundle holds an Inspect or Debug module or a probe case
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

const cwd = new URL("../../", import.meta.url);
const entries = ["explicit", "explicit-combined", "sugared", "members", "class", "combined"];
const build = (entry, minify) => {
  execFileSync(process.execPath, ["node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/sugar-size.config.mjs"],
    { cwd, stdio: ["ignore", 2, 2], windowsHide: true, env: { ...process.env, SUGAR_SIZE_ENTRY: entry, SUGAR_SIZE_MINIFY: String(minify) } });
  return new URL(`generated-sugar-size/${entry}${minify ? "" : "-unminified"}/bundle.js`, cwd);
};
const sha = file => createHash("sha256").update(readFileSync(file)).digest("hex");
const option = name => { const i = process.argv.indexOf(name); return i < 0 ? undefined : process.argv[i + 1] ?? ""; };

if (process.argv.includes("--absence")) {
  const region = /\/\/#region [^\n]*experiments\/Ranvier\.View\.Foundation\/(Inspect|Debug)\.js/;
  const probe = /\b(ListProbe|ArmProbe|BoundaryProbe|HoleProbe|InstanceProbe|MountProbe)\b/;
  const failures = [];
  for (const entry of ["explicit", "combined"]) {
    const text = readFileSync(build(entry, false), "utf8");
    if (region.test(text)) failures.push(`${entry}: the bundle holds an Inspect or Debug module`);
    if (probe.test(text)) failures.push(`${entry}: the bundle names a probe case`);
  }
  for (const failure of failures) console.error(failure);
  process.exitCode = failures.length ? 1 : 0;
} else {
  const hashes = Object.fromEntries(entries.map(entry => [entry, sha(build(entry, true))]));
  const out = option("--out"), compare = option("--compare");
  if (out !== undefined) {
    mkdirSync(dirname(resolve(out)), { recursive: true });
    writeFileSync(resolve(out), JSON.stringify(hashes, null, 2) + "\n");
  }
  if (compare !== undefined) {
    const expected = JSON.parse(readFileSync(resolve(compare), "utf8"));
    const differing = entries.filter(entry => expected[entry] !== hashes[entry]);
    for (const entry of differing) console.error(`${entry}: ${expected[entry]} at the base, ${hashes[entry]} on the branch`);
    process.exitCode = differing.length ? 1 : 0;
  }
  console.log(JSON.stringify(hashes, null, 2));
}
```

Append to `examples/Fable.Ranvier.Playground/.gitignore`:

```
generated-tests-base/
generated-conformance/
generated-inspect/
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd examples/Fable.Ranvier.Playground && node --test benchmarks/frameworks/gates.test.mjs`
Expected: PASS, 3 tests.

- [ ] **Step 5: Record the baseline at the merge base**

The branch head is the merge base until Task 3 changes a view source; `git diff --stat 8311aa1 HEAD -- experiments examples` must print nothing.

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-dom-twin/examples/Fable.Ranvier.Playground
npm ci
npm run compile:tests
node benchmarks/frameworks/bundle-hashes.mjs --out ../../docs/research/2026-10-21-dom-twin/bundle-base.json
dotnet fable tests/DomTests.fsproj --outDir generated-tests-base --configuration Release
node benchmarks/frameworks/bundle-hashes.mjs --compare ../../docs/research/2026-10-21-dom-twin/bundle-base.json
node benchmarks/frameworks/generated-diff.mjs generated-tests-base generated-tests
```

Expected: `bundle-base.json` holds six 64-character hex hashes; both checks exit 0 and `generated-diff.mjs` prints nothing.

- [ ] **Step 6: Record the re-baseline procedure in the research folder**

After a merge of `origin/master` that touches `experiments/` or `examples/Fable.Ranvier.Playground/`, the base is rebuilt from a detached worktree of the new merge base (its own `npm ci`, never a junction):

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-dom-twin
git worktree add --detach ../Ranvier-dom-twin-base $(git merge-base HEAD origin/master)
cd ../Ranvier-dom-twin-base/examples/Fable.Ranvier.Playground
npm ci
npm run compile:tests
node benchmarks/frameworks/bundle-hashes.mjs --out C:/Users/shaya/RiderProjects/Ranvier-dom-twin/docs/research/2026-10-21-dom-twin/bundle-base.json
rtk rm -rf C:/Users/shaya/RiderProjects/Ranvier-dom-twin/examples/Fable.Ranvier.Playground/generated-tests-base
dotnet fable tests/DomTests.fsproj --outDir C:/Users/shaya/RiderProjects/Ranvier-dom-twin/examples/Fable.Ranvier.Playground/generated-tests-base --configuration Release
cd C:/Users/shaya/RiderProjects/Ranvier-dom-twin
git worktree remove ../Ranvier-dom-twin-base --force
```

The base worktree has no `bundle-hashes.mjs` until this branch merges; copy the branch's script into its `benchmarks/frameworks/` before running it, and leave it untracked there. Write these commands into `docs/research/2026-10-21-dom-twin.md` under "Re-recording the bundle baseline" (the note itself is created in Task 2; create it here with that one section if Task 2 has not run).

- [ ] **Step 7: Run the gates**

G-NET, G-FORMAT. (No `.fs` file changed; G-DOM is not required.)

- [ ] **Step 8: Commit**

```bash
rtk git add examples/Fable.Ranvier.Playground/benchmarks/frameworks/bundle-hashes.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/generated-diff.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/gates.test.mjs examples/Fable.Ranvier.Playground/.gitignore docs/research/2026-10-21-dom-twin/bundle-base.json docs/research/2026-10-21-dom-twin.md
git diff --cached --check
git commit -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/task01.txt
```

Message: `test(view): bundle identity and generated JavaScript gates for the DOM twin`.

### Task 2: jsdom loop-time baseline and the research note

**Files:**
- Create: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/loop-time.mjs`
- Create: `docs/research/2026-10-21-dom-twin/loop-jsdom.json` (generated)
- Create or modify: `docs/research/2026-10-21-dom-twin.md`

**Interfaces:**
- Produces: `node benchmarks/frameworks/loop-time.mjs --loop jsdom --runs <n> --out <file>` writing `{ loop, runs, steps: { [step]: { cold: number[], warm: number[], median: number, min: number, max: number } } }` in milliseconds. Task 23 adds `--loop twin-test`.

- [ ] **Step 1: Write the failing check**

`loop-time.mjs` validates its own output shape; the check is a one-run smoke run:

Run: `cd examples/Fable.Ranvier.Playground && node benchmarks/frameworks/loop-time.mjs --loop jsdom --runs 1 --out C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/loop-smoke.json`
Expected: FAIL, `Cannot find module`.

- [ ] **Step 2: Write `loop-time.mjs`**

```js
// Edit-to-result loop timings for the DOM twin spec (§7.2). Run behind the bench lock.
// --loop jsdom: edits tests/DelegationChecks.fs (appends, then removes, one comment line), then measures
//   compile:  `dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release` (cold: the first run
//             after `--noCache`; warm: the later runs)
//   watch:    the time from the edit to the rewrite of generated-tests/DelegationChecks.js under `dotnet fable watch`
//   node:     `node --test tests/delegation.test.mjs`
// Writes { loop, runs, steps } with per-step cold and warm samples, median, min and max, in milliseconds. The edited
// file is restored before the script exits.
import { execFileSync, spawn } from "node:child_process";
import { readFileSync, writeFileSync, statSync, existsSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";

const cwd = new URL("../../", import.meta.url);
const option = (name, fallback) => { const i = process.argv.indexOf(name); return i < 0 ? fallback : process.argv[i + 1]; };
const loop = option("--loop", "jsdom"), runs = Number(option("--runs", "10")), out = option("--out");
const fixture = new URL("tests/DelegationChecks.fs", cwd);
const emitted = new URL("generated-tests/DelegationChecks.js", cwd);
const original = readFileSync(fixture, "utf8");
const edit = i => writeFileSync(fixture, original + `\n// loop-time ${i}\n`);
const time = action => { const start = performance.now(); action(); return performance.now() - start; };
const run = (file, args) => execFileSync(file, args, { cwd, stdio: ["ignore", "ignore", 2], windowsHide: true });
const summary = samples => {
  const sorted = [...samples].sort((a, b) => a - b);
  return { median: sorted[Math.floor(sorted.length / 2)], min: sorted[0], max: sorted[sorted.length - 1] };
};
const waitForRewrite = async (since, limitMs) => {
  const start = performance.now();
  while (performance.now() - start < limitMs) {
    if (existsSync(emitted) && statSync(emitted).mtimeMs > since) return performance.now() - start;
    await new Promise(r => setTimeout(r, 20));
  }
  throw new Error(`generated-tests/DelegationChecks.js was not rewritten within ${limitMs} ms`);
};

if (loop !== "jsdom") throw new Error(`Unknown loop ${loop}; this script measures jsdom, Task 23 adds twin-test`);
const steps = { compile: { cold: [], warm: [] }, watch: { cold: [], warm: [] }, node: { cold: [], warm: [] } };
try {
  for (let i = 0; i < runs; i++) {
    edit(i);
    const bucket = i === 0 ? "cold" : "warm";
    steps.compile[bucket].push(time(() => run("dotnet", ["fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", ...(i === 0 ? ["--noCache"] : [])])));
    steps.node[bucket].push(time(() => run(process.execPath, ["--test", "tests/delegation.test.mjs"])));
  }
  const watcher = spawn("dotnet", ["fable", "watch", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release"], { cwd, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
  try {
    await new Promise((ok, fail) => { watcher.stdout.on("data", d => { if (/Watching/.test(String(d))) ok(); }); watcher.on("exit", c => fail(new Error(`fable watch exited ${c}`))); });
    for (let i = 0; i < runs; i++) {
      const since = statSync(emitted).mtimeMs;
      edit(1000 + i);
      steps.watch[i === 0 ? "cold" : "warm"].push(await waitForRewrite(since, 120000));
    }
  } finally {
    watcher.kill();
  }
} finally {
  writeFileSync(fixture, original);
}
for (const step of Object.values(steps)) Object.assign(step, summary([...step.cold, ...step.warm]));
const result = { loop, runs, steps };
if (out) { mkdirSync(dirname(resolve(out)), { recursive: true }); writeFileSync(resolve(out), JSON.stringify(result, null, 2) + "\n"); }
console.log(JSON.stringify(result, null, 2));
```

If `dotnet fable watch` prints a different ready line than `Watching`, read its first lines and match those; record the text used in the research note.

- [ ] **Step 3: Run the smoke check**

Run: `node benchmarks/frameworks/loop-time.mjs --loop jsdom --runs 1 --out C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/loop-smoke.json`
Expected: exit 0; the JSON has `steps.compile.cold`, `steps.watch.cold` and `steps.node.cold` with one sample each; `git status --short tests/DelegationChecks.fs` prints nothing.

- [ ] **Step 4: Record the baseline**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-dom-twin/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/loop-time.mjs --loop jsdom --runs 10 --out ../../docs/research/2026-10-21-dom-twin/loop-jsdom.json
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

- [ ] **Step 5: Write the research note**

`docs/research/2026-10-21-dom-twin.md` holds, in this order: a header naming the spec and the merge commit `8311aa1`; "Probes" with Appendix A.1 and A.2 of the spec copied verbatim (code and output); "Bundle baseline" naming `bundle-base.json` and the six entries; "Re-recording the bundle baseline" (Task 1 Step 6); "jsdom loop baseline" as a table of step, cold, warm median, min and max from `loop-jsdom.json`, with the machine (`[System.Environment]::ProcessorCount`, OS, `dotnet --version`, `node --version`).

- [ ] **Step 6: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 7: Commit**

Paths: `loop-time.mjs`, `docs/research/2026-10-21-dom-twin.md`, `docs/research/2026-10-21-dom-twin/loop-jsdom.json`. Message: `docs(research): DOM twin probes and the jsdom loop baseline`.

### Task 3: A draft `Native.fs` with Hydration's seven helpers

This task fixes the form every later `Native` entry takes and proves gates 1 and 3 hold for it.

**Files:**
- Create: `experiments/Ranvier.View.Portable/Native.fs`
- Modify: `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj` (compile `Native.fs` first; two `InternalsVisibleTo`)
- Modify: `experiments/Ranvier.View.Foundation/Hydration.fs:26-48`, `:297-301`
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (link `Native.fs` before `Lifetime.fs`)

**Interfaces:**
- Produces (module `internal Ranvier.View.Foundation.Native`, both hosts unless marked):
  - `isNullish: 'T -> bool`, `nextOrNull: Node -> Node`, `firstChildOf: Node -> Node`, `removeNode: Node -> unit`, `textContentOf: Node -> string`, `elementById: string -> Element`, `document: unit -> Document`
  - .NET only: `type IHost` (members below), `install: IHost -> System.IDisposable`
- Every `Native` primitive has this shape: one `#if FABLE_COMPILER` declaration with the exact Emit text, and its `#else` F# body directly after it. Files that use a primitive call it as `Native.<name>` at each call site, and the file's private declaration is deleted. (Ruling, Task 3: an inline alias delete; each use calls `Native.nextOrNull` makes Fable bind each argument to a temporary, `node_2 = node_1`, which gate 3 shows in `Hydration.js`; a direct call substitutes the argument into the Emit text and leaves the JavaScript unchanged. Every "alias" row in Tasks 4 and 5 means this: delete the declaration and call the `Native` function at its uses.)

- [ ] **Step 1: Write `Native.fs`**

```fsharp
/// <summary>
/// The host primitives of the view sources. Under Fable each is the Emit text it replaced; on .NET each is F# over the
/// <c>Fable.Core.TS.Dom</c> interfaces, the installed host, or .NET collections.
/// </summary>
module internal Ranvier.View.Foundation.Native

open Fable.Core
open Fable.Core.TS.Dom

#if !FABLE_COMPILER
/// <summary>The DOM globals of the current thread; the DOM twin installs one per document.</summary>
type IHost =
    abstract Document: Document
    abstract QueueMicrotask: (unit -> unit) -> unit
    abstract SetTimeout: (unit -> unit) * int -> obj
    abstract ClearTimeout: obj -> unit
    abstract RequestAnimationFrame: (float -> unit) -> obj
    abstract CancelAnimationFrame: obj -> unit
    /// <summary>Removes the nodes strictly between two siblings, as <c>Range.deleteContents</c> does.</summary>
    abstract DeleteBetween: Node * Node -> unit
    /// <summary>Adds an event listener and returns its removal.</summary>
    abstract Listen: target: obj * eventName: string * listener: (obj -> unit) -> (unit -> unit)
    /// <summary>Makes <c>currentTarget</c> of <c>event</c> read <c>read ()</c> until the dispatch ends.</summary>
    abstract OverrideCurrentTarget: event: obj * read: (unit -> obj) -> unit

[<Sealed; AbstractClass>]
type private HostSlot =
    [<System.ThreadStatic; DefaultValue>]
    static val mutable private current: IHost

    static member Current
        with get () = HostSlot.current
        and set (value: IHost) = HostSlot.current <- value

/// <summary>Installs <c>host</c> for the current thread until the returned value is disposed.</summary>
let install (host: IHost) : System.IDisposable =
    let previous = HostSlot.Current
    HostSlot.Current <- host

    { new System.IDisposable with
        member _.Dispose() = HostSlot.Current <- previous
    }

let private host () : IHost =
    let current = HostSlot.Current

    if isNull (box current) then
        raise (System.InvalidOperationException "No DOM host is installed on this thread; create a TwinDocument first")

    current

let inline private orNull (value: 'T option) : 'R =
    match value with
    | Some item -> unbox<'R> item
    | None -> Unchecked.defaultof<'R>
#endif

#if FABLE_COMPILER
[<Emit("document")>]
let document () : Document = jsNative
#else
let document () : Document = (host ()).Document
#endif

#if FABLE_COMPILER
[<Emit("$0 == null")>]
let isNullish (value: 'T) : bool = jsNative
#else
let isNullish (value: 'T) : bool = isNull (box value)
#endif

#if FABLE_COMPILER
[<Emit("$0.nextSibling")>]
let nextOrNull (node: Node) : Node = jsNative
#else
let nextOrNull (node: Node) : Node = orNull node.nextSibling
#endif

#if FABLE_COMPILER
[<Emit("$0.firstChild")>]
let firstChildOf (node: Node) : Node = jsNative
#else
let firstChildOf (node: Node) : Node = orNull node.firstChild
#endif

#if FABLE_COMPILER
[<Emit("$0.remove()")>]
let removeNode (node: Node) : unit = jsNative
#else
let removeNode (node: Node) : unit = (unbox<ChildNode> node).remove ()
#endif

#if FABLE_COMPILER
[<Emit("$0.textContent")>]
let textContentOf (node: Node) : string = jsNative
#else
let textContentOf (node: Node) : string = Option.toObj node.textContent
#endif

#if FABLE_COMPILER
[<Emit("document.getElementById($0)")>]
let elementById (id: string) : Element = jsNative
#else
let elementById (id: string) : Element = orNull ((document ()).getElementById id)
#endif
```

`isRootMarker` stays in `Hydration.fs` with its Emit and gains a local `#else` body in Step 2: it is a one-file predicate, and later marker predicates join `Native` in Task 5.

- [ ] **Step 2: Point `Hydration.fs` at `Native`**

Replace `Hydration.fs:26-48` (the five Emit declarations before `rootMarker`, keeping `rootMarker` and `clearAfter` unchanged) with:

```fsharp
let inline private firstChildOf (node: Node) : Node = Native.firstChildOf node

let inline private following (node: Node) : Node = Native.nextOrNull node

let inline private absent (node: Node) : bool = Native.isNullish node

#if FABLE_COMPILER
[<Emit("$0.nodeType === 8 && $0.data === '[r'")>]
let private isRootMarker (node: Node) : bool = jsNative
#else
let private isRootMarker (node: Node) : bool =
    node.nodeType = 8.0 && (unbox<CharacterData> node).data = "[r"
#endif
```

and keep delete; each use calls `Native.removeNode` where `removeNode` was declared. Replace `:297-301` with:

```fsharp
let inline private elementById (id: string) : Element = Native.elementById id

let inline private textOf (element: Element) : string = Native.textContentOf element
```

- [ ] **Step 3: Wire the projects**

In `Ranvier.View.Portable.fsproj`, insert `<Compile Include="Native.fs" />` before `<Compile Include="Lifetime.fs" />`, and add `<InternalsVisibleTo Include="Ranvier.View.Twin" />` and `<InternalsVisibleTo Include="Ranvier.View.Twin.Tests" />` after the existing three. In `DomTests.fsproj`, insert `<Compile Include="../../../experiments/Ranvier.View.Portable/Native.fs" Link="Native.fs" />` before the `Lifetime.fs` link.

- [ ] **Step 4: Run gates 1 and 3**

G-BUNDLE.
Expected: `bundle-hashes.mjs --compare` exits 0; `generated-diff.mjs` prints at most `allowed experiments/Ranvier.View.Portable/Native.js`. If `Hydration.js` differs, an alias was not inlined: diff the two files, fix the alias, rerun.

- [ ] **Step 5: Run the remaining gates**

G-DOM (the hydration and server runners are part of `test:dom`), G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT.

- [ ] **Step 6: Record the result**

Append to `docs/research/2026-10-21-dom-twin.md` under "Native form": the seven helpers moved, the gate 1 hashes (unchanged), and the gate 3 output.

- [ ] **Step 7: Commit**

Paths: `Native.fs`, `Ranvier.View.Portable.fsproj`, `Hydration.fs`, `DomTests.fsproj`, the research note. Message: `refactor(view): move Hydration's host primitives into Native`.

---
## Phase 1: `Native.fs`

### Task 4: Collections, arrays and expandos, and the twin test project

> Amended by A2 (Amendments after the phase 0 review).

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Native.fs` (append the collection, array and expando groups)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs:101-128` (module-level tables), `:156-160`, `:183-239`, `:301-311`, `:3510-3515`
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs:367-372`, `:709-718`, `:727-728`
- Modify: `experiments/Ranvier.View.Portable/Departures.fs:55-77`
- Create: `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj`, `Main.fs`, `NativeTests.fs`
- Modify: `Ranvier.slnx` (`/tests/` folder), `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj` (`InternalsVisibleTo`)

**Interfaces:**
- Consumes: Task 3's primitive shape and `Native.isNullish`.
- Produces (both hosts; tables are `obj` under Fable, `NativeTable` on .NET):
  - `newMap: unit -> obj`, `newSet: unit -> obj`, `newWeakMap: unit -> obj`, `newWeakSet: unit -> obj`
  - `has: obj -> 'K -> bool`, `get: obj -> 'K -> 'T`, `getIndex: obj -> 'K -> int` (`-1` when absent), `set: obj -> 'K -> 'T -> unit`, `add: obj -> 'K -> unit`, `delete: obj -> 'K -> unit`, `forEach: obj -> ('K -> unit) -> unit`
  - `listAt: ResizeArray<'T> -> int -> 'T`, `listSet: ResizeArray<'T> -> int -> 'T -> unit`, `listTruncate: ResizeArray<'T> -> int -> unit`, `listSplice: ResizeArray<'T> -> int -> int -> 'T array -> unit`
  - `arrayAt: 'T array -> int -> 'T`, `arraySet: 'T array -> int -> 'T -> unit`, `arrayCopy: 'T array -> 'T array`, `arraySlice: 'T array -> int -> int -> 'T array`, `allocate: int -> obj array`
  - `expando: obj -> string -> 'T`, `setExpando: obj -> string -> 'T -> unit`
  - .NET only: `type SameValueZero` (an `IEqualityComparer<obj>`), `type NativeTable`
- Produces: the Expecto executable `tests/Ranvier.View.Twin.Tests`, which later tasks extend.

- [ ] **Step 1: Merge `master` and rebase citations**

```bash
rtk git fetch origin
git merge origin/master -F C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/merge.txt
rtk grep -c "Emit(" experiments/Ranvier.View.Foundation/*.fs experiments/Ranvier.View.Portable/*.fs
```

`merge.txt` holds `Merge origin/master into feat/dom-twin`, a blank line and the two trailer lines. If the merge brought view changes, re-record the bundle baseline (Task 1 Step 6) and compare the counts with spec §2.1; add each new helper to the replacement table of this task, Task 5 or Task 6 by its kind.

- [ ] **Step 2: Create the test project and write the failing tests**

`tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <IsPackable>false</IsPackable>
        <GenerateProgramFile>false</GenerateProgramFile>
        <GenerateDocumentationFile>false</GenerateDocumentationFile>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="NativeTests.fs" />
        <Compile Include="Main.fs" />
    </ItemGroup>
    <ItemGroup>
        <PackageReference Include="Expecto" Version="11.0.0-alpha8" />
        <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.15.5" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
        <PackageReference Update="FSharp.Core" Version="10.1.401" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="../../src/Ranvier/Ranvier.fsproj" />
        <ProjectReference Include="../../experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj" />
        <ProjectReference Include="../../experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj" />
    </ItemGroup>
</Project>
```

`Main.fs`:

```fsharp
module Ranvier.View.Twin.Tests.Main

open Expecto

[<EntryPoint>]
let main argv = runTestsInAssemblyWithCLIArgs [] argv
```

`NativeTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.NativeTests

open Expecto
open Ranvier.View.Foundation

type private Row = { Id: int }

[<Tests>]
let tests =
    testList
        "Native"
        [
            testCase "a map keys strings, numbers and booleans by value"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map "a" 1
                Native.set map 2 2
                Native.set map true 3
                Expect.equal (Native.getIndex map ("a" + "")) 1 "string key"
                Expect.equal (Native.getIndex map 2.0) 2 "an int and a float of equal value are one key"
                Expect.equal (Native.getIndex map true) 3 "boolean key"
                Expect.equal (Native.getIndex map "missing") -1 "absent key"

            testCase "SameValueZero keys erased Fable primitives by value"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map (Some 7) 1
                Native.set map 'x' 2
                Native.set map 9L 3
                Expect.equal (Native.getIndex map 7) 1 "Some 7 is the key 7"
                Expect.equal (Native.getIndex map "x") 2 "a char is a one-character string"
                Expect.equal (Native.getIndex map 9L) 3 "an int64 keys by value"
                Expect.equal (Native.getIndex map nan) -1 "NaN is absent"
                Native.set map nan 4
                Expect.equal (Native.getIndex map nan) 4 "NaN equals NaN"
                Native.set map -0.0 5
                Expect.equal (Native.getIndex map 0.0) 5 "-0 equals +0"

            testCase "records key by reference"
            <| fun () ->
                let map = Native.newMap ()
                Native.set map { Id = 1 } 1
                Expect.equal (Native.getIndex map { Id = 1 }) -1 "a structurally equal record is another key"

            testCase "a set iterates in insertion order over a snapshot"
            <| fun () ->
                let set = Native.newSet ()

                for name in [ "click"; "input"; "focusin" ] do
                    Native.add set name

                let seen = ResizeArray<string>()

                Native.forEach set (fun (name: string) ->
                    seen.Add name
                    Native.add set (name + "!"))

                Expect.equal (List.ofSeq seen) [ "click"; "input"; "focusin" ] "insertion order; entries added during the walk are not visited"

            testCase "a weak map holds a reachable key until it is deleted"
            <| fun () ->
                let key = obj ()
                let table = Native.newWeakMap ()
                Native.set table key "v"
                Expect.isTrue (Native.has table key) "held"
                Expect.equal (Native.get<obj, string> table key) "v" "value"
                Native.delete table key
                Expect.isFalse (Native.has table key) "deleted"

            testCase "list reads past the end are null and writes at the end append"
            <| fun () ->
                let items = ResizeArray<string> [ "a" ]
                Expect.isNull (Native.listAt items 3) "past the end"
                Native.listSet items 1 "b"
                Expect.equal (List.ofSeq items) [ "a"; "b" ] "appended"
                Native.listSplice items 0 1 [| "x"; "y" |]
                Expect.equal (List.ofSeq items) [ "x"; "y"; "b" ] "spliced"
                Native.listTruncate items 1
                Expect.equal (List.ofSeq items) [ "x" ] "truncated"

            testCase "array slices clamp and reads past the end are null"
            <| fun () ->
                let rows = [| "a"; "b"; "c" |]
                Expect.equal (Native.arraySlice rows 1 10) [| "b"; "c" |] "clamped"
                Expect.isNull (Native.arrayAt rows 5) "past the end"
                Expect.equal (Native.allocate 3).Length 3 "allocated"

            testCase "expandos round trip per object"
            <| fun () ->
                let a, b = obj (), obj ()
                Native.setExpando a "$$ranvierWidget" true
                Expect.equal (Native.expando<bool> a "$$ranvierWidget") true "set"
                Expect.isNull (Native.expando<obj> b "$$ranvierWidget") "another object"
        ]
```

Add to `Ranvier.slnx`, in the `/tests/` folder: `<Project Path="tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj" />`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The value, constructor, namespace or type 'newMap' is not defined`.

- [ ] **Step 4: Append the .NET collection types and the three groups to `Native.fs`**

```fsharp
#if !FABLE_COMPILER
/// <summary>
/// JavaScript's SameValueZero over the values Fable writes as primitives: strings, chars, numbers, booleans and
/// <c>Some</c> of those compare by value, NaN equals NaN and -0 equals +0; every other value compares by reference.
/// </summary>
[<Sealed>]
type SameValueZero private () =
    static member val Instance = SameValueZero ()

    static member internal Normal(value: obj) : obj =
        match value with
        | null -> null
        | :? string -> value
        | :? char as c -> box (string c)
        | :? bool -> value
        | :? int8
        | :? uint8
        | :? int16
        | :? uint16
        | :? int32
        | :? uint32
        | :? float32
        | :? float -> box (System.Convert.ToDouble value)
        | _ ->
            let t = value.GetType ()

            if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<option<_>> then
                SameValueZero.Normal (t.GetProperty("Value").GetValue value)
            else
                value

    interface System.Collections.Generic.IEqualityComparer<obj> with
        member _.Equals(a, b) =
            match SameValueZero.Normal a, SameValueZero.Normal b with
            | null, null -> true
            | (:? string as x), (:? string as y) -> System.String.Equals (x, y, System.StringComparison.Ordinal)
            | (:? float as x), (:? float as y) -> x = y || (System.Double.IsNaN x && System.Double.IsNaN y)
            | (:? System.ValueType as x), (:? System.ValueType as y) -> x.GetType () = y.GetType () && x.Equals y
            | x, y -> obj.ReferenceEquals (x, y)

        member _.GetHashCode a =
            match SameValueZero.Normal a with
            | null -> 0
            | :? string as s -> System.StringComparer.Ordinal.GetHashCode s
            | :? float as f -> if System.Double.IsNaN f then 0x7ff8 elif f = 0.0 then 0 else f.GetHashCode ()
            | :? System.ValueType as v -> v.GetHashCode ()
            | other -> System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode other

/// <summary>A JavaScript <c>Map</c>, <c>Set</c>, <c>WeakMap</c> or <c>WeakSet</c> on .NET; a set stores each key as its own value.</summary>
[<Sealed; AllowNullLiteral>]
type NativeTable(weak: bool) =
    let ordered =
        if weak then null else System.Collections.Generic.OrderedDictionary<obj, obj> (SameValueZero.Instance)

    let held =
        if weak then System.Runtime.CompilerServices.ConditionalWeakTable<obj, obj> () else null

    member _.Has(key: obj) =
        if weak then
            let mutable value = null
            held.TryGetValue (key, &value)
        else
            ordered.ContainsKey key

    member _.Get(key: obj) : obj =
        let mutable value = null

        if weak then
            held.TryGetValue (key, &value) |> ignore
        else
            ordered.TryGetValue (key, &value) |> ignore

        value

    member _.Set(key: obj, value: obj) =
        if weak then held.AddOrUpdate (key, value) else ordered[key] <- value

    member _.Delete(key: obj) =
        if weak then held.Remove key |> ignore else ordered.Remove key |> ignore

    member _.Keys: obj array =
        if weak then invalidOp "A weak table cannot be enumerated" else Seq.toArray ordered.Keys

let private table (value: obj) : NativeTable = value :?> NativeTable

let private expandos =
    System.Runtime.CompilerServices.ConditionalWeakTable<obj, System.Collections.Generic.Dictionary<string, obj>> ()
#endif

// Collections

#if FABLE_COMPILER
[<Emit("new Map()")>]
let newMap () : obj = jsNative

[<Emit("new Set()")>]
let newSet () : obj = jsNative

[<Emit("new WeakMap()")>]
let newWeakMap () : obj = jsNative

[<Emit("new WeakSet()")>]
let newWeakSet () : obj = jsNative
#else
let newMap () : obj = NativeTable false
let newSet () : obj = NativeTable false
let newWeakMap () : obj = NativeTable true
let newWeakSet () : obj = NativeTable true
#endif

#if FABLE_COMPILER
[<Emit("$0.has($1)")>]
let has (tableValue: obj) (key: 'K) : bool = jsNative
#else
let has (tableValue: obj) (key: 'K) : bool = (table tableValue).Has (box key)
#endif

#if FABLE_COMPILER
[<Emit("$0.get($1)")>]
let get (tableValue: obj) (key: 'K) : 'T = jsNative
#else
let get (tableValue: obj) (key: 'K) : 'T =
    match (table tableValue).Get (box key) with
    | null -> Unchecked.defaultof<'T>
    | value -> unbox<'T> value
#endif

#if FABLE_COMPILER
[<Emit("($0.get($1) ?? -1)")>]
let getIndex (tableValue: obj) (key: 'K) : int = jsNative
#else
let getIndex (tableValue: obj) (key: 'K) : int =
    match (table tableValue).Get (box key) with
    | null -> -1
    | value -> unbox<int> value
#endif

#if FABLE_COMPILER
[<Emit("$0.set($1, $2)")>]
let set (tableValue: obj) (key: 'K) (value: 'T) : unit = jsNative
#else
let set (tableValue: obj) (key: 'K) (value: 'T) : unit = (table tableValue).Set (box key, box value)
#endif

#if FABLE_COMPILER
[<Emit("$0.add($1)")>]
let add (tableValue: obj) (key: 'K) : unit = jsNative
#else
let add (tableValue: obj) (key: 'K) : unit = (table tableValue).Set (box key, box key)
#endif

#if FABLE_COMPILER
[<Emit("$0.delete($1)")>]
let delete (tableValue: obj) (key: 'K) : unit = jsNative
#else
let delete (tableValue: obj) (key: 'K) : unit = (table tableValue).Delete (box key)
#endif

#if FABLE_COMPILER
[<Emit("$0.forEach($1)")>]
let forEach (tableValue: obj) (action: 'K -> unit) : unit = jsNative
#else
let forEach (tableValue: obj) (action: 'K -> unit) : unit =
    for key in (table tableValue).Keys do
        action (unbox<'K> key)
#endif

// Arrays: a JavaScript read past the end is undefined and a write at the end appends.

#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let listAt (items: ResizeArray<'T>) (index: int) : 'T = jsNative

[<Emit("$0[$1] = $2")>]
let listSet (items: ResizeArray<'T>) (index: int) (value: 'T) : unit = jsNative

[<Emit("$0.length = $1")>]
let listTruncate (items: ResizeArray<'T>) (count: int) : unit = jsNative

[<Emit("$0.splice($1, $2, ...$3)")>]
let listSplice (items: ResizeArray<'T>) (start: int) (count: int) (inserted: 'T array) : unit = jsNative

[<Emit("$0[$1]")>]
let arrayAt (items: 'T array) (index: int) : 'T = jsNative

[<Emit("$0[$1] = $2")>]
let arraySet (items: 'T array) (index: int) (value: 'T) : unit = jsNative

[<Emit("$0.slice()")>]
let arrayCopy (items: 'T array) : 'T array = jsNative

[<Emit("$0.slice($1, $2)")>]
let arraySlice (items: 'T array) (start: int) (finish: int) : 'T array = jsNative

[<Emit("new Array($0)")>]
let allocate (length: int) : obj array = jsNative
#else
let listAt (items: ResizeArray<'T>) (index: int) : 'T =
    if index >= 0 && index < items.Count then items[index] else Unchecked.defaultof<'T>

let listSet (items: ResizeArray<'T>) (index: int) (value: 'T) : unit =
    if index = items.Count then items.Add value else items[index] <- value

let listTruncate (items: ResizeArray<'T>) (count: int) : unit =
    if count < items.Count then items.RemoveRange (count, items.Count - count)

let listSplice (items: ResizeArray<'T>) (start: int) (count: int) (inserted: 'T array) : unit =
    items.RemoveRange (start, min count (items.Count - start))
    items.InsertRange (start, inserted)

let arrayAt (items: 'T array) (index: int) : 'T =
    if index >= 0 && index < items.Length then items[index] else Unchecked.defaultof<'T>

let arraySet (items: 'T array) (index: int) (value: 'T) : unit = items[index] <- value

let arrayCopy (items: 'T array) : 'T array = Array.copy items

let arraySlice (items: 'T array) (start: int) (finish: int) : 'T array =
    let finish = min finish items.Length
    if finish <= start then [||] else Array.sub items start (finish - start)

let allocate (length: int) : obj array = Array.zeroCreate length
#endif

// Expandos: the per-object properties Fable writes as JavaScript properties.

#if FABLE_COMPILER
[<Emit("$0[$1]")>]
let expando (target: obj) (name: string) : 'T = jsNative

[<Emit("$0[$1] = $2")>]
let setExpando (target: obj) (name: string) (value: 'T) : unit = jsNative
#else
let expando (target: obj) (name: string) : 'T =
    let mutable slots = null

    if expandos.TryGetValue (target, &slots) then
        match slots.TryGetValue name with
        | true, value -> unbox<'T> value
        | false, _ -> Unchecked.defaultof<'T>
    else
        Unchecked.defaultof<'T>

let setExpando (target: obj) (name: string) (value: 'T) : unit =
    let slots =
        expandos.GetValue (target, fun _ -> System.Collections.Generic.Dictionary<string, obj> ())

    slots[name] <- box value
#endif
```

The named expando helpers keep their own Emit text (Step 5); under Fable, `expando` and `setExpando` serve only code that runs on both hosts by design, such as Inspect (Task 20).

- [ ] **Step 5: Replace the helpers with direct `Native` calls**

Each row deletes the declaration and changes its uses to the `Native` call shown (the Task 3 ruling: an inline alias makes Fable bind arguments to temporaries, which gate 3 rejects). Step 7's gate 3 confirms that each `Native` Emit string is the text of the row's original.

| File | Declaration | Replacement |
|---|---|---|
| `Mounting.fs:112-113` | `isDelegated` | delete; each use calls `Native.has` |
| `Mounting.fs:183-184` | `at` | delete; each use calls `Native.listAt` |
| `Mounting.fs:187-188` | `setAt` | delete; each use calls `Native.listSet` |
| `Mounting.fs:191-192` | `truncate` | delete; each use calls `Native.listTruncate` |
| `Mounting.fs:195-196` | `item` | delete; each use calls `Native.arrayAt` |
| `Mounting.fs:199-200` | `setItem` | delete; each use calls `Native.arraySet` |
| `Mounting.fs:202-203` | `isNullish` | delete; each use calls `Native.isNullish` |
| `Mounting.fs:205-206` | `copyRows` | delete; each use calls `Native.arrayCopy` |
| `Mounting.fs:213-214` | `spliceList` | delete; each use calls `Native.listSplice` |
| `Mounting.fs:216-217` | `sliceRows` | delete; each use calls `Native.arraySlice` |
| `Mounting.fs:222-223` | `rowMap` | delete; each use calls `Native.newMap` |
| `Mounting.fs:225-226` | `rowMapSet` | delete; each use calls `Native.set` |
| `Mounting.fs:229-230` | `rowMapGet` | delete; each use calls `Native.getIndex` |
| `Mounting.fs:232-233` | `rowSet` | delete; each use calls `Native.newSet` |
| `Mounting.fs:235-236` | `rowSetAdd` | delete; each use calls `Native.add` |
| `Mounting.fs:238-239` | `rowSetHas` | delete; each use calls `Native.has` |
| `Mounting.fs:301-302` | `nameSet` | delete; each use calls `Native.newSet` |
| `Mounting.fs:304-305` | `setHas` | delete; each use calls `Native.has` |
| `Mounting.fs:307-308` | `setAdd` | delete; each use calls `Native.add` |
| `Mounting.fs:310-311` | `setIter` | delete; each use calls `Native.forEach` |
| `Mounting.fs:118-119` | `createSharedListeners` | delete; each use calls `Native.newMap` |
| `Mounting.fs:124-125` | `createHostCounts` | delete; each use calls `Native.newWeakMap` |
| `TemplateDom.fs:709-710` | `allocate` | delete; each use calls `Native.allocate` |
| `TemplateDom.fs:713-714` | `at` | delete; each use calls `Native.arrayAt` |
| `TemplateDom.fs:717-718` | `store` | delete; each use calls `Native.arraySet` |
| `Departures.fs:55-56` | `weakMap` | delete; each use calls `Native.newWeakMap` |
| `Departures.fs:58-59` | `weakSet` | delete; each use calls `Native.newWeakSet` |
| `Departures.fs:61-62` | `lookup` | delete; each use calls `Native.get` |
| `Departures.fs:64-65` | `store` | delete; each use calls `Native.set` |
| `Departures.fs:67-68` | `holds` | delete; each use calls `Native.has` |
| `Departures.fs:70-71` | `forget` | delete; each use calls `Native.delete` |
| `Departures.fs:73-74` | `mark` | delete; each use calls `Native.add` |
| `Departures.fs:76-77` | `isNullish` | delete; each use calls `Native.isNullish` |

The module-level tables keep their place (spec risk 4). `createDelegatedEvents` keeps its Emit under `#if FABLE_COMPILER` and gains a .NET body:

```fsharp
#if FABLE_COMPILER
[<Emit("new Set(['beforeinput', 'click', 'contextmenu', 'dblclick', 'focusin', 'focusout', 'input', 'keydown', 'keyup', 'mousedown', 'mousemove', 'mouseout', 'mouseover', 'mouseup', 'pointerdown', 'pointermove', 'pointerout', 'pointerover', 'pointerup', 'touchend', 'touchmove', 'touchstart'])")>]
let private createDelegatedEvents () : obj = jsNative
#else
let private createDelegatedEvents () : obj =
    let events = Native.newSet ()

    for name in
        [
            "beforeinput"; "click"; "contextmenu"; "dblclick"; "focusin"; "focusout"; "input"; "keydown"; "keyup"
            "mousedown"; "mousemove"; "mouseout"; "mouseover"; "mouseup"; "pointerdown"; "pointermove"; "pointerout"
            "pointerover"; "pointerup"; "touchend"; "touchmove"; "touchstart"
        ] do
        Native.add events name

    events
#endif
```

Copy the Emit string from `Mounting.fs:106`; the F# list must equal it name for name.

The named expandos keep their Emit text and gain .NET bodies over `Native.expando`:

```fsharp
#if FABLE_COMPILER
[<Emit("$0.$$ranvierWidget === true")>]
let private isWidget (node: Node) : bool = jsNative

[<Emit("$0.$$ranvierWidget = true")>]
let private markWidget (node: Node) : unit = jsNative

[<Emit("$0['$$ranvier:' + $1] = $2")>]
let private setDelegatedHandler (node: Node) (eventName: string) (handler: obj -> unit) : unit = jsNative
#else
let private isWidget (node: Node) : bool = Native.expando<bool> node "$$ranvierWidget"

let private markWidget (node: Node) : unit = Native.setExpando node "$$ranvierWidget" true

let private setDelegatedHandler (node: Node) (eventName: string) (handler: obj -> unit) : unit =
    Native.setExpando node ("$$ranvier:" + eventName) handler
#endif
```

In `TemplateDom.fs:367-372` and `:727-728`, the same pattern: `attachScope` → `Native.setExpando node "$$ranvierScope" scope`; `scopeOf` → `Native.expando<ViewScope> node "$$ranvierScope"`; `attachState` → `Native.setExpando node "$$ranvier" state`. The .NET keys are the F# literals of the Emit strings, so the delegated listener's .NET body (Task 6) reads the same keys.

`spliceRows` (`Mounting.fs:209-210`) has no .NET body: a .NET array cannot change length. Keep its declaration under `#if FABLE_COMPILER`, and give its one call site (`Mounting.fs:3513-3515`) a local `#if`:

```fsharp
                                        then
#if FABLE_COMPILER
                                            let next = copyRows priorRows
                                            spliceRows next start removed (Array.init inserted (fun offset -> lookup (start + offset)))
                                            next
#else
                                            Array.concat
                                                [|
                                                    Array.sub priorRows 0 start
                                                    Array.init inserted (fun offset -> lookup (start + offset))
                                                    Array.sub priorRows (start + removed) (priorRows.Length - start - removed)
                                                |]
#endif
```

- [ ] **Step 6: Add the Foundation `InternalsVisibleTo`**

In `Ranvier.View.Foundation.fsproj`'s `ItemGroup`: `<InternalsVisibleTo Include="Ranvier.View.Twin" />` and `<InternalsVisibleTo Include="Ranvier.View.Twin.Tests" />`.

- [ ] **Step 7: Run the tests and the gates**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 8 tests.

Then G-BUNDLE (no disallowed file), G-DOM, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT.

- [ ] **Step 8: Commit**

Paths: `Native.fs`, `Mounting.fs`, `TemplateDom.fs`, `Departures.fs`, `Ranvier.View.Foundation.fsproj`, `Ranvier.slnx`, the three test-project files. Message: `refactor(view): Native collections, arrays and expandos with .NET bodies`.

### Task 5: Node primitives, globals and the host

> Amended by A2, A8 (Amendments after the phase 0 review).

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Native.fs` (append the node, document, timer and microtask groups)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs:16-20`, `:150-180`, `:243-250`, and each use of `document` and `globalThis` in the file
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs:11-12`, `:156-170`, `:424-428`, `:720-724`, `:737-745`, `:1134-1144`, `:1190-1195`, `:1260-1261`
- Modify: `experiments/Ranvier.View.Foundation/DocumentHead.fs:12-40`, `:131-142`
- Modify: `experiments/Ranvier.View.Foundation/Placement.fs:222-227`, `Prepared.fs:20-21`, `Microtask.fs`, `experiments/Ranvier.View.Portable/Departures.fs:79-92`
- Create: `tests/Ranvier.View.Twin.Tests/ModuleInitTests.fs`

**Interfaces:**
- Consumes: Task 3's `IHost`, `install`, `document`; Task 4's tables.
- Produces (both hosts): `parentOrNull: Node -> Node`, `lastChildOf: Node -> Node`, `isComment: Node -> bool`, `dataOf: Node -> string`, `setData: obj -> obj -> unit`, `isChildOf: Node -> Node -> bool`, `isDetached: Node -> bool`, `clearChildren: Node -> unit`, `detach: Node -> unit`, `appendNode: Node -> Node -> unit`, `replaceNode: Node -> Node -> Node -> unit`, `importDeep: Node -> Node`, `setAttributeOrRemove: obj -> obj -> string -> unit`, `createElementIn: Document -> string -> Element`, `documentOf: Node -> Document`, `queueMicrotask: (unit -> unit) -> unit`, `setTimeout: (unit -> unit) -> int -> obj`, `clearTimeout: obj -> unit`, `newNodeWeakSet: unit -> Fable.Core.TS.Es.WeakSet<Node>`.
- Predicates used by one file (`isStartMarker`, `isRegionStart`, `isSeparator`, `isRowMarker`, `isEmptyText`, `sameElementName`, `isElementNamed`, `isRcdataElement`, `fillsFragment`, `atRoot`, `dataDiffers`, `attributeDiffers`, `defaultValueDiffers`, the DocumentHead queries) stay in their files with a local `#if` pair.

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/ModuleInitTests.fs` (compile it before `Main.fs`):

```fsharp
module Ranvier.View.Twin.Tests.ModuleInitTests

open System
open System.Runtime.CompilerServices
open Expecto
open Ranvier.View.Foundation

let private startupTypes (assembly: Reflection.Assembly) =
    assembly.GetTypes ()
    |> Array.filter (fun t -> t.FullName.StartsWith "<StartupCode$")

let private recordingHost (posted: ResizeArray<unit -> unit>) =
    { new Native.IHost with
        member _.Document = Unchecked.defaultof<_>
        member _.QueueMicrotask callback = posted.Add callback
        member _.SetTimeout(_, _) = null
        member _.ClearTimeout _ = ()
        member _.RequestAnimationFrame _ = null
        member _.CancelAnimationFrame _ = ()
        member _.DeleteBetween(_, _) = ()
        member _.Listen(_, _, _) = ignore
        member _.OverrideCurrentTarget(_, _) = ()
    }

[<Tests>]
let tests =
    testList
        "Module initialisation"
        [
            testCase "Portable and Foundation initialise on .NET with no host"
            <| fun () ->
                for assembly in [ typeof<Scheduling.Queue>.Assembly; typeof<ScopePhase>.Assembly ] do
                    for t in startupTypes assembly do
                        try
                            RuntimeHelpers.RunClassConstructor t.TypeHandle
                        with :? TypeInitializationException as e ->
                            failtestf "%s failed to initialise: %s" t.FullName (e.GetBaseException ()).Message

            testCase "reading the document with no host names TwinDocument"
            <| fun () ->
                let e = Expect.throwsC (fun () -> Native.document () |> ignore) id
                Expect.isTrue (e :? InvalidOperationException) "InvalidOperationException"
                Expect.stringContains e.Message "create a TwinDocument first" "message"

            testCase "an installed host serves the thread until it is disposed"
            <| fun () ->
                let posted = ResizeArray<unit -> unit>()

                do
                    use _ = Native.install (recordingHost posted)
                    Native.queueMicrotask ignore
                    Expect.equal posted.Count 1 "posted through the host"

                Expect.throwsT<InvalidOperationException> (fun () -> Native.queueMicrotask ignore) "uninstalled"
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `queueMicrotask is not defined`; once Step 3 adds it, the first test fails with `<StartupCode$Ranvier-View-Foundation>.$Ranvier.View.Foundation.Mounting failed to initialise: You've hit dummy code used for Fable bindings` until Step 4 is done.

- [ ] **Step 3: Append the node, document, timer and microtask groups to `Native.fs`**

Before writing each Emit string, copy it from the cited declaration.

```fsharp
// Nodes

#if FABLE_COMPILER
[<Emit("$0.parentNode")>]
let parentOrNull (node: Node) : Node = jsNative
#else
let parentOrNull (node: Node) : Node = orNull node.parentNode
#endif

#if FABLE_COMPILER
[<Emit("$0.lastChild")>]
let lastChildOf (node: Node) : Node = jsNative
#else
let lastChildOf (node: Node) : Node = orNull node.lastChild
#endif

#if FABLE_COMPILER
[<Emit("$0.nodeType === 8")>]
let isComment (node: Node) : bool = jsNative
#else
let isComment (node: Node) : bool = node.nodeType = 8.0
#endif

#if FABLE_COMPILER
[<Emit("$0.data")>]
let dataOf (node: Node) : string = jsNative
#else
let dataOf (node: Node) : string = (unbox<CharacterData> node).data
#endif

#if FABLE_COMPILER
[<Emit("$0.data = $1 == null ? '' : $1")>]
let setData (node: obj) (value: obj) : unit = jsNative
#else
let setData (node: obj) (value: obj) : unit =
    (unbox<CharacterData> node).data <- (if isNull value then "" else string value)
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode === $1")>]
let isChildOf (node: Node) (parent: Node) : bool = jsNative
#else
let isChildOf (node: Node) (parent: Node) : bool = obj.ReferenceEquals (parentOrNull node, parent)
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode == null")>]
let isDetached (node: Node) : bool = jsNative
#else
let isDetached (node: Node) : bool = isNull (box (parentOrNull node))
#endif

#if FABLE_COMPILER
[<Emit("$0.textContent = ''")>]
let clearChildren (parent: Node) : unit = jsNative
#else
let clearChildren (parent: Node) : unit = parent.textContent <- Some ""
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode != null && $0.parentNode.removeChild($0)")>]
let detach (node: Node) : unit = jsNative
#else
let detach (node: Node) : unit =
    let parent = parentOrNull node

    if not (isNull (box parent)) then
        parent.removeChild node |> ignore
#endif

#if FABLE_COMPILER
[<Emit("$0.appendChild($1)")>]
let appendNode (parent: Node) (node: Node) : unit = jsNative
#else
let appendNode (parent: Node) (node: Node) : unit = parent.appendChild node |> ignore
#endif

#if FABLE_COMPILER
[<Emit("$0.parentNode.replaceChild($1, $2)")>]
let replaceNode (existing: Node) (replacement: Node) (replaced: Node) : unit = jsNative
#else
let replaceNode (existing: Node) (replacement: Node) (replaced: Node) : unit =
    (parentOrNull existing).replaceChild (replacement, replaced) |> ignore
#endif

#if FABLE_COMPILER
[<Emit("document.importNode($0, true)")>]
let importDeep (node: Node) : Node = jsNative
#else
let importDeep (node: Node) : Node = (document ()).importNode (node, Some true)
#endif

#if FABLE_COMPILER
[<Emit("$1 == null ? $0.removeAttribute($2) : $0.setAttribute($2, $1)")>]
let setAttributeOrRemove (node: obj) (value: obj) (name: string) : unit = jsNative
#else
let setAttributeOrRemove (node: obj) (value: obj) (name: string) : unit =
    let element = unbox<Element> node

    if isNull value then
        element.removeAttribute name
    else
        element.setAttribute (name, string value)
#endif

// Documents

#if FABLE_COMPILER
[<Emit("$0.createElement($1)")>]
let createElementIn (owner: Document) (tag: string) : Element = jsNative
#else
let createElementIn (owner: Document) (tag: string) : Element = owner.createElement (tag, None) :> Element
#endif

#if FABLE_COMPILER
[<Emit("($0.ownerDocument ?? document)")>]
let documentOf (node: Node) : Document = jsNative
#else
let documentOf (node: Node) : Document =
    match node.ownerDocument with
    | Some owner -> owner
    | None -> document ()
#endif

// Timers

#if FABLE_COMPILER
[<Emit("setTimeout($0, $1)")>]
let setTimeout (action: unit -> unit) (milliseconds: int) : obj = jsNative

[<Emit("clearTimeout($0)")>]
let clearTimeout (timer: obj) : unit = jsNative
#else
let setTimeout (action: unit -> unit) (milliseconds: int) : obj = (host ()).SetTimeout (action, milliseconds)

let clearTimeout (timer: obj) : unit = (host ()).ClearTimeout timer
#endif
```

Two primitives replace `[<Global>]` member calls rather than Emit helpers. Their Fable form must reproduce what Fable writes today for `globalThis.queueMicrotask callback` (`Microtask.fs:9-10`) and `globalThis.WeakSet.Create ()` (`Mounting.fs:101-102`): read both in `generated-tests-base/experiments/Ranvier.View.Foundation/Microtask.js` and `Mounting.js`, and use that text as the Emit string. The block below assumes Fable wrote `globalThis.queueMicrotask(callback)` and `new globalThis.WeakSet()`; replace either string if the base output differs:

```fsharp
#if FABLE_COMPILER
[<Emit("globalThis.queueMicrotask($0)")>]
let queueMicrotask (callback: unit -> unit) : unit = jsNative

[<Emit("new globalThis.WeakSet()")>]
let newNodeWeakSet () : Fable.Core.TS.Es.WeakSet<Node> = jsNative
#else
let queueMicrotask (callback: unit -> unit) : unit = (host ()).QueueMicrotask callback

/// <summary>A JavaScript <c>WeakSet</c> of nodes.</summary>
[<Sealed>]
type private NodeWeakSet() =
    let held = System.Runtime.CompilerServices.ConditionalWeakTable<Node, obj> ()

    interface Fable.Core.TS.Es.WeakSet<Node> with
        member this.add value =
            held.AddOrUpdate (value, null)
            this :> _

        member _.delete value = held.Remove value

        member _.has value =
            let mutable found = null
            held.TryGetValue (value, &found)

let newNodeWeakSet () : Fable.Core.TS.Es.WeakSet<Node> = NodeWeakSet ()
#endif
```

Read the members of `Fable.Core.TS.Es.WeakSet<'T>` with `fcs_nuget_members` on `Xantham.Fable.Core.TS` before writing the class; implement any further member with `raise (System.NotSupportedException "WeakSet.<member> is not used by the view")`.

- [ ] **Step 4: Replace the declarations and globals**

| File | Declarations | Replacement |
|---|---|---|
| `Mounting.fs:16-20` | `[<Global>] globalThis`, `[<Global "document">] document` | delete both; `createClaims ()` returns `Native.newNodeWeakSet ()`; each `document.` use becomes `(Native.document ()).` |
| `Mounting.fs:150-154`, `:163-164`, `:168-169`, `:179-180` | `isChildOf`, `isDetached`, `nextOrNull`, `clearChildren`, `parentOrNull` | delete; each use calls the `Native` function of the same name |
| `Mounting.fs:176-177`, `:243-250` | `fillsFragment`, `isSeparator`, `isRowMarker`, `commentData` | local `#if` pairs; .NET bodies over `Native.parentOrNull`, `isComment`, `dataOf`, `firstChildOf`, `lastChildOf` and `nodeType = 11.0` |
| `TemplateDom.fs:11-12` | `[<Global "document">] document` | delete; each use becomes `(Native.document ())` |
| `TemplateDom.fs:156-166` | `absent`, `isComment`, `dataOf`, `following` | delete; each use calls `Native.isNullish`, `Native.isComment`, `Native.dataOf` or `Native.nextOrNull` |
| `TemplateDom.fs:169-170`, `:424-425`, `:1143-1144`, `:1190-1195`, `:1260-1261` | `isStartMarker`, `isRegionStart`, `isEmptyText`, `sameElementName`, `isElementNamed`, `isRcdataElement` | local `#if` pairs; each .NET body transcribes its JavaScript expression over the `Native` node primitives and `Element.localName`, `Element.prefix` (an option) and `Node.nodeName` |
| `TemplateDom.fs:427-428`, `:720-724`, `:1134-1141` | `importDeep`, `setData`, `setAttribute`, `replaceNode`, `appendNode`, `removeNode` | delete; each use calls the `Native` function of the same name (`setAttribute`'s uses call `Native.setAttributeOrRemove`) |
| `TemplateDom.fs:737-745` | `dataDiffers`, `attributeDiffers`, `defaultValueDiffers` | local `#if` pairs; .NET: `Native.dataOf (unbox node) <> (if isNull value then "" else string value)`; `Option.toObj ((unbox<Element> node).getAttribute name) <> (if isNull value then null else string value)`; `defaultValue` read through `unbox<HTMLInputElement>` (the twin bridge serves `defaultValue` on every form control) |
| `DocumentHead.fs:12-40`, `:131-142` | the head helpers | `createElement`: delete, and each use calls `Native.createElementIn`; `documentOf` is public and inlined into other files (`Hydration.fs`), so it keeps its own Emit under `#if FABLE_COMPILER` with an `#else` body that calls `Native.documentOf` (a plain function would emit a call where Fable now inlines the expression); `replaceWith`, `detach`, `isConnected`, `markOf`, `appendToHead`, `marked`, `hasMarked`, `removeUnclaimed`, `titleOf`, `textOf`, `setTitleOf` → local `#if` pairs, the .NET bodies over `Document.head`, `head.querySelectorAll "[data-ranvier-head]"`, `getAttribute "data-ranvier-head"`, `ChildNode.replaceWith`, `Node.isConnected` and `Document.title` |
| `Placement.fs:222-227` | `parentOf`, `atRoot` | `parentOf`: delete, and its uses call `Native.parentOrNull`; a local `#if` pair for `atRoot` (`parentOrNull node` null or `nodeType = 11.0`) |
| `Prepared.fs:20-21` | `[<Global "document">] document` | delete; uses become `(Native.document ())` |
| `Microtask.fs:6-10` | `[<Global>] globalThis`, `post` | `let private post callback = Native.queueMicrotask callback` |
| `Departures.fs:79-92` | `nextOrNull`, `parentOf`, `detach`, `startTimer`, `stopTimer` | delete; each use calls the `Native` function of the same name (`startTimer`'s uses call `Native.setTimeout`, `stopTimer`'s `Native.clearTimeout`) |

Where a .NET body reads a DOM member that returns an option (`nextSibling`, `parentNode`, `firstChild`, `lastChild`, `getAttribute`, `prefix`), unwrap it with `Option.toObj` or a `Native` primitive; never compare an option with `null`. `DomEvents.listenDirect` and the hand-ported blocks are Task 6's.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 11 tests. A failing startup type names its file; its first `jsNative` value is still a module-level value. If it names a `Fable.Ranvier` file (`src/Fable.Ranvier/Global.fs`), Foundation reads a core-side Fable global during initialisation: stop and report it, since the fix is a `src/` change.

- [ ] **Step 6: Run the gates**

G-BUNDLE, G-DOM, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT.

- [ ] **Step 7: Commit**

Paths: `Native.fs`, `Mounting.fs`, `TemplateDom.fs`, `DocumentHead.fs`, `Placement.fs`, `Prepared.fs`, `Microtask.fs`, `Departures.fs`, `ModuleInitTests.fs`, the test project file. Message: `refactor(view): Native node primitives and host globals; Foundation initialises on .NET`.

### Task 6: The hand-ported blocks

> Amended by A3 (Amendments after the phase 0 review).

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs:129-139` (delegated listener and its removal), `:147-148` (`overlaps`), `:172-173` (`deleteBetween`)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs:747-760` (`keepData`, `keepAttributes`, `keepElement`)
- Modify: `experiments/Ranvier.View.Foundation/DocumentHead.fs:55-122` (`keepHead`)
- Modify: `experiments/Ranvier.View.Foundation/DomEvents.fs:5-7` (`listenDirect`)
- Modify: `experiments/Ranvier.View.Portable/Departures.fs:48-53` (`seam`), `Native.fs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DelegationChecks.fs` (one divergence case)
- Create: `tests/Ranvier.View.Twin.Tests/SeamTests.fs`

**Interfaces:**
- Consumes: `Native.expando`, `setExpando`, `get`, `set`, `isNullish`, `parentOrNull`, `firstChildOf`, `nextOrNull`, `lastChildOf`, `removeNode`; `IHost.Listen`, `IHost.OverrideCurrentTarget`, `IHost.DeleteBetween`; `TemplateDom.dispatch: Func<obj array, Node, obj, unit>`.
- Produces: .NET-only `Native.listen: obj -> string -> (obj -> unit) -> (unit -> unit)`, `Native.overrideCurrentTarget: obj -> (unit -> obj) -> unit`, `Native.deleteBetween: Node -> Node -> unit`, `Native.childNodesOf: Node -> Node array`; .NET bodies that the twin runs from Task 13 on; a non-null `Departures.seam` on .NET. Each block keeps its JavaScript under `#if FABLE_COMPILER`, and its F# port sits in the `#else` directly after it (spec risk 1).

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/SeamTests.fs` (compile it before `Main.fs`):

```fsharp
module Ranvier.View.Twin.Tests.SeamTests

open Expecto
open Ranvier.View.Foundation

[<Tests>]
let tests =
    testList
        "Departures seam"
        [
            testCase "the seam is an object on .NET whose flags start off"
            <| fun () ->
                let seam = Departures.seam
                Expect.isNotNull (box seam) "seam"
                Expect.isFalse seam.Enabled "exits off"
                Expect.isFalse seam.ReflowEnabled "reflows off"

            testCase "the seam's operations are settable"
            <| fun () ->
                let seam = Departures.seam
                let previous = seam.NextLive
                seam.NextLive <- System.Func<_, _> id
                Expect.isNotNull (box seam.NextLive) "set"
                seam.NextLive <- previous
        ]
```

Add a divergence case to `DelegationChecks.fs` (spec §6.3: a native listener between target and host) and list it in `cases`:

```fsharp
let private nativeStopBetween () =
    use graph = new Graph ()
    let host = element "main"
    let clicks = ResizeArray<int>()

    let view =
        H.Program.div [||] [| H.Program.button [| H.Events.onClick (fun _ -> clicks.Add 1) |] [||] |]

    let mounted = mount graph host view
    let wrapper = host.querySelector "div" |> unbox<HTMLElement>
    wrapper.addEventListener ("click", Some (U2.Case1 (fun (event: Event) -> event.stopPropagation ())), None)
    (host.querySelector "button" |> unbox<HTMLButtonElement>).click ()
    equal 0 clicks.Count
    mounted.Dispose ()
```

Case name: `"a native listener that stops propagation between target and host keeps the delegated handler from running"`. The delegated listener sits on the host, above the wrapper, so the wrapper's `stopPropagation` keeps the click from reaching it; read `docs/content/view/events.md:57-68` first. If the page documents another outcome, report the conflict instead of asserting either.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL, `seam: Expected value to not be null`.

- [ ] **Step 3: Add the .NET-only host calls to `Native.fs`**

```fsharp
#if !FABLE_COMPILER
let listen (target: obj) (eventName: string) (listener: obj -> unit) : unit -> unit =
    (host ()).Listen (target, eventName, listener)

let overrideCurrentTarget (event: obj) (read: unit -> obj) : unit = (host ()).OverrideCurrentTarget (event, read)

let deleteBetween (first: Node) (last: Node) : unit = (host ()).DeleteBetween (first, last)

let childNodesOf (node: Node) : Node array =
    let children = ResizeArray<Node>()
    let mutable child = firstChildOf node

    while not (isNullish child) do
        children.Add child
        child <- nextOrNull child

    children.ToArray ()
#endif
```

- [ ] **Step 4: Port the delegated listener**

Wrap the existing declarations at `Mounting.fs:129-139` (the comment, `delegateFrom` and `undelegateFrom`) in `#if FABLE_COMPILER`, unchanged, and add this `#else`:

```fsharp
#else
type private HostListeners = Collections.Generic.Dictionary<string, struct (int * (unit -> unit))>

// Walks from the event's target, or from above the inner host recorded on the event, to `host`, presenting each node
// as currentTarget: a node's direct handler runs, a template event node dispatches with its instance state, and
// `cancelBubble` ends the walk.
let private walk (host: Node) (eventName: string) (event: obj) =
    let key = "$$ranvier:" + eventName
    let inner: Node = Native.expando event "$$ranvierHost"

    let mutable node: Node =
        if Native.isNullish inner then
            unbox<Node> (Option.toObj (unbox<Event> event).target)
        else
            Native.parentOrNull inner

    Native.overrideCurrentTarget event (fun () -> box node)
    let mutable going = true

    while going && not (Native.isNullish node) do
        let handler: obj -> unit = Native.expando node key

        if not (Native.isNullish handler) then
            handler event
            going <- not (unbox<Event> event).cancelBubble
        else
            let state: obj array = Native.expando node "$$ranvier"

            if not (Native.isNullish state) then
                TemplateDom.dispatch.Invoke (state, node, event)
                going <- not (unbox<Event> event).cancelBubble

        if going then
            if obj.ReferenceEquals (node, host) then
                going <- false
            else
                node <- Native.parentOrNull node

    Native.setExpando event "$$ranvierHost" host

let private listenersOf (host: Node) : HostListeners =
    match Native.get<Node, HostListeners> hostCounts host with
    | null ->
        let created = HostListeners ()
        Native.set hostCounts host created
        created
    | existing -> existing

let private delegateFrom: Func<Node, string, unit> =
    Func<Node, string, unit>(fun host eventName ->
        let listeners = listenersOf host

        match listeners.TryGetValue eventName with
        | true, struct (count, remove) -> listeners[eventName] <- struct (count + 1, remove)
        | false, _ -> listeners[eventName] <- struct (1, Native.listen host eventName (walk host eventName)))

let private undelegateFrom: Func<Node, string, unit> =
    Func<Node, string, unit>(fun host eventName ->
        let listeners = listenersOf host

        match listeners.TryGetValue eventName with
        | true, struct (1, remove) ->
            remove ()
            listeners.Remove eventName |> ignore
        | true, struct (count, remove) -> listeners[eventName] <- struct (count - 1, remove)
        | false, _ -> ())
#endif
```

The .NET port keeps one host listener per host and event name, counted by mounts; the JavaScript shares one listener function across hosts and reads the host as `this`. Both register and remove the host's listener at the same counts.

- [ ] **Step 5: Port `overlaps`, `deleteBetween`, the rollback snapshots and `listenDirect`**

`overlaps` (`Mounting.fs:147-148`), Emit kept under `#if FABLE_COMPILER`:

```fsharp
#else
let private overlaps (registry: WeakSet<Node>) (node: Node) : bool =
    let mutable found = false
    let mutable cursor = node

    while not found && not (Native.isNullish cursor) do
        found <- registry.has cursor
        cursor <- Native.parentOrNull cursor

    let pending = Collections.Generic.Stack<Node> (Native.childNodesOf node)

    while not found && pending.Count > 0 do
        let current = pending.Pop ()
        found <- registry.has current

        for child in Native.childNodesOf current do
            pending.Push child

    found
#endif
```

`deleteBetween` (`:172-173`): `#else` `let private deleteBetween (first: Node) (last: Node) : unit = Native.deleteBetween first last`.

The rollback snapshots (`TemplateDom.fs:747-760`), Emits kept under `#if FABLE_COMPILER`, each a port of its JavaScript:

```fsharp
#else
let private attributesOf (element: Element) : (string * string) array =
    let attributes = element.attributes

    Array.init (int attributes.length) (fun i ->
        let attribute = (attributes.item (float i)).Value
        attribute.name, attribute.value)

let private setAttributesBack (element: Element) (attributes: (string * string) array) =
    for name, _ in attributesOf element do
        element.removeAttribute name

    for name, value in attributes do
        element.setAttribute (name, value)

let private keepData (node: obj) : unit -> unit =
    let data = Native.dataOf (unbox<Node> node)
    fun () -> Native.setData node (box data)

let private keepAttributes (node: obj) : unit -> unit =
    let element = unbox<Element> node
    let attributes = attributesOf element
    fun () -> setAttributesBack element attributes

let private keepElement (node: obj) : unit -> unit =
    let element = unbox<Element> node
    let children = Native.childNodesOf element
    let attributes = attributesOf element
    let value, isChecked, selected = Native.formStateOf node

    fun () ->
        for child in Native.childNodesOf element do
            Native.removeNode child

        for child in children do
            Native.appendNode element child

        setAttributesBack element attributes
        Native.restoreFormState node value isChecked selected
#endif
```

with two .NET-only helpers in `Native.fs`: `formStateOf: obj -> string * bool * bool` (reads `value` from `HTMLInputElement`, `HTMLTextAreaElement`, `HTMLSelectElement`, `HTMLOptionElement` or `HTMLButtonElement`, `checked` from `HTMLInputElement`, `selected` from `HTMLOptionElement`; `null`/`false` for the others, as the JavaScript's `'value' in n` tests do), and `restoreFormState: obj -> string -> bool -> bool -> unit` (writes each back only when it differs, as the JavaScript does). The JavaScript calls `n.replaceChildren(...c)`; the remove-then-append above has the same result on the twin, which implements both.

`DomEvents.listenDirect`:

```fsharp
#if FABLE_COMPILER
[<Emit("(() => { $0.addEventListener($1, $2); return () => $0.removeEventListener($1, $2); })()")>]
let listenDirect (node: obj) (eventName: string) (handler: obj -> unit) : unit -> unit = jsNative
#else
let listenDirect (node: obj) (eventName: string) (handler: obj -> unit) : unit -> unit =
    Ranvier.View.Foundation.Native.listen node eventName handler
#endif
```

- [ ] **Step 6: Port `keepHead`'s copy path**

The .NET body is the JavaScript's `if (!O)` branch (`DocumentHead.fs:89-95`) and the matching branch of `Restore`; the twin document has no `MutationObserver` (spec §3.2). Wrap `DocumentHead.fs:52-122` (the comment and `keepHead`) in `#if FABLE_COMPILER` and add:

```fsharp
#else
let private attributesOf (element: Element) : (string * string) array =
    let attributes = element.attributes

    Array.init (int attributes.length) (fun i ->
        let attribute = (attributes.item (float i)).Value
        attribute.name, attribute.value)

let private setAttributes (element: Element) (attributes: (string * string) array) =
    if attributesOf element <> attributes then
        for name, _ in attributesOf element do
            element.removeAttribute name

        for name, value in attributes do
            element.setAttribute (name, value)

[<Sealed>]
type private KeptCopy(document: Document) =
    let head: Node = document.head :> Node
    let children = if Native.isNullish head then [||] else Native.childNodesOf head

    let attributes =
        children
        |> Array.map (fun n -> if n.nodeType = 1.0 then attributesOf (unbox<Element> n) else null)

    let titles =
        children
        |> Array.map (fun n -> if n.nodeName = "TITLE" then Native.childNodesOf n else null)

    interface Served with
        member _.Restore() =
            if not (Native.isNullish head) then
                for i in 0 .. children.Length - 1 do
                    let current = Native.childNodesOf head |> Array.tryItem i

                    match current with
                    | Some n when obj.ReferenceEquals (n, children[i]) -> ()
                    | _ -> head.insertBefore (children[i], current) |> ignore

                while (Native.childNodesOf head).Length > children.Length do
                    Native.removeNode (Native.lastChildOf head)

                for i in 0 .. children.Length - 1 do
                    if not (isNull attributes[i]) then
                        setAttributes (unbox<Element> children[i]) attributes[i]

                    let kept = titles[i]

                    if not (isNull kept) then
                        let present = Native.childNodesOf children[i]

                        if present.Length <> kept.Length || Array.exists2 (fun a b -> not (obj.ReferenceEquals (a, b))) present kept then
                            for child in present do
                                Native.removeNode child

                            for child in kept do
                                Native.appendNode children[i] child

        member _.Release() = ()

let private keepHead: Func<Document, Served> =
    Func<Document, Served>(fun document -> KeptCopy document :> Served)
#endif
```

`Document.head` is `HTMLHeadElement` in Xantham 0.2.1 (not an option), and `Node.insertBefore`'s second parameter is an option of the Xantham node type; convert `current` to that option as `fcs_signature_help` reports.

- [ ] **Step 7: Give `Departures.seam` a .NET object**

```fsharp
/// <summary>The seam, an empty object until the first registration.</summary>
#if FABLE_COMPILER
let seam: Seam = Fable.Core.JsInterop.emitJsExpr () "{}"
#else
[<Sealed>]
type private SeamObject() =
    let mutable enabled = false
    let mutable reflowEnabled = false
    let mutable retire: System.Func<Node, ResizeArray<unit -> unit>, bool> = null
    let mutable nextLive: System.Func<Node, Node> = null
    let mutable holds: System.Func<Node, Node, bool> = null
    let mutable within: System.Func<Node, bool> = null
    let mutable finish: System.Action<ResizeArray<unit -> unit>, ResizeArray<exn>, ResizeArray<unit -> unit>> = null
    let mutable before: System.Func<Node, (unit -> unit) -> unit, unit -> unit> = null

    interface Seam with
        member _.Enabled
            with get () = enabled
            and set value = enabled <- value

        member _.ReflowEnabled
            with get () = reflowEnabled
            and set value = reflowEnabled <- value

        member _.Retire
            with get () = retire
            and set value = retire <- value

        member _.NextLive
            with get () = nextLive
            and set value = nextLive <- value

        member _.Holds
            with get () = holds
            and set value = holds <- value

        member _.Within
            with get () = within
            and set value = within <- value

        member _.End
            with get () = finish
            and set value = finish <- value

        member _.Before
            with get () = before
            and set value = before <- value

let seam: Seam = SeamObject ()
#endif
```

Compare the members with `Departures.fs:15-47` after the latest merge; a member the merge added gets the same field-and-property pair.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 13 tests.

- [ ] **Step 9: Run the gates**

G-BUNDLE, G-DOM (runs the new delegation case under jsdom), G-BROWSER, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT. Gate 3 lists no file besides `Native.js`; an F# port placed outside its `#else` shows here.

- [ ] **Step 10: Commit**

Paths: `Mounting.fs`, `TemplateDom.fs`, `DocumentHead.fs`, `DomEvents.fs`, `Departures.fs`, `Native.fs`, `DelegationChecks.fs`, `SeamTests.fs`, the test project file. Message: `refactor(view): .NET ports of the delegated listener, head keeping, rollback snapshots and departure seam`.

---

## Phase 2: the twin DOM

The twin project starts here. Each task below adds one file to `experiments/Ranvier.View.Twin` and its tests to `tests/Ranvier.View.Twin.Tests`; the order (tree, HTML, bridge, events, selectors, clock and session) is the dependency order.

### Task 7: The node tree and form-control state

> Amended by A4 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Twin/Ranvier.View.Twin.fsproj`, `experiments/Ranvier.View.Twin/Tree.fs`
- Create: `tests/Ranvier.View.Twin.Tests/TreeTests.fs`
- Modify: `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj`, `Ranvier.slnx`

**Interfaces:**
- Produces (namespace `Ranvier.View.Twin`):
  - `NodeKind = Element | Text | Comment | Fragment | Document`
  - `exception TwinDomException of name: string * detail: string` (`HierarchyRequestError`, `NotFoundError`)
  - `[<AbstractClass; AllowNullLiteral>] TwinNode`: `Kind`, `Parent`, `FirstChild`, `LastChild`, `NextSibling`, `PreviousSibling`, `Document: TwinDocument`, `Children: TwinNode list`, `AppendChild: TwinNode -> TwinNode`, `InsertBefore: TwinNode * TwinNode -> TwinNode` (null reference appends), `RemoveChild: TwinNode -> TwinNode`, `ReplaceChild: TwinNode * TwinNode -> TwinNode`, `Remove: unit -> unit`, `TextContent: string with get, set`, `IsConnected: bool`, `Contains: TwinNode -> bool`, `Clone: deep: bool -> TwinNode`, `member val internal Proxy: obj`
  - `[<Sealed; AllowNullLiteral>] TwinCharacterData` (`Text` or `Comment`): `Data: string with get, set`
  - `[<Sealed; AllowNullLiteral>] TwinElement`: `LocalName`, `Attributes: (string * string) list`, `GetAttribute: string -> string` (null when absent), `SetAttribute: string * string -> unit`, `RemoveAttribute: string -> unit`, `HasAttribute: string -> bool`, `Content: TwinNode` (template only), `Value: string with get, set`, `DefaultValue: string with get, set`, `Checked: bool with get, set`, `Selected: bool with get, set`
  - `[<Sealed; AllowNullLiteral>] TwinFragment`
  - `[<Sealed; AllowNullLiteral>] TwinDocument`: `new: unit -> TwinDocument` (with `html`, `head`, `body`), `Html`, `Head`, `Body: TwinElement`, `CreateElement: string -> TwinElement`, `CreateText: string -> TwinCharacterData`, `CreateComment: string -> TwinCharacterData`, `CreateFragment: unit -> TwinFragment`, `ElementById: string -> TwinElement`, `ActiveElement: TwinElement with get, set`, `OwnerThread: int`, `Check: operation: string -> unit`, `OnDispose: (unit -> unit) -> unit`, `interface IDisposable`
- Produces (`Ranvier.View.Twin.Twin` project): `IsPackable=false`, references Foundation, Portable and the core.

- [ ] **Step 1: Merge `master` and rebase citations**

Task 4 Step 1's commands. Phase 2 cites no Foundation line beyond `members.txt` checks, so a merge here needs only the baseline step when it touched view sources.

- [ ] **Step 2: Create the project and write the failing tests**

`experiments/Ranvier.View.Twin/Ranvier.View.Twin.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <OutputType>Library</OutputType>
        <IsPackable>false</IsPackable>
        <GenerateDocumentationFile>true</GenerateDocumentationFile>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="Tree.fs" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="../../src/Ranvier/Ranvier.fsproj" />
        <ProjectReference Include="../Ranvier.View.Portable/Ranvier.View.Portable.fsproj" />
        <ProjectReference Include="../Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj" />
        <PackageReference Update="FSharp.Core" Version="10.1.401" />
        <InternalsVisibleTo Include="Ranvier.View.Twin.Tests" />
    </ItemGroup>
</Project>
```

Add `<Folder Name="/experiments/Ranvier.View.Twin/"><Project Path="experiments/Ranvier.View.Twin/Ranvier.View.Twin.fsproj" /></Folder>` to `Ranvier.slnx` after the Portable folder, and a `ProjectReference` to it from the test project.

`tests/Ranvier.View.Twin.Tests/TreeTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.TreeTests

open Expecto
open Ranvier.View.Twin

let private names (node: TwinNode) =
    node.Children
    |> List.map (fun c ->
        match c with
        | :? TwinElement as e -> e.LocalName
        | :? TwinCharacterData as t -> "#" + t.Data
        | _ -> "?")

[<Tests>]
let tests =
    testList
        "Tree"
        [
            testCase "a new document has html, head and body"
            <| fun () ->
                use document = new TwinDocument ()
                Expect.equal (names document) [ "html" ] "root"
                Expect.equal (names document.Html) [ "head"; "body" ] "html"
                Expect.isTrue document.Body.IsConnected "connected"

            testCase "insertBefore moves a node and keeps sibling links consistent"
            <| fun () ->
                use document = new TwinDocument ()
                let a, b, c = document.CreateElement "a", document.CreateElement "b", document.CreateElement "c"

                for n in [ a; b; c ] do
                    document.Body.AppendChild n |> ignore

                document.Body.InsertBefore (c, a) |> ignore
                Expect.equal (names document.Body) [ "c"; "a"; "b" ] "moved"
                Expect.isNull c.PreviousSibling "first"
                Expect.equal a.PreviousSibling (c :> TwinNode) "back link"
                Expect.equal b.NextSibling null "last"

            testCase "inserting a fragment moves its children and empties it"
            <| fun () ->
                use document = new TwinDocument ()
                let fragment = document.CreateFragment ()
                fragment.AppendChild (document.CreateText "x") |> ignore
                fragment.AppendChild (document.CreateComment "y") |> ignore
                document.Body.AppendChild fragment |> ignore
                Expect.equal (names document.Body) [ "#x"; "#y" ] "children moved"
                Expect.isEmpty fragment.Children "fragment emptied"

            testCase "inserting an ancestor into its descendant is a HierarchyRequestError"
            <| fun () ->
                use document = new TwinDocument ()
                let outer = document.CreateElement "div"
                let inner = document.CreateElement "span"
                outer.AppendChild inner |> ignore

                Expect.throwsC (fun () -> inner.AppendChild outer |> ignore) (fun e ->
                    match e with
                    | TwinDomException (name, _) -> Expect.equal name "HierarchyRequestError" "name"
                    | other -> failtestf "unexpected %O" other)

            testCase "textContent concatenates descendant text and setting it replaces the children"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                div.AppendChild (document.CreateText "a") |> ignore
                div.AppendChild (document.CreateComment "skip") |> ignore
                let span = document.CreateElement "span"
                span.AppendChild (document.CreateText "b") |> ignore
                div.AppendChild span |> ignore
                Expect.equal div.TextContent "ab" "comments excluded"
                div.TextContent <- ""
                Expect.isEmpty div.Children "cleared"

            testCase "attributes keep insertion order and setAttribute lowercases the name"
            <| fun () ->
                use document = new TwinDocument ()
                let e = document.CreateElement "div"
                e.SetAttribute ("B", "1")
                e.SetAttribute ("a", "2")
                e.SetAttribute ("b", "3")
                Expect.equal e.Attributes [ "b", "3"; "a", "2" ] "order and replacement"

            testCase "an input's value reads the attribute until it is written"
            <| fun () ->
                use document = new TwinDocument ()
                let input = document.CreateElement "input"
                input.SetAttribute ("value", "a")
                Expect.equal input.Value "a" "attribute"
                input.Value <- "b"
                input.SetAttribute ("value", "c")
                Expect.equal input.Value "b" "dirty value"
                Expect.equal input.DefaultValue "c" "defaultValue reflects the attribute"

            testCase "a checkbox's checked follows the attribute until it is written"
            <| fun () ->
                use document = new TwinDocument ()
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                box.SetAttribute ("checked", "")
                Expect.isTrue box.Checked "attribute"
                box.Checked <- false
                box.SetAttribute ("checked", "")
                Expect.isFalse box.Checked "dirty checkedness"

            testCase "checking a radio unchecks the others of its group"
            <| fun () ->
                use document = new TwinDocument ()
                let radio name =
                    let r = document.CreateElement "input"
                    r.SetAttribute ("type", "radio")
                    r.SetAttribute ("name", name)
                    document.Body.AppendChild r |> ignore
                    r

                let a, b, other = radio "g", radio "g", radio "h"
                a.Checked <- true
                other.Checked <- true
                b.Checked <- true
                Expect.equal (a.Checked, b.Checked, other.Checked) (false, true, true) "group"

            testCase "a select's value is its first selected option's value"
            <| fun () ->
                use document = new TwinDocument ()
                let select = document.CreateElement "select"

                for v in [ "a"; "b" ] do
                    let o = document.CreateElement "option"
                    o.SetAttribute ("value", v)
                    select.AppendChild o |> ignore

                Expect.equal select.Value "a" "first option by default"
                select.Value <- "b"
                Expect.equal select.Value "b" "selected by value"
                Expect.isFalse (select.Children[0] :?> TwinElement).Selected "deselected"

            testCase "a textarea's default value is its text"
            <| fun () ->
                use document = new TwinDocument ()
                let area = document.CreateElement "textarea"
                area.AppendChild (document.CreateText "t") |> ignore
                Expect.equal area.Value "t" "default"
                area.Value <- "u"
                Expect.equal area.DefaultValue "t" "defaultValue unchanged"

            testCase "a template element owns a content fragment"
            <| fun () ->
                use document = new TwinDocument ()
                let template = document.CreateElement "template"
                Expect.equal template.Content.Kind NodeKind.Fragment "content"
                Expect.isNull (document.CreateElement "div").Content "other elements"

            testCase "a document used from another thread raises"
            <| fun () ->
                use document = new TwinDocument ()
                let mutable failure: exn = null
                let thread = System.Threading.Thread (fun () -> try document.Check "appendChild" with e -> failure <- e)
                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure :? System.InvalidOperationException) "InvalidOperationException"
                Expect.stringContains failure.Message "belongs to thread" "message"
        ]
```

Compile `TreeTests.fs` before `Main.fs`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The type 'TwinDocument' is not defined`.

- [ ] **Step 4: Write `Tree.fs`**

```fsharp
namespace Ranvier.View.Twin

open System
open System.Collections.Generic

[<RequireQualifiedAccess>]
type NodeKind =
    | Element
    | Text
    | Comment
    | Fragment
    | Document

/// <summary>A tree operation the DOM rejects; <c>name</c> is the DOM exception name.</summary>
exception TwinDomException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>A node of a twin document. Each node has one bridged object, the value Foundation sees.</summary>
[<AbstractClass; AllowNullLiteral>]
type TwinNode internal (kind: NodeKind, owner: TwinDocument) =
    let mutable parent: TwinNode = null
    let mutable firstChild: TwinNode = null
    let mutable lastChild: TwinNode = null
    let mutable nextSibling: TwinNode = null
    let mutable previousSibling: TwinNode = null

    member _.Kind = kind
    member _.Parent = parent
    member _.FirstChild = firstChild
    member _.LastChild = lastChild
    member _.NextSibling = nextSibling
    member _.PreviousSibling = previousSibling

    member this.Document: TwinDocument =
        if isNull owner then this :?> TwinDocument else owner

    /// <summary>The bridged object; set once by the bridge.</summary>
    member val internal Proxy: obj = null with get, set

    member _.Children: TwinNode list =
        let rec collect (node: TwinNode) acc =
            if isNull node then List.rev acc else collect node.NextSibling (node :: acc)

        collect firstChild []

    member internal _.SetLinks(p, n, pr) =
        parent <- p
        nextSibling <- n
        previousSibling <- pr

    member internal _.SetNext n = nextSibling <- n
    member internal _.SetPrevious p = previousSibling <- p
    member internal _.SetFirst f = firstChild <- f
    member internal _.SetLast l = lastChild <- l

    member this.Contains(other: TwinNode) =
        let mutable cursor = other
        let mutable found = false

        while not found && not (isNull cursor) do
            found <- obj.ReferenceEquals (cursor, this)
            cursor <- cursor.Parent

        found

    member this.IsConnected =
        let mutable cursor: TwinNode = this

        while not (isNull cursor.Parent) do
            cursor <- cursor.Parent

        cursor.Kind = NodeKind.Document

    member private this.Unlink(child: TwinNode) =
        let previous, next = child.PreviousSibling, child.NextSibling

        if isNull previous then this.SetFirst next else previous.SetNext next
        if isNull next then this.SetLast previous else next.SetPrevious previous
        child.SetLinks (null, null, null)

    member this.RemoveChild(child: TwinNode) : TwinNode =
        if isNull child || not (obj.ReferenceEquals (child.Parent, this)) then
            raise (TwinDomException ("NotFoundError", "The node to remove is not a child of this node."))

        this.Unlink child
        child

    member this.Remove() =
        if not (isNull parent) then
            parent.RemoveChild this |> ignore

    member this.InsertBefore(node: TwinNode, reference: TwinNode) : TwinNode =
        if not (isNull reference) && not (obj.ReferenceEquals (reference.Parent, this)) then
            raise (TwinDomException ("NotFoundError", "The reference node is not a child of this node."))

        if node.Contains this then
            raise (TwinDomException ("HierarchyRequestError", "The new child is an ancestor of the parent."))

        if node.Kind = NodeKind.Document then
            raise (TwinDomException ("HierarchyRequestError", "A document cannot be inserted."))

        if node.Kind = NodeKind.Fragment then
            for child in node.Children do
                this.InsertBefore (child, reference) |> ignore

            node
        else
            let reference = if obj.ReferenceEquals (reference, node) then node.NextSibling else reference
            node.Remove ()
            let previous = if isNull reference then lastChild else reference.PreviousSibling
            node.SetLinks (this, reference, previous)

            if isNull previous then firstChild <- node else previous.SetNext node
            if isNull reference then lastChild <- node else reference.SetPrevious node
            node

    member this.AppendChild(node: TwinNode) = this.InsertBefore (node, null)

    member this.ReplaceChild(node: TwinNode, child: TwinNode) : TwinNode =
        if isNull child || not (obj.ReferenceEquals (child.Parent, this)) then
            raise (TwinDomException ("NotFoundError", "The node to replace is not a child of this node."))

        let reference = if obj.ReferenceEquals (child.NextSibling, node) then node.NextSibling else child.NextSibling
        this.RemoveChild child |> ignore
        this.InsertBefore (node, reference) |> ignore
        child

    abstract TextContent: string with get, set

    default this.TextContent
        with get () =
            let text = Text.StringBuilder ()

            let rec walk (node: TwinNode) =
                for child in node.Children do
                    match child with
                    | :? TwinCharacterData as data when data.Kind = NodeKind.Text -> text.Append data.Data |> ignore
                    | :? TwinCharacterData -> ()
                    | _ -> walk child

            walk this
            text.ToString ()
        and set value =
            for child in this.Children do
                this.RemoveChild child |> ignore

            if not (String.IsNullOrEmpty value) then
                this.AppendChild (this.Document.CreateText value) |> ignore

    abstract Clone: deep: bool -> TwinNode

and [<Sealed; AllowNullLiteral>] TwinCharacterData internal (kind: NodeKind, owner: TwinDocument, data: string) =
    inherit TwinNode(kind, owner)
    let mutable data = data

    member _.Data
        with get () = data
        and set value = data <- (if isNull value then "" else value)

    override _.TextContent
        with get () = data
        and set value = data <- (if isNull value then "" else value)

    override this.Clone _ = TwinCharacterData (kind, this.Document, data)

and [<Sealed; AllowNullLiteral>] TwinFragment internal (owner: TwinDocument) =
    inherit TwinNode(NodeKind.Fragment, owner)

    override this.Clone deep =
        let copy = TwinFragment this.Document

        if deep then
            for child in this.Children do
                copy.AppendChild (child.Clone true) |> ignore

        copy

and [<Sealed; AllowNullLiteral>] TwinElement internal (owner: TwinDocument, localName: string) =
    inherit TwinNode(NodeKind.Element, owner)
    let attributes = ResizeArray<string * string>()
    let content = if localName = "template" then TwinFragment owner else null
    let mutable dirtyValue: string = null
    let mutable checkedness = false
    let mutable dirtyChecked = false
    let mutable selectedness = false
    let mutable dirtySelected = false

    member _.LocalName = localName
    member _.Attributes: (string * string) list = List.ofSeq attributes
    member _.Content: TwinNode = content

    member _.GetAttribute(name: string) : string =
        let name = name.ToLowerInvariant ()

        match attributes |> Seq.tryFind (fun (n, _) -> n = name) with
        | Some (_, value) -> value
        | None -> null

    member this.HasAttribute(name: string) = not (isNull (this.GetAttribute name))

    member _.SetAttribute(name: string, value: string) =
        let name = name.ToLowerInvariant ()

        match attributes.FindIndex (fun (n, _) -> n = name) with
        | -1 -> attributes.Add ((name, value))
        | index -> attributes[index] <- (name, value)

    member _.RemoveAttribute(name: string) =
        let name = name.ToLowerInvariant ()
        attributes.RemoveAll (fun (n, _) -> n = name) |> ignore

    member private this.InputType =
        match this.GetAttribute "type" with
        | null -> "text"
        | t -> t.ToLowerInvariant ()

    member private this.Options: TwinElement list =
        let rec walk (node: TwinNode) =
            node.Children
            |> List.collect (fun c ->
                match c with
                | :? TwinElement as e when e.LocalName = "option" -> [ e ]
                | :? TwinElement as e when e.LocalName = "optgroup" -> walk e
                | _ -> [])

        walk this

    /// <summary>The form value, by HTML's value modes: the attribute until the property is written, then the written value.</summary>
    member this.Value
        with get () =
            match localName with
            | "input" when this.InputType = "checkbox" || this.InputType = "radio" ->
                match this.GetAttribute "value" with
                | null -> "on"
                | value -> value
            | "input" -> if isNull dirtyValue then (match this.GetAttribute "value" with null -> "" | v -> v) else dirtyValue
            | "textarea" -> if isNull dirtyValue then this.TextContent else dirtyValue
            | "option" ->
                match this.GetAttribute "value" with
                | null -> String.Join (" ", this.TextContent.Split ([| ' '; '\t'; '\n'; '\f'; '\r' |], StringSplitOptions.RemoveEmptyEntries))
                | value -> value
            | "select" ->
                match this.Options |> List.tryFind (fun o -> o.Selected) with
                | Some option -> option.Value
                | None -> ""
            | "button" -> match this.GetAttribute "value" with null -> "" | v -> v
            | _ -> null
        and set (value: string) =
            match localName with
            | "input" when this.InputType = "checkbox" || this.InputType = "radio" -> this.SetAttribute ("value", value)
            | "input"
            | "textarea" -> dirtyValue <- (if isNull value then "" else value)
            | "select" ->
                let mutable found = false

                for option in this.Options do
                    let selected = not found && option.Value = value
                    found <- found || selected
                    option.Selected <- selected
            | "option"
            | "button" -> this.SetAttribute ("value", value)
            | _ -> ()

    member this.DefaultValue
        with get () =
            match localName with
            | "textarea" -> this.TextContent
            | _ -> match this.GetAttribute "value" with null -> "" | v -> v
        and set (value: string) =
            match localName with
            | "textarea" -> this.TextContent <- value
            | _ -> this.SetAttribute ("value", value)

    member this.Checked
        with get () = if dirtyChecked then checkedness else this.HasAttribute "checked"
        and set (value: bool) =
            dirtyChecked <- true
            checkedness <- value

            if value && localName = "input" && this.InputType = "radio" then
                let name = this.GetAttribute "name"

                if not (String.IsNullOrEmpty name) then
                    let mutable root: TwinNode = this

                    while not (isNull root.Parent) do
                        root <- root.Parent

                    let rec walk (node: TwinNode) =
                        for child in node.Children do
                            match child with
                            | :? TwinElement as e when
                                not (obj.ReferenceEquals (e, this))
                                && e.LocalName = "input"
                                && e.InputType = "radio"
                                && e.GetAttribute "name" = name
                                ->
                                e.Checked <- false
                            | _ -> ()

                            walk child

                    walk root

    /// <summary>An option's selectedness; a select with no selected option selects its first, as a single-choice select does.</summary>
    member this.Selected
        with get () =
            if localName <> "option" then
                false
            elif dirtySelected then
                selectedness
            elif this.HasAttribute "selected" then
                true
            else
                match this.Parent with
                | :? TwinElement as select when select.LocalName = "select" && not (select.HasAttribute "multiple") ->
                    let options = select.Options
                    not (options |> List.exists (fun o -> o.HasAttribute "selected" || o.SelectedIsDirty))
                    && obj.ReferenceEquals (List.tryHead options |> Option.toObj, this)
                | _ -> false
        and set (value: bool) =
            dirtySelected <- true
            selectedness <- value

    member internal _.SelectedIsDirty = dirtySelected && selectedness

    override this.Clone deep =
        let copy = TwinElement (this.Document, localName)

        for name, value in attributes do
            copy.SetAttribute (name, value)

        if localName = "input" then
            if not (isNull dirtyValue) then copy.Value <- dirtyValue
            if dirtyChecked then copy.Checked <- checkedness

        if deep then
            for child in this.Children do
                copy.AppendChild (child.Clone true) |> ignore

            if not (isNull content) then
                for child in content.Children do
                    copy.Content.AppendChild (child.Clone true) |> ignore

        copy

and [<Sealed; AllowNullLiteral>] TwinDocument() as this =
    inherit TwinNode(NodeKind.Document, null)
    let ownerThread = Environment.CurrentManagedThreadId
    let disposals = ResizeArray<unit -> unit>()
    let html = TwinElement (this, "html")
    let head = TwinElement (this, "head")
    let body = TwinElement (this, "body")

    do
        this.AppendChild html |> ignore
        html.AppendChild head |> ignore
        html.AppendChild body |> ignore

    member _.Html = html
    member _.Head = head
    member _.Body = body
    member _.OwnerThread = ownerThread
    member val ActiveElement: TwinElement = null with get, set

    member this.CreateElement(tag: string) = TwinElement (this, tag.ToLowerInvariant ())
    member this.CreateText(data: string) = TwinCharacterData (NodeKind.Text, this, data)
    member this.CreateComment(data: string) = TwinCharacterData (NodeKind.Comment, this, data)
    member this.CreateFragment() = TwinFragment this

    member this.ElementById(id: string) : TwinElement =
        let rec find (node: TwinNode) =
            node.Children
            |> List.tryPick (fun c ->
                match c with
                | :? TwinElement as e when e.GetAttribute "id" = id -> Some e
                | _ -> find c)

        find this |> Option.toObj

    /// <summary>Raises <c>InvalidOperationException</c> naming <c>operation</c> when called off the creating thread.</summary>
    member _.Check(operation: string) =
        let current = Environment.CurrentManagedThreadId

        if current <> ownerThread then
            raise (
                InvalidOperationException (
                    $"The DOM twin document belongs to thread {ownerThread}; {operation} ran on thread {current}."
                )
            )

    member _.OnDispose(action: unit -> unit) = disposals.Add action

    override this.Clone _ = raise (TwinDomException ("NotSupportedError", "A twin document cannot be cloned."))

    interface IDisposable with
        member _.Dispose() =
            for action in Seq.rev (List.ofSeq disposals) do
                action ()

            disposals.Clear ()
```

`TwinElement.InputType` is used across instances in `Checked`'s radio walk; make it `member internal`, not `private`. If F# rejects `as this` with base-class constructor arguments in `TwinDocument`, build `html`, `head` and `body` in a `do` block after the base call.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 26 tests.

- [ ] **Step 6: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 7: Commit**

Paths: the twin project file, `Tree.fs`, `TreeTests.fs`, the test project file, `Ranvier.slnx`. Message: `feat(twin): the DOM twin's node tree and form-control state`.

### Task 8: HTML parsing and serialisation

**Files:**
- Create: `experiments/Ranvier.View.Twin/Html.fs`
- Create: `tests/Ranvier.View.Twin.Tests/HtmlTests.fs`

**Interfaces:**
- Consumes: Task 7's tree; `Ranvier.View.Foundation.Representation.escapeHtml: bool -> string -> string` (internal; the twin has Portable's `InternalsVisibleTo`).
- Produces (module `Ranvier.View.Twin.Html`):
  - `exception TwinParseException of construct: string * line: int * column: int`
  - `parseInto: parent: TwinNode -> html: string -> unit` (appends the parsed nodes to `parent`; under a `template` element, to its content)
  - `parse: document: TwinDocument -> html: string -> TwinFragment`
  - `outer: TwinNode -> string`, `inner: TwinNode -> string` (jsdom's serialisation)
  - `server: TwinNode -> string` (the children of a node in Server's escaping)

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/HtmlTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.HtmlTests

open Expecto
open Ranvier.View.Twin

let private roundTrip (html: string) =
    use document = new TwinDocument ()
    Html.inner (Html.parse document html)

let private rejects (html: string) (construct: string) =
    use document = new TwinDocument ()

    Expect.throwsC (fun () -> Html.parse document html |> ignore) (fun e ->
        match e with
        | TwinParseException (found, line, column) ->
            Expect.stringContains found construct "construct"
            Expect.isGreaterThan line 0 "line"
            Expect.isGreaterThan column 0 "column"
        | other -> failtestf "unexpected %O" other)

[<Tests>]
let tests =
    testList
        "Html"
        [
            testCase "markers, attributes and void elements round trip"
            <| fun () ->
                let html = "<!--[r--><div class=\"panel\" title=\"T &amp;\">Hello <input value=\"x\"><!--]--></div>"
                Expect.equal (roundTrip html) html "round trip"

            testCase "unquoted and single-quoted attribute values parse"
            <| fun () ->
                Expect.equal (roundTrip "<p a=1 b='2'></p>") "<p a=\"1\" b=\"2\"></p>" "normalised quotes"

            testCase "tags and attribute names are lowercased"
            <| fun () -> Expect.equal (roundTrip "<DIV ID=\"a\"></DIV>") "<div id=\"a\"></div>" "lowercased"

            testCase "character references decode"
            <| fun () ->
                Expect.equal (roundTrip "<p>&lt;&gt;&amp;&quot;&apos;&nbsp;&#65;&#x42;</p>") "<p>&lt;&gt;&amp;\"'&nbsp;AB</p>" "decoded"

            testCase "raw text and RCDATA keep their content"
            <| fun () ->
                Expect.equal (roundTrip "<script>if (a < b) {}</script>") "<script>if (a < b) {}</script>" "script raw"
                Expect.equal (roundTrip "<textarea>&lt;b&gt;</textarea>") "<textarea>&lt;b&gt;</textarea>" "textarea decodes"

            testCase "the leading line feed of pre and textarea is dropped"
            <| fun () ->
                Expect.equal (roundTrip "<pre>\nx</pre>") "<pre>x</pre>" "pre"
                Expect.equal (roundTrip "<textarea>\ny</textarea>") "<textarea>y</textarea>" "textarea"

            testCase "template content parses into the content fragment"
            <| fun () ->
                use document = new TwinDocument ()
                let fragment = Html.parse document "<template><b>x</b></template>"
                let template = fragment.FirstChild :?> TwinElement
                Expect.isEmpty template.Children "no direct children"
                Expect.equal (Html.inner template.Content) "<b>x</b>" "content"
                Expect.equal (Html.inner fragment) "<template><b>x</b></template>" "serialised"

            testCase "serialisers escape the jsdom and server sets"
            <| fun () ->
                use document = new TwinDocument ()
                let p = document.CreateElement "p"
                p.SetAttribute ("title", "a&b\"c <d>\re")
                p.AppendChild (document.CreateText "x&y<z> \"q\r") |> ignore
                let host = document.CreateElement "div"
                host.AppendChild p |> ignore
                Expect.equal (Html.inner host) "<p title=\"a&amp;b&quot;c&nbsp;<d>\re\">x&amp;y&lt;z&gt;&nbsp;\"q\r</p>" "jsdom"
                Expect.equal (Html.server host) "<p title=\"a&amp;b&quot;c &lt;d&gt;&#13;e\">x&amp;y&lt;z&gt; \"q&#13;</p>" "server"
                Expect.equal (Html.server (Html.parse document (Html.server host))) (Html.server host) "server round trip"

            testCase "carriage returns in the input are normalised to line feeds"
            <| fun () -> Expect.equal (roundTrip "<p>a\r\nb\rc</p>") "<p>a\nb\nc</p>" "normalised"

            testCase "server text with a leading line feed in pre keeps its doubled line feed"
            <| fun () ->
                use document = new TwinDocument ()
                let s = "<pre>\n\nx</pre>"
                Expect.equal (Html.server (Html.parse document s)) s "Server writes a second line feed"

            testCase "an implied end tag is rejected"
            <| fun () -> rejects "<p>a<div>b</div></p>" "implied end tag"

            testCase "a table without tbody is rejected"
            <| fun () -> rejects "<table><tr><td>a</td></tr></table>" "<table> without <tbody>"

            testCase "text inside a table is rejected as foster parenting"
            <| fun () -> rejects "<table>x</table>" "foster parenting"

            testCase "a named reference outside the set is rejected"
            <| fun () -> rejects "<p>&copy;</p>" "&copy;"

            testCase "a mismatched end tag is rejected"
            <| fun () -> rejects "<b><i>x</b></i>" "end tag </b>"
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The value or constructor 'Html' is not defined`.

- [ ] **Step 3: Write `Html.fs`**

```fsharp
/// <summary>The twin's HTML: a parser for the subset Server writes and template markup holds, and two serialisers.</summary>
module Ranvier.View.Twin.Html

open System
open System.Collections.Generic
open System.Text

/// <summary>Markup outside the parser's subset; <c>construct</c> names it, at a 1-based line and column.</summary>
exception TwinParseException of construct: string * line: int * column: int with
    override this.Message = $"The DOM twin's parser does not accept {this.construct} (line {this.line}, column {this.column}); run this markup under jsdom"

let private voids =
    HashSet<string> [ "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"; "link"; "meta"; "source"; "track"; "wbr" ]

let private rawText = HashSet<string> [ "script"; "style" ]
let private escapableRaw = HashSet<string> [ "textarea"; "title" ]
let private serialisedRaw = HashSet<string> [ "style"; "script"; "xmp"; "iframe"; "noembed"; "noframes"; "plaintext" ]
let private leadingLineFeed = HashSet<string> [ "pre"; "listing"; "textarea" ]

let private closesParagraph =
    HashSet<string> [ "address"; "article"; "aside"; "blockquote"; "details"; "div"; "dl"; "fieldset"; "figcaption"; "figure"; "footer"; "form"; "h1"; "h2"; "h3"; "h4"; "h5"; "h6"; "header"; "hr"; "main"; "menu"; "nav"; "ol"; "p"; "pre"; "section"; "table"; "ul" ]

let private selfClosing = HashSet<string> [ "li"; "option"; "dd"; "dt"; "tr"; "td"; "th" ]
let private documentLevel = HashSet<string> [ "html"; "head"; "body" ]

let private tableContent =
    dict [ "table", set [ "caption"; "colgroup"; "thead"; "tbody"; "tfoot"; "template"; "script"; "style" ]
           "tbody", set [ "tr"; "template"; "script"; "style" ]
           "thead", set [ "tr"; "template"; "script"; "style" ]
           "tfoot", set [ "tr"; "template"; "script"; "style" ]
           "tr", set [ "td"; "th"; "template"; "script"; "style" ] ]

let private named = dict [ "amp", "&"; "lt", "<"; "gt", ">"; "quot", "\""; "apos", "'"; "nbsp", " " ]

type private Reader(text: string) =
    let mutable index = 0
    member _.Index = index
    member _.AtEnd = index >= text.Length
    member _.Peek = if index < text.Length then text[index] else '\000'
    member _.PeekAt k = if index + k < text.Length then text[index + k] else '\000'
    member _.Advance n = index <- index + n
    member _.StartsWith(s: string) = String.CompareOrdinal (text, index, s, 0, s.Length) = 0

    member _.StartsWithIgnoreCase(s: string) =
        index + s.Length <= text.Length
        && String.Compare (text, index, s, 0, s.Length, StringComparison.OrdinalIgnoreCase) = 0

    member _.Position =
        let mutable line, column = 1, 1

        for i in 0 .. index - 1 do
            if text[i] = '\n' then
                line <- line + 1
                column <- 1
            else
                column <- column + 1

        line, column

    member this.Fail(construct: string) =
        let line, column = this.Position
        raise (TwinParseException (construct, line, column))

let private isAsciiLetter c = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
let private isSpace c = c = ' ' || c = '\t' || c = '\n' || c = '\f'

/// <summary>Reads a character reference at <c>&amp;</c>; a bare <c>&amp;</c> stays literal.</summary>
let private reference (reader: Reader) (out: StringBuilder) =
    let codePoint (value: int) =
        if value = 0 || value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF) then "�"
        elif value >= 0x80 && value <= 0x9F then reader.Fail $"the numeric reference &#{value}; in the C1 range"
        else Char.ConvertFromUtf32 value

    if reader.PeekAt 1 = '#' then
        let hex = reader.PeekAt 2 = 'x' || reader.PeekAt 2 = 'X'
        let start = if hex then 3 else 2
        let digits = StringBuilder ()
        let mutable k = start

        while (if hex then Uri.IsHexDigit (reader.PeekAt k) else Char.IsAsciiDigit (reader.PeekAt k)) do
            digits.Append (reader.PeekAt k) |> ignore
            k <- k + 1

        if digits.Length = 0 || reader.PeekAt k <> ';' then
            reader.Fail "a numeric reference without digits or a semicolon"

        let value = Convert.ToInt32 (digits.ToString (), if hex then 16 else 10)
        out.Append (codePoint value) |> ignore
        reader.Advance (k + 1)
    elif isAsciiLetter (reader.PeekAt 1) then
        let name = StringBuilder ()
        let mutable k = 1

        while Char.IsAsciiLetterOrDigit (reader.PeekAt k) do
            name.Append (reader.PeekAt k) |> ignore
            k <- k + 1

        let name = name.ToString ()

        match named.TryGetValue name with
        | true, value when reader.PeekAt k = ';' ->
            out.Append value |> ignore
            reader.Advance (k + 1)
        | _ -> reader.Fail $"&{name};"
    else
        out.Append '&' |> ignore
        reader.Advance 1

let private normalise (html: string) = html.Replace("\r\n", "\n").Replace ('\r', '\n')

let private parseNodes (parent: TwinNode) (html: string) =
    let document = parent.Document
    let reader = Reader (normalise html)
    let stack = Stack<TwinNode>()
    stack.Push parent

    let container (node: TwinNode) =
        match node with
        | :? TwinElement as e when e.LocalName = "template" -> e.Content
        | _ -> node

    let currentName () =
        match stack.Peek () with
        | :? TwinElement as e -> e.LocalName
        | _ -> ""

    let append (node: TwinNode) = (container (stack.Peek ())).AppendChild node |> ignore

    let appendText (text: string) =
        if text.Length > 0 then
            match tableContent.TryGetValue (currentName ()) with
            | true, _ when text |> Seq.exists (isSpace >> not) -> reader.Fail "foster parenting (text inside a table)"
            | _ ->
                match (container (stack.Peek ())).LastChild with
                | :? TwinCharacterData as last when last.Kind = NodeKind.Text -> last.Data <- last.Data + text
                | _ -> append (document.CreateText text)

    let readUntil (stop: string) (decode: bool) (dropLeadingLineFeed: bool) =
        let out = StringBuilder ()

        if dropLeadingLineFeed && reader.Peek = '\n' then
            reader.Advance 1

        while not (reader.AtEnd || reader.StartsWithIgnoreCase stop) do
            if decode && reader.Peek = '&' then reference reader out
            else
                out.Append reader.Peek |> ignore
                reader.Advance 1

        if reader.AtEnd then reader.Fail $"an unclosed {stop.Substring 2}" else out.ToString ()

    let readName () =
        let out = StringBuilder ()

        while not (reader.AtEnd || isSpace reader.Peek || reader.Peek = '/' || reader.Peek = '>' || reader.Peek = '=') do
            out.Append (Char.ToLowerInvariant reader.Peek) |> ignore
            reader.Advance 1

        out.ToString ()

    let skipSpace () =
        while isSpace reader.Peek do
            reader.Advance 1

    let readAttributeValue () =
        let out = StringBuilder ()

        match reader.Peek with
        | '"'
        | '\'' as quote ->
            reader.Advance 1

            while not (reader.AtEnd || reader.Peek = quote) do
                if reader.Peek = '&' then reference reader out
                else
                    out.Append reader.Peek |> ignore
                    reader.Advance 1

            if reader.AtEnd then reader.Fail "an unterminated attribute value"
            reader.Advance 1
        | _ ->
            while not (reader.AtEnd || isSpace reader.Peek || reader.Peek = '>') do
                if reader.Peek = '&' then reference reader out
                else
                    out.Append reader.Peek |> ignore
                    reader.Advance 1

        out.ToString ()

    let startTag () =
        reader.Advance 1
        let name = readName ()

        if documentLevel.Contains name then reader.Fail $"the document-level tag <{name}>"

        let current = currentName ()

        if current = "p" && closesParagraph.Contains name then reader.Fail $"an implied end tag (<p> closed by <{name}>)"
        if selfClosing.Contains name && current = name then reader.Fail $"an implied end tag (<{name}> inside <{name}>)"

        match tableContent.TryGetValue current with
        | true, allowed when not (allowed.Contains name) ->
            if current = "table" && name = "tr" then reader.Fail "a <table> without <tbody>"
            else reader.Fail $"foster parenting (<{name}> inside <{current}>)"
        | _ -> ()

        let element = document.CreateElement name
        skipSpace ()

        while not (reader.AtEnd || reader.Peek = '>' || reader.StartsWith "/>") do
            let attribute = readName ()
            skipSpace ()

            let value =
                if reader.Peek = '=' then
                    reader.Advance 1
                    skipSpace ()
                    readAttributeValue ()
                else
                    ""

            if attribute.Length = 0 then reader.Fail "an empty attribute name"
            if not (element.HasAttribute attribute) then element.SetAttribute (attribute, value)
            skipSpace ()

        if reader.AtEnd then reader.Fail $"an unterminated <{name}> tag"
        reader.Advance (if reader.Peek = '/' then 2 else 1)
        append element

        if rawText.Contains name then
            appendRaw element (readUntil $"</{name}" false false)
            reader.Advance (name.Length + 2)
            skipSpace ()
            reader.Advance 1
        elif escapableRaw.Contains name then
            appendRaw element (readUntil $"</{name}" true (name = "textarea"))
            reader.Advance (name.Length + 2)
            skipSpace ()
            reader.Advance 1
        elif not (voids.Contains name) then
            stack.Push element

            if leadingLineFeed.Contains name && reader.Peek = '\n' then
                reader.Advance 1

    and appendRaw (element: TwinElement) (text: string) =
        if text.Length > 0 then element.AppendChild (document.CreateText text) |> ignore

    let endTag () =
        reader.Advance 2
        let name = readName ()
        skipSpace ()
        if reader.Peek <> '>' then reader.Fail $"an unterminated </{name}> tag"
        reader.Advance 1

        if voids.Contains name then reader.Fail $"the end tag </{name}> of a void element"
        if stack.Count = 1 || currentName () <> name then reader.Fail $"the end tag </{name}> that does not close <{currentName ()}>"
        stack.Pop () |> ignore

    let comment () =
        reader.Advance 4

        if reader.StartsWith ">" || reader.StartsWith "->" then reader.Fail "an abruptly closed comment"

        let out = StringBuilder ()

        while not (reader.AtEnd || reader.StartsWith "-->") do
            out.Append reader.Peek |> ignore
            reader.Advance 1

        if reader.AtEnd then reader.Fail "an unclosed comment"
        reader.Advance 3
        append (document.CreateComment (out.ToString ()))

    let text = StringBuilder ()

    let flush () =
        appendText (text.ToString ())
        text.Clear () |> ignore

    while not reader.AtEnd do
        if reader.StartsWith "<!--" then
            flush ()
            comment ()
        elif reader.StartsWithIgnoreCase "<!doctype html>" then
            reader.Advance 15
        elif reader.StartsWith "<!" || reader.StartsWith "<?" then
            reader.Fail "a bogus comment"
        elif reader.StartsWith "</" && isAsciiLetter (reader.PeekAt 2) then
            flush ()
            endTag ()
        elif reader.Peek = '<' && isAsciiLetter (reader.PeekAt 1) then
            flush ()
            startTag ()
        elif reader.Peek = '&' then
            reference reader text
        else
            text.Append reader.Peek |> ignore
            reader.Advance 1

    flush ()

    if stack.Count > 1 then
        reader.Fail $"an unclosed <{currentName ()}>"

/// <summary>Appends the nodes of <c>html</c> to <c>parent</c>, or to its content when <c>parent</c> is a template.</summary>
let parseInto (parent: TwinNode) (html: string) : unit = parseNodes parent html

let parse (document: TwinDocument) (html: string) : TwinFragment =
    let fragment = document.CreateFragment ()
    parseNodes fragment html
    fragment

let private jsdomText (text: string) =
    text.Replace("&", "&amp;").Replace(" ", "&nbsp;").Replace("<", "&lt;").Replace (">", "&gt;")

let private jsdomAttribute (value: string) =
    value.Replace("&", "&amp;").Replace(" ", "&nbsp;").Replace ("\"", "&quot;")

let rec private write (out: StringBuilder) (text: string -> string) (attribute: string -> string) (server: bool) (parentName: string) (node: TwinNode) =
    match node with
    | :? TwinElement as e ->
        out.Append('<').Append e.LocalName |> ignore

        for name, value in e.Attributes do
            out.Append(' ').Append(name).Append("=\"").Append(attribute value).Append '"' |> ignore

        out.Append '>' |> ignore

        if not (voids.Contains e.LocalName) then
            let children = (if e.LocalName = "template" then e.Content else e :> TwinNode).Children

            match children with
            | (:? TwinCharacterData as first) :: _ when server && leadingLineFeed.Contains e.LocalName && first.Kind = NodeKind.Text && first.Data.StartsWith "\n" ->
                out.Append '\n' |> ignore
            | _ -> ()

            for child in children do
                write out text attribute server e.LocalName child

            out.Append("</").Append(e.LocalName).Append '>' |> ignore
    | :? TwinCharacterData as c when c.Kind = NodeKind.Comment -> out.Append("<!--").Append(c.Data).Append "-->" |> ignore
    | :? TwinCharacterData as c ->
        out.Append (if serialisedRaw.Contains parentName then c.Data else text c.Data) |> ignore
    | _ ->
        for child in node.Children do
            write out text attribute server parentName child

let private serialise text attribute server (nodes: TwinNode list) (parentName: string) =
    let out = StringBuilder ()

    for node in nodes do
        write out text attribute server parentName node

    out.ToString ()

let private nameOf (node: TwinNode) =
    match node with
    | :? TwinElement as e -> e.LocalName
    | _ -> ""

let private childrenOf (node: TwinNode) =
    match node with
    | :? TwinElement as e when e.LocalName = "template" -> e.Content.Children
    | _ -> node.Children

/// <summary>The node in jsdom's serialisation: <c>outerHTML</c> for an element, the data of a text node escaped as text.</summary>
let outer (node: TwinNode) : string = serialise jsdomText jsdomAttribute false [ node ] (nameOf node.Parent)

/// <summary>The node's children in jsdom's serialisation, as <c>innerHTML</c> returns them.</summary>
let inner (node: TwinNode) : string = serialise jsdomText jsdomAttribute false (childrenOf node) (nameOf node)

/// <summary>The node's children in Server's escaping, with Server's doubled leading line feed in <c>pre</c>, <c>textarea</c> and <c>listing</c>.</summary>
let server (node: TwinNode) : string =
    serialise
        (Ranvier.View.Foundation.Representation.escapeHtml false)
        (Ranvier.View.Foundation.Representation.escapeHtml true)
        true
        (childrenOf node)
        (nameOf node)
```

`startTag` and `appendRaw` reference each other: declare `appendRaw` before `startTag` (it needs only `document`), and drop the `and`. Add `Html.fs` to the twin project after `Tree.fs`, and `HtmlTests.fs` to the test project before `Main.fs`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 41 tests. The `serialisers escape` expectation for jsdom is the `entities` rule the spec cites (`escape.js:154-171` in the main checkout's Playground `node_modules`): text escapes `& < >` and U+00A0, attributes `& "` and U+00A0, and a carriage return passes through both.

- [ ] **Step 5: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 6: Commit**

Paths: `Html.fs`, `HtmlTests.fs`, the twin and test project files. Message: `feat(twin): subset HTML parser and the jsdom and Server serialisers`.

### Task 9: The interface bridge

**Files:**
- Create: `experiments/Ranvier.View.Twin/Bridge.fs`
- Create: `experiments/Ranvier.View.Twin/members.txt` (generated by Task 13; created empty here)
- Create: `tests/Ranvier.View.Twin.Tests/BridgeTests.fs`
- Modify: `experiments/Ranvier.View.Twin/Ranvier.View.Twin.fsproj` (`Bridge.fs` after `Html.fs`; `PackageReference Include="Xantham.Fable.Core.TS" Version="0.2.1"` comes through Portable)

**Interfaces:**
- Consumes: Tasks 7 and 8.
- Produces (namespace `Ranvier.View.Twin`):
  - `type NodeProxy` (`DispatchProxy` subclass) with `member Node: TwinNode`
  - `[<AutoOpen>] module NodeNative` with the extension `TwinNode.Native: Fable.Core.TS.Dom.Node` (the same object on every read)
  - `module Bridge`: `ensure: unit -> unit` (registers the core members once), `nodeOf: obj -> TwinNode` (null for a non-twin value), `served: unit -> string list` (the `Interface.member` names served in the process, sorted), `resetServed: unit -> unit`, `registerMember: name: string -> arity: int -> (TwinNode -> obj array -> obj) -> unit` (used by Tasks 10 and 11 to add event and selector members)
  - `NotSupportedException "The DOM twin does not implement <Interface>.<member>"` for every other member

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/BridgeTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.BridgeTests

open System
open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Twin

[<Tests>]
let tests =
    testList
        "Bridge"
        [
            testCase "a node has one bridged object"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                Expect.isTrue (obj.ReferenceEquals (div.Native, div.Native)) "same object"
                Expect.equal (Bridge.nodeOf div.Native) (div :> TwinNode) "back to the node"

            testCase "the interface follows the tag"
            <| fun () ->
                use document = new TwinDocument ()
                Expect.isTrue ((document.CreateElement "input").Native :? HTMLInputElement) "input"
                Expect.isTrue ((document.CreateElement "template").Native :? HTMLTemplateElement) "template"
                Expect.isTrue ((document.CreateElement "button").Native :? HTMLButtonElement) "button"
                Expect.isTrue ((document.CreateElement "section").Native :? HTMLElement) "section"
                Expect.isTrue ((document.CreateText "x").Native :? Text) "text"
                Expect.isTrue ((document.CreateComment "x").Native :? Comment) "comment"
                Expect.isTrue ((document.CreateElement "input").Native :? ARIAMixin) "inherited interfaces"

            testCase "nullable returns are options and numbers are floats"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native
                Expect.isNone div.firstChild "no child"
                Expect.equal div.nodeType 1.0 "nodeType"
                let text = document.CreateText "t"
                div.appendChild text.Native |> ignore
                Expect.isTrue (obj.ReferenceEquals (div.firstChild.Value, text.Native)) "Some child"

            testCase "generated property writers reach the form state"
            <| fun () ->
                use document = new TwinDocument ()
                let input = document.CreateElement "input"
                (input.Native :?> HTMLInputElement).value <- "x"
                (input.Native :?> HTMLInputElement).``checked`` <- true
                Expect.equal (input.Value, input.Checked) ("x", true) "written"

            testCase "an unimplemented member names its interface and member"
            <| fun () ->
                use document = new TwinDocument ()
                let element = (document.CreateElement "div").Native :?> HTMLElement

                Expect.throwsC (fun () -> element.offsetWidth |> ignore) (fun e ->
                    Expect.isTrue (e :? NotSupportedException) "NotSupportedException"
                    Expect.equal e.Message "The DOM twin does not implement HTMLElement.offsetWidth" "message")

            testCase "innerHTML parses and serialises"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native :?> Element
                div.innerHTML <- "<b>x</b><!--m-->"
                Expect.equal div.innerHTML "<b>x</b><!--m-->" "round trip"
                Expect.equal div.childNodes.length 2.0 "children"

            testCase "served members are recorded by interface"
            <| fun () ->
                Bridge.resetServed ()
                use document = new TwinDocument ()
                (document.CreateElement "div").Native.nodeType |> ignore
                Expect.contains (Bridge.served ()) "Node.nodeType" "recorded"

            testCase "a call from another thread raises"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native
                let mutable failure: exn = null
                let thread = Threading.Thread (fun () -> try div.nodeType |> ignore with e -> failure <- e)
                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure :? InvalidOperationException) "InvalidOperationException"
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The field, constructor or member 'Native' is not defined`.

- [ ] **Step 3: Write `Bridge.fs`**

```fsharp
namespace Ranvier.View.Twin

open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection

type private Member = TwinNode -> obj array -> obj

/// <summary>The bridged object of a twin node: a proxy of the DOM interface its kind and tag imply.</summary>
type NodeProxy() =
    inherit DispatchProxy()
    member val Node: TwinNode = null with get, set

    override this.Invoke(targetMethod: MethodInfo, args: obj array) : obj = BridgeCore.invoke this.Node targetMethod args

and internal BridgeCore private () =
    static let table = Dictionary<string, Member>()
    static let interfaces = Dictionary<string, Type>()
    static let someOf = Dictionary<Type, MethodInfo>()

    // Every member served in the process, across threads: the record behind members.txt.
    static let served = System.Collections.Concurrent.ConcurrentDictionary<string, byte> ()

    static member Table = table

    static member Domain = typeof<Fable.Core.TS.Dom.Node>.Assembly

    static member Interface(name: string) : Type =
        match interfaces.TryGetValue name with
        | true, t -> t
        | false, _ ->
            let t = BridgeCore.Domain.GetType ("Fable.Core.TS+Dom+" + name, true)
            interfaces[name] <- t
            t

    static member Serve(name: string) = served.TryAdd (name, 0uy) |> ignore
    static member Served: string seq = served.Keys
    static member ResetServed() = served.Clear ()

    /// <summary>The proxy of <c>node</c>, created on first use.</summary>
    static member ProxyOf(node: TwinNode) : obj =
        if isNull node then
            null
        else
            if isNull node.Proxy then
                let proxy = DispatchProxy.Create (BridgeCore.InterfaceOf node, typeof<NodeProxy>) :?> NodeProxy
                proxy.Node <- node
                node.Proxy <- proxy

            node.Proxy

    static member InterfaceOf(node: TwinNode) : Type =
        match node with
        | :? TwinDocument -> BridgeCore.Interface "Document"
        | :? TwinFragment -> BridgeCore.Interface "DocumentFragment"
        | :? TwinCharacterData as c when c.Kind = NodeKind.Text -> BridgeCore.Interface "Text"
        | :? TwinCharacterData -> BridgeCore.Interface "Comment"
        | :? TwinElement as e ->
            let map = BridgeCore.Interface "HTMLElementTagNameMap"

            match map.GetProperty e.LocalName with
            | null -> BridgeCore.Interface "HTMLElement"
            | property -> property.PropertyType
        | _ -> BridgeCore.Interface "Node"

    static member Unwrap(value: obj) : obj =
        match value with
        | :? NodeProxy as proxy -> box proxy.Node
        | null -> null
        | _ ->
            let t = value.GetType ()

            if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<option<_>> then
                BridgeCore.Unwrap (t.GetProperty("Value").GetValue value)
            elif FSharpType.IsUnion (t, true) && t.Namespace = "Fable.Core" then
                BridgeCore.Unwrap ((FSharpValue.GetUnionFields (value, t, true) |> snd)[0])
            else
                value

    static member Wrap(returnType: Type, value: obj) : obj =
        let value =
            match value with
            | :? TwinNode as node -> BridgeCore.ProxyOf node
            | _ -> value

        if returnType = typeof<Void> then
            null
        elif returnType.IsGenericType && returnType.GetGenericTypeDefinition () = typedefof<option<_>> then
            if isNull value then
                null
            else
                let some =
                    match someOf.TryGetValue returnType with
                    | true, m -> m
                    | false, _ ->
                        let m = returnType.GetMethod "Some"
                        someOf[returnType] <- m
                        m

                some.Invoke (null, [| value |])
        elif returnType = typeof<float> then
            match value with
            | :? int as i -> box (float i)
            | _ -> value
        else
            value

    static member invoke (node: TwinNode) (targetMethod: MethodInfo) (args: obj array) : obj =
        node.Document.Check targetMethod.Name
        let name = targetMethod.Name
        let arity = if isNull args then 0 else args.Length
        let key = $"{name}/{arity}"
        let display = if name.StartsWith "get_" || name.StartsWith "set_" then name.Substring 4 else name
        let qualified = targetMethod.DeclaringType.Name + "." + display

        match table.TryGetValue key with
        | true, implementation ->
            BridgeCore.Serve qualified
            let args = if isNull args then [||] else Array.map BridgeCore.Unwrap args
            BridgeCore.Wrap (targetMethod.ReturnType, implementation node args)
        | false, _ -> raise (NotSupportedException ("The DOM twin does not implement " + qualified))

[<RequireQualifiedAccess>]
module Bridge =
    let nodeOf (value: obj) : TwinNode =
        match value with
        | :? NodeProxy as proxy -> proxy.Node
        | _ -> null

    let registerMember (name: string) (arity: int) (implementation: TwinNode -> obj array -> obj) =
        BridgeCore.Table[$"{name}/{arity}"] <- implementation

    let served () : string list = BridgeCore.Served |> Seq.sort |> List.ofSeq
    let resetServed () = BridgeCore.ResetServed ()

    let private element (node: TwinNode) = node :?> TwinElement
    let private text (value: obj) = if isNull value then "" else string value
    let private flag (value: obj) = match value with :? bool as b -> b | _ -> false

    let private getter name (read: TwinNode -> obj) = registerMember ("get_" + name) 0 (fun node _ -> read node)
    let private setter name (write: TwinNode -> obj -> unit) = registerMember ("set_" + name) 1 (fun node args -> write node args[0]; null)
    let private method name arity (run: TwinNode -> obj array -> obj) = registerMember name arity run

    let private nodeType (node: TwinNode) =
        match node.Kind with
        | NodeKind.Element -> 1
        | NodeKind.Text -> 3
        | NodeKind.Comment -> 8
        | NodeKind.Document -> 9
        | NodeKind.Fragment -> 11

    let private nodeName (node: TwinNode) =
        match node with
        | :? TwinElement as e -> e.LocalName.ToUpperInvariant ()
        | _ ->
            match node.Kind with
            | NodeKind.Text -> "#text"
            | NodeKind.Comment -> "#comment"
            | NodeKind.Document -> "#document"
            | _ -> "#document-fragment"

    let private childList (nodes: unit -> TwinNode list) : obj = ListProxy.Create nodes

    let private core =
        lazy
            (
        // Node
        getter "nodeType" (nodeType >> box)
        getter "nodeName" (nodeName >> box)
        getter "parentNode" (fun n -> box n.Parent)
        getter "parentElement" (fun n -> match n.Parent with :? TwinElement as e -> box e | _ -> null)
        getter "firstChild" (fun n -> box n.FirstChild)
        getter "lastChild" (fun n -> box n.LastChild)
        getter "nextSibling" (fun n -> box n.NextSibling)
        getter "previousSibling" (fun n -> box n.PreviousSibling)
        getter "ownerDocument" (fun n -> if n.Kind = NodeKind.Document then null else box n.Document)
        getter "isConnected" (fun n -> box n.IsConnected)
        getter "childNodes" (fun n -> childList (fun () -> n.Children))
        getter "textContent" (fun n -> box n.TextContent)
        setter "textContent" (fun n v -> n.TextContent <- text v)
        method "appendChild" 1 (fun n a -> box (n.AppendChild (a[0] :?> TwinNode)))
        method "insertBefore" 2 (fun n a -> box (n.InsertBefore (a[0] :?> TwinNode, a[1] :?> TwinNode)))
        method "removeChild" 1 (fun n a -> box (n.RemoveChild (a[0] :?> TwinNode)))
        method "replaceChild" 2 (fun n a -> box (n.ReplaceChild (a[0] :?> TwinNode, a[1] :?> TwinNode)))
        method "contains" 1 (fun n a -> box (not (isNull a[0]) && n.Contains (a[0] :?> TwinNode)))
        method "cloneNode" 1 (fun n a -> box (n.Clone (flag a[0])))
        // CharacterData
        getter "data" (fun n -> box (n :?> TwinCharacterData).Data)
        setter "data" (fun n v -> (n :?> TwinCharacterData).Data <- text v)
        // ChildNode
        method "remove" 0 (fun n _ -> n.Remove (); null)
        method "replaceWith" 1 (fun n a ->
            let parent = n.Parent

            for replacement in (a[0] :?> obj array) |> Array.map BridgeCore.Unwrap do
                parent.InsertBefore ((match replacement with :? TwinNode as r -> r | v -> n.Document.CreateText (string v) :> TwinNode), n) |> ignore

            n.Remove ()
            null)
        // ParentNode
        getter "children" (fun n -> childList (fun () -> n.Children |> List.filter (fun c -> c.Kind = NodeKind.Element)))
        method "replaceChildren" 1 (fun n a ->
            for child in n.Children do
                n.RemoveChild child |> ignore

            for child in (a[0] :?> obj array) |> Array.map BridgeCore.Unwrap do
                n.AppendChild (match child with :? TwinNode as c -> c | v -> n.Document.CreateText (string v) :> TwinNode) |> ignore

            null)
        // Element
        getter "localName" (fun n -> box (element n).LocalName)
        getter "tagName" (fun n -> box ((element n).LocalName.ToUpperInvariant ()))
        getter "prefix" (fun _ -> null)
        getter "id" (fun n -> box (text ((element n).GetAttribute "id")))
        setter "id" (fun n v -> (element n).SetAttribute ("id", text v))
        getter "className" (fun n -> box (text ((element n).GetAttribute "class")))
        setter "className" (fun n v -> (element n).SetAttribute ("class", text v))
        method "getAttribute" 1 (fun n a -> box ((element n).GetAttribute (string a[0])))
        method "setAttribute" 2 (fun n a -> (element n).SetAttribute (string a[0], text a[1]); null)
        method "removeAttribute" 1 (fun n a -> (element n).RemoveAttribute (string a[0]); null)
        method "hasAttribute" 1 (fun n a -> box ((element n).HasAttribute (string a[0])))
        getter "attributes" (fun n -> AttributesProxy.Create (element n))
        getter "innerHTML" (fun n -> box (Html.inner n))
        setter "innerHTML" (fun n v ->
            let target = match n with :? TwinElement as e when e.LocalName = "template" -> e.Content | _ -> n

            for child in target.Children do
                target.RemoveChild child |> ignore

            Html.parseInto n (text v))
        getter "outerHTML" (fun n -> box (Html.outer n))
        // HTMLTemplateElement
        getter "content" (fun n -> box (element n).Content)
        // Form controls
        getter "value" (fun n -> box (element n).Value)
        setter "value" (fun n v -> (element n).Value <- text v)
        getter "defaultValue" (fun n -> box (element n).DefaultValue)
        setter "defaultValue" (fun n v -> (element n).DefaultValue <- text v)
        getter "checked" (fun n -> box (element n).Checked)
        setter "checked" (fun n v -> (element n).Checked <- flag v)
        getter "defaultChecked" (fun n -> box ((element n).HasAttribute "checked"))
        getter "selected" (fun n -> box (element n).Selected)
        setter "selected" (fun n v -> (element n).Selected <- flag v)
        getter "type" (fun n -> box (match (element n).GetAttribute "type" with null -> (if (element n).LocalName = "button" then "submit" else "text") | t -> t.ToLowerInvariant ()))
        getter "disabled" (fun n -> box ((element n).HasAttribute "disabled"))
        setter "disabled" (fun n v -> if flag v then (element n).SetAttribute ("disabled", "") else (element n).RemoveAttribute "disabled")
        // Document
        getter "head" (fun n -> box (n :?> TwinDocument).Head)
        getter "body" (fun n -> box (n :?> TwinDocument).Body)
        getter "documentElement" (fun n -> box (n :?> TwinDocument).Html)
        getter "title" (fun n ->
            match n with
            | :? TwinDocument as d ->
                d.Head.Children
                |> List.tryPick (fun c -> match c with :? TwinElement as e when e.LocalName = "title" -> Some (box e.TextContent) | _ -> None)
                |> Option.defaultValue (box "")
            | _ -> box (text ((element n).GetAttribute "title")))
        setter "title" (fun n v ->
            match n with
            | :? TwinDocument as d ->
                let existing = d.Head.Children |> List.tryPick (fun c -> match c with :? TwinElement as e when e.LocalName = "title" -> Some e | _ -> None)
                let title = match existing with Some t -> t | None -> d.Head.AppendChild (d.CreateElement "title") :?> TwinElement
                title.TextContent <- text v
            | _ -> (element n).SetAttribute ("title", text v))
        method "createElement" 2 (fun n a -> box ((n :?> TwinDocument).CreateElement (string a[0])))
        method "createTextNode" 1 (fun n a -> box ((n :?> TwinDocument).CreateText (text a[0])))
        method "createComment" 1 (fun n a -> box ((n :?> TwinDocument).CreateComment (text a[0])))
        method "createDocumentFragment" 0 (fun n _ -> box ((n :?> TwinDocument).CreateFragment ()))
        method "importNode" 2 (fun _ a -> box ((a[0] :?> TwinNode).Clone (flag a[1])))
        method "getElementById" 1 (fun n a -> box ((n :?> TwinDocument).ElementById (string a[0])))
        getter "activeElement" (fun n -> box (n :?> TwinDocument).ActiveElement))

    /// <summary>Registers the core members once per process; every entry point that creates a proxy calls it first.</summary>
    let ensure () = core.Force ()

[<AutoOpen>]
module NodeNative =
    type TwinNode with
        /// <summary>The node as <c>Fable.Core.TS.Dom</c> sees it: the same object on every read.</summary>
        member this.Native: Fable.Core.TS.Dom.Node =
            Bridge.ensure ()
            BridgeCore.ProxyOf this :?> Fable.Core.TS.Dom.Node
```

Indent the registration lines inside `lazy ( ... )` one level deeper than shown; Fantomas fixes the layout. Registration is explicit rather than a module-level `do`, because an F# library runs a file's top-level `do` only when one of the file's static values is first read, which a call to a function does not guarantee.

`ListProxy` and `AttributesProxy` are two more `DispatchProxy` subclasses in the same file, before `NodeProxy`'s `and`: `ListProxy.Create (nodes: unit -> TwinNode list) : obj` proxies `NodeListOf<Node>` with `get_length` (a float), `item` (an option of the node's proxy, `None` past the end) and `get_Item`; `AttributesProxy.Create (element: TwinElement) : obj` proxies `NamedNodeMap` with `get_length` and `item`, each item an `AttrProxy` (proxying `Attr`) with `get_name` and `get_value` read from the element at call time. Any other member of those three raises the same `NotSupportedException` with its interface and member. A `childNodes` list is live: `item` reads `nodes ()` at each call, as the DOM's is.

Two rules hold for every table entry: an argument arrives unwrapped (`BridgeCore.Unwrap` turns proxies into twin nodes, options into their value or null, and `Fable.Core.U2`-style erased unions into their case value), and a returned twin node is wrapped into its proxy and then into the declared return type (an option or a float). `replaceWith` and `replaceChildren` receive their `params` array as one `obj array` argument; read the parameter types with `fcs_nuget_members` and adjust the unwrapping if Xantham declares them otherwise.

Create `experiments/Ranvier.View.Twin/members.txt` empty; Task 13 fills it.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 49 tests.

- [ ] **Step 5: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 6: Commit**

Paths: `Bridge.fs`, `members.txt`, `BridgeTests.fs`, the twin and test project files. Message: `feat(twin): DispatchProxy bridge from the DOM interfaces to the twin tree`.

### Task 10: Events, activation and focus

> Amended by A4 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Twin/Events.fs`
- Create: `experiments/Ranvier.View.Testing/Ranvier.View.Testing.fsproj`, `experiments/Ranvier.View.Testing/EventSpec.fs`
- Create: `tests/Ranvier.View.Twin.Tests/EventTests.fs`
- Modify: `experiments/Ranvier.View.Twin/Ranvier.View.Twin.fsproj` (`Events.fs` after `Bridge.fs`; reference Testing), `Ranvier.slnx`

**Interfaces:**
- Consumes: Task 9's `Bridge.registerMember`, `BridgeCore.ProxyOf`, `BridgeCore.Interface`.
- Produces:
  - `Ranvier.View.Testing.EventSpec` (both hosts): `type EventKind = Plain | Mouse | Pointer | Keyboard | Input | Focus`; `type EventSpec = { Type: string; Kind: EventKind; Bubbles: bool; Cancelable: bool; Init: (string * obj) list }`; `module EventSpec`: `create: Document -> EventSpec -> Event` (Fable: the host's constructor; .NET: the installed source); .NET only: `type IEventSource = abstract Create: EventSpec -> Event`, `install: IEventSource -> IDisposable`
  - `Ranvier.View.Twin.TwinEvent` (class): `Type`, `Bubbles`, `Cancelable`, `Target: TwinNode`, `CurrentTarget: TwinNode`, `Phase: int`, `PropagationStopped`, `ImmediateStopped`, `DefaultPrevented`, `Init: IDictionary<string, obj>`, `CurrentTargetOverride: (unit -> obj) with get, set`
  - `module Events`: `ensure: unit -> unit`; `eventOf: obj -> TwinEvent`; `listen: TwinNode -> string -> capture: bool -> (obj -> unit) -> (unit -> unit)`; `dispatch: TwinNode -> TwinEvent -> bool` (false when cancelled); `proxyOf: TwinEvent -> obj`; `create: EventSpec -> TwinEvent`; `click: TwinElement -> unit`; `focus: TwinElement -> unit`; `blur: TwinElement -> unit`; `exception TwinListenerException of eventType: string * inner: exn`
  - Bridge members: `addEventListener`, `removeEventListener`, `dispatchEvent`, `click`, `focus`, `blur`, and on events `type`, `target`, `currentTarget`, `eventPhase`, `bubbles`, `cancelable`, `cancelBubble` (get and set), `defaultPrevented`, `preventDefault`, `stopPropagation`, `stopImmediatePropagation`, `isTrusted`, and each `Init` key as a getter (`key`, `button`, `data`, `inputType`, `relatedTarget`, `detail`)
- A listener's exception does not stop the dispatch; once the dispatch ends, the first one is raised as `TwinListenerException` naming the event type. Under jsdom the same exception goes to jsdom's virtual console; a conformance case that throws from a listener carries the `[twin]` or `[jsdom]` tag.

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/EventTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.EventTests

open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Twin

let private setup () =
    let document = new TwinDocument ()
    let outer = document.CreateElement "div"
    let inner = document.CreateElement "button"
    outer.AppendChild inner |> ignore
    document.Body.AppendChild outer |> ignore
    document, outer, inner

let private plain name bubbles =
    Events.create { Type = name; Kind = Ranvier.View.Testing.Plain; Bubbles = bubbles; Cancelable = true; Init = [] }

[<Tests>]
let tests =
    testList
        "Events"
        [
            testCase "capture runs root to target, then target, then bubble"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()
                Events.listen outer "x" true (fun _ -> log.Add "outer capture") |> ignore
                Events.listen inner "x" false (fun _ -> log.Add "inner") |> ignore
                Events.listen outer "x" false (fun _ -> log.Add "outer bubble") |> ignore
                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [ "outer capture"; "inner"; "outer bubble" ] "order"

            testCase "a non-bubbling event skips the bubble phase"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()
                Events.listen outer "x" false (fun _ -> log.Add "outer") |> ignore
                Events.dispatch inner (plain "x" false) |> ignore
                Expect.isEmpty log "no bubble"

            testCase "stopPropagation finishes the node's listeners; stopImmediatePropagation does not"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let log = ResizeArray<string>()
                Events.listen inner "x" false (fun e -> (Events.eventOf e).StopPropagation (); log.Add "a") |> ignore
                Events.listen inner "x" false (fun _ -> log.Add "b") |> ignore
                Events.listen outer "x" false (fun _ -> log.Add "outer") |> ignore
                Events.dispatch inner (plain "x" true) |> ignore
                Expect.equal (List.ofSeq log) [ "a"; "b" ] "stopped after the node"

            testCase "preventDefault on a cancelable event makes dispatch return false"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document
                Events.listen inner "x" false (fun e -> (Events.eventOf e).PreventDefault ()) |> ignore
                Expect.isFalse (Events.dispatch inner (plain "x" true)) "cancelled"

            testCase "clicking a checkbox toggles it and fires click, input and change"
            <| fun () ->
                use document = new TwinDocument ()
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                document.Body.AppendChild box |> ignore
                let log = ResizeArray<string>()

                for name in [ "click"; "input"; "change" ] do
                    Events.listen box name false (fun _ -> log.Add (name + ":" + string box.Checked)) |> ignore

                Events.click box
                Expect.equal (List.ofSeq log) [ "click:True"; "input:True"; "change:True" ] "sequence"

            testCase "a cancelled checkbox click restores the state and fires no change"
            <| fun () ->
                use document = new TwinDocument ()
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                document.Body.AppendChild box |> ignore
                let mutable changed = false
                Events.listen box "click" false (fun e -> (Events.eventOf e).PreventDefault ()) |> ignore
                Events.listen box "change" false (fun _ -> changed <- true) |> ignore
                Events.click box
                Expect.equal (box.Checked, changed) (false, false) "restored"

            testCase "clicking a submit button in a form fires submit"
            <| fun () ->
                use document = new TwinDocument ()
                let form = document.CreateElement "form"
                let button = document.CreateElement "button"
                form.AppendChild button |> ignore
                document.Body.AppendChild form |> ignore
                let mutable submitted = false
                Events.listen form "submit" false (fun _ -> submitted <- true) |> ignore
                Events.click button
                Expect.isTrue submitted "submit"

            testCase "clicking a label clicks its control"
            <| fun () ->
                use document = new TwinDocument ()
                let label = document.CreateElement "label"
                let box = document.CreateElement "input"
                box.SetAttribute ("type", "checkbox")
                label.AppendChild box |> ignore
                document.Body.AppendChild label |> ignore
                Events.click label
                Expect.isTrue box.Checked "forwarded"

            testCase "focus moves fire blur, focusout, focus and focusin in order"
            <| fun () ->
                use document = new TwinDocument ()
                let a, b = document.CreateElement "input", document.CreateElement "input"
                document.Body.AppendChild a |> ignore
                document.Body.AppendChild b |> ignore
                let log = ResizeArray<string>()

                for name in [ "blur"; "focusout"; "focus"; "focusin" ] do
                    Events.listen document.Body name true (fun e -> log.Add (name + ":" + (Bridge.nodeOf (Events.eventOf e).Target.Native :?> TwinElement).LocalName)) |> ignore

                Events.focus a
                log.Clear ()
                Events.focus b
                Expect.equal (List.ofSeq log) [ "blur:input"; "focusout:input"; "focus:input"; "focusin:input" ] "order"
                Expect.equal document.ActiveElement b "active"

            testCase "a click arrives as a PointerEvent proxy"
            <| fun () ->
                let document, _, inner = setup ()
                use _ = document
                let mutable seen: obj = null
                Events.listen inner "click" false (fun e -> seen <- e) |> ignore
                Events.click inner
                Expect.isTrue (seen :? PointerEvent) "PointerEvent"
                Expect.isTrue (obj.ReferenceEquals ((seen :?> Event).target.Value, inner.Native)) "target"

            testCase "a listener's exception surfaces after the dispatch ends"
            <| fun () ->
                let document, outer, inner = setup ()
                use _ = document
                let mutable outerRan = false
                Events.listen inner "x" false (fun _ -> failwith "boom") |> ignore
                Events.listen outer "x" false (fun _ -> outerRan <- true) |> ignore
                Expect.throwsT<TwinListenerException> (fun () -> Events.dispatch inner (plain "x" true) |> ignore) "raised"
                Expect.isTrue outerRan "dispatch continued"
        ]
```

`Events.eventOf: obj -> TwinEvent` reads the twin event behind a listener's argument.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The value or constructor 'Events' is not defined`.

- [ ] **Step 3: Create the testing project with `EventSpec.fs`**

`experiments/Ranvier.View.Testing/Ranvier.View.Testing.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <OutputType>Library</OutputType>
        <IsPackable>false</IsPackable>
        <GenerateDocumentationFile>true</GenerateDocumentationFile>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="EventSpec.fs" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="../Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj" />
        <PackageReference Update="FSharp.Core" Version="10.1.401" />
    </ItemGroup>
</Project>
```

Add it to `Ranvier.slnx` in a `/experiments/Ranvier.View.Testing/` folder; the twin project references it.

`EventSpec.fs`:

```fsharp
namespace Ranvier.View.Testing

open Fable.Core
open Fable.Core.TS.Dom

type EventKind =
    | Plain
    | Mouse
    | Pointer
    | Keyboard
    | Input
    | Focus

/// <summary>An event to dispatch: its type, constructor family, flags and the extra fields of its init dictionary.</summary>
type EventSpec =
    {
        Type: string
        Kind: EventKind
        Bubbles: bool
        Cancelable: bool
        Init: (string * obj) list
    }

[<RequireQualifiedAccess>]
module EventSpec =
    let constructorName (kind: EventKind) =
        match kind with
        | Plain -> "Event"
        | Mouse -> "MouseEvent"
        | Pointer -> "PointerEvent"
        | Keyboard -> "KeyboardEvent"
        | Input -> "InputEvent"
        | Focus -> "FocusEvent"

#if FABLE_COMPILER
    [<Emit("new (globalThis[$0])($1, $2)")>]
    let private construct (name: string) (eventType: string) (init: obj) : Event = jsNative

    /// <summary>The event under the host's constructors; a jsdom runner exposes them on <c>globalThis</c>.</summary>
    let create (_: Document) (spec: EventSpec) : Event =
        let init = JsInterop.createObj (("bubbles", box spec.Bubbles) :: ("cancelable", box spec.Cancelable) :: spec.Init)
        construct (constructorName spec.Kind) spec.Type init
#else
    /// <summary>The creator of events on .NET; the DOM twin installs one per document.</summary>
    type IEventSource =
        abstract Create: EventSpec -> Event

    [<Sealed; AbstractClass>]
    type private Slot =
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private current: IEventSource

        static member Current
            with get () = Slot.current
            and set (value: IEventSource) = Slot.current <- value

    let install (source: IEventSource) : System.IDisposable =
        let previous = Slot.Current
        Slot.Current <- source

        { new System.IDisposable with
            member _.Dispose() = Slot.Current <- previous
        }

    let create (_: Document) (spec: EventSpec) : Event =
        match Slot.Current with
        | null -> raise (System.InvalidOperationException "No event source is installed on this thread; create a TwinDocument first")
        | source -> source.Create spec
#endif
```

- [ ] **Step 4: Write `Events.fs`**

```fsharp
namespace Ranvier.View.Twin

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open Ranvier.View.Testing

/// <summary>A listener raised during a dispatch; the dispatch ran every other listener first.</summary>
exception TwinListenerException of eventType: string * inner: exn with
    override this.Message = $"A '{this.eventType}' listener raised: {this.inner.Message}"

[<AllowNullLiteral>]
type TwinEvent(eventType: string, kind: EventKind, bubbles: bool, cancelable: bool, init: IDictionary<string, obj>) =
    member _.Type = eventType
    member _.Kind = kind
    member _.Bubbles = bubbles
    member _.Cancelable = cancelable
    member _.Init = init
    member val Target: TwinNode = null with get, set
    member val CurrentTarget: TwinNode = null with get, set
    member val CurrentTargetOverride: unit -> obj = Unchecked.defaultof<_> with get, set
    member val Phase = 0 with get, set
    member val PropagationStopped = false with get, set
    member val ImmediateStopped = false with get, set
    member val DefaultPrevented = false with get, set
    member this.StopPropagation() = this.PropagationStopped <- true

    member this.StopImmediatePropagation() =
        this.PropagationStopped <- true
        this.ImmediateStopped <- true

    member this.PreventDefault() =
        if cancelable then this.DefaultPrevented <- true

type EventProxy() =
    inherit System.Reflection.DispatchProxy()
    member val Event: TwinEvent = null with get, set

    override this.Invoke(targetMethod, args) =
        let e = this.Event
        let name = targetMethod.Name
        let display = if name.StartsWith "get_" || name.StartsWith "set_" then name.Substring 4 else name

        let value: obj =
            match name with
            | "get_type" -> box e.Type
            | "get_target" -> BridgeCore.ProxyOf e.Target
            | "get_currentTarget" -> if isNull (box e.CurrentTargetOverride) then BridgeCore.ProxyOf e.CurrentTarget else e.CurrentTargetOverride ()
            | "get_eventPhase" -> box (float e.Phase)
            | "get_bubbles" -> box e.Bubbles
            | "get_cancelable" -> box e.Cancelable
            | "get_cancelBubble" -> box e.PropagationStopped
            | "set_cancelBubble" -> (if (args[0] :?> bool) then e.StopPropagation ()); null
            | "get_defaultPrevented" -> box e.DefaultPrevented
            | "get_isTrusted" -> box false
            | "preventDefault" -> e.PreventDefault (); null
            | "stopPropagation" -> e.StopPropagation (); null
            | "stopImmediatePropagation" -> e.StopImmediatePropagation (); null
            | _ when name.StartsWith "get_" && e.Init.ContainsKey display ->
                match e.Init[display] with
                | :? TwinNode as node -> BridgeCore.ProxyOf node
                | value -> value
            | _ -> raise (NotSupportedException ("The DOM twin does not implement " + targetMethod.DeclaringType.Name + "." + display))

        BridgeCore.Serve (targetMethod.DeclaringType.Name + "." + display)
        BridgeCore.Wrap (targetMethod.ReturnType, value)

[<RequireQualifiedAccess>]
module Events =
    type private Listener = { Type: string; Capture: bool; Callback: obj -> unit; mutable Removed: bool }

    let private listeners = ConditionalWeakTable<TwinNode, ResizeArray<Listener>> ()
    let private proxies = ConditionalWeakTable<TwinEvent, obj> ()

    let private kindOf (eventType: string) =
        match eventType with
        | "click" | "dblclick" | "auxclick" | "contextmenu" -> Pointer
        | t when t.StartsWith "pointer" -> Pointer
        | t when t.StartsWith "mouse" -> Mouse
        | t when t.StartsWith "key" -> Keyboard
        | "input" | "beforeinput" -> Input
        | t when t.StartsWith "focus" || t = "blur" -> Focus
        | _ -> Plain

    let create (spec: EventSpec) : TwinEvent =
        let init = Dictionary<string, obj> ()

        for key, value in spec.Init do
            init[key] <- value

        TwinEvent (spec.Type, spec.Kind, spec.Bubbles, spec.Cancelable, init)

    let proxyOf (event: TwinEvent) : obj =
        proxies.GetValue (event, fun e ->
            let iface =
                BridgeCore.Interface (EventSpec.constructorName e.Kind)

            let proxy = System.Reflection.DispatchProxy.Create (iface, typeof<EventProxy>) :?> EventProxy
            proxy.Event <- e
            box proxy)

    let eventOf (value: obj) : TwinEvent = (value :?> EventProxy).Event

    let listen (node: TwinNode) (eventType: string) (capture: bool) (callback: obj -> unit) : unit -> unit =
        let list = listeners.GetValue (node, fun _ -> ResizeArray ())
        let entry = { Type = eventType; Capture = capture; Callback = callback; Removed = false }
        list.Add entry

        fun () ->
            entry.Removed <- true
            list.Remove entry |> ignore

    let private invoke (node: TwinNode) (event: TwinEvent) (phase: int) (failures: ResizeArray<exn>) =
        match listeners.TryGetValue node with
        | true, list ->
            event.CurrentTarget <- node
            event.Phase <- phase

            for entry in List.ofSeq list do
                let applies =
                    entry.Type = event.Type
                    && not entry.Removed
                    && (phase = 2 || (phase = 1) = entry.Capture)

                if applies && not event.ImmediateStopped then
                    try
                        entry.Callback (proxyOf event)
                    with error ->
                        failures.Add error
        | false, _ -> ()

    /// <summary>Dispatches <c>event</c> at <c>target</c> through the capture, target and bubble phases; false when cancelled.</summary>
    let dispatch (target: TwinNode) (event: TwinEvent) : bool =
        target.Document.Check ("dispatch " + event.Type)
        event.Target <- target
        let path = ResizeArray<TwinNode>()
        let mutable cursor = target.Parent

        while not (isNull cursor) do
            path.Add cursor
            cursor <- cursor.Parent

        let failures = ResizeArray<exn>()

        for i in path.Count - 1 .. -1 .. 0 do
            if not event.PropagationStopped then invoke path[i] event 1 failures

        if not event.PropagationStopped then invoke target event 2 failures

        if event.Bubbles then
            for node in path do
                if not event.PropagationStopped then invoke node event 3 failures

        event.Phase <- 0
        event.CurrentTarget <- null
        event.CurrentTargetOverride <- Unchecked.defaultof<_>

        if failures.Count > 0 then
            raise (TwinListenerException (event.Type, failures[0]))

        not event.DefaultPrevented

    let private fire (target: TwinNode) (eventType: string) (bubbles: bool) (cancelable: bool) =
        dispatch target (create { Type = eventType; Kind = kindOf eventType; Bubbles = bubbles; Cancelable = cancelable; Init = [] })

    let private isDisabled (e: TwinElement) =
        e.HasAttribute "disabled" && (e.LocalName = "button" || e.LocalName = "input" || e.LocalName = "select" || e.LocalName = "textarea")

    let private formOf (e: TwinElement) =
        let mutable cursor = e.Parent

        while not (isNull cursor) && not (match cursor with :? TwinElement as f -> f.LocalName = "form" | _ -> false) do
            cursor <- cursor.Parent

        cursor

    let private labelled (label: TwinElement) : TwinElement =
        match label.GetAttribute "for" with
        | null ->
            let rec find (node: TwinNode) =
                node.Children
                |> List.tryPick (fun c ->
                    match c with
                    | :? TwinElement as e when List.contains e.LocalName [ "input"; "select"; "textarea"; "button" ] -> Some e
                    | _ -> find c)

            find label |> Option.toObj
        | id -> label.Document.ElementById id

    /// <summary>The <c>click ()</c> of HTML: activation behaviour around a bubbling, cancelable click.</summary>
    let rec click (element: TwinElement) : unit =
        if not (isDisabled element) then
            let kind = if element.LocalName = "input" then match element.GetAttribute "type" with null -> "text" | t -> t.ToLowerInvariant () else ""
            let toggles = kind = "checkbox" || kind = "radio"
            let before = element.Checked

            let group =
                if kind = "radio" then
                    let mutable root: TwinNode = element
                    while not (isNull root.Parent) do root <- root.Parent

                    let rec radios (node: TwinNode) =
                        node.Children
                        |> List.collect (fun c ->
                            match c with
                            | :? TwinElement as e when e.LocalName = "input" && e.GetAttribute "type" = "radio" && e.GetAttribute "name" = element.GetAttribute "name" -> e :: radios c
                            | _ -> radios c)

                    radios root |> List.map (fun r -> r, r.Checked)
                else
                    []

            if kind = "checkbox" then element.Checked <- not before
            if kind = "radio" then element.Checked <- true

            let accepted = fire element "click" true true

            if toggles then
                if not accepted then
                    if kind = "checkbox" then element.Checked <- before
                    for radio, state in group do radio.Checked <- state
                elif element.Checked <> before then
                    fire element "input" true false |> ignore
                    fire element "change" true false |> ignore
            elif accepted then
                let submits =
                    (element.LocalName = "button" && (match element.GetAttribute "type" with null | "submit" -> true | _ -> false))
                    || (element.LocalName = "input" && kind = "submit")

                match formOf element with
                | :? TwinElement as form when submits -> fire form "submit" true true |> ignore
                | _ -> ()

                if element.LocalName = "label" then
                    match labelled element with
                    | null -> ()
                    | control -> click control

    let private focusable (e: TwinElement) =
        not (isDisabled e)
        && (List.contains e.LocalName [ "input"; "select"; "textarea"; "button" ]
            || (e.LocalName = "a" && e.HasAttribute "href")
            || e.HasAttribute "tabindex")

    let blur (element: TwinElement) : unit =
        let document = element.Document

        if obj.ReferenceEquals (document.ActiveElement, element) then
            document.ActiveElement <- null
            fire element "blur" false false |> ignore
            fire element "focusout" true false |> ignore

    let focus (element: TwinElement) : unit =
        let document = element.Document

        if focusable element && not (obj.ReferenceEquals (document.ActiveElement, element)) then
            match document.ActiveElement with
            | null -> ()
            | previous -> blur previous

            document.ActiveElement <- element
            fire element "focus" false false |> ignore
            fire element "focusin" true false |> ignore

    // Per node: the listeners added through addEventListener, by type and callback object, with their removal.
    let private added = ConditionalWeakTable<TwinNode, ResizeArray<string * obj * (unit -> unit)>> ()

    let private registered =
        lazy
            (Bridge.ensure ()

             Bridge.registerMember "addEventListener" 3 (fun node args ->
                 let eventType, callback = string args[0], args[1]
                 let capture = match args[2] with :? bool as b -> b | _ -> false

                 let invoke =
                     match callback with
                     | :? (Fable.Core.TS.Dom.Event -> unit) as f -> fun (e: obj) -> f (e :?> Fable.Core.TS.Dom.Event)
                     | :? (obj -> unit) as f -> f
                     | other -> raise (NotSupportedException $"The DOM twin does not accept a {other.GetType().Name} listener")

                 let list = added.GetValue (node, fun _ -> ResizeArray ())

                 if not (list.Exists (fun (t, c, _) -> t = eventType && obj.ReferenceEquals (c, callback))) then
                     list.Add ((eventType, callback, listen node eventType capture invoke))

                 null)

             Bridge.registerMember "removeEventListener" 3 (fun node args ->
                 match added.TryGetValue node with
                 | true, list ->
                     match list.FindIndex (fun (t, c, _) -> t = string args[0] && obj.ReferenceEquals (c, args[1])) with
                     | -1 -> ()
                     | index ->
                         let _, _, remove = list[index]
                         remove ()
                         list.RemoveAt index
                 | false, _ -> ()

                 null)

             Bridge.registerMember "dispatchEvent" 1 (fun node args -> box (dispatch node (eventOf args[0])))
             Bridge.registerMember "click" 0 (fun node _ -> click (node :?> TwinElement); null)
             Bridge.registerMember "focus" 1 (fun node _ -> focus (node :?> TwinElement); null)
             Bridge.registerMember "focus" 0 (fun node _ -> focus (node :?> TwinElement); null)
             Bridge.registerMember "blur" 0 (fun node _ -> blur (node :?> TwinElement); null))

    /// <summary>Registers the event members once per process, after the core members.</summary>
    let ensure () = registered.Force ()
```

`addEventListener`'s `options` argument arrives unwrapped (a `bool` or an options object); read `capture` from an options object with `Init`-style reflection only if a conformance case passes one. Every test in `EventTests.fs` calls `Events.ensure ()` first: add it to `setup` and to each test that creates its own document. From Task 12 on, `TwinDocument.Create` calls it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 60 tests.

- [ ] **Step 6: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 7: Commit**

Paths: `Events.fs`, the testing project file and `EventSpec.fs`, `EventTests.fs`, the twin and test project files, `Ranvier.slnx`. Message: `feat(twin): event dispatch phases, activation behaviour and focus`.

### Task 11: The selector engine

> Amended by A8 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Testing/Selector.fs` (after `EventSpec.fs`)
- Create: `experiments/Ranvier.View.Twin/SelectorMembers.fs` (after `Events.fs`)
- Create: `tests/Ranvier.View.Twin.Tests/SelectorTests.fs`

**Interfaces:**
- Consumes: Task 9's `Bridge.registerMember`, `Bridge.nodeOf`, `ListProxy.Create` (make `ListProxy` `internal`, not `private`).
- Produces (module `Ranvier.View.Testing.Selector`, both hosts):
  - `exception SelectorException of selector: string * column: int`
  - `type Parsed` (opaque); `parse: string -> Parsed`; `matches: Parsed -> Element -> bool`
  - `queryAll: root: Node -> selector: string -> Element array` (descendants of `root` in document order); `query: root: Node -> selector: string -> Element option`
- Produces (twin): `SelectorMembers.ensure: unit -> unit`, registering `querySelector`, `querySelectorAll`, `matches` and `closest` through the engine, so jsdom and the twin match identically only where both use the engine (the testing API); the twin's own `querySelector` is the engine.

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/SelectorTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.SelectorTests

open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Twin
open Ranvier.View.Testing

let private tree () =
    SelectorMembers.ensure ()
    let document = new TwinDocument ()
    let root = document.CreateElement "div"
    Html.parseInto root "<ul id=\"l\" class=\"a b\"><li data-k=\"1\">x</li><li data-k=\"22\" class=\"b\">y</li><li><input type=\"checkbox\" checked><button disabled></button></li></ul><p>z</p>"
    document, root.Native

let private tags (elements: Element array) = elements |> Array.map (fun e -> e.localName) |> List.ofArray

[<Tests>]
let tests =
    testList
        "Selector"
        [
            testCase "type, id, class and universal selectors"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (Selector.queryAll root "li").Length 3 "type"
                Expect.equal (tags (Selector.queryAll root "#l")) [ "ul" ] "id"
                Expect.equal (Selector.queryAll root ".b").Length 2 "class"
                Expect.equal (Selector.queryAll root "*").Length 7 "universal"

            testCase "attribute operators"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (Selector.queryAll root "[data-k]").Length 2 "exists"
                Expect.equal (Selector.queryAll root "[data-k=\"1\"]").Length 1 "equals"
                Expect.equal (Selector.queryAll root "[class~=b]").Length 2 "includes"
                Expect.equal (Selector.queryAll root "[data-k^=2]").Length 1 "prefix"
                Expect.equal (Selector.queryAll root "[data-k$='2']").Length 1 "suffix"
                Expect.equal (Selector.queryAll root "[class*=\" b\"]").Length 1 "contains"

            testCase "pseudo-classes"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (tags (Selector.queryAll root ":checked")) [ "input" ] "checked"
                Expect.equal (tags (Selector.queryAll root ":disabled")) [ "button" ] "disabled"
                Expect.equal (Selector.queryAll root "li:first-child").[0].textContent (Some "x") "first-child"
                Expect.equal (Selector.queryAll root "li:last-child").Length 1 "last-child"
                Expect.equal (Selector.queryAll root "li:nth-child(2)").[0].textContent (Some "y") "nth-child"
                Expect.equal (Selector.queryAll root "li:not(.b)").Length 2 "not"

            testCase "combinators and lists keep document order"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (tags (Selector.queryAll root "ul > li > input")) [ "input" ] "child"
                Expect.equal (tags (Selector.queryAll root "div button")) [ "button" ] "descendant from the root's own tag"
                Expect.equal (tags (Selector.queryAll root "p, ul")) [ "ul"; "p" ] "list in document order"

            testCase "the twin's querySelector is the engine"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal ((root :?> Element).querySelectorAll "li").length 3.0 "querySelectorAll"
                Expect.isSome ((root :?> Element).querySelector "#l") "querySelector"

            testCase "unsupported syntax names the column"
            <| fun () ->
                Expect.throwsC (fun () -> Selector.parse "li:hover" |> ignore) (fun e ->
                    match e with
                    | SelectorException (selector, column) -> Expect.equal (selector, column) ("li:hover", 3) "column of the pseudo-class"
                    | other -> failtestf "unexpected %O" other)
        ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The namespace or module 'Selector' is not defined`.

- [ ] **Step 3: Write `Selector.fs`**

```fsharp
/// <summary>One selector engine over the DOM interfaces, for the testing API on both hosts and the twin's own queries.</summary>
module Ranvier.View.Testing.Selector

open Fable.Core.TS.Dom

/// <summary>Selector syntax outside the supported set, at a 0-based column.</summary>
exception SelectorException of selector: string * column: int with
    override this.Message = $"The selector '{this.selector}' is not supported at column {this.column}"

type private Operator =
    | Exists
    | Equals
    | Includes
    | Prefix
    | Suffix
    | Contains

type private Simple =
    | Tag of string
    | Universal
    | Id of string
    | Class of string
    | Attribute of name: string * operator: Operator * value: string
    | Checked
    | Disabled
    | FirstChild
    | LastChild
    | NthChild of int
    | Not of Simple

type private Combinator =
    | Descendant
    | Child

/// <summary>A selector list; each complex selector holds its compounds right to left with the combinator to the next.</summary>
type Parsed = private Parsed of (Simple list * Combinator) list list

let private isNameChar (c: char) = System.Char.IsLetterOrDigit c || c = '-' || c = '_'

let parse (selector: string) : Parsed =
    let fail column = raise (SelectorException (selector, column))
    let mutable i = 0
    let peek () = if i < selector.Length then selector[i] else '\000'

    let name () =
        let start = i
        while i < selector.Length && isNameChar selector[i] do i <- i + 1
        if i = start then fail start
        selector.Substring (start, i - start)

    let skipSpace () =
        while peek () = ' ' do i <- i + 1

    let quoted () =
        match peek () with
        | '"' | '\'' as q ->
            i <- i + 1
            let start = i
            while i < selector.Length && selector[i] <> q do i <- i + 1
            if i >= selector.Length then fail start
            let value = selector.Substring (start, i - start)
            i <- i + 1
            value
        | _ -> name ()

    let rec simple () : Simple =
        let start = i

        match peek () with
        | '#' -> i <- i + 1; Id (name ())
        | '.' -> i <- i + 1; Class (name ())
        | '*' -> i <- i + 1; Universal
        | '[' ->
            i <- i + 1
            skipSpace ()
            let attribute = (name ()).ToLowerInvariant ()
            skipSpace ()

            let operator =
                match peek () with
                | ']' -> Exists
                | '=' -> Equals
                | '~' -> Includes
                | '^' -> Prefix
                | '$' -> Suffix
                | '*' -> Contains
                | _ -> fail i

            let value =
                if operator = Exists then
                    ""
                else
                    i <- i + (if operator = Equals then 1 else 2)
                    if operator <> Equals && selector[i - 1] <> '=' then fail (i - 1)
                    skipSpace ()
                    quoted ()

            skipSpace ()
            if peek () <> ']' then fail i
            i <- i + 1
            Attribute (attribute, operator, value)
        | ':' ->
            i <- i + 1

            match name () with
            | "checked" -> Checked
            | "disabled" -> Disabled
            | "first-child" -> FirstChild
            | "last-child" -> LastChild
            | "nth-child" ->
                if peek () <> '(' then fail i
                i <- i + 1
                let n = name ()
                if peek () <> ')' then fail i
                i <- i + 1

                match System.Int32.TryParse n with
                | true, value -> NthChild value
                | false, _ -> fail (i - n.Length - 1)
            | "not" ->
                if peek () <> '(' then fail i
                i <- i + 1
                let inner = simple ()
                if peek () <> ')' then fail i
                i <- i + 1
                Not inner
            | _ -> fail start
        | c when isNameChar c -> Tag ((name ()).ToLowerInvariant ())
        | _ -> fail start

    let compound () =
        let parts = ResizeArray<Simple>()
        parts.Add (simple ())

        while i < selector.Length && (match peek () with '#' | '.' | '[' | ':' | '*' -> true | _ -> false) do
            parts.Add (simple ())

        List.ofSeq parts

    let complex () =
        let compounds = ResizeArray<Simple list * Combinator>()
        skipSpace ()
        let mutable current = compound ()
        let mutable going = true

        while going do
            let before = i
            skipSpace ()

            match peek () with
            | '>' ->
                i <- i + 1
                skipSpace ()
                compounds.Add ((current, Child))
                current <- compound ()
            | ',' | '\000' -> going <- false
            | _ when i > before ->
                compounds.Add ((current, Descendant))
                current <- compound ()
            | _ -> fail i

        compounds.Add ((current, Descendant))
        compounds |> Seq.rev |> List.ofSeq

    let list = ResizeArray<(Simple list * Combinator) list>()
    list.Add (complex ())

    while peek () = ',' do
        i <- i + 1
        list.Add (complex ())

    if i < selector.Length then fail i
    Parsed (List.ofSeq list)

let private parentElement (element: Element) : Element =
    match element.parentNode with
    | Some parent when (unbox<Node> parent).nodeType = 1.0 -> unbox<Element> parent
    | _ -> Unchecked.defaultof<_>

let private siblings (element: Element) : Element list =
    match element.parentNode with
    | Some parent ->
        let mutable child = (unbox<Node> parent).firstChild
        let result = ResizeArray<Element>()

        let mutable going = true

        while going do
            match child with
            | Some value ->
                let node = unbox<Node> value
                if node.nodeType = 1.0 then result.Add (unbox<Element> node)
                child <- node.nextSibling
            | None -> going <- false

        List.ofSeq result
    | None -> [ element ]

let private attribute (element: Element) (name: string) = Option.toObj (element.getAttribute name)

let rec private test (element: Element) (simple: Simple) : bool =
    match simple with
    | Tag tag -> element.localName = tag
    | Universal -> true
    | Id id -> attribute element "id" = id
    | Class name ->
        match attribute element "class" with
        | null -> false
        | classes -> classes.Split ([| ' '; '\t'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries) |> Array.contains name
    | Attribute (name, operator, value) ->
        match attribute element name, operator with
        | null, _ -> false
        | _, Exists -> true
        | actual, Equals -> actual = value
        | actual, Includes -> actual.Split ' ' |> Array.contains value
        | actual, Prefix -> value <> "" && actual.StartsWith value
        | actual, Suffix -> value <> "" && actual.EndsWith value
        | actual, Contains -> value <> "" && actual.Contains value
    | Checked ->
        match element.localName with
        | "input" -> (unbox<HTMLInputElement> element).``checked``
        | "option" -> (unbox<HTMLOptionElement> element).selected
        | _ -> false
    | Disabled ->
        List.contains element.localName [ "button"; "input"; "select"; "textarea"; "option"; "optgroup"; "fieldset" ]
        && not (isNull (attribute element "disabled"))
    | FirstChild -> obj.ReferenceEquals (List.tryHead (siblings element) |> Option.toObj, element)
    | LastChild -> obj.ReferenceEquals (List.tryLast (siblings element) |> Option.toObj, element)
    | NthChild n -> siblings element |> List.tryItem (n - 1) |> Option.exists (fun e -> obj.ReferenceEquals (e, element))
    | Not inner -> not (test element inner)

let rec private matchFrom (element: Element) (compounds: (Simple list * Combinator) list) : bool =
    match compounds with
    | [] -> true
    | (simples, _) :: rest when List.forall (test element) simples ->
        match rest with
        | [] -> true
        | (_, _) :: _ ->
            let _, combinator = List.head compounds

            match combinator with
            | Child -> let parent = parentElement element in not (isNull (box parent)) && matchFrom parent rest
            | Descendant ->
                let mutable ancestor = parentElement element
                let mutable found = false

                while not found && not (isNull (box ancestor)) do
                    found <- matchFrom ancestor rest
                    ancestor <- parentElement ancestor

                found
    | _ -> false

let matches (Parsed list) (element: Element) : bool = list |> List.exists (matchFrom element)

let queryAll (root: Node) (selector: string) : Element array =
    let parsed = parse selector
    let result = ResizeArray<Element>()

    let rec walk (node: Node) =
        let mutable child = node.firstChild

        let mutable going = true

        while going do
          match child with
          | None -> going <- false
          | Some value ->
            let current = unbox<Node> value

            if current.nodeType = 1.0 then
                let element = unbox<Element> current
                if matches parsed element then result.Add element
                walk current

            child <- current.nextSibling

    walk root
    result.ToArray ()

let query (root: Node) (selector: string) : Element option = queryAll root selector |> Array.tryHead
```

Each compound carries the combinator that joins it to the compound on its left; `parse` reverses the list so matching starts at the subject. Check that `complex ()` stores the combinator with the right-hand compound (the one `matchFrom` reads from `List.head compounds`): a test of `"ul > li > input"` and `"div button"` in Step 1 fails if they are paired the wrong way, and the fix is to store `(compound, combinatorToItsLeft)`. The two child walks test the option with `match`, per the repository hazard; indent the body of `walk`'s `Some` branch (the `if` and the `child <- current.nextSibling` line) under it, which Fantomas lays out.

`SelectorMembers.fs`:

```fsharp
module internal Ranvier.View.Twin.SelectorMembers

open Ranvier.View.Testing

let private registered =
    lazy
        (Bridge.ensure ()

         Bridge.registerMember "querySelector" 1 (fun node args ->
             match Selector.query node.Native (string args[0]) with
             | Some element -> box (Bridge.nodeOf element)
             | None -> null)

         Bridge.registerMember "querySelectorAll" 1 (fun node args ->
             let found = Selector.queryAll node.Native (string args[0]) |> Array.map (fun e -> Bridge.nodeOf e) |> List.ofArray
             ListProxy.Create (fun () -> found))

         Bridge.registerMember "matches" 1 (fun node args -> box (Selector.matches (Selector.parse (string args[0])) (unbox node.Native)))

         Bridge.registerMember "closest" 1 (fun node args ->
             let parsed = Selector.parse (string args[0])
             let mutable cursor = node

             while not (isNull cursor) && not (cursor :? TwinElement && Selector.matches parsed (unbox cursor.Native)) do
                 cursor <- cursor.Parent

             box cursor))

let ensure () = registered.Force ()
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 66 tests.

- [ ] **Step 5: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 6: Commit**

Paths: `Selector.fs`, `SelectorMembers.fs`, `SelectorTests.fs`, the testing, twin and test project files. Message: `feat(testing): selector engine for both hosts; the twin's queries use it`.

### Task 12: The clock, the host and the session

> Amended by A4 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Twin/Clock.fs`, `Host.fs`, `Session.fs` (in that order after `SelectorMembers.fs`)
- Modify: `experiments/Ranvier.View.Twin/Tree.fs` (`TwinDocument.AfterDispatch`, `DispatchDepth`), `Events.fs` (`dispatch` calls `AfterDispatch` at depth 0)
- Create: `tests/Ranvier.View.Twin.Tests/ClockTests.fs`, `SessionTests.fs`, `Fixtures.fs`

**Interfaces:**
- Consumes: Tasks 7-11; `Native.IHost`, `Native.install`; `EventSpec.IEventSource`, `EventSpec.install`; `Mounting.mountWith`, `Mounting.Mounted`, `Mounting.BuildContext`; `Scheduling.CommitMode`, `Scheduling.CommitFailure`; `Template.Component`, `Component.render`, `Program.run`.
- Produces:
  - `TwinClock` and `ClockPending` with the spec §3.8 members, plus `SetTimeout: (unit -> unit) * float -> int`, `ClearTimeout: int -> unit`, `RequestAnimationFrame: (float -> unit) -> int`, `CancelAnimationFrame: int -> unit`; `exception TwinRunawayException of queue: string * origin: string`
  - `TwinHost`: `static Create: unit -> TwinHost`, `Document: TwinDocument`, `Clock: TwinClock`; implements `Native.IHost` and `EventSpec.IEventSource`; installed for the thread until its document is disposed
  - `Commits`, `Reports`, `TwinMount`, `TwinMount.defaults`, `Session` (spec §3.9, without `Dump` until Task 21), `exception TwinReportException of reports: exn list list`, `module Twin` with `mount`, `mountWith`, `mountView`
- A session settles after each top-level dispatch (`Clock.Settle` then the report check), so an interaction on a queued mount is visible on return; harnesses (Task 13) use `TwinHost` alone and settle only when the case asks.

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/Fixtures.fs`:

```fsharp
module Ranvier.View.Twin.Tests.Fixtures

open Ranvier
open Ranvier.View.Foundation.Template

type CounterState = { Count: Signal<int> }

let counter =
    Component.define (fun _ (start: int) -> { Count = Api.createSignal start }) (fun t ->
        t.button
            [| t.Events.on ("click", fun state _ -> state.Count.Value <- state.Count.Value + 1) |]
            [| t.text (fun state -> string state.Count.Value) |])

let row =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.li [||] [| t.text (fun item -> string (item ())) |])

let rows =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul [||] [| t.each (fun (n: int) -> n) (fun (items: Signal<int array>) -> items.Value) row |])

let failing =
    Component.define (fun _ () -> ()) (fun t ->
        t.button [| t.Events.on ("click", fun _ _ -> failwith "handler failed") |] [||])
```

`tests/Ranvier.View.Twin.Tests/ClockTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.ClockTests

open Expecto
open Ranvier.View.Twin

[<Tests>]
let tests =
    testList
        "Clock"
        [
            testCase "timers run by due time, then insertion, with microtasks after each"
            <| fun () ->
                let clock = TwinClock ()
                let log = ResizeArray<string>()
                clock.SetTimeout ((fun () -> log.Add "b"; clock.Post (fun () -> log.Add "b-micro")), 10.0) |> ignore
                clock.SetTimeout ((fun () -> log.Add "a"), 5.0) |> ignore
                clock.SetTimeout ((fun () -> log.Add "c"), 10.0) |> ignore
                clock.AdvanceBy 10.0
                Expect.equal (List.ofSeq log) [ "a"; "b"; "b-micro"; "c" ] "order"
                Expect.equal clock.Now 10.0 "now"

            testCase "frames run at each FrameMs boundary crossed"
            <| fun () ->
                let clock = TwinClock ()
                let stamps = ResizeArray<float>()
                let rec frame (t: float) = stamps.Add t; if stamps.Count < 3 then clock.RequestAnimationFrame frame |> ignore
                clock.RequestAnimationFrame frame |> ignore
                clock.AdvanceBy 40.0
                Expect.equal (List.ofSeq stamps) [ 16.0; 32.0 ] "two boundaries"
                clock.Frame ()
                Expect.equal (List.ofSeq stamps) [ 16.0; 32.0; 40.0 ] "Frame runs at Now"

            testCase "real time never advances the clock"
            <| fun () ->
                let clock = TwinClock ()
                System.Threading.Thread.Sleep 20
                Expect.equal clock.Now 0.0 "unchanged"

            testCase "Settle raises when a queue keeps refilling"
            <| fun () ->
                let clock = TwinClock ()
                use graph = new Ranvier.Graph ()
                let rec again () = clock.Post again
                clock.Post again

                Expect.throwsC (fun () -> clock.Settle (graph, 100)) (fun e ->
                    match e with
                    | TwinRunawayException (queue, _) -> Expect.equal queue "microtasks" "queue"
                    | other -> failtestf "unexpected %O" other)

            testCase "the TimeProvider reads twin time"
            <| fun () ->
                let clock = TwinClock ()
                clock.AdvanceBy 1500.0
                Expect.equal (clock.TimeProvider.GetUtcNow ()) (System.DateTimeOffset.UnixEpoch.AddMilliseconds 1500.0) "now"
        ]
```

`tests/Ranvier.View.Twin.Tests/SessionTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.SessionTests

open System
open Expecto
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Twin
open Ranvier.View.Foundation.Representation
open Ranvier.View.Twin.Tests.Fixtures

let private items (session: Session) =
    let list = session.Host.querySelectorAll "li"
    [ for i in 0 .. int list.length - 1 -> Bridge.nodeOf (list.item (float i)).Value ]

[<Tests>]
let tests =
    testList
        "Session"
        [
            testCase "a mounted component renders and a click updates it"
            <| fun () ->
                use session = Twin.mount counter 1
                Expect.equal (session.Text ()) "1" "first render"
                (session.Host.querySelector "button").Value |> unbox<HTMLElement> |> fun b -> b.click ()
                Expect.equal (session.Text ()) "2" "after the click"

            testCase "removing two middle rows keeps the other three"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 1; 2; 3; 4; 5 |]
                        Program.run (Ranvier.View.Foundation.Template.Component.render rows source.Value) context.Backend)

                let before = items session
                session.Act (fun () -> source.Value.Value <- [| 1; 2; 5 |])
                Expect.equal (session.Text ()) "125" "text"
                let after = items session
                Expect.isTrue (obj.ReferenceEquals (after[0], before[0]) && obj.ReferenceEquals (after[2], before[4])) "nodes kept"

            testCase "sessions on two threads are isolated"
            <| fun () ->
                let results = Collections.Concurrent.ConcurrentBag<string>()
                let mutable sessionOfFirst: Session = Unchecked.defaultof<_>
                let started = new Threading.ManualResetEventSlim ()

                let first =
                    Threading.Thread (fun () ->
                        use session = Twin.mount counter 10
                        sessionOfFirst <- session
                        started.Set ()
                        Threading.Thread.Sleep 50
                        results.Add (session.Text ()))

                let second =
                    Threading.Thread (fun () ->
                        use session = Twin.mount counter 20
                        (session.Host.querySelector "button").Value |> unbox<HTMLElement> |> fun b -> b.click ()
                        results.Add (session.Text ()))

                first.Start ()
                started.Wait ()
                second.Start ()
                second.Join ()
                first.Join ()
                Expect.equal (Set.ofSeq results) (set [ "10"; "21" ]) "each thread saw its own document"
                Expect.throwsT<InvalidOperationException> (fun () -> sessionOfFirst.Host.nodeType |> ignore) "cross-thread use"

            testCase "a handler failure under Raise ends the interaction that caused it"
            <| fun () ->
                use session = Twin.mount failing ()
                let button = (session.Host.querySelector "button").Value |> unbox<HTMLElement>
                Expect.throwsC (fun () -> button.click ()) (fun e -> Expect.isTrue (e :? TwinReportException || e :? TwinListenerException) "raised")

            testCase "Collect keeps the reports on the session"
            <| fun () ->
                use session = Twin.mountWith { TwinMount.defaults with Reports = Reports.Collect } failing ()
                let button = (session.Host.querySelector "button").Value |> unbox<HTMLElement>
                button.click ()
                Expect.equal session.Reports.Length 1 "one report, and the click returned"
                Expect.stringContains (session.Reports |> List.concat |> List.head).Message "handler failed" "the handler's error"

            testCase "queued commits apply when the session settles"
            <| fun () ->
                use session =
                    Twin.mountWith { TwinMount.defaults with Commits = Commits.Queued Ranvier.View.Foundation.Scheduling.CollectAndContinue } counter 1

                session.Clock.RunMicrotasks () |> ignore
                (session.Host.querySelector "button").Value |> unbox<HTMLElement> |> fun b -> b.click ()
                Expect.equal (session.Text ()) "2" "settled after the dispatch"
        ]
```

A handler failure on a template node goes to the mount's `report` when the handler is outside a boundary, or raises out of the delegated listener; the third test accepts either outcome, and Task 13 pins the one jsdom shows. Under `Reports.Collect` the session also collects listener failures (Amendment A4), so the Collect test holds whichever route the failure takes: a mount report or a `TwinListenerException`, each one entry of `Session.Reports`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The type 'TwinClock' is not defined`.

- [ ] **Step 3: Write `Clock.fs`**

```fsharp
namespace Ranvier.View.Twin

open System
open System.Collections.Generic

type ClockPending = { Microtasks: int; Timers: int; Frames: int; NextTimerAt: float option }

/// <summary>A queue that kept refilling past <c>Settle</c>'s round limit; <c>origin</c> names its last callback.</summary>
exception TwinRunawayException of queue: string * origin: string with
    override this.Message = $"The twin's {this.queue} kept refilling; the last callback was {this.origin}"

type private Timer = { Id: int; Due: float; Sequence: int; Callback: unit -> unit }

/// <summary>The twin's event loop: microtasks, timers and animation frames, run only when the test asks.</summary>
[<Sealed>]
type TwinClock() as this =
    let microtasks = Queue<unit -> unit>()
    let timers = ResizeArray<Timer>()
    let frames = ResizeArray<int * (float -> unit)>()
    let mutable now = 0.0
    let mutable sequence = 0
    let mutable lastOrigin = ""
    let origin (callback: obj) = callback.GetType().FullName

    let provider =
        { new TimeProvider() with
            override _.GetUtcNow() = DateTimeOffset.UnixEpoch.AddMilliseconds now
            override _.TimestampFrequency = 10_000_000L
            override _.GetTimestamp() = int64 (now * 10_000.0)

            override _.CreateTimer(callback, state, dueTime, period) =
                let mutable id = -1
                let mutable periodMs = -1.0

                let rec schedule (delay: float) =
                    id <- this.SetTimeout ((fun () ->
                        callback.Invoke state
                        if periodMs > 0.0 then schedule periodMs), delay)

                let change (due: TimeSpan) (every: TimeSpan) =
                    if id >= 0 then this.ClearTimeout id
                    id <- -1
                    periodMs <- (if every = Threading.Timeout.InfiniteTimeSpan then -1.0 else every.TotalMilliseconds)
                    if due <> Threading.Timeout.InfiniteTimeSpan then schedule due.TotalMilliseconds
                    true

                change dueTime period |> ignore

                { new Threading.ITimer with
                    member _.Change(due, every) = change due every
                    member _.Dispose() = if id >= 0 then this.ClearTimeout id
                    member _.DisposeAsync() = (if id >= 0 then this.ClearTimeout id); ValueTask ()
                }
        }

    member _.Now = now
    member _.TimeProvider: TimeProvider = provider
    member val FrameMs = 16.0 with get, set
    member _.Post(callback: unit -> unit) = microtasks.Enqueue callback

    member _.RunMicrotasks() : int =
        let mutable count = 0

        while microtasks.Count > 0 do
            let callback = microtasks.Dequeue ()
            lastOrigin <- origin callback
            callback ()
            count <- count + 1

        count

    member _.SetTimeout(callback: unit -> unit, milliseconds: float) : int =
        sequence <- sequence + 1
        timers.Add { Id = sequence; Due = now + max 0.0 milliseconds; Sequence = sequence; Callback = callback }
        sequence

    member _.ClearTimeout(id: int) = timers.RemoveAll (fun t -> t.Id = id) |> ignore

    member _.RequestAnimationFrame(callback: float -> unit) : int =
        sequence <- sequence + 1
        frames.Add ((sequence, callback))
        sequence

    member _.CancelAnimationFrame(id: int) = frames.RemoveAll (fun (i, _) -> i = id) |> ignore

    member private this.RunTimer(timer: Timer) =
        timers.Remove timer |> ignore
        now <- max now timer.Due
        lastOrigin <- origin timer.Callback
        timer.Callback ()
        this.RunMicrotasks () |> ignore

    member this.Frame() =
        let due = List.ofSeq frames
        frames.Clear ()

        for _, callback in due do
            lastOrigin <- origin callback
            callback now
            this.RunMicrotasks () |> ignore

    member this.AdvanceBy(milliseconds: float) =
        let target = now + milliseconds
        let mutable going = true

        while going do
            let timer =
                timers
                |> Seq.filter (fun t -> t.Due <= target)
                |> Seq.sortBy (fun t -> t.Due, t.Sequence)
                |> Seq.tryHead

            let frame =
                if frames.Count = 0 then None
                else
                    let boundary = (floor (now / this.FrameMs) + 1.0) * this.FrameMs
                    if boundary <= target then Some boundary else None

            match timer, frame with
            | Some t, Some f when f < t.Due ->
                now <- f
                this.Frame ()
            | Some t, _ -> this.RunTimer t
            | None, Some f ->
                now <- f
                this.Frame ()
            | None, None -> going <- false

        now <- target

    member _.Pending: ClockPending =
        {
            Microtasks = microtasks.Count
            Timers = timers.Count
            Frames = frames.Count
            NextTimerAt = if timers.Count = 0 then None else Some (timers |> Seq.map (fun t -> t.Due) |> Seq.min)
        }

    /// <summary>Pumps the graph's inbox, drains microtasks and runs due timers until all three are empty.</summary>
    member this.Settle(graph: Ranvier.Graph, ?maxRounds: int) =
        let limit = defaultArg maxRounds 10_000
        let counts = Dictionary<string, int> ()
        let bump name n = counts[name] <- (match counts.TryGetValue name with | true, c -> c | _ -> 0) + n
        let mutable rounds = 0
        let mutable busy = true

        while busy do
            let pumped = graph.Pump ()
            let ran = this.RunMicrotasks ()
            let due = timers |> Seq.filter (fun t -> t.Due <= now) |> Seq.sortBy (fun t -> t.Due, t.Sequence) |> List.ofSeq

            for timer in due do
                this.RunTimer timer

            bump "inbox" pumped
            bump "microtasks" ran
            bump "timers" due.Length
            busy <- pumped + ran + due.Length > 0
            rounds <- rounds + 1

            if busy && rounds >= limit then
                let busiest = counts |> Seq.maxBy (fun pair -> pair.Value)
                raise (TwinRunawayException (busiest.Key, lastOrigin))
```

`RunMicrotasks` drains to empty, so a microtask that re-posts itself never returns inside `Settle`'s first round. Cap the drain inside `Settle`: give `RunMicrotasks` a private overload `RunMicrotasks(limit: int)` that stops after `limit` callbacks, and have `Settle` call it with `limit` so the round count grows; the public `RunMicrotasks ()` keeps draining to empty, as the spec's doc comment says. The `Settle raises` test fails until this is in place.

- [ ] **Step 4: Write `Host.fs`**

```fsharp
namespace Ranvier.View.Twin

open System
open Ranvier.View.Foundation
open Ranvier.View.Testing

/// <summary>A twin document with its clock, installed as the thread's DOM host until the document is disposed.</summary>
[<Sealed>]
type TwinHost private (document: TwinDocument, clock: TwinClock) =
    member _.Document = document
    member _.Clock = clock

    static member Create() : TwinHost =
        Events.ensure ()
        SelectorMembers.ensure ()
        let document = new TwinDocument ()
        let host = TwinHost (document, TwinClock ())
        let native = Native.install host
        let events = EventSpec.install host
        document.OnDispose (fun () -> native.Dispose ())
        document.OnDispose (fun () -> events.Dispose ())
        host

    interface Native.IHost with
        member _.Document = document.Native :?> Fable.Core.TS.Dom.Document
        member _.QueueMicrotask callback = clock.Post callback
        member _.SetTimeout(action, milliseconds) = box (clock.SetTimeout (action, float milliseconds))
        member _.ClearTimeout timer = match timer with :? int as id -> clock.ClearTimeout id | _ -> ()
        member _.RequestAnimationFrame callback = box (clock.RequestAnimationFrame callback)
        member _.CancelAnimationFrame frame = match frame with :? int as id -> clock.CancelAnimationFrame id | _ -> ()

        member _.DeleteBetween(first, last) =
            let last = Bridge.nodeOf last
            let mutable cursor = (Bridge.nodeOf first).NextSibling

            while not (isNull cursor) && not (obj.ReferenceEquals (cursor, last)) do
                let next = cursor.NextSibling
                cursor.Remove ()
                cursor <- next

        member _.Listen(target, eventName, listener) = Events.listen (Bridge.nodeOf target) eventName false listener
        member _.OverrideCurrentTarget(event, read) = (Events.eventOf event).CurrentTargetOverride <- read

    interface EventSpec.IEventSource with
        member _.Create spec = Events.proxyOf (Events.create spec) :?> Fable.Core.TS.Dom.Event
```

Disposal actions run newest first (Task 7's `Dispose`), so the event source uninstalls before the DOM host.

In `Tree.fs`, add to `TwinDocument`: `member val internal AfterDispatch: unit -> unit = ignore with get, set` and `member val internal DispatchDepth = 0 with get, set`. In `Events.dispatch`, increment `target.Document.DispatchDepth` before the phases and decrement it in a `finally`; after the decrement, when the depth is 0 and no listener failed, call `target.Document.AfterDispatch ()`. A listener failure raises before `AfterDispatch`, so the failure, not a report check, is what the caller sees.

- [ ] **Step 5: Write `Session.fs`**

```fsharp
namespace Ranvier.View.Twin

open System
open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Scheduling
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Representation

[<RequireQualifiedAccess>]
type Commits =
    | Synchronous
    | Queued of CommitFailure

/// <summary>What a session does with the failures the mount passes to <c>report</c>.</summary>
[<RequireQualifiedAccess>]
type Reports =
    /// <summary>Collects them in <c>Session.Reports</c>.</summary>
    | Collect
    /// <summary>Raises <c>TwinReportException</c> from the call during which they arrived.</summary>
    | Raise

/// <summary>The failures a mount reported during one call under <c>Reports.Raise</c>.</summary>
exception TwinReportException of reports: exn list list with
    override this.Message =
        "The mount reported: "
        + String.Join ("; ", this.reports |> List.concat |> List.map (fun e -> e.Message))

type TwinMount =
    {
        Commits: Commits
        Reports: Reports
        GraphOptions: GraphOptions
        /// <summary>The host element's HTML; the view mounts at its end.</summary>
        Host: string
        /// <summary>The head's HTML before the mount.</summary>
        Head: string
    }

module TwinMount =
    let defaults =
        {
            Commits = Commits.Synchronous
            Reports = Reports.Raise
            GraphOptions = { GraphOptions.Default with Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher) }
            Host = "<div></div>"
            Head = ""
        }

[<Sealed>]
type Session internal (host: TwinHost, element: TwinElement, graph: Graph, policy: Reports) =
    let collected = ResizeArray<exn list>()
    let pending = ResizeArray<exn list>()
    let mutable mounted: Mounting.Mounted = Unchecked.defaultof<_>

    member internal _.Report(errors: exn list) =
        match policy with
        | Reports.Collect -> collected.Add errors
        | Reports.Raise -> pending.Add errors

    member internal _.Attach(value: Mounting.Mounted) = mounted <- value

    member internal _.RaisePending() =
        if pending.Count > 0 then
            let reports = List.ofSeq pending
            pending.Clear ()
            raise (TwinReportException reports)

    member internal this.AfterDispatch() =
        host.Clock.Settle graph
        this.RaisePending ()

    member _.Document = host.Document
    member _.Host: Fable.Core.TS.Dom.HTMLElement = unbox element.Native
    member _.Graph = graph
    member _.Clock = host.Clock
    member _.Mounted = mounted
    member _.Reports: exn list list = List.ofSeq collected

    /// <summary>The host's children in jsdom's serialisation, markers included.</summary>
    member _.Html() = Html.inner element
    member _.Text() = element.TextContent

    /// <summary>Runs <c>action</c> inside the graph and settles the clock.</summary>
    member this.Act(action: unit -> unit) =
        graph.Run action
        host.Clock.Settle graph
        this.RaisePending ()

    interface IDisposable with
        member _.Dispose() =
            if not (isNull (box mounted)) then mounted.Dispose ()
            (graph :> IDisposable).Dispose ()
            (host.Document :> IDisposable).Dispose ()

[<RequireQualifiedAccess>]
module Twin =
    let private start (options: TwinMount) =
        let host = TwinHost.Create ()

        try
            Html.parseInto host.Document.Head options.Head
            Html.parseInto host.Document.Body options.Host

            let element =
                match host.Document.Body.Children |> List.tryFind (fun n -> n :? TwinElement) with
                | Some e -> e :?> TwinElement
                | None -> invalidArg "options" "TwinMount.Host must hold an element"

            let graph = new Graph (options.GraphOptions)
            let session = new Session (host, element, graph, options.Reports)
            host.Document.AfterDispatch <- session.AfterDispatch
            host, element, graph, session
        with _ ->
            (host.Document :> IDisposable).Dispose ()
            reraise ()

    /// <summary>Mounts authored content through <c>Mounting.mountWith</c>.</summary>
    let mountView (options: TwinMount) (build: Mounting.BuildContext -> unit) : Session =
        let host, element, graph, session = start options

        try
            let mode =
                match options.Commits with
                | Commits.Synchronous -> SynchronousCommits
                | Commits.Queued failure -> QueuedCommits (failure, host.Clock.Post)

            session.Attach (Mounting.mountWith mode graph None session.Report element.Native None build)
            session.RaisePending ()
            session
        with _ ->
            (session :> IDisposable).Dispose ()
            reraise ()

    let mountWith (options: TwinMount) (definition: Component<'props>) (props: 'props) : Session =
        mountView options (fun context -> Program.run (Component.render definition props) context.Backend)

    let mount (definition: Component<'props>) (props: 'props) : Session = mountWith TwinMount.defaults definition props
```

`graph.Run` returns the body's value; with `action: unit -> unit` it returns unit. If `GraphOptions` lacks a `Dispatcher` field of that shape in this repo's core, read `docs/content/guide/testing.md:33-41` for the current form and copy it.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, 77 tests. A `NotSupportedException` here names a member the mount uses that Task 9 did not register: register it in `Bridge.fs` with a one-line test in `BridgeTests.fs`, and rerun.

- [ ] **Step 7: Run the gates**

G-NET, G-FORMAT.

- [ ] **Step 8: Commit**

Paths: `Clock.fs`, `Host.fs`, `Session.fs`, `Tree.fs`, `Events.fs`, `Bridge.fs` (if members were added), `Fixtures.fs`, `ClockTests.fs`, `SessionTests.fs`, the project files. Message: `feat(twin): deterministic clock, host installation and the session API`.

### Task 13: The conformance harness, both runners and the first three modules

> Amended by A4 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Testing/Conformance.fs` (after `Selector.fs`)
- Create: `examples/Fable.Ranvier.Playground/tests/JsdomHarness.fs`, `ConformanceCases.fs`, `conformance.test.mjs`
- Modify: `examples/Fable.Ranvier.Playground/tests/RangeChecks.fs`, `TemplateChecks.fs`, `DelegationChecks.fs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (link `EventSpec.fs`, `Selector.fs`, `Conformance.fs` after `Hydration.fs`; compile `JsdomHarness.fs` and `ConformanceCases.fs` after the last migrated module)
- Modify: `examples/Fable.Ranvier.Playground/package.json` (`test:dom`: remove `tests/range.test.mjs tests/template.test.mjs tests/delegation.test.mjs`, add `tests/conformance.test.mjs`)
- Delete: `examples/Fable.Ranvier.Playground/tests/range.test.mjs`, `template.test.mjs`, `delegation.test.mjs`
- Create: `tests/Ranvier.View.Twin.Tests/TwinHarness.fs`, `ConformanceTests.fs`, `MembersTests.fs`
- Modify: `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj` (link the three check modules and `ConformanceCases.fs`), `experiments/Ranvier.View.Twin/members.txt`

**Interfaces:**
- Consumes: Task 12's `TwinHost`; Task 10's `EventSpec`.
- Produces (module `Ranvier.View.Testing.Conformance`, both hosts):
  - `type Harness` (spec §3.9: `Platform`, `Document`, `Post`, `RunMicrotasks`, `AdvanceBy`, `Record`)
  - `type Case = string * (Harness -> unit)`
  - `harness: unit -> Harness` (the harness of the running case; raises outside one); `document: unit -> Document`
  - `ofChecks: (string * (unit -> unit)) array -> Case array` (runs each check with the case's harness current)
  - `platformOnly: platform: string -> reason: string -> string * (unit -> unit) -> string * (unit -> unit)` (prefixes `[jsdom]` or `[twin]` and the reason)
  - `skippedOn: platform: string -> name: string -> bool`
- Produces: `ConformanceCases.modules: (string * Conformance.Case array) array`; the jsdom runner writing `generated-conformance/jsdom/`; the .NET runner (`ConformanceTests`) writing `generated-conformance/twin/`; `members.txt` compared by `MembersTests`.

- [ ] **Step 1: Write `Conformance.fs`**

```fsharp
/// <summary>The conformance case format: cases written once, run under jsdom and on the DOM twin.</summary>
module Ranvier.View.Testing.Conformance

open Fable.Core.TS.Dom

/// <summary>The host-specific operations a case uses: the document, the clock, and the artefact sink.</summary>
type Harness =
    abstract Platform: string
    abstract Document: Document
    /// <summary>The <c>post</c> of <c>QueuedCommits</c>: appends to the harness's queue.</summary>
    abstract Post: (unit -> unit) -> unit
    /// <summary>Drains the harness's queue, including callbacks queued while it drains.</summary>
    abstract RunMicrotasks: unit -> unit
    abstract AdvanceBy: float -> unit
    /// <summary>Records a named artefact (host HTML, dump text) for the cross-host comparison.</summary>
    abstract Record: name: string * text: string -> unit

type Case = string * (Harness -> unit)

#if FABLE_COMPILER
let mutable private current: Harness = Unchecked.defaultof<_>

let private read () = current
let private write (value: Harness) = current <- value
#else
[<Sealed; AbstractClass>]
type private Slot =
    [<System.ThreadStatic; DefaultValue>]
    static val mutable private current: Harness

    static member Current
        with get () = Slot.current
        and set (value: Harness) = Slot.current <- value

let private read () = Slot.Current
let private write (value: Harness) = Slot.Current <- value
#endif

let harness () : Harness =
    match read () with
    | null -> raise (System.InvalidOperationException "No conformance case is running on this thread")
    | value -> value

let document () : Document = (harness ()).Document

let private run (value: Harness) (check: unit -> unit) =
    let previous = read ()
    write value

    try
        check ()
    finally
        write previous

let ofChecks (checks: (string * (unit -> unit)) array) : Case array =
    checks |> Array.map (fun (name, check) -> name, (fun value -> run value check))

let platformOnly (platform: string) (reason: string) (name: string, check: unit -> unit) =
    $"[{platform}] {name} ({reason})", check

let skippedOn (platform: string) (name: string) =
    (name.StartsWith "[jsdom]" && platform <> "jsdom") || (name.StartsWith "[twin]" && platform <> "twin")
```

`read ()` reads the module mutable inside a function body, so Fable's single-use snapshot inlining (repository hazard) does not apply; keep it that way.

- [ ] **Step 2: Write the jsdom harness and runner**

`examples/Fable.Ranvier.Playground/tests/JsdomHarness.fs`:

```fsharp
module JsdomHarness

open Fable.Core.TS.Dom
open Ranvier.View.Testing.Conformance

/// <summary>A harness over jsdom: a post queue drained by <c>RunMicrotasks</c>, and mock timers ticked by <c>AdvanceBy</c>.</summary>
let create (document: Document) (record: System.Action<string, string>) (tick: System.Action<float>) : Harness =
    let queue = ResizeArray<unit -> unit>()

    { new Harness with
        member _.Platform = "jsdom"
        member _.Document = document
        member _.Post callback = queue.Add callback

        member _.RunMicrotasks() =
            let mutable i = 0

            while i < queue.Count do
                queue[i] ()
                i <- i + 1

            queue.Clear ()

        member _.AdvanceBy milliseconds = tick.Invoke milliseconds
        member _.Record(name, text) = record.Invoke (name, text)
    }
```

`examples/Fable.Ranvier.Playground/tests/ConformanceCases.fs`:

```fsharp
module ConformanceCases

open Ranvier.View.Testing

let modules: (string * Conformance.Case array) array =
    [|
        "RangeChecks", RangeChecks.cases
        "TemplateChecks", TemplateChecks.cases
        "DelegationChecks", DelegationChecks.cases
    |]
```

`examples/Fable.Ranvier.Playground/tests/conformance.test.mjs`:

```js
// The shared conformance cases under jsdom. Writes each case's artefacts to generated-conformance/jsdom/<module>/<case>/.
import test, { after, mock } from "node:test";
import { JSDOM } from "jsdom";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { create } from "../generated-tests/JsdomHarness.js";
import { modules } from "../generated-tests/ConformanceCases.js";
import { skippedOn } from "../generated-tests/experiments/Ranvier.View.Testing/Conformance.js";

const dom = new JSDOM("<!doctype html><html><head></head><body></body></html>");
const globals = ["window", "document", "Event", "MouseEvent", "PointerEvent", "KeyboardEvent", "InputEvent", "FocusEvent"];
globalThis.window = dom.window;
for (const name of globals.slice(1)) globalThis[name] = name === "document" ? dom.window.document : dom.window[name];
after(() => { dom.window.close(); for (const name of globals) delete globalThis[name]; });

const root = new URL("../generated-conformance/jsdom/", import.meta.url);
rmSync(root, { recursive: true, force: true });
export const slug = name => name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 80);

for (const [moduleName, cases] of modules) {
  for (const [name, run] of cases) {
    if (skippedOn("jsdom", name)) { test(`${moduleName}: ${name}`, { skip: "another host's case" }, () => {}); continue; }
    test(`${moduleName}: ${name}`, () => {
      const directory = new URL(`${moduleName}/${slug(name)}/`, root);
      let count = 0;
      const record = (artefact, text) => {
        mkdirSync(directory, { recursive: true });
        writeFileSync(new URL(`${String(++count).padStart(2, "0")}-${slug(artefact)}.txt`, directory), text);
      };
      mock.timers.enable({ apis: ["setTimeout", "setInterval", "Date"] });
      try { run(create(dom.window.document, record, ms => mock.timers.tick(ms))); }
      finally { mock.timers.reset(); }
    });
  }
}
```

Fable compiles `System.Action<string, string>` invocation to a two-argument call, so the JavaScript lambdas above are the delegates; check `generated-tests/JsdomHarness.js` after the first compile. If Fable emits the `Conformance.js` module under another path, take the path from `generated-tests/` and fix the import.

- [ ] **Step 3: Write the twin harness, runner and member record**

`tests/Ranvier.View.Twin.Tests/TwinHarness.fs`:

```fsharp
module Ranvier.View.Twin.Tests.TwinHarness

open System
open System.IO
open Ranvier.View.Twin
open Ranvier.View.Testing.Conformance

/// <summary>The repository root: <c>RANVIER_REPO</c> when set, else the directory four levels above this file.</summary>
let repository () =
    match Environment.GetEnvironmentVariable "RANVIER_REPO" with
    | null
    | "" -> Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "..", ".."))
    | root -> root

let slug (name: string) =
    let text = Text.RegularExpressions.Regex.Replace (name.ToLowerInvariant (), "[^a-z0-9]+", "-")
    let text = text.Trim '-'
    if text.Length > 80 then text.Substring (0, 80) else text

let create (host: TwinHost) (directory: string) : Harness =
    let mutable count = 0

    { new Harness with
        member _.Platform = "twin"
        member _.Document = host.Document.Native :?> Fable.Core.TS.Dom.Document
        member _.Post callback = host.Clock.Post callback
        member _.RunMicrotasks() = host.Clock.RunMicrotasks () |> ignore
        member _.AdvanceBy milliseconds = host.Clock.AdvanceBy milliseconds

        member _.Record(name, text) =
            count <- count + 1
            Directory.CreateDirectory directory |> ignore
            File.WriteAllText (Path.Combine (directory, $"%02d{count}-{slug name}.txt"), text)
    }
```

`__SOURCE_DIRECTORY__` is a compile-time constant, so the root resolves in a SageFs session too (SageFs#142); the twin itself reads no path.

`tests/Ranvier.View.Twin.Tests/ConformanceTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.ConformanceTests

open System.IO
open Expecto
open Ranvier.View.Twin
open Ranvier.View.Testing

let output () =
    Path.Combine (TwinHarness.repository (), "examples", "Fable.Ranvier.Playground", "generated-conformance", "twin")

let private runCase (moduleName: string) (name: string) (run: Conformance.Harness -> unit) () =
    let host = TwinHost.Create ()

    try
        run (TwinHarness.create host (Path.Combine (output (), moduleName, TwinHarness.slug name)))
    finally
        (host.Document :> System.IDisposable).Dispose ()

[<Tests>]
let tests =
    testList
        "Conformance"
        [
            for moduleName, cases in ConformanceCases.modules do
                testList
                    moduleName
                    [
                        for name, run in cases do
                            if Conformance.skippedOn "twin" name then
                                ptestCase name ignore
                            else
                                testCase name (runCase moduleName name run)
                    ]
        ]
```

`tests/Ranvier.View.Twin.Tests/MembersTests.fs` (compiled after `ConformanceTests.fs`):

```fsharp
module Ranvier.View.Twin.Tests.MembersTests

open System
open System.IO
open Expecto
open Ranvier.View.Twin

let private recorded () =
    Path.Combine (TwinHarness.repository (), "experiments", "Ranvier.View.Twin", "members.txt")

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Bridge coverage"
        [
            testCase "the served members equal members.txt"
            <| fun () ->
                let served = Bridge.served () |> List.filter (fun m -> not (m.StartsWith "Test"))
                let path = recorded ()

                if Environment.GetEnvironmentVariable "RANVIER_UPDATE_MEMBERS" = "1" then
                    File.WriteAllLines (path, served)
                else
                    let expected = File.ReadAllLines path |> List.ofArray
                    let added = served |> List.except expected
                    Expect.isEmpty added $"Members served but missing from members.txt (rerun with RANVIER_UPDATE_MEMBERS=1): {added}"
        ]
```

The record is the union of what every test served in the run, so this check is meaningful only for an unfiltered run; Expecto runs it last when it is `testSequenced` and in the last compiled file. Members recorded but no longer served do not fail the run; review removes them.

`ConformanceTests` deletes `generated-conformance/twin/` once before the first case: add `do if Directory.Exists (output ()) then Directory.Delete (output (), true)` as the first item of a `testSequenced` setup, or delete it in `Main.fs` before `runTestsInAssemblyWithCLIArgs` when the arguments hold no `--filter`.

- [ ] **Step 4: Migrate `RangeChecks`, `TemplateChecks` and `DelegationChecks`**

For each module, in this order, and with the jsdom run green after each:

1. Move every module-level `[<Global>]` value and every `jsNative` Emit helper under `#if FABLE_COMPILER` (repository hazard: the test executable initialises every file). Give each Emit helper a `#else` body over the DOM interfaces when one exists. Where none exists (`spyClickListeners` in `DelegationChecks.fs:25-26` patches the host's `addEventListener`, which a proxy cannot be patched to do), tag the cases that use it `Conformance.platformOnly "jsdom" "patches addEventListener on the host"` and leave the `#else` body raising `NotSupportedException`.
2. Replace `globalThis.document` with `Conformance.document ()` and `[<Global "document">]` uses with the same call.
3. Change the export to `let cases: Conformance.Case array = Conformance.ofChecks [| ... |]`, keeping every name.
4. Add the module to `ConformanceCases.modules`, delete its old `*.test.mjs`, and edit `test:dom` in `package.json`.
5. Run `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/conformance.test.mjs` (from `examples/Fable.Ranvier.Playground`): every case passes under jsdom.
6. Link the module into the twin test project (`<Compile Include="../../examples/Fable.Ranvier.Playground/tests/<Module>.fs" Link="Checks/<Module>.fs" />` before `ConformanceCases.fs`, which is also linked) and run `dotnet test tests/Ranvier.View.Twin.Tests -c Release --filter "FullyQualifiedName~Conformance"`.

On a twin failure, apply these rules in order and add a twin unit test for each fix:

- `NotSupportedException` naming a member: register the member in `Bridge.fs` (or `Events.fs`), with a `BridgeTests` case.
- A twin behaviour that differs from jsdom (serialisation, event order, form state): fix the twin to jsdom's behaviour, with a unit test in the matching test file.
- A `Native` .NET body that differs from its JavaScript: fix the body in `Native.fs` or the file's `#else`, and run G-BUNDLE to show the Fable side did not move.
- A difference rooted in .NET semantics outside the view (number formatting in a fixture, `string` of a float): tag the case `Conformance.platformOnly "jsdom" "<reason>"` and list it in the research note's "Platform-specific cases" section, which each phase's review reads.
- Anything else, including a change needed in Mounting, TemplateDom or the core: stop and report it.

`RangeChecks` opens `Fable.Ranvier`; cases that call `Fable.Ranvier.Dom` members (outside the view sources) take the `[jsdom]` tag with the reason `src/Fable.Ranvier is not a view source`.

- [ ] **Step 5: Record the served members**

Run: `RANVIER_UPDATE_MEMBERS=1 dotnet test tests/Ranvier.View.Twin.Tests -c Release` (PowerShell: `$env:RANVIER_UPDATE_MEMBERS = '1'; dotnet test ...; Remove-Item Env:RANVIER_UPDATE_MEMBERS`), then `dotnet test tests/Ranvier.View.Twin.Tests -c Release` without it.
Expected: `members.txt` holds one `Interface.member` per line, sorted; the second run passes, every test included.

- [ ] **Step 6: Run the gates**

G-DOM, G-BROWSER, G-SUGAR, G-NET, G-FORMAT. G-BUNDLE too: the migrated modules compile into `generated-tests`, and gate 3 lists them (`RangeChecks.js`, `TemplateChecks.js`, `DelegationChecks.js`, `JsdomHarness.js`, `ConformanceCases.js` and the Testing modules); run it with `--allow "(^|/)(Native|RangeChecks|TemplateChecks|DelegationChecks|JsdomHarness|ConformanceCases|EventSpec|Selector|Conformance)\.js$"` and confirm no Foundation or Portable module is listed. Gate 1's hashes must still match: the sugar-size entries do not import the check modules.

- [ ] **Step 7: Record the phase result**

Append to `docs/research/2026-10-21-dom-twin.md`: "Platform-specific cases" (each tagged case, its module and reason) and the twin's served-member count.

- [ ] **Step 8: Commit**

Paths: every file this task lists. Message: `test(view): shared conformance cases for range, template and delegation checks under jsdom and the twin`.

---

## Phase 3: testing library and artefact diff

### Task 14: Queries, interactions and expectations

> Amended by A4 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Testing/Query.fs`, `Interact.fs`, `Expect.fs` (after `Conformance.fs`)
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (link the three files)
- Create: `tests/Ranvier.View.Twin.Tests/TestingApiTests.fs`
- Create: `examples/Fable.Ranvier.Playground/tests/TestingApiChecks.fs` (the same checks as conformance cases), added to `ConformanceCases.modules`

**Interfaces:**
- Consumes: `Selector`, `EventSpec`, `Conformance`.
- Produces (both hosts):
  - `Query`: `query`, `queryAll`, `get`, `byText` (spec §3.9); `exception QueryException of selector: string * count: int * excerpt: string`
  - `Interact`: `click`, `input`, `change`, `check`, `select`, `key`, `dispatch` (spec §3.9); each records `root`'s `innerHTML` with the harness when a case is running (`Conformance.recordIfRunning`)
  - `Expect`: `text`, `count`, `attribute`, `value`, `html` (spec §3.9); `exception ExpectationException of message: string`
  - `Conformance.recordIfRunning: name: string -> text: string -> unit` (added to `Conformance.fs`; no-op outside a case)
- The dump excerpt in `QueryException` and `ExpectationException` is `""` until Task 22 fills it.

- [ ] **Step 1: Merge `master` and rebase citations**

Task 4 Step 1's commands.

- [ ] **Step 2: Write the failing checks**

`examples/Fable.Ranvier.Playground/tests/TestingApiChecks.fs` (both hosts; linked into the twin tests, so it carries no `jsNative` value):

```fsharp
module TestingApiChecks

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template
open Ranvier.View.Testing

type private FormState = { Name: Signal<string>; Agreed: Signal<bool>; Key: Signal<string> }

let private form =
    Component.define (fun _ () -> { Name = Api.createSignal ""; Agreed = Api.createSignal false; Key = Api.createSignal "" }) (fun t ->
        t.div
            [||]
            [|
                t.input [| t.Attrs.className "name"; t.Events.on ("input", fun s (e: Event) -> s.Name.Value <- (unbox<HTMLInputElement> e.target.Value).value) |] [||]
                t.input [| t.Attrs.className "agree"; t.Attrs.``type`` "checkbox"; t.Events.on ("change", fun s (e: Event) -> s.Agreed.Value <- (unbox<HTMLInputElement> e.target.Value).``checked``) |] [||]
                t.input [| t.Attrs.className "keys"; t.Events.on ("keydown", fun s (e: Event) -> s.Key.Value <- (unbox<KeyboardEvent> e).key) |] [||]
                t.p [||] [| t.text (fun s -> s.Name.Value + "|" + string s.Agreed.Value + "|" + s.Key.Value) |]
            |])

let private mounted () =
    let host = (Conformance.document ()).createElement "main"
    let graph = new Graph ()
    let mount = Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run (Component.render form ()) context.Backend)
    host :> Node, graph, mount

let private interactions () =
    let root, graph, mount = mounted ()
    Interact.input root "input.name" "Ada"
    Interact.check root "input.agree" true
    Interact.key root "input.keys" "Enter"
    Expect.text root "p" "Ada|True|Enter"
    Expect.count root "input" 3
    Expect.attribute root "input.agree" "type" (Some "checkbox")
    Expect.value root "input.name" "Ada"
    mount.Dispose ()
    graph.Dispose ()

let private failures () =
    let root, graph, mount = mounted ()

    let raised =
        try
            Query.get root "li" |> ignore
            false
        with QueryException (selector, count, _) -> selector = "li" && count = 0

    if not raised then failwith "Query.get did not raise for zero matches"

    let failed =
        try
            Expect.text root "p" "nope"
            false
        with ExpectationException message -> message.Contains "nope"

    if not failed then failwith "Expect.text did not raise"
    mount.Dispose ()
    graph.Dispose ()

let private byText () =
    let root, graph, mount = mounted ()
    Interact.input root "input.name" "Zed"
    let p = Query.byText root "Zed|False|"
    if p.localName <> "p" then failwith "byText found the wrong element"
    mount.Dispose ()
    graph.Dispose ()

let cases: Conformance.Case array =
    Conformance.ofChecks
        [|
            "interactions drive input, check and key and the expectations read the result", interactions
            "a query or expectation that fails raises with the selector", failures
            "byText returns the innermost element with the text", byText
        |]
```

`True` is .NET's and Fable's `string true`? Fable writes `"true"`; .NET writes `"True"`. Write the fixture's text as `(if s.Agreed.Value then "yes" else "no")` and the expectations `"Ada|yes|Enter"`, `"Zed|no|"`, so the case is the same on both hosts (spec risk 3).

`tests/Ranvier.View.Twin.Tests/TestingApiTests.fs` checks the selection-specific rules on the twin alone:

```fsharp
module Ranvier.View.Twin.Tests.TestingApiTests

open Expecto
open Ranvier.View.Twin
open Ranvier.View.Testing

[<Tests>]
let tests =
    testList
        "Testing API"
        [
            testCase "check clicks only when the state differs"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let root = host.Document.CreateElement "div"
                Html.parseInto root "<input type=\"checkbox\" checked>"
                let mutable clicks = 0
                Events.listen root "click" true (fun _ -> clicks <- clicks + 1) |> ignore
                Interact.check root.Native "input" true
                Expect.equal clicks 0 "already checked"
                Interact.check root.Native "input" false
                Expect.equal clicks 1 "one click"

            testCase "input fires beforeinput and skips the write when it is cancelled"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let root = host.Document.CreateElement "div"
                Html.parseInto root "<input>"
                Events.listen root "beforeinput" true (fun e -> (Events.eventOf e).PreventDefault ()) |> ignore
                Interact.input root.Native "input" "x"
                Expect.equal (root.FirstChild :?> TwinElement).Value "" "not written"

            testCase "select sets the value, then fires input and change"
            <| fun () ->
                let host = TwinHost.Create ()
                use _ = host.Document
                let root = host.Document.CreateElement "div"
                Html.parseInto root "<select><option value=\"a\">A</option><option value=\"b\">B</option></select>"
                let log = ResizeArray<string>()
                Events.listen root "input" true (fun _ -> log.Add "input") |> ignore
                Events.listen root "change" true (fun _ -> log.Add "change") |> ignore
                Interact.select root.Native "select" "b"
                Expect.equal ((root.FirstChild :?> TwinElement).Value, List.ofSeq log) ("b", [ "input"; "change" ]) "selected"
        ]
```

`beforeinput` must be cancelable (spec §3.9: "unless it is cancelled"): `Interact.input` creates it with `Cancelable = true`.

- [ ] **Step 3: Run the checks to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The value or constructor 'Interact' is not defined`.

- [ ] **Step 4: Write `Query.fs`, `Interact.fs` and `Expect.fs`**

```fsharp
module Ranvier.View.Testing.Query

open Fable.Core.TS.Dom

/// <summary>A query that requires one match found <c>count</c>; <c>excerpt</c> is a dump of the root.</summary>
exception QueryException of selector: string * count: int * excerpt: string with
    override this.Message = $"Expected one match for '{this.selector}', found {this.count}.{this.excerpt}"

/// <summary>The dump excerpt appended to query and expectation failures; Task 22 sets it.</summary>
let mutable internal excerptOf: Node -> string = fun _ -> ""

let query (root: Node) (selector: string) : Element option = Selector.query root selector
let queryAll (root: Node) (selector: string) : Element array = Selector.queryAll root selector

/// <summary>The single match; raises <c>QueryException</c> with the match count and a dump of <c>root</c> otherwise.</summary>
let get (root: Node) (selector: string) : Element =
    match queryAll root selector with
    | [| element |] -> element
    | found -> raise (QueryException (selector, found.Length, excerptOf root))

let private normalised (text: string) =
    System.String.Join (" ", text.Split ([| ' '; '\t'; '\n'; '\r'; '\f' |], System.StringSplitOptions.RemoveEmptyEntries))

/// <summary>The innermost element whose whitespace-normalised <c>textContent</c> equals <c>text</c>.</summary>
let byText (root: Node) (text: string) : Element =
    let candidates =
        queryAll root "*"
        |> Array.filter (fun e -> normalised (Option.defaultValue "" e.textContent) = text)

    let innermost =
        candidates
        |> Array.filter (fun e -> not (candidates |> Array.exists (fun other -> not (obj.ReferenceEquals (other, e)) && e.contains (Some (other :> Node)))))

    match innermost with
    | [| element |] -> element
    | found -> raise (QueryException ($"text \"{text}\"", found.Length, excerptOf root))
```

Under Fable, `excerptOf` is a module mutable read inside function bodies, which the inlining hazard does not touch. Check `Node.contains`'s parameter type with `fcs_signature_help` (Task 5's reflection shows `contains(FSharpOption)`).

```fsharp
module Ranvier.View.Testing.Interact

open Fable.Core.TS.Dom

let private record (root: Node) (step: string) =
    Conformance.recordIfRunning step (unbox<Element> root).innerHTML

let private fire (target: Element) (spec: EventSpec) : bool =
    target.dispatchEvent (EventSpec.create (Conformance.documentOf target) spec)

let private plain name bubbles cancelable = { Type = name; Kind = Plain; Bubbles = bubbles; Cancelable = cancelable; Init = [] }

/// <summary>Calls <c>click ()</c> on the single match: activation behaviour, then a bubbling <c>click</c>.</summary>
let click (root: Node) (selector: string) : unit =
    (unbox<HTMLElement> (Query.get root selector)).click ()
    record root ("click " + selector)

/// <summary>Fires <c>beforeinput</c>; unless it is cancelled, writes <c>value</c> and fires a bubbling <c>input</c>.</summary>
let input (root: Node) (selector: string) (value: string) : unit =
    let target = Query.get root selector

    if fire target { Type = "beforeinput"; Kind = Input; Bubbles = true; Cancelable = true; Init = [ "data", box value; "inputType", box "insertText" ] } then
        (unbox<HTMLInputElement> target).value <- value
        fire target { Type = "input"; Kind = Input; Bubbles = true; Cancelable = false; Init = [ "data", box value; "inputType", box "insertText" ] } |> ignore

    record root ("input " + selector)

/// <summary>Fires a bubbling <c>change</c>.</summary>
let change (root: Node) (selector: string) : unit =
    fire (Query.get root selector) (plain "change" true false) |> ignore
    record root ("change " + selector)

let check (root: Node) (selector: string) (isChecked: bool) : unit =
    let target = Query.get root selector

    if (unbox<HTMLInputElement> target).``checked`` <> isChecked then
        click root selector

let select (root: Node) (selector: string) (value: string) : unit =
    let target = Query.get root selector
    (unbox<HTMLSelectElement> target).value <- value
    fire target (plain "input" true false) |> ignore
    fire target (plain "change" true false) |> ignore
    record root ("select " + selector)

let key (root: Node) (selector: string) (key: string) : unit =
    let target = Query.get root selector

    for name in [ "keydown"; "keyup" ] do
        fire target { Type = name; Kind = Keyboard; Bubbles = true; Cancelable = true; Init = [ "key", box key ] } |> ignore

    record root ("key " + selector)

/// <summary>Dispatches <c>spec</c> on the single match; returns false when a listener cancelled it.</summary>
let dispatch (root: Node) (selector: string) (spec: EventSpec) : bool =
    let accepted = fire (Query.get root selector) spec
    record root (spec.Type + " " + selector)
    accepted
```

Add to `Conformance.fs`: `let recordIfRunning (name: string) (text: string) = match read () with null -> () | value -> value.Record (name, text)` and `let documentOf (node: Node) : Document = match node.ownerDocument with Some d -> d | None -> unbox node`.

```fsharp
module Ranvier.View.Testing.Expect

open Fable.Core.TS.Dom

exception ExpectationException of message: string with
    override this.Message = this.message

let private fail (root: Node) (detail: string) =
    raise (ExpectationException (detail + Query.excerptOf root))

let text (root: Node) (selector: string) (expected: string) : unit =
    let actual = Option.defaultValue "" (Query.get root selector).textContent
    if actual <> expected then fail root $"'{selector}' text: expected \"{expected}\", actual \"{actual}\""

let count (root: Node) (selector: string) (expected: int) : unit =
    let actual = (Query.queryAll root selector).Length
    if actual <> expected then fail root $"'{selector}' count: expected {expected}, actual {actual}"

let attribute (root: Node) (selector: string) (name: string) (expected: string option) : unit =
    let actual = (Query.get root selector).getAttribute name
    if actual <> expected then fail root $"'{selector}' [{name}]: expected %A{expected}, actual %A{actual}"

let value (root: Node) (selector: string) (expected: string) : unit =
    let actual = (unbox<HTMLInputElement> (Query.get root selector)).value
    if actual <> expected then fail root $"'{selector}' value: expected \"{expected}\", actual \"{actual}\""

/// <summary>The jsdom serialisation of <c>root</c>'s children equals <c>expected</c>.</summary>
let html (root: Node) (expected: string) : unit =
    let actual = (unbox<Element> root).innerHTML
    if actual <> expected then fail root $"html: expected {expected}, actual {actual}"
```

`%A` formats an option differently under Fable and .NET; write the attribute message with `Option.defaultValue "(absent)"` instead, so expectation texts match across hosts.

- [ ] **Step 5: Run the checks to verify they pass**

Run (jsdom): `cd examples/Fable.Ranvier.Playground && npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/conformance.test.mjs`
Run (twin): `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS on both; the twin run includes the three `TestingApiChecks` cases and the three `TestingApiTests`.

- [ ] **Step 6: Run the gates**

G-DOM, G-SUGAR, G-NET, G-FORMAT; G-BUNDLE with Task 13's `--allow` list plus `Query|Interact|Expect|TestingApiChecks`.

- [ ] **Step 7: Commit**

Message: `feat(testing): queries, interactions and expectations for both hosts`.

### Task 15: Artefact diff in CI, and the boundary and placement modules

**Files:**
- Create: `examples/Fable.Ranvier.Playground/tests/conformance-diff.mjs`, `examples/Fable.Ranvier.Playground/tests/conformance-diff.test.mjs`
- Modify: `.github/workflows/ci.yml` (`playground` job: two steps after `npm test`)
- Modify: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`, `PlacementChecks.fs`, `ConformanceCases.fs`, `package.json`; delete `boundary.test.mjs`, `placement.test.mjs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DelegationChecks.fs`, `TemplateChecks.fs` (artefact steps in the divergence cases)
- Modify: `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj` (link the two modules)

**Interfaces:**
- Consumes: `Conformance.recordIfRunning`, the two runners.
- Produces: `node tests/conformance-diff.mjs [jsdomDir] [twinDir]` (defaults `generated-conformance/jsdom`, `generated-conformance/twin`): exits 1 at the first differing or missing file, printing a unified diff; `export function compareTrees(left, right): { file: string, diff: string } | null`.

- [ ] **Step 1: Write the failing test**

`tests/conformance-diff.test.mjs`:

```js
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { compareTrees } from "./conformance-diff.mjs";

const tree = files => {
  const root = mkdtempSync(join(tmpdir(), "conf-"));
  for (const [path, text] of Object.entries(files)) { mkdirSync(join(root, path, ".."), { recursive: true }); writeFileSync(join(root, path), text); }
  return root;
};

test("equal trees compare equal", () => {
  const files = { "M/case/01-click.txt": "<b>1</b>" };
  assert.equal(compareTrees(tree(files), tree(files)), null);
});

test("a differing byte reports the file and a diff", () => {
  const result = compareTrees(tree({ "M/c/01-x.txt": "a\nb\n" }), tree({ "M/c/01-x.txt": "a\nc\n" }));
  assert.equal(result.file, "M/c/01-x.txt");
  assert.match(result.diff, /^-b$/m);
  assert.match(result.diff, /^\+c$/m);
});

test("a file present on one host only is a difference", () => {
  const result = compareTrees(tree({ "M/c/01-x.txt": "a" }), tree({}));
  assert.equal(result.file, "M/c/01-x.txt");
  assert.match(result.diff, /missing on the twin/);
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd examples/Fable.Ranvier.Playground && node --test tests/conformance-diff.test.mjs`
Expected: FAIL, `Cannot find module`.

- [ ] **Step 3: Write `conformance-diff.mjs`**

```js
// Compares the conformance artefacts of the jsdom and twin runs file by file (spec §6.3); exits 1 at the first
// difference with a unified diff. A file on one side only is a difference.
import { readdirSync, readFileSync, statSync, existsSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const files = root => {
  if (!existsSync(root)) return [];
  const result = [];
  const walk = d => { for (const n of readdirSync(d)) { const p = join(d, n); if (statSync(p).isDirectory()) walk(p); else result.push(relative(root, p).split(sep).join("/")); } };
  walk(root);
  return result;
};

const unified = (left, right) => {
  const a = left.split("\n"), b = right.split("\n"), out = [];
  let i = 0;
  while (i < a.length && i < b.length && a[i] === b[i]) i++;
  out.push(`@@ line ${i + 1} @@`);
  for (let k = Math.max(0, i - 2); k < i; k++) out.push(` ${a[k]}`);
  for (let k = i; k < Math.min(a.length, i + 5); k++) out.push(`-${a[k]}`);
  for (let k = i; k < Math.min(b.length, i + 5); k++) out.push(`+${b[k]}`);
  return out.join("\n");
};

export function compareTrees(left, right) {
  const names = [...new Set([...files(left), ...files(right)])].sort();
  for (const name of names) {
    const l = existsSync(join(left, name)) ? readFileSync(join(left, name), "utf8") : null;
    const r = existsSync(join(right, name)) ? readFileSync(join(right, name), "utf8") : null;
    if (l === null) return { file: name, diff: "missing under jsdom" };
    if (r === null) return { file: name, diff: "missing on the twin" };
    if (l !== r) return { file: name, diff: unified(l, r) };
  }
  return null;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const base = new URL("../", import.meta.url);
  const left = process.argv[2] ?? fileURLToPath(new URL("generated-conformance/jsdom", base));
  const right = process.argv[3] ?? fileURLToPath(new URL("generated-conformance/twin", base));
  const result = compareTrees(left, right);
  if (result) { console.error(`${result.file} differs\n${result.diff}`); process.exitCode = 1; }
  else console.log(`${files(left).length} artefacts equal`);
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `node --test tests/conformance-diff.test.mjs`
Expected: PASS, 3 tests.

- [ ] **Step 5: Record artefacts in the divergence cases**

In each divergence case named by spec §6.3 that exists after Tasks 6 and 13 (delegated dispatch with nested hosts, `stopPropagation` in a handler and in a native listener between target and host, and the keyed cases in `TemplateChecks` whose keys are structurally equal records), add `Conformance.recordIfRunning "<step>" host.innerHTML` after each interaction, with a step name that says what happened (`"after the second click"`). The keyed case with structurally equal records: if `TemplateChecks` has none, add `"rows keyed by structurally equal records keep one row per reference"` mounting a list keyed by `id` over `[| { Id = 1 }; { Id = 1 } |]` and recording `innerHTML` after the mount and after reversing the array.

- [ ] **Step 6: Migrate `BoundaryChecks` and `PlacementChecks`**

Apply the check-module migration procedure (Repository procedures) to each, in that order.

- [ ] **Step 7: Add the CI steps**

In `.github/workflows/ci.yml`'s `playground` job, after `Run the playground tests`:

```yaml
      # The conformance cases on the DOM twin, then the byte comparison with the jsdom run's artefacts (spec §6.3).
      - name: Run the conformance cases on the twin
        run: dotnet test tests/Ranvier.View.Twin.Tests -c Release
        working-directory: .
      - name: Compare the jsdom and twin artefacts
        run: node tests/conformance-diff.mjs
```

- [ ] **Step 8: Run the gates and the diff locally**

G-DOM, G-BROWSER, G-SUGAR, G-NET, G-FORMAT, G-BUNDLE (allow list extended with `BoundaryChecks|PlacementChecks`); then `cd examples/Fable.Ranvier.Playground && node tests/conformance-diff.mjs`.
Expected: `N artefacts equal`. A difference is a twin defect (fix the twin, with a unit test) or a platform difference (tag the case), never an edited expectation.

- [ ] **Step 9: Commit**

Message: `test(view): conformance artefact diff in CI; boundary and placement checks on both hosts`.

### Task 16: Sugar, template catalogue and unsafe HTML modules

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/tests/SugarChecks.fs`, `TemplateCatalogueChecks.fs`, `UnsafeHtmlChecks.fs`, `ConformanceCases.fs`, `package.json`; delete `sugar.test.mjs`, `template-catalogue.test.mjs`, `unsafe-html.test.mjs`
- Modify: `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj` (link the three modules and `PlaygroundSugar.fs` if `SugarChecks` uses it)

**Interfaces:**
- Consumes: Tasks 13-15.
- Produces: 3 more modules in `ConformanceCases.modules`.

- [ ] **Step 1: Write the failing run**

Add the three modules to `ConformanceCases.modules` and link them into the twin test project before changing them.
Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL, the test executable aborts at startup with `TypeInitializationException` naming the first linked module that holds a module-level `jsNative` value.

- [ ] **Step 2: Migrate the three modules**

Apply the check-module migration procedure to `SugarChecks`, `TemplateCatalogueChecks` and `UnsafeHtmlChecks`, in that order. `UnsafeHtml` content goes through the twin's parser (spec §3.5); a case whose markup lies outside the subset raises `TwinParseException` on the twin and takes the `[jsdom]` tag with the construct as its reason. `sugar.test.mjs` may hold assertions outside `cases`; move each into `SugarChecks.cases` as a case before deleting the file, or keep the file and remove only the `cases` loop if they read emitted JavaScript (those stay jsdom-side).

- [ ] **Step 3: Run the cases on both hosts**

Run: `cd examples/Fable.Ranvier.Playground && npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/conformance.test.mjs && cd ../.. && dotnet test tests/Ranvier.View.Twin.Tests -c Release && cd examples/Fable.Ranvier.Playground && node tests/conformance-diff.mjs`
Expected: PASS; artefacts equal.

- [ ] **Step 4: Run the gates**

G-DOM, G-SUGAR, G-NET, G-FORMAT, G-BUNDLE (allow list extended with the three module names).

- [ ] **Step 5: Commit**

Message: `test(view): sugar, template catalogue and unsafe HTML checks on both hosts`.

---

## Phase 4: queued commits, async, head, hydration and departures

### Task 17: Queued commits, `Settle` and the pending-region and owned-async modules

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/tests/PendingRegionChecks.fs`, `OwnedAsyncChecks.fs`, `ConformanceCases.fs`, `package.json`; delete `pending-regions.test.mjs`, `owned-async.test.mjs`
- Create: `tests/Ranvier.View.Twin.Tests/AsyncTests.fs`

**Interfaces:**
- Consumes: `Session.Act`, `TwinClock.Settle`, `Commits.Queued`, `TwinMount.GraphOptions` (a `ManualDispatcher`).
- Produces: `OwnedAsyncChecks.cases: Conformance.Case array`, with the promise-awaiting cases tagged `[jsdom]` (spec §6.3, host queues).

- [ ] **Step 1: Merge `master` and rebase citations**

Task 4 Step 1's commands. Re-read `Mounting.fs:4109-4165` (`mountWith`'s queued branch) and `Scheduling.fs:195-210` after the merge; Task 12's session code depends on them.

- [ ] **Step 2: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/AsyncTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.AsyncTests

open System.Threading.Tasks
open Expecto
open Ranvier
open Ranvier.View.Twin
open Ranvier.View.Foundation.Template

type private Loaded = { Result: AsyncMemo<string> }

let private loader (source: TaskCompletionSource<string>) =
    Component.define (fun _ () -> { Result = Api.createAsyncMemo (fun _ _ -> source.Task) }) (fun t ->
        t.p [||] [| t.text (fun s -> if s.Result.IsWaiting then "loading" else s.Result.Value) |])

[<Tests>]
let tests =
    testList
        "Async"
        [
            testCase "a result completed off-thread applies when the session settles"
            <| fun () ->
                let source = TaskCompletionSource<string> ()
                use session = Twin.mountWith { TwinMount.defaults with Commits = Commits.Queued Ranvier.View.Foundation.Scheduling.CollectAndContinue } (loader source) ()
                session.Act ignore
                Expect.equal (session.Text ()) "loading" "pending"
                Task.Run(fun () -> source.SetResult "done").Wait ()
                session.Act ignore
                Expect.equal (session.Text ()) "done" "settled"

            testCase "queued commits wait for the clock"
            <| fun () ->
                use session = Twin.mountWith { TwinMount.defaults with Commits = Commits.Queued Ranvier.View.Foundation.Scheduling.CollectAndContinue } Fixtures.counter 1
                Expect.isGreaterThan session.Clock.Pending.Microtasks -1 "queue readable"
                session.Clock.RunMicrotasks () |> ignore
                Expect.equal (session.Text ()) "1" "committed"
        ]
```

`Api.createAsyncMemo`'s exact signature, and whether `AsyncMemo` exposes `IsWaiting`, are in `src/Ranvier/Api.fs`; read them and adjust the fixture to the core's API (the spec's §Core primitives list `ManualDispatcher` and `Graph.Pump`; the docs' pattern is `docs/content/guide/testing.md:30-60`).

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release --filter "FullyQualifiedName~Async"`
Expected: FAIL at first on the off-thread result: `Settle` must pump the graph before the session reads the text. Fix whichever of `Session.Act` and `Settle` misorders the pump, the microtasks and the timers.

- [ ] **Step 4: Migrate `PendingRegionChecks` and `OwnedAsyncChecks`**

Apply the check-module migration procedure. `OwnedAsyncChecks.cases` today has type `(string * (unit -> JS.Promise<unit>)) array`: each case whose body awaits a promise becomes `Conformance.platformOnly "jsdom" "awaits JavaScript promise delivery"` wrapped so that the jsdom runner awaits its promise (give `conformance.test.mjs` an `await` on a returned promise: `const result = run(...); if (result && typeof result.then === "function") await result;`, and type those cases' runner function as returning `obj` under Fable). A case that only needs queued commits or `Graph.Pump` is rewritten synchronously over `Conformance.harness().RunMicrotasks ()` and runs on both hosts.

- [ ] **Step 5: Run the cases on both hosts and the diff**

Task 16 Step 3's command.

- [ ] **Step 6: Run the gates**

G-DOM, G-SUGAR, G-NET, G-FORMAT, G-BUNDLE (allow list extended).

- [ ] **Step 7: Commit**

Message: `test(view): queued commits and async settling on the twin; pending-region and owned-async checks on both hosts`.

### Task 18: Document head, hydration and server parity

> Amended by A8 (Amendments after the phase 0 review).

**Files:**
- Modify: `experiments/Ranvier.View.Twin/Session.fs` (`Twin.hydrate`, `Twin.hydrateHtml`)
- Modify: `examples/Fable.Ranvier.Playground/tests/DocumentChecks.fs`, `HydrationChecks.fs`, `ConformanceCases.fs`, `package.json`; delete `document.test.mjs`, `hydration.test.mjs`
- Create: `tests/Ranvier.View.Twin.Tests/ServerParityTests.fs`
- Modify: the test project (link `experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs` and `SugarFixtures.fs`, and the two modules)

**Interfaces:**
- Consumes: `Server.renderPage: Server.Options -> Component<'p> -> 'p -> Page` (`Page = { Body; Head }`), `Server.render`, `Hydration.hydrateWith`, `Hydration.HydrationOptions`, `Html.server`, `Html.parse`.
- Produces: `Twin.hydrate: Server.Options -> Hydration.HydrationOptions -> Component<'props> -> 'props -> Session`; `Twin.hydrateHtml: TwinMount -> Hydration.HydrationOptions -> body: string -> Component<'props> -> 'props -> Session`.

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/ServerParityTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.ServerParityTests

open Expecto
open Ranvier.View.Foundation
open Ranvier.View.Twin

let private fixtures: (string * (Server.Options -> string)) list =
    [
        "greeting", fun options -> Server.render options ServerFixtures.greeting ServerFixtures.greetingProps
        "staged", fun options -> Server.render options ServerFixtures.staged (ServerFixtures.Working 3)
        "listed", fun options -> Server.render options ServerFixtures.listed ServerFixtures.listedItems
    ]

[<Tests>]
let tests =
    testList
        "Server parity"
        [
            for name, render in fixtures do
                testCase $"{name}: Html.server of the parsed render is the render"
                <| fun () ->
                    let host = TwinHost.Create ()
                    use _ = host.Document
                    let html = render Server.Options.defaults
                    Expect.equal (Html.server (Html.parse host.Document html)) html "identity"

            testCase "strict hydration of the greeting keeps the server's nodes"
            <| fun () ->
                let page = Server.renderPage Server.Options.defaults ServerFixtures.clicking ServerFixtures.greetingProps
                use session = Twin.hydrateHtml TwinMount.defaults { Hydration.HydrationOptions.defaults with Strict = true } page.Body ServerFixtures.clicking ServerFixtures.greetingProps
                Expect.isEmpty session.Reports "no reports"
                Expect.stringContains (session.Html ()) "<button" "button kept"
        ]
```

Extend `fixtures` to every value of `ServerFixtures` and `SugarFixtures` that `ServerChecks.fs` and `SugarServerChecks.fs` render (spec §6.4: "every fixture"); read those two files for the props each fixture takes. The node-identity check of `docs/content/view/testing.md:103-105` goes in as: capture `Html.parse`'s button before `hydrateHtml` by parsing into the host yourself (`Twin.hydrateHtml` parses `body` into the host), then compare it with the button that `session.Host.querySelector "button"` returns after hydration.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `hydrateHtml is not defined`.

- [ ] **Step 3: Add `Twin.hydrate` and `Twin.hydrateHtml`**

```fsharp
    /// <summary>Hydrates the given server HTML instead of rendering it.</summary>
    let hydrateHtml (options: TwinMount) (hydration: Hydration.HydrationOptions) (body: string) (definition: Component<'props>) (props: 'props) : Session =
        match options.Commits with
        | Commits.Queued _ -> raise (NotSupportedException "Hydration supports synchronous commits only")
        | Commits.Synchronous -> ()

        let host, element, graph, session = start options

        try
            Html.parseInto element body
            session.Attach (Hydration.hydrateWith SynchronousCommits graph None session.Report hydration element.Native definition props)
            session.RaisePending ()
            session
        with _ ->
            (session :> IDisposable).Dispose ()
            reraise ()

    /// <summary>
    /// Renders with <c>Server.renderPage</c>, writes the body into the host and the head into the document, and hydrates
    /// with <c>Hydration.hydrateWith SynchronousCommits</c>, the one mode hydration supports.
    /// </summary>
    let hydrate (server: Server.Options) (hydration: Hydration.HydrationOptions) (definition: Component<'props>) (props: 'props) : Session =
        let page = Server.renderPage server definition props
        hydrateHtml { TwinMount.defaults with Head = page.Head } hydration page.Body definition props
```

`Server.Options` may carry `GraphOptions`; the session's graph uses `TwinMount.defaults.GraphOptions` (a `ManualDispatcher`) either way.

- [ ] **Step 4: Migrate `DocumentChecks` and `HydrationChecks`**

Apply the check-module migration procedure. `DocumentChecks` covers `keepHead`'s observer path under jsdom and its copy path on the twin (spec §3.2); the head-restore-after-failed-hydration divergence case (spec §6.3) records the head's `innerHTML` after the failure on both hosts. `HydrationChecks` is 3800 lines; migrate it whole, in one change, and run its cases on both hosts before committing.

- [ ] **Step 5: Run the cases on both hosts and the diff**

Task 16 Step 3's command.

- [ ] **Step 6: Run the gates**

G-DOM, G-BROWSER, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT, G-BUNDLE (allow list extended).

- [ ] **Step 7: Commit**

Message: `feat(twin): hydration over server HTML and server parity; document and hydration checks on both hosts`.

### Task 19: Departures on the twin

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/tests/DepartureChecks.fs`, `ConformanceCases.fs`, `package.json`; delete `departures.test.mjs`
- Create: `tests/Ranvier.View.Twin.Tests/DepartureTests.fs`

**Interfaces:**
- Consumes: `Departures.safetyTimeout` (10 000 ms), the motion hooks (`Hooks.motion`, `Hooks.exit`, `Hooks.reflow`, as `DepartureChecks.fs` writes them), `TwinClock.AdvanceBy`.
- Produces: `DepartureChecks.cases` on both hosts.

- [ ] **Step 1: Write the failing test**

`tests/Ranvier.View.Twin.Tests/DepartureTests.fs` mounts the first held-exit fixture of `DepartureChecks.fs` (copy its view and its exit hook, which never calls `finish`) through `Twin.mountView`, removes the row with `session.Act`, and asserts:

```fsharp
                Expect.stringContains (session.Html ()) "leaving-row" "held after removal"
                session.Clock.AdvanceBy 9999.0
                Expect.stringContains (session.Html ()) "leaving-row" "held before the safety timeout"
                session.Clock.AdvanceBy 1.0
                Expect.isFalse ((session.Html ()).Contains "leaving-row") "finished at 10 s"
```

with the fixture's row carrying `class="leaving-row"`.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release --filter "FullyQualifiedName~Departure"`
Expected: FAIL; the first run shows whether the hook registers on .NET (Task 6's seam) and whether the safety timer reaches the clock (`Native.setTimeout` → `TwinHost.SetTimeout`).

- [ ] **Step 3: Fix what the run shows and migrate `DepartureChecks`**

Fix the twin or a `Native` body as the check-module migration procedure's rules say, then migrate `DepartureChecks` with that procedure. The divergence cases of spec §6.3 (held exits across reorder, clear and disposal) record `innerHTML` after each step.

- [ ] **Step 4: Run the cases on both hosts and the diff**

Task 16 Step 3's command.

- [ ] **Step 5: Run the gates**

G-DOM, G-BROWSER, G-SUGAR, G-NET, G-FORMAT, G-BUNDLE (allow list extended).

- [ ] **Step 6: Commit**

Message: `test(view): departures and the safety timeout on the twin clock; departure checks on both hosts`.

---

## Phase 5: inspection and `Debug.dump`

### Task 20: The inspection registry under `RANVIER_VIEW_INSPECT`

> Amended by A5 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Foundation/Inspect.fs` (after `Placement.fs` in the Foundation project and in `DomTests.fsproj`'s links)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`ListRegion` `:3193`, `ArmRegion` `:3640`, `HoleRegion` `:3828`, `BoundaryRegion` `:3857`/`ViewBoundary` `:1667`, `MountState.Run` `:2675`), `TemplateDom.fs` (`bind` `:1030`)
- Modify: `experiments/Ranvier.View.Foundation/Scheduling.fs` (`Queue.Flushing`, under the define)
- Modify: `Directory.Build.targets` (`RanvierViewInspect`)
- Create: `tests/Ranvier.View.Twin.Tests/InspectTests.fs`

**Interfaces:**
- Consumes: `Native.newWeakMap`, `Native.get`, `Native.set`, `Native.delete`; the region variables named in spec §4.1.
- Produces (module `internal Ranvier.View.Foundation.Inspect`, under `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`): `Probe`, `BoundaryProbeState` (spec §4.2, with `MountProbe of phase: (unit -> ScopePhase) * queued: bool * flushing: (unit -> bool) * prefix: string`), `register: Node -> Probe -> (unit -> unit)`, `probeOf: Node -> Probe option`; `Scheduling.Queue.Flushing: bool` (internal, same define).

- [ ] **Step 1: Merge `master` and rebase citations**

Task 4 Step 1's commands, then re-locate each call site above by its member name (`member private this.ListRegion`, `ArmRegion`, `HoleRegion`, `BoundaryRegion`, `type ViewBoundary`, `member this.Run(before`, `let private bind`) and use the lines found.

- [ ] **Step 2: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/InspectTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.InspectTests

open Expecto
open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Twin
open Ranvier.View.Foundation.Representation
open Ranvier.View.Twin.Tests.Fixtures

let private probes (session: Session) =
    let rec walk (node: TwinNode) =
        [
            match Inspect.probeOf (unbox node.Native) with
            | Some probe -> probe
            | None -> ()

            for child in node.Children do
                yield! walk child
        ]

    walk (Bridge.nodeOf session.Host)

[<Tests>]
let tests =
    testList
        "Inspect"
        [
            testCase "a keyed list registers its rows' keys in order"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 3; 1; 2 |]
                        Program.run (Template.Component.render rows source.Value) context.Backend)

                let keys =
                    probes session
                    |> List.pick (fun p ->
                        match p with
                        | Inspect.ListProbe (_, rows) -> Some (rows () |> Array.map (fun (key, _) -> unbox<int> key))
                        | _ -> None)

                Expect.equal keys [| 3; 1; 2 |] "keys"

            testCase "an instance registers its definition and value"
            <| fun () ->
                use session = Twin.mount counter 4

                let found =
                    probes session
                    |> List.exists (fun p ->
                        match p with
                        | Inspect.InstanceProbe (_, instance, _, _) -> instance :? CounterState
                        | _ -> false)

                Expect.isTrue found "instance probe"

            testCase "a disposed mount leaves the registry"
            <| fun () ->
                let session = Twin.mount counter 4
                let root = Bridge.nodeOf session.Host
                let first = root.FirstChild
                (session :> System.IDisposable).Dispose ()
                Expect.isNone (Inspect.probeOf (unbox first.Native)) "removed"
        ]
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `The namespace or module 'Inspect' is not defined`.

- [ ] **Step 4: Write `Inspect.fs`**

```fsharp
/// <summary>The inspection registry: per region start marker and template instance root, closures over the state the mount keeps.</summary>
module internal Ranvier.View.Foundation.Inspect

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Foundation.Template

[<NoEquality; NoComparison>]
type Probe =
    /// <summary>A list region: the keys of its laid-out rows, in order, and each row's first node.</summary>
    | ListProbe of path: InstancePath * rows: (unit -> (obj * Node) array)
    /// <summary>An arm region: the selected key and definition, or null when empty.</summary>
    | ArmProbe of path: InstancePath * selected: (unit -> struct (obj * TemplateComponent))
    | BoundaryProbe of path: InstancePath * state: (unit -> BoundaryProbeState)
    | HoleProbe of path: InstancePath
    /// <summary>A template instance: its definition, instance value, owner and path; keyed by its first root node.</summary>
    | InstanceProbe of TemplateComponent * instance: obj * owner: Owner * path: InstancePath
    /// <summary>A mount: its phase, commit mode, whether its queue is flushing, and its identity prefix; keyed by its range's start marker.</summary>
    | MountProbe of phase: (unit -> ScopePhase) * queued: bool * flushing: (unit -> bool) * prefix: string

and [<NoEquality; NoComparison>] BoundaryProbeState =
    | Ready
    | Pending of waiting: int * revealed: bool
    | Failed of error: exn * generation: int

let private table = Native.newWeakMap ()

/// <summary>Registers <c>probe</c> for <c>node</c> until the returned cleanup runs.</summary>
let register (node: Node) (probe: Probe) : unit -> unit =
    Native.set table node probe

    fun () ->
        match Native.get<Node, Probe> table node with
        | null -> ()
        | current when obj.ReferenceEquals (current, probe) -> Native.delete table node
        | _ -> ()

let probeOf (node: Node) : Probe option =
    match Native.get<Node, Probe> table node with
    | null -> None
    | probe -> Some probe
#endif
```

A union value is never `null` under Fable or .NET, so `Native.get` returning `null` means absent on both hosts (Fable returns `undefined`, which `match ... with null` tests with `==`; check the emitted JavaScript).

- [ ] **Step 5: Add the call sites**

Each call site is one block under `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`, registering at the node the spec names and joining the owner that owns the region or instance (spec §4.2), shown here for the list region; the others follow the same three-line shape with their own probe:

```fsharp
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
        site.Owner.OnCleanup (
            Inspect.register regionStart (Inspect.ListProbe (parentPath.Row (region, -1), fun () -> previousKeys |> Option.ofObj |> Option.defaultValue [||] |> Array.mapi (fun i key -> key, (Native.arrayAt previousRows i).First)))
        )
#endif
```

- `ListRegion`: the closure reads `previousKeys` and `previousRows` (the last pass's keys and rows, `Mounting.fs:3203-3210`).
- `ArmRegion`: `ArmProbe (path, fun () -> struct (armKey, <the selected TemplateComponent, or null for an authored arm>))`, from `armKey` and the arm's definition (`:3650-3656`).
- `ViewBoundary`: `BoundaryProbe (path, fun () -> if failed then Failed (<the held failure>, generation) elif waiting > 0 || not revealed then Pending (waiting, revealed) else Ready)` (`:1700-1719`).
- `HoleRegion`: `HoleProbe path`.
- `TemplateDom.bind`: `InstanceProbe (<the compiled TemplateComponent>, state[StateInstance], <the instance owner>, <the instance path>)`, registered at `roots[0]` with the cleanup joined to the instance owner.
- `MountState.Run`: `MountProbe ((fun () -> this.Root.Phase), <queued?>, (fun () -> <queue>.Flushing), <identity prefix>)`, registered at the range's start marker.

Read the owner each region already uses for its cleanups (`site.Owner`, `RegionSite.Own`, or the instance owner in `bind`) and call `OnCleanup` on it; if the owner type exposes another cleanup method, use that. Add `member internal _.Flushing = flushing` to `Scheduling.Queue`, under the same define.

- [ ] **Step 6: Add the MSBuild property**

In `Directory.Build.targets`, after the `RanvierTrace` group:

```xml
    <!--
    `RanvierViewInspect=true` compiles the view's inspection registry and `Debug` into Fable builds
    (`RANVIER_VIEW_INSPECT`). Debug defaults to on; any other configuration defaults to off. .NET builds of
    Foundation always hold the registry.
    -->
    <PropertyGroup>
        <RanvierViewInspect Condition="'$(RanvierViewInspect)' == '' and '$(Configuration)' == 'Debug'">true</RanvierViewInspect>
    </PropertyGroup>

    <PropertyGroup Condition="'$(RanvierViewInspect)' == 'true'">
        <DefineConstants>$(DefineConstants);RANVIER_VIEW_INSPECT</DefineConstants>
    </PropertyGroup>
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, the three `Inspect` tests included.

- [ ] **Step 8: Run the gates, including gates 1 and 2**

G-BUNDLE (gate 1 with the define off: hashes equal), then `cd examples/Fable.Ranvier.Playground && node benchmarks/frameworks/bundle-hashes.mjs --absence` (gate 2: exit 0), G-SIZE, G-DOM (its `test:sugar-debug` compiles DomTests in Debug, where the define is now on), G-BROWSER, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT.

- [ ] **Step 9: Commit**

Message: `feat(view): inspection registry for regions and instances under RANVIER_VIEW_INSPECT`.

### Task 21: `Debug.capture`, the text and JSON forms, and `Session.Dump`

> Amended by A6, A7 (Amendments after the phase 0 review).

**Files:**
- Create: `experiments/Ranvier.View.Foundation/Debug.fs` (after `Hydration.fs`; and in `DomTests.fsproj`'s links)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`Mounted.Range`, internal: first and last node of the mount's range)
- Modify: `experiments/Ranvier.View.Twin/Session.fs` (`Session.Dump`)
- Create: `tests/Ranvier.View.Twin.Tests/DumpTests.fs`

**Interfaces:**
- Consumes: `Inspect.probeOf`, `Mounted.Range`, `Template.structuralHash` (`Template.fs:1409`), `Template.numberText` (`Template.fs:52-110`), `INode.Status`, the `Peek` members, `Graph.IsOnGraphThread`, `Api.untrack`.
- Produces (module `Ranvier.View.Foundation.Debug`, under `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`): `DumpOptions` and `DumpOptions.defaults` (`MaxRows = 12`, `MaxText = 80`, `MaxItems = 5`, `MaxDepth = 64`, `MaxChars = 65536`, `Values = true`, `Markers = false`, `Head = true`, `NodeIds = false`); `DumpNode`, `InstanceDump`, `FieldDump`, `BoundaryDump`, `Elided`, `Dump` (spec §4.3); `capture`, `captureNode`, `text`, `json`, `dump`, `nodeAt: Mounting.Mounted * string -> Ranvier.INode option`. `Session.Dump: ?options: DumpOptions -> string`.

- [ ] **Step 1: Write the failing tests**

`tests/Ranvier.View.Twin.Tests/DumpTests.fs`:

```fsharp
module Ranvier.View.Twin.Tests.DumpTests

open System
open Expecto
open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template
open Ranvier.View.Twin
open Ranvier.View.Foundation.Representation
open Ranvier.View.Twin.Tests.Fixtures

type Node' = { Name: string; mutable Next: Node' option }
type DeepState = { Cyclic: Node'; Long: string; Deep: obj }

let private deep =
    Component.define (fun _ () ->
        let cyclic = { Name = "a"; Next = None }
        cyclic.Next <- Some cyclic
        let rec nest n : obj = if n = 0 then box "bottom" else box [ nest (n - 1) ]
        { Cyclic = cyclic; Long = String ('x', 10_000); Deep = nest 200 }) (fun t -> t.p [||] [||])

[<Tests>]
let tests =
    testList
        "Dump"
        [
            testCase "a counter dumps its instance, field and bound text"
            <| fun () ->
                use session = Twin.mount counter 4
                let text = session.Dump ()
                Expect.stringContains text "instance CounterState" "instance"
                Expect.stringContains text "Count  signal  4" "field"
                Expect.stringContains text "=\"4\"" "bound text"

            testCase "dump bounds cyclic, long and deep values"
            <| fun () ->
                use session = Twin.mount deep ()
                let text = session.Dump ()
                Expect.stringContains text "(10000)" "long string cut with its length"
                Expect.isLessThan text.Length 65536 "within MaxChars"
                Expect.stringContains text "…" "elided"

            testCase "a 1000-row list dumps 12 rows and one elision line"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal (Array.init 1000 id)
                        Program.run (Component.render rows source.Value) context.Backend)

                let lines = (session.Dump ()).Split '\n'
                Expect.equal (lines |> Array.filter (fun l -> l.TrimStart().StartsWith "row ")).Length 12 "rows"
                Expect.equal (lines |> Array.filter (fun l -> l.Contains "… 988 rows (keys 9 … 996)")).Length 1 "elision"

            testCase "MaxChars ends the text with the omitted line count"
            <| fun () ->
                use session = Twin.mount counter 1
                let text = session.Dump { Debug.DumpOptions.defaults with MaxChars = 40 }
                Expect.stringContains text "lines omitted" "truncated"

            testCase "a dump changes no observer count and leaves a stale memo stale"
            <| fun () ->
                let mutable runs = 0
                let source = ref Unchecked.defaultof<Signal<int>>
                let memo = ref Unchecked.defaultof<Memo<int>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal 1
                        memo.Value <- Api.createMemo (fun () -> runs <- runs + 1; source.Value.Value * 2)
                        Program.run (Component.render counter 0) context.Backend)

                source.Value.Value <- 5
                let before = runs
                session.Dump () |> ignore
                Expect.equal runs before "the memo did not run"

            testCase "the JSON form names its schema and parses"
            <| fun () ->
                use session = Twin.mount counter 2
                let json = Debug.json (Debug.capture Debug.DumpOptions.defaults session.Mounted)
                let document = Text.Json.JsonDocument.Parse json
                Expect.equal (document.RootElement.GetProperty("schema").GetString ()) "ranvier-view-dump/1" "schema"

            testCase "a dump off the graph's thread raises"
            <| fun () ->
                use session = Twin.mount counter 2
                let mutable failure: exn = null
                let thread = Threading.Thread (fun () -> try session.Dump () |> ignore with e -> failure <- e)
                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure :? InvalidOperationException) "raised"
        ]
```

Under `RANVIER_COUNTERS`, add the spec's counter check (`Counters.Snapshot ()` equal before and after `capture`) inside `#if RANVIER_COUNTERS`, and under `RANVIER_TRACE` the trace-log check (no new event), read through the `Trace` queries; run the test project once with `-p:RanvierTrace=true` for the latter.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: FAIL to compile, `Dump is not a member of Session`.

- [ ] **Step 3: Write `Debug.fs`**

The module has four parts; write each with its own tests passing before the next.

1. **Types** exactly as spec §4.3, with `DumpOptions.defaults` as listed in Interfaces.
2. **Capture.** `capture options mounted` checks `graph.IsOnGraphThread` (raise `InvalidOperationException "Debug.dump ran off the graph's thread"`) and the mount probe's `flushing ()` (raise `"Debug.dump ran inside a flush"`), then walks the mount's range from `Mounted.Range`'s first node to its last under `Api.untrack`:
   - an element becomes `Element (tag, attributes, properties, children)`, with `properties` the form state (`value`, `checked`, `selected`) when it differs from the attributes;
   - a text node becomes `Text (data, bound)`, `bound` when the node is a slot target of the enclosing instance;
   - a node with a probe opens the matching `DumpNode` (`List`, `Arm`, `Boundary`, `Hole`, `Instance`), its children being the nodes up to the region's end marker; a node `Departures.seam.Within` reports is wrapped in `Leaving`;
   - `List` rows beyond `MaxRows` become one `Elided { Count; FirstKey; LastKey }`, keeping the first `MaxRows - 3` and the last 3;
   - an instance's fields: on .NET, `FSharpType.IsRecord`/`GetRecordFields` (anonymous records included) or tuple items of the instance value; under Fable, `Object.keys` order through `JsInterop`; a class instance shows the public properties whose values are core nodes;
   - a field value that is a core node (`:? INode`) shows `pending` when `Status` has `Pending`, `error: <message>` when it has `Error` (read the error through the node's `Peek`-side member that holds it), `uninitialised` when it has `Uninitialized`, else its `Peek` value read by reflection (`GetProperty "Peek"`) on .NET and `node.Peek` under Fable; the field `Kind` is `signal`, `memo`, `asyncmemo` or the type name;
   - values: strings quoted and cut at `MaxText` with `… (<length>)`; numbers through `Template.numberText`; options, lists and arrays show `MaxItems` items then `…`; records show field names; other objects their type name; a value already on the current path prints `<cycle>`; depth beyond `MaxDepth` prints `…`.
3. **Text** (spec §4.4): two-space indentation per level, the line forms of the spec's example, the head section when `Head` is set (the `data-ranvier-head` elements of the document head), then `MaxChars` truncation: cut at the last whole line within the limit and append `… <n> lines omitted`.
4. **JSON**: `{"schema":"ranvier-view-dump/1","mount":…,"root":[…],"head":[…]}`, keys in declaration order, written by hand (no serializer dependency under Fable), strings escaped per RFC 8259.

`nodeAt (mounted, "r-0.columns")` splits the path at the last `.`, finds the instance whose path text is the prefix, and returns the field's value when it is an `INode`.

`Session.Dump` (in `Session.fs`): `member _.Dump(?options: Debug.DumpOptions) = Debug.text (Debug.capture (defaultArg options Debug.DumpOptions.defaults) mounted)`.

Every line format and value rule is the same code on both hosts; the only `#if FABLE_COMPILER` branches are the field enumeration and the `Peek` read. The `string` of a float must never appear: numbers go through `numberText`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release`
Expected: PASS, the seven `Dump` tests included.

- [ ] **Step 5: Run the gates**

G-BUNDLE (gate 1: hashes equal with the define off), `node benchmarks/frameworks/bundle-hashes.mjs --absence`, G-SIZE, G-DOM, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT.

- [ ] **Step 6: Commit**

Message: `feat(view): Debug.dump text and JSON of a mounted view, read through Peek and Status`.

### Task 22: Golden dumps on both hosts, failure excerpts and the inspect cost

> Amended by A6 (Amendments after the phase 0 review).

**Files:**
- Create: `examples/Fable.Ranvier.Playground/tests/DumpChecks.fs` (the golden fixtures as conformance cases), added to `ConformanceCases.modules` under `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`
- Create: `tests/Ranvier.View.Twin.Tests/dumps/*.txt` (generated, then reviewed)
- Create: `tests/Ranvier.View.Twin.Tests/GoldenTests.fs`
- Modify: `experiments/Ranvier.View.Testing/Query.fs` (`excerptOf` set from `Debug` under the define)
- Modify: `examples/Fable.Ranvier.Playground/package.json` (`test:conformance-inspect`: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS --define RANVIER_VIEW_INSPECT && node --test tests/conformance.test.mjs`; appended to `test`)
- Create: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/sugar-size-inspect.mjs`; modify `bundle-hashes.mjs` (`--inspect-cost`)
- Modify: `docs/research/2026-10-21-dom-twin.md`

**Interfaces:**
- Consumes: `Debug.capture`, `Debug.text`, `Debug.json`, `Debug.captureNode`.
- Produces: golden dump texts for the Board, keyed list, arm, boundary (ready, pending, failed), hole, departure and head fixtures (spec §6.5); excerpts of 40 lines on query and expectation failures; `node benchmarks/frameworks/bundle-hashes.mjs --inspect-cost` printing the `inspect` entry's raw, gzip and Brotli delta over `combined`.

- [ ] **Step 1: Write the fixtures and the failing golden test**

`DumpChecks.fs` defines each fixture as a component in the Template API and one case per fixture that mounts it, applies the fixture's actions, and records `Debug.text (Debug.captureNode options graph host)` with `Conformance.recordIfRunning "dump" ...` and the host's `innerHTML`. The Board fixture follows spec §4.4's example: a `Board` instance with `columns` (a memo over three column records), `adding` (a signal) and `query` (an async memo left pending), a heading with a bound card count, a keyed list of `Card` rows of which one is leaving, an arm keyed `"edit"` and a boundary pending on one read.

`GoldenTests.fs` compares each twin artefact with `tests/Ranvier.View.Twin.Tests/dumps/<case>.txt`, writing the file when `RANVIER_UPDATE_DUMPS=1`.

- [ ] **Step 2: Run to verify the goldens fail, then record them**

Run: `dotnet test tests/Ranvier.View.Twin.Tests -c Release --filter "FullyQualifiedName~Golden"`
Expected: FAIL, `dumps/board.txt` missing. Then run once with `RANVIER_UPDATE_DUMPS=1`, read every generated file against spec §4.4's rules, and fix `Debug.fs` until each reads as the spec's example does; commit the files only once read.

- [ ] **Step 3: Wire the excerpts**

In `Query.fs`, under `#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER`, set `excerptOf` to the first 40 lines of `Debug.text (Debug.captureNode Debug.DumpOptions.defaults <graph> root)` for the nearest enclosing instance, prefixed by a newline; the graph comes from the mount probe that `Inspect` registered above `root`. Add a `TestingApiChecks` case asserting that the `ExpectationException` message holds `instance` on the twin and, under the inspect build, under jsdom.

- [ ] **Step 4: Run both hosts, with and without the define**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-dom-twin/examples/Fable.Ranvier.Playground
npm run compile:tests -- --define RANVIER_VIEW_COUNTERS --define RANVIER_VIEW_INSPECT
node --test tests/conformance.test.mjs
cd ../..
dotnet test tests/Ranvier.View.Twin.Tests -c Release
cd examples/Fable.Ranvier.Playground
node tests/conformance-diff.mjs
npm run test:dom
```

Expected: every step passes; the diff reports the dump artefacts equal (spec success criterion 6); `test:dom` (define off) passes with the `DumpChecks` module compiled out.

- [ ] **Step 5: Measure the inspect cost (gate 4)**

`sugar-size-inspect.mjs` re-exports the `combined` entry's functions and calls `Debug.dump` once on a mounted fixture; `bundle-hashes.mjs --inspect-cost` builds it from a `generated-inspect/` compile (`dotnet fable tests/DomTests.fsproj --outDir generated-inspect --configuration Release --define RANVIER_VIEW_INSPECT`, with `sugar-size.config.mjs` reading `SUGAR_SIZE_SOURCE` for the directory) and prints the delta over `combined`, minified, raw, gzip and Brotli. Record the numbers in the research note under "Inspect cost"; it is not a budget.

- [ ] **Step 6: Run the gates**

G-BUNDLE (gate 1 equal), `--absence` (gate 2), G-SIZE, G-DOM, G-BROWSER, G-VIEW-NET, G-SUGAR, G-NET, G-FORMAT.

- [ ] **Step 7: Commit**

Message: `test(view): golden dumps equal on both hosts; dump excerpts on test failures; inspect build cost`.

---

## Phase 6: agent workflow and docs

### Task 23: Twin loop timings, SageFs instructions and the docs pages

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/loop-time.mjs` (`--loop twin-test`)
- Create: `docs/research/2026-10-21-dom-twin/loop-twin.json` (generated)
- Modify: `docs/research/2026-10-21-dom-twin.md` (§7.2 results)
- Modify: `docs/content/view/testing.md` ("Twin checks on .NET", "Dumping a view"), `docs/content/guide/tracing.md` (a link to the dump), `AGENTS.md` (the twin session under "F# experiments")
- Modify: `docs/samples/view/` (one sample per new page section, if the page's other sections have samples)

**Interfaces:**
- Consumes: everything above.
- Produces: the loop-time comparison of spec §7.2 and the user docs.

- [ ] **Step 1: Merge `master` and rebase citations**

Task 4 Step 1's commands.

- [ ] **Step 2: Add the twin loop to `loop-time.mjs`**

`--loop twin-test` edits `tests/Ranvier.View.Twin.Tests/Fixtures.fs` the same way the jsdom loop edits its fixture, and times `dotnet test tests/Ranvier.View.Twin.Tests -c Release --filter "FullyQualifiedName~Session"` (cold: after `dotnet build-server shutdown`; warm: the later runs), writing the same JSON shape. Check: `node benchmarks/frameworks/loop-time.mjs --loop twin-test --runs 1 --out C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/loop-twin-smoke.json` exits 0 and restores the fixture.

- [ ] **Step 3: Measure**

Behind the bench lock: `--loop twin-test --runs 10 --out ../../docs/research/2026-10-21-dom-twin/loop-twin.json`. Then the SageFs loops, by hand in a SageFs session on `tests/Ranvier.View.Twin.Tests/Ranvier.View.Twin.Tests.fsproj` (create it, wait for Ready): 10 times, edit `Fixtures.fs`, `hard_reset_fsi_session rebuild=true`, evaluate `Twin.mount Fixtures.counter 1 |> fun s -> s.Text ()`, recording each tool call's elapsed time; and 10 warm evaluations of one interaction and one dump. `stop_session` the session afterwards. Record per-case times for a 1000-row keyed mount, a full reorder and the Board hydration on the twin and under jsdom, and the bridge's share of the 1000-row case (decision 1's threshold: a quarter).

- [ ] **Step 4: Write the docs**

`docs/content/view/testing.md` gains "Twin checks on .NET" (mount, interact, expect; the clock; queued commits and `Settle`; the platform differences of spec §3.11 and risk 3) and "Dumping a view" (`Session.Dump`, `Debug.dump` in an inspect client build, the text form's lines, `NodeIds` and `Debug.nodeAt` with `Trace.why`). `docs/content/guide/tracing.md` links "Dumping a view" from its list of questions. `AGENTS.md` adds, under the SageFs section, the twin session (`tests/Ranvier.View.Twin.Tests` project, the spec §3.10 loop). Write each page by `.claude/rules/comments.md`'s prose rules; run `dotnet fsi docs/samples/view/Generate.fsx --check` if the page's samples are generated.

- [ ] **Step 5: Run the gates**

G-VIEW-NET, G-NET, G-FORMAT; `dotnet fsi build.fsx -- docs` builds the site without errors.

- [ ] **Step 6: Commit**

Message: `docs(view): twin checks, view dumps and the measured loop times`.

---

## Phase 7: follow-ons

Each needs its own decision and plan: generated bridge classes (decision 1, only if Task 23's bridge share exceeds a quarter of a case's time), a fake anime engine (decision 9), and `SVGElement` bridging.
