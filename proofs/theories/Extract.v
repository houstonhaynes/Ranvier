From stdpp Require Import prelude gmap.
From Stdlib Require Extraction.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Marking Engine Optimised.

(** ** The end-state view *)

(** The fields of a node the differential check compares. A kind's irrelevant fields hold their
    construction values, and the driver leaves them out. *)
Record node_view := {
  v_id : node_id;
  v_kind : kind;
  v_value : val;
  v_status : status;
  v_failure : option val;
  v_freshness : freshness;
  v_unannounced : bool;
  v_runs : nat;
  v_disposed : bool;
  v_queued : bool;
  v_sources : nat;
  v_observers : nat
}.

(** The live queue length and the root owner's error count, beside each node in id order. *)
Record state_view := { v_nodes : list node_view; v_queue : nat; v_errors : nat }.

(**md
The compared fields of node `n`, whose stored node is `d`, with its source and observer counts in `s`.

- [s:] the state.
- [n:] the node id.
- [d:] the stored node.
*)
Definition view_node (s : state) (n : node_id) (d : node) : node_view :=
  {| v_id := n; v_kind := n_kind d; v_value := n_value d; v_status := n_status d;
     v_failure := n_failure d; v_freshness := n_freshness d; v_unannounced := n_unannounced d; v_runs := n_runs d;
     v_disposed := n_disposed d; v_queued := n_queued d;
     v_sources := length (source_slots (sources_of (g_edges s) n));
     v_observers := length (observer_items (observers_of (g_edges s) n)) |}.

(** Node ids run from 1 to [g_next_id]. *)
Definition node_ids (s : state) : list node_id := seq 1 (g_next_id s).

(**md
The end-state view of `s`: its nodes in id order, its pending queue length and its root owner's error count.
*)
Definition view_of (s : state) : state_view :=
  {| v_nodes := omap (fun n => view_node s n <$> g_nodes s !! n) (node_ids s);
     v_queue := length (pending s);
     v_errors := default 0 (length ∘ o_errors <$> g_owners s !! root_owner_id) |}.

(**md
The ids of the memos in `s`, in id order.
*)
Definition memo_ids (s : state) : list node_id :=
  omap (fun n => match n_kind <$> g_nodes s !! n with Some KMemo => Some n | _ => None end) (node_ids s).

(** A host read of each memo existing at the start, in id order, then [Graph.Flush]. *)
Definition closing (fuel : nat) : M unit :=
  fun s =>
    match fold_left
       (fun (m : M unit) n =>
          let* _ := m in
          let* _ := exec fuel [] (CTryRead 0 1 (EConst (Z.of_nat n)) (CRet (EConst 0%Z))) in
          m_return tt)
       (memo_ids s) (m_return tt)
      s with
    | Some (s', _) => run_flush fuel s'
    | None => None
    end.

(**md
The trace through the closing reads and flush, the view at the program's end, and the view after
the closing. `None` when the run or the closing is stuck or runs out of fuel.

- [fuel:] the bound on nested steps.
- [fl:] the flags of the new graph.
- [c:] the program run.
*)
Definition observed (fuel : nat) (fl : flags) (c : cmd) : option (list event * state_view * state_view) :=
  match run_top fuel c (init fl) with
  | None => None
  | Some (s, _) =>
    match closing fuel s with
    | None => None
    | Some (s', _) => Some (g_trace s', view_of s, view_of s')
    end
  end.

(** [closing] on the engine of [Pure/Optimised.v]. *)
Definition oclosing (fuel : nat) : M unit :=
  fun s =>
    match fold_left
       (fun (m : M unit) n =>
          let* _ := m in
          let* _ := oexec fuel [] (CTryRead 0 1 (EConst (Z.of_nat n)) (CRet (EConst 0%Z))) in
          m_return tt)
       (memo_ids s) (m_return tt)
      s with
    | Some (s', _) => orun_flush fuel s'
    | None => None
    end.

(**md
`observed` for the engine of `Pure/Optimised.v` in skip mode `m`. The epoch and the stamps are not in
the view.

- [fuel:] the bound on nested steps.
- [fl:] the flags of the new graph.
- [m:] the skip mode.
- [c:] the program run.
*)
Definition observed_mode (fuel : nat) (fl : flags) (m : skip_mode) (c : cmd)
    : option (list event * state_view * state_view) :=
  match oexec fuel [] c (with_skip m (init fl)) with
  | None => None
  | Some (s, _) =>
    match oclosing fuel s with
    | None => None
    | Some (s', _) => Some (g_trace s', view_of s, view_of s')
    end
  end.

Lemma eqM_closing_fold fuel (l : list node_id) (m1 m2 : M unit) :
  eqM m1 m2 ->
  eqM (fold_left (fun (m : M unit) n =>
         let* _ := m in let* _ := exec fuel [] (CTryRead 0 1 (EConst (Z.of_nat n)) (CRet (EConst 0%Z))) in m_return tt) l m1)
      (fold_left (fun (m : M unit) n =>
         let* _ := m in let* _ := oexec fuel [] (CTryRead 0 1 (EConst (Z.of_nat n)) (CRet (EConst 0%Z))) in m_return tt) l m2).
Proof.
  revert m1 m2. induction l as [|n l IH]; intros m1 m2 Hm; simpl; [done|]. apply IH.
  apply eqM_bind; [done|intros _]. apply eqM_bind; [apply (Oexec _ (osims_all fuel))|intros _].
  apply eqM_refl, keeps_return.
Qed.

(**md
**Under `SkipOff` the optimised engine's observation is the present engine's.**

- [fuel:] the bound on nested steps.
- [fl:] the flags of the new graph.
- [c:] the program run.
*)
Theorem observed_mode_off fuel fl c : observed_mode fuel fl SkipOff c = observed fuel fl c.
Proof.
  unfold observed_mode, observed, run_top. rewrite with_skip_off_init.
  destruct (Oexec _ (osims_all fuel) [] c) as [E K].
  rewrite <- (E (init fl) eq_refl).
  destruct (exec fuel [] c (init fl)) as [[s r]|] eqn:Ex; [|done].
  assert (Hs : g_skip s = SkipOff) by exact (K (init fl) s r eq_refl Ex).
  unfold oclosing, closing.
  destruct (eqM_closing_fold fuel (memo_ids s) (m_return tt) (m_return tt) (eqM_refl _ (keeps_return _))) as [F G].
  rewrite <- (F s Hs).
  match goal with |- context [match fold_left ?g (memo_ids s) (m_return tt) s with Some _ => _ | None => _ end] =>
    destruct (fold_left g (memo_ids s) (m_return tt) s) as [[s1 []]|] eqn:Ef; [|done] end.
  assert (Hs1 : g_skip s1 = SkipOff) by exact (G _ _ _ Hs Ef).
  by rewrite <- (proj1 (Oflush _ (osims_all fuel)) s1 Hs1).
Qed.

(** ** Extraction *)

(** Generates [engine.ml] and [engine.mli] in the working directory, [proofs/] under [build.sh]. [nat] and [Z]
    stay inductive, so the extracted interpreter computes as [vm_compute] does and adds no realised
    constant to what the differential check trusts. [proofs/differential/] holds the committed copy, and
    [check.sh] fails when the two differ. *)
Extraction Language OCaml.
Set Extraction Output Directory ".".

Extraction "engine.ml" trace_of observed observed_mode current_flags.
