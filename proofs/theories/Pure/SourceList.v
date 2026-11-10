From stdpp Require Import prelude gmap.
From Ranvier.Base Require Import Prelude.
From Ranvier.Pure Require Import ObserverSet.

(** A computation's source list. A slot holds a source and its edge handle, [None] for
    [EdgeHandle.Unlinked]. *)
Record source_list := { source_slots : list (node_id * option nat); source_cursor : nat }.

(** Both ends of every edge. A node absent from a map has an empty set or list. *)
Record EdgeWorld := { world_observers : gmap node_id observer_set; world_sources : gmap node_id source_list }.

Definition empty_source_list : source_list := {| source_slots := []; source_cursor := 0 |}.
Definition empty_world : EdgeWorld := {| world_observers := ∅; world_sources := ∅ |}.

(**md
The observers of `s` in `w`, empty when `s` has none stored.
*)
Definition observers_of (w : EdgeWorld) (s : node_id) : observer_set := default empty_observer_set (world_observers w !! s).
(**md
The source list of `c` in `w`, empty when `c` has none stored.
*)
Definition sources_of (w : EdgeWorld) (c : node_id) : source_list := default empty_source_list (world_sources w !! c).
(**md
Slot `i` of computation `c` in `w`: its source and edge handle, or `None` past the end of the list.

- [w:] the edge world.
- [c:] the computation.
- [i:] the slot index.
*)
Definition slot_at (w : EdgeWorld) (c : node_id) (i : nat) : option (node_id * option nat) :=
  source_slots (sources_of w c) !! i.

(**md
`w` with the observer set of `s` replaced by `o`.
*)
Definition set_observers (s : node_id) (o : observer_set) (w : EdgeWorld) : EdgeWorld :=
  {| world_observers := <[s := o]> (world_observers w); world_sources := world_sources w |}.
(**md
`w` with the source list of `c` replaced by `l`.
*)
Definition set_sources (c : node_id) (l : source_list) (w : EdgeWorld) : EdgeWorld :=
  {| world_observers := world_observers w; world_sources := <[c := l]> (world_sources w) |}.

(**md
`BeginRun`: `w` with the cursor of `c` moved to the start of its slots.
*)
Definition sources_begin_run (c : node_id) (w : EdgeWorld) : EdgeWorld :=
  set_sources c {| source_slots := source_slots (sources_of w c); source_cursor := 0 |} w.

(**md
`w` with the edge handle of slot `i` of `c` replaced by `mh`.

- [c:] the computation.
- [i:] the slot index.
- [mh:] the new handle, `None` for an unlinked slot.
*)
Definition set_handle (c : node_id) (i : nat) (mh : option nat) (w : EdgeWorld) : EdgeWorld :=
  let l := sources_of w c in
  set_sources c {| source_slots := alter (fun p => (p.1, mh)) i (source_slots l); source_cursor := source_cursor l |} w.

(**md
`MovedIn`: gives slot `slot` of `c` the handle `h`.
*)
Definition sources_moved_in (c : node_id) (slot h : nat) (w : EdgeWorld) : EdgeWorld :=
  set_handle c slot (Some h) w.

(**md
`RemoveObserverAt`, with the `MovedIn` of the observer it moves.

- [self:] the observer removed.
- [s:] the source whose observers are changed.
- [h:] the handle of `self` in the observers of `s`.
*)
Definition remove_edge (self s : node_id) (h : nat) (w : EdgeWorld) : option EdgeWorld :=
  match observers_remove_at self h (observers_of w s) with
  | Some (o', n) =>
    let w1 := set_observers s o' w in
    Some match n with
         | None => w1
         | Some (m, ms, h') => sources_moved_in m ms h' w1
         end
  | None => None
  end.

(**md
One iteration of `trimFrom`. The handle comes from the world current at this slot.

- [self:] the computation trimmed.
- [i:] the slot removed.
*)
Definition sources_remove_slot (self : node_id) (i : nat) (w : EdgeWorld) : option EdgeWorld :=
  match slot_at w self i with
  | Some (s, Some h) => set_handle self i None <$> remove_edge self s h w
  | _ => Some w
  end.

(**md
Removes `n` slots of `self` from slot `i` on, in order, by `sources_remove_slot`.
*)
Fixpoint trim_loop (self : node_id) (i n : nat) (w : EdgeWorld) : option EdgeWorld :=
  match n with
  | 0 => Some w
  | S n' => sources_remove_slot self i w ≫= trim_loop self (S i) n'
  end.

(**md
`trimFrom`: removes the edges of the slots of `self` from `from` on, then drops those slots.

- [self:] the computation trimmed.
- [from:] the first slot dropped.
*)
Definition sources_trim_from (self : node_id) (from : nat) (w : EdgeWorld) : option EdgeWorld :=
  trim_loop self from (length (source_slots (sources_of w self)) - from) w ≫= fun w1 =>
  let l1 := sources_of w1 self in
  Some (set_sources self {| source_slots := take from (source_slots l1); source_cursor := source_cursor l1 |} w1).

(**md
`EndRun`: trims the slots at and after the cursor of `self`.
*)
Definition sources_end_run (self : node_id) (w : EdgeWorld) : option EdgeWorld :=
  let l := sources_of w self in
  if decide (source_cursor l < length (source_slots l)) then sources_trim_from self (source_cursor l) w else Some w.

(**md
`Clear`: removes every edge of `self` and empties its slot list, with the cursor at the start.
*)
Definition sources_clear (self : node_id) (w : EdgeWorld) : option EdgeWorld :=
  sources_trim_from self 0 w ≫= fun w1 =>
  Some (set_sources self {| source_slots := source_slots (sources_of w1 self); source_cursor := 0 |} w1).

(**md
Appends slot `(s, mh)` to `self` with the cursor after it, and stores `o` as the observers of `s`.

- [self:] the computation.
- [s:] the source appended.
- [mh:] the slot's edge handle.
- [o:] the observer set of `s` after the append.
*)
Definition sources_append (self s : node_id) (mh : option nat) (o : observer_set) (w : EdgeWorld) : EdgeWorld :=
  let l := sources_of w self in
  set_observers s o
    (set_sources self {| source_slots := source_slots l ++ [(s, mh)]; source_cursor := S (length (source_slots l)) |} w).

(** The slot and edge of [Append], after its trim. Slot 0 adds unconditionally. [SourceList.held] is
    abstracted to membership in the slots. *)
Definition sources_link (self s : node_id) (w : EdgeWorld) : EdgeWorld :=
  let l := sources_of w self in
  let slot := length (source_slots l) in
  let o := observers_of w s in
  let add := fun _ : unit => sources_append self s (Some (observers_add self slot o).2) (observers_add self slot o).1 w in
  if decide (slot = 0) then add ()
  else
    match observers_try_add self slot o with
    | (o', TAdded h) => sources_append self s (Some h) o' w
    | (o', TUnlinked) => sources_append self s None o' w
    | (o', TUnknown) =>
      if decide (s ∈ fst <$> source_slots l) then sources_append self s None o' w else add ()
    end.

(** [SourceList.Add]: a match at the cursor, a repeat of the source just matched, or a miss. *)
Definition sources_add (self s : node_id) (w : EdgeWorld) : option EdgeWorld :=
  let l := sources_of w self in
  let cur := source_cursor l in
  if decide (fst <$> source_slots l !! cur = Some s) then
    Some (set_sources self {| source_slots := source_slots l; source_cursor := S cur |} w)
  else if decide (0 < cur /\ fst <$> source_slots l !! pred cur = Some s) then Some w
  else sources_end_run self w ≫= fun w1 => Some (sources_link self s w1).

(** Slot [i] of [c] holds [(s, Some h)] exactly when position [h] of [s]'s observers holds [(c, i)]. *)
Definition edge_match (w : EdgeWorld) : Prop :=
  forall c i s h, slot_at w c i = Some (s, Some h) <-> observer_items (observers_of w s) !! h = Some (c, i).

(** A slot holds an edge exactly when it is its source's first slot. *)
Definition first_owns (w : EdgeWorld) (c : node_id) : Prop :=
  forall i s mh, slot_at w c i = Some (s, mh) ->
    (is_Some mh <-> forall j, j < i -> fst <$> slot_at w c j <> Some s).

Definition world_well_formed (w : EdgeWorld) : Prop := edge_match w /\ forall c, first_owns w c.

(** A source has at most one slot holding an edge, per computation. Unlike [first_owns], this holds
    between the removals of a trim. *)
Definition slots_unique (w : EdgeWorld) : Prop :=
  forall c s i j hi hj,
    slot_at w c i = Some (s, Some hi) -> slot_at w c j = Some (s, Some hj) -> i = j.

(** An observer occupies at most one position of each source's observers. *)
Definition observers_once (w : EdgeWorld) : Prop :=
  forall s c i j,
    observer_view (observers_of w s) !! i = Some c -> observer_view (observers_of w s) !! j = Some c -> i = j.

(** Every world the source-list operations build from nothing, in any interleaving per node. *)
Inductive world_reachable : EdgeWorld -> Prop :=
  | reachable_empty : world_reachable empty_world
  | reachable_begin c w : world_reachable w -> world_reachable (sources_begin_run c w)
  | reachable_add c s w w' : world_reachable w -> sources_add c s w = Some w' -> world_reachable w'
  | reachable_end c w w' : world_reachable w -> sources_end_run c w = Some w' -> world_reachable w'
  | reachable_clear c w w' : world_reachable w -> sources_clear c w = Some w' -> world_reachable w'.

(** ** Reading updated worlds *)

Lemma observers_of_set_observers s o w s' :
  observers_of (set_observers s o w) s' = if decide (s' = s) then o else observers_of w s'.
Proof.
  unfold observers_of, set_observers. simpl. destruct (decide (s' = s)) as [->|Hne].
  - by rewrite lookup_insert_eq.
  - by rewrite lookup_insert_ne by congruence.
Qed.

Lemma sources_of_set_sources c l w c' :
  sources_of (set_sources c l w) c' = if decide (c' = c) then l else sources_of w c'.
Proof.
  unfold sources_of, set_sources. simpl. destruct (decide (c' = c)) as [->|Hne].
  - by rewrite lookup_insert_eq.
  - by rewrite lookup_insert_ne by congruence.
Qed.

Lemma observers_of_set_sources c l w s : observers_of (set_sources c l w) s = observers_of w s.
Proof. reflexivity. Qed.

Lemma sources_of_set_observers s o w c : sources_of (set_observers s o w) c = sources_of w c.
Proof. reflexivity. Qed.

Lemma slot_at_set_observers s o w c i : slot_at (set_observers s o w) c i = slot_at w c i.
Proof. reflexivity. Qed.

Lemma slot_at_set_sources c l w c' i :
  slot_at (set_sources c l w) c' i = if decide (c' = c) then source_slots l !! i else slot_at w c' i.
Proof. unfold slot_at. rewrite sources_of_set_sources. by destruct (decide (c' = c)). Qed.

Lemma observers_of_set_handle c i mh w s : observers_of (set_handle c i mh w) s = observers_of w s.
Proof. reflexivity. Qed.

Lemma cursor_set_handle c i mh w c' :
  source_cursor (sources_of (set_handle c i mh w) c') = source_cursor (sources_of w c').
Proof.
  unfold set_handle. rewrite sources_of_set_sources. destruct (decide (c' = c)) as [->|]; done.
Qed.

Lemma slot_at_set_handle c i mh w c' j :
  slot_at (set_handle c i mh w) c' j =
    if decide (c' = c /\ j = i) then (fun p => (p.1, mh)) <$> slot_at w c i else slot_at w c' j.
Proof.
  unfold set_handle. rewrite slot_at_set_sources.
  destruct (decide (c' = c)) as [->|Hne]; simpl.
  - destruct (decide (c = c /\ j = i)) as [[_ ->]|Hne].
    + apply list_lookup_alter_eq.
    + apply list_lookup_alter_ne. intros ->. tauto.
  - rewrite decide_False by tauto. done.
Qed.

Lemma fst_set_handle c i mh w c' j :
  fst <$> slot_at (set_handle c i mh w) c' j = fst <$> slot_at w c' j.
Proof.
  rewrite slot_at_set_handle. destruct (decide (c' = c /\ j = i)) as [[-> ->]|]; [|done].
  by destruct (slot_at w c i).
Qed.

Definition owns (w : EdgeWorld) (c : node_id) (j : nat) : Prop :=
  exists s h, slot_at w c j = Some (s, Some h).

Lemma owns_is_Some w c i s mh :
  slot_at w c i = Some (s, mh) -> owns w c i <-> is_Some mh.
Proof.
  unfold owns. intros Hs. rewrite Hs. split.
  - intros (s' & h & ?). simplify_eq. eauto.
  - intros [h ->]. eauto.
Qed.

Lemma owns_set_handle_ne c i mh w c' j :
  ~ (c' = c /\ j = i) -> owns (set_handle c i mh w) c' j <-> owns w c' j.
Proof. intros Hne. unfold owns. rewrite slot_at_set_handle, decide_False by done. done. Qed.

Lemma owns_set_handle_none c i w : ~ owns (set_handle c i None w) c i.
Proof.
  unfold owns. rewrite slot_at_set_handle, decide_True by done.
  intros (s & h & Hs). by destruct (slot_at w c i).
Qed.

(** ** Removing a slot's edge *)

(** What a stretch of [trim_loop] does to slots. *)
Definition trim_relation (self : node_id) (from upto : nat) (w w' : EdgeWorld) : Prop :=
  (forall c j, fst <$> slot_at w' c j = fst <$> slot_at w c j) /\
  (forall c j, (c <> self \/ j < from \/ upto <= j) -> owns w' c j <-> owns w c j) /\
  (forall j, from <= j < upto -> ~ owns w' self j) /\
  (forall c, source_cursor (sources_of w' c) = source_cursor (sources_of w c)).

Lemma observers_remove_at_ok c s h o :
  observer_items o !! h = Some (c, s) -> exists r, observers_remove_at c h o = Some r.
Proof.
  intros Hh. unfold observers_remove_at. rewrite Hh, decide_True by done.
  pose proof (lookup_lt_Some _ _ _ Hh) as Hlt.
  destruct (decide (h = pred (length (observer_items o)))) as [|Hne]; [by eexists|].
  destruct (lookup_lt_is_Some_2 (observer_items o) (pred (length (observer_items o))) ltac:(lia)) as [[m ms] ->].
  by eexists.
Qed.

Lemma remove_step self i s h w :
  edge_match w ->
  slot_at w self i = Some (s, Some h) ->
  exists w', sources_remove_slot self i w = Some w' /\ edge_match w' /\ trim_relation self i (S i) w w'.
Proof.
  intros Hm Hs. red in Hm.
  assert (Hh : observer_items (observers_of w s) !! h = Some (self, i)) by (by apply Hm).
  pose proof (lookup_lt_Some _ _ _ Hh) as Hhlen.
  destruct (observers_remove_at_ok _ _ _ _ Hh) as [[o' n] Hr].
  destruct (observers_remove_at_spec _ _ _ _ _ Hr) as (_ & Hsame & Hgone & Hnotice).
  unfold sources_remove_slot, remove_edge. rewrite Hs, Hr. simpl.
  destruct Hnotice as [[Hl ->]|(m & ms & Hhl & Hlast & -> & Hhole)].
  - eexists. split; [reflexivity|]. split.
    + intros c j s' q.
      rewrite slot_at_set_handle, observers_of_set_handle, observers_of_set_observers.
      destruct (decide (c = self /\ j = i)) as [[-> ->]|Hni].
      * rewrite slot_at_set_observers, Hs. simpl. split; [congruence|]. intros Hq.
        destruct (decide (s' = s)) as [->|Hs'].
        -- destruct (decide (q = h)) as [->|Hqh].
           ++ rewrite Hgone in Hq by lia. done.
           ++ destruct (decide (q < pred (length (observer_items (observers_of w s))))) as [Hql|Hql].
              ** rewrite Hsame in Hq by done. apply Hm in Hq. congruence.
              ** rewrite Hgone in Hq by lia. done.
        -- apply Hm in Hq. congruence.
      * rewrite slot_at_set_observers.
        destruct (decide (s' = s)) as [->|Hs']; [|apply Hm]. rewrite Hm. split.
        -- intros Hq. pose proof (lookup_lt_Some _ _ _ Hq) as Hqlt.
           destruct (decide (q = h)) as [->|Hqh]; [rewrite Hh in Hq; simplify_eq; tauto|].
           rewrite Hsame by lia. done.
        -- intros Hq. destruct (decide (q < pred (length (observer_items (observers_of w s))))) as [Hql|Hql].
           ++ destruct (decide (q = h)) as [->|Hqh]; [lia|]. rewrite Hsame in Hq by done. done.
           ++ rewrite Hgone in Hq by lia. done.
    + split_and!.
      * intros c j. rewrite fst_set_handle, slot_at_set_observers. done.
      * intros c j Hcj. rewrite owns_set_handle_ne by (intros [-> ->]; lia).
        unfold owns. setoid_rewrite slot_at_set_observers. done.
      * intros j Hj. assert (j = i) as -> by lia. apply owns_set_handle_none.
      * intros c. rewrite cursor_set_handle. done.
  - assert (Hms : slot_at w m ms = Some (s, Some (pred (length (observer_items (observers_of w s))))))
      by (by apply Hm).
    assert (Hne : ~ (m = self /\ ms = i)).
    { intros [-> ->]. rewrite Hs in Hms. simplify_eq. lia. }
    eexists. split; [reflexivity|]. split.
    + intros c j s' q.
      rewrite slot_at_set_handle, observers_of_set_handle. unfold sources_moved_in.
      rewrite observers_of_set_handle, observers_of_set_observers.
      destruct (decide (c = self /\ j = i)) as [[-> ->]|Hni].
      * rewrite slot_at_set_handle, decide_False by (intros [-> ->]; tauto).
        rewrite slot_at_set_observers, Hs. simpl. split; [congruence|]. intros Hq.
        destruct (decide (s' = s)) as [->|Hs'].
        -- destruct (decide (q = h)) as [->|Hqh].
           ++ rewrite Hhole in Hq. simplify_eq. tauto.
           ++ destruct (decide (q < pred (length (observer_items (observers_of w s))))) as [Hql|Hql].
              ** rewrite Hsame in Hq by done. apply Hm in Hq. congruence.
              ** rewrite Hgone in Hq by lia. done.
        -- apply Hm in Hq. congruence.
      * rewrite slot_at_set_handle.
        destruct (decide (c = m /\ j = ms)) as [[-> ->]|Hnm].
        -- rewrite slot_at_set_observers, Hms. simpl. split.
           ++ intros Heq. simplify_eq. rewrite decide_True by done. done.
           ++ intros Hq. destruct (decide (s' = s)) as [->|Hs'].
              ** destruct (decide (q = h)) as [->|Hqh]; [done|].
                 destruct (decide (q < pred (length (observer_items (observers_of w s))))) as [Hql|Hql].
                 --- rewrite Hsame in Hq by done. apply Hm in Hq. rewrite Hms in Hq.
                     simplify_eq. lia.
                 --- rewrite Hgone in Hq by lia. done.
              ** apply Hm in Hq. congruence.
        -- rewrite slot_at_set_observers.
           destruct (decide (s' = s)) as [->|Hs']; [|apply Hm]. rewrite Hm. split.
           ++ intros Hq. pose proof (lookup_lt_Some _ _ _ Hq) as Hqlt.
              destruct (decide (q = h)) as [->|Hqh]; [rewrite Hh in Hq; simplify_eq; tauto|].
              destruct (decide (q = pred (length (observer_items (observers_of w s))))) as [->|Hql];
                [rewrite Hlast in Hq; simplify_eq; tauto|].
              rewrite Hsame by lia. done.
           ++ intros Hq. destruct (decide (q = h)) as [->|Hqh].
              ** rewrite Hhole in Hq. simplify_eq. tauto.
              ** destruct (decide (q < pred (length (observer_items (observers_of w s))))) as [Hql|Hql].
                 --- rewrite Hsame in Hq by done. done.
                 --- rewrite Hgone in Hq by lia. done.
    + split_and!.
      * intros c j. rewrite fst_set_handle. unfold sources_moved_in. rewrite fst_set_handle.
        rewrite slot_at_set_observers. done.
      * intros c j Hcj. rewrite owns_set_handle_ne by (intros [-> ->]; lia).
        unfold sources_moved_in.
        destruct (decide (c = m /\ j = ms)) as [[-> ->]|Hnm].
        -- unfold owns. rewrite slot_at_set_handle, decide_True by done.
           rewrite slot_at_set_observers, Hms. simpl. split; eauto.
        -- rewrite owns_set_handle_ne by done. unfold owns.
           setoid_rewrite slot_at_set_observers. done.
      * intros j Hj. assert (j = i) as -> by lia. apply owns_set_handle_none.
      * intros c. rewrite cursor_set_handle. unfold sources_moved_in.
        rewrite cursor_set_handle. done.
Qed.

Lemma remove_slot_ok self i w :
  edge_match w ->
  exists w', sources_remove_slot self i w = Some w' /\ edge_match w' /\ trim_relation self i (S i) w w'.
Proof.
  intros Hm. destruct (slot_at w self i) as [[s [h|]]|] eqn:Hs.
  - by eapply remove_step.
  - exists w. unfold sources_remove_slot. rewrite Hs. split_and!; [done|done|].
    unfold trim_relation. split_and!; try done.
    intros j Hj. assert (j = i) as -> by lia. unfold owns. rewrite Hs.
    intros (s' & h & ?). simplify_eq.
  - exists w. unfold sources_remove_slot. rewrite Hs. split_and!; [done|done|].
    unfold trim_relation. split_and!; try done.
    intros j Hj. assert (j = i) as -> by lia. unfold owns. rewrite Hs.
    intros (s' & h & ?). simplify_eq.
Qed.

Lemma trim_loop_ok self n i w :
  edge_match w ->
  exists w', trim_loop self i n w = Some w' /\ edge_match w' /\ trim_relation self i (i + n) w w'.
Proof.
  revert i w. induction n as [|n IH]; intros i w Hm; simpl.
  - exists w. split_and!; [done|done|]. unfold trim_relation. split_and!; try done. intros j Hj. lia.
  - destruct (remove_slot_ok self i w Hm) as (w1 & -> & Hm1 & Hfst1 & Hown1 & Hno1 & Hcur1). simpl.
    destruct (IH (S i) w1 Hm1) as (w' & -> & Hm' & Hfst' & Hown' & Hno' & Hcur').
    exists w'. split_and!; [done|done|]. unfold trim_relation. split_and!.
    + intros c j. rewrite Hfst', Hfst1. done.
    + intros c j Hcj. rewrite Hown' by (destruct Hcj as [|[|]]; [tauto|right; left; lia|right; right; lia]).
      apply Hown1. destruct Hcj as [|[|]]; [tauto|right; left; lia|right; right; lia].
    + intros j Hj. destruct (decide (j = i)) as [->|Hne].
      * rewrite Hown' by (right; left; lia). apply Hno1. lia.
      * apply Hno'. lia.
    + intros c. rewrite Hcur', Hcur1. done.
Qed.

(** What a trim from [from] does to slots. *)
Definition trimmed (self : node_id) (from : nat) (w w' : EdgeWorld) : Prop :=
  (forall j, j < from ->
     fst <$> slot_at w' self j = fst <$> slot_at w self j /\ (owns w' self j <-> owns w self j)) /\
  (forall j, from <= j -> slot_at w' self j = None) /\
  (forall c j, c <> self ->
     fst <$> slot_at w' c j = fst <$> slot_at w c j /\ (owns w' c j <-> owns w c j)) /\
  (forall c, source_cursor (sources_of w' c) = source_cursor (sources_of w c)).

Lemma sources_trim_from_ok self from w :
  edge_match w ->
  exists w', sources_trim_from self from w = Some w' /\ edge_match w' /\ trimmed self from w w'.
Proof.
  intros Hm. unfold sources_trim_from.
  destruct (trim_loop_ok self (length (source_slots (sources_of w self)) - from) from w Hm)
    as (w1 & -> & Hm1 & Hfst & Hown & Hno & Hcur). simpl.
  assert (Hnot : forall j, from <= j -> ~ owns w1 self j).
  { intros j Hj Ho. destruct (decide (j < length (source_slots (sources_of w self)))) as [Hl|Hl].
    - apply (Hno j); [lia|done].
    - apply Hown in Ho; [|right; right; lia].
      destruct Ho as (s & h & Hs). unfold slot_at in Hs. apply lookup_lt_Some in Hs. lia. }
  eexists. split; [reflexivity|]. split; [|unfold trimmed; split_and!].
  - intros c j s q. rewrite slot_at_set_sources, observers_of_set_sources.
    destruct (decide (c = self)) as [->|Hne]; simpl; [|apply Hm1].
    destruct (decide (j < from)) as [Hj|Hj].
    + rewrite lookup_take_lt by done. apply Hm1.
    + rewrite lookup_take_ge by lia. split; [done|]. intros Hq.
      apply Hm1 in Hq. exfalso. eapply (Hnot j); [lia|]. eexists _, _. exact Hq.
  - intros j Hj. unfold owns. rewrite !slot_at_set_sources, decide_True by done. simpl.
    rewrite lookup_take_lt by done. split; [apply Hfst|]. apply Hown. tauto.
  - intros j Hj. rewrite slot_at_set_sources, decide_True by done. simpl.
    apply lookup_take_ge. done.
  - intros c j Hne. unfold owns. rewrite !slot_at_set_sources, decide_False by done.
    split; [apply Hfst|]. apply Hown. tauto.
  - intros c. rewrite sources_of_set_sources.
    destruct (decide (c = self)) as [->|]; simpl; apply Hcur.
Qed.

Lemma sources_end_run_ok self w :
  edge_match w ->
  exists w', sources_end_run self w = Some w' /\ edge_match w' /\
             trimmed self (source_cursor (sources_of w self)) w w'.
Proof.
  intros Hm. unfold sources_end_run.
  destruct (decide (source_cursor (sources_of w self) < length (source_slots (sources_of w self)))) as [Hl|Hl].
  - by apply sources_trim_from_ok.
  - exists w. split_and!; [done|done|]. unfold trimmed. split_and!; try done.
    intros j Hj. apply lookup_ge_None_2. lia.
Qed.

(** ** Slots, source by source *)

Lemma first_owns_transfer w w1 c from :
  (forall j, j < from ->
     fst <$> slot_at w1 c j = fst <$> slot_at w c j /\ (owns w1 c j <-> owns w c j)) ->
  (forall j, from <= j -> slot_at w1 c j = None) ->
  first_owns w c -> first_owns w1 c.
Proof.
  intros Hsame Hnone Hf i s mh Hs.
  assert (Hi : i < from).
  { destruct (decide (i < from)) as [|Hge]; [done|]. rewrite Hnone in Hs by lia. done. }
  destruct (Hsame i Hi) as [Hfst Hown].
  destruct (slot_at w c i) as [[s0 mh0]|] eqn:Hs0; rewrite Hs in Hfst; simpl in Hfst; [|done].
  simplify_eq.
  rewrite <- (owns_is_Some _ _ _ _ _ Hs), Hown, (owns_is_Some _ _ _ _ _ Hs0), (Hf _ _ _ Hs0).
  split.
  - intros Hall j Hj. rewrite (proj1 (Hsame j ltac:(lia))). by apply Hall.
  - intros Hall j Hj. rewrite <- (proj1 (Hsame j ltac:(lia))). by apply Hall.
Qed.

Lemma first_owns_trimmed self from w w' :
  trimmed self from w w' ->
  (forall c, first_owns w c) -> forall c, first_owns w' c.
Proof.
  intros (Hsame & Hnone & Hother & _) Hf c.
  destruct (decide (c = self)) as [->|Hne].
  - eapply first_owns_transfer; [apply Hsame|apply Hnone|apply Hf].
  - eapply first_owns_transfer with (from := length (source_slots (sources_of w' c))).
    + intros j _. by apply Hother.
    + intros j Hj. by apply lookup_ge_None_2.
    + apply Hf.
Qed.

Lemma slot_at_append self s mh o w c j :
  slot_at (sources_append self s mh o w) c j =
    if decide (c = self) then (source_slots (sources_of w self) ++ [(s, mh)]) !! j else slot_at w c j.
Proof.
  unfold sources_append. rewrite slot_at_set_observers, slot_at_set_sources.
  destruct (decide (c = self)); done.
Qed.

Lemma first_source s (l : list (node_id * option nat)) :
  s ∈ fst <$> l ->
  exists i mh, l !! i = Some (s, mh) /\ forall j, j < i -> fst <$> l !! j <> Some s.
Proof.
  induction l as [|[s0 mh0] l IH]; simpl; intros Hin.
  - by apply not_elem_of_nil in Hin.
  - destruct (decide (s0 = s)) as [->|Hne].
    + exists 0, mh0. split; [done|]. intros j Hj. lia.
    + apply elem_of_cons in Hin as [->|Hin]; [done|].
      destruct (IH Hin) as (i & mh & Hi & Hb). exists (S i), mh. split; [done|].
      intros [|j] Hj; simpl; [congruence|]. apply Hb. lia.
Qed.

Lemma first_owns_edge w c s :
  first_owns w c ->
  s ∈ fst <$> source_slots (sources_of w c) ->
  exists i h, slot_at w c i = Some (s, Some h).
Proof.
  intros Hf Hin. destruct (first_source _ _ Hin) as (i & mh & Hi & Hb).
  destruct (proj2 (Hf i s mh Hi) Hb) as [h ->]. eauto.
Qed.

Lemma slot_source w c i s mh :
  slot_at w c i = Some (s, mh) -> s ∈ fst <$> source_slots (sources_of w c).
Proof.
  intros Hs. apply list_elem_of_fmap. exists (s, mh). split; [done|].
  by eapply list_elem_of_lookup_2.
Qed.

(** [c] is in [s]'s observers exactly when [s] has a slot in [c]'s list. *)
Lemma held_iff_slot w c s :
  world_well_formed w -> c ∈ observer_view (observers_of w s) <-> s ∈ fst <$> source_slots (sources_of w c).
Proof.
  intros [Hm Hf]. rewrite view_elem. split.
  - intros (q & i & Hq). apply Hm in Hq. by eapply slot_source.
  - intros Hin. destruct (first_owns_edge _ _ _ (Hf c) Hin) as (i & h & Hs).
    exists h, i. by apply Hm.
Qed.

(** Two slots of one source holding edges are one slot. *)
Lemma first_owns_unique w c s i j hi hj :
  first_owns w c ->
  slot_at w c i = Some (s, Some hi) -> slot_at w c j = Some (s, Some hj) -> i = j.
Proof.
  intros Hf Hi Hj. destruct (decide (i = j)) as [|Hne]; [done|]. exfalso.
  destruct (decide (i < j)) as [Hlt|Hge].
  - eapply (proj1 (Hf _ _ _ Hj) ltac:(eauto) i Hlt). by rewrite Hi.
  - eapply (proj1 (Hf _ _ _ Hi) ltac:(eauto) j ltac:(lia)). by rewrite Hj.
Qed.

Lemma well_formed_held_once w c s :
  world_well_formed w -> s ∈ fst <$> source_slots (sources_of w c) -> held_once c (observers_of w s).
Proof.
  intros Hw Hin. pose proof Hw as [Hm Hf]. split; [by apply held_iff_slot|].
  intros i j Hi Hj.
  apply view_lookup in Hi as [a Ha]. apply view_lookup in Hj as [b Hb].
  apply Hm in Ha. apply Hm in Hb.
  assert (a = b) as -> by (eapply first_owns_unique; [apply Hf|exact Ha|exact Hb]).
  rewrite Ha in Hb. by simplify_eq.
Qed.

Lemma lookup_append_lt (l : list (node_id * option nat)) x j :
  j < length l -> (l ++ [x]) !! j = l !! j.
Proof. apply lookup_app_l. Qed.

Lemma lookup_append_eq (l : list (node_id * option nat)) x : (l ++ [x]) !! length l = Some x.
Proof. rewrite lookup_app_r, Nat.sub_diag by lia. done. Qed.

Lemma lookup_append_gt (l : list (node_id * option nat)) x j :
  length l < j -> (l ++ [x]) !! j = None.
Proof. intros Hj. apply lookup_ge_None_2. rewrite length_app. simpl. lia. Qed.

(** ** Appending *)

Lemma sources_link_cases self s w :
  (sources_link self s w = sources_append self s None (observers_of w s) w /\
   (self ∈ observer_view (observers_of w s) \/ s ∈ fst <$> source_slots (sources_of w self))) \/
  (sources_link self s w =
     sources_append self s (Some (length (observer_items (observers_of w s))))
       (observers_add self (length (source_slots (sources_of w self))) (observers_of w s)).1 w /\
   (source_slots (sources_of w self) = [] \/ self ∉ observer_view (observers_of w s) \/
    s ∉ fst <$> source_slots (sources_of w self))).
Proof.
  unfold sources_link.
  destruct (decide (length (source_slots (sources_of w self)) = 0)) as [H0|H0].
  { right. split; [reflexivity|]. left. by apply nil_length_inv. }
  destruct (observers_try_add_spec self (length (source_slots (sources_of w self))) (observers_of w s))
    as [[-> Hc]|[(h & -> & -> & Hc)|(-> & _ & _)]].
  - left. split; [reflexivity|]. by left.
  - right. split; [reflexivity|]. by right; left.
  - destruct (decide (s ∈ fst <$> source_slots (sources_of w self))) as [Hin|Hin].
    + left. split; [reflexivity|]. by right.
    + right. split; [reflexivity|]. by right; right.
Qed.

Lemma edge_match_append_none self s w :
  edge_match w -> edge_match (sources_append self s None (observers_of w s) w).
Proof.
  intros Hm c j s' q. rewrite slot_at_append.
  unfold sources_append. rewrite observers_of_set_observers, observers_of_set_sources.
  assert (Hobs : (if decide (s' = s) then observers_of w s else observers_of w s') = observers_of w s').
  { by destruct (decide (s' = s)) as [->|]. }
  rewrite Hobs.
  destruct (decide (c = self)) as [->|Hne]; [|apply Hm].
  destruct (decide (j < length (source_slots (sources_of w self)))) as [Hl|Hl].
  - rewrite lookup_append_lt by done. apply Hm.
  - split.
    + destruct (decide (j = length (source_slots (sources_of w self)))) as [->|Hj].
      * rewrite lookup_append_eq. done.
      * rewrite lookup_append_gt by lia. done.
    + intros Hq. apply Hm in Hq. apply lookup_lt_Some in Hq. lia.
Qed.

Lemma edge_match_append_add self s w :
  edge_match w ->
  edge_match
    (sources_append self s (Some (length (observer_items (observers_of w s))))
       (observers_add self (length (source_slots (sources_of w self))) (observers_of w s)).1 w).
Proof.
  intros Hm c j s' q. red in Hm. rewrite slot_at_append.
  unfold sources_append. rewrite observers_of_set_observers, observers_of_set_sources.
  set (slot := length (source_slots (sources_of w self))).
  set (len := length (observer_items (observers_of w s))).
  assert (Hnoslot : forall z, observer_items (observers_of w z) !! q <> Some (self, slot)).
  { intros z Hq. apply Hm in Hq. apply lookup_lt_Some in Hq. subst slot. lia. }
  assert (Hlenq : forall c' j', observer_items (observers_of w s) !! len <> Some (c', j')).
  { intros c' j'. subst len. rewrite lookup_ge_None_2 by lia. done. }
  destruct (decide (s' = s)) as [->|Hs']; simpl.
  - assert (Hitems : forall x, (observer_items (observers_of w s) ++ [(self, slot)]) !! q = Some x <->
              (q < len /\ observer_items (observers_of w s) !! q = Some x) \/ (q = len /\ x = (self, slot))).
    { intros x. rewrite lookup_app_Some. subst len. split.
      - intros [Hq|[Hge Hq]].
        + left. split; [eapply lookup_lt_Some; done|done].
        + right. destruct (q - length (observer_items (observers_of w s))) as [|d] eqn:Hd; simpl in Hq.
          * simplify_eq. split; [lia|done].
          * by rewrite lookup_nil in Hq.
      - intros [[_ Hq]|[-> ->]]; [by left|]. right. split; [lia|].
        rewrite Nat.sub_diag. done. }
    rewrite Hitems.
    destruct (decide (c = self)) as [->|Hne].
    + destruct (decide (j < slot)) as [Hl|Hl].
      * rewrite lookup_append_lt by done. fold (slot_at w self j). rewrite Hm. split.
        -- intros Hq. left. split; [|done]. by eapply lookup_lt_Some.
        -- intros [[_ Hq]|[-> Heq]]; [done|]. simplify_eq. lia.
      * destruct (decide (j = slot)) as [->|Hj].
        -- unfold slot; rewrite lookup_append_eq; fold slot. split.
           ++ intros Heq. simplify_eq. by right.
           ++ intros [[_ Hq]|[-> _]]; [|done]. by apply Hnoslot in Hq.
        -- rewrite lookup_append_gt by lia. split; [done|].
           intros [[_ Hq]|[_ Heq]]; [|simplify_eq; tauto].
           apply Hm in Hq. apply lookup_lt_Some in Hq. lia.
    + rewrite Hm. split.
      * intros Hq. left. split; [|done]. by eapply lookup_lt_Some.
      * intros [[_ Hq]|[_ Heq]]; [done|]. simplify_eq.
  - destruct (decide (c = self)) as [->|Hne]; [|apply Hm].
    destruct (decide (j < slot)) as [Hl|Hl].
    + rewrite lookup_append_lt by done. apply Hm.
    + split.
      * destruct (decide (j = slot)) as [->|Hj].
        -- unfold slot; rewrite lookup_append_eq; fold slot. intros Heq. simplify_eq.
        -- rewrite lookup_append_gt by lia. done.
      * intros Hq. apply Hm in Hq. apply lookup_lt_Some in Hq. lia.
Qed.

Lemma edge_match_link self s w : edge_match w -> edge_match (sources_link self s w).
Proof.
  intros Hm. destruct (sources_link_cases self s w) as [[-> _]|[-> _]].
  - by apply edge_match_append_none.
  - by apply edge_match_append_add.
Qed.

Lemma first_owns_append self s mh o w :
  first_owns w self ->
  (is_Some mh <-> s ∉ fst <$> source_slots (sources_of w self)) ->
  first_owns (sources_append self s mh o w) self.
Proof.
  intros Hf Hmh i s' mh'. rewrite !slot_at_append, decide_True by done.
  setoid_rewrite slot_at_append. setoid_rewrite (decide_True _ _ (eq_refl self)).
  destruct (decide (i < length (source_slots (sources_of w self)))) as [Hl|Hl].
  - rewrite lookup_append_lt by done. intros Hi. rewrite (Hf _ _ _ Hi). split.
    + intros Hall j Hj. rewrite lookup_append_lt by lia. by apply Hall.
    + intros Hall j Hj. specialize (Hall j Hj). rewrite lookup_append_lt in Hall by lia. done.
  - destruct (decide (i = length (source_slots (sources_of w self)))) as [->|Hi].
    + rewrite lookup_append_eq. intros Heq. simplify_eq. rewrite Hmh. split.
      * intros Hnot j Hj. rewrite lookup_append_lt by lia. intros Hs.
        apply Hnot. apply list_elem_of_fmap.
        destruct (source_slots (sources_of w self) !! j) as [[s0 m0]|] eqn:E; simplify_eq/=.
        exists (s', m0). split; [done|]. by eapply list_elem_of_lookup_2.
      * intros Hall Hin. apply list_elem_of_lookup in Hin as [j Hj].
        rewrite list_lookup_fmap in Hj.
        destruct (source_slots (sources_of w self) !! j) as [[s0 m0]|] eqn:E; simplify_eq/=.
        pose proof (lookup_lt_Some _ _ _ E) as Hjl.
        apply (Hall j Hjl). rewrite lookup_append_lt by done. rewrite E. done.
    + rewrite lookup_append_gt by lia. done.
Qed.

Lemma first_owns_append_other self s mh o w c :
  c <> self -> first_owns w c -> first_owns (sources_append self s mh o w) c.
Proof.
  intros Hne Hf i s' mh'. rewrite slot_at_append, decide_False by done.
  setoid_rewrite slot_at_append. setoid_rewrite (decide_False _ _ Hne). apply Hf.
Qed.

Lemma well_formed_link self s w : world_well_formed w -> world_well_formed (sources_link self s w).
Proof.
  intros Hw. pose proof Hw as [Hm Hf]. split; [by apply edge_match_link|]. intros c.
  destruct (decide (c = self)) as [->|Hne].
  - destruct (sources_link_cases self s w) as [[-> Hc]|[-> Hc]].
    + apply first_owns_append; [done|]. split; [by intros []|].
      intros Hnot. exfalso. apply Hnot.
      destruct Hc as [Hc|Hc]; [by apply (held_iff_slot _ _ _ Hw) in Hc|done].
    + apply first_owns_append; [done|]. split; [|eauto]. intros _.
      destruct Hc as [->|[Hc|Hc]]; [|by rewrite (held_iff_slot _ _ _ Hw) in Hc|done].
      simpl. apply not_elem_of_nil.
  - destruct (sources_link_cases self s w) as [[-> _]|[-> _]];
      by apply first_owns_append_other.
Qed.

(** ** Preservation *)

Lemma well_formed_end self w :
  world_well_formed w -> exists w', sources_end_run self w = Some w' /\ world_well_formed w' /\
                        trimmed self (source_cursor (sources_of w self)) w w'.
Proof.
  intros [Hm Hf]. destruct (sources_end_run_ok self w Hm) as (w' & Hr & Hm' & Ht).
  exists w'. split_and!; [done|split; [done|]|done]. by eapply first_owns_trimmed.
Qed.

Lemma sources_clear_ok self w :
  edge_match w ->
  exists w', sources_clear self w = Some w' /\ edge_match w' /\
             source_slots (sources_of w' self) = [] /\ source_cursor (sources_of w' self) = 0 /\
             (forall c j, c <> self ->
                fst <$> slot_at w' c j = fst <$> slot_at w c j /\ (owns w' c j <-> owns w c j)) /\
             (forall s, self ∉ observer_view (observers_of w' s)).
Proof.
  intros Hm. destruct (sources_trim_from_ok self 0 w Hm) as (w1 & Hr & Hm1 & _ & Hnone & Hother & _).
  assert (Hslots : source_slots (sources_of w1 self) = []).
  { destruct (source_slots (sources_of w1 self)) as [|x l] eqn:E; [done|].
    exfalso. specialize (Hnone 0 ltac:(lia)). unfold slot_at in Hnone. rewrite E in Hnone. done. }
  unfold sources_clear. rewrite Hr. simpl.
  eexists. split; [reflexivity|].
  assert (Hm' : edge_match (set_sources self {| source_slots := source_slots (sources_of w1 self); source_cursor := 0 |} w1)).
  { intros c j s q. rewrite slot_at_set_sources, observers_of_set_sources.
    destruct (decide (c = self)) as [->|]; [|apply Hm1]. simpl. apply Hm1. }
  split_and!; [done|..].
  - rewrite sources_of_set_sources, decide_True by done. simpl. done.
  - rewrite sources_of_set_sources, decide_True by done. done.
  - intros c j Hne. unfold owns. rewrite !slot_at_set_sources, decide_False by done.
    apply Hother. done.
  - intros s. rewrite view_elem. intros (q & i & Hq). apply Hm' in Hq.
    unfold slot_at in Hq. rewrite sources_of_set_sources, decide_True in Hq by done.
    simpl in Hq. rewrite Hslots in Hq. done.
Qed.

Lemma well_formed_clear self w :
  world_well_formed w -> exists w', sources_clear self w = Some w' /\ world_well_formed w'.
Proof.
  intros [Hm Hf]. destruct (sources_clear_ok self w Hm) as (w' & Hr & Hm' & Hs & _ & Hother & _).
  exists w'. split; [done|]. split; [done|]. intros c.
  destruct (decide (c = self)) as [->|Hne].
  - intros i s mh Hi. unfold slot_at in Hi. rewrite Hs in Hi. done.
  - eapply first_owns_transfer with (from := length (source_slots (sources_of w' c))).
    + intros j _. by apply Hother.
    + intros j Hj. by apply lookup_ge_None_2.
    + apply Hf.
Qed.

Lemma well_formed_begin c w : world_well_formed w -> world_well_formed (sources_begin_run c w).
Proof.
  intros [Hm Hf]. unfold sources_begin_run.
  assert (Hslot : forall c' i,
    slot_at (set_sources c {| source_slots := source_slots (sources_of w c); source_cursor := 0 |} w) c' i = slot_at w c' i).
  { intros c' i. rewrite slot_at_set_sources. destruct (decide (c' = c)) as [->|]; done. }
  split.
  - intros c' i s h. rewrite Hslot, observers_of_set_sources. apply Hm.
  - intros c' i s mh. rewrite !Hslot. setoid_rewrite Hslot. apply Hf.
Qed.

Lemma slot_at_advance self c j w :
  slot_at (set_sources self {| source_slots := source_slots (sources_of w self);
                           source_cursor := S (source_cursor (sources_of w self)) |} w) c j = slot_at w c j.
Proof.
  rewrite slot_at_set_sources. destruct (decide (c = self)) as [->|]; done.
Qed.

Lemma well_formed_add self s w :
  world_well_formed w -> exists w', sources_add self s w = Some w' /\ world_well_formed w'.
Proof.
  intros Hw. pose proof Hw as [Hm Hf]. unfold sources_add.
  destruct (decide (fst <$> source_slots (sources_of w self) !! source_cursor (sources_of w self) = Some s)) as [Hp|Hp].
  - eexists. split; [reflexivity|]. split.
    + intros c i s' h. rewrite slot_at_advance, observers_of_set_sources. apply Hm.
    + intros c i s' mh. rewrite !slot_at_advance. setoid_rewrite slot_at_advance. apply Hf.
  - destruct (decide (0 < source_cursor (sources_of w self) /\
                      fst <$> source_slots (sources_of w self) !! pred (source_cursor (sources_of w self)) = Some s));
      [by eexists|].
    destruct (well_formed_end self w Hw) as (w1 & -> & Hw1 & _). simpl.
    eexists. split; [reflexivity|]. by apply well_formed_link.
Qed.

Lemma reachable_well_formed w : world_reachable w -> world_well_formed w.
Proof.
  induction 1 as [|c w _ IH|c s w w' _ IH Ha|c w w' _ IH He|c w w' _ IH Hc].
  - split.
    + intros c i s h. unfold slot_at, observers_of, sources_of. simpl. rewrite !lookup_empty. simpl.
      rewrite !lookup_nil. done.
    + intros c i s mh. unfold slot_at, sources_of. simpl. rewrite lookup_empty. simpl.
      rewrite lookup_nil. done.
  - by apply well_formed_begin.
  - destruct (well_formed_add c s w IH) as (w'' & Ha' & Hw). congruence.
  - destruct (well_formed_end c w IH) as (w'' & He' & Hw & _). congruence.
  - destruct (well_formed_clear c w IH) as (w'' & Hc' & Hw). congruence.
Qed.

(** ** What a tracked read does to the source list *)

(**md
**A read matching the slot at the cursor only advances the cursor.**

When the slot at the cursor holds the source being read, `Add` succeeds, the cursor moves up one, and every observer set, this computation's slots and every other computation's source list stay as they were.

- [self:] the computation reading.
- [s:] the source read.
- [w:] the edge world before the read.
*)
Theorem sources_add_positional self s w :
  fst <$> slot_at w self (source_cursor (sources_of w self)) = Some s ->
  exists w', sources_add self s w = Some w' /\
    world_observers w' = world_observers w /\
    source_slots (sources_of w' self) = source_slots (sources_of w self) /\
    source_cursor (sources_of w' self) = S (source_cursor (sources_of w self)) /\
    forall c, c <> self -> sources_of w' c = sources_of w c.
Proof.
  intros Hp. unfold sources_add. unfold slot_at in Hp. rewrite decide_True by done.
  eexists. split; [reflexivity|]. split_and!; [done|..].
  - rewrite sources_of_set_sources, decide_True by done. done.
  - rewrite sources_of_set_sources, decide_True by done. done.
  - intros c Hne. rewrite sources_of_set_sources, decide_False by done. done.
Qed.

(**md
**Reading the source just matched again changes nothing.**

When the slot at the cursor holds a different source and the slot just before the cursor holds this one, `Add` returns the world unchanged.

- [self:] the computation reading.
- [s:] the source read.
- [w:] the edge world, which `sources_add` leaves unchanged.
*)
Theorem sources_add_repeat self s w :
  fst <$> slot_at w self (source_cursor (sources_of w self)) <> Some s ->
  0 < source_cursor (sources_of w self) ->
  fst <$> slot_at w self (pred (source_cursor (sources_of w self))) = Some s ->
  sources_add self s w = Some w.
Proof.
  intros Hp Hc Hr. unfold slot_at in *. unfold sources_add.
  rewrite decide_False by done. rewrite decide_True by done. done.
Qed.

Lemma slots_fst_trimmed self from w w' :
  trimmed self from w w' ->
  fst <$> source_slots (sources_of w' self) = take from (fst <$> source_slots (sources_of w self)).
Proof.
  intros (Hsame & Hnone & _ & _). apply list_eq. intros j.
  rewrite list_lookup_fmap.
  destruct (decide (j < from)) as [Hj|Hj].
  - rewrite lookup_take_lt by done. rewrite list_lookup_fmap. apply Hsame. done.
  - rewrite lookup_take_ge by lia. fold (slot_at w' self j). rewrite Hnone by lia. done.
Qed.

(**md
**A read matching neither slot drops the rest of the list and appends the source.**

When the read matches neither the slot at the cursor nor the one before it, the new list is the slots before the cursor followed by the source, with the cursor at its end. The two ends of every edge still agree, and the computation appears only in the observers of sources in its new list.

Premise: the two ends of every edge agree beforehand (`edge_match`).

- [self:] the computation reading.
- [s:] the source read.
- [w:] the edge world before the read.
*)
Theorem sources_add_truncates self s w :
  edge_match w ->
  fst <$> slot_at w self (source_cursor (sources_of w self)) <> Some s ->
  ~ (0 < source_cursor (sources_of w self) /\ fst <$> slot_at w self (pred (source_cursor (sources_of w self))) = Some s) ->
  exists w', sources_add self s w = Some w' /\
    fst <$> source_slots (sources_of w' self) =
      take (source_cursor (sources_of w self)) (fst <$> source_slots (sources_of w self)) ++ [s] /\
    source_cursor (sources_of w' self) = length (source_slots (sources_of w' self)) /\
    edge_match w' /\
    forall s', s' ∉ fst <$> source_slots (sources_of w' self) -> self ∉ observer_view (observers_of w' s').
Proof.
  intros Hm Hp Hr. unfold slot_at in *. unfold sources_add.
  rewrite decide_False by done. rewrite decide_False by done.
  destruct (sources_end_run_ok self w Hm) as (w1 & -> & Hm1 & Ht). simpl.
  assert (Hm' := edge_match_link self s w1 Hm1).
  assert (Hshape : exists mh, source_slots (sources_of (sources_link self s w1) self) =
                     source_slots (sources_of w1 self) ++ [(s, mh)] /\
                   source_cursor (sources_of (sources_link self s w1) self) = S (length (source_slots (sources_of w1 self)))).
  { destruct (sources_link_cases self s w1) as [[-> _]|[-> _]]; unfold sources_append;
      rewrite sources_of_set_observers, sources_of_set_sources, decide_True by done; simpl; eauto. }
  destruct Hshape as (mh & Hsl & Hcur).
  eexists. split; [reflexivity|]. split_and!.
  - rewrite Hsl, fmap_app. simpl. rewrite (slots_fst_trimmed _ _ _ _ Ht). done.
  - rewrite Hcur, Hsl, length_app. simpl. lia.
  - done.
  - intros s' Hnot. rewrite view_elem. intros (q & i & Hq). apply Hm' in Hq.
    apply Hnot. by eapply slot_source.
Qed.

(** ** At most one edge per source *)

(**md
**A computation holds at most one edge per source, in that source's first slot.**

In every world built from empty by the source-list operations, two slots of a computation that hold edges to one source are the same slot, and a slot holds an edge exactly when no earlier slot names its source.

- [w:] a world built by the source-list operations from an empty world.
*)
Theorem sources_no_duplicate_edge w :
  world_reachable w ->
  (forall c s i j hi hj,
     slot_at w c i = Some (s, Some hi) -> slot_at w c j = Some (s, Some hj) -> i = j) /\
  (forall c i s mh, slot_at w c i = Some (s, mh) ->
     (is_Some mh <-> forall j, j < i -> fst <$> slot_at w c j <> Some s)).
Proof.
  intros Hr. destruct (reachable_well_formed w Hr) as [_ Hf]. split.
  - intros c s i j hi hj. by apply first_owns_unique.
  - intros c. apply Hf.
Qed.

(** ** Trimming keeps the edges consistent *)

Lemma well_formed_slots_unique w : world_well_formed w -> slots_unique w.
Proof. intros [_ Hf] c s i j hi hj. by apply first_owns_unique. Qed.

(**md
**Removing a computation's edge moves only another computation's edge.**

The observer that a removal moves into the hole is a different computation from the one being removed.

Premises: the two ends of every edge agree (`edge_match`), every computation holds at most one edge per source (`slots_unique`), and the removal uses the handle stored in the computation's slot.

- [w:] the world before the removal.
- [self:] the computation whose slot is removed.
- [i:] the slot removed.
- [s:] the source in slot `i`.
- [h:] the handle in slot `i`.
- [o':] the observer set of `s` after the removal.
- [m:] the observer moved into the hole.
- [ms:] the slot of `m`.
- [h':] the new handle of `m`.
*)
Theorem trim_moves_only_others w self i s h o' m ms h' :
  edge_match w -> slots_unique w ->
  slot_at w self i = Some (s, Some h) ->
  observers_remove_at self h (observers_of w s) = Some (o', Some (m, ms, h')) ->
  m <> self.
Proof.
  intros Hm Hu Hs Hr ->.
  destruct (observers_remove_at_spec _ _ _ _ _ Hr) as (_ & _ & _ & [[_ ?]|(m' & ms' & Hhl & Hl & Hn & _)]);
    [done|].
  simplify_eq.
  apply Hm in Hl.
  assert (ms' = i) as -> by (eapply Hu; [exact Hl|exact Hs]).
  rewrite Hs in Hl. simplify_eq. lia.
Qed.

Lemma slots_unique_observers_once w :
  edge_match w -> slots_unique w -> observers_once w.
Proof.
  intros Hm Hu s c i j Hi Hj.
  apply view_lookup in Hi as [a Ha]. apply view_lookup in Hj as [b Hb].
  apply Hm in Ha. apply Hm in Hb.
  assert (a = b) as -> by (eapply Hu; [exact Ha|exact Hb]).
  rewrite Ha in Hb. by simplify_eq.
Qed.

Lemma trim_relation_owns_back self from upto w w' c j :
  trim_relation self from upto w w' -> owns w' c j -> owns w c j.
Proof.
  intros (_ & Hown & Hno & _) Ho.
  destruct (decide (c = self)) as [->|Hne]; [|by apply (Hown c j); [left|]].
  destruct (decide (from <= j < upto)) as [Hj|Hj].
  - exfalso. by eapply Hno.
  - apply (Hown self j); [|done]. right. lia.
Qed.

Lemma slots_unique_trim_relation self from upto w w' :
  trim_relation self from upto w w' -> slots_unique w -> slots_unique w'.
Proof.
  intros Hr Hu c s i j hi hj Hi Hj. pose proof Hr as [Hfst _].
  assert (Hback : forall k h, slot_at w' c k = Some (s, Some h) ->
                  exists h0, slot_at w c k = Some (s, Some h0)).
  { intros k h Hk. destruct (trim_relation_owns_back _ _ _ _ _ c k Hr ltac:(eexists _, _; exact Hk))
      as (s0 & h0 & Hk0).
    specialize (Hfst c k). rewrite Hk, Hk0 in Hfst. simpl in Hfst. simplify_eq. eauto. }
  destruct (Hback _ _ Hi) as [h0 Hi0]. destruct (Hback _ _ Hj) as [h1 Hj1].
  by eapply Hu.
Qed.

(**md
**Every world between the removals of a trim stays consistent.**

After any number of removals, the two ends of every edge agree, each computation holds at most one edge per source, each observer set holds an observer at most once, and the next removal moves only another computation's edge.

Premises: the first two properties hold when the trim starts.

- [self:] the computation trimmed.
- [from:] the first slot removed.
- [k:] the number of slots removed.
- [w:] the world before the trim.
- [w':] the world after the trim.
*)
Theorem trim_every_removal self from k w w' :
  edge_match w -> slots_unique w ->
  trim_loop self from k w = Some w' ->
  edge_match w' /\ slots_unique w' /\ observers_once w' /\
  forall s h o' m ms h',
    slot_at w' self (from + k) = Some (s, Some h) ->
    observers_remove_at self h (observers_of w' s) = Some (o', Some (m, ms, h')) ->
    m <> self.
Proof.
  intros Hm Hu Ht.
  destruct (trim_loop_ok self k from w Hm) as (w1 & Ht1 & Hm1 & Hr).
  assert (w1 = w') as -> by congruence.
  pose proof (slots_unique_trim_relation _ _ _ _ _ Hr Hu) as Hu'.
  split_and!; [done|done|by apply slots_unique_observers_once|].
  intros s h o' m ms h' Hs Hrm. by eapply trim_moves_only_others.
Qed.

(** A reachable world, or one between two removals of a trim of a reachable world: the worlds a walk's
    mutations can pass through. *)
Definition world_point (w : EdgeWorld) : Prop :=
  exists self from k w0, world_reachable w0 /\ trim_loop self from k w0 = Some w.

Lemma reachable_point w : world_reachable w -> world_point w.
Proof. intros Hr. exists 0, 0, 0, w. done. Qed.

(**md
**An observer in a set occupies exactly one position.**

Holds in every reachable world and in every world between the removals of a trim that starts from one.

- [w:] a `world_point`.
- [s:] the source.
- [c:] an observer of `s`.
*)
Theorem observer_held_once w s c :
  world_point w -> c ∈ observer_view (observers_of w s) -> held_once c (observers_of w s).
Proof.
  intros (self & from & k & w0 & Hr & Ht) Hin.
  pose proof (reachable_well_formed w0 Hr) as Hw.
  destruct (trim_every_removal self from k w0 w (proj1 Hw) (well_formed_slots_unique _ Hw) Ht)
    as (_ & _ & Honce & _).
  split; [done|]. apply Honce.
Qed.

(**md
**A clear stays correct when a removal moves the computation's own later edge.**

Take the source list A, B, A with an edge in each slot, the later A edge last in A's observers. The first removal moves that edge, the later slot then holds the moved handle, and `Clear` still succeeds, leaving an empty list and the computation in no observer set.

Premises: this exact list and the two ends of every edge agreeing. Reachable worlds exclude this list ([`sources_no_duplicate_edge`](#sources_no_duplicate_edge)), so this covers a world built by hand: see [`aba_reachable_trim`](#aba_reachable_trim).

- [w:] the world, whose slots for `self` are `A`, `B`, `A`.
- [self:] the computation trimmed.
- [A:] the source in the first and third slots.
- [B:] the source in the second slot.
- [ha:] the handle in the first slot.
- [hb:] the handle in the second slot.
- [ha':] the handle in the third slot, the last position of the observers of `A`.
*)
Theorem sources_trim_moves_own_edge w self A B ha hb ha' :
  edge_match w ->
  source_slots (sources_of w self) = [(A, Some ha); (B, Some hb); (A, Some ha')] ->
  S ha' = length (observer_items (observers_of w A)) ->
  (exists w1, sources_remove_slot self 0 w = Some w1 /\ slot_at w1 self 2 = Some (A, Some ha)) /\
  (exists w', sources_clear self w = Some w' /\ source_slots (sources_of w' self) = [] /\
     forall s, self ∉ observer_view (observers_of w' s)).
Proof.
  intros Hm Hsl Hlast. split.
  - assert (H0 : slot_at w self 0 = Some (A, Some ha)) by (unfold slot_at; rewrite Hsl; done).
    assert (H2 : slot_at w self 2 = Some (A, Some ha')) by (unfold slot_at; rewrite Hsl; done).
    pose proof (proj1 (Hm _ _ _ _) H0) as Hh0.
    pose proof (proj1 (Hm _ _ _ _) H2) as Hh2.
    assert (Hne : ha <> ha') by (intros ->; congruence).
    destruct (observers_remove_at_ok _ _ _ _ Hh0) as [[o' n] Hr].
    destruct (observers_remove_at_spec _ _ _ _ _ Hr) as (_ & _ & _ & Hn).
    rewrite <- Hlast in Hn. simpl in Hn.
    destruct Hn as [[? _]|(m & ms & _ & Hl & -> & _)]; [done|].
    rewrite Hh2 in Hl. simplify_eq.
    unfold sources_remove_slot, remove_edge. rewrite H0, Hr. simpl.
    eexists. split; [reflexivity|].
    rewrite slot_at_set_handle, decide_False by lia.
    unfold sources_moved_in. rewrite slot_at_set_handle, decide_True by done.
    rewrite slot_at_set_observers, H2. done.
  - destruct (sources_clear_ok self w Hm) as (w' & Hr & _ & Hs & _ & _ & Hgone). eauto.
Qed.

(**md
**A read that `TryAdd` can't answer for still leaves one edge.**

When a read misses its slot, the source has more than `scan_limit` observers and `observer_latest` holds another observer, `TryAdd` answers `TUnknown`. `Add` still succeeds, the world stays well formed, and the computation holds exactly one position in the source's observers, whether or not the source already had a slot.

Premise: the world is well formed beforehand. The observer count and `observer_latest` are measured after the trim of the slots from the cursor onward.

- [self:] the computation reading.
- [s:] the source read.
- [w:] the world before the read.
- [w1:] the world after `sources_end_run` on `self`.
*)
Theorem sources_add_unknown_no_duplicate self s w w1 :
  world_well_formed w ->
  fst <$> slot_at w self (source_cursor (sources_of w self)) <> Some s ->
  ~ (0 < source_cursor (sources_of w self) /\ fst <$> slot_at w self (pred (source_cursor (sources_of w self))) = Some s) ->
  sources_end_run self w = Some w1 ->
  scan_limit < length (observer_items (observers_of w1 s)) ->
  fst <$> observer_items (observers_of w1 s) !! observer_latest (observers_of w1 s) <> Some self ->
  (observers_try_add self (length (source_slots (sources_of w1 self))) (observers_of w1 s)).2 = TUnknown /\
  exists w', sources_add self s w = Some w' /\ world_well_formed w' /\ held_once self (observers_of w' s).
Proof.
  intros Hw Hp Hr He Habove Hstale. unfold slot_at in *. split.
  - unfold observers_try_add. rewrite decide_False by (unfold scan_limit in *; lia).
    rewrite decide_False by done. rewrite decide_False by lia. done.
  - unfold sources_add. rewrite decide_False by done. rewrite decide_False by done.
    rewrite He. simpl.
    destruct (well_formed_end self w Hw) as (w1' & He' & Hw1 & _).
    assert (w1' = w1) as -> by congruence.
    eexists. split; [reflexivity|].
    pose proof (well_formed_link self s w1 Hw1) as Hw'. split; [done|].
    apply well_formed_held_once; [done|].
    destruct (sources_link_cases self s w1) as [[-> _]|[-> _]]; unfold sources_append;
      rewrite sources_of_set_observers, sources_of_set_sources, decide_True by done; simpl;
      rewrite fmap_app; apply elem_of_app; right; simpl; apply list_elem_of_singleton; done.
Qed.

(** ** Checked runs *)

Definition reads (rs : list (node_id * node_id)) (w : EdgeWorld) : option EdgeWorld :=
  foldl (fun mw r => mw ≫= sources_add r.1 r.2) (Some w) rs.

(** Observers 1 to 9, each reading source 5. *)
Definition nine_readers : list (node_id * node_id) :=
  [(1,5); (2,5); (3,5); (4,5); (5,5); (6,5); (7,5); (8,5); (9,5)].

(** 100 has a slot for 5, and reads 5 again with [observer_latest] on observer 9. *)
Example unknown_held :
  ((fun w => (observer_view (observers_of w 5), source_slots (sources_of w 100))) <$>
     reads ([(100,5); (100,6)] ++ nine_readers ++ [(100,5)]) empty_world) =
  Some ([100; 1; 2; 3; 4; 5; 6; 7; 8; 9], [(5, Some 0); (6, Some 0); (5, None)]).
Proof. vm_compute. reflexivity. Qed.

(** 100 has no slot for 5: the same answer, and 100 gets its one edge. *)
Example unknown_unheld :
  ((fun w => (observer_view (observers_of w 5), source_slots (sources_of w 100))) <$>
     reads (nine_readers ++ [(100,6); (100,5)]) empty_world) =
  Some ([1; 2; 3; 4; 5; 6; 7; 8; 9; 100], [(6, Some 0); (5, Some 9)]).
Proof. vm_compute. reflexivity. Qed.

(** A, B, A from a run, with 101 also observing A. A trim moves 101's edge, and 101's slot follows. *)
Example aba_reachable_trim :
  ((fun w => (observer_view (observers_of w 5), observer_view (observers_of w 6), source_slots (sources_of w 100),
              source_slots (sources_of w 101))) <$>
     (reads [(100,5); (100,6); (100,5); (101,5)] empty_world ≫= fun w =>
      sources_end_run 100 (sources_begin_run 100 w))) =
  Some ([101], [], [], [(5, Some 0)]).
Proof. vm_compute. reflexivity. Qed.

(** The list of [sources_trim_moves_own_edge], with A = 5 and B = 6. *)
Definition aba_world : EdgeWorld :=
  set_sources 100 {| source_slots := [(5, Some 0); (6, Some 0); (5, Some 1)]; source_cursor := 0 |}
    (set_observers 5 {| observer_items := [(100, 0); (100, 2)]; observer_latest := 1 |}
       (set_observers 6 {| observer_items := [(100, 1)]; observer_latest := 0 |} empty_world)).

Lemma aba_world_match : edge_match aba_world.
Proof.
  intros c i s h. unfold aba_world.
  rewrite slot_at_set_sources, observers_of_set_sources, slot_at_set_observers, slot_at_set_observers.
  rewrite !observers_of_set_observers.
  assert (Hempty : forall c' i', slot_at empty_world c' i' = None).
  { intros c' i'. unfold slot_at, sources_of. simpl. rewrite lookup_empty. simpl.
    rewrite lookup_nil. done. }
  assert (Hobs : forall z q, observer_items (observers_of empty_world z) !! q = None).
  { intros z q. unfold observers_of. simpl. rewrite lookup_empty. simpl. apply lookup_nil. }
  rewrite Hempty.
  destruct (decide (s = 5)) as [->|Hs5]; [|destruct (decide (s = 6)) as [->|Hs6]]; simpl.
  - destruct (decide (c = 100)) as [->|Hc]; simpl.
    + destruct i as [|[|[|i]]], h as [|[|h]]; simpl; split; intros Heq; simplify_eq; done.
    + destruct h as [|[|h]]; simpl; split; intros Heq; simplify_eq; done.
  - destruct (decide (c = 100)) as [->|Hc]; simpl.
    + destruct i as [|[|[|i]]], h as [|h]; simpl; split; intros Heq; simplify_eq; done.
    + destruct h as [|h]; simpl; split; intros Heq; simplify_eq; done.
  - rewrite Hobs. destruct (decide (c = 100)) as [->|Hc]; simpl; [|done].
    destruct i as [|[|[|i]]]; simpl; split; intros Heq; simplify_eq; done.
Qed.

(** [aba_world] meets the premises of [sources_trim_moves_own_edge]. *)
Example aba_world_premises :
  edge_match aba_world /\
  source_slots (sources_of aba_world 100) = [(5, Some 0); (6, Some 0); (5, Some 1)] /\
  S 1 = length (observer_items (observers_of aba_world 5)).
Proof. split; [apply aba_world_match|]. vm_compute. auto. Qed.

(** [trimFrom] reading every handle before its first removal. *)
Fixpoint snapshot_trim (self : node_id) (l : list (node_id * option nat)) (w : EdgeWorld) : option EdgeWorld :=
  match l with
  | [] => Some w
  | (s, Some h) :: r => remove_edge self s h w ≫= snapshot_trim self r
  | (_, None) :: r => snapshot_trim self r w
  end.

(** Handles read before the removals fail on that list: the later A slot holds a stale handle. *)
Example snapshot_trim_fails :
  (fun _ => ()) <$> snapshot_trim 100 (source_slots (sources_of aba_world 100)) aba_world = None.
Proof. vm_compute. reflexivity. Qed.
