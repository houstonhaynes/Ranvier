From stdpp Require Import prelude gmap.
From Ranvier.Base Require Import Prelude.
From Ranvier.Pure Require Import ObserverSet SourceList State.

(** Slot [i] of [c] holds [(s, Some h)] exactly when position [h] of [s]'s observers holds [(c, i)].
    The invariant constrains only slots holding a handle. [read_without_edge] is coherent with a read
    source and no edge to it, and [aba_world] is coherent with two edges per source. [world_well_formed] gives
    [first_owns] and [slots_unique], and [observer_held_once] gives [held_once]. *)
Definition coherent (w : EdgeWorld) : Prop := edge_match w.

(** ** Source-list operations keep the two ends of every edge in agreement *)

Lemma coherent_empty : coherent empty_world.
Proof.
  intros c i s h. unfold slot_at, observers_of, sources_of. simpl. rewrite !lookup_empty. simpl.
  rewrite !lookup_nil. done.
Qed.

(**md
**Starting a run keeps the two ends of every edge in agreement.**

`BeginRun` on a coherent world gives a coherent world.

- [c:] the computation that starts a run.
- [w:] the coherent world before the run.
*)
Theorem coherent_begin c w : coherent w -> coherent (sources_begin_run c w).
Proof.
  intros Hm c' i s h. unfold sources_begin_run. rewrite slot_at_set_sources, observers_of_set_sources.
  destruct (decide (c' = c)) as [->|]; apply Hm.
Qed.

(**md
**A tracked read succeeds and keeps the two ends of every edge in agreement.**

`Add` on a coherent world returns a world, and that world is coherent.

- [self:] the computation reading.
- [s:] the source read.
- [w:] the coherent world before the read.
*)
Theorem coherent_add self s w :
  coherent w -> exists w', sources_add self s w = Some w' /\ coherent w'.
Proof.
  intros Hm. unfold sources_add.
  destruct (decide (fst <$> source_slots (sources_of w self) !! source_cursor (sources_of w self) = Some s)) as [Hp|Hp].
  - eexists. split; [reflexivity|]. intros c i s' h.
    rewrite slot_at_advance, observers_of_set_sources. apply Hm.
  - destruct (decide (0 < source_cursor (sources_of w self) /\
                      fst <$> source_slots (sources_of w self) !! pred (source_cursor (sources_of w self)) = Some s));
      [by eexists|].
    destruct (sources_end_run_ok self w Hm) as (w1 & -> & Hm1 & _). simpl.
    eexists. split; [reflexivity|]. by apply edge_match_link.
Qed.

(**md
**Ending a run succeeds and keeps the two ends of every edge in agreement.**

`EndRun` on a coherent world returns a world, and that world is coherent.

- [self:] the computation ending its run.
- [w:] the coherent world before the end.
*)
Theorem coherent_end self w :
  coherent w -> exists w', sources_end_run self w = Some w' /\ coherent w'.
Proof. intros Hm. destruct (sources_end_run_ok self w Hm) as (w' & Hr & Hm' & _). eauto. Qed.

(**md
**Clearing a source list succeeds and keeps the two ends of every edge in agreement.**

`Clear` on a coherent world returns a world, and that world is coherent.

- [self:] the computation cleared.
- [w:] the coherent world before the clear.
*)
Theorem coherent_clear self w :
  coherent w -> exists w', sources_clear self w = Some w' /\ coherent w'.
Proof. intros Hm. destruct (sources_clear_ok self w Hm) as (w' & Hr & Hm' & _). eauto. Qed.

(**md
**Removing a slot's edge keeps coherence, and the moved observer takes the removed handle.**

The result is the removal, the blanked slot and, when the removal moved another observer, that observer's `MovedIn`. The moved observer's slot holds the source with the removed handle.

Premises: a coherent world, and a slot holding the source with this handle.

- [self:] the computation whose slot is removed.
- [i:] the slot removed.
- [s:] the source in slot `i`.
- [h:] the handle in slot `i`.
- [w:] the coherent world before the removal.
*)
Theorem coherent_moved self i s h w :
  coherent w ->
  slot_at w self i = Some (s, Some h) ->
  exists w', sources_remove_slot self i w = Some w' /\ coherent w' /\
    (forall o',
       observers_remove_at self h (observers_of w s) = Some (o', None) ->
       w' = set_handle self i None (set_observers s o' w)) /\
    (forall o' m ms h',
       observers_remove_at self h (observers_of w s) = Some (o', Some (m, ms, h')) ->
       h' = h /\
       w' = set_handle self i None (sources_moved_in m ms h' (set_observers s o' w)) /\
       slot_at w' m ms = Some (s, Some h')).
Proof.
  intros Hm Hs.
  destruct (remove_step self i s h w Hm Hs) as (w' & Hr & Hm' & _).
  exists w'. split_and!; [done|done|..].
  - intros o' Hrm.
    unfold sources_remove_slot, remove_edge in Hr. rewrite Hs, Hrm in Hr. simpl in Hr. by simplify_eq.
  - intros o' m ms h' Hrm.
    unfold sources_remove_slot, remove_edge in Hr. rewrite Hs, Hrm in Hr. simpl in Hr. simplify_eq.
    destruct (observers_remove_at_spec _ _ _ _ _ Hrm)
      as (_ & _ & _ & [[_ ?]|(m' & ms' & _ & _ & Hn & Hhole)]); [done|].
    simplify_eq.
    split_and!; [done|done|].
    apply Hm'. unfold sources_moved_in. rewrite !observers_of_set_handle, observers_of_set_observers, decide_True by done.
    done.
Qed.

(** ** A partial update breaks that agreement *)

(**md
**A removal without blanking the removed slot breaks coherence.**

*Check, not a guarantee.*

From a coherent world, removing the slot's observer entry and leaving the slot's handle in place gives a world that is not coherent. This marks a step the engine has to pair with another.

- [self:] the computation whose slot holds the edge.
- [i:] the slot holding the edge.
- [s:] the source of the edge.
- [h:] the handle of the edge.
- [w:] the coherent world before the removal.
- [w1:] the world after `remove_edge`.
*)
Theorem remove_edge_alone_breaks_coherent self i s h w w1 :
  coherent w ->
  slot_at w self i = Some (s, Some h) ->
  remove_edge self s h w = Some w1 ->
  ~ coherent w1.
Proof.
  intros Hm Hs Hr Hm1. unfold remove_edge in Hr.
  destruct (observers_remove_at self h (observers_of w s)) as [[o' n]|] eqn:Er; [|done].
  destruct (observers_remove_at_spec _ _ _ _ _ Er) as (_ & _ & Hgone & Hnotice).
  destruct Hnotice as [[Hl ->]|(m & ms & Hhl & Hlast & -> & Hhole)]; simpl in Hr; injection Hr as <-.
  - assert (Hq : observer_items (observers_of (set_observers s o' w) s) !! h = Some (self, i)).
    { apply Hm1. rewrite slot_at_set_observers. done. }
    rewrite observers_of_set_observers, decide_True in Hq by done.
    rewrite Hgone in Hq by lia. done.
  - apply Hm in Hlast.
    assert (Hne : ~ (self = m /\ i = ms)).
    { intros [-> ->]. rewrite Hs in Hlast. simplify_eq. lia. }
    assert (Hq : observer_items (observers_of (sources_moved_in m ms h (set_observers s o' w)) s) !! h = Some (self, i)).
    { apply Hm1. unfold sources_moved_in. rewrite slot_at_set_handle, decide_False by done.
      rewrite slot_at_set_observers. done. }
    unfold sources_moved_in in Hq. rewrite observers_of_set_handle, observers_of_set_observers, decide_True in Hq by done.
    rewrite Hhole in Hq. simplify_eq. tauto.
Qed.

(**md
**A move to a different handle without a removal breaks coherence.**

*Check, not a guarantee.*

From a coherent world, giving a slot a different handle and leaving the observer set in place gives a world that is not coherent. This marks a step the engine has to pair with another.

- [c:] the computation whose slot changes.
- [slot:] the slot changed.
- [s:] the source in the slot.
- [h:] the slot's handle before the change.
- [h':] the handle the slot is given, different from `h`.
- [w:] the coherent world before the change.
*)
Theorem moved_alone_breaks_coherent c slot s h h' w :
  coherent w ->
  slot_at w c slot = Some (s, Some h) ->
  h <> h' ->
  ~ coherent (sources_moved_in c slot h' w).
Proof.
  intros Hm Hs Hne Hm'.
  assert (Hmoved : slot_at (sources_moved_in c slot h' w) c slot = Some (s, Some h')).
  { unfold sources_moved_in. rewrite slot_at_set_handle, decide_True by done. rewrite Hs. done. }
  apply Hm' in Hmoved. unfold sources_moved_in in Hmoved. rewrite observers_of_set_handle in Hmoved.
  apply Hm in Hmoved. rewrite Hs in Hmoved. simplify_eq.
Qed.

(** ** What an edge means *)

(**md
**In a coherent world, either side of an edge implies the other.**

`c` observes `src` exactly when `c` is in `src`'s observers, and exactly when a slot of `c` holds `src` with a handle.

- [s:] the state, whose edges are coherent.
- [src:] the source.
- [c:] the observer.
*)
Theorem edge_iff_both_sides s src c :
  coherent (g_edges s) ->
  (edge s src c <-> c ∈ observer_view (observers_of (g_edges s) src)) /\
  (edge s src c <-> exists i h, slot_at (g_edges s) c i = Some (src, Some h)).
Proof.
  intros Hm. unfold edge. split.
  - rewrite view_elem. split.
    + intros (i & h & _ & Hp). eauto.
    + intros (h & i & Hp). exists i, h. split; [by apply Hm|done].
  - split.
    + intros (i & h & Hs & _). eauto.
    + intros (i & h & Hs). exists i, h. split; [done|by apply Hm].
Qed.

(**md
**In a well-formed world, a computation observes exactly the sources in its slots.**

A world is well formed when the two ends of every edge agree and a slot holds an edge exactly when no earlier slot of the computation names its source. Every source in a slot has an edge, whichever slot holds the handle.

- [s:] the state, whose edges are well formed.
- [src:] the source.
- [c:] the observer.
*)
Theorem edge_iff_read s src c :
  world_well_formed (g_edges s) ->
  edge s src c <-> src ∈ fst <$> source_slots (sources_of (g_edges s) c).
Proof.
  intros Hw. rewrite (proj1 (edge_iff_both_sides s src c (proj1 Hw))). by apply held_iff_slot.
Qed.

(** A coherent world where [1] has read [5] with no edge to it. [first_owns] excludes this world. *)
Example read_without_edge (w := set_sources 1 {| source_slots := [(5, None)]; source_cursor := 1 |} empty_world) :
  coherent w /\ 5 ∈ fst <$> source_slots (sources_of w 1) /\ ~ first_owns w 1 /\
  forall s, g_edges s = w -> ~ edge s 5 1.
Proof.
  assert (Hslot : slot_at w 1 0 = Some (5, None)) by (subst w; vm_compute; reflexivity).
  assert (Hm : coherent w).
  { intros c i s h. subst w. rewrite slot_at_set_sources, observers_of_set_sources. split.
    - destruct (decide (c = 1)) as [->|Hc].
      + destruct i as [|i]; simpl; intros Heq; simplify_eq.
      + intros Heq. by apply coherent_empty in Heq.
    - intros Hq. apply coherent_empty in Hq. unfold slot_at, sources_of in Hq. simpl in Hq.
      rewrite lookup_empty in Hq. simpl in Hq. rewrite lookup_nil in Hq. done. }
  split_and!; [done| |..].
  - subst w. vm_compute. left.
  - intros Hf. destruct (proj2 (Hf 0 5 None Hslot) ltac:(intros; lia)) as [? ?]. done.
  - intros s Hs (i & h & Hi & _). rewrite Hs in Hi.
    destruct i as [|i]; [rewrite Hslot in Hi; done|].
    subst w. unfold slot_at in Hi. rewrite sources_of_set_sources, decide_True in Hi by done.
    simpl in Hi. rewrite lookup_nil in Hi. done.
Qed.
