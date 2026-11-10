From stdpp Require Import prelude.
From stdpp Require Import gmap.
From Ranvier.Base Require Import Prelude.
From Ranvier.Base Require Import Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Engine.

(** * Ledger closures of the pure-tier report *)

Local Notation V := EVar.
Local Notation K := EConst.
Local Notation Nop := (CRet (EConst 0%Z)).

Local Definition final (p : cmd) : state :=
  default (init current_flags) (fst <$> run_top 100000 p (init current_flags)).

(** An effect reads memo [2], the host disposes the memo, and then writes its source. *)
Definition disposed_keeps_observer (k : cmd) : cmd :=
  CSignal 1 (K 0%Z) (
  CMemo 2 Pure CmpEq (CRead 10 (V 1) (CRet (V 10))) (
  CEffect 3 (CRead 10 (V 2) (CLog (V 10) Nop)) (
  CDispose (V 2) (CWrite (V 1) (K 5%Z) k)))).

(**md
**A disposed memo keeps its observers, and its reader stays clean on the stored value.**

*Check, not a guarantee.* After the disposal and the write, signal `1` holds 5, and memo `2` is disposed with its stored 0 and still holds effect `3`
as its observer. The effect has one run, is `Clean` and unqueued, and the trace has no second run. Core.fs 4372-4376
documents both halves: a disposed memo answers reads with the last value it computed, and stops being woken. Closed in
`BUGS.md` as spec corrected.
*)
Example disposed_memo_keeps_observers :
  let s := final (disposed_keeps_observer Nop) in
  (g ← g_nodes s !! 1; d ← g_nodes s !! 2; e ← g_nodes s !! 3;
   Some (n_value g, n_disposed d, n_value d, observer_view (observers_of (g_edges s) 2),
         n_freshness e, n_queued e, n_runs e)) = Some (5%Z, true, 0%Z, [3], Clean, false, 1) /\
  g_trace s = [EvRun 3; EvLog 3 0%Z; EvDispose 2].
Proof. split; vm_compute; reflexivity. Qed.
