From stdpp Require Import prelude gmap.
From Ranvier.Base Require Import Prelude.
From Ranvier.Pure Require Import ObserverSet SourceList State.

(** ** Marks *)

(** A copy of [d] with new values of [n_freshness], [n_unannounced] and [n_queued]. *)
Definition node_marked (f : freshness) (u q : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := f; n_unannounced := u; n_runs := n_runs d; n_published := n_published d;
     n_disposed := n_disposed d; n_queued := q; n_in_cleanup := n_in_cleanup d;
     n_scope := n_scope d; n_violated := n_violated d; n_body := n_body d; n_env := n_env d;
     n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition set_node (n : node_id) (d : node) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := <[n := d]> (g_nodes s);
     g_owners := g_owners s; g_queue := g_queue s; g_queue_head := g_queue_head s;
     g_flushing := g_flushing s; g_flush_owed := g_flush_owed s; g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s;
     g_current := g_current s; g_untracked := g_untracked s; g_current_owner := g_current_owner s;
     g_next_id := g_next_id s; g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

(** [Graph.Schedule]. *)
Definition enqueue (n : node_id) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s;
     g_owners := g_owners s; g_queue := g_queue s ++ [n]; g_queue_head := g_queue_head s;
     g_flushing := g_flushing s; g_flush_owed := g_flush_owed s; g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s;
     g_current := g_current s; g_untracked := g_untracked s; g_current_owner := g_current_owner s;
     g_next_id := g_next_id s; g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

(** The backward walk of [NotifyDirty], [NotifyDirtyExcept] and [NotifyCheck]. The natural is the
    cursor plus one, and each step reads the observer set of the current state. A cursor at or past the
    count descends one position at a time, which reaches the position [i <- count - 1] selects. *)
Fixpoint walk_marks (mark : node_id -> state -> option state) (src : node_id) (n : nat) (s : state)
    : option state :=
  match n with
  | 0 => Some s
  | S i =>
    if decide (length (observer_items (observers_of (g_edges s) src)) <= i) then walk_marks mark src i s
    else match observer_items (observers_of (g_edges s) src) !! i with
         | Some (c, _) => mark c s ≫= walk_marks mark src i
         | None => None
         end
  end.

(**md
Marks the observers of `src` with `mark`, walking backward from the observer count of the starting state.

- [mark:] the mark applied to each observer visited.
- [src:] the source whose observers are marked.
*)
Definition notify_with (mark : node_id -> state -> option state) (src : node_id) (s : state) : option state :=
  walk_marks mark src (length (observer_items (observers_of (g_edges s) src))) s.

(** The [elif unannounced] test. *)
Definition announce_due (s : state) (d : node) : bool :=
  f_unannounced (g_flags s) && n_unannounced d.

(** [Effect.MarkDirty] and [Effect.MarkCheck], writing freshness [f]. *)
Definition effect_marked (f : freshness) (n : node_id) (d : node) (s : state) : state :=
  if n_disposed d || n_in_cleanup d then s
  else if n_queued d then set_node n (node_marked f (n_unannounced d) true d) s
  else enqueue n (set_node n (node_marked f (n_unannounced d) true d) s).

(** [MarkCheck]. A signal and an absent node take no mark: every F# observer is a computation.
    [None] is exhausted fuel. *)
Fixpoint mark_check (fuel : nat) (n : node_id) (s : state) {struct fuel} : option state :=
  match fuel with
  | 0 => None
  | S f =>
    match g_nodes s !! n with
    | Some d =>
      match n_kind d with
      | KMemo =>
        if decide (n_freshness d = Clean) then
          notify_with (mark_check f) n (set_node n (node_marked Check (n_unannounced d) (n_queued d) d) s)
        else if announce_due s d then
          notify_with (mark_check f) n (set_node n (node_marked (n_freshness d) false (n_queued d) d) s)
        else Some s
      | KEffect =>
        Some (effect_marked (if decide (n_freshness d = Clean) then Check else n_freshness d) n d s)
      | KSignal => Some s
      end
    | None => Some s
    end
  end.

(**md
`NotifyCheck`: marks each observer of `src` with `mark_check`.

- [fuel:] the bound on the recursion of the marks.
- [src:] the source whose observers are marked.
*)
Definition notify_check (fuel : nat) (src : node_id) (s : state) : option state :=
  notify_with (mark_check fuel) src s.

(** [MarkDirty]. A memo tells its observers [Check], so the recursion runs through [mark_check]. *)
Definition mark_dirty (fuel : nat) (n : node_id) (s : state) : option state :=
  match fuel with
  | 0 => None
  | S f =>
    match g_nodes s !! n with
    | Some d =>
      match n_kind d with
      | KMemo =>
        if decide (n_freshness d = Clean) then
          notify_with (mark_check f) n (set_node n (node_marked Dirty (n_unannounced d) (n_queued d) d) s)
        else if announce_due s d then
          notify_with (mark_check f) n (set_node n (node_marked Dirty false (n_queued d) d) s)
        else if decide (n_freshness d = Dirty) then Some s
        else Some (set_node n (node_marked Dirty (n_unannounced d) (n_queued d) d) s)
      | KEffect => Some (effect_marked Dirty n d s)
      | KSignal => Some s
      end
    | None => Some s
    end
  end.

(**md
`NotifyDirty`: marks each observer of `src` with `mark_dirty`.

- [fuel:] the bound on the recursion of the marks.
- [src:] the source whose observers are marked.
*)
Definition notify_dirty (fuel : nat) (src : node_id) (s : state) : option state :=
  notify_with (mark_dirty fuel) src s.

(** The visit of [NotifyDirtyExcept]. [r] is [CurrentComputation], and [None] is null. *)
Definition mark_except (fuel : nat) (r : option node_id) (c : node_id) (s : state) : option state :=
  if decide (r = Some c) then Some s else mark_dirty fuel c s.

(**md
`NotifyDirtyExcept`: marks each observer of `src` with `mark_dirty`, skipping the computation `r`.

- [fuel:] the bound on the recursion of the marks.
- [src:] the source whose observers are marked.
- [r:] the current computation, or `None`.
*)
Definition notify_dirty_except (fuel : nat) (src : node_id) (r : option node_id) (s : state) : option state :=
  notify_with (mark_except fuel r) src s.

(** Any of the five marking operations, from [s] to [s']. *)
Definition any_mark (fuel : nat) (n : node_id) (r : option node_id) (s s' : state) : Prop :=
  mark_check fuel n s = Some s' \/ mark_dirty fuel n s = Some s' \/
  notify_check fuel n s = Some s' \/ notify_dirty fuel n s = Some s' \/
  notify_dirty_except fuel n r s = Some s'.

(** A node's contribution to [mark_measure]: its clean transition and its pending announcement. Only a
    memo forwards a mark. *)
Definition mark_score (d : node) : nat :=
  match n_kind d with
  | KMemo => (if decide (n_freshness d = Clean) then 1 else 0) + (if n_unannounced d then 1 else 0)
  | _ => 0
  end.

Definition nodes_score (m : gmap node_id node) : nat :=
  sum_list (mark_score ∘ snd <$> map_to_list m).

(** Bound on the walks started by later marks: one per memo in [Clean], one per memo holding an
    announcement. *)
Definition mark_measure (s : state) : nat := nodes_score (g_nodes s).

(** ** The primitive steps *)

(**md
One change to a node's freshness mark: a memo takes a freshness at or above its own and keeps or clears its
unannounced flag, a queued effect takes a freshness at or above its own, or an unqueued effect does so and
is enqueued.
*)
Inductive mark_prim : state -> state -> Prop :=
  | prim_memo s n d f u :
      g_nodes s !! n = Some d ->
      n_kind d = KMemo ->
      freshness_le (n_freshness d) f ->
      (u = n_unannounced d \/ u = false) ->
      mark_prim s (set_node n (node_marked f u (n_queued d) d) s)
  | prim_effect s n d f :
      g_nodes s !! n = Some d ->
      n_kind d = KEffect ->
      n_queued d = true ->
      freshness_le (n_freshness d) f ->
      mark_prim s (set_node n (node_marked f (n_unannounced d) true d) s)
  | prim_enqueue s n d f :
      g_nodes s !! n = Some d ->
      n_kind d = KEffect ->
      n_queued d = false ->
      freshness_le (n_freshness d) f ->
      mark_prim s (enqueue n (set_node n (node_marked f (n_unannounced d) true d) s)).

Local Notation marks := (rtc mark_prim).

Lemma marks_trans s1 s2 s3 : marks s1 s2 -> marks s2 s3 -> marks s1 s3.
Proof. intros H1 H2. by trans s2. Qed.

Lemma freshness_le_refl f : freshness_le f f.
Proof. unfold freshness_le. lia. Qed.

Lemma freshness_le_trans f g h : freshness_le f g -> freshness_le g h -> freshness_le f h.
Proof. unfold freshness_le. lia. Qed.

Lemma freshness_le_clean f : freshness_le Clean f.
Proof. unfold freshness_le. simpl. lia. Qed.

Lemma freshness_le_dirty f : freshness_le f Dirty.
Proof. destruct f; unfold freshness_le; simpl; lia. Qed.

Lemma walk_marks_rtc (mark : node_id -> state -> option state) src n s s' :
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  walk_marks mark src n s = Some s' ->
  marks s s'.
Proof.
  intros Hm. revert s. induction n as [|i IH]; intros s Hw; simpl in Hw.
  - simplify_eq. apply rtc_refl.
  - destruct (decide _) as [_|_]; [by apply IH|].
    destruct (observer_items (observers_of (g_edges s) src) !! i) as [[c ?]|]; [|done].
    destruct (mark c s) as [s1|] eqn:Ec; simpl in Hw; [|done].
    eapply marks_trans; [by eapply Hm|by apply IH].
Qed.

Lemma notify_with_rtc mark src s s' :
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  notify_with mark src s = Some s' ->
  marks s s'.
Proof. intros Hm. by apply walk_marks_rtc. Qed.

Lemma effect_marked_rtc f n d s :
  g_nodes s !! n = Some d ->
  n_kind d = KEffect ->
  freshness_le (n_freshness d) f ->
  marks s (effect_marked f n d s).
Proof.
  intros Hd Hk Hf. unfold effect_marked.
  destruct (_ || _); [apply rtc_refl|].
  destruct (n_queued d) eqn:Hq; apply rtc_once; [eapply prim_effect|eapply prim_enqueue]; done.
Qed.

Lemma mark_check_rtc f n s s' : mark_check f n s = Some s' -> marks s s'.
Proof.
  revert n s s'. induction f as [|f IH]; intros n s s' Hm; simpl in Hm; [done|].
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|simplify_eq; apply rtc_refl].
  destruct (n_kind d) eqn:Hk.
  - simplify_eq. apply rtc_refl.
  - destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
    + eapply rtc_l.
      * eapply prim_memo; [done|done| |by left]. rewrite Hc. apply freshness_le_clean.
      * eapply notify_with_rtc; [|done]. intros ???. apply IH.
    + destruct (announce_due s d).
      * eapply rtc_l.
        -- eapply prim_memo; [done|done|apply freshness_le_refl|by right].
        -- eapply notify_with_rtc; [|done]. intros ???. apply IH.
      * simplify_eq. apply rtc_refl.
  - simplify_eq. apply effect_marked_rtc; [done|done|].
    destruct (decide (n_freshness d = Clean)) as [->|_]; [apply freshness_le_clean|apply freshness_le_refl].
Qed.

Lemma mark_dirty_rtc f n s s' : mark_dirty f n s = Some s' -> marks s s'.
Proof.
  destruct f as [|f]; intros Hm; simpl in Hm; [done|].
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|simplify_eq; apply rtc_refl].
  destruct (n_kind d) eqn:Hk.
  - simplify_eq. apply rtc_refl.
  - destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
    + eapply rtc_l.
      * eapply prim_memo; [done|done|apply freshness_le_dirty|by left].
      * eapply notify_with_rtc; [|done]. intros ???. apply mark_check_rtc.
    + destruct (announce_due s d).
      * eapply rtc_l.
        -- eapply prim_memo; [done|done|apply freshness_le_dirty|by right].
        -- eapply notify_with_rtc; [|done]. intros ???. apply mark_check_rtc.
      * destruct (decide (n_freshness d = Dirty)); simplify_eq; [apply rtc_refl|].
        apply rtc_once. eapply prim_memo; [done|done|apply freshness_le_dirty|by left].
  - simplify_eq. apply effect_marked_rtc; [done|done|apply freshness_le_dirty].
Qed.

Lemma mark_except_rtc f r c s s' : mark_except f r c s = Some s' -> marks s s'.
Proof.
  unfold mark_except. destruct (decide _); [intros; simplify_eq; apply rtc_refl|apply mark_dirty_rtc].
Qed.

Lemma any_mark_rtc f n r s s' : any_mark f n r s s' -> marks s s'.
Proof.
  intros [H|[H|[H|[H|H]]]].
  - by eapply mark_check_rtc.
  - by eapply mark_dirty_rtc.
  - eapply notify_with_rtc; [|done]. intros ???. apply mark_check_rtc.
  - eapply notify_with_rtc; [|done]. intros ???. apply mark_dirty_rtc.
  - eapply notify_with_rtc; [|done]. intros ???. apply mark_except_rtc.
Qed.

(** ** What a mark holds fixed *)

Lemma prim_edges s s' : mark_prim s s' -> g_edges s' = g_edges s.
Proof. by intros []. Qed.

Lemma marks_edges s s' : marks s s' -> g_edges s' = g_edges s.
Proof.
  induction 1 as [|s1 s2 s3 Hp _ IH]; [done|]. rewrite IH. by apply prim_edges.
Qed.

(** [d'] is a later copy of [d] under marks. *)
Definition node_rises (d d' : node) : Prop :=
  n_kind d' = n_kind d /\ n_disposed d' = n_disposed d /\ n_in_cleanup d' = n_in_cleanup d /\
  freshness_le (n_freshness d) (n_freshness d') /\ (n_unannounced d = false -> n_unannounced d' = false).

Lemma node_rises_refl d : node_rises d d.
Proof. split_and!; try done. apply freshness_le_refl. Qed.

Lemma node_rises_trans d1 d2 d3 : node_rises d1 d2 -> node_rises d2 d3 -> node_rises d1 d3.
Proof.
  intros (H1 & H2 & H3 & H4 & H5) (H1' & H2' & H3' & H4' & H5').
  split_and!; try congruence; [|auto]. by eapply freshness_le_trans.
Qed.

Lemma prim_rises s s' m d :
  mark_prim s s' -> g_nodes s !! m = Some d ->
  exists d', g_nodes s' !! m = Some d' /\ node_rises d d'.
Proof.
  intros Hp Hm. destruct Hp as [s n d0 f u Hd Hk Hf Hu|s n d0 f Hd Hk Hq Hf|s n d0 f Hd Hk Hq Hf]; simpl.
  all: destruct (decide (m = n)) as [->|Hne].
  all: try (rewrite lookup_insert_ne by done; exists d; split; [done|apply node_rises_refl]).
  all: rewrite lookup_insert_eq; eexists; split; [reflexivity|]; simplify_eq.
  all: split_and!; simpl; try done.
  destruct Hu as [-> | ->]; auto.
Qed.

Lemma marks_rises s s' m d :
  marks s s' -> g_nodes s !! m = Some d ->
  exists d', g_nodes s' !! m = Some d' /\ node_rises d d'.
Proof.
  intros Hr. revert d. induction Hr as [s|s1 s2 s3 Hp _ IH]; intros d Hm.
  - exists d. split; [done|apply node_rises_refl].
  - destruct (prim_rises _ _ _ _ Hp Hm) as (d2 & Hd2 & Hr2).
    destruct (IH _ Hd2) as (d3 & Hd3 & Hr3). exists d3. split; [done|by eapply node_rises_trans].
Qed.

(** ** The measure *)

Lemma sum_list_perm (l1 l2 : list nat) : l1 ≡ₚ l2 -> sum_list l1 = sum_list l2.
Proof. induction 1; simpl; lia. Qed.

Lemma nodes_score_delete m n d :
  m !! n = Some d -> nodes_score m = mark_score d + nodes_score (delete n m).
Proof.
  intros Hd. unfold nodes_score.
  rewrite <- (sum_list_perm _ _ (fmap_Permutation _ _ _ (map_to_list_delete m n d Hd))).
  reflexivity.
Qed.

Lemma nodes_score_insert m n d d' :
  m !! n = Some d ->
  nodes_score (<[n := d']> m) + mark_score d = nodes_score m + mark_score d'.
Proof.
  intros Hd. rewrite (nodes_score_delete m n d Hd).
  rewrite (nodes_score_delete (<[n := d']> m) n d') by apply lookup_insert_eq.
  rewrite delete_insert_eq. lia.
Qed.

Lemma score_marked_le d f u q :
  freshness_le (n_freshness d) f -> (u = n_unannounced d \/ u = false) ->
  mark_score (node_marked f u q d) <= mark_score d.
Proof.
  intros Hf Hu. unfold mark_score. simpl. destruct (n_kind d); [lia| |lia].
  destruct (decide (n_freshness d = Clean)) as [Hc|Hc]; destruct (decide (f = Clean)) as [->|Hf'].
  - destruct Hu as [-> | ->]; [lia|destruct (n_unannounced d); lia].
  - destruct Hu as [-> | ->]; [lia|destruct (n_unannounced d); lia].
  - destruct (n_freshness d); [done|unfold freshness_le in Hf; simpl in Hf; lia..].
  - destruct Hu as [-> | ->]; [lia|destruct (n_unannounced d); lia].
Qed.


Lemma score_effect d : n_kind d = KEffect -> mark_score d = 0.
Proof. intros Hk. unfold mark_score. by rewrite Hk. Qed.

Lemma prim_measure s s' : mark_prim s s' -> mark_measure s' <= mark_measure s.
Proof.
  intros Hp. destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hq Hf|s n d f Hd Hk Hq Hf];
    unfold mark_measure; simpl.
  - pose proof (nodes_score_insert (g_nodes s) n d (node_marked f u (n_queued d) d) Hd).
    pose proof (score_marked_le d f u (n_queued d) Hf Hu). lia.
  - pose proof (nodes_score_insert (g_nodes s) n d (node_marked f (n_unannounced d) true d) Hd).
    rewrite (score_effect (node_marked f (n_unannounced d) true d)), (score_effect d) in * by done. lia.
  - pose proof (nodes_score_insert (g_nodes s) n d (node_marked f (n_unannounced d) true d) Hd).
    rewrite (score_effect (node_marked f (n_unannounced d) true d)), (score_effect d) in * by done. lia.
Qed.

Lemma marks_measure s s' : marks s s' -> mark_measure s' <= mark_measure s.
Proof.
  induction 1 as [|s1 s2 s3 Hp _ IH]; [done|]. pose proof (prim_measure _ _ Hp). lia.
Qed.

(** A walk's start uses up a clean transition or an announcement. *)
Lemma measure_memo_clean s n d f q :
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d = Clean -> f <> Clean ->
  S (mark_measure (set_node n (node_marked f (n_unannounced d) q d) s)) <= mark_measure s.
Proof.
  intros Hd Hk Hc Hf. unfold mark_measure. simpl.
  pose proof (nodes_score_insert (g_nodes s) n d (node_marked f (n_unannounced d) q d) Hd) as Hi.
  unfold mark_score in Hi. simpl in Hi. rewrite Hk, Hc in Hi.
  rewrite decide_True in Hi by done. rewrite decide_False in Hi by done. lia.
Qed.

Lemma measure_memo_announce s n d f q :
  g_nodes s !! n = Some d -> n_kind d = KMemo -> n_freshness d <> Clean -> f <> Clean -> n_unannounced d = true ->
  S (mark_measure (set_node n (node_marked f false q d) s)) <= mark_measure s.
Proof.
  intros Hd Hk Hc Hf Hu. unfold mark_measure. simpl.
  pose proof (nodes_score_insert (g_nodes s) n d (node_marked f false q d) Hd) as Hi.
  unfold mark_score in Hi. simpl in Hi. rewrite Hk, Hu in Hi.
  rewrite !decide_False in Hi by done. lia.
Qed.

Lemma announce_due_unannounced s d : announce_due s d = true -> n_unannounced d = true.
Proof. unfold announce_due. destruct (f_unannounced _), (n_unannounced d); done. Qed.

(** ** Marking finishes *)

Lemma walk_marks_some (mark : node_id -> state -> option state) src n s bound :
  (forall c s0, mark_measure s0 < bound -> is_Some (mark c s0)) ->
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  mark_measure s < bound ->
  is_Some (walk_marks mark src n s).
Proof.
  intros Hs Hr. revert s. induction n as [|i IH]; intros s Hb; simpl; [by eexists|].
  destruct (decide _) as [_|Hlt]; [by apply IH|].
  destruct (lookup_lt_is_Some_2 (observer_items (observers_of (g_edges s) src)) i) as [[c slot] Hc]; [lia|].
  rewrite Hc. destruct (Hs c s Hb) as [s1 Hs1]. rewrite Hs1. simpl.
  apply IH. pose proof (marks_measure _ _ (Hr _ _ _ Hs1)). lia.
Qed.

Lemma mark_check_some f n s : mark_measure s < f -> is_Some (mark_check f n s).
Proof.
  revert n s. induction f as [|f IH]; intros n s Hb; [lia|]. simpl.
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|by eexists].
  destruct (n_kind d) eqn:Hk; [by eexists| |by eexists].
  destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
  - apply (walk_marks_some _ _ _ _ f); [intros; by apply IH|intros ???; apply mark_check_rtc|].
    pose proof (measure_memo_clean s n d Check (n_queued d) Hd Hk Hc ltac:(done)). lia.
  - destruct (announce_due s d) eqn:Hu; [|by eexists].
    apply (walk_marks_some _ _ _ _ f); [intros; by apply IH|intros ???; apply mark_check_rtc|].
    pose proof (measure_memo_announce s n d (n_freshness d) (n_queued d) Hd Hk Hc Hc
                  (announce_due_unannounced _ _ Hu)). lia.
Qed.

Lemma mark_dirty_some f n s : mark_measure s < f -> is_Some (mark_dirty f n s).
Proof.
  destruct f as [|f]; intros Hb; [lia|]. simpl.
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|by eexists].
  destruct (n_kind d) eqn:Hk; [by eexists| |by eexists].
  destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
  - apply (walk_marks_some _ _ _ _ f);
      [intros; by apply mark_check_some|intros ???; apply mark_check_rtc|].
    pose proof (measure_memo_clean s n d Dirty (n_queued d) Hd Hk Hc ltac:(done)). lia.
  - destruct (announce_due s d) eqn:Hu.
    + apply (walk_marks_some _ _ _ _ f);
        [intros; by apply mark_check_some|intros ???; apply mark_check_rtc|].
      pose proof (measure_memo_announce s n d Dirty (n_queued d) Hd Hk Hc ltac:(done)
                    (announce_due_unannounced _ _ Hu)). lia.
    + destruct (decide (n_freshness d = Dirty)); by eexists.
Qed.

(**md
**Marking a node finishes on any graph, cycles included.**

With fuel above `mark_measure`, `mark_check` and `mark_dirty` return a state.

- [fuel:] the fuel, above `mark_measure s`.
- [n:] the node marked.
- [s:] the starting state.
*)
Theorem mark_terminates fuel n s :
  mark_measure s < fuel ->
  is_Some (mark_check fuel n s) /\ is_Some (mark_dirty fuel n s).
Proof. intros Hb. split; [by apply mark_check_some|by apply mark_dirty_some]. Qed.

(**md
**Marking a node's observers finishes on any graph, cycles included.**

With fuel above `mark_measure`, `notify_check`, `notify_dirty` and `notify_dirty_except` return a state.

- [fuel:] the fuel, above `mark_measure s`.
- [n:] the source whose observers are marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the starting state.
*)
Theorem notify_terminates fuel n r s :
  mark_measure s < fuel ->
  is_Some (notify_check fuel n s) /\ is_Some (notify_dirty fuel n s) /\
  is_Some (notify_dirty_except fuel n r s).
Proof.
  intros Hb. split_and!.
  - apply (walk_marks_some _ _ _ _ fuel);
      [intros; by apply mark_check_some|intros ???; apply mark_check_rtc|done].
  - apply (walk_marks_some _ _ _ _ fuel);
      [intros; by apply mark_dirty_some|intros ???; apply mark_dirty_rtc|done].
  - apply (walk_marks_some _ _ _ _ fuel); [|intros ???; apply mark_except_rtc|done].
    intros c s0 Hb0. unfold mark_except. destruct (decide _); [by eexists|by apply mark_dirty_some].
Qed.

(** ** Marks raise freshness and leave edges alone *)

(**md
**Marks only raise freshness.**

After any of the five marking operations, each existing node still exists, and its freshness has stayed or risen in the order `Clean`, `Check`, `Dirty`.

- [fuel:] the fuel.
- [n:] the node or source marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] a node of `s`.
- [d:] the node `m` in `s`.
*)
Theorem mark_monotone fuel n r s s' m d :
  any_mark fuel n r s s' ->
  g_nodes s !! m = Some d ->
  exists d', g_nodes s' !! m = Some d' /\ freshness_le (n_freshness d) (n_freshness d').
Proof.
  intros Ha Hm. destruct (marks_rises _ _ _ _ (any_mark_rtc _ _ _ _ _ Ha) Hm) as (d' & Hd' & _ & _ & _ & Hf & _).
  eauto.
Qed.

(**md
**A `Check` mark leaves a `Dirty` node `Dirty`.**

Covers `mark_check` only. `mark_monotone` gives the same for every marking operation.

- [fuel:] the fuel.
- [n:] the node marked.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] a `Dirty` node of `s`.
- [d:] the node `m` in `s`.
*)
Theorem dirty_not_lost fuel n s s' m d :
  mark_check fuel n s = Some s' ->
  g_nodes s !! m = Some d ->
  n_freshness d = Dirty ->
  exists d', g_nodes s' !! m = Some d' /\ n_freshness d' = Dirty.
Proof.
  intros Hc Hm Hf. destruct (marks_rises _ _ _ _ (mark_check_rtc _ _ _ _ Hc) Hm)
    as (d' & Hd' & _ & _ & _ & Hle & _).
  exists d'. split; [done|]. rewrite Hf in Hle. destruct (n_freshness d'); [| |done];
    unfold freshness_le in Hle; simpl in Hle; lia.
Qed.

(**md
**Marks leave the edges alone.**

Each of the five marking operations leaves both ends of every edge unchanged.

- [fuel:] the fuel.
- [n:] the node or source marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state before the marking.
- [s':] the state after the marking.
*)
Theorem mark_preserves_edges fuel n r s s' :
  any_mark fuel n r s s' -> g_edges s' = g_edges s.
Proof. intros Ha. by eapply marks_edges, any_mark_rtc. Qed.

(** ** A marking walk visits a fixed list *)

(**md
Marks the observers `vis` in order with `mark`, stopping at the first `None`.
*)
Fixpoint mark_list (mark : node_id -> state -> option state) (vis : list node_id) (s : state) : option state :=
  match vis with
  | [] => Some s
  | c :: r => mark c s ≫= mark_list mark r
  end.

Lemma length_view o : length (observer_view o) = length (observer_items o).
Proof. unfold observer_view. by rewrite length_fmap. Qed.

Lemma walk_clamp i o muts vis o' :
  length (observer_items o) <= i -> walk i o muts vis o' -> walk (S i) o muts vis o'.
Proof.
  intros Hle Hw. inversion Hw as [|n' j o2 c slot ms o1 rest vis2 o3 Hmin Hj Ha Hr]; subst.
  - apply walk_end. lia.
  - eapply walk_visit with (i := j); [lia|done|done|done].
Qed.

Lemma walk_static n o :
  walk n o ((fun _ => []) <$> rev (take n (observer_view o))) (rev (take n (observer_view o))) o.
Proof.
  induction n as [|i IH].
  - rewrite take_0. simpl. apply walk_end. lia.
  - destruct (decide (length (observer_items o) <= i)) as [Hle|Hlt].
    + rewrite !take_ge in * by (rewrite length_view; lia). by apply walk_clamp.
    + destruct (lookup_lt_is_Some_2 (observer_items o) i) as [[c slot] Hc]; [lia|].
      assert (Hv : observer_view o !! i = Some c) by (apply view_lookup; eauto).
      rewrite (take_S_r _ _ _ Hv), rev_app_distr. simpl.
      eapply walk_visit with (i := i); [lia|done|reflexivity|done].
Qed.

Lemma walk_marks_list (mark : node_id -> state -> option state) src n s :
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  walk_marks mark src n s = mark_list mark (rev (take n (observer_view (observers_of (g_edges s) src)))) s.
Proof.
  intros Hm. revert s. induction n as [|i IH]; intros s; simpl.
  - done.
  - destruct (decide _) as [Hle|Hlt].
    + rewrite IH. rewrite !take_ge by (rewrite length_view; lia). done.
    + destruct (lookup_lt_is_Some_2 (observer_items (observers_of (g_edges s) src)) i) as [[c slot] Hc]; [lia|].
      assert (Hv : observer_view (observers_of (g_edges s) src) !! i = Some c) by (apply view_lookup; eauto).
      rewrite Hc, (take_S_r _ _ _ Hv), rev_app_distr. simpl.
      destruct (mark c s) as [s1|] eqn:E; simpl; [|done].
      rewrite IH. rewrite (marks_edges _ _ (Hm _ _ _ E)). done.
Qed.

Lemma notify_with_list (mark : node_id -> state -> option state) src s :
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  notify_with mark src s = mark_list mark (rev (observer_view (observers_of (g_edges s) src))) s.
Proof.
  intros Hm. unfold notify_with. rewrite walk_marks_list by done.
  rewrite take_ge by (rewrite length_view; lia). done.
Qed.

(**md
**A marking walk visits the observers present at its start, last position first.**

Each notify operation folds its mark over that one list, and the walk is a `walk` with empty mutations at every visit. A `walk` with a mutation behaves differently: see [`walk_visits_added_observer`](Ranvier.Pure.Witnesses.html#walk_visits_added_observer).

- [fuel:] the fuel.
- [src:] the source whose observers are marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state.
*)
Theorem notify_walk_unmutated fuel src r s (o := observers_of (g_edges s) src) (vis := rev (observer_view o)) :
  walk (length (observer_items o)) o ((fun _ => []) <$> vis) vis o /\
  notify_check fuel src s = mark_list (mark_check fuel) vis s /\
  notify_dirty fuel src s = mark_list (mark_dirty fuel) vis s /\
  notify_dirty_except fuel src r s = mark_list (mark_except fuel r) vis s.
Proof.
  split_and!.
  - pose proof (walk_static (length (observer_items o)) o) as Hw.
    rewrite take_ge in Hw by (rewrite length_view; lia). exact Hw.
  - apply notify_with_list. intros ???. apply mark_check_rtc.
  - apply notify_with_list. intros ???. apply mark_dirty_rtc.
  - apply notify_with_list. intros ???. apply mark_except_rtc.
Qed.

Lemma mark_list_visit (mark : node_id -> state -> option state) vis s s' c :
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  mark_list mark vis s = Some s' ->
  c ∈ vis ->
  exists s1 s2, marks s s1 /\ mark c s1 = Some s2 /\ marks s2 s'.
Proof.
  intros Hm. revert s. induction vis as [|c0 r IH]; intros s Hl Hc; [set_solver|].
  simpl in Hl. destruct (mark c0 s) as [s1|] eqn:E; simpl in Hl; [|done].
  assert (Hmarks : marks s1 s').
  { clear -Hm Hl. revert s1 Hl. induction r as [|c1 r' IHr]; intros s1 Hl; simpl in Hl.
    - simplify_eq. apply rtc_refl.
    - destruct (mark c1 s1) as [s2|] eqn:E; simpl in Hl; [|done].
      eapply marks_trans; [by eapply Hm|by apply IHr]. }
  apply elem_of_cons in Hc as [->|Hc].
  - exists s, s1. split_and!; [apply rtc_refl|done|done].
  - destruct (IH _ Hl Hc) as (s2 & s3 & H1 & H2 & H3). exists s2, s3. split_and!; [|done|done].
    eapply marks_trans; [by eapply Hm|done].
Qed.

Lemma elem_rev (c : node_id) l : c ∈ l -> c ∈ rev l.
Proof. rewrite !list_elem_of_In. apply in_rev. Qed.

(** ** Marks reach observers *)

(** The computations affected by a mark: a memo, or an effect outside its disposal and its cleanups.
    [in_cleanup_observer_stays_clean] and [disposed_running_observer_stays_clean] show observers
    outside this set. *)
Definition takes_marks (s : state) (m : node_id) : Prop :=
  exists d, g_nodes s !! m = Some d /\
    (n_kind d = KMemo \/ (n_kind d = KEffect /\ n_disposed d = false /\ n_in_cleanup d = false)).

(**md
Node `m` exists in `s` and is `Check` or `Dirty`.
*)
Definition ends_stale (s : state) (m : node_id) : Prop :=
  exists d, g_nodes s !! m = Some d /\ stale (n_freshness d) = true.

Lemma stale_rises f g : freshness_le f g -> stale f = true -> stale g = true.
Proof. destruct f, g; unfold freshness_le; simpl; try done; lia. Qed.

Lemma marks_stale s s' m : marks s s' -> ends_stale s m -> ends_stale s' m.
Proof.
  intros Hr (d & Hd & Hst). destruct (marks_rises _ _ _ _ Hr Hd) as (d' & Hd' & _ & _ & _ & Hf & _).
  exists d'. split; [done|]. by eapply stale_rises.
Qed.

Lemma marks_takes s s' m : marks s s' -> takes_marks s m -> takes_marks s' m.
Proof.
  intros Hr (d & Hd & Hk). destruct (marks_rises _ _ _ _ Hr Hd) as (d' & Hd' & H1 & H2 & H3 & _ & _).
  exists d'. split; [done|]. rewrite H1, H2, H3. done.
Qed.

Lemma set_node_stale s n d : stale (n_freshness d) = true -> ends_stale (set_node n d s) n.
Proof. intros Hst. exists d. split; [apply lookup_insert_eq|done]. Qed.

Lemma effect_marked_stale f n d s :
  stale f = true -> n_disposed d = false -> n_in_cleanup d = false ->
  ends_stale (effect_marked f n d s) n.
Proof.
  intros Hf Hd Hc. unfold effect_marked. rewrite Hd, Hc. simpl.
  destruct (n_queued d); by apply set_node_stale.
Qed.

Lemma stale_of_not_clean f : f <> Clean -> stale f = true.
Proof. by destruct f. Qed.

Lemma notify_with_rtc_check f n s s' :
  notify_with (mark_check f) n s = Some s' -> marks s s'.
Proof. apply notify_with_rtc. intros ???. apply mark_check_rtc. Qed.

Lemma mark_check_stale f m s s' :
  takes_marks s m -> mark_check f m s = Some s' -> ends_stale s' m.
Proof.
  intros Ht Hc. destruct Ht as (d & Hd & Hk).
  destruct (decide (n_freshness d = Clean)) as [Hf|Hf]; last first.
  { eapply marks_stale; [by eapply mark_check_rtc|]. exists d. split; [done|].
    by apply stale_of_not_clean. }
  destruct f as [|f]; simpl in Hc; [done|]. rewrite Hd in Hc.
  destruct Hk as [Hk|(Hk & Hdi & Hcl)]; rewrite Hk in Hc.
  - rewrite decide_True in Hc by done. eapply marks_stale; [by eapply notify_with_rtc_check|].
    by apply set_node_stale.
  - rewrite decide_True in Hc by done. simplify_eq. by apply effect_marked_stale.
Qed.

Lemma mark_dirty_stale f m s s' :
  takes_marks s m -> mark_dirty f m s = Some s' -> ends_stale s' m.
Proof.
  intros Ht Hc. destruct Ht as (d & Hd & Hk).
  destruct (decide (n_freshness d = Clean)) as [Hf|Hf]; last first.
  { eapply marks_stale; [by eapply mark_dirty_rtc|]. exists d. split; [done|].
    by apply stale_of_not_clean. }
  destruct f as [|f]; simpl in Hc; [done|]. rewrite Hd in Hc.
  destruct Hk as [Hk|(Hk & Hdi & Hcl)]; rewrite Hk in Hc.
  - rewrite decide_True in Hc by done. eapply marks_stale; [by eapply notify_with_rtc_check|].
    by apply set_node_stale.
  - simplify_eq. by apply effect_marked_stale.
Qed.

Lemma notify_with_reaches (P : state -> Prop) (mark : node_id -> state -> option state) src s s' m :
  (forall s0 s1, marks s0 s1 -> P s0 -> P s1) ->
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  (forall s0 s1, takes_marks s0 m -> mark m s0 = Some s1 -> P s1) ->
  notify_with mark src s = Some s' ->
  m ∈ observer_view (observers_of (g_edges s) src) ->
  takes_marks s m ->
  P s'.
Proof.
  intros Hp Hr Hst Hn Hin Ht. rewrite notify_with_list in Hn by done.
  destruct (mark_list_visit _ _ _ _ m Hr Hn (elem_rev _ _ Hin))
    as (s1 & s2 & H1 & H2 & H3).
  eapply Hp; [done|]. eapply Hst; [|done]. by eapply marks_takes.
Qed.

Lemma notify_with_stale (mark : node_id -> state -> option state) src s s' m :
  (forall c s0 s1, mark c s0 = Some s1 -> marks s0 s1) ->
  (forall s0 s1, takes_marks s0 m -> mark m s0 = Some s1 -> ends_stale s1 m) ->
  notify_with mark src s = Some s' ->
  m ∈ observer_view (observers_of (g_edges s) src) ->
  takes_marks s m ->
  ends_stale s' m.
Proof.
  apply (notify_with_reaches (fun s0 => ends_stale s0 m)). intros ???. by eapply marks_stale.
Qed.

(**md
**A marking walk leaves each observer stale.**

Each observer of the source that takes marks ends `Check` or `Dirty`. `notify_dirty_except` skips the running computation, so the guarantee covers observers other than that one. An observer takes marks when it is a memo, or an effect outside its disposal and its cleanups.

- [fuel:] the fuel.
- [src:] the source whose observers are marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] an observer of `src` that takes marks.
*)
Theorem notify_stales fuel src r s s' m :
  m ∈ observer_view (observers_of (g_edges s) src) ->
  takes_marks s m ->
  (notify_check fuel src s = Some s' \/ notify_dirty fuel src s = Some s' \/
   (notify_dirty_except fuel src r s = Some s' /\ r <> Some m)) ->
  ends_stale s' m.
Proof.
  intros Hin Ht [Hn|[Hn|[Hn Hne]]].
  - eapply notify_with_stale; [|intros ??; apply mark_check_stale|done|done|done].
    intros ???. apply mark_check_rtc.
  - eapply notify_with_stale; [|intros ??; apply mark_dirty_stale|done|done|done].
    intros ???. apply mark_dirty_rtc.
  - eapply notify_with_stale; [intros ???; apply mark_except_rtc| |done|done|done].
    intros s0 s1 Ht0. unfold mark_except. rewrite decide_False by done. by apply mark_dirty_stale.
Qed.

Lemma set_node_edges n d s : g_edges (set_node n d s) = g_edges s.
Proof. done. Qed.

Lemma set_node_takes s n f u d m :
  g_nodes s !! n = Some d -> n_kind d = KMemo ->
  (u = n_unannounced d \/ u = false) -> freshness_le (n_freshness d) f ->
  takes_marks s m -> takes_marks (set_node n (node_marked f u (n_queued d) d) s) m.
Proof.
  intros Hd Hk Hu Hf. apply marks_takes. apply rtc_once. by eapply prim_memo.
Qed.

(** The walk a memo's transition starts, from the state after its write. *)
Lemma forward_members f n s1 s' m :
  notify_with (mark_check f) n s1 = Some s' ->
  m ∈ observer_view (observers_of (g_edges s1) n) ->
  takes_marks s1 m ->
  ends_stale s' m.
Proof. intros Hn Hin Ht. eapply (notify_stales f n None s1 s' m); [done|done|by left]. Qed.

(**md
**A clean memo goes stale and passes the mark to its observers.**

After `mark_check` or `mark_dirty` of a `Clean` memo, the memo is stale, and so is each of its observers that takes marks. An effect in its cleanups stays clean: see [`in_cleanup_observer_stays_clean`](Ranvier.Pure.Witnesses.html#in_cleanup_observer_stays_clean).

- [fuel:] the fuel.
- [n:] the `Clean` memo marked.
- [d:] the node `n` in `s`.
- [os:] the observers of `n` in `s`.
- [s:] the state before the marking.
- [s':] the state after the marking.
*)
Theorem mark_forwards_on_clean fuel n d os s s' :
  g_nodes s !! n = Some d ->
  n_kind d = KMemo ->
  n_freshness d = Clean ->
  observer_view (observers_of (g_edges s) n) = os ->
  (mark_check fuel n s = Some s' \/ mark_dirty fuel n s = Some s') ->
  ends_stale s' n /\ forall m, m ∈ os -> takes_marks s m -> ends_stale s' m.
Proof.
  intros Hd Hk Hc <- Hm.
  destruct fuel as [|f]; [by destruct Hm|].
  destruct Hm as [Hm|Hm]; simpl in Hm; rewrite Hd, Hk in Hm; rewrite decide_True in Hm by done.
  all: split; [eapply marks_stale; [by eapply notify_with_rtc_check|by apply set_node_stale]|].
  all: intros m Hin Ht; eapply forward_members; [done|by rewrite set_node_edges|].
  all: eapply set_node_takes; [done|done|by left| |done]; rewrite Hc; apply freshness_le_clean.
Qed.

(**md
**A stale memo holding an announcement passes the mark on and spends the announcement.**

After `mark_check` or `mark_dirty`, the memo is still stale with `n_unannounced` cleared, and each of its observers that takes marks is stale.

Premises: the `f_unannounced` flag is on, and the memo is stale with `n_unannounced` set.

- [fuel:] the fuel.
- [n:] the stale memo marked.
- [d:] the node `n` in `s`, which holds an announcement.
- [os:] the observers of `n` in `s`.
- [s:] the state before the marking.
- [s':] the state after the marking.
*)
Theorem mark_forwards_on_unannounced fuel n d os s s' :
  f_unannounced (g_flags s) = true ->
  g_nodes s !! n = Some d ->
  n_kind d = KMemo ->
  stale (n_freshness d) = true ->
  n_unannounced d = true ->
  observer_view (observers_of (g_edges s) n) = os ->
  (mark_check fuel n s = Some s' \/ mark_dirty fuel n s = Some s') ->
  (exists d', g_nodes s' !! n = Some d' /\ stale (n_freshness d') = true /\ n_unannounced d' = false) /\
  forall m, m ∈ os -> takes_marks s m -> ends_stale s' m.
Proof.
  intros Hflag Hd Hk Hst Hu <- Hm.
  assert (Hc : n_freshness d <> Clean) by (intros Hc; rewrite Hc in Hst; done).
  assert (Hdue : announce_due s d = true) by (unfold announce_due; by rewrite Hflag, Hu).
  destruct fuel as [|f]; [by destruct Hm|].
  destruct Hm as [Hm|Hm]; simpl in Hm; rewrite Hd, Hk in Hm; rewrite decide_False in Hm by done;
    rewrite Hdue in Hm.
  - split.
    + destruct (marks_rises _ _ n (node_marked (n_freshness d) false (n_queued d) d)
                  (notify_with_rtc_check _ _ _ _ Hm) ltac:(apply lookup_insert_eq))
        as (d' & Hd' & _ & _ & _ & Hf & Hfalse).
      exists d'. split_and!; [done| |by apply Hfalse]. by eapply stale_rises.
    + intros m Hin Ht. eapply forward_members; [done|by rewrite set_node_edges|].
      eapply set_node_takes; [done|done|by right|apply freshness_le_refl|done].
  - split.
    + destruct (marks_rises _ _ n (node_marked Dirty false (n_queued d) d)
                  (notify_with_rtc_check _ _ _ _ Hm) ltac:(apply lookup_insert_eq))
        as (d' & Hd' & _ & _ & _ & Hf & Hfalse).
      exists d'. split_and!; [done| |by apply Hfalse]. by eapply stale_rises.
    + intros m Hin Ht. eapply forward_members; [done|by rewrite set_node_edges|].
      eapply set_node_takes; [done|done|by right|apply freshness_le_dirty|done].
Qed.

(** ** Effects queue once *)

(** [queue[queueHead..queueCount-1]]. F# releases a slot as it is consumed. *)
Definition pending (s : state) : list node_id := drop (g_queue_head s) (g_queue s).

Definition entries_of (n : node_id) (l : list node_id) : nat := length (filter (fun x => x = n) l).

(** The queue's agreement with [queued] for [n]: at most one pending entry, and none while unset. The
    flag can be set with no entry, between a dequeue and [queued <- false] in [Execute]. *)
Definition queue_once (s : state) (n : node_id) : Prop :=
  forall d, g_nodes s !! n = Some d ->
    entries_of n (pending s) <= 1 /\ (n_queued d = false -> n ∉ pending s).

Lemma entries_absent n l : n ∉ l -> entries_of n l = 0.
Proof.
  induction l as [|x l IH]; intros Hn; [done|].
  assert (Hx : x <> n) by (intros ->; apply Hn; left).
  assert (Hl : n ∉ l) by (intros Hin; apply Hn; by right).
  unfold entries_of in *. rewrite filter_cons_False by done. by apply IH.
Qed.

Lemma pending_enqueue n s :
  pending (enqueue n s) = drop (g_queue_head s) (g_queue s ++ [n]).
Proof. done. Qed.

Lemma entries_after_enqueue n h q :
  n ∉ drop h q -> entries_of n (drop h (q ++ [n])) <= 1.
Proof.
  intros Hn. destruct (decide (h <= length q)) as [Hle|Hgt].
  - rewrite drop_app_le by done. unfold entries_of. rewrite filter_app, length_app.
    pose proof (entries_absent _ _ Hn) as H0. unfold entries_of in H0. rewrite H0.
    rewrite filter_cons_True by done. simpl. lia.
  - rewrite drop_ge by (rewrite length_app; simpl; lia). simpl. unfold entries_of. simpl. lia.
Qed.

Lemma elem_after_enqueue (n : node_id) h (q : list node_id) : h <= length q -> n ∈ drop h (q ++ [n]).
Proof.
  intros Hle. rewrite drop_app_le by done. set_solver.
Qed.

Lemma elem_of_enqueued (x n : node_id) h (q : list node_id) :
  x ∈ drop h (q ++ [n]) -> x ∈ drop h q \/ x = n.
Proof.
  destruct (decide (h <= length q)) as [Hle|Hgt].
  - rewrite drop_app_le by done. set_solver.
  - rewrite drop_ge by (rewrite length_app; simpl; lia). set_solver.
Qed.

Lemma entries_other (m n : node_id) h (q : list node_id) :
  m <> n -> entries_of m (drop h (q ++ [n])) = entries_of m (drop h q).
Proof.
  intros Hne. destruct (decide (h <= length q)) as [Hle|Hgt].
  - rewrite drop_app_le by done. unfold entries_of. rewrite filter_app, length_app.
    rewrite filter_cons_False by done. simpl. lia.
  - rewrite !drop_ge by (try rewrite length_app; simpl; lia). done.
Qed.

Lemma prim_queue_once s s' m : mark_prim s s' -> queue_once s m -> queue_once s' m.
Proof.
  intros Hp Hq d0 Hd0.
  destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hqd Hf|s n d f Hd Hk Hqd Hf]; simpl in Hd0.
  all: destruct (decide (m = n)) as [->|Hne]; [rewrite lookup_insert_eq in Hd0|rewrite lookup_insert_ne in Hd0 by done].
  - simplify_eq. apply (Hq d Hd).
  - apply (Hq d0 Hd0).
  - simplify_eq. split; [apply (Hq d Hd)|done].
  - apply (Hq d0 Hd0).
  - simplify_eq. destruct (Hq d Hd) as [_ Hun]. split; [|done].
    rewrite pending_enqueue. apply entries_after_enqueue. by apply Hun.
  - destruct (Hq d0 Hd0) as [Hle Hun]. rewrite pending_enqueue. split.
    + rewrite entries_other by done. done.
    + intros Hu Hin. apply elem_of_enqueued in Hin as [Hin | ->]; [by apply Hun|done].
Qed.

Lemma effect_marked_queues f n d s :
  g_nodes s !! n = Some d ->
  n_disposed d = false ->
  n_in_cleanup d = false ->
  queue_once s n ->
  let s' := effect_marked f n d s in
  (exists d', g_nodes s' !! n = Some d' /\ n_queued d' = true) /\ queue_once s' n /\
  (g_queue_head s <= length (g_queue s) -> n_queued d = false -> n ∈ pending s').
Proof.
  intros Hd Hdi Hcl Hq s'. subst s'. unfold effect_marked. rewrite Hdi, Hcl. simpl.
  destruct (Hq d Hd) as [Hle Hun].
  destruct (n_queued d) eqn:Hqd.
  - split_and!.
    + eexists. split; [apply lookup_insert_eq|done].
    + intros d0 Hd0. simpl in Hd0. rewrite lookup_insert_eq in Hd0. simplify_eq. split; [done|done].
    + intros _ ?. done.
  - split_and!.
    + eexists. split; [apply lookup_insert_eq|done].
    + intros d0 Hd0. simpl in Hd0. rewrite lookup_insert_eq in Hd0. simplify_eq. split; [|done].
      rewrite pending_enqueue. apply entries_after_enqueue. by apply Hun.
    + intros Hh _. rewrite pending_enqueue. by apply elem_after_enqueue.
Qed.

(**md
**Marks keep each effect's queue entries in step with its `queued` flag.**

Each marking operation preserves `queue_once`: at most one pending entry for the node, and none while its flag is unset. An effect marked along several paths has at most one pending entry.

Premise: `queue_once` holds beforehand.

- [fuel:] the fuel.
- [n:] the node or source marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] a node with at most one pending queue entry in `s`.
*)
Theorem mark_keeps_queue_once fuel n r s s' m :
  any_mark fuel n r s s' -> queue_once s m -> queue_once s' m.
Proof.
  intros Ha. apply any_mark_rtc in Ha. induction Ha as [|s1 s2 s3 Hp _ IH]; [done|].
  intros Hq. apply IH. by eapply prim_queue_once.
Qed.

(**md
**A marked effect ends queued.**

After `mark_check` or `mark_dirty`, the effect's `n_queued` is set and `queue_once` still holds. An effect that was not queued has a pending entry, provided the queue head lies within the queue.

Premises: the effect is outside its disposal and its cleanups, and `queue_once` holds beforehand.

- [fuel:] the fuel.
- [n:] the effect marked.
- [d:] the node `n` in `s`.
- [s:] the state before the marking.
- [s':] the state after the marking.
*)
Theorem effect_mark_queues fuel n d s s' :
  g_nodes s !! n = Some d ->
  n_kind d = KEffect ->
  n_disposed d = false ->
  n_in_cleanup d = false ->
  queue_once s n ->
  (mark_check fuel n s = Some s' \/ mark_dirty fuel n s = Some s') ->
  (exists d', g_nodes s' !! n = Some d' /\ n_queued d' = true) /\ queue_once s' n /\
  (g_queue_head s <= length (g_queue s) -> n_queued d = false -> n ∈ pending s').
Proof.
  intros Hd Hk Hdi Hcl Hq Hm.
  destruct fuel as [|f]; [by destruct Hm|].
  destruct Hm as [Hm|Hm]; simpl in Hm; rewrite Hd, Hk in Hm; simplify_eq; by apply effect_marked_queues.
Qed.

(**md
**A disposed effect ignores marks.**

`mark_check` and `mark_dirty` return the state unchanged, given any positive fuel.

- [fuel:] the fuel, which the marks run at `S fuel`.
- [n:] the disposed effect marked.
- [d:] the node `n` in `s`.
- [s:] the state, which the marks leave unchanged.
*)
Theorem disposed_effect_ignores_marks fuel n d s :
  g_nodes s !! n = Some d ->
  n_kind d = KEffect ->
  n_disposed d = true ->
  mark_check (S fuel) n s = Some s /\ mark_dirty (S fuel) n s = Some s.
Proof.
  intros Hd Hk Hdi. simpl. rewrite Hd, Hk. unfold effect_marked. rewrite Hdi. done.
Qed.

(**md
**An effect running its cleanups ignores marks.**

`mark_check` and `mark_dirty` return the state unchanged, given any positive fuel.

- [fuel:] the fuel, which the marks run at `S fuel`.
- [n:] the effect marked, which is in a cleanup.
- [d:] the node `n` in `s`.
- [s:] the state, which the marks leave unchanged.
*)
Theorem in_cleanup_ignores_marks fuel n d s :
  g_nodes s !! n = Some d ->
  n_kind d = KEffect ->
  n_in_cleanup d = true ->
  mark_check (S fuel) n s = Some s /\ mark_dirty (S fuel) n s = Some s.
Proof.
  intros Hd Hk Hcl. simpl. rewrite Hd, Hk. unfold effect_marked. rewrite Hcl, orb_true_r. done.
Qed.

(** ** How Dirty and Check marks differ *)

(**md
Node `m` exists in `s` and is `Dirty`.
*)
Definition ends_dirty (s : state) (m : node_id) : Prop :=
  exists d, g_nodes s !! m = Some d /\ n_freshness d = Dirty.

Lemma marks_dirty s s' m : marks s s' -> ends_dirty s m -> ends_dirty s' m.
Proof.
  intros Hr (d & Hd & Hf). destruct (marks_rises _ _ _ _ Hr Hd) as (d' & Hd' & _ & _ & _ & Hle & _).
  exists d'. split; [done|]. rewrite Hf in Hle. destruct (n_freshness d'); [| |done];
    unfold freshness_le in Hle; simpl in Hle; lia.
Qed.

Lemma set_node_dirty s n d : n_freshness d = Dirty -> ends_dirty (set_node n d s) n.
Proof. intros Hf. exists d. split; [apply lookup_insert_eq|done]. Qed.

(**md
**A `Dirty` mark makes its target `Dirty`.**

A memo, or an effect outside its disposal and its cleanups, ends `Dirty`. That includes an effect already queued on `Check`.

- [fuel:] the fuel.
- [n:] the node marked.
- [s:] the state before the marking.
- [s':] the state after the marking.
*)
Theorem mark_dirty_ends_dirty fuel n s s' :
  takes_marks s n -> mark_dirty fuel n s = Some s' -> ends_dirty s' n.
Proof.
  intros (d & Hd & Hk) Hc.
  destruct fuel as [|f]; simpl in Hc; [done|]. rewrite Hd in Hc.
  destruct Hk as [Hk|(Hk & Hdi & Hcl)]; rewrite Hk in Hc.
  - destruct (decide (n_freshness d = Clean)).
    { eapply marks_dirty; [by eapply notify_with_rtc_check|]. by apply set_node_dirty. }
    destruct (announce_due s d).
    { eapply marks_dirty; [by eapply notify_with_rtc_check|]. by apply set_node_dirty. }
    destruct (decide (n_freshness d = Dirty)); simplify_eq; [by exists d|]. by apply set_node_dirty.
  - simplify_eq. unfold effect_marked. rewrite Hdi, Hcl. simpl.
    destruct (n_queued d); by apply set_node_dirty.
Qed.

(**md
**A dirty walk leaves each observer `Dirty`.**

Each observer of the source that takes marks ends `Dirty`. `notify_dirty_except` skips the running computation, so the guarantee covers observers other than that one.

- [fuel:] the fuel.
- [src:] the source whose observers are marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] an observer of `src` that takes marks.
*)
Theorem notify_dirty_ends_dirty fuel src r s s' m :
  m ∈ observer_view (observers_of (g_edges s) src) ->
  takes_marks s m ->
  (notify_dirty fuel src s = Some s' \/
   (notify_dirty_except fuel src r s = Some s' /\ r <> Some m)) ->
  ends_dirty s' m.
Proof.
  intros Hin Ht [Hn|[Hn Hne]].
  - eapply (notify_with_reaches (fun s0 => ends_dirty s0 m));
      [intros ???; by eapply marks_dirty|intros ???; apply mark_dirty_rtc|
       intros ??; apply mark_dirty_ends_dirty|done|done|done].
  - eapply (notify_with_reaches (fun s0 => ends_dirty s0 m));
      [intros ???; by eapply marks_dirty|intros ???; apply mark_except_rtc| |done|done|done].
    intros s0 s1 Ht0. unfold mark_except. rewrite decide_False by done. by apply mark_dirty_ends_dirty.
Qed.

(** Every node of [s] is present in [s'], and is [Dirty] in [s'] only if it was in [s]. *)
Definition keeps_undirty (s s' : state) : Prop :=
  forall m d, g_nodes s !! m = Some d ->
    exists d', g_nodes s' !! m = Some d' /\ (n_freshness d' = Dirty -> n_freshness d = Dirty).

Lemma keeps_undirty_refl s : keeps_undirty s s.
Proof. intros m d Hd. eauto. Qed.

Lemma keeps_undirty_trans s1 s2 s3 :
  keeps_undirty s1 s2 -> keeps_undirty s2 s3 -> keeps_undirty s1 s3.
Proof.
  intros H1 H2 m d Hd. destruct (H1 _ _ Hd) as (d2 & Hd2 & Hf2).
  destruct (H2 _ _ Hd2) as (d3 & Hd3 & Hf3). eauto.
Qed.

Lemma set_node_undirty s n d f u q :
  g_nodes s !! n = Some d -> (f = Dirty -> n_freshness d = Dirty) ->
  keeps_undirty s (set_node n (node_marked f u q d) s).
Proof.
  intros Hd Hf m d0 Hm. simpl. destruct (decide (m = n)) as [->|Hne].
  - rewrite lookup_insert_eq. rewrite Hd in Hm. simplify_eq. eauto.
  - rewrite lookup_insert_ne by done. eauto.
Qed.

Lemma walk_undirty (mark : node_id -> state -> option state) src n s s' :
  (forall c s0 s1, mark c s0 = Some s1 -> keeps_undirty s0 s1) ->
  walk_marks mark src n s = Some s' ->
  keeps_undirty s s'.
Proof.
  intros Hm. revert s. induction n as [|i IH]; intros s Hw; simpl in Hw.
  - simplify_eq. apply keeps_undirty_refl.
  - destruct (decide _) as [_|_]; [by apply IH|].
    destruct (observer_items (observers_of (g_edges s) src) !! i) as [[c ?]|]; [|done].
    destruct (mark c s) as [s1|] eqn:Ec; simpl in Hw; [|done].
    eapply keeps_undirty_trans; [by eapply Hm|by apply IH].
Qed.

Lemma mark_check_undirty f n s s' : mark_check f n s = Some s' -> keeps_undirty s s'.
Proof.
  revert n s s'. induction f as [|f IH]; intros n s s' Hm; simpl in Hm; [done|].
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|simplify_eq; apply keeps_undirty_refl].
  destruct (n_kind d).
  - simplify_eq. apply keeps_undirty_refl.
  - destruct (decide (n_freshness d = Clean)).
    { eapply keeps_undirty_trans;
        [|eapply (walk_undirty (mark_check f)); [intros ???; apply IH|exact Hm]].
      apply set_node_undirty; [done|by intros]. }
    destruct (announce_due s d); [|simplify_eq; apply keeps_undirty_refl].
    eapply keeps_undirty_trans;
      [|eapply (walk_undirty (mark_check f)); [intros ???; apply IH|exact Hm]].
    apply set_node_undirty; [done|by intros].
  - simplify_eq. unfold effect_marked.
    assert (Hup : keeps_undirty s (set_node n (node_marked
                    (if decide (n_freshness d = Clean) then Check else n_freshness d) (n_unannounced d) true d) s)).
    { apply set_node_undirty; [done|]. destruct (decide _); by intros. }
    destruct (_ || _); [apply keeps_undirty_refl|].
    destruct (n_queued d); exact Hup.
Qed.

(**md
**A `Check` mark makes no node `Dirty`.**

`mark_check` and `notify_check` leave each node short of `Dirty` still short of it.

- [fuel:] the fuel.
- [n:] the node or source marked.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] a node of `s` that is not `Dirty`.
- [d:] the node `m` in `s`.
*)
Theorem mark_check_keeps_check fuel n s s' m d :
  (mark_check fuel n s = Some s' \/ notify_check fuel n s = Some s') ->
  g_nodes s !! m = Some d ->
  n_freshness d <> Dirty ->
  exists d', g_nodes s' !! m = Some d' /\ n_freshness d' <> Dirty.
Proof.
  intros Hc Hm Hf.
  assert (Hk : keeps_undirty s s').
  { destruct Hc as [Hc|Hc]; [by eapply mark_check_undirty|].
    eapply (walk_undirty (mark_check fuel)); [intros ???; apply mark_check_undirty|exact Hc]. }
  destruct (Hk _ _ Hm) as (d' & Hd' & Hd). exists d'. split; [done|]. intros Hx. by apply Hf, Hd.
Qed.

(**md
**A `Dirty` mark on a memo makes no other node `Dirty`.**

`mark_dirty` of a memo leaves every other node that was short of `Dirty` still short of it.

- [fuel:] the fuel.
- [n:] the memo marked.
- [d:] the node `n` in `s`.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [m:] a node other than `n`.
- [d0:] the node `m` in `s`, which is not `Dirty`.
*)
Theorem memo_mark_dirty_keeps_others_short fuel n d s s' m d0 :
  g_nodes s !! n = Some d ->
  n_kind d = KMemo ->
  mark_dirty fuel n s = Some s' ->
  m <> n ->
  g_nodes s !! m = Some d0 ->
  n_freshness d0 <> Dirty ->
  exists d', g_nodes s' !! m = Some d' /\ n_freshness d' <> Dirty.
Proof.
  intros Hd Hk Hc Hne Hm Hf.
  assert (Hset : forall f u q, g_nodes (set_node n (node_marked f u q d) s) !! m = Some d0).
  { intros. simpl. by rewrite lookup_insert_ne. }
  assert (Hwalk : forall f g u,
            notify_with (mark_check f) n (set_node n (node_marked g u (n_queued d) d) s) = Some s' ->
            exists d', g_nodes s' !! m = Some d' /\ n_freshness d' <> Dirty).
  { intros f g u Hn. eapply (mark_check_keeps_check f n); [right; exact Hn|apply Hset|done]. }
  destruct fuel as [|f]; simpl in Hc; [done|]. rewrite Hd, Hk in Hc.
  destruct (decide (n_freshness d = Clean)); [by eapply Hwalk|].
  destruct (announce_due s d); [by eapply Hwalk|].
  destruct (decide (n_freshness d = Dirty)); simplify_eq; [by exists d0|].
  exists d0. split; [apply Hset|done].
Qed.

(**md
**Marking a clean memo leaves its announcement flag as it was.**

`mark_check` sets the memo to `Check` and `mark_dirty` sets it to `Dirty`, each with `n_unannounced` unchanged, and then each walks the observers with `mark_check`. Given any positive fuel.

- [fuel:] the fuel, which the marks run at `S fuel`.
- [n:] the `Clean` memo marked.
- [d:] the node `n` in `s`.
- [s:] the state before the marking.
*)
Theorem clean_memo_keeps_unannounced fuel n d s :
  g_nodes s !! n = Some d ->
  n_kind d = KMemo ->
  n_freshness d = Clean ->
  mark_check (S fuel) n s =
    notify_with (mark_check fuel) n (set_node n (node_marked Check (n_unannounced d) (n_queued d) d) s) /\
  mark_dirty (S fuel) n s =
    notify_with (mark_check fuel) n (set_node n (node_marked Dirty (n_unannounced d) (n_queued d) d) s).
Proof.
  intros Hd Hk Hc. split.
  - simpl. rewrite Hd, Hk, decide_True by done. reflexivity.
  - unfold mark_dirty. simpl. rewrite Hd, Hk, decide_True by done. reflexivity.
Qed.
