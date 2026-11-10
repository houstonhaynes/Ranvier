From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Reference Require Export Semantics.

(** * The reference trace is deterministic *)

Lemma settle_mono o1 o2 p :
  (forall i l, o1 i l = o2 i l) ->
  forall f1 f2 st r,
    (f1 <= f2)%nat ->
    settle f1 o1 p st = Some r ->
    settle f2 o2 p st = Some r.
Proof.
  intros Hext f1. induction f1 as [|f1 IH]; intros f2 st r Hle Hs.
  - destruct f2; simpl in *;
      destruct (run_set p st (effect_ids p)) as [[|n ns]|]; simpl in *; congruence.
  - destruct f2 as [|f2]; [lia|]. simpl in *.
    destruct (run_set p st (effect_ids p)) as [[|n ns]|]; simpl in *; try congruence.
    rewrite <- Hext.
    destruct (run_round p (o1 (rs_round st) (n :: ns)) st) as [[st1 r1]|]; simpl in *; [|discriminate].
    destruct (settle f1 o1 p (next_round st1)) as [[st2 r2]|] eqn:E; simpl in *; [|discriminate].
    rewrite (IH f2 (next_round st1) (st2, r2)); [done|lia|done].
Qed.

Lemma run_history_mono o1 o2 p f1 f2 :
  (forall i l, o1 i l = o2 i l) ->
  (f1 <= f2)%nat ->
  forall h st r,
    run_history f1 o1 p h st = Some r ->
    run_history f2 o2 p h st = Some r.
Proof.
  intros Hext Hle h. induction h as [|b h IH]; intros st r Hs; simpl in *; [done|].
  destruct (apply_batch p b st) as [[st0 r0]|]; simpl in *; [|discriminate].
  destruct (settle f1 o1 p st0) as [[st1 r1]|] eqn:E1; simpl in *; [|discriminate].
  rewrite (settle_mono o1 o2 p Hext f1 f2 _ _ Hle E1). simpl.
  destruct (run_history f1 o1 p h st1) as [[st2 r2]|] eqn:E2; simpl in *; [|discriminate].
  rewrite (IH st1 (st2, r2) E2). done.
Qed.

Lemma reference_runs_mono o1 o2 p f1 f2 h rs :
  (forall i l, o1 i l = o2 i l) ->
  (f1 <= f2)%nat ->
  reference_runs f1 o1 p h = Some rs ->
  reference_runs f2 o2 p h = Some rs.
Proof.
  intros Hext Hle. unfold reference_runs. rewrite <- Hext.
  destruct (run_round _ _ _) as [[st1 r1]|]; simpl; [|discriminate].
  destruct (settle f1 o1 p (next_round st1)) as [[st2 r2]|] eqn:E1; simpl; [|discriminate].
  rewrite (settle_mono o1 o2 p Hext f1 f2 _ _ Hle E1). simpl.
  destruct (run_history f1 o1 p h st2) as [[st3 r3]|] eqn:E2; simpl; [|discriminate].
  rewrite (run_history_mono o1 o2 p f1 f2 Hext Hle h st2 (st3, r3) E2). done.
Qed.

(**md
**The reference gives one trace for a program, a history and an effect order.**

Two orders that agree at every round on every list, with any two fuels that both reach a result, give the same trace.

- [f1:] the fuel of the first run.
- [f2:] the fuel of the second run.
- [o1:] the effect order of the first run.
- [o2:] the effect order of the second run, which agrees with `o1` on every round and list.
- [p:] the program.
- [h:] the history.
- [t1:] the trace of the first run.
- [t2:] the trace of the second run.
*)
Theorem reference_trace_deterministic f1 f2 o1 o2 p h t1 t2 :
  (forall i l, o1 i l = o2 i l) ->
  reference_trace f1 o1 p h = Some t1 ->
  reference_trace f2 o2 p h = Some t2 ->
  t1 = t2.
Proof.
  intros Hext. unfold reference_trace.
  destruct (reference_runs f1 o1 p h) as [r1|] eqn:E1; simpl; [|discriminate].
  destruct (reference_runs f2 o2 p h) as [r2|] eqn:E2; simpl; [|discriminate].
  intros [= <-] [= <-].
  destruct (Nat.le_total f1 f2) as [Hle|Hle].
  - rewrite (reference_runs_mono o1 o2 p f1 f2 h r1 Hext Hle E1) in E2. congruence.
  - rewrite (reference_runs_mono o2 o1 p f2 f1 h r2 (fun i l => eq_sym (Hext i l)) Hle E2) in E1. congruence.
Qed.

(** * A memo's value follows its dependencies *)

#[local] Arguments memo_value : simpl never.
#[local] Arguments live_read : simpl never.
#[local] Arguments frozen_read : simpl never.

#[local] Ltac bind_in H :=
  repeat match type of H with
  | context [?x ≫= _] =>
      let E := fresh "E" in destruct x eqn:E; simpl in H; [|discriminate H]
  | context [match ?o with Ok _ => _ | Fail _ => _ end] =>
      is_var o; destruct o; simpl in H
  end.

(** Splits on a scope's retirement where a [CCleanup] case tests it. *)
#[local] Ltac split_retired H :=
  match type of H with
  | context [body_retired ?st] =>
      let R := fresh "R" in destruct (body_retired st) eqn:R
  | _ => idtac
  end.

(** Rewrites the goal with the expression and node results [bind_in] found under binders. *)
#[local] Ltac rw_binds :=
  repeat match goal with
  | E : node_of _ = Some _ |- _ => rewrite E; clear E; simpl
  | E : eval_expr _ _ _ = Some _ |- _ => first [rewrite E; clear E; simpl | clear E]
  end.

Lemma note_fail_signals n o st : body_signals (note_fail n o st) = body_signals st.
Proof. by destruct o. Qed.
Lemma note_fail_reads n o st : body_reads (note_fail n o st) = body_reads st.
Proof. by destruct o. Qed.
Lemma note_fail_writes n o st : body_writes (note_fail n o st) = body_writes st.
Proof. by destruct o. Qed.

Lemma exec_reads_mono p rd n c :
  forall g st o st' x,
    exec p rd n g c st = Some (o, st') ->
    x ∈ body_reads st ->
    x ∈ body_reads st'.
Proof.
  induction c; intros g st o' st' x' Hex Hx; simpl in Hex; split_retired Hex; bind_in Hex;
    repeat case_match; simplify_eq/=; try done;
    try (eapply IHc; [exact Hex|]; simpl; set_solver);
    try (eapply IHc1; [exact Hex|]; done);
    try (eapply IHc2; [exact Hex|]; done);
    try (simpl; set_solver).
  - destruct p0 as [o1 st1]; simpl in Hex. eapply IHc2; [exact Hex|].
    rewrite note_fail_reads. eapply IHc1; [exact E|done].
  - destruct p0; simplify_eq/=. eapply IHc2; [exact Hex|]. eapply IHc1; [exact E|done].
Qed.

Lemma exec_ext p (rd1 rd2 : node_id -> option outcome) n c :
  forall g st o st',
    well_behaved_memo c = true ->
    exec p (fun _ => rd1) n g c st = Some (o, st') ->
    (forall t u, (t, u) ∈ body_reads st' -> rd1 t = Some u -> rd2 t = Some u) ->
    exec p (fun _ => rd2) n g c st = Some (o, st').
Proof.
  induction c; intros g st o' st' Hwb Hex Hag; simpl in Hwb, Hex |- *;
    rewrite ?andb_true_iff in Hwb; try discriminate Hwb; bind_in Hex; simplify_eq/=; try done.
  all: rw_binds.
  all: repeat match goal with H : _ /\ _ |- _ => destruct H end.
  all: try match goal with
    | E : ?r ?t = Some ?u |- _ =>
        rewrite (Hag t u);
        [ simpl
        | first [ simpl; set_solver
                | eapply exec_reads_mono; [exact Hex|]; simpl; set_solver ]
        | exact E ]
    end.
  all: try done.
  all: try by repeat case_match; eauto.
  destruct p0 as [o1 st1]; simpl in *. rewrite (IHc1 g st o1 st1); [|done|done|].
  - simpl. destruct o1; simplify_eq/=; eauto.
  - intros t u Ht. apply Hag. destruct o1; simplify_eq/=; [|done].
    eapply exec_reads_mono; [exact Hex|done].
Qed.

Lemma node_value_ext p frz s1 s2 :
  map_Forall (fun _ c => well_behaved_memo c = true) (program_memos p) ->
  forall k n o,
    node_value k p frz s1 n = Some o ->
    (forall s, s ∈ node_deps k p frz s1 n -> s1 !! s = s2 !! s) ->
    node_value k p frz s2 n = Some o.
Proof.
  intros Hwb k. induction k as [|k IH]; intros n o Hv Hdeps; simpl in *; [discriminate|].
  destruct (frz !! n) as [o0|]; [done|].
  destruct (program_signals p !! n) as [v|] eqn:Es.
  - rewrite <- (Hdeps n); [done|set_solver].
  - destruct (program_memos p !! n) as [c|] eqn:Em; [|discriminate].
    destruct (exec p (fun _ => node_value k p frz s1) n [] c (body_start ∅)) as [[o1 st1]|] eqn:Ex;
      simpl in Hv; [|discriminate]. simplify_eq.
    erewrite (exec_ext p _ (node_value k p frz s2) n c); [done|by eapply Hwb|exact Ex|].
    intros t u Hin Ht. apply (IH t u Ht). intros s Hs. apply Hdeps.
    apply list_elem_of_In, in_flat_map. exists (t, u). split.
    + by apply list_elem_of_In.
    + by apply list_elem_of_In.
Qed.

(**md
**With the same disposed memos, a memo's value depends only on the signals its evaluation reached.**

A memo's value over one set of signals is also its value over any other set that agrees on the signals in `memo_deps` of the first evaluation, with the same disposed memos' answers.

Premise: the program is well formed.

- [p:] a well-formed program.
- [frz:] the answers of disposed memos.
- [s1:] the signal values the memo is evaluated over.
- [s2:] a second set of signal values.
- [n:] the memo or signal read.
- [o:] the outcome over `s1`.
*)
Theorem memo_value_extensional p frz s1 s2 n o :
  well_formed_program p ->
  memo_value p frz s1 n = Some o ->
  (forall s, s ∈ memo_deps p frz s1 n -> s1 !! s = s2 !! s) ->
  memo_value p frz s2 n = Some o.
Proof.
  intros (_ & _ & _ & Hwb & _). apply node_value_ext. exact Hwb.
Qed.

(** * Glitch freedom *)

(** Re-executes the record [r] with the reader [rd]. *)
Definition replay_run (p : program) (rd : reader) (r : run) : option body_state :=
  match run_kind r with
  | RkEffect =>
      body ← program_effects p !! run_node r;
      run_effect p rd (run_node r) body (run_ran_cleanups r) (run_snapshot r)
  | RkDispose => dispose_node p rd (run_node r) (run_ran_cleanups r) (run_snapshot r)
  | RkHostRead => host_read p rd (run_node r) (run_snapshot r)
  end.

(** The run's reads and events are those of a replay with every read answered from [run_snapshot r]
    and [run_frozen r]. A cleanup's environment, and so a value it logs, can date from the run that
    registered it. *)
Definition computed_from_snapshot (p : program) (r : run) : Prop :=
  exists bs,
    replay_run p (frozen_read p (run_frozen r) (run_snapshot r)) r = Some bs /\
    run_reads r = body_reads bs /\
    run_all_reads r = body_all_reads bs /\
    run_events r = body_events bs.

Lemma exec_writes_len p rd n c :
  forall g st o st',
    exec p rd n g c st = Some (o, st') ->
    (length (body_writes st) <= length (body_writes st'))%nat.
Proof.
  induction c; intros g st o' st' Hex; simpl in Hex; split_retired Hex; bind_in Hex;
    repeat case_match; simplify_eq/=; try done;
    try (eapply IHc; exact Hex).
  all: try (destruct p0 as [o1 st1]; simpl in Hex).
  all: try (pose proof (IHc1 _ _ _ _ E) as Hl1; pose proof (IHc2 _ _ _ _ Hex) as Hl2;
            rewrite ?note_fail_writes in Hl2; simpl in Hl1; lia).
  all: try (destruct o1; simplify_eq/=).
  all: try (etrans; [eapply IHc1; exact E|eapply IHc2; exact Hex]).
  all: try (eapply IHc1; exact E).
  all: try (eapply IHc1; exact Hex).
  all: try (eapply IHc2; exact Hex).
  all: try apply IHc in Hex; try apply IHc2 in Hex.
  all: unfold body_track, body_write, body_add_cleanup, body_emit in Hex; simpl in Hex;
    rewrite ?length_app in Hex; simpl in Hex; lia.
Qed.

Lemma exec_frozen p frz n c :
  forall g st o st',
    exec p (live_read p frz) n g c st = Some (o, st') ->
    length (body_writes st') = length (body_writes st) ->
    exec p (frozen_read p frz (body_signals st)) n g c st = Some (o, st') /\ body_signals st' = body_signals st.
Proof.
  induction c; intros g st o' st' Hex Hlen; simpl in Hex |- *; split_retired Hex; bind_in Hex;
    simplify_eq/=; try done.
  all: rw_binds.
  all: unfold frozen_read, live_read in *.
  all: repeat match goal with E : memo_value _ _ _ _ = Some _ |- _ => rewrite E; clear E; simpl end.
  all: try done.
  all: try (apply IHc; done).
  all: try (apply (IHc _ (body_track _ _ st)); done).
  all: try (apply (IHc _ (body_read_untracked _ _ st)); done).
  all: try (apply (IHc _ (body_add_cleanup _ _ st)); done).
  all: try (apply (IHc2 _ (body_add_cleanup _ _ st)); done).
  all: try (apply (IHc _ (body_emit _ st)); done).
  - repeat case_match; [apply IHc2|apply IHc1]; done.
  - exfalso. destruct (decide _); [|discriminate Hex].
    apply exec_writes_len in Hex. unfold body_write in Hex. simpl in Hex.
    rewrite length_app in Hex. simpl in Hex. lia.
  - destruct p0 as [o1 st1]; simpl in *.
    pose proof (exec_writes_len _ _ _ _ _ _ _ _ E) as Hl1. simpl in Hl1.
    pose proof (exec_writes_len _ _ _ _ _ _ _ _ Hex) as Hl2. rewrite note_fail_writes in Hl2.
    destruct (IHc1 _ _ _ _ E ltac:(simpl; lia)) as [Hf Hs]. simpl in Hf, Hs.
    unfold frozen_read in Hf. rewrite Hf. simpl.
    destruct (IHc2 _ _ _ _ Hex ltac:(rewrite note_fail_writes; lia)) as [Hf' Hs'].
    rewrite note_fail_signals, Hs in Hf', Hs'. split; [exact Hf'|exact Hs'].
  - destruct p0 as [o1 st1]; simpl in *.
    pose proof (exec_writes_len _ _ _ _ _ _ _ _ E) as Hl1.
    destruct o1; simplify_eq/=.
    + pose proof (exec_writes_len _ _ _ _ _ _ _ _ Hex) as Hl2.
      destruct (IHc1 _ _ _ _ E ltac:(lia)) as [Hf Hs]. unfold frozen_read in Hf. rewrite Hf. simpl.
      destruct (IHc2 _ _ _ _ Hex ltac:(lia)) as [Hf' Hs']. rewrite Hs in Hf'.
      split; [done|congruence].
    + destruct (IHc1 _ _ _ _ E ltac:(lia)) as [Hf Hs]. unfold frozen_read in Hf. rewrite Hf. simpl.
      done.
Qed.

Lemma run_cleanups_writes_len p rd n cs :
  forall st st',
    run_cleanups p rd n cs st = Some st' ->
    (length (body_writes st) <= length (body_writes st'))%nat.
Proof.
  induction cs as [|[b g] cs IH]; intros st st' Hr; simpl in Hr; simplify_eq/=; [done|].
  bind_in Hr. destruct p0 as [o1 st1]; simpl in *.
  pose proof (exec_writes_len _ _ _ _ _ _ _ _ E). simpl in *. pose proof (IH _ _ Hr).
  rewrite note_fail_writes in *. lia.
Qed.

Lemma run_cleanups_frozen p frz n cs :
  forall st st',
    run_cleanups p (live_read p frz) n cs st = Some st' ->
    length (body_writes st') = length (body_writes st) ->
    run_cleanups p (frozen_read p frz (body_signals st)) n cs st = Some st' /\ body_signals st' = body_signals st.
Proof.
  induction cs as [|[b g] cs IH]; intros st st' Hr Hlen; simpl in *; simplify_eq/=; [done|].
  bind_in Hr. destruct p0 as [o1 st1]; simpl in *.
  pose proof (exec_writes_len _ _ _ _ _ _ _ _ E) as Hl1. simpl in Hl1.
  pose proof (run_cleanups_writes_len _ _ _ _ _ _ Hr) as Hl2. rewrite note_fail_writes in Hl2.
  destruct (exec_frozen _ _ _ _ _ _ _ _ E ltac:(simpl; lia)) as [He Hs]. simpl in He, Hs.
  rewrite He. simpl.
  destruct (IH _ _ Hr ltac:(rewrite note_fail_writes; lia)) as [Hr' Hs'].
  rewrite note_fail_signals, Hs in Hr', Hs'. split; [exact Hr'|exact Hs'].
Qed.

Lemma run_effect_frozen p frz n body cs sigs bs :
  run_effect p (live_read p frz) n body cs sigs = Some bs ->
  body_writes bs = [] ->
  run_effect p (frozen_read p frz sigs) n body cs sigs = Some bs.
Proof.
  unfold run_effect. intros Hr Hw. bind_in Hr. destruct p0 as [o1 st1]; simplify_eq/=.
  rewrite note_fail_writes in Hw.
  pose proof (run_cleanups_writes_len _ _ _ _ _ _ E) as Hl1.
  pose proof (exec_writes_len _ _ _ _ _ _ _ _ E0) as Hl2. simpl in Hl1, Hl2.
  rewrite Hw in Hl2. simpl in Hl2.
  destruct (run_cleanups_frozen _ _ _ _ _ _ E ltac:(simpl; lia)) as [Hc Hs]. simpl in Hc, Hs.
  rewrite Hc. simpl.
  destruct (exec_frozen _ _ _ _ _ _ _ _ E0 ltac:(simpl; rewrite Hw; simpl; lia)) as [He _].
  simpl in He. rewrite Hs in He. rewrite He. done.
Qed.

Lemma dispose_node_frozen p frz n cs sigs bs :
  dispose_node p (live_read p frz) n cs sigs = Some bs ->
  body_writes bs = [] ->
  dispose_node p (frozen_read p frz sigs) n cs sigs = Some bs.
Proof.
  unfold dispose_node. intros Hr Hw. bind_in Hr. simplify_eq/=.
  destruct (run_cleanups_frozen _ _ _ _ _ _ E ltac:(simpl; rewrite Hw; done)) as [Hc _].
  simpl in Hc. rewrite Hc. done.
Qed.

Lemma host_read_frozen p frz n sigs :
  host_read p (frozen_read p frz sigs) n sigs = host_read p (live_read p frz) n sigs.
Proof. reflexivity. Qed.

(** The record [r] comes from a run, disposal or host read of its node. *)
Definition run_of (p : program) (r : run) : Prop :=
  exists bs,
    replay_run p (live_read p (run_frozen r)) r = Some bs /\
    run_reads r = body_reads bs /\
    run_all_reads r = body_all_reads bs /\
    run_events r = body_events bs /\
    run_writes r = body_writes bs.

Lemma run_round_runs p ns :
  forall st st' rs,
    run_round p ns st = Some (st', rs) ->
    Forall (run_of p) rs.
Proof.
  induction ns as [|n ns IH]; intros st st' rs Hr; simpl in Hr; [by simplify_eq|].
  bind_in Hr. destruct p0 as [st1 rs1]; simplify_eq/=.
  constructor; [|by eapply IH].
  exists b. unfold replay_run. simpl. rewrite E. simpl. rewrite E0. done.
Qed.

Lemma settle_runs p fuel order :
  forall st st' rs,
    settle fuel order p st = Some (st', rs) ->
    Forall (run_of p) rs.
Proof.
  induction fuel as [|fuel IH]; intros st st' rs Hs; simpl in Hs; bind_in Hs;
    destruct l as [|m ns]; simplify_eq/=; try done.
  bind_in Hs. destruct p0 as [st1 r1]; simpl in Hs. bind_in Hs.
  destruct p0 as [st2 r2]; simplify_eq/=.
  apply Forall_app. split; [by eapply run_round_runs|by eapply IH].
Qed.

Lemma apply_input_runs p i st st' rs :
  apply_input p i st = Some (st', rs) ->
  Forall (run_of p) rs.
Proof.
  destruct i as [t v|n|n]; simpl.
  - destruct (decide _); intros; simplify_eq/=; done.
  - unfold apply_dispose. destruct (decide _); [intros; simplify_eq/=; done|].
    destruct (program_effects p !! n) as [c|]; [|destruct (program_memos p !! n) as [c|]]; intros Hr;
      [| |discriminate Hr]; bind_in Hr; simplify_eq/=;
      apply Forall_singleton; eexists; split_and!; [unfold replay_run; simpl; eassumption|done..].
  - intros Hr. bind_in Hr. simplify_eq/=.
    apply Forall_singleton; eexists; split_and!; [unfold replay_run; simpl; eassumption|done..].
Qed.

Lemma apply_batch_runs p b :
  forall st st' rs,
    apply_batch p b st = Some (st', rs) ->
    Forall (run_of p) rs.
Proof.
  induction b as [|i b IH]; intros st st' rs Hr; simpl in Hr; [by simplify_eq|].
  bind_in Hr. destruct p0 as [st1 r1]; simpl in Hr. bind_in Hr.
  destruct p0 as [st2 r2]; simplify_eq/=.
  apply Forall_app. split; [by eapply apply_input_runs|by eapply IH].
Qed.

Lemma run_history_runs p fuel order h :
  forall st st' rs,
    run_history fuel order p h st = Some (st', rs) ->
    Forall (run_of p) rs.
Proof.
  induction h as [|b h IH]; intros st st' rs Hr; simpl in Hr; [by simplify_eq|].
  bind_in Hr. destruct p0 as [st0 r0]; simpl in Hr. bind_in Hr.
  destruct p0 as [st1 r1]; simpl in Hr. bind_in Hr.
  destruct p0 as [st2 r2]; simplify_eq/=.
  apply Forall_app. split; [by eapply apply_batch_runs|].
  apply Forall_app. split; [by eapply settle_runs|by eapply IH].
Qed.

Lemma reference_runs_runs fuel order p h rs :
  reference_runs fuel order p h = Some rs ->
  Forall (run_of p) rs.
Proof.
  intros Hr. unfold reference_runs in Hr. bind_in Hr.
  destruct p0 as [st1 r1]; simpl in Hr. bind_in Hr.
  destruct p0 as [st2 r2]; simpl in Hr. bind_in Hr.
  destruct p0 as [st3 r3]; simplify_eq/=.
  apply Forall_app. split; [by eapply run_round_runs|].
  apply Forall_app. split; [by eapply settle_runs|by eapply run_history_runs].
Qed.

Lemma run_of_snapshot p r :
  run_of p r -> run_writes r = [] -> computed_from_snapshot p r.
Proof.
  intros (bs & Hr & Hreads & Hall & Hev & Hw) Hnone.
  exists bs. split_and!; try done.
  unfold replay_run in *. destruct (run_kind r).
  - destruct (program_effects p !! run_node r) as [body|]; simpl in *; [|discriminate].
    apply run_effect_frozen; [done|congruence].
  - apply dispose_node_frozen; [done|congruence].
  - by rewrite host_read_frozen.
Qed.

Lemma note_fail_log_tag n o st :
  (forall m v, EvLog m v ∈ body_events st -> m = n) ->
  forall m v, EvLog m v ∈ body_events (note_fail n o st) -> m = n.
Proof.
  intros Htag m v. destruct o; simpl; [apply Htag|].
  rewrite elem_of_app, list_elem_of_singleton.
  intros [?|Heq]; [by eapply Htag|discriminate].
Qed.

Lemma emit_other_log_tag n e st :
  (forall m v, e <> EvLog m v) ->
  (forall m v, EvLog m v ∈ body_events st -> m = n) ->
  forall m v, EvLog m v ∈ body_events (body_emit e st) -> m = n.
Proof.
  intros Hne Htag m v. simpl. rewrite elem_of_app, list_elem_of_singleton.
  intros [?|Heq]; [by eapply Htag|by exfalso; eapply Hne].
Qed.

Lemma exec_log_tag p rd n c :
  forall g st o st',
    exec p rd n g c st = Some (o, st') ->
    (forall m v, EvLog m v ∈ body_events st -> m = n) ->
    forall m v, EvLog m v ∈ body_events st' -> m = n.
Proof.
  induction c; intros g st o' st' Hex Htag; simpl in Hex; split_retired Hex; bind_in Hex;
    repeat case_match; simplify_eq/=; try done;
    try (eapply IHc; [exact Hex|]; done).
  all: try (destruct p0 as [o1 st1]; simplify_eq/=).
  all: try (eapply IHc2; [exact Hex|]; apply note_fail_log_tag; eapply IHc1; [exact E|];
            apply emit_other_log_tag; [discriminate|done]).
  all: try (eapply IHc2; [exact Hex|]; eapply IHc1; [exact E|done]).
  all: try (eapply IHc1; [exact E|done]).
  all: try (eapply IHc1; [exact Hex|done]).
  all: try (eapply IHc2; [exact Hex|done]).
  eapply IHc; [exact Hex|]. intros m v'. simpl. rewrite elem_of_app, list_elem_of_singleton.
  intros [?|Heq]; [by eapply Htag|by simplify_eq].
Qed.

Lemma run_cleanups_log_tag p rd n cs :
  forall st st',
    run_cleanups p rd n cs st = Some st' ->
    (forall m v, EvLog m v ∈ body_events st -> m = n) ->
    forall m v, EvLog m v ∈ body_events st' -> m = n.
Proof.
  induction cs as [|[b g] cs IH]; intros st st' Hr Htag; simpl in Hr; simplify_eq/=; [done|].
  bind_in Hr. destruct p0 as [o1 st1]; simpl in *.
  eapply IH; [exact Hr|]. apply note_fail_log_tag. eapply exec_log_tag; [exact E|].
  apply emit_other_log_tag; [discriminate|done].
Qed.

Lemma run_of_log_tag p r m v :
  run_of p r -> EvLog m v ∈ run_events r -> m = run_node r.
Proof.
  intros (bs & Hr & _ & _ & -> & _). unfold replay_run in Hr. destruct (run_kind r).
  - bind_in Hr. unfold run_effect in Hr. bind_in Hr.
    destruct p0 as [o1 st1]; simplify_eq/=.
    apply note_fail_log_tag. eapply exec_log_tag; [exact E1|].
    apply emit_other_log_tag; [discriminate|]. simpl.
    eapply run_cleanups_log_tag; [exact E0|].
    intros ? ? Hin'. simpl in Hin'. by apply not_elem_of_nil in Hin'.
  - unfold dispose_node in Hr. bind_in Hr. simplify_eq/=.
    apply emit_other_log_tag; [discriminate|].
    eapply run_cleanups_log_tag; [exact E|].
    intros ? ? Hin'. simpl in Hin'. by apply not_elem_of_nil in Hin'.
  - unfold host_read in Hr. bind_in Hr. simplify_eq/=.
    intros Hin. apply list_elem_of_singleton in Hin. discriminate Hin.
Qed.

(**md
**The run holding a log, when it writes no signal, computes from one snapshot.**

A trace is the concatenation of its records' events, and a given `EvLog` lies in some record for its node. When that record writes no signal, its reads and events are reproduced by replaying it with every read answered from one snapshot of the signals and the disposed memos' values. The guarantee is silent on a record that writes, and on every other record. A cleanup's environment, and so a value it logs, can date from the run that registered it. This describes the reference, not the engine.

- [fuel:] the fuel of each settling.
- [order:] the effect order.
- [p:] the program.
- [h:] the history.
- [evs:] the reference trace.
- [n:] the node that logs.
- [v:] the value logged.
*)
Theorem glitch_free_by_definition fuel order p h evs n v :
  reference_trace fuel order p h = Some evs ->
  EvLog n v ∈ evs ->
  exists rs r,
    reference_runs fuel order p h = Some rs /\
    evs = concat (run_events <$> rs) /\
    r ∈ rs /\
    run_node r = n /\
    EvLog n v ∈ run_events r /\
    (run_writes r = [] -> computed_from_snapshot p r).
Proof.
  intros Htr Hin. unfold reference_trace in Htr.
  destruct (reference_runs fuel order p h) as [rs|] eqn:Er; simpl in Htr; [|discriminate]. simplify_eq.
  apply list_elem_of_In, in_concat in Hin as (evs & Hevs & Hin).
  apply list_elem_of_In in Hevs, Hin.
  apply list_elem_of_fmap in Hevs as (r & -> & Hr).
  pose proof (proj1 (Forall_forall _ _) (reference_runs_runs _ _ _ _ _ Er) r Hr) as Hrun.
  exists rs, r. split_and!; try done.
  - symmetry. by eapply run_of_log_tag.
  - by apply run_of_snapshot.
Qed.

(** * Observations and refinement *)

(**md
**Equal full traces have equal settled views.**

`observe_full` distinguishes at least as much as `observe_settled`.

- [t1:] the first trace.
- [t2:] the second trace.
*)
Theorem observe_settled_coarser t1 t2 :
  observe observe_full t1 = observe observe_full t2 ->
  observe observe_settled t1 = observe observe_settled t2.
Proof. simpl. intros ->. reflexivity. Qed.

(**md
**An engine refining the full trace refines the settled view.**

- [engine:] the engine, mapping a program and history to its trace.
*)
Theorem refines_full_implies_settled engine :
  refines observe_full engine -> refines observe_settled engine.
Proof.
  intros Hr p h t Hwf He.
  destruct (Hr p h t Hwf He) as (ord & Hord & Hall).
  exists ord. split; [done|]. intros h' Hp.
  destruct (Hall h' Hp) as (te & fuel & tr & Hte & Hpre & Htr & Heq).
  exists te, fuel, tr. split_and!; [done|done|done|by apply observe_settled_coarser].
Qed.

Lemma run_history_app fuel o p h k :
  forall st st2 rs,
    run_history fuel o p (h ++ k) st = Some (st2, rs) ->
    exists st1 r1 r2,
      run_history fuel o p h st = Some (st1, r1) /\
      run_history fuel o p k st1 = Some (st2, r2) /\
      rs = r1 ++ r2.
Proof.
  induction h as [|b h IH]; intros st st2 rs Hr; simpl in *.
  - by exists st, [], rs.
  - destruct (apply_batch p b st) as [[st0 r0]|]; simpl in *; [|discriminate].
    destruct (settle fuel o p st0) as [[st1 r1]|]; simpl in *; [|discriminate].
    destruct (run_history fuel o p (h ++ k) st1) as [[st3 r3]|] eqn:E; simpl in *; [|discriminate].
    injection Hr as <- <-.
    destruct (IH _ _ _ E) as (st4 & r4 & r5 & E4 & E5 & ->).
    exists st4, (r0 ++ r1 ++ r4), r5. rewrite E4. simpl.
    split_and!; [done|done|by rewrite <- !app_assoc].
Qed.

(**md
**A history's trace extends the trace of each batch prefix.**

Under one order and fuel, if the history has a trace, each batch prefix has a trace that is a prefix of it.

- [fuel:] the fuel of each settling.
- [o:] the effect order.
- [p:] the program.
- [h:] the history.
- [h':] a prefix of `h`.
- [t:] the reference trace of `h`.
*)
Theorem reference_trace_extends fuel o p h h' t :
  prefix h' h ->
  reference_trace fuel o p h = Some t ->
  exists t', reference_trace fuel o p h' = Some t' /\ prefix t' t.
Proof.
  intros [k ->]. unfold reference_trace, reference_runs.
  destruct (run_round _ _ _) as [[st1 r1]|]; simpl; [|discriminate].
  destruct (settle fuel o p (next_round st1)) as [[st2 r2]|]; simpl; [|discriminate].
  destruct (run_history fuel o p (h' ++ k) st2) as [[st3 r3]|] eqn:E; simpl; [|discriminate].
  destruct (run_history_app _ _ _ _ _ _ _ _ E) as (st4 & r4 & r5 & E4 & _ & ->).
  rewrite E4. simpl. intros [= <-].
  eexists. split; [reflexivity|]. exists (concat (run_events <$> r5)).
  rewrite <- concat_app, <- fmap_app. by rewrite <- !app_assoc.
Qed.

(**md
**The reference refines itself on the full trace.**

Holds under each valid order and any fuel, at every batch prefix of a well-formed program. `refines` only constrains histories the reference answers at that fuel.

- [fuel:] the fuel of each settling.
- [ord:] the effect order, which is valid.
*)
Theorem reference_refines_itself fuel ord :
  is_effect_order ord -> refines observe_full (reference_trace fuel ord).
Proof.
  intros Hord p h t _ Ht. exists ord. split; [done|]. intros h' Hp.
  destruct (reference_trace_extends _ _ _ _ _ _ Hp Ht) as (t' & Ht' & Hpre).
  by exists t', fuel, t'.
Qed.

(**md
**The reference answers whenever it settles.**

Under a valid order, some fuel gives a trace for a well-formed program and history, provided the reference settles under every valid order.

- [ord:] the effect order, which is valid.
*)
Theorem reference_trace_answers ord : is_effect_order ord -> engine_answers (fun k => reference_trace k ord).
Proof.
  intros Hord p h _ Hs. destruct (Hs ord Hord) as (fuel & t & Ht). by exists fuel, t.
Qed.

(**md
**An engine with no traces passes `refines` for every observation.**

*Check, not a guarantee.*

`refines` constrains only the answers an engine gives, so this is a caution about the definition. An engine proof also owes `engine_answers`, which rejects this engine: see [`none_engine_not_answers`](#none_engine_not_answers).

- [O:] the observation.
*)
Theorem none_engine_refines O : refines O (fun _ _ => None).
Proof. intros p h t _ Hn. discriminate Hn. Qed.

(** * Examples *)

(**md
One order for every round.

- [o:] the reordering applied to each round's effects.
*)
Definition const_order (o : list node_id -> list node_id) : effect_order := fun _ => o.

Lemma const_order_valid o : (forall l, o l ≡ₚ l) -> is_effect_order (const_order o).
Proof. intros Ho i l. apply Ho. Qed.

(**md
The effect order that keeps each round's order.
*)
Definition order_identity : effect_order := const_order (fun l => l).
(**md
The effect order that reverses each round.
*)
Definition order_reversed : effect_order := const_order (fun l => reverse l).

Lemma order_identity_valid : is_effect_order order_identity.
Proof. apply const_order_valid. done. Qed.
Lemma order_reversed_valid : is_effect_order order_reversed.
Proof. apply const_order_valid. intros l. apply reverse_Permutation. Qed.

Local Notation ret := (CRet (EConst 0)).
Local Notation s0 := (EConst 0).
Local Notation s1 := (EConst 1).

#[local] Arguments map_Forall : simpl never.
#[local] Arguments EvRun _%_nat.
#[local] Arguments EvLog _%_nat _.
#[local] Arguments EvCleanup _%_nat.
#[local] Arguments EvFail _%_nat _.
#[local] Arguments EvDispose _%_nat.
#[local] Arguments EvHostRead _%_nat _.
#[local] Arguments memo_value _ _ _ _%_nat.
#[local] Arguments memo_deps _ _ _ _%_nat.
#[local] Arguments redundant _ _ _%_nat _%_nat.

#[local] Ltac solve_rank :=
  let rd := fresh "rd" in
  let Hex := fresh "Hex" in
  let Hin := fresh "Hin" in
  intros rd ? ? ? ? Hex Hin _;
  vm_compute in Hex;
  repeat match type of Hex with
  | context [rd ?k] => destruct (rd k) as [[?|?]|]; vm_compute in Hex
  end;
  simplify_eq;
  apply list_elem_of_In in Hin; simpl in Hin; intuition (simplify_eq; simpl; lia).

#[local] Ltac solve_forall tac :=
  repeat (apply map_Forall_insert_2; [cbv beta; tac|]);
  apply map_Forall_empty.

#[local] Ltac solve_rwf :=
  refine (conj _ (conj _ (conj _ (conj _ (conj _ _)))));
  [ simpl; rewrite ?dom_insert_L, ?dom_singleton_L; set_solver
  | simpl; rewrite ?dom_insert_L, ?dom_singleton_L; set_solver
  | simpl; rewrite ?dom_insert_L, ?dom_singleton_L; set_solver
  | simpl; solve_forall ltac:(reflexivity)
  | simpl; solve_forall ltac:(reflexivity)
  | exists (fun n => n); simpl; solve_forall ltac:(solve_rank) ].

(** A diamond: one signal, two memos over it, a memo over both, an effect over that. *)
Definition diamond : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CRet (EAdd (EVar 0) (EConst 1)));
               2%nat := CRead 0 s0 (CRet (EMul (EVar 0) (EConst 2)));
               3%nat := CRead 0 s1 (CRead 1 (EConst 2) (CRet (EAdd (EVar 0) (EVar 1)))) ]};
  program_effects := {[ 9%nat := CRead 0 (EConst 3) (CLog (EVar 0) ret) ]};
|}.

Lemma diamond_well_formed : well_formed_program diamond.
Proof. solve_rwf. Qed.

Example diamond_one_run_per_batch :
  reference_trace 5 order_identity diamond [[RWrite 0 1]; [RWrite 0 2]] =
    Some [EvRun 9; EvLog 9 1; EvRun 9; EvLog 9 4; EvRun 9; EvLog 9 7].
Proof. vm_compute. reflexivity. Qed.

Example diamond_value : memo_value diamond ∅ {[ 0%nat := 1 ]} 3 = Some (Ok 4).
Proof. vm_compute. reflexivity. Qed.

(** A diamond with two effects over its last memo. Each effect runs once per batch, in either
    order. *)
Definition diamond_two_effects : program := {|
  program_signals := program_signals diamond;
  program_memos := program_memos diamond;
  program_effects := {[ 8%nat := CRead 0 (EConst 3) (CLog (EVar 0) ret);
                 9%nat := CRead 0 (EConst 3) (CLog (EVar 0) ret) ]};
|}.

Lemma diamond_two_effects_well_formed : well_formed_program diamond_two_effects.
Proof. solve_rwf. Qed.

Example diamond_one_run_per_batch_in_order :
  reference_trace 5 order_identity diamond_two_effects [[RWrite 0 1]] =
    Some [EvRun 8; EvLog 8 1; EvRun 9; EvLog 9 1; EvRun 8; EvLog 8 4; EvRun 9; EvLog 9 4].
Proof. vm_compute. reflexivity. Qed.

Example diamond_one_run_per_batch_reversed :
  reference_trace 5 order_reversed diamond_two_effects [[RWrite 0 1]] =
    Some [EvRun 9; EvLog 9 1; EvRun 8; EvLog 8 1; EvRun 9; EvLog 9 4; EvRun 8; EvLog 8 4].
Proof. vm_compute. reflexivity. Qed.

(** A memo's logs are discarded, and an effect's are events. *)
Definition logging_memo : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CLog (EConst 100) (CRet (EVar 0))) ]};
  program_effects := {[ 9%nat := CRead 0 s1 (CLog (EVar 0) ret) ]};
|}.

Lemma logging_memo_well_formed : well_formed_program logging_memo.
Proof. solve_rwf. Qed.

Example memo_logs_discarded :
  reference_trace 5 order_identity logging_memo [] = Some [EvRun 9; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

(** A memo of nesting depth [size (program_memos p)] has a value. *)
Definition chain : program := {|
  program_signals := {[ 0%nat := 5 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CRet (EVar 0));
               2%nat := CRead 0 s1 (CRet (EVar 0));
               3%nat := CRead 0 (EConst 2) (CRet (EVar 0)) ]};
  program_effects := ∅;
|}.

Example chain_value : memo_value chain ∅ (program_signals chain) 3 = Some (Ok 5).
Proof. vm_compute. reflexivity. Qed.

(** A cutoff: a memo over a moving signal, with an unmoved value. *)
Definition cutoff : program := {|
  program_signals := {[ 0%nat := 1 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CRet (ELt (EVar 0) (EConst 10))) ]};
  program_effects := {[ 9%nat := CRead 0 s1 (CLog (EVar 0) ret) ]};
|}.

Lemma cutoff_well_formed : well_formed_program cutoff.
Proof. solve_rwf. Qed.

Example cutoff_no_wake :
  reference_trace 5 order_identity cutoff [[RWrite 0 2]] = Some [EvRun 9; EvLog 9 1].
Proof. vm_compute. reflexivity. Qed.

Example cutoff_moved_memo_wakes :
  reference_trace 5 order_identity cutoff [[RWrite 0 20]] = Some [EvRun 9; EvLog 9 1; EvRun 9; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

(** A batch that restores a signal's value. *)
Definition one_read : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat := CRead 0 s0 (CLog (EVar 0) ret) ]};
|}.

Example batch_restored_no_run :
  reference_trace 5 order_identity one_read [[RWrite 0 1; RWrite 0 0]] = Some [EvRun 9; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

Example batch_moved_runs :
  reference_trace 5 order_identity one_read [[RWrite 0 0; RWrite 0 1]] =
    Some [EvRun 9; EvLog 9 0; EvRun 9; EvLog 9 1].
Proof. vm_compute. reflexivity. Qed.

Example write_to_absent_signal_is_stuck :
  reference_trace 5 order_identity one_read [[RWrite 3 1]] = None.
Proof. vm_compute. reflexivity. Qed.

(** Cleanups run before the body, newest first, with the environment of their registration. *)
Definition cleanups : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CRead 0 s0 (CCleanup (CLog (EVar 0) ret) (CCleanup (CLog (EConst 100) ret) ret)) ]};
|}.

Example cleanups_first :
  reference_trace 5 order_identity cleanups [[RWrite 0 1]] =
    Some [EvRun 9; EvCleanup 9; EvLog 9 100; EvCleanup 9; EvLog 9 0; EvRun 9].
Proof. vm_compute. reflexivity. Qed.

(** A failed body keeps its reads and leaves later effects running. *)
Definition failing : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CThrow 7);
                 9%nat := CRead 0 s0 (CLog (EVar 0) ret) ]};
|}.

Example failed_effect_later_effects_run :
  reference_trace 5 order_identity failing [[RWrite 0 1]] =
    Some [EvRun 8; EvFail 8 7; EvRun 9; EvLog 9 0; EvRun 8; EvFail 8 7; EvRun 9; EvLog 9 1].
Proof. vm_compute. reflexivity. Qed.

(** A zero divisor fails the effect and leaves later effects running. *)
Definition dividing : program := {|
  program_signals := {[ 0%nat := 1 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CLog (EDiv (EConst 10) (EVar 0)) ret);
                 9%nat := CRead 0 s0 (CLog (EVar 0) ret) ]};
|}.

Example zero_divisor_later_effects_run :
  reference_trace 5 order_identity dividing [[RWrite 0 0]] =
    Some [EvRun 8; EvLog 8 10; EvRun 9; EvLog 9 1; EvRun 8; EvFail 8 div_zero_failure; EvRun 9;
          EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

(** A zero divisor in a memo is the memo's failure. *)
Definition dividing_memo : program := {|
  program_signals := {[ 0%nat := 1 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CRet (EDiv (EConst 10) (EVar 0))) ]};
  program_effects := {[ 9%nat := CTryRead 0 1 s1 (CLog (EVar 0) (CLog (EVar 1) ret)) ]};
|}.

Example zero_divisor_memo_failure :
  reference_trace 5 order_identity dividing_memo [[RWrite 0 0]] =
    Some [EvRun 9; EvLog 9 10; EvLog 9 1; EvRun 9; EvLog 9 div_zero_failure; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

(** A failure is a value: an unmoved failure wakes nothing, and [CTryRead] observes it. *)
Definition failing_memo : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := {[ 1%nat :=
    CRead 0 s0 (CIf (ELt (EVar 0) (EConst 10)) (CThrow 7) (CThrow 8)) ]};
  program_effects := {[ 9%nat := CTryRead 0 1 s1 (CLog (EVar 0) (CLog (EVar 1) ret)) ]};
|}.

Example failure_unmoved_no_wake :
  reference_trace 5 order_identity failing_memo [[RWrite 0 1]] = Some [EvRun 9; EvLog 9 7; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

Example failure_moved_wakes :
  reference_trace 5 order_identity failing_memo [[RWrite 0 10]] =
    Some [EvRun 9; EvLog 9 7; EvLog 9 0; EvRun 9; EvLog 9 8; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

Definition untracked : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat := CReadUntracked 0 s0 (CLog (EVar 0) ret) ]};
|}.

Example untracked_read_no_wake :
  reference_trace 5 order_identity untracked [[RWrite 0 1]] = Some [EvRun 9; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

(** Effect 8 copies signal 0 to signal 1. Effect 9 reads both. *)
Definition writer : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CWrite s1 (EVar 0) ret);
                 9%nat := CRead 0 s0 (CRead 1 s1 (CLog (EAdd (EVar 0) (EVar 1)) ret)) ]};
|}.

Lemma writer_well_formed : well_formed_program writer.
Proof.
  refine (conj _ (conj _ (conj _ (conj _ (conj _ _)))));
    [ simpl; rewrite ?dom_empty_L; set_solver
    | simpl; rewrite ?dom_insert_L, ?dom_singleton_L; set_solver
    | simpl; rewrite ?dom_empty_L; set_solver
    | simpl; apply map_Forall_empty
    | simpl; solve_forall ltac:(reflexivity)
    | exists (fun n => n); simpl; apply map_Forall_empty ].
Qed.

(** A later effect of a round reads the writes of an earlier one, and the next round's run set is
    empty. *)
Example write_read_later_in_round :
  reference_trace 5 order_identity writer [[RWrite 0 1]] =
    Some [EvRun 8; EvRun 9; EvLog 9 0; EvRun 8; EvRun 9; EvLog 9 2].
Proof. vm_compute. reflexivity. Qed.

(** An effect that ran before the write runs again in the next round. *)
Example write_wakes_next_round :
  reference_trace 5 order_reversed writer [[RWrite 0 1]] =
    Some [EvRun 9; EvLog 9 0; EvRun 8; EvRun 9; EvLog 9 1; EvRun 8; EvRun 9; EvLog 9 2].
Proof. vm_compute. reflexivity. Qed.

Definition climbing : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CRead 0 s0 (CIf (ELt (EVar 0) (EConst 2)) (CWrite s0 (EAdd (EVar 0) (EConst 1)) ret) ret) ]};
|}.

Example self_write_settles :
  reference_trace 5 order_identity climbing [] = Some [EvRun 9; EvRun 9; EvRun 9].
Proof. vm_compute. reflexivity. Qed.

Definition diverging : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat := CRead 0 s0 (CWrite s0 (EAdd (EVar 0) (EConst 1)) ret) ]};
|}.

Example self_write_exhausts_fuel : reference_trace 50 order_identity diverging [] = None.
Proof. vm_compute. reflexivity. Qed.

(** A run that writes between two reads logs values from two snapshots. *)
Definition two_snapshots : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CRead 0 s0 (CLog (EVar 0) (CWrite s0 (EConst 5) (CRead 1 s0 (CLog (EVar 1) ret)))) ]};
|}.

Example write_between_reads_live :
  reference_trace 5 order_identity two_snapshots [] =
    Some [EvRun 9; EvLog 9 0; EvLog 9 5; EvRun 9; EvLog 9 5; EvLog 9 5].
Proof. vm_compute. reflexivity. Qed.

Lemma write_between_reads_not_from_snapshot :
  exists rs r, reference_runs 5 order_identity two_snapshots [] = Some rs /\ r ∈ rs /\
    ~ computed_from_snapshot two_snapshots r.
Proof.
  eexists _, _. split; [vm_compute; reflexivity|]. split; [left|].
  intros (bs & Hr & _ & _ & Hev).
  vm_compute in Hr. injection Hr as <-.
  vm_compute in Hev. discriminate Hev.
Qed.

(** The dependencies of a memo are those of its branch taken. *)
Definition branching : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 10; 2%nat := 20 ]};
  program_memos := {[ 3%nat :=
    CRead 0 s0 (CIf (EVar 0) (CRead 1 s1 (CRet (EVar 1))) (CRead 1 (EConst 2) (CRet (EVar 1)))) ]};
  program_effects := ∅;
|}.

Example branching_deps : memo_deps branching ∅ (program_signals branching) 3 = [0%nat; 2%nat].
Proof. vm_compute. reflexivity. Qed.

Example branching_ignores_other_branch :
  memo_value branching ∅ (<[1%nat := 99]> (program_signals branching)) 3 = Some (Ok 20).
Proof. vm_compute. reflexivity. Qed.

(** A dependency cycle is outside the fragment, and has no value. *)
Definition cyclic : program := {|
  program_signals := ∅;
  program_memos := {[ 1%nat := CRead 0 (EConst 2) ret; 2%nat := CRead 0 s1 ret ]};
  program_effects := ∅;
|}.

Lemma cyclic_not_well_formed : ~ well_formed_program cyclic.
Proof.
  intros (_ & _ & _ & _ & _ & rank & Hr).
  assert (program_memos cyclic !! 1%nat = Some (CRead 0 (EConst 2) ret)) as H1 by (vm_compute; reflexivity).
  assert (program_memos cyclic !! 2%nat = Some (CRead 0 s1 ret)) as H2 by (vm_compute; reflexivity).
  assert (rank 2%nat < rank 1%nat)%nat as Hlt1.
  { eapply (Hr _ _ H1 (fun _ => Some (Ok 0)));
      [vm_compute; reflexivity | simpl; left | vm_compute; eauto]. }
  assert (rank 1%nat < rank 2%nat)%nat as Hlt2.
  { eapply (Hr _ _ H2 (fun _ => Some (Ok 0)));
      [vm_compute; reflexivity | simpl; left | vm_compute; eauto]. }
  lia.
Qed.

Example cyclic_no_value : memo_value cyclic ∅ ∅ 1 = None.
Proof. vm_compute. reflexivity. Qed.

Example well_behaved_effect_accepts :
  well_behaved_effect
    (CLet 0 (EConst 1) (CIf (EVar 0)
      (CRead 1 s0 (CTryRead 2 3 s0 (CReadUntracked 4 s0 (CWrite s0 (EVar 1)
        (CCleanup ret (CBatch ret (CLog (EVar 1) ret)))))))
      (CThrow 1))) = true.
Proof. reflexivity. Qed.
Example well_behaved_effect_rejects_signal : well_behaved_effect (CSignal 0 (EConst 1) ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_effect_rejects_memo : well_behaved_effect (CMemo 0 Owning CmpEq ret ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_effect_rejects_effect : well_behaved_effect (CEffect 0 ret ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_effect_rejects_dispose : well_behaved_effect (CDispose s0 ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_effect_rejects_in_cleanup : well_behaved_effect (CCleanup (CDispose s0 ret) ret) = false.
Proof. reflexivity. Qed.
Example well_behaved_effect_rejects_prev : well_behaved_effect (CRet EPrev) = false.
Proof. reflexivity. Qed.

(** ** Redundant runs *)

Definition diamond_runs := default [] (reference_runs 5 order_identity diamond [[RWrite 0 1]]).

#[local] Ltac solve_not_redundant :=
  let Hp := fresh "Hp" in
  let Hc := fresh "Hc" in
  let Heq := fresh "Heq" in
  let Hk := fresh "Hk" in
  intros (_ & ? & ? & ? & Hk & Hp & Hc & Heq); injection Hk as <-;
  vm_compute in Hp, Hc; injection Hp as <-; injection Hc as <-;
  vm_compute in Heq; discriminate Heq.

Lemma diamond_run_not_redundant : ~ redundant diamond diamond_runs 9 1.
Proof. solve_not_redundant. Qed.

Lemma initial_run_not_redundant : ~ redundant diamond diamond_runs 9 0.
Proof. intros (_ & j & prev & cur & Hk & _). discriminate Hk. Qed.

(** Effect 8 reads signal 0. Effect 9 reads signal 1 and resets signal 0. *)
Definition resetting : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CLog (EVar 0) ret);
                 9%nat := CRead 0 s1 (CWrite s0 (EConst 0) ret) ]};
|}.

Definition resetting_runs := default [] (reference_runs 5 order_reversed resetting [[RWrite 0 1; RWrite 1 1]]).

Example resetting_trace :
  reference_trace 5 order_reversed resetting [[RWrite 0 1; RWrite 1 1]] =
    Some [EvRun 9; EvRun 8; EvLog 8 0; EvRun 9; EvRun 8; EvLog 8 0].
Proof. vm_compute. reflexivity. Qed.

(** The reference tier itself has a redundant run. Effect 8 is in the round's run set from the
    round's snapshot, and an earlier effect of the round restores the value effect 8 read. *)
Lemma reference_run_redundant : redundant resetting resetting_runs 8 1.
Proof.
  split; [vm_compute; eauto|].
  eexists 0%nat, _, _. split; [reflexivity|].
  split; [vm_compute; reflexivity|]. split; [vm_compute; reflexivity|].
  vm_compute. reflexivity.
Qed.

(** Effect 8 reads signal 0 tracked and signal 2 untracked, and logs the untracked value. Effect 9
    reads signal 1 and resets signal 0. *)
Definition untracked_value : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0; 2%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CReadUntracked 1 (EConst 2) (CLog (EVar 1) ret));
                 9%nat := CRead 0 s1 (CWrite s0 (EConst 0) ret) ]};
|}.

Definition untracked_value_history : history := [[RWrite 0 1; RWrite 1 1; RWrite 2 42]].

Definition untracked_value_runs :=
  default [] (reference_runs 5 order_reversed untracked_value untracked_value_history).

Example untracked_value_trace :
  reference_trace 5 order_reversed untracked_value untracked_value_history =
    Some [EvRun 9; EvRun 8; EvLog 8 0; EvRun 9; EvRun 8; EvLog 8 42].
Proof. vm_compute. reflexivity. Qed.

Example untracked_value_not_redundant : ~ redundant untracked_value untracked_value_runs 8 1.
Proof. solve_not_redundant. Qed.

(** An untracked read selects the branch: the first run makes two tracked reads, and the second makes
    none and logs. *)
Definition untracked_branch : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0; 2%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CReadUntracked 0 (EConst 2) (CIf (EVar 0) (CLog (EConst 100) ret) (CRead 1 s0 (CRead 2 s1 ret))) ]};
|}.

Definition untracked_branch_history : history := [[RWrite 1 1; RWrite 2 1]].

Definition untracked_branch_runs :=
  default [] (reference_runs 5 order_identity untracked_branch untracked_branch_history).

Example untracked_branch_trace :
  reference_trace 5 order_identity untracked_branch untracked_branch_history =
    Some [EvRun 9; EvRun 9; EvLog 9 100].
Proof. vm_compute. reflexivity. Qed.

Example untracked_branch_not_redundant : ~ redundant untracked_branch untracked_branch_runs 9 1.
Proof. solve_not_redundant. Qed.

(** A redundant run's events can differ from its previous run's: its cleanups come from the previous
    run's registrations. *)
Definition resetting_cleanup : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CCleanup (CLog (EConst 100) ret) ret);
                 9%nat := CRead 0 s1 (CWrite s0 (EConst 0) ret) ]};
|}.

Definition resetting_cleanup_runs :=
  default [] (reference_runs 5 order_reversed resetting_cleanup [[RWrite 0 1; RWrite 1 1]]).

Example resetting_cleanup_trace :
  reference_trace 5 order_reversed resetting_cleanup [[RWrite 0 1; RWrite 1 1]] =
    Some [EvRun 9; EvRun 8; EvRun 9; EvCleanup 8; EvLog 8 100; EvRun 8].
Proof. vm_compute. reflexivity. Qed.

Example redundant_run_with_cleanup : redundant resetting_cleanup resetting_cleanup_runs 8 1.
Proof.
  split; [vm_compute; eauto|].
  eexists 0%nat, _, _. split; [reflexivity|].
  split; [vm_compute; reflexivity|]. split; [vm_compute; reflexivity|].
  vm_compute. reflexivity.
Qed.

(** A run with a cleanup read has different reads from a previous run without one. *)
Definition cleanup_read : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CCleanup (CReadUntracked 1 s1 (CLog (EVar 1) ret)) ret);
                 9%nat := CRead 0 s1 (CWrite s0 (EConst 0) ret) ]};
|}.

Definition cleanup_read_runs :=
  default [] (reference_runs 5 order_reversed cleanup_read [[RWrite 0 1; RWrite 1 1]]).

Example cleanup_read_not_redundant : ~ redundant cleanup_read cleanup_read_runs 8 1.
Proof. solve_not_redundant. Qed.

(** ** Failures *)

(** The effect fails while signal 0 is 1. *)
Definition fails_at_one : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CRead 0 s0 (CIf (EEq (EVar 0) s1) (CThrow 7) (CLog (EVar 0) ret)) ]};
|}.

Example failing_round_later_rounds_run :
  reference_trace 5 order_identity fails_at_one [[RWrite 0 1]; [RWrite 0 2]] =
    Some [EvRun 9; EvLog 9 0; EvRun 9; EvFail 9 7; EvRun 9; EvLog 9 2].
Proof. vm_compute. reflexivity. Qed.

(** A read of a failed memo fails the reading effect with the memo's failure. *)
Definition reads_failed_memo : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := {[ 1%nat := CThrow 7 ]};
  program_effects := {[ 9%nat := CRead 0 s1 ret ]};
|}.

Example failed_read_emits_failure :
  reference_trace 5 order_identity reads_failed_memo [] = Some [EvRun 9; EvFail 9 7].
Proof. vm_compute. reflexivity. Qed.

(** A failed cleanup emits its failure and ends only itself. *)
Definition failing_cleanup : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CRead 0 s0 (CCleanup (CLog (EConst 1) ret) (CCleanup (CThrow 7) (CLog (EVar 0) ret))) ]};
|}.

Example failed_cleanup_ends_itself :
  reference_trace 5 order_identity failing_cleanup [[RWrite 0 1]] =
    Some [EvRun 9; EvLog 9 0; EvCleanup 9; EvFail 9 7; EvCleanup 9; EvLog 9 1; EvRun 9; EvLog 9 1].
Proof. vm_compute. reflexivity. Qed.

(** ** Disposal *)

(** Disposing an effect runs its cleanups once and ends its runs. A second disposal does nothing. *)
Example dispose_runs_cleanups_once :
  reference_trace 5 order_identity cleanups [[RDispose 9]; [RWrite 0 1]; [RDispose 9]; [RWrite 0 2]] =
    Some [EvRun 9; EvCleanup 9; EvLog 9 100; EvCleanup 9; EvLog 9 0; EvDispose 9].
Proof. vm_compute. reflexivity. Qed.

(** Inputs of a batch apply in order, and a write before the disposal in a batch takes no run. *)
Example write_then_dispose_no_run :
  reference_trace 5 order_identity one_read [[RWrite 0 1; RDispose 9]] =
    Some [EvRun 9; EvLog 9 0; EvDispose 9].
Proof. vm_compute. reflexivity. Qed.

(** A cleanup registered during disposal runs at once. A cleanup registered during a re-run's
    discharge joins the new scope. *)
Definition nested_cleanup : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat :=
    CRead 0 s0 (CCleanup (CCleanup (CLog (EConst 1) ret) (CLog (EConst 2) ret)) ret) ]};
|}.

Example retired_cleanup_runs_at_once :
  reference_trace 5 order_identity nested_cleanup [[RDispose 9]] =
    Some [EvRun 9; EvCleanup 9; EvCleanup 9; EvLog 9 1; EvLog 9 2; EvDispose 9].
Proof. vm_compute. reflexivity. Qed.

Example discharge_cleanup_joins_new_scope :
  reference_trace 5 order_identity nested_cleanup [[RWrite 0 1]; [RDispose 9]] =
    Some [EvRun 9; EvCleanup 9; EvLog 9 2; EvRun 9; EvCleanup 9; EvCleanup 9; EvLog 9 1; EvLog 9 2;
          EvCleanup 9; EvLog 9 1; EvDispose 9].
Proof. vm_compute. reflexivity. Qed.

(** A disposal's cleanup writes apply, and wake the effects that read them. *)
Definition cleanup_writes : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CCleanup (CWrite s0 (EConst 5) ret) ret;
                 9%nat := CRead 0 s0 (CLog (EVar 0) ret) ]};
|}.

Example disposal_cleanup_write_wakes :
  reference_trace 5 order_identity cleanup_writes [[RDispose 8]] =
    Some [EvRun 8; EvRun 9; EvLog 9 0; EvCleanup 8; EvDispose 8; EvRun 9; EvLog 9 5].
Proof. vm_compute. reflexivity. Qed.

(** Effect 9 reads memo 1 and signal 2. Memo 1 is signal 0 plus one. *)
Definition memo_and_signal : program := {|
  program_signals := {[ 0%nat := 0; 2%nat := 0 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CRet (EAdd (EVar 0) s1)) ]};
  program_effects := {[ 9%nat := CRead 0 s1 (CRead 1 (EConst 2) (CLog (EVar 0) ret)) ]};
|}.

Example disposed_memo_answers_last_value :
  reference_trace 5 order_identity memo_and_signal [[RDispose 1]; [RWrite 0 5]; [RWrite 2 1]] =
    Some [EvRun 9; EvLog 9 1; EvDispose 1; EvRun 9; EvLog 9 1].
Proof. vm_compute. reflexivity. Qed.

(** A memo disposed after a write in its batch keeps its value over that write, and a reader whose
    last read differs runs. *)
Example write_then_dispose_memo_current_value :
  reference_trace 5 order_identity memo_and_signal [[RWrite 0 5; RDispose 1]; [RWrite 2 1]] =
    Some [EvRun 9; EvLog 9 1; EvDispose 1; EvRun 9; EvLog 9 6; EvRun 9; EvLog 9 6].
Proof. vm_compute. reflexivity. Qed.

(** A disposed memo keeps its failure. *)
Definition dispose_failed_memo : program := {|
  program_signals := {[ 0%nat := 0; 2%nat := 0 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CIf (EVar 0) ret (CThrow 7)) ]};
  program_effects := {[ 9%nat := CTryRead 0 1 s1 (CRead 2 (EConst 2) (CLog (EVar 1) ret)) ]};
|}.

Example disposed_memo_keeps_failure :
  reference_trace 5 order_identity dispose_failed_memo [[RDispose 1]; [RWrite 0 1]; [RWrite 2 1]] =
    Some [EvRun 9; EvLog 9 0; EvDispose 1; EvRun 9; EvLog 9 0].
Proof. vm_compute. reflexivity. Qed.

Example dispose_signal_is_stuck : reference_trace 5 order_identity one_read [[RDispose 0]] = None.
Proof. vm_compute. reflexivity. Qed.

Example dispose_unknown_is_stuck : reference_trace 5 order_identity one_read [[RDispose 3]] = None.
Proof. vm_compute. reflexivity. Qed.

(** Effect 9 reads memo 1 only once signal 2 is nonzero, so the initial runs leave memo 1 unread. The
    reference answers its value over the signals at its disposal. *)
Definition unread_memo : program := {|
  program_signals := {[ 0%nat := 0; 2%nat := 0 ]};
  program_memos := {[ 1%nat := CRead 0 s0 (CRet (EVar 0)) ]};
  program_effects := {[ 9%nat :=
    CRead 0 (EConst 2) (CIf (EVar 0) (CRead 1 s1 (CLog (EVar 1) ret)) ret) ]};
|}.

Example disposed_unread_memo_value :
  reference_trace 5 order_identity unread_memo [[RWrite 0 5]; [RDispose 1]; [RWrite 2 1]] =
    Some [EvRun 9; EvDispose 1; EvRun 9; EvLog 9 5].
Proof. vm_compute. reflexivity. Qed.

(** ** Round-indexed orders *)

(** Forward on even rounds, reversed on odd ones. *)
Definition by_parity : effect_order :=
  fun i l => if Nat.even i then l else reverse l.

Lemma by_parity_order : is_effect_order by_parity.
Proof.
  intros i l. unfold by_parity. destruct (Nat.even i); [done|apply reverse_Permutation].
Qed.

Example order_varies_by_round :
  reference_trace 5 by_parity diamond_two_effects [[RWrite 0 1]; [RWrite 0 2]] =
    Some [EvRun 8; EvLog 8 1; EvRun 9; EvLog 9 1; EvRun 9; EvLog 9 4; EvRun 8; EvLog 8 4;
          EvRun 8; EvLog 8 7; EvRun 9; EvLog 9 7].
Proof. vm_compute. reflexivity. Qed.

(** ** What dropping a redundant run changes *)

Definition resetting_trace_without_redundant : list event :=
  [EvRun 9; EvRun 8; EvLog 8 0; EvRun 9].

Definition resetting_full_trace : list event :=
  [EvRun 9; EvRun 8; EvLog 8 0; EvRun 9; EvRun 8; EvLog 8 0].

(** On a program with no cleanups, removing this redundant run preserves [observe_settled]. With a failing
    cleanup it need not: [redundant_failing_cleanup_changes_settled]. *)
Example redundant_run_settled_equal :
  observe observe_settled resetting_full_trace = observe observe_settled resetting_trace_without_redundant.
Proof. vm_compute. reflexivity. Qed.

Example redundant_run_full_differs :
  observe observe_full resetting_full_trace <> observe observe_full resetting_trace_without_redundant.
Proof. vm_compute. discriminate. Qed.

(** [resetting_full_trace] is the trace in which [reference_run_redundant] finds the run. *)
Example resetting_full_trace_is_reference :
  reference_trace 5 order_reversed resetting [[RWrite 0 1; RWrite 1 1]] = Some resetting_full_trace.
Proof. vm_compute. reflexivity. Qed.

(** The last run's value is observed. *)
Example settled_sees_last_value :
  observe observe_settled [EvRun 9; EvLog 9 0; EvRun 9; EvLog 9 1] <>
    observe observe_settled [EvRun 9; EvLog 9 0; EvRun 9; EvLog 9 0].
Proof. vm_compute. discriminate. Qed.

(** A failure of an earlier run is observed, and so is the failure of the last. *)
Example settled_sees_earlier_failure :
  observe observe_settled [EvRun 9; EvFail 9 7; EvRun 9] <> observe observe_settled [EvRun 9; EvRun 9].
Proof. vm_compute. discriminate. Qed.

Example settled_sees_last_failure :
  observe observe_settled [EvRun 9; EvRun 9; EvFail 9 7] <> observe observe_settled [EvRun 9; EvFail 9 7; EvRun 9].
Proof. vm_compute. discriminate. Qed.

Example settled_sees_disposal :
  observe observe_settled [EvRun 9; EvDispose 9] <> observe observe_settled [EvRun 9].
Proof. vm_compute. discriminate. Qed.

Example settled_sees_host_read :
  observe observe_settled [EvRun 9; EvHostRead 0 (Ok 1)] <> observe observe_settled [EvRun 9; EvHostRead 0 (Ok 0)].
Proof. vm_compute. discriminate. Qed.

(** A redundant run whose discharged cleanup holds the history's only failure: removing the run
    changes [observe_settled]. *)
Definition resetting_failing_cleanup : program := {|
  program_signals := {[ 0%nat := 0; 1%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 8%nat := CRead 0 s0 (CCleanup (CThrow 7) ret);
                 9%nat := CRead 0 s1 (CWrite s0 (EConst 0) ret) ]};
|}.

Definition resetting_failing_cleanup_runs :=
  default [] (reference_runs 5 order_reversed resetting_failing_cleanup [[RWrite 0 1; RWrite 1 1]]).

(**md
**Dropping a redundant run can change what a reader sees, when its cleanup fails.**

*Check, not a guarantee.*

This is a counterexample on one program, `resetting_failing_cleanup`, under the reversed order: effect 8's second run counts as redundant and discharges a failing cleanup, and the trace without that run has a different `observe_settled`. The failure is the one the removal loses.
*)
Theorem redundant_failing_cleanup_changes_settled :
  reference_trace 5 order_reversed resetting_failing_cleanup [[RWrite 0 1; RWrite 1 1]] =
    Some [EvRun 9; EvRun 8; EvRun 9; EvCleanup 8; EvFail 8 7; EvRun 8] /\
  redundant resetting_failing_cleanup resetting_failing_cleanup_runs 8 1 /\
  observe observe_settled [EvRun 9; EvRun 8; EvRun 9; EvCleanup 8; EvFail 8 7; EvRun 8] <>
    observe observe_settled [EvRun 9; EvRun 8; EvRun 9].
Proof.
  split_and!.
  - vm_compute. reflexivity.
  - split; [vm_compute; eauto|].
    eexists 0%nat, _, _. split; [reflexivity|].
    split; [vm_compute; reflexivity|]. split; [vm_compute; reflexivity|].
    vm_compute. reflexivity.
  - vm_compute. discriminate.
Qed.

(** [observe_settled] admits an engine that drops a non-failing logging cleanup of a re-run. *)
Example logging_cleanup_drop_unobserved :
  reference_trace 5 order_identity cleanups [[RWrite 0 1]] =
    Some [EvRun 9; EvCleanup 9; EvLog 9 100; EvCleanup 9; EvLog 9 0; EvRun 9] /\
  observe observe_settled [EvRun 9; EvCleanup 9; EvLog 9 100; EvCleanup 9; EvLog 9 0; EvRun 9] =
    observe observe_settled [EvRun 9; EvRun 9].
Proof. split; vm_compute; reflexivity. Qed.

(** ** Host reads *)

Example host_read_sees_earlier_write :
  reference_trace 5 order_identity one_read [[RRead 0; RWrite 0 1; RRead 0]] =
    Some [EvRun 9; EvLog 9 0; EvHostRead 0 (Ok 0); EvHostRead 0 (Ok 1); EvRun 9; EvLog 9 1].
Proof. vm_compute. reflexivity. Qed.

Example host_read_memo_over_current_signals :
  reference_trace 5 order_identity memo_and_signal [[RWrite 0 5; RRead 1]] =
    Some [EvRun 9; EvLog 9 1; EvHostRead 1 (Ok 6); EvRun 9; EvLog 9 6].
Proof. vm_compute. reflexivity. Qed.

Example host_read_disposed_memo :
  reference_trace 5 order_identity memo_and_signal [[RDispose 1]; [RWrite 0 5; RRead 1]] =
    Some [EvRun 9; EvLog 9 1; EvDispose 1; EvHostRead 1 (Ok 1)].
Proof. vm_compute. reflexivity. Qed.

Example host_read_failed_memo :
  reference_trace 5 order_identity reads_failed_memo [[RRead 1]] =
    Some [EvRun 9; EvFail 9 7; EvHostRead 1 (Fail 7)].
Proof. vm_compute. reflexivity. Qed.

Example host_read_effect_is_stuck : reference_trace 5 order_identity one_read [[RRead 9]] = None.
Proof. vm_compute. reflexivity. Qed.

(** A memo read and then disposed in one batch keeps the value the read answered. *)
Example host_read_then_dispose_memo :
  reference_trace 5 order_identity memo_and_signal [[RWrite 0 5; RRead 1; RDispose 1]; [RRead 1]] =
    Some [EvRun 9; EvLog 9 1; EvHostRead 1 (Ok 6); EvDispose 1; EvRun 9; EvLog 9 6;
          EvHostRead 1 (Ok 6)].
Proof. vm_compute. reflexivity. Qed.

(** No effect reads signal 0. *)
Definition unlogged_signal : program := {|
  program_signals := {[ 0%nat := 0 ]};
  program_memos := ∅;
  program_effects := {[ 9%nat := ret ]};
|}.

Definition with_write : history := [[RWrite 0 1]; [RRead 0]].
Definition write_dropped : history := [[]; [RRead 0]].

(** Dropping the write leaves [observe_settled] unchanged until the host reads the signal. *)
Example dropped_write_unobserved_before_read :
  observe observe_settled <$> reference_trace 5 order_identity unlogged_signal [[RWrite 0 1]] =
    observe observe_settled <$> reference_trace 5 order_identity unlogged_signal [[]].
Proof. vm_compute. reflexivity. Qed.

Example dropped_write_observed_by_host_read :
  observe observe_settled <$> reference_trace 5 order_identity unlogged_signal with_write <>
    observe observe_settled <$> reference_trace 5 order_identity unlogged_signal write_dropped.
Proof. vm_compute. discriminate. Qed.

(** ** What refinement rejects *)

Lemma one_read_well_formed : well_formed_program one_read.
Proof.
  refine (conj _ (conj _ (conj _ (conj _ (conj _ _)))));
    [ simpl; rewrite ?dom_empty_L; set_solver
    | simpl; rewrite ?dom_singleton_L; set_solver
    | simpl; rewrite ?dom_empty_L; set_solver
    | simpl; apply map_Forall_empty
    | simpl; solve_forall ltac:(reflexivity)
    | exists (fun n => n); simpl; apply map_Forall_empty ].
Qed.

Lemma singleton_order_id (ord : effect_order) i (n : node_id) :
  is_effect_order ord -> ord i [n] = [n].
Proof. intros Hord. apply Permutation_singleton_r, Hord. Qed.

(** Under every valid order, [one_read] starts with its effect's run. *)
Lemma one_read_first_run fuel ord t :
  is_effect_order ord ->
  reference_trace fuel ord one_read [] = Some t ->
  t = [EvRun 9; EvLog 9 0].
Proof.
  intros Hord. unfold reference_trace, reference_runs.
  assert (effect_ids one_read = [9%nat]) as Hids by (vm_compute; reflexivity).
  rewrite Hids, singleton_order_id by done.
  destruct fuel; vm_compute; congruence.
Qed.

(** Answers only a history of two batches, with the trace of the last batch's run. On the two-batch
    [one_read] history it has the reference's [observe_settled], and [refines] rejects it at a prefix. *)
Definition final_batch_engine (_ : program) (h : history) : option (list event) :=
  if Nat.eqb (length h) 2 then Some [EvRun 9; EvLog 9 2] else None.

Definition two_writes : history := [[RWrite 0 1]; [RWrite 0 2]].

Example final_batch_engine_settled_at_full_history :
  observe observe_settled <$> final_batch_engine one_read two_writes =
    observe observe_settled <$> reference_trace 5 order_identity one_read two_writes.
Proof. vm_compute. reflexivity. Qed.

(**md
**`refines` rejects an engine that answers only the full history.**

*Check, not a guarantee.*

An engine matching the reference's settled view on a two-batch history fails at the empty prefix, where it gives no answer. This tests the definition of `refines`.
*)
Theorem final_batch_engine_not_refines : ~ refines observe_settled final_batch_engine.
Proof.
  intros Hr.
  destruct (Hr one_read two_writes _ one_read_well_formed eq_refl) as (ord & _ & Hall).
  destruct (Hall [] (prefix_nil _)) as (te & _ & _ & Hte & _).
  discriminate Hte.
Qed.

(** Traces keeping only each prefix's last run have the reference's [observe_settled] at each prefix of
    [two_writes]. Only trace extension rejects them: [last_run_engine_not_refines]. *)
Example last_run_traces_match_every_prefix :
  Some (observe observe_settled [EvRun 9; EvLog 9 0]) =
    observe observe_settled <$> reference_trace 5 order_identity one_read [] /\
  Some (observe observe_settled [EvRun 9; EvLog 9 1]) =
    observe observe_settled <$> reference_trace 5 order_identity one_read [[RWrite 0 1]] /\
  Some (observe observe_settled [EvRun 9; EvLog 9 2]) =
    observe observe_settled <$> reference_trace 5 order_identity one_read two_writes.
Proof. vm_compute. auto. Qed.

(** Answers each history with only the last run of [two_writes]'s matching prefix. *)
Definition last_run_engine (_ : program) (h : history) : option (list event) :=
  match h with
  | [] => Some [EvRun 9; EvLog 9 0]
  | [_] => Some [EvRun 9; EvLog 9 1]
  | _ => Some [EvRun 9; EvLog 9 2]
  end.

(**md
**`refines` rejects traces that match at each prefix but don't extend each other.**

*Check, not a guarantee.*

An engine answering each prefix of `two_writes` with only its last run has the reference's settled view at each prefix, and fails the requirement that a prefix's trace is a prefix of the whole trace. This tests the definition of `refines`.
*)
Theorem last_run_engine_not_refines : ~ refines observe_settled last_run_engine.
Proof.
  intros Hr.
  destruct (Hr one_read two_writes _ one_read_well_formed eq_refl) as (ord & _ & Hall).
  destruct (Hall [] (prefix_nil _)) as (te & _ & _ & Hte & [k Hk] & _).
  injection Hte as <-. discriminate Hk.
Qed.

(** An order that drops its effects gives the empty trace, so [refines] requires a valid order. *)
Example invalid_order_runs_nothing : reference_trace 0 (fun _ _ => []) one_read [] = Some [].
Proof. vm_compute. reflexivity. Qed.

(**md
**`refines` rejects an engine that emits nothing.**

*Check, not a guarantee.*

A valid order runs every effect in the initial round, so an empty trace misses a run's value under `observe_settled`. This tests the definition of `refines`.
*)
Theorem silent_engine_not_refines : ~ refines observe_settled (fun _ _ => Some []).
Proof.
  intros Hr.
  destruct (Hr one_read [] [] one_read_well_formed eq_refl) as (ord & Hord & Hall).
  destruct (Hall [] (prefix_nil _)) as (te & fuel & t' & Hte & _ & Ht & Heq).
  injection Hte as <-.
  rewrite (one_read_first_run _ _ _ Hord Ht) in Heq. vm_compute in Heq. discriminate Heq.
Qed.

(**md
**`engine_answers` rejects an engine with no traces.**

*Check, not a guarantee.*

The counterpart of [`none_engine_refines`](#none_engine_refines). This tests the definition of `engine_answers`.
*)
Theorem none_engine_not_answers : ~ engine_answers (fun _ _ _ => None).
Proof.
  intros Ha.
  assert (exists (k : nat) (t : list event), @None (list event) = Some t) as (? & ? & Hn);
    [|discriminate Hn].
  apply (Ha one_read []); [apply one_read_well_formed|].
  intros ord Hord. exists 0%nat, [EvRun 9; EvLog 9 0]. unfold reference_trace, reference_runs.
  assert (effect_ids one_read = [9%nat]) as Hids by (vm_compute; reflexivity).
  rewrite Hids, singleton_order_id by done. vm_compute. reflexivity.
Qed.
