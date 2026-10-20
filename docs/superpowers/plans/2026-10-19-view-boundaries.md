# View boundaries and live setter failures implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add structural view boundaries (a pending view until every binding under the content has a value, an error view after any failure under the content, a retry) and report live DOM setter failures, in Tree, Program and template authoring, under synchronous and queued commits.

**Architecture:** A new internal `ViewScope` interface receives a binding's pending reads and failures. A `MountState` is the scope of its bindings when no boundary encloses it; a `Boundary` region object in `Mounting.fs` is the scope under a boundary, and every nested `MountState` keeps the scope it was created with. Bindings register pending reads with their scope, setter failures are caught and sent to the scope, the commit queue reports each flush's failures per scope, and handlers under a boundary send their exceptions to it. The boundary mounts its content, pending and error views as nested mounts under the region's core root, and runs its reveal and teardown from one core effect, so neither runs inside the binding or region pass that requested it.

**Tech Stack:** F# compiled with Fable 5.18 to JavaScript; Ranvier core (`src/Ranvier`, read-only here); jsdom node tests (`npm run test:dom`); Playwright Chromium (`npm run test:browser`); the framework benchmark harness under `examples/Fable.Ranvier.Playground/benchmarks/frameworks`.

**Spec:** [`docs/superpowers/specs/2026-10-19-view-boundaries-design.md`](../specs/2026-10-19-view-boundaries-design.md), approved 2026-10-19 with Decisions 1a, 2a, 3 (keep throwing) and 4a. Read it before any task.

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

- `Api.createRoot`, `Api.runWithOwner`, `Owner` and `MountLifetime` (`Lifetime.fs`, over core owners): the boundary region owns one root (`RegionSite.Own`); its content, pending view and error view are each a nested mount whose root is a child of that root. Disposing the region disposes all three.
- `Api.onCleanup`: a binding under a boundary unregisters its pending read when its owner is disposed.
- `Api.createEffect` with an `Api.createSignal` (`wake`): the boundary's reveal and teardown run in this effect, after the effect that requested them.
- `Api.createSignal false`: `BoundaryStatus.IsPending`, a tracked reader.
- `NotReadyException` (core async sources and async memos): what a pending read is.
- `MountLifetime.Capture`: the teardown window that collects the content's cleanup errors for the failure report.

**View-owned state:**

- The waiting count of a boundary, and a `Waiting` bit in each binding's state word. Core suspense (`createSuspense`, `createBoundary`) catches pending reads inside its own body only; a binding reads in its own effect, outside every core boundary body (spec, "What happens today"). The approved spec places this count in the view.
- The content's detached fragment and the row shown between the markers: DOM nodes and their layout.
- The failures a boundary received before its effect runs, and the failures held while its content sets up.
- The scope of each commit slot. The commit `Queue` is view state already under review (rule file, last paragraph); this plan adds one field to its slots.
- A `$$ranvierScope` property on template event nodes under a boundary: event delegation state.

## Global Constraints

- View-side only: no file under `src/` changes. Core changes, if a task finds one necessary, stop the task and are raised with the user (`.claude/rules/view-uses-core.md`).
- Work in `C:\Users\shaya\RiderProjects\Ranvier-fable-ranvier-dom` on branch `poc/fable-ranvier-dom`.
- Gates, run at the end of every task, all exit 0. The script `$CLAUDE_JOB_DIR/tmp/pocgates.sh` (today `C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/pocgates.sh`) runs them; its equivalent, from the worktree root:
  ```bash
  dotnet fsi build.fsx -- test
  npm ci
  dotnet fsi build.fsx -- test-fable
  cd examples/Fable.Ranvier.Playground
  npm ci
  npm run test:dom
  npm run test:browser
  dotnet fable benchmarks/ripple/RipplePerf.fsproj --outDir generated-ripple --configuration Release --noCache
  ```
  Tasks that change `Representation.fs` or `Template.fs` (3 and 8) also run `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`.
- Inner loop (not a gate): from `examples/Fable.Ranvier.Playground`, `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs` (or the `.test.mjs` file that holds the task's tests).
- Benchmarks (`keyed-rows.mjs`, `run.mjs --template-comparison`, `boundary-reveal.mjs`) run one at a time behind the lock: `mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` before, `rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` after. If `mkdir` fails, another benchmark holds the lock: wait and retry. Never remove a lock you did not create.
- Commits: one conventional line, a blank line, then exactly these two lines:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
  ```
  Before each commit: `dotnet fantomas <each changed .fs file>` (tool pinned at 8.0.5), `git add` of named paths only, `git diff --cached --check` empty. Push with `git push origin HEAD:poc/fable-ranvier-dom`. Never rebase, amend a pushed commit or force-push.
- Leave the untracked `examples/Fable.Ranvier.Playground/generated-keyed-*` directories and `examples/Fable.Ranvier.Playground/benchmarks/frameworks/_lines.tmp.mjs` alone: never add, delete or clean them. New build output directories (`generated-keyed-bnd0`, `generated-keyed-bnd1`, `generated-boundary-dist`) stay untracked too.
- Comments follow `.claude/rules/comments.md`. No `FOR-REVIEW` comment is committed.
- Every existing test passes unchanged, except the four tests Task 1 adds, which Task 2 updates to the new contract. Any other test that has to change is a plan conflict: stop and report it.
- Every new test runs in jsdom (`npm run test:dom`) and in the Chromium range harness (`tests/range.html`, run by `npm run test:browser`). Task 3 registers `BoundaryChecks.fs` in both.

## Rulings on the spec

Each ruling picks an implementation where the spec leaves one open.

1. **The mount's scope is the `MountState` itself, and deduplication stays per `MountState`.** An authored keyed row is its own `MountState`, so a failed memo read by two rows is reported once per row, as today (`"a keyed row reading a failed memo reports the failure"`). *If wrong:* moving deduplication to the outermost mount changes that test and one field.
2. **Setter failures share the reader deduplication (`lastReported`).** It is cleared only when a failed reader of the same mount recovers. *If wrong:* a setter that raises one cached exception object, succeeds, and raises it again is reported once instead of twice; adding a `WriteFailed` bit to the state word fixes it.
3. **A boundary reports through the mount's `report` callback**, not through the enclosing boundary, in one call holding the failures followed by the content's teardown errors. Reporting through the enclosing boundary would make every inner failure tear down the outer content. *If wrong:* one call site in `Boundary.TearDown`.
4. **`BoundaryFailure.Error` is the first exception of the failing call.** The others are reported. *If wrong:* the error view sees one exception where an aggregate was expected.
5. **The pending view is set up only when the content still waits after its setup.** A content that reads every value at setup is revealed at once and the pending view never runs. *If wrong:* a pending view whose setup has side effects sees them skipped.
6. **Failures received during the content's setup are held, and the error view replaces the content once setup returns.** Tearing the content down from inside its own setup would dispose a half-built mount. *If wrong:* none observable; the error view appears in the same call.
7. **Reveal and teardown run in one core effect of the boundary (`wake`), after the binding, handler or region pass that requested them.** Running them synchronously inside a list pass would move the pass's nodes out of the parent the pass captured, and the pass would then fail its own validation (Review Focus 4). Under queued commits a pending binding's first value is written immediately (`Binding.startQueued`), so the reveal effect sees every binding committed. *If wrong:* a caller that expects the reveal before the binding's own write returns sees it after; no test or caller depends on that.
8. **A pending or error view that fails during the enclosing mount's setup, with no enclosing boundary, unwinds `mountWith`** (Decision 3 extended). After setup it goes to the enclosing scope. *If wrong:* `mountWith` returns a mount with nothing between the boundary's markers instead of throwing.
9. **Handler exceptions outside every boundary propagate as today.** Decision 2a covers handlers under a boundary only. *If wrong:* the mount would report them instead; the wrappers in Tasks 6 and 8 gain an `else` branch.
10. **Template authoring is `t.boundary (pending: Component<unit>) (failed: Component<BoundaryFailure>) (content: Component<BoundaryStatus>)`.** The spec's sketch uses named arguments, which F# curried members do not have, and an arm for `failed`; an arm's payload reader adds nothing when the failure is fixed for the error view's life. The content receives `IsPending` through its props. Program and Tree get `ops.Boundary (pending, failed, content)` with `content: BoundaryStatus -> 'view`. *If wrong:* the change is confined to `TemplateBuilder.boundary` and the `Operations.Boundary` field.
11. **`IsPending` is false before the first reveal**, and after it is true while any binding under the boundary waits. *If wrong:* content cannot show a spinner before its first reveal, which it is not visible for anyway.
12. **Region item readers (`each`/`Keyed` items, `matchWith`/`Branch` selectors) that raise `NotReadyException` are not registered.** The spec registers bindings only ("Rows, arms and nested components register through their bindings"). Today such a reader fails the region's setup; under a boundary that shows the error view. *If wrong:* a list whose items are async shows the error view instead of the pending view; registering region readers is a follow-up in `ListRegion` and `ArmRegion`.
13. **The HTML backend writes `<!--[-->`/`<!--]-->` around every boundary**, Tree and Program included; a template's boundary region does not add its own pair. `Backend.Boundary`'s default renders the content only, for the Browser interpreter and the frozen baseline backend. *If wrong:* hydration (Task 9 of the framework plan) finds no markers in authored output, or doubled markers in template output.
14. **The queue reports a flush's failures inside the flush, after its commit loop**, one call per scope in order of the scope's first failure, each holding that scope's failures in commit order. A scope whose reporter throws does not stop the others; their errors are raised together after all scopes. *If wrong:* a reporter that calls `Flush` raises "Recursive commit flushing is prohibited".
15. **A queued binding's immediate first write (`Binding.startQueued`'s `first` path), when it fails after setup, goes to the scope directly.** That write runs outside the queue's commit loop, so the queue never sees it. *If wrong:* such failures reach neither the queue nor the scope, as today.
16. **A template event node under a boundary carries its scope as `$$ranvierScope`.** The template state array keeps its layout, so rows outside boundaries keep their retained heap. *If wrong:* one property per event node under a boundary.

## Review Focus

1. A reader or setter failure raised inside a content binding's own effect tears the content down; a later write to the failed source must not rerun the disposed binding, throw, or change the error view. Test: `readerFailure` (Task 5) writes the source again after the failure.
2. `Retry` called from a handler of the error view disposes the view that is dispatching the event; the dispatch must finish without an exception and the content must remount. Test: `retry` (Task 5) clicks the error view's button twice.
3. A mount moved with `MoveBefore` while its boundary is pending must reveal at the new parent, and the content's delegated handlers must fire there. Test: `movedWhilePending` (Task 4).
4. A pending row removed from a list before the reveal must release its registration, and the boundary must reveal once the remaining bindings have values, without failing the list pass that removed the row. Test: `removedPendingRow` (Task 4).
5. Two content bindings failing in the same queued flush must produce one report call, one teardown and one error view. Test: `twoQueuedFailures` (Task 5).

## File structure

| File | Responsibility | Tasks |
|---|---|---|
| `experiments/Ranvier.View.Foundation/Scope.fs` (new) | `ViewScope` interface; `BindingState` bits | 2, 4 |
| `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj` | compiles `Scope.fs` after `Lifetime.fs` | 2 |
| `experiments/Ranvier.View.Foundation/Scheduling.fs` | slot scope; per-scope report after a flush | 2 |
| `experiments/Ranvier.View.Foundation/Binding.fs` | `startQueuedIn`: first-write failures to the scope | 2 |
| `experiments/Ranvier.View.Foundation/Representation.fs` | `BoundaryFailure`, `BoundaryStatus`, `BoundaryViews`, `Backend.Boundary`, `Operations.Boundary`, server boundary in `Html.render` | 3 |
| `experiments/Ranvier.View.Foundation/Authoring.fs` | `Authoring.boundary` | 3 |
| `experiments/Ranvier.View.Foundation/Mounting.fs` | `MountState` as scope; setter catch; pending registration; `Boundary` region; handler catch | 2, 4, 5, 6, 7, 8 |
| `experiments/Ranvier.View.Foundation/TemplateDom.fs` | `RegionKind.BoundaryContent`; `MountServices.Scope`; template pending registration and handler catch | 4, 8 |
| `experiments/Ranvier.View.Foundation/Template.fs` | `RegionRow.BoundaryRow`, `t.boundary`, server and expansion of the boundary region | 8 |
| `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` | links `Scope.fs`; compiles `BoundaryChecks.fs`, `BoundaryPerf.fs` | 2, 3, 10 |
| `examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs` | live setter failure pins and contract | 1, 2 |
| `examples/Fable.Ranvier.Playground/tests/BindingChecks.fs` | queue per-scope report | 2 |
| `examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs` | `Boundary` field of its `Operations` value | 3 |
| `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs` (new) | every boundary test | 3–8 |
| `examples/Fable.Ranvier.Playground/tests/boundary.test.mjs` (new), `tests/range.html`, `package.json` | runs `BoundaryChecks` in jsdom and Chromium | 3 |
| `examples/Fable.Ranvier.Playground/tests/BoundaryPerf.fs` (new), `benchmarks/frameworks/boundary-solid.jsx`, `boundary-page.mjs`, `boundary-reveal.mjs` (new), `page.mjs`, `vite.config.mjs` | boundary reveal benchmark against Solid 2's `Loading` | 10 |
| `docs/research/framework-performance/investigation/view-boundaries/` (new) | before/after benchmark results | 1, 10 |
| `docs/research/2026-10-19-view-boundaries.md` (new), `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` | measurements; Task 5 status | 10 |

## Spec coverage

| Spec item | Task |
|---|---|
| Testing 1: today's behaviour pinned | 1 |
| Live setter failures, synchronous and queued, one report per object, equal value retries (Testing 8) | 2 |
| Server HTML (Testing 10) | 3 (Tree, Program), 8 (template) |
| Authoring `ops.Boundary`, `t.boundary` | 3, 8 |
| Initial and coordinated reveal, rows and arms, one insertion (Testing 2) | 4, 8 |
| Nested boundaries (Testing 3) | 4 |
| Disposal during pending (Testing 9) | 4 |
| Failure kinds, error view, cleanups once, reported once (Testing 5) | 5, 6, 8 |
| `Retry` (Testing 6) | 5, 8 |
| Pending and error view failures go to the enclosing boundary (Testing 7) | 5 |
| Decision 1a, `IsPending` (Testing 4) | 7, 8 |
| Decision 2a, handler exceptions | 6, 8 |
| Decision 3 | 5 (under a boundary), existing tests (without) |
| Decision 4a | 5, 8 |
| Mutation checks | 9 |
| Performance | 1, 10 |

---

### Task 1: Pin today's live setter failure behaviour and record benchmark baselines

**Files:**
- Modify: `examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs` (new function before `let cases`, four entries in `cases`)
- Create: `docs/research/framework-performance/investigation/view-boundaries/keyed-before.json`, `docs/research/framework-performance/investigation/view-boundaries/template-before.json` (benchmark output)

**Interfaces:**
- Consumes: `Mounting.mountWith`, `Mounted.Flush`, `Mounted.CommitErrors`, `Typed.div`, `Typed.reactiveProperty`.
- Produces: `liveSetterFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) (queued: bool)` in `BindingIntegrationChecks.fs`; Task 2 changes its expectations.

No production code changes in this task. The tests pass on first run: they record what the code does now.

- [ ] **Step 1: Add the pinning test**

In `BindingIntegrationChecks.fs`, insert before `let cases =`:

```fsharp
/// <summary>
/// Mounts a div whose title is written by a setter that throws on its first "bad" write. Writing <c>tick</c> re-runs
/// the reader with the same value.
/// </summary>
let private liveSetterFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let tick = graph.Run (fun () -> Api.createSignal 0)
    let reported = ResizeArray<exn>()
    let mutable attempts = 0
    let host = host ()

    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let view =
        Typed.div
            ops
            [|
                Typed.reactiveProperty
                    (fun (node: HTMLDivElement) (value: string) ->
                        if value = "bad" then
                            attempts <- attempts + 1

                            if attempts = 1 then
                                failwith "setter"

                        node.title <- value)
                    (fun () ->
                        tick.Value |> ignore
                        source.Value)
            |]
            [||]

    let mounted =
        mountWith mode graph None reported.AddRange host None (fun context -> run view context.Backend)

    let div = host.querySelector "div" |> unbox<HTMLDivElement>
    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()
    equal ("a", 0, (if queued then 1 else 0)) (div.title, reported.Count, mounted.CommitErrors.Length)
    graph.Run (fun () -> tick.Value <- 1)
    mounted.Flush ()
    equal ("bad", 0, 2) (div.title, reported.Count, attempts)
    mounted.Dispose ()
```

Append to the array inside `cases` (after `"two throwing component cleanups are reported together on disposal", componentCleanupFailures`):

```fsharp
            "tree: a live synchronous setter failure is not reported and an equal later value retries the write",
            fun () -> liveSetterFailure Tree.ops Tree.run false
            "program: a live synchronous setter failure is not reported and an equal later value retries the write",
            fun () -> liveSetterFailure Program.ops Program.run false
            "tree: a live queued setter failure reaches only the queue's errors",
            fun () -> liveSetterFailure Tree.ops Tree.run true
            "program: a live queued setter failure reaches only the queue's errors",
            fun () -> liveSetterFailure Program.ops Program.run true
```

- [ ] **Step 2: Run the tests and confirm they pass**

From `examples/Fable.Ranvier.Playground`:

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/binding.test.mjs`
Expected: PASS, including the four new tests. A failure here means today's behaviour differs from the spec's "What happens today": stop and report the failing expectation.

- [ ] **Step 3: Record the keyed-rows baseline**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/keyed-rows.mjs --build --dist=generated-keyed-bnd0 --label=keyed-before --out=C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/docs/research/framework-performance/investigation/view-boundaries
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Expected: `view-boundaries/keyed-before.json` exists and its `summaries` hold every operation for `ranvier` and `solid`.

- [ ] **Step 4: Record the template-comparison baseline**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/run.mjs --build --template-comparison
cp ../../docs/research/framework-performance/investigation/template-framework-results.json ../../docs/research/framework-performance/investigation/view-boundaries/template-before.json
git -C ../.. checkout -- docs/research/framework-performance/investigation/template-framework-results.json
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Expected: `view-boundaries/template-before.json` exists; `git status` shows the tracked `template-framework-results.json` unchanged.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs
git add examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs docs/research/framework-performance/investigation/view-boundaries/keyed-before.json docs/research/framework-performance/investigation/view-boundaries/template-before.json
git diff --cached --check
git commit -F - <<'EOF'
test(view): pin unreported live setter failures and record boundary benchmark baselines

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 2: Report live setter failures through the mount's scope

**Files:**
- Create: `experiments/Ranvier.View.Foundation/Scope.fs`
- Modify: `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj`, `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`
- Modify: `experiments/Ranvier.View.Foundation/Scheduling.fs` (`Slot`, `Queue.Reserve`, `Queue.Flush`, new `Queue.Report`)
- Modify: `experiments/Ranvier.View.Foundation/Binding.fs` (`startQueued` → `startQueuedIn`)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`RowBuilder` services, `MountState`: `reportReaderFailure` → `reportFailures`, `startBinding`, `Scope`, `interface ViewScope`, MountServices `ReportFailure`)
- Test: `examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs`, `examples/Fable.Ranvier.Playground/tests/BindingChecks.fs`

**Interfaces:**
- Produces:
  - `type internal ViewScope` with `abstract Tracks: bool`, `abstract Wait: unit -> unit`, `abstract Ready: unit -> unit`, `abstract Fail: exn list -> unit` (namespace `Ranvier.View.Foundation`, file `Scope.fs`).
  - `module internal BindingState` with `[<Literal>] ReadFailed = 1` (Task 4 adds `Waiting = 2`).
  - `Queue.Reserve(scope: ViewScope) : Slot` (internal); `Queue.Reserve()` keeps its meaning (no scope).
  - `Binding.startQueuedIn (scope: ViewScope) (settingUp: unit -> bool) registerCleanup isLive queue equals read write`; `Binding.startQueued` is `startQueuedIn` with a null scope.
  - `MountState.Scope : ViewScope` (internal member), and `MountState :> ViewScope` with `Tracks = false`, `Fail` = report once per exception object.
  - `startBinding (scope: ViewScope) (equals: Func<_, _, bool>) read write` (private to `MountState`).

- [ ] **Step 1: Change the Task 1 expectations to the new contract and add the deduplication and queue tests**

In `BindingIntegrationChecks.fs`, rename `liveSetterFailure`'s doc comment and change its two `equal` lines:

```fsharp
    equal ("a", [ "setter" ], (if queued then 1 else 0)) (div.title, [ for error in reported -> error.Message ], mounted.CommitErrors.Length)
    graph.Run (fun () -> tick.Value <- 1)
    mounted.Flush ()
    equal ("bad", 1, 2) (div.title, reported.Count, attempts)
```

Insert after `liveSetterFailure`:

```fsharp
/// <summary>Mounts a div whose setter raises one cached exception object for every value but "a".</summary>
let private repeatedSetterFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "a")
    let failure = Exception "cached"
    let reported = ResizeArray<exn>()
    let host = host ()

    let mode =
        if queued then
            QueuedCommits (CollectAndContinue, ignore)
        else
            SynchronousCommits

    let view =
        Typed.div
            ops
            [|
                Typed.reactiveProperty
                    (fun (node: HTMLDivElement) (value: string) ->
                        if value <> "a" then
                            raise failure

                        node.title <- value)
                    (fun () -> source.Value)
            |]
            [||]

    let mounted =
        mountWith mode graph None reported.AddRange host None (fun context -> run view context.Backend)

    graph.Run (fun () -> source.Value <- "b")
    mounted.Flush ()
    graph.Run (fun () -> source.Value <- "c")
    mounted.Flush ()
    equal [ "cached" ] [ for error in reported -> error.Message ]
    mounted.Dispose ()
```

Replace the four case names added in Task 1 and add four more:

```fsharp
            "tree: a live synchronous setter failure is reported and an equal later value retries the write",
            fun () -> liveSetterFailure Tree.ops Tree.run false
            "program: a live synchronous setter failure is reported and an equal later value retries the write",
            fun () -> liveSetterFailure Program.ops Program.run false
            "tree: a live queued setter failure is reported after the flush and kept in the queue's errors",
            fun () -> liveSetterFailure Tree.ops Tree.run true
            "program: a live queued setter failure is reported after the flush and kept in the queue's errors",
            fun () -> liveSetterFailure Program.ops Program.run true
            "tree: a synchronous setter raising one exception object twice is reported once",
            fun () -> repeatedSetterFailure Tree.ops Tree.run false
            "program: a synchronous setter raising one exception object twice is reported once",
            fun () -> repeatedSetterFailure Program.ops Program.run false
            "tree: a queued setter raising one exception object twice is reported once",
            fun () -> repeatedSetterFailure Tree.ops Tree.run true
            "program: a queued setter raising one exception object twice is reported once",
            fun () -> repeatedSetterFailure Program.ops Program.run true
```

In `BindingChecks.fs`, insert before its `let cases`:

```fsharp
/// <summary>A scope that records each failure call as its name followed by the failures' messages.</summary>
let private recordingScope (name: string) (log: ResizeArray<string>) : ViewScope =
    { new ViewScope with
        member _.Tracks = false
        member _.Wait() = ()
        member _.Ready() = ()

        member _.Fail(errors) =
            log.Add (name + ":" + String.concat "," [ for error in errors -> error.Message ])
    }

/// <summary>Flushes four failing commits: two under one scope, one under another, one without a scope.</summary>
let private failuresByScope failure =
    use graph = new Graph ()
    let queue = Queue (graph, failure, ignore)
    let log = ResizeArray<string>()
    let left, right = recordingScope "left" log, recordingScope "right" log
    let a, b, c, d = queue.Reserve left, queue.Reserve right, queue.Reserve left, queue.Reserve ()
    queue.Enqueue (a, (fun () -> failwith "a"))
    queue.Enqueue (b, (fun () -> failwith "b"))
    queue.Enqueue (c, (fun () -> failwith "c"))
    queue.Enqueue (d, (fun () -> failwith "d"))
    queue.Flush ()
    List.ofSeq log, [ for error in queue.Errors -> error.Message ]

let private collectedByScope () =
    equal ([ "left:a,c"; "right:b" ], [ "a"; "b"; "c"; "d" ]) (failuresByScope CollectAndContinue)

let private cancelledAreUnreported () =
    equal ([ "left:a" ], [ "a" ]) (failuresByScope FailFastCancel)
```

and add to `BindingChecks.cases`:

```fsharp
        "a flush reports each scope's failures in one call, in commit order", collectedByScope
        "fail-fast cancellation reports only the commit that failed", cancelledAreUnreported
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/binding.test.mjs`
Expected: the compile fails: `The type 'ViewScope' is not defined` in `BindingChecks.fs`.

- [ ] **Step 3: Add `Scope.fs` and compile it**

Create `experiments/Ranvier.View.Foundation/Scope.fs`:

```fsharp
namespace Ranvier.View.Foundation

/// <summary>The receiver of the pending reads and failures of the bindings, rows and handlers mounted under it.</summary>
type internal ViewScope =
    /// <summary>True for a boundary: its bindings report pending reads through <c>Wait</c> and <c>Ready</c>.</summary>
    abstract Tracks: bool
    /// <summary>Records a binding whose read is pending.</summary>
    abstract Wait: unit -> unit
    /// <summary>Records that a waiting binding read a value or was disposed.</summary>
    abstract Ready: unit -> unit
    /// <summary>Takes failures in the order they occurred.</summary>
    abstract Fail: exn list -> unit

/// <summary>The bits of a binding's state word.</summary>
module internal BindingState =
    /// <summary>The binding's last read raised an exception other than a pending read.</summary>
    [<Literal>]
    let ReadFailed = 1
```

In `Ranvier.View.Foundation.fsproj`, after `<Compile Include="Lifetime.fs" />` add `<Compile Include="Scope.fs" />`.

In `tests/DomTests.fsproj`, after the `Lifetime.fs` link line add:

```xml
    <Compile Include="../../../experiments/Ranvier.View.Foundation/Scope.fs" Link="Scope.fs" />
```

`Scope.fs` must compile before `Scheduling.fs`; the line after the `Lifetime.fs` link satisfies that.

- [ ] **Step 4: Give slots a scope and report each flush's failures per scope**

In `Scheduling.fs` replace the `Slot` type:

```fsharp
type Slot internal (owner: obj, scope: ViewScope) =
    member internal _.Owner = owner
    member internal _.Scope = scope
    member val internal Version = 0 with get, set
    member val internal Listed = false with get, set
    member val internal Action: (unit -> unit) option = None with get, set
```

Replace `Queue.Reserve`:

```fsharp
    member _.Reserve() =
        if disposed then
            raise (InvalidOperationException "Commit queue is disposed")

        Slot (this, Unchecked.defaultof<_>)

    /// <summary>Reserves a slot whose commit failures go to <c>scope</c> after each flush.</summary>
    member internal _.Reserve(scope: ViewScope) =
        if disposed then
            raise (InvalidOperationException "Commit queue is disposed")

        Slot (this, scope)
```

In `Queue.Flush`, declare `let failedSlots = ResizeArray<Slot>()` beside `let failures = ResizeArray<exn>()`, change the commit handler and report after the loop, inside `graph.Run`:

```fsharp
                    for slot, version, action in work do
                        if stopped then
                            this.Cancel slot
                        elif not disposed && slot.Version = version then
                            match action with
                            | None -> ()
                            | Some commit ->
                                try
                                    Api.untrack commit
                                with error ->
                                    failures.Add error
                                    failedSlots.Add slot
                                    stopped <- failure = FailFastCancel

                    if failures.Count > 0 then
                        Queue.Report (failures, failedSlots))
```

Add to `Queue`:

```fsharp
    /// <summary>Sends each scope the failures of its slots in one call, in commit order.</summary>
    static member private Report(failures: ResizeArray<exn>, slots: ResizeArray<Slot>) =
        let reporterErrors = ResizeArray<exn>()

        for index in 0 .. slots.Count - 1 do
            let scope = slots[index].Scope
            let mutable first = not (isNull (box scope))
            let mutable earlier = 0

            while first && earlier < index do
                if obj.ReferenceEquals (slots[earlier].Scope, scope) then
                    first <- false

                earlier <- earlier + 1

            if first then
                let errors =
                    [
                        for later in index .. slots.Count - 1 do
                            if obj.ReferenceEquals (slots[later].Scope, scope) then
                                failures[later]
                    ]

                try
                    scope.Fail errors
                with error ->
                    reporterErrors.Add error

        if reporterErrors.Count = 1 then
            raise reporterErrors[0]
        elif reporterErrors.Count > 1 then
            raise (AggregateException ("Commit failure reporting failed", reporterErrors))
```

- [ ] **Step 5: Send a queued binding's failed first write to its scope**

Replace `Binding.fs` from the doc comment of `startQueued` to the end:

```fsharp
/// <summary>Creates an owned text effect whose updates are committed through the supplied queue.</summary>
/// <remarks>The first settled value is written immediately. A suspended or failed read cancels any older queued write.
/// A failed immediate write after setup goes to <c>scope</c>; a failed queued write goes to the queue, which reports it
/// to <c>scope</c> after the flush. The enclosing owner disposes the effect.</remarks>
let internal startQueuedIn (scope: ViewScope) (settingUp: unit -> bool) registerCleanup isLive (queue: Queue) equals read write =
    let slot = queue.Reserve scope
    let mutable first = true
    let mutable version = 0
    let mutable hasCommitted = false
    let mutable committed = Unchecked.defaultof<_>
    registerCleanup (fun () -> queue.Cancel slot)

    Api.createEffect (fun () ->
        version <- version + 1
        let current = version
        queue.Cancel slot
        Api.onCleanup (fun () -> queue.Cancel slot)
        let value = read ()
        let owner = Api.getOwner ()

        let commit () =
            if
                isLive ()
                && not owner.IsDisposed
                && version = current
            then
                Api.runWithOwner owner (fun () ->
                    Api.untrack (fun () ->
                        let changed = not hasCommitted || not (equals committed value)

                        if
                            changed
                            && isLive ()
                            && not owner.IsDisposed
                            && version = current
                        then
                            write value

                            if version = current then
                                committed <- value
                                hasCommitted <- true))

        if first then
            first <- false

            if isNull (box scope) then
                commit ()
            else
                try
                    commit ()
                with error when not (settingUp ()) ->
                    scope.Fail [ error ]
        else
            queue.Enqueue (slot, commit))

/// <summary>Creates an owned text effect whose updates are committed through the supplied queue, with no scope.</summary>
let internal startQueued registerCleanup isLive (queue: Queue) equals read write =
    startQueuedIn Unchecked.defaultof<_> (fun () -> false) registerCleanup isLive queue equals read write
```

- [ ] **Step 6: Make `MountState` the scope of its bindings and catch live setter failures**

In `Mounting.fs`:

1. In `RowBuilder`'s `MountServices` implementation, replace `member _.ReportFailure(error) = state.ReportReaderFailure error` with:

```fsharp
        member _.ReportFailure(error) =
            state.Scope.Fail [ error ]
```

2. In `MountState`, replace the comment and body of `reportReaderFailure` (currently `// Sends a reader failure to report ...` through its `raise (AggregateException ("Binding reader and reporter failed", ...))`) with:

```fsharp
    // Reports the failures in one untracked call. A pending read is dropped, and each exception object is reported once
    // until `clearReported` runs.
    let reportFailures (errors: exn list) =
        let fresh = ResizeArray<exn>()

        for error in errors do
            match error with
            | NotReadyException _ -> ()
            | _ when obj.ReferenceEquals (error, lastReported) -> ()
            | _ ->
                lastReported <- error
                fresh.Add error

        if fresh.Count > 0 then
            let reported = List.ofSeq fresh

            try
                Api.untrack (fun () -> report reported)
            with reporterError ->
                raise (AggregateException ("Binding failure and reporter failed", List.toArray (reported @ [ reporterError ])))
```

3. Replace `startBinding` (from `let startBinding` through `bindingSetupFailure |> Option.iter raise` and `#endif`) with:

```fsharp
    let startBinding (scope: ViewScope) (equals: Func<_, _, bool>) read write =
#if LADDER_NAIVE_BINDINGS
        Api.createEffect (fun () -> write (read ()))
#else
        match scheduling with
        | Synchronous ->
            let mutable hasCommitted = false
            let mutable committed = Unchecked.defaultof<_>
            let mutable pending = Unchecked.defaultof<_>

            let commit () =
                let changed =
                    not hasCommitted
                    || not (equals.Invoke (committed, pending))

                if changed && not root.IsDisposed then
                    try
                        write pending
                        committed <- pending
                        hasCommitted <- true
                    with
                    | error when root.Phase = SettingUp ->
                        bindingSetupFailure <- Some error
                        reraise ()
                    | error -> scope.Fail [ error ]

            let mutable state = 0

            Api.createEffect (fun () ->
                let value =
                    try
                        read ()
                    with error ->
                        match error with
                        | NotReadyException _ -> ()
                        | _ ->
                            state <- state ||| BindingState.ReadFailed
                            scope.Fail [ error ]

                        reraise ()

                if state &&& BindingState.ReadFailed <> 0 then
                    state <- state &&& ~~~BindingState.ReadFailed
                    clearReported ()

                if not root.IsDisposed then
                    pending <- value
                    Api.untrack commit)
        | Queued queue ->
            let live () =
                not root.IsDisposed

            let guardedWrite value =
                try
                    write value
                with error when root.Phase = SettingUp ->
                    bindingSetupFailure <- Some error
                    reraise ()

            let mutable state = 0

            let reportingRead () =
                let value =
                    try
                        read ()
                    with error ->
                        match error with
                        | NotReadyException _ -> ()
                        | _ ->
                            state <- state ||| BindingState.ReadFailed
                            scope.Fail [ error ]

                        reraise ()

                if state &&& BindingState.ReadFailed <> 0 then
                    state <- state &&& ~~~BindingState.ReadFailed
                    clearReported ()

                value

            Binding.startQueuedIn
                scope
                (fun () -> root.Phase = SettingUp)
                root.OnCleanup
                live
                queue
                (fun left right -> equals.Invoke (left, right))
                reportingRead
                guardedWrite

        bindingSetupFailure |> Option.iter raise
#endif
```

4. Replace the member `ReportReaderFailure` with:

```fsharp
    /// <summary>The receiver of the pending reads and failures of the bindings this mount creates.</summary>
    member internal this.Scope: ViewScope = this :> ViewScope
```

5. Pass the scope at every `startBinding` call: in `BindText`, `ReactiveAttribute`, `ReactiveProperty` and `Bind`, change `startBinding (` / `startBinding attributeEquals` / `startBinding propertyEquals` to `startBinding this.Scope (` / `startBinding this.Scope attributeEquals` / `startBinding this.Scope propertyEquals`, and give each of those overrides a `this` self-identifier where it has `_` (`override this.BindText(node, read)`, `override this.ReactiveAttribute(key, read)`, `override this.ReactiveProperty(read, write)`, `override this.Bind(equals, read)`).

6. In `MountState`'s `MountServices` implementation, replace `member _.ReportFailure(error) = reportReaderFailure error` with:

```fsharp
        member this.ReportFailure(error) =
            this.Scope.Fail [ error ]
```

7. After `MountState`'s `interface TemplateDom.MountServices with` block, add:

```fsharp
    interface ViewScope with
        member _.Tracks = false
        member _.Wait() = ()
        member _.Ready() = ()

        member _.Fail(errors) =
            reportFailures errors
```

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/binding.test.mjs tests/template.test.mjs tests/range.test.mjs`
Expected: PASS, all tests.

- [ ] **Step 8: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 9: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Scope.fs experiments/Ranvier.View.Foundation/Scheduling.fs experiments/Ranvier.View.Foundation/Binding.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs examples/Fable.Ranvier.Playground/tests/BindingChecks.fs
git add experiments/Ranvier.View.Foundation/Scope.fs experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj experiments/Ranvier.View.Foundation/Scheduling.fs experiments/Ranvier.View.Foundation/Binding.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/tests/BindingIntegrationChecks.fs examples/Fable.Ranvier.Playground/tests/BindingChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): report live setter failures once per exception through the binding's scope

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 3: Boundary authoring surface and server HTML for Tree and Program

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Representation.fs` (types before `Backend`, `Backend.Boundary`, `BoundaryViews`, `Operations.Boundary`, `Tree.ops`, `Program.ops`, `Html.render`)
- Modify: `experiments/Ranvier.View.Foundation/Authoring.fs`
- Modify: `examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs` (`modifierTokens`)
- Create: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`, `examples/Fable.Ranvier.Playground/tests/boundary.test.mjs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, `examples/Fable.Ranvier.Playground/tests/range.html`, `examples/Fable.Ranvier.Playground/package.json`

**Interfaces:**
- Produces (module `Ranvier.View.Foundation.Representation`):
  - `type BoundaryFailure = { Error: exn; Retry: unit -> unit }`
  - `type BoundaryStatus = { IsPending: unit -> bool }`
  - `type BoundaryViews = { Pending: Backend -> unit; Failed: BoundaryFailure -> Backend -> unit; Content: BoundaryStatus -> Backend -> unit }`
  - `Backend.Boundary: BoundaryViews -> unit`, default: `views.Content { IsPending = fun () -> false } this`
  - `Operations<'view>.Boundary: 'view * (BoundaryFailure -> 'view) * (BoundaryStatus -> 'view) -> 'view`
  - `Authoring.boundary pending failed content`
  - `BoundaryChecks.cases: (string * (unit -> unit)) array`, run by `tests/boundary.test.mjs` and `tests/range.html`.

In this task the DOM backend uses `Backend.Boundary`'s default; Task 4 overrides it.

- [ ] **Step 1: Create the boundary test module with the server test**

Create `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`:

```fsharp
module BoundaryChecks

open System
open Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Scheduling
open Ranvier.View.Foundation.Mounting

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>Renders a boundary over one async source as HTML while it is pending, settled and failed.</summary>
let private server (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            let source = Api.createAsyncSource<string> ()

            let view =
                ops.Boundary (
                    ops.Text "<loading>",
                    (fun failure -> ops.Text failure.Error.Message),
                    fun _ -> Typed.div ops [||] [| Typed.reactiveText ops (fun () -> source.Value) |]
                )

            let render () =
                Html.render Reject (run view)

            equal "<!--[-->&lt;loading&gt;<!--]-->" (render ())
            source.Settle "<&"
            equal "<!--[--><div>&lt;&amp;</div><!--]-->" (render ())
            source.Fail (Exception "<failed>")
            equal "<!--[-->&lt;failed&gt;<!--]-->" (render ())))

let cases: (string * (unit -> unit)) array =
    [|
        "tree HTML renders a boundary's pending, content or error view between markers", fun () -> server Tree.ops Tree.run
        "program HTML renders a boundary's pending, content or error view between markers",
        fun () -> server Program.ops Program.run
    |]
```

Create `examples/Fable.Ranvier.Playground/tests/boundary.test.mjs`:

```js
import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests/BoundaryChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
globalThis.FocusEvent = dom.window.FocusEvent;
after(() => { dom.window.close(); delete globalThis.window; delete globalThis.document; delete globalThis.FocusEvent; });
for (const [name, check] of cases) test(name, () => check());
```

In `tests/DomTests.fsproj`, after `<Compile Include="TemplateCatalogueChecks.fs" />` add `<Compile Include="BoundaryChecks.fs" />`.

In `tests/range.html`, add the import and spread it into `window.rangeCases`:

```html
import { cases as boundaries } from "../generated-tests/BoundaryChecks.js";
```

```js
window.rangeCases = [...cases, ...bindings, ...integration, ...generated, ...attributes, ...boundaries];
```

In `package.json`, append ` tests/boundary.test.mjs` to the file list of the `test:dom` script, after `tests/owned-async.test.mjs`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: the compile fails: `The record label 'Boundary' is not defined` or `The field, constructor or member 'Boundary' is not defined`.

- [ ] **Step 3: Add the boundary types and the backend operation**

In `Representation.fs`, insert before `[<AbstractClass>] type Backend() =`:

```fsharp
/// <summary>The failure an error view receives.</summary>
[<NoEquality; NoComparison>]
type BoundaryFailure =
    {
        /// <summary>The first exception of the failure that replaced the boundary's content.</summary>
        Error: exn
        /// <summary>Disposes the error view and mounts the content again, with a new setup and a new pending phase.</summary>
        Retry: unit -> unit
    }

/// <summary>The state of a boundary that its content reads.</summary>
[<NoEquality; NoComparison>]
type BoundaryStatus =
    {
        /// <summary>A tracked reader: true after the first reveal while a binding under the boundary waits for a pending read.</summary>
        IsPending: unit -> bool
    }
```

In `Backend`, after the `default this.Template(definition, props)` member, add:

```fsharp
    /// <summary>Renders content under a boundary with its pending and error views.</summary>
    /// <remarks>The default renders the content only.</remarks>
    abstract Boundary: BoundaryViews -> unit

    default this.Boundary(views) =
        views.Content { IsPending = fun () -> false } this
```

After the `TemplateDefinition` type (still in the `and` group), add:

```fsharp
/// <summary>The three views of a boundary, each rendered through the backend it receives.</summary>
and [<NoEquality; NoComparison>] BoundaryViews =
    {
        Pending: Backend -> unit
        Failed: BoundaryFailure -> Backend -> unit
        Content: BoundaryStatus -> Backend -> unit
    }
```

In `Operations<'view>`, after the `Embed` field, add:

```fsharp
        /// <summary>Renders the content under a boundary: the pending view until the content's bindings have values, the error view after a failure under the content.</summary>
        Boundary: 'view * (BoundaryFailure -> 'view) * (BoundaryStatus -> 'view) -> 'view
```

In `Tree.ops`, after `Embed = Embed`, add:

```fsharp
            Boundary =
                fun (pending, failed, content) ->
                    Embed (fun backend ->
                        backend.Boundary
                            {
                                Pending = run pending
                                Failed = fun failure target -> run (failed failure) target
                                Content = fun status target -> run (content status) target
                            })
```

In `Program.ops`, after the `Embed` field, add:

```fsharp
            Boundary =
                fun (pending, failed, content) ->
                    View (fun backend ->
                        backend.Boundary
                            {
                                Pending = fun target -> pending.Invoke target
                                Failed = fun failure target -> (failed failure).Invoke target
                                Content = fun status target -> (content status).Invoke target
                            })
```

- [ ] **Step 4: Render boundaries in server HTML**

In `Html.render`, replace `let output = System.Text.StringBuilder ()` with `let mutable sink = System.Text.StringBuilder ()`, replace both `output.Append` calls in `append` with `sink.Append`, and replace the final `output.ToString ()` with `sink.ToString ()`. Add to the backend object expression, after `member this.Template(definition, props)`:

```fsharp
                member this.Boundary(views) =
                    append "<!--[-->"
                    let outer = sink
                    let buffer = System.Text.StringBuilder ()
                    sink <- buffer

                    let outcome =
                        try
                            try
                                views.Content { IsPending = fun () -> false } this
                                None
                            with error ->
                                Some error
                        finally
                            sink <- outer

                    match outcome with
                    | None -> sink.Append (buffer.ToString ()) |> ignore
                    | Some (Ranvier.NotReadyException _) -> views.Pending this
                    | Some error -> views.Failed { Error = error; Retry = ignore } this

                    append "<!--]-->"
```

In `Authoring.fs`, append:

```fsharp
/// <summary>Renders <c>content</c> under a boundary: <c>pending</c> until its bindings have values, <c>failed</c> after a failure under it.</summary>
let inline boundary pending failed content =
    Program.ops.Boundary (pending, failed, content)
```

In `tests/FrameworkDiagnostics.fs`, in `modifierTokens`, after the `Embed` field add:

```fsharp
        Boundary = fun _ -> invalidOp "Token extraction requires an element"
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs tests/representation.test.mjs`
Expected: PASS.

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj` (from the worktree root)
Expected: exit 0.

- [ ] **Step 6: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0; `npm run test:browser` includes the two new cases through `range.html`.

- [ ] **Step 7: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Representation.fs experiments/Ranvier.View.Foundation/Authoring.fs examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Foundation/Representation.fs experiments/Ranvier.View.Foundation/Authoring.fs examples/Fable.Ranvier.Playground/tests/FrameworkDiagnostics.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs examples/Fable.Ranvier.Playground/tests/boundary.test.mjs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/tests/range.html examples/Fable.Ranvier.Playground/package.json
git diff --cached --check
git commit -F - <<'EOF'
feat(view): add boundary views to the representations and render them in server HTML

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 4: Coordinated reveal under DOM boundaries

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Scope.fs` (`BindingState.Waiting`)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`RegionKind.BoundaryContent`, `materialise`'s `read`)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (two emits, `Boundary` class, `MountState` constructor and `Scope`, `startBinding`, `MountRow`, `Boundary` override, `BoundaryRegion`, `RegionOf`, `mountWith`)
- Test: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`

**Interfaces:**
- Consumes: `ViewScope`, `BindingState`, `startBinding (scope)` (Task 2); `BoundaryViews`, `BoundaryStatus`, `Operations.Boundary` (Task 3).
- Produces:
  - `BindingState.Waiting = 2`.
  - `TemplateDom.RegionKind.BoundaryContent`: a region whose single item's value is a `BoundaryViews`.
  - `MountState(scheduling, graph, parent, parentOwner, report, host, boundary: ViewScope)`: `boundary` is null outside every boundary; `MountState.Scope` returns `boundary` when it is set.
  - `MountState.MountRow(current: unit -> obj, render: (unit -> obj) -> Backend -> unit, scope: ViewScope) : Row`.
  - `Boundary` (private class in `Mounting.fs`): constructor `(graph, report, site: RegionSite, regionEnd: Node, views: BoundaryViews, enclosing: ViewScope, unwinds: unit -> bool, mountView: (unit -> obj) -> ((unit -> obj) -> Backend -> unit) -> ViewScope -> Row)`, member `Start: unit -> unit`, implements `ViewScope` with `Tracks = true`. Task 5 adds `Settle`'s failure branch, `TearDown` and `Retry`; Task 7 adds the `IsPending` signal.

- [ ] **Step 1: Write the reveal tests**

In `BoundaryChecks.fs`, insert after `server`:

```fsharp
let private host () : HTMLElement =
    globalThis.document.createElement "div"

let private modeOf queued =
    if queued then
        QueuedCommits (CollectAndContinue, ignore)
    else
        SynchronousCommits

/// <summary>Counts the node's own <c>insertBefore</c> calls; the returned function restores it and returns the count.</summary>
[<Emit("(() => { let count = 0; const old = $0.insertBefore; $0.insertBefore = function(n, b) { count++; return old.call(this, n, b); }; return () => { $0.insertBefore = old; return count; }; })()")>]
let private countInserts (node: Node) : unit -> int = jsNative

let private strict (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

/// <summary>
/// A boundary whose content holds a text on one source, keyed rows reading a second source, and an arm reading the
/// first. The content reveals in one insertion once both sources settle.
/// </summary>
let private initialReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let first = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let second = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let log = ResizeArray<string>()
    let host = host ()

    let content (_: BoundaryStatus) =
        ops.Component (fun setup ->
            log.Add "content"
            setup.OnCleanup (fun () -> log.Add "content cleanup")

            ops.Fragment
                [|
                    Typed.reactiveText ops (fun () -> first.Value)
                    Keyed.list ops id (fun () -> [ 1; 2 ]) (fun current -> Typed.reactiveText ops (fun () -> second.Value + string (current ())))
                    Branch.select ops id (fun () -> 0) (fun _ -> Typed.reactiveText ops (fun () -> "|" + first.Value))
                |])

    let pending =
        ops.Component (fun setup ->
            log.Add "pending"
            setup.OnCleanup (fun () -> log.Add "pending cleanup")
            ops.Text "loading")

    let view =
        ops.Boundary (pending, (fun failure -> ops.Text failure.Error.Message), content)

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal ("loading", [ "content"; "pending" ]) (host.textContent, List.ofSeq log)
    let inserts = countInserts host
    graph.Run (fun () -> first.Settle "A")
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> second.Settle "B")
    mounted.Flush ()
    equal ("AB1B2|A", 1) (host.textContent, inserts ())
    equal [ "content"; "pending"; "pending cleanup" ] (List.ofSeq log)
    mounted.Dispose ()
    equal ([ "content"; "pending"; "pending cleanup"; "content cleanup" ], "") (List.ofSeq log, host.textContent)

/// <summary>An inner boundary inside an outer boundary's content; each reveals when its own source settles.</summary>
let private nestedReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued innerFirst =
    use graph = new Graph ()
    let outer = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let inner = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let host = host ()

    let failed (failure: BoundaryFailure) =
        ops.Text failure.Error.Message

    let innerView =
        ops.Boundary (ops.Text "[inner loading]", failed, (fun _ -> Typed.reactiveText ops (fun () -> inner.Value)))

    let view =
        ops.Boundary (
            ops.Text "[outer loading]",
            failed,
            fun _ -> ops.Fragment [| Typed.reactiveText ops (fun () -> outer.Value); innerView |]
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal "[outer loading]" host.textContent

    if innerFirst then
        graph.Run (fun () -> inner.Settle "B")
        mounted.Flush ()
        equal "[outer loading]" host.textContent
        graph.Run (fun () -> outer.Settle "A")
        mounted.Flush ()
    else
        graph.Run (fun () -> outer.Settle "A")
        mounted.Flush ()
        equal "A[inner loading]" host.textContent
        graph.Run (fun () -> inner.Settle "B")
        mounted.Flush ()

    equal "AB" host.textContent
    mounted.Dispose ()

/// <summary>Disposes a mount while its boundary is pending, then settles the source.</summary>
let private disposedWhilePending (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let log = ResizeArray<string>()
    let host = host ()

    let logged name (view: 'view) =
        ops.Component (fun setup ->
            log.Add name
            setup.OnCleanup (fun () -> log.Add (name + " cleanup"))
            view)

    let view =
        ops.Boundary (
            logged "pending" (ops.Text "loading"),
            (fun failure -> ops.Text failure.Error.Message),
            fun _ -> logged "content" (Typed.reactiveText ops (fun () -> source.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    let inserts = countInserts host
    mounted.Dispose ()
    equal ([ "content"; "pending"; "pending cleanup"; "content cleanup" ], 0.) (List.ofSeq log, host.childNodes.length)
    graph.Run (fun () -> source.Settle "late")
    mounted.Flush ()
    equal (4, 0., 0) (log.Count, host.childNodes.length, inserts ())

/// <summary>Moves a mount to another host while its boundary is pending; the content reveals there and handles clicks.</summary>
let private movedWhilePending (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let mutable clicks = 0
    let first, second = host (), host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                Typed.button
                    ops
                    [| Typed.listener "click" (fun _ -> clicks <- clicks + 1) |]
                    [| Typed.reactiveText ops (fun () -> source.Value) |]
        )

    let mounted =
        mountWith (modeOf queued) graph None strict first None (fun context -> run view context.Backend)

    mounted.MoveBefore (second, None)
    graph.Run (fun () -> source.Settle "moved")
    mounted.Flush ()
    equal ("", "moved") (first.textContent, second.textContent)
    (second.querySelector "button" |> unbox<HTMLButtonElement>).click ()
    equal 1 clicks
    mounted.Dispose ()

/// <summary>Removes the one row still pending before the reveal; the boundary reveals the remaining rows.</summary>
let private removedPendingRow (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let never = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let other = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure -> ops.Text failure.Error.Message),
            fun _ ->
                Keyed.list ops id (fun () -> rows.Value) (fun current ->
                    Typed.reactiveText ops (fun () -> if current () = 2 then never.Value else other.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    graph.Run (fun () -> other.Settle "x")
    mounted.Flush ()
    equal "loading" host.textContent
    graph.Run (fun () -> rows.Value <- [ 1 ])
    mounted.Flush ()
    equal "x" host.textContent
    mounted.Dispose ()
```

Append to `cases`:

```fsharp
        "tree synchronous: a boundary reveals its content in one insertion once every binding, row and arm has a value",
        fun () -> initialReveal Tree.ops Tree.run false
        "tree queued: a boundary reveals its content in one insertion once every binding, row and arm has a value",
        fun () -> initialReveal Tree.ops Tree.run true
        "program synchronous: a boundary reveals its content in one insertion once every binding, row and arm has a value",
        fun () -> initialReveal Program.ops Program.run false
        "program queued: a boundary reveals its content in one insertion once every binding, row and arm has a value",
        fun () -> initialReveal Program.ops Program.run true
        "tree synchronous: a nested boundary reveals after its outer boundary", fun () -> nestedReveal Tree.ops Tree.run false false
        "program queued: a nested boundary reveals after its outer boundary", fun () -> nestedReveal Program.ops Program.run true false
        "tree queued: a nested boundary revealed first appears with its outer boundary",
        fun () -> nestedReveal Tree.ops Tree.run true true
        "program synchronous: a nested boundary revealed first appears with its outer boundary",
        fun () -> nestedReveal Program.ops Program.run false true
        "tree synchronous: disposal during the pending phase disposes the hidden content and the pending view",
        fun () -> disposedWhilePending Tree.ops Tree.run false
        "program queued: disposal during the pending phase disposes the hidden content and the pending view",
        fun () -> disposedWhilePending Program.ops Program.run true
        "tree queued: a boundary moved while pending reveals at its new parent and handles clicks there",
        fun () -> movedWhilePending Tree.ops Tree.run true
        "program synchronous: a boundary moved while pending reveals at its new parent and handles clicks there",
        fun () -> movedWhilePending Program.ops Program.run false
        "tree synchronous: removing the last pending row reveals the boundary", fun () -> removedPendingRow Tree.ops Tree.run false
        "program queued: removing the last pending row reveals the boundary", fun () -> removedPendingRow Program.ops Program.run true
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: FAIL. `initialReveal` fails with `Expected ("loading", ["content"; "pending"]); got ("", ["content"])` (the default backend renders the content directly; text bindings read pending sources and leave their nodes empty).

- [ ] **Step 3: Add the `Waiting` bit and the boundary region kind**

In `Scope.fs`, in `BindingState`, add:

```fsharp
    /// <summary>The binding's last read was pending and the binding is registered with its scope.</summary>
    [<Literal>]
    let Waiting = 2
```

In `TemplateDom.fs`, add a case to `RegionKind`:

```fsharp
    /// <summary>A boundary: the region's single item's value is its <c>BoundaryViews</c>, read once.</summary>
    | BoundaryContent
```

and in `materialise`, change the `read` match's first arm to:

```fsharp
            match region.Kind with
            | ViewHole
            | BoundaryContent ->
                let snapshot = items.Invoke owner
                fun () -> snapshot
```

- [ ] **Step 4: Add the `Boundary` region class**

In `Mounting.fs`, after the `deleteBetween` emit, add:

```fsharp
/// <summary>Whether <c>first</c> and <c>last</c> are the first and last children of a document fragment.</summary>
[<Emit("$0.parentNode != null && $0.parentNode.nodeType === 11 && $0.parentNode.firstChild === $0 && $0.parentNode.lastChild === $1")>]
let private fillsFragment (first: Node) (last: Node) : bool = jsNative

[<Emit("$0.parentNode")>]
let private parentOrNull (node: Node) : Node = jsNative
```

Insert after the `RegionSite` type, before `and internal MountState`:

```fsharp
/// <summary>
/// A boundary between region markers: its content, mounted detached until no binding under it waits for a pending read,
/// and its pending view, shown between the markers meanwhile.
/// </summary>
/// <remarks>
/// The pending view's failures go to <c>enclosing</c>; while <c>unwinds</c> is true they raise instead. The reveal runs
/// in the boundary's effect, after the binding or pass whose read or disposal released the last pending read.
/// </remarks>
and [<Sealed; AllowNullLiteral>] private Boundary
    (
        graph: Graph,
        report: exn list -> unit,
        site: RegionSite,
        regionEnd: Node,
        views: BoundaryViews,
        enclosing: ViewScope,
        unwinds: unit -> bool,
        mountView: (unit -> obj) -> ((unit -> obj) -> Backend -> unit) -> ViewScope -> Row
    ) =
    // The content's nested mount; null until its setup returns.
    let mutable content: Row = null
    // The view between the markers while the content is hidden; null otherwise.
    let mutable shown: Row = null
    let mutable waiting = 0
    let mutable settingUp = false
    let mutable revealed = false
    let mutable wake: Signal<int> = Unchecked.defaultof<_>
    let mutable status: BoundaryStatus = Unchecked.defaultof<_>

    let noItem () : obj = null

    let place (row: Row) =
        let parent = site.Parent ()

        if fillsFragment row.First row.Last then
            parent.insertBefore (parentOrNull row.First, regionEnd)
            |> ignore
        else
            Row.MoveBefore (row, parent, regionEnd)

        row.Mount.Relocate ()
        site.LaidOut.Add row

    let retire () =
        if not (isNull shown) then
            let row = shown
            shown <- null
            site.LaidOut.Clear ()
            row.Mount.Dispose ()

    member private _.Show(render: Backend -> unit) =
        let mutable row: Row = null

        try
            row <- Api.runWithOwner site.Owner (fun () -> mountView noItem (fun _ backend -> render backend) enclosing)
            place row
            shown <- row
        with error ->
            if not (isNull row) then
                row.Mount.Dispose ()

            if unwinds () then
                reraise ()

            enclosing.Fail [ error ]

    member private _.Reveal() =
        revealed <- true
        retire ()
        place content

    member private this.Settle() =
        if
            not revealed
            && not settingUp
            && waiting = 0
            && not (isNull content)
            && site.Live ()
        then
            this.Reveal ()

    member private this.MountContent() =
        settingUp <- true
        waiting <- 0

        try
            content <- Api.runWithOwner site.Owner (fun () -> mountView noItem (fun _ backend -> views.Content status backend) (this :> ViewScope))
        finally
            settingUp <- false

        if waiting = 0 then this.Reveal () else this.Show views.Pending

    /// <summary>Creates the boundary's effect and mounts its content. Call once, inside the region's owner.</summary>
    member this.Start() =
        wake <- Api.createSignal 0
        status <- { IsPending = fun () -> false }

        Api.createEffect (fun () ->
            wake.Value |> ignore
            Api.untrack (fun () -> this.Settle ()))

        this.MountContent ()

    interface ViewScope with
        member _.Tracks = true

        member _.Wait() =
            waiting <- waiting + 1

        member _.Ready() =
            if waiting > 0 then
                waiting <- waiting - 1

                if waiting = 0 && not revealed then
                    Api.untrack (fun () -> wake.Value <- wake.Value + 1)

        member _.Fail(errors) =
            enclosing.Fail errors
```

- [ ] **Step 5: Give `MountState` its boundary and register pending reads**

In `Mounting.fs`:

1. Change the `MountState` declaration to:

```fsharp
and internal MountState
    (scheduling: Scheduling, graph: Graph, parent: MountLifetime, parentOwner: Owner, report: exn list -> unit, host: Node, boundary: ViewScope) =
```

2. Replace the `Scope` member added in Task 2 with:

```fsharp
    /// <summary>The receiver of the pending reads and failures of the bindings this mount creates: its boundary, or the mount itself.</summary>
    member internal this.Scope: ViewScope =
        if isNull (box boundary) then
            this :> ViewScope
        else
            boundary
```

3. Replace `startBinding` with:

```fsharp
    let startBinding (scope: ViewScope) (equals: Func<_, _, bool>) read write =
#if LADDER_NAIVE_BINDINGS
        Api.createEffect (fun () -> write (read ()))
#else
        let mutable state = 0

        if scope.Tracks then
            Api.onCleanup (fun () ->
                if state &&& BindingState.Waiting <> 0 then
                    state <- state &&& ~~~BindingState.Waiting
                    scope.Ready ())

        match scheduling with
        | Synchronous ->
            let mutable hasCommitted = false
            let mutable committed = Unchecked.defaultof<_>
            let mutable pending = Unchecked.defaultof<_>

            let commit () =
                let changed =
                    not hasCommitted
                    || not (equals.Invoke (committed, pending))

                if changed && not root.IsDisposed then
                    try
                        write pending
                        committed <- pending
                        hasCommitted <- true
                    with
                    | error when root.Phase = SettingUp ->
                        bindingSetupFailure <- Some error
                        reraise ()
                    | error -> scope.Fail [ error ]

            Api.createEffect (fun () ->
                let value =
                    try
                        read ()
                    with error ->
                        match error with
                        | NotReadyException _ ->
                            if
                                state &&& BindingState.Waiting = 0
                                && scope.Tracks
                            then
                                state <- state ||| BindingState.Waiting
                                scope.Wait ()
                        | _ ->
                            state <- state ||| BindingState.ReadFailed
                            scope.Fail [ error ]

                        reraise ()

                let previous = state

                if previous <> 0 then
                    state <- 0

                    if previous &&& BindingState.ReadFailed <> 0 then
                        clearReported ()

                if not root.IsDisposed then
                    pending <- value
                    Api.untrack commit

                if previous &&& BindingState.Waiting <> 0 then
                    scope.Ready ())
        | Queued queue ->
            let live () =
                not root.IsDisposed

            let guardedWrite value =
                try
                    write value
                with error when root.Phase = SettingUp ->
                    bindingSetupFailure <- Some error
                    reraise ()

            let reportingRead () =
                let value =
                    try
                        read ()
                    with error ->
                        match error with
                        | NotReadyException _ ->
                            if
                                state &&& BindingState.Waiting = 0
                                && scope.Tracks
                            then
                                state <- state ||| BindingState.Waiting
                                scope.Wait ()
                        | _ ->
                            state <- state ||| BindingState.ReadFailed
                            scope.Fail [ error ]

                        reraise ()

                let previous = state

                if previous <> 0 then
                    state <- 0

                    if previous &&& BindingState.ReadFailed <> 0 then
                        clearReported ()

                    if previous &&& BindingState.Waiting <> 0 then
                        scope.Ready ()

                value

            Binding.startQueuedIn
                scope
                (fun () -> root.Phase = SettingUp)
                root.OnCleanup
                live
                queue
                (fun left right -> equals.Invoke (left, right))
                reportingRead
                guardedWrite

        bindingSetupFailure |> Option.iter raise
#endif
```

4. Replace `MountRow` with:

```fsharp
    /// <summary>Mounts an authored row or a boundary's view as a nested mount under <c>scope</c> and returns its nodes.</summary>
    member private _.MountRow(current: unit -> obj, render: (unit -> obj) -> Backend -> unit, scope: ViewScope) : Row =
        let rowHost = fragment ()

        let mounted =
            MountState(scheduling, graph, root, Unchecked.defaultof<_>, report, rowHost, scope)
                .Run(None, (fun context -> render current context.Backend))

        let first =
            rowHost.firstChild
            |> Option.defaultWith (fun () -> invalid "A keyed row has no start boundary")

        let last =
            rowHost.lastChild
            |> Option.defaultWith (fun () -> invalid "A keyed row has no end boundary")

        Row (first, last, Mount = mounted)
```

and change its three callers to pass this mount's boundary: in `ListRegion.buildRow` `this.MountRow (current, render, boundary)`, in `ArmRegion.mountArm` `this.MountRow (current, render, boundary)`, in `HoleRegion` `this.MountRow ((fun () -> view), render, boundary)`.

5. Add the boundary region after `HoleRegion`:

```fsharp
    /// <summary>Mounts a boundary between the markers; its content's bindings report to it, its views to this mount's scope.</summary>
    member private this.BoundaryRegion(regionStart: Node, regionEnd: Node, placement: RegionPlacement, views: BoundaryViews) =
        let site = this.Site (regionStart, regionEnd, placement)

        let created =
            Boundary (
                graph,
                report,
                site,
                regionEnd,
                views,
                this.Scope,
                (fun () -> isNull (box boundary) && root.Phase = SettingUp),
                fun current render scope -> this.MountRow (current, render, scope)
            )

        site.Own (fun () -> created.Start ())
```

6. Add the case to `RegionOf`:

```fsharp
        | TemplateDom.BoundaryContent ->
            let pairs = items.Read ()
            this.BoundaryRegion (start, finish, placement, unbox<BoundaryViews> (KeyedItems.value (pairs[0])))
```

7. Override the backend operation, after `override this.Switch(read, render)`:

```fsharp
    override this.Boundary(views) =
        let items: KeyedItems =
            {
                Read = fun () -> [| box ((null: obj), box views) |]
                Key = KeyedItems.pairKey
            }

        this.AuthoredRegion (TemplateDom.BoundaryContent, items, (fun _ _ -> ()))
```

8. In `mountWith`, pass `Unchecked.defaultof<_>` as the new last argument of both `MountState(...)` constructions.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs tests/binding.test.mjs tests/template.test.mjs tests/range.test.mjs tests/delegation.test.mjs`
Expected: PASS, all tests.

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Scope.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Foundation/Scope.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): reveal a boundary's content once every binding under it has a value

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 5: Failures under a boundary: teardown, error view, report and retry

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`Mounted.Abandon`, `Boundary`, `MountState.Site`)
- Test: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`

**Interfaces:**
- Consumes: `Boundary` (Task 4), `MountLifetime.Capture`, `MountLifetime.Abandon`.
- Produces:
  - `Mounted.Abandon: unit -> unit` (internal): tears the mount down inside a teardown window serving its reporter.
  - `Boundary` failure semantics: `Fail` during content setup holds the failures; afterwards the boundary's effect tears the content down, mounts the error view and reports the failures followed by the teardown errors in one `report` call. `BoundaryFailure.Retry` remounts the content.
  - Region reports (row and arm setup failures, release failures) of a mount under a boundary go to the boundary.

- [ ] **Step 1: Write the failure tests**

In `BoundaryChecks.fs`, insert after `removedPendingRow`:

```fsharp
let private messages (reports: ResizeArray<exn list>) =
    [ for errors in reports -> errors |> List.map _.Message |> List.distinct ]

/// <summary>
/// Mounts <c>content</c> under a boundary whose error view shows the failure's message. The content and error views log
/// their setup and cleanup.
/// </summary>
let private underBoundary
    (ops: Operations<'view>)
    (run: 'view -> Backend -> unit)
    queued
    (graph: Graph)
    (host: HTMLElement)
    (log: ResizeArray<string>)
    (reports: ResizeArray<exn list>)
    (content: unit -> 'view)
    =
    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure ->
                ops.Component (fun setup ->
                    log.Add ("error " + failure.Error.Message)
                    setup.OnCleanup (fun () -> log.Add "error cleanup")
                    ops.Text ("failed: " + failure.Error.Message))),
            fun _ ->
                ops.Component (fun setup ->
                    log.Add "content"
                    setup.OnCleanup (fun () -> log.Add "content cleanup")
                    content ())
        )

    mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

let private readerFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.reactiveText ops (fun () -> if source.Value = "bad" then failwith "reader" else source.Value))

    equal "ok" host.textContent
    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()
    let expected = "failed: reader", [ "content"; "content cleanup"; "error reader" ], [ [ "reader" ] ]
    equal expected (host.textContent, List.ofSeq log, messages reports)
    graph.Run (fun () -> source.Value <- "bad again")
    mounted.Flush ()
    equal expected (host.textContent, List.ofSeq log, messages reports)
    mounted.Dispose ()

let private setterFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.div
                ops
                [|
                    Typed.reactiveProperty
                        (fun (node: HTMLDivElement) (value: string) ->
                            if value = "bad" then
                                failwith "setter"

                            node.title <- value)
                        (fun () -> source.Value)
                |]
                [| Typed.reactiveText ops (fun () -> source.Value) |])

    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    equal
        ("failed: setter", [ "content"; "content cleanup"; "error setter" ], [ [ "setter" ] ], (if queued then 1 else 0))
        (host.textContent, List.ofSeq log, messages reports, mounted.CommitErrors.Length)

    mounted.Dispose ()

let private rowSetupFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let rows = graph.Run (fun () -> Api.createSignal [ 1; 2 ])
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Keyed.list ops id (fun () -> rows.Value) (fun current ->
                ops.Component (fun _ ->
                    if current () = 3 then
                        failwith "row setup"

                    Typed.reactiveText ops (fun () -> string (current ())))))

    equal "12" host.textContent
    graph.Run (fun () -> rows.Value <- [ 1; 2; 3 ])
    mounted.Flush ()

    equal
        ("failed: row setup", [ "content"; "content cleanup"; "error row setup" ], [ [ "row setup" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()

let private armSetupFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let selected = graph.Run (fun () -> Api.createSignal 0)
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Branch.select ops id (fun () -> selected.Value) (fun current ->
                ops.Component (fun _ ->
                    if current () = 1 then
                        failwith "arm setup"

                    ops.Text "arm")))

    equal "arm" host.textContent
    graph.Run (fun () -> selected.Value <- 1)
    mounted.Flush ()

    equal
        ("failed: arm setup", [ "content"; "content cleanup"; "error arm setup" ], [ [ "arm setup" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()

/// <summary>Two setters under one boundary fail in the same queued flush.</summary>
let private twoQueuedFailures (ops: Operations<'view>) (run: 'view -> Backend -> unit) =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createSignal "ok")
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let failing (message: string) =
        Typed.reactiveProperty
            (fun (node: HTMLDivElement) (value: string) ->
                if value = "bad" then
                    failwith message

                node.title <- value)
            (fun () -> source.Value)

    let mounted =
        underBoundary ops run true graph host log reports (fun () -> Typed.div ops [| failing "first"; failing "second" |] [||])

    graph.Run (fun () -> source.Value <- "bad")
    mounted.Flush ()

    equal
        ("failed: first", [ "content"; "content cleanup"; "error first" ], [ [ "first"; "second" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()

/// <summary>A content whose setup fails while <c>broken</c>; the error view's button retries it.</summary>
let private retry (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let mutable broken = true
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string> ()))
    let setups = ResizeArray<string>()
    let reports = ResizeArray<exn list>()
    let host = host ()

    let view =
        ops.Boundary (
            ops.Text "loading",
            (fun failure ->
                Typed.button
                    ops
                    [| Typed.listener "click" (fun _ -> failure.Retry ()) |]
                    [| ops.Text ("retry " + failure.Error.Message) |]),
            fun _ ->
                ops.Component (fun _ ->
                    setups.Add "content"

                    if broken then
                        failwith "setup"

                    Typed.reactiveText ops (fun () -> flight.Value.Value))
        )

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    let click () =
        (host.querySelector "button" |> unbox<HTMLButtonElement>).click ()

    equal ("retry setup", 1) (host.textContent, setups.Count)
    click ()
    equal ("retry setup", 2, 2) (host.textContent, setups.Count, reports.Count)
    broken <- false
    click ()
    equal ("loading", 3) (host.textContent, setups.Count)
    graph.Run (fun () -> flight.Value.Settle "ready")
    mounted.Flush ()
    equal ("ready", 2) (host.textContent, reports.Count)
    mounted.Dispose ()

/// <summary>An inner boundary's pending view fails; the outer boundary shows its error view.</summary>
let private pendingViewFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string> ())
    let reports = ResizeArray<exn list>()
    let host = host ()

    let failed prefix (failure: BoundaryFailure) =
        ops.Text (prefix + failure.Error.Message)

    let inner =
        ops.Boundary (
            ops.Component (fun _ -> failwith "pending view"),
            failed "inner: ",
            fun _ -> Typed.reactiveText ops (fun () -> source.Value)
        )

    let view =
        ops.Boundary (ops.Text "outer loading", failed "outer: ", (fun _ -> inner))

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    equal ("outer: pending view", [ [ "pending view" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

/// <summary>An inner boundary's content and error view both fail; the outer boundary shows its error view.</summary>
let private errorViewFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let reports = ResizeArray<exn list>()
    let host = host ()

    let failed prefix (failure: BoundaryFailure) =
        ops.Text (prefix + failure.Error.Message)

    let inner =
        ops.Boundary (
            ops.Text "inner loading",
            (fun _ -> ops.Component (fun _ -> failwith "error view")),
            fun _ -> ops.Component (fun _ -> failwith "content")
        )

    let view =
        ops.Boundary (ops.Text "outer loading", failed "outer: ", (fun _ -> inner))

    let mounted =
        mountWith (modeOf queued) graph None reports.Add host None (fun context -> run view context.Backend)

    equal ("outer: error view", [ [ "content" ]; [ "error view" ] ]) (host.textContent, messages reports)
    mounted.Dispose ()
```

Append to `cases`:

```fsharp
        "tree synchronous: a failed reader under a boundary shows the error view, cleans up once and reports once",
        fun () -> readerFailure Tree.ops Tree.run false
        "program queued: a failed reader under a boundary shows the error view, cleans up once and reports once",
        fun () -> readerFailure Program.ops Program.run true
        "tree synchronous: a live setter failure under a boundary replaces the content with the error view",
        fun () -> setterFailure Tree.ops Tree.run false
        "program synchronous: a live setter failure under a boundary replaces the content with the error view",
        fun () -> setterFailure Program.ops Program.run false
        "tree queued: a queued setter failure under a boundary replaces the content after the flush",
        fun () -> setterFailure Tree.ops Tree.run true
        "program queued: a queued setter failure under a boundary replaces the content after the flush",
        fun () -> setterFailure Program.ops Program.run true
        "tree queued: a row setup failure under a boundary shows the error view", fun () -> rowSetupFailure Tree.ops Tree.run true
        "program synchronous: a row setup failure under a boundary shows the error view",
        fun () -> rowSetupFailure Program.ops Program.run false
        "tree synchronous: an arm setup failure under a boundary shows the error view", fun () -> armSetupFailure Tree.ops Tree.run false
        "program queued: an arm setup failure under a boundary shows the error view", fun () -> armSetupFailure Program.ops Program.run true
        "tree: two setters failing in one queued flush tear the content down once", fun () -> twoQueuedFailures Tree.ops Tree.run
        "program: two setters failing in one queued flush tear the content down once", fun () -> twoQueuedFailures Program.ops Program.run
        "tree synchronous: retry remounts the content with a new setup and pending phase", fun () -> retry Tree.ops Tree.run false
        "program queued: retry remounts the content with a new setup and pending phase", fun () -> retry Program.ops Program.run true
        "tree queued: a failing pending view goes to the enclosing boundary", fun () -> pendingViewFailure Tree.ops Tree.run true
        "program synchronous: a failing pending view goes to the enclosing boundary",
        fun () -> pendingViewFailure Program.ops Program.run false
        "tree synchronous: a failing error view goes to the enclosing boundary", fun () -> errorViewFailure Tree.ops Tree.run false
        "program queued: a failing error view goes to the enclosing boundary", fun () -> errorViewFailure Program.ops Program.run true
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: FAIL. `readerFailure` fails with `Expected ("failed: reader", ...); got ("ok", ["content"], [["reader"]])`: the failure is reported through the mount and the content stays. `retry` fails because the content's setup failure escapes `mountWith`.

- [ ] **Step 3: Add `Mounted.Abandon`**

In `Mounted`, after `member _.Dispose()`, add:

```fsharp
    /// <summary>Tears the mount down; inside a teardown window serving its reporter, its errors join that window.</summary>
    member internal _.Abandon() =
        state.Root.Abandon ()
```

- [ ] **Step 4: Give `Boundary` its failure path**

In `Boundary`:

1. Update the type's doc comment summary to:

```fsharp
/// <summary>
/// A boundary between region markers: its content, mounted detached until no binding under it waits for a pending read;
/// its pending view, shown between the markers meanwhile; and its error view, which replaces both after a failure under
/// the content.
/// </summary>
/// <remarks>
/// The pending and error views' failures go to <c>enclosing</c>; while <c>unwinds</c> is true they raise instead. The
/// reveal and the teardown run in the boundary's effect, after the binding, handler or pass that requested them.
/// </remarks>
```

2. Add these fields after `let mutable revealed = false`:

```fsharp
    let mutable failed = false
    // Advances on each failure; a retry of an earlier failure is ignored.
    let mutable generation = 0
    // Failures received while the content sets up.
    let held = ResizeArray<exn>()
    // Failures received after setup, waiting for the boundary's effect.
    let mutable received: exn list = []
```

3. Add after `let noItem () : obj = null`:

```fsharp
    let reportAll (errors: exn list) =
        if not errors.IsEmpty then
            try
                Api.untrack (fun () -> report errors)
            with reporterError ->
                raise (AggregateException ("Boundary failure and reporter failed", List.toArray (errors @ [ reporterError ])))
```

4. Replace `Reveal`, `Settle` and `MountContent` with:

```fsharp
    member private this.Reveal() =
        revealed <- true

        try
            retire ()
            place content
        with error ->
            this.TearDown [ error ]

    /// <summary>
    /// Disposes the content and the pending view in one teardown window, mounts the error view, and reports
    /// <c>errors</c> followed by the teardown's errors.
    /// </summary>
    member private this.TearDown(errors: exn list) =
        failed <- true
        generation <- generation + 1
        let current = generation

        let teardown, raised =
            MountLifetime.Capture (
                graph,
                report,
                fun () ->
                    if not (isNull content) then
                        content.Mount.Abandon ()

                    if not (isNull shown) then
                        shown.Mount.Abandon ()
            )

        content <- null
        shown <- null
        site.LaidOut.Clear ()
        waiting <- 0
        revealed <- false

        let failure =
            {
                Error = List.head errors
                Retry = fun () -> this.Retry current
            }

        let mutable unwound = None

        try
            this.Show (views.Failed failure)
        with error ->
            unwound <- Some error

        reportAll (errors @ teardown @ Option.toList raised)
        unwound |> Option.iter raise

    member private this.Retry(expected: int) =
        if failed && generation = expected && site.Live () then
            graph.Run (fun () ->
                retire ()
                failed <- false
                this.MountContent ())

    member private this.Settle() =
        if not (List.isEmpty received) then
            let errors = received
            received <- []

            if not failed && site.Live () then
                this.TearDown errors
            else
                reportAll errors
        elif
            not revealed
            && not failed
            && not settingUp
            && waiting = 0
            && not (isNull content)
            && site.Live ()
        then
            this.Reveal ()

    member private this.MountContent() =
        settingUp <- true
        waiting <- 0
        held.Clear ()
        let mutable failure = None

        try
            try
                content <- Api.runWithOwner site.Owner (fun () -> mountView noItem (fun _ backend -> views.Content status backend) (this :> ViewScope))
            with error ->
                failure <- Some error
        finally
            settingUp <- false

        match failure with
        | Some error -> this.TearDown (error :: List.ofSeq held)
        | None when held.Count > 0 -> this.TearDown (List.ofSeq held)
        | None when waiting = 0 -> this.Reveal ()
        | None -> this.Show views.Pending
```

5. Replace the interface's `Fail` member with:

```fsharp
        member _.Fail(errors) =
            let errors =
                errors
                |> List.filter (fun error ->
                    match error with
                    | NotReadyException _ -> false
                    | _ -> true)

            if not errors.IsEmpty then
                if settingUp then
                    held.AddRange errors
                elif failed || not (site.Live ()) then
                    reportAll errors
                else
                    received <- received @ errors
                    graph.Run (fun () -> Api.untrack (fun () -> wake.Value <- wake.Value + 1))
```

- [ ] **Step 5: Send region reports under a boundary to the boundary**

Replace `MountState.Site` with:

```fsharp
    /// <summary>The site of a region between <c>regionStart</c> and <c>regionEnd</c>, placed in the mount's range.</summary>
    /// <remarks>Under a boundary the region's reports go to the boundary.</remarks>
    member private this.Site(regionStart: Node, regionEnd: Node, placement: RegionPlacement) : RegionSite =
        let regionReport =
            if isNull (box boundary) then
                report
            else
                fun errors -> boundary.Fail errors

        let site = RegionSite (this, regionStart, regionEnd, regionReport, releaseNodes)

        this.PlaceRegion (regionStart, regionEnd, placement, (fun () -> site.Validate ()), (fun () -> Row.Relocate site.LaidOut))

        site
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs tests/binding.test.mjs tests/template.test.mjs tests/range.test.mjs tests/delegation.test.mjs`
Expected: PASS, all tests.

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): replace a failed boundary's content with its error view and retry it on request

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 6: Event handler exceptions under a boundary (authored views)

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`MountState.Listen`)
- Test: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`

**Interfaces:**
- Consumes: `underBoundary`, `messages` (Task 5 tests); `MountState.Scope` (Task 4).
- Produces: a handler attached through `Backend.Listen` under a boundary sends its exception to the boundary; outside every boundary the handler is unchanged.

- [ ] **Step 1: Write the handler tests**

In `BoundaryChecks.fs`, insert after `errorViewFailure`:

```fsharp
[<Emit("$0.dispatchEvent(new FocusEvent('focus'))")>]
let private dispatchFocus (node: HTMLElement) : unit = jsNative

/// <summary>A button whose <c>eventName</c> handler throws, under a boundary; click is delegated, focus is direct.</summary>
let private handlerFailure (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued (eventName: string) =
    use graph = new Graph ()
    let host, log, reports = host (), ResizeArray<string>(), ResizeArray<exn list>()

    let mounted =
        underBoundary ops run queued graph host log reports (fun () ->
            Typed.button ops [| Typed.listener eventName (fun _ -> failwith "handler") |] [| ops.Text "press" |])

    let button = host.querySelector "button" |> unbox<HTMLButtonElement>

    if eventName = "focus" then
        dispatchFocus button
    else
        button.click ()

    equal
        ("failed: handler", [ "content"; "content cleanup"; "error handler" ], [ [ "handler" ] ])
        (host.textContent, List.ofSeq log, messages reports)

    mounted.Dispose ()
```

Append to `cases`:

```fsharp
        "tree synchronous: a throwing delegated handler under a boundary shows the error view",
        fun () -> handlerFailure Tree.ops Tree.run false "click"
        "program queued: a throwing delegated handler under a boundary shows the error view",
        fun () -> handlerFailure Program.ops Program.run true "click"
        "tree queued: a throwing direct handler under a boundary shows the error view",
        fun () -> handlerFailure Tree.ops Tree.run true "focus"
        "program synchronous: a throwing direct handler under a boundary shows the error view",
        fun () -> handlerFailure Program.ops Program.run false "focus"
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: FAIL with `Expected ("failed: handler", ...); got ("press", ["content"], [])` for each of the four cases.

- [ ] **Step 3: Wrap handlers under a boundary**

Replace `MountState.Listen` with:

```fsharp
    override this.Listen(eventName, handler) =
        let node = currentElement ()
        let scope = this.Scope

        let invoke =
            if scope.Tracks then
                let owned = Typed.ownedHandler handler

                fun event ->
                    try
                        owned event
                    with error ->
                        scope.Fail [ error ]
            else
                Typed.ownedHandler handler

        if isDelegated delegatedEvents eventName then
            setDelegatedHandler node eventName invoke
            registerDelegated eventName
        else
            listenDirect node eventName invoke
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs tests/delegation.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): send exceptions of handlers under a boundary to the boundary

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 7: Keep revealed content on later pending reads and expose `IsPending`

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`Boundary`)
- Test: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`

**Interfaces:**
- Consumes: `Boundary` (Tasks 4–5).
- Produces: `BoundaryStatus.IsPending` reads a core signal that is true after the first reveal while the waiting count is above zero. A pending read after the reveal leaves the content and its last values on screen (Decision 1a).

- [ ] **Step 1: Write the test**

In `BoundaryChecks.fs`, insert after `handlerFailure`:

```fsharp
/// <summary>Refetches a revealed boundary's source: the content stays with its last value and reads IsPending.</summary>
let private pendingAfterReveal (ops: Operations<'view>) (run: 'view -> Backend -> unit) queued =
    use graph = new Graph ()
    let flight = graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string> ()))
    let log = ResizeArray<string>()
    let host = host ()

    let view =
        ops.Boundary (
            ops.Component (fun _ ->
                log.Add "pending"
                ops.Text "loading"),
            (fun failure -> ops.Text failure.Error.Message),
            fun status ->
                ops.Fragment
                    [|
                        Typed.reactiveText ops (fun () -> flight.Value.Value)
                        Typed.reactiveText ops (fun () -> if status.IsPending () then "*" else ".")
                    |]
        )

    let mounted =
        mountWith (modeOf queued) graph None strict host None (fun context -> run view context.Backend)

    equal "loading" host.textContent
    graph.Run (fun () -> flight.Value.Settle "one")
    mounted.Flush ()
    equal "one." host.textContent
    graph.Run (fun () -> flight.Value <- Api.createAsyncSource<string> ())
    mounted.Flush ()
    equal ("one*", 1) (host.textContent, log.Count)
    graph.Run (fun () -> flight.Value.Settle "two")
    mounted.Flush ()
    equal ("two.", 1) (host.textContent, log.Count)
    mounted.Dispose ()
```

Append to `cases`:

```fsharp
        "tree synchronous: a pending read after the reveal keeps the content and sets IsPending",
        fun () -> pendingAfterReveal Tree.ops Tree.run false
        "tree queued: a pending read after the reveal keeps the content and sets IsPending",
        fun () -> pendingAfterReveal Tree.ops Tree.run true
        "program synchronous: a pending read after the reveal keeps the content and sets IsPending",
        fun () -> pendingAfterReveal Program.ops Program.run false
        "program queued: a pending read after the reveal keeps the content and sets IsPending",
        fun () -> pendingAfterReveal Program.ops Program.run true
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: FAIL with `Expected ("one*", 1); got ("one.", 1)`.

- [ ] **Step 3: Back `IsPending` with a signal**

In `Boundary`:

1. Add the field `let mutable pending: Signal<bool> = Unchecked.defaultof<_>` after `let mutable wake`.
2. In `TearDown`, after `revealed <- false`, add `pending.Value <- false`.
3. In `Start`, replace `status <- { IsPending = fun () -> false }` with:

```fsharp
        pending <- Api.createSignal false
        status <- { IsPending = fun () -> pending.Value }
```

4. Replace the interface's `Wait` and `Ready` members with:

```fsharp
        member _.Wait() =
            waiting <- waiting + 1

            if revealed then
                pending.Value <- true

        member _.Ready() =
            if waiting > 0 then
                waiting <- waiting - 1

                if waiting = 0 then
                    if revealed then
                        pending.Value <- false
                    else
                        Api.untrack (fun () -> wake.Value <- wake.Value + 1)
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): keep revealed boundary content on later pending reads and expose IsPending

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 8: Template boundaries

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Template.fs` (`RegionRow.BoundaryRow`, `rowRenderer`, `rowValue`, `writeNode`'s server region, `expandNode`, `expandRow`, `TemplateBuilder.boundary`)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`compile`'s kind, `MountServices.Scope`, two emits, `dispatchFor`, `startRender`, `startIsolated`, `listenDirect`, `bind`, `materialise`'s `rows`)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`RowBuilder` and `MountState` `MountServices.Scope`)
- Test: `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs`

**Interfaces:**
- Consumes: `BoundaryViews`, `Backend.Boundary` (Task 3), `RegionKind.BoundaryContent`, `MountState.BoundaryRegion` (Task 4), `BindingState.Waiting` (Task 4).
- Produces:
  - `TemplateBuilder.boundary (pending: Component<unit>) (failed: Component<BoundaryFailure>) (content: Component<BoundaryStatus>) : TemplateView<'inst>`.
  - `RegionRow.BoundaryRow`: the region's single item's value is a `BoundaryViews`.
  - `MountServices.Scope: ViewScope`.

- [ ] **Step 1: Write the template tests**

In `BoundaryChecks.fs`, add `open Ranvier.View.Foundation.Template` after `open Ranvier.View.Foundation.Mounting`, and insert after `pendingAfterReveal`:

```fsharp
let private templateLog = ResizeArray<string>()

let mutable private templateFlight: Signal<AsyncSource<string>> =
    Unchecked.defaultof<_>

let private templatePending =
    Component.define
        (fun setup () ->
            templateLog.Add "pending"
            setup.OnCleanup (fun () -> templateLog.Add "pending cleanup"))
        (fun t -> t.span [||] [| t.text "loading" |])

let private templateFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.button
            [| t.Events.onClick (fun failure _ -> failure.Retry ()) |]
            [| t.text (fun failure -> "failed: " + failure.Error.Message) |])

let private templateRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun t ->
        t.span [||] [| t.text (fun item -> templateFlight.Value.Value + string (item ())) |])

let private templateArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text (fun () -> "|" + templateFlight.Value.Value) |])

let private templateContent =
    Component.define
        (fun setup (status: BoundaryStatus) ->
            templateLog.Add "content"
            setup.OnCleanup (fun () -> templateLog.Add "content cleanup")
            status)
        (fun t ->
            t.div
                [||]
                [|
                    t.text (fun _ -> templateFlight.Value.Value)
                    t.each id (fun _ -> [| 1; 2 |]) templateRow
                    t.branch (fun _ -> 0) (fun _ -> t.arm templateArm ())
                    t.text (fun status -> if status.IsPending () then "*" else ".")
                |])

let private guarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed templateContent |])

/// <summary>A template boundary reveals rows and arms in one insertion, then keeps them through a refetch.</summary>
let private templateReveal () =
    use graph = new Graph ()
    templateLog.Clear ()
    templateFlight <- graph.Run (fun () -> Api.createSignal (Api.createAsyncSource<string> ()))
    let host = host ()

    let mounted =
        Mounting.mount graph None strict host None (fun context -> Program.run (Component.render guarded ()) context.Backend)

    equal ("loading", [ "content"; "pending" ]) (host.textContent, List.ofSeq templateLog)
    let outer = host.querySelector "div" |> unbox<HTMLElement>
    let inserts = countInserts outer
    graph.Run (fun () -> templateFlight.Value.Settle "A")
    equal ("AA1A2|A.", 1) (host.textContent, inserts ())
    equal [ "content"; "pending"; "pending cleanup" ] (List.ofSeq templateLog)
    graph.Run (fun () -> templateFlight.Value <- Api.createAsyncSource<string> ())
    equal "AA1A2|A*" host.textContent
    graph.Run (fun () -> templateFlight.Value.Settle "B")
    equal "BB1B2|B." host.textContent
    mounted.Dispose ()
    equal ("content cleanup", "") (templateLog[templateLog.Count - 1], host.textContent)

type private Fragile = { Value: Signal<string> }

let mutable private fragileValue: Signal<string> = Unchecked.defaultof<_>
let mutable private fragileBroken = false

let private fragileContent =
    Component.define
        (fun _ (_: BoundaryStatus) ->
            if fragileBroken then
                failwith "setup"

            { Value = fragileValue })
        (fun t ->
            t.button
                [|
                    t.Props.propertyWith (
                        (fun (_: HTMLButtonElement) (value: string) ->
                            if value = "bad" then
                                failwith "setter"),
                        (fun fragile -> fragile.Value.Value),
                        primitiveSame
                    )
                    t.Events.onClick (fun _ _ -> failwith "handler")
                    t.Events.onFocus (fun _ _ -> failwith "focus handler")
                |]
                [| t.text (fun fragile -> fragile.Value.Value) |])

let private fragile =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary templatePending templateFailed fragileContent |])

/// <summary>Mounts the fragile template boundary, runs <c>trigger</c>, and expects the error view for <c>message</c>.</summary>
let private templateFailure (trigger: Graph -> HTMLElement -> unit) (message: string) () =
    use graph = new Graph ()
    fragileBroken <- false
    fragileValue <- graph.Run (fun () -> Api.createSignal "ok")
    let host = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run (Component.render fragile ()) context.Backend)

    equal "ok" host.textContent
    trigger graph host
    equal ("failed: " + message, [ [ message ] ]) (host.textContent, messages reports)
    mounted.Dispose ()

let private button (host: HTMLElement) =
    host.querySelector "button" |> unbox<HTMLButtonElement>

/// <summary>A template content whose setup fails while broken; the error view's button retries it.</summary>
let private templateRetry () =
    use graph = new Graph ()
    fragileBroken <- true
    fragileValue <- graph.Run (fun () -> Api.createSignal "ok")
    let host = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        Mounting.mount graph None reports.Add host None (fun context -> Program.run (Component.render fragile ()) context.Backend)

    equal "failed: setup" host.textContent
    (button host).click ()
    equal ("failed: setup", 2) (host.textContent, reports.Count)
    fragileBroken <- false
    (button host).click ()
    equal ("ok", 2) (host.textContent, reports.Count)
    mounted.Dispose ()

let mutable private serverSource: AsyncSource<string> = Unchecked.defaultof<_>

let private serverPending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let private serverFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t ->
        t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let private serverContent =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.span [||] [| t.text (fun _ -> serverSource.Value) |])

let private guardedServer =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary serverPending serverFailed serverContent |])

/// <summary>Renders the template boundary as HTML while its source is pending, settled and failed.</summary>
let private templateServer () =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            serverSource <- Api.createAsyncSource<string> ()

            let render () =
                Html.render Omit (Program.run (Component.render guardedServer ()))

            equal "<div><!--[--><span>loading</span><!--]--></div>" (render ())
            serverSource.Settle "<A>"
            equal "<div><!--[--><span>&lt;A&gt;</span><!--]--></div>" (render ())
            serverSource.Fail (Exception "<bad>")
            equal "<div><!--[--><span>&lt;bad&gt;</span><!--]--></div>" (render ())))
```

Append to `cases`:

```fsharp
        "template: a boundary reveals rows and arms in one insertion and keeps them through a refetch", templateReveal
        "template: a live setter failure under a boundary shows the error view",
        templateFailure (fun graph _ -> graph.Run (fun () -> fragileValue.Value <- "bad")) "setter"
        "template: a throwing delegated handler under a boundary shows the error view",
        templateFailure (fun _ host -> (button host).click ()) "handler"
        "template: a throwing direct handler under a boundary shows the error view",
        templateFailure (fun _ host -> dispatchFocus (button host)) "focus handler"
        "template: retry remounts a content whose setup failed", templateRetry
        "template HTML renders a boundary's pending, content or error view between markers", templateServer
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs`
Expected: the compile fails: `The field, constructor or member 'boundary' is not defined`.

- [ ] **Step 3: Add the template boundary region**

In `Template.fs`:

1. Add a case to `RegionRow`:

```fsharp
    /// <summary>The item is the region's <c>BoundaryViews</c>, read once per instance.</summary>
    | BoundaryRow
```

2. In `rowRenderer`, add:

```fsharp
    | BoundaryRow -> backend.Boundary (unbox<BoundaryViews>(current ()))
```

3. Change `rowValue`'s second arm to `| ViewRow | ArmRow | BoundaryRow -> KeyedItems.value item`.

4. In `writeNode`'s `RegionNode` arm, replace the `Server (_, _, backend)` branch with:

```fsharp
            | Server (_, _, backend) ->
                let items = Array.copy (region.Items.Invoke state.Instance)
                Projection.ensureUniqueKeys region.Key.Invoke items

                match region.Row with
                | BoundaryRow -> serverRow backend region.Row (items[0])
                | _ ->
                    append "<!--[-->"

                    for item in items do
                        serverRow backend region.Row item

                    append "<!--]-->"
```

5. In `expandNode`'s `RegionNode` arm, change `| ViewRow ->` to `| ViewRow | BoundaryRow ->` (the body is unchanged). In `expandRow`, change `| ViewRow -> ops.Embed (rowRenderer row current)` to:

```fsharp
    | ViewRow
    | BoundaryRow -> ops.Embed (rowRenderer row current)
```

6. In `TemplateBuilder`, after `member _.view`, add:

```fsharp
    /// <summary>
    /// A boundary between region markers. <c>content</c> receives the boundary's status and is shown once every binding
    /// under it has read a value; <c>pending</c> is shown until then, and <c>failed</c> replaces either after a failure
    /// under the content.
    /// </summary>
    member _.boundary (pending: Component<unit>) (failed: Component<BoundaryFailure>) (content: Component<BoundaryStatus>) : TemplateView<'inst> =
        let views =
            {
                Pending = fun backend -> backend.Template (pending, box ())
                Failed = fun failure backend -> backend.Template (failed, box failure)
                Content = fun status backend -> backend.Template (content, box status)
            }

        TemplateView (
            RegionNode
                {
                    Items = Func<obj, obj array>(fun _ -> [| box ((null: obj), box views) |])
                    Key = KeyedItems.pairKey
                    Row = BoundaryRow
                }
        )
```

In `TemplateDom.fs`:

1. In `compile`, add `| BoundaryRow -> BoundaryContent` to the `Kind` match, and in `materialise` change the `rows` match's last arm to `| ViewRow | BoundaryRow -> null`.

2. Add `abstract Scope: ViewScope` to `MountServices`, after `abstract ClearFailure: unit -> unit`.

3. Immediately before `let private dispatchFor`, add:

```fsharp
/// <summary>Marks an event node of a template instance under a boundary with the boundary.</summary>
[<Emit("$0.$$ranvierScope = $1")>]
let private attachScope (node: obj) (scope: ViewScope) : unit = jsNative

/// <summary>The boundary of an event node, or null outside every boundary.</summary>
[<Emit("$0.$$ranvierScope")>]
let private scopeOf (node: Node) : ViewScope = jsNative
```

4. In `dispatchFor`, replace the handler invocation with:

```fsharp
                        if target = index && eventName = eventType then
                            graph.Run (fun () ->
                                Api.runWithOwner owner (fun () ->
                                    Api.untrack (fun () ->
                                        try
                                            handler.Invoke (state[StateInstance], event)
                                        with error ->
                                            let scope = scopeOf node

                                            if isNull (box scope) then
                                                reraise ()

                                            scope.Fail [ error ])))
```

5. Replace `startRender` with:

```fsharp
/// <summary>Starts the effect that reads every render slot of a part and commits them together.</summary>
let private startRender (services: MountServices) (state: obj array) =
    let mutable flags = 0

    if services.Scope.Tracks then
        Api.onCleanup (fun () ->
            if flags &&& BindingState.Waiting <> 0 then
                flags <- flags &&& ~~~BindingState.Waiting
                services.Scope.Ready ())

    Api.createEffect (fun () ->
        let compiled = at state StateCompiled :?> Compiled
        let instance = at state StateInstance
        let renderCount = compiled.RenderSlots.Length
        let previousBase = StateNodes + compiled.NodeCount

        let pendingBase =
            previousBase
            + renderCount
            + compiled.IsolatedSlots.Length

        let mutable failure: exn = null
        let mutable waiting = false

        for k in 0 .. renderCount - 1 do
            let _, slot = at compiled.RenderSlots k

            let value =
                try
                    slot.Read.Invoke instance
                with
                | NotReadyException _ ->
                    waiting <- true
                    unset
                | error ->
                    flags <- flags ||| BindingState.ReadFailed
                    services.ReportFailure error

                    if isNull failure then
                        failure <- error

                    unset

            store state (pendingBase + k) value

        Api.untrack (fun () ->
            for k in 0 .. renderCount - 1 do
                let nodeIndex, slot = at compiled.RenderSlots k
                commit services state slot nodeIndex (previousBase + k) (at state (pendingBase + k)))

        if waiting then
            if
                flags &&& BindingState.Waiting = 0
                && services.Scope.Tracks
            then
                flags <- flags ||| BindingState.Waiting
                services.Scope.Wait ()
        elif flags &&& BindingState.Waiting <> 0 then
            flags <- flags &&& ~~~BindingState.Waiting
            services.Scope.Ready ()

        if not (isNull failure) then
            flags <- flags ||| BindingState.ReadFailed
            raise failure
        elif flags &&& BindingState.ReadFailed <> 0 then
            flags <- flags &&& ~~~BindingState.ReadFailed
            services.ClearFailure ())

    services.RaiseSetupFailure ()
```

6. Replace `startIsolated` with:

```fsharp
/// <summary>Starts the effect of the isolated slot at index <c>k</c> of a part.</summary>
let private startIsolated (services: MountServices) (state: obj array) (k: int) =
    let mutable flags = 0

    if services.Scope.Tracks then
        Api.onCleanup (fun () ->
            if flags &&& BindingState.Waiting <> 0 then
                flags <- flags &&& ~~~BindingState.Waiting
                services.Scope.Ready ())

    Api.createEffect (fun () ->
        let compiled = at state StateCompiled :?> Compiled
        let nodeIndex, slot = at compiled.IsolatedSlots k

        let previousIndex =
            StateNodes
            + compiled.NodeCount
            + compiled.RenderSlots.Length
            + k

        let mutable failure: exn = null
        let mutable waiting = false

        let value =
            try
                slot.Read.Invoke (at state StateInstance)
            with
            | NotReadyException _ ->
                waiting <- true
                unset
            | error ->
                flags <- flags ||| BindingState.ReadFailed
                services.ReportFailure error
                failure <- error
                unset

        Api.untrack (fun () -> commit services state slot nodeIndex previousIndex value)

        if waiting then
            if
                flags &&& BindingState.Waiting = 0
                && services.Scope.Tracks
            then
                flags <- flags ||| BindingState.Waiting
                services.Scope.Wait ()
        elif flags &&& BindingState.Waiting <> 0 then
            flags <- flags &&& ~~~BindingState.Waiting
            services.Scope.Ready ()

        if not (isNull failure) then
            flags <- flags ||| BindingState.ReadFailed
            raise failure
        elif flags &&& BindingState.ReadFailed <> 0 then
            flags <- flags &&& ~~~BindingState.ReadFailed
            services.ClearFailure ())

    services.RaiseSetupFailure ()
```

7. Replace `listenDirect`'s callback with:

```fsharp
        fun event ->
            if not owner.IsDisposed then
                try
                    graph.Run (fun () -> Api.runWithOwner owner (fun () -> Api.untrack (fun () -> handler.Invoke (instance, event))))
                with error ->
                    let scope = services.Scope

                    if not scope.Tracks then
                        reraise ()

                    scope.Fail [ error ]
```

8. In `bind`, replace the delegated branch's `attachState` block with:

```fsharp
            if attached <> nodeIndex then
                attachState node state

                if services.Scope.Tracks then
                    attachScope node services.Scope

                attached <- nodeIndex
```

In `Mounting.fs`, add `member _.Scope = state.Scope` to `RowBuilder`'s `MountServices` implementation, and `member this.Scope = this.Scope` to `MountState`'s.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/boundary.test.mjs tests/template.test.mjs tests/template-catalogue.test.mjs tests/delegation.test.mjs tests/range.test.mjs`
Expected: PASS, all tests.

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj` (from the worktree root)
Expected: exit 0.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Foundation/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): add template boundaries with pending registration and handler failures

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 9: Mutation checks

**Files:**
- No committed source change. Each mutant is applied, tested and reverted.
- Create: `docs/research/2026-10-19-view-boundaries.md` (mutation section; Task 10 adds the measurements)

**Interfaces:**
- Consumes: every task above.

For each mutant: apply the edit, run `npm run test:dom` from `examples/Fable.Ranvier.Playground`, confirm that the named test fails, then restore the file with `git checkout -- <file>`. A mutant that survives is a missing test: add the test to the owning task's test file, commit it as `test(view): ...`, and rerun.

- [ ] **Step 1: Waiting registration (authored)** — in `Mounting.fs` `startBinding`'s synchronous branch, delete the line `scope.Wait ()`. Expected failure: `tree synchronous: a boundary reveals its content in one insertion ...`, with `Expected ("loading", ["content"; "pending"])`: the content reveals at setup, empty.
- [ ] **Step 2: Waiting registration (template)** — in `TemplateDom.fs` `startRender`, delete `services.Scope.Wait ()`. Expected failure: `template: a boundary reveals rows and arms in one insertion ...`.
- [ ] **Step 3: Reveal count check** — in `Boundary.Settle`, delete the line `&& waiting = 0`. Expected failure: `... a boundary reveals its content in one insertion once every binding, row and arm has a value` (`Expected "loading"` after the first source settles).
- [ ] **Step 4: Unregistration on disposal** — in `startBinding`, delete the `if scope.Tracks then Api.onCleanup (...)` block. Expected failure: `... removing the last pending row reveals the boundary`.
- [ ] **Step 5: Setter catch** — in `startBinding`'s synchronous `commit`, delete the arm `| error -> scope.Fail [ error ]`. Expected failure: `tree: a live synchronous setter failure is reported ...` and `tree synchronous: a live setter failure under a boundary replaces the content ...`.
- [ ] **Step 6: Queued per-scope report** — in `Queue.Flush`, delete `if failures.Count > 0 then Queue.Report (failures, failedSlots)`. Expected failure: `a flush reports each scope's failures in one call, in commit order`, `tree: a live queued setter failure is reported after the flush ...`, `tree queued: a queued setter failure under a boundary replaces the content after the flush`.
- [ ] **Step 7: Authored handler catch** — in `MountState.Listen`, replace the `invoke` binding with `let invoke = Typed.ownedHandler handler`. Expected failure: `tree synchronous: a throwing delegated handler under a boundary shows the error view`.
- [ ] **Step 8: Template handler catch** — in `TemplateDom.dispatchFor`, replace the `try ... with` with `handler.Invoke (state[StateInstance], event)`. Expected failure: `template: a throwing delegated handler under a boundary shows the error view`.
- [ ] **Step 9: Reveal deferred to the boundary's effect** — in `Boundary`'s `Ready`, replace `Api.untrack (fun () -> wake.Value <- wake.Value + 1)` with `this.Settle ()` (give the member a `this` self-identifier). Expected failure: `... removing the last pending row reveals the boundary` (the list pass fails its validation and the error view appears).
- [ ] **Step 10: Record the results**

Create `docs/research/2026-10-19-view-boundaries.md`:

```markdown
# View boundaries: mutation checks and measurements

Date: 2026-10-19. Branch `poc/fable-ranvier-dom`. Plan: [view boundaries](../superpowers/plans/2026-10-19-view-boundaries.md).

## Mutation checks

| Mutant | Killed by |
|---|---|
| Authored waiting registration removed | <test name from Step 1> |
| Template waiting registration removed | <test name from Step 2> |
| Reveal count check removed | <test name from Step 3> |
| Unregistration on disposal removed | <test name from Step 4> |
| Synchronous setter catch removed | <test names from Step 5> |
| Queued per-scope report removed | <test names from Step 6> |
| Authored handler catch removed | <test name from Step 7> |
| Template handler catch removed | <test name from Step 8> |
| Reveal run synchronously in `Ready` | <test name from Step 9> |
```

Fill each cell with the failing test names `npm run test:dom` printed for that mutant.

- [ ] **Step 11: Confirm the tree is clean and commit**

Run: `git status --short | grep -v generated-keyed` — expected: only `docs/research/2026-10-19-view-boundaries.md` and the files the user's status already listed as untracked.

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
git add docs/research/2026-10-19-view-boundaries.md
git diff --cached --check
git commit -F - <<'EOF'
docs(research): record the view boundary mutation checks

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 10: Boundary reveal benchmark, before/after comparisons and status

**Files:**
- Create: `examples/Fable.Ranvier.Playground/tests/BoundaryPerf.fs`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-solid.jsx`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-page.mjs`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-reveal.mjs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/page.mjs`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/vite.config.mjs`
- Create: `docs/research/framework-performance/investigation/view-boundaries/keyed-after.json`, `template-after.json`, `boundary-reveal.json`
- Modify: `docs/research/2026-10-19-view-boundaries.md`, `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md`

**Interfaces:**
- Consumes: `Program.ops.Boundary` (Task 3), the DOM boundary (Tasks 4–7).
- Produces: `BoundaryPerf.mountReveal (host: HTMLElement) (width: int) : unit -> unit`; `boundary-solid.jsx` `mountReveal(host, width): Promise<() => void>`; `window.boundaryReveal = { sample, verify, retain }`.

- [ ] **Step 1: Add the Ranvier fixture**

Create `examples/Fable.Ranvier.Playground/tests/BoundaryPerf.fs`:

```fsharp
/// <summary>Boundary reveal fixture: rows under one boundary that reveals once one async source settles.</summary>
module BoundaryPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

/// <summary>Mounts <c>width</c> rows reading one pending source under a boundary, settles the source, and returns the disposal.</summary>
let mountReveal (host: HTMLElement) (width: int) : unit -> unit =
    let graph = new Graph ()
    let source = graph.Run (fun () -> Api.createAsyncSource<string> ())

    let row (index: int) =
        Program.element Div [| Attribute ("class", "row") |] [| Program.bind (=) (fun () -> source.Value + string index) |]

    let view =
        Program.ops.Boundary (Program.text "loading", (fun failure -> Program.text failure.Error.Message), (fun _ -> Program.fragment (Array.init width row)))

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context -> Program.run view context.Backend)

    graph.Run (fun () -> source.Settle "ready")

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()
```

In `tests/DomTests.fsproj`, after `<Compile Include="KeyedRowsPerf.fs" />` add `<Compile Include="BoundaryPerf.fs" />`.

- [ ] **Step 2: Add the Solid 2 fixture and the page adapter**

Create `examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-solid.jsx`:

```jsx
import { createMemo, flush, Loading } from "solid-js";
import { render } from "@solidjs/web";

// Rows reading one async value under a Loading boundary; resolves the value and waits for the reveal.
export async function mountReveal(host, width) {
  let resolve;
  const promise = new Promise(settle => { resolve = settle; });
  const dispose = render(() => {
    const value = createMemo(() => promise);
    return <Loading fallback={<span>loading</span>}>
      {Array.from({ length: width }, (_, i) => <div class="row">{value() + i}</div>)}
    </Loading>;
  }, host);
  flush();
  resolve("ready");
  for (let turn = 0; turn < 20 && host.querySelectorAll(".row").length !== width; turn++) {
    await Promise.resolve();
    flush();
  }
  return dispose;
}
```

Create `examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-page.mjs`:

```js
import { mountReveal as ranvierReveal } from "../../generated-tests/BoundaryPerf.js";
import { mountReveal as solidReveal } from "./boundary-solid.jsx";

const adapters = { ranvier: ranvierReveal, solid: solidReveal };
let hostIndex = 0;
const freshHost = () => {
  const host = document.createElement("div");
  host.id = `boundary-${hostIndex++}`;
  document.body.append(host);
  return host;
};

window.boundaryReveal = {
  // Mounts `count` boundaries of `width` rows on fresh hosts, each revealed; returns milliseconds per boundary.
  async sample(mode, width, count) {
    const hosts = Array.from({ length: count }, freshHost), disposals = [];
    const start = performance.now();
    for (const host of hosts) disposals.push(await adapters[mode](host, width));
    const ms = (performance.now() - start) / count;
    for (const dispose of disposals) dispose();
    for (const host of hosts) host.remove();
    return ms;
  },
  async verify(mode, width) {
    const host = freshHost();
    const dispose = await adapters[mode](host, width);
    const rows = Array.from(host.querySelectorAll(".row"), row => row.textContent);
    dispose();
    host.remove();
    const expected = Array.from({ length: width }, (_, i) => `ready${i}`);
    if (JSON.stringify(rows) !== JSON.stringify(expected)) throw Error(`${mode}/${width}: ${JSON.stringify(rows)}`);
    return { mode, width, rows: rows.length };
  },
  async retain(mode, width, count) {
    const hosts = Array.from({ length: count }, freshHost), disposals = [];
    for (const host of hosts) disposals.push(await adapters[mode](host, width));
    return () => { for (const dispose of disposals) dispose(); for (const host of hosts) host.remove(); };
  }
};
```

In `benchmarks/frameworks/page.mjs`, after `import "./keyed-page.mjs";` add `import "./boundary-page.mjs";`.

In `benchmarks/frameworks/vite.config.mjs`, change the Solid file test to:

```js
      if (!/\/frameworks\/(semantics-|keyed-|boundary-)?solid\.jsx$/.test(id)) return;
```

- [ ] **Step 3: Add the runner**

Create `examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-reveal.mjs`:

```js
// Boundary reveal benchmark: rows under one boundary revealed after one async source settles, Ranvier against Solid 2's Loading.
// Usage: node benchmarks/frameworks/boundary-reveal.mjs [--build] [--dist=<dir>] [--label=<name>] [--out=<dir>] [--port=<n>]
import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const argument = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.slice(name.length + 3) ?? fallback;
const output = argument("out") ?? fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/view-boundaries/", import.meta.url));
const label = argument("label", "boundary-reveal");
const outDir = argument("dist") ?? "generated-boundary-dist";
const env = { ...process.env, RanvierCounters: "false", RANVIER_PERF_OUTPUT: outDir };
const port = Number(argument("port", 5188));
const modes = ["ranvier", "solid"], widths = [1, 16, 128], processes = 3, rounds = 15, warmups = 3;
const counts = { 1: 64, 16: 32, 128: 8 };

if (process.argv.includes("--build")) {
  for (const args of [
    ["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"],
    ["dotnet", "fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release", "--noCache"],
    ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"]
  ]) execFileSync("rtk", args, { cwd, stdio: "inherit", windowsHide: true, env });
}

const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let serverOutput = "", browser;
server.stdout.on("data", data => serverOutput += data);
server.stderr.on("data", data => serverOutput += data);
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
const url = `http://127.0.0.1:${port}/benchmarks/frameworks/index.html`;
const samples = [], heaps = [], verified = [];

try {
  let ready = false;
  for (let attempt = 0; attempt < 100 && !ready; attempt++) {
    if (server.exitCode !== null) throw Error(serverOutput);
    try { ready = (await fetch(url)).ok; } catch {}
    if (!ready) await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(serverOutput);
  for (let replicate = 0; replicate < processes; replicate++) {
    browser = await chromium.launch();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(url);
    await page.waitForFunction(() => window.boundaryReveal);
    for (const width of widths) for (const mode of modes)
      verified.push(await page.evaluate(({ mode, width }) => window.boundaryReveal.verify(mode, width), { mode, width }));
    for (const width of widths) {
      samples.push(...await page.evaluate(async ({ modes, width, count, rounds, warmups, replicate }) => {
        const rows = [];
        for (let round = -warmups; round < rounds; round++) for (let offset = 0; offset < modes.length; offset++) {
          const mode = modes[(round + warmups + offset + replicate) % modes.length];
          const ms = await window.boundaryReveal.sample(mode, width, count);
          if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid timing ${mode}/${width}: ${ms}`);
          if (round >= 0) rows.push({ mode, width, replicate, round, count, ms });
        }
        return rows;
      }, { modes, width, count: counts[width], rounds, warmups, replicate }));
      console.log(`Measured width ${width}, process ${replicate + 1}`);
    }
    const cdp = await page.context().newCDPSession(page);
    for (const width of widths) for (const mode of modes) {
      const count = 32;
      await cdp.send("HeapProfiler.collectGarbage");
      const before = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(async ({ mode, width, count }) => { window.releaseBoundaries = await window.boundaryReveal.retain(mode, width, count); }, { mode, width, count });
      await cdp.send("HeapProfiler.collectGarbage");
      const retained = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(() => { window.releaseBoundaries(); delete window.releaseBoundaries; });
      heaps.push({ mode, width, replicate, count, retainedBytesPerRow: (retained.usedSize - before.usedSize) / (count * width) });
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close();
    browser = undefined;
  }
  const summaries = widths.flatMap(width => modes.map(mode => {
    const rows = samples.filter(row => row.width === width && row.mode === mode);
    const byProcessUs = [0, 1, 2].map(replicate => median(rows.filter(row => row.replicate === replicate).map(row => row.ms)) * 1000);
    return { width, mode, medianUs: median(rows.map(row => row.ms)) * 1000, byProcessUs };
  }));
  const result = {
    measuredAt: new Date().toISOString(),
    baseCommit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(),
    node: process.version, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`,
    unit: "milliseconds per boundary",
    notes: [
      "Each sample mounts `count` boundaries of `width` rows on fresh hosts; a boundary reveals after its one async source settles, and the adapter awaits one promise per mount.",
      "Solid 2 resolves a promise read by an async memo under <Loading>; Ranvier settles an async source synchronously.",
      "retainedBytesPerRow is the GC-stabilised retained JavaScript heap of 32 revealed boundaries divided by their rows; it is reported, not gated."
    ],
    verified, samples, summaries, heaps
  };
  await mkdir(output, { recursive: true });
  await writeFile(`${output}/${label}.json`, JSON.stringify(result, null, 2) + "\n");
  console.table(summaries.map(({ width, mode, medianUs, byProcessUs }) => ({ width, mode, medianUs: medianUs.toFixed(1), byProcessUs: byProcessUs.map(value => value.toFixed(1)).join(" / ") })));
  console.table(heaps);
} finally {
  if (browser) await browser.close();
  server.kill();
}
```

- [ ] **Step 4: Verify the benchmark adapters**

Run (Playground directory): `npm run test:dom` — expected PASS (the fixture compiles with the tests).

Then, behind the lock:

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/boundary-reveal.mjs --build
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Expected: `verified` holds six entries; `view-boundaries/boundary-reveal.json` exists. If `verify` throws for `solid`, Solid 2's async memo API differs from this fixture: stop and report the error text rather than changing the measured shape.

- [ ] **Step 5: Measure keyed rows and the template comparison after the change**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/keyed-rows.mjs --build --dist=generated-keyed-bnd1 --label=keyed-after --out=C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/docs/research/framework-performance/investigation/view-boundaries
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/run.mjs --build --template-comparison
cp ../../docs/research/framework-performance/investigation/template-framework-results.json ../../docs/research/framework-performance/investigation/view-boundaries/template-after.json
git -C ../.. checkout -- docs/research/framework-performance/investigation/template-framework-results.json
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Compare `keyed-before.json` with `keyed-after.json` and `template-before.json` with `template-after.json`, for the Ranvier modes: for every operation (and every width and phase of the template comparison), the per-process ranges (`byProcessUs`, minimum to maximum) before and after must overlap. If any range is disjoint and slower after, stop: report the operation, both ranges and the commit, and do not continue to Step 6.

- [ ] **Step 6: Record the measurements and the status**

Append to `docs/research/2026-10-19-view-boundaries.md`:

```markdown
## Measurements

Results: [`framework-performance/investigation/view-boundaries/`](framework-performance/investigation/view-boundaries/).

### Operations without a boundary

| Benchmark | Operation | Before (µs, per-process range) | After (µs, per-process range) | Ranges overlap |
|---|---|---|---|---|
| keyed-rows | <one row per operation in keyed-before.json, ranvier mode> | | | |
| template comparison | <one row per width and phase, ranvier and ranvier-template modes> | | | |

### Boundary reveal

| Width | Ranvier median (µs) | Solid 2 median (µs) | Ranvier retained bytes per row | Solid 2 retained bytes per row |
|---|---|---|---|---|
| 1 | | | | |
| 16 | | | | |
| 128 | | | | |
```

Fill every cell from the JSON files: medians from `summaries`, ranges from `byProcessUs`, retained bytes from the median of `heaps[].retainedBytesPerRow` per mode and width.

In `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md`, Task 5: change `- [ ] Implement structural view boundaries and coordinated reveal semantics. ...` and `- [ ] Define explicit handling of live synchronous DOM setter failures and deferred commit reporting for the production API.` to `- [x]`, each followed by ` Done 2026-10-19: [plan](2026-10-19-view-boundaries.md), [measurements](../../research/2026-10-19-view-boundaries.md).`; in "Open, in proposed order", replace item 5's text with `Done: structural view boundaries and live setter-failure handling ([plan](2026-10-19-view-boundaries.md)).`

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas examples/Fable.Ranvier.Playground/tests/BoundaryPerf.fs
git add examples/Fable.Ranvier.Playground/tests/BoundaryPerf.fs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-solid.jsx examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-page.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/boundary-reveal.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/page.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/vite.config.mjs docs/research/framework-performance/investigation/view-boundaries/keyed-after.json docs/research/framework-performance/investigation/view-boundaries/template-after.json docs/research/framework-performance/investigation/view-boundaries/boundary-reveal.json docs/research/2026-10-19-view-boundaries.md docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md
git diff --cached --check
git commit -F - <<'EOF'
perf(view): measure boundary reveal against Solid 2 and confirm unchanged timings without boundaries

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```
