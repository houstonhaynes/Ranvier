# Ranvier core proofs

Rocq/Iris development over the synchronous reactive core in `src/Ranvier/Core.fs`, pinned at
commit `d701e6c5`.

## Commands

- Build: `bash proofs/build.sh`
- Audit: `bash proofs/check.sh`
- Differential check: `bash proofs/differential/run.sh [count] [seed]`
- Documentation: `bash proofs/docs.sh`, which prints where the pages are. Start at the Guarantees page.
- Navigator site: `bash proofs/site.sh`, which writes `site/output`. Published at `/proofs/` of the documentation site.

All five run from any directory. See `VERSIONS.md` for the toolchain. The audit and the differential check also need the
.NET SDK, since they rebuild `src/Ranvier`. Under WSL they run the Windows `dotnet.exe`.

## Status

The reference and pure tiers are complete apart from the unproved obligations under `## Unproved obligations` in
`BUGS.md`, and include the optimisation result `optimised_trace_eq`. The heap tier holds
its foundation: `Heap/ObserverSet.v`, `Heap/SourceList.v` and `Heap/Ghost.v`, every specification closed under the
global context.

The heap layers above the foundation (marking, runs, the scheduler, ownership) and heap adequacy are deferred until the
rulings on the bugs in `BUGS.md` are made and applied, since each ruling that changes the engine changes what those
layers prove. Their obligations are listed in `Heap/Ghost.v`'s header. Each Awaiting-ruling item in `BUGS.md` will be
tracked as a GitHub issue labelled `proofs`.

## The link to the F#

Rocq cannot read F#. The theorems are about the model, and they reach `Core.fs` only through `CORRESPONDENCE.md`. The
differential check is testing evidence for that table, and it is not a proof: agreement says the model and the F# gave one
trace on the programs generated, and says nothing of other programs. Agreement also says nothing of whether the behaviour
is right. A program can reach a Confirmed entry of `BUGS.md` and agree, since the model has that bug too.

`differential/run.sh` generates programs, runs each on real `Signal`, `Memo` and `Effect` objects (`Generate.fsx`) and on
the extracted interpreter (`driver.ml`), and compares the traces and the end states. It exits nonzero on the first
difference, printing the program, both sides and the seed, and also when 5% or more of the programs are skipped, when more
than 1% are excluded, or when the compared programs fall below a coverage floor.

The end states are `observed` in `theories/Extract.v` and `Interpreter.View` in `Generate.fsx`: each node's value, status,
failure, freshness, `unannounced`, runs, disposal, queued flag, and source and observer counts, with the queue length and the
root's error count. They are compared at the program's end and again after a closing that reads every memo and flushes. The
first keeps a stale node visible, and the second shows what it was hiding. **The view reads private fields by name, so
refactoring one of these fields in `Core.fs` means updating the view.** `.github/workflows/proofs-differential.yml`
runs it when `proofs/`, `src/Ranvier/Core.fs` or the files compiled before it change, with the run number as the seed, so a
run tries new programs. A change the generated programs do not distinguish passes, so the check narrows drift and does not
eliminate it. The watchdog covers the F# side only: the workflow's `timeout-minutes` bounds the model driver.

What the comparison leaves out, each reported by `run.sh`:

- **Pure-scope violations.** A pure memo's body creates no node or cleanup, by construction: the F# raises a new exception
  each time, and the model has one failure identity. `run.sh` prints how many draws this drops.
- **Repeated zero divisors.** The same reason applies: a program raising more than one is excluded when it runs, up to a cap
  of 1%.
- **Programs that diverge on both sides.** The model has no result, and the F# passed its budget or ended its process
  by a stack overflow or the watchdog (`fatal`). Any other exit of the F# process fails the check. Neither has a
  complete trace to compare. `self_read_check_overflow` in `BUGS.md` is how a generated program overflows. One side
  diverging alone fails the check, as does a stuck program.

The generated programs have no `createRoot`, no owner API, no teardown of the root, no comparer API wrappers, no async nodes
and no second thread, since the command language has none. `Graph.Flush` appears only in the closing. Writes from bodies keep
to a higher creation index than the body's reads, or sit under a guard that stops changing the value, apart from a template
whose effect cleanup writes its own source under the core's `inCleanup` guard.

After changing the interpreter, copy the generated files: `bash proofs/build.sh`, then
`cp proofs/engine.ml proofs/engine.mli proofs/differential/`. `check.sh` fails until they match. CI compiles the committed
copy, so it needs no Rocq.

## Layout

- `theories/Base/Prelude.v`: identifiers and the freshness order.
- `theories/Base/Syntax.v`: the command language.
- `theories/Reference/Semantics.v`: the meaning of a program and an input history, observations and
  the refinement relation.
- `theories/Reference/Properties.v`: determinism, glitch freedom, refinement, redundancy, examples.
- `theories/Pure/ObserverSet.v`: `ObserverSet`, and the backward re-clamped walk.
- `theories/Pure/SourceList.v`: `SourceList` over both ends of every edge, and the edge invariants.
- `theories/Pure/Witnesses.v`: checked counterexamples and behaviour witnesses.
- `theories/Pure/Ledger.v`: the witnesses behind the ledger closures of the pure-tier report.
- `theories/Pure/WholeGraph.v`: the behaviour of a whole program: `gh_passed`, reads and convergence, over the fragment of well-behaved programs.
- `theories/Pure/Refinement.v`: the engine program of a reference program, and refinement to the reference semantics.
- `theories/Pure/State.v`: the graph state, and `edge`.
- `theories/Pure/Edges.v`: edge coherence over the source-list operations.
- `theories/Pure/Marking.v`: freshness marks and the walks that spread them.
- `theories/Pure/Check.v`: check resolution, over an invariant held by every engine function.
- `theories/Pure/Engine.v`: the executable interpreter of the command language over the graph state.
- `theories/Pure/Runs.v`: the wake-up statement, the invariant every engine function keeps, and what a memo's run guarantees.
- `theories/Pure/Scheduler.v`: the scheduler's invariant over every engine function, the queue, and flushes.
- `theories/Pure/Ownership.v`: teardown order, each cleanup once, disposal and late registrations.
- `theories/Heap/ObserverSet.v`: `ObserverSet` in HeapLang, with its specifications against the pure functions, and the walk.
- `theories/Heap/SourceList.v`: `SourceList` in HeapLang over a heap edge world, with its specifications against the pure functions.
- `theories/Heap/Ghost.v`: the ghost state of the heap graph and `is_graph`.
- `conformance/Harness.fsx`: the graph with its trace, and reflection over the core's private state.
- `conformance/Scenarios.fsx`: the scenarios on real `Signal`, `Memo` and `Effect` objects.
- `theories/Conformance/Expected.v`: their traces, generated by `Scenarios.fsx`.
- `theories/Conformance/Check.v`: the scenarios as programs, each checked against its trace.
- `theories/Extract.v`: the end-state view and the closing, and the extraction of `trace_of`, `observed` and `current_flags` to OCaml.
- `differential/engine.ml`, `differential/engine.mli`: the committed extraction.
- `differential/driver.ml`: reads programs and runs the extracted `observed`.
- `differential/Generate.fsx`: the program generator, the text form, and the interpreter over real objects.
- `differential/run.sh`: the comparison.
- `theories/Audit.v`: one `Print Assumptions` per theorem; `check.sh` reads it.
- `docs.sh`, `docs/guarantees.py`, `docs/ranvier.css`, `docs/ranvier.js`: the pages, the Guarantees overview and their checks.
- `site.sh`, `site/`: the Navigator site, built with `FSharp.Data.Rocq` and `Nacara.Plugin.Rocq`.
- `BUGS.md`: defect ledger. `CORRESPONDENCE.md`: model-to-F# map.

## Theorem index

| Theorem | File | Claim |
|---|---|---|
| `reference_trace_deterministic` | `Reference/Properties.v` | Agreeing orders and any two sufficient fuels give one trace. |
| `memo_value_extensional` | `Reference/Properties.v` | With the same disposed memos, a memo's value depends only on the signals its evaluation discovers. |
| `glitch_free_by_definition` | `Reference/Properties.v` | The run holding an `EvLog`, when it writes no signal, has its reads and events computed from one signal snapshot and its cleanups' captured environments, which can hold values from an earlier snapshot. |
| `reference_run_redundant` | `Reference/Properties.v` | Witness: the reference tier has a redundant run when an effect restores a value mid-round. |
| `untracked_value_not_redundant` | `Reference/Properties.v` | A run whose untracked read moved is not redundant. |
| `untracked_branch_not_redundant` | `Reference/Properties.v` | A run whose reads are a subset of the previous run's, selected by an untracked read, is not redundant. |
| `zero_divisor_later_effects_run` | `Reference/Properties.v` | A zero divisor fails its effect, and later effects run. |
| `observe_settled_coarser` | `Reference/Properties.v` | `observe_full` equality implies `observe_settled` equality. |
| `refines_full_implies_settled` | `Reference/Properties.v` | An engine refining `observe_full` refines `observe_settled`. |
| `reference_refines_itself` | `Reference/Properties.v` | The reference under a valid order and any fuel refines `observe_full`, at every batch prefix, with extending traces. |
| `reference_trace_answers` | `Reference/Properties.v` | The reference indexed by its fuel satisfies `engine_answers` under a valid order. |
| `final_batch_engine_not_refines` | `Reference/Properties.v` | An engine answering only a full history does not refine `observe_settled`, though its full-history view matches. |
| `last_run_traces_match_every_prefix` | `Reference/Properties.v` | Traces keeping only each prefix's last run match `observe_settled` at every prefix of a history. |
| `last_run_engine_not_refines` | `Reference/Properties.v` | An engine answering those traces does not refine `observe_settled`: its traces do not extend. |
| `reference_trace_extends` | `Reference/Properties.v` | A history's trace extends each batch prefix's trace, under the same order and fuel. |
| `redundant_failing_cleanup_changes_settled` | `Reference/Properties.v` | Witness: removing a redundant run whose cleanup fails changes `observe_settled`. |
| `logging_cleanup_drop_unobserved` | `Reference/Properties.v` | Witness: `observe_settled` admits dropping a re-run's non-failing logging cleanup. |
| `host_read_sees_earlier_write` | `Reference/Properties.v` | A host read answers at its position in the batch. |
| `host_read_then_dispose_memo` | `Reference/Properties.v` | A memo read and then disposed in one batch keeps the value the read answered. |
| `dropped_write_unobserved_before_read` | `Reference/Properties.v` | Dropping a write to a signal no effect logs leaves `observe_settled` unchanged. |
| `dropped_write_observed_by_host_read` | `Reference/Properties.v` | A later host read makes that dropped write observable under `observe_settled`. |
| `silent_engine_not_refines` | `Reference/Properties.v` | An engine that emits nothing does not refine `observe_settled`. |
| `none_engine_refines` | `Reference/Properties.v` | An engine with no traces refines every observation. |
| `none_engine_not_answers` | `Reference/Properties.v` | That engine fails `engine_answers`. |
| `failing_round_later_rounds_run` | `Reference/Properties.v` | A failed run emits `EvFail`, and later rounds run. |
| `dispose_runs_cleanups_once` | `Reference/Properties.v` | Disposing an effect runs its cleanups once and ends its runs. |
| `disposed_memo_answers_last_value` | `Reference/Properties.v` | A disposed memo answers reads with its last value. |
| `order_varies_by_round` | `Reference/Properties.v` | The effect order can differ between rounds of a history. |
| `redundant_run_settled_equal` | `Reference/Properties.v` | On the cleanup-free `resetting` program, traces differing by a redundant run have equal `observe_settled`. |
| `redundant_run_full_differs` | `Reference/Properties.v` | The same traces have unequal `observe_full`. |
| `observers_add_position` | `Pure/ObserverSet.v` | The handle is the position, the view appends, and `observer_latest` is the handle. |
| `observers_remove_at_swap` | `Pure/ObserverSet.v` | The last observer moves into the hole and the notice names it. Removing the last gives no notice. |
| `observers_remove_mismatch` | `Pure/ObserverSet.v` | A handle holding another observer, or none, gives `None`. |
| `observers_try_add_cases` | `Pure/ObserverSet.v` | At or below `scan_limit`, `TUnknown` is impossible and `TUnlinked` holds exactly when the observer is held. |
| `walk_covers` | `Pure/ObserverSet.v` | An observer held exactly once at every point of a walk is visited, under any mutations. |
| `walk_visits_only_seen` | `Pure/ObserverSet.v` | Each visited observer was present at its visit. |
| `sources_add_positional` | `Pure/SourceList.v` | A read matching the cursor's slot advances the cursor and leaves every edge unchanged. |
| `sources_add_repeat` | `Pure/SourceList.v` | A read of the source just matched leaves the world unchanged. |
| `sources_add_truncates` | `Pure/SourceList.v` | A miss drops every slot from the cursor and the edges they held, then appends. |
| `sources_no_duplicate_edge` | `Pure/SourceList.v` | In a reachable world, a slot holds an edge exactly when it is its source's first slot. |
| `trim_moves_only_others` | `Pure/SourceList.v` | Over `edge_match` and `slots_unique`, a removal moves only another computation's edge. |
| `trim_every_removal` | `Pure/SourceList.v` | `trim_loop` keeps both invariants at every removal, with each observer held at most once and only others' edges moved. |
| `observer_held_once` | `Pure/SourceList.v` | In a reachable world or between two removals of its trim, a present observer is `held_once`: the premise of `walk_covers`. |
| `sources_trim_moves_own_edge` | `Pure/SourceList.v` | Review Focus 4: for A, B, A with two A edges, the later handle is read after the move, and a clear removes every edge. |
| `sources_add_unknown_no_duplicate` | `Pure/SourceList.v` | Review Focus 5: above the limit with a stale `observer_latest`, the answer is `TUnknown` and the source ends with one edge. |
| `unknown_held`, `unknown_unheld` | `Pure/SourceList.v` | Runs reaching that case, with and without an earlier slot. |
| `aba_reachable_trim` | `Pure/SourceList.v` | A, B, A from a run: a trim moves another observer's edge, and its slot follows. |
| `aba_world_premises` | `Pure/SourceList.v` | A concrete world meets the premises of `sources_trim_moves_own_edge`. |
| `snapshot_trim_fails` | `Pure/SourceList.v` | Handles read before the removals fail on that world. |
| `walk_covers_duplicate_observer` | `Pure/Witnesses.v` | An observer held twice can be missed though present throughout. Closed in `BUGS.md`. |
| `walk_visits_moved_twice` | `Pure/Witnesses.v` | A moved observer is visited twice. |
| `walk_visits_added_observer` | `Pure/Witnesses.v` | An observer added during a walk is visited when a later removal moves it below the cursor. Closed in `BUGS.md`. |
| `coherent_begin` | `Pure/Edges.v` | `sources_begin_run` preserves `coherent`. |
| `coherent_add`, `coherent_end`, `coherent_clear` | `Pure/Edges.v` | From a coherent world, each returns `Some`, and the result is coherent. |
| `coherent_empty` | `Pure/Edges.v` | The empty world is coherent. |
| `coherent_moved` | `Pure/Edges.v` | Removing a slot's edge returns a coherent world, of a stated shape with and without a move. A moved observer takes the removed handle, and its slot holds it. |
| `moved_alone_breaks_coherent` | `Pure/Edges.v` | A move to a different handle with no removal breaks coherence. |
| `remove_edge_alone_breaks_coherent` | `Pure/Edges.v` | A removal with no blanking of the removed slot breaks coherence. |
| `edge_iff_both_sides` | `Pure/Edges.v` | In a coherent world, an edge holds exactly when `c` is in `src`'s observers, and exactly when a slot of `c` holds `src` with a handle. |
| `edge_iff_read` | `Pure/Edges.v` | In a well-formed world, an edge holds exactly when `src` is in `c`'s slots. |
| `read_without_edge` | `Pure/Edges.v` | A coherent world where a computation read a source and has no edge to it, so `first_owns` fails. |
| `check_invariant_init`, `check_invariant_run_top`, `check_invariant_update` | `Pure/Check.v` | `check_invariant` holds of a new graph and after every function of the engine, for all programs and fuel. `engine_ok_all` is the induction. |
| `unmoved_short_of_dirty` | `Pure/Check.v` | Under `check_invariant` and `reads_settled`, a memo in `Clean` or `Check` has `moved_since_last_run` false. |
| `resolve_clean_unmoved` | `Pure/Check.v` | `update_if_necessary` taking a memo from `Check` to `Clean` leaves no moved source, under `reads_settled`. It follows from `check_invariant`'s preservation and `unmoved_short_of_dirty`, without using the walk. |
| `resolve_clean_no_move_statement` | `Pure/Check.v` | **Open statement.** The unqualified statement, false on the present engine: `resolve_clean_no_move_falsified`. |
| `resolve_clean_no_stale_source_statement` | `Pure/Check.v` | **Open statement.** A memo resolved `Clean` has no live stale source, false on the present engine: `resolve_clean_no_stale_source_falsified`. A disposed source stays stale: `resolve_over_disposed_dirty_source`. |
| `resolve_dirty_stops_walk`, `resolve_walk_step` | `Pure/Check.v` | A node out of `Check` ends the walk with the state unchanged. In `Check`, the walk updates the slot's source and moves to the next slot. |
| `resolve_clears_unannounced` | `Pure/Check.v` | `update_if_necessary` taking a memo from `Check` to `Clean` ends a live memo's `n_unannounced`, on every path. A disposed memo can keep it: `disposed_window_keeps_unannounced`. |
| `resolve_walk_check_clears` | `Pure/Check.v` | A walk ending in `Check` makes `refresh` return the node `Clean` with `n_unannounced` false and no run. |
| `signal_update_noop` | `Pure/Check.v` | `update_if_necessary` on a signal returns the state unchanged. |
| `disposed_memo_no_refresh` | `Pure/Check.v` | A read of a disposed memo keeps every node, and answers the stored value or failure. |
| `check_resolves_over_moved_source`, `resolve_clean_no_move_falsified` | `Pure/Witnesses.v` | A memo resolves to `Clean` over a source that moved after its read. Confirmed. |
| `check_two_reads_over_moved_source` | `Pure/Witnesses.v` | The same skip through a second read of the source. |
| `resolve_clean_no_stale_source_falsified`, `disposed_window_keeps_unannounced` | `Pure/Witnesses.v` | A reader resolves `Clean` over a `Dirty` source. A memo disposed in `RecomputeScoped`'s window ends `Clean` with `n_unannounced`. |
| `check_cutoff_settled`, `self_marking_through_memo_unmoved` | `Pure/Witnesses.v` | `reads_settled` holds on an ordinary resolution, and on the reader of `self_marking_wake_lost`, which goes clean over its source's stored value. |
| `mark_terminates`, `notify_terminates` | `Pure/Marking.v` | Fuel above `mark_measure` gives a result for every mark and walk, on any graph, cycles included. |
| `mark_monotone` | `Pure/Marking.v` | Freshness only rises, for every node, under all five marking operations. |
| `dirty_not_lost` | `Pure/Marking.v` | A `Dirty` node stays `Dirty` under `mark_check`. |
| `mark_preserves_edges` | `Pure/Marking.v` | All five marking operations leave `g_edges` unchanged. Covers `Memo` and `Effect` marks. |
| `notify_walk_unmutated` | `Pure/Marking.v` | Each marking walk is a `walk` with empty mutations, folding its mark over the observers at its start. |
| `notify_stales` | `Pure/Marking.v` | Each walk leaves each observer in `takes_marks` stale, apart from the skipped computation. |
| `mark_forwards_on_clean` | `Pure/Marking.v` | A clean memo goes stale, and each observer in `takes_marks` ends stale. |
| `mark_forwards_on_unannounced` | `Pure/Marking.v` | With the flag set, a stale memo holding an announcement does the same, and spends it. |
| `effect_mark_queues` | `Pure/Marking.v` | An effect outside disposal and cleanups ends queued, with at most one pending entry. |
| `mark_keeps_queue_once` | `Pure/Marking.v` | All five marking operations keep `queue_once`, for every node. |
| `disposed_effect_ignores_marks`, `in_cleanup_ignores_marks` | `Pure/Marking.v` | Such an effect's marks return the state unchanged. |
| `mark_dirty_ends_dirty` | `Pure/Marking.v` | `mark_dirty` leaves a memo or an effect in `takes_marks` `Dirty`, an already-queued effect included. |
| `notify_dirty_ends_dirty` | `Pure/Marking.v` | `notify_dirty` and `notify_dirty_except` leave each observer in `takes_marks` `Dirty`, apart from the skipped computation. |
| `mark_check_keeps_check` | `Pure/Marking.v` | `mark_check` and `notify_check` raise no node to `Dirty`. |
| `memo_mark_dirty_keeps_others_short` | `Pure/Marking.v` | `mark_dirty` of a memo raises no other node to `Dirty`. |
| `clean_memo_keeps_unannounced` | `Pure/Marking.v` | The `Clean` transition starts its walk with `n_unannounced` unchanged. |
| `in_cleanup_observer_stays_clean` | `Pure/Witnesses.v` | A memo's in-cleanup observer, with a coherent edge, stays clean, which `takes_marks` excepts. Closed in `BUGS.md`. |
| `disposed_running_observer_stays_clean` | `Pure/Witnesses.v` | An effect disposed mid-body holds an edge while running, and ignores the mark. Awaiting ruling in `BUGS.md`. |
| `self_marking_cleanup_left_behind` | `Pure/Witnesses.v` | A memo's cleanup marks its reader during the reader's run: the reader ends stale, its effect clean and idle, and a flush changes nothing. Confirmed in `BUGS.md`. |
| `self_marking_body_left_behind` | `Pure/Witnesses.v` | The same end state from a memo writing its own source. Confirmed in `BUGS.md`. |
| `self_marking_through_memo_left_behind` | `Pure/Witnesses.v` | The same end state through a reading memo. Confirmed in `BUGS.md`. |
| `read_move_leaves_effect_queued` | `Pure/Witnesses.v` | A read of the left-behind memo queues its effect and owes no flush. Confirmed in `BUGS.md`. |
| `nested_self_run_ends_clean_and_wrong` | `Pure/Witnesses.v` | A nested self-run trims an edge the outer run read, and the memo ends clean with the outer value. Confirmed in `BUGS.md`. |
| `self_read_check_resolves_itself` | `Pure/Witnesses.v` | A self-reading memo in `Check` has itself as its first slot, and a read of it has no result at fuel 500 or 2000. Confirmed in `BUGS.md`. |
| `self_read_second_resolves_itself` | `Pure/Witnesses.v` | The same with the self-read second, behind a source that holds its value. |
| `mutual_read_check_resolves_itself` | `Pure/Witnesses.v` | The same through two memos reading each other by constant id. |
| `requeue_holds_two_entries` | `Pure/Witnesses.v` | A re-queued effect has two entries in `g_queue` and one in `pending`. |
| `engine_settled_preserved` | `Pure/Runs.v` | Every engine function, at every fuel, keeps `engine_settled`: the check invariant, stack discipline, id bounds, cleanup lists, in-cleanup effects on the stack, and the told record. |
| `engine_settled_init`, `engine_inv_init` | `Pure/Runs.v` | A new graph under the current flags satisfies both invariants. |
| `no_lost_wakeups_unless_passed` | `Pure/Runs.v` | After a program, an observer of a live stale source is covered or in `gh_passed`, the record of a walk ending over a stale source with no announcement. |
| `no_lost_wakeups_when_nothing_passed` | `Pure/Runs.v` | With `gh_passed` empty at the end, `wake_ok` holds. |
| `no_wake_lost_unless_resolved` | `Pure/Runs.v` | After a program, an observer of a live stale source is stale, queued, running or in `gh_over`: the source's flag excuses nothing. |
| `no_lost_wakeups_statement` | `Pure/Runs.v` | **Open statement.** The unqualified statement, false on the present engine: `no_lost_wakeups_falsified`. |
| `engine_inv_preserved_statement` | `Pure/Runs.v` | **Open statement.** Every engine function keeps the invariant with `wake_ok`, false on the present engine: `engine_inv_preserved_falsified`. |
| `later_mark_forwarded` | `Pure/Runs.v` | From `wake_ok`, a later `mark_check` or `mark_dirty` of a live memo leaves every observer stale, queued or running. |
| `marks_survive_run_statement` | `Pure/Runs.v` | **Open statement.** A memo stale at a point in its body is stale at its run's end, holding `n_unannounced`, false on the present engine: `marks_survive_run_falsified`. |
| `marks_survive_body_end` | `Pure/Runs.v` | The tail of a run: a memo stale when its body returns is stale when its run returns, holding `n_unannounced`. |
| `cutoff` | `Pure/Runs.v` | A `CmpEq` run whose stored view holds still adds no mark to any node and nothing to the queue. |
| `moved_marks_except_running` | `Pure/Runs.v` | A moved run leaves every observer that takes marks `Dirty`, apart from the computation current at its start. |
| `notice_skips_current`, `moved_run_skips_current` | `Pure/Runs.v` | That computation stays short of `Dirty` if it was. |
| `run_end_edges_are_reads_statement` | `Pure/Runs.v` | **Open statement.** After a run, a live computation's source list is `read_list` of all its run's reads, false on the present engine: `run_end_edges_are_reads_falsified`. |
| `run_end_edges_are_latest_reads` | `Pure/Runs.v` | The same over the reads since its latest run start. |
| `run_end_edges_no_nested_run` | `Pure/Runs.v` | The same over the run's reads, when no nested self-run started. |
| `disposed_stale_source_read` | `Pure/Witnesses.v` | A disposed stale memo's reader is uncovered, which `wake_ok`'s live premise excepts. Closed in `BUGS.md`. |
| `walk_passed_source_marked`, `walk_passed_source_cleanup` | `Pure/Witnesses.v` | A walk passes a source, a later source's run marks it, and the effect ends clean and idle over it with no flag. Confirmed in `BUGS.md`. |
| `no_lost_wakeups_falsified`, `engine_inv_preserved_falsified` | `Pure/Witnesses.v` | Both Open statements above are false, from that program. |
| `walk_passed_stays_deaf` | `Pure/Witnesses.v` | After the walk, another write of the source wakes nothing. |
| `marks_survive_run_falsified`, `run_end_edges_are_reads_falsified` | `Pure/Witnesses.v` | Both Open statements are false, from a nested self-run. |
| `skip_removed_fires` | `Pure/Witnesses.v` | With the skip of `NotifyDirtyExcept` removed, the current computation ends `Dirty`. |
| `ordinary_programs_nothing_passed`, `ordinary_stale_edge_covered`, `flagged_stale_edge_covered` | `Conformance/Check.v` | `gh_passed` is empty on the ordinary conformance programs, and `no_lost_wakeups_when_nothing_passed` applies across a live stale edge. |
| `effect_resolves_over_moved_source` | `Pure/Witnesses.v` | An effect reads a source, writes it, reads a memo over it, and never runs again with the new value. Confirmed in `BUGS.md`. |
| `diamond_ends_memo_stale` | `Pure/Witnesses.v` | With no write in any body, a memo's run ends `Dirty` holding its flag. |
| `reader_mark_off_fires` | `Pure/Witnesses.v` | With `MarkReaderCheck` off, `wake_told` fails and `wake_ok` still holds. |
| `unannounced_off_fires` | `Pure/Witnesses.v` | With `n_unannounced` never set, `wake_ok` fails. |
| `sched_ok_all` | `Pure/Scheduler.v` | Every engine function, at every fuel, keeps `sched_inv`, under the premise `sched_ok` lists for it: a memo function needs a memo and, from `refresh` down, an open stale read or a running flush; `execute_effect` takes `n` in progress to none, with `n` an effect during a flush; `run_effect` the same with none in progress; `run_effect_body` also a live effect; `discharge` an open stale read or a running flush; `teardown` and `run_cleanup` an existing owner, with an allowance per cleanup handed to them; `drain` a running flush; `settle_flush` the invariant without its owed clause. Each returns the context fields as it found them. Nodes keep kinds, disposal only rises, a disposed effect's runs stay put, and a host's cleanup events rise by at most its registrations. |
| `sched_inv_init`, `sched_inv_run_top` | `Pure/Scheduler.v` | A new graph satisfies `sched_inv`, and a whole program keeps it. |
| `queued_at_most_once`, `queued_at_most_once_new_graph` | `Pure/Scheduler.v` | Each node has at most one live queue entry, and `queued` is set exactly when it has one or it is the effect mid-`Execute`. `queue_once` follows. |
| `flush_no_nest` | `Pure/Scheduler.v` | `run_flush` with `g_flushing` set returns the state it was given. |
| `owed_settled` | `Pure/Scheduler.v` | A program from no batch, pull or current computation ends with no flush owed. |
| `flush_returns_clean` | `Pure/Scheduler.v` | A flush that returns has an empty queue, and every live effect `Clean` and unqueued, with or without a failing body. `Clean` is a mark. |
| `flush_reruns_dirty` | `Pure/Scheduler.v` | A live effect `Dirty` at its dequeue, or raised to `Dirty` by its walk, has its `EvRun` count rise, or ends disposed. |
| `body_failure_contained`, `cleanup_failure_contained`, `late_cleanup_contained`, `memo_failure_contained`, `comparer_throw_contained` | `Pure/Scheduler.v` | A sub-body ending in `Fail`, or a throwing comparer, leaves the enclosing function returning, with the failure recorded where the F# records it. |
| `memo_scope_violation_returns`, `memo_comparer_replaces_failure` | `Pure/Scheduler.v` | The two other ways a memo's run records a failure: a scope violation, and a throwing comparer over an unchanged failure. |
| `drain_continues`, `teardown_continues` | `Pure/Scheduler.v` | The drain and the teardown go on from the state each step returned. |
| `drain_no_escape` | `Pure/Scheduler.v` | A flush that returns leaves `g_flushing` false and an empty live queue. `flush_survives_body`, `flush_survives_cleanup`, `flush_survives_child_release`, `flush_survives_memo_run` and `flush_survives_comparer` run each failure with an effect behind it. |
| `dispose_reverse_order` | `Pure/Ownership.v` | `dispose_scope` is one merged list of the detached children and cleanups, each once, with acquisitions strictly falling. |
| `owners_in_order` | `Pure/Ownership.v` | After a program on a new graph, every owner's lists are in acquisition order. |
| `cleanup_once`, `cleanup_once_new_graph` | `Pure/Ownership.v` | A host's `EvCleanup` events, with the cleanups its owners hold, are at most its registrations; and each list entry takes one step of `teardown`, or one `run_cleanup` on the late path. Structural, per entry, since a registration carries no id. |
| `disposed_no_edges`, `disposed_no_edges_new_graph` | `Pure/Ownership.v` | A disposed node off the running stack has no slots, no edge and no observer position. |
| `disposed_effect_never_runs` | `Pure/Ownership.v` | From a disposed effect, a program, a flush, a `drain` or its dequeue adds no run and no `EvRun`, and the dequeue changes only `queued`. `queued_effect_disposed_skipped` has a disposal mid-flush. |
| `disposed_memo_answers` | `Pure/Ownership.v` | A read of a disposed memo keeps nodes, trace and queue, and answers what it stored. `disposed_unread_answers_default` has it answer a default. |
| `late_registration_immediate`, `late_attach_disposed` | `Pure/Ownership.v` | On a disposed scope, a cleanup runs before the body goes on, and a node is disposed at once. |
| `stale_link_detach_noop` | `Pure/Ownership.v` | `unlink` of a link from an earlier generation returns the state it was given, which pins the model's check and no reachable behaviour. `stale_link_reached` reaches it. |
| `teardown_runs_in_reverse`, `late_registrations_run_at_once` | `Pure/Ownership.v` | The two orders on a program, against the F# scenarios of the same programs. |
| `failed_batch_leaves_queue` | `Pure/Witnesses.v` | A batch whose body fails leaves its effect queued and `Dirty` with no flush owed. Awaiting ruling in `BUGS.md`. |
| `well_behaved_nothing_passed`, `well_behaved_no_lost_wakeups` | `Pure/WholeGraph.v` | A program from a new graph with every memo outside the self-dirtying case and every effect only reading ends with `gh_passed` empty and `wake_ok`. `self_dirtying` covers `CmpNever`, so a default memo returning a fresh record is outside this. |
| `glitch_free_read` | `Pure/WholeGraph.v` | In such a program, a read of a node outside the self-dirtying case answers what it denotes over the current signals, and a disposed memo what it stored. |
| `read_quiescent_statement` | `Pure/WholeGraph.v` | **Open statement.** At quiescence, a read of a pure node answers what the node denotes. False: `read_quiescent_falsified`. `read_quiescent_batch_free_statement` is the form with a batch-free cone, unproved with no proof attempted, by the ruling in `BUGS.md`. |
| `convergence_all_statement` | `Pure/WholeGraph.v` | **Open statement.** At quiescence, every effect with an owner ran and read what its sources compute, in each program satisfying the premises. False: `convergence_all_falsified`. The fix phase's target, with effect writes, memo cleanups, `CmpNever`, and memo writes reaching no later read of the body inside it. `BUGS.md` lists the exclusions, each of which also excludes programs a fix may reach. |
| `convergence`, `converged` | `Pure/WholeGraph.v` | In such a program at quiescence, each live effect is `Clean` and has an `EvRun`. Each source of a tracked read in its last run has a recorded read answering what the source denotes. Its last run's `EvLog`s are a run of its body over the denotation, with each untracked read answered by some value, or by some failure when its target is a memo. With no untracked read, the body gives one result over those reads and the denotation, with those logs. |
| `witness_302`, `witness_290` | `Pure/Witnesses.v` | The regressions: with `f_unannounced` off `wake_ok` fails, and with `f_reader_mark` off `wake_told` fails, on the programs of scenarios S03 and S04. |
| `wake_predicates_miss_self_marking` | `Pure/Witnesses.v` | `wake_ok` and `wake_told` both hold on the states `self_marking_wake_lost` leaves behind. |
| `self_dirtying_covers_known_cases` | `Pure/Witnesses.v` | `self_dirtying` holds on the memos of the Confirmed self-marking and self-reading bugs. |
| `convergence_reached` | `Pure/Witnesses.v` | A live effect over live memos reaches every premise of `convergence`. |
| `conformance_programs_well_behaved` | `Conformance/Check.v` | Seven conformance programs satisfy both premises. |
| `effects_only_read_needed` | `Pure/Witnesses.v` | `effect_resolves_over_moved_source` falsifies the conclusion of `convergence`, and fails `effects_only_read`. |
| `glitch_free_read_falsified` | `Pure/Witnesses.v` | A read of a disposed memo answers what it stored, and its body answers otherwise. |
| `read_settles_writer` | `Pure/Witnesses.v` | A read in a state with a queue can settle a flush, and answer past what its node denoted at the start. Awaiting ruling in `BUGS.md`. |
| `read_quiescent_falsified` | `Pure/Witnesses.v` | At quiescence, a read of a pure memo answers past what it denotes. Confirmed in `BUGS.md`. |
| `row_child_effect_holds`, `row_effect_disposes_holds`, `row_pure_cleanup_holds`, `row_pure_creation_holds`, `row_failing_write_holds` | `Pure/Witnesses.v` | A form inside the premises of `convergence_all_statement`, where the conclusion holds on the present engine. |
| `row_memo_untracked_excluded`, `row_throwing_comparer_excluded`, `row_read_between_writes_excluded`, `row_memo_creates_excluded`, `row_memo_prev_excluded` | `Pure/Witnesses.v` | A form a named premise of `convergence_all_statement` excludes. |
| `check_over_moved_excluded` | `Pure/Witnesses.v` | `memos_read_before_write` excludes the shape of `check_resolves_over_moved_source`, whose bug stays owed. |
| `untracked_conservative`, `prev_conservative`, `creation_conservative`, `comparer_conservative` | `Pure/Witnesses.v` | A premise of `convergence_all_statement` is false on a program where `den_all` answers and `converged_all` holds: the excluded form sits on a branch that never runs, or the throwing comparer is never called. |
| `effect_dispose_outside`, `memo_dispose_outside`, `memo_prev_outside` | `Pure/Witnesses.v` | A disposal in an effect fails `effects_only_read`, and a disposal or `EPrev` in a memo fails `well_behaved_program`. |
| `widened_forms_reached` | `Pure/Witnesses.v` | Batches and computed read targets, in memos and effects, reach every premise of `convergence`. |
| `untracked_read_reached` | `Pure/Witnesses.v` | An effect's untracked read reaches every premise of `convergence`, and its log stays behind until a tracked read wakes the effect. |
| `cmp_never_passes_source`, `never_lost_update` | `Pure/Witnesses.v` | With `CmpNever` memos and no write in a body, a walk passes a source, and an effect stays `Clean` on a stale value with an empty queue. A default memo returning a fresh record reaches this. Confirmed in `BUGS.md`. |
| `weak_refinement_except` | `Pure/Refinement.v` | On a well-formed program that is `refinable`, the engine's trace has the reference's `observe_settled` at every batch prefix, and its traces extend. Holds for every `Rank`, so a memo may read a higher id. Not covered: effect writes, cleanups, child effects, untracked reads, a memo's read of an equal or higher rank on a path with no result, a memo disposal, a memo with `CmpNever`. |
| `exact_refinement_except` | `Pure/Refinement.v` | On those programs, with the same `EvRun`s at every prefix, the traces are equal, under the order those runs follow. |
| `refinable_meets_convergence` | `Pure/Refinement.v` | Those programs reach every premise of `convergence`, at every batch. Its comment names each gap between `well_formed_program` and those premises. |
| `engine_order_exact_except` | `Pure/Refinement.v` | On those programs, a flush's `EvRun`s are a sublist of the entries its drain takes, which extend the queue at its start: "runs follow the drain". The conjunct on `engine_order` is conditional on `queue_steady`. |
| `history_order_is_effect_order`, `exact_refinement_history` | `Pure/Refinement.v` | `history_order`, each round's effects in the order of that round's engine runs, is an effect order, and it can instantiate `exact_refinement_except`. `runs_agree` stays a premise: `runs_agree_bridge` in `BUGS.md`. |
| `notify_queues_observers_reversed`, `notify_after_swap` | `Pure/Refinement.v` | A walk over effect observers queues them from the last position to the first, and after a swap-removal the moved observer takes the removed one's place. Open through memos: `queue_order_through_memos` in `BUGS.md`. |
| `reads_end`, `drain_ends` | `Pure/Refinement.v` | Reads end and raise no `mu`, the weights of stale memos and the live queue entries. Each entry the drain takes lowers `mu`. |
| `engine_of_answers_on` | `Pure/Refinement.v` | On a well-formed program meeting `refinable` and `bodies_total`, where the reference gives a trace under some order, some fuel gives the engine's trace. `bodies_total` reads each body over all answers to its reads: on a path reached by values, no unbound variable, a non-negative target naming a signal or memo of the program, a memo's below itself, batches allowed, on paths no run takes as well. After an expression that fails it constrains nothing, so a write, cleanup or untracked read there meets it, and `refinable` stays. It is the proof's reach: `bodies_conservative`, and `answers_where_reference_settles` in `BUGS.md`. `refinable_alone_no_answer` shows `refinable` alone is not enough, on a program where the reference has no answer at fuel 5 under `order_identity` (computed). |
| `weak_refinement_statement` | `Pure/Refinement.v` | **Open statement.** Every engine trace on a well-formed program has the reference's settled view. False: `weak_refinement_falsified`. |
| `exact_refinement_statement` | `Pure/Refinement.v` | **Open statement.** Every engine trace on a well-formed program is the reference's. False: `exact_refinement_falsified`. |
| `engine_order_exact_statement` | `Pure/Refinement.v` | **Open statement.** A flush's `EvRun`s on those programs follow the queue at its start. False: `engine_order_exact_falsified`. |
| `engine_of_answers_statement` | `Pure/Refinement.v` | **Open statement.** The engine answers wherever the reference settles. False: `engine_of_answers_falsified`. |
| `weak_refinement_falsified`, `exact_refinement_falsified`, `engine_order_exact_falsified`, `engine_of_answers_falsified` | `Pure/Witnesses.v` | The four Open statements are false: from `disposed_unread_memo_value`, from `batch_restored_value_reruns`, from a requeue of the `S15` class against the queue at a flush's start, and from `restored_source` at every fuel. |
| `history_order_reached`, `answers_reached` | `Pure/Witnesses.v` | `drain_order` reaches `runs_agree` at `history_order`, where the identity order fails, and `queue_steady`, `bodies_total` and `inputs_total`. |
| `refinable_reached` | `Pure/Witnesses.v` | A program with a memo, a try-read and a failure reaches the premises, under a history with a write, a read and a disposal. |
| `reads_higher_refinable` | `Pure/Witnesses.v` | A memo reading a memo with a higher id is refinable under a rank that swaps the two, and not under the ids. |
| `disposed_memo_keeps_observers` | `Pure/Ledger.v` | A disposed memo keeps its reader's edge, and the reader stays clean on the stored value after a write to the memo's former source. Closed in `BUGS.md`. |
| `trace_of_mode_off`, `engine_of_mode_off` | `Pure/Optimised.v` | Under `SkipOff` the optimised engine's trace is `trace_of`'s, at every fuel and flags. |
| `observed_mode_off` | `Extract.v` | Under `SkipOff` the differential check's model side is `observed`. |
| `mark_dirty_saturated_id`, `marks_keep_saturated`, `notify_dirty_stamped_id` | `Pure/Optimised.v` | A `Dirty` mark of a saturated node is the identity, marks keep saturation, and under `stamp_ok` a stamped signal's walk is the identity. |
| `mark_dirty_saturates_falsified`, `mark_dirty_saturates_or_window` | `Pure/Optimised.v` | A `Dirty` mark leaves its node saturated except a `Clean` memo holding a due announcement, the 4191 case. |
| `unchecked_record_unsound`, `unchecked_record_breaks_sim` | `Pure/Optimised.v` | A record after every walk (`SkipUnchecked`) breaks `stamp_ok`, and on the reachable `prog_window` ends outside `sim` with an equal trace. |
| `walk_record_sound`, `checked_record_withheld`, `prog_window_checked` | `Pure/Optimised.v` | The checked record keeps `stamp_ok`, withholds the stamp in the 4191 case, and `prog_window` ends in `sim`. |
| `optimised_simulates`, `stamp_ok_preserved` | `Pure/Optimised.v` | From `sim` with bounded stamps, the optimised engine's runs end as the present engine's, in `sim`, at every fuel; `stamp_ok` holds throughout. |
| `optimised_trace_eq`, `engine_of_optimised_eq` | `Pure/Optimised.v` | The optimised engine's trace is the present engine's, at every fuel and flags. |
| `optimised_refines`, `optimised_exact_except`, `optimised_order_exact_except`, `engine_of_optimised_answers_on`, `witness_302_optimised` | `Pure/Optimised.v` | The present engine's refinement, answer and #302 results hold of the optimised engine. The order result holds at a present-engine state equal to the optimised one apart from the skip's fields. |
| `optimised_cleanups`, `cleanup_registered_in_teardown_runs_late` | `Pure/Optimised.v` | The optimised engine's cleanup events are the present engine's, at most one per registration; a cleanup registered during its host's teardown runs after the host's next run, as Core.fs documents. |
| `leave_dirty_cleanup_skip`, `leave_dirty_unannounced_skip` | `Pure/Optimised.v` | Without the advances at 4825 and 4290 a write skips a walk it owes: each trace differs from `SkipOff`'s, and `SkipAll` agrees with `SkipOff`. |
| `observers_add_spec`, `observers_try_add_spec`, `observers_remove_at_spec` | `Heap/ObserverSet.v` | The heap `Add`, `TryAdd` and `RemoveAt` return what `observers_add`, `observers_try_add` and `observers_remove_at` return and leave the set they give. |
| `walk_spec` | `Heap/ObserverSet.v` | The heap walk calls its visitor on every observer held exactly once throughout: `walk_covers` in HeapLang. |
| `sources_add_spec`, `sources_end_run_spec`, `sources_clear_spec`, `sources_begin_run_spec` | `Heap/SourceList.v` | On a world whose edge ends agree, the heap operations leave the world `sources_add`, `sources_end_run`, `sources_clear` and `sources_begin_run` give. |
| `moved_past_list_clears`, `moved_past_list_incoherent` | `Heap/SourceList.v` | An incoherent world the pure trim completes by a `MovedIn` past the end: the reason the heap specifications take `edge_match`. |
| `is_graph_agree`, `is_graph_edge`, `is_graph_fresh_lb`, `is_graph_fresh_valid`, `is_graph_alloc` | `Heap/Ghost.v` | The heap graph agrees with the pure state and its edge fragments, a freshness lower bound stays below the node's code, and a heap edge world starts a graph. |
| `epoch_rule_rise`, `fresh_map_grows`, `is_graph_fresh_epoch` | `Heap/Ghost.v` | Under the epoch protocol every freshness code grows, a step that only raises freshness keeps the epochs, and at an unchanged epoch a lower bound bounds the freshness. |
| `is_graph_open`, `is_graph_edge_match`, `is_graph_loc`, `graph_loc_agree` | `Heap/Ghost.v` | A graph opens to its heap and closes at a later state with the same nodes under the epoch protocol, moving the edge fragments it is given; its edge world is coherent, and a node's locations are stable. |
| `graph_add_node_spec`, `resolver_extend_spec` | `Heap/Ghost.v` | A new node joins the graph with empty edge structures, through a resolver extended by one closure. |

`Pure/Marking.v` guards the forwarding half of #302 only: a memo holding an announcement tells its observers. Never setting `unannounced` falls to `wake_ok` and `marks_survive_body_end`, and removing `MarkReaderCheck` to `wake_told`: `unannounced_off_fires`, `reader_mark_off_fires`. `wake_ok` holds with `MarkReaderCheck` removed, since the source holds its flag.

## Pure-tier report

The state of the reference and pure tiers at Task 13, the ledger, the differential results, and the optimisation chosen for
Task 13a.

### What the pure tier establishes

- **Engine invariants, over every engine function and every program.** `check_invariant` (`engine_ok_all`),
  `engine_settled_preserved`, `sched_ok_all` and the ownership results: each cleanup at most once, teardown in reverse
  acquisition order, a disposed effect never runs, a disposed node off the stack holds no edge.
- **No lost wake-ups, with one named exception.** `no_lost_wakeups_unless_passed`: an observer of a live stale source is
  covered or in `gh_passed`. `well_behaved_nothing_passed` empties `gh_passed` on programs whose memos are outside the
  self-dirtying case and whose effects only read.
- **Whole-graph behaviour on that fragment.** `glitch_free_read` and `convergence`.
- **Weak refinement on the `refinable` fragment.** `weak_refinement_except`: the engine's trace has the reference's
  `observe_settled` at every batch prefix, and its traces extend.
- **Exact refinement, conditionally.** `exact_refinement_except` gives equal traces under `runs_agree`, at an effect order
  that `history_order` supplies. `history_order` takes each round's order from the engine's own runs, so the order conclusion
  holds by construction: it shows the engine's order is a valid reference order, and it says nothing of which order the
  observer positions and walks produce. `engine_order_exact_except` gives "runs follow the drain".
- **The second direction, on a narrower fragment.** `engine_of_answers_on`: where the reference settles, some fuel gives
  the engine's trace, on `refinable` programs meeting `bodies_total`.

### What it does not establish

- **Five unproved obligations**, under `## Unproved obligations` in `BUGS.md`: `runs_agree_bridge` (no theorem derives
  `runs_agree` from the absence of redundant runs, so `exact_refinement_except` keeps it as a premise),
  `queue_order_through_memos` (the queue order through a memo's forwarding walk), `answers_where_reference_settles`
  (`engine_of_answers_on` without `bodies_total`), `memos_below_stuck_paths` (`refinable`'s rank premise on paths with
  no result), and `cleanups_before_replacement` (Task 13a: each cleanup registered outside its host's teardown runs before
  the host's next run or its disposal).
- **Twelve Open statements**, each a `Definition` the present engine falsifies by a checked counterexample, listed
  under `## Open statements` in `BUGS.md`. Eleven have a proved weaker form named there;
  `resolve_clean_no_stale_source_statement` has only its witness.
- **The fragment's edges.** `refinable` excludes effect writes, effect cleanups, child effects, untracked reads, memo
  disposal and `CmpNever`. A default memo returning a fresh record each run is `CmpNever`.
- **Ranvier itself.** Every claim reaches `Core.fs` through `CORRESPONDENCE.md`, with the differential check as testing
  evidence.

### The ledger

Suspected 0, Modelled 0, Confirmed 9, Awaiting ruling 14, Closed 9, Unproved obligations 5, Open statements 12. Task 13 closed
the two Suspected entries as spec corrected: `self_writing_memo_ends_stale` (the S14 program, whose memo answers 0 for every
signal, so its reader's value is right; the wrong-value form is Confirmed as `self_marking_wake_lost`) and
`disposed_memo_keeps_observers` (`disposed_memo_keeps_observers` in `Pure/Ledger.v`, and the F# by a harness script, with
Core.fs 4372-4376 documenting both halves).

### Differential results

`bash proofs/differential/run.sh 2000 8` on the Task 13 tree, exit 0: 1989 programs agree on traces and end states, 3
diverged on both sides, 0 fatal, and 8 excluded for repeated zero divisors, of a cap of 20. Every coverage floor was met,
among them 1124 disposals, 1630 writes from an effect, 78 memos left stale by their own run and 592 self-marking cleanups.
The generator also dropped 539 draws of a node or cleanup in a pure memo's body. Agreement is evidence for
`CORRESPONDENCE.md`, not a proof, and a program reaching a Confirmed entry agrees when the model has the bug too.

### Candidate optimisations

The gate for every candidate is `Obs(O) = Obs(reference)` over the `refinable` fragment. Measured costs come from
`bench/Ranvier.Benchmarks` in Release, in process, on one machine, with the command below; each number is a mean over
BenchmarkDotNet's default job. They are the cost of the present behaviour that a candidate would remove, and none is a
measurement of an optimised engine.

    dotnet run --project bench/Ranvier.Benchmarks -c Release --no-build -- --inProcess --filter "*SignalBenchmarks.Write" "*EffectBenchmarks.*" "*DiamondBenchmarks.*" "*ChainBenchmarks.WriteThenReadTail"

| Rank | Candidate | Source | Observation | Tier | Measured cost today | Eligible |
|---|---|---|---|---|---|---|
| 1 | **Skip a signal's notify walk while every observer is saturated.** Saturated: `Dirty`, not running, a memo holding no announcement, an effect queued or disposed. | Ranvier's direction: `Memo.Dispose`'s doc names the cost of a memo "marked dirty for the lifetime of the graph". | Trace equality with the present engine, so `observe_full` against it is unchanged and `observe_settled` against the reference is inherited. | Pure: an invariant over every engine function, and a simulation. Heap: the epoch and the per-signal record. | `SignalBenchmarks.Write`, Guarded: 2.54 ns at 0 observers, 8.04 ns at 8, 65.80 ns at 64 dirty unread memos. `EffectBenchmarks`: `BatchOfTenWrites` 2976 ns against `WriteAndFlush` 1179 ns at 64 effects, 363 ns against 160 ns at 8. | **Chosen.** |
| 2 | **Skip a redundant effect run:** compare the run's reads at `Dirty`, or ignore a `Dirty` mark from a source the running computation has not yet read. | Ledger: `batch_restored_value_reruns`, `diamond_marks_running_effect`, `requeue_runs_after_later_entry`, `redundant_run_reads_untracked`. | `observe_settled`: it changes run counts. | Pure (algorithm); heap for per-slot values or versions. | No benchmark has a redundant run. The nearest, `EffectBenchmarks.WriteAndFlush` at one effect, is 23.87 ns for a write and one run of a one-read effect. | No: it relies on six entries Awaiting ruling, these four and `redundant_failing_cleanup_changes_settled` and `logging_cleanup_drop_unobserved`. |
| 3 | **Release the sources of a `Dirty` memo with no observers**, relinking on its next read. | Ranvier's direction, as rank 1. | `observe_settled`: the relink appends the memo to each source's observers, which changes walk order and so effect order. | Pure, with a simulation up to observer order; heap for the relink. | The cost removed is rank 1's. Host-read memos relink on every read: `ChainBenchmarks.WriteThenReadTail` 33.49 ns at depth 1, `DiamondBenchmarks.WriteThenRead` 58.73 ns, both reading a tail memo with no observers. Their regression is unmeasured. | Yes, ranked below 1: same saving, a changed order, and a regression on host reads. |
| 4 | **Resolve a memo's `Check` once before its effect observers are queued**, queueing them only when it moves. | The `Check` fan-out of `notify_dirty` and `mark_check`. | `observe_settled`: effect order changes. | Pure, heavy: a new scheduler. | No repository benchmark has a memo fanning out to effects. | Yes, not measured. |
| 5 | **Re-queue an effect without a dead queue entry.** | `requeue_holds_two_entries`, `queued_at_most_once`. | Trace equality. | Heap only: the pure model's queue is a list. | No benchmark isolates it. | Yes, trivial in the pure tier. |
| 6 | **Walk observers without re-clamping during a mark.** | `notify_walk_unmutated`: a mark walk has no mutations. | Trace equality. | Heap only. | Inside rank 1's walk cost. | Yes, trivial in the pure tier, and void under rank 3. |

### The chosen optimisation

**Rank 1, the saturated-signal write.** The graph keeps a run epoch, advanced whenever a node leaves `Dirty`: a memo's `Run`
and `RecomputeScoped`, and an effect's `Execute`. A signal's notify walk run with no computation running records the epoch
on the signal when every observer it visited ends saturated. A later write of a new value skips the walk while the
signal's record equals the epoch, and `AddObserver` clears the record.

Reasons:

- **A measured cost.** In `SignalBenchmarks.Write`, every write after the first walks memos that are already `Dirty`, and
  each mark is a no-op: 65.80 ns at 64 observers against 2.54 ns at none. In a batch, the nine writes after the first walk
  queued `Dirty` effects: 2976 ns for `BatchOfTenWrites` against 1179 ns for `WriteAndFlush` at 64 effects. The saving is
  bounded by these differences. The optimisation's own overhead is an epoch increment per run and a compare per write;
  Task 13a's measurement is below.
- **Nontrivial to prove.** The obligation is an invariant over every engine function: a signal whose record equals the
  epoch has only saturated observers, so `mark_dirty` is the identity on each. Every transition out of saturation has to
  advance the epoch or clear the record. `n_unannounced`, set at a stale run's end, makes a `Dirty` memo unsaturated, and
  this is the #302 path, so `witness_302`'s failure with `f_unannounced` off has to carry over to the optimised engine.
- **Checkable by the differential test.** The skip is a transition of the model, extracted with the present engine as
  `observed_mode`. The end-state view compares source and observer counts as it does now.
- **No ruling needed.** It changes no event, so no Awaiting entry bears on it.

**Observation.** The optimisation changes no run count, so it needs no move from `observe_full` to `observe_settled`
against the present engine. `optimised_refines` is stated with `observe_settled`, because that is what the present engine
refines unconditionally on `refinable`. The stronger statement Task 13a can prove is trace equality with `engine_of`, which
carries `exact_refinement_except` across under the same `runs_agree` premise.

**Effect order.** `history_order` holds by construction, so no theorem of this tier fixes the order a walk produces.
Task 13a proves the order itself: each skipped walk queues nothing and changes no node, so the queue's content and order
equal the present engine's at every step. Trace equality is that statement.

**Cleanups.** `optimised_cleanups` follows from trace equality and `cleanup_once`, since the optimised engine starts and
discharges the same runs.

**Heap obligations.** The epoch and the per-signal record. A walk that visits an implementor of `IComputation` other than
the core `Memo` and `Effect`, such as `Binding`, records nothing, because those implementors clear their own staleness with
no epoch advance. The differential check covers `Signal`, `Memo` and `Effect` only.

### Task 13a: the design gate

`Pure/Optimised.v` models the optimised engine as its own transitions beside `Pure/Engine.v`, with the advance table
of design D2 (4191, 4207, 4290, 4807, 4825), the record after a setter's walk, the skip, and the clear on
`AddObserver` and `TryAddObserver`. Under `SkipOff` it is the present engine (`trace_of_mode_off`).

An unconditional record is unsound under D2. `RecomputeScoped` (4191) writes `Clean` and leaves `unannounced` set, and
`MarkDirty` (4483-4498) of a `Clean` memo keeps the flag: the memo ends `Dirty` with its announcement due, which is not
saturated (`mark_dirty_saturates_falsified`). A cleanup that writes a source of its memo inside that memo's discharge
records a stamp `stamp_ok` rejects (`unchecked_record_unsound`), and on `prog_window` the end states differ in the
disposed memo's `unannounced` (`unchecked_record_breaks_sim`).

By the round 2 ruling, the record checks that the walk left every observer saturated (`all_saturated`). With that check,
and an advance at an effect's creation, the optimised engine simulates the present engine at every fuel
(`optimised_simulates`, `rsims_all`), its trace is the present engine's (`optimised_trace_eq`), and the refinement,
order, answer and #302 results carry over. The examples `leave_dirty_cleanup_skip` and `leave_dirty_unannounced_skip`
show the advances at 4825 and 4290 are each needed. `cleanups_before_replacement` is false over every registration, as
Core.fs documents (`cleanup_registered_in_teardown_runs_late`); its corrected form is an unproved obligation in `BUGS.md`.

**Outcome: keep.** The F# implementation is on the separate branch `claude/notify-skip-epoch` (commit `8d37379d`), not on
this branch. Its benchmarks ran against this branch's F#, interleaved, in 3x3 fresh processes. Real
gains: `EffectBenchmarks.BatchOfTenWrites` at 8 effects (ratio 0.40) and 64 (0.36), and `SignalBenchmarks.Write` at 64
observers, Guarded (0.03). No real regression on the binding shapes, `Effect.WriteAndFlush` or `WriteAndPropagate`. The
resolution is about 5%, since the machine moved between states about 1.5 times apart, so a smaller regression is
undetectable. `WriteAndPropagate` at 8 and 64 observers is unresolved, and no out-of-process run was made.

The differential check on this branch runs against the present F#, without the skip. Its model side is `observed_mode`
in the mode `RANVIER_SKIP_MODE` names: `off` by default and in CI, where it is `observed` (`observed_mode_off`). `all` runs
the optimised model against the same F#, and `leave-dirty` and `unchecked` run the two rejected designs of
`Pure/Optimised.v`.

## Literature

The from-scratch consistency results of the Adapton papers, compared with the reference tier. Each
claim below is scoped to the parts listed here.

**Sources read.**

- Hammer, Khoo Yit Phang, Hicks, Foster, *Adapton: Composable, Demand-Driven Incremental Computation*,
  technical report CS-TR-5027, University of Maryland, July 2013
  ([hdl.handle.net/1903/14708](http://hdl.handle.net/1903/14708)). Read: the abstract, the
  introduction's summary of the formalism, the meta-theory statements of Section 3 (Theorems 3.1 to
  3.3), the rule prose at the end of Section 4.1, Section 4.2's statements (Theorems 4.1 to 4.3), the
  opening of Section 5 and the statement of Theorem 5.2, and the conclusion's consistency sentence. Not
  read: the rule figures in detail, and any proofs.
- Hammer, Dunfield, Headley, Labich, Foster, Hicks, Van Horn, *Incremental Computation with Names*,
  OOPSLA 2015, extended version ([arXiv:1503.07792](https://arxiv.org/abs/1503.07792)). Read from the PDF
  text: the introduction's statement of the property, Section 4.2's description of the reference system,
  Section 4.3, Appendix B's account of the invariant, and the statement of Theorem B.13. Read only
  through a summarising fetch of the ar5iv rendering: Section 2.1 on the inner layer, Section 4.2.3 on
  legal modifications and the name check, and Definitions B.9 and B.10. Not checked: the proof of
  Theorem B.13.
- Hammer, Dunfield, Economou, Narasimhamurthy, *Refinement types for precisely named cache locations*
  ([arXiv:1610.00097](https://arxiv.org/abs/1610.00097), title and authors as on the arXiv page; version 1
  was titled *Typed Adapton* and listed Hammer and Dunfield). Read: the abstract of version 1 only, so
  nothing here is claimed about its results.

**Not accessed.** The PLDI 2014 paper. The ACM page returned 403, and the copy at
`people.mpi-sws.org/~hammer/adapton-pldi2014.pdf` returned 404. CS-TR-5027 has the same title and authors.
Nothing here is claimed about the PLDI text's theorem numbering or wording.

**Statements.**

- CS-TR-5027 Theorem 4.3 (Soundness): for well-formed prior knowledge `K` and a well-typed store, an
  incremental reduction `K; S1 |- e ⇓ S2; T` holds if and only if a basic reduction
  `S1 |- e ⇓ S2; trm(T)` holds. The report calls this extensional consistency: the final store and the
  terminal expression agree, and the trace structure is free. Theorems 3.2 and 3.3 make the inner layer
  read-only and deterministic.
- arXiv:1503.07792 Theorem B.13: given an incremental derivation `H1 |- e ⇓ H2; t` that is satisfactory,
  a pointer set `P1` that covers its reads and is apart from its writes, and a from-scratch consistent
  `H1`, there is a non-incremental derivation over the restricted graphs with the same `t`, the
  restricted graphs grow, `P2 = P1 ∪ dom(W)`, and `H2` is from-scratch consistent.

**Differences, and what was adopted.**

| Source | Difference | Reference tier | Decision |
|---|---|---|---|
| CS-TR-5027 Thm 4.3 | The soundness theorem is an equivalence: each direction gives a reduction. | `refines` has one direction, so an engine with no traces refines everything: `none_engine_refines`. | Adopted as `engine_answers`, stated separately so that `refines` keeps its form. **An engine proof owes both `refines` and `engine_answers`.** |
| Thm 4.3, Thm B.13 | Both relate one evaluation to its from-scratch evaluation: a result and a store. | A trace over a history, compared per effect by `observe_settled` or whole by `observe_full`, at every batch prefix, with the engine's traces extending across prefixes. | Kept. `observe_settled` is the analogue of comparing terminal results. `observe_full` has no analogue in the statements read, which leave trace structure free. |
| Thm 4.3 | The final store is compared. | Signal writes are not events, so a signal's value is compared only through logged reads and host reads. | Partly adopted: `RRead` makes a signal's value observable at any batch position, in both observations. A value the history never reads stays unobserved. |
| Thm 3.2, Section 2.1 | The inner layer cannot write. Writes come from the outer layer between evaluations. | Memos match this (`well_behaved_memo`). Effects write during a flush, and they read those writes. | Kept. The statements read give effects no meaning, and they do not justify any weaker statement about effects. |
| CS-TR-5027 abstract, Section 5 | Demand drives evaluation, and patching skips what is not relevant. | A memo's value is defined over current signals, whether or not it is demanded. Observers are effects and host reads. | Kept: the from-scratch value is what `memo_value` already defines. |
| Thm 4.3, Thm B.13 | Consistency holds for any well-formed prior knowledge `K`, and Theorem B.13 carries a whole-graph invariant through its induction. | `refines` compares traces and names no engine state. | Not applied in this tier: engine state lives below it. The factoring is a suggested shape for the engine invariant: every cached memo value equals `memo_value` over current signals, with disposed memos as the exception below. |
| Statements read | No disposal in them. | A disposed memo answers its last value, so it is deliberately not from-scratch consistent. | Kept, as `Core.fs` documents. `rs_frozen` makes `memo_value` the from-scratch value relative to the disposed memos' answers. |
| Thm B.13, Section 4.2.3 | Name checks and allocation (`P1`, `W`). | A static node set with fixed identities. | No counterpart. |
| CS-TR-5027 Section 5 | The order of patching steps is a source of nondeterminism in the declarative semantics. | The order of effects is observable. `refines` takes one valid round-indexed order per history. | Kept: `observe_full` is order-sensitive, and an effect's writes can make settled values depend on the order. |
| Statements read | No failures in them. | A failure is an outcome, compared by identity. | No counterpart. |
| Thm B.13 | A written proof, and the parts read mention no mechanisation. | Proofs are checked by Rocq. | Kept. |

No literature result here is used to weaken a statement.

**Compared at Task 12: `convergence` and `refines observe_settled`.** The sources are those read above. The PLDI 2014 text stays unread, so nothing here is claimed about it.

| Source | Difference | Here | Decision |
|---|---|---|---|
| Thm B.13 | The invariant is whole-graph: a from-scratch consistent `H1` goes in and a consistent `H2` comes out, over a satisfactory derivation. | `convergence` states the end of a whole program, per effect, from a whole-graph invariant (`whole`). It is stated over the end state, and the invariant is the proof's own. | Kept. Stating the invariant as the theorem would add nothing a caller uses. The factoring suggested at Task 3a is what `whole`'s `w_value` and `w_reads` hold. |
| Thm 4.3 | Both directions: incremental reduction if and only if basic reduction. | `weak_refinement_except` is one direction, and `engine_answers` the other. `engine_of_answers_falsified` refutes the second at every fuel on a program that writes. `drain_ends` is the termination argument on a read-only program, and `engine_of_answers_on` the second direction there, under `bodies_total`. | Kept as the two statements, each with a stated fragment. |
| Thm 4.3 | The final store is compared. | `observe_settled` compares per-effect views and host reads. `convergence` adds each effect's recorded reads. | `convergence` is the stronger of the two on the store: every source read by a live effect answers its denotation. |
| Thm 3.2, Section 2.1 | The inner layer cannot write. | `refinable` keeps effects read-only as well, a stronger restriction than the papers' on the outer layer, which writes between evaluations. Here the host is the outer layer. | Provisional, by the owner's scope. It is not justified by the papers. |

The refinement statements are not better factored by adopting a statement from these sources. None was adopted.
