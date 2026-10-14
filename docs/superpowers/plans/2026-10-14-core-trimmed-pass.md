# Trimmed Keyed Projection Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a `KeyedProjection` pass's hashing, `keyOf` calls, entry lookups and item writes proportional to the changed positions, with before/after measurements committed as raw JSON.

**Architecture:** `KeyedProjection` copies its source into its own buffer, keeps the last installed pass's items and a private copy of its keys, and reconciles only the positions whose item differs under the graph's equality policy, after trimming the common prefix and suffix. `Projection` gains staged removals, a staged key array and two pass hooks (`Applied`, `Abandoned`). A gate keeps full passes for a `keyOf` that tracks a source, creates a node or uses the pass's owner. A pass nested in another always runs in full.

**Tech Stack:** F# (.NET 10, Fable 5.18.0), Expecto (.NET) and Fable.Mocha, BenchmarkDotNet, Node 26 with `@solidjs/signals` 2.0.0-rc.14.

**Spec:** `docs/superpowers/specs/2026-10-14-core-trimmed-pass-design.md` (revised after review; it is the authority)

## Global Constraints

- Worktree `C:\Users\shaya\RiderProjects\Ranvier-core-trimmed-pass`, branch `perf/core-trimmed-pass`. Never push, open a PR or merge.
- Prefix shell commands with `rtk` (`node`, `npm`, `dotnet fable` may run directly).
- Commit messages: conventional, one line, `-m` only, no body, no `Co-Authored-By` or `Claude-Session` lines, no `!`.
- Comments follow `C:\Users\shaya\RiderProjects\Ranvier\.claude\rules\comments.md`: state contracts, no defensive justification, no restated signatures. No `FOR-REVIEW` comments may be committed.
- Fable: no interface type tests (`:? IFoo`), no secondary constructors with a self-identifier.
- `keyOf` contract (verbatim for docs): "Equal items produce equal keys: an item equal, under the graph's equality policy, to the item it replaces in the previous pass's order (same index from the start, or same offset from the end) keeps its key and row without a call to `keyOf`. `keyOf` must return equal keys for equal items."
- **Gates** (every task that changes `src` or `tests` runs all of them before committing; record counts in the report):
  1. `rtk dotnet test tests/Ranvier.Tests -c Release` — all pass.
  2. `rtk dotnet test tests/Ranvier.Tests -c Release -p:RanvierTrace=true` — all pass.
  3. Fable, from the repo root (run `npm ci` once first):
     ```
     dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests -c Release
     RanvierTrace=true dotnet fable fable/Ranvier.Tests.Fable -e .fs.js -o dist/tests-traced -c Release
     node fable/Ranvier.Tests.Fable/Report.mjs
     ```
     Exit code 0, and the untriaged-failure list matches Task 1's baseline. `Report.mjs` rewrites the tracked `docs/.ai/fable-compat.md`: commit it with the task when the task adds tests, otherwise `rtk git restore docs/.ai/fable-compat.md`.
  4. `dotnet fsi tools/verify-trace.fsx --lint` — passes (if the flag differs, read the script's usage line).
  5. Format: `dotnet fantomas <each changed .fs>` then `dotnet fantomas --check <each changed .fs>`.
  6. `rtk git grep -n FOR-REVIEW -- src tests bench` — empty.
- A single test list runs with `rtk dotnet run --project tests/Ranvier.Tests -c Release -f net10.0 -- --filter-test-list "Trimmed pass" --summary`. Check that the summary's test count is non-zero; a filter that matches nothing reports success.
- Measurements run on a clean tree. Figures are measurements, not gates.

## Review Focus

1. A pass nested in another (a `CreateFirst` factory writing the source and reading a sibling row) must run in full and match a full-pass projection. Test in Task 3.
2. A caller mutating the array `Keys` returned (`Array.Reverse proj.Keys`) must not corrupt the next pass. Test in Task 3.
3. A `keyOf` that reads a signal only for an item that enters after the first pass must turn the gate off on the pass that adds it. Test in Task 3.
4. A `CreateFirst` factory failure after a trimmed pass, then a source change: the next pass runs in full and matches the oracle. Differential test in Task 3 and a targeted test in Task 4.
5. `int[]` sources under Fable (typed arrays) on the trimmed path. Test in Task 2, exercised again by Task 3's cost tests over `Item[]` and an `int[]` swap.

---

### Task 1: Benchmarks on the branch and the "before" measurements

**Files:**
- Commit (already present, untracked): `docs/superpowers/specs/2026-10-14-core-trimmed-pass-design.md`, `docs/superpowers/plans/2026-10-14-core-trimmed-pass.md`
- Add (already copied, untracked): `bench/Ranvier.CoreListBench/{Fixture.fs,Ranvier.CoreListBench.fsproj,run.mjs,package.json,package-lock.json,.gitignore}`
- Modify: `bench/Ranvier.CoreListBench/run.mjs`
- Create: `bench/Ranvier.Benchmarks/Lists.fs`; Modify: `bench/Ranvier.Benchmarks/Ranvier.Benchmarks.fsproj` (compile `Lists.fs` after `Projections.fs`)
- Create: `docs/research/core-trimmed-pass/` (raw results), `docs/research/2026-10-14-core-trimmed-pass.md`

**Interfaces:**
- Produces: `node bench/Ranvier.CoreListBench/run.mjs [--build] [--base=<dir holding Fixture.js>] [--label=<name>]` writing `docs/research/core-trimmed-pass/<label>.json`; BenchmarkDotNet class `Ranvier.Benchmarks.Lists.ListPassBenchmarks`; `B0` recorded in the report.

- [ ] **Step 1: Commit the spec and plan.** `rtk git add docs/superpowers` then `rtk git commit -m "docs: add the trimmed keyed projection pass spec and plan"`.

- [ ] **Step 2: Record the test baselines** on HEAD: gates 1–4 from the Global Constraints. Write into the report: the .NET pass counts (untraced and traced), and `Report.mjs`'s summary (per build and delivery) with the untriaged inline failures by name. Restore `docs/.ai/fable-compat.md` afterwards if it changed.

- [ ] **Step 3: `run.mjs` changes.**
  - Change `output` to `` `${root}docs/research/core-trimmed-pass/` ``.
  - Parameterise `ranvierFixture(dir)` on the directory of `Fixture.js` (default `${here}out/`).
  - Add `--base=<dir>`. The parent resolves it with `path.resolve(process.cwd(), value)` and passes the absolute directory to each child as `--base=<abs>`. The child then loads a second Ranvier fixture from it as framework `base`. Every round shuffles the framework names with the seeded `random` (Fisher–Yates) and times each once. The correctness check compares all snapshots.
  - When `base` is present the summary gains `baseMs`, `vsBase` (median, q1, q3 and per-process medians of the same-round ratio `ranvier / base`) and `baseRatio` (`base / solid`, same shape as `ratio`).
  - The parent records `baseDir`, `baseCommit` (`git -C <baseDir> rev-parse HEAD`) and `baseDirty` (`git -C <baseDir> status --porcelain`), and fails if `--base-commit=<sha>` is given and differs from `baseCommit`.
  - Capture `dirty` at the start of the run, before any build, as `dirtyAtStart`, and keep the existing end-of-run `dirty`.
  - Keep `--processes`, `--warmup`, `--rounds`, `--ops`, `--label`.

- [ ] **Step 4: Add the .NET list benchmark** `bench/Ranvier.Benchmarks/Lists.fs`:

```fsharp
module Ranvier.Benchmarks.Lists

open BenchmarkDotNet.Attributes
open Ranvier

type Item = { Id: int; Label: Signal<string> }

[<AllowNullLiteral>]
type Row(id: int) =
    member _.Id = id
    member val Text = "" with get, set

/// <summary>
/// The production row form over <c>Rows</c> items: <c>createProjectionOnce CreateFirst</c> keyed by id, one effect per row
/// writing its label, and, with <c>Layout</c>, one effect reading every row through <c>Keys</c>. Each benchmark alternates
/// between two states of equal cost, so no per-invocation setup is needed. Every benchmark runs two passes.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "ListPass">]
type ListPassBenchmarks() =
    let graph = new Graph ()
    let mutable items: Signal<Item array> = Unchecked.defaultof<_>
    let mutable projection: Projection<int, Row> = Unchecked.defaultof<_>
    let mutable full: Item array = [||]
    let mutable other: Item array = [||]
    let mutable sink = 0
    let mutable next = 1

    let build count =
        Array.init count (fun _ ->
            let id = next
            next <- next + 1
            { Id = id; Label = Signal (graph, "row " + string id) })

    [<Params(1000, 10000)>]
    member val Rows = 1000 with get, set

    [<Params(true, false)>]
    member val Layout = true with get, set

    [<GlobalSetup>]
    member this.Setup() =
        use _ = graph.Activate ()
        items <- Signal (graph, [||])
        full <- build this.Rows
        other <- build this.Rows
        let layout = this.Layout

        createRoot (fun _ ->
            projection <-
                createProjectionOnce
                    ProjectionOptions.CreateFirst
                    (fun (item: Item) -> item.Id)
                    (fun item ->
                        let row = Row ((item ()).Id)
                        createEffect (fun () -> row.Text <- (item ()).Label.Value)
                        row)
                    (fun () -> items.Value :> seq<_>)

            createEffect (fun () ->
                let keys = projection.Keys

                if layout then
                    untrack (fun () ->
                        for key in keys do
                            sink <- sink + (projection.Get key).Id)))

        items.Value <- full

        if (untrack (fun () -> projection.Keys)).Length <> this.Rows then
            failwith "The projection did not build every row."

    /// <summary>Swaps the items at 1 and <c>Rows - 2</c>.</summary>
    [<Benchmark>]
    member _.Swap() =
        let swapped = Array.copy items.Peek
        let last = swapped.Length - 2
        let first = swapped[1]
        swapped[1] <- swapped[last]
        swapped[last] <- first
        items.Value <- swapped
        items.Value <- full

    /// <summary>Removes the item at 3, then restores it, which creates its row again.</summary>
    [<Benchmark>]
    member _.RemoveAndRestore() =
        items.Value <- Array.removeAt 3 full
        items.Value <- full

    /// <summary>Replaces the item at 500 with an equal-key copy, then restores the original.</summary>
    [<Benchmark>]
    member _.UpdateOneItem() =
        items.Value <- Array.updateAt 500 { full[500] with Id = full[500].Id } full
        items.Value <- full

    /// <summary>Replaces every row with a new set, then restores the original set.</summary>
    [<Benchmark>]
    member _.ReplaceAll() =
        items.Value <- other
        items.Value <- full

    /// <summary>Clears the list, then recreates every row.</summary>
    [<Benchmark>]
    member _.ClearAndCreate() =
        items.Value <- [||]
        items.Value <- full

    [<GlobalCleanup>]
    member _.Cleanup() =
        graph.Dispose ()
```

  `Swap` writes the swapped array and then `full`, so each invocation is two swaps. Adjust only what fails to compile (`createRoot: (Owner -> 'T) -> 'T`, `createEffect: (unit -> unit) -> unit`, `untrack`, `createProjectionOnce options keyOf factory source` are in `Ranvier.Api`, opened by `open Ranvier`; if `Api` must be opened explicitly, add `open Ranvier.Api`). Smoke run: `dotnet run -c Release --project bench/Ranvier.Benchmarks -- --filter '*ListPass*' --job dry --artifacts "$CLAUDE_JOB_DIR/tmp/bdn-smoke"` (use `--short` if `--job dry` is rejected; `Program.fs` handles `--short`).

- [ ] **Step 5: Format and commit the benchmarks.** `dotnet fantomas bench/Ranvier.Benchmarks/Lists.fs bench/Ranvier.CoreListBench/Fixture.fs` and `--check` them. `rtk git add bench/Ranvier.CoreListBench bench/Ranvier.Benchmarks` then `rtk git commit -m "bench: add the core-only keyed list benchmarks against Solid 2 and on .NET"`. Record `git rev-parse HEAD` as `B0` in the report. Check `git status --porcelain` is empty; restore anything a gate rewrote.

- [ ] **Step 6: Measure "before".**
  - Node first, on the clean tree: `node bench/Ranvier.CoreListBench/run.mjs --build --label=before` → `docs/research/core-trimmed-pass/before.json`. Check `commit` = `B0` and `dirtyAtStart` is empty (`dirty` may list `before.json`'s own directory only if it existed before; it should be empty because the file is written after the status call).
  - .NET: capture `git status --porcelain` into a variable, then run `dotnet run -c Release --project bench/Ranvier.Benchmarks -- --filter '*ListPass*' '*ProjectionBenchmarks*' --artifacts "$CLAUDE_JOB_DIR/tmp/bdn-before"`. Copy `results/*-report-full-compressed.json` and `results/*-report-github.md` from that directory into `docs/research/core-trimmed-pass/dotnet-before/`. Write `docs/research/core-trimmed-pass/dotnet-before/provenance.json` with a small `node -e` script: `{ commit: <git rev-parse HEAD>, dirty: <the captured status lines>, measuredAt: <ISO time>, command: <the command string>, dotnet: <dotnet --version>, cpu: <os.cpus()[0].model> }`. Confirm `git status --porcelain` lists only `docs/research/`.

- [ ] **Step 7: Start the note** `docs/research/2026-10-14-core-trimmed-pass.md`: title, date, a "Method" section and a "Before" section.
  - Method: the commits (`B0`; the spec's base description), machine (CPU, Node, .NET, Fable, Solid version from the JSON), and this paragraph verbatim: "Seven Node processes, each with 10 warmup and 31 timed rounds per operation. A round times the operation once per framework in a seeded random order, each on a fresh list after a forced GC. Before timing, each operation's resulting rows (order and text) are checked equal across the frameworks." Add: "Each .NET benchmark runs two passes and alternates between two states of equal cost. With `Layout`, an effect reads every row after every key change, which is linear in the list length in both versions."
  - Before: a table per Node operation (Solid 2 ms, Ranvier ms, ratio [q1, q3], per-process medians) and a .NET table (benchmark, Rows, Layout, mean, allocated).

- [ ] **Step 8: Commit.** `rtk git add docs/research` then `rtk git commit -m "docs(research): record the keyed list pass before the trimmed pass"`.

---

### Task 2: Pass plumbing, own item buffer and the purity gate (no trimming yet)

**Files:**
- Modify: `src/Ranvier/Core.fs` (`Graph.NodeCount`)
- Modify: `src/Ranvier/Platform.fs` (`asArray`)
- Modify: `src/Ranvier/Projections.fs`: `IProjectionPass`, `Projection`, `RowsOf`, `KeyedProjection`
- Modify: `src/Ranvier/Combinators.fs` (the grouping projection's `IProjectionPass` implementation, near line 490)
- Create: `tests/Ranvier.Tests/TrimmedPass.fs`; Modify: `tests/Ranvier.Tests/TestFiles.props` (after `Projections.fs`), `fable/Ranvier.Tests.Fable/Main.fs` (add `testList "TrimmedPass.fs" [ Ranvier.Tests.TrimmedPass.tests ]` after the `"Projections.fs"` list; the name must equal the file name). `tests/Ranvier.Tests/Main.fs` scans the assembly and needs nothing.

**Interfaces:**
- Produces (used by Task 3):
  - `Graph.NodeCount: int` (internal): the last id `NextId` issued.
  - `Platform.asArray: seq<'T> -> 'T[]`: the sequence itself when it is an array (on Fable: a JS `Array` or a typed array other than `DataView`), else null.
  - `IProjectionPass.Applied: unit -> unit`, `IProjectionPass.Abandoned: unit -> unit`.
  - `Projection` internals: `TrackedReads: int` (count of `IComputation.AddSource` calls), `ScopeRequests: int` (count of `IScopeHost.Scope` reads), `Removed: ResizeArray<'K>`, `StageRemovals(): unit`, `StageKeys(keys: 'K[]): unit` (null keeps the published keys), `PublishedKeys: 'K[]`, `Runs` (public, existing).
  - `RowsOf`: `Stage(key: 'K, item: 'T): unit`, `HasRow(key: 'K): bool`, `abstract Applied`/`Abandoned` with no-op defaults.
  - `KeyedProjection` fields: `lastItems`, `lastCount`, `lastKeys`, `spare`, `passItems`, `passCount`, `passRun`, `trimValid`, `keyPure`; members `Read()`, `KeyOf(item)`.

- [ ] **Step 1: Write the tests** in `tests/Ranvier.Tests/TrimmedPass.fs`, module `Ranvier.Tests.TrimmedPass`, with `[<Tests>] let tests = testList "Trimmed pass" [ ... ]` at the bottom of the file. Copy the `open` lines from the top of `tests/Ranvier.Tests/Projections.fs`.

```fsharp
test "list, lazy seq and array sources give the same keys and rows" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items = createSignal [ 3; 1; 2 ]
    let fromList = createProjection id (fun x -> x * 10) (fun () -> items.Value)
    let fromSeq = createProjection id (fun x -> x * 10) (fun () -> seq { for x in items.Value -> x })
    let fromArray = createProjection id (fun x -> x * 10) (fun () -> List.toArray items.Value :> seq<_>)
    let read (p: Projection<int, int>) = p.Keys |> Array.map (fun k -> k, p.Get k)

    for next in [ [ 1; 2; 3 ]; [ 2 ]; []; [ 4; 2; 9 ] ] do
        items.Value <- next
        Expect.equal (read fromSeq) (read fromList) $"seq after {next}"
        Expect.equal (read fromArray) (read fromList) $"array after {next}"
}

test "an array mutated in place and republished is compared against the previous pass's copy" {
    use g = new Graph ()
    use _ = g.Activate ()
    let array = [| "a"; "b"; "c" |]
    let tick = createSignal 0

    let proj =
        createProjection id id (fun () ->
            tick.Value |> ignore
            array :> seq<_>)

    Expect.sequenceEqual proj.Keys [ "a"; "b"; "c" ] "first pass"
    tick.Value <- 1
    Expect.sequenceEqual proj.Keys [ "a"; "b"; "c" ] "an unchanged republish"
    array[1] <- "x"
    tick.Value <- 2
    Expect.sequenceEqual proj.Keys [ "a"; "x"; "c" ] "the in-place change is seen"
}

test "an int array source reads by index" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items = createSignal [| 1; 2; 3; 4 |]
    let proj = createProjection id (fun x -> x + 100) (fun () -> items.Value :> seq<_>)
    Expect.sequenceEqual proj.Keys [ 1; 2; 3; 4 ] "first pass"
    items.Value <- [| 1; 3; 2; 4 |]
    Expect.sequenceEqual proj.Keys [ 1; 3; 2; 4 ] "swapped"
    items.Value <- [| 3; 1 |]
    Expect.sequenceEqual proj.Keys [ 3; 1 ] "shrunk"
    Expect.equal (proj.Get 1) 101 "row"
}

test "a lazy source is enumerated to completion before keyOf runs" {
    use g = new Graph ()
    use _ = g.Activate ()
    let log = ResizeArray<string>()

    let proj =
        createProjection
            (fun x ->
                log.Add ("key " + string x)
                x)
            id
            (fun () ->
                seq {
                    for x in [ 1; 2 ] do
                        log.Add ("item " + string x)
                        yield x
                })

    proj.Keys |> ignore
    Expect.sequenceEqual log [ "item 1"; "item 2"; "key 1"; "key 2" ] "source first, then keyOf"
}
```

- [ ] **Step 2: Run the list** (Global Constraints command). Expected: the lazy-source test fails (order interleaved today), the other three pass. Confirm the count is 4.

- [ ] **Step 3: Graph and Platform.**
  - `Core.fs`, in `Graph` beside `NextId`: `member internal _.NodeCount = nextId`.
  - `Platform.fs`, following its existing `#if FABLE_COMPILER` layout:

```fsharp
#if FABLE_COMPILER
    /// <summary>The sequence when it is a JavaScript array or typed array, else null.</summary>
    [<Fable.Core.Emit("(Array.isArray($0) || (ArrayBuffer.isView($0) && !($0 instanceof DataView))) ? $0 : null")>]
    let asArray (items: seq<'T>) : 'T[] = Fable.Core.Util.jsNative
#else
    /// <summary>The sequence when it is an array, else null.</summary>
    let asArray (items: seq<'T>) : 'T[] =
        match items with
        | :? ('T[]) as array -> array
        | _ -> null
#endif
```

- [ ] **Step 4: Projection plumbing** in `src/Ranvier/Projections.fs`.
  - `IProjectionPass`, after `ClearStaged`:

```fsharp
    /// <summary>Runs after a snapshot pass's diff applies, once the key set is published.</summary>
    abstract Applied: unit -> unit

    /// <summary>Runs when a pass fails or suspends before its diff applies in full, and when the projection is disposed.</summary>
    abstract Abandoned: unit -> unit
```

  - In `Projection`, before `publishKeys`:

```fsharp
    /// <summary>Whether the pass in progress staged its removed keys in <c>removed</c>.</summary>
    let mutable removalsStaged = false

    /// <summary>Whether the pass in progress staged its key array.</summary>
    let mutable keysStaged = false

    /// <summary>The staged key array, or null when the pass kept the published keys.</summary>
    let mutable stagedKeys: 'K[] = null

    let mutable trackedReads = 0
    let mutable scopeRequests = 0
```

  - `publishKeys` becomes:

```fsharp
    let publishKeys () =
        if keysStaged then
            if not (isNull stagedKeys) then
                Tracer.Moved (graph, id, null)
                keys.WriteExcept (stagedKeys, puller)
                Tracer.Notified graph
        else
            <the existing body, indented one level>
```

  - `IComputation.AddSource`: `trackedReads <- trackedReads + 1` on its own line before `sources.Add (...)`.
  - `IScopeHost.Scope`: `scopeRequests <- scopeRequests + 1` as its first line.
  - `Run()`: after `removed.Clear ()`, add `removalsStaged <- false`, `keysStaged <- false`, `stagedKeys <- null`, each on its own line.
  - `ApplyDiff`: wrap the `entries.Iterate (...)` in `if not removalsStaged then`.
  - `SettleAdditions`: after `publishKeys ()`, add `this.Pass.Applied ()`.
  - `Abandon`: add `this.Pass.Abandoned ()` as its first line.
  - `Dispose`: inside `if not disposed then`, after `disposed <- true`, add `this.Pass.Abandoned ()`.
  - Members (beside `PassKeys`/`Seen`):

```fsharp
    member internal _.TrackedReads = trackedReads
    member internal _.ScopeRequests = scopeRequests
    member internal _.Removed = removed
    member internal _.PublishedKeys = keys.Peek

    /// <summary>Marks <c>Removed</c> as the pass's removed keys; the diff skips its walk of the entries.</summary>
    member internal _.StageRemovals() = removalsStaged <- true

    /// <summary>Sets the key array the diff publishes; null keeps the published keys.</summary>
    member internal _.StageKeys(array: 'K[]) =
        keysStaged <- true
        stagedKeys <- array
```

  - The grouping projection in `Combinators.fs`: `member _.Applied() = ()` and `member _.Abandoned() = ()` in its `IProjectionPass` implementation.

- [ ] **Step 5: `RowsOf`.** Split `Visit`, keeping its duplicate-key comment:

```fsharp
    member internal this.Visit(key: 'K, item: 'T) =
        if not (this.Seen.Add key) then
            // Two items, one key: one of them would silently disappear. The
            // throw fails the pass, and reaches the boundary around the read.
            raise (ProjectionFailures.duplicateKey key)

        this.PassKeys.Add key
        this.Stage (key, item)

    /// <summary>Stages a write to the item source of the row of <c>key</c>, or a row to create when it has none.</summary>
    member internal this.Stage(key: 'K, item: 'T) =
        let entry = this.Entries.Find key

        if isNull entry then
            adds.Add (struct (key, item))
        else
            writes.Add (struct ((entry :?> ItemRow<'T, 'K, 'V>).Item, item))

    member internal this.HasRow(key: 'K) = not (isNull (this.Entries.Find key))

    abstract Applied: unit -> unit
    default _.Applied() = ()

    abstract Abandoned: unit -> unit
    default _.Abandoned() = ()
```

  In `RowsOf`'s `interface IProjectionPass`: `member this.Applied() = this.Applied ()` and `member this.Abandoned() = this.Abandoned ()`.

- [ ] **Step 6: `KeyedProjection`**: own buffer, gate, install.

```fsharp
type internal KeyedProjection<'T, 'K, 'V when 'K: equality>
    (graph: Graph, keyOf: 'T -> 'K, map: 'T -> 'V, factory: (unit -> 'T) -> (unit -> 'V), source: unit -> 'T seq) =
    inherit RowsOf<'T, 'K, 'V>(graph, map, factory)

    /// <summary>The items of the last installed pass, in <c>lastItems[0 .. lastCount - 1]</c>.</summary>
    let mutable lastItems: 'T[] = Array.empty
    let mutable lastCount = 0

    /// <summary>A private copy of the key array of the last installed pass.</summary>
    let mutable lastKeys: 'K[] = Array.empty

    /// <summary>The published key array <c>lastKeys</c> was copied from.</summary>
    let mutable lastPublished: 'K[] = null

    /// <summary>A reusable item buffer, or null while a pass holds it.</summary>
    let mutable spare: 'T[] = Array.empty

    /// <summary>The items of the pass in progress, in <c>passItems[0 .. passCount - 1]</c>, read when <c>Runs</c> was <c>passRun</c>.</summary>
    let mutable passItems: 'T[] = Array.empty
    let mutable passCount = 0
    let mutable passRun = -1

    /// <summary>Whether <c>lastItems</c> and <c>lastKeys</c> describe the rows. True only between an installing <c>Applied</c> and the next <c>Enumerate</c>.</summary>
    let mutable trimValid = false

    /// <summary>Whether every <c>keyOf</c> call so far read no tracked source, created no node and used no owner.</summary>
    let mutable keyPure = true

    /// <summary>A buffer of at least <c>size</c> items: <c>spare</c> when it fits, else a new array.</summary>
    let take (size: int) =
        let buffer = spare
        spare <- null

        if isNull buffer || buffer.Length < size then
            Array.zeroCreate (max size (max 8 (if isNull buffer then 0 else buffer.Length * 2)))
        else
            buffer

    /// <summary>Returns <c>passItems</c> to <c>spare</c> when no buffer is spare.</summary>
    let release () =
        if isNull spare && not (obj.ReferenceEquals (passItems, lastItems)) then
            Array.fill passItems 0 passCount Unchecked.defaultof<'T>
            spare <- passItems

        passItems <- Array.empty
        passCount <- 0

    /// <summary>Reads the source into a buffer owned by this pass.</summary>
    member private this.Read() =
        let items = source ()
        let array = Platform.asArray items
        let mutable buffer = Array.empty
        let mutable count = 0

        if isNull array then
            buffer <- take 8

            for item in items do
                if count = buffer.Length then
                    let grown = Array.zeroCreate (buffer.Length * 2)
                    Array.blit buffer 0 grown 0 count
                    buffer <- grown

                buffer[count] <- item
                count <- count + 1
        else
            count <- array.Length
            buffer <- take count
            Array.blit array 0 buffer 0 count

        passItems <- buffer
        passCount <- count
        passRun <- this.Runs

    /// <summary><c>keyOf</c>, clearing <c>keyPure</c> when the call reads a tracked source, creates a node or uses the pass's owner.</summary>
    member private this.KeyOf(item: 'T) =
        let reads = this.TrackedReads
        let scopes = this.ScopeRequests
        let nodes = graph.NodeCount

        try
            keyOf item
        finally
            if
                this.TrackedReads <> reads
                || this.ScopeRequests <> scopes
                || graph.NodeCount <> nodes
            then
                keyPure <- false

    override this.Enumerate() =
        trimValid <- false
        this.Read ()
        let items = passItems
        let count = passCount

        for i in 0 .. count - 1 do
            let item = items[i]
            this.Visit (this.KeyOf item, item)

    override this.Applied() =
        if this.Runs = passRun && keyPure then
            let published = this.PublishedKeys

            if not (obj.ReferenceEquals (published, lastPublished)) then
                lastKeys <- Array.copy published
                lastPublished <- published

            if lastCount > 0 then
                Array.fill lastItems 0 lastCount Unchecked.defaultof<'T>

            spare <- lastItems
            lastItems <- passItems
            lastCount <- passCount
            passItems <- Array.empty
            passCount <- 0
            trimValid <- true
        else
            trimValid <- false
            release ()

    override _.Abandoned() =
        trimValid <- false
        release ()

        if this.IsDisposedInternal then
            lastItems <- Array.empty
            lastKeys <- Array.empty
            lastPublished <- null
            spare <- Array.empty
            lastCount <- 0
```

  Notes:
  - `Abandoned` reads `this.IsDisposedInternal`, so write it as `override this.Abandoned()`.
  - `spare <- lastItems` after filling `lastItems` with defaults: the first `Applied` installs `passItems` and makes the initial `Array.empty` the spare, which `take` replaces on demand.
  - A nested pass (a factory or cleanup writing the source and reading the projection) takes a fresh buffer, because the outer pass holds the spare (`spare` is null). The nested pass's `Applied` installs, since its `passRun` equals `Runs`. The outer pass's `Applied` then sees `Runs <> passRun` and takes the mismatch branch. In that branch `passItems` is the nested pass's buffer, possibly now `lastItems`: `release` must not fill or reuse a buffer that is `lastItems`, which the `ReferenceEquals` check guarantees. Loops read `passItems`/`passCount` through locals taken at their start, so a nested `Read` cannot change the array an outer loop walks.

- [ ] **Step 7: Run the list, then all gates.** The four tests pass. All `Projections.fs` and `Combinators.fs` tests still pass, untraced, traced and under Fable.

- [ ] **Step 8: Commit.** `rtk git add src tests fable docs/.ai/fable-compat.md` then `rtk git commit -m "refactor(core): read a keyed projection's source into its own buffer and record its last applied pass"`.

---

### Task 3: The trimmed pass

**Files:**
- Modify: `src/Ranvier/Projections.fs` (`KeyedProjection`)
- Modify: `tests/Ranvier.Tests/TrimmedPass.fs`

**Interfaces:**
- Consumes: everything Task 2 produces.
- Produces: `KeyedProjection.Trim()` (private), per the spec's "Trimmed pass" section.

- [ ] **Step 1: Add helpers** above `[<Tests>] let tests`:

```fsharp
type private Item = { Id: int; Text: string }

/// A createProjectionWith over items 1..n, counting keyOf calls, factory runs and cleanups.
let private counted (n: int) =
    let items = createSignal (Array.init n (fun i -> { Id = i + 1; Text = string (i + 1) }))
    let keyCalls = ref 0
    let created = ResizeArray<int>()
    let disposed = ResizeArray<int>()

    let proj =
        createProjectionWith
            (fun (item: Item) ->
                keyCalls.Value <- keyCalls.Value + 1
                item.Id)
            (fun item ->
                let id = (item ()).Id
                created.Add id
                onCleanup (fun () -> disposed.Add id)
                fun () -> (item ()).Text)
            (fun () -> items.Value :> seq<_>)

    createEffect (fun () ->
        for key in proj.Keys do
            proj.Get key |> ignore)

    items, keyCalls, created, disposed, proj
```

  If the effect's failure on a failing pass escapes the write that causes it, make the effect catch: `try (for key in proj.Keys do proj.Get key |> ignore) with _ -> ()`.

- [ ] **Step 2: Cost tests** (fail before Step 6):

```fsharp
test "a swap of two items calls keyOf twice and creates no row" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items, keyCalls, created, _, proj = counted 1000
    keyCalls.Value <- 0
    created.Clear ()
    let swapped = Array.copy items.Value
    let first = swapped[1]
    swapped[1] <- swapped[998]
    swapped[998] <- first
    items.Value <- swapped
    Expect.equal keyCalls.Value 2 "keyOf runs for the two moved positions"
    Expect.isEmpty created "no row is created"
    Expect.equal proj.Keys[1] 999 "the keys moved"
    Expect.equal proj.Keys[998] 2 "the keys moved"
}

test "removing one item calls keyOf zero times and disposes its row" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items, keyCalls, _, disposed, proj = counted 1000
    keyCalls.Value <- 0
    items.Value <- Array.removeAt 3 items.Value
    Expect.equal keyCalls.Value 0 "prefix and suffix cover every kept item"
    Expect.sequenceEqual disposed [ 4 ] "the removed row is disposed"
    Expect.equal proj.Keys.Length 999 "one key fewer"
}

test "prepending one item calls keyOf once" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items, keyCalls, created, _, proj = counted 1000
    Expect.sequenceEqual created [ 1..1000 ] "the first pass created every row"
    keyCalls.Value <- 0
    created.Clear ()
    items.Value <- Array.append [| { Id = 5000; Text = "new" } |] items.Value
    Expect.equal keyCalls.Value 1 "only the new item"
    Expect.sequenceEqual created [ 5000 ] "one row created"
    Expect.equal proj.Keys[0] 5000 "the new key leads"
}

test "replacing one item with an equal-key copy calls keyOf once and updates that row" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items, keyCalls, created, _, proj = counted 1000
    keyCalls.Value <- 0
    created.Clear ()
    items.Value <- Array.updateAt 500 { Id = 501; Text = "changed" } items.Value
    Expect.equal keyCalls.Value 1 "only the replaced item"
    Expect.isEmpty created "the row survives"
    Expect.equal (proj.Get 501) "changed" "the row reads the new item"
}

test "an int array swap calls keyOf twice" {
    use g = new Graph ()
    use _ = g.Activate ()
    let calls = ref 0
    let items = createSignal (Array.init 100 id)

    let proj =
        createProjection
            (fun (x: int) ->
                calls.Value <- calls.Value + 1
                x)
            id
            (fun () -> items.Value :> seq<_>)

    proj.Keys |> ignore
    calls.Value <- 0
    let swapped = Array.copy items.Value
    swapped[10] <- 90
    swapped[90] <- 10
    items.Value <- swapped
    Expect.equal proj.Keys[10] 90 "swapped"
    Expect.equal calls.Value 2 "two changed positions"
}
```

- [ ] **Step 3: Duplicate tests** (pass before and after Step 6):

```fsharp
let private duplicateAfter (edit: Item[] -> Item[]) =
    use g = new Graph ()
    use _ = g.Activate ()
    let items, _, _, disposed, proj = counted 10
    // An unchanged republish first, so the duplicate meets a trimmed pass.
    items.Value <- Array.copy items.Value
    items.Value <- edit items.Value
    Expect.throwsT<System.InvalidOperationException> (fun () -> proj.Keys |> ignore) "the pass fails"
    Expect.stringContains proj.Error.Message "twice in one pass" "the duplicate-key failure"
    Expect.isEmpty disposed "the failed pass retired no row"
```

  Place `duplicateAfter` above `tests` with the helpers, and add these tests:

```fsharp
test "a changed key equal to a prefix key fails the pass" {
    duplicateAfter (Array.updateAt 5 { Id = 1; Text = "dup" })
}

test "a changed key equal to a suffix key fails the pass" {
    duplicateAfter (Array.updateAt 2 { Id = 10; Text = "dup" })
}

test "a changed key equal to a key kept at an unchanged middle position fails the pass" {
    // Equal lengths: positions 2 and 7 change; position 4 keeps its item with key 5.
    duplicateAfter (fun a ->
        a
        |> Array.updateAt 2 { Id = 100; Text = "x" }
        |> Array.updateAt 7 { Id = 5; Text = "dup" })
}

test "two changed positions with one key fail the pass" {
    duplicateAfter (fun a ->
        a
        |> Array.updateAt 3 { Id = 200; Text = "x" }
        |> Array.updateAt 6 { Id = 200; Text = "y" })
}

test "a length change whose new middle repeats a prefix key fails the pass" {
    duplicateAfter (Array.insertAt 4 { Id = 2; Text = "dup" })
}
```

- [ ] **Step 4: Contract and re-entry tests.** Expected before Step 6: the in-place mutation test FAILS (the full pass re-keys); the rest pass.

```fsharp
test "an item mutated in place at the same position keeps its key" {
    use g = new Graph ()
    use _ = g.Activate ()
    let cell = ref 1
    let a = box cell
    let items = createSignal [| a |]
    let proj = createProjection (fun (o: obj) -> (o :?> int ref).Value) id (fun () -> items.Value :> seq<_>)
    Expect.sequenceEqual proj.Keys [ 1 ] "first key"
    cell.Value <- 2
    items.Value <- [| a |]
    Expect.sequenceEqual proj.Keys [ 1 ] "the same item keeps its key"
}

test "a keyOf that reads a signal for one item keeps updating" {
    use g = new Graph ()
    use _ = g.Activate ()
    let flag = createSignal 0
    let items = createSignal [| 1; 2; 3; 7 |]
    let proj = createProjection (fun x -> if x = 7 then 70 + flag.Value else x) id (fun () -> items.Value :> seq<_>)
    Expect.sequenceEqual proj.Keys [ 1; 2; 3; 70 ] "first pass"
    flag.Value <- 1
    Expect.sequenceEqual proj.Keys [ 1; 2; 3; 71 ] "the key follows the signal"
    items.Value <- Array.copy items.Value
    flag.Value <- 2
    Expect.sequenceEqual proj.Keys [ 1; 2; 3; 72 ] "still after an unchanged republish"
}

test "a keyOf that reads a signal for an item entering later turns trimming off" {
    use g = new Graph ()
    use _ = g.Activate ()
    let flag = createSignal 0
    let items = createSignal [| 1; 2; 3 |]
    let proj = createProjection (fun x -> if x = 7 then 70 + flag.Value else x) id (fun () -> items.Value :> seq<_>)
    Expect.sequenceEqual proj.Keys [ 1; 2; 3 ] "first pass"
    items.Value <- Array.copy items.Value
    items.Value <- [| 1; 2; 3; 7 |]
    Expect.sequenceEqual proj.Keys [ 1; 2; 3; 70 ] "7 enters"
    items.Value <- Array.copy items.Value
    flag.Value <- 5
    Expect.sequenceEqual proj.Keys [ 1; 2; 3; 75 ] "the key follows the signal"
}

test "a keyOf that registers a cleanup keeps registering it every pass" {
    use g = new Graph ()
    use _ = g.Activate ()
    let cleanups = ref 0
    let items = createSignal [| 1; 2; 3 |]

    let proj =
        createProjection
            (fun x ->
                onCleanup (fun () -> cleanups.Value <- cleanups.Value + 1)
                x)
            id
            (fun () -> items.Value :> seq<_>)

    proj.Keys |> ignore
    items.Value <- Array.copy items.Value
    proj.Keys |> ignore
    Expect.equal cleanups.Value 3 "the first pass's three cleanups ran"
    items.Value <- Array.copy items.Value
    proj.Keys |> ignore
    Expect.equal cleanups.Value 6 "the second pass registered three again"
}

test "a caller reversing the Keys array in place does not change the next pass" {
    use g = new Graph ()
    use _ = g.Activate ()
    let items, _, _, disposed, proj = counted 3
    System.Array.Reverse proj.Keys
    items.Value <- Array.take 2 items.Value
    Expect.sequenceEqual disposed [ 3 ] "the removed key is the last item's"
    Expect.equal (proj.Get 1) "1" "row 1 survives"
}

test "a create-first factory that writes the source and reads a sibling row matches a full pass" {
    // Run the same script on a trimmed projection and on one whose keyOf reads an unused signal.
    let script (forceFull: bool) =
        use g = new Graph ()
        use _ = g.Activate ()
        let unused = createSignal 0
        let items = createSignal [| 1; 2 |]
        let mutable proj: Projection<int, int> = Unchecked.defaultof<_>

        proj <-
            createProjectionWithOptions
                ProjectionOptions.CreateFirst
                (fun x ->
                    if forceFull then unused.Value |> ignore
                    x)
                (fun item ->
                    if item () = 3 then
                        items.Value <- [| 1; 2; 3; 4 |]
                        proj.TryGet 1 |> ignore

                    fun () -> item () * 10)
                (fun () -> items.Value :> seq<_>)

        proj.Keys |> ignore
        items.Value <- Array.copy items.Value
        proj.Keys |> ignore
        items.Value <- [| 1; 2; 3 |]

        let result =
            try
                proj.Keys |> Array.map (fun k -> k, proj.Get k) |> Array.toList |> Ok
            with ex ->
                Error (ex.GetType().Name)

        result, proj.Status

    Expect.equal (script false) (script true) "trimmed and full passes agree"
}
```

  Check `createProjectionWithOptions`'s argument order and `TryGet`'s name in `src/Ranvier/Api.fs:559` and `Projection`; adapt the script without changing what it does. If the full-pass oracle itself raises, the test still compares the two outcomes.

- [ ] **Step 5: Differential test.** Copy the `Lcg` type from the top of `tests/Ranvier.Tests/Projections.fs` (it is private there) and guard against `Next 0` on empty lists (return 0 without drawing). For seeds 1..20, run 300 operations over `Item[]` lists of length 0..40, choosing among: swap two positions, move one item, insert a new item, remove one, replace an item with an equal-key copy (`{ x with Text = x.Text + "'" }`), replace with a new key, append several, clear, reverse, unchanged republish (`Array.copy`), inject a duplicate key at a random position, and (create-first kinds only) insert an item with `Id >= 10000`, whose factory raises. After a failing operation the next one starts from the last list that passed.
  - Kinds, cycled by seed: `createProjectionWith`, `createProjectionWithOptions ProjectionOptions.CreateFirst`, `createProjectionOnce ProjectionOptions.CreateFirst`. The factory records `created` and registers `onCleanup` recording `disposed`; it raises for `Id >= 10000`.
  - Each kind runs twice over one source signal: trimmed (`keyOf = fun i -> i.Id`) and full (`keyOf = fun i -> unused.Value |> ignore; i.Id` with `unused = createSignal 0` never written).
  - Each projection has a key reader from `NewKeyReader ()`, read after every operation, and an effect reading `Keys` and every row (catching).
  - After every operation assert equal between the two: `Keys` (or the exception type name when reading raises); every row value; `created` as a sequence; `disposed` for this operation as a sorted list; the key reader's changes as `set [ for kv in delta.Changes -> kv.Key, kv.Value ]` plus `IsReset`; `Status`; and whether `Error` is null, and when it is not, whether its message contains `"twice in one pass"`.
  - Print the seed and operation index in every assertion message.

- [ ] **Step 6: Implement the trimmed pass.** In `KeyedProjection`, add:

```fsharp
    /// <summary>The keys at the previous pass's changed positions.</summary>
    let previousChanged = Platform.KeySet<'K>()

    /// <summary>The keys at this pass's changed positions.</summary>
    let changed = Platform.KeySet<'K>()

    /// <summary>The changed previous positions of the pass in progress.</summary>
    let changedAt = ResizeArray<int>()

    let same = graph.Options.Equality.Comparer<'T>()
```

```fsharp
    /// <summary>
    /// Reconciles the positions whose item differs from the last installed pass, after trimming the common prefix and
    /// suffix, and stages the removed keys and the key array.
    /// </summary>
    member private this.Trim() =
        let prev = lastItems
        let prevKeys = lastKeys
        let n0 = lastCount
        let cur = passItems
        let n1 = passCount
        let limit = min n0 n1
        let mutable start = 0

        while start < limit && same.Equals (prev[start], cur[start]) do
            start <- start + 1

        let mutable end0 = n0 - 1
        let mutable end1 = n1 - 1

        while end0 >= start
              && end1 >= start
              && same.Equals (prev[end0], cur[end1]) do
            end0 <- end0 - 1
            end1 <- end1 - 1

        // Equal-length middles compare index by index.
        let aligned = n0 = n1
        changedAt.Clear ()

        for i in start..end0 do
            if not aligned || not (same.Equals (prev[i], cur[i])) then
                changedAt.Add i
                previousChanged.Add (prevKeys[i]) |> ignore

        let mutable nextKeys: 'K[] = null

        if not aligned then
            nextKeys <- Array.zeroCreate n1
            Array.blit prevKeys 0 nextKeys 0 start
            Array.blit prevKeys (end0 + 1) nextKeys (end1 + 1) (n0 - end0 - 1)

        let visit (i: int) =
            let item = cur[i]
            let key = this.KeyOf item

            if not (changed.Add key) then
                raise (ProjectionFailures.duplicateKey key)

            if not (previousChanged.Contains key) && this.HasRow key then
                // The key belongs to a kept position.
                raise (ProjectionFailures.duplicateKey key)

            this.Stage (key, item)

            if aligned then
                if not (EqualityComparer<'K>.Default.Equals (key, prevKeys[i])) then
                    if isNull nextKeys then
                        nextKeys <- Array.copy prevKeys

                    nextKeys[i] <- key
            else
                nextKeys[i] <- key

        try
            if aligned then
                for i in changedAt do
                    visit i
            else
                for i in start..end1 do
                    visit i

            let removed = this.Removed

            for i in changedAt do
                let key = prevKeys[i]

                if not (changed.Contains key) then
                    removed.Add key

            this.StageRemovals ()
            this.StageKeys nextKeys
        finally
            for i in changedAt do
                previousChanged.Remove (prevKeys[i]) |> ignore

            for key in changedKeys do
                changed.Remove key |> ignore

            changedKeys.Clear ()
```

  with a further field `let changedKeys = ResizeArray<'K>()` ("The keys this pass added to <c>changed</c>.") and, in `visit`, `changedKeys.Add key` immediately after a successful `changed.Add key`.

  and change `Enumerate`:

```fsharp
    override this.Enumerate() =
        let trim = trimValid && keyPure
        trimValid <- false
        this.Read ()

        if trim then
            this.Trim ()
        else
            let items = passItems
            let count = passCount

            for i in 0 .. count - 1 do
                let item = items[i]
                this.Visit (this.KeyOf item, item)
```

  Notes:
  - The `finally` empties `previousChanged` and `changed` by removing what this pass added, so their cost follows the changes on both platforms (`HashSet.Clear` costs the set's capacity on .NET). It runs on a duplicate raised mid-loop too.
  - Check `KeySet`'s `Add`/`Remove`/`Contains` signatures in `src/Ranvier/Platform.fs` (`Remove` may return `unit`); adapt `|> ignore`.
  - `prevKeys` is the private copy; `Array.copy prevKeys` before mutating. Never write into `lastKeys`.
  - A duplicate raised from `Trim` precedes all staging; `Run`'s `with` calls `ClearStaged` and `Abandon`.

- [ ] **Step 7: Run the list**: every test passes. Then all gates.

- [ ] **Step 8: Commit.** `rtk git add src tests docs/.ai/fable-compat.md` then `rtk git commit -m "perf(core): reconcile only a keyed projection's changed positions"`.

---

### Task 4: Consumers of the pass, and the documented `keyOf` contract

**Files:**
- Modify: `tests/Ranvier.Tests/TrimmedPass.fs`
- Modify: `src/Ranvier/Api.fs` (the remarks of `createProjectionWith`, and of `createProjectionWithOptions`, `createProjectionOnce`, `createProjection` where they carry their own `keyOf` remarks)
- Modify: `docs/content/guide/projections.fsx` (find the `keyOf` introduction with `grep -n "keyOf" docs/content/guide/projections.fsx`)
- Modify (only if a test fails): `src/Ranvier/Projections.fs`

**Interfaces:**
- Consumes: Task 3's trimmed pass and the `counted` helper.

- [ ] **Step 1: Consumer tests.** Each runs a full first pass, an unchanged republish (`Array.copy`) so later writes take trimmed passes, then the operations named:
  - **Pending rows:** `createProjectionWith` over 20 items whose factory's reader, for key 3, reads `createAsyncSource<int>()` (copy the pattern of `pendingRow` at `tests/Ranvier.Tests/Projections.fs:1823`). Read every row in an effect (catching) so the pending row is observed. Then: `AnyPending` is true and `PendingKeys` is `[| 3 |]`; swap positions 10 and 15: still `[| 3 |]`; remove key 3: `AnyPending` is false.
  - **Key readers:** `let reader = proj.NewKeyReader ()`, read once (reset). Then after each of these, `let delta = reader.Read ()` and compare `set [ for kv in delta.Changes -> kv.Key, kv.Value ]`: a swap (empty set), removing key 4 (`(4, KeyChange.Removed)`), inserting key 500 (`(500, KeyChange.Added)`), replacing key 6 by key 600 at the same position (`(6, Removed)`, `(600, Added)`).
  - **CreateFirst rollback:** `createProjectionOnce ProjectionOptions.CreateFirst` whose factory raises for id 666, with creation and disposal logs. After a trimmed swap, insert `{ Id = 666 }` at index 10: reading `Keys` raises. Reading `Keys` again raises the held error with `Runs` unchanged and no new factory call. No previous row was disposed, and the logs show 666 neither kept nor leaked. Replace 666 by 667: the keys equal the expected list. A following swap calls `keyOf` twice (trimming resumed).
  - **Held duplicate then trimming:** a duplicate-key failure as in `duplicateAfter`, then a write that removes the duplicate: the keys are right; a following removal calls `keyOf` zero times.
  - **Combinators over trimmed inner projections:** an upstream `createProjection (fun (i: Item) -> i.Id) (fun i -> i.Id) (fun () -> items.Value :> seq<_>)`, and `Projection.filter (fun v -> v % 2 = 0)`, `Projection.sortBy (fun v -> -v)` and `Projection.groupBy (fun v -> v % 3)` over it (check the exact module/function names and argument order in `src/Ranvier/Combinators.fs` near lines 949, 1088 and 1189). After a swap, a removal and an insert in the upstream list, each downstream key array equals the one computed with `List.filter`/`List.sortBy`/grouping on the plain list, and each group's members equal the expected ones after an item moves between groups.
  - **Disposal during a trimmed pass:** a removed row's cleanup disposes the projection: the pass stops, `Keys` reads empty, no exception escapes. Copy the shape of the existing test found by `grep -n "dispos" tests/Ranvier.Tests/Projections.fs`.

- [ ] **Step 2: Run them.** A failure is a defect of the pass: fix it in `Projections.fs` with the smallest change and record the cause in the report.

- [ ] **Step 3: Document the contract.** In `src/Ranvier/Api.fs`, in the remarks of `createProjectionWith`, after the paragraph ending "Duplicate keys fail the pass.", add:

```
    /// <para>
    /// Equal items produce equal keys: an item equal, under the graph's equality policy, to the item it replaces in the
    /// previous pass's order (same index from the start, or same offset from the end) keeps its key and row without a
    /// call to <c>keyOf</c>. <c>keyOf</c> must return equal keys for equal items. A <c>keyOf</c> that reads a tracked
    /// source, creates a node or registers a cleanup runs for every item of every pass. <c>source</c> is enumerated in
    /// full before the first <c>keyOf</c> call of a pass.
    /// </para>
```

  Add the same paragraph to each of the other three functions only where it has its own `keyOf` remarks. In `docs/content/guide/projections.fsx`, after the `keyOf` introduction, add: "Equal items keep their keys: a pass calls `keyOf` only for the positions whose item changed since the previous pass."

- [ ] **Step 4: Gates and commit.** `rtk git add src tests docs` then `rtk git commit -m "test(core): cover pending rows, key readers, create-first rollback and combinators over trimmed passes"`. If the docs change is large enough to stand alone, commit it first with `docs(core): document that equal items keep their keys without a keyOf call`.

---

### Task 5: The "after" measurements and the note

**Files:**
- Create: `docs/research/core-trimmed-pass/after.json`, `docs/research/core-trimmed-pass/dotnet-after/`
- Modify: `docs/research/2026-10-14-core-trimmed-pass.md`

- [ ] **Step 1: Check the fixture is unchanged.** `rtk git diff --quiet <B0> HEAD -- bench/Ranvier.CoreListBench/Fixture.fs bench/Ranvier.CoreListBench/Ranvier.CoreListBench.fsproj bench/Ranvier.Benchmarks/Lists.fs` exits 0. Record `A1 = git rev-parse HEAD`. Check `git status --porcelain` is empty.

- [ ] **Step 2: Build the before fixture.** `rtk git worktree add ../Ranvier-trimmed-base <B0>`, then in `../Ranvier-trimmed-base/bench/Ranvier.CoreListBench`: `dotnet fable Ranvier.CoreListBench.fsproj --outDir out --configuration Release --noCache` and write `out/package.json` containing `{"type":"module"}`.

- [ ] **Step 3: Measure.**
  - Node: `node bench/Ranvier.CoreListBench/run.mjs --build --base=C:/Users/shaya/RiderProjects/Ranvier-trimmed-base/bench/Ranvier.CoreListBench/out --base-commit=<B0> --label=after`. Check `commit` = `A1`, `dirtyAtStart` empty, `baseCommit` = `B0`, `baseDirty` lists only the untracked `out/` if anything (it is git-ignored, so empty).
  - .NET: as Task 1 Step 6 with `--artifacts "$CLAUDE_JOB_DIR/tmp/bdn-after"`, into `docs/research/core-trimmed-pass/dotnet-after/` with a generated `provenance.json`.
  - `rtk git worktree remove ../Ranvier-trimmed-base`.

- [ ] **Step 4: Write the "After" section.** Node table per operation: Solid 2 ms, before ms (`baseMs` from `after.json`, paired), after ms, after/Solid ratio [q1, q3], after/before paired ratio `vsBase` [q1, q3], per-process `vsBase` medians. Say that `before.json` records `B0` in two-framework rounds and is not paired with `after.json`. .NET table: benchmark, Rows, Layout, before mean, after mean, ratio, before and after allocation, marked "separate runs, not paired". Then a short list of what moved and what did not, with the remaining gaps to Solid 2 as measured numbers, no targets.

- [ ] **Step 5: Commit.** `rtk git add docs/research` then `rtk git commit -m "docs(research): measure the trimmed keyed projection pass against the full pass and Solid 2"`.
