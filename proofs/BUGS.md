# Defect ledger

Only Confirmed entries count as bugs. Where `src/Ranvier/Core.fs` documents a behaviour as
intended, the entry closes as "spec corrected".

## Suspected

## Modelled

## Confirmed

- `self_marking_wake_lost` (scenarios `self_marking_cleanup`, `self_marking_body`, `self_marking_through_memo`;
  `theories/Pure/Witnesses.v`: `self_marking_cleanup_left_behind`, `self_marking_body_left_behind`,
  `self_marking_through_memo_left_behind`): a run marked during itself ends stale with an unchanged value, its reader's
  `Check` resolves to `Clean`, and the reader keeps a value the memo's next run would change. No mark is due. F# trace of
  `self_marking_cleanup` before its closing read: `R6; L5 0; L6 0; C4; L5 0`. The signal holds 50, `x` is `Dirty` with value
  0, and the effect is clean and saw only 0, with an empty queue. An explicit `Flush` adds no event and changes no status.
  The harness asserts each of these, and the closing host read answers 50. `self_marking_body` is the same end state from a
  memo writing its own source, with the signal at 2, the memo at 0 and its next run answering 2.
  Path: `Effect.Execute`'s walk (Core.fs 4792) pulls `x`. A write during `x`'s run marks `x` from `Clean` (4483-4498), and
  the notice reaches an effect already `Check`. `x`'s value does not move, so it does not mark the effect dirty, and it ends
  with `unannounced` (4289). The walk ends and the effect goes `Clean` (4798-4799). `MarkReaderCheck` (4339) is on the body
  read path only. `Memo.ResolveCheck` (4314-4316) has the same gap, through a reading memo: `self_marking_through_memo`.
  The nearest docs: 4071-4072 describe the flag as the next mark's notice and say nothing of a reader that resolves in the
  meantime, and 4301-4302 say "Reaching the end still in `Check` means nothing above moved", which
  `self_marking_through_memo` contradicts: the reading memo goes clean over a source whose next run moves. 3438-3441 and the
  doc of `lateLinkConverges` (`AsyncEdges.fs`, 2e8f9690) point the same way: a reader is told when the node ends a run stale.
  The spec names this as its known starting candidate.
  **`wake_ok` holds here.** Every stale node in each end state holds `n_unannounced` (`stale_all_unannounced`), so the edge from the stale
  node to the clean, unqueued, idle observer is covered by the last disjunct alone: `wake_ok` does not claim a later mark will arrive.
  `wake_told` fails on these states unless the observer is in `gh_over`, and these observers are. `walk_passed_source_marked` is what
  falsifies `no_lost_wakeups_statement`. **The property that fails:** when a flush returns with an empty live
  queue and nothing running, every effect's last run read, from each source, the value that source's body computes from the
  current signals. A form over each source's stored value passes on these witnesses, since the effect read the stored 0.
  These programs are outside the well-behaved fragment, since `well_behaved_memo` rejects `CWrite` and `CCleanup`, so the
  fragment's convergence theorem and refinement are not falsified. `convergence_all_statement` (`theories/Pure/WholeGraph.v`) is the statement of that property over all
  programs, the fix phase's target, and `convergence_all_falsified` proves it false on `self_marking_cleanup`. `resolve_clean_no_stale_source_statement` (Task 8) is the Open statement at a single resolution, falsified through `stale_source_reader`: the
  reader answers 0 with the signal at 2 and its source `Dirty`, and the source's next run answers 2. The harness asserts each of these.
  **Contrary evidence, by shape.** `tests/Ranvier.Tests/Invalidation.fs:33-35` says "A memo that writes is outside the
  contract" and "The same shape arises legitimately from a source that settles inline". `self_marking_body` and
  `self_marking_through_memo` are memos that write, so that comment puts them outside the contract. `self_marking_cleanup` is
  the strongest case: no body writes, only a cleanup writes a source. Core.fs 4178-4181 describes a cleanup that writes a source and
  then reads the memo, and this cleanup does not read it, so no document covers this form. The inline settle is `S03`'s shape, which async nodes reach and which this entry has not run on them.
- `read_move_leaves_effect_queued` (scenario `self_marking_cleanup`, closing read; `theories/Pure/Witnesses.v`, same name): a
  stale read whose run moves its value queues a clean observer, and no flush follows. After `x.Value`, the effect is `Dirty`
  and queued, the queue holds 1, `flushOwed` is false and `Runs` is 1. An explicit `Flush` then runs it and logs 50. The
  harness asserts each of these. The read's move goes through `NotifyDirtyExcept` (Core.fs 4283), and only a write calls
  `RequestFlush` (3822), so `ExitPull` finds nothing owed (3243). The doc at 3159-3161 says effects "run once the outermost
  body or read has finished". The only route found starts from the left-behind state above: an effect observing a stale
  memo is otherwise already queued by the mark that made it stale. Outside the well-behaved fragment by that route.
- `nested_self_recompute_resets_cursor` (scenario `nested_self_run`; `theories/Pure/Witnesses.v`:
  `nested_self_run_ends_clean_and_wrong`): a run reads `a`, writes `flip`, which marks it, and reads itself. The nested run
  (Core.fs 4209) takes another branch and its `EndRun` trims `a`'s edge, and the outer run then stores the value it computed
  from `a`. After `flip <- 2`, the memo is clean with value 10, its body now answers 20, `a` has no observers and `b`
  has one. After `a <- 11` the memo is still clean at 10. The effect is clean, nothing is queued or running, and an explicit
  `Flush` changes nothing. The harness asserts each of these and the witness states them. F# trace: `[EvRun 5; EvLog 5 10; EvRun 5; EvLog 5 10; EvHostRead 4 (Ok 10)]`. Unlike `self_marking_wake_lost`, the
  memo is clean, so no marked state records the difference. Core.fs 4205-4206 covers a self-read's overflow and 4178-4181 a
  cleanup's nested run, and neither states this. A self-read and a write put it outside the well-behaved fragment. The
  glitch-free claim excepts the self-dirtying case.
  **Theorems.** This falsifies `marks_survive_run_statement` and `run_end_edges_are_reads_statement`: `marks_survive_run_falsified`, `run_end_edges_are_reads_falsified`. The nested run clears a mark the
  outer run received, and the outer run read `a` with no slot for it. Both are Open below. `marks_survive_body_end` covers the tail of a run from the body's return. `run_end_edges_are_latest_reads` states the source
  list against the reads since the latest run start, and `run_end_edges_no_nested_run` against the run's reads when no nested self-run started. A form of the first with a mark at any point and a premise excluding the
  clearing paths (a nested run, a self-read's `ResolveCheck`, the discharge window) was not attempted.

- `self_read_check_overflow` (scenarios `self_read_check_overflow`, `mutual_read_check_overflow`, run by `--fatal`;
  `theories/Pure/Witnesses.v`: `self_read_check_resolves_itself`, `self_read_second_resolves_itself`,
  `mutual_read_check_resolves_itself`): a memo reads itself and also reads another memo `a` over a signal. Two reads leave it
  clean with itself among its sources, and a write to the signal marks `a` dirty, which tells the memo `Check`. The next read
  overflows the stack, which ends the process: no caller can catch it. Program:
  `(signal 1 (k 0) (memo 2 pure eq (read 10 (v 1) (ret (v 10))) (memo 3 pure eq (read 10 (v 3) (read 11 (v 2) (ret (v 11)))) (read 20 (v 3) (read 21 (v 3) (write (v 1) (k 1) (read 22 (v 3) (ret (k 0)))))))))`.
  The harness asserts `Clean`, then `Check`, then that the memo observes itself, before the fatal read.
  **The shape.** The other source has to be a memo, since that is what delivers `Check`. A memo reading itself and a signal runs
  fine, because the write marks it `Dirty`, and `Dirty` runs the body with no walk. The self-slot need not be first. With the
  self-read second the walk reaches it when `a` holds its value (`a` as `lt s 100`, `self_read_second_resolves_itself`), and stops
  short of it when `a` moves, which runs fine. Two memos reading each other overflow the same way, by constant id in the command
  language: `(signal 1 (k 0) (memo 2 pure eq (read 10 (v 1) (ret (v 10))) (memo 3 pure eq (read 11 (k 4) (read 12 (v 2) (ret (v 12)))) (memo 4 pure eq (read 13 (k 3) (ret (v 13))) (tryread 20 21 (k 3) (tryread 22 23 (k 4) (tryread 24 25 (k 3) (tryread 26 27 (k 4) (write (v 1) (k 1) (tryread 28 29 (k 3) (ret (k 0))))))))))))`.
  Without its final read that program returns on both sides, with the two sides agreeing and memo 3 in `Check`.
  Path: `TrackCurrent` (Core.fs 4334), then for a host read `Pull` (4336, 4349-4353), to `Refresh` (4360) to `ResolveCheck` (4304),
  whose walk calls `UpdateIfNecessary` (4309) on the memo itself, which is still in `Check`, so `EnsureCurrent` (4318) enters
  `Refresh` again, and no body runs between the frames. Mark and body guards never see it. `Generate.fsx`'s step and depth budgets
  count bodies, and do not see it either. The model has the same walk: `resolve_walk` spends fuel and changes nothing, so the read
  has no result at the fuels the witnesses state, where the same program without that read returns.
  **Against the documents.** Core.fs 4205-4206 says marking clean before the body "turns a memo that reads itself from a stack
  overflow into a default value". That holds for the path it names, `Run`, and the walk in `ResolveCheck` is a second path with no
  such guard. The test "a memo that reads itself yields a default rather than overflowing" (`tests/Ranvier.Tests/Invalidation.fs:66`)
  says of a self-read: "Not a supported thing to write, but a cycle must fail as a value, not as a stack overflow the caller cannot
  catch." That test reads through `Peek`, so it takes no edge to itself and does not reach this path.
  **The contract is the owner's call.** A self-reading memo may be outside the contract, and the owner decides that. No document
  found says this outcome is intended, which is why the entry is Confirmed and not closed as spec corrected.
  **Theorems.** `glitch_free_read` (Task 11) and `self_dirtying` exclude this program: a read of a node, or of itself, later than the reader makes the reader self-dirtying, for every answer of its earlier reads: `self_dirtying_covers_known_cases`. `convergence_all_statement` takes `memos_ranked` for the same reason. No proved theorem is falsified, and none is
  weakened. `well_formed_program` includes `memos_acyclic`, which rejects this program, so Task 12's `engine_of_answers_statement` and `weak_refinement_statement` keep
  their form with that premise. `well_behaved_memo` alone accepts a self-read and is no substitute for it. Any statement that `read`
  or `update_if_necessary` returns needs `memos_acyclic` as a premise, and Task 8's `resolve_clean_no_move_statement` and Task 9's
  `engine_inv_preserved_statement` hold over results, which this read has none of. In the differential check this program is `fatal` on the
  F# side and has no result in the model, which counts as a both-sides skip and closes nothing.

- `check_resolves_over_moved_source` (scenario `check_over_moved`; `theories/Pure/Witnesses.v`, same name, and
  `resolve_clean_no_move_falsified`): a memo `x` reads `a`, writes `a`'s source, and reads `b`, a memo over `a` that has run
  before. The write marks `a` `Dirty`, and `x` and `b` `Check`. `b`'s `ResolveCheck` runs `a` with `x` as the current
  computation, and `NotifyDirtyExcept` (Core.fs 4283) skips `x`. `x`'s run ends in `Check` with its value from the old `a`.
  The next read walks `x`'s sources, both now clean, and `x` goes `Clean` with no run: `x` answers 0 with `a` at 1. F# trace:
  `[EvHostRead 3 (Ok 0); EvLog 4 0; EvHostRead 4 (Ok 0); EvHostRead 4 (Ok 0)]`. The harness asserts `Check`, the answer 0, `Clean`
  and `a` at 1. Against the documents: Core.fs 4301-4302 says "Reaching the end still in `Check` means nothing above moved", and
  here `a` moved. The doc of `NotifyDirtyExcept` (Core.fs 386-393) gives the skip's reason: the running computation "receives the
  new value from that very read". This reader read the source earlier in its run, and takes no new value from the walk.
  `well_behaved_memo` rejects this program, since it writes.
  **Variant, same cause:** `check_two_reads`, witness `check_two_reads_over_moved_source`. The body reads `a`, writes the source,
  reads `a` again and answers the first read. F# trace: `[EvHostRead 2 (Ok 0); EvLog 3 0; EvHostRead 3 (Ok 0); EvHostRead 3 (Ok 0)]`.
  The harness asserts `Check`, the answer 0, `Clean`, and `a` at 1. `gh_seen` keeps every view of a source read in a run, so
  `moved_since_last_run` sees the first read.
  **Contrary evidence.** `tests/Ranvier.Tests/Invalidation.fs:33-35` says "A memo that writes is outside the contract", and this
  memo writes. From reading, not run: a cleanup that writes under `x`'s run reaches the same skip. Core.fs 4178-4181 describes a cleanup
  that writes a source and then reads the memo, so no document covers a cleanup that only writes. **Theorems.** `resolve_clean_no_move_statement` is false on this engine, and is the Open statement below. `convergence_all_statement` excludes this shape, conservatively: `check_over_moved_excluded`.
  `resolve_clean_unmoved` proves it under `reads_settled`, from `check_invariant`'s preservation and `unmoved_short_of_dirty`, without using the walk: the memo is not in `gh_unnoticed`, and neither it
  nor a source is running. `gh_unnoticed` records four events: this skip, a read of a running node, the `Dirty` cleared at the start
  of `RecomputeScoped` (Core.fs 4191), and a run with no move ending on a view other than the published one, which takes a nested run.
  `check_cutoff_settled` shows the premise holding on an ordinary resolution. From `Clean` or `Check`, the conclusion holds of any
  memo: `unmoved_short_of_dirty`.
  **The other Confirmed entries.** `self_marking_wake_lost` does not falsify `resolve_clean_no_move_statement`, since the reader read the
  stored value: `self_marking_through_memo_unmoved`. It falsifies `resolve_clean_no_stale_source_statement`, a separate Open statement, so a fix
  of the skip alone leaves that one open: `resolve_clean_no_stale_source_falsified`, scenario `stale_source_reader`. `read_move_leaves_effect_queued` changes no memo's reads. `nested_self_recompute_resets_cursor`
  starts from `Dirty`, and its lost read has no slot, so `moved_since_last_run` does not see it. `self_read_check_overflow` has no
  result to conclude from, and the theorem's premise is conditional on one: `self_read_check_resolves_itself` has the read at no result.

- `walk_passed_source_marked` (scenarios `walk_passed_source_marked`, `walk_passed_source_cleanup`; `theories/Pure/Witnesses.v`, same names):
  a check walk passes a source, and a later source's run marks it again. The effect reads `a` then `b`, and `b` writes `a`'s source, in its
  body or in a cleanup. A write to `b`'s source queues the effect. `Effect.Execute`'s walk (Core.fs 4792) finds `a` clean and runs `b`. The write marks
  `a` `Dirty` from `Clean`, and `a`'s notice finds the effect already in `Check`, so it adds nothing. `b`'s value holds, and the walk ends with the
  effect `Check`, which goes `Clean` (4798-4799). F# trace: `[EvRun 5; EvLog 5 0; EvHostRead 3 (Ok 2)]`, and with the cleanup
  `[EvRun 5; EvLog 5 0; EvCleanup 4; EvHostRead 3 (Ok 2)]`. Before the closing read the harness asserts the signal at 1, `a` at 0 and `Dirty` with
  `unannounced` false, one effect run, status `None` for the effect, an empty queue, and an explicit `Flush` changing nothing. The effect logged 0, and
  no mark is due: no disjunct of `wake_ok` covers the edge, so this is a lost wake-up in the sense of the design, and not only a stale value.
  **The effect stays deaf.** The harness then writes the signal again, to 2, and the effect still has one run: `a` is `Dirty` with no announcement, so it tells no observer. The effect
  wakes when another source of it moves, or when `a` is read and a flush follows. `walk_passed_stays_deaf` states this in the model.
  `ResolveCheck` (4308-4316) has the same walk. Against the documents: 4301-4302 says "Reaching the end still in `Check` means nothing above moved",
  and a source here is `Dirty` at the end. 4178-4181 describes a cleanup that writes a source and then reads the memo, and this cleanup does not read it, so no document covers this form.
  `tests/Ranvier.Tests/Invalidation.fs:33-35` puts a memo that writes outside the contract. In the cleanup form no memo body writes, so that comment does not reach this form.
  **Theorems.** `no_lost_wakeups_statement` and `engine_inv_preserved_statement` are false: `no_lost_wakeups_falsified`, `engine_inv_preserved_falsified`.
  `no_lost_wakeups_unless_passed` proves the exception is the only one: an observer outside `gh_passed` is covered. `gh_passed` is reached only by a walk
  ending over a live, stale source with no announcement, and it is per computation: once an observer has passed one source, the exception covers all its sources until its next run.
  `ordinary_programs_nothing_passed` checks `gh_passed` empty on the ordinary conformance programs. `well_behaved_nothing_passed` proves it empty on any program whose memos are outside the self-dirtying case and whose effects only read.
  `diamond_ends_memo_stale` ends a memo's run stale with no write in any body. `CmpNever` is the counterexample on a program with no write: `cmp_never_passes_source`.

- `cmp_never_passes_source` (scenarios `cmp_never_passes_source`, `cmp_never_lost_update`, `fresh_record_lost_update`; `theories/Pure/Witnesses.v`: `cmp_never_passes_source`, `never_lost_update`): **a lost update.** With no write in any body, an effect
  stays `Clean` on a stale value, the queue is empty, and `Flush` does nothing. Writes keep landing without waking the effect, until something reads the stale memo.
  **The default route.** No custom comparer is needed. `fresh_record_lost_update` builds its memos through the public constructor under default options, with two memos returning a new record per run. `GraphOptions.Default` holds `JsIdentityPolicy` (`Types.fs` 539), which hands out `ReferenceComparer` for every
  reference type other than `string` (`Types.fs` 294-307). `docs/content/guide/equality.md` says so: "Records, unions, `Some x`, other reference types | By reference: an equal copy propagates". So each run of a memo returning a new such value counts as a move, which the model states as `CmpNever`. `CORRESPONDENCE.md` lists the values run: a new record, `Some x`, tuple or array and a `float` memo returning `nan` lose the update, and a struct record, a string, `None`, a nullary union case, `[]` and the same instance returned each run do not. The public
  `createMemoWithComparer` (`Api.fs` 257) reaches the same state with a comparer whose `Equals` answers false: `cmp_never_lost_update`.
  **Path.** The effect reads `a`, then `b`, at the end of a chain from memo `m5`, and `a` reads `m5`. A write queues the effect. `Effect.Execute`'s walk (Core.fs 4792) runs `a`, whose read of `m5` runs it, whose read of `m4` moves and leaves `m5` stale. `a` holds its value.
  Reaching `b` runs the chain and `m5` again, and that run counts as a move, so its notice marks `a` `Dirty` from `Clean`. `a`'s notice finds the effect in `Check`, and the effect goes `Clean` (4798-4799). `a` now tells no observer, so a write of a signal only `a` reads stops at `a`.
  F# trace of both scenarios: `[EvRun 10; EvLog 10 0; EvHostRead 6 (Ok 8); EvRun 10; EvLog 10 8]`. The harness asserts, after `t <- 7` and again after `t <- 8`: `a.Peek` 0, statuses `Stale` and `None`, an empty queue and a `Flush` changing nothing, with one effect run. The closing host read answers 8, and the flush after it runs the effect.
  `cmp_never_passes_source` is the shorter form, ending at the passed source with nothing further to lose.
  **Confirmed, with no documentation behind it.** The guide documents the comparison and says an equal copy propagates. No document found says a propagating memo can leave a reader asleep. 4301-4302 says "Reaching the end still in `Check` means nothing above moved", and here something above moved.
  **Same cause as `walk_passed_source_marked`:** a walk passes a source, a later source's run marks it, the notice finds the effect in `Check`, and the walk ends. Here the mark comes from a move of a fresh value and not from a write, so one fix can close both, and a fix that only guards body writes closes neither route here.
  **Theorems.** Those values map to `CmpNever`, and `well_behaved_program` excludes `CmpNever`: `well_behaved_nothing_passed` and `convergence` do not reach this program, and "well-behaved" does not cover a default memo returning a new record each run.
  `never_lost_update` is the second falsifier of `convergence_all_statement`, the fix phase's target: `convergence_all_falsified`.

- `quiescent_read_answers_past_flush` (scenarios `quiescent_read_answers_past_flush`, `quiescent_read_answers_past_flush_by_cleanup`; `theories/Pure/Witnesses.v`: `read_past_flush`, `read_past_flush_by_cleanup`, `read_quiescent_falsified`): at quiescence, a host read of a pure memo answers a value its body does not compute over the signals at the read's start.
  **Path.** The walk passes memo `a`, as in `walk_passed_source_marked`, so the effect sleeps on stale `a` with the queue empty and a `Flush` changing nothing. Memo `n` is pure: it reads `a`, and opens and closes a batch. The read of `n` runs `a`, whose move queues the effect, and `n`'s batch owes a flush. `ExitPull` settles it (Core.fs 3239-3244), inside `TrackCurrent`, and the effect writes 9. `Memo.Value` (4539-4548) reads the value after that, so the read answers 9 where `n` denoted 1.
  F# traces: `[EvRun 6; EvLog 6 0; EvRun 6; EvLog 6 1; EvRun 6; EvLog 6 9; EvHostRead 5 (Ok 9)]`, and with memo `b`'s cleanup writing, so no memo body writes, `[EvRun 6; EvLog 6 0; EvCleanup 4; EvRun 6; EvLog 6 1; EvRun 6; EvLog 6 9; EvHostRead 5 (Ok 9)]`. The harness asserts `a` holding 0 and stale, the effect's status `None`, an empty queue and the signal at 1 before the read, and the signal at 9 after it.
  **Confirmed, with no documentation behind it.** Core.fs 3159-3161 documents when the flush runs, "once the outermost body or read has finished". No document found says a read answers past the state it started in.
  **What a fix owes:** `read_quiescent_statement`, an Open statement below.
  **Cause.** Shared with `walk_passed_source_marked`: the sleeping effect is what the read wakes. Computed, `read_past_flush_passed`: the effect is in `gh_passed`, over a `Dirty` source holding 0. The second ingredient is the flush ahead of the answer, the question of `read_settles_writer`. Ran: the witness and both scenarios. Reasoned, with no fix run: a fix of the walk leaves no sleeping effect to wake on this route, and a read answering ahead of its flush closes the statement on this route too.

- `effect_resolves_over_moved_source` (scenario of the same name; `theories/Pure/Witnesses.v`, same name): the effect form of
  `check_resolves_over_moved_source`. An effect reads `a`, writes `a`'s source, and reads `b`, a memo over `a`. The write marks `a` `Dirty` and the
  running effect `Check` and queued. `b`'s walk runs `a` with the effect as the current computation, and `NotifyDirtyExcept` (Core.fs 4283) skips the effect.
  The next execution walks `a` and `b`, both clean, and the effect goes `Clean` with no run. F# trace: `[EvHostRead 3 (Ok 0); EvRun 4; EvLog 4 0; EvRun 4; EvLog 4 2; EvRun 4; EvLog 4 1]`. The harness
  asserts `a` at 1 and `Clean`, one effect run, status `None`, an empty queue and a `Flush` changing nothing: the effect's body used 0 and `a` holds 1. The next write of the source wakes it, and the harness asserts the runs that follow, three in all.
  **Same cause as `check_resolves_over_moved_source`:** one fix, a different victim.
  Against the documents: 386-393 give the skip's reason, that the computation "receives the new value from that very read", and this read is of `b`. Unlike a memo,
  an effect that writes is inside the contract: `well_behaved_memo` restricts memos only. `wake_ok` holds, since `a` is `Clean`. So this falsifies no theorem of this layer.
  **`convergence`.** `effects_only_read_needed` proves the conclusion of `convergence` false on this program, whose memos are all well behaved. `convergence` takes `effects_only_read` as a named premise, which excludes every effect that writes. The premise is provisional: the ruling on this entry decides whether it stays or narrows.
  **What `effects_only_read` excludes.** A write rests on this counterexample. A cleanup, a disposal and a node created in the body rest on the proof's reach: unproved by choice of scope, with no proof attempted and no counterexample known, and the owner has been told. `effect_dispose_outside` compiles the disposal's exclusion, and `memo_dispose_outside` and `memo_prev_outside` those of a disposal and `EPrev` in a memo. Batches, computed read targets and untracked reads are inside it. `converged` promises an untracked read's log only a run of the body with that read answered by some value, or by some failure when its target is a memo: its source can move with no run, and the state records no answer. `convergence_all_statement` has none of these premises, and this entry falls under it.
  `moved_run_skips_current` states the skip, and `skip_removed_fires` shows it fails with the skip removed.

## Awaiting ruling

Each entry of Task 12 is a difference between the engine and the reference, with a checked `Example` in `theories/Pure/Witnesses.v` holding both traces. `weak_refinement_except` or `exact_refinement_except` takes each as a named premise until the owner rules. All of them are outside `src/Ranvier`'s documents unless the entry says so.

- `disposed_unread_memo_value` (scenario of the same name; `theories/Reference/Properties.v` and `theories/Pure/Witnesses.v`, same name): which value a disposed memo keeps when it has had no demand since its sources moved. By the owner's ruling, the reference keeps the memo's value over the signals current at the disposal's position in the batch.
  **F#, ran:** a memo over `s`, written to 5 and disposed unread, then read by an effect. F# trace `[EvRun 4; EvDispose 2; EvRun 4; EvLog 4 0]`, and the harness asserts `m.Peek` 0. The reference's trace is `[EvRun 9; EvDispose 1; EvRun 9; EvLog 9 5]`. So F# answers the type's default where the reference answers 5. Core.fs: a memo starts `Dirty` and `Uninitialized` with a default value (4063-4065), `Create` does not compute it (4144-4147), the read paths do not refresh a disposed memo (4323, 4335), and `Value` answers the stored value (4539-4548), as the doc at 4372 states. That doc's "the last value it computed" does not describe a memo that never computed one. `disposed_memo_answers` and `disposed_unread_answers_default` state the engine's side.
  **Theorem:** `weak_refinement_falsified` proves the unconditional statement false from this entry. `weak_refinement_except` takes `no_memo_disposal`.
  **The question for the owner:** does the reference keep this engine-independent value, or does it model demand as Core.fs has it? Modelling demand would put one engine's laziness in the tier every engine proves against.
- `write_then_dispose_memo_current_value` (scenario of the same name; same files, same name): a batch writes a memo's source and disposes the memo. **F#, ran:** `[EvRun 4; EvLog 4 1; EvDispose 2; EvRun 4; EvLog 4 1]`, and the harness asserts `m.Peek` 1 and that the reader did not run at the batch's flush. The reference answers 6 and runs the reader: `[EvRun 9; EvLog 9 1; EvDispose 1; EvRun 9; EvLog 9 6; EvRun 9; EvLog 9 6]`. The same question and the same premise as `disposed_unread_memo_value`.
- `batch_restored_value_reruns` (scenario `S07`; `theories/Pure/Witnesses.v`, same name, and `batch_restored_redundant`): a batch writes 1 then 0 under an effect that reads the signal directly. The first write marks the effect `Dirty`, a `Dirty` effect runs with no comparison of its reads, and it logs the value of its previous run. F# trace: `[EvRun 2; EvLog 2 0; EvRun 2; EvLog 2 0]`. The reference's: `[EvRun 1; EvLog 1 0]`, with the ids of the reference program. The second run is `redundant`. It preserves `observe_settled` here, and `weak_refinement_except` holds across it. It breaks `observe_full`: `exact_refinement_falsified`.
  **The question for the owner:** is a redundant run acceptable, as the cost of comparing nothing at `Dirty`?
- `diamond_marks_running_effect` (scenario `S15`; `theories/Pure/Witnesses.v`, same name): the effect reads `[h, x, y]` and `x` reads `[h, y]`, and a batch writes `h` and `y`'s source. Under the effect's run, `y`'s run moves and marks the effect through its old edge, since `NotifyDirtyExcept` skips only `x`, the current computation. The effect runs again on equal reads. F# trace: `[EvRun 5; EvLog 3 0; EvLog 4 0; EvLog 5 0; EvRun 5; EvLog 3 1; EvLog 4 2; EvLog 5 4; EvRun 5; EvLog 5 4]`. The same question as `batch_restored_value_reruns`, and the same two theorems.
- `requeue_runs_after_later_entry` (scenario of the same name; `theories/Pure/Witnesses.v`, same name, on `requeue_order`): the class of `diamond_marks_running_effect`, with a second effect over `h` created first. A batch writes `h` and `y`'s source. **F#, ran:** after the initial runs, `[EvRun 6; EvLog 6 6; EvRun 5; EvLog 5 1; EvRun 6; EvLog 6 6]`, which `Conformance/Check.v` matches on the model. The queue at the flush's start is `5`, `4` in the reference's ids, and the flush runs `5`, `4`, `5`. It refutes the queue at a flush's start as the order of a flush: `engine_order_exact_falsified`. `history_order` is a different order, and this entry has a redundant run under it.
  **The question for the owner:** the same as for `diamond_marks_running_effect`.
- `redundant_run_reads_untracked` (scenario of the same name; `theories/Pure/Witnesses.v`, same name): a redundant run changes `observe_settled` when the body has an untracked read. A batch writes `u`, then restores `s`, under an effect reading `s` and logging `u` untracked. F# trace: `[EvRun 3; EvLog 3 0; EvRun 3; EvLog 3 5]`. The reference does not run the effect, since its tracked read holds: `[EvRun 2; EvLog 2 0]` with the reference's ids. This checks the reasoned carry from Task 11: `convergence` gives no log equality for such a body, and on this program no log equality holds. `weak_refinement_except` takes `effects_read_only`, which rejects an untracked read.
  **The question for the owner:** does an untracked read owe a value at all when its effect runs for no other reason? A fix that stops redundant runs closes this entry.
- `effect_restored_source_loop` (scenario `effect_restored_source_reruns`, with a budget; `theories/Pure/Witnesses.v`: `restored_source_no_answer`): an effect reads a source, writes it and restores it. The write marks the running effect, so it runs again and writes again. The reference compares reads, finds them equal, and settles after one run. Computed: `engine_of` has no answer at fuel 1000, and the reference answers `[EvRun 1]`. F#: the scenario's budget of 3 gives 4 runs with equal reads. Proved: `restored_source_never`, no answer at every fuel, and `engine_of_answers_falsified`, from `restored_source_well_formed` and `restored_source_settles`. The effect writes, which `effects_read_only` excludes.
  **The question for the owner:** the ruling on effect writes decides this entry with `effect_resolves_over_moved_source`.
- `read_settles_writer` (scenario of the same name; `theories/Pure/Witnesses.v`, same name): a read settles a flush and answers after it, so the answer reflects what the flush wrote. Program: a pure memo with a batch, and a writing effect left queued by `failed_batch_leaves_queue`, itself Awaiting ruling. F# trace: `[EvRun 4; EvLog 4 0; EvRun 4; EvLog 4 1; EvRun 4; EvLog 4 9; EvHostRead 3 (Ok 9)]`.
  **The documents.** Core.fs 3159-3161 says effects inside a body or stale read "run once the outermost body or read has finished", which documents when the flush runs. `Memo.Value` (4539-4548) reads the value after `TrackCurrent`'s `ExitPull`, ahead of the read's return. No document covers the answer. `quiescent_read_answers_past_flush` under Confirmed reaches the same answer with no failed batch.
  The answer agrees with neither end consistently. `read_before_flush_answer`, computed: with the writer reading no `n`, the read answers 1, and `n` denotes 9 after it.
  **The question for the owner:** does a read answer before or after the flush it settles?

- `disposed_window_keeps_unannounced` (scenario `disposed_self_window`; `theories/Pure/Witnesses.v`, same name): `RecomputeScoped`
  sets `Clean` before `Discharge` (Core.fs 4191-4192) and does not clear `unannounced`. A nested run ends stale, so the flag is set,
  and registers a cleanup that disposes the memo. An untracked self-read then enters the window, and the cleanup disposes the memo:
  it ends `Clean`, disposed, with the flag set, from a read that started in `Check`. F# trace:
  `[EvHostRead 4 (Ok 0); EvCleanup 4; EvDispose 4; EvHostRead 4 (Ok 0)]`, and the harness asserts `Clean`, the flag and the disposal
  from the private fields. Only private fields differ, Core.fs 4071-4073 is honoured, and no document covers the flag on a disposed
  memo. `resolve_clears_unannounced` takes `n_disposed d' = false` for this witness, and proves every path of a live memo. The question
  for the owner: should disposal or the window clear the flag?
- `disposed_running_observer_stays_clean` (`theories/Pure/Witnesses.v`, scenario of the same name): an effect that disposes
  itself mid-body keeps running, a later read links an edge, its write leaves it clean and unqueued, and the `finally` at
  Core.fs 4854-4855 clears the edge. The harness asserts each step inside the body: the memo has 0 observers after the
  disposal and 1 after the read, the effect's status is `None` with `queued` false after the write, and 0 observers at the
  end. `check_disposed_window` states the window of the model. No Core.fs doc covers this window. `Memo.Dispose`'s doc at 4373 ("stops
  tracking and stops being woken") and `Effect.Dispose`'s at 4882 ("Detaches from every source") lean the other way. Nothing is lost: the effect is
  disposed, so no later run owes a value, and the edge ends with the run. The question for the owner: is the window intended?
  The case falls under the running disjunct of `later_mark_forwarded`, not under `takes_marks`.
- `reference_run_redundant` (`theories/Reference/Properties.v`): the reference tier itself has a
  redundant run. A round's run set comes from its starting snapshot, and an earlier effect of the
  round can restore the value a later one read. Under `order_reversed`, the `resetting` program re-runs effect
  8 on the value of its previous run. The definition follows the ruling for Task 3. The question for
  the owner: does a run set stay fixed at the round's start, or does a run re-compare when its turn
  comes? Not a Core.fs defect.
- `redundant_failing_cleanup_changes_settled` (`theories/Reference/Properties.v`): removing a redundant
  run can change `observe_settled`. In `resetting_failing_cleanup` under `order_reversed` and history
  `[[RWrite 0 1; RWrite 1 1]]`, run 1 of effect 8 is redundant, and the only `EvFail 8 7` of the trace
  comes from the cleanup it discharges. `sv_failures` holds the distinct failures of the history, so a
  removed run takes out a failure that occurs only in its cleanups. The design's claim that removing a
  redundant run preserves `observe_settled` holds on the cleanup-free example `redundant_run_settled_equal`
  and is false here. `observe_settled` and `redundant` are unchanged, pending the owner's ruling.
- `logging_cleanup_drop_unobserved` (`theories/Reference/Properties.v`): `observe_settled` admits an engine
  that drops a non-failing logging cleanup of a re-run: `[R9;C9;L100;C9;L0;R9]` and `[R9;R9]` have equal
  views. Task 13a's `optimised_cleanups` covers this for the worked optimisation.

- `failed_batch_leaves_queue` (scenario `failed_batch`; `theories/Pure/Witnesses.v`, same name): a batch whose body fails applies its write and
  skips `RequestFlush` (Core.fs 3303-3310), so the effect it woke stays `Dirty` and queued with `flushOwed` false, until something else flushes. The harness asserts the queue holds
  1, `flushOwed` false, one run and `Dirty`, and an explicit `Flush` runs it. F# trace: `[EvRun 2; EvLog 2 0; EvRun 2; EvLog 2 1]`.
  **The documents.** `docs/content/guide/batch.md:83-84` documents this behaviour: effects queued by the failed batch's writes run at the next flush. Other sources read differently.
  `tests/Ranvier.Tests/Reentrancy.fs:176` has the title "a batch that throws still flushes the writes it made", and its body asserts the write happened and that a later write runs effects.
  `tests/Ranvier.Tests/Batching.fs:203`, "a batch that throws is not left open", asserts a later write flushes. Core.fs 3159 says effects inside a batch "run when the batch ends".
  `owed_settled` holds here: it says no flush is owed, and leaves the queue open.
  **The question for the owner:** the guide documents this; does the owner accept it as the contract? `read_move_leaves_effect_queued` is Confirmed with the same end state and has no such documentation, so the two can be ruled differently.

## Closed

- `moved_past_list_clears` (`theories/Heap/SourceList.v`, with `moved_past_list_incoherent`), closed as spec corrected
  (Task 14). The heap source-list specifications as "the pure function succeeds, so the code leaves its world" are false
  on an incoherent world: observer 2 recorded at slot 3 of a source, with no slots. The pure `sources_clear` of the
  other observer succeeds, moving 2, and its `MovedIn` past the end leaves the world as it is; the heap `MovedIn`, like
  `SourceList.MovedIn` (Core.fs 599-607, 715), writes `rest[2]` unchecked, into a blank cell or past the array. The
  correspondence row of `sources_moved_in` documents that a coherent world reaches neither. The specifications take
  `edge_match`, which every reachable world has (`reachable_well_formed`). Not reproducible on F#: unreachable.
- `cleanup_registered_in_teardown_runs_late` (`theories/Pure/Optimised.v`), reproduced and closed as spec corrected. A
  cleanup that registers a cleanup with the scope it is torn down from: the second cleanup joins the emptied scope and
  runs at the next teardown, after the host's next run. Core.fs 1130-1132 documents this: "A cleanup or child
  registered while this teardown runs joins the emptied scope and outlives the teardown." Scenario
  `S18_cleanup_in_teardown`, F# trace `R2; L2 0; C2; R2; L2 1; C2; C2; L2 7; R2; L2 2`, equal to the model's. It
  falsifies Task 13a's `cleanups_before_replacement` read over every registration; the corrected statement is under
  Unproved obligations.
- `walk_covers_duplicate_observer` (`theories/Pure/Witnesses.v`), reproduced and closed as spec corrected. The test
  "a walk can miss an observer held at two positions" (`tests/Ranvier.Tests/Observers.fs`) runs the witness on the F#
  with a stub `IComputation`: `NotifyDirty` visits the observers at positions 1 and 0, and misses the one held at both
  through the walk. `ObserverSet` documents each observer as held at most once (Core.fs 190), and `ISource.AddObserver`
  requires "an observer the caller holds no edge for" (Core.fs 65), so the walk's coverage claim at Core.fs 360-369 holds
  under that premise, and `walk_covers` takes it as `held_once`. The callers that have to keep it are
  `SourceList.Append` (Core.fs 744, 769), where `observer_held_once` proves it, and `Bindings.fs:51`,
  `Combinators.fs:686`, `Combinators.fs:944`, `Projections.fs:676` and `ValueReaders.fs:100`, argued from reading in
  `CORRESPONDENCE.md`.
- `in_cleanup_observer_stays_clean` (`theories/Pure/Witnesses.v`), reproduced and closed as spec corrected. Core.fs
  4651-4671 documents the suppression as intended and not lossy. A clean memo's observer that is an effect in its
  cleanups stays clean, so `mark_forwards_on_clean` and `mark_forwards_on_unannounced` conclude "ends stale" for each
  observer in `takes_marks`. Scenario `in_cleanup_observer_stays_clean`: the cleanup's write moves the memo, no extra run
  follows, and the body reads the written value 100.
- `walk_visits_added_observer` (`theories/Pure/Witnesses.v`), closed for `Memo` and `Effect` marks: a walk's
  mutations are empty, so a mark cannot add the observer the witness visits. `mark_preserves_edges` holds for
  all five marking operations, and `notify_walk_unmutated` states each walk as `walk` with no mutations, so the
  doc at Core.fs 360-362 holds for walks whose visits are these two marks. The witness stays as a fact about
  `walk`, whose mutations over-approximate a mark. No theorem covers the other implementors of `MarkDirty` and
  `MarkCheck`, and they are outside the core nodes modelled here. From reading only, each writes its own fields,
  queues, forwards, or notifies, and none adds or removes an edge: `changed`, which is `touch ()`
  (`Combinators.fs:794, 799`), `touched` (`Combinators.fs:801`, `ValueReaders.fs:64`), `touchSummary`
  (`Projections.fs:441`), `RowWatch` (`Projections.fs:55`), `Binding` (`Bindings.fs:133`), the projection at
  `Projections.fs:1229`, `Streams.fs:504`, `Timed.fs:293`, `ForwardingEdge` (462) and the three other computations
  of `Core.fs` (5160, 5916, 6532). A change to any of these can reopen the entry.

- `disposed_stale_source_read` (scenario of the same name; `theories/Pure/Witnesses.v`, same name), reproduced and closed as spec corrected. A memo disposed
  while stale, then read by an effect, links an edge with no mark (Core.fs 4335): the source is `Dirty`, with no announcement, over a clean idle observer, so the first
  statement of `wake_ok` is false. F# trace: `[EvHostRead 2 (Ok 0); EvDispose 2; EvRun 3; EvLog 3 0; EvHostRead 2 (Ok 0)]`. The harness asserts `Dirty`, one observer, the
  effect's status and an empty queue. Core.fs 4372-4376 documents it: a disposed memo "still answers reads, with the last value it computed" and "stops being woken". No wake-up
  is owed, so `wake_ok`, `wake_told` and `later_mark_forwarded` cover live sources, and `resolve_over_disposed_dirty_source` is the same case through a walk.

- `self_writing_memo_ends_stale` (scenario `S14`), reproduced and closed as spec corrected (Task 13). A memo writes its own
  source, and an effect reads it. The memo's run ends `Dirty` holding `n_unannounced`, and the effect resolves its `Check` to
  `Clean`. The memo's body answers 0 for every value of the signal, so the effect's 0 is what the memo computes. The harness
  asserts the signal at 2, the memo `Stale` and the effect clear. F# trace:
  `[EvRun 3; EvLog 2 0; EvLog 3 0; EvLog 2 1; EvLog 2 2; EvLog 2 3; EvHostRead 2 (Ok 0); EvHostRead 1 (Ok 3)]`, and `check_S14`
  (`theories/Conformance/Check.v`) gives the model the same trace. `ordinary_programs_nothing_passed` has `gh_passed` empty at
  the end, so `no_lost_wakeups_when_nothing_passed` covers every edge from the stale memo: the memo holds its announcement.
  Core.fs 4200-4204 documents a run that invalidates its own source ending `Dirty`, and 4071-4072 documents the flag as the
  next mark's notice. The form with a wrong value is a memo whose next run changes its value: `self_marking_wake_lost` under
  Confirmed.
- `disposed_memo_keeps_observers` (`theories/Pure/Ledger.v`, same name), reproduced and closed as spec corrected (Task 13).
  `Memo.Dispose` clears the memo's sources and leaves its observers (Core.fs 4377-4398), so a reader keeps its edge to the
  disposed memo. Program: an effect reads memo `m` over `s`, the host disposes `m`, then writes `s <- 5`. The model ends with
  `m` disposed at 0 and observed by the effect, and the effect `Clean`, unqueued, with one run; trace
  `[EvRun 3; EvLog 3 0; EvDispose 2]`. F#, scenario `disposed_memo_keeps_observers` of `Scenarios.fsx`, with
  `check_disposed_memo_keeps_observers` in `Conformance/Check.v`: `m.Peek` 0, `s.Peek` 5, `ObserverCount` 1, one effect run, `queued` false, `Clean`, and the same events. Core.fs
  4372-4376 documents both halves: a disposed memo "still answers reads, with the last value it computed" and "stops being
  woken". The reader holds that value, which no later write moves, so no mark is owed.
- `glitch_free_read_statement` (`theories/Pure/WholeGraph.v`), closed as spec corrected. A disposed memo answers what it stored, as Core.fs 4372-4376 documents, as `disposed_stale_source_read` closed the same form of `wake_ok`. `glitch_free_read_falsified` is the record. `glitch_free_read` is the form proved. The form over the node alone is `read_settles_writer` under Awaiting ruling, and at quiescence `quiescent_read_answers_past_flush` under Confirmed.

## Rulings

- Scaffolding (M6, Task 11 fix round 1): `theories/Pure/WholeGraph.v` keeps its internal names public this round. Task 12 uses qualified imports if a name collides.
- `read_quiescent_batch_free_statement` (Task 11 fix round 2): a `Definition` with no proof and no `Admitted`. No proof is attempted: Task 12 does not use it, and the reviewer estimates 1000 to 1500 lines. Evidence, fuzz over the lost wake-up's shape, 3000 programs per run: batches allowed, 640 wrong answers in about 21000 reads, so the harness can see the bug; batch-free cones, 0 in 12970; no batch anywhere, 0 in 21032.
- The premises of `convergence_all_statement` cover the whole program, not an effect's cone (Task 11 fix round 3). A memo nobody reads, with an untracked read, takes `self_marking_cleanup` out of the target while the bug stays (computed), and the same applies to `memos_no_prev`, `memos_no_creation` and `memos_no_throwing_comparer`. The premises keep this scope. It narrows the target's reach and leaves the target correct: a fix owes the behaviour regardless of an unrelated memo.
- Forms 4 and 5 of `convergence`, effect cleanups and child effects (Task 11 fix round 2): they stay excluded, on the proof's reach. They are unproved by choice of scope, with no proof attempted, and the owner has been told. The reviewer's estimates are 800 to 1500 and 1500 to 2500 lines, and its search of 4000 programs per form found no counterexample.

## Definition changes

Changes to `redundant` from its first statement in the plan.

- `redundant : program -> list event -> node_id -> nat -> Prop` became
  `redundant : program -> list run -> node_id -> nat -> Prop`. A `list event` holds no read values, and
  neither does `program`, since the values depend on the history. The prose definition, "every value
  it read is equal to its previous run's", needs them. A `run` holds the reads and the events of one
  run, and `reference_trace` is the concatenation of `run_events` over `reference_runs`.
- "Equal to its previous run's" reads as: `run_all_reads` of the run equals `run_all_reads` of the
  previous run, as lists. `run_all_reads` holds every read of the run, tracked and untracked,
  cleanups included, in order. The initial run has no previous run and is not redundant. A run with
  no reads is redundant when its previous run also had none.
- Replaced at the first review: inclusion of the tracked `run_reads` in the previous run's. That form
  held for runs that read different values and produced different output. `untracked_value` logs an
  untracked read that moved from 0 to 42. `untracked_branch` selects its branch by an untracked read
  and logs for the first time with no tracked reads. Both are now checked as not redundant. The run
  set still depends on tracked reads only.
- `runs_of` keeps effect runs only: `reference_runs` now also holds disposal records, which are not runs.
- `redundant` compares reads, not events. A rerun runs the cleanups its previous run registered, so
  its events can differ from the previous run's on equal reads: `redundant_run_with_cleanup`.

## Unproved obligations

Statements Task 12 leaves unproved, with no `Admitted` behind them. None is a difference between the engine and the reference, and none carries a question for the owner.

- `runs_agree_bridge` (`theories/Pure/Refinement.v`, the premise of `exact_refinement_except`): no theorem derives `runs_agree` from the absence of redundant runs. `runs_agree` excludes a redundant run, a missed run and a reordering: `runs_agree_has_content`. The bridge wants: on a refinable program, each batch's runs in the engine and the reference's run set of that batch are equal as sets, each effect once, then `runs_agree` holds at `history_order`. `history_order_reached` meets the premise on one program by computation.
- `queue_order_through_memos` (`theories/Pure/Refinement.v`, beside `notify_queues_observers_reversed`): the walk's order is proved for a source's direct effect observers, and across a swap-removal in `notify_after_swap`. A memo among the observers forwards through `mark_check`, depth first, under a freshness test that reads the state as the walk changes it. The smallest open lemma: `notify_dirty` appends the effects of a depth-first walk of `observer_items`, from the last position, entering a memo only when `Clean` or owing an announcement, and skipping an effect that is queued, disposed or in a cleanup. `history_order` takes its rounds from the engine's runs, so no theorem computes it from observer positions.
- `answers_where_reference_settles` (`theories/Pure/Refinement.v`, the premise `bodies_total` of `engine_of_answers_on`): `refinable` plus the reference's success, with no `bodies_total`. The inputs are done: `inputs_total_of_runs` derives them from the reference's success. **The bodies stop here.** Measure: `mu` is unaffected, and it needs no premise on bodies. Step: `exec_tot`, at each `on_value`, `on_node` and read, which takes `body_ends` over all answers because the layer's post-condition `kept` says nothing of a read's answer. Two lemmas are open, and each is the smallest I can state. `read_answers_denotation`: from `gd` under a flush or batch, a read ending in `tot` answers `memo_value` over the current signals. `Aread` proves this of a completed call under `rd_pre`, so the layer would carry `aS` as well as `gd`, with `run_begin` at each body's start, and `Aexec` takes `body_eval A e cm = Some r0` as a premise, which the reference's success has to supply for each body the engine starts. `engine_evaluates_within_reference`: each memo body the engine runs at given signals, and each effect body at given reads, is one whose evaluation by the reference's `memo_value`, `reads_moved` or `run_round` at those signals gave a result, which couples `resolve_walk`'s stop at the first moved source to `reads_moved`'s stop at the first difference. Not a counterexample: by reading, the engine's walk evaluates sources in slot order and stops as the reference does, and `bodies_conservative` shows the premise is stricter than needed.
- `memos_below_stuck_paths` (`theories/Pure/Refinement.v`, the field `memos_below` of `refinable`): `memos_below` takes a rank from the class `Rank`, and every theorem over `refinable` holds for every rank, so the id order is gone (proved; `reads_higher_refinable` is a memo reading a higher id inside the theorems). What remains is the stuck path: `memos_below` asks for a lower rank on every path of a memo's body, and `memos_acyclic` only on the paths that return. Not attempted. Where it stops: `tbl_pure` builds `pure_at`, which covers every path, from `memos_below`, so a premise over the paths that return needs a `pure_at` over those paths through `WholeGraph.v`. No counterexample: a read on a stuck path has no effect on either side's answer, by reading.

- `cleanups_before_replacement` (Task 13a, `theories/Pure/Optimised.v`): each cleanup registered outside its host's
  teardown has its `EvCleanup` before the host's next `EvRun` or its `EvDispose`. Not attempted: it is a statement of
  the present engine, it needs an invariant tying a host's scope (`n_scope`, `o_host`) to the cleanups registered with
  it, and the optimised engine inherits it unchanged through `optimised_trace_eq`. What is proved: the optimised trace's
  `EvCleanup` events are the present engine's, at most one per registration, and each teardown runs each detached
  entry once (`optimised_cleanups`).

## Open statements

Statements of what the fix phase owes, each a `Definition` of type `Prop` that the present engine falsifies. Each entry
names the statement, its proved `_falsified` witness and the proved weaker form later layers use. `check.sh` rejects every `Admitted`, `admit`, `give_up`, `Axiom`, `Parameter` and `Conjecture` under
`theories/`.

- `resolve_clean_no_move_statement` (`theories/Pure/Check.v`): `check_resolves_over_moved_source` falsifies it on the present engine, and
  `resolve_clean_no_move_falsified` proves that. Weaker form: `resolve_clean_unmoved`, under `reads_settled`.
- `resolve_clean_no_stale_source_statement` (`theories/Pure/Check.v`): `self_marking_wake_lost` falsifies it on the present engine, and
  `resolve_clean_no_stale_source_falsified` proves that. It covers live sources, since a disposed source stays stale without this
  bug: `resolve_over_disposed_dirty_source`. No weaker form is proved.
- `no_lost_wakeups_statement` (`theories/Pure/Runs.v`): `walk_passed_source_marked` falsifies it on the present engine, and `no_lost_wakeups_falsified`
  proves that. Weaker forms: `no_lost_wakeups_when_nothing_passed`, `no_lost_wakeups_unless_passed` and `no_wake_lost_unless_resolved`.
- `engine_inv_preserved_statement` (`theories/Pure/Runs.v`): the same counterexample, and `engine_inv_preserved_falsified` proves that.
  Weaker form: `engine_settled_preserved`.
- `marks_survive_run_statement` (`theories/Pure/Runs.v`): `nested_self_recompute_resets_cursor` falsifies it on the present engine, and `marks_survive_run_falsified`
  proves that. Weaker form: `marks_survive_body_end`.
- `run_end_edges_are_reads_statement` (`theories/Pure/Runs.v`): the same Confirmed bug, and `run_end_edges_are_reads_falsified` proves that.
  Weaker forms: `run_end_edges_are_latest_reads` and `run_end_edges_no_nested_run`.
- `weak_refinement_statement` (`theories/Pure/Refinement.v`): `disposed_unread_memo_value` falsifies it on the present engine and reference, and `weak_refinement_falsified` proves that. A ruling on that entry, a fix of `redundant_run_reads_untracked` and of `effect_resolves_over_moved_source`, the unattempted proofs for effect cleanups and child effects, and `memos_below_stuck_paths` under Unproved obligations are what it needs. `memos_below` takes any rank, so a memo reading a higher id is inside `weak_refinement_except` (proved: `reads_higher_refinable`; computed: `memo_reads_higher_id` gives identical `eng` and `rtrace` traces over a two-batch history). Weaker form: `weak_refinement_except`.
- `exact_refinement_statement` (`theories/Pure/Refinement.v`): `batch_restored_value_reruns` falsifies it, and `exact_refinement_falsified` proves that. Weaker form: `exact_refinement_except`.
- `engine_order_exact_statement` (`theories/Pure/Refinement.v`): this statement takes the queue at a flush's start as the flush's order. `requeue_runs_after_later_entry` falsifies that definition of the order, and `engine_order_exact_falsified` proves that. It says nothing of `history_order`. A fix of `diamond_marks_running_effect` that stops marking a running effect removes this requeue, by reading. Weaker form: `engine_order_exact_except`, which delivers "runs follow the drain".
- `engine_of_answers_statement` (`theories/Pure/Refinement.v`): `effect_restored_source_loop` falsifies it at every fuel, and `engine_of_answers_falsified` proves that. A ruling on effect writes decides what it should say. Weaker form: `engine_of_answers_on`, on a narrower fragment: `refinable`, `bodies_total` and the reference's success. See `answers_where_reference_settles`.
- `read_quiescent_statement` (`theories/Pure/WholeGraph.v`): `quiescent_read_answers_past_flush` falsifies it on the present engine, and `read_quiescent_falsified` proves that. Weaker form: `glitch_free_read`.
- `convergence_all_statement` (`theories/Pure/WholeGraph.v`): `self_marking_wake_lost` and the lost update of `cmp_never_passes_source` falsify it on the present engine, and `convergence_all_falsified` proves that, with each premise proved on both. Weaker form: `convergence`.
  **The fix phase's target.** Inside it, as the bugs the fix owes: effect writes, memo writes reaching no later read of the body, memo cleanups, `CmpNever`, and effect cleanups, child effects and disposals.
  **What it is not asked to fix through this statement.** Each premise, with what it excludes. Each field is checked over a memo's whole body, whichever path runs, and over every live memo of the program, so each one also excludes programs a fix may reach. `memos_no_throwing_comparer` is checked on the comparer, whether or not it fires.
  On a path that reaches it, a memo cycle, a self-read, an untracked read, `EPrev` or an excluded created node has no `den_all` answer. A throwing comparer is a failure `den_all` does not model, once the comparer fires.
  A memo reading `v`, then `if v<100 ret v else <excluded form>`, read by an effect, with the signal written to 5, has a false premise, `den_all` answers `Ok 5` and the conclusion holds: `untracked_conservative`, `prev_conservative` and `creation_conservative`. With a `CmpThrows` memo that ran once and a comparer never called, the premise is false, `den_all` answers `Ok 0` and the conclusion holds: `comparer_conservative`. All four are proved in `Witnesses.v`. `memos_ranked` has the same gap by reading, through a self-read on a branch that never runs.
  `memos_ranked` and `memos_read_before_write` are the notable cases, below.
  - `memos_ranked`: a memo cycle or self-read has no denotation on a path that reaches it. Conservatively, also a computed target with no bound, since the rank quantifies over every answer, as `memos_acyclic` does.
  - `memos_no_untracked`: a reader keeps the value of the read's run, which is what an untracked read means. `row_memo_untracked_excluded`; F#: signal 5, memo 0, `Clean`.
  - `memos_no_throwing_comparer`: the comparer's failure is outside the body's computation. `row_throwing_comparer_excluded`; F#: logs 0 then 7. Modelling it in the denotation is the alternative the ruling left open.
  - `memos_no_prev`: `EPrev` answers from the memo's history. `row_memo_prev_excluded`.
  - `memos_no_creation`: a created node's id varies by run. A signal in any memo, and a memo or effect in an `Owning` memo. `row_memo_creates_excluded`. The ruling names the `Pure` case. This premise covers the rest, where `erase` has no value.
  - `memos_read_before_write`: a read in a memo's body after a write on the same path that reaches its target. `row_read_between_writes_excluded`: every run answers 1 and `den_all` answers 2; F#: signal 2, memo 1.
    **Conservative.** It also excludes the shape of `check_resolves_over_moved_source`: `check_over_moved_excluded` (proved). There the effect saw 0, `den_all` answers 1 and the memo is `Clean`: `check_over_moved_left_behind` (computed). Reasoned: the memo's next run answers 1 and is stable, so a fix can reach this shape. That bug stays owed through the Open `resolve_clean_no_move_statement`. A narrower form tried in review left 30 unclassified candidates (fuzz, seed 73), so no narrower form is shown.
  **Forms the statement holds, proved on the present engine:** `row_child_effect_holds`, `row_effect_disposes_holds` (the logs clause has a gate for an effect creating or disposing a node), `row_pure_cleanup_holds`, `row_pure_creation_holds` (each denotes `pure_scope_failure`), `row_failing_write_holds` (`erase` keeps a write's value failure). `converged_all` covers effects with an owner: `row_pure_creation_holds` found that `exec` adds an effect created under `Pure` and then fails, leaving a node that never ran, where F# raises before the effect exists.
  Disposal and cycles need no further premise. `den_all` answers a disposed memo's stored view, and a body with no end fails the `run_top` premise. That is reasoned. Fuzz on the present engine, over programs passing a sufficient test of the premises: 0 hits in 15930 programs with effect cleanups, writes, child effects, disposals, batches and untracked reads; 694 hits in 1494 on the lost wake-up's template, the shortest a memo cleanup writing, so the harness sees the bug. The test admits no memo body that writes.
  **Programs with no fixed point.** Two memos writing each other's sources, `s1 := s2<2` and `s2 := s1+s3`, with `s3` at 1, have no fixed point, and the present engine ends on them. The pair is the shortest of 3 oscillating programs of 3904 passing the premises in review's fuzz, seed 71, and review inspected only the shortest. Reasoned: a correct engine does not end on a program with no fixed point, so the statement holds on that program only because such a fix hangs, which turns a program that ends today into a hang. Ten further `or_diverge` hits from the same search are unclassified.
  **Whole-program premises.** The premises cover the whole program, not an effect's cone: `## Rulings` records this.
