# Assessment of the defect ledger

An independent reading of `BUGS.md` against `src/Ranvier` at `4f03ee9a` (2026-11-10). It asks one question of every
entry: does this matter to Ranvier as shipped, or only to the model?

## Summary

- **The ledger is accurate.** Every mechanism checked matches the F#. `src/` is byte-identical to the pin `d701e6c5`, so
  every `Core.fs` line number in `BUGS.md` and `CORRESPONDENCE.md` is current (38 of 38 sampled references hit).
- **"Confirmed 9" overstates the count and understates the worst one.** The nine entries reduce to four root causes. One
  of them loses updates in ordinary code that uses only `createSignal`, `createMemo` and `createEffect` under default
  options, with no write in any body. That one deserves a fix before anything else in this folder.
- **Of the 14 entries awaiting a ruling, two are defects to fix** (`failed_batch_leaves_queue`,
  `diamond_marks_running_effect`), one exposes a separate hazard (`effect_restored_source_loop`), and eleven can be ruled
  as intended or are questions about the model only.
- **None of the nine closed entries needs reopening.**
- **The strong theorems cover a small part of real usage.** Convergence and refinement hold for value-compared memos with
  pure bodies feeding read-only effects on a static graph. Effects that write, effect cleanups, child effects, memos
  returning fresh records, `createEffectOn`, bindings, projections, async nodes, owners and the Fable build are outside
  them.

## How this was checked

Eleven analyses, each followed by an adversarial review that re-read the cited code. Statements below are marked by how
they were established:

- **Run**: reproduced on an out-of-tree Release build of `src/Ranvier` at HEAD, through the public API.
- **Read**: established from the F# source, docs and tests. Not executed.
- **Ledger**: taken from `BUGS.md` or the scenario source.

No test suite was run, so no proposed fix below has been checked against the existing tests. The Rocq project was not
built. The Fable build was not exercised.

## Root cause 1: missed invalidation during dependency revalidation

**Entries:** `cmp_never_passes_source`, `walk_passed_source_marked`, `self_marking_wake_lost`, and as consequences
`read_move_leaves_effect_queued` and `quiescent_read_answers_past_flush`.

**Severity: high.**

A node in the `Check` state revalidates its dependencies in read order and sets itself `Clean` if none reported a
change (`Effect.Execute`, Core.fs 4788-4799; `Memo.ResolveCheck`, 4304-4316). The loop does not detect a dependency that
is invalidated after it has been revalidated. That happens when recomputing a later dependency invalidates an earlier
one: `MarkCheck` is a no-op on a node already in `Check` (4757-4768), so the invalidation is dropped and the node is
marked `Clean` with a stale dependency. If that dependency's `unannounced` flag is clear, later writes to it do not
notify the node either.

### It is reachable from ordinary code (run)

`BUGS.md` shows this with a ten-node scenario. Eight nodes are enough, one fresh-value memo is enough, and a plain
view-model produces the shape:

```fsharp
type VM = { Rows: int list }
type Model = { ShowDone: bool; Items: int list }

let model = createSignal { ShowDone = false; Items = [3; 1; 2] }
let title = createSignal 0
let sorted = createMemo (fun _ -> model.Value.Items |> List.sort)
let filtered = createMemo (fun _ -> sorted.Value |> List.filter (fun x -> x > 1))
let vm = createMemo (fun _ -> if model.Value.ShowDone then { Rows = filtered.Value } else { Rows = sorted.Value })
let header = createMemo (fun _ -> model.Value |> ignore; vm.Value |> ignore; title.Value)
let hasRows = createMemo (fun _ -> not vm.Value.Rows.IsEmpty)
let log = ResizeArray<int> ()
createEffect (fun () -> let h = header.Value in hasRows.Value |> ignore; log.Add h)

model.Value <- { model.Peek with ShowDone = true }
title.Value <- 7
title.Value <- 8
flush ()
// log = [0]. The effect never sees 7 or 8.
// header.Value from outside answers 8, and only a flush after that read runs the effect.
```

What the shape needs, by varying it across twelve topologies:

- one default-comparer memo returning a fresh reference value (`vm`), whose conditional read switches from a memo to a
  second memo that reads the first;
- a memo (`header`) that reads the switching signal before the fresh-value memo, and has another source of its own;
- a memo over the fresh-value memo whose value holds (`hasRows`);
- one observer that reads `header` before `hasRows`.

Remove any one and the update arrives. `createMemo` compares reference types by reference under the default policy
(Types.fs 294-307, 537-539; `docs/content/guide/equality.md:16`), so records, lists, tuples and `Some x` all qualify.
`Mvu.Select` (`src/Ranvier.Elmish/Mvu.fs:114-118`) builds exactly the "every memo reads the model signal" ingredient. No
file in the repository was found to contain the complete shape.

The same loss was run with `createEffectOn` as the observer, and with a reading memo feeding `bindMemo`.

### The async route (run)

`self_marking_wake_lost` is filed as needing a memo that writes or an owning memo's cleanup that writes. `BUGS.md` notes
that async nodes reach the same shape "which this entry has not run on them". They do: a `createAsync` loader that settles
a `createAsyncSource` read earlier by the same memo leaves the effect on the old value (`log = [0]`, source holds 50).
The trigger still has user code settling a source during a computation. Whether `Ranvier.Query` does that on its own was
not checked.

### Size of the fix

`BUGS.md` names two revalidation loops. The same loop, with the same unconditional transition to `Clean`, is duplicated
in nine places (read):

| Site | Lines |
|---|---|
| `Memo.ResolveCheck` | Core.fs 4308-4316 |
| `Effect.Execute` | Core.fs 4792-4799 |
| `EffectOn.Execute` | Core.fs 5182-5193 |
| `AsyncMemo` | Core.fs 5844-5856 |
| `Boundary.Pull` | Core.fs 6396-6408 |
| `Timed` | Timed.fs 310-321 |
| `StreamMemo` | Streams.fs 437-448, 524-538 |
| `Projection` | Projections.fs 1088-1098 |

Core.fs 5492-5498 has a further `Check`-to-`Clean` resolution that was not traced. A fix at two sites leaves the loss in
the other seven. Factoring the walk into one helper first would make the fix one change.

A candidate, not built: a per-node flag set when an invalidation arrives at a node already in `Check`, cleared when
revalidation starts, and tested when it ends to repeat the loop instead of setting `Clean`. Two cautions from review. The
flag will be set on most diamond-shaped graphs, so the cost is a second pass over the dependencies in the common case, on
a path the source comments call nanosecond-sensitive (4319-4323). And a memo that invalidates its own dependency on every
run would loop indefinitely where today it terminates with a stale value, so the repeat needs an iteration limit or a
ruling.

### The two consequences

- `read_move_leaves_effect_queued` is reachable only from the stale states above. The revalidation fix resolves it. Low.
- `quiescent_read_answers_past_flush` is this bug combined with the open question in `read_settles_writer`. The
  revalidation fix resolves its two scenarios but not the behaviour: `read_settles_writer` reproduces it without this
  bug, so that ruling is needed regardless. Low.

## Root cause 2: self-notification suppression drops a required re-run

**Entries:** `effect_resolves_over_moved_source`, `check_resolves_over_moved_source`.

**Severity: medium for the effect form, low for the memo form.**

When a memo's value changes, `NotifyDirtyExcept` (Core.fs 395-410, called from `Memo.Run` at 4283) invalidates every
dependent except the currently executing computation. The exclusion is wrong in one case: the executing computation read
memo `a` earlier in the same run, then wrote `a`'s signal, then triggered a recompute of `a`. It holds a value of `a`
from before the write, and the only invalidation that would schedule its re-run is suppressed.

Run: an effect that reads `a`, writes `a`'s signal and reads a memo over `a` ends with its last run on the pre-write
value, `Clean`, nothing queued. It stays that way until an independent write to that signal, which never comes if the
effect is the only writer.

The trigger is narrower than the entry reads. The later read must be `a` again, or a memo over `a` that was current
before the write. The direct form, an effect writing a signal it reads, works and is tested (`Reentrancy.fs:107-125`).

Effects that write are documented usage (`docs/content/concepts/contracts.md:56-57`, `docs/content/guide/effects.md:136`,
Core.fs 4667-4669). Those texts promise a re-run when an effect writes a signal it reads. This effect reads a memo over
the written signal, so the failing case extends the documented contract and does not breach its literal wording. **Owner's
ruling needed:** is a write to a signal read through a memo inside the contract? If yes, this is a medium defect.

A candidate fix, not built: exclude the executing computation only when it has not yet read this dependency in the
current run, by comparing the observer entry's slot index with the reader's source-list cursor. It touches the Fable slot representation
(Core.fs 253-254) and about ten `NotifyDirtyExcept` call sites.

## Root cause 3: unbounded recursion on a cyclic memo dependency

**Entry:** `self_read_check_overflow`. `nested_self_recompute_resets_cursor` shares the root and leaves behind the
self-edge this one needs.

**Severity: medium. Fix regardless of any ruling.**

A memo that reads itself, or two memos that read each other, answers a default on first evaluation and overflows the
stack on a later write (read: `ResolveCheck` 4304-4316 re-enters through `UpdateIfNecessary` with no guard). On .NET that
is a `StackOverflowException`: uncatchable, the whole process, no message naming the node.

The trigger is a programming error. The failure mode contradicts intent written down twice: Core.fs 4203-4206 says marking
clean before the body "turns a memo that reads itself from a stack overflow into a default value", and
`tests/Ranvier.Tests/Invalidation.fs:66-83` says a cycle "must fail as a value, not as a stack overflow the caller cannot
catch". That protection covers a read during the body. The `Check` path has none.

A re-entrancy guard on the walk is off the hot path. `AsyncMemo` (5848) and `Boundary` (6400) have the same unguarded
shape and were not traced to an overflow.

One thing to check before settling severity: the suite has supported self-reads inside memo cleanups
(`DischargeReentry.fs:33-48, 99-110`; `MemoScopes.fs:315-323`). If such a read links a self-edge, the overflow is
reachable from a supported pattern. Not established.

`nested_self_recompute_resets_cursor` alone is low: it needs a self-read and a self-write together. The direct defect is
that the outer run overwrites the nested run's newer value at 4240, not the cursor reset the title names.

## Root cause 4: signal writes in memo bodies are specified inconsistently

Not a defect. It decides how several entries read.

`BUGS.md` cites one source for "a memo that writes is outside the contract": a test comment
(`tests/Ranvier.Tests/Invalidation.fs:33`). The published contract page says the opposite
(`docs/content/concepts/contracts.md:58-59`: "A write inside a memo body wakes effects that run once the outermost body
or read has finished"), `Reentrancy.fs:304` tests memo-body writes as defined behaviour, and Core.fs 4030-4034 defines a
pure memo's restriction as creating owned nodes, not writing signals.

**Owner's ruling needed:** are signal writes in a memo body supported?

- If no: say so in `contracts.md`, and consider raising as node creation does (Api.fs 236-238). The body-write forms of
  `self_marking_wake_lost`, `walk_passed_source_marked` and `check_resolves_over_moved_source` then stop being user-facing.
  This is a contract change: `Invalidation.fs:20-37` and several conformance scenarios would need rewriting.
- Either way, the cleanup-write and async forms stay open, and root cause 1 does not depend on this ruling at all.

## Recommended rulings for the 14 entries awaiting one

| Entry | Recommendation | Why |
|---|---|---|
| `failed_batch_leaves_queue` | **Fix.** | `RunBatch` calls `RequestFlush` after its `try/finally` (Core.fs 3296-3312), so a throwing body leaves effects queued over new signal values. `KeyedCollection.Edit` passes a user callback into the batch. Core.fs 3159 says effects run when the batch ends, while the guide describes the present behaviour. The fix must keep a flush failure from replacing the body's exception. Medium at most: any later value-changing write drains the queue. |
| `diamond_marks_running_effect` | **Defect, extra work only.** Write a failing test first. | Run: one write gives two runs of the effect, on every write. Each run sees final values. It contradicts the intent stated at Core.fs 385-394 and tested for neighbouring shapes (`Propagation.fs:24-42`). The fix is not small: see root cause 2's candidate. |
| `requeue_runs_after_later_entry` | Follows the diamond. Otherwise intended. | A requeue appends at the tail (3022-3033). No order between unrelated effects is promised. |
| `effect_restored_source_loop` | Keep "a body write to a read source requeues". **Separately, bound reruns in the untraced build.** | See "A hazard the ledger does not list" below. |
| `read_settles_writer` | Intended. **Document it.** | A host read of a stale memo can run queued effects before it returns, and answers the stored value at return (4539-4548, 3225-3248). Needed whatever happens to root cause 1. |
| `batch_restored_value_reruns` | Intended. | A signal compares a write with its stored value only. Cutoff belongs to memos and `createEffectOn`. Run: the same restore through a memo does not run the effect. |
| `redundant_run_reads_untracked` | Intended. | An untracked read owes no value between runs. The F# answer is the fresher one. |
| `disposed_unread_memo_value` | Keep the engine's behaviour. **Document it.** | Computing at disposal would contradict laziness and run user code during teardown. The gap is that the read returns `default`, which is `null` for records, lists and strings, with `Status.Uninitialized` still set. Raising instead would break `Reads.fs:39-49` and the scenario's trace. |
| `write_then_dispose_memo_current_value` | Intended. Close as spec corrected. | Documented (Core.fs 4371-4376, `memos.md:471-490`) and tested. |
| `disposed_window_keeps_unannounced` | Intended. | No observable difference. Private fields only. |
| `disposed_running_observer_stays_clean` | Intended. | Pinned by `DischargeReentry.fs:666-751` and `Reentrancy.fs:24-47`. Avoiding it costs a branch on every tracked read. |
| `reference_run_redundant` | Run set fixed at the round's start. | Model only. It decides whether the engine's redundant runs are defects or permitted cost. |
| `redundant_failing_cleanup_changes_settled` | No change to `Core.fs`. | Model only. Keep as a constraint: a redundant run with cleanups is not removable. |
| `logging_cleanup_drop_unobserved` | **Rule that re-run cleanups are part of the contract.** | `observe_settled` cannot see them, so a change that dropped cleanups could pass a proof stated over it. No theorem ties `Core.fs` cleanup execution to the reference. That assurance rests on the F# tests. |

`disposed_unread_memo_value` opens with "By the owner's ruling", sits under Awaiting ruling, and has no entry under
`## Rulings`. Its status should be made unambiguous.

## Unlisted issue: no re-run limit in the untraced build

**A non-converging self-writing effect is an infinite loop in the shipped build.**

Found through `effect_restored_source_loop`. The traced build raises after 1000 self-requeues (`Trace.fs:1806-1823`). The
untraced Release build, which is the NuGet package, has no bound: the write that starts the flush never returns, and the
queue grows by one slot per run (run: 100001 runs in 20 ms). This applies to any self-writing effect that does not
converge, not only the restore form in the ledger.

Related, by reading only: `docs/content/guide/cleanup.md:99` describes a cleanup's write as visible to the next body and
starting no further run. That is true of `createEffect`, whose `inCleanup` guard the ledger closed as
`in_cleanup_observer_stays_clean`. For `createEffectOn` both halves are false (`EffectSplit.fs:410-427` logs
`[1; 2; 3; 10]`), and the guide's own example shape moved to `createEffectOn` re-queues on every discharge. **Owner's
ruling needed:** should `createEffectOn` carry the same guard, or should the guide state the difference?

## Closed entries

None of the nine was closed too easily. Each rests on a doc comment, guide page or named test that was read. Two notes:

- `walk_covers_duplicate_observer` is closed on the premise that no observer is held twice. That holds at HEAD by reading
  all five link sites. It is a convention with no check behind it, and a break would be a missed wake-up.
- `self_writing_memo_ends_stale` (S14) is a visible facet of `self_marking_wake_lost`. Its end state should change when
  root cause 1 is fixed, and the scenario should be re-ruled then.

## What the proofs cover

**Inside the strong theorems** (`convergence`, `glitch_free_read`, `weak_refinement_except`): default-policy memos over
value types, strings, or the same instance each run, with pure bodies; effects that only read and log; a static graph; one
thread; .NET.

**Outside them:**

- memos returning fresh records, tuples, lists, options or arrays. This is the class root cause 1 lives in;
- effects that write, register cleanups or create child effects. These are "unproved by choice of scope, no proof
  attempted" (`BUGS.md`, Rulings);
- untracked reads in bodies, memo disposal, previous-value memos;
- twelve of the fourteen `IComputation` implementors: `EffectOn`, `AsyncMemo`, `Boundary`, `Binding`, `ValueRow`,
  `StreamMemo`, `Timed`, `FoldReads`, `FoldRow`, `ForwardingEdge`, projections and row watches. Neither the model nor the
  differential check reaches them;
- `createRoot` and the owner API, the dispatcher, the Fable build.

Outside the proofs is not untested: the ordinary suite covers these node types.

**Points on reading the theorems:**

- `no_lost_wakeups_unless_passed` is a bookkeeping invariant with a named exception, and the exception is reachable by
  the program above. The line "No lost wake-ups, with one named exception" in `README.md` invites a stronger reading.
- `weak_refinement_except` holds if the engine returns. The other direction is false in general
  (`engine_of_answers_falsified`).
- `optimised_trace_eq` is an equivalence between two models. The F# for the optimisation is on the unmerged branch
  `claude/notify-skip-epoch`, and its correspondence to the model rests on the differential check alone.
- The twelve Open statements are `Definition`s the engine falsifies. They share the `_statement` suffix with
  `read_quiescent_batch_free_statement`, which is unproved and unrefuted. A reader of the proofs site can take either kind
  for an established property.
- The model has one failure identity. F# raises a fresh exception per throw, so a memo that keeps failing re-wakes its
  readers each time. That is tested design (`Cutoff.fs:197-235`), and the model's failure cutoff describes the rarer case.
- The differential check detects drift between model and F#. A bug they share agrees, as `README.md` says.

**Gating.** `proofs-site.yml` runs the full audit on changes to `src/Ranvier/**` and `proofs/**`. By
`.github/rulesets/master.json` as checked in, neither proofs workflow is a required check, so `Core.fs` can change under a
red proofs job. Whether that file is the ruleset applied on GitHub was not verified. Either make the jobs required or
state in `README.md` that they are advisory.

## Corrections to the ledger

1. `BUGS.md:22` cites `MarkReaderCheck (4339)`. The call is at 4340.
2. `CORRESPONDENCE.md:181` says of the saturated-write skip "The F# side is not written", while line 187 describes it and
   `README.md` names the branch and commit that hold it.
3. The walk entries name two sites of nine (see root cause 1).
4. `self_marking_wake_lost` does not say in plain terms that the next value-changing write to the stale memo's sources
   notifies the observer (4499-4504). That recovery is what separates it from `walk_passed_source_marked`, whose observer
   receives no further notifications.
5. `self_marking_wake_lost` and `check_resolves_over_moved_source` cite the test comment as contrary evidence and omit
   `contracts.md:58-59`, which says the opposite.
6. `quiescent_read_answers_past_flush` does not say the empty batch in its memo is necessary. Without it the read returns
   with the woken effect queued and unrun (run).
7. `effect_restored_source_loop` omits the traced build's rerun guard, so it reads as if Ranvier has no answer to runaway
   effects.
8. `diamond_marks_running_effect` says it is outside `src/Ranvier`'s documents. Core.fs 385-394 names the spurious second
   run as what `NotifyDirtyExcept` prevents.
9. `README.md` says `refinable` excludes untracked reads, and `BUGS.md` says untracked reads are inside
   `effects_only_read`. Both can hold of different predicates. Each should name its predicate.
10. `CORRESPONDENCE.md` names eight node types outside the model. `Boundary` is an in-file implementor it does not name.

## Housekeeping

- **Stale binary.** `conformance/Harness.fsx:3` loads `src/Ranvier/bin/Release/net10.0/Ranvier.dll`. In this checkout that
  file is dated 2026-11-05, and the last `Core.fs` change is dated 2026-11-08. Run `dotnet build src/Ranvier -c Release`
  before re-running the scenarios locally.
- **Reflection coupling.** Both harnesses read private fields of the core by name, with no null check, so a rename fails
  with a `NullReferenceException` that does not name the field. Every name exists at HEAD.
- **The skip branch.** `origin/claude/notify-skip-epoch` is based on the pin, changes `Core.fs` in 26 hunks and
  `MarkDirty` from `unit` to `bool` across six other files, and lacks the heap tier. Merging it shifts every line
  reference past Core.fs 98 and needs the differential CI switched to `RANVIER_SKIP_MODE=all`.
- **The pin is prose.** Nothing checks `d701e6c5` or the line numbers mechanically. They go stale together at the next
  `Core.fs` change.

## Not established

- Whether any proposed fix passes the existing test suite.
- Whether the view layer contains the shape of root cause 1. `src/Ranvier.View*`, `src/Fable.Ranvier` and three examples
  hold no tracked files on `master`, so the usage survey covered the core, Elmish, Query, Mcp, CSharp, Dom, two examples
  and four samples.
- Whether `Ranvier.Query` reaches the async route on its own.
- Whether the `AsyncMemo`, `Boundary`, `Timed`, `StreamMemo` and `Projection` copies of the walk lose updates. They carry
  the same loop and were not run.
- Behaviour under Fable for every item here.
- How far `glitch_free_read`'s statement was narrowed when `glitch_free_read_statement` closed. `WholeGraph.v` was not
  read for it.

## Suggested order of work

1. A failing Expecto test from the view-model program above, then the revalidation fix across all nine sites.
2. A re-entrancy guard for memo cycles.
3. Rulings on memo-body writes and on writes to a signal read through a memo. These decide root cause 2.
4. `failed_batch_leaves_queue`.
5. A rerun bound in the untraced build, and the `createEffectOn` cleanup ruling.
6. The remaining rulings in the table, which unblock the heap tier.
