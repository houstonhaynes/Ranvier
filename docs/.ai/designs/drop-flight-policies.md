# Drop and DropWhilePending flight policies: design

**Status:** implemented on `feat/drop-flight-policies`, pending the §7 benchmark gate; §11 records how the
implementation differs from this note. Issue #61, "[Flight Policy] Drop new runs while one is in progress". Maintainer
decision: two new `FlightPolicy` cases, `Drop` and `DropWhilePending`, provided they add no cost to the existing
four. Line references are to `src/Ranvier/Core.fs` and `src/Ranvier/Types.fs` at `c4275bd`. Costs are read from the
code; nothing was measured. §6 names the benchmarks that check them.

## 1. Goal

Debounce and throttle (`docs/.ai/designs/debounce-throttle.md`) are timed gates on a source. They do not decide what
an async memo does with a change while its own flight is in progress. Of the four flight policies
(`Types.fs:65-96`), three start a new flight on every change and `FinishCurrent` folds the changes into one trailing
run. None discards the change outright, which is R3's `AwaitOperation.Drop` and Rx's `exhaustMap`: a submit button
that ignores clicks while the request is in flight.

- **`Drop`** discards every change that arrives during a flight. The flight's outcome, Ready or Failed, is applied as
  under `KeepLatest`, and nothing is replayed. The next change after the settle launches normally.
- **`DropWhilePending`** discards the changes while the flight is pending. A successful flight keeps its value and
  the changes stay discarded. A failed flight with at least one change discarded during it does not publish its
  error: the memo stays Pending and runs once more against the current inputs. A failed flight with no change
  discarded settles to its error as usual.

`FinishCurrent`'s design (§3 there) rejected a pure drop because the settled value pairs with inputs the graph has
left. That is now the requested contract for `Drop`, and for `DropWhilePending` on success. The generation check
(`Core.fs:3864-3871`) is unaffected: only one flight is in progress at a time, so `gen = generation` always holds at
the settle.

## 2. Proposed API

```fsharp
type FlightPolicy =
    | CancelPrevious
    | KeepLatest
    | Queue
    | FinishCurrent
    /// <summary>
    /// Discards every change that arrives while a flight is in progress. The flight's outcome is applied as under
    /// <c>KeepLatest</c>, and the next change after it settles starts a flight.
    /// </summary>
    /// <remarks>
    /// A discarded change runs no body and starts no flight, and the memo stays clean: the settled value is the
    /// current value until a source changes again, even though the inputs it was computed from have since changed.
    /// </remarks>
    | Drop
    /// <summary>
    /// Discards the changes that arrive while a flight is pending. A Ready outcome is applied as under <c>Drop</c>. A
    /// Failed outcome with a change discarded during the flight is replaced by one more run against the current
    /// inputs.
    /// </summary>
    /// <remarks>
    /// The memo stays Pending through the replacing run, and its <c>Previous</c> is the value published before the
    /// failed flight. A Failed outcome with no change discarded is applied as under <c>KeepLatest</c>.
    /// </remarks>
    | DropWhilePending
```

The cases are appended, so the existing `FlightPolicy+Tags` constants keep their values (0-3); `Drop = 4`,
`DropWhilePending = 5`. `ToString` (`Types.fs:100-105`) gains two arms. `GraphOptions.Default.WithFlightPolicy Drop`
selects it; C# reaches both as `FlightPolicy.Drop` and `FlightPolicy.DropWhilePending`. No other surface changes.

## 3. Semantics

"Discarded" means: no body run, no flight, no `CancellationTokenSource`, freshness returns to Clean, sources stay as
tracked by the flight in progress.

| Event | `Drop` | `DropWhilePending` |
| --- | --- | --- |
| Change, no flight in progress | Runs the body and starts a flight, as `KeepLatest` does. | Same. |
| Change during a flight, pulled (a reader reads the memo) | Discarded at `Start`. Status stays Pending. Records `RunDropped`. | Same, and the flight is marked as having discarded a change (`queued = 2`). |
| Change during a flight, not pulled before the settle | Discarded at the settle: freshness Dirty or Check returns to Clean, so the next read does not relaunch. | On success, discarded at the settle as under `Drop`. On failure, counts as a discarded change (row below). |
| Flight settles Completed, nothing discarded | Ready, value written, readers woken. | Same. |
| Flight settles Completed, changes discarded | Ready with the flight's value, readers woken. Nothing replayed; the memo is Clean. | Same as `Drop`. |
| Flight settles Faulted or Canceled, nothing discarded | Error, readers woken. | Same. |
| Flight settles Faulted or Canceled, changes discarded | Error, readers woken. Nothing replayed. | Outcome discarded (`FlightDrop`, reason `Retried`). Status stays Pending, freshness Dirty, readers notified dirty and a flush requested. Exactly one run starts at the next read; an effect reading the memo runs it in that flush. |
| Replacing run settles | — | As any flight: a failure with no change discarded during it settles to Error, so failures do not loop on their own. |
| Body suspends on a pending source (`NotReadyException`, `Core.fs:4235-4242`) | No flight started, `queued` stays 0, so the next change runs normally. | Same. |
| Dispose during a flight | Unchanged: token cancelled, `ObjectDisposedException` (`Core.fs:4371-4412`). The settle is dropped as `Disposed`. | Same; the replacing run is not started (`when owed && not disposed`). |
| Effect reading the memo | Every change during the flight still notifies it dirty (`MarkDirty`, `Core.fs:4344-4355`, unchanged); it re-runs, reads Pending and is woken by the settle. It sees the flight's value or error once. | Same on success. On a failure with changes discarded it is notified dirty, re-reads, and its read launches the replacing run; it never observes the discarded error. |
| `Previous.Settled` | Complete whenever the body runs: one flight at a time, `chained` never moves, so the waiter path is never taken (as `FinishCurrent`, `Core.fs:3804`). | Same. The replacing run's `Previous` is the value published before the failed flight, since a failure writes no value. |

**Demand-driven runs.** An async memo starts a flight only when pulled (`EnsureCurrent`, `Core.fs:4274-4278`). A
change that nobody pulls during the flight leaves freshness Dirty past the settle; under `KeepLatest` the next read
relaunches. Under `Drop` that relaunch would replay a discarded change, so the settle clears it. This is the second
discard site, and the one a pull-only design misses.

**Pending propagation.** Nothing waits on the discarded change. Under `Drop` every current settle ends in `wake ()`
(`Core.fs:4030-4043`), and under `DropWhilePending` a failure with a change discarded ends in the trailing arm's
`NotifyDirty` and `RequestFlush`, so a reader suspended on the memo is always re-run.

**Inline settle and body-time writes.** A change made during the body's synchronous part, before the settle attaches
(`Core.fs:4249-4266`), is also a change during the flight: it is discarded at the settle under both policies. The
existing policies relaunch on the next read instead.

## 4. How it works

Reuse the `queued` encoding `FinishCurrent` already uses (`Core.fs:3891-3896`): 0 no flight, 1 a flight in progress,
2 a flight in progress that has discarded (`Drop`, `DropWhilePending`) or deferred (`FinishCurrent`) a change. No new
field. The `queued` doc comment widens to name the three policies.

Changes, all in `AsyncMemo`:

1. **`Start`** (`Core.fs:4117-4126`). Replace the policy test with one that the three policies share:
   ```fsharp
   if queued <> 0 && not graph.Options.FlightPolicy.IsQueue then
       freshness <- Freshness.Clean
       queued <- 2
       Tracer.RunDeferred (graph, id, generation)      // FinishCurrent
       Tracer.RunDropped (graph, id, generation, 0)    // Drop, DropWhilePending; see §5
   ```
   `queued <> 0` already implies `Queue` or `FinishCurrent`; with the new cases it implies `Queue` or one of the three
   deferring policies, so `not IsQueue` selects exactly those. `IsQueue` and the current
   `match ... | FinishCurrent -> true | _ -> false` are each one `Tag` compare, so the cost under `Queue` with results
   outstanding is unchanged. Extending the match to three cases instead would compile to a `switch` and is avoided.
   The two trace calls are `[<Conditional("RANVIER_TRACE")>]`; choosing between them is a trace-build-only branch
   (pass the policy into one `Tracer` method, or test `IsFinishCurrent` inside the conditional call's argument).
2. **`Launch`**, token source (`Core.fs:4156-4167`): `Drop | DropWhilePending` join the `KeepLatest | Queue |
   FinishCurrent` arm (one shared source, retired once quiet).
3. **`Launch`**, settle attachment (`Core.fs:4252-4264`): `Drop | DropWhilePending` join the `FinishCurrent` arm
   (`queued <- 1`, `whenSettled`).
4. **`fail`** (`Core.fs:4184-4193`): unchanged; the `_` arm covers both.
5. **`retireIfQuiet`** (`Core.fs:3980-3986`): `Drop | DropWhilePending` join the `Queue | FinishCurrent when
   queued = 0` arm. Every flight settles into `queued = 0`, so, as under `FinishCurrent`, each flight allocates one
   source, never cancelled.
6. **`applyResult`**, first match (`Core.fs:3995-4007`), two new arms:
   ```fsharp
   | Drop ->
       queued <- 0
       if freshness <> Freshness.Clean then
           Tracer.RunDropped (graph, id, gen, 1)
           freshness <- Freshness.Clean
       gen = generation
   | DropWhilePending ->
       let dropped = queued = 2 || freshness <> Freshness.Clean
       queued <- 0
       owed <- dropped && not outcome.IsCompleted
       if dropped && not owed && freshness <> Freshness.Clean then
           Tracer.RunDropped (graph, id, gen, 1)
           freshness <- Freshness.Clean
       gen = generation
   ```
   `owed` then drives the existing code unchanged: `suspended` (`Core.fs:4012-4015`) is true, a Completed outcome
   cannot reach the held arm (`owed` implies a failure), and the failure arm (`Core.fs:4026-4029`) drops it. Its
   reason becomes `(if owed then (if FinishCurrent then 4 else 5) else 3)`, evaluated only in trace builds.
7. **`applyResult`**, final match (`Core.fs:4047-4059`): `DropWhilePending` joins the guarded trailing arm,
   `| FinishCurrent | DropWhilePending when owed && not disposed`, and `Drop | DropWhilePending` join the no-op arm.
   The arm sets freshness Dirty, notifies observers dirty and requests a flush: the replacing run starts at the next
   read, exactly as `FinishCurrent`'s trailing run.
8. **`MarkDirty`, `MarkCheck`, `Pull`, `Dispose`**: unchanged.

Under `Drop` a Check left at the settle is cleared without walking the sources. The sources it would have walked are
brought current by their own next read; the memo's value is not recomputed either way.

## 5. Trace

- **`TraceEventKind.RunDropped = 25`** (`TraceEvents.fs`, beside `RunDeferred = 24`): a change reached an async
  memo during a flight under `Drop` or `DropWhilePending` and was discarded. `Other`: the puller, or 0. `Arg`: the
  flight in progress. `Flag`: 1 when discarded at the settle, never pulled; 0 when discarded at `Start`. `Cause`: the
  first dirty `Mark` since the previous run. `Tracer.RunDropped` in `Trace.fs` mirrors `Tracer.RunDeferred`
  (`Trace.fs:1316-1330`). Add the Fable name in `TraceNames.eventKind`.
- **`TraceDropReason.Retried = 5`**: a failure arrived after a change was discarded under `DropWhilePending`; the
  memo runs again. Add the Fable name in `TraceNames.dropReason` (`TraceEvents.fs:253-260`).
- **`Settle.Flag`** is unchanged: `DropWhilePending` never holds a Completed outcome, and `Drop` never holds.
- **Replacing run's notification.** It reuses `Tracer.TrailingRun` (`Trace.fs:1337-1346`), whose cause is
  `log.SettleOf id`. `FlightDrop` does not note a settle, so after a dropped failure that cause is not the drop. Have
  `Tracer.FlightDrop` note its sequence number for reasons `Trailing` and `Retried`, so the notification's cause is
  the discarded failure (the same gap exists for `FinishCurrent` today; see §10).
- `TraceModel` (`TraceModel.fs:550`) already maps any `FlightDrop` flag to `Dropped (seq, reason)`; no change.

## 6. Cost model

**Code that uses the existing four policies.**

| Path | `Drop` | `DropWhilePending` |
| --- | --- | --- |
| Signal write, memo recompute, flush, effect run, `MarkDirty`, `Pull` | None. No code on these paths changes. | None. |
| `Start` (once per launch) | None. `queued <> 0` is false for `CancelPrevious` and `KeepLatest`. Under `Queue` with results outstanding and under `FinishCurrent` with a flight in progress, one `Tag` compare, as today (`IsQueue` in place of the `FinishCurrent` test). | Same; shares the branch. |
| `Launch`, `retireIfQuiet`, `applyResult` matches | None. New cases join or add arms of matches on `FlightPolicy`, which compile to a `switch` on `Tag`; the targets taken by the existing cases do not change. | Same. |
| `applyResult` `suspended` and outcome match | None. Unchanged code; `owed` stays false outside `FinishCurrent` and `DropWhilePending`. | Same. |
| Memory per `AsyncMemo` | None: `queued` is reused. | None. |
| Allocations | None. | None. |
| Trace builds | Argument choice in a `Conditional` call; removed with the call in release builds. | Same. |

Both policies can be free together; neither needs a field or a shared test that the other avoids. The one shared
site, `Start`, is free only if it is written as `not IsQueue`; a three-case match there would replace one compare
with a `switch` under `Queue` and `FinishCurrent`. A dedicated `byte` field for the "discarded" flag, or reading
`freshness` outside the new arms, would cost the other policies, and the design does neither. The JIT may lay out a
six-entry `switch` differently from a four-entry one; §7's gate checks that the instruction counts hold.

**Code that uses `Drop` or `DropWhilePending`.** A discarded change allocates nothing and runs no body. For N changes
during a flight, `Drop` costs one body run and one flight in place of `KeepLatest`'s N + 1; `DropWhilePending` costs
the same on success and one more run on failure.

## 7. Benchmarks and counters

- `bench/Ranvier.Benchmarks/Suspension.fs:230-244`: add `"Drop"` and `"DropWhilePending"` to `FlightBenchmarks`'
  `Policy` `Params` and to the `Setup` match. Expected under both: `WritesDuringFlight` makes one body run and
  allocates independently of `Writes`. The master side of the A/B run drops the two names from `Params`; the
  existing four must show no time or allocation change.
- `bench/Ranvier.Counters/Scenarios.fs:1060-1069`: add `"Ranvier (Drop)"` and `"Ranvier (DropWhilePending)"` to the
  `flight` scenario. The four existing engines' `InstructionRetired` (run-to-run variance about 0.5 %) is the gate
  for §6's "none". The new engines have no history; the counters history reader must tolerate a new engine label
  (check against the fix in PR #86).
- A failing-flight case (`DropWhilePending` replacing run) is optional; no existing scenario faults a flight.

## 8. Tests

`tests/Ranvier.Tests/Async.fs`, under `ManualDispatcher`, a `Drop` graph and a `DropWhilePending` graph beside the
`FinishCurrent` helper (`Async.fs:33-38`):

`Drop`
1. Discards the changes during a flight and keeps its value: two pulled writes, `Runs = 1`, Ready with the first
   inputs' value, and a later read does not relaunch.
2. Discards a change no reader pulled during the flight: write with no observer, settle, read; `Runs = 1`.
3. Runs the next change after the flight settles: `Runs = 2`, value from the new inputs.
4. Settles a failed flight with a discarded change to its error and replays nothing.
5. Disposed during a flight runs nothing more.
6. Does not hold a run back behind a body suspended on a pending source.
7. Wakes an effect that read the memo while pending; the effect sees the value once.
8. Hands the settled value to the next run as `Previous` (.NET only: reads `Settled` synchronously).

`DropWhilePending`
1. Keeps a successful flight's value and discards the changes during it; no relaunch on read.
2. Discards an unpulled change on success.
3. Replaces a failed flight that discarded a change with one run: status stays Pending, `Runs = 2`, the second run
   sees the current inputs, then Ready.
4. Replaces a failed flight that discarded an unpulled change.
5. Settles a failed flight with no change discarded to its error, `Runs = 1`.
6. A replacing run that fails with no change discarded settles to Error (no loop).
7. Disposed with a failure owed runs nothing more.
8. Starts the replacing run for an effect that reads the memo; the effect never observes the discarded error.
9. The replacing run's `Previous` is the value published before the failed flight (.NET only).
10. Does not hold a run back behind a body suspended on a pending source.

Elsewhere:
- `Tracing.fs`: `RunDropped` with `Flag` 0 and 1; `FlightDrop` with `TraceDropReason.Retried`; the replacing run's
  notification cause.
- `Retention.fs:40` and `AsyncEdges.fs:835`: add both cases to the policy loops.
- `Texts.fs:19`: `ToString` for both.
- `tests/Ranvier.CSharp.Tests/AsyncTests.cs`: one test per policy through `FlightPolicy.Drop` and
  `FlightPolicy.DropWhilePending`.
- `docs/maps/authoring/MapFence.fs:38-39` and `docs/maps/tests/AuthoringTests.fs:371-386`: `policy=drop` and
  `policy=drop-while-pending`.
- The Fable suite runs the same cases; record them in `docs/.ai/fable-compat.md` (untraced, traced and .NET-only
  tables, as at lines 304-348 and 508-509).

## 9. Docs and compatibility

- `docs/.ai/public-api-baseline.txt`: `FlightPolicy+Tags Drop`, `FlightPolicy+Tags DropWhilePending`, `get_IsDrop`,
  `get_IsDropWhilePending`, `static get_Drop`, `static get_DropWhilePending`. Binary compatible; source-breaking for
  an F# caller who matches `FlightPolicy` exhaustively (FS0025, `MatchFailureException` on the new cases). Ranvier is
  unreleased.
- `docs/content/guide/async-memos.md` (the async-and-pending guide): policy table (line 154), a "When to use Drop"
  details block beside `FinishCurrent`'s (line 198), the R3 and SignalsDotnet mapping table (`Drop` maps to R3
  `AwaitOperation.Drop`; line 223), token-source cost (lines 255-270) and the `Previous` paragraph (lines 322-323).
- `docs/content/concepts/async-graph.md` (lines 69, 90), `concepts/contracts.md` (line 144), `concepts/roadmap.md`
  (line 30; issue #61 closes).
- `docs/content/guide/tracing.md` (lines 244-251): `retried` drop reason and `RunDropped`.
- `docs/content/guide/signal-maps.md` (line 197): the two `policy=` flags.
- `docs/content/benchmarks/suspension.md`, `docs/content/benchmarks/counters.md` (line 79) and
  `docs/.ai/benchmarks/README.md` (lines 47, 222): policy lists and result rows.
- `Types.fs`: the two case docs in §2; `Core.fs:3804` (`Previous` remarks), `3873-3877` (`cts`), `3891-3896`
  (`queued`), `3977` (`retireIfQuiet`) and `4115` (`Start`) name the new policies.

Fable and AOT: two nullary cases and no new field; outcomes already arrive on a microtask. No `#if`, no reflection.

## 10. Open questions

1. `DropWhilePending`'s replacing run starts at the next read (lazy, as every async run and `FinishCurrent`'s trailing
   run). An unobserved memo runs it on its next read; an observed one runs it in the flush the settle requests. Is
   lazy acceptable, or must the run start eagerly inside `applyResult`? Eager needs `applyResult` to call `Launch`,
   which it cannot reach today (a `let`-bound closure before the members).
2. Should `Drop` discard a change made by the body's own synchronous part (§3, inline settle)? The design does.
3. `FinishCurrent`'s trailing-run notification has the same cause gap as §5's replacing run; fix both together?
4. Per-memo policy remains a separate design (`finish-current-flight-policy.md` §8): a submit memo wants `Drop` while
   a search memo in the same graph wants `CancelPrevious`.

## 11. Implementation notes

1. **Discarding at the settle brings the sources current.** §4 step 6 clears a Dirty or Check freshness at the
   settle without walking the sources. That strands a memo source: a `Memo` left Dirty or Check notifies its
   observers only on its Clean transition (`Memo.MarkDirty`, `Memo.MarkCheck`), so once the async memo is Clean,
   the source's next change never reaches it and the memo serves the settled value forever. The same holds for a
   change discarded at `Start` when the pull's check walk stopped early on a Dirty freshness. The settle therefore
   runs `refreshSources` (each source's `UpdateIfNecessary`, inside `EnterPull`/`ExitPull`, as `Pull`'s check walk)
   before clearing freshness, whenever `queued = 2` or freshness is not Clean. `RunDropped` with `Flag` 1 is recorded
   only when freshness is still Dirty after the walk; a Check that resolves unchanged is no discarded change. Two
   tests pin it: "Drop keeps a memo source current across a change discarded at the settle" and "... at a read".
   The walk is in the `Drop` and `DropWhilePending` arms only and costs the other policies nothing. A source memo's
   body may now run at the settle rather than at a later read, and an async memo source left Dirty starts its
   flight at the settle, with no read. Its own settle then notifies the memo and runs it: the source's value moved
   after the memo settled. Skipping async sources in the walk would strand the memo again (an async memo left Dirty
   also notifies only on its Clean transition), and a memo source can itself start an upstream flight as it
   recomputes, so no walk avoids it. Absorbing the source's later settle would need a test on `MarkDirty`, which
   every policy pays. "Drop starts an async memo source's flight at the settle and runs on its value" and its
   `DropWhilePending` twin pin it.
2. **`DropWhilePending` failure test.** A Check alone at a failed settle is resolved by the same walk first, so a
   memo source that recomputes to the same value does not owe a replacing run. `owed` is set when `queued = 2` or
   the walk leaves freshness Dirty.
3. **Disposed.** Both arms skip the discard when the memo is disposed; the outcome is dropped as `Disposed`.
4. **Trace.** `Tracer.RunDeferred` takes a `deferred` flag, `graph.Options.FlightPolicy.IsFinishCurrent`, evaluated
   only in trace builds, and records `RunDropped` (`Flag` 0) when false. `Tracer.RunDropped` records the settle-time
   discard (`Flag` 1). Both forget the node's first dirty mark (`TraceLog.ClearDirty`), so the next run's cause is
   its own mark rather than the discarded one. `Tracer.FlightDrop` notes its sequence number for reasons `Trailing`
   and `Retried`, so the trailing or replacing run's notification names the dropped failure; this closes §10 open
   question 3 for `FinishCurrent` too. The signal-map model renders the `retried` reason.
5. §10 questions 1, 2 and 4 are answered as the design proposed: the replacing run is lazy, a body-time change is
   discarded, and per-memo policy stays a separate design.
