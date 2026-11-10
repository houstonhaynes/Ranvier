From stdpp Require Import prelude.
From Ranvier.Base Require Import Prelude.

(** [EdgeHandle.ScanLimit]. *)
Definition scan_limit : nat := 8.

(** An observer set. [observer_items] holds each observer with the slot passed back in its [MovedIn], and a
    position is an edge's handle. *)
Record observer_set := { observer_items : list (node_id * nat); observer_latest : nat }.

Definition empty_observer_set : observer_set := {| observer_items := []; observer_latest := 0 |}.

(** [TUnlinked] and [TUnknown] are [EdgeHandle.Unlinked] and [EdgeHandle.Unknown]. *)
Inductive tried := TAdded (h : nat) | TUnlinked | TUnknown.

(**md
The observers of `o` in position order, without their slots.
*)
Definition observer_view (o : observer_set) : list node_id := fst <$> observer_items o.

(**md
Appends the observer `c` and returns the new set with the handle of the new position, the old observer count.

- [c:] the observing computation.
- [slot:] the observer's slot, kept for the `MovedIn` notice of a later move.
- [o:] the set added to.
*)
Definition observers_add (c : node_id) (slot : nat) (o : observer_set) : observer_set * nat :=
  let h := length (observer_items o) in
  ({| observer_items := observer_items o ++ [(c, slot)]; observer_latest := h |}, h).

(**md
Adds `c` unless the set already holds it. The answer is `TAdded` with the new handle, `TUnlinked` when `c`
is already held, or `TUnknown` when the set exceeds `scan_limit` and `c` is not the latest observer, so
membership is not decided.

- [c:] the observing computation.
- [slot:] the observer's slot.
- [o:] the set added to.
*)
Definition observers_try_add (c : node_id) (slot : nat) (o : observer_set) : observer_set * tried :=
  let add := fun _ : unit => let '(o', h) := observers_add c slot o in (o', TAdded h) in
  if decide (length (observer_items o) = 0) then add ()
  else if decide (fst <$> observer_items o !! observer_latest o = Some c) then (o, TUnlinked)
  else if decide (length (observer_items o) <= scan_limit) then
    if decide (c ∈ observer_view o) then (o, TUnlinked) else add ()
  else (o, TUnknown).

(**md
Swap-removal. The result's notice is (observer, slot, new handle) for the observer moved. `None`
as the whole result is the `InvalidOperationException` of a handle holding another observer.

- [c:] the observer removed.
- [h:] the handle, which has to be the position of `c`.
- [o:] the set removed from.
*)
Definition observers_remove_at (c : node_id) (h : nat) (o : observer_set) : option (observer_set * option (node_id * nat * nat)) :=
  let last := pred (length (observer_items o)) in
  match observer_items o !! h with
  | Some (c', _) =>
    if decide (c' = c) then
      if decide (h = last) then
        Some ({| observer_items := take last (observer_items o); observer_latest := observer_latest o |}, None)
      else
        match observer_items o !! last with
        | Some (m, ms) =>
          Some ({| observer_items := <[h := (m, ms)]> (take last (observer_items o));
                   observer_latest := if decide (observer_latest o = last) then h else observer_latest o |},
                Some (m, ms, h))
        | None => None
        end
    else None
  | None => None
  end.

(** A mutation of the observer set between two visits of a walk. *)
Inductive walk_mutation := WAdd (c : node_id) (slot : nat) | WRemove (c : node_id) (h : nat).

(**md
The set after the mutation `m`, or `None` when a removal fails.
*)
Definition apply_mutation (m : walk_mutation) (o : observer_set) : option observer_set :=
  match m with
  | WAdd c slot => Some (observers_add c slot o).1
  | WRemove c h => fst <$> observers_remove_at c h o
  end.

(**md
The set after the mutations `ms` in order, or `None` when one fails.
*)
Fixpoint apply_mutations (ms : list walk_mutation) (o : observer_set) : option observer_set :=
  match ms with
  | [] => Some o
  | m :: r => apply_mutation m o ≫= apply_mutations r
  end.

(**md
The walk of `NotifyDirty`, `NotifyDirtyExcept` and `NotifyCheck`. The first argument is the
cursor plus one, and starts as the observer count. Each step re-clamps it to the count, visits the
observer below it, then applies that visit's mutations.

`walk n o muts vis o'` relates the cursor `n` and the starting set `o` to the mutations applied at each
visit (`muts`), the observers visited in order (`vis`) and the final set `o'`.
*)
Inductive walk : nat -> observer_set -> list (list walk_mutation) -> list node_id -> observer_set -> Prop :=
  | walk_end n o :
      min n (length (observer_items o)) = 0 ->
      walk n o [] [] o
  | walk_visit n i o c slot ms o1 rest vis o' :
      min n (length (observer_items o)) = S i ->
      observer_items o !! i = Some (c, slot) ->
      apply_mutations ms o = Some o1 ->
      walk i o1 rest vis o' ->
      walk n o (ms :: rest) (c :: vis) o'.

(** The observer set at every point of a mutation sequence: the start, then after each mutation. *)
Fixpoint mutation_points (ms : list walk_mutation) (o : observer_set) : list observer_set :=
  o :: match ms with
       | [] => []
       | m :: r =>
         match apply_mutation m o with
         | Some o1 => mutation_points r o1
         | None => []
         end
       end.

(**md
`c` occupies exactly one position of `o`.

- [c:] the observer.
- [o:] the observer set.
*)
Definition held_once (c : node_id) (o : observer_set) : Prop :=
  c ∈ observer_view o /\ forall i j, observer_view o !! i = Some c -> observer_view o !! j = Some c -> i = j.

Lemma view_lookup o i c : observer_view o !! i = Some c <-> exists s, observer_items o !! i = Some (c, s).
Proof.
  unfold observer_view. rewrite list_lookup_fmap. split.
  - destruct (observer_items o !! i) as [[c' s]|]; simpl; intros H; simplify_eq; eauto.
  - intros [s ->]. reflexivity.
Qed.

Lemma view_elem o c : c ∈ observer_view o <-> exists i s, observer_items o !! i = Some (c, s).
Proof.
  rewrite list_elem_of_lookup. split.
  - intros [i Hi]. apply view_lookup in Hi as [s Hs]. eauto.
  - intros (i & s & Hs). exists i. apply view_lookup. eauto.
Qed.

(** ** Adding an observer *)

(**md
**Adding an observer appends it to the end of the set.**

The returned handle is the old observer count, and that position now holds the observer with its slot. The visible order is the old order followed by the new observer, and `observer_latest` holds the new handle. Holds for every set.

- [c:] the observer added.
- [slot:] the slot recorded with the observer.
- [o:] the set added to.
*)
Theorem observers_add_position c slot o :
  (observers_add c slot o).2 = length (observer_items o) /\
  observer_items (observers_add c slot o).1 !! length (observer_items o) = Some (c, slot) /\
  observer_view (observers_add c slot o).1 = observer_view o ++ [c] /\
  observer_latest (observers_add c slot o).1 = length (observer_items o).
Proof.
  unfold observers_add, observer_view. simpl. rewrite fmap_app. split_and!; try done.
  rewrite lookup_app_r by lia. rewrite Nat.sub_diag. done.
Qed.

(** Every answer of [observers_try_add], with its effect. *)
Lemma observers_try_add_spec c slot o :
  (observers_try_add c slot o = (o, TUnlinked) /\ c ∈ observer_view o) \/
  (exists h, observers_try_add c slot o = ((observers_add c slot o).1, TAdded h) /\
             h = (observers_add c slot o).2 /\ c ∉ observer_view o) \/
  (observers_try_add c slot o = (o, TUnknown) /\ scan_limit < length (observer_items o) /\
   fst <$> observer_items o !! observer_latest o <> Some c).
Proof.
  unfold observers_try_add. destruct (decide (length (observer_items o) = 0)) as [H0|H0].
  { right; left. eexists. split_and!; [reflexivity|reflexivity|].
    rewrite view_elem. intros (i & s & Hi). apply lookup_lt_Some in Hi. lia. }
  destruct (decide (fst <$> observer_items o !! observer_latest o = Some c)) as [Hl|Hl].
  { left. split; [done|]. rewrite view_elem.
    destruct (observer_items o !! observer_latest o) as [[c' s]|] eqn:E; simplify_eq/=. eauto. }
  destruct (decide (length (observer_items o) <= scan_limit)) as [Hs|Hs].
  - destruct (decide (c ∈ observer_view o)) as [Hc|Hc]; [by left|].
    right; left. eexists. split_and!; [reflexivity|reflexivity|done].
  - right; right. split_and!; [done|lia|done].
Qed.

(**md
**A small set always knows whether it holds an observer.**

With at most `scan_limit` observers, `TryAdd` answers `TAdded` or `TUnlinked`, and `TUnlinked` exactly when the observer is already in the set.

- [c:] the observer offered.
- [slot:] the slot recorded if `c` is added.
- [o:] the set, which holds at most `scan_limit` observers.
*)
Theorem observers_try_add_cases c slot o :
  length (observer_items o) <= scan_limit ->
  (observers_try_add c slot o).2 <> TUnknown /\
  ((observers_try_add c slot o).2 = TUnlinked <-> c ∈ observer_view o).
Proof.
  intros Hlen.
  destruct (observers_try_add_spec c slot o) as [[-> Hc]|[(h & -> & _ & Hc)|(_ & Hb & _)]]; simpl.
  - split; [done|tauto].
  - split; [done|]. split; [done|tauto].
  - unfold scan_limit in *. lia.
Qed.

(** ** Removing an observer *)

(**md
**A handle that holds another observer fails the removal.**

When the handle's position is out of range or holds a different observer, `RemoveAt` returns `None`, the model's `InvalidOperationException`.

- [c:] the observer removed.
- [h:] the handle, which does not point at `c`.
- [o:] the set removed from.
*)
Theorem observers_remove_mismatch c h o :
  fst <$> observer_items o !! h <> Some c -> observers_remove_at c h o = None.
Proof.
  unfold observers_remove_at. destruct (observer_items o !! h) as [[c' s]|]; simpl; intros Hne; [|done].
  rewrite decide_False; [done|]. congruence.
Qed.

(**md
**Removal fills the hole with the last observer and reports the move.**

Removing from a middle position moves the last observer there. The result names that observer, its slot and its new handle, and `observer_latest` follows it if it pointed at the moved observer. Removing the last observer shortens the set and reports no move. The handle has to be the observer's actual position.

- [c:] the observer removed, at position `length pre`.
- [s:] the slot of `c`.
- [m:] the last observer, which moves into the hole.
- [ms:] the slot of `m`.
- [pre:] the observers before `c`.
- [mid:] the observers between `c` and `m`.
- [latest:] the value of `observer_latest`.
*)
Theorem observers_remove_at_swap c s m ms pre mid latest :
  observers_remove_at c (length pre)
    {| observer_items := pre ++ (c, s) :: mid ++ [(m, ms)]; observer_latest := latest |} =
    Some ({| observer_items := pre ++ (m, ms) :: mid;
             observer_latest := if decide (latest = length pre + S (length mid))
                        then length pre else latest |},
          Some (m, ms, length pre)) /\
  observers_remove_at c (length pre) {| observer_items := pre ++ [(c, s)]; observer_latest := latest |} =
    Some ({| observer_items := pre; observer_latest := latest |}, None).
Proof.
  split.
  - unfold observers_remove_at. simpl.
    assert (Hlen : pred (length (pre ++ (c, s) :: mid ++ [(m, ms)])) = length pre + S (length mid)).
    { rewrite !length_app. simpl. rewrite length_app. simpl. lia. }
    rewrite Hlen.
    rewrite lookup_app_r, Nat.sub_diag by lia. simpl.
    rewrite decide_True by done. rewrite decide_False by lia.
    rewrite lookup_app_r by lia.
    replace (length pre + S (length mid) - length pre) with (S (length mid)) by lia. simpl.
    rewrite lookup_app_r, Nat.sub_diag by lia. simpl.
    assert (Htake : take (length pre + S (length mid)) (pre ++ (c, s) :: mid ++ [(m, ms)]) =
                    pre ++ (c, s) :: mid).
    { replace (pre ++ (c, s) :: mid ++ [(m, ms)]) with ((pre ++ (c, s) :: mid) ++ [(m, ms)])
        by (rewrite <- app_assoc; reflexivity).
      apply take_app_length'. rewrite length_app. simpl. lia. }
    rewrite Htake.
    rewrite insert_app_r_alt, Nat.sub_diag by lia. reflexivity.
  - unfold observers_remove_at. simpl.
    rewrite length_app. simpl. replace (pred (length pre + 1)) with (length pre) by lia.
    rewrite lookup_app_r, Nat.sub_diag by lia. simpl.
    rewrite decide_True by done. rewrite decide_True by done.
    rewrite take_app_length. reflexivity.
Qed.

(** A successful removal, by position. [last] is the position of the last observer before it. *)
Lemma observers_remove_at_spec c h o o' n (last := pred (length (observer_items o))) :
  observers_remove_at c h o = Some (o', n) ->
  (exists s, observer_items o !! h = Some (c, s)) /\
  (forall q, q <> h -> q < last -> observer_items o' !! q = observer_items o !! q) /\
  (forall q, last <= q -> observer_items o' !! q = None) /\
  ((h = last /\ n = None) \/
   (exists m ms, h < last /\ observer_items o !! last = Some (m, ms) /\ n = Some (m, ms, h) /\
                 observer_items o' !! h = Some (m, ms))).
Proof.
  unfold observers_remove_at. fold last.
  destruct (observer_items o !! h) as [[c' s]|] eqn:Eh; [|done].
  pose proof (lookup_lt_Some _ _ _ Eh) as Hlt.
  assert (Hlen : h <= last) by (subst last; lia).
  destruct (decide (c' = c)) as [->|]; [|done].
  destruct (decide (h = last)) as [->|Hne].
  - intros [= <- <-]. split_and!; [eauto|..]; simpl.
    + intros q _ Hq. by rewrite lookup_take_lt.
    + intros q Hq. by rewrite lookup_take_ge.
    + left. done.
  - destruct (observer_items o !! last) as [[m ms]|] eqn:El; [|done].
    assert (Hhl : h < last) by lia.
    assert (Htl : length (take last (observer_items o)) = last).
    { apply length_take_le. subst last; lia. }
    intros [= <- <-]. split_and!; [eauto|..]; simpl.
    + intros q Hq Hql. rewrite list_lookup_insert_ne by done. by rewrite lookup_take_lt.
    + intros q Hq. rewrite list_lookup_insert_ne by lia. by rewrite lookup_take_ge.
    + right. eexists _, _. split_and!; [done|done|done|].
      rewrite list_lookup_insert_eq by lia. done.
Qed.

(** ** Notification walks reach their observers *)

Lemma apply_mutations_app ms1 ms2 o :
  apply_mutations (ms1 ++ ms2) o = apply_mutations ms1 o ≫= apply_mutations ms2.
Proof.
  revert o. induction ms1 as [|m r IH]; intros o; simpl; [done|].
  destruct (apply_mutation m o) as [o1|]; simpl; [apply IH|done].
Qed.

Lemma mutation_points_app (P : observer_set -> Prop) ms r o o1 :
  apply_mutations ms o = Some o1 ->
  Forall P (mutation_points (ms ++ r) o) ->
  Forall P (mutation_points ms o) /\ Forall P (mutation_points r o1).
Proof.
  revert o. induction ms as [|m ms' IH]; intros o Ha HP; simpl in *.
  - simplify_eq. split; [|done]. constructor; [|constructor].
    destruct r; simpl in HP; inversion HP; done.
  - destruct (apply_mutation m o) as [o2|] eqn:E; simpl in *; [|done].
    inversion HP as [|?? Ho HPt]; subst. destruct (IH _ Ha HPt) as [Hm Hr].
    split; [constructor; done|done].
Qed.

(** A held-once observer that stays present never moves to a higher position. *)
Lemma mutation_keeps_below m o o1 c p :
  apply_mutation m o = Some o1 ->
  held_once c o ->
  c ∈ observer_view o1 ->
  observer_view o !! p = Some c ->
  exists p', p' <= p /\ observer_view o1 !! p' = Some c.
Proof.
  intros Hm [_ Hu] Hin1 Hp. destruct m as [d slot|d h]; simpl in Hm.
  - simplify_eq. exists p. split; [done|].
    apply view_lookup in Hp as [s Hs]. apply view_lookup. exists s.
    apply lookup_app_l_Some. done.
  - destruct (observers_remove_at d h o) as [[o2 n]|] eqn:Er; simpl in Hm; [|done]. simplify_eq.
    destruct (observers_remove_at_spec _ _ _ _ _ Er) as (_ & Hsame & Hgone & Hnotice).
    apply view_elem in Hin1 as (q & s & Hq).
    destruct (decide (q = h)) as [->|Hqh].
    + destruct Hnotice as [[-> ->]|(m' & ms & Hhl & Hl & -> & Hh)].
      * rewrite Hgone in Hq; [done|lia].
      * rewrite Hh in Hq. simplify_eq.
        assert (pred (length (observer_items o)) = p) as <-.
        { apply Hu; [apply view_lookup; eauto|done]. }
        exists h. split; [lia|]. apply view_lookup. eauto.
    + destruct (decide (q < pred (length (observer_items o)))) as [Hql|Hql].
      * rewrite Hsame in Hq by done.
        assert (q = p) as <-.
        { apply Hu; [apply view_lookup; eauto|done]. }
        exists q. split; [done|]. apply view_lookup. exists s.
        rewrite Hsame by done. done.
      * rewrite Hgone in Hq; [done|lia].
Qed.

Lemma mutations_keep_below ms o o1 c p :
  apply_mutations ms o = Some o1 ->
  Forall (held_once c) (mutation_points ms o) ->
  observer_view o !! p = Some c ->
  exists p', p' <= p /\ observer_view o1 !! p' = Some c.
Proof.
  revert o p. induction ms as [|m r IH]; intros o p Ha HP Hp; simpl in *.
  - simplify_eq. eauto.
  - destruct (apply_mutation m o) as [o2|] eqn:E; simpl in *; [|done].
    inversion HP as [|?? Ho HP']; subst.
    destruct r as [|m' r'] eqn:Er.
    all: assert (Ho2 : held_once c o2)
      by (subst; simpl in HP'; inversion HP'; done).
    all: destruct (mutation_keeps_below _ _ _ _ _ E Ho (proj1 Ho2) Hp) as (p1 & Hle & Hp1).
    all: destruct (IH _ _ Ha HP' Hp1) as (p2 & Hle2 & Hp2).
    all: exists p2; split; [lia|done].
Qed.

Lemma walk_covers_from n o muts vis o' c p :
  walk n o muts vis o' ->
  Forall (held_once c) (mutation_points (concat muts) o) ->
  observer_view o !! p = Some c ->
  p < n ->
  c ∈ vis.
Proof.
  intros Hw. revert p.
  induction Hw as [n o Hmin|n i o c0 slot ms o1 rest vis o' Hmin Hi Ha Hw IH];
    intros p HP Hp Hpn.
  - apply view_lookup in Hp as [s Hs]. apply lookup_lt_Some in Hs. lia.
  - apply view_lookup in Hp as [s Hs]. pose proof (lookup_lt_Some _ _ _ Hs) as Hplt.
    destruct (decide (p = i)) as [->|Hne].
    { rewrite Hi in Hs. simplify_eq. constructor. }
    constructor. simpl in HP.
    destruct (mutation_points_app _ _ _ _ _ Ha HP) as [H1 H2].
    destruct (mutations_keep_below _ _ _ _ _ Ha H1 (proj2 (view_lookup _ _ _) ltac:(eauto)))
      as (p' & Hle & Hp').
    eapply IH; [done|done|lia].
Qed.

(**md
**A notification walk visits every observer held exactly once throughout.**

The set can change between visits. An observer in exactly one position at the start, and after every mutation of the walk, appears among the visits at least once. The walk starts from the full observer count.

An observer held twice can be missed, as [`walk_covers_duplicate_observer`](Ranvier.Pure.Witnesses.html#walk_covers_duplicate_observer) shows. A moved observer can be visited twice, as [`walk_visits_moved_twice`](Ranvier.Pure.Witnesses.html#walk_visits_moved_twice) shows.

- [o:] the observer set at the start of the walk.
- [muts:] the mutations applied after each visit.
- [vis:] the observers visited, in order.
- [o':] the observer set at the end of the walk.
- [c:] the observer that is held once throughout.
*)
Theorem walk_covers o muts vis o' c :
  walk (length (observer_items o)) o muts vis o' ->
  Forall (held_once c) (mutation_points (concat muts) o) ->
  c ∈ vis.
Proof.
  intros Hw HP.
  assert (Ho : held_once c o).
  { destruct (concat muts) as [|m r]; simpl in HP; inversion HP; done. }
  destruct Ho as [Hin _]. apply list_elem_of_lookup in Hin as [p Hp].
  eapply walk_covers_from; [done|done|done|].
  apply view_lookup in Hp as [s Hs]. by eapply lookup_lt_Some.
Qed.

(**md
**A walk visits only observers present at the time of the visit.**

The `k`th observer visited belongs to the set as it stands after the mutations of the `k` earlier visits. Holds from any starting cursor. An observer added during the walk can be visited: see [`walk_visits_added_observer`](Ranvier.Pure.Witnesses.html#walk_visits_added_observer).

- [n:] the starting cursor plus one.
- [o:] the observer set at the start of the walk.
- [muts:] the mutations applied after each visit.
- [vis:] the observers visited, in order.
- [o':] the observer set at the end of the walk.
- [k:] the index of a visit.
*)
Theorem walk_visits_only_seen n o muts vis o' :
  walk n o muts vis o' ->
  forall k c, vis !! k = Some c ->
  exists ok, apply_mutations (concat (take k muts)) o = Some ok /\ c ∈ observer_view ok.
Proof.
  intros Hw. induction Hw as [|n i o c0 slot ms o1 rest vis o' Hmin Hi Ha Hw IH];
    intros k c Hk; [done|].
  destruct k as [|k]; simpl in Hk.
  - simplify_eq. exists o. split; [done|]. apply view_elem. eauto.
  - destruct (IH _ _ Hk) as (ok & Hok & Hc). exists ok. split; [|done].
    simpl. rewrite apply_mutations_app, Ha. done.
Qed.
