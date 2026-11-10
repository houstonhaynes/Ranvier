From stdpp Require Import prelude.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Marking Engine Runs Witnesses Scheduler Ownership WholeGraph.
From Ranvier.Pure Require Import Ledger.
From Ranvier.Pure Require Optimised.
From Ranvier.Conformance Require Import Expected.

Open Scope Z_scope.

(** One program per scenario of [proofs/conformance/Scenarios.fsx]. A node's variable is its id. A body's
    locals start at 10. *)
Local Notation V := EVar.
Local Notation K := EConst.
Local Notation Nop := (CRet (EConst 0)).
Local Notation R := CRead.
Local Notation W := CWrite.
Local Notation L := CLog.

(**md
Diamond: two memos over one signal, and an effect reading both.
*)
Definition prog_S01 : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Pure CmpEq (R 10 (V 1) (L (V 10) (CRet (EAdd (V 10) (K 1))))) (
  CMemo 3 Pure CmpEq (R 10 (V 1) (L (V 10) (CRet (EMul (V 10) (K 2))))) (
  CEffect 4 (R 10 (V 2) (R 11 (V 3) (L (EAdd (V 10) (V 11)) Nop))) (
  W (V 1) (K 1) (W (V 1) (K 1) (W (V 1) (K 2) Nop)))))).

(**md
Cutoff: writes that leave a memo's value unchanged, under an effect reading the memo.
*)
Definition prog_S02 : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Pure CmpEq (R 10 (V 1) (L (V 10) (CRet (ELt (V 10) (K 10))))) (
  CEffect 3 (R 10 (V 2) (L (V 10) Nop)) (
  W (V 1) (K 1) (W (V 1) (K 20) (W (V 1) (K 30) Nop))))).

(**md
A conditional branch flips and drops an edge, and a write to the old branch's signal follows.
*)
Definition prog_S05 : cmd :=
  CSignal 1 (K 1) (CSignal 2 (K 10) (CSignal 3 (K 20) (
  CEffect 4
    (R 10 (V 1) (CIf (V 10) (R 11 (V 2) (L (V 11) Nop)) (R 11 (V 3) (L (V 11) Nop)))) (
  W (V 1) (K 0) (W (V 2) (K 11) (W (V 3) (K 21) Nop)))))).

(**md
An effect reads A, B, A, and its re-run reads B untracked.
*)
Definition prog_S06 : cmd :=
  CSignal 1 (K 0) (CSignal 2 (K 0) (
  CEffect 3
    (R 10 (V 1) (
     CIf (EEq (V 10) (K 0))
       (R 11 (V 2) (R 12 (V 1) (L (EAdd (EAdd (V 10) (V 11)) (V 12)) Nop)))
       (CReadUntracked 11 (V 2) (L (EAdd (V 10) (V 11)) Nop)))) (
  W (V 1) (K 1) (W (V 2) (K 5) (W (V 1) (K 2) Nop))))).

(**md
A batch writes 1 then 0 to a signal an effect reads directly.
*)
Definition prog_S07 : cmd :=
  CSignal 1 (K 0) (
  CEffect 2 (R 10 (V 1) (L (V 10) Nop)) (
  CBatch (W (V 1) (K 1) (W (V 1) (K 0) Nop)) Nop)).

(**md
An effect's cleanup writes the effect's own source.
*)
Definition prog_S08 : cmd :=
  CSignal 1 (K 0) (
  CEffect 2
    (R 10 (V 1) (L (V 10) (CCleanup (W (V 1) (EAdd (V 10) (K 10)) Nop) Nop))) (
  W (V 1) (K 1) (W (V 1) (K 11) Nop))).

(**md
An effect body throws in a flush, between two effects over the same signal, and a later write recovers it.
*)
Definition prog_S09a : cmd :=
  CSignal 1 (K 0) (
  CEffect 2 (R 10 (V 1) (L (V 10) Nop)) (
  CEffect 3 (R 10 (V 1) (CIf (ELt (K 0) (V 10)) (CThrow 7) (L (V 10) Nop))) (
  CEffect 4 (R 10 (V 1) (L (V 10) Nop)) (
  W (V 1) (K 1) (W (V 1) (K 0) Nop))))).

(**md
An effect's cleanup throws during each re-run, beside another effect over the same signal.
*)
Definition prog_S09b : cmd :=
  CSignal 1 (K 0) (
  CEffect 2 (R 10 (V 1) (L (V 10) Nop)) (
  CEffect 3 (R 10 (V 1) (CCleanup (CThrow 8) (L (V 10) Nop))) (
  W (V 1) (K 1) (W (V 1) (K 2) Nop)))).

(**md
An effect's body disposes the effect and keeps reading.
*)
Definition prog_S10 : cmd :=
  CSignal 1 (K 0) (
  CBatch
    (CEffect 2
      (R 10 (V 1) (L (V 10) (CCleanup (L (K 100) Nop)
        (CDispose (V 2) (R 11 (V 1) (L (V 11) Nop)))))) Nop) (
  CEffect 3 (R 10 (V 1) (L (V 10) Nop)) (
  W (V 1) (K 1) (W (V 1) (K 2) Nop)))).

(**md
An owning memo's cleanup writes the memo's source and reads the memo.
*)
Definition prog_S11 : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Owning CmpEq
    (R 10 (V 1) (
     CCleanup
       (CIf (EEq (V 10) (K 1))
         (W (V 1) (K 2) (R 11 (V 2) (L (V 11) Nop)))
         (R 11 (V 2) (L (V 11) Nop)))
     (CRet (V 10)))) (
  CEffect 3 (R 10 (V 2) (L (V 10) Nop)) (
  W (V 1) (K 1) (W (V 1) (K 5) Nop)))).

(**md
Owner disposal: an effect with interleaved cleanups and child effects, disposed twice, then a child disposed.
*)
Definition prog_S12 : cmd :=
  CEffect 1
    (CCleanup (L (K 1) Nop) (
     CEffect 2 (CCleanup (L (K 21) Nop) Nop) (
     CCleanup (L (K 2) Nop) (
     CEffect 3 (CCleanup (L (K 31) Nop) Nop) (
     CCleanup (L (K 3) (CCleanup (L (K 4) Nop) Nop)) Nop))))) (
  CDispose (V 1) (CDispose (V 1) (CDispose (K 2) Nop))).

(**md
An effect writes the signal it reads, climbing to 3.
*)
Definition prog_S13 : cmd :=
  CSignal 1 (K 0) (
  CEffect 2
    (R 10 (V 1) (L (V 10) (CIf (ELt (V 10) (K 3)) (W (V 1) (EAdd (V 10) (K 1)) Nop) Nop))) Nop).

(**md
A memo writes the signal it reads, under an effect reading the memo, then host reads of the memo and the signal.
*)
Definition prog_S14 : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Pure CmpEq
    (R 10 (V 1) (L (V 10) (CIf (ELt (V 10) (K 3)) (W (V 1) (EAdd (V 10) (K 1)) Nop) Nop))) (
  CEffect 3 (R 10 (V 2) (L (V 10) Nop)) (
  R 20 (V 2) (R 21 (V 1) Nop)))).

(**md
An effect reads `h`, `x` and `y`, `x` reads `h` and `y`, and a batch writes `h` and `y`'s source.
*)
Definition prog_S15 : cmd :=
  CSignal 1 (K 0) (CSignal 2 (K 0) (
  CMemo 3 Pure CmpEq (R 10 (V 2) (L (V 10) (CRet (V 10)))) (
  CMemo 4 Pure CmpEq
    (R 10 (V 1) (R 11 (V 3) (L (EAdd (V 10) (V 11)) (CRet (EAdd (V 10) (V 11)))))) (
  CEffect 5
    (R 10 (V 1) (R 11 (V 4) (R 12 (V 3) (L (EAdd (EAdd (V 10) (V 11)) (V 12)) Nop)))) (
  CBatch (W (V 1) (K 1) (W (V 2) (K 1) Nop)) Nop))))).

Definition prog_in_cleanup_observer_stays_clean : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Pure CmpEq (R 10 (V 1) (CRet (V 10))) (
  CEffect 3
    (R 10 (V 2) (L (V 10) (CCleanup (W (V 1) (K 100) Nop) Nop))) (
  W (V 1) (K 1) (R 20 (V 1) (W (V 1) (K 2) (R 21 (V 2) Nop)))))).

Definition prog_disposed_running_observer_stays_clean : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Pure CmpEq (R 10 (V 1) (CRet (V 10))) (
  CBatch
    (CEffect 3
      (CDispose (V 3) (R 10 (V 2) (L (V 10) (W (V 1) (EAdd (V 10) (K 1)) Nop)))) Nop) (
  W (V 1) (K 50) (R 20 (V 2) Nop)))).

Definition prog_self_marking_cleanup : cmd := self_marking_cleanup (R 20 (V 5) Nop).
Definition prog_self_marking_body : cmd := self_marking_body (R 20 (V 2) Nop).
Definition prog_self_marking_through_memo : cmd := self_marking_through_memo (R 20 (V 3) Nop).
Definition prog_nested_self_run : cmd := nested_self_run (R 20 (V 4) Nop).
Definition prog_check_over_moved : cmd := check_over_moved (R 22 (V 4) Nop).
Definition prog_check_two_reads : cmd := check_two_reads (R 22 (V 3) Nop).
Definition prog_stale_source_reader : cmd := stale_source_reader (R 22 (V 3) (R 23 (V 3) (R 24 (V 2) Nop))).
Definition prog_disposed_memo_keeps_observers : cmd := disposed_keeps_observer Nop.
Definition prog_disposed_self_window : cmd := disposed_window (R 22 (V 4) Nop).
Definition prog_disposed_stale_source_read : cmd := disposed_stale_read (R 22 (V 2) Nop).
Definition prog_walk_passed_source_marked : cmd := walk_passed (W (V 1) (K 2) (R 22 (V 3) Nop)).
Definition prog_walk_passed_source_cleanup : cmd := walk_passed_by_cleanup (W (V 1) (K 2) (R 22 (V 3) Nop)).
Definition prog_effect_resolves_over_moved_source : cmd := effect_over_moved (W (V 1) (K 2) Nop).

(**md
A memo that answers its previous value plus 1, and an owning memo that creates a memo on one branch, with host reads before and after a write.
*)
Definition prog_previous_value : cmd :=
  CSignal 1 (K 0) (
  CMemo 2 Pure CmpEq
    (R 10 (V 1) (CIf (EEq (V 10) (K 0)) (CRet (K 1)) (CRet (EAdd EPrev (K 1))))) (
  CMemo 3 Owning CmpEq
    (R 10 (V 1) (
     CIf (V 10)
       (CMemo 11 Pure CmpEq (CRet (K 7)) (R 12 (V 11) (L (V 12) (CRet (V 10)))))
       (CRet (K 42)))) (
  R 20 (V 2) (R 21 (V 3) (W (V 1) (K 5) (R 22 (V 2) (R 23 (V 3) Nop))))))).

Example check_S01 : trace_of 100000 current_flags prog_S01 = Some exp_S01.
Proof. vm_compute. reflexivity. Qed.
Example check_S02 : trace_of 100000 current_flags prog_S02 = Some exp_S02.
Proof. vm_compute. reflexivity. Qed.
Example check_S03 : trace_of 100000 current_flags prog_S03 = Some exp_S03.
Proof. vm_compute. reflexivity. Qed.
Example check_S04 : trace_of 100000 current_flags prog_S04 = Some exp_S04.
Proof. vm_compute. reflexivity. Qed.
Example check_S05 : trace_of 100000 current_flags prog_S05 = Some exp_S05.
Proof. vm_compute. reflexivity. Qed.
Example check_S06 : trace_of 100000 current_flags prog_S06 = Some exp_S06.
Proof. vm_compute. reflexivity. Qed.
Example check_S07 : trace_of 100000 current_flags prog_S07 = Some exp_S07.
Proof. vm_compute. reflexivity. Qed.
Example check_S08 : trace_of 100000 current_flags prog_S08 = Some exp_S08.
Proof. vm_compute. reflexivity. Qed.
Example check_S09a : trace_of 100000 current_flags prog_S09a = Some exp_S09a.
Proof. vm_compute. reflexivity. Qed.
Example check_S09b : trace_of 100000 current_flags prog_S09b = Some exp_S09b.
Proof. vm_compute. reflexivity. Qed.
Example check_S10 : trace_of 100000 current_flags prog_S10 = Some exp_S10.
Proof. vm_compute. reflexivity. Qed.
Example check_S11 : trace_of 100000 current_flags prog_S11 = Some exp_S11.
Proof. vm_compute. reflexivity. Qed.
Example check_S12 : trace_of 100000 current_flags prog_S12 = Some exp_S12.
Proof. vm_compute. reflexivity. Qed.
Example check_S13 : trace_of 100000 current_flags prog_S13 = Some exp_S13.
Proof. vm_compute. reflexivity. Qed.
Example check_S14 : trace_of 100000 current_flags prog_S14 = Some exp_S14.
Proof. vm_compute. reflexivity. Qed.
Example check_S15 : trace_of 100000 current_flags prog_S15 = Some exp_S15.
Proof. vm_compute. reflexivity. Qed.
(** The programs of [Optimised.leave_dirty_cleanup_skip] and [Optimised.leave_dirty_unannounced_skip]. Their
    variables are not the node ids. *)
Example check_S16_cleanup_rewrite :
  trace_of 100000 current_flags Optimised.prog_cleanup_rewrite = Some exp_S16_cleanup_rewrite.
Proof. vm_compute. reflexivity. Qed.
Example check_S17_self_write_announce :
  trace_of 100000 current_flags Optimised.prog_self_write_announce = Some exp_S17_self_write_announce.
Proof. vm_compute. reflexivity. Qed.
Example check_S18_cleanup_in_teardown :
  trace_of 100000 current_flags Optimised.prog_cleanup_in_teardown = Some exp_S18_cleanup_in_teardown.
Proof. vm_compute. reflexivity. Qed.
Example check_in_cleanup_observer_stays_clean :
  trace_of 100000 current_flags prog_in_cleanup_observer_stays_clean =
    Some exp_in_cleanup_observer_stays_clean.
Proof. vm_compute. reflexivity. Qed.
Example check_disposed_running_observer_stays_clean :
  trace_of 100000 current_flags prog_disposed_running_observer_stays_clean =
    Some exp_disposed_running_observer_stays_clean.
Proof. vm_compute. reflexivity. Qed.

(** The state the F# scenarios assert beside their traces. *)
Local Definition final (p : cmd) : option state :=
  fst <$> run_top 100000 p (init current_flags).

(** [S09a] before its recovering write: the effect holds its failure. *)
Example check_S09a_status :
  let p :=
    CSignal 1 (K 0) (
    CEffect 2 (R 10 (V 1) (L (V 10) Nop)) (
    CEffect 3 (R 10 (V 1) (CIf (ELt (K 0) (V 10)) (CThrow 7) (L (V 10) Nop))) (
    CEffect 4 (R 10 (V 1) (L (V 10) Nop)) (
    W (V 1) (K 1) Nop)))) in
  (s ← final p; d ← g_nodes s !! 3%nat; Some (n_status d, n_failure d)) = Some (StError, Some 7).
Proof. vm_compute. reflexivity. Qed.

(** [S09b]: each cleanup failure stays out of the flush and lands in the root's errors. *)
Example check_S09b_errors :
  (s ← final prog_S09b; x ← g_owners s !! root_owner_id; Some (o_errors x)) = Some [8; 8].
Proof. vm_compute. reflexivity. Qed.

(** [S10]: only the live effect observes the signal. *)
Example check_S10_edges :
  (s ← final prog_S10; Some (observer_items (observers_of (g_edges s) 1%nat))) = Some [(3%nat, 0%nat)].
Proof. vm_compute. reflexivity. Qed.

(** The disposed effect's edge to the memo ends with its run. *)
Example check_disposed_running_edges :
  (s ← final prog_disposed_running_observer_stays_clean; Some (observer_items (observers_of (g_edges s) 2%nat))) = Some [].
Proof. vm_compute. reflexivity. Qed.

(** The scenario ends with [Graph.Flush], which has no command. *)
Local Definition trace_then_flush (p : cmd) : option (list event) :=
  r ← run_top 100000%nat p (init current_flags);
  r' ← run_flush 100000%nat r.1;
  Some (g_trace r'.1).

Example check_self_marking_cleanup :
  trace_then_flush prog_self_marking_cleanup = Some exp_self_marking_cleanup.
Proof. vm_compute. reflexivity. Qed.
Example check_self_marking_body :
  trace_of 100000 current_flags prog_self_marking_body = Some exp_self_marking_body.
Proof. vm_compute. reflexivity. Qed.
Example check_self_marking_through_memo :
  trace_of 100000 current_flags prog_self_marking_through_memo = Some exp_self_marking_through_memo.
Proof. vm_compute. reflexivity. Qed.
Example check_nested_self_run :
  trace_of 100000 current_flags prog_nested_self_run = Some exp_nested_self_run.
Proof. vm_compute. reflexivity. Qed.
Example check_check_over_moved :
  trace_of 100000 current_flags prog_check_over_moved = Some exp_check_over_moved.
Proof. vm_compute. reflexivity. Qed.
Example check_check_two_reads :
  trace_of 100000 current_flags prog_check_two_reads = Some exp_check_two_reads.
Proof. vm_compute. reflexivity. Qed.
Example check_stale_source_reader :
  trace_of 100000 current_flags prog_stale_source_reader = Some exp_stale_source_reader.
Proof. vm_compute. reflexivity. Qed.
(** `BUGS.md`, `disposed_memo_keeps_observers`: a disposed memo keeps its observer and wakes none. *)
Example check_disposed_memo_keeps_observers :
  trace_of 100000 current_flags prog_disposed_memo_keeps_observers = Some exp_disposed_memo_keeps_observers.
Proof. vm_compute. reflexivity. Qed.
Example check_disposed_self_window :
  trace_of 100000 current_flags prog_disposed_self_window = Some exp_disposed_self_window.
Proof. vm_compute. reflexivity. Qed.
Example check_disposed_stale_source_read :
  trace_of 100000 current_flags prog_disposed_stale_source_read = Some exp_disposed_stale_source_read.
Proof. vm_compute. reflexivity. Qed.
Example check_walk_passed_source_marked :
  trace_of 100000 current_flags prog_walk_passed_source_marked = Some exp_walk_passed_source_marked.
Proof. vm_compute. reflexivity. Qed.
Example check_walk_passed_source_cleanup :
  trace_of 100000 current_flags prog_walk_passed_source_cleanup = Some exp_walk_passed_source_cleanup.
Proof. vm_compute. reflexivity. Qed.
Example check_effect_resolves_over_moved_source :
  trace_of 100000 current_flags prog_effect_resolves_over_moved_source = Some exp_effect_resolves_over_moved_source.
Proof. vm_compute. reflexivity. Qed.
Example check_previous_value :
  trace_of 100000 current_flags prog_previous_value = Some exp_previous_value.
Proof. vm_compute. reflexivity. Qed.

(** A memo nested in a published memo has no previous value on its first run, as [compute] gets
    [ValueNone] at Core.fs 4234: [EPrev] is stuck. *)
Example nested_memo_prev_stuck :
  trace_of 100000 current_flags
    (CSignal 1 (K 0) (
     CMemo 2 Owning CmpEq
       (R 10 (V 1) (CIf (V 10)
         (CMemo 11 Pure CmpEq (CRet EPrev) (R 12 (V 11) (L (V 12) (CRet (V 10)))))
         (CRet (K 42)))) (
     R 20 (V 2) (W (V 1) (K 5) (R 21 (V 2) Nop))))) = None.
Proof. vm_compute. reflexivity. Qed.

(** The window of [disposed_running_observer_stays_clean], from a body running for effect 3 after its
    disposal: the read links an edge, and the write leaves the effect clean and unqueued. *)
Example check_disposed_window :
  let setup :=
    CSignal 1 (K 0) (CMemo 2 Pure CmpEq (R 10 (V 1) (CRet (V 10))) (CEffect 3 Nop Nop)) in
  let e : env := [(3%nat, 3); (2%nat, 2); (1%nat, 1)] in
  let run (b : cmd) :=
    r ← run_top 100000%nat setup (init current_flags);
    r' ← hosted 3%nat (exec 100000%nat e (CDispose (V 3) b)) r.1;
    Some r'.1 in
  (s ← run (R 10 (V 2) Nop); Some (observer_items (observers_of (g_edges s) 2%nat))) = Some [(3%nat, 0%nat)] /\
  (s ← run (R 10 (V 2) (W (V 1) (K 1) Nop)); d ← g_nodes s !! 3%nat;
   Some (n_freshness d, n_queued d, length (pending s))) = Some (Clean, false, 0%nat).
Proof. split; vm_compute; reflexivity. Qed.

(** ** [gh_passed] on the conformance programs *)

(**md
The state a program ends in, under `current_flags`, or a new graph when the program has no result.
*)
Local Definition end_of (p : cmd) : state :=
  default (init current_flags) (fst <$> run_top 100000 p (init current_flags)).

Example ordinary_programs_nothing_passed :
  Forall (fun p => nothing_passed (end_of p))
    [prog_S01; prog_S02; prog_S03; prog_S04; prog_S05; prog_S06; prog_S07; prog_S08; prog_S09a; prog_S09b;
     prog_S10; prog_S11; prog_S12; prog_S13; prog_S14; prog_S15; prog_previous_value].
Proof. repeat apply Forall_cons_2; try apply Forall_nil_2; vm_compute; reflexivity. Qed.

Local Lemma covered_of_nothing_passed p src c :
  run_top 100000 p (init current_flags) = Some (end_of p, Ok 0) ->
  nothing_passed (end_of p) -> edge (end_of p) src c -> stale_of (end_of p) src -> live_of (end_of p) src ->
  observer_covered (end_of p) src c.
Proof.
  intros Hr Hn He Hs Hl. exact (no_lost_wakeups_when_nothing_passed 100000 p _ _ _ engine_settled_init Hr Hn src c He Hs Hl).
Qed.

(** A write leaves a memo [Dirty] and its reader in [Check]: a live, stale edge with [gh_passed] empty, whose
    observer is stale. *)
Example ordinary_stale_edge_covered :
  let s := end_of (check_cutoff Nop) in
  stale_of s 2%nat /\ edge s 2%nat 3%nat /\ nothing_passed s /\ observer_covered s 2%nat 3%nat.
Proof.
  intros s.
  assert (He : edge s 2%nat 3%nat) by (exists 0%nat, 0%nat; split; vm_compute; reflexivity).
  assert (Hn : nothing_passed s) by (vm_compute; reflexivity).
  assert (Hs : stale_of s 2%nat) by (eexists; split; vm_compute; reflexivity).
  assert (Hl : live_of s 2%nat) by (eexists; split; vm_compute; reflexivity).
  split_and!; [done|done|done|].
  apply covered_of_nothing_passed; [vm_compute; reflexivity|done|done|done|done].
Qed.

(** A memo's cleanup leaves it [Dirty] over a clean, idle effect, with [gh_passed] empty: the edge is covered by the
    memo's flag alone. *)
Example flagged_stale_edge_covered :
  let s := end_of (self_marking_cleanup Nop) in
  stale_of s 5%nat /\ edge s 5%nat 6%nat /\ nothing_passed s /\ observer_covered s 5%nat 6%nat.
Proof.
  intros s.
  assert (He : edge s 5%nat 6%nat) by (exists 0%nat, 0%nat; split; vm_compute; reflexivity).
  assert (Hn : nothing_passed s) by (vm_compute; reflexivity).
  assert (Hs : stale_of s 5%nat) by (eexists; split; vm_compute; reflexivity).
  assert (Hl : live_of s 5%nat) by (eexists; split; vm_compute; reflexivity).
  split_and!; [done|done|done|].
  apply covered_of_nothing_passed; [vm_compute; reflexivity|done|done|done|done].
Qed.

(** ** Scheduler and ownership *)

Example check_teardown_order : trace_of 100000%nat current_flags teardown_order = Some exp_teardown_order.
Proof. vm_compute. reflexivity. Qed.

Example check_late_registrations : trace_of 100000%nat current_flags late_registrations = Some exp_late_registrations.
Proof. vm_compute. reflexivity. Qed.

Example check_flush_child_release : trace_of 100000%nat current_flags flush_child_release = Some exp_flush_child_release.
Proof. vm_compute. reflexivity. Qed.

Example check_flush_memo_failure : trace_of 100000%nat current_flags flush_memo_failure = Some exp_flush_memo_failure.
Proof. vm_compute. reflexivity. Qed.

Example check_queued_effect_disposed : trace_of 100000%nat current_flags queued_effect_disposed = Some exp_queued_effect_disposed.
Proof. vm_compute. reflexivity. Qed.

Example check_late_cleanup_failure : trace_of 100000%nat current_flags late_cleanup_failure = Some exp_late_cleanup_failure.
Proof. vm_compute. reflexivity. Qed.

Example check_memo_scope_violation : trace_of 100000%nat current_flags memo_scope_violation = Some exp_memo_scope_violation.
Proof. vm_compute. reflexivity. Qed.

Example check_memo_comparer_failure : trace_of 100000%nat current_flags memo_comparer_failure = Some exp_memo_comparer_failure.
Proof. vm_compute. reflexivity. Qed.

Example check_flush_comparer : trace_of 100000%nat current_flags flush_comparer = Some exp_flush_comparer.
Proof. vm_compute. reflexivity. Qed.

(** The program ends at the failed batch, and the F# scenario's explicit flush is the [run_flush] here. *)
Example check_failed_batch :
  (r ← run_top 100000%nat failed_batch (init current_flags); r' ← run_flush 1000%nat r.1; Some (g_trace r'.1)) =
  Some exp_failed_batch.
Proof. vm_compute. reflexivity. Qed.

(** ** The whole graph *)

Example check_cmp_never_passes_source :
  trace_of 100000%nat current_flags (cmp_never_source (CRead 20 (V 5) Nop)) = Some exp_cmp_never_passes_source.
Proof. vm_compute. reflexivity. Qed.

(** The two scenarios share a model program. The closing read queues the effect, and the F# scenario's explicit flush is the
    [run_flush] here. *)
Local Definition never_lost_trace : option (list event) :=
  r ← run_top 100000%nat (never_lost (CWrite (V 2) (K 8) (CRead 20 (V 6) Nop))) (init current_flags);
  r' ← run_flush 1000%nat r.1; Some (g_trace r'.1).

Example check_cmp_never_lost_update :
  never_lost_trace = Some exp_cmp_never_lost_update.
Proof. vm_compute. reflexivity. Qed.

Example check_fresh_record_lost_update :
  never_lost_trace = Some exp_fresh_record_lost_update.
Proof. vm_compute. reflexivity. Qed.

(** The host read is the [read] here, and the F# scenario records its answer once the read returns. *)
Example check_read_settles_writer :
  (r ← run_top 100000%nat read_flush (init current_flags); r' ← read 1000%nat 3%nat true r.1; Some (g_trace r'.1 ++ [EvHostRead 3%nat r'.2])) = Some exp_read_settles_writer.
Proof. vm_compute. reflexivity. Qed.

(** The host read is the [read] here, as for [read_settles_writer]. *)
Example check_quiescent_read_answers_past_flush :
  (r ← run_top 100000%nat read_past_flush (init current_flags); r' ← read 1000%nat 5%nat true r.1; Some (g_trace r'.1 ++ [EvHostRead 5%nat r'.2])) = Some exp_quiescent_read_answers_past_flush.
Proof. vm_compute. reflexivity. Qed.

Example check_quiescent_read_answers_past_flush_by_cleanup :
  (r ← run_top 100000%nat read_past_flush_by_cleanup (init current_flags); r' ← read 1000%nat 5%nat true r.1; Some (g_trace r'.1 ++ [EvHostRead 5%nat r'.2])) = Some exp_quiescent_read_answers_past_flush_by_cleanup.
Proof. vm_compute. reflexivity. Qed.

Example check_disposed_unread_memo_value :
  trace_of 100000 current_flags
    (CSignal 1 (K 0) (CMemo 2 Pure CmpEq (R 10 (V 1) (CRet (V 10))) (CSignal 3 (K 0) (
     CEffect 4 (R 10 (V 3) (CIf (EEq (V 10) (K 0)) Nop (R 11 (V 2) (L (V 11) Nop)))) (
     W (V 1) (K 5) (CDispose (V 2) (W (V 3) (K 1) Nop))))))) = Some exp_disposed_unread_memo_value.
Proof. vm_compute. reflexivity. Qed.

Example check_write_then_dispose_memo_current_value :
  trace_of 100000 current_flags
    (CSignal 1 (K 0) (CMemo 2 Pure CmpEq (R 10 (V 1) (CRet (EAdd (V 10) (K 1)))) (CSignal 3 (K 0) (
     CEffect 4 (R 10 (V 2) (R 11 (V 3) (L (V 10) Nop))) (
     CBatch (W (V 1) (K 5) (CDispose (V 2) Nop)) (W (V 3) (K 1) Nop)))))) = Some exp_write_then_dispose_memo_current_value.
Proof. vm_compute. reflexivity. Qed.

Example check_redundant_run_reads_untracked :
  trace_of 100000 current_flags
    (CSignal 1 (K 0) (CSignal 2 (K 0) (
     CEffect 3 (R 10 (V 1) (CReadUntracked 11 (V 2) (L (V 11) Nop))) (
     CBatch (W (V 2) (K 5) (W (V 1) (K 1) (W (V 1) (K 0) Nop))) Nop)))) = Some exp_redundant_run_reads_untracked.
Proof. vm_compute. reflexivity. Qed.

Example check_requeue_runs_after_later_entry :
  trace_of 100000 current_flags
    (CSignal 1 (K 0) (CSignal 2 (K 0) (
     CMemo 3 Pure CmpEq (R 10 (V 2) (CRet (V 10))) (
     CMemo 4 Pure CmpEq (R 10 (V 1) (R 11 (V 3) (CRet (EAdd (V 10) (V 11))))) (
     CEffect 5 (R 10 (V 1) (L (V 10) Nop)) (
     CEffect 6 (R 10 (V 1) (R 11 (V 4) (R 12 (V 3) (L (EAdd (EAdd (V 10) (V 11)) (V 12)) Nop)))) (
     CBatch (W (V 1) (K 1) (W (V 2) (K 2) Nop)) Nop))))))) = Some exp_requeue_runs_after_later_entry.
Proof. vm_compute. reflexivity. Qed.

Example check_effect_restored_source_reruns :
  trace_of 100000 current_flags
    (CSignal 1 (K 0) (CSignal 2 (K 3) (
     CEffect 3 (R 10 (V 1) (L (V 10) (CReadUntracked 11 (V 2)
       (CIf (V 11) (W (V 2) (ESub (V 11) (K 1)) (W (V 1) (K 1) (W (V 1) (K 0) Nop))) Nop)))) Nop))) = Some exp_effect_restored_source_reruns.
Proof. vm_compute. reflexivity. Qed.

(** The conformance programs with every memo outside the self-dirtying case and every effect only reading. *)
Example conformance_programs_well_behaved :
  Forall (fun p => well_behaved_b (end_of p) = true /\ effects_only_read_b (end_of p) = true)
    [prog_S01; prog_S02; prog_S03; prog_S05; prog_S07; prog_S09a; prog_S15].
Proof. repeat apply Forall_cons_2; try apply Forall_nil_2; split; vm_compute; reflexivity. Qed.
