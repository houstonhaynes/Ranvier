From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith Sorted.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Edges Marking Engine Check Runs Scheduler.

Open Scope nat_scope.

(** ** The order of a teardown *)

(** One step of [Owner.DisposeScope]: a child's release, or a cleanup with its environment and acquisition count. *)
Inductive teardown_item :=
  | ItemChild (l : link)
  | ItemCleanup (c : cmd * env * nat).

(**md
The acquisition count of a teardown step: its link's, or its cleanup's.
*)
Definition item_acquired (i : teardown_item) : nat :=
  match i with ItemChild l => l_acquired l | ItemCleanup c => c.2 end.

(** The merge [teardown] makes of its two lists. *)
Fixpoint schedule (ls : list link) (cs : list (cmd * env * nat)) : list teardown_item :=
  match ls with
  | [] => ItemCleanup <$> cs
  | l :: ls' =>
    (fix go (cs : list (cmd * env * nat)) : list teardown_item :=
       match cs with
       | [] => ItemChild l :: schedule ls' []
       | c :: cs' =>
         if decide (c.2 < l_acquired l) then ItemChild l :: schedule ls' cs
         else ItemCleanup c :: go cs'
       end) cs
  end.

(** The steps of [schedule] in order, each at the fuel [teardown] gives it. *)
Fixpoint run_items (fuel : nat) (o : owner_id) (items : list teardown_item) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    match items with
    | [] => m_return tt
    | ItemChild l :: r => let* _ := release f (l_child l) in run_items f o r
    | ItemCleanup (b, e, _) :: r => let* _ := run_cleanup f o b e in run_items f o r
    end
  end.

Lemma schedule_cons l ls c cs :
  schedule (l :: ls) (c :: cs) =
  if decide (c.2 < l_acquired l) then ItemChild l :: schedule ls (c :: cs)
  else ItemCleanup c :: schedule (l :: ls) cs.
Proof. reflexivity. Qed.

Lemma teardown_items f o : forall ls cs s, teardown f o ls cs s = run_items f o (schedule ls cs) s.
Proof.
  induction f as [|f IH]; intros ls cs s; [done|].
  destruct ls as [|l ls], cs as [|[[b e] t] cs].
  - done.
  - simpl. unfold m_bind. destruct (run_cleanup f o b e s) as [[s1 []]|]; [|done]. apply IH.
  - simpl. unfold m_bind. destruct (release f (l_child l) s) as [[s1 []]|]; [|done]. apply (IH ls []).
  - rewrite schedule_cons. cbn [teardown]. cbn [fst snd]. destruct (decide (t < l_acquired l)).
    + cbn [run_items]. unfold m_bind. destruct (release f (l_child l) s) as [[s1 []]|]; [|done]. apply IH.
    + cbn [run_items]. unfold m_bind. destruct (run_cleanup f o b e s) as [[s1 []]|]; [|done]. apply (IH (l :: ls) cs).
Qed.

Lemma schedule_perm : forall ls cs, schedule ls cs ≡ₚ (ItemChild <$> ls) ++ (ItemCleanup <$> cs).
Proof.
  induction ls as [|l ls IHl]; [done|]. induction cs as [|c cs IHc].
  - simpl. rewrite (IHl []). done.
  - rewrite schedule_cons. destruct (decide (c.2 < l_acquired l)).
    + rewrite (IHl (c :: cs)). done.
    + rewrite IHc. change (ItemCleanup <$> c :: cs) with (ItemCleanup c :: (ItemCleanup <$> cs)).
      apply Permutation_middle.
Qed.

(** The cleanups of a list of steps, in order. *)
Fixpoint cleanups_of (items : list teardown_item) : list (cmd * env * nat) :=
  match items with
  | [] => []
  | ItemChild _ :: r => cleanups_of r
  | ItemCleanup c :: r => c :: cleanups_of r
  end.

Lemma schedule_cleanups : forall ls cs, cleanups_of (schedule ls cs) = cs.
Proof.
  induction ls as [|l ls IHl].
  - induction cs as [|c cs IHc]; [done|]. change (c :: cleanups_of (schedule [] cs) = c :: cs). by rewrite IHc.
  - induction cs as [|c cs IHc]; [apply (IHl [])|]. rewrite schedule_cons.
    destruct (decide (c.2 < l_acquired l)).
    + exact (IHl (c :: cs)).
    + change (c :: cleanups_of (schedule (l :: ls) cs) = c :: cs). by rewrite IHc.
Qed.

Local Notation desc := (StronglySorted gt).

Lemma schedule_sorted : forall ls cs,
  desc (l_acquired <$> ls) -> desc (snd <$> cs) ->
  (forall l c, l ∈ ls -> c ∈ cs -> l_acquired l <> c.2) ->
  desc (item_acquired <$> schedule ls cs).
Proof.
  assert (Hmem : forall ls cs a, a ∈ item_acquired <$> schedule ls cs -> a ∈ (l_acquired <$> ls) \/ a ∈ (snd <$> cs)).
  { intros ls cs a Ha. apply list_elem_of_fmap in Ha as (i&->&Hi).
    rewrite schedule_perm in Hi.
    apply elem_of_app in Hi as [Hi|Hi]; apply list_elem_of_fmap in Hi as (z&->&Hz).
    - left. apply list_elem_of_fmap. eauto.
    - right. apply list_elem_of_fmap. eauto. }
  induction ls as [|l ls IHl]; intros cs0.
  - intros _ Hc _. simpl. rewrite <- list_fmap_compose. exact Hc.
  - induction cs0 as [|c cs IHc]; intros Hl Hc Hd.
    + simpl. cbn [fmap list_fmap] in Hl. apply StronglySorted_inv in Hl as [Hl Hf]. apply SSorted_cons.
      * apply IHl; [done|apply SSorted_nil|intros ?? _ Hx; by apply not_elem_of_nil in Hx].
      * apply Forall_forall. intros a Ha. apply Hmem in Ha as [Ha|Ha]; [by eapply Forall_forall in Hf|by apply not_elem_of_nil in Ha].
    + rewrite schedule_cons. pose proof Hl as Hl0. pose proof Hc as Hc0. cbn [fmap list_fmap] in Hl, Hc.
      apply StronglySorted_inv in Hl as [Hl Hfl]. apply StronglySorted_inv in Hc as [Hc Hfc].
      destruct (decide (c.2 < l_acquired l)) as [Hlt|Hge].
      * cbn [fmap list_fmap item_acquired]. apply SSorted_cons.
        -- apply IHl; [done|done|]. intros l' c' Hl' Hc'. apply Hd; [by right|done].
        -- apply Forall_forall. intros a Ha. apply Hmem in Ha as [Ha|Ha]; [by eapply Forall_forall in Hfl|].
           apply elem_of_cons in Ha as [->|Ha]; [lia|]. eapply Forall_forall in Hfc; [|exact Ha]. simpl in *. lia.
      * cbn [fmap list_fmap item_acquired]. apply SSorted_cons.
        -- apply IHc; [done|done|]. intros l' c' Hl' Hc'. apply Hd; [done|by right].
        -- assert (Hne : c.2 <> l_acquired l) by (intros E; apply (Hd l c); [left|left|done]).
           apply Forall_forall. intros a Ha. apply Hmem in Ha as [Ha|Ha].
           ++ apply elem_of_cons in Ha as [->|Ha]; [lia|]. eapply Forall_forall in Hfl; [|exact Ha]. simpl in *. lia.
           ++ by eapply Forall_forall in Hfc.
Qed.

Lemma ss_rev_gt (l : list nat) : StronglySorted lt l -> desc (rev l).
Proof.
  induction 1 as [|a l Hs IH Hf]; [apply SSorted_nil|]. simpl. apply ss_snoc; [done|].
  apply Forall_forall. intros b Hb. apply list_elem_of_In in Hb. apply in_rev in Hb. apply list_elem_of_In in Hb.
  eapply Forall_forall in Hf; [|exact Hb]. simpl in *. lia.
Qed.

Lemma ss_links_map (l : list link) : StronglySorted link_before l -> StronglySorted lt (l_acquired <$> l).
Proof.
  induction 1 as [|a l Hs IH Hf]; [apply SSorted_nil|]. simpl. apply SSorted_cons; [done|].
  apply Forall_forall. intros b Hb. apply list_elem_of_fmap in Hb as (z&->&Hz).
  by eapply Forall_forall in Hf.
Qed.

(**md
The acquisition counts `os`, truncated to the shortest of the three parallel lists.
*)
Fixpoint zip_orders (bs : list cmd) (es : list env) (os : list nat) : list nat :=
  match bs, es, os with
  | _ :: bs', _ :: es', o :: os' => o :: zip_orders bs' es' os'
  | _, _, _ => []
  end.

Lemma zip_snd bs es os : snd <$> zip_cleanups bs es os = zip_orders bs es os.
Proof.
  revert es os. induction bs as [|b bs IH]; intros [|e es] [|o os]; simpl; try done. by rewrite IH.
Qed.

Lemma zip_orders_sub bs es os x : x ∈ zip_orders bs es os -> x ∈ os.
Proof.
  revert es os. induction bs as [|b bs IH]; intros [|e es] [|o os]; simpl; try (intros Hx; by apply not_elem_of_nil in Hx).
  intros Hx. apply elem_of_cons in Hx as [->|Hx]; [left|right; by eapply IH].
Qed.

Lemma zip_orders_sorted bs es os : StronglySorted lt os -> StronglySorted lt (zip_orders bs es os).
Proof.
  revert es os. induction bs as [|b bs IH]; intros [|e es] [|o os]; simpl; try (intros; apply SSorted_nil).
  intros Hs. apply StronglySorted_inv in Hs as [Hs Hf]. apply SSorted_cons; [by apply IH|].
  apply Forall_forall. intros x Hx. apply zip_orders_sub in Hx. by eapply Forall_forall in Hf.
Qed.

Lemma fmap_rev {A B} (g : A -> B) (l : list A) : g <$> rev l = rev (g <$> l).
Proof. induction l as [|a l IH]; [done|]. simpl. rewrite fmap_app, IH. done. Qed.

Lemma cleanups_snd y : snd <$> owner_cleanups y = rev (zip_orders (o_cleanups y) (o_cleanup_envs y) (o_cleanup_order y)).
Proof. unfold owner_cleanups. rewrite fmap_rev. by rewrite zip_snd. Qed.

(**md
**A scope tears down its children and cleanups in reverse acquisition order.**

`dispose_scope` detaches the owner's lists and then runs one merged list of steps. Each child and each cleanup held at that moment appears in it exactly once, and their acquisition counts strictly fall. So a later acquisition is torn down first, and a cleanup runs before the release of any child acquired ahead of it. This covers the lists detached at the start: a registration made during the teardown joins the emptied scope.

Premise: the owner's lists are in acquisition order, `acq_ok`. [`owners_in_order`](#owners_in_order) holds that of every owner after a program. The fuel in the first clause is one step less than `dispose_scope` is given, and a step that runs out of fuel leaves the whole call with no result.

- [f:] the fuel, which `dispose_scope` runs at `S f`.
- [o:] the owner disposed.
- [y:] the owner state of `o` in `s`.
- [s:] the state before the disposal.
*)
Theorem dispose_reverse_order f o y s :
  g_owners s !! o = Some y -> acq_ok y ->
  let items := schedule (rev (o_children y)) (owner_cleanups y) in
  dispose_scope (S f) o s = run_items f o items (with_owners (<[o := owner_detached y]> (g_owners s)) s) /\
  StronglySorted gt (item_acquired <$> items) /\
  items ≡ₚ (ItemChild <$> rev (o_children y)) ++ (ItemCleanup <$> owner_cleanups y).
Proof.
  intros Hy [H1 H2 H3 H4 H5 _ _] items. split_and!.
  - cbn [dispose_scope]. unfold m_bind, get_owner. rewrite Hy. unfold put_owner, m_modify. apply teardown_items.
  - apply schedule_sorted.
    + rewrite fmap_rev. by apply ss_rev_gt, ss_links_map.
    + rewrite cleanups_snd. by apply ss_rev_gt, zip_orders_sorted.
    + intros l c Hl Hc E. apply list_elem_of_In, in_rev, list_elem_of_In in Hl.
      eapply Forall_forall in H5; [|exact Hl]. apply H5. rewrite E.
      apply (zip_orders_sub (o_cleanups y) (o_cleanup_envs y)).
      apply list_elem_of_In, in_rev, list_elem_of_In. rewrite <- cleanups_snd.
      apply list_elem_of_fmap. eauto.
  - apply schedule_perm.
Qed.

(**md
**After any program, every owner's children and cleanups are in acquisition order.**

Premise: the program starts from a new graph.

- [f:] the fuel.
- [fl:] the flags of the new graph.
- [c:] the command run.
- [s':] the state after the run.
- [r:] the outcome of the run.
- [o:] an owner of `s'`.
- [y:] the owner state of `o`.
*)
Corollary owners_in_order f fl c s' r o y :
  run_top f c (init fl) = Some (s', r) -> g_owners s' !! o = Some y -> acq_ok y.
Proof.
  intros Hm Hy. pose proof (sched_inv_run_top f c _ s' r (sched_inv_init fl) Hm) as (Ha&_).
  exact (sd_acq _ _ Ha o y Hy).
Qed.

(** ** Each cleanup once *)

Lemma late_equation f e b k o y s :
  g_current_owner s = Some o -> g_owners s !! o = Some y -> o_disposed y = true ->
  exec (S f) e (CCleanup b k) s =
    (let* _ := detached o (run_cleanup f o b e) in
     let* _ := request_flush f in
     exec f e k) (ghost_registered (o_host y) s).
Proof.
  intros Hc Hy Hd. cbn [exec]. unfold m_bind at 1, current_owner. rewrite Hc.
  rewrite (bind_get_owner _ _ _ _ Hy), bind_m_modify, Hd. unfold m_bind. simpl.
  destruct (detached o (run_cleanup f o b e) _) as [[s2 []]|]; [|done]. done.
Qed.

Lemma zip_bodies bs es os :
  length es = length bs -> length os = length bs ->
  (fun p : cmd * env * nat => p.1.1) <$> zip_cleanups bs es os = bs.
Proof.
  revert es os. induction bs as [|b bs IH]; intros [|e es] [|o os]; simpl; try done; try lia.
  intros H1 H2. rewrite IH; [done|lia|lia].
Qed.

(** What the engine does with each entry of an owner's cleanup list. *)
Definition cleanup_entries (f : nat) : Prop :=
  (forall ls cs, cleanups_of (schedule ls cs) = cs) /\
  (forall o ls cs s, teardown f o ls cs s = run_items f o (schedule ls cs) s) /\
  (forall o b e t r s, run_items (S f) o (ItemCleanup (b, e, t) :: r) s =
     (let* _ := run_cleanup f o b e in run_items f o r) s) /\
  (forall y, o_children (owner_detached y) = [] /\ o_cleanups (owner_detached y) = [] /\
     o_cleanup_envs (owner_detached y) = [] /\ o_cleanup_order (owner_detached y) = []) /\
  (forall o y s, g_owners s !! o = Some y ->
     dispose_scope (S f) o s =
       teardown f o (rev (o_children y)) (owner_cleanups y) (with_owners (<[o := owner_detached y]> (g_owners s)) s)) /\
  (forall y, acq_ok y -> (fun p : cmd * env * nat => p.1.1) <$> owner_cleanups y = rev (o_cleanups y)) /\
  (forall e b k o y s, g_current_owner s = Some o -> g_owners s !! o = Some y -> o_disposed y = true ->
     exec (S f) e (CCleanup b k) s =
       (let* _ := detached o (run_cleanup f o b e) in
        let* _ := request_flush f in
        exec f e k) (ghost_registered (o_host y) s) /\
     g_owners (ghost_registered (o_host y) s) = g_owners s).

Lemma cleanup_entries_all f : cleanup_entries f.
Proof.
  split_and!.
  - apply schedule_cleanups.
  - intros. apply teardown_items.
  - reflexivity.
  - done.
  - intros o y s Hy. cbn [dispose_scope]. unfold m_bind, get_owner. by rewrite Hy.
  - intros y [_ _ _ _ _ H6 H7]. unfold owner_cleanups. rewrite fmap_rev. by rewrite zip_bodies.
  - intros e b k o y s Hc Hy Hd. split; [by apply late_equation|done].
Qed.

(**md
**Each cleanup entry runs once, and a host's cleanup events never exceed its registrations.**

The count is structural, per list entry, because a registration carries no id. These hold together:

- **The bound.** After a program, `EvCleanup h` events plus the cleanups still held by owners of host `h` are at most the cleanups registered with owners of host `h`, late ones included. `g_registered` is ghost state written at `CCleanup`. This is a sum per host, so it alone would hold of a teardown running one cleanup twice and dropping the next.
- **The list.** The cleanups of `schedule ls cs`, in order, are exactly `cs`. The bodies of `owner_cleanups` are `o_cleanups` reversed, for an owner in `acq_ok`.
- **The teardown.** `teardown` is `run_items` over that schedule, and `run_items` runs `run_cleanup` once for each cleanup step and goes on with the rest. `dispose_scope` hands it the owner's detached entries, and `owner_detached` leaves all the lists empty, so a later teardown holds none of them.
- **The late path.** On a disposed current owner, `CCleanup` runs `run_cleanup` once under `detached`, with no write to the owners.

Premises of the bound: [the scheduler's invariant](Ranvier.Pure.Scheduler.html#sched_inv) and the bound hold at the start. `cleanup_once_new_graph` discharges both for a program on a new graph. An entry's body can itself register or dispose, and those are further registrations and further steps.

- [f:] the fuel.
*)
Theorem cleanup_once f :
  (forall c s s' r h, sched_inv None s -> cleanup_score h s <= registered h s ->
     run_top f c s = Some (s', r) -> cleanup_score h s' <= registered h s') /\
  cleanup_entries f.
Proof.
  split; [|apply cleanup_entries_all].
  intros c s s' r h Hi Hb Hm. destruct (Sexec _ (sched_ok_all f) None [] c _ _ _ Hi I Hm) as [_ (_&_&Hc)].
  specialize (Hc h). unfold zero in Hc. lia.
Qed.

(**md
**On a new graph, each cleanup entry runs once, and a host's cleanup events never exceed its registrations.**

The count is structural, per list entry, because a registration carries no id. The second part is `cleanup_entries`, as [`cleanup_once`](#cleanup_once) describes it.

- [f:] the fuel.
- [fl:] the flags of the new graph.
*)
Corollary cleanup_once_new_graph f fl :
  (forall c s' r h, run_top f c (init fl) = Some (s', r) -> count_cleanups h (g_trace s') <= registered h s') /\
  cleanup_entries f.
Proof.
  split; [|apply cleanup_entries_all].
  intros c s' r h Hm. assert (Hb : cleanup_score h (init fl) <= registered h (init fl)).
  { unfold cleanup_score, registered, owners_pending, owner_pending. simpl.
    rewrite map_to_list_insert, map_to_list_empty by apply lookup_empty. simpl.
    destruct (decide _); simpl; lia. }
  pose proof (proj1 (cleanup_once f) c _ s' r h (sched_inv_init fl) Hb Hm) as H.
  unfold cleanup_score in H. lia.
Qed.

(** ** Disposal *)

(**md
**A disposed computation holds no source edges once its run has ended.**

After a program, a disposed node that is not on the running stack has no slots, no [`edge`](Ranvier.Pure.State.html#edge) from any source, and appears in no source's observers. A computation disposed mid-body links edges until its run ends: [`disposed_running_observer_stays_clean`](Ranvier.Pure.Witnesses.html#disposed_running_observer_stays_clean).

Premise: `engine_settled` holds at the start. That invariant's `b_disposed` states the slots clause, so this is a preservation statement. `disposed_no_edges_new_graph` discharges the premise for a program on a new graph.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state, which satisfies `engine_settled`.
- [s':] the state after the run.
- [r:] the outcome of the run.
- [n:] a disposed node that is not running in `s'`.
- [d:] the node `n` in `s'`.
*)
Theorem disposed_no_edges f c s s' r n d :
  engine_settled s -> run_top f c s = Some (s', r) ->
  g_nodes s' !! n = Some d -> n_disposed d = true -> n ∉ g_running s' ->
  source_slots (sources_of (g_edges s') n) = [] /\
  (forall src, ~ edge s' src n) /\
  (forall src, n ∉ observer_view (observers_of (g_edges s') src)).
Proof.
  intros Hs Hm Hd Hdi Hnr.
  pose proof (Pexec _ _ (engine_settled_preserved f) [] c _ _ _ Hs I Hm) as [[Hi [Hb Ht]] Hw].
  pose proof (b_disposed _ Hb n d Hd Hdi Hnr) as Hv.
  assert (Hslots : source_slots (sources_of (g_edges s') n) = []).
  { unfold slot_view in Hv. simpl in Hv. by apply fmap_nil_inv in Hv. }
  assert (Hedge : forall src, ~ edge s' src n).
  { intros src (i&h&Hsl&_). unfold slot_at in Hsl. rewrite Hslots in Hsl. done. }
  split_and!; [done|done|].
  intros src Hin. apply (Hedge src).
  pose proof (me_reachable _ (b_env _ Hb)) as Hr.
  apply (proj1 (edge_iff_both_sides s' src n (proj1 (reachable_well_formed _ Hr)))). exact Hin.
Qed.

(**md
**On a new graph, a disposed computation holds no source edges once its run has ended.**

- [f:] the fuel.
- [c:] the command run on a new graph with `current_flags`.
- [s':] the state after the run.
- [r:] the outcome of the run.
- [n:] a disposed node that is not running in `s'`.
- [d:] the node `n` in `s'`.
*)
Corollary disposed_no_edges_new_graph f c s' r n d :
  run_top f c (init current_flags) = Some (s', r) ->
  g_nodes s' !! n = Some d -> n_disposed d = true -> n ∉ g_running s' ->
  source_slots (sources_of (g_edges s') n) = [] /\
  (forall src, ~ edge s' src n) /\
  (forall src, n ∉ observer_view (observers_of (g_edges s') src)).
Proof. apply disposed_no_edges. apply engine_settled_init. Qed.

(**md
**A disposed effect never runs again.**

From a state where the effect is disposed, a whole program, a flush or a `drain` that returns leaves it disposed, with `n_runs` unchanged and no new `EvRun n` in the trace. The `drain` clause covers a disposal mid-flush: from the loop head after the effect that disposed it, its own entry is dequeued with no run, as [`queued_effect_disposed_skipped`](#queued_effect_disposed_skipped) has it. The `execute_effect` clauses are that dequeue, which changes only `queued`, as Core.fs 4785 does. A body in progress at its disposal keeps running to its end: this is about later runs.

Premises: [the scheduler's invariant](Ranvier.Pure.Scheduler.html#sched_inv) holds at the start, with no effect in progress, or for `execute_effect` with this effect in progress. A `drain` starts during a flush.

- [f:] the fuel.
- [n:] the disposed effect.
- [d:] the node `n`, as stored in the starting states.
*)
Theorem disposed_effect_never_runs f n d :
  n_kind d = KEffect -> n_disposed d = true ->
  (forall s c s' r, sched_inv None s -> g_nodes s !! n = Some d -> run_top f c s = Some (s', r) ->
     (exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true /\ n_runs d' = n_runs d) /\
     count_runs n (g_trace s') = count_runs n (g_trace s)) /\
  (forall s s' a, sched_inv None s -> g_nodes s !! n = Some d -> run_flush f s = Some (s', a) ->
     (exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true /\ n_runs d' = n_runs d) /\
     count_runs n (g_trace s') = count_runs n (g_trace s)) /\
  (forall s s' a, sched_inv None s -> g_nodes s !! n = Some d -> g_flushing s = true -> drain f s = Some (s', a) ->
     (exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true /\ n_runs d' = n_runs d) /\
     count_runs n (g_trace s') = count_runs n (g_trace s)) /\
  (forall s s' a, sched_inv (Some n) s -> g_nodes s !! n = Some d -> g_flushing s = true -> execute_effect f n s = Some (s', a) ->
     (exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true /\ n_runs d' = n_runs d) /\
     count_runs n (g_trace s') = count_runs n (g_trace s)) /\
  (forall s, g_nodes s !! n = Some d ->
     execute_effect (S f) n s = Some (set_node n (node_with_queued false d) s, tt)).
Proof.
  intros Hk Hz.
  assert (Hrel : forall s s', g_nodes s !! n = Some d -> data_rel s s' ->
     (exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true /\ n_runs d' = n_runs d) /\
     count_runs n (g_trace s') = count_runs n (g_trace s)).
  { intros s s' Hd R. destruct (r_nodes _ _ R n d Hd) as (d'&Hd'&_&Hi'&Hr'). split.
    - exists d'. auto.
    - exact (r_runs _ _ R n d Hd Hk Hz). }
  split_and!.
  - intros s c s' r Hi Hd Hm. destruct (Sexec _ (sched_ok_all f) None [] c _ _ _ Hi I Hm) as [_ (_&R&_)]. by apply Hrel.
  - intros s s' a Hi Hd Hm. destruct (Sflush _ (sched_ok_all f) None _ _ _ Hi I Hm) as [_ (_&R&_)]. by apply Hrel.
  - intros s s' a Hi Hd Hf Hm. destruct (Sdrain _ (sched_ok_all f) _ _ _ Hi Hf Hm) as [_ (_&R&_)]. by apply Hrel.
  - intros s s' a Hi Hd Hf Hm.
    assert (Hc : eff_ctx n s) by (split; [by exists d|exact Hf]).
    destruct (Sexecute _ (sched_ok_all f) n _ _ _ Hi Hc Hm) as [_ (_&R&_)]. by apply Hrel.
  - intros s Hd. cbn [execute_effect]. rewrite (bind_get_node _ _ _ _ Hd), Hz. unfold modify_node. by rewrite Hd.
Qed.

Lemma track_keeps n s s' :
  track n s = Some (s', tt) -> g_nodes s' = g_nodes s /\ g_trace s' = g_trace s /\ g_queue s' = g_queue s.
Proof.
  unfold track. destruct (g_current s); [|intros; simplify_eq; done].
  destruct (sources_add _ _ _); [|done]. intros Hm. simplify_eq. unfold ghost_read. simpl.
  destruct (g_nodes s !! n); done.
Qed.

(**md
**A disposed memo answers from what it stored, without running.**

A read of a disposed memo that returns leaves every node, the trace and the queue as they were, so no body ran and nothing was scheduled. It answers `Fail` of the stored failure when the status is `StError` (0 when `n_failure` is `None`), and otherwise `Ok` of the stored value. Core.fs 4372-4376 documents that a disposed memo "still answers reads, with the last value it computed".

That stored value need not be a computed one, and need not be up to date. A memo disposed before its first run answers the default: [`disposed_unread_answers_default`](#disposed_unread_answers_default). The reference tier answers from the signals at the disposal, and `BUGS.md` holds that difference as `disposed_unread_memo_value` and `write_then_dispose_memo_current_value`, Awaiting ruling. A tracked read can still add an edge from the disposed memo: [`disposed_stale_source_read`](Ranvier.Pure.Witnesses.html#disposed_stale_source_read).

- [f:] the fuel.
- [n:] the disposed memo read.
- [b:] whether the read is tracked.
- [s:] the state before the read.
- [s':] the state after the read.
- [d:] the node `n` in `s`.
- [o:] the outcome of the read.
*)
Theorem disposed_memo_answers f n b s s' d o :
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_disposed d = true ->
  read f n b s = Some (s', o) ->
  g_nodes s' = g_nodes s /\ g_trace s' = g_trace s /\ g_queue s' = g_queue s /\
  o = match n_status d with StError => Fail (default 0%Z (n_failure d)) | _ => Ok (n_value d) end.
Proof.
  intros Hd Hk Hz Hr.
  assert (Hnode : forall f s s' o, g_nodes s !! n = Some d -> read_node f n s = Some (s', o) ->
     g_nodes s' = g_nodes s /\ g_trace s' = g_trace s /\ g_queue s' = g_queue s /\
     o = match n_status d with StError => Fail (default 0%Z (n_failure d)) | _ => Ok (n_value d) end).
  { clear -Hk Hz. intros f s s' o Hd Hr. destruct f as [|f]; [done|]. cbn [read_node] in Hr.
    rewrite (bind_get_node _ _ _ _ Hd), Hk in Hr.
    assert (Hl : stale_live d = false) by (unfold stale_live; rewrite Hz; apply andb_false_r).
    rewrite Hl in Hr. unfold m_bind in Hr.
    destruct (track n s) as [[s1 []]|] eqn:Et; [|done].
    destruct (track_keeps _ _ _ Et) as (H1&H2&H3).
    assert (Hd1 : g_nodes s1 !! n = Some d) by (by rewrite H1).
    unfold get_node in Hr. rewrite Hd1 in Hr.
    destruct (n_status d); unfold m_return in Hr; by simplify_eq. }
  destruct f as [|f]; [done|]. cbn [read] in Hr.
  destruct b; [by eapply Hnode|].
  unfold untracked, m_bind, m_modify, m_return in Hr.
  destruct (read_node f n _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  apply Hnode in E as (H1&H2&H3&H4); [|exact Hd]. split_and!; done.
Qed.

(** ** Late registrations *)

(**md
**A cleanup registered on a disposed scope runs at once.**

On a disposed current owner, `CCleanup b k` counts the registration, runs the cleanup under `detached`, requests a flush, and only then continues with `k`, with no write to the owner's cleanup list. When the whole command returns, the trace starts with the starting trace followed by `EvCleanup` of the owner's host.

Premises: the current owner is disposed, and for the second clause [the scheduler's invariant](Ranvier.Pure.Scheduler.html#sched_inv) holds and the fuel leaves the cleanup at least one step.

- [f:] the fuel, which `exec` runs at `S f`.
- [x:] the effect in progress for `sched_inv`, if any.
- [e:] the environment of the command.
- [b:] the cleanup registered.
- [k:] the command that follows the registration.
- [o:] the current owner, which is disposed.
- [y:] the owner state of `o` in `s`.
- [s:] the state before the registration.
*)
Theorem late_registration_immediate f x e b k o y s :
  g_current_owner s = Some o -> g_owners s !! o = Some y -> o_disposed y = true ->
  exec (S f) e (CCleanup b k) s =
    (let* _ := detached o (run_cleanup f o b e) in
     let* _ := request_flush f in
     exec f e k) (ghost_registered (o_host y) s) /\
  (sched_inv x s -> forall f' s' r, f = S f' -> exec (S f) e (CCleanup b k) s = Some (s', r) ->
     (g_trace s ++ [EvCleanup (o_host y)]) `prefix_of` g_trace s').
Proof.
  intros Hc Hy Hd.
  pose proof (late_equation f e b k o y s Hc Hy Hd) as Heq.
  split; [exact Heq|]. intros Hi f' s' r -> Hm.
  rewrite Heq in Hm. pose proof (step_ghost x (o_host y) s Hi) as H1.
  unfold m_bind in Hm at 1.
  destruct (detached o (run_cleanup (S f') o b e) _) as [[s2 []]|] eqn:Ed; [|done].
  rewrite detached_unfold in Ed.
  destruct (run_cleanup (S f') o b e (detached_state o _)) as [[s3 []]|] eqn:Ec; [|done]. simplify_eq.
  pose proof (detached_in x o _ H1) as H2.
  assert (Hy2 : g_owners (detached_state o (ghost_registered (o_host y) s)) !! o = Some y) by exact Hy.
  pose proof (cleanup_emits _ x o b e y _ _ _ H2 Hy2 Ec) as Hp.
  destruct (Scleanup _ (sched_ok_all (S f')) x o b e _ _ _ H2 (mk_is_Some _ _ Hy2) Ec) as [H3 F3].
  destruct (detached_out x _ o _ s3 H1 H3 F3) as [H4 F4].
  assert (Hk : sok x x (fun _ => True) (let* _ := request_flush (S f') in exec (S f') e k)).
  { apply sok_bind0; [apply _|apply (Srequest _ (sched_ok_all (S f')))|intros _; apply (Sexec _ (sched_ok_all (S f')))]. }
  destruct (Hk _ _ _ H4 I Hm) as [_ (_&R&_)].
  etrans; [|exact (r_trace _ _ R)]. exact Hp.
Qed.

(**md
**A node attached to a disposed scope is disposed at once.**

`attach` on a disposed owner releases the node first. When it returns the node is disposed, and it holds a link with no generation. So an effect created on a disposed scope is disposed before the mark of its creation, and [`disposed_effect_never_runs`](#disposed_effect_never_runs) applies from there: [`late_registrations_run_at_once`](#late_registrations_run_at_once) has such a child.

Premises: the owner is disposed, the node exists, and [the scheduler's invariant](Ranvier.Pure.Scheduler.html#sched_inv) holds.

- [f:] the fuel, which `attach` runs at `S f`.
- [x:] the effect in progress for `sched_inv`, if any.
- [n:] the node attached.
- [o:] the disposed owner it is attached to.
- [y:] the owner state of `o` in `s`.
- [s:] the state before the attach.
- [s':] the state after the attach.
*)
Theorem late_attach_disposed f x n o y s s' :
  sched_inv x s -> g_owners s !! o = Some y -> o_disposed y = true -> is_Some (g_nodes s !! n) ->
  attach (S f) n o s = Some (s', tt) ->
  exists d, g_nodes s' !! n = Some d /\ n_disposed d = true /\
    n_link d = Some {| l_child := n; l_owner := o; l_generation := None; l_acquired := 0 |}.
Proof.
  intros Hi Hy Hd [e Hn] Hm. cbn [attach] in Hm. rewrite (bind_get_owner _ _ _ _ Hy), Hd in Hm.
  unfold m_bind in Hm. destruct (release f n s) as [[s1 []]|] eqn:Er; [|done].
  destruct f as [|f]; [done|]. cbn [release] in Er.
  rewrite (bind_modify_node _ _ _ _ _ Hn) in Er.
  assert (Hu : nstep (node_with_link None) e) by (unfold nstep; cbn; split_and!; intros; try split; done).
  destruct (step_node x n (node_with_link None) e s Hu Hn Hi) as [H1 _].
  destruct (dispose_node_disposes _ x n _ _ _ H1 ltac:(simpl; rewrite lookup_insert_eq; by eexists) Er) as (d&Hd1&Hz).
  unfold modify_node in Hm. rewrite Hd1 in Hm. simplify_eq. simpl.
  rewrite lookup_insert_eq. eexists. split_and!; try done.
Qed.

(**md
**Detaching a stale link changes nothing.**

`unlink` of a link whose generation is not the owner's current one returns the state it was given. A teardown bumps the generation before its first step, so a child disposed during that teardown takes this path.

This pins the model's check and no reachable behaviour. On reachable states `owner_without` matches on child and acquisition, and a later generation holds no such link, so the check is redundant there. The F#'s check guards `Prev` and `Next` of a detached list (Core.fs 1053-1072), which the model does not have.

Premise: the owner exists.

- [l:] a link from an earlier generation of its owner.
- [x:] the owner state of the link's owner in `s`.
- [s:] the state, which `unlink` returns unchanged.
*)
Theorem stale_link_detach_noop l x s :
  g_owners s !! l_owner l = Some x -> l_generation l <> Some (o_generation x) ->
  unlink l s = Some (s, tt).
Proof.
  intros Hy Hg. unfold unlink. rewrite (bind_get_owner _ _ _ _ Hy). destruct (decide _); done.
Qed.

(** ** Examples *)

Local Notation V := EVar.
Local Notation K := EConst.
Local Notation Nop := (CRet (EConst 0%Z)).

Local Definition final (p : cmd) : state :=
  default (init current_flags) (fst <$> run_top 100000 p (init current_flags)).

(** An effect's cleanup, its child with a cleanup, and a second cleanup, then the effect's disposal. *)
Definition teardown_order : cmd :=
  CSignal 1 (K 0%Z) (
  CEffect 2 (CRead 10 (V 1) (
    CCleanup (CLog (K 1%Z) Nop) (
    CEffect 11 (CCleanup (CLog (K 2%Z) Nop) Nop) (
    CCleanup (CLog (K 3%Z) Nop) Nop)))) (
  CDispose (V 2) Nop)).

(** The later cleanup first, then the child's, then the earlier cleanup. *)
Example teardown_runs_in_reverse :
  trace_of 100000 current_flags teardown_order =
    Some [EvRun 2; EvRun 3; EvCleanup 2; EvLog 2 3%Z; EvCleanup 3; EvLog 3 2%Z; EvCleanup 2; EvLog 2 1%Z; EvDispose 2].
Proof. vm_compute. reflexivity. Qed.

(** An effect disposes itself mid-body, then registers a cleanup and creates a child. *)
Definition late_registrations : cmd :=
  CBatch
    (CEffect 1 (CDispose (V 1) (
       CCleanup (CLog (K 100%Z) Nop) (
       CEffect 11 (CLog (K 5%Z) Nop) (
       CLog (K 7%Z) Nop)))) Nop) Nop.

(** The cleanup runs before the body's next log, and the child never runs. *)
Example late_registrations_run_at_once :
  trace_of 100000 current_flags late_registrations =
    Some [EvRun 1; EvDispose 1; EvCleanup 1; EvLog 1 100%Z; EvLog 1 7%Z].
Proof. vm_compute. reflexivity. Qed.

(** An effect ahead in the queue disposes the effect behind it, and the entry is dequeued with no run. *)
Definition queued_effect_disposed : cmd :=
  CSignal 1 (K 0%Z) (
  CEffect 2 (CRead 10 (V 1) (CLog (V 10) Nop)) (
  CEffect 3 (CRead 10 (V 1) (CIf (V 10) (CDispose (K 2%Z) Nop) Nop)) (
  CWrite (V 1) (K 1%Z) Nop))).

Example queued_effect_disposed_skipped :
  trace_of 100000 current_flags queued_effect_disposed =
    Some [EvRun 2; EvLog 2 0%Z; EvRun 3; EvRun 3; EvDispose 2].
Proof. vm_compute. reflexivity. Qed.

(** A late cleanup fails, and the body goes on. *)
Definition late_cleanup_failure : cmd :=
  CBatch (CEffect 1 (CDispose (V 1) (CCleanup (CThrow 8%Z) (CLog (K 7%Z) Nop))) Nop) Nop.

Example late_cleanup_failure_recorded :
  trace_of 100000 current_flags late_cleanup_failure =
    Some [EvRun 1; EvDispose 1; EvCleanup 1; EvFail 1 8%Z; EvLog 1 7%Z].
Proof. vm_compute. reflexivity. Qed.

(** A memo disposed with no read answers the default, with no run. *)
Example disposed_unread_answers_default :
  let p := CSignal 1 (K 5%Z) (CMemo 2 Pure CmpEq (CRead 10 (V 1) (CRet (V 10))) (CDispose (V 2) (CRead 20 (V 2) Nop))) in
  trace_of 100000 current_flags p = Some [EvDispose 2; EvHostRead 2 (Ok 0%Z)] /\
  (n_runs <$> g_nodes (final p) !! 2) = Some 0.
Proof. split; vm_compute; reflexivity. Qed.

(** An effect's child holds a link of the scope's generation, and a teardown's first step makes that link stale. *)
Example stale_link_reached :
  let before := final (CSignal 1 (K 0%Z) (CEffect 2 (CRead 10 (V 1) (CEffect 11 Nop Nop)) Nop)) in
  (exists l x w,
     (n_link <$> g_nodes before !! 3) = Some (Some l) /\
     g_owners before !! l_owner l = Some x /\
     w = with_owners (<[l_owner l := owner_detached x]> (g_owners before)) before /\
     g_owners w !! l_owner l = Some (owner_detached x) /\
     l_generation l <> Some (o_generation (owner_detached x)) /\
     unlink l w = Some (w, tt)).
Proof.
  intros before. eexists _, _, _. split_and!; try (vm_compute; reflexivity).
  vm_compute. discriminate.
Qed.
