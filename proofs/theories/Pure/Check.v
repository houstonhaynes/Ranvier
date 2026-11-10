From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Edges Marking Engine.

Open Scope nat_scope.

(** ** What a resolution claims *)

(** The views of [c]'s tracked reads of [y] since [y] last entered [c]'s read prefix. *)
Definition seen_of (g : ghost) (c y : node_id) : list read_view := default [] (gh_seen g !! (c, y)).

(** A source of [n] has a value, status or failure different from one of [n]'s tracked reads of it, or a
    slot of [n] holds a source with no read. *)
Definition moved_since_last_run (s : state) (n : node_id) : Prop :=
  exists src i mh d,
    slot_at (g_edges s) n i = Some (src, mh) /\ g_nodes s !! src = Some d /\
    (seen_of (g_ghost s) n src = [] \/
     exists v, v ∈ seen_of (g_ghost s) n src /\ v <> read_view_of d).

(** The premise [resolve_clean_unmoved] takes: [n]'s reads have no unnoticed difference, and neither [n]
    nor a source of [n] is running. *)
Definition reads_settled (s : state) (n : node_id) : Prop :=
  n ∉ gh_unnoticed (g_ghost s) /\ n ∉ g_running s /\
  forall src, src ∈ fst <$> source_slots (sources_of (g_edges s) n) -> src ∉ g_running s.

(** ** Source lists, without their handles *)

(**md
The sources of the slots of `c`, in order, with its cursor.
*)
Definition slot_view (w : EdgeWorld) (c : node_id) : list node_id * nat :=
  (fst <$> source_slots (sources_of w c), source_cursor (sources_of w c)).

Lemma read_prefix_view w c : read_prefix w c = take (slot_view w c).2 (slot_view w c).1.
Proof. unfold read_prefix, slot_view. simpl. apply fmap_take. Qed.

Lemma slots_view w c : fst <$> source_slots (sources_of w c) = (slot_view w c).1.
Proof. done. Qed.

Lemma slots_length w c : length (source_slots (sources_of w c)) = length (slot_view w c).1.
Proof. unfold slot_view. simpl. by rewrite length_fmap. Qed.

Lemma slot_view_set_observers s o w c : slot_view (set_observers s o w) c = slot_view w c.
Proof. unfold slot_view. by rewrite sources_of_set_observers. Qed.

Lemma fmap_fst_alter (l : list (node_id * option nat)) i mh :
  fst <$> alter (fun p : node_id * option nat => (p.1, mh)) i l = fst <$> l.
Proof.
  revert i. induction l as [|a l IH]; intros [|i]; simpl; try done. f_equal. apply IH.
Qed.

Lemma slot_view_set_handle c i mh w c' : slot_view (set_handle c i mh w) c' = slot_view w c'.
Proof.
  unfold slot_view, set_handle. rewrite sources_of_set_sources.
  destruct (decide (c' = c)) as [->|]; simpl; [|done]. by rewrite fmap_fst_alter.
Qed.

Lemma slot_view_remove_slot self i w w' c :
  sources_remove_slot self i w = Some w' -> slot_view w' c = slot_view w c.
Proof.
  unfold sources_remove_slot. destruct (slot_at w self i) as [[s [h|]]|]; intros Hr; try (by simplify_eq).
  unfold remove_edge in Hr. destruct (observers_remove_at self h (observers_of w s)) as [[o' [[[m ms] h']|]]|];
    simpl in Hr; simplify_eq.
  - unfold sources_moved_in. by rewrite !slot_view_set_handle, slot_view_set_observers.
  - by rewrite slot_view_set_handle, slot_view_set_observers.
Qed.

Lemma slot_view_trim_loop self i n w w' c :
  trim_loop self i n w = Some w' -> slot_view w' c = slot_view w c.
Proof.
  revert i w. induction n as [|n IH]; intros i w Hr; simpl in Hr; [by simplify_eq|].
  destruct (sources_remove_slot self i w) as [w1|] eqn:E; simpl in Hr; [|done].
  rewrite (IH _ _ Hr). by eapply slot_view_remove_slot.
Qed.

Lemma slot_view_trim self from w w' c :
  sources_trim_from self from w = Some w' ->
  slot_view w' c =
    if decide (c = self) then (take from (slot_view w c).1, (slot_view w c).2) else slot_view w c.
Proof.
  unfold sources_trim_from. intros Hr. destruct (trim_loop _ _ _ _) as [w1|] eqn:E; simpl in Hr; [|done]. simplify_eq.
  pose proof (slot_view_trim_loop _ _ _ _ _ c E) as Hv.
  destruct (decide (c = self)) as [->|Hne].
  - rewrite <- Hv. unfold slot_view. rewrite sources_of_set_sources, decide_True by done. simpl.
    by rewrite fmap_take.
  - rewrite <- Hv. unfold slot_view. by rewrite sources_of_set_sources, decide_False by done.
Qed.

Lemma slot_view_end self w w' c :
  sources_end_run self w = Some w' ->
  (slot_view w self).2 <= length (slot_view w self).1 ->
  slot_view w' c =
    if decide (c = self) then (take (slot_view w c).2 (slot_view w c).1, (slot_view w c).2)
    else slot_view w c.
Proof.
  unfold sources_end_run. intros Hr Hle.
  destruct (decide (source_cursor (sources_of w self) < length (source_slots (sources_of w self)))) as [Hl|Hl].
  - rewrite (slot_view_trim _ _ _ _ _ Hr). destruct (decide (c = self)) as [->|]; done.
  - simplify_eq. destruct (decide (c = self)) as [->|]; [|done].
    rewrite slots_length in Hl. change (source_cursor (sources_of w' self)) with (slot_view w' self).2 in Hl.
    rewrite take_ge by lia. by destruct (slot_view w' self).
Qed.

Lemma slot_view_clear self w w' c :
  sources_clear self w = Some w' ->
  slot_view w' c = if decide (c = self) then ([], 0) else slot_view w c.
Proof.
  unfold sources_clear. intros Hr. destruct (sources_trim_from self 0 w) as [w1|] eqn:E; simpl in Hr; [|done]. simplify_eq.
  pose proof (slot_view_trim _ _ _ _ c E) as Hv.
  unfold slot_view at 1. rewrite sources_of_set_sources.
  destruct (decide (c = self)) as [->|Hne]; simpl.
  - rewrite slots_view, Hv. simpl. by rewrite take_0.
  - by rewrite <- Hv.
Qed.

Lemma slot_view_begin c w c' :
  slot_view (sources_begin_run c w) c' =
    if decide (c' = c) then ((slot_view w c').1, 0) else slot_view w c'.
Proof.
  unfold slot_view, sources_begin_run. rewrite sources_of_set_sources. destruct (decide (c' = c)) as [->|]; done.
Qed.

Lemma slot_view_append self s mh o w c :
  slot_view (sources_append self s mh o w) c =
    if decide (c = self) then ((slot_view w c).1 ++ [s], S (length (slot_view w c).1))
    else slot_view w c.
Proof.
  unfold sources_append, slot_view. rewrite sources_of_set_observers, sources_of_set_sources.
  destruct (decide (c = self)) as [->|]; simpl; [|done].
  by rewrite fmap_app, length_fmap.
Qed.

Lemma slot_view_link self s w c :
  slot_view (sources_link self s w) c =
    if decide (c = self) then ((slot_view w c).1 ++ [s], S (length (slot_view w c).1))
    else slot_view w c.
Proof.
  destruct (sources_link_cases self s w) as [[-> _]|[-> _]]; apply slot_view_append.
Qed.

Lemma elem_take (l : list node_id) n x : x ∈ take n l -> x ∈ l.
Proof. intros H. rewrite <- (take_drop n l). apply elem_of_app. by left. Qed.

(** What a tracked read does to the slots. *)
Lemma slot_view_add self s w w' :
  (slot_view w self).2 <= length (slot_view w self).1 ->
  sources_add self s w = Some w' ->
  (forall c, c <> self -> slot_view w' c = slot_view w c) /\
  (slot_view w' self).2 <= length (slot_view w' self).1 /\
  (forall x, x ∈ (slot_view w' self).1 -> x ∈ (slot_view w self).1 \/ x = s) /\
  (forall x, x ∈ read_prefix w' self -> x ∈ read_prefix w self \/ x = s).
Proof.
  intros Hle. unfold sources_add.
  destruct (decide (fst <$> source_slots (sources_of w self) !! source_cursor (sources_of w self) = Some s)) as [Hp|Hp].
  - intros Hr. simplify_eq.
    assert (Hv : forall c, slot_view (set_sources self {| source_slots := source_slots (sources_of w self);
                   source_cursor := S (source_cursor (sources_of w self)) |} w) c =
                 if decide (c = self) then ((slot_view w c).1, S (slot_view w c).2) else slot_view w c).
    { intros c. unfold slot_view. rewrite sources_of_set_sources. by destruct (decide (c = self)) as [->|]. }
    assert (Hlt : source_cursor (sources_of w self) < length (source_slots (sources_of w self))).
    { apply lookup_lt_is_Some_1. destruct (source_slots (sources_of w self) !! source_cursor (sources_of w self)) eqn:E;
        simpl in Hp; [by eexists|done]. }
    assert (Hlk : (slot_view w self).1 !! (slot_view w self).2 = Some s).
    { unfold slot_view. simpl. rewrite list_lookup_fmap. done. }
    split_and!.
    + intros c Hc. by rewrite Hv, decide_False.
    + rewrite Hv, decide_True by done. unfold slot_view. simpl. rewrite length_fmap. lia.
    + intros x. rewrite Hv, decide_True by done. cbn [fst snd]. auto.
    + intros x. rewrite !read_prefix_view, Hv, decide_True by done. cbn [fst snd].
      rewrite (take_S_r _ _ s) by done. rewrite elem_of_app, list_elem_of_singleton. tauto.
  - destruct (decide (0 < source_cursor (sources_of w self) /\
                      fst <$> source_slots (sources_of w self) !! pred (source_cursor (sources_of w self)) = Some s)).
    { intros Hr. simplify_eq. split_and!; auto. }
    intros Hr. destruct (sources_end_run self w) as [w1|] eqn:E; simpl in Hr; simplify_eq.
    pose proof (slot_view_end _ _ _ self E Hle) as Hself.
    rewrite decide_True in Hself by done.
    split_and!.
    + intros c Hc. rewrite slot_view_link, decide_False by done.
      pose proof (slot_view_end _ _ _ c E Hle) as Hv. by rewrite decide_False in Hv.
    + rewrite slot_view_link, decide_True by done. cbn [fst snd]. rewrite length_app. simpl. lia.
    + intros x. rewrite slot_view_link, decide_True by done. cbn [fst snd].
      rewrite elem_of_app, list_elem_of_singleton, Hself. cbn [fst snd].
      intros [Hx|Hx]; [left|by right]. by eapply elem_take.
    + intros x. rewrite !read_prefix_view, slot_view_link, decide_True by done. cbn [fst snd].
      rewrite Hself. cbn [fst snd]. rewrite take_ge by (rewrite length_app; simpl; lia).
      rewrite elem_of_app, list_elem_of_singleton. tauto.
Qed.

Lemma read_prefix_in w c x : x ∈ read_prefix w c -> x ∈ fst <$> source_slots (sources_of w c).
Proof. rewrite read_prefix_view. apply elem_take. Qed.

(** ** The invariant *)

(**md
Node `n` exists in `s` with kind `k`.
*)
Definition has_kind (n : node_id) (k : kind) (s : state) : Prop :=
  exists d, g_nodes s !! n = Some d /\ n_kind d = k.

(** A memo with no [Dirty] and no unnoticed difference read each source's published view, and a node outside
    any run has published its view. [x] is a node yet to publish, and [e] a node whose new view its observers
    have yet to take. A live, [Clean] memo holds [n_unannounced] only inside its latest [gh_window]. *)
Record check_invariant_except (x e : option node_id) (s : state) : Prop := {
  ci_reachable : world_reachable (g_edges s);
  ci_cursor : forall c, (slot_view (g_edges s) c).2 <= length (slot_view (g_edges s) c).1;
  ci_ended : forall c, has_kind c KMemo s -> c ∉ g_running s ->
    (slot_view (g_edges s) c).2 = length (slot_view (g_edges s) c).1;
  ci_next : forall n, is_Some (g_nodes s !! n) -> n <= g_next_id s;
  ci_slots : forall c y, y ∈ (slot_view (g_edges s) c).1 ->
    is_Some (g_nodes s !! c) /\ (has_kind y KMemo s \/ has_kind y KSignal s);
  ci_running : forall c, c ∈ g_running s -> is_Some (g_nodes s !! c);
  ci_current : forall c, g_current s = Some c -> c ∈ g_running s;
  ci_queue : forall y, y ∈ g_queue s -> has_kind y KEffect s;
  ci_published : forall n d, g_nodes s !! n = Some d -> n_kind d <> KEffect -> n ∉ g_running s ->
    x <> Some n -> gh_published (g_ghost s) !! n = Some (read_view_of d);
  ci_seen : forall c d y, g_nodes s !! c = Some d -> n_kind d = KMemo -> y ∈ read_prefix (g_edges s) c ->
    e <> Some y ->
    (forall v, v ∈ seen_of (g_ghost s) c y -> gh_published (g_ghost s) !! y = Some v) \/ n_freshness d = Dirty \/
    c ∈ gh_unnoticed (g_ghost s);
  ci_read : forall c y, y ∈ read_prefix (g_edges s) c -> seen_of (g_ghost s) c y <> [];
  ci_window_dom : forall n r, gh_window (g_ghost s) !! n = Some r -> is_Some (g_nodes s !! n);
  ci_window_le : forall n d r, g_nodes s !! n = Some d -> gh_window (g_ghost s) !! n = Some r ->
    r <= n_runs d;
  ci_unannounced : forall n d, g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d = Clean ->
    n_unannounced d = true -> n_disposed d = false ->
    exists r, gh_window (g_ghost s) !! n = Some r /\ n_runs d <= r
}.

Notation check_invariant_unpublished x := (check_invariant_except x None).

(**md
**What the check invariant says.**

The invariant relates a memo's reads to what its sources last told their observers. It says nothing of whether a stored value is up to date.

- **Reads.** A signal or memo outside any run has told its observers its stored value, status and failure. Each source a memo has read in its latest run has a recorded read. Each recorded read equals what the source last told its observers, unless the memo is `Dirty` or listed in `gh_unnoticed`.
- **The announcement flag.** A live `Clean` memo holds `n_unannounced` only when it has not run since `RecomputeScoped` last cleared its `Dirty`.
- **Bookkeeping.** The edges are ones the source-list operations can build from empty. Cursors lie within their lists, and a memo outside any run has its cursor at the end. Slots name existing computations and hold only signals and memos. Running nodes exist, the current computation is running, and the queue holds only effects.
*)
Definition check_invariant : state -> Prop := check_invariant_unpublished None.

(** The parts of a state [memo_frame] reads. *)
Definition nodes_and_windows (s : state) : gmap node_id node * gmap node_id nat := (g_nodes s, gh_window (g_ghost s)).

(** The latest window of [n] has seen a run since it opened. *)
Definition closed (p : gmap node_id node * gmap node_id nat) (n : node_id) : Prop :=
  forall d r, p.1 !! n = Some d -> p.2 !! n = Some r -> r < n_runs d.

(**md
The node `d` is disposed, has a closed window, or holds no announcement.
*)
Definition settled_flag (p : gmap node_id node * gmap node_id nat) (n : node_id) (d : node) : Prop :=
  n_disposed d = true \/ closed p n \/ n_unannounced d = false.

(** What the engine holds of a memo from [p] to [q]. A memo out of [Clean] that ends [Clean] ends with
    [settled_flag]. *)
Definition memo_frame (p q : gmap node_id node * gmap node_id nat) (m : node_id) : Prop :=
  forall d, p.1 !! m = Some d -> n_kind d = KMemo ->
    exists d', q.1 !! m = Some d' /\ n_kind d' = KMemo /\ n_runs d <= n_runs d' /\
      (n_disposed d = true -> n_disposed d' = true) /\
      (q.2 !! m = p.2 !! m \/ closed q m \/ n_disposed d' = true) /\
      (settled_flag p m d -> settled_flag q m d') /\
      (n_freshness d <> Clean -> n_freshness d' = Clean -> settled_flag q m d').

Lemma memo_frame_refl p m : memo_frame p p m.
Proof. intros d Hd Hk. exists d. split_and!; auto. intros H1 H2. done. Qed.

Lemma closed_keep p q n d d' :
  closed p n -> q.2 !! n = p.2 !! n -> p.1 !! n = Some d -> q.1 !! n = Some d' ->
  n_runs d <= n_runs d' -> closed q n.
Proof.
  intros Hc Hw Hd Hd' Hle e r He Hr. rewrite Hd' in He. injection He as <-.
  rewrite Hw in Hr. pose proof (Hc d r Hd Hr). lia.
Qed.

Lemma memo_frame_trans p q t m : memo_frame p q m -> memo_frame q t m -> memo_frame p t m.
Proof.
  intros H1 H2 d Hd Hk.
  destruct (H1 d Hd Hk) as (d1 & Hd1 & Hk1 & Hr1 & Hm1 & Hw1 & Hg1 & Hs1).
  destruct (H2 d1 Hd1 Hk1) as (d2 & Hd2 & Hk2 & Hr2 & Hm2 & Hw2 & Hg2 & Hs2).
  exists d2. split_and!; [done|done|lia|auto| |auto|].
  - destruct Hw2 as [Hw2|[Hw2|Hw2]]; [|by right; left|by right; right].
    destruct Hw1 as [Hw1|[Hw1|Hw1]].
    + left. congruence.
    + right. left. eapply closed_keep; eauto.
    + right. right. auto.
  - intros Hc Hc2. destruct (decide (n_freshness d1 = Clean)) as [Hc1|Hc1]; [apply Hg2; auto|auto].
Qed.

(** What every engine function returns unchanged. *)
Definition frame (s s' : state) : Prop :=
  g_running s' = g_running s /\ g_current s' = g_current s /\
  (forall n k, has_kind n k s -> has_kind n k s') /\
  forall m, memo_frame (nodes_and_windows s) (nodes_and_windows s') m.

Lemma frame_refl s : frame s s.
Proof. split_and!; auto. intros m. apply memo_frame_refl. Qed.

Lemma frame_trans s1 s2 s3 : frame s1 s2 -> frame s2 s3 -> frame s1 s3.
Proof.
  intros (H1 & H2 & H3 & H4) (H1' & H2' & H3' & H4'). split_and!; try congruence; auto.
  intros m. eapply memo_frame_trans; eauto.
Qed.

Lemma invariant_weaken x s : check_invariant s -> check_invariant_unpublished x s.
Proof.
  intros H. destruct H. split; try done. intros n d ???? . eauto using ci_published0.
Qed.

Lemma invariant_running n s : check_invariant_unpublished (Some n) s -> n ∈ g_running s -> check_invariant s.
Proof.
  intros H Hr. destruct H. split; try done. intros m d Hd Hk Hm _.
  apply (ci_published0 m d); try done. intros Heq. simplify_eq.
Qed.

(** ** Updates holding the invariant *)

(** Fields the invariant does not read. *)
Definition irrelevant (s s' : state) : Prop :=
  g_edges s' = g_edges s /\ g_nodes s' = g_nodes s /\ g_ghost s' = g_ghost s /\
  g_running s' = g_running s /\ g_current s' = g_current s /\ g_next_id s' = g_next_id s /\
  g_queue s' = g_queue s.

Lemma has_kind_same n k s s' : g_nodes s' = g_nodes s -> has_kind n k s -> has_kind n k s'.
Proof. unfold has_kind. intros ->. done. Qed.

Lemma invariant_irrelevant x e s s' : irrelevant s s' -> check_invariant_except x e s -> check_invariant_except x e s'.
Proof.
  intros (He & Hn & Hg & Hr & Hc & Hx & Hq) H. destruct H.
  split; unfold has_kind in *; rewrite ?He, ?Hn, ?Hg, ?Hr, ?Hc, ?Hx, ?Hq; done.
Qed.

Lemma frame_same s s' :
  g_running s' = g_running s -> g_current s' = g_current s -> nodes_and_windows s' = nodes_and_windows s -> frame s s'.
Proof.
  intros Hr Hc Hn. split_and!; try done.
  - intros n k. apply has_kind_same. unfold nodes_and_windows in Hn. by injection Hn.
  - intros m. rewrite Hn. apply memo_frame_refl.
Qed.

Lemma frame_irrelevant s s' : irrelevant s s' -> frame s s'.
Proof.
  intros (He & Hn & Hg & Hr & Hc & Hx & Hq). apply frame_same; try done.
  unfold nodes_and_windows. by rewrite Hn, Hg.
Qed.

Local Ltac gs := cbn [g_edges g_nodes g_ghost g_running g_current g_next_id g_queue gh_published gh_seen gh_unnoticed gh_window].

Local Ltac irr := unfold irrelevant; simpl; split_and!; reflexivity.

(** The changes to a memo the engine makes outside a run's start and end. *)
Definition memo_step (u : node -> node) (d : node) : Prop :=
  n_runs d <= n_runs (u d) /\ (n_disposed d = true -> n_disposed (u d) = true) /\
  (n_unannounced (u d) = false \/ n_unannounced (u d) = n_unannounced d) /\
  (n_freshness d <> Clean -> n_freshness (u d) = Clean -> n_unannounced (u d) = false).

(** A change to a node that keeps its kind, its view unless an effect, and a memo's [Dirty]. *)
Definition node_step (u : node -> node) (d : node) : Prop :=
  n_kind (u d) = n_kind d /\
  (n_kind d <> KEffect -> read_view_of (u d) = read_view_of d) /\
  (n_kind d = KMemo -> n_freshness d = Dirty -> n_freshness (u d) = Dirty) /\
  n_runs d <= n_runs (u d) /\
  (n_kind d = KMemo -> memo_step u d) /\
  (n_kind d = KMemo -> n_freshness (u d) = Clean -> n_unannounced (u d) = true -> n_disposed (u d) = false ->
     n_freshness d = Clean /\ n_unannounced d = true /\ n_disposed d = false /\ n_runs (u d) = n_runs d).

Local Ltac ms :=
  unfold memo_step; split_and!; try done; intros; simpl in *;
  first [lia | done | congruence | by right | by left].

Local Ltac ns :=
  unfold node_step; split_and!; try done;
  try (intros; simpl in *; first [lia | done | congruence]);
  try (intros _; ms);
  try (intros; simpl in *; split_and!; done).

Lemma has_kind_set n d d' m k s :
  g_nodes s !! n = Some d -> n_kind d' = n_kind d ->
  has_kind m k s -> has_kind m k (set_node n d' s).
Proof.
  intros Hd Hk (e & He & Hek). unfold has_kind. simpl.
  destruct (decide (m = n)) as [->|Hne].
  - rewrite lookup_insert_eq. eexists. split; [done|]. simplify_eq. congruence.
  - rewrite lookup_insert_ne by done. eauto.
Qed.

Lemma has_kind_set_back n d d' m k s :
  g_nodes s !! n = Some d -> n_kind d' = n_kind d ->
  has_kind m k (set_node n d' s) -> has_kind m k s.
Proof.
  intros Hd Hk (e & He & Hek). unfold has_kind. simpl in He.
  destruct (decide (m = n)) as [->|Hne].
  - rewrite lookup_insert_eq in He. simplify_eq. eexists. split; [done|]. congruence.
  - rewrite lookup_insert_ne in He by done. eauto.
Qed.

Lemma is_Some_set m n d s : is_Some (g_nodes s !! n) -> is_Some (g_nodes s !! m) -> is_Some (g_nodes (set_node n d s) !! m).
Proof.
  intros _ H. simpl. destruct (decide (m = n)) as [->|]; [rewrite lookup_insert_eq; by eexists|].
  by rewrite lookup_insert_ne.
Qed.

Lemma is_Some_set_back m n d s : is_Some (g_nodes s !! n) -> is_Some (g_nodes (set_node n d s) !! m) -> is_Some (g_nodes s !! m).
Proof.
  intros Hn. simpl. destruct (decide (m = n)) as [->|Hne]; [done|]. by rewrite lookup_insert_ne.
Qed.

Lemma invariant_set_gen x e n u d s :
  check_invariant_except x e s -> g_nodes s !! n = Some d -> n_kind (u d) = n_kind d ->
  (x <> Some n -> n_kind d <> KEffect -> n ∉ g_running s -> read_view_of (u d) = read_view_of d) ->
  (n_kind d = KMemo -> n_freshness d = Dirty -> n_freshness (u d) = Dirty \/ n ∈ gh_unnoticed (g_ghost s)) ->
  n_runs d <= n_runs (u d) ->
  (n_kind d = KMemo -> n_freshness (u d) = Clean -> n_unannounced (u d) = true -> n_disposed (u d) = false ->
     (n_freshness d = Clean /\ n_unannounced d = true /\ n_disposed d = false /\ n_runs (u d) = n_runs d) \/
     exists r, gh_window (g_ghost s) !! n = Some r /\ n_runs (u d) <= r) ->
  check_invariant_except x e (set_node n (u d) s).
Proof.
  intros H Hd Hk Hv Hf Hr Hu. destruct H.
  assert (Hsome : is_Some (g_nodes s !! n)) by eauto.
  unfold set_node. split; gs; try done.
  - intros c Hc. apply ci_ended0. exact (has_kind_set_back n d (u d) c KMemo s Hd Hk Hc).
  - intros m Hm. apply ci_next0. exact (is_Some_set_back m n (u d) s Hsome Hm).
  - intros c y Hy. destruct (ci_slots0 c y Hy) as [Hc Hyk]. split.
    + exact (is_Some_set c n (u d) s Hsome Hc).
    + destruct Hyk; [left|right]; eapply (has_kind_set n d (u d)); eauto.
  - intros c Hc. exact (is_Some_set c n (u d) s Hsome (ci_running0 c Hc)).
  - intros y Hy. eapply (has_kind_set n d (u d)); eauto.
  - intros m e'. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros Heq Hek Hrn Hx. simplify_eq.
      rewrite Hk in Hek. rewrite Hv by done. eauto.
    + rewrite lookup_insert_ne by done. eauto.
  - intros c e' y. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros Heq Hek Hy Hex. simplify_eq. rewrite Hk in Hek.
      destruct (ci_seen0 n d y Hd Hek Hy Hex) as [H|[H|H]]; [by left|..|by right; right].
      destruct (Hf Hek H) as [H1|H1]; [by right; left|by right; right].
    + rewrite lookup_insert_ne by done. eauto.
  - intros m r Hw. exact (is_Some_set m n (u d) s Hsome (ci_window_dom0 m r Hw)).
  - intros m e' r. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros Heq Hw. simplify_eq.
      pose proof (ci_window_le0 n d r Hd Hw). lia.
    + rewrite lookup_insert_ne by done. eauto.
  - intros m e'. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros Heq Hek Hc Hun Hl. simplify_eq. rewrite Hk in Hek.
      destruct (Hu Hek Hc Hun Hl) as [(H1 & H2 & H3 & H4)|H1]; [|done].
      rewrite H4. eauto.
    + rewrite lookup_insert_ne by done. eauto.
Qed.

Lemma invariant_set x e n u d s :
  check_invariant_except x e s -> g_nodes s !! n = Some d -> node_step u d -> check_invariant_except x e (set_node n (u d) s).
Proof.
  intros H Hd (Hk & Hv & Hf & Hr & _ & Hu). apply invariant_set_gen; auto.
Qed.

Lemma frame_set n u d s :
  g_nodes s !! n = Some d -> n_kind (u d) = n_kind d -> (n_kind d = KMemo -> memo_step u d) ->
  frame s (set_node n (u d) s).
Proof.
  intros Hd Hk Hm. split_and!; try done.
  - intros m k. by apply has_kind_set with d.
  - intros m e. unfold nodes_and_windows. cbn [fst snd g_nodes g_ghost set_node].
    destruct (decide (m = n)) as [->|Hne].
    + intros He Hek. rewrite Hd in He. injection He as <-.
      destruct (Hm Hek) as (H1 & H2 & H3 & H4).
      assert (Hkeep : closed (g_nodes s, gh_window (g_ghost s)) n ->
                      closed (<[n := u d]> (g_nodes s), gh_window (g_ghost s)) n).
      { intros Hc. eapply closed_keep; [exact Hc|done|exact Hd|apply lookup_insert_eq|done]. }
      exists (u d). rewrite lookup_insert_eq. split_and!; [done|congruence|done|done|by left| |].
      * intros [G|[G|G]]; [left; auto|right; left; auto|].
        destruct H3 as [H3|H3]; [right; right; done|right; right; congruence].
      * intros Hc Hc'. right. right. auto.
    + rewrite lookup_insert_ne by done. intros He Hek. exists e.
      split_and!; auto.
      * intros [G|[G|G]]; [by left|right; left|by right; right].
        intros e' r Hl Hw. simpl in Hl, Hw. rewrite lookup_insert_ne in Hl by done. exact (G e' r Hl Hw).
      * intros H1 H2. done.
Qed.

Lemma frame_node n u d s :
  g_nodes s !! n = Some d -> node_step u d -> frame s (set_node n (u d) s).
Proof. intros Hd (Hk & _ & _ & _ & Hm & _). by apply frame_set. Qed.

(** ** Marks hold the invariant *)

Lemma invariant_enqueue x e n s : has_kind n KEffect s -> check_invariant_except x e s -> check_invariant_except x e (enqueue n s).
Proof.
  intros Hn H. destruct H. split; simpl; try done.
  intros y Hy. apply (has_kind_same y KEffect s); [done|].
  apply elem_of_app in Hy as [Hy|Hy]; [auto|]. apply list_elem_of_singleton in Hy. by subst.
Qed.

Local Lemma freshness_rise f d : freshness_le (n_freshness d) f -> n_freshness d = Dirty -> f = Dirty.
Proof. intros Hle Hd. rewrite Hd in Hle. destruct f; unfold freshness_le in Hle; simpl in Hle; try lia; done. Qed.

Local Lemma freshness_rise_clean d : freshness_le (n_freshness d) Clean -> n_freshness d = Clean.
Proof. destruct (n_freshness d); unfold freshness_le; simpl; try lia; done. Qed.

Local Lemma marked_step f u q d :
  freshness_le (n_freshness d) f -> (u = n_unannounced d \/ u = false) -> node_step (node_marked f u q) d.
Proof.
  intros Hle Hu. unfold node_step, memo_step. simpl. split_and!.
  - done.
  - done.
  - intros _ H. by eapply freshness_rise.
  - lia.
  - intros _. split_and!.
    + lia.
    + done.
    + destruct Hu; auto.
    + intros H1 ->. by apply freshness_rise_clean in Hle.
  - intros _ -> Hun Hl. apply freshness_rise_clean in Hle. destruct Hu as [Hu|Hu]; subst u; [|done].
    split_and!; done.
Qed.

Local Lemma marked_effect f u q d :
  n_kind d = KEffect -> freshness_le (n_freshness d) f -> node_step (node_marked f u q) d.
Proof.
  intros Hk Hle. unfold node_step. simpl. split_and!; try done; intros; congruence || lia.
Qed.

Lemma prim_invariant x e s s' : mark_prim s s' -> check_invariant_except x e s -> check_invariant_except x e s' /\ frame s s'.
Proof.
  intros Hp H.
  destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hq Hf|s n d f Hd Hk Hq Hf].
  - pose proof (marked_step f u (n_queued d) d Hf Hu) as Hs.
    split; [by apply (invariant_set x e n (node_marked f u (n_queued d)) d)|].
    by apply (frame_node n (node_marked f u (n_queued d)) d).
  - pose proof (marked_effect f (n_unannounced d) true d Hk Hf) as Hs.
    split; [by apply (invariant_set x e n (node_marked f (n_unannounced d) true) d)|].
    by apply (frame_node n (node_marked f (n_unannounced d) true) d).
  - pose proof (marked_effect f (n_unannounced d) true d Hk Hf) as Hstep.
    assert (Hs : check_invariant_except x e (set_node n (node_marked f (n_unannounced d) true d) s)).
    { by apply (invariant_set x e n (node_marked f (n_unannounced d) true) d). }
    assert (Hfr : frame s (set_node n (node_marked f (n_unannounced d) true d) s)).
    { by apply (frame_node n (node_marked f (n_unannounced d) true) d). }
    split.
    + apply invariant_enqueue; [|done]. destruct Hfr as (_ & _ & Hka & _). apply Hka. by exists d.
    + eapply frame_trans; [exact Hfr|]. by apply frame_same.
Qed.

Lemma marks_invariant x e s s' : rtc mark_prim s s' -> check_invariant_except x e s -> check_invariant_except x e s' /\ frame s s'.
Proof.
  induction 1 as [s|s1 s2 s3 Hp _ IH]; intros H; [split; [done|apply frame_refl]|].
  destruct (prim_invariant _ _ _ _ Hp H) as [H2 F2]. destruct (IH H2) as [H3 F3].
  split; [done|]. by eapply frame_trans.
Qed.

(** A mark holds each node's view, the edges and the ghost state. *)
Lemma prim_holds s s' : mark_prim s s' ->
  g_edges s' = g_edges s /\ g_ghost s' = g_ghost s /\
  forall m d, g_nodes s !! m = Some d ->
    exists d', g_nodes s' !! m = Some d' /\ n_kind d' = n_kind d /\ read_view_of d' = read_view_of d /\
               (n_freshness d = Dirty -> n_freshness d' = Dirty).
Proof.
  intros Hp.
  destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hq Hf|s n d f Hd Hk Hq Hf];
    (split_and!; [done|done|]); intros m e He; simpl;
    (destruct (decide (m = n)) as [->|Hne];
     [rewrite lookup_insert_eq; eexists; split_and!; [done|..]; simplify_eq; simpl; try done; intros; by eapply freshness_rise
     |rewrite lookup_insert_ne by done; eexists; split_and!; eauto]).
Qed.

Lemma marks_holds s s' : rtc mark_prim s s' ->
  g_edges s' = g_edges s /\ g_ghost s' = g_ghost s /\
  forall m d, g_nodes s !! m = Some d ->
    exists d', g_nodes s' !! m = Some d' /\ n_kind d' = n_kind d /\ read_view_of d' = read_view_of d /\
               (n_freshness d = Dirty -> n_freshness d' = Dirty).
Proof.
  induction 1 as [s|s1 s2 s3 Hp _ IH].
  - split_and!; [done|done|]. intros m d Hd. eexists. split_and!; eauto.
  - destruct (prim_holds _ _ Hp) as (He & Hg & Hn). destruct IH as (He' & Hg' & Hn').
    split_and!; try congruence. intros m d Hd.
    destruct (Hn _ _ Hd) as (d2 & Hd2 & Hk & Hv & Hf).
    destruct (Hn' _ _ Hd2) as (d3 & Hd3 & Hk' & Hv' & Hf').
    exists d3. split_and!; [done|congruence|congruence|auto].
Qed.

(** ** Source-list steps *)

Lemma invariant_edges_common x s w' self :
  check_invariant_unpublished x s ->
  world_reachable w' ->
  (forall c, (slot_view w' c).2 <= length (slot_view w' c).1) ->
  (forall c, c <> self -> slot_view w' c = slot_view (g_edges s) c) ->
  (forall y, y ∈ (slot_view w' self).1 -> y ∈ (slot_view (g_edges s) self).1) ->
  (forall y, y ∈ read_prefix w' self -> y ∈ read_prefix (g_edges s) self) ->
  (has_kind self KMemo s -> self ∉ g_running s ->
     (slot_view w' self).2 = length (slot_view w' self).1) ->
  check_invariant_unpublished x (with_edges w' s).
Proof.
  intros H Hr Hcur Hoth Hsl Hpre Hend. destruct H. unfold with_edges.
  split; gs; try done.
  - intros c Hc Hnr. destruct (decide (c = self)) as [->|Hne]; [by apply Hend|].
    rewrite Hoth by done. by apply ci_ended0.
  - intros c y Hy. destruct (decide (c = self)) as [->|Hne]; [apply ci_slots0; by apply Hsl|].
    rewrite Hoth in Hy by done. eauto.
  - intros c d y Hd Hk Hy _. destruct (decide (c = self)) as [->|Hne]; [eapply ci_seen0; eauto|].
    eapply ci_seen0; eauto. rewrite read_prefix_view, Hoth in Hy by done.
    by rewrite read_prefix_view.
  - intros c y Hy. destruct (decide (c = self)) as [->|Hne]; [eauto|].
    apply ci_read0. rewrite read_prefix_view, Hoth in Hy by done. by rewrite read_prefix_view.
Qed.

Lemma invariant_end x n w' s :
  check_invariant_unpublished x s -> sources_end_run n (g_edges s) = Some w' ->
  check_invariant_unpublished x (with_edges w' s) /\ (slot_view w' n).2 = length (slot_view w' n).1.
Proof.
  intros H Hr. pose proof (ci_cursor _ _ _ H n) as Hle.
  pose proof (fun c => slot_view_end _ _ _ c Hr Hle) as Hv.
  assert (Hself : slot_view w' n = (take (slot_view (g_edges s) n).2 (slot_view (g_edges s) n).1,
                                    (slot_view (g_edges s) n).2)).
  { rewrite Hv, decide_True by done. done. }
  assert (Hlen : (slot_view w' n).2 = length (slot_view w' n).1).
  { rewrite Hself. cbn [fst snd]. rewrite length_take. lia. }
  split; [|done].
  eapply invariant_edges_common; [done|..].
  - eapply reachable_end; [apply (ci_reachable _ _ _ H)|done].
  - intros c. destruct (decide (c = n)) as [->|Hne]; [lia|].
    rewrite Hv, decide_False by done. apply (ci_cursor _ _ _ H).
  - intros c Hc. by rewrite Hv, decide_False.
  - intros y. rewrite Hself. cbn [fst snd]. apply elem_take.
  - intros y. rewrite !read_prefix_view. rewrite Hself. cbn [fst snd].
    rewrite take_ge by (rewrite length_take; lia). done.
  - done.
Qed.

Lemma invariant_clear x n w' s :
  check_invariant_unpublished x s -> sources_clear n (g_edges s) = Some w' ->
  check_invariant_unpublished x (with_edges w' s) /\ (slot_view w' n).2 = length (slot_view w' n).1.
Proof.
  intros H Hr. pose proof (fun c => slot_view_clear _ _ _ c Hr) as Hv.
  assert (Hself : slot_view w' n = ([], 0)) by (rewrite Hv, decide_True by done; done).
  split; [|by rewrite Hself].
  eapply invariant_edges_common; [done|..].
  - eapply reachable_clear; [apply (ci_reachable _ _ _ H)|done].
  - intros c. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; cbn [fst snd]; lia|].
    rewrite Hv, decide_False by done. apply (ci_cursor _ _ _ H).
  - intros c Hc. by rewrite Hv, decide_False.
  - intros y. rewrite Hself. cbn [fst snd]. intros Hy. by apply not_elem_of_nil in Hy.
  - intros y. rewrite read_prefix_view, Hself. cbn [fst snd]. intros Hy. by apply not_elem_of_nil in Hy.
  - by rewrite Hself.
Qed.

(** ** Hoare triples over the monad *)

(**md
`P` survives every `frame` step.
*)
Class Stable (P : state -> Prop) := stable : forall s s', frame s s' -> P s -> P s'.

Global Instance stable_true : Stable (fun _ => True).
Proof. by intros ???. Qed.

Global Instance stable_kind n k : Stable (has_kind n k).
Proof. intros s s' (_ & _ & H & _) Hk. auto. Qed.

(**md
From a state satisfying `check_invariant` and `P`, a completed step `m` ends in a state satisfying
`check_invariant`, related to the start by `frame`.

- [P:] the premise on the starting state.
- [m:] the step.
*)
Definition ok_under {A} (P : state -> Prop) (m : M A) : Prop :=
  forall s s' a, check_invariant s -> P s -> m s = Some (s', a) -> check_invariant s' /\ frame s s'.

Local Notation ok m := (ok_under (fun _ => True) m).

Lemma ok_any {A} P (m : M A) : ok m -> ok_under P m.
Proof. intros H s s' a Hi _ Hm. by eapply H. Qed.

Lemma ok_weaken {A} (P Q : state -> Prop) (m : M A) : (forall s, Q s -> P s) -> ok_under P m -> ok_under Q m.
Proof. intros HPQ H s s' a Hi HQ Hm. eapply H; eauto. Qed.

Lemma ok_bind {A B} P `{!Stable P} (m : M A) (k : A -> M B) :
  ok_under P m -> (forall a, ok_under P (k a)) -> ok_under P (m_bind m k).
Proof.
  intros Hm Hk s s' b Hi HP Hr. unfold m_bind in Hr.
  destruct (m s) as [[s1 a]|] eqn:E; [|done].
  destruct (Hm _ _ _ Hi HP E) as [Hi1 F1].
  destruct (Hk a _ _ _ Hi1 (stable _ _ F1 HP) Hr) as [Hi2 F2].
  split; [done|]. by eapply frame_trans.
Qed.

(** The step of a bind, for proofs that read the state in between. *)
Lemma run_bind {A B} P (m : M A) (k : A -> M B) s s' b :
  ok_under P m -> check_invariant s -> P s -> m_bind m k s = Some (s', b) ->
  exists s1 a, m s = Some (s1, a) /\ check_invariant s1 /\ frame s s1 /\ k a s1 = Some (s', b).
Proof.
  intros Hm Hi HP Hr. unfold m_bind in Hr.
  destruct (m s) as [[s1 a]|] eqn:E; [|done].
  destruct (Hm _ _ _ Hi HP E). eauto 8.
Qed.

Lemma ok_return {A} P (a : A) : ok_under P (m_return a).
Proof. intros s s' b Hi _ Hm. unfold m_return in Hm. simplify_eq. split; [done|apply frame_refl]. Qed.

Lemma ok_stuck {A} P : ok_under P (@stuck A).
Proof. intros s s' b _ _ Hm. done. Qed.

Lemma ok_get P : ok_under P m_get.
Proof. intros s s' b Hi _ Hm. unfold m_get in Hm. simplify_eq. split; [done|apply frame_refl]. Qed.

Lemma ok_get_node P n : ok_under P (get_node n).
Proof.
  intros s s' b Hi _ Hm. unfold get_node in Hm. destruct (g_nodes s !! n); simplify_eq.
  split; [done|apply frame_refl].
Qed.

(** A read of a node, with what it found. *)
Lemma ok_get_node_at {B} P n (k : node -> M B) :
  (forall d, ok_under (fun s => P s /\ g_nodes s !! n = Some d) (k d)) ->
  ok_under P (m_bind (get_node n) k).
Proof.
  intros H s s' b Hi HP Hm. unfold m_bind, get_node in Hm.
  destruct (g_nodes s !! n) as [d|] eqn:E; [|done].
  eapply (H d); eauto.
Qed.

Lemma ok_modify_irrelevant P (g : state -> state) :
  (forall s, irrelevant s (g s)) -> ok_under P (m_modify g).
Proof.
  intros Hg s s' b Hi _ Hm. unfold m_modify in Hm. simplify_eq.
  split; [eapply invariant_irrelevant; eauto|apply frame_irrelevant; auto].
Qed.

Local Ltac ok_mod := apply ok_modify_irrelevant; intros ?; irr.

Lemma ok_emit P e : ok_under P (emit e).
Proof. ok_mod. Qed.

(** A change to the ghost fields of runs alone. *)
Definition with_run_ghost (R : gmap node_id (list node_id)) (A O : gset node_id) (L : gmap node_id (list node_id))
    (N : gset node_id) (s : state) : state :=
  with_ghost {| gh_published := gh_published (g_ghost s); gh_seen := gh_seen (g_ghost s);
                gh_unnoticed := gh_unnoticed (g_ghost s); gh_window := gh_window (g_ghost s);
                gh_reads := R; gh_passed := A; gh_over := O; gh_run_reads := L; gh_nested := N |} s.

Definition reads_or_passed_only (g : state -> state) : Prop :=
  forall s, exists R A O L N, g s = with_run_ghost R A O L N s.

Lemma invariant_reads_only s R A O L N : check_invariant s -> check_invariant (with_run_ghost R A O L N s).
Proof. intros H. destruct H. unfold with_run_ghost, with_ghost. by split; gs. Qed.

Lemma ok_reads_only P g : reads_or_passed_only g -> ok_under P (m_modify g).
Proof.
  intros Hg s s' b Hi _ Hm. unfold m_modify in Hm. simplify_eq.
  destruct (Hg s) as (R & A & O & L & N & ->).
  split; [by apply invariant_reads_only|by apply frame_same].
Qed.

Lemma resolved_reads_only n : reads_or_passed_only (ghost_resolved n).
Proof. intros s. by do 5 eexists. Qed.

Lemma effect_start_reads_only n : reads_or_passed_only (ghost_effect_start n).
Proof. intros s. by do 5 eexists. Qed.

Lemma ok_get_owner P o : ok_under P (get_owner o).
Proof.
  intros s s' b Hi _ Hm. unfold get_owner in Hm. destruct (g_owners s !! o); simplify_eq.
  split; [done|apply frame_refl].
Qed.

Lemma ok_put_owner P o x : ok_under P (put_owner o x).
Proof. ok_mod. Qed.

Lemma ok_modify_at P n u d :
  node_step u d -> ok_under (fun s => P s /\ g_nodes s !! n = Some d) (modify_node n u).
Proof.
  intros Hu s s' b Hi [_ Hd] Hm. unfold modify_node in Hm. rewrite Hd in Hm. simplify_eq.
  split; [by apply (invariant_set None None n u d)|]. by apply frame_node.
Qed.

Lemma ok_modify_node P n u : (forall d, node_step u d) -> ok_under P (modify_node n u).
Proof.
  intros Hu s s' b Hi _ Hm. unfold modify_node in Hm.
  destruct (g_nodes s !! n) as [d|] eqn:Hd; simplify_eq.
  split; [by apply (invariant_set None None n u d)|]. by apply frame_node.
Qed.

Lemma ok_modify_effect n u :
  (forall d, n_kind (u d) = n_kind d) -> (forall d, n_runs d <= n_runs (u d)) ->
  ok_under (has_kind n KEffect) (modify_node n u).
Proof.
  intros Hu Hr s s' b Hi (d & Hd & Hk) Hm. unfold modify_node in Hm. rewrite Hd in Hm. simplify_eq.
  assert (Hs : node_step u d).
  { unfold node_step. split_and!; auto; intros; congruence. }
  split; [by apply (invariant_set None None n u d)|by apply frame_node].
Qed.

Lemma ok_mark P (g : state -> option state) :
  (forall s s', g s = Some s' -> rtc mark_prim s s') -> ok_under P (m_step g).
Proof.
  intros Hg s s' b Hi _ Hm. unfold m_step in Hm. destruct (g s) as [s1|] eqn:E; simplify_eq.
  apply (marks_invariant None None); [by eapply Hg|done].
Qed.

Lemma ok_on_value P e x (k : val -> M outcome) :
  (forall v, ok_under P (k v)) -> ok_under P (on_value e x k).
Proof.
  intros Hk s s' b Hi HP Hm. unfold on_value in Hm.
  destruct (eval_expr e (env_lookup e prev_var) x) as [[v|y]|]; simplify_eq.
  - by eapply Hk.
  - split; [done|apply frame_refl].
Qed.

Lemma ok_on_node P e x (k : node_id -> M outcome) :
  (forall t, ok_under P (k t)) -> ok_under P (on_node e x k).
Proof.
  intros Hk. apply ok_on_value. intros v. destruct (decide (0 <= v)%Z); [apply Hk|apply ok_stuck].
Qed.

Lemma ok_note P n o : ok_under P (note_host_read n o).
Proof.
  intros s s' b Hi HP Hm. unfold note_host_read in Hm.
  destruct (g_current s); [simplify_eq; split; [done|apply frame_refl]|].
  destruct (g_current_owner s) as [r|]; [|simplify_eq; split; [done|apply frame_refl]].
  destruct (decide (r = root_owner_id)); [|simplify_eq; split; [done|apply frame_refl]].
  by eapply ok_emit.
Qed.

Lemma ok_log_label P : ok_under P log_label.
Proof.
  intros s s' b Hi _ Hm. unfold log_label in Hm.
  destruct (g_current s), (g_current_owner s); simplify_eq; split; try done; apply frame_refl.
Qed.

Lemma invariant_with_current x o s :
  (forall c, o = Some c -> c ∈ g_running s) -> check_invariant_unpublished x s -> check_invariant_unpublished x (with_current o s).
Proof. intros Ho H. destruct H. split; simpl; done. Qed.

Lemma ok_hosted {A} n (body : M A) :
  ok body -> ok_under (fun s => n ∈ g_running s) (hosted n body).
Proof.
  intros Hb s s' a Hi Hn Hm. unfold hosted, m_bind, m_modify, m_return in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  assert (H0 : check_invariant (with_current_owner None (with_current (Some n) s))).
  { eapply invariant_irrelevant; [|apply (invariant_with_current None (Some n) s); [intros ? [=->]; done|done]]. irr. }
  destruct (Hb _ _ _ H0 I E) as [Hi1 (Hr & _ & Hk & Hf)]. simpl in Hr.
  split.
  - eapply invariant_irrelevant; [|eapply invariant_with_current; [|exact Hi1]]; [irr|].
    rewrite Hr. apply (ci_current _ _ _ Hi).
  - split_and!; [simpl; done|done| |exact Hf]. intros m k Hmk. apply (has_kind_same m k s1); [done|].
    apply Hk. done.
Qed.

Lemma ok_untracked {A} (body : M A) : ok body -> ok (untracked body).
Proof.
  intros Hb s s' a Hi _ Hm. unfold untracked, m_bind, m_modify, m_return in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  assert (H0 : check_invariant (with_untracked (match g_current s with Some _ => true | None => g_untracked s end) (with_current None s))).
  { eapply invariant_irrelevant; [|apply (invariant_with_current None None s); [done|done]]. irr. }
  destruct (Hb _ _ _ H0 I E) as [Hi1 (Hr & _ & Hk & Hf)]. simpl in Hr.
  split.
  - eapply invariant_irrelevant; [|eapply invariant_with_current; [|exact Hi1]]; [irr|].
    rewrite Hr. apply (ci_current _ _ _ Hi).
  - split_and!; [simpl; done|done| |exact Hf]. intros m k Hmk. apply (has_kind_same m k s1); [done|].
    apply Hk. done.
Qed.

Lemma ok_detached o (body : M unit) : ok body -> ok (detached o body).
Proof.
  intros Hb s s' a Hi _ Hm. unfold detached, m_bind, m_modify in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  assert (H0 : check_invariant (with_batch_depth (S (g_batch_depth s)) (with_current_owner (Some o) (with_current None s)))).
  { eapply invariant_irrelevant; [|apply (invariant_with_current None None s); [done|done]]. irr. }
  destruct (Hb _ _ _ H0 I E) as [Hi1 (Hr & _ & Hk & Hf)]. simpl in Hr.
  split.
  - eapply invariant_irrelevant; [|eapply invariant_with_current; [|exact Hi1]]; [irr|].
    rewrite Hr. apply (ci_current _ _ _ Hi).
  - split_and!; [simpl; done|done| |exact Hf]. intros m k Hmk. apply (has_kind_same m k s1); [done|].
    apply Hk. done.
Qed.

(** ** A tracked read *)

Lemma seen_of_ne (G : ghost) S c y c' y' v :
  gh_seen G = <[(c', y') := v]> S -> (c, y) <> (c', y') ->
  seen_of G c y = default [] (S !! (c, y)).
Proof. intros HG Hne. unfold seen_of. rewrite HG. by rewrite lookup_insert_ne. Qed.

Lemma invariant_track s c y w' d :
  check_invariant s -> g_current s = Some c -> sources_add c y (g_edges s) = Some w' ->
  g_nodes s !! y = Some d -> n_kind d <> KEffect ->
  check_invariant (ghost_read c y (read_prefix (g_edges s) c) (with_edges w' s)).
Proof.
  intros H Hc Ha Hd Hk. unfold ghost_read. simpl. rewrite Hd.
  assert (Hr := ci_reachable _ _ _ H). assert (Hcur := ci_cursor _ _ _ H).
  assert (Hrun := ci_current _ _ _ H c Hc).
  destruct (slot_view_add _ _ _ _ (Hcur c) Ha) as (Hoth & Hle & Hsl & Hpre).
  assert (Hpub := ci_published _ _ _ H). assert (Hseen := ci_seen _ _ _ H). assert (Hread := ci_read _ _ _ H).
  assert (Hprefix : forall c' y', y' ∈ read_prefix w' c' -> (c', y') <> (c, y) ->
            y' ∈ read_prefix (g_edges s) c').
  { intros c' y' Hy' Hne. destruct (decide (c' = c)) as [->|Hc'].
    - destruct (Hpre _ Hy') as [Hin| ->]; [done|naive_solver].
    - rewrite read_prefix_view, Hoth in Hy' by done. by rewrite read_prefix_view. }
  destruct H. unfold with_ghost, with_edges. split; gs; try done.
  - eapply reachable_add; eauto.
  - intros c'. destruct (decide (c' = c)) as [->|Hne]; [done|]. by rewrite Hoth.
  - intros c' Hc' Hnr. destruct (decide (c' = c)) as [->|Hne]; [done|].
    rewrite Hoth by done. by apply ci_ended0.
  - intros c' y' Hy'. destruct (decide (c' = c)) as [->|Hne].
    + split; [by apply ci_running0|].
      destruct (Hsl _ Hy') as [Hin| ->]; [eapply ci_slots0; eauto|].
      destruct (n_kind d) eqn:Ekd; [right; by exists d|left; by exists d|done].
    + rewrite Hoth in Hy' by done. eauto.
  - intros c' e y' He Hek Hy' _.
    destruct (decide ((c', y') = (c, y))) as [Heq|Hne].
    + injection Heq as -> ->.
      destruct (decide (y ∈ g_running s)) as [Hin|Hnr].
      * right. right. set_solver.
      * assert (Hp := Hpub y d Hd Hk Hnr ltac:(done)).
        destruct (decide (y ∈ read_prefix (g_edges s) c)) as [Hin|Hout].
        -- destruct (Hseen c e y He Hek Hin ltac:(done)) as [H1|[H1|H1]];
             [left|by right; left|by right; right].
           intros v. unfold seen_of. simpl. rewrite lookup_insert_eq. simpl.
           rewrite elem_of_cons.
           intros [->|Hv]; [done|]. by apply H1.
        -- left. intros v. unfold seen_of. simpl. rewrite lookup_insert_eq. simpl.
           rewrite list_elem_of_singleton. by intros ->.
    + erewrite seen_of_ne; [|reflexivity|done].
      destruct (Hseen _ _ _ He Hek (Hprefix _ _ Hy' Hne) ltac:(done)) as [H1|[H1|H1]]; [by left|by right; left|].
      right. right. destruct (decide (y ∈ g_running s)); set_solver.
  - intros c' y' Hy'. destruct (decide ((c', y') = (c, y))) as [Heq|Hne].
    + injection Heq as -> ->. unfold seen_of. simpl. rewrite lookup_insert_eq. simpl. done.
    + erewrite seen_of_ne; [|reflexivity|done]. apply Hread. by apply Hprefix.
Qed.

Lemma ok_track n k : k <> KEffect -> ok_under (has_kind n k) (track n).
Proof.
  intros Hk s s' a Hi (d & Hd & Hdk) Hm. unfold track in Hm.
  destruct (g_current s) as [c|] eqn:Ec; [|simplify_eq; split; [done|apply frame_refl]].
  destruct (sources_add c n (g_edges s)) as [w|] eqn:Ea; [|done]. simplify_eq.
  split.
  - eapply invariant_track; eauto.
  - unfold ghost_read. simpl. rewrite Hd. by apply frame_same.
Qed.

(** ** New nodes *)

Lemma invariant_new_node s n d :
  check_invariant s -> n = S (g_next_id s) -> n_unannounced d = false ->
  check_invariant (ghost_publish n (set_node n d (with_next_id n s))) /\
  frame s (ghost_publish n (set_node n d (with_next_id n s))).
Proof.
  intros H -> Hnew. set (n := S (g_next_id s)).
  assert (Habs : g_nodes s !! n = None).
  { destruct (g_nodes s !! n) eqn:E; [|done].
    pose proof (ci_next _ _ _ H n ltac:(rewrite E; eauto)). subst n. lia. }
  assert (Hne : forall m, is_Some (g_nodes s !! m) -> m <> n).
  { intros m Hm ->. rewrite Habs in Hm. by destruct Hm. }
  assert (Hkind : forall m k, has_kind m k s -> m <> n).
  { intros m k (e & He & _). apply Hne. eauto. }
  assert (Hup : forall m k, has_kind m k s ->
            has_kind m k (ghost_publish n (set_node n d (with_next_id n s)))).
  { intros m k Hm. pose proof (Hkind _ _ Hm) as Hmn. destruct Hm as (e & He & Hek).
    exists e. unfold ghost_publish. simpl. rewrite lookup_insert_eq. simpl.
    rewrite lookup_insert_ne by done. auto. }
  assert (Hslots := ci_slots _ _ _ H).
  assert (Hnoslots : slot_view (g_edges s) n = ([], 0)).
  { destruct (slot_view (g_edges s) n) as [l c] eqn:E.
    assert (Hl : l = []).
    { destruct l as [|y l]; [done|].
      destruct (Hslots n y) as [Hn _]; [rewrite E; simpl; left|]. by destruct (Hne n Hn). }
    subst. pose proof (ci_cursor _ _ _ H n) as Hc. rewrite E in Hc. simpl in Hc. f_equal. lia. }
  assert (Hwabs : gh_window (g_ghost s) !! n = None).
  { destruct (gh_window (g_ghost s) !! n) as [r|] eqn:E; [|done].
    destruct (Hne n (ci_window_dom _ _ _ H n r E)). done. }
  split.
  - unfold ghost_publish. simpl. rewrite lookup_insert_eq. simpl.
    destruct H. unfold with_ghost, set_node, with_next_id. split; gs; try done.
    + intros c Hc Hnr. destruct (decide (c = n)) as [->|Hcn].
      { rewrite Hnoslots; done. }
      apply ci_ended0; [|done]. destruct Hc as (e & He & Hek). exists e.
      simpl in He. rewrite lookup_insert_ne in He by done. done.
    + intros m. destruct (decide (m = n)) as [->|Hm].
      * lia.
      * rewrite lookup_insert_ne by done. intros Hs. pose proof (ci_next0 _ Hs). subst n. lia.
    + intros c y Hy. destruct (Hslots c y Hy) as [Hc Hyk]. split.
      * pose proof (Hne c Hc). rewrite lookup_insert_ne by done. done.
      * destruct Hyk as [Hyk|Hyk]; [left|right]; apply (Hup y _ ) in Hyk;
          unfold ghost_publish in Hyk; simpl in Hyk; rewrite lookup_insert_eq in Hyk; exact Hyk.
    + intros c Hc. pose proof (Hne c (ci_running0 c Hc)). rewrite lookup_insert_ne by done.
      by apply ci_running0.
    + intros y Hy. pose proof (Hup y _ (ci_queue0 y Hy)) as Hyk.
      unfold ghost_publish in Hyk. simpl in Hyk. rewrite lookup_insert_eq in Hyk. exact Hyk.
    + intros m e. destruct (decide (m = n)) as [->|Hm].
      * rewrite lookup_insert_eq. intros [=<-] _ _ _. by rewrite lookup_insert_eq.
      * rewrite !lookup_insert_ne by done. eauto.
    + intros c e y. destruct (decide (c = n)) as [->|Hcn].
      * rewrite read_prefix_view, Hnoslots. simpl.
        intros _ _ Hy. by apply not_elem_of_nil in Hy.
      * rewrite lookup_insert_ne by done. intros He Hek Hy _.
        assert (Hyn : y <> n).
        { intros ->. destruct (Hslots c n (read_prefix_in _ _ _ Hy)) as [_ [Hk|Hk]];
            by apply (Hkind n _ Hk). }
        rewrite lookup_insert_ne by done. eapply ci_seen0; eauto.
    + intros m r Hw. destruct (decide (m = n)) as [->|Hm]; [rewrite lookup_insert_eq; by eexists|].
      rewrite lookup_insert_ne by done. eauto.
    + intros m e r. destruct (decide (m = n)) as [->|Hm].
      * intros _ Hr. change (gh_window (g_ghost s) !! n = Some r) in Hr. congruence.
      * rewrite lookup_insert_ne by done. eauto.
    + intros m e. destruct (decide (m = n)) as [->|Hm].
      * rewrite lookup_insert_eq. intros [=<-] _ _ Hun. congruence.
      * rewrite lookup_insert_ne by done. eauto.
  - unfold ghost_publish. simpl. rewrite lookup_insert_eq. split_and!; [done|done| |].
    + intros m k Hmk. pose proof (Hup m k Hmk) as Hu.
      unfold ghost_publish in Hu. simpl in Hu. rewrite lookup_insert_eq in Hu. exact Hu.
    + intros m e He Hek. simpl in He.
      assert (Hmn : m <> n) by (apply Hne; eauto).
      exists e. simpl. rewrite lookup_insert_ne by done. split_and!; auto.
      * intros [G|[G|G]]; [by left|right; left|by right; right].
        intros e' r Hl Hw. simpl in Hl. rewrite lookup_insert_ne in Hl by done. eauto.
      * intros H1 H2. done.
Qed.

Lemma ok_new_node {B} P `{!Stable P} (D : node_id -> node) (k : node_id -> M B) :
  (forall n, n_unannounced (D n) = false) ->
  (forall n, ok_under P (k n)) ->
  ok_under P (m_bind next_id (fun n => m_bind (add_node n (D n)) (fun _ => k n))).
Proof.
  intros Hu Hk s s' b Hi HP Hm. unfold m_bind, next_id, add_node, m_modify in Hm. simpl in Hm.
  destruct (invariant_new_node s (S (g_next_id s)) (D (S (g_next_id s))) Hi eq_refl (Hu _)) as [Hi1 F1].
  destruct (Hk _ _ _ _ Hi1 (stable _ _ F1 HP) Hm) as [Hi2 F2].
  split; [done|]. by eapply frame_trans.
Qed.

(** ** A publication *)

Lemma prefix_observer w c n :
  world_reachable w -> n ∈ read_prefix w c -> c ∈ observer_view (observers_of w n).
Proof.
  intros Hr Hin. apply (held_iff_slot w c n (reachable_well_formed _ Hr)). by apply read_prefix_in.
Qed.

Lemma prim_dom s s' m : mark_prim s s' -> is_Some (g_nodes s' !! m) -> is_Some (g_nodes s !! m).
Proof.
  intros Hp. destruct Hp as [s n d f u Hd _ _ _|s n d f Hd _ _ _|s n d f Hd _ _ _]; simpl;
    (destruct (decide (m = n)) as [->|Hne]; [intros _; by eexists|by rewrite lookup_insert_ne]).
Qed.

Lemma marks_dom s s' m : rtc mark_prim s s' -> is_Some (g_nodes s' !! m) -> is_Some (g_nodes s !! m).
Proof.
  induction 1 as [s|s1 s2 s3 Hp _ IH]; [done|]. intros H. eapply prim_dom; eauto.
Qed.

(** [n]'s new view in [gh_published], and marks after it. A memo holding an earlier read of [n] has read only the
    new view, gone [Dirty], or joined [gh_unnoticed]. *)
Lemma invariant_told n d s G s' :
  check_invariant_unpublished (Some n) s ->
  g_nodes s !! n = Some d ->
  gh_published G = <[n := read_view_of d]> (gh_published (g_ghost s)) ->
  gh_seen G = gh_seen (g_ghost s) ->
  gh_window G = gh_window (g_ghost s) ->
  gh_unnoticed (g_ghost s) ⊆ gh_unnoticed G ->
  rtc mark_prim (with_ghost G s) s' ->
  (forall c dc, g_nodes s !! c = Some dc -> n_kind dc = KMemo -> n ∈ read_prefix (g_edges s) c ->
     (forall v, v ∈ seen_of G c n -> v = read_view_of d) \/ ends_dirty s' c \/ c ∈ gh_unnoticed G) ->
  check_invariant s' /\ frame s s'.
Proof.
  intros H Hd Hpub Hseen Hwin Hun Hm Hkey.
  destruct G as [P S U W Rd Pa Ov Rr Ne]. cbn [gh_published gh_seen gh_window gh_unnoticed] in Hpub, Hseen, Hwin, Hun, Hkey.
  subst P S W.
  assert (H1 : check_invariant_except (Some n) (Some n) (with_ghost {| gh_published := <[n := read_view_of d]> (gh_published (g_ghost s));
      gh_seen := gh_seen (g_ghost s); gh_unnoticed := U; gh_window := gh_window (g_ghost s); gh_reads := Rd; gh_passed := Pa; gh_over := Ov; gh_run_reads := Rr; gh_nested := Ne |} s)).
  { destruct H. unfold with_ghost. split; gs; try done.
    - intros m e Hm1 Hm2 Hm3 Hx. rewrite lookup_insert_ne by congruence. eauto.
    - intros c e y He Hek Hy Hyn.
      destruct (ci_seen0 c e y He Hek Hy ltac:(done)) as [H1|[H1|H1]]; [left|by right; left|].
      + intros v Hv. rewrite lookup_insert_ne by congruence. by apply H1.
      + right. right. set_solver. }
  destruct (marks_invariant _ _ _ _ Hm H1) as [H2 F2].
  destruct (marks_holds _ _ Hm) as (He & Hg & Hn).
  simpl in He, Hg.
  split.
  - destruct H2. split; try done.
    + intros m e Hm1 Hm2 Hm3 Hx. destruct (decide (m = n)) as [->|Hne].
      * destruct (Hn n d Hd) as (d' & Hd' & _ & Hv & _).
        assert (d' = e) by congruence. subst d'.
        rewrite Hg. simpl. rewrite lookup_insert_eq. by rewrite Hv.
      * apply (ci_published0 m e); try done. congruence.
    + intros c e y He' Hek Hy _. destruct (decide (y = n)) as [->|Hne].
      * destruct (marks_dom _ _ c Hm ltac:(by eexists)) as [dc Hdc].
        destruct (Hn c dc Hdc) as (d' & Hd' & Hk' & _ & _).
        assert (d' = e) by congruence. subst d'.
        rewrite He in Hy.
        destruct (Hkey c dc Hdc ltac:(congruence) Hy) as [K|[(d2 & Hd2 & K)|K]].
        -- left. rewrite Hg. intros v Hv. simpl. rewrite lookup_insert_eq. by rewrite (K v Hv).
        -- assert (d2 = e) by congruence. subst d2. by right; left.
        -- right. right. by rewrite Hg.
      * eapply ci_seen0; eauto. congruence.
  - eapply frame_trans; [|exact F2]. by apply frame_same.
Qed.

(** ** Starting and ending runs *)

Lemma invariant_run_begin n u d s :
  check_invariant s -> g_nodes s !! n = Some d -> n_kind (u d) = n_kind d ->
  n_unannounced (u d) = false -> n_runs d <= n_runs (u d) ->
  check_invariant (with_running (n :: g_running s)
    (ghost_run_start n (with_edges (sources_begin_run n (g_edges s)) (set_node n (u d) s)))).
Proof.
  intros H Hd Hk Hun Hle.
  assert (Hsome : is_Some (g_nodes s !! n)) by eauto.
  assert (Hr := ci_reachable _ _ _ H). destruct H.
  unfold with_running, ghost_run_start, with_ghost, with_edges, set_node.
  split; gs.
  - by apply reachable_begin.
  - intros c. rewrite slot_view_begin. destruct (decide (c = n)); cbn [fst snd]; [lia|done].
  - intros c Hc Hnr. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne].
    + exfalso. apply Hnr. left.
    + apply ci_ended0; [exact (has_kind_set_back n d (u d) c KMemo s Hd Hk Hc)|].
      intros Hin. apply Hnr. by right.
  - intros m Hm. apply ci_next0. exact (is_Some_set_back m n (u d) s Hsome Hm).
  - intros c y Hy. rewrite slot_view_begin in Hy.
    assert (Hy' : y ∈ (slot_view (g_edges s) c).1).
    { destruct (decide (c = n)); done. }
    destruct (ci_slots0 c y Hy') as [Hc Hyk]. split.
    + exact (is_Some_set c n (u d) s Hsome Hc).
    + destruct Hyk; [left|right]; eapply (has_kind_set n d (u d)); eauto.
  - intros c Hc. apply elem_of_cons in Hc as [->|Hc].
    + exact (is_Some_set n n (u d) s Hsome Hsome).
    + exact (is_Some_set c n (u d) s Hsome (ci_running0 c Hc)).
  - intros c Hc. right. auto.
  - intros y Hy. eapply (has_kind_set n d (u d)); eauto.
  - intros m e. destruct (decide (m = n)) as [->|Hne].
    + intros _ _ Hnr. exfalso. apply Hnr. left.
    + rewrite lookup_insert_ne by done. intros He Hek Hnr Hx.
      apply (ci_published0 m e); try done. intros Hin. apply Hnr. by right.
  - intros c e y. rewrite read_prefix_view, slot_view_begin.
    destruct (decide (c = n)) as [->|Hne].
    + cbn [fst snd]. rewrite take_0. intros _ _ Hy. by apply not_elem_of_nil in Hy.
    + rewrite lookup_insert_ne by done. intros He Hek Hy Hx.
      rewrite <- read_prefix_view in Hy.
      destruct (ci_seen0 c e y He Hek Hy Hx) as [H1|[H1|H1]]; [by left|by right; left|].
      right. right. set_solver.
  - intros c y. rewrite read_prefix_view, slot_view_begin.
    destruct (decide (c = n)) as [->|Hne].
    + cbn [fst snd]. rewrite take_0. intros Hy. by apply not_elem_of_nil in Hy.
    + rewrite <- read_prefix_view. apply ci_read0.
  - intros m r Hw. exact (is_Some_set m n (u d) s Hsome (ci_window_dom0 m r Hw)).
  - intros m e r. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros Heq Hw. simplify_eq. pose proof (ci_window_le0 n d r Hd Hw). lia.
    + rewrite lookup_insert_ne by done. eauto.
  - intros m e. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros Heq _ _ Hu. simplify_eq. congruence.
    + rewrite lookup_insert_ne by done. eauto.
Qed.

Lemma has_kind_unique n k k' s : has_kind n k s -> has_kind n k' s -> k = k'.
Proof. intros (d & Hd & <-) (d' & Hd' & <-). by simplify_eq. Qed.

Lemma invariant_effect_begin n r s :
  check_invariant s -> has_kind n KEffect s -> (forall c, c ∈ g_running s -> c ∈ r) -> n ∈ r ->
  (forall c, c ∈ r -> c = n \/ c ∈ g_running s) ->
  check_invariant (with_edges (sources_begin_run n (g_edges s)) (with_running r s)).
Proof.
  intros H Hk Hsub Hn Hback.
  assert (Hr := ci_reachable _ _ _ H). destruct H.
  assert (Hsome : is_Some (g_nodes s !! n)) by (destruct Hk as (d & Hd & _); eauto).
  unfold with_running, with_edges. split; gs.
  - by apply reachable_begin.
  - intros c. rewrite slot_view_begin. destruct (decide (c = n)); cbn [fst snd]; [lia|done].
  - intros c Hc Hnr. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne].
    + exfalso. by apply Hnr.
    + apply ci_ended0; [exact Hc|]. intros Hin. by apply Hnr, Hsub.
  - done.
  - intros c y Hy. rewrite slot_view_begin in Hy. apply ci_slots0. destruct (decide (c = n)); done.
  - intros c Hc. destruct (Hback c Hc) as [->|Hin]; auto.
  - intros c Hc. auto.
  - done.
  - intros m e He Hek Hnr Hx. apply (ci_published0 m e); try done. intros Hin. by apply Hnr, Hsub.
  - intros c e y He Hek Hy Hx. rewrite read_prefix_view, slot_view_begin in Hy.
    destruct (decide (c = n)) as [->|Hne].
    + cbn [fst snd] in Hy. rewrite take_0 in Hy. by apply not_elem_of_nil in Hy.
    + rewrite <- read_prefix_view in Hy. eauto.
  - intros c y Hy. rewrite read_prefix_view, slot_view_begin in Hy.
    destruct (decide (c = n)) as [->|Hne].
    + cbn [fst snd] in Hy. rewrite take_0 in Hy. by apply not_elem_of_nil in Hy.
    + rewrite <- read_prefix_view in Hy. eauto.
  - done.
  - done.
  - done.
Qed.

Lemma invariant_pop n rest x s :
  check_invariant_unpublished x s -> g_running s = n :: rest ->
  (has_kind n KMemo s -> (slot_view (g_edges s) n).2 = length (slot_view (g_edges s) n).1) ->
  (forall c, g_current s = Some c -> c ∈ rest) ->
  x = Some n \/ has_kind n KEffect s ->
  check_invariant_unpublished x (with_running rest s).
Proof.
  intros H Hrun Hlen Hcur Hok. destruct H. unfold with_running. split; gs; try done.
  - intros c Hc Hnr. destruct (decide (c = n)) as [->|Hne]; [auto|].
    apply ci_ended0; [done|]. rewrite Hrun. intros Hin. apply elem_of_cons in Hin as [->|Hin]; [done|auto].
  - intros c Hc. apply ci_running0. rewrite Hrun. by right.
  - intros m e He Hek Hnr Hx. apply (ci_published0 m e); try done.
    rewrite Hrun. intros Hin. apply elem_of_cons in Hin as [->|Hin]; [|done].
    destruct Hok as [->|Hk]; [done|].
    destruct Hk as (e' & He' & Hek'). simplify_eq.
Qed.

(** ** Binds, one step at a time *)

Lemma bind_bind {A B C} (m : M A) (k1 : A -> M B) (k2 : B -> M C) s :
  m_bind (m_bind m k1) k2 s = m_bind m (fun x => m_bind (k1 x) k2) s.
Proof. unfold m_bind. by destruct (m s) as [[s1 a]|]. Qed.

Lemma bind_some {A B} (m : M A) (k : A -> M B) s s1 a :
  m s = Some (s1, a) -> m_bind m k s = k a s1.
Proof. intros Hm. unfold m_bind. by rewrite Hm. Qed.

Lemma bind_get_node {B} n d (k : node -> M B) s :
  g_nodes s !! n = Some d -> m_bind (get_node n) k s = k d s.
Proof. intros Hd. unfold m_bind, get_node. by rewrite Hd. Qed.

Lemma bind_modify_node {B} n u d (k : unit -> M B) s :
  g_nodes s !! n = Some d -> m_bind (modify_node n u) k s = k tt (set_node n (u d) s).
Proof. intros Hd. unfold m_bind, modify_node. by rewrite Hd. Qed.

Lemma bind_m_modify {B} g (k : unit -> M B) s : m_bind (m_modify g) k s = k tt (g s).
Proof. done. Qed.

Lemma bind_m_return {A B} (a : A) (k : A -> M B) s : m_bind (m_return a) k s = k a s.
Proof. done. Qed.

Lemma bind_get {B} (k : state -> M B) s : m_bind m_get k s = k s s.
Proof. done. Qed.

Lemma bind_edges_opt {B} u (k : unit -> M B) s s' a :
  m_bind (edges_opt u) k s = Some (s', a) ->
  exists w, u (g_edges s) = Some w /\ k tt (with_edges w s) = Some (s', a).
Proof.
  unfold m_bind, edges_opt. destruct (u (g_edges s)) as [w|]; [eauto|done].
Qed.

Lemma bind_step {B} g (k : unit -> M B) s s' a :
  m_bind (m_step g) k s = Some (s', a) ->
  exists s1, g s = Some s1 /\ k tt s1 = Some (s', a).
Proof.
  unfold m_bind, m_step. destruct (g s) as [s1|]; [eauto|done].
Qed.

Local Ltac name_k K :=
  match goal with H : m_bind _ ?k _ = _ |- _ => set (K := k) in H end.

Lemma ok_edges_opt_end P n (b : bool) : ok_under P (edges_opt (if b then sources_clear n else sources_end_run n)).
Proof.
  intros s s' a Hi _ Hm. unfold edges_opt in Hm.
  destruct b.
  - destruct (sources_clear n (g_edges s)) as [w|] eqn:E; [|done]. simplify_eq.
    split; [by destruct (invariant_clear _ _ _ _ Hi E)|]. by apply frame_same.
  - destruct (sources_end_run n (g_edges s)) as [w|] eqn:E; [|done]. simplify_eq.
    split; [by destruct (invariant_end _ _ _ _ Hi E)|]. by apply frame_same.
Qed.

Lemma ok_edges_clear P n : ok_under P (edges_opt (sources_clear n)).
Proof. exact (ok_edges_opt_end P n true). Qed.

Lemma ok_mark_dirty P f n : ok_under P (m_step (mark_dirty f n)).
Proof. apply ok_mark. intros s0 s1 Hm. by eapply mark_dirty_rtc. Qed.

Lemma ok_mark_reader P f : ok_under P (mark_reader f).
Proof.
  intros s s' a Hi _ Hm. unfold mark_reader in Hm.
  destruct (g_current s) as [c|]; [|simplify_eq; split; [done|apply frame_refl]].
  destruct (f_reader_mark (g_flags s)); [|simplify_eq; split; [done|apply frame_refl]].
  destruct (mark_check f c s) as [s1|] eqn:E; [|done]. simplify_eq.
  apply (marks_invariant None None); [by eapply mark_check_rtc|done].
Qed.

Lemma ok_current_owner P : ok_under P current_owner.
Proof.
  intros s s' a Hi _ Hm. unfold current_owner in Hm.
  destruct (g_current_owner s) as [o|]; [simplify_eq; split; [done|apply frame_refl]|].
  destruct (g_current s) as [h|] eqn:Ec; [|done].
  destruct (g_nodes s !! h) as [d|] eqn:Hd; [|done].
  destruct (n_scope d) as [o|].
  { simplify_eq. split; [eapply invariant_irrelevant; [|exact Hi]; irr|apply frame_irrelevant; irr]. }
  destruct (match n_kind d, n_mode d with KMemo, Pure => true | _, _ => false end).
  - simplify_eq.
    assert (Hs : node_step (node_with_violated true) d) by ns.
    split; [by apply (invariant_set None None h (node_with_violated true) d)|by apply (frame_node h (node_with_violated true) d)].
  - simplify_eq.
    set (s1 := with_owners _ s).
    assert (Hi1 : check_invariant s1) by (eapply invariant_irrelevant; [|exact Hi]; irr).
    assert (Hd1 : g_nodes s1 !! h = Some d) by done.
    set (o := stdpp.base.fresh (dom (g_owners s))).
    assert (Hs : node_step (node_with_scope (Some o)) d) by ns.
    assert (Hi2 := invariant_set None None h (node_with_scope (Some o)) d s1 Hi1 Hd1 Hs).
    assert (F2 := frame_node h (node_with_scope (Some o)) d s1 Hd1 Hs).
    split.
    + eapply invariant_irrelevant; [|exact Hi2]. irr.
    + eapply frame_trans; [apply frame_irrelevant; irr|].
      eapply frame_trans; [exact F2|]. apply frame_irrelevant. irr.
Qed.

(** ** The engine keeps the invariant *)

(** After a run, or a recompute that ended, the latest window of [n] has closed, or [n] is disposed. *)
Definition run_post (s : state) (n : node_id) : Prop :=
  exists d, g_nodes s !! n = Some d /\ (closed (nodes_and_windows s) n \/ n_disposed d = true).

(**md
A completed step `m` from a state satisfying `check_invariant`, where `n` is a memo, ends in a state
satisfying `run_post` for `n`.
*)
Definition ok_post (m : M unit) (n : node_id) : Prop :=
  forall s s' a, check_invariant s -> has_kind n KMemo s -> m s = Some (s', a) -> run_post s' n.

Definition in_effect_run (n : node_id) (s : state) : Prop :=
  has_kind n KEffect s /\ n ∈ g_running s.

Global Instance stable_effect_run n : Stable (in_effect_run n).
Proof.
  intros s s' (Hr & _ & Hk' & _) [Hk Hn]. split; [auto|by rewrite Hr].
Qed.

(**md
Every engine function at fuel `f`, from `exec` to `drain`, satisfies `ok_under` with the premise it needs,
and `run_memo` and `recompute` satisfy `ok_post`.

- [f:] the fuel.
*)
Record engine_ok (f : nat) : Prop := {
  Eexec : forall e c, ok (exec f e c);
  Eread : forall n b, ok (read f n b);
  Eread_node : forall n, ok (read_node f n);
  Eupdate : forall n, ok (update_if_necessary f n);
  Erefresh_or_pull : forall n, ok_under (has_kind n KMemo) (refresh_or_pull f n);
  Epull : forall n, ok_under (has_kind n KMemo) (pull f n);
  Erefresh : forall n, ok_under (has_kind n KMemo) (refresh f n);
  Ewalk : forall n i, ok (resolve_walk f n i);
  Erecompute : forall n, ok_under (has_kind n KMemo) (recompute f n);
  Erun_memo : forall n, ok_under (has_kind n KMemo) (run_memo f n);
  Eexecute : forall n, ok_under (has_kind n KEffect) (execute_effect f n);
  Erun_effect : forall n, ok_under (has_kind n KEffect) (run_effect f n);
  Ebody : forall n, ok_under (in_effect_run n) (run_effect_body f n);
  Edischarge : forall o, ok (discharge f o);
  Eretire : forall o, ok (retire f o);
  Edispose_scope : forall o, ok (dispose_scope f o);
  Eteardown : forall o ls cs, ok (teardown f o ls cs);
  Ecleanup : forall o b e, ok (run_cleanup f o b e);
  Erelease : forall n, ok (release f n);
  Edispose : forall n, ok (dispose_node f n);
  Eattach : forall n o, ok (attach f n o);
  Ewrite : forall n v, ok (write_signal f n v);
  Erequest : ok (request_flush f);
  Esettle : ok (settle_flush f);
  Eflush : ok (run_flush f);
  Edrain : ok (drain f);
  Erun_post : forall n, ok_post (run_memo f n) n;
  Erecompute_post : forall n, ok_post (recompute f n) n
}.

Local Ltac nstep := intros ?; ns.

Local Ltac ih IH :=
  first
    [ apply (Eexec _ IH) | apply (Eread _ IH) | apply (Eread_node _ IH) | apply (Eupdate _ IH)
    | apply (Erefresh_or_pull _ IH) | apply (Epull _ IH) | apply (Erefresh _ IH) | apply (Ewalk _ IH)
    | apply (Erecompute _ IH) | apply (Erun_memo _ IH) | apply (Eexecute _ IH)
    | apply (Erun_effect _ IH) | apply (Ebody _ IH) | apply (Edischarge _ IH) | apply (Eretire _ IH)
    | apply (Edispose_scope _ IH) | apply (Eteardown _ IH) | apply (Ecleanup _ IH)
    | apply (Erelease _ IH) | apply (Edispose _ IH) | apply (Eattach _ IH) | apply (Ewrite _ IH)
    | apply (Erequest _ IH) | apply (Esettle _ IH) | apply (Eflush _ IH) | apply (Edrain _ IH) ].

Local Ltac go1 IH :=
  first
    [ apply ok_return | apply ok_stuck | apply ok_get | apply ok_get_node | apply ok_get_owner
    | apply ok_put_owner | apply ok_emit | apply ok_note | apply ok_log_label | apply ok_current_owner
    | apply ok_mark_reader | apply ok_mark_dirty | apply ok_edges_clear | apply ok_edges_opt_end
    | apply ok_on_node; intros ? | apply ok_on_value; intros ?
    | apply ok_new_node; [apply _|intros ?; reflexivity|intros ?]
    | apply ok_detached
    | apply ok_modify_node; nstep
    | apply ok_modify_irrelevant; intros ?; irr
    | ih IH
    | apply ok_any; ih IH
    | apply ok_bind; [apply _| |intros ?]
    | match goal with |- ok_under _ (match ?x with _ => _ end) => destruct x end ].

Local Ltac go IH := repeat go1 IH.

Section Step.
  Variable f : nat.
  Hypothesis IH : engine_ok f.

  Lemma ok_exec_S e c : ok (exec (S f) e c).
  Proof. destruct c; cbn [exec]; go IH. Qed.

  Lemma ok_read_S n b : ok (read (S f) n b).
  Proof. destruct b; cbn [read]; [ih IH|]. apply ok_untracked. ih IH. Qed.

  Lemma ok_read_node_S n : ok (read_node (S f) n).
  Proof.
    cbn [read_node]. apply ok_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek.
    - apply (ok_weaken (has_kind n KSignal)); [intros s [_ Hd]; by exists d|].
      apply ok_bind; [apply _|apply ok_track; done|intros _; apply ok_return].
    - apply (ok_weaken (has_kind n KMemo)); [intros s [_ Hd]; by exists d|].
      apply ok_bind; [apply _| |intros _; go IH].
      destruct (stale_live d).
      + apply ok_bind; [apply _|ih IH|intros _].
        apply ok_bind; [apply _|apply ok_track; done|intros _].
        go IH.
      + apply ok_track. done.
    - apply ok_stuck.
  Qed.

  Lemma ok_update_S n : ok (update_if_necessary (S f) n).
  Proof.
    cbn [update_if_necessary]. apply ok_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek; try apply ok_return.
    destruct (stale_live d); [|apply ok_return].
    apply (ok_weaken (has_kind n KMemo)); [intros s [_ Hd]; by exists d|]. ih IH.
  Qed.

  Lemma ok_refresh_or_pull_S n : ok_under (has_kind n KMemo) (refresh_or_pull (S f) n).
  Proof.
    cbn [refresh_or_pull]. apply ok_bind; [apply _|apply ok_get|intros s].
    destruct (deferring s); ih IH.
  Qed.

  Lemma ok_pull_S n : ok_under (has_kind n KMemo) (pull (S f) n).
  Proof.
    cbn [pull]. apply ok_bind; [apply _|apply ok_modify_irrelevant; intros ?; irr|intros _].
    apply ok_bind; [apply _|ih IH|intros _].
    apply ok_bind; [apply _|apply ok_modify_irrelevant; intros ?; irr|intros _].
    apply ok_bind; [apply _|apply ok_get|intros s].
    destruct (_ && _); [apply ok_any; ih IH|apply ok_return].
  Qed.

  Lemma ok_refresh_S n : ok_under (has_kind n KMemo) (refresh (S f) n).
  Proof.
    cbn [refresh]. apply ok_bind; [apply _|apply ok_get_node|intros d].
    apply ok_bind; [apply _| |intros _].
    - destruct (decide (n_freshness d = Check)); [|apply ok_return].
      apply ok_bind; [apply _|apply ok_any; ih IH|intros _].
      apply ok_get_node_at. intros d'.
      destruct (decide (n_freshness d' = Check)) as [Hc|_]; [|apply ok_return].
      intros s s' a Hi [_ Hd] Hm. rewrite bind_m_modify in Hm.
      destruct (resolved_reads_only n s) as (R & A & O & L & N & Hg). rewrite Hg in Hm. simpl in Hm.
      assert (Hd1 : g_nodes (with_run_ghost R A O L N s) !! n = Some d') by exact Hd.
      assert (Hi1 := invariant_reads_only s R A O L N Hi).
      destruct (ok_modify_at (fun _ => True) n (fun x => node_with_unannounced false (node_with_freshness Clean x)) d'
                 ltac:(ns) _ _ _ Hi1 (conj I Hd1) Hm) as [Hi2 F2].
      split; [done|]. eapply frame_trans; [|exact F2]. by apply frame_same.
    - apply ok_bind; [apply _|apply ok_get_node|intros d'].
      destruct (decide (n_freshness d' = Dirty)); [ih IH|apply ok_return].
  Qed.

  Lemma ok_walk_S n i : ok (resolve_walk (S f) n i).
  Proof. cbn [resolve_walk]. go IH. Qed.

  Lemma invariant_cleared n d s :
    check_invariant s -> g_nodes s !! n = Some d ->
    check_invariant (ghost_dirty_cleared n (n_runs d) (set_node n (node_with_freshness Clean d) s)).
  Proof.
    intros H Hd.
    assert (H1 : check_invariant (ghost_dirty_cleared n (n_runs d) s)).
    { destruct H. unfold ghost_dirty_cleared, with_ghost. split; gs; try done.
      - intros c e y He Hek Hy Hx.
        destruct (ci_seen0 c e y He Hek Hy Hx) as [H1|[H1|H1]]; [by left|by right; left|].
        right. right. set_solver.
      - intros m r. destruct (decide (m = n)) as [->|Hne]; [intros _; by eexists|].
        rewrite lookup_insert_ne by done. eauto.
      - intros m e r. destruct (decide (m = n)) as [->|Hne].
        + rewrite lookup_insert_eq. rewrite Hd. intros [=<-] [=<-]. done.
        + rewrite lookup_insert_ne by done. eauto.
      - intros m e He Hek Hc Hu Hl. destruct (decide (m = n)) as [->|Hne].
        + rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eexists. split; [done|lia].
        + rewrite lookup_insert_ne by done. eauto. }
    assert (Hin : n ∈ gh_unnoticed (g_ghost (ghost_dirty_cleared n (n_runs d) s))) by (simpl; set_solver).
    refine (invariant_set_gen None None n (node_with_freshness Clean) d _ H1 Hd eq_refl
              (fun _ _ _ => eq_refl) (fun _ _ => or_intror Hin) (le_n _) _).
    intros _ _ _ _. right. exists (n_runs d). split; [simpl; apply lookup_insert_eq|done].
  Qed.

  Lemma ok_recompute_full n s s' a :
    check_invariant s -> has_kind n KMemo s -> recompute (S f) n s = Some (s', a) ->
    check_invariant s' /\ frame s s' /\ run_post s' n.
  Proof.
    intros Hi Hk Hm. cbn [recompute] in Hm.
    destruct Hk as (d & Hd & Hkd).
    rewrite (bind_get_node _ _ _ _ Hd) in Hm.
    destruct (n_scope d) as [o|].
    2: { pose proof (Erun_memo _ IH n _ _ _ Hi ltac:(by exists d) Hm) as [H1 H2].
         split_and!; [done|done|]. exact (Erun_post _ IH n _ _ _ Hi ltac:(by exists d) Hm). }
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm. rewrite bind_m_modify in Hm.
    pose proof (invariant_cleared n d s Hi Hd) as Hi1.
    set (s1 := ghost_dirty_cleared n (n_runs d) (set_node n (node_with_freshness Clean d) s)) in Hi1, Hm.
    assert (Hd1 : g_nodes s1 !! n = Some (node_with_freshness Clean d)) by (simpl; apply lookup_insert_eq).
    assert (Hw1 : gh_window (g_ghost s1) !! n = Some (n_runs d)) by (simpl; apply lookup_insert_eq).
    assert (Hk1 : has_kind n KMemo s1) by (eexists; split; [exact Hd1|done]).
    assert (Hkinds1 : forall m k, has_kind m k s -> has_kind m k s1).
    { intros m k Hmk. exact (has_kind_set n d (node_with_freshness Clean d) m k s Hd eq_refl Hmk). }
    assert (Hother : forall m, m <> n -> memo_frame (nodes_and_windows s) (nodes_and_windows s1) m).
    { intros m Hne e He Hek. simpl in He. exists e. simpl. rewrite lookup_insert_ne by done.
      split_and!; auto.
      - left. by rewrite lookup_insert_ne.
      - intros [G|[G|G]]; [by left|right; left|by right; right].
        intros e' r Hl Hw. simpl in Hl, Hw. rewrite lookup_insert_ne in Hl by done. rewrite lookup_insert_ne in Hw by done. eauto.
      - intros H1 H2. done. }
    assert (Hd_ok : ok (discharge f o)) by ih IH.
    destruct (run_bind _ _ _ _ _ _ Hd_ok Hi1 I Hm) as (s2 & [] & Ed & Hi2 & F2 & Hm2).
    destruct (proj1 (proj2 (proj2 F2)) n KMemo Hk1) as (d2 & Hd2 & Hk2).
    rewrite (bind_get_node _ _ _ _ Hd2) in Hm2.
    (* the three ways out, each from a state with the post *)
    assert (Hout : check_invariant s' /\ frame s2 s' /\ run_post s' n).
    { destruct (n_disposed d2) eqn:Edis.
      - unfold m_return in Hm2. simplify_eq. split_and!; [done|apply frame_refl|]. exists d2. auto.
      - destruct (decide (n_runs d2 = n_runs d)) as [Heq|Hne].
        + pose proof (Erun_memo _ IH n _ _ _ Hi2 ltac:(by exists d2) Hm2) as [H1 H2].
          split_and!; [done|done|]. exact (Erun_post _ IH n _ _ _ Hi2 ltac:(by exists d2) Hm2).
        + destruct (decide (n_freshness d2 = Dirty)).
          * pose proof (Erecompute _ IH n _ _ _ Hi2 ltac:(by exists d2) Hm2) as [H1 H2].
            split_and!; [done|done|]. exact (Erecompute_post _ IH n _ _ _ Hi2 ltac:(by exists d2) Hm2).
          * unfold m_return in Hm2. simplify_eq. split_and!; [done|apply frame_refl|].
            exists d2. split; [done|]. left.
            destruct F2 as (_ & _ & _ & Hfr).
            destruct (Hfr n (node_with_freshness Clean d) Hd1 ltac:(done)) as (e & He & _ & Hr & _ & Hw & _ & _).
            simpl in He. assert (e = d2) by congruence. subst e.
            destruct Hw as [Hw|[Hw|Hw]].
            -- intros e r Hl Hr'. simpl in Hl, Hr'. rewrite Hd2 in Hl. injection Hl as <-.
               assert (Hw' : gh_window (g_ghost s') !! n = gh_window (g_ghost s1) !! n) by exact Hw.
               change (gh_window (g_ghost s') !! n = Some r) in Hr'. rewrite Hw', Hw1 in Hr'. injection Hr' as <-.
               simpl in Hr. lia.
            -- done.
            -- congruence. }
    destruct Hout as (Hi' & F' & Hpost).
    split_and!; [done| |done].
    assert (F12 : frame s1 s') by (eapply frame_trans; [exact F2|exact F']).
    destruct F12 as (Hr12 & Hc12 & Hk12 & Hf12).
    split_and!; [done|done|intros m k Hmk; auto|].
    intros m. destruct (decide (m = n)) as [->|Hne]; [|eapply memo_frame_trans; [apply Hother; done|apply Hf12]].
    intros e He Hek. simpl in He. rewrite Hd in He. injection He as <-.
    destruct (Hf12 n (node_with_freshness Clean d) Hd1 ltac:(done)) as (e' & He' & Hk' & Hr' & Hdi' & _ & _ & _).
    destruct Hpost as (e2 & He2 & Hp). simpl in He'. assert (e2 = e') by congruence. subst e2.
    exists e'. split_and!; [done|done|done|auto| |intros _; destruct Hp; [right; left|left]; done|intros _ _; destruct Hp; [right; left|left]; done].
    destruct Hp; [right; left|right; right]; done.
  Qed.

  Lemma ok_recompute_S n : ok_under (has_kind n KMemo) (recompute (S f) n).
  Proof. intros s s' a Hi Hk Hm. destruct (ok_recompute_full n s s' a Hi Hk Hm) as (? & ? & _). by split. Qed.

  Lemma ok_recompute_post_S n : ok_post (recompute (S f) n) n.
  Proof. intros s s' a Hi Hk Hm. by destruct (ok_recompute_full n s s' a Hi Hk Hm) as (_ & _ & ?). Qed.

  Lemma ok_run_memo_full n s s' a :
    check_invariant s -> has_kind n KMemo s -> run_memo (S f) n s = Some (s', a) ->
    check_invariant s' /\ frame s s' /\ run_post s' n.
  Proof.
    intros Hi Hk Hm. cbn [run_memo] in Hm.
    destruct Hk as (d & Hd & Hkd).
    rewrite (bind_get_node _ _ _ _ Hd) in Hm.
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm.
    unfold edges, push_running in Hm. rewrite !bind_m_modify in Hm.
    set (u := fun x => node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone
        (node_with_unannounced false (node_with_freshness Clean x))))) in Hm.
    pose proof (invariant_run_begin n u d s Hi Hd eq_refl eq_refl (le_S _ _ (le_n _))) as Hi4.
    set (s4 := with_running _ (ghost_run_start _ _)) in Hi4.
    assert (Hset : frame s (set_node n (u d) s)).
    { apply (frame_set n u d s Hd eq_refl). intros _. ms. }
    assert (Hkinds4 : forall m k, has_kind m k s -> has_kind m k s4).
    { intros m k Hmk. exact (has_kind_set n d (u d) m k s Hd eq_refl Hmk). }
    assert (Hfr4 : forall m, memo_frame (nodes_and_windows s) (nodes_and_windows s4) m) by apply (proj2 (proj2 (proj2 Hset))).
    assert (Hrun4 : g_running s4 = n :: g_running s) by done.
    assert (Hcur4 : g_current s4 = g_current s) by done.
    assert (Hd4'0 : g_nodes s4 !! n = Some (u d)) by (simpl; apply lookup_insert_eq).
    assert (Hclosed4 : closed (nodes_and_windows s4) n).
    { intros e r He Hr. simpl in He, Hr. rewrite lookup_insert_eq in He. injection He as <-.
      pose proof (ci_window_le _ _ _ Hi n d r Hd Hr). simpl. lia. }
    assert (Hin4 : n ∈ g_running s4) by (rewrite Hrun4; left).
    destruct (run_bind _ _ _ _ _ _ (ok_hosted n _ (Eexec _ IH _ _)) Hi4 Hin4 Hm)
      as (s5 & r & _ & Hi5 & F5 & Hm5).
    destruct F5 as (Hrun5 & Hcur5 & Hkinds5 & Hfr5). rewrite Hrun4 in Hrun5. rewrite Hcur4 in Hcur5.
    assert (Hk5 : has_kind n KMemo s5) by (apply Hkinds5, Hkinds4; by exists d).
    destruct Hk5 as (d1 & Hd1 & Hk1).
    rewrite (bind_get_node _ _ _ _ Hd1) in Hm5.
    set (result := if n_violated d1 then Fail pure_scope_failure else r) in Hm5.
    (* the value *)
    name_k K6.
    assert (Hs6 : exists s6, K6 tt s6 = Some (s', a) /\ check_invariant_unpublished (Some n) s6 /\ frame s5 s6).
    { destruct result as [v|y].
      - rewrite (bind_modify_node _ _ _ _ _ Hd1) in Hm5. eexists. split; [exact Hm5|].
        split.
        + apply (invariant_set_gen (Some n) None n (fun x => node_with_published true (node_with_value v x)) d1);
            [by apply invariant_weaken|done|done|done|intros; left; done|done|intros ???; left; simpl in *; split_and!; done].
        + apply (frame_set n (fun x => node_with_published true (node_with_value v x)) d1); [done|done|intros _; ms].
      - rewrite bind_m_return in Hm5. eexists. split; [exact Hm5|]. split; [by apply invariant_weaken|apply frame_refl]. }
    clear Hm5. destruct Hs6 as (s6 & Hm6 & Hi6 & F6). subst K6. cbv beta in Hm6.
    destruct F6 as (Hrun6 & Hcur6 & Hkinds6 & Hfr6). rewrite Hrun5 in Hrun6. rewrite Hcur5 in Hcur6.
    (* the edges *)
    apply bind_edges_opt in Hm6 as (w & Hw & Hm7).
    assert (Hi7 : check_invariant_unpublished (Some n) (with_edges w s6) /\ (slot_view w n).2 = length (slot_view w n).1).
    { destruct (n_disposed d1); [by apply invariant_clear|by apply invariant_end]. }
    destruct Hi7 as [Hi7 Hlen].
    (* the pop *)
    unfold pop_running in Hm7. rewrite bind_m_modify in Hm7.
    assert (Hcurin : forall c, g_current s = Some c -> c ∈ g_running s) by apply (ci_current _ _ _ Hi).
    simpl in Hm7. rewrite Hrun6 in Hm7. simpl in Hm7.
    assert (Hi8 : check_invariant_unpublished (Some n) (with_running (g_running s) (with_edges w s6))).
    { apply (invariant_pop n); [done|done|done|simpl; rewrite Hcur6; done|by left]. }
    set (s8 := with_running _ (with_edges w s6)) in Hi8, Hm7.
    assert (Hkinds8 : forall m k, has_kind m k s5 -> has_kind m k s8).
    { intros m k Hmk. apply (has_kind_same m k s6); [done|]. apply Hkinds6. done. }
    assert (Hrun8 : g_running s8 = g_running s) by done.
    assert (Hcur8 : g_current s8 = g_current s) by (simpl; done).
    assert (Hfr8 : forall m, memo_frame (nodes_and_windows s6) (nodes_and_windows s8) m) by (intros m; apply memo_frame_refl).
    (* the failure *)
    name_k K9.
    assert (Hk8 : has_kind n KMemo s8) by (apply Hkinds8; by exists d1).
    assert (Hs9 : exists s9, K9 tt s9 = Some (s', a) /\ check_invariant_unpublished (Some n) s9 /\ frame s8 s9).
    { destruct result as [v|y].
      - rewrite bind_m_return in Hm7. eexists. split; [exact Hm7|]. split; [done|apply frame_refl].
      - destruct Hk8 as (e & He & Hek).
        rewrite (bind_modify_node _ _ _ _ _ He) in Hm7. eexists. split; [exact Hm7|]. split.
        + apply (invariant_set_gen (Some n) None n
            (fun x => node_with_violated false (node_with_status StError (node_with_failure (Some y) x))) e);
            [done|done|done|done|intros; left; done|done|intros ???; left; simpl in *; split_and!; done].
        + apply (frame_set n (fun x => node_with_violated false (node_with_status StError (node_with_failure (Some y) x))) e);
            [done|done|intros _; ms]. }
    clear Hm7. destruct Hs9 as (s9 & Hm9 & Hi9 & F9). subst K9. cbv beta in Hm9.
    destruct F9 as (Hrun9 & Hcur9 & Hkinds9 & Hfr9). rewrite Hrun8 in Hrun9. rewrite Hcur8 in Hcur9.
    assert (Hk9 : has_kind n KMemo s9) by (by apply Hkinds9).
    destruct Hk9 as (d2 & Hd2 & Hk2).
    rewrite (bind_get_node _ _ _ _ Hd2) in Hm9.
    (* moved *)
    name_k K10.
    assert (Hs10 : exists s10 mv, K10 mv s10 = Some (s', a) /\ check_invariant_unpublished (Some n) s10 /\ frame s9 s10).
    { destruct (decide _).
      - rewrite bind_m_return in Hm9. do 2 eexists. split; [exact Hm9|]. split; [done|apply frame_refl].
      - destruct (n_comparer d2) as [| |y].
        + rewrite bind_m_return in Hm9. do 2 eexists. split; [exact Hm9|]. split; [done|apply frame_refl].
        + rewrite bind_m_return in Hm9. do 2 eexists. split; [exact Hm9|]. split; [done|apply frame_refl].
        + rewrite bind_bind in Hm9. rewrite (bind_modify_node _ _ _ _ _ Hd2) in Hm9. rewrite bind_m_return in Hm9.
          do 2 eexists. split; [exact Hm9|]. split.
          * apply (invariant_set_gen (Some n) None n
              (fun x => node_with_status StError (node_with_failure (Some y) (node_with_value (n_value d) x))) d2);
              [done|done|done|done|intros; left; done|done|intros ???; left; simpl in *; split_and!; done].
          * apply (frame_set n (fun x => node_with_status StError (node_with_failure (Some y) (node_with_value (n_value d) x))) d2);
              [done|done|intros _; ms]. }
    clear Hm9. destruct Hs10 as (s10 & mv & Hm10 & Hi10 & F10). subst K10. cbv beta in Hm10.
    destruct F10 as (Hrun10 & Hcur10 & Hkinds10 & Hfr10). rewrite Hrun9 in Hrun10. rewrite Hcur9 in Hcur10.
    rewrite bind_get in Hm10. rewrite bind_m_modify in Hm10.
    assert (Hk10 : has_kind n KMemo s10) by (apply Hkinds10; by exists d2).
    destruct Hk10 as (d3 & Hd3 & Hk3).
    unfold ghost_run_end in Hm10. rewrite Hd3 in Hm10.
    set (G := {| gh_published := _; gh_seen := _; gh_unnoticed := _; gh_window := _; gh_reads := _; gh_passed := _; gh_over := _; gh_run_reads := _; gh_nested := _ |}) in Hm10.
    (* the notice *)
    name_k K11.
    assert (Hs11 : exists s11, K11 tt s11 = Some (s', a) /\ check_invariant s11 /\ frame s10 s11).
    { assert (Hreach : world_reachable (g_edges s10)) by apply (ci_reachable _ _ _ Hi10).
      destruct mv.
      - apply bind_step in Hm10 as (s11 & Hn & Hm11).
        exists s11. split; [exact Hm11|].
        apply (invariant_told n d3 s10 G); [done|done|done|done|done|simpl; set_solver| |].
        + eapply (any_mark_rtc f n (g_current s10)). right; right; right; right. done.
        + intros c dc Hc Hkc Hpre.
          destruct (decide (g_current s10 = Some c)) as [Hr|Hr].
          * right. right. subst G. simpl. unfold run_end_unnoticed. rewrite Hr.
            rewrite decide_True by done. set_solver.
          * right. left. eapply (notify_dirty_ends_dirty f n (g_current s10) (with_ghost G s10)).
            -- by apply prefix_observer.
            -- exists dc. split; [done|by left].
            -- right. split; [exact Hn|done].
      - rewrite bind_m_return in Hm10. exists (with_ghost G s10). split; [exact Hm10|].
        apply (invariant_told n d3 s10 G); [done|done|done|done|done|simpl; set_solver|apply rtc_refl|].
        intros c dc Hc Hkc Hpre. subst G. simpl. unfold run_end_unnoticed.
        destruct (decide (gh_published (g_ghost s10) !! n = Some (read_view_of d3))) as [Hp|Hp].
        + destruct (ci_seen _ _ _ Hi10 c dc n Hc Hkc Hpre ltac:(done)) as [H1|[H1|H1]].
          * left. intros v Hv. pose proof (H1 v Hv) as H2. rewrite Hp in H2. by injection H2.
          * right. left. by exists dc.
          * right. right. set_solver.
        + right. right. apply elem_of_union_l, elem_of_list_to_set. by apply prefix_observer. }
    clear Hm10. destruct Hs11 as (s11 & Hm11 & Hi11 & F11). subst K11. cbv beta in Hm11.
    destruct F11 as (Hrun11 & Hcur11 & Hkinds11 & Hfr11). rewrite Hrun10 in Hrun11. rewrite Hcur10 in Hcur11.
    assert (Hk11 : has_kind n KMemo s11) by (apply Hkinds11; by exists d3).
    destruct Hk11 as (d4 & Hd4 & Hk4).
    rewrite (bind_get_node _ _ _ _ Hd4) in Hm11.
    assert (Hkinds_all : forall m k, has_kind m k s -> has_kind m k s11).
    { intros m k Hmk. apply Hkinds11, Hkinds10, Hkinds9, Hkinds8, Hkinds5, Hkinds4, Hmk. }
    assert (Hd4' : (nodes_and_windows s4).1 !! n = Some (u d)) by exact Hd4'0.
    assert (Hfr_late : forall m, memo_frame (nodes_and_windows s4) (nodes_and_windows s11) m).
    { intros m. eapply memo_frame_trans; [apply Hfr5|].
      eapply memo_frame_trans; [apply Hfr6|].
      eapply memo_frame_trans; [apply Hfr8|].
      eapply memo_frame_trans; [apply Hfr9|].
      eapply memo_frame_trans; [apply Hfr10|apply Hfr11]. }
    assert (Hfr_all : forall m, memo_frame (nodes_and_windows s) (nodes_and_windows s11) m).
    { intros m. eapply memo_frame_trans; [apply Hfr4|apply Hfr_late]. }
    assert (Hpost : closed (nodes_and_windows s11) n \/ n_disposed d4 = true).
    { destruct (Hfr_late n (u d) Hd4' ltac:(done)) as (e & He & _ & Hr & _ & Hwn & _ & _).
      simpl in He. assert (e = d4) by congruence. subst e.
      destruct Hwn as [Hwn|[Hwn|Hwn]]; [left|by left|by right].
      eapply closed_keep; [exact Hclosed4|exact Hwn|exact Hd4'|exact Hd4|exact Hr]. }
    destruct (decide (n_freshness d4 = Clean)) as [Hc4|Hc4].
    - unfold m_return in Hm11. simplify_eq. split_and!; [done| |by exists d4].
      split_and!; [done|done|done|done].
    - unfold announce in Hm11. destruct (f_unannounced (g_flags s11)).
      2: { simplify_eq. split_and!; [done| |by exists d4]. split_and!; [done|done|done|done]. }
      unfold modify_node in Hm11. rewrite Hd4 in Hm11. simplify_eq.
      assert (Hclosed' : closed (nodes_and_windows (set_node n (node_with_unannounced true d4) s11)) n \/
                         n_disposed (node_with_unannounced true d4) = true).
      { destruct Hpost as [Hp|Hp]; [left|by right].
        intros e r9 He Hr. simpl in He. rewrite lookup_insert_eq in He. injection He as <-.
        exact (Hp d4 r9 Hd4 Hr). }
      split_and!.
      + refine (invariant_set_gen None None n (node_with_unannounced true) d4 s11 Hi11 Hd4 eq_refl
                  (fun _ _ _ => eq_refl) (fun _ H => or_introl H) (le_n _) _).
        intros _ Hc. simpl in Hc. congruence.
      + split_and!; [done|done| |].
        * intros m k Hmk. apply (has_kind_set n d4 (node_with_unannounced true d4)); [done|done|auto].
        * intros m. destruct (decide (m = n)) as [->|Hne].
          -- intros e He Hek. simpl in He. rewrite Hd in He. injection He as <-.
             destruct (Hfr_all n d Hd Hek) as (e' & He' & Hk' & Hr' & Hdi' & _).
             simpl in He'. assert (e' = d4) by congruence. subst e'.
             exists (node_with_unannounced true d4). simpl. rewrite lookup_insert_eq. split_and!; [done|done|done|done| | |].
             ++ destruct Hclosed' as [H1|H1]; [right; left|right; right]; done.
             ++ intros _. destruct Hclosed' as [H1|H1]; [right; left|left]; done.
             ++ intros _ _. destruct Hclosed' as [H1|H1]; [right; left|left]; done.
          -- eapply memo_frame_trans; [apply Hfr_all|].
             intros e He Hek. simpl in He. exists e. simpl. rewrite lookup_insert_ne by done.
             split_and!; auto.
             ++ intros [G1|[G1|G1]]; [by left|right; left|by right; right].
                intros e' r9 Hl Hw2. simpl in Hl, Hw2. rewrite lookup_insert_ne in Hl by done. eauto.
             ++ intros H1 H2. done.
      + exists (node_with_unannounced true d4). split; [simpl; apply lookup_insert_eq|done].
  Qed.

  Lemma ok_run_memo_S n : ok_under (has_kind n KMemo) (run_memo (S f) n).
  Proof. intros s s' a Hi Hk Hm. destruct (ok_run_memo_full n s s' a Hi Hk Hm) as (? & ? & _). by split. Qed.

  Lemma ok_run_post_S n : ok_post (run_memo (S f) n) n.
  Proof. intros s s' a Hi Hk Hm. by destruct (ok_run_memo_full n s s' a Hi Hk Hm) as (_ & _ & ?). Qed.

  Lemma ok_execute_S n : ok_under (has_kind n KEffect) (execute_effect (S f) n).
  Proof.
    cbn [execute_effect]. apply ok_bind; [apply _|apply ok_get_node|intros d].
    destruct (n_disposed d).
    { apply ok_modify_node. nstep. }
    apply ok_bind; [apply _| |intros _].
    - destruct (decide _); [|apply ok_return].
      apply ok_bind; [apply _|apply ok_any; ih IH|intros _].
      apply ok_bind; [apply _|apply ok_get_node|intros d'].
      destruct (decide _); [|apply ok_return].
      apply ok_bind; [apply _|apply ok_reads_only, resolved_reads_only|intros _].
      apply ok_modify_effect; intros ?; simpl; try done; lia.
    - apply ok_bind; [apply _|apply ok_get_node|intros d'].
      apply ok_bind; [apply _|apply ok_modify_effect; intros ?; simpl; try done; lia|intros _].
      destruct (decide _); [ih IH|apply ok_return].
  Qed.

  (** The rest of an effect's run, through its pop. *)
  Definition ok_effect_rest {A} (n : node_id) (rest : list node_id) (m : M A) : Prop :=
    forall s s' a, check_invariant s -> has_kind n KEffect s -> g_running s = n :: rest ->
      (forall c, g_current s = Some c -> c ∈ rest) ->
      m s = Some (s', a) ->
      check_invariant s' /\ g_running s' = rest /\ g_current s' = g_current s /\
      (forall m k, has_kind m k s -> has_kind m k s') /\
      forall m, memo_frame (nodes_and_windows s) (nodes_and_windows s') m.

  Lemma ok_effect_rest_bind {A B} n rest (m : M A) (k : A -> M B) :
    ok_under (in_effect_run n) m -> (forall a, ok_effect_rest n rest (k a)) -> ok_effect_rest n rest (m_bind m k).
  Proof.
    intros Hm Hk s s' b Hi Hkn Hr Hc Hb. unfold m_bind in Hb.
    destruct (m s) as [[s1 a]|] eqn:E; [|done].
    destruct (Hm _ _ _ Hi ltac:(split; [done|rewrite Hr; left]) E) as [Hi1 (Hr1 & Hc1 & Hk1 & Hf1)].
    destruct (Hk a _ _ _ Hi1 (Hk1 _ _ Hkn) ltac:(congruence) ltac:(by rewrite Hc1) Hb)
      as (Hi2 & Hr2 & Hc2 & Hk2 & Hf2).
    split_and!; [done|done|congruence|auto|].
    intros x. eapply memo_frame_trans; eauto.
  Qed.

  Lemma ok_effect_rest_pop n rest : ok_effect_rest n rest pop_running.
  Proof.
    intros s s' a Hi Hk Hr Hc Hm. unfold pop_running, m_modify in Hm. simplify_eq.
    simpl. rewrite Hr. simpl. split_and!; [|done|done|done|intros m; apply memo_frame_refl].
    apply (invariant_pop n); [done|done| |done|by right].
    intros Hm. pose proof (has_kind_unique _ _ _ _ Hk Hm). done.
  Qed.

  Lemma ok_run_effect_S n : ok_under (has_kind n KEffect) (run_effect (S f) n).
  Proof.
    intros s s' a Hi Hk Hm. cbn [run_effect] in Hm.
    unfold push_running, edges in Hm. rewrite !bind_m_modify in Hm.
    assert (Hi1 : check_invariant (with_edges (sources_begin_run n (g_edges (with_running (n :: g_running s) s)))
                              (with_running (n :: g_running s) s))).
    { refine (invariant_effect_begin n (n :: g_running s) s Hi Hk _ _ _).
      - intros c Hc. by right.
      - left.
      - intros c Hc. by apply elem_of_cons in Hc. }
    assert (Hrest : ok_effect_rest n (g_running s) (
      let* _ := m_modify (ghost_effect_start n) in
      let* d := get_node n in
      let* _ :=
        match n_scope d with
        | Some o =>
          let* _ := modify_node n (node_with_in_cleanup true) in
          let* _ := discharge f o in
          modify_node n (node_with_in_cleanup false)
        | None => m_return tt
        end in
      let* d' := get_node n in
      let* _ := if n_disposed d' then m_return tt else run_effect_body f n in
      pop_running)).
    { apply ok_effect_rest_bind; [apply ok_reads_only, effect_start_reads_only|intros _].
      apply ok_effect_rest_bind; [apply ok_get_node|intros d].
      apply ok_effect_rest_bind.
      { destruct (n_scope d) as [o|]; [|apply ok_return].
        apply ok_bind; [apply _|apply ok_modify_node; nstep|intros _].
        apply ok_bind; [apply _|apply ok_any; ih IH|intros _].
        apply ok_modify_node. nstep. }
      intros _.
      apply ok_effect_rest_bind; [apply ok_get_node|intros d'].
      apply ok_effect_rest_bind.
      { destruct (n_disposed d'); [apply ok_return|ih IH]. }
      intros _. apply ok_effect_rest_pop. }
    destruct (Hrest _ _ _ Hi1 Hk ltac:(done) (ci_current _ _ _ Hi) Hm) as (Hi2 & Hr2 & Hc2 & Hk2 & Hf2).
    split; [done|]. split_and!; [done|done|auto|exact Hf2].
  Qed.

  Lemma ok_body_S n : ok_under (in_effect_run n) (run_effect_body (S f) n).
  Proof.
    cbn [run_effect_body].
    apply ok_bind; [apply _|apply ok_get_node|intros d].
    apply ok_bind; [apply _| |intros _].
    { apply (ok_weaken (has_kind n KEffect)); [intros s [? _]; done|apply ok_modify_effect; intros ?; simpl; try done; lia]. }
    apply ok_bind; [apply _|apply ok_emit|intros _].
    apply ok_bind; [apply _| |intros r].
    { apply (ok_weaken (fun s => n ∈ g_running s)); [intros s [_ ?]; done|apply ok_hosted; ih IH]. }
    apply ok_bind; [apply _|apply ok_get_node|intros d'].
    apply ok_bind; [apply _|apply ok_edges_opt_end|intros _].
    destruct r; [apply ok_return|].
    apply ok_bind; [apply _| |intros _; apply ok_emit].
    apply (ok_weaken (has_kind n KEffect)); [intros s [? _]; done|apply ok_modify_effect; intros ?; simpl; try done; lia].
  Qed.

  Lemma ok_discharge_S o : ok (discharge (S f) o).
  Proof. cbn [discharge]. go IH. Qed.

  Lemma ok_retire_S o : ok (retire (S f) o).
  Proof. cbn [retire]. go IH. Qed.

  Lemma ok_dispose_scope_S o : ok (dispose_scope (S f) o).
  Proof. cbn [dispose_scope]. go IH. Qed.

  Lemma ok_teardown_S o ls cs : ok (teardown (S f) o ls cs).
  Proof. cbn [teardown]. go IH. Qed.

  Lemma ok_cleanup_S o b e : ok (run_cleanup (S f) o b e).
  Proof. cbn [run_cleanup]. unfold record_error. go IH. Qed.

  Lemma ok_release_S n : ok (release (S f) n).
  Proof. cbn [release]. go IH. Qed.

  Lemma ok_dispose_S n : ok (dispose_node (S f) n).
  Proof. cbn [dispose_node]. unfold unlink. go IH. Qed.

  Lemma ok_attach_S n o : ok (attach (S f) n o).
  Proof. cbn [attach]. go IH. Qed.

  Lemma ok_write_S n v : ok (write_signal (S f) n v).
  Proof.
    cbn [write_signal]. apply ok_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek; try apply ok_stuck.
    destruct (decide _); [apply ok_return|].
    intros s s' a Hi [_ Hd] Hm.
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm. rewrite bind_m_modify in Hm.
    set (s1 := set_node n (node_with_value v d) s) in Hm.
    assert (Hi1 : check_invariant_unpublished (Some n) s1).
    { apply (invariant_set_gen (Some n) None n (node_with_value v) d);
        [by apply invariant_weaken|done|done|done|intros; congruence|done|intros; congruence]. }
    assert (F1 : frame s s1) by (apply (frame_set n (node_with_value v) d); [done|done|intros; congruence]).
    assert (Hd1 : g_nodes s1 !! n = Some (node_with_value v d)) by (simpl; apply lookup_insert_eq).
    unfold ghost_publish in Hm. rewrite Hd1 in Hm.
    apply bind_step in Hm as (s2 & Hn & Hm2).
    set (G := {| gh_published := _; gh_seen := _; gh_unnoticed := _; gh_window := _; gh_reads := _; gh_passed := _; gh_over := _; gh_run_reads := _; gh_nested := _ |}) in Hn.
    assert (Hreach : world_reachable (g_edges s1)) by apply (ci_reachable _ _ _ Hi1).
    destruct (invariant_told n (node_with_value v d) s1 G s2) as [Hi2 F2]; [done|done|done|done|done|simpl; set_solver| | |].
    { apply (any_mark_rtc f n None). right; right; right; left. done. }
    { intros c dc Hc Hkc Hpre. right. left.
      eapply (notify_dirty_ends_dirty f n None (with_ghost G s1)).
      - by apply prefix_observer.
      - exists dc. split; [done|by left].
      - left. exact Hn. }
    assert (Hflush : ok (request_flush f)) by ih IH.
    destruct (Hflush _ _ _ Hi2 I Hm2) as [Hi3 F3].
    split; [done|]. eapply frame_trans; [exact F1|]. by eapply frame_trans.
  Qed.

  Lemma ok_request_S : ok (request_flush (S f)).
  Proof. cbn [request_flush]. go IH. Qed.

  Lemma ok_settle_S : ok (settle_flush (S f)).
  Proof. cbn [settle_flush]. go IH. Qed.

  Lemma ok_flush_S : ok (run_flush (S f)).
  Proof. cbn [run_flush]. go IH. Qed.

  Lemma ok_drain_S : ok (drain (S f)).
  Proof.
    cbn [drain]. intros s s' a Hi _ Hm. rewrite bind_get in Hm.
    destruct (g_queue s !! g_queue_head s) as [n|] eqn:Eq.
    - rewrite bind_m_modify in Hm.
      assert (Hin : n ∈ g_queue s) by (apply list_elem_of_lookup; by exists (g_queue_head s)).
      assert (Hi1 : check_invariant (with_queue_head (S (g_queue_head s)) s)) by (eapply invariant_irrelevant; [|exact Hi]; irr).
      assert (Hk : has_kind n KEffect (with_queue_head (S (g_queue_head s)) s)) by apply (ci_queue _ _ _ Hi n Hin).
      assert (Hrest : ok_under (has_kind n KEffect) (let* _ := execute_effect f n in drain f)).
      { apply ok_bind; [apply _|ih IH|intros _; apply ok_any; ih IH]. }
      destruct (Hrest _ _ _ Hi1 Hk Hm) as [Hi2 F2].
      split; [done|]. eapply frame_trans; [|exact F2]. apply frame_irrelevant. irr.
    - unfold m_modify in Hm. simplify_eq. split.
      + destruct Hi. unfold with_queue, with_queue_head. split; gs; try done.
        intros y Hy. by apply not_elem_of_nil in Hy.
      + by apply frame_same.
  Qed.
End Step.

(**md
**Every engine function keeps the check invariant, at every fuel.**

When a function returns, the [check invariant](#check_invariant) still holds, `g_running` and `g_current` are back to their starting values, each node keeps its kind, and each memo's run count and disposal only go forward (`frame`). After `run_memo` or `recompute` of a memo, the memo is disposed, or `RecomputeScoped` has never cleared its `Dirty`, or the memo has run since `RecomputeScoped` last did.

Premises: the invariant holds at the start. Functions over a memo or an effect require a node of that kind, and `run_effect_body` requires that effect's run to be in progress.

- [f:] the fuel.
*)
Theorem engine_ok_all f : engine_ok f.
Proof.
  induction f as [|f IH].
  - split; intros; try (intros s s' a _ _ Hm; destruct_all (option nat); done);
      intros s s' a _ _ Hm; done.
  - split.
    + apply ok_exec_S, IH.
    + apply ok_read_S, IH.
    + apply ok_read_node_S, IH.
    + apply ok_update_S, IH.
    + apply ok_refresh_or_pull_S, IH.
    + apply ok_pull_S, IH.
    + apply ok_refresh_S, IH.
    + apply ok_walk_S, IH.
    + apply ok_recompute_S, IH.
    + apply ok_run_memo_S, IH.
    + apply ok_execute_S, IH.
    + apply ok_run_effect_S, IH.
    + apply ok_body_S, IH.
    + apply ok_discharge_S, IH.
    + apply ok_retire_S, IH.
    + apply ok_dispose_scope_S, IH.
    + apply ok_teardown_S, IH.
    + apply ok_cleanup_S, IH.
    + apply ok_release_S, IH.
    + apply ok_dispose_S, IH.
    + apply ok_attach_S, IH.
    + apply ok_write_S, IH.
    + apply ok_request_S, IH.
    + apply ok_settle_S, IH.
    + apply ok_flush_S, IH.
    + apply ok_drain_S, IH.
    + apply ok_run_post_S, IH.
    + apply ok_recompute_post_S, IH.
Qed.

(** ** The invariant holds wherever a program goes *)

Lemma slot_view_empty c : slot_view empty_world c = ([], 0).
Proof. unfold slot_view, sources_of. simpl. rewrite lookup_empty. done. Qed.

(**md
**A new graph satisfies the check invariant.**

The [check invariant](#check_invariant) holds under any flags.

- [fl:] the flags of the new graph.
*)
Theorem check_invariant_init fl : check_invariant (init fl).
Proof.
  unfold init. split; gs.
  - apply reachable_empty.
  - intros c. rewrite slot_view_empty. simpl. lia.
  - intros c (d & Hd & _). simpl in Hd. rewrite lookup_empty in Hd. done.
  - intros n Hn. rewrite lookup_empty in Hn. by destruct Hn.
  - intros c y Hy. rewrite slot_view_empty in Hy. by apply not_elem_of_nil in Hy.
  - intros c Hc. by apply not_elem_of_nil in Hc.
  - done.
  - intros y Hy. by apply not_elem_of_nil in Hy.
  - intros n d Hd. rewrite lookup_empty in Hd. done.
  - intros c d y Hd. rewrite lookup_empty in Hd. done.
  - intros c y Hy. rewrite read_prefix_view, slot_view_empty in Hy.
    simpl in Hy. by apply not_elem_of_nil in Hy.
  - intros n r Hr. simpl in Hr. rewrite lookup_empty in Hr. done.
  - intros n d r Hd. rewrite lookup_empty in Hd. done.
  - intros n d Hd. rewrite lookup_empty in Hd. done.
Qed.

(**md
**A whole program keeps the check invariant.**

If the [check invariant](#check_invariant) holds at the start and `run_top` returns, the invariant holds in the final state. A program that gets stuck or runs out of fuel returns nothing, and is outside this guarantee.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state.
- [s':] the state after the run.
- [r:] the outcome of the run.
*)
Theorem check_invariant_run_top f c s s' r :
  check_invariant s -> run_top f c s = Some (s', r) -> check_invariant s'.
Proof. intros Hi Hm. exact (proj1 (Eexec _ (engine_ok_all f) [] c _ _ _ Hi I Hm)). Qed.

(**md
**`update_if_necessary` keeps the check invariant.**

If the [check invariant](#check_invariant) holds at the start and `update_if_necessary` returns, the invariant holds afterwards.

- [f:] the fuel.
- [n:] the node brought up to date.
- [s:] the starting state.
- [s':] the state after the update.
*)
Theorem check_invariant_update f n s s' :
  check_invariant s -> update_if_necessary f n s = Some (s', tt) -> check_invariant s'.
Proof. intros Hi Hm. exact (proj1 (Eupdate _ (engine_ok_all f) n _ _ _ Hi I Hm)). Qed.

(** ** What check resolution guarantees *)

(**md
**A memo short of `Dirty` with settled reads has read what each source now stores.**

Each source in the memo's slots has a recorded read, and each recorded read equals the source's stored value, status and failure, which can themselves be stale.

Premises: the [check invariant](#check_invariant) holds, and `reads_settled`: the memo has no unnoticed difference in its reads, and neither it nor any of its sources is running.

- [s:] the state.
- [n:] a memo that is not `Dirty`.
- [d:] the node `n` in `s`.
*)
Theorem unmoved_short_of_dirty s n d :
  check_invariant s -> g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d <> Dirty ->
  reads_settled s n -> ~ moved_since_last_run s n.
Proof.
  intros Hi Hd Hk Hf (Hun & Hnr & Hsr) (src & i & mh & e & Hslot & He & Hne).
  assert (Hin : src ∈ fst <$> source_slots (sources_of (g_edges s) n)).
  { apply list_elem_of_lookup. exists i. rewrite list_lookup_fmap. unfold slot_at in Hslot. by rewrite Hslot. }
  assert (Hpre : src ∈ read_prefix (g_edges s) n).
  { rewrite read_prefix_view. pose proof (ci_ended _ _ _ Hi n ltac:(by exists d) Hnr) as Hl.
    rewrite Hl, firstn_all. done. }
  assert (Hkind : n_kind e <> KEffect).
  { destruct (ci_slots _ _ _ Hi n src Hin) as [_ [(e' & He' & Hk')|(e' & He' & Hk')]]; simplify_eq; congruence. }
  pose proof (ci_published _ _ _ Hi src e He Hkind (Hsr src Hin) ltac:(done)) as Hpub.
  destruct Hne as [Hne|(v & Hv & Hne)]; [exact (ci_read _ _ _ Hi n src Hpre Hne)|].
  destruct (ci_seen _ _ _ Hi n d src Hd Hk Hpre ltac:(done)) as [H1|[H1|H1]]; [|done|done].
  apply Hne. pose proof (H1 v Hv) as H2. rewrite Hpub in H2. by injection H2.
Qed.

(**md
**A memo resolved `Clean` with settled reads has read what each source now stores.**

Each source in the memo's slots has a recorded read, and each recorded read equals the source's stored value, status and failure, which can themselves be stale: [`self_marking_through_memo_unmoved`](Ranvier.Pure.Witnesses.html#self_marking_through_memo_unmoved) has the source `Dirty` and the reader `Clean`.

Premises: the [check invariant](#check_invariant) holds, the memo starts in `Check`, `update_if_necessary` returns with the memo `Clean`, and `reads_settled` holds at the end. `reads_settled` says the memo has no unnoticed difference in its reads, and neither it nor any of its sources is running. The guarantee comes from the invariant and that premise, not from the walk. The unqualified claim is false: see `resolve_clean_no_move_statement`.

- [f:] the fuel.
- [n:] the memo, in `Check` before the update.
- [s:] the state before the update.
- [s':] the state after the update.
- [d:] the node `n` in `s`.
- [d':] the node `n` in `s'`, which is `Clean`.
*)
Theorem resolve_clean_unmoved f n s s' d d' :
  check_invariant s ->
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d = Check ->
  update_if_necessary f n s = Some (s', tt) ->
  g_nodes s' !! n = Some d' -> n_freshness d' = Clean ->
  reads_settled s' n ->
  ~ moved_since_last_run s' n.
Proof.
  intros Hi Hd Hk _ Hu Hd' Hc Hset.
  pose proof (Eupdate _ (engine_ok_all f) n _ _ _ Hi I Hu) as [Hi' (_ & _ & Hks & _)].
  destruct (Hks n KMemo ltac:(by exists d)) as (e & He & Hek). simplify_eq.
  eapply unmoved_short_of_dirty; eauto. congruence.
Qed.

(** The unqualified statement, which the fix phase owes. [check_resolves_over_moved_source] in [BUGS.md] is
    its counterexample on the present engine. *)
Definition resolve_clean_no_move_statement : Prop :=
  forall f n s s' d d',
    check_invariant s ->
    g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d = Check ->
    update_if_necessary f n s = Some (s', tt) ->
    g_nodes s' !! n = Some d' -> n_freshness d' = Clean ->
    ~ moved_since_last_run s' n.

(**md
**Open statement, false on the present engine: a memo resolved `Clean` has read what each source now stores, without the `reads_settled` premise.**

`resolve_clean_no_move_statement` is the behaviour the fix phase owes. The Confirmed bug [`check_resolves_over_moved_source`](Ranvier.Pure.Witnesses.html#check_resolves_over_moved_source) falsifies it, and [`resolve_clean_no_move_falsified`](Ranvier.Pure.Witnesses.html#resolve_clean_no_move_falsified) proves that. Until the fix, later layers use [`resolve_clean_unmoved`](#resolve_clean_unmoved).
*)

(** A memo that [update_if_necessary] resolves [Clean] has no live stale source, which the fix phase owes.
    [self_marking_wake_lost] in [BUGS.md] is its counterexample on the present engine. A disposed source stays
    stale: [resolve_over_disposed_dirty_source]. *)
Definition resolve_clean_no_stale_source_statement : Prop :=
  forall f n s s' d d',
    check_invariant s ->
    g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d = Check ->
    update_if_necessary f n s = Some (s', tt) ->
    g_nodes s' !! n = Some d' -> n_freshness d' = Clean ->
    forall src e, src ∈ fst <$> source_slots (sources_of (g_edges s') n) -> g_nodes s' !! src = Some e ->
      n_disposed e = false -> stale (n_freshness e) = false.

(**md
**Open statement, false on the present engine: a memo resolved `Clean` has no live stale source.**

`resolve_clean_no_stale_source_statement` is the behaviour the fix phase owes. The Confirmed bug `self_marking_wake_lost` falsifies it, and [`resolve_clean_no_stale_source_falsified`](Ranvier.Pure.Witnesses.html#resolve_clean_no_stale_source_falsified) proves that. The statement covers live sources only, since a disposed source stays stale: see [`resolve_over_disposed_dirty_source`](Ranvier.Pure.Witnesses.html#resolve_over_disposed_dirty_source).
*)

(**md
**The check walk stops as soon as the node leaves `Check`.**

With the node `Clean` or `Dirty`, the walk returns the state unchanged at this slot, given any positive fuel.

- [f:] the fuel, which the walk runs at `S f`.
- [n:] the node walked.
- [i:] the slot the walk is at.
- [s:] the state, which the walk returns unchanged.
- [d:] the node `n` in `s`.
*)
Theorem resolve_dirty_stops_walk f n i s d :
  g_nodes s !! n = Some d -> n_freshness d <> Check ->
  resolve_walk (S f) n i s = Some (s, tt).
Proof.
  intros Hd Hne. cbn [resolve_walk]. rewrite (bind_get_node _ _ _ _ Hd), bind_get.
  by rewrite decide_False.
Qed.

(**md
**The check walk updates one source, then moves to the next slot.**

With the node in `Check` and a source in this slot, the walk runs `update_if_necessary` on that source and continues from the next slot, given any positive fuel.

- [f:] the fuel, which the walk runs at `S f`.
- [n:] the node walked, in `Check`.
- [i:] the slot the walk is at.
- [s:] the state.
- [d:] the node `n` in `s`.
- [src:] the source in slot `i`.
- [mh:] the edge handle of slot `i`.
*)
Theorem resolve_walk_step f n i s d src mh :
  g_nodes s !! n = Some d -> n_freshness d = Check ->
  source_slots (sources_of (g_edges s) n) !! i = Some (src, mh) ->
  resolve_walk (S f) n i s = m_bind (update_if_necessary f src) (fun _ => resolve_walk f n (S i)) s.
Proof.
  intros Hd Hc Hs. cbn [resolve_walk]. rewrite (bind_get_node _ _ _ _ Hd), bind_get.
  rewrite decide_True by done. by rewrite Hs.
Qed.

(**md
**Resolving a live memo to `Clean` clears its announcement flag.**

Holds on every path of `update_if_necessary`: the walk ending in `Check`, or a run after it.

Premises: the [check invariant](#check_invariant) holds, the memo starts in `Check` and ends `Clean`, and the memo is not disposed at the end. A memo disposed during `RecomputeScoped` can keep the flag, as [`disposed_window_keeps_unannounced`](Ranvier.Pure.Witnesses.html#disposed_window_keeps_unannounced) shows.

- [f:] the fuel.
- [n:] the memo, in `Check` before the update.
- [s:] the state before the update.
- [s':] the state after the update.
- [d:] the node `n` in `s`.
- [d':] the node `n` in `s'`, which is `Clean` and not disposed.
*)
Theorem resolve_clears_unannounced f n s s' d d' :
  check_invariant s ->
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d = Check ->
  update_if_necessary f n s = Some (s', tt) ->
  g_nodes s' !! n = Some d' -> n_freshness d' = Clean -> n_disposed d' = false ->
  n_unannounced d' = false.
Proof.
  intros Hi Hd Hk Hc Hu Hd' Hc' Hl.
  destruct (Eupdate _ (engine_ok_all f) n _ _ _ Hi I Hu) as [Hi' (_ & _ & _ & Hfr)].
  destruct (Hfr n d Hd Hk) as (e & He & Hek & _ & _ & _ & _ & Hs).
  simpl in He. assert (e = d') by congruence. subst e.
  destruct (Hs ltac:(congruence) Hc') as [H|[H|H]]; [congruence| |done].
  destruct (n_unannounced d') eqn:Eu; [|done].
  destruct (ci_unannounced _ _ _ Hi' n d' Hd' Hek Hc' Eu Hl) as (r & Hr & Hle).
  pose proof (H d' r Hd' Hr). lia.
Qed.

(**md
**A check walk that ends in `Check` makes the node `Clean` with no run after the walk.**

For a node that starts in `Check`, `refresh` succeeds with the node as the walk left it, except `Clean` with `n_unannounced` false. Its run count is the one the walk left, which says nothing of runs during the walk.

- [f:] the fuel, which `refresh` runs at `S f`.
- [n:] the memo, in `Check` before and after the walk.
- [s:] the state before the walk.
- [s1:] the state after the walk.
- [d:] the node `n` in `s`.
- [d1:] the node `n` in `s1`.
*)
Theorem resolve_walk_check_clears f n s s1 d d1 :
  g_nodes s !! n = Some d -> n_freshness d = Check ->
  resolve_walk f n 0 s = Some (s1, tt) ->
  g_nodes s1 !! n = Some d1 -> n_freshness d1 = Check ->
  exists s', refresh (S f) n s = Some (s', tt) /\
    g_nodes s' !! n = Some (node_with_unannounced false (node_with_freshness Clean d1)) /\
    n_freshness (node_with_unannounced false (node_with_freshness Clean d1)) = Clean /\
    n_unannounced (node_with_unannounced false (node_with_freshness Clean d1)) = false /\
    n_runs (node_with_unannounced false (node_with_freshness Clean d1)) = n_runs d1.
Proof.
  intros Hd Hc Hw Hd1 Hc1.
  assert (Hg : g_nodes (ghost_resolved n s1) !! n = Some d1) by exact Hd1.
  assert (Hd2 : g_nodes (set_node n (node_with_unannounced false (node_with_freshness Clean d1)) (ghost_resolved n s1)) !! n =
                Some (node_with_unannounced false (node_with_freshness Clean d1))) by (simpl; apply lookup_insert_eq).
  exists (set_node n (node_with_unannounced false (node_with_freshness Clean d1)) (ghost_resolved n s1)).
  split_and!; [|done|done|done|done].
  cbn [refresh]. rewrite (bind_get_node _ _ _ _ Hd), decide_True by done.
  rewrite bind_bind, (bind_some _ _ _ _ _ Hw).
  rewrite bind_bind, (bind_get_node _ _ _ _ Hd1), decide_True by done.
  rewrite bind_bind, bind_m_modify, (bind_modify_node _ _ _ _ _ Hg).
  rewrite (bind_get_node _ _ _ _ Hd2), decide_False by done.
  reflexivity.
Qed.

(**md
**Updating a signal does nothing.**

`update_if_necessary` on a signal returns the state unchanged, given any positive fuel.

- [f:] the fuel, which the update runs at `S f`.
- [n:] the signal.
- [s:] the state, which the update returns unchanged.
- [d:] the node `n` in `s`.
*)
Theorem signal_update_noop f n s d :
  g_nodes s !! n = Some d -> n_kind d = KSignal ->
  update_if_necessary (S f) n s = Some (s, tt).
Proof.
  intros Hd Hk. cbn [update_if_necessary]. rewrite (bind_get_node _ _ _ _ Hd), Hk. done.
Qed.

Lemma track_nodes n s s' : track n s = Some (s', tt) -> g_nodes s' = g_nodes s.
Proof.
  unfold track. destruct (g_current s); [|intros; by simplify_eq].
  destruct (sources_add _ _ _); [|done]. intros Hm. simplify_eq. unfold ghost_read. simpl.
  by destruct (g_nodes s !! n).
Qed.

Lemma read_node_disposed f n s s' d o :
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_disposed d = true ->
  read_node f n s = Some (s', o) ->
  g_nodes s' = g_nodes s /\
  o = match n_status d with StError => Fail (default 0%Z (n_failure d)) | _ => Ok (n_value d) end.
Proof.
  intros Hd Hk Hdisp Hr. destruct f as [|f]; [done|]. cbn [read_node] in Hr.
  rewrite (bind_get_node _ _ _ _ Hd), Hk in Hr.
  assert (Hl : stale_live d = false) by (unfold stale_live; rewrite Hdisp; apply andb_false_r).
  rewrite Hl in Hr. unfold m_bind in Hr.
  destruct (track n s) as [[s1 []]|] eqn:Et; [|done].
  apply track_nodes in Et.
  assert (Hd1 : g_nodes s1 !! n = Some d) by (by rewrite Et).
  fold (m_bind (get_node n) (fun d' => match n_status d' with
      | StError => m_return (Fail (default 0%Z (n_failure d')))
      | _ => m_return (Ok (n_value d')) end) s1) in Hr.
  rewrite (bind_get_node _ _ _ _ Hd1) in Hr.
  destruct (n_status d); unfold m_return in Hr; by simplify_eq.
Qed.

(**md
**Reading a disposed memo keeps every node as it was and answers what it stored.**

The read returns a failure when the memo's status is `StError`, otherwise the stored value. The failure is `n_failure`, or 0 when `n_failure` is `None`. This covers nodes only: a tracked read can still add an edge.

- [f:] the fuel.
- [n:] the disposed memo read.
- [b:] whether the read is tracked.
- [s:] the state before the read.
- [s':] the state after the read.
- [d:] the node `n` in `s`.
- [o:] the outcome of the read.
*)
Theorem disposed_memo_no_refresh f n b s s' d o :
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_disposed d = true ->
  read f n b s = Some (s', o) ->
  g_nodes s' = g_nodes s /\
  o = match n_status d with StError => Fail (default 0%Z (n_failure d)) | _ => Ok (n_value d) end.
Proof.
  intros Hd Hk Hdisp Hr. destruct f as [|f]; [done|]. cbn [read] in Hr.
  destruct b; [by eapply read_node_disposed|].
  unfold untracked, m_bind, m_modify, m_return in Hr.
  destruct (read_node f n _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  eapply read_node_disposed in E as [Hn Ho]; [|done|done|done]. split; [done|done].
Qed.
