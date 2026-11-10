From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Edges Marking Engine Check.

Open Scope nat_scope.

(** ** The wake-up statement *)

(**md
Node `n` exists in `s` and is `Check` or `Dirty`.
*)
Definition stale_of (s : state) (n : node_id) : Prop :=
  exists d, g_nodes s !! n = Some d /\ stale (n_freshness d) = true.

(**md
Node `n` is stale, or exists with its queued flag set.
*)
Definition stale_or_queued (s : state) (n : node_id) : Prop :=
  stale_of s n \/ exists d, g_nodes s !! n = Some d /\ n_queued d = true.

Definition n_unannounced_of (s : state) (n : node_id) : bool :=
  default false (n_unannounced <$> g_nodes s !! n).

(**md
Node `n` exists in `s` and is not disposed.
*)
Definition live_of (s : state) (n : node_id) : Prop :=
  exists d, g_nodes s !! n = Some d /\ n_disposed d = false.

(**md
The observer `c` of `src` is stale or queued, is running, or `src` holds an announcement.

- [s:] the state.
- [src:] the observed source.
- [c:] the observer.
*)
Definition observer_covered (s : state) (src c : node_id) : Prop :=
  stale_or_queued s c \/ In c (g_running s) \/ n_unannounced_of s src = true.

(** A disposed source is outside the statement: Core.fs 4372-4376 documents that it stops being woken and
    answers its last value. [disposed_stale_source_read] is the observer it leaves. *)
Definition wake_ok (s : state) : Prop :=
  forall src c, edge s src c -> stale_of s src -> live_of s src -> observer_covered s src c.

(** The form that holds on the present engine, with observers in [gh_passed] as its exception. *)
Definition wake_ok_unless_passed (s : state) : Prop :=
  forall src c, edge s src c -> stale_of s src -> live_of s src ->
    observer_covered s src c \/ c ∈ gh_passed (g_ghost s).

(** No computation has resolved [Clean] over a live, stale, unannounced source since its latest run start. *)
Definition nothing_passed (s : state) : Prop := gh_passed (g_ghost s) = ∅.

(** Every observer of a live, stale source is stale, queued or running, or a walk of its resolved [Clean] over a
    live, stale source. The source's flag is not a disjunct, so a reader left unmarked breaks this statement. *)
Definition wake_told (s : state) : Prop :=
  forall src c, edge s src c -> stale_of s src -> live_of s src ->
    stale_or_queued s c \/ In c (g_running s) \/ c ∈ gh_over (g_ghost s).

(** ** Reads *)

(**md
Appends the read `x` to `acc` unless `x` ends `acc`.
*)
Definition read_step (acc : list node_id) (x : node_id) : list node_id :=
  if decide (last acc = Some x) then acc else acc ++ [x].

(** A run's tracked reads as [sources_add] stores them: a read of the source just read adds nothing. *)
Definition read_list (reads : list node_id) : list node_id := foldl read_step [] reads.

(**md
The tracked reads of `c` since its latest run start, in order, from the ghost state.
*)
Definition reads_of (s : state) (c : node_id) : list node_id := default [] (gh_reads (g_ghost s) !! c).

(** A computation's tracked reads since its outermost run start. *)
Definition run_reads_of (s : state) (c : node_id) : list node_id := default [] (gh_run_reads (g_ghost s) !! c).

Lemma read_list_snoc l x : read_list (l ++ [x]) = read_step (read_list l) x.
Proof. unfold read_list. by rewrite foldl_app. Qed.

(** ** The invariant *)

(** [RecomputeScoped] has cleared [c]'s [Dirty], and [c] has not run since. *)
Definition in_window (s : state) (c : node_id) (dc : node) : Prop :=
  exists r, gh_window (g_ghost s) !! c = Some r /\ n_runs dc <= r.

(** The two forms of the invariant: with the source's flag and [gh_passed], and without the flag and with [gh_over]. *)
Definition resolved_set (b : bool) (g : ghost) : gset node_id := if b then gh_passed g else gh_over g.

(**md
The disjunction `told_on` requires of a computation `c` that read a live, stale source `e`: `c` is stale,
the source is announced (in the form with the flag), `c` is in the resolved set, or `c` is in its window.

- [b:] `true` for the form with the source's flag and `gh_passed`, `false` for the form with `gh_over`.
- [s:] the state.
- [c:] the computation.
- [dc:] the node of `c`.
- [e:] the node of the source.
*)
Definition told_cover (b : bool) (s : state) (c : node_id) (dc e : node) : Prop :=
  stale (n_freshness dc) = true \/ (b = true /\ n_unannounced e = true) \/ c ∈ resolved_set b (g_ghost s) \/ in_window s c dc.

(** A live computation that has read a live, stale source since its latest run start is stale, or the source
    holds an announcement, or the computation is in [gh_passed] or its window. [R] restricts the pairs. *)
Definition told_on (R : node_id -> node_id -> Prop) (s : state) : Prop :=
  forall b c src dc e, R c src -> g_nodes s !! c = Some dc -> g_nodes s !! src = Some e ->
    src ∈ read_prefix (g_edges s) c -> stale (n_freshness e) = true -> n_disposed e = false ->
    n_disposed dc = false -> told_cover b s c dc e.

(**md
`told_on` for every pair of computation and source.
*)
Definition told : state -> Prop := told_on (fun _ _ => True).

(**md
Every node with its queued flag set is stale.
*)
Definition queued_ok (s : state) : Prop :=
  forall n d, g_nodes s !! n = Some d -> n_queued d = true -> stale (n_freshness d) = true.

(** The graph facts a mark depends on. *)
Record mark_env (s : state) : Prop := {
  me_reachable : world_reachable (g_edges s);
  me_cleanup : forall c d, g_nodes s !! c = Some d -> n_in_cleanup d = true -> (slot_view (g_edges s) c).2 = 0;
  me_slots : forall c y, y ∈ (slot_view (g_edges s) c).1 -> has_kind y KMemo s \/ has_kind y KSignal s;
  me_signal : forall c d, g_nodes s !! c = Some d -> n_kind d = KSignal -> (slot_view (g_edges s) c).1 = []
}.

(** ** Marks *)

Lemma read_prefix_in' w c x : x ∈ read_prefix w c -> x ∈ (slot_view w c).1.
Proof. rewrite read_prefix_view. apply elem_take. Qed.

(** What a primitive mark keeps of a node. *)
Definition mark_same (d d' : node) : Prop :=
  n_kind d' = n_kind d /\ n_disposed d' = n_disposed d /\ n_in_cleanup d' = n_in_cleanup d /\
  n_runs d' = n_runs d /\ n_scope d' = n_scope d /\ (n_kind d = KMemo -> n_queued d' = n_queued d) /\
  (n_kind d = KSignal -> n_freshness d' = n_freshness d) /\ n_comparer d' = n_comparer d.

Lemma mark_same_refl d : mark_same d d.
Proof. split_and!; done. Qed.

Lemma mark_same_trans d1 d2 d3 : mark_same d1 d2 -> mark_same d2 d3 -> mark_same d1 d3.
Proof.
  intros (?&?&?&?&?&H&G&?) (K&?&?&?&?&H'&G'&?). split_and!; try congruence.
  - intros Hk. rewrite H', H; [done|done|congruence].
  - intros Hk. rewrite G', G; [done|done|congruence].
Qed.

(** Everything a mark holds fixed: all but nodes and the queue. *)
Record mark_fixed (s s' : state) : Prop := {
  mf_edges : g_edges s' = g_edges s;
  mf_ghost : g_ghost s' = g_ghost s;
  mf_running : g_running s' = g_running s;
  mf_current : g_current s' = g_current s;
  mf_flags : g_flags s' = g_flags s;
  mf_owners : g_owners s' = g_owners s;
  mf_flushing : g_flushing s' = g_flushing s;
  mf_forward : forall m d, g_nodes s !! m = Some d -> exists d', g_nodes s' !! m = Some d' /\ mark_same d d' /\
     freshness_le (n_freshness d) (n_freshness d') /\ (n_queued d = true -> n_queued d' = true) /\
     read_view_of d' = read_view_of d;
  mf_back : forall m d', g_nodes s' !! m = Some d' -> exists d, g_nodes s !! m = Some d /\ mark_same d d'
}.

Lemma mark_fixed_refl s : mark_fixed s s.
Proof.
  split; try done.
  - intros m d Hd. exists d. split_and!; first [done|apply mark_same_refl|apply freshness_le_refl].
  - intros m d Hd. exists d. split; [done|apply mark_same_refl].
Qed.

Lemma mark_fixed_trans s1 s2 s3 : mark_fixed s1 s2 -> mark_fixed s2 s3 -> mark_fixed s1 s3.
Proof.
  intros [H1 H2 H3 H4 H5 H6 H7 H8 H9] [J1 J2 J3 J4 J5 J6 J7 J8 J9].
  split; try congruence.
  - intros m d Hd. destruct (H8 m d Hd) as (d2&Hd2&Hs&Hf&Hq&Hv).
    destruct (J8 m d2 Hd2) as (d3&Hd3&Hs'&Hf'&Hq'&Hv').
    exists d3. split_and!; [done|by eapply mark_same_trans|by eapply freshness_le_trans|auto|congruence].
  - intros m d' Hd'. destruct (J9 m d' Hd') as (d2&Hd2&Hs).
    destruct (H9 m d2 Hd2) as (d1&Hd1&Hs'). exists d1. split; [done|by eapply mark_same_trans].
Qed.

Lemma set_marked_fixed s n d f u q :
  g_nodes s !! n = Some d -> freshness_le (n_freshness d) f -> (n_queued d = true -> q = true) ->
  (n_kind d = KMemo -> q = n_queued d) -> n_kind d <> KSignal ->
  mark_fixed s (set_node n (node_marked f u q d) s).
Proof.
  intros Hd Hf Hq Hm Hsg. split; try done; simpl.
  - intros m e He. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eexists. split_and!; first [done|apply mark_same_refl|split_and!; done].
    + rewrite lookup_insert_ne by done. exists e. split_and!; first [done|apply mark_same_refl|apply freshness_le_refl].
  - intros m e. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. exists d. split; [done|split_and!; done].
    + rewrite lookup_insert_ne by done. intros He. exists e. split; [done|apply mark_same_refl].
Qed.

Lemma enqueue_fixed n s : mark_fixed s (enqueue n s).
Proof.
  split; try done; simpl.
  - intros m d Hd. exists d. split_and!; first [done|apply mark_same_refl|apply freshness_le_refl].
  - intros m d Hd. exists d. split; [done|apply mark_same_refl].
Qed.

Lemma prim_fixed s s' : mark_prim s s' -> mark_fixed s s'.
Proof.
  intros Hp. destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hq Hf|s n d f Hd Hk Hq Hf].
  - apply set_marked_fixed; [done|done|done|done|congruence].
  - apply set_marked_fixed; [done|done|done|congruence|congruence].
  - eapply mark_fixed_trans; [|apply enqueue_fixed]. apply set_marked_fixed; [done|done|done|congruence|congruence].
Qed.

Lemma marks_fixed s s' : rtc mark_prim s s' -> mark_fixed s s'.
Proof.
  induction 1 as [s|s1 s2 s3 Hp _ IH]; [apply mark_fixed_refl|].
  eapply mark_fixed_trans; [by apply prim_fixed|done].
Qed.

Lemma has_kind_fixed s s' n k : mark_fixed s s' -> has_kind n k s -> has_kind n k s'.
Proof.
  intros Hf (d&Hd&Hk). destruct (mf_forward _ _ Hf n d Hd) as (d'&Hd'&(Hs&_)&_).
  exists d'. split; [done|congruence].
Qed.

Lemma mark_env_fixed s s' : mark_fixed s s' -> mark_env s -> mark_env s'.
Proof.
  intros Hf H. destruct H as [H1 H2 H3 H4]. pose proof (mf_edges _ _ Hf) as He. pose proof (mf_back _ _ Hf) as Hb.
  split; rewrite ?He; try done.
  - intros c d Hd Hc. destruct (Hb c d Hd) as (d0&Hd0&(_&_&Hs&_)). eapply H2; [done|congruence].
  - intros c y Hy. destruct (H3 c y Hy) as [Hk|Hk]; [left|right]; by eapply has_kind_fixed.
  - intros c d Hd Hc. destruct (Hb c d Hd) as (d0&Hd0&(Hs&_)). eapply H4; [done|congruence].
Qed.

(** A write of [n]'s freshness keeps every pair whose source is another node. *)
Lemma told_marked R s n d f u q :
  told_on R s -> g_nodes s !! n = Some d -> freshness_le (n_freshness d) f ->
  told_on (fun c src => R c src /\ src <> n) (set_node n (node_marked f u q d) s).
Proof.
  intros H Hd Hf b c src dc e [Hr Hne] Hc He Hpre Hst Hde Hdc. simpl in Hc, He, Hpre.
  rewrite lookup_insert_ne in He by done.
  destruct (decide (c = n)) as [->|Hcn].
  - rewrite lookup_insert_eq in Hc. injection Hc as <-. simpl in Hdc.
    destruct (H b n src d e Hr Hd He Hpre Hst Hde Hdc) as [C|[C|[C|C]]]; unfold told_cover; simpl.
    + left. by eapply stale_rises.
    + by right; left.
    + by right; right; left.
    + by right; right; right.
  - rewrite lookup_insert_ne in Hc by done.
    exact (H b c src dc e Hr Hc He Hpre Hst Hde Hdc).
Qed.

(** A write of a stale [n]'s freshness that keeps its flag keeps every pair. *)
Lemma told_marked_stale R s n d f q :
  told_on R s -> g_nodes s !! n = Some d -> freshness_le (n_freshness d) f -> stale (n_freshness d) = true ->
  told_on R (set_node n (node_marked f (n_unannounced d) q d) s).
Proof.
  intros H Hd Hf Hst b c src dc e Hr Hc He Hpre Hs Hde Hdc. simpl in Hc, He, Hpre.
  destruct (decide (src = n)) as [->|Hsn].
  - rewrite lookup_insert_eq in He. injection He as <-. simpl in Hde.
    destruct (decide (c = n)) as [->|Hcn].
    + rewrite lookup_insert_eq in Hc. injection Hc as <-. left. simpl. by eapply stale_rises.
    + rewrite lookup_insert_ne in Hc by done.
      destruct (H b c n dc d Hr Hc Hd Hpre Hst Hde Hdc) as [C|[C|[C|C]]]; unfold told_cover; simpl.
      * by left.
      * by right; left.
      * by right; right; left.
      * by right; right; right.
  - rewrite lookup_insert_ne in He by done.
    assert (Hk : told_on (fun c src => R c src /\ src <> n) (set_node n (node_marked f (n_unannounced d) q d) s)).
    { by apply told_marked. }
    apply (Hk b c src dc e); simpl; auto. by rewrite lookup_insert_ne.
Qed.

Lemma queued_marked s n d f u q :
  queued_ok s -> (q = true -> stale f = true) ->
  queued_ok (set_node n (node_marked f u q d) s).
Proof.
  intros H Hq m e. simpl. destruct (decide (m = n)) as [->|Hne].
  - rewrite lookup_insert_eq. intros [=<-]. simpl. auto.
  - rewrite lookup_insert_ne by done. apply H.
Qed.

Lemma walk_keeps (mark : node_id -> state -> option state) (P : state -> Prop) src k s s' :
  (forall c s0 s1, P s0 -> mark c s0 = Some s1 -> P s1) -> P s -> walk_marks mark src k s = Some s' -> P s'.
Proof.
  intros Hm. revert s. induction k as [|i IH]; intros s Hs Hw; simpl in Hw.
  - by simplify_eq.
  - destruct (decide _); [by eapply IH|].
    destruct (observer_items (observers_of (g_edges s) src) !! i) as [[c ?]|]; [|done].
    destruct (mark c s) as [s1|] eqn:E; simpl in Hw; [|done]. eapply IH; [|exact Hw]. by eapply Hm.
Qed.

(** What a mark needs and keeps, with the told pairs of [R]. *)
Definition mark_state (R : node_id -> node_id -> Prop) (s : state) : Prop :=
  mark_env s /\ told_on R s /\ queued_ok s.

Lemma set_marked_prim s n d f u :
  g_nodes s !! n = Some d -> n_kind d = KMemo -> freshness_le (n_freshness d) f ->
  (u = n_unannounced d \/ u = false) ->
  mark_fixed s (set_node n (node_marked f u (n_queued d) d) s).
Proof. intros. apply marks_fixed, rtc_once. by eapply prim_memo. Qed.

Lemma pair_takes s s' c src dc :
  mark_env s -> mark_fixed s s' -> g_nodes s' !! c = Some dc -> src ∈ read_prefix (g_edges s) c ->
  n_disposed dc = false -> takes_marks s c.
Proof.
  intros He Hf Hc Hpre Hl.
  destruct (mf_back _ _ Hf c dc Hc) as (d&Hd&(Hk&Hdi&_)). exists d. split; [done|].
  destruct (n_kind d) eqn:Ek; [|by left|right].
  - exfalso. pose proof (me_signal _ He c d Hd Ek) as H. rewrite read_prefix_view, H in Hpre.
    rewrite take_nil in Hpre. by apply not_elem_of_nil in Hpre.
  - split_and!; [done|congruence|].
    destruct (n_in_cleanup d) eqn:Ec; [|done]. exfalso.
    pose proof (me_cleanup _ He c d Hd Ec) as H. rewrite read_prefix_view, H, take_0 in Hpre.
    by apply not_elem_of_nil in Hpre.
Qed.

(** The walk from a memo's new freshness restores the pairs whose source is the memo. *)
Lemma forward_told f R n d F u s s' :
  (forall c R0 s0 s1, mark_state R0 s0 -> mark_check f c s0 = Some s1 -> mark_state R0 s1) ->
  mark_state R s -> g_nodes s !! n = Some d -> n_kind d = KMemo -> freshness_le (n_freshness d) F ->
  (u = n_unannounced d \/ u = false) -> stale F = true ->
  notify_with (mark_check f) n (set_node n (node_marked F u (n_queued d) d) s) = Some s' ->
  mark_state R s'.
Proof.
  intros IH (He&Ht&Hq) Hd Hk Hf Hu HF Hw.
  set (s1 := set_node n (node_marked F u (n_queued d) d) s) in Hw.
  assert (Hf1 : mark_fixed s s1) by (by apply set_marked_prim).
  assert (Hrt : rtc mark_prim s1 s') by (eapply notify_with_rtc; [|exact Hw]; intros ???; apply mark_check_rtc).
  assert (Hf2 := marks_fixed _ _ Hrt).
  assert (HQ : queued_ok s1).
  { apply queued_marked; [done|]. intros Hqd. by eapply stale_rises, Hq. }
  assert (Hend : mark_state (fun c src => R c src /\ src <> n) s').
  { refine (walk_keeps _ _ _ _ _ _ _ _ Hw).
    - intros c s0 s2 Hs0 Hm. exact (IH c _ s0 s2 Hs0 Hm).
    - split_and!; [by eapply mark_env_fixed|by apply told_marked|done]. }
  destruct Hend as (He'&Ht'&Hq').
  split_and!; [done| |done].
  intros b c src dc e Hr Hc Hes Hpre Hs Hde Hdc.
  destruct (decide (src = n)) as [->|Hne]; [|exact (Ht' b c src dc e (conj Hr Hne) Hc Hes Hpre Hs Hde Hdc)].
  pose proof (mark_fixed_trans _ _ _ Hf1 Hf2) as Hall.
  assert (HE : g_edges s' = g_edges s) by apply Hall.
  rewrite HE in Hpre.
  assert (Hin : c ∈ observer_view (observers_of (g_edges s1) n)).
  { change (g_edges s1) with (g_edges s). apply prefix_observer; [apply He|done]. }
  assert (Ht1 : takes_marks s1 c).
  { eapply (pair_takes s1 s' c n dc); [by eapply mark_env_fixed|done|done|done|done]. }
  destruct (forward_members _ _ _ _ _ Hw Hin Ht1) as (dc2&Hdc2&Hst).
  assert (dc2 = dc) by congruence. subst dc2. by left.
Qed.

Lemma effect_mark_keeps R s n d F :
  mark_state R s -> g_nodes s !! n = Some d -> n_kind d = KEffect -> freshness_le (n_freshness d) F -> stale F = true ->
  mark_state R (effect_marked F n d s).
Proof.
  intros Hs Hd Hk HF HFs. unfold effect_marked. destruct Hs as (He&Ht&Hq).
  destruct (n_disposed d || n_in_cleanup d); [split_and!; done|].
  assert (Hset : mark_state R (set_node n (node_marked F (n_unannounced d) true d) s)).
  { assert (Hfx : mark_fixed s (set_node n (node_marked F (n_unannounced d) true d) s)).
    { apply set_marked_fixed; [done|done|done|congruence|congruence]. }
    split_and!.
    - by eapply mark_env_fixed.
    - intros b c src dc e Hr Hc Hes Hpre Hst Hde Hdc.
      assert (Hsn : src <> n).
      { intros ->. simpl in Hpre. destruct (me_slots _ He c n (read_prefix_in' _ _ _ Hpre)) as [Hk'|Hk'];
          pose proof (has_kind_unique n _ KEffect s Hk' ltac:(by exists d)); done. }
      apply (told_marked R s n d F (n_unannounced d) true Ht Hd HF b c src dc e); auto.
    - apply queued_marked; auto. }
  destruct (n_queued d); [done|].
  destruct Hset as (He2&Ht2&Hq2). split_and!; [eapply mark_env_fixed; [apply enqueue_fixed|done]|done|done].
Qed.

Lemma mark_check_keeps f : forall c R s s', mark_state R s -> mark_check f c s = Some s' -> mark_state R s'.
Proof.
  induction f as [|f IH]; intros n R s s' Hs Hm; simpl in Hm; [done|].
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|by simplify_eq].
  destruct (n_kind d) eqn:Hk.
  - by simplify_eq.
  - destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
    + eapply (forward_told f R n d Check); [exact IH|done|done|done| |by left|done|exact Hm].
      rewrite Hc. apply freshness_le_clean.
    + destruct (announce_due s d); [|by simplify_eq].
      eapply (forward_told f R n d (n_freshness d)); [exact IH|done|done|done|apply freshness_le_refl|by right|by apply stale_of_not_clean|exact Hm].
  - simplify_eq. apply effect_mark_keeps; [done|done|done|..].
    + destruct (decide _) as [->|_]; [apply freshness_le_clean|apply freshness_le_refl].
    + destruct (decide _) as [_|Hn]; [done|by apply stale_of_not_clean].
Qed.

Lemma mark_dirty_keeps f c R s s' : mark_state R s -> mark_dirty f c s = Some s' -> mark_state R s'.
Proof.
  rename c into n. intros Hs Hm. destruct f as [|f]; simpl in Hm; [done|].
  destruct (g_nodes s !! n) as [d|] eqn:Hd; [|by simplify_eq].
  destruct (n_kind d) eqn:Hk.
  - by simplify_eq.
  - destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
    + eapply (forward_told f R n d Dirty); [apply mark_check_keeps|done|done|done|apply freshness_le_dirty|by left|done|exact Hm].
    + destruct (announce_due s d).
      * eapply (forward_told f R n d Dirty); [apply mark_check_keeps|done|done|done|apply freshness_le_dirty|by right|done|exact Hm].
      * destruct (decide (n_freshness d = Dirty)); [by simplify_eq|]. simplify_eq.
        destruct Hs as (He&Ht&Hq). split_and!.
        -- eapply mark_env_fixed; [|done]. apply set_marked_prim; [done|done|apply freshness_le_dirty|by left].
        -- apply told_marked_stale; [done|done|apply freshness_le_dirty|by apply stale_of_not_clean].
        -- apply queued_marked; done.
  - simplify_eq. apply effect_mark_keeps; [done|done|done|apply freshness_le_dirty|done].
Qed.

Lemma notify_keeps (mark : node_id -> state -> option state) R src s s' :
  (forall c s0 s1, mark_state R s0 -> mark c s0 = Some s1 -> mark_state R s1) ->
  mark_state R s -> notify_with mark src s = Some s' -> mark_state R s'.
Proof. intros Hm Hs Hw. exact (walk_keeps _ _ _ _ _ _ Hm Hs Hw). Qed.

Lemma any_mark_keeps f n r R s s' : mark_state R s -> any_mark f n r s s' -> mark_state R s'.
Proof.
  intros Hs [H|[H|[H|[H|H]]]].
  - by eapply mark_check_keeps.
  - by eapply mark_dirty_keeps.
  - eapply notify_keeps; [|done|done]. intros ???. apply mark_check_keeps.
  - eapply notify_keeps; [|done|done]. intros ???. apply mark_dirty_keeps.
  - eapply notify_keeps; [|done|done]. intros c s0 s1 Hs0 Hm. unfold mark_except in Hm.
    destruct (decide _); [by simplify_eq|by eapply mark_dirty_keeps].
Qed.

Local Arguments slot_view : simpl never.
Local Arguments read_prefix : simpl never.

(** ** The invariant beside the check invariant *)

(**md
No element of `l` is followed by itself.
*)
Definition no_repeat (l : list node_id) : Prop := forall i x, l !! i = Some x -> l !! S i <> Some x.

(**md
The bookkeeping facts about flags, source lists, cursors, running nodes, cleanups and ghost reads that
`check_invariant` does not state. Each field is one such fact.
*)
Record run_base (s : state) : Prop := {
  b_flag_u : f_unannounced (g_flags s) = true;
  b_flag_r : f_reader_mark (g_flags s) = true;
  b_env : mark_env s;
  b_queued : queued_ok s;
  b_cursor : forall c, (slot_view (g_edges s) c).2 <= length (slot_view (g_edges s) c).1;
  b_ended : forall c, c ∉ g_running s -> (slot_view (g_edges s) c).2 = length (slot_view (g_edges s) c).1;
  b_disposed : forall c d, g_nodes s !! c = Some d -> n_disposed d = true -> c ∉ g_running s ->
    (slot_view (g_edges s) c).1 = [];
  b_no_repeat : forall c, no_repeat (slot_view (g_edges s) c).1;
  b_reads : forall c d, g_nodes s !! c = Some d -> n_disposed d = false ->
    read_prefix (g_edges s) c = read_list (reads_of s c);
  b_memo : forall n d, g_nodes s !! n = Some d -> n_kind d = KMemo -> n_queued d = false /\ n_in_cleanup d = false;
  b_cleanup_running : forall c d, g_nodes s !! c = Some d -> n_in_cleanup d = true -> c ∈ g_running s;
  b_cleanup_disposed : forall c d, g_nodes s !! c = Some d -> n_in_cleanup d = true -> n_disposed d = true ->
    (slot_view (g_edges s) c).1 = [];
  b_cleanup_current : forall c d, g_current s = Some c -> g_nodes s !! c = Some d -> n_in_cleanup d = false;
  b_current : forall c, g_current s = Some c -> c ∈ g_running s;
  b_running_kind : forall c, c ∈ g_running s -> has_kind c KMemo s \/ has_kind c KEffect s;
  b_effect_flushing : forall c, has_kind c KEffect s -> c ∈ g_running s -> g_flushing s = true;
  b_lists : forall o x, g_owners s !! o = Some x ->
    length (o_cleanups x) = length (o_cleanup_envs x) /\ length (o_cleanups x) = length (o_cleanup_order x);
  b_window : (forall n r, gh_window (g_ghost s) !! n = Some r -> has_kind n KMemo s) /\
    (forall n l, gh_reads (g_ghost s) !! n = Some l -> is_Some (g_nodes s !! n)) /\
    (forall n d, g_nodes s !! n = Some d -> n_kind d = KSignal -> n_freshness d = Clean) /\
    (forall c, c ∉ gh_nested (g_ghost s) -> run_reads_of s c = reads_of s c)
}.

(**md
`run_base` and `told` hold.
*)
Definition run_extras (s : state) : Prop := run_base s /\ told s.

(** What the engine holds of a whole call beside [frame]. *)
Record ekept (s s' : state) : Prop := {
  k_flushing : g_flushing s' = g_flushing s;
  k_cleanup : forall c d, g_nodes s !! c = Some d -> exists d', g_nodes s' !! c = Some d' /\ n_in_cleanup d' = n_in_cleanup d;
  k_window : forall m d' r, g_nodes s' !! m = Some d' -> gh_window (g_ghost s') !! m = Some r ->
    n_disposed d' = false -> n_runs d' <= r ->
    exists d r0, g_nodes s !! m = Some d /\ gh_window (g_ghost s) !! m = Some r0 /\ n_disposed d = false /\ n_runs d <= r0
}.

Lemma ekept_refl s : ekept s s.
Proof.
  split; [done|intros c d Hd; by exists d|intros m d r ????; by exists d, r].
Qed.

Lemma ekept_trans s1 s2 s3 : ekept s1 s2 -> ekept s2 s3 -> ekept s1 s3.
Proof.
  intros [H1 H2 H3] [J1 J2 J3]. split; [congruence| |].
  - intros c d Hd. destruct (H2 c d Hd) as (d2&Hd2&Hc2). destruct (J2 c d2 Hd2) as (d3&Hd3&Hc3).
    exists d3. split; [done|congruence].
  - intros m d r Hd Hw Hl Hr. destruct (J3 m d r Hd Hw Hl Hr) as (d2&r2&Hd2&Hw2&Hl2&Hr2). eauto.
Qed.

(** The fields [run_base] and [told] read. *)
Record fields_same (s s' : state) : Prop := {
  fs_edges : g_edges s' = g_edges s;
  fs_nodes : g_nodes s' = g_nodes s;
  fs_window : gh_window (g_ghost s') = gh_window (g_ghost s);
  fs_reads : gh_reads (g_ghost s') = gh_reads (g_ghost s);
  fs_passed : gh_passed (g_ghost s') = gh_passed (g_ghost s);
  fs_over : gh_over (g_ghost s') = gh_over (g_ghost s);
  fs_run_reads : gh_run_reads (g_ghost s') = gh_run_reads (g_ghost s);
  fs_nested : gh_nested (g_ghost s') = gh_nested (g_ghost s);
  fs_running : g_running s' = g_running s;
  fs_current : g_current s' = g_current s;
  fs_flags : g_flags s' = g_flags s;
  fs_owners : g_owners s' = g_owners s;
  fs_flushing : g_flushing s' = g_flushing s
}.

Lemma mark_env_nodes_edges s s' :
  g_edges s' = g_edges s -> g_nodes s' = g_nodes s -> mark_env s -> mark_env s'.
Proof.
  intros He Hn [H1 H2 H3 H4]. split; unfold has_kind in *; rewrite ?He, ?Hn; done.
Qed.

Lemma base_same s s' : fields_same s s' -> run_base s -> run_base s'.
Proof.
  intros [He Hn Hw Hr Hp Hov Hrr Hne Hru Hc Hf Ho Hfl] H. destruct H as [B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  assert (B3' := mark_env_nodes_edges _ _ He Hn B3).
  split; unfold has_kind, reads_of, run_reads_of, queued_ok in *; rewrite ?He, ?Hn, ?Hw, ?Hr, ?Hrr, ?Hne, ?Hp, ?Hru, ?Hc, ?Hf, ?Ho, ?Hfl; done.
Qed.

Lemma told_same R s s' : fields_same s s' -> told_on R s -> told_on R s'.
Proof.
  intros [He Hn Hw Hr Hp Hov Hrr Hne Hru Hc Hf Ho Hfl] H.
  unfold told_on, told_cover, in_window, resolved_set in *. rewrite ?He, ?Hn, ?Hw, ?Hp, ?Hov. done.
Qed.

Lemma extras_same s s' : fields_same s s' -> run_extras s -> run_extras s'.
Proof. intros Hs [H1 H2]. split; [by eapply base_same|by eapply told_same]. Qed.

Lemma ekept_same s s' : fields_same s s' -> ekept s s'.
Proof.
  intros [He Hn Hw Hr Hp Hov Hrr Hne Hru Hc Hf Ho Hfl]. split; [done| |].
  - intros c d Hd. exists d. by rewrite Hn.
  - intros m d r. rewrite Hn, Hw. intros. by exists d, r.
Qed.

(** ** Updates of one node *)

Lemma has_kind_put n d d' m k s :
  g_nodes s !! n = Some d -> n_kind d' = n_kind d -> has_kind m k s -> has_kind m k (set_node n d' s).
Proof. apply has_kind_set. Qed.

Lemma base_set n u d s :
  run_base s -> g_nodes s !! n = Some d -> n_kind (u d) = n_kind d ->
  (n_queued (u d) = true -> stale (n_freshness (u d)) = true) ->
  (n_disposed (u d) = true -> n ∉ g_running s -> (slot_view (g_edges s) n).1 = []) ->
  (n_disposed (u d) = false -> n_disposed d = false) ->
  (n_kind d = KMemo -> n_queued (u d) = false /\ n_in_cleanup (u d) = false) ->
  (n_in_cleanup (u d) = true -> n ∈ g_running s /\ (slot_view (g_edges s) n).2 = 0 /\
     (n_disposed (u d) = true -> (slot_view (g_edges s) n).1 = []) /\ g_current s <> Some n) ->
  (n_kind d = KSignal -> n_freshness (u d) = Clean) ->
  run_base (set_node n (u d) s).
Proof.
  intros H Hd Hk Hq Hdi Hl Hm Hc Hsg. destruct H as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  assert (Hup : forall m k, has_kind m k s -> has_kind m k (set_node n (u d) s)).
  { intros m k. by apply has_kind_put with d. }
  split; try done; simpl.
  - split; simpl; try done.
    + intros c e. destruct (decide (c = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hcl. by destruct (Hc Hcl) as (_&H2&_).
      * rewrite lookup_insert_ne by done. apply E2.
    + intros c y Hy. destruct (E3 c y Hy) as [K|K]; [left|right]; auto.
    + intros c e. destruct (decide (c = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hk'. rewrite Hk in Hk'. apply (E4 n d Hd Hk').
      * rewrite lookup_insert_ne by done. apply E4.
  - intros m e. simpl. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. done.
    + rewrite lookup_insert_ne by done. eauto.
  - intros c e. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. exact Hdi.
    + rewrite lookup_insert_ne by done. apply B7.
  - intros c e. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hle. eapply B9; [exact Hd|auto].
    + rewrite lookup_insert_ne by done. apply B9.
  - intros m e. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hk'. rewrite Hk in Hk'. auto.
    + rewrite lookup_insert_ne by done. apply B10.
  - intros c e. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hcl. by destruct (Hc Hcl).
    + rewrite lookup_insert_ne by done. apply B11.
  - intros c e. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hcl Hde. by destruct (Hc Hcl) as (_&_&H3&_); apply H3.
    + rewrite lookup_insert_ne by done. apply B12.
  - intros c e Hcur. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. destruct (n_in_cleanup (u d)) eqn:E; [|done].
      destruct (Hc eq_refl) as (_&_&_&Hx). done.
    + rewrite lookup_insert_ne by done. intros He. eapply B13; [done|exact He].
  - intros c Hr. destruct (B15 c Hr) as [K|K]; [left|right]; auto.
  - intros c Hke Hr. apply (B16 c); [|done].
    exact (has_kind_set_back n d (u d) c KEffect s Hd Hk Hke).
  - destruct B18 as [W [W' [W'' W4]]]. split_and!; [| | |exact W4].
    + intros m r Hw. apply Hup. eauto.
    + intros m l Hl'. destruct (W' m l Hl') as [e He].
      destruct (decide (m = n)) as [->|Hne]; [rewrite lookup_insert_eq; by eexists|rewrite lookup_insert_ne by done; by exists e].
    + intros m e. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hk'. rewrite Hk in Hk'. auto.
      * rewrite lookup_insert_ne by done. apply W''.
Qed.

(** A write of [n] leaves the pairs of other observers. *)
Lemma told_others R n u d s :
  told_on R s -> g_nodes s !! n = Some d ->
  (stale (n_freshness (u d)) = true -> n_disposed (u d) = false ->
     stale (n_freshness d) = true /\ n_disposed d = false /\ (n_unannounced d = true -> n_unannounced (u d) = true)) ->
  told_on (fun c y => R c y /\ c <> n) (set_node n (u d) s).
Proof.
  intros H Hd Hsrc b c src dc e [Hr Hne] Hc He Hpre Hst Hde Hdc. simpl in Hc, He, Hpre.
  rewrite lookup_insert_ne in Hc by done.
  destruct (decide (src = n)) as [->|Hsn].
  - rewrite lookup_insert_eq in He. injection He as <-.
    destruct (Hsrc Hst Hde) as (H1&H2&H3).
    destruct (H b c n dc d Hr Hc Hd Hpre H1 H2 Hdc) as [C|[C|[C|C]]].
    + by left.
    + right; left. destruct C. auto.
    + by right; right; left.
    + by right; right; right.
  - rewrite lookup_insert_ne in He by done. exact (H b c src dc e Hr Hc He Hpre Hst Hde Hdc).
Qed.

(** A write of [n] that leaves it covered wherever it was keeps every pair. *)
Lemma told_set R n u d s :
  told_on R s -> g_nodes s !! n = Some d ->
  (stale (n_freshness (u d)) = true -> n_disposed (u d) = false ->
     stale (n_freshness d) = true /\ n_disposed d = false /\ (n_unannounced d = true -> n_unannounced (u d) = true)) ->
  (n_disposed (u d) = false -> n_disposed d = false) ->
  (stale (n_freshness d) = true -> stale (n_freshness (u d)) = true) ->
  (n_runs (u d) <= n_runs d \/ gh_window (g_ghost s) !! n = None) ->
  told_on R (set_node n (u d) s).
Proof.
  intros H Hd Hsrc Hl Hs Hr b c src dc e HR Hc He Hpre Hst Hde Hdc.
  destruct (decide (c = n)) as [->|Hne].
  - simpl in Hc, He, Hpre. rewrite lookup_insert_eq in Hc. injection Hc as <-.
    destruct (decide (src = n)) as [->|Hsn].
    + rewrite lookup_insert_eq in He. injection He as <-. left. done.
    + rewrite lookup_insert_ne in He by done.
      destruct (H b n src d e HR Hd He Hpre Hst Hde (Hl Hdc)) as [C|[C|[C|C]]].
      * left. auto.
      * by right; left.
      * by right; right; left.
      * right; right; right. destruct C as (r&Hw&Hle). exists r. split; [done|].
        destruct Hr as [Hr|Hr]; [simpl in *; lia|simpl in Hw; congruence].
  - apply (told_others R n u d s H Hd Hsrc b c src dc e); auto.
Qed.

(** A change to a node's other fields. *)
Definition neutral (u : node -> node) (d : node) : Prop :=
  n_kind (u d) = n_kind d /\ n_freshness (u d) = n_freshness d /\ n_unannounced (u d) = n_unannounced d /\
  n_disposed (u d) = n_disposed d /\ (n_queued (u d) = true -> n_queued d = true) /\
  n_in_cleanup (u d) = n_in_cleanup d /\ n_runs (u d) = n_runs d.

Lemma ekept_set n u d s :
  g_nodes s !! n = Some d -> n_in_cleanup (u d) = n_in_cleanup d ->
  (n_disposed (u d) = false -> n_disposed d = false) -> n_runs d <= n_runs (u d) ->
  ekept s (set_node n (u d) s).
Proof.
  intros Hd Hc Hl Hr. split; try done.
  - intros m e He. simpl. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eauto.
    + rewrite lookup_insert_ne by done. eauto.
  - intros m e r. simpl. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hw Hl' Hle. exists d, r. split_and!; auto. lia.
    + rewrite lookup_insert_ne by done. intros He Hw Hl' Hle. exists e, r. auto.
Qed.

Lemma extras_neutral n u d s :
  run_extras s -> g_nodes s !! n = Some d -> neutral u d ->
  run_extras (set_node n (u d) s) /\ ekept s (set_node n (u d) s).
Proof.
  intros [Hb Ht] Hd (N1&N2&N3&N4&N5&N6&N7). split; [split|].
  - apply base_set; try done.
    + rewrite N2. intros Hq. by eapply (b_queued _ Hb), N5.
    + rewrite N4. intros H1 H2. by eapply (b_disposed _ Hb).
    + by rewrite N4.
    + rewrite N6. intros Hk. destruct (b_memo _ Hb n d Hd Hk) as [H1 H2]. split; [|done].
      destruct (n_queued (u d)) eqn:Eq; [|done]. rewrite N5 in H1; done.
    + rewrite N6, N4. intros Hc. split_and!.
      * by eapply (b_cleanup_running _ Hb).
      * by eapply (me_cleanup _ (b_env _ Hb)).
      * intros H1. by eapply (b_cleanup_disposed _ Hb).
      * intros Hcur. pose proof (b_cleanup_current _ Hb n d Hcur Hd). congruence.
    + rewrite N2. intros Hk. destruct (b_window _ Hb) as (_&_&W&_). eauto.
  - apply told_set; try done.
    + rewrite N2, N4, N3. intros H1 H2. auto.
    + by rewrite N4.
    + by rewrite N2.
    + left. lia.
  - apply ekept_set; try done; [by rewrite N4|lia].
Qed.

(** ** Marks keep the invariant *)

Lemma base_marks R f n r s s' :
  run_base s -> told_on R s -> any_mark f n r s s' -> (run_base s' /\ told_on R s') /\ ekept s s'.
Proof.
  intros Hb Ht Hm.
  destruct (any_mark_keeps f n r R s s' (conj (b_env _ Hb) (conj Ht (b_queued _ Hb))) Hm) as (He&Ht'&Hq).
  pose proof (marks_fixed _ _ (any_mark_rtc _ _ _ _ _ Hm)) as Hf.
  destruct Hf as [F1 F2 F3 F4 F5 F6 F7 F8 F9].
  assert (Hf : mark_fixed s s') by (split; done).
  destruct Hb as [B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  split; [split; [split|exact Ht']|split].
  - by rewrite F5.
  - by rewrite F5.
  - done.
  - done.
  - by rewrite F1.
  - rewrite F1, F3. done.
  - rewrite F1, F3. intros c d Hd Hdi Hnr. destruct (F9 c d Hd) as (d0&Hd0&(_&Hs&_)). eapply B7; [done|congruence|done].
  - by rewrite F1.
  - unfold reads_of. rewrite F1, F2. intros c d Hd Hl. destruct (F9 c d Hd) as (d0&Hd0&(_&Hs&_)). eapply B9; [done|congruence].
  - intros m d Hd Hk. destruct (F9 m d Hd) as (d0&Hd0&(Hk0&_&Hs&_&_&Hq0&_&_)).
    destruct (B10 m d0 Hd0 ltac:(congruence)) as [H1 H2]. split; [|congruence].
    rewrite Hq0; [done|congruence].
  - rewrite F3. intros c d Hd Hc. destruct (F9 c d Hd) as (d0&Hd0&(_&_&Hs&_)). eapply B11; [done|congruence].
  - rewrite F1. intros c d Hd Hc Hdi. destruct (F9 c d Hd) as (d0&Hd0&(_&Hs1&Hs&_)). eapply B12; [done|congruence|congruence].
  - rewrite F4. intros c d Hcur Hd. destruct (F9 c d Hd) as (d0&Hd0&(_&_&Hs&_)). rewrite Hs. eauto.
  - by rewrite F4, F3.
  - rewrite F3. intros c Hc. destruct (B15 c Hc) as [K|K]; [left|right]; by eapply has_kind_fixed.
  - rewrite F3, F7. intros c (d&Hd&Hk) Hc. destruct (F9 c d Hd) as (d0&Hd0&(Hk0&_)).
    apply (B16 c); [exists d0; split; [done|congruence]|done].
  - by rewrite F6.
  - rewrite F2. destruct B18 as [W1 [W2 [W3 W4]]]. split_and!; [| | |unfold reads_of, run_reads_of in *; rewrite F2; exact W4].
    + intros m r0 Hw. eapply has_kind_fixed; [done|eauto].
    + intros m l Hl'. destruct (W2 m l Hl') as [e0 He0]. destruct (F8 m e0 He0) as (e'&He'&_). by exists e'.
    + intros m e0 He0 Hk. destruct (F9 m e0 He0) as (d0&Hd0&(Hk0&_&_&_&_&_&Hsg&_)).
      rewrite Hsg by congruence. apply (W3 m d0 Hd0). congruence.
  - done.
  - intros c d Hd. destruct (F8 c d Hd) as (d'&Hd'&(_&_&Hs&_)&_). eauto.
  - intros m d' r0 Hd' Hw Hl Hle. destruct (F9 m d' Hd') as (d&Hd&(_&Hs1&_&Hs2&_)).
    rewrite F2 in Hw. exists d, r0. split_and!; [done|done|congruence|lia].
Qed.

Lemma extras_marks f n r s s' :
  run_extras s -> any_mark f n r s s' -> run_extras s' /\ ekept s s'.
Proof. intros [Hb Ht] Hm. exact (base_marks _ f n r s s' Hb Ht Hm). Qed.

(** ** Source lists under a read *)

Lemma last_take (L : list node_id) k x :
  k <= length L -> last (take k L) = Some x -> 0 < k /\ L !! pred k = Some x.
Proof.
  intros Hk Hl. destruct k as [|k]; [by rewrite take_0 in Hl|].
  destruct (lookup_lt_is_Some_2 L k) as [y Hy]; [lia|].
  rewrite (take_S_r L k y Hy), last_snoc in Hl. injection Hl as ->. split; [lia|done].
Qed.

Lemma last_take_of (L : list node_id) k x : L !! k = Some x -> last (take (S k) L) = Some x.
Proof. intros Hy. by rewrite (take_S_r L k x Hy), last_snoc. Qed.

Lemma no_repeat_take L k : no_repeat L -> no_repeat (take k L).
Proof.
  intros H i x Hi Hn. apply lookup_take_Some in Hi as [Hi _]. apply lookup_take_Some in Hn as [Hn _].
  by apply (H i x).
Qed.

Lemma no_repeat_snoc L s : no_repeat L -> last L <> Some s -> no_repeat (L ++ [s]).
Proof.
  intros H Hl i x Hi Hn.
  destruct (decide (i < length L)) as [Hlt|Hge].
  - rewrite lookup_app_l in Hi by done.
    destruct (decide (S i < length L)) as [Hlt2|Hge2].
    + rewrite lookup_app_l in Hn by done. by apply (H i x).
    + rewrite lookup_app_r in Hn by lia.
      assert (Hz : S i - length L = 0) by lia. rewrite Hz in Hn. simpl in Hn. injection Hn as ->.
      apply Hl. rewrite last_lookup. replace (pred (length L)) with i by lia. done.
  - rewrite lookup_ge_None_2 in Hn; [done|]. rewrite length_app. simpl. lia.
Qed.

Lemma add_exact self s w w' :
  (slot_view w self).2 <= length (slot_view w self).1 -> no_repeat (slot_view w self).1 ->
  sources_add self s w = Some w' ->
  read_prefix w' self = read_step (read_prefix w self) s /\ no_repeat (slot_view w' self).1.
Proof.
  intros Hle Hn. rewrite !read_prefix_view. unfold sources_add.
  destruct (decide (fst <$> source_slots (sources_of w self) !! source_cursor (sources_of w self) = Some s)) as [Hp|Hp].
  - intros Hr. simplify_eq.
    assert (Hv : slot_view (set_sources self {| source_slots := source_slots (sources_of w self);
                   source_cursor := S (source_cursor (sources_of w self)) |} w) self =
                 ((slot_view w self).1, S (slot_view w self).2)).
    { unfold slot_view. rewrite sources_of_set_sources. by rewrite decide_True. }
    assert (Hlk : (slot_view w self).1 !! (slot_view w self).2 = Some s).
    { unfold slot_view. cbn [fst snd]. rewrite list_lookup_fmap. done. }
    rewrite Hv. cbn [fst snd]. split; [|done].
    rewrite (take_S_r _ _ s Hlk). unfold read_step. rewrite decide_False; [done|].
    intros Hl. apply last_take in Hl as [H0 Hl]; [|done].
    apply (Hn _ _ Hl). replace (S (pred (slot_view w self).2)) with (slot_view w self).2 by lia. done.
  - destruct (decide (0 < source_cursor (sources_of w self) /\
                      fst <$> source_slots (sources_of w self) !! pred (source_cursor (sources_of w self)) = Some s)) as [Hq|Hq].
    + intros Hr. injection Hr as <-. split; [|done].
      destruct Hq as [H0 Hq]. unfold read_step. rewrite decide_True; [done|].
      assert (Hlk : (slot_view w self).1 !! pred (slot_view w self).2 = Some s).
      { unfold slot_view. cbn [fst snd]. rewrite list_lookup_fmap. done. }
      pose proof (last_take_of _ _ _ Hlk) as Hl.
      replace (S (pred (slot_view w self).2)) with (slot_view w self).2 in Hl; [done|].
      unfold slot_view. cbn [snd]. lia.
    + intros Hr. destruct (sources_end_run self w) as [w1|] eqn:E; simpl in Hr; simplify_eq.
      pose proof (slot_view_end _ _ _ self E Hle) as Hself. rewrite decide_True in Hself by done.
      assert (Hnot : last (take (slot_view w self).2 (slot_view w self).1) <> Some s).
      { intros Hl. apply last_take in Hl as [H0 Hl]; [|done]. apply Hq. split.
        - unfold slot_view in H0. exact H0.
        - unfold slot_view in Hl. cbn [fst snd] in Hl. by rewrite list_lookup_fmap in Hl. }
      rewrite slot_view_link, decide_True by done. cbn [fst snd]. rewrite Hself. cbn [fst snd].
      split.
      * rewrite take_ge by (rewrite length_app; simpl; lia). unfold read_step. by rewrite decide_False.
      * apply no_repeat_snoc; [by apply no_repeat_take|done].
Qed.

Lemma read_step_in acc x y : y ∈ read_step acc x -> y ∈ acc \/ y = x.
Proof.
  unfold read_step. destruct (decide _); [auto|].
  rewrite elem_of_app, list_elem_of_singleton. tauto.
Qed.

Lemma read_step_has acc x : x ∈ read_step acc x.
Proof.
  unfold read_step. destruct (decide _) as [Hl|_].
  - apply last_Some_elem_of in Hl. done.
  - rewrite elem_of_app, list_elem_of_singleton. auto.
Qed.

Local Tactic Notation "gsimp" :=
  cbn [with_ghost with_edges with_running with_next_id with_owners with_current set_node ghost_run_start ghost_effect_start ghost_dirty_cleared g_edges g_nodes g_ghost g_running g_current g_flags
       g_owners g_flushing gh_published gh_seen gh_unnoticed gh_window gh_reads gh_run_reads gh_nested gh_over gh_passed g_queue g_next_id from_option id].
Local Tactic Notation "gsimp" "in" "*" :=
  cbn [with_ghost with_edges with_running with_next_id with_owners with_current set_node ghost_run_start ghost_effect_start ghost_dirty_cleared g_edges g_nodes g_ghost g_running g_current g_flags
       g_owners g_flushing gh_published gh_seen gh_unnoticed gh_window gh_reads gh_run_reads gh_nested gh_over gh_passed g_queue g_next_id from_option id] in *.
Local Tactic Notation "gsimp" "in" ne_hyp_list(H) :=
  cbn [with_ghost with_edges with_running with_next_id with_owners with_current set_node ghost_run_start ghost_effect_start ghost_dirty_cleared g_edges g_nodes g_ghost g_running g_current g_flags
       g_owners g_flushing gh_published gh_seen gh_unnoticed gh_window gh_reads gh_run_reads gh_nested gh_over gh_passed g_queue g_next_id from_option id] in H.

Lemma ci_running' s c : c ∈ g_running s -> (forall c, c ∈ g_running s -> has_kind c KMemo s \/ has_kind c KEffect s) -> is_Some (g_nodes s !! c).
Proof. intros Hc H. destruct (H c Hc) as [(d&Hd&_)|(d&Hd&_)]; by exists d. Qed.

(** A tracked read of [n]: every pair holds apart from the new one. *)
Lemma extras_track s c n w d :
  run_extras s -> g_current s = Some c -> sources_add c n (g_edges s) = Some w ->
  g_nodes s !! n = Some d -> n_kind d <> KEffect ->
  let s' := ghost_read c n (read_prefix (g_edges s) c) (with_edges w s) in
  run_base s' /\ told_on (fun c' y => ~ (c' = c /\ y = n)) s' /\
  n ∈ read_prefix w c.
Proof.
  intros [Hb Ht] Hc Ha Hd Hk s'.
  assert (Hs' : s' = with_ghost {| gh_published := gh_published (g_ghost s);
      gh_seen := <[(c, n) := read_view_of d :: (if decide (n ∈ read_prefix (g_edges s) c) then default [] (gh_seen (g_ghost s) !! (c, n)) else [])]> (gh_seen (g_ghost s));
      gh_unnoticed := if decide (n ∈ g_running s) then {[c]} ∪ gh_unnoticed (g_ghost s) else gh_unnoticed (g_ghost s);
      gh_window := gh_window (g_ghost s);
      gh_reads := <[c := default [] (gh_reads (g_ghost s) !! c) ++ [n]]> (gh_reads (g_ghost s));
      gh_passed := gh_passed (g_ghost s); gh_over := gh_over (g_ghost s);
      gh_run_reads := <[c := default [] (gh_run_reads (g_ghost s) !! c) ++ [n]]> (gh_run_reads (g_ghost s));
      gh_nested := gh_nested (g_ghost s) |} (with_edges w s)).
  { subst s'. unfold ghost_read. gsimp. by rewrite Hd. }
  destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  pose proof (B14 c Hc) as Hrun.
  destruct (slot_view_add _ _ _ _ (B5 c) Ha) as (Hoth & Hle & Hsl & Hpre).
  destruct (add_exact _ _ _ _ (B5 c) (B8 c) Ha) as [Hex Hnr].
  assert (Hpre_oth : forall c', c' <> c -> read_prefix w c' = read_prefix (g_edges s) c').
  { intros c' Hne. by rewrite !read_prefix_view, Hoth. }
  rewrite Hs'. split_and!.
  - split; gsimp; try done.
    + split; gsimp.
      * eapply reachable_add; eauto.
      * intros c' e He Hcl. destruct (decide (c' = c)) as [->|Hne].
        -- pose proof (B13 c e Hc He). congruence.
        -- rewrite Hoth by done. eauto.
      * intros c' y Hy. destruct (decide (c' = c)) as [->|Hne].
        -- destruct (Hsl y Hy) as [Hy'| ->]; [eauto|].
           destruct (n_kind d) eqn:Ek; [right; by exists d|left; by exists d|done].
        -- rewrite Hoth in Hy by done. eauto.
      * intros c' e He Hek. destruct (decide (c' = c)) as [->|Hne].
        -- exfalso. destruct (B15 c Hrun) as [(e'&He'&Hk')|(e'&He'&Hk')]; simplify_eq; congruence.
        -- rewrite Hoth by done. eauto.
    + intros c'. destruct (decide (c' = c)) as [->|Hne]; [done|]. by rewrite Hoth.
    + intros c' Hnr'. destruct (decide (c' = c)) as [->|Hne]; [done|]. rewrite Hoth by done. auto.
    + intros c' e He Hde Hnr'. destruct (decide (c' = c)) as [->|Hne]; [done|]. rewrite Hoth by done. eauto.
    + intros c'. destruct (decide (c' = c)) as [->|Hne]; [done|]. by rewrite Hoth.
    + intros c' e He Hl. unfold reads_of. gsimp. destruct (decide (c' = c)) as [->|Hne].
      * rewrite lookup_insert_eq. gsimp. rewrite read_list_snoc, Hex.
        f_equal. eapply B9; [exact He|done].
      * rewrite lookup_insert_ne by done. rewrite Hpre_oth by done. eapply B9; [exact He|done].
    + intros c' e He Hcl Hde. destruct (decide (c' = c)) as [->|Hne].
      * pose proof (B13 c e Hc He). congruence.
      * rewrite Hoth by done. eauto.
    + destruct B18 as [W1 [W2 [W3 W4]]]. split_and!; [done| |done|].
      2: { intros c' Hc'. unfold run_reads_of, reads_of. gsimp. destruct (decide (c' = c)) as [->|Hne].
           - rewrite !lookup_insert_eq. gsimp. f_equal. exact (W4 c Hc').
           - rewrite !lookup_insert_ne by done. exact (W4 c' Hc'). }
      intros m l. gsimp. destruct (decide (m = c)) as [->|Hne]; [intros _; destruct (ci_running' s c Hrun B15) as [e He]; by exists e|].
      rewrite lookup_insert_ne by done. apply W2.
  - intros b c' y dc e Hr Hc' He Hin Hst Hde Hdc. gsimp in Hc' He Hin.
    unfold told_cover, in_window, resolved_set. gsimp.
    destruct (decide (c' = c)) as [->|Hne].
    + rewrite Hex in Hin. apply read_step_in in Hin as [Hin| ->]; [|by destruct Hr].
      exact (Ht b c y dc e I Hc' He Hin Hst Hde Hdc).
    + rewrite Hpre_oth in Hin by done. exact (Ht b c' y dc e I Hc' He Hin Hst Hde Hdc).
  - rewrite Hex. apply read_step_has.
Qed.

(** ** Run starts *)

Lemma slot_begin_prefix c w c' :
  read_prefix (sources_begin_run c w) c' = if decide (c' = c) then [] else read_prefix w c'.
Proof.
  rewrite !read_prefix_view, slot_view_begin. destruct (decide (c' = c)); cbn [fst snd]; [apply take_0|done].
Qed.

Lemma extras_run_begin n u d s :
  run_extras s -> g_nodes s !! n = Some d -> n_kind d = KMemo -> n_kind (u d) = KMemo ->
  n_freshness (u d) = Clean -> n_disposed (u d) = n_disposed d -> n_queued (u d) = n_queued d ->
  n_in_cleanup (u d) = n_in_cleanup d -> n_runs d <= n_runs (u d) ->
  let s' := with_running (n :: g_running s) (ghost_run_start n (with_edges (sources_begin_run n (g_edges s)) (set_node n (u d) s))) in
  run_extras s' /\ ekept s s'.
Proof.
  intros [Hb Ht] Hd Hk Hk' Hc HD HQ HC Hr s'.
  destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  assert (Hmem := B10 n d Hd Hk).
  assert (Hup : forall m k, has_kind m k s -> has_kind m k s').
  { intros m k Hm. subst s'. refine (has_kind_set n d (u d) m k s Hd _ Hm). congruence. }
  assert (Hdown : forall m k, has_kind m k s' -> has_kind m k s).
  { intros m k Hm. refine (has_kind_set_back n d (u d) m k s Hd _ Hm). congruence. }
  split; [split; [split|]|split].
  - done.
  - done.
  - subst s'. split; cbn [with_running ghost_run_start with_edges set_node with_ghost g_edges g_nodes].
    + by apply reachable_begin.
    + intros c e. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne]; [done|].
      rewrite lookup_insert_ne by done. eauto.
    + intros c y. rewrite slot_view_begin. intros Hy.
      assert (Hy' : y ∈ (slot_view (g_edges s) c).1) by (destruct (decide (c = n)); done).
      destruct (E3 c y Hy') as [K|K]; [left|right]; by apply (has_kind_set n d (u d)); [done|congruence|].
    + intros c e. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne]; cbn [fst].
      * rewrite lookup_insert_eq. intros [=<-]. congruence.
      * rewrite lookup_insert_ne by done. eauto.
  - intros m e. subst s'. gsimp. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. rewrite HQ, (proj1 Hmem). done.
    + rewrite lookup_insert_ne by done. apply B4.
  - intros c. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)); cbn [fst snd]; [lia|done].
  - intros c. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne].
    + intros Hnr. exfalso. apply Hnr. left.
    + intros Hnr. apply B6. intros Hin. apply Hnr. by right.
  - intros c e. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne].
    + intros _ _ Hnr. exfalso. apply Hnr. left.
    + rewrite lookup_insert_ne by done. intros He Hde Hnr. eapply B7; [done|done|]. intros Hin. apply Hnr. by right.
  - intros c. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|_]; apply B8.
  - intros c e. subst s'. gsimp. unfold reads_of. gsimp. rewrite slot_begin_prefix. destruct (decide (c = n)) as [->|Hne].
    + rewrite !lookup_insert_eq. done.
    + rewrite !lookup_insert_ne by done. intros He Hl. eapply B9; [exact He|done].
  - intros m e. subst s'. gsimp. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] _. by rewrite HQ, HC.
    + rewrite lookup_insert_ne by done. apply B10.
  - intros c e. subst s'. gsimp. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hcl. left.
    + rewrite lookup_insert_ne by done. intros He Hcl. right. eauto.
  - intros c e. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne]; cbn [fst].
    + rewrite lookup_insert_eq. intros [=<-] Hcl. rewrite HC, (proj2 Hmem) in Hcl. done.
    + rewrite lookup_insert_ne by done. eauto.
  - intros c e Hcur. subst s'. gsimp in *. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. rewrite HC. apply Hmem.
    + rewrite lookup_insert_ne by done. intros He. eapply B13; [done|exact He].
  - intros c Hcur. subst s'. gsimp. right. auto.
  - intros c Hin. subst s'. gsimp in Hin. apply elem_of_cons in Hin as [->|Hin].
    + left. exists (u d). split; [gsimp; apply lookup_insert_eq|done].
    + destruct (B15 c Hin) as [K|K]; [left|right]; auto.
  - intros c Hke Hin. subst s'. gsimp in Hin. apply elem_of_cons in Hin as [->|Hin].
    + exfalso. pose proof (Hdown _ _ Hke) as Hke'. destruct Hke' as (e&He&Hke'). rewrite Hd in He. injection He as <-. congruence.
    + apply (B16 c); [auto|done].
  - done.
  - destruct B18 as [W1 [W2 [W3 W4]]]. split_and!.
    4: { subst s'. intros c. unfold run_reads_of, reads_of. gsimp. destruct (decide (n ∈ g_running s)) as [Hin|Hin].
         - intros Hc'. assert (c <> n) by set_solver. rewrite lookup_insert_ne by done.
           apply W4. set_solver.
         - intros Hc'. destruct (decide (c = n)) as [->|Hne]; [by rewrite !lookup_insert_eq|].
           rewrite !lookup_insert_ne by done. apply W4. set_solver. }
    + intros m r Hw. apply Hup. eauto.
    + subst s'. gsimp. intros m l. destruct (decide (m = n)) as [->|Hne]; [intros _; rewrite lookup_insert_eq; by eexists|].
      rewrite !lookup_insert_ne by done. apply W2.
    + subst s'. gsimp. intros m e. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] _. exact Hc.
      * rewrite lookup_insert_ne by done. apply W3.
  - intros b c y dc e _ Hc' He Hin Hst Hde Hdc. subst s'. gsimp in Hc' He Hin. unfold told_cover, in_window, resolved_set. gsimp.
    rewrite slot_begin_prefix in Hin. destruct (decide (c = n)) as [->|Hne]; [by apply not_elem_of_nil in Hin|].
    rewrite lookup_insert_ne in Hc' by done.
    destruct (decide (y = n)) as [->|Hyn].
    + rewrite lookup_insert_eq in He. injection He as <-. rewrite Hc in Hst. done.
    + rewrite lookup_insert_ne in He by done.
      destruct (Ht b c y dc e I Hc' He Hin Hst Hde Hdc) as [C|[C|[C|C]]].
      * by left.
      * by right; left.
      * right; right; left. unfold resolved_set in C. destruct b; set_solver.
      * by right; right; right.
  - done.
  - intros c e He. subst s'. gsimp. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eauto.
    + rewrite lookup_insert_ne by done. eauto.
  - intros m e r. subst s'. gsimp. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hw Hl Hle. exists d, r. split_and!; [done|done|congruence|lia].
    + rewrite lookup_insert_ne by done. intros He Hw Hl Hle. exists e, r. auto.
Qed.

(** ** Edge steps *)

Lemma extras_edges self w s :
  run_extras s -> world_reachable w ->
  (forall c, c <> self -> slot_view w c = slot_view (g_edges s) c) ->
  (forall y, y ∈ (slot_view w self).1 -> y ∈ (slot_view (g_edges s) self).1) ->
  (forall y, y ∈ read_prefix w self -> y ∈ read_prefix (g_edges s) self) ->
  (slot_view w self).2 <= length (slot_view w self).1 ->
  (self ∉ g_running s -> (slot_view w self).2 = length (slot_view w self).1) ->
  (forall d, g_nodes s !! self = Some d -> n_disposed d = true -> self ∉ g_running s -> (slot_view w self).1 = []) ->
  no_repeat (slot_view w self).1 ->
  (forall d, g_nodes s !! self = Some d -> n_disposed d = false -> read_prefix w self = read_list (reads_of s self)) ->
  (forall d, g_nodes s !! self = Some d -> n_in_cleanup d = true -> (slot_view w self).2 = 0 /\
     (n_disposed d = true -> (slot_view w self).1 = [])) ->
  (forall d, g_nodes s !! self = Some d -> n_kind d = KSignal -> (slot_view w self).1 = []) ->
  run_extras (with_edges w s) /\ ekept s (with_edges w s).
Proof.
  intros [Hb Ht] Hr Hoth Hsl Hpre Hcur Hend Hdis Hnr Hrd Hcl Hsig.
  destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  assert (Hpre_oth : forall c', c' <> self -> read_prefix w c' = read_prefix (g_edges s) c').
  { intros c' Hne. by rewrite !read_prefix_view, Hoth. }
  split; [split; [split|]|].
  - done.
  - done.
  - split; gsimp.
    + done.
    + intros c e He Hc. destruct (decide (c = self)) as [->|Hne]; [eapply Hcl; eauto|]. rewrite Hoth by done. eauto.
    + intros c y Hy. destruct (decide (c = self)) as [->|Hne]; [eapply E3, Hsl; done|]. rewrite Hoth in Hy by done. eauto.
    + intros c e He Hk. destruct (decide (c = self)) as [->|Hne]; [eauto|]. rewrite Hoth by done. eauto.
  - done.
  - intros c. gsimp. destruct (decide (c = self)) as [->|Hne]; [done|]. rewrite Hoth by done. auto.
  - intros c. gsimp. destruct (decide (c = self)) as [->|Hne]; [done|]. rewrite Hoth by done. auto.
  - intros c e He Hd Hn. gsimp in *. destruct (decide (c = self)) as [->|Hne]; [eauto|]. rewrite Hoth by done. eauto.
  - intros c. gsimp. destruct (decide (c = self)) as [->|Hne]; [done|]. rewrite Hoth by done. auto.
  - intros c e He Hl. gsimp in *. unfold reads_of. gsimp. destruct (decide (c = self)) as [->|Hne]; [eauto|].
    rewrite Hpre_oth by done. eapply B9; [exact He|done].
  - done.
  - done.
  - intros c e He Hc Hd. gsimp in *. destruct (decide (c = self)) as [->|Hne]; [eapply Hcl; eauto|]. rewrite Hoth by done. eauto.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - intros b c y dc e _ Hc He Hin Hst Hde Hdc. gsimp in Hin.
    destruct (decide (c = self)) as [->|Hne].
    + exact (Ht b self y dc e I Hc He (Hpre _ Hin) Hst Hde Hdc).
    + rewrite Hpre_oth in Hin by done. exact (Ht b c y dc e I Hc He Hin Hst Hde Hdc).
  - split; [done| |].
    + intros c d Hd. exists d. done.
    + intros m d r Hd Hw Hl Hle. exists d, r. done.
Qed.

Lemma extras_end self w s :
  run_extras s -> self ∈ g_running s -> sources_end_run self (g_edges s) = Some w ->
  run_extras (with_edges w s) /\ ekept s (with_edges w s) /\ (slot_view w self).2 = length (slot_view w self).1.
Proof.
  intros H Hrun Hw. pose proof (proj1 H) as Hb.
  pose proof (b_cursor _ Hb self) as Hle.
  pose proof (fun c => slot_view_end _ _ _ c Hw Hle) as Hv.
  assert (Hself : slot_view w self = (take (slot_view (g_edges s) self).2 (slot_view (g_edges s) self).1,
                                    (slot_view (g_edges s) self).2)).
  { rewrite Hv, decide_True by done. done. }
  assert (Hlen : (slot_view w self).2 = length (slot_view w self).1).
  { rewrite Hself. cbn [fst snd]. rewrite length_take. lia. }
  assert (Hprefix : read_prefix w self = read_prefix (g_edges s) self).
  { rewrite !read_prefix_view, Hself. cbn [fst snd]. rewrite take_ge; [done|]. rewrite length_take. lia. }
  assert (Hx : run_extras (with_edges w s) /\ ekept s (with_edges w s)).
  { apply (extras_edges self w s H).
  - eapply reachable_end; [apply (b_env _ Hb)|done].
  - intros c Hc. by rewrite Hv, decide_False.
  - intros y. rewrite Hself. cbn [fst]. apply elem_take.
  - by rewrite Hprefix.
  - lia.
  - done.
  - intros _ _ _ Hnr. by destruct (Hnr Hrun).
  - rewrite Hself. cbn [fst]. by apply no_repeat_take, (b_no_repeat _ Hb).
  - intros d Hd Hl. rewrite Hprefix. eapply (b_reads _ Hb); [exact Hd|done].
  - intros d Hd Hc. rewrite Hself. cbn [fst snd]. split.
    + by eapply (me_cleanup _ (b_env _ Hb)).
    + intros Hde. erewrite (b_cleanup_disposed _ Hb); [|exact Hd|done|done]. apply take_nil.
  - intros d Hd Hk. rewrite Hself. cbn [fst]. erewrite (me_signal _ (b_env _ Hb)); [|exact Hd|done]. apply take_nil.
  }
  destruct Hx as [H1 H2]. split_and!; done.
Qed.

Lemma extras_clear self w s d :
  run_extras s -> g_nodes s !! self = Some d -> n_disposed d = true -> sources_clear self (g_edges s) = Some w ->
  run_extras (with_edges w s) /\ ekept s (with_edges w s) /\ slot_view w self = ([], 0).
Proof.
  intros H Hd Hde Hw. pose proof (proj1 H) as Hb.
  pose proof (fun c => slot_view_clear _ _ _ c Hw) as Hv.
  assert (Hself : slot_view w self = ([], 0)) by (rewrite Hv, decide_True by done; done).
  assert (Hprefix : read_prefix w self = []).
  { rewrite read_prefix_view, Hself. done. }
  assert (Hx : run_extras (with_edges w s) /\ ekept s (with_edges w s)).
  { apply (extras_edges self w s H).
  - eapply reachable_clear; [apply (b_env _ Hb)|done].
  - intros c Hc. by rewrite Hv, decide_False.
  - rewrite Hself. intros y Hy. by apply not_elem_of_nil in Hy.
  - rewrite Hprefix. intros y Hy. by apply not_elem_of_nil in Hy.
  - rewrite Hself. cbn. lia.
  - by rewrite Hself.
  - by rewrite Hself.
  - rewrite Hself. intros i x Hi. done.
  - intros d' Hd' Hl. congruence.
  - rewrite Hself. done.
  - by rewrite Hself.
  }
  destruct Hx as [H1 H2]. split_and!; done.
Qed.

Lemma extras_pop n rest s :
  run_extras s -> g_running s = n :: rest ->
  (slot_view (g_edges s) n).2 = length (slot_view (g_edges s) n).1 ->
  (forall d, g_nodes s !! n = Some d -> n_disposed d = true -> (slot_view (g_edges s) n).1 = []) ->
  (forall d, g_nodes s !! n = Some d -> n_in_cleanup d = false) ->
  (forall c, g_current s = Some c -> c ∈ rest) ->
  run_extras (with_running rest s) /\ ekept s (with_running rest s).
Proof.
  intros [Hb Ht] Hrun Hend Hdis Hcl Hcur.
  destruct Hb as [B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  rewrite Hrun in *.
  split; [split; [split|]|split; [done|intros c d Hd; by exists d|intros m d r ????; by exists d, r]].
  - done.
  - done.
  - destruct B3 as [E1 E2 E3 E4]. split; done.
  - done.
  - done.
  - intros c Hnr. destruct (decide (c = n)) as [->|Hne]; [done|]. apply B6. intros Hin.
    apply elem_of_cons in Hin as [->|Hin]; [done|]. by apply Hnr.
  - intros c e He Hd Hnr. destruct (decide (c = n)) as [->|Hne]; [eauto|]. eapply B7; [done|done|].
    intros Hin. apply elem_of_cons in Hin as [->|Hin]; [done|]. by apply Hnr.
  - done.
  - done.
  - done.
  - intros c e He Hc. gsimp. destruct (decide (c = n)) as [->|Hne].
    + pose proof (Hcl e He). congruence.
    + pose proof (B11 c e He Hc) as Hin. apply elem_of_cons in Hin as [->|Hin]; [done|done].
  - done.
  - done.
  - done.
  - intros c Hc. apply B15. by right.
  - intros c Hk Hc. apply (B16 c); [done|by right].
  - done.
  - done.
  - done.
Qed.

(** ** Announcements and resolutions *)

Lemma extras_announce n d s :
  run_extras s -> g_nodes s !! n = Some d ->
  run_extras (set_node n (node_with_unannounced true d) s) /\ ekept s (set_node n (node_with_unannounced true d) s).
Proof.
  intros [Hb Ht] Hd. split; [split|].
  - apply (base_set n (node_with_unannounced true) d); try done.
    + simpl. intros Hq. by eapply (b_queued _ Hb).
    + simpl. intros H1 H2. by eapply (b_disposed _ Hb).
    + simpl. intros Hk. by eapply (b_memo _ Hb).
    + simpl. intros Hc. split_and!.
      * by eapply (b_cleanup_running _ Hb).
      * by eapply (me_cleanup _ (b_env _ Hb)).
      * intros H1. by eapply (b_cleanup_disposed _ Hb).
      * intros Hcur. pose proof (b_cleanup_current _ Hb n d Hcur Hd). congruence.
    + simpl. intros Hk. destruct (b_window _ Hb) as (_&_&W&_). eauto.
  - apply (told_set _ n (node_with_unannounced true) d); try done.
    left. simpl. lia.
  - apply (ekept_set n (node_with_unannounced true) d); done.
Qed.

Lemma ghost_resolved_fields n s :
  g_edges (ghost_resolved n s) = g_edges s /\ g_nodes (ghost_resolved n s) = g_nodes s /\
  g_running (ghost_resolved n s) = g_running s /\ g_current (ghost_resolved n s) = g_current s /\
  g_flags (ghost_resolved n s) = g_flags s /\ g_owners (ghost_resolved n s) = g_owners s /\
  g_flushing (ghost_resolved n s) = g_flushing s /\ gh_window (g_ghost (ghost_resolved n s)) = gh_window (g_ghost s) /\
  gh_reads (g_ghost (ghost_resolved n s)) = gh_reads (g_ghost s).
Proof. done. Qed.

Lemma base_passed s s' :
  g_edges s' = g_edges s -> g_nodes s' = g_nodes s -> g_running s' = g_running s -> g_current s' = g_current s ->
  g_flags s' = g_flags s -> g_owners s' = g_owners s -> g_flushing s' = g_flushing s ->
  gh_window (g_ghost s') = gh_window (g_ghost s) -> gh_reads (g_ghost s') = gh_reads (g_ghost s) ->
  gh_run_reads (g_ghost s') = gh_run_reads (g_ghost s) -> gh_nested (g_ghost s') = gh_nested (g_ghost s) ->
  run_base s -> run_base s'.
Proof.
  intros He Hn Hru Hc Hf Ho Hfl Hw Hr Hrr Hne H. destruct H as [B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  assert (B3' := mark_env_nodes_edges _ _ He Hn B3).
  split; unfold has_kind, reads_of, run_reads_of, queued_ok in *; rewrite ?He, ?Hn, ?Hw, ?Hr, ?Hrr, ?Hne, ?Hru, ?Hc, ?Hf, ?Ho, ?Hfl; done.
Qed.

Lemma slot_holds_in p s n src e :
  src ∈ fst <$> source_slots (sources_of (g_edges s) n) -> g_nodes s !! src = Some e -> p e = true -> slot_holds p s n = true.
Proof.
  intros Hin He Hp. apply existsb_exists. exists src. split; [by apply list_elem_of_In|]. by rewrite He.
Qed.

Lemma add_if_in (x : gset node_id) n : n ∈ add_if true n x.
Proof. simpl. set_solver. Qed.

Lemma add_if_keeps b (x : gset node_id) n m : m ∈ x -> m ∈ add_if b n x.
Proof. destruct b; simpl; set_solver. Qed.

(** A walk ending in [Check], then [Clean] for [n]: an observer of a stale, unannounced source joins [gh_passed]. *)
Lemma extras_resolved n u d s :
  run_extras s -> g_nodes s !! n = Some d ->
  n_kind (u d) = n_kind d -> n_freshness (u d) = Clean -> n_disposed (u d) = n_disposed d ->
  n_queued (u d) = false -> n_in_cleanup (u d) = n_in_cleanup d -> n_runs (u d) = n_runs d ->
  run_extras (set_node n (u d) (ghost_resolved n s)) /\ ekept s (set_node n (u d) (ghost_resolved n s)).
Proof.
  intros [Hb Ht] Hd Hk Hc HD HQ HC HR.
  destruct (ghost_resolved_fields n s) as (F1&F2&F3&F4&F5&F6&F7&F8&F9).
  assert (Hb1 : run_base (ghost_resolved n s)) by (eapply base_passed; [exact F1|exact F2|exact F3|exact F4|exact F5|exact F6|exact F7|exact F8|exact F9|reflexivity|reflexivity|exact Hb]).
  assert (Hd1 : g_nodes (ghost_resolved n s) !! n = Some d) by (by rewrite F2).
  split; [split|].
  - apply (base_set n u d); try done.
    + rewrite HQ. done.
    + rewrite HD. intros H1 H2. by eapply (b_disposed _ Hb1).
    + by rewrite HD.
    + rewrite HQ, HC. intros Hk'. split; [done|]. by eapply (b_memo _ Hb1).
    + rewrite HC, HD. intros Hcl. split_and!.
      * by eapply (b_cleanup_running _ Hb1).
      * by eapply (me_cleanup _ (b_env _ Hb1)).
      * intros H1. by eapply (b_cleanup_disposed _ Hb1).
      * intros Hcur. pose proof (b_cleanup_current _ Hb1 n d Hcur Hd1). congruence.
  - intros b c src dc e _ Hc1 He Hin Hst Hde Hdc. gsimp in Hc1 He Hin.
    rewrite F1 in Hin. unfold told_cover, in_window, resolved_set. gsimp.
    destruct (decide (src = n)) as [->|Hsn].
    { rewrite lookup_insert_eq in He. injection He as <-. rewrite Hc in Hst. done. }
    rewrite lookup_insert_ne in He by done. rewrite F2 in He.
    assert (Hkeep : forall m, m ∈ resolved_set b (g_ghost s) ->
              m ∈ (if b then gh_passed (g_ghost (ghost_resolved n s)) else gh_over (g_ghost (ghost_resolved n s)))).
    { intros m Hm. destruct b; simpl; by apply add_if_keeps. }
    destruct (decide (c = n)) as [->|Hcn].
    + rewrite lookup_insert_eq in Hc1. injection Hc1 as <-.
      assert (Hslot : src ∈ fst <$> source_slots (sources_of (g_edges s) n)) by (by apply read_prefix_in).
      destruct b.
      * destruct (n_unannounced e) eqn:Eu; [by right; left|].
        right; right; left. simpl.
        rewrite (slot_holds_in (fun e => stale_live e && negb (n_unannounced e)) s n src e); [apply add_if_in|done|done|].
        unfold stale_live. rewrite Hst, Hde, Eu. done.
      * right; right; left. simpl.
        rewrite (slot_holds_in stale_live s n src e); [apply add_if_in|done|done|].
        unfold stale_live. rewrite Hst, Hde. done.
    + rewrite lookup_insert_ne in Hc1 by done. rewrite F2 in Hc1.
      destruct (Ht b c src dc e I Hc1 He Hin Hst Hde Hdc) as [C|[C|[C|C]]].
      * by left.
      * by right; left.
      * right; right; left. by apply Hkeep.
      * right; right; right. destruct C as (r&Hw&Hle). exists r. rewrite F8. done.
  - split.
    + gsimp. done.
    + intros c e He. gsimp. destruct (decide (c = n)) as [->|Hne].
      * rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eauto.
      * rewrite lookup_insert_ne by done. rewrite F2. eauto.
    + intros m e r. gsimp. rewrite F8. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hw Hl Hle. exists d, r. split_and!; [done|done|congruence|lia].
      * rewrite lookup_insert_ne by done. rewrite F2. intros He Hw Hl Hle. exists e, r. auto.
Qed.

(** ** Windows, effect starts, disposals and new nodes *)

Lemma extras_cleared n d s :
  run_extras s -> g_nodes s !! n = Some d -> n_kind d = KMemo ->
  run_extras (ghost_dirty_cleared n (n_runs d) (set_node n (node_with_freshness Clean d) s)).
Proof.
  intros [Hb Ht] Hd Hk.
  assert (Hb0 : run_base (set_node n (node_with_freshness Clean d) s)).
  { pose proof (b_memo _ Hb n d Hd Hk) as [Hmq Hmc].
    apply (base_set n (node_with_freshness Clean) d); simpl; try done; try (intros; congruence); try (intros; split; congruence).
    intros H1 H2. by eapply (b_disposed _ Hb). }
  destruct Hb0 as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  split; [split|].
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - done.
  - destruct B18 as [W1 [W2 [W3 W4]]]. split_and!; [|done|done|done].
    intros m r. gsimp. destruct (decide (m = n)) as [->|Hne].
    + intros _. exists (node_with_freshness Clean d). split; [apply lookup_insert_eq|done].
    + rewrite lookup_insert_ne by done. apply W1.
  - intros b c src dc e _ Hc He Hin Hst Hde Hdc. gsimp in Hc He Hin. unfold told_cover, in_window, resolved_set. gsimp.
    destruct (decide (src = n)) as [->|Hsn].
    { rewrite lookup_insert_eq in He. injection He as <-. done. }
    rewrite lookup_insert_ne in He by done.
    destruct (decide (c = n)) as [->|Hcn].
    + rewrite lookup_insert_eq in Hc. injection Hc as <-. right; right; right. exists (n_runs d).
      rewrite lookup_insert_eq. split; [done|]. simpl. lia.
    + rewrite lookup_insert_ne in Hc by done.
      destruct (Ht b c src dc e I Hc He Hin Hst Hde Hdc) as [C|[C|[C|C]]].
      * by left.
      * by right; left.
      * by right; right; left.
      * right; right; right. destruct C as (r&Hw&Hle). exists r. rewrite lookup_insert_ne by done. done.
Qed.

Lemma extras_effect_begin n s :
  run_base s -> told_on (fun c _ => c <> n) s -> has_kind n KEffect s -> n ∉ g_running s -> g_flushing s = true ->
  let s' := ghost_effect_start n (with_edges (sources_begin_run n (g_edges (with_running (n :: g_running s) s)))
              (with_running (n :: g_running s) s)) in
  run_extras s' /\ ekept s s'.
Proof.
  intros Hb Ht Hk Hnr Hfl s'.
  destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  split; [split; [split|]|split].
  - done.
  - done.
  - subst s'. split; gsimp.
    + by apply reachable_begin.
    + intros c e. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne]; [done|]. eauto.
    + intros c y. rewrite slot_view_begin. intros Hy.
      apply (E3 c y). destruct (decide (c = n)); done.
    + intros c e. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne]; cbn [fst]; eauto.
  - done.
  - intros c. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)); cbn [fst snd]; [lia|done].
  - intros c. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne].
    + intros Hn. exfalso. apply Hn. left.
    + intros Hn. apply B6. intros Hin. apply Hn. by right.
  - intros c e. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne].
    + intros _ _ Hn. exfalso. apply Hn. left.
    + intros He Hde Hn. eapply B7; [done|done|]. intros Hin. apply Hn. by right.
  - intros c. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|_]; apply B8.
  - intros c e. subst s'. gsimp. unfold reads_of. gsimp. rewrite slot_begin_prefix. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. done.
    + rewrite lookup_insert_ne by done. intros He Hl. eapply B9; [exact He|done].
  - done.
  - intros c e He Hc. subst s'. gsimp. right. eauto.
  - intros c e. subst s'. gsimp. rewrite slot_view_begin. destruct (decide (c = n)) as [->|Hne]; cbn [fst]; eauto.
  - done.
  - intros c Hc. subst s'. gsimp. right. auto.
  - intros c Hin. subst s'. gsimp in Hin. apply elem_of_cons in Hin as [->|Hin]; [right; exact Hk|].
    destruct (B15 c Hin) as [K|K]; [left; exact K|right; exact K].
  - done.
  - done.
  - destruct B18 as [W1 [W2 [W3 W4]]]. split_and!; [done| |done|].
    2: { subst s'. intros c. unfold run_reads_of, reads_of. gsimp. intros Hc'.
         destruct (decide (c = n)) as [->|Hne]; [by rewrite !lookup_insert_eq|].
         rewrite !lookup_insert_ne by done. apply W4. set_solver. }
    subst s'. gsimp. intros m l. destruct (decide (m = n)) as [->|Hne].
    + intros _. destruct Hk as (d&Hd&_). by exists d.
    + rewrite lookup_insert_ne by done. apply W2.
  - intros b c y dc e _ Hc He Hin Hst Hde Hdc. subst s'. gsimp in Hc He Hin. unfold told_cover, in_window, resolved_set. gsimp.
    rewrite slot_begin_prefix in Hin. destruct (decide (c = n)) as [->|Hne]; [by apply not_elem_of_nil in Hin|].
    destruct (Ht b c y dc e Hne Hc He Hin Hst Hde Hdc) as [C|[C|[C|C]]].
    + by left.
    + by right; left.
    + right; right; left. unfold resolved_set in C. destruct b; set_solver.
    + by right; right; right.
  - done.
  - intros c d Hd. exists d. done.
  - intros m d r Hd Hw Hl Hle. exists d, r. done.
Qed.

Definition lists_ok (x : owner_state) : Prop :=
  length (o_cleanups x) = length (o_cleanup_envs x) /\ length (o_cleanups x) = length (o_cleanup_order x).

Lemma extras_owners O s :
  run_extras s -> (forall o x, O !! o = Some x -> lists_ok x) -> run_extras (with_owners O s).
Proof.
  intros [Hb Ht] Ho. destruct Hb as [B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  destruct B3 as [E1 E2 E3 E4].
  exact (conj (Build_run_base (with_owners O s) B1 B2 (Build_mark_env (with_owners O s) E1 E2 E3 E4) B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 Ho B18) Ht).
Qed.

Lemma ekept_owners O s : ekept s (with_owners O s).
Proof. split; [done|intros c d Hd; by exists d|intros m d r ????; by exists d, r]. Qed.

Lemma lists_insert s o x :
  run_base s -> lists_ok x -> forall o' y, <[o := x]> (g_owners s) !! o' = Some y -> lists_ok y.
Proof.
  intros Hb Hx o' y. destruct (decide (o' = o)) as [->|Hne].
  - rewrite lookup_insert_eq. by intros [=<-].
  - rewrite lookup_insert_ne by done. apply (b_lists _ Hb).
Qed.

Lemma extras_dispose n d w s O :
  run_extras s -> g_nodes s !! n = Some d -> sources_clear n (g_edges s) = Some w ->
  (forall o x, O !! o = Some x -> lists_ok x) ->
  let s' := with_edges w (with_owners O (set_node n (node_with_link None (node_with_disposed true d)) s)) in
  run_extras s' /\ ekept s s'.
Proof.
  intros [Hb Ht] Hd Hw Ho s'.
  pose proof (fun c => slot_view_clear _ _ _ c Hw) as Hv.
  assert (Hself : slot_view w n = ([], 0)) by (rewrite Hv, decide_True by done; done).
  assert (Hoth : forall c, c <> n -> slot_view w c = slot_view (g_edges s) c) by (intros c Hc; by rewrite Hv, decide_False).
  assert (Hpre_oth : forall c', c' <> n -> read_prefix w c' = read_prefix (g_edges s) c').
  { intros c' Hne. by rewrite !read_prefix_view, Hoth. }
  set (u := node_with_link None (node_with_disposed true d)) in s'.
  assert (Hup : forall m k, has_kind m k s -> has_kind m k s').
  { intros m k Hm. subst s'. exact (has_kind_set n d u m k s Hd eq_refl Hm). }
  assert (Hdown : forall m k, has_kind m k s' -> has_kind m k s).
  { intros m k Hm. exact (has_kind_set_back n d u m k s Hd eq_refl Hm). }
  destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  split; [split; [split|]|split].
  - done.
  - done.
  - split; subst s'; cbn [with_edges with_owners set_node g_edges g_nodes].
    + eapply reachable_clear; eauto.
    + intros c e. destruct (decide (c = n)) as [->|Hne]; [by rewrite Hself|].
      rewrite Hoth by done. rewrite lookup_insert_ne by done. eauto.
    + intros c y. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; intros Hy; by apply not_elem_of_nil in Hy|].
      rewrite Hoth by done. intros Hy. destruct (E3 c y Hy) as [K|K]; [left|right]; exact (has_kind_set n d u _ _ s Hd eq_refl K).
    + intros c e. destruct (decide (c = n)) as [->|Hne]; [by rewrite Hself|].
      rewrite Hoth by done. rewrite lookup_insert_ne by done. eauto.
  - intros m e. subst s'. cbn [with_edges with_owners set_node g_nodes]. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. simpl. apply (B4 n d Hd).
    + rewrite lookup_insert_ne by done. apply B4.
  - intros c. subst s'. cbn [with_edges g_edges]. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; cbn; lia|]. rewrite Hoth by done. auto.
  - intros c. subst s'. cbn [with_edges g_edges]. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; done|]. rewrite Hoth by done. auto.
  - intros c e. subst s'. cbn [with_edges with_owners set_node g_edges g_nodes g_running]. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; done|].
    rewrite Hoth by done. rewrite lookup_insert_ne by done. eauto.
  - intros c. subst s'. cbn [with_edges g_edges]. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; intros i x Hi; done|]. rewrite Hoth by done. auto.
  - intros c e. subst s'. cbn [with_edges with_owners set_node g_edges g_nodes]. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. done.
    + rewrite lookup_insert_ne by done. rewrite Hpre_oth by done. intros He Hl. unfold reads_of. cbn [with_edges with_owners set_node g_ghost]. eapply B9; [exact He|done].
  - intros m e. subst s'. cbn [with_edges with_owners set_node g_nodes]. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. simpl. apply (B10 n d Hd).
    + rewrite lookup_insert_ne by done. apply B10.
  - intros c e. subst s'. cbn [with_edges with_owners set_node g_nodes g_running]. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. simpl. apply (B11 n d Hd).
    + rewrite lookup_insert_ne by done. apply B11.
  - intros c e. subst s'. cbn [with_edges with_owners set_node g_edges g_nodes]. destruct (decide (c = n)) as [->|Hne]; [rewrite Hself; done|].
    rewrite Hoth by done. rewrite lookup_insert_ne by done. eauto.
  - intros c e Hcur. subst s'. cbn [with_edges with_owners set_node g_current g_nodes] in *. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. simpl. apply (B13 n d Hcur Hd).
    + rewrite lookup_insert_ne by done. intros He. exact (B13 c e Hcur He).
  - done.
  - intros c Hc. destruct (B15 c Hc) as [K|K]; [left|right]; auto.
  - intros c Hk Hc. apply (B16 c); [auto|done].
  - done.
  - destruct B18 as [W1 [W2 [W3 W4]]]. split_and!; [| | |exact W4].
    + intros m r Hr. apply Hup. eauto.
    + subst s'. cbn [with_edges with_owners set_node g_ghost g_nodes]. intros m l Hl. destruct (W2 m l Hl) as [e He].
      destruct (decide (m = n)) as [->|Hne]; [rewrite lookup_insert_eq; by eexists|rewrite lookup_insert_ne by done; by exists e].
    + subst s'. cbn [with_edges with_owners set_node g_nodes]. intros m e. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hk. simpl. apply (W3 n d Hd). done.
      * rewrite lookup_insert_ne by done. apply W3.
  - intros b c y dc e _ Hc He Hin Hst Hde Hdc. subst s'.
    cbn [with_edges with_owners set_node g_nodes g_edges] in Hc, He, Hin.
    destruct (decide (y = n)) as [->|Hyn]; [rewrite lookup_insert_eq in He; injection He as <-; done|].
    rewrite lookup_insert_ne in He by done.
    destruct (decide (c = n)) as [->|Hcn]; [rewrite lookup_insert_eq in Hc; injection Hc as <-; done|].
    rewrite lookup_insert_ne in Hc by done. rewrite Hpre_oth in Hin by done.
    exact (Ht b c y dc e I Hc He Hin Hst Hde Hdc).
  - done.
  - intros c e He. subst s'. cbn [with_edges with_owners set_node g_nodes]. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eauto.
    + rewrite lookup_insert_ne by done. eauto.
  - intros m e r. subst s'. cbn [with_edges with_owners set_node g_nodes g_ghost]. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] _ Hl. done.
    + rewrite lookup_insert_ne by done. intros He Hw1 Hl Hle. exists e, r. done.
Qed.

Lemma extras_new s (n : node_id) D :
  check_invariant s -> run_extras s -> n = S (g_next_id s) -> n_queued D = false -> n_in_cleanup D = false ->
  n_disposed D = false -> (n_kind D = KSignal -> n_freshness D = Clean) ->
  let s' := ghost_publish n (set_node n D (with_next_id n s)) in
  run_extras s' /\ ekept s s'.
Proof.
  intros Hi [Hb Ht] Hn HQ HC HD HS s'.
  assert (Habs : g_nodes s !! n = None).
  { destruct (g_nodes s !! n) eqn:E; [|done].
    pose proof (ci_next _ _ _ Hi n (mk_is_Some _ _ E)). lia. }
  assert (Hne : forall m, is_Some (g_nodes s !! m) -> m <> n).
  { intros m Hm ->. rewrite Habs in Hm. by destruct Hm. }
  assert (Hkind : forall m k, has_kind m k s -> m <> n).
  { intros m k (e & He & _). apply Hne. eauto. }
  assert (Hs' : s' = with_ghost {| gh_published := <[n := read_view_of D]> (gh_published (g_ghost s)); gh_seen := gh_seen (g_ghost s);
                  gh_unnoticed := gh_unnoticed (g_ghost s); gh_window := gh_window (g_ghost s); gh_reads := gh_reads (g_ghost s);
                  gh_passed := gh_passed (g_ghost s); gh_over := gh_over (g_ghost s);
                  gh_run_reads := gh_run_reads (g_ghost s); gh_nested := gh_nested (g_ghost s) |} (set_node n D (with_next_id n s))).
  { subst s'. unfold ghost_publish. simpl. by rewrite lookup_insert_eq. }
  clearbody s'. subst s'.
  destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  assert (Hup : forall m k, has_kind m k s -> has_kind m k (with_ghost {| gh_published := <[n := read_view_of D]> (gh_published (g_ghost s)); gh_seen := gh_seen (g_ghost s);
                  gh_unnoticed := gh_unnoticed (g_ghost s); gh_window := gh_window (g_ghost s); gh_reads := gh_reads (g_ghost s);
                  gh_passed := gh_passed (g_ghost s); gh_over := gh_over (g_ghost s);
                  gh_run_reads := gh_run_reads (g_ghost s); gh_nested := gh_nested (g_ghost s) |} (set_node n D (with_next_id n s)))).
  { intros m k Hm. pose proof (Hkind _ _ Hm) as Hmn. destruct Hm as (e & He & Hek).
    exists e. simpl. rewrite lookup_insert_ne by done. auto. }
  assert (Hnoslots : (slot_view (g_edges s) n).1 = []).
  { destruct (slot_view (g_edges s) n).1 as [|y l] eqn:E; [done|].
    destruct (ci_slots _ _ _ Hi n y) as [Hn0 _]; [rewrite E; left|]. by destruct (Hne n Hn0). }
  assert (Hnoprefix : read_prefix (g_edges s) n = []).
  { rewrite read_prefix_view, Hnoslots. apply take_nil. }
  destruct B18 as [W1 [W2 [W3 W4]]].
  assert (Hnr : gh_reads (g_ghost s) !! n = None).
  { destruct (gh_reads (g_ghost s) !! n) as [l|] eqn:E; [|done]. destruct (Hne n (W2 n l E)). done. }
  assert (Hnw : gh_window (g_ghost s) !! n = None).
  { destruct (gh_window (g_ghost s) !! n) as [r|] eqn:E; [|done]. destruct (Hkind n _ (W1 n r E)). done. }
  assert (Hrun : n ∉ g_running s).
  { intros Hin. exact (Hne n (ci_running _ _ _ Hi n Hin) eq_refl). }
  split; [split; [split|]|split].
  - done.
  - done.
  - split; gsimp.
    + done.
    + intros c e. destruct (decide (c = n)) as [->|Hcn].
      * intros _ _. pose proof (B5 n) as Hc. rewrite Hnoslots in Hc. cbn [length] in Hc. lia.
      * rewrite lookup_insert_ne by done. eauto.
    + intros c y Hy. destruct (E3 c y Hy) as [K|K]; [left|right]; auto.
    + intros c e. destruct (decide (c = n)) as [->|Hcn]; [intros _ _; done|]. rewrite lookup_insert_ne by done. eauto.
  - intros m e. gsimp. destruct (decide (m = n)) as [->|Hmn].
    + rewrite lookup_insert_eq. intros [=<-]. congruence.
    + rewrite lookup_insert_ne by done. apply B4.
  - done.
  - done.
  - intros c e. gsimp. destruct (decide (c = n)) as [->|Hcn]; [intros _ _ _; done|]. rewrite lookup_insert_ne by done. eauto.
  - done.
  - intros c e. gsimp. unfold reads_of. gsimp. destruct (decide (c = n)) as [->|Hcn].
    + rewrite Hnr. intros _ _. done.
    + rewrite lookup_insert_ne by done. intros He Hl. eapply B9; [exact He|done].
  - intros m e. gsimp. destruct (decide (m = n)) as [->|Hmn].
    + rewrite lookup_insert_eq. intros [=<-] _. done.
    + rewrite lookup_insert_ne by done. apply B10.
  - intros c e. gsimp. destruct (decide (c = n)) as [->|Hcn].
    + rewrite lookup_insert_eq. intros [=<-] Hc. congruence.
    + rewrite lookup_insert_ne by done. apply B11.
  - intros c e. gsimp. destruct (decide (c = n)) as [->|Hcn].
    + rewrite lookup_insert_eq. intros [=<-] Hc. congruence.
    + rewrite lookup_insert_ne by done. apply B12.
  - intros c e Hcur. gsimp in Hcur. gsimp. destruct (decide (c = n)) as [->|Hcn].
    + rewrite lookup_insert_eq. intros [=<-]. done.
    + rewrite lookup_insert_ne by done. intros He. exact (B13 c e Hcur He).
  - done.
  - intros c Hc. destruct (B15 c Hc) as [K|K]; [left|right]; auto.
  - intros c (e&He&Hk) Hc. gsimp in He Hc. destruct (decide (c = n)) as [->|Hcn]; [done|].
    rewrite lookup_insert_ne in He by done. apply (B16 c); [by exists e|done].
  - done.
  - split_and!; [| | |exact W4].
    + intros m r Hr. apply Hup. eauto.
    + gsimp. intros m l Hl. destruct (W2 m l Hl) as [e He].
      destruct (decide (m = n)) as [->|Hmn]; [by rewrite lookup_insert_eq; eexists|rewrite lookup_insert_ne by done; by exists e].
    + gsimp. intros m e. destruct (decide (m = n)) as [->|Hmn].
      * rewrite lookup_insert_eq. intros [=<-]. done.
      * rewrite lookup_insert_ne by done. apply W3.
  - intros b c y dc e _ Hc He Hin Hst Hde Hdc. gsimp in Hc He Hin. unfold told_cover, in_window, resolved_set. gsimp.
    destruct (decide (c = n)) as [->|Hcn]; [rewrite Hnoprefix in Hin; by apply not_elem_of_nil in Hin|].
    rewrite lookup_insert_ne in Hc by done.
    destruct (decide (y = n)) as [->|Hyn].
    + exfalso. destruct (E3 c n (read_prefix_in' _ _ _ Hin)) as [K|K]; by apply (Hkind n _ K).
    + rewrite lookup_insert_ne in He by done. exact (Ht b c y dc e I Hc He Hin Hst Hde Hdc).
  - done.
  - intros c e He. exists e. gsimp. assert (c <> n) by (apply Hne; by exists e). rewrite lookup_insert_ne by done. done.
  - intros m e r. gsimp. destruct (decide (m = n)) as [->|Hmn].
    + intros _ Hw. rewrite Hnw in Hw. done.
    + rewrite lookup_insert_ne by done. intros He Hw Hl Hle. by exists e, r.
Qed.

(** ** Triples over the monad *)

(**md
`check_invariant` and `run_extras` hold.
*)
Definition run_core (s : state) : Prop := check_invariant s /\ run_extras s.
(**md
`frame` and `ekept` relate `s` to `s'`.
*)
Definition run_kept (s s' : state) : Prop := frame s s' /\ ekept s s'.

Lemma kept_refl s : run_kept s s.
Proof. split; [apply frame_refl|apply ekept_refl]. Qed.

Lemma kept_trans s1 s2 s3 : run_kept s1 s2 -> run_kept s2 s3 -> run_kept s1 s3.
Proof. intros [H1 H2] [J1 J2]. split; [by eapply frame_trans|by eapply ekept_trans]. Qed.

(**md
From a state satisfying `run_core` and `P`, a completed step `m` ends in a state satisfying `run_core`,
related to the start by `run_kept`.

- [P:] the premise on the starting state.
- [m:] the step.
*)
Definition ok2 {A} (P : state -> Prop) (m : M A) : Prop :=
  forall s s' a, run_core s -> P s -> m s = Some (s', a) -> run_core s' /\ run_kept s s'.

(**md
`P` survives every `run_kept` step.
*)
Class Stable2 (P : state -> Prop) := stable2 : forall s s', run_kept s s' -> P s -> P s'.

Global Instance stable2_true : Stable2 (fun _ => True).
Proof. by intros ???. Qed.

Global Instance stable2_kind n k : Stable2 (has_kind n k).
Proof. intros s s' [(_ & _ & H & _) _] Hk. auto. Qed.

(** A flush's loop: no body running, the flush flag set, and only memos on the stack. *)
Definition flush_ctx (s : state) : Prop :=
  g_current s = None /\ g_flushing s = true /\ forall c, c ∈ g_running s -> has_kind c KMemo s.

Global Instance stable2_flush : Stable2 flush_ctx.
Proof.
  intros s s' [(Hr & Hc & Hk & _) [Hf _ _]] (H1 & H2 & H3). split_and!; [congruence|congruence|].
  intros c Hin. rewrite Hr in Hin. auto.
Qed.

(**md
Node `n` is an effect and a flush loop is in progress.
*)
Definition effect_ctx (n : node_id) (s : state) : Prop := has_kind n KEffect s /\ flush_ctx s.

Global Instance stable2_effect n : Stable2 (effect_ctx n).
Proof.
  intros s s' Hk [H1 H2]. split; [exact (stable2 _ _ Hk H1)|exact (stable2 _ _ Hk H2)].
Qed.

(** An effect's body: the effect on the stack, outside its cleanups. *)
Definition body_ctx (n : node_id) (s : state) : Prop :=
  n ∈ g_running s /\ has_kind n KEffect s /\ forall d, g_nodes s !! n = Some d -> n_in_cleanup d = false.

Global Instance stable2_body n : Stable2 (body_ctx n).
Proof.
  intros s s' [(Hr & Hc & Hk & _) [_ Hcl _]] (H1 & H2 & H3). split_and!.
  - by rewrite Hr.
  - auto.
  - intros d' Hd'. destruct H2 as (d&Hd&_). destruct (Hcl n d Hd) as (e&He&Hs). rewrite Hd' in He. injection He as <-.
    rewrite Hs. auto.
Qed.

Lemma ok2_of {A} (Q P : state -> Prop) (m : M A) :
  (forall s, P s -> Q s) -> ok_under Q m ->
  (forall s s' a, check_invariant s -> run_extras s -> P s -> m s = Some (s', a) -> run_extras s' /\ ekept s s') ->
  ok2 P m.
Proof.
  intros HPQ Hq He s s' a [Hi Hx] HP Hm.
  destruct (Hq _ _ _ Hi (HPQ _ HP) Hm) as [Hi' F].
  destruct (He _ _ _ Hi Hx HP Hm) as [Hx' E]. split; split; done.
Qed.

Lemma ok2_any {A} P (m : M A) : ok2 (fun _ => True) m -> ok2 P m.
Proof. intros H s s' a Hc _ Hm. by eapply H. Qed.

Lemma ok2_weaken {A} (P Q : state -> Prop) (m : M A) : (forall s, Q s -> P s) -> ok2 P m -> ok2 Q m.
Proof. intros HPQ H s s' a Hc HQ Hm. eapply H; eauto. Qed.

Lemma ok2_bind {A B} P `{!Stable2 P} (m : M A) (k : A -> M B) :
  ok2 P m -> (forall a, ok2 P (k a)) -> ok2 P (m_bind m k).
Proof.
  intros Hm Hk s s' b Hc HP Hr. unfold m_bind in Hr.
  destruct (m s) as [[s1 a]|] eqn:E; [|done].
  destruct (Hm _ _ _ Hc HP E) as [Hc1 K1].
  destruct (Hk a _ _ _ Hc1 (stable2 _ _ K1 HP) Hr) as [Hc2 K2].
  split; [done|]. by eapply kept_trans.
Qed.

Lemma ok2_same {A} (Q P : state -> Prop) (m : M A) :
  (forall s, P s -> Q s) -> ok_under Q m ->
  (forall s s' a, m s = Some (s', a) -> fields_same s s') -> ok2 P m.
Proof.
  intros HPQ Hq Hf. apply (ok2_of Q); [done|done|].
  intros s s' a _ Hx _ Hm. pose proof (Hf _ _ _ Hm). split; [by eapply extras_same|by apply ekept_same].
Qed.

Local Ltac same_done := intros ??? ?; simplify_eq; split; reflexivity.

Lemma ok2_return {A} P (a : A) : ok2 P (m_return a).
Proof. apply (ok2_same P); [done|apply ok_return|]. unfold m_return. same_done. Qed.

Lemma ok2_stuck {A} P : ok2 P (@stuck A).
Proof. intros s s' b _ _ Hm. done. Qed.

Lemma ok2_get P : ok2 P m_get.
Proof. apply (ok2_same P); [done|apply ok_get|]. unfold m_get. same_done. Qed.

Lemma ok2_get_node P n : ok2 P (get_node n).
Proof.
  apply (ok2_same P); [done|apply ok_get_node|]. intros s s' a Hm. unfold get_node in Hm.
  destruct (g_nodes s !! n); simplify_eq. split; reflexivity.
Qed.

Lemma ok2_get_node_at {B} P n (k : node -> M B) :
  (forall d, ok2 (fun s => P s /\ g_nodes s !! n = Some d) (k d)) -> ok2 P (m_bind (get_node n) k).
Proof.
  intros H s s' b Hc HP Hm. unfold m_bind, get_node in Hm.
  destruct (g_nodes s !! n) as [d|] eqn:E; [|done]. eapply (H d); eauto.
Qed.

Lemma ok2_get_owner_at {B} P o (k : owner_state -> M B) :
  (forall x, lists_ok x -> ok2 P (k x)) -> ok2 P (m_bind (get_owner o) k).
Proof.
  intros H s s' b Hc HP Hm. unfold m_bind, get_owner in Hm.
  destruct (g_owners s !! o) as [x|] eqn:E; [|done].
  eapply (H x); [exact (b_lists _ (proj1 (proj2 Hc)) o x E)|done|done|done].
Qed.

Lemma ok2_put_owner P o x : lists_ok x -> ok2 P (put_owner o x).
Proof.
  intros Hx. apply (ok2_of P); [done|apply ok_put_owner|].
  intros s s' a _ He _ Hm. unfold put_owner, m_modify in Hm. simplify_eq. split.
  - apply extras_owners; [done|]. apply lists_insert; [apply He|done].
  - apply ekept_owners.
Qed.

Lemma ok2_emit P e : ok2 P (emit e).
Proof. apply (ok2_same P); [done|apply ok_emit|]. unfold emit, m_modify. same_done. Qed.

Lemma ok2_on_value P e x (k : val -> M outcome) :
  (forall v, ok2 P (k v)) -> ok2 P (on_value e x k).
Proof.
  intros Hk s s' b Hc HP Hm. unfold on_value in Hm.
  destruct (eval_expr e (env_lookup e prev_var) x) as [[v|y]|]; simplify_eq.
  - by eapply Hk.
  - split; [done|apply kept_refl].
Qed.

Lemma ok2_on_node P e x (k : node_id -> M outcome) :
  (forall t, ok2 P (k t)) -> ok2 P (on_node e x k).
Proof.
  intros Hk. apply ok2_on_value. intros v. destruct (decide (0 <= v)%Z); [apply Hk|apply ok2_stuck].
Qed.

Lemma ok2_note P n o : ok2 P (note_host_read n o).
Proof.
  intros s s' b Hc HP Hm. unfold note_host_read in Hm.
  destruct (g_current s); [simplify_eq; split; [done|apply kept_refl]|].
  destruct (g_current_owner s) as [r|]; [|simplify_eq; split; [done|apply kept_refl]].
  destruct (decide (r = root_owner_id)); [|simplify_eq; split; [done|apply kept_refl]].
  by eapply ok2_emit.
Qed.

Lemma ok2_log_label P : ok2 P log_label.
Proof.
  intros s s' a Hc _ Hm. unfold log_label in Hm.
  destruct (g_current s), (g_current_owner s); simplify_eq; split; try done; apply kept_refl.
Qed.

Lemma ok2_seq {A B} P (m : M A) (k : A -> M B) :
  ok2 P m -> (forall a, ok2 (fun _ => True) (k a)) -> ok2 P (m_bind m k).
Proof.
  intros Hm Hk s s' b Hc HP Hr. unfold m_bind in Hr.
  destruct (m s) as [[s1 a]|] eqn:E; [|done].
  destruct (Hm _ _ _ Hc HP E) as [Hc1 K1].
  destruct (Hk a _ _ _ Hc1 I Hr) as [Hc2 K2].
  split; [done|]. by eapply kept_trans.
Qed.

Lemma ok2_get_at {B} P (k : state -> M B) :
  (forall s0, ok2 (fun s => P s /\ s = s0) (k s0)) -> ok2 P (m_bind m_get k).
Proof. intros H s s' b Hc HP Hm. rewrite bind_get in Hm. eapply (H s); eauto. Qed.

Lemma ok2_modify_same P (g : state -> state) :
  (forall s, irrelevant s (g s)) -> (forall s, fields_same s (g s)) -> ok2 P (m_modify g).
Proof.
  intros Hg Hf. apply (ok2_same P); [done|by apply ok_modify_irrelevant|].
  unfold m_modify. intros s s' a Hm. simplify_eq. apply Hf.
Qed.

Local Ltac ok2_mod := apply ok2_modify_same; intros ?; [unfold irrelevant; simpl; split_and!; reflexivity|split; reflexivity].

Lemma ok2_modify_neutral P n u : (forall d, node_step u d) -> (forall d, neutral u d) -> ok2 P (modify_node n u).
Proof.
  intros Hu Hn. apply (ok2_of P); [done|by apply ok_modify_node|].
  intros s s' a _ He _ Hm. unfold modify_node in Hm. destruct (g_nodes s !! n) as [d|] eqn:Hd; simplify_eq.
  by apply extras_neutral.
Qed.

Local Ltac neu := intros ?; unfold neutral; simpl; split_and!; first [reflexivity|done].
Local Ltac ms :=
  unfold memo_step; split_and!; try done; intros; simpl in *;
  first [lia | done | congruence | by right | by left].

Local Ltac ns :=
  unfold node_step; split_and!; try done;
  try (intros; simpl in *; first [lia | done | congruence]);
  try (intros _; ms);
  try (intros; simpl in *; split_and!; done).

Local Ltac nst := intros ?; ns.

Lemma ok2_mark P (g : state -> option state) :
  (forall s s', g s = Some s' -> exists f n r, any_mark f n r s s') -> ok2 P (m_step g).
Proof.
  intros Hg. apply (ok2_of P); [done|apply ok_mark; intros s s' H; destruct (Hg _ _ H) as (f&n&r&Hm); by eapply any_mark_rtc|].
  intros s s' a _ He _ Hm. unfold m_step in Hm. destruct (g s) as [s1|] eqn:E; simplify_eq.
  destruct (Hg _ _ E) as (f&n&r&Ha). by eapply extras_marks.
Qed.

Lemma ok2_mark_dirty P f n : ok2 P (m_step (mark_dirty f n)).
Proof. apply ok2_mark. intros s s' H. exists f, n, None. by right; left. Qed.



Lemma ok2_new_node {B} P `{!Stable2 P} (D : node_id -> node) (k : node_id -> M B) :
  (forall n, n_unannounced (D n) = false) -> (forall n, n_queued (D n) = false) -> (forall n, n_in_cleanup (D n) = false) ->
  (forall n, n_disposed (D n) = false) -> (forall n, n_kind (D n) = KSignal -> n_freshness (D n) = Clean) ->
  (forall n, ok2 P (k n)) ->
  ok2 P (m_bind next_id (fun n => m_bind (add_node n (D n)) (fun _ => k n))).
Proof.
  intros Hu Hq Hcl Hdi Hsg Hk s s' b [Hi Hx] HP Hm. unfold m_bind, next_id, add_node, m_modify in Hm. simpl in Hm.
  destruct (invariant_new_node s (S (g_next_id s)) (D (S (g_next_id s))) Hi eq_refl (Hu _)) as [Hi1 F1].
  destruct (extras_new s (S (g_next_id s)) (D (S (g_next_id s))) Hi Hx eq_refl (Hq _) (Hcl _) (Hdi _) (Hsg _)) as [Hx1 E1].
  assert (K1 : run_kept s (ghost_publish (S (g_next_id s)) (set_node (S (g_next_id s)) (D (S (g_next_id s))) (with_next_id (S (g_next_id s)) s)))) by (split; done).
  destruct (Hk _ _ _ _ (conj Hi1 Hx1) (stable2 _ _ K1 HP) Hm) as [Hc2 K2].
  split; [done|]. by eapply kept_trans.
Qed.

Lemma ok2_current_owner P : ok2 P current_owner.
Proof.
  apply (ok2_of P); [done|apply ok_current_owner|].
  intros s s' a _ Hx _ Hm. unfold current_owner in Hm.
  destruct (g_current_owner s) as [o|]; [simplify_eq; split; [done|apply ekept_refl]|].
  destruct (g_current s) as [h|] eqn:Ec; [|done].
  destruct (g_nodes s !! h) as [d|] eqn:Hd; [|done].
  destruct (n_scope d) as [o|].
  { simplify_eq. split.
    - eapply extras_same; [|exact Hx]. split; reflexivity.
    - apply ekept_same. split; reflexivity. }
  destruct (match n_kind d, n_mode d with KMemo, Pure => true | _, _ => false end).
  - simplify_eq. apply (extras_neutral h (node_with_violated true) d s Hx Hd). unfold neutral; simpl; split_and!; first [reflexivity|done].
  - simplify_eq.
    set (o := stdpp.base.fresh (dom (g_owners s))).
    assert (Hx1 : run_extras (with_owners (<[o := new_owner h (Some root_owner_id) (n_disposed d)]> (g_owners s)) s)).
    { apply extras_owners; [done|]. apply lists_insert; [apply Hx|]. unfold lists_ok. simpl. done. }
    set (s1 := with_owners _ s) in Hx1.
    assert (Hd1 : g_nodes s1 !! h = Some d) by done.
    destruct (extras_neutral h (node_with_scope (Some o)) d s1 Hx1 Hd1 ltac:(unfold neutral; simpl; split_and!; first [reflexivity|done])) as [Hx2 E2].
    split.
    + eapply extras_same; [|exact Hx2]. split; reflexivity.
    + eapply ekept_trans; [apply ekept_owners|]. eapply ekept_trans; [exact E2|]. apply ekept_same. split; reflexivity.
Qed.

(** The contexts of a body. *)
Lemma base_current o s :
  run_base s -> (forall c, o = Some c -> c ∈ g_running s /\ forall d, g_nodes s !! c = Some d -> n_in_cleanup d = false) ->
  run_base (with_current o s).
Proof.
  intros H Ho. destruct H as [B1 B2 B3 B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18].
  destruct B3 as [E1 E2 E3 E4].
  refine (Build_run_base (with_current o s) B1 B2 (Build_mark_env (with_current o s) E1 E2 E3 E4) B4 B5 B6 B7 B8 B9 B10 B11 B12 _ _ B15 B16 B17 B18).
  - intros c d Hc Hd. destruct (Ho c Hc) as [_ H]. exact (H d Hd).
  - intros c Hc. by destruct (Ho c Hc).
Qed.

Lemma extras_current o s :
  run_extras s -> (forall c, o = Some c -> c ∈ g_running s /\ forall d, g_nodes s !! c = Some d -> n_in_cleanup d = false) ->
  run_extras (with_current o s).
Proof. intros [H1 H2] Ho. split; [by apply base_current|done]. Qed.



Lemma ekept_nodes s s' :
  g_flushing s' = g_flushing s -> g_nodes s' = g_nodes s -> gh_window (g_ghost s') = gh_window (g_ghost s) -> ekept s s'.
Proof.
  intros Hf Hn Hw. split; [done| |].
  - intros c d Hd. exists d. by rewrite Hn.
  - intros m d r. rewrite Hn, Hw. intros. by exists d, r.
Qed.

(** A body under another current computation: [s0] enters and [s2] restores. *)
Lemma ctx_common o s s0 s1 s2 :
  run_core s ->
  (forall c, o = Some c -> c ∈ g_running s /\ forall d, g_nodes s !! c = Some d -> n_in_cleanup d = false) ->
  irrelevant (with_current o s) s0 -> fields_same (with_current o s) s0 ->
  (run_core s0 -> run_core s1 /\ run_kept s0 s1) ->
  irrelevant (with_current (g_current s) s1) s2 -> fields_same (with_current (g_current s) s1) s2 ->
  run_core s2 /\ run_kept s s2.
Proof.
  intros [Hi Hx] Ho He1 He2 Hb Hl1 Hl2.
  assert (Hrun : forall c, o = Some c -> c ∈ g_running s) by (intros c Hc; by destruct (Ho c Hc)).
  assert (Hc0 : run_core s0).
  { split.
    - eapply invariant_irrelevant; [exact He1|]. by apply invariant_with_current.
    - eapply extras_same; [exact He2|]. by apply extras_current. }
  destruct (Hb Hc0) as [[Hi1 Hx1] [Fr Ek]].
  destruct He2 as [_ HN HW _ _ _ _ _ HR _ _ _ HF]. cbn [with_current g_edges g_nodes g_ghost g_running g_current g_flags g_owners g_flushing] in *.
  assert (Hk0 : ekept s s1).
  { eapply ekept_trans; [|exact Ek]. apply ekept_nodes; done. }
  destruct Fr as (Hr1 & Hc1 & Hk1' & Hf1).
  assert (Hrs : g_running s1 = g_running s) by congruence.
  destruct Hl2 as [L1 L2 L3 L4 L5 L5' La Lb L6 L7 L8 L9 L10]. cbn [with_current g_edges g_nodes g_ghost g_running g_current g_flags g_owners g_flushing] in L1, L2, L3, L4, L5, L5', La, Lb, L6, L7, L8, L9, L10.
  assert (Hres : forall c, g_current s = Some c -> c ∈ g_running s1 /\ forall d, g_nodes s1 !! c = Some d -> n_in_cleanup d = false).
  { intros c Hc. pose proof (proj1 Hx) as Hbb. rewrite Hrs. split; [by apply (b_current _ Hbb)|].
    intros d Hd. destruct (ci_running' s c (b_current _ Hbb c Hc) (b_running_kind _ Hbb)) as [e He].
    destruct (k_cleanup _ _ Hk0 c e He) as (e'&He'&Hs). rewrite Hd in He'. injection He' as <-. rewrite Hs.
    exact (b_cleanup_current _ Hbb c e Hc He). }
  split; [split|split].
  - eapply invariant_irrelevant; [exact Hl1|]. apply invariant_with_current; [|done]. intros c Hc. by destruct (Hres c Hc).
  - assert (Hfs : fields_same (with_current (g_current s) s1) s2) by (split; done).
    eapply extras_same; [exact Hfs|]. by apply extras_current.
  - split_and!.
    + rewrite L6. done.
    + done.
    + intros m k Hm. assert (Hm0 : has_kind m k s0) by (destruct Hm as (d&Hd&Hdk); exists d; split; [by rewrite HN|done]).
      destruct (Hk1' m k Hm0) as (d&Hd&Hdk). exists d. split; [by rewrite L2|done].
    + intros m. unfold nodes_and_windows. rewrite L2, L3. unfold nodes_and_windows in Hf1. specialize (Hf1 m). rewrite HN, HW in Hf1. done.
  - eapply ekept_trans; [exact Hk0|]. apply ekept_nodes; done.
Qed.

Lemma ok2_hosted {A} n (body : M A) :
  ok2 (fun _ => True) body ->
  ok2 (fun s => n ∈ g_running s /\ forall d, g_nodes s !! n = Some d -> n_in_cleanup d = false) (hosted n body).
Proof.
  intros Hb s s' a Hc [Hn Hcl] Hm. unfold hosted, m_bind, m_modify, m_return in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  apply (ctx_common (Some n) s (with_current_owner None (with_current (Some n) s)) s1); [done| | | | | |].
  - intros c [=<-]. done.
  - unfold irrelevant. simpl. split_and!; reflexivity.
  - split; reflexivity.
  - intros Hc0. exact (Hb _ _ _ Hc0 I E).
  - unfold irrelevant. simpl. split_and!; reflexivity.
  - split; reflexivity.
Qed.

Lemma ok2_untracked {A} (body : M A) : ok2 (fun _ => True) body -> ok2 (fun _ => True) (untracked body).
Proof.
  intros Hb s s' a Hc _ Hm. unfold untracked, m_bind, m_modify, m_return in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  apply (ctx_common None s (with_untracked (match g_current s with Some _ => true | None => g_untracked s end) (with_current None s)) s1); [done| | | | | |].
  - done.
  - unfold irrelevant. simpl. split_and!; reflexivity.
  - split; reflexivity.
  - intros Hc0. exact (Hb _ _ _ Hc0 I E).
  - unfold irrelevant. simpl. split_and!; reflexivity.
  - split; reflexivity.
Qed.

Lemma ok2_detached o (body : M unit) : ok2 (fun _ => True) body -> ok2 (fun _ => True) (detached o body).
Proof.
  intros Hb s s' a Hc _ Hm. unfold detached, m_bind, m_modify in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  apply (ctx_common None s (with_batch_depth (S (g_batch_depth s)) (with_current_owner (Some o) (with_current None s))) s1); [done| | | | | |].
  - done.
  - unfold irrelevant. simpl. split_and!; reflexivity.
  - split; reflexivity.
  - intros Hc0. exact (Hb _ _ _ Hc0 I E).
  - unfold irrelevant. simpl. split_and!; reflexivity.
  - split; reflexivity.
Qed.

(** ** Reads *)

Definition quiet_source (n : node_id) (s : state) : Prop :=
  exists d, g_nodes s !! n = Some d /\ n_kind d <> KEffect /\
    (stale (n_freshness d) = false \/ n_disposed d = true).

Lemma ghost_read_nodes c n pre s : g_nodes (ghost_read c n pre s) = g_nodes s.
Proof. unfold ghost_read. by destruct (g_nodes s !! n). Qed.

Lemma ghost_read_flushing c n pre s : g_flushing (ghost_read c n pre s) = g_flushing s.
Proof. unfold ghost_read. by destruct (g_nodes s !! n). Qed.

Lemma ghost_read_window c n pre s : gh_window (g_ghost (ghost_read c n pre s)) = gh_window (g_ghost s).
Proof. unfold ghost_read. by destruct (g_nodes s !! n). Qed.

Lemma ghost_read_current c n pre s : g_current (ghost_read c n pre s) = g_current s.
Proof. unfold ghost_read. by destruct (g_nodes s !! n). Qed.

Lemma ok2_track_quiet n : ok2 (quiet_source n) (track n).
Proof.
  apply (ok2_of (fun s => exists k, k <> KEffect /\ has_kind n k s)).
  - intros s (d&Hd&Hk&_). exists (n_kind d). split; [done|by exists d].
  - intros s s' a Hi (k&Hk&Hn) Hm. exact (ok_track n k Hk s s' a Hi Hn Hm).
  - intros s s' a _ Hx (d&Hd&Hk&Hq) Hm. unfold track in Hm.
    destruct (g_current s) as [c|] eqn:Ec; [|simplify_eq; split; [done|apply ekept_refl]].
    destruct (sources_add c n (g_edges s)) as [w|] eqn:Ea; [|done]. simplify_eq.
    destruct (extras_track s c n w d Hx Ec Ea Hd Hk) as (Hb&Ht&_).
    split; [split; [done|]|].
    + intros b c' y dc e _ Hc' He Hin Hst Hde Hdc.
      destruct (decide (c' = c /\ y = n)) as [[-> ->]|Hne]; [|exact (Ht b c' y dc e Hne Hc' He Hin Hst Hde Hdc)].
      rewrite ghost_read_nodes in He. simpl in He. rewrite Hd in He. injection He as <-.
      destruct Hq; congruence.
    + apply ekept_nodes; [by rewrite ghost_read_flushing|by rewrite ghost_read_nodes|by rewrite ghost_read_window].
Qed.

(** The read of a memo after its refresh: its [Track], then [MarkReaderCheck] if it is still stale. *)
Lemma ok2_track_mark f n :
  ok2 (has_kind n KMemo)
    (let* _ := track n in let* d' := get_node n in if stale_live d' then mark_reader f else m_return tt).
Proof.
  apply (ok2_of (has_kind n KMemo)); [done|..].
  { apply ok_bind; [apply _|apply ok_track; done|intros _].
    apply ok_bind; [apply _|apply ok_get_node|intros d'].
    destruct (stale_live d'); [apply ok_mark_reader|apply ok_return]. }
  intros s s' a Hi Hx (d&Hd&Hk) Hm. unfold m_bind in Hm. unfold track in Hm.
  destruct (g_current s) as [c|] eqn:Ec.
  - destruct (sources_add c n (g_edges s)) as [w|] eqn:Ea; [|done].
    assert (Hkne : n_kind d <> KEffect) by congruence.
    destruct (extras_track s c n w d Hx Ec Ea Hd Hkne) as (Hb&Ht&Hin).
    set (s1 := ghost_read c n (read_prefix (g_edges s) c) (with_edges w s)) in *.
    assert (Hd1 : g_nodes s1 !! n = Some d).
    { subst s1. by rewrite ghost_read_nodes. }
    assert (Ek1 : ekept s s1).
    { apply ekept_nodes; subst s1; [by rewrite ghost_read_flushing|by rewrite ghost_read_nodes|by rewrite ghost_read_window]. }
    unfold get_node in Hm. rewrite Hd1 in Hm.
    destruct (stale_live d) eqn:El.
    + unfold mark_reader in Hm.
      assert (Ec1 : g_current s1 = Some c) by (subst s1; by rewrite ghost_read_current).
      rewrite Ec1 in Hm. rewrite (b_flag_r _ Hb) in Hm.
      destruct (mark_check f c s1) as [s2|] eqn:Em; [|done]. simplify_eq.
      assert (Ham : any_mark f c None s1 s') by (by left).
      destruct (base_marks _ f c None s1 s' Hb Ht Ham) as [[Hb2 Ht2] E2].
      split; [split; [done|]|by eapply ekept_trans].
      intros b c' y dc e _ Hc' He Hin' Hst Hde Hdc.
      destruct (decide (c' = c /\ y = n)) as [[-> ->]|Hne]; [|exact (Ht2 b c' y dc e Hne Hc' He Hin' Hst Hde Hdc)].
      left.
      assert (Hf : mark_fixed s1 s') by (eapply marks_fixed, any_mark_rtc; exact Ham).
      destruct (mf_back _ _ Hf c dc Hc') as (d0&Hd0&(Hk0&Hl0&_)).
      assert (Hkc : n_kind d0 = KMemo \/ n_kind d0 = KEffect).
      { destruct (b_running_kind _ Hb c (b_current _ Hb c Ec1)) as [(e0&He0&K)|(e0&He0&K)]; rewrite Hd0 in He0; injection He0 as <-; auto. }
      assert (Ht1 : takes_marks s1 c).
      { exists d0. split; [done|]. destruct Hkc as [K|K]; [by left|right]. split_and!; [done|congruence|].
        exact (b_cleanup_current _ Hb c d0 Ec1 Hd0). }
      destruct (mark_check_stale _ _ _ _ Ht1 Em) as (d9&Hd9&Hs9). simplify_eq. done.
    + unfold m_return in Hm. simplify_eq.
      split; [split; [done|]|done].
      intros b c' y dc e _ Hc' He Hin' Hst Hde Hdc.
      destruct (decide (c' = c /\ y = n)) as [[-> ->]|Hne]; [|exact (Ht b c' y dc e Hne Hc' He Hin' Hst Hde Hdc)].
      rewrite Hd1 in He. injection He as <-. unfold stale_live in El. rewrite Hst, Hde in El. done.
  - unfold get_node in Hm. rewrite Hd in Hm. destruct (stale_live d).
    + unfold mark_reader in Hm. rewrite Ec in Hm. simplify_eq. split; [done|apply ekept_refl].
    + unfold m_return in Hm. simplify_eq. split; [done|apply ekept_refl].
Qed.

(** ** Resolutions *)

Lemma set_node_twice n x y s : set_node n x (set_node n y s) = set_node n x s.
Proof. unfold set_node. simpl. by rewrite insert_insert_eq. Qed.

Lemma ok2_resolved_memo n d' :
  n_freshness d' = Check ->
  ok2 (fun s => has_kind n KMemo s /\ g_nodes s !! n = Some d')
    (let* _ := m_modify (ghost_resolved n) in
     modify_node n (fun x => node_with_unannounced false (node_with_freshness Clean x))).
Proof.
  intros Hcheck.
  apply (ok2_of (fun s => g_nodes s !! n = Some d')); [by intros s [_ ?]|..].
  - intros s s' a Hi Hd Hm. rewrite bind_m_modify in Hm.
    destruct (resolved_reads_only n s) as (R & A & O & L & N & Hg). rewrite Hg in Hm. simpl in Hm.
    assert (Hd1 : g_nodes (with_run_ghost R A O L N s) !! n = Some d') by exact Hd.
    assert (Hi1 := invariant_reads_only s R A O L N Hi).
    destruct (ok_modify_at (fun _ => True) n (fun x => node_with_unannounced false (node_with_freshness Clean x)) d'
               ltac:(ns) _ _ _ Hi1 (conj I Hd1) Hm) as [Hi2 F2].
    split; [done|]. eapply frame_trans; [|exact F2]. by apply frame_same.
  - intros s s' a _ Hx [(e&He&Hk) Hd] Hm. rewrite bind_m_modify in Hm. cbv beta in Hm. unfold modify_node in Hm.
    destruct (ghost_resolved_fields n s) as (F1&F2&_).
    rewrite F2, Hd in Hm. injection Hm as <- <-.
    rewrite Hd in He. injection He as <-.
    destruct (b_memo _ (proj1 Hx) n d' Hd Hk) as [Hq Hc].
    apply (extras_resolved n (fun x => node_with_unannounced false (node_with_freshness Clean x)) d' s Hx Hd); done.
Qed.

(** ** The engine keeps the invariant *)

Definition body_post (s : state) (n : node_id) : Prop :=
  (slot_view (g_edges s) n).2 = length (slot_view (g_edges s) n).1 /\
  forall d, g_nodes s !! n = Some d -> n_disposed d = true -> (slot_view (g_edges s) n).1 = [].

(**md
Every engine function at fuel `f` satisfies `ok2` with the premise it needs, so it keeps `run_core` and
`run_kept`. `run_effect` and `run_effect_body` are stated with the narrower hypotheses they need.

- [f:] the fuel.
*)
Record runs_ok (f : nat) : Prop := {
  Rexec : forall e c, ok2 (fun _ => True) (exec f e c);
  Rread : forall n b, ok2 (fun _ => True) (read f n b);
  Rread_node : forall n, ok2 (fun _ => True) (read_node f n);
  Rupdate : forall n, ok2 (fun _ => True) (update_if_necessary f n);
  Rrefresh_or_pull : forall n, ok2 (has_kind n KMemo) (refresh_or_pull f n);
  Rpull : forall n, ok2 (has_kind n KMemo) (pull f n);
  Rrefresh : forall n, ok2 (has_kind n KMemo) (refresh f n);
  Rwalk : forall n i, ok2 (fun _ => True) (resolve_walk f n i);
  Rrecompute : forall n, ok2 (has_kind n KMemo) (recompute f n);
  Rrun_memo : forall n, ok2 (has_kind n KMemo) (run_memo f n);
  Rexecute : forall n, ok2 (effect_ctx n) (execute_effect f n);
  Rrun_effect : forall n s s' a, check_invariant s -> run_base s -> told_on (fun c _ => c <> n) s -> effect_ctx n s ->
    run_effect f n s = Some (s', a) -> run_core s' /\ run_kept s s';
  Rbody : forall n s s' a, run_core s -> body_ctx n s -> run_effect_body f n s = Some (s', a) ->
    run_core s' /\ run_kept s s' /\ body_post s' n;
  Rdischarge : forall o, ok2 (fun _ => True) (discharge f o);
  Rretire : forall o, ok2 (fun _ => True) (retire f o);
  Rdispose_scope : forall o, ok2 (fun _ => True) (dispose_scope f o);
  Rteardown : forall o ls cs, ok2 (fun _ => True) (teardown f o ls cs);
  Rcleanup : forall o b e, ok2 (fun _ => True) (run_cleanup f o b e);
  Rrelease : forall n, ok2 (fun _ => True) (release f n);
  Rdispose : forall n, ok2 (fun _ => True) (dispose_node f n);
  Rattach : forall n o, ok2 (fun _ => True) (attach f n o);
  Rwrite : forall n v, ok2 (fun _ => True) (write_signal f n v);
  Rrequest : ok2 (fun _ => True) (request_flush f);
  Rsettle : ok2 (fun _ => True) (settle_flush f);
  Rflush : ok2 (fun s => g_current s = None) (run_flush f);
  Rdrain : ok2 flush_ctx (drain f)
}.

Local Ltac rih IH :=
  first
    [ apply (Rexec _ IH) | apply (Rread _ IH) | apply (Rread_node _ IH) | apply (Rupdate _ IH)
    | apply (Rrefresh_or_pull _ IH) | apply (Rpull _ IH) | apply (Rrefresh _ IH) | apply (Rwalk _ IH)
    | apply (Rrecompute _ IH) | apply (Rrun_memo _ IH) | apply (Rexecute _ IH)
    | apply (Rdischarge _ IH) | apply (Rretire _ IH)
    | apply (Rdispose_scope _ IH) | apply (Rteardown _ IH) | apply (Rcleanup _ IH)
    | apply (Rrelease _ IH) | apply (Rdispose _ IH) | apply (Rattach _ IH) | apply (Rwrite _ IH)
    | apply (Rrequest _ IH) | apply (Rsettle _ IH) | apply (Rdrain _ IH) ].

Local Ltac lists_tac :=
  apply ok2_put_owner; repeat match goal with H : lists_ok _ |- _ => destruct H end;
  unfold lists_ok; cbn [o_cleanups o_cleanup_envs o_cleanup_order owner_with_cleanup owner_detached owner_retired owner_with_error owner_without owner_with_child];
  rewrite ?length_app; cbn [length]; split; lia.

Local Ltac rgo1 IH :=
  first
    [ apply ok2_return | apply ok2_stuck | apply ok2_get | apply ok2_get_node
    | apply ok2_emit | apply ok2_note | apply ok2_log_label | apply ok2_current_owner
    | apply ok2_mark_dirty
    | apply ok2_on_node; intros ? | apply ok2_on_value; intros ?
    | apply ok2_new_node; [apply _|intros ?; reflexivity|intros ?; reflexivity|intros ?; reflexivity|intros ?; reflexivity|intros ? Hx; first [reflexivity|(simpl in Hx; discriminate Hx)]|intros ?]
    | apply ok2_detached
    | lists_tac
    | apply ok2_modify_neutral; [nst|neu]
    | ok2_mod
    | rih IH
    | apply ok2_any; rih IH
    | apply ok2_get_owner_at; intros ? ?
    | apply ok2_bind; [apply _| |intros ?]
    | match goal with |- ok2 _ (match ?x with _ => _ end) => destruct x end
    | match goal with |- ok2 _ (if ?x then _ else _) => destruct x end ].

Local Ltac rgo IH := repeat rgo1 IH.

Local Ltac base_side Hb :=
  simpl; intros; first
    [ done | congruence | contradiction | by eapply (b_queued _ Hb) | by eapply (b_disposed _ Hb)
    | match goal with H : forall _ : n_disposed _ = true, _ |- _ => by apply H end
    | (destruct (b_window _ Hb) as (_&_&W&_); by eapply W)
    | (split_and!; first [done | by eapply (b_cleanup_running _ Hb) | congruence]) ].

Local Ltac name_k K :=
  match goal with H : m_bind _ ?k _ = _ |- _ => set (K := k) in H end.

Section Step.
  Variable f : nat.
  Hypothesis IH : runs_ok f.

  Lemma ok2_exec_S e c : ok2 (fun _ => True) (exec (S f) e c).
  Proof. destruct c; cbn [exec]; rgo IH. Qed.

  Lemma ok2_read_S n b : ok2 (fun _ => True) (read (S f) n b).
  Proof. destruct b; cbn [read]; [rih IH|]. apply ok2_untracked. rih IH. Qed.

  Lemma ok2_read_node_S n : ok2 (fun _ => True) (read_node (S f) n).
  Proof.
    cbn [read_node]. apply ok2_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek.
    - apply ok2_seq; [|intros _; apply ok2_return].
      intros s s' a Hc [_ Hd] Hm. refine (ok2_track_quiet n s s' a Hc _ Hm).
      exists d. split_and!; [done|congruence|]. left.
      destruct (b_window _ (proj1 (proj2 Hc))) as (_&_&W&_). rewrite (W n d Hd Ek). done.
    - apply ok2_seq; [|intros _; rgo IH].
      destruct (stale_live d) eqn:El.
      + apply (ok2_weaken (has_kind n KMemo)); [intros s [_ Hd]; by exists d|].
        apply ok2_bind; [apply _|rih IH|intros _]. apply ok2_track_mark.
      + eapply ok2_weaken; [|apply ok2_track_quiet].
        intros s [_ Hd]. exists d. split_and!; [done|congruence|].
        unfold stale_live in El. destruct (stale (n_freshness d)), (n_disposed d); simpl in El; auto; done.
    - apply ok2_stuck.
  Qed.

  Lemma ok2_update_S n : ok2 (fun _ => True) (update_if_necessary (S f) n).
  Proof.
    cbn [update_if_necessary]. apply ok2_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek; try apply ok2_return.
    destruct (stale_live d); [|apply ok2_return].
    apply (ok2_weaken (has_kind n KMemo)); [intros s [_ Hd]; by exists d|]. rih IH.
  Qed.

  Lemma ok2_refresh_or_pull_S n : ok2 (has_kind n KMemo) (refresh_or_pull (S f) n).
  Proof.
    cbn [refresh_or_pull]. apply ok2_bind; [apply _|apply ok2_get|intros s].
    destruct (deferring s); rih IH.
  Qed.

  Lemma ok2_pull_S n : ok2 (has_kind n KMemo) (pull (S f) n).
  Proof.
    cbn [pull]. apply ok2_bind; [apply _|ok2_mod|intros _].
    apply ok2_bind; [apply _|rih IH|intros _].
    apply ok2_bind; [apply _|ok2_mod|intros _].
    apply ok2_bind; [apply _|apply ok2_get|intros s].
    destruct (_ && _); [apply ok2_any; rih IH|apply ok2_return].
  Qed.

  Lemma ok2_refresh_S n : ok2 (has_kind n KMemo) (refresh (S f) n).
  Proof.
    cbn [refresh]. apply ok2_bind; [apply _|apply ok2_get_node|intros d].
    apply ok2_bind; [apply _| |intros _].
    - destruct (decide (n_freshness d = Check)); [|apply ok2_return].
      apply ok2_bind; [apply _|apply ok2_any; rih IH|intros _].
      apply ok2_get_node_at. intros d'.
      destruct (decide (n_freshness d' = Check)) as [Hc|_]; [|apply ok2_return].
      by apply ok2_resolved_memo.
    - apply ok2_bind; [apply _|apply ok2_get_node|intros d'].
      destruct (decide (n_freshness d' = Dirty)); [rih IH|apply ok2_return].
  Qed.

  Lemma ok2_walk_S n i : ok2 (fun _ => True) (resolve_walk (S f) n i).
  Proof. cbn [resolve_walk]. rgo IH. Qed.

  Lemma ok2_recompute_S n : ok2 (has_kind n KMemo) (recompute (S f) n).
  Proof.
    apply (ok2_of (has_kind n KMemo)); [done|apply (Erecompute _ (engine_ok_all (S f)))|].
    intros s s' a Hi Hx Hk Hm0.
    pose proof Hm0 as Hm. destruct Hk as (d & Hd & Hkd). cbn [recompute] in Hm.
    rewrite (bind_get_node _ _ _ _ Hd) in Hm.
    destruct (n_scope d) as [o|].
    2: { destruct (Rrun_memo _ IH n _ _ _ (conj Hi Hx) ltac:(by exists d) Hm) as [[_ ?] [_ ?]]. by split. }
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm. rewrite bind_m_modify in Hm. cbv beta in Hm.
    pose proof (invariant_cleared f (engine_ok_all f) n d s Hi Hd) as Hi1.
    pose proof (extras_cleared n d s Hx Hd Hkd) as Hx1.
    set (s1 := ghost_dirty_cleared n (n_runs d) (set_node n (node_with_freshness Clean d) s)) in Hi1, Hx1, Hm.
    assert (Hd1 : g_nodes s1 !! n = Some (node_with_freshness Clean d)) by (simpl; apply lookup_insert_eq).
    unfold m_bind in Hm. destruct (discharge f o s1) as [[s2 []]|] eqn:Ed; [|done].
    destruct (Rdischarge _ IH o _ _ _ (conj Hi1 Hx1) I Ed) as [Hc2 K2].
    assert (Hk2 : has_kind n KMemo s2).
    { destruct K2 as [(_ & _ & Hkk & _) _]. apply Hkk. eexists. split; [exact Hd1|done]. }
    destruct Hk2 as (d2 & Hd2 & Hk2). unfold get_node in Hm. rewrite Hd2 in Hm.
    assert (Hout : run_extras s' /\ ekept s2 s').
    { destruct (n_disposed d2) eqn:Edis.
      - unfold m_return in Hm. simplify_eq. split; [apply Hc2|apply ekept_refl].
      - destruct (decide (n_runs d2 = n_runs d)) as [Heq|Hne].
        + destruct (Rrun_memo _ IH n _ _ _ Hc2 ltac:(by exists d2) Hm) as [[_ ?] [_ ?]]. by split.
        + destruct (decide (n_freshness d2 = Dirty)).
          * destruct (Rrecompute _ IH n _ _ _ Hc2 ltac:(by exists d2) Hm) as [[_ ?] [_ ?]]. by split.
          * unfold m_return in Hm. simplify_eq. split; [apply Hc2|apply ekept_refl]. }
    destruct Hout as [Hx' E'].
    split; [done|].
    assert (E12 : ekept s1 s') by (eapply ekept_trans; [apply K2|exact E']).
    destruct (Erecompute_post _ (engine_ok_all (S f)) n _ _ _ Hi ltac:(by exists d) Hm0) as (e&He&Hp).
    split.
    - rewrite (k_flushing _ _ E12). done.
    - intros c dc Hdc. destruct (decide (c = n)) as [->|Hne].
      + rewrite Hd in Hdc. injection Hdc as <-. destruct (k_cleanup _ _ E12 n _ Hd1) as (d'&Hd'&Hs). exists d'. split; [done|]. rewrite Hs. done.
      + assert (Hdc1 : g_nodes s1 !! c = Some dc).
        { change (<[n := node_with_freshness Clean d]> (g_nodes s) !! c = Some dc). by rewrite lookup_insert_ne. }
        exact (k_cleanup _ _ E12 c dc Hdc1).
    - intros m dm r Hdm Hw Hl Hle. destruct (decide (m = n)) as [->|Hne].
      + exfalso. rewrite He in Hdm. injection Hdm as <-. destruct Hp as [Hp|Hp]; [|congruence].
        pose proof (Hp e r He Hw). lia.
      + destruct (k_window _ _ E12 m dm r Hdm Hw Hl Hle) as (d0&r0&Hd0&Hw0&Hl0&Hr0).
        exists d0, r0. split_and!; [| |done|done].
        * change (<[n := node_with_freshness Clean d]> (g_nodes s) !! m = Some d0) in Hd0. by rewrite lookup_insert_ne in Hd0.
        * change (<[n := n_runs d]> (gh_window (g_ghost s)) !! m = Some r0) in Hw0. by rewrite lookup_insert_ne in Hw0.
  Qed.

  Lemma run2_bind {A B} P (m : M A) (k : A -> M B) s s' b :
    ok2 P m -> run_core s -> P s -> m_bind m k s = Some (s', b) ->
    exists s1 a, m s = Some (s1, a) /\ run_core s1 /\ run_kept s s1 /\ k a s1 = Some (s', b).
  Proof.
    intros Hm Hc HP Hr. unfold m_bind in Hr. destruct (m s) as [[s1 a]|] eqn:E; [|done].
    destruct (Hm _ _ _ Hc HP E). eauto 8.
  Qed.

  Lemma ok2_run_memo_S n : ok2 (has_kind n KMemo) (run_memo (S f) n).
  Proof.
    apply (ok2_of (has_kind n KMemo)); [done|apply (Erun_memo _ (engine_ok_all (S f)))|].
    intros s s' a Hi Hx (d & Hd & Hkd) Hm. cbn [run_memo] in Hm.
    rewrite (bind_get_node _ _ _ _ Hd) in Hm.
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm.
    unfold edges, push_running in Hm. rewrite !bind_m_modify in Hm.
    set (u := fun x => node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone
        (node_with_unannounced false (node_with_freshness Clean x))))) in Hm.
    pose proof (invariant_run_begin n u d s Hi Hd eq_refl eq_refl (le_S _ _ (le_n _))) as Hi4.
    destruct (extras_run_begin n u d s Hx Hd Hkd Hkd eq_refl eq_refl eq_refl eq_refl (le_S _ _ (le_n _))) as [Hx4 E4].
    set (s4 := with_running _ (ghost_run_start _ _)) in Hi4, Hx4, E4.
    assert (Hm4 : g_nodes s4 !! n = Some (u d)) by (simpl; apply lookup_insert_eq).
    assert (Hrun4 : g_running s4 = n :: g_running s) by done.
    assert (Hcur4 : g_current s4 = g_current s) by done.
    assert (Hmem : n_in_cleanup (u d) = false) by exact (proj2 (b_memo _ (proj1 Hx) n d Hd Hkd)).
    assert (Hctx : n ∈ g_running s4 /\ forall e, g_nodes s4 !! n = Some e -> n_in_cleanup e = false).
    { split; [rewrite Hrun4; left|]. intros e He. rewrite Hm4 in He. by injection He as <-. }
    destruct (run2_bind _ _ _ _ _ _ (ok2_hosted n _ (Rexec _ IH _ _)) (conj Hi4 Hx4) Hctx Hm)
      as (s5 & r & _ & [_ Hx5] & [F5 E5] & Hm5).
    destruct F5 as (Hrun5 & Hcur5 & Hkinds5 & _). rewrite Hrun4 in Hrun5. rewrite Hcur4 in Hcur5.
    assert (Hk5 : has_kind n KMemo s5) by (apply Hkinds5; by exists (u d)).
    destruct Hk5 as (d1 & Hd1 & Hk1).
    rewrite (bind_get_node _ _ _ _ Hd1) in Hm5.
    set (result := if n_violated d1 then Fail pure_scope_failure else r) in Hm5.
    assert (Hcurin : forall c, g_current s = Some c -> c ∈ g_running s) by apply (b_current _ (proj1 Hx)).
    (* the value *)
    name_k K6.
    assert (Hs6 : exists s6, K6 tt s6 = Some (s', a) /\ run_extras s6 /\ ekept s5 s6 /\ g_running s6 = g_running s5 /\
        g_current s6 = g_current s5 /\ exists d6, g_nodes s6 !! n = Some d6 /\ n_disposed d6 = n_disposed d1 /\ n_kind d6 = KMemo).
    { destruct result as [v|y].
      - rewrite (bind_modify_node _ _ _ _ _ Hd1) in Hm5. eexists. split; [exact Hm5|].
        destruct (extras_neutral n (fun x => node_with_published true (node_with_value v x)) d1 s5 Hx5 Hd1
                    ltac:(unfold neutral; simpl; split_and!; first [reflexivity|done])) as [H1 H2].
        split_and!; [done|done|done|done|]. eexists. split_and!; [simpl; apply lookup_insert_eq|done|done].
      - rewrite bind_m_return in Hm5. eexists. split; [exact Hm5|].
        split_and!; [done|apply ekept_refl|done|done|]. by exists d1. }
    clear Hm5. destruct Hs6 as (s6 & Hm6 & Hx6 & E6 & Hrun6 & Hcur6 & d6 & Hd6 & Hdi6 & Hk6). subst K6. cbv beta in Hm6.
    rewrite Hrun5 in Hrun6. rewrite Hcur5 in Hcur6.
    (* the edges *)
    apply bind_edges_opt in Hm6 as (w & Hw & Hm7).
    assert (Hs7 : run_extras (with_edges w s6) /\ ekept s6 (with_edges w s6) /\
        (slot_view w n).2 = length (slot_view w n).1 /\
        (forall e, g_nodes s6 !! n = Some e -> n_disposed e = true -> (slot_view w n).1 = [])).
    { destruct (n_disposed d1) eqn:Edi.
      - destruct (extras_clear n w s6 d6 Hx6 Hd6 ltac:(congruence) Hw) as (A&B&C).
        split_and!; [done|done|by rewrite C|by rewrite C].
      - destruct (extras_end n w s6 Hx6 ltac:(rewrite Hrun6; left) Hw) as (A&B&C).
        split_and!; [done|done|done|]. intros e He Hde. congruence. }
    destruct Hs7 as (Hx7 & E7 & Hlen & Hempty).
    (* the pop *)
    unfold pop_running in Hm7. rewrite bind_m_modify in Hm7.
    simpl in Hm7. rewrite Hrun6 in Hm7. simpl in Hm7.
    assert (Hp : run_extras (with_running (g_running s) (with_edges w s6)) /\
                 ekept (with_edges w s6) (with_running (g_running s) (with_edges w s6))).
    { apply (extras_pop n).
      - done.
      - done.
      - done.
      - exact Hempty.
      - intros e He. change (g_nodes s6 !! n = Some e) in He. assert (e = d6) by congruence. subst e.
        exact (proj2 (b_memo _ (proj1 Hx6) n d6 Hd6 Hk6)).
      - intros c Hc. apply Hcurin. change (g_current s6 = Some c) in Hc. congruence. }
    destruct Hp as [Hx8 E8].
    set (s8 := with_running _ (with_edges w s6)) in Hx8, E8, Hm7.
    assert (Hk8 : has_kind n KMemo s8) by (exists d6; done).
    (* the failure *)
    name_k K9.
    assert (Hs9 : exists s9, K9 tt s9 = Some (s', a) /\ run_extras s9 /\ ekept s8 s9 /\ has_kind n KMemo s9).
    { destruct result as [v|y].
      - rewrite bind_m_return in Hm7. eexists. split; [exact Hm7|]. split_and!; [done|apply ekept_refl|done].
      - destruct Hk8 as (e & He & Hek).
        rewrite (bind_modify_node _ _ _ _ _ He) in Hm7. eexists. split; [exact Hm7|].
        destruct (extras_neutral n (fun x => node_with_violated false (node_with_status StError (node_with_failure (Some y) x))) e s8 Hx8 He
                    ltac:(unfold neutral; simpl; split_and!; first [reflexivity|done])) as [H1 H2].
        split_and!; [done|done|]. eexists. split; [simpl; apply lookup_insert_eq|done]. }
    clear Hm7. destruct Hs9 as (s9 & Hm9 & Hx9 & E9 & Hk9). subst K9. cbv beta in Hm9.
    destruct Hk9 as (d2 & Hd2 & Hk2).
    rewrite (bind_get_node _ _ _ _ Hd2) in Hm9.
    (* moved *)
    name_k K10.
    assert (Hs10 : exists s10 mv, K10 mv s10 = Some (s', a) /\ run_extras s10 /\ ekept s9 s10 /\ has_kind n KMemo s10).
    { destruct (decide _).
      - rewrite bind_m_return in Hm9. do 2 eexists. split; [exact Hm9|]. split_and!; [done|apply ekept_refl|by exists d2].
      - destruct (n_comparer d2) as [| |y].
        + rewrite bind_m_return in Hm9. do 2 eexists. split; [exact Hm9|]. split_and!; [done|apply ekept_refl|by exists d2].
        + rewrite bind_m_return in Hm9. do 2 eexists. split; [exact Hm9|]. split_and!; [done|apply ekept_refl|by exists d2].
        + rewrite bind_bind in Hm9. rewrite (bind_modify_node _ _ _ _ _ Hd2) in Hm9. rewrite bind_m_return in Hm9.
          do 2 eexists. split; [exact Hm9|].
          destruct (extras_neutral n (fun x => node_with_status StError (node_with_failure (Some y) (node_with_value (n_value d) x))) d2 s9 Hx9 Hd2
                      ltac:(unfold neutral; simpl; split_and!; first [reflexivity|done])) as [H1 H2].
          split_and!; [done|done|]. eexists. split; [simpl; apply lookup_insert_eq|done]. }
    clear Hm9. destruct Hs10 as (s10 & mv & Hm10 & Hx10 & E10 & Hk10). subst K10. cbv beta in Hm10.
    rewrite bind_get in Hm10. rewrite bind_m_modify in Hm10.
    destruct Hk10 as (d3 & Hd3 & Hk3).
    unfold ghost_run_end in Hm10. rewrite Hd3 in Hm10.
    set (G := {| gh_published := _; gh_seen := _; gh_unnoticed := _; gh_window := _; gh_reads := _; gh_passed := _; gh_over := _; gh_run_reads := _; gh_nested := _ |}) in Hm10.
    assert (Hfs : fields_same s10 (with_ghost G s10)) by (split; reflexivity).
    (* the notice *)
    name_k K11.
    assert (Hs11 : exists s11, K11 tt s11 = Some (s', a) /\ run_extras s11 /\ ekept s10 s11 /\ has_kind n KMemo s11).
    { pose proof (extras_same _ _ Hfs Hx10) as Hxg. pose proof (ekept_same _ _ Hfs) as Eg.
      destruct mv.
      - apply bind_step in Hm10 as (s11 & Hn & Hm11).
        exists s11. split; [exact Hm11|].
        assert (Ham : any_mark f n (g_current s10) (with_ghost G s10) s11) by (right; right; right; right; done).
        destruct (extras_marks _ _ _ _ _ Hxg Ham) as [H1 H2].
        split_and!; [done|by eapply ekept_trans|].
        eapply has_kind_fixed; [apply marks_fixed; by eapply any_mark_rtc|]. by exists d3.
      - rewrite bind_m_return in Hm10. exists (with_ghost G s10). split; [exact Hm10|].
        split_and!; [done|done|by exists d3]. }
    clear Hm10. destruct Hs11 as (s11 & Hm11 & Hx11 & E11 & Hk11). subst K11. cbv beta in Hm11.
    destruct Hk11 as (d4 & Hd4 & Hk4).
    rewrite (bind_get_node _ _ _ _ Hd4) in Hm11.
    assert (Eall : ekept s s11).
    { eapply ekept_trans; [exact E4|]. eapply ekept_trans; [exact E5|]. eapply ekept_trans; [exact E6|].
      eapply ekept_trans; [exact E7|]. eapply ekept_trans; [exact E8|]. eapply ekept_trans; [exact E9|].
      eapply ekept_trans; [exact E10|exact E11]. }
    destruct (decide (n_freshness d4 = Clean)).
    - unfold m_return in Hm11. simplify_eq. by split.
    - unfold announce in Hm11. rewrite (b_flag_u _ (proj1 Hx11)) in Hm11.
      unfold modify_node in Hm11. rewrite Hd4 in Hm11. simplify_eq.
      destruct (extras_announce n d4 s11 Hx11 Hd4) as [H1 H2]. split; [done|by eapply ekept_trans].
  Qed.

  Lemma effect_tail n d s s' a :
    run_core s -> effect_ctx n s -> g_nodes s !! n = Some d -> n_freshness d <> Check ->
    (let* _ := modify_node n (fun x => node_with_freshness Clean (node_with_queued false x)) in
     if decide (n_freshness d = Dirty) then run_effect f n else m_return tt) s = Some (s', a) ->
    run_extras s' /\ ekept s s'.
  Proof.
    intros [Hi Hx] Hctx Hd Hnc Hm.
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm.
    set (u := fun x => node_with_freshness Clean (node_with_queued false x)) in Hm.
    destruct Hx as [Hb Ht].
    destruct (decide (n_freshness d = Dirty)) as [Hdirty|Hne].
    - assert (Hb1 : run_base (set_node n (u d) s)).
      { apply (base_set n u d); try done.
        - simpl. intros H1 H2. by eapply (b_disposed _ Hb).
        - simpl. intros Hk. exfalso. destruct Hctx as [(e&He&Hek) _]. rewrite Hd in He. injection He as <-. congruence.
        - simpl. intros Hc. split_and!.
          + by eapply (b_cleanup_running _ Hb).
          + by eapply (me_cleanup _ (b_env _ Hb)).
          + intros H1. by eapply (b_cleanup_disposed _ Hb).
          + intros Hcur. destruct Hctx as [_ (Hn&_)]. congruence. }
      assert (Ht1 : told_on (fun c _ => c <> n) (set_node n (u d) s)).
      { pose proof (told_others _ n u d s Ht Hd ltac:(simpl; done)) as H.
        intros b c y dc e Hr. apply (H b c y dc e). by split. }
      assert (Hok : ok_under (has_kind n KEffect) (modify_node n u)).
      { apply ok_modify_effect; intros ?; simpl; try done; lia. }
      assert (Hmod : modify_node n u s = Some (set_node n (u d) s, tt)).
      { unfold modify_node. by rewrite Hd. }
      destruct (Hok _ _ _ Hi (proj1 Hctx) Hmod) as [Hi1 F1].
      assert (E1 : ekept s (set_node n (u d) s)) by (apply (ekept_set n u d); done).
      assert (Hctx1 : effect_ctx n (set_node n (u d) s)).
      { refine (stable2 _ _ _ Hctx). by split. }
      destruct (Rrun_effect _ IH n _ _ _ Hi1 Hb1 Ht1 Hctx1 Hm) as [[_ Hx2] [_ E2]].
      split; [done|by eapply ekept_trans].
    - unfold m_return in Hm. simplify_eq.
      assert (Hclean : n_freshness d = Clean) by (destruct (n_freshness d); done).
      apply (extras_neutral n u d s); [by split|done|].
      unfold neutral. simpl. split_and!; first [done|congruence].
  Qed.

  Lemma ok2_execute_S n : ok2 (effect_ctx n) (execute_effect (S f) n).
  Proof.
    apply (ok2_of (has_kind n KEffect)); [by intros s [? _]|apply (Eexecute _ (engine_ok_all (S f)))|].
    intros s s' a Hi Hx Hctx Hm. cbn [execute_effect] in Hm.
    destruct Hctx as [Hk Hfl]. destruct Hk as (d&Hd&Hkd).
    rewrite (bind_get_node _ _ _ _ Hd) in Hm.
    assert (Hctx : effect_ctx n s) by (split; [by exists d|done]).
    destruct (n_disposed d) eqn:Edi.
    { unfold modify_node in Hm. rewrite Hd in Hm. simplify_eq.
      apply (extras_neutral n (node_with_queued false) d s); [done|done|].
      unfold neutral. simpl. split_and!; done. }
    destruct (decide (n_freshness d = Check)) as [Hc|Hc].
    - rewrite bind_bind in Hm.
      destruct (run2_bind _ _ _ _ _ _ (Rwalk _ IH n 0) (conj Hi Hx) I Hm) as (s1 & [] & _ & Hc1 & K1 & Hm1).
      assert (Hctx1 : effect_ctx n s1) by (exact (stable2 _ _ K1 Hctx)).
      destruct (proj1 Hctx1) as (d1&Hd1&Hkd1).
      rewrite bind_bind, (bind_get_node _ _ _ _ Hd1) in Hm1.
      destruct (decide (n_freshness d1 = Check)) as [Hc2|Hc2].
      + rewrite bind_bind, bind_m_modify in Hm1. cbv beta in Hm1.
        destruct (ghost_resolved_fields n s1) as (F1&F2&_).
        assert (Hg : g_nodes (ghost_resolved n s1) !! n = Some d1) by (by rewrite F2).
        rewrite (bind_modify_node _ _ _ _ _ Hg) in Hm1.
        assert (Hd2 : g_nodes (set_node n (node_with_freshness Clean d1) (ghost_resolved n s1)) !! n = Some (node_with_freshness Clean d1))
          by (simpl; apply lookup_insert_eq).
        rewrite (bind_get_node _ _ _ _ Hd2) in Hm1.
        rewrite (bind_modify_node _ _ _ _ _ Hd2) in Hm1.
        rewrite decide_False in Hm1 by (simpl; done).
        unfold m_return in Hm1. simplify_eq. rewrite set_node_twice.
        destruct (extras_resolved n (fun x => node_with_freshness Clean (node_with_queued false (node_with_freshness Clean x))) d1 s1 (proj2 Hc1) Hd1
                    eq_refl eq_refl eq_refl eq_refl eq_refl eq_refl) as [H1 H2].
        split; [done|]. eapply ekept_trans; [apply K1|exact H2].
      + rewrite bind_m_return, (bind_get_node _ _ _ _ Hd1) in Hm1.
        destruct (effect_tail n d1 s1 s' a Hc1 Hctx1 Hd1 Hc2 Hm1) as [H1 H2].
        split; [done|]. eapply ekept_trans; [apply K1|exact H2].
    - rewrite bind_m_return, (bind_get_node _ _ _ _ Hd) in Hm.
      exact (effect_tail n d s s' a (conj Hi Hx) Hctx Hd Hc Hm).
  Qed.

  Lemma ok2_body_S n s s' a :
    run_core s -> body_ctx n s -> run_effect_body (S f) n s = Some (s', a) -> run_core s' /\ run_kept s s' /\ body_post s' n.
  Proof.
    intros Hc Hctx Hm.
    destruct Hc as [Hi Hx]. destruct Hctx as (Hrun & Hk & Hcl).
    destruct (Ebody _ (engine_ok_all (S f)) n _ _ _ Hi (conj Hk Hrun) Hm) as [Hi' F'].
    cbn [run_effect_body] in Hm.
    destruct Hk as (d&Hd&Hkd).
    rewrite (bind_get_node _ _ _ _ Hd), (bind_modify_node _ _ _ _ _ Hd) in Hm.
    set (u := fun x => node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone x))) in Hm.
    assert (Hok : ok_under (has_kind n KEffect) (modify_node n u)).
    { apply ok_modify_effect; intros ?; simpl; try done; lia. }
    assert (Hmod : modify_node n u s = Some (set_node n (u d) s, tt)).
    { unfold modify_node. by rewrite Hd. }
    destruct (Hok _ _ _ Hi ltac:(by exists d) Hmod) as [Hi1 F1].
    assert (Hb := proj1 Hx). assert (Ht := proj2 Hx).
    assert (Hnw : gh_window (g_ghost s) !! n = None).
    { destruct (gh_window (g_ghost s) !! n) as [r|] eqn:E; [|done]. exfalso.
      destruct (b_window _ Hb) as (W&_). destruct (W n r E) as (e&He&Hek). rewrite Hd in He. injection He as <-. congruence. }
    assert (Hx1 : run_extras (set_node n (u d) s)).
    { split.
      - pose proof (Hcl d Hd) as Hcl'. apply (base_set n u d); base_side Hb.
      - apply (told_set _ n u d s Ht Hd); simpl; try done. by right. }
    assert (E1 : ekept s (set_node n (u d) s)) by (apply (ekept_set n u d); simpl; try done; lia).
    cbv beta in Hm. unfold emit in Hm. rewrite bind_m_modify in Hm. cbv beta in Hm.
    set (s2 := with_trace _ _) in Hm.
    assert (Hfs : fields_same (set_node n (u d) s) s2) by (split; reflexivity).
    assert (Hi2 : check_invariant s2) by (eapply invariant_irrelevant; [|exact Hi1]; unfold irrelevant; simpl; split_and!; reflexivity).
    assert (Hx2 := extras_same _ _ Hfs Hx1).
    assert (E2 := ekept_same _ _ Hfs).
    assert (Hd2 : g_nodes s2 !! n = Some (u d)) by (simpl; apply lookup_insert_eq).
    assert (Hctx2 : n ∈ g_running s2 /\ forall e, g_nodes s2 !! n = Some e -> n_in_cleanup e = false).
    { split; [done|]. intros e He. rewrite Hd2 in He. injection He as <-. simpl. exact (Hcl d Hd). }
    destruct (run2_bind _ _ _ _ _ _ (ok2_hosted n _ (Rexec _ IH _ _)) (conj Hi2 Hx2) Hctx2 Hm)
      as (s3 & r & _ & [Hi3 Hx3] & [F3 E3] & Hm3).
    destruct F3 as (Hrun3 & _ & Hk3 & _).
    assert (Hk33 : has_kind n KEffect s3) by (apply Hk3; eexists; split; [exact Hd2|done]).
    destruct Hk33 as (d3&Hd3&Hkd3).
    rewrite (bind_get_node _ _ _ _ Hd3) in Hm3.
    apply bind_edges_opt in Hm3 as (w & Hw & Hm4).
    assert (Hrun33 : n ∈ g_running s3).
    { rewrite Hrun3. exact (proj1 Hctx2). }
    assert (Hs4 : run_extras (with_edges w s3) /\ ekept s3 (with_edges w s3) /\ body_post (with_edges w s3) n).
    { destruct (n_disposed d3) eqn:Edi.
      - destruct (extras_clear n w s3 d3 Hx3 Hd3 Edi Hw) as (A&B&C).
        split_and!; [done|done|]. unfold body_post. cbn [with_edges g_edges]. rewrite C. split; done.
      - destruct (extras_end n w s3 Hx3 Hrun33 Hw) as (A&B&C).
        split_and!; [done|done|]. split; [exact C|]. intros e He Hde. change (g_nodes s3 !! n = Some e) in He. congruence. }
    destruct Hs4 as (Hx4 & E4 & Hp4).
    assert (Eall : ekept s (with_edges w s3)).
    { eapply ekept_trans; [exact E1|]. eapply ekept_trans; [exact E2|]. eapply ekept_trans; [exact E3|exact E4]. }
    destruct r as [v|y].
    - unfold m_return in Hm4. simplify_eq. split_and!; [split; done|split; done|done].
    - assert (Hd4 : g_nodes (with_edges w s3) !! n = Some d3) by exact Hd3.
      rewrite (bind_modify_node _ _ _ _ _ Hd4) in Hm4.
      unfold emit, m_modify in Hm4. injection Hm4 as <- <-.
      destruct (extras_neutral n (fun x => node_with_status StError (node_with_failure (Some y) x)) d3 _ Hx4 Hd4
                  ltac:(unfold neutral; simpl; split_and!; first [reflexivity|done])) as [H1 H2].
      split_and!.
      + split; [done|]. eapply extras_same; [|exact H1]. split; reflexivity.
      + split; [done|]. eapply ekept_trans; [exact Eall|]. eapply ekept_trans; [exact H2|]. apply ekept_same. split; reflexivity.
      + destruct Hp4 as [P1 P2]. split; [exact P1|]. intros e He Hde.
        cbn [with_trace set_node g_nodes with_edges] in He. rewrite lookup_insert_eq in He. injection He as <-.
        apply (P2 d3 Hd4). done.
  Qed.

  Lemma ok2_run_effect_S n s s' a :
    check_invariant s -> run_base s -> told_on (fun c _ => c <> n) s -> effect_ctx n s ->
    run_effect (S f) n s = Some (s', a) -> run_core s' /\ run_kept s s'.
  Proof.
    intros Hi Hb Ht Hctx Hm.
    destruct Hctx as [Hk [Hcur [Hfl Hmemo]]].
    destruct (Erun_effect _ (engine_ok_all (S f)) n _ _ _ Hi Hk Hm) as [Hi' F'].
    cbn [run_effect] in Hm.
    unfold push_running, edges in Hm. rewrite !bind_m_modify in Hm. cbv beta in Hm.
    assert (Hnr : n ∉ g_running s).
    { intros Hin. pose proof (Hmemo n Hin) as Hkm. by pose proof (has_kind_unique n _ _ s Hk Hkm). }
    destruct (extras_effect_begin n s Hb Ht Hk Hnr Hfl) as [Hx1 E1].
    set (s1 := ghost_effect_start n _) in Hx1, E1, Hm.
    assert (Hi1 : check_invariant s1).
    { refine (invariant_reads_only _ _ _ _ _ _ (invariant_effect_begin n (n :: g_running s) s Hi Hk _ _ _)).
      - intros c Hc. by right.
      - left.
      - intros c Hc. by apply elem_of_cons in Hc. }
    assert (Hv : forall c, slot_view (g_edges s1) c = if decide (c = n) then ((slot_view (g_edges s) c).1, 0) else slot_view (g_edges s) c).
    { intros c. exact (slot_view_begin n (g_edges s) c). }
    destruct Hk as (d&Hd&Hkd).
    assert (Hd1 : g_nodes s1 !! n = Some d) by exact Hd.
    rewrite (bind_get_node _ _ _ _ Hd1) in Hm.
    assert (Hcl0 : n_in_cleanup d = false).
    { destruct (n_in_cleanup d) eqn:E; [|done]. exfalso. apply Hnr. eapply (b_cleanup_running _ Hb); [exact Hd|done]. }
    assert (Hslots : n_disposed d = true -> (slot_view (g_edges s1) n).1 = []).
    { intros Hde. rewrite Hv, decide_True by done. cbn [fst]. eapply (b_disposed _ Hb); [exact Hd|done|done]. }
    assert (Hcur1 : (slot_view (g_edges s1) n).2 = 0) by (rewrite Hv, decide_True by done; done).
    name_k K.
    assert (Hs4 : exists s4 d4, K tt s4 = Some (s', a) /\ run_core s4 /\ ekept s1 s4 /\ g_running s4 = g_running s1 /\
        g_current s4 = g_current s /\ g_nodes s4 !! n = Some d4 /\ n_in_cleanup d4 = false /\ n_kind d4 = KEffect /\
        (n_disposed d4 = true -> (slot_view (g_edges s4) n).1 = [])).
    { destruct (n_scope d) as [o|].
      - rewrite (bind_bind _ _ _ s1) in Hm. rewrite (bind_modify_node _ _ _ _ _ Hd1) in Hm.
        set (s2 := set_node n (node_with_in_cleanup true d) s1) in Hm.
        assert (Hok : ok_under (fun _ => True) (modify_node n (node_with_in_cleanup true))).
        { apply ok_modify_node. intros ?. ns. }
        assert (Hmod : modify_node n (node_with_in_cleanup true) s1 = Some (s2, tt)).
        { unfold modify_node. by rewrite Hd1. }
        destruct (Hok _ _ _ Hi1 I Hmod) as [Hi2 F2].
        pose proof (proj1 Hx1) as Hb1. pose proof (proj2 Hx1) as Ht1.
        assert (Hb2 : run_base s2).
        { assert (Hin1 : n ∈ g_running s1) by left.
          assert (Hcur2 : g_current s1 <> Some n) by (change (g_current s <> Some n); by rewrite Hcur).
          apply (base_set n (node_with_in_cleanup true) d); base_side Hb1. }
        assert (Ht2 : told s2).
        { apply (told_set _ n (node_with_in_cleanup true) d s1 Ht1 Hd1); simpl; try done. left. lia. }
        assert (Hd2 : g_nodes s2 !! n = Some (node_with_in_cleanup true d)) by (simpl; apply lookup_insert_eq).
        rewrite bind_bind in Hm.
        destruct (run2_bind _ _ _ _ _ _ (Rdischarge _ IH o) (conj Hi2 (conj Hb2 Ht2)) I Hm) as (s3 & [] & _ & [Hi3 Hx3] & [F3 E3] & Hm3).
        destruct (k_cleanup _ _ E3 n _ Hd2) as (d3&Hd3&Hc3).
        simpl in Hc3.
        destruct (F3) as (Hrun3 & Hcur3 & Hk3 & _).
        assert (Hkd3 : n_kind d3 = KEffect).
        { destruct (Hk3 n KEffect ltac:(eexists; split; [exact Hd2|done])) as (e&He&Hek). rewrite Hd3 in He. by injection He as <-. }
        rewrite (bind_modify_node _ _ _ _ _ Hd3) in Hm3.
        set (s4 := set_node n (node_with_in_cleanup false d3) s3) in Hm3.
        assert (Hempty3 : n_disposed d3 = true -> (slot_view (g_edges s3) n).1 = []).
        { intros Hde. eapply (b_cleanup_disposed _ (proj1 Hx3)); [exact Hd3|done|done]. }
        assert (Hok4 : ok_under (fun _ => True) (modify_node n (node_with_in_cleanup false))).
        { apply ok_modify_node. intros ?. ns. }
        assert (Hmod4 : modify_node n (node_with_in_cleanup false) s3 = Some (s4, tt)).
        { unfold modify_node. by rewrite Hd3. }
        destruct (Hok4 _ _ _ Hi3 I Hmod4) as [Hi4 F4].
        assert (Hb4 : run_base s4).
        { pose proof (proj1 Hx3) as Hb3. apply (base_set n (node_with_in_cleanup false) d3); base_side Hb3. }
        assert (Ht4 : told s4).
        { apply (told_set _ n (node_with_in_cleanup false) d3 s3 (proj2 Hx3) Hd3); simpl; try done. left. lia. }
        exists s4, (node_with_in_cleanup false d3). split_and!.
        + exact Hm3.
        + split; [done|by split].
        + split.
          * exact (k_flushing _ _ E3).
          * intros c e He. destruct (decide (c = n)) as [->|Hne].
            -- exists (node_with_in_cleanup false d3). split; [simpl; apply lookup_insert_eq|]. rewrite Hd1 in He. injection He as <-. by rewrite Hcl0.
            -- assert (He2 : g_nodes s2 !! c = Some e) by (simpl; by rewrite lookup_insert_ne).
               destruct (k_cleanup _ _ E3 c e He2) as (e'&He'&Hs). exists e'. simpl. rewrite lookup_insert_ne by done. auto.
          * intros m e r. simpl. destruct (decide (m = n)) as [->|Hne].
            -- rewrite lookup_insert_eq. intros [=<-] Hw Hl Hle. simpl in Hl, Hle.
               destruct (k_window _ _ E3 n d3 r Hd3 Hw Hl Hle) as (d0&r0&Hd0&Hw0&Hl0&Hr0).
               rewrite Hd2 in Hd0. injection Hd0 as <-. exists d, r0. split_and!; [done|done|done|done].
            -- rewrite lookup_insert_ne by done. intros He Hw Hl Hle.
               destruct (k_window _ _ E3 m e r He Hw Hl Hle) as (d0&r0&Hd0&Hw0&Hl0&Hr0).
               exists d0, r0. split_and!; [|done|done|done]. simpl in Hd0. by rewrite lookup_insert_ne in Hd0.
        + exact Hrun3.
        + exact Hcur3.
        + simpl. apply lookup_insert_eq.
        + done.
        + simpl. done.
        + simpl. exact Hempty3.
      - rewrite bind_m_return in Hm. exists s1, d. split_and!; [exact Hm|by split|apply ekept_refl|done|done|done|done|done|done]. }
    clear Hm. destruct Hs4 as (s4 & d4 & Hm4 & Hc4 & E4 & Hrun4 & Hcu4 & Hd4 & Hcl4 & Hk4 & Hempty4). subst K. cbv beta in Hm4.
    rewrite (bind_get_node _ _ _ _ Hd4) in Hm4.
    assert (Hrun4' : g_running s4 = n :: g_running s) by (rewrite Hrun4; done).
    assert (Hfinal : forall s5, run_extras s5 -> ekept s s5 -> g_running s5 = n :: g_running s -> g_current s5 = g_current s ->
        (slot_view (g_edges s5) n).2 = length (slot_view (g_edges s5) n).1 ->
        (forall e, g_nodes s5 !! n = Some e -> n_disposed e = true -> (slot_view (g_edges s5) n).1 = []) ->
        (forall e, g_nodes s5 !! n = Some e -> n_in_cleanup e = false) ->
        pop_running s5 = Some (s', a) -> run_core s' /\ run_kept s s').
    { intros s5 Hx5 E5 Hrun5 Hcur5 Hlen5 Hemp5 Hcl5 Hp. unfold pop_running, m_modify in Hp.
      rewrite Hrun5 in Hp. simpl in Hp. injection Hp as <- <-.
      destruct (extras_pop n (g_running s) s5 Hx5 Hrun5 Hlen5 Hemp5 Hcl5) as [H1 H2].
      { intros c Hc. rewrite Hcur5, Hcur in Hc. done. }
      split; [split; done|split; [done|by eapply ekept_trans]]. }
    assert (E14 : ekept s s4) by (by eapply ekept_trans).
    destruct (n_disposed d4) eqn:Edi.
    - rewrite bind_m_return in Hm4.
      apply (Hfinal s4); [apply Hc4|done|done|done| | | |done].
      + rewrite (Hempty4 eq_refl). pose proof (b_cursor _ (proj1 (proj2 Hc4)) n) as Hle. rewrite (Hempty4 eq_refl) in Hle. cbn [length] in *. lia.
      + intros e He _. exact (Hempty4 eq_refl).
      + intros e He. rewrite Hd4 in He. by injection He as <-.
    - unfold m_bind in Hm4. destruct (run_effect_body f n s4) as [[s5 []]|] eqn:Eb; [|done].
      assert (Hctx4 : body_ctx n s4).
      { split_and!.
        - rewrite Hrun4'. left.
        - by exists d4.
        - intros e He. rewrite Hd4 in He. by injection He as <-. }
      destruct (Rbody _ IH n _ _ _ Hc4 Hctx4 Eb) as (Hc5 & [F5 E5] & [Pl Pe]).
      destruct F5 as (Hr5 & Hcu5 & _ & _).
      apply (Hfinal s5); [apply Hc5|by eapply ekept_trans|congruence|congruence|done|done| |done].
      intros e He. destruct (k_cleanup _ _ E5 n d4 Hd4) as (e'&He'&Hs). rewrite He in He'. injection He' as <-. congruence.
  Qed.

  Lemma ok2_discharge_S o : ok2 (fun _ => True) (discharge (S f) o).
  Proof.
    cbn [discharge]. apply ok2_bind; [apply _|apply ok2_detached; rih IH|intros _].
    apply ok2_bind; [apply _|apply ok2_get|intros s]. destruct (_ && _ && _); [ok2_mod|apply ok2_return].
  Qed.

  Lemma ok2_retire_S o : ok2 (fun _ => True) (retire (S f) o).
  Proof.
    cbn [retire]. apply ok2_bind; [apply _|apply ok2_detached|intros _]; rgo IH.
  Qed.

  Lemma ok2_dispose_scope_S o : ok2 (fun _ => True) (dispose_scope (S f) o).
  Proof. cbn [dispose_scope]. rgo IH. Qed.

  Lemma ok2_teardown_S o ls cs : ok2 (fun _ => True) (teardown (S f) o ls cs).
  Proof. cbn [teardown]. rgo IH. Qed.

  Lemma ok2_cleanup_S o b e : ok2 (fun _ => True) (run_cleanup (S f) o b e).
  Proof. cbn [run_cleanup]. unfold record_error. rgo IH. Qed.

  Lemma ok2_release_S n : ok2 (fun _ => True) (release (S f) n).
  Proof. cbn [release]. rgo IH. Qed.

  Lemma ok2_attach_S n o : ok2 (fun _ => True) (attach (S f) n o).
  Proof. cbn [attach]. rgo IH. Qed.

  Lemma with_owners_same s : with_owners (g_owners s) s = s.
  Proof. by destruct s. Qed.

  Lemma ok2_dispose_S n : ok2 (fun _ => True) (dispose_node (S f) n).
  Proof.
    apply (ok2_of (fun _ => True)); [done|apply (Edispose _ (engine_ok_all (S f)))|].
    intros s s' a Hi Hx _ Hm. cbn [dispose_node] in Hm.
    destruct (g_nodes s !! n) as [d|] eqn:Hd; [|by unfold m_bind, get_node in Hm; rewrite Hd in Hm].
    rewrite (bind_get_node _ _ _ _ Hd) in Hm.
    destruct (n_disposed d) eqn:Edi.
    { unfold m_return in Hm. simplify_eq. split; [done|apply ekept_refl]. }
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm. cbv beta in Hm.
    set (u := node_with_link None (node_with_disposed true d)) in Hm.
    set (s1 := set_node n u s) in Hm.
    assert (Hlists : forall o x, g_owners s !! o = Some x -> lists_ok x) by (apply (b_lists _ (proj1 Hx))).
    name_k K.
    assert (Hun : exists O, (forall o x, O !! o = Some x -> lists_ok x) /\ K tt (with_owners O s1) = Some (s', a)).
    { destruct (n_link d) as [l|].
      - unfold unlink, m_bind, get_owner in Hm.
        destruct (g_owners s1 !! l_owner l) as [x|] eqn:Eo; [|done].
        assert (Hxl : lists_ok x) by (exact (Hlists _ _ Eo)).
        destruct (decide _).
        + exists (<[l_owner l := owner_without l x]> (g_owners s1)). split; [|exact Hm].
          intros o y. change (<[l_owner l := owner_without l x]> (g_owners s) !! o = Some y -> lists_ok y).
          destruct (decide (o = l_owner l)) as [->|Hne].
          * rewrite lookup_insert_eq. intros [=<-]. exact Hxl.
          * rewrite lookup_insert_ne by done. apply Hlists.
        + exists (g_owners s1). split; [exact Hlists|]. rewrite with_owners_same. exact Hm.
      - exists (g_owners s1). split; [exact Hlists|]. rewrite with_owners_same. rewrite bind_m_return in Hm. exact Hm. }
    clear Hm. destruct Hun as (O & HO & Hm). subst K. cbv beta in Hm.
    apply bind_edges_opt in Hm as (w & Hw & Hm).
    change (sources_clear n (g_edges s) = Some w) in Hw.
    destruct (extras_dispose n d w s O Hx Hd Hw HO) as [Hx3 E3].
    assert (Hi1 : check_invariant s1).
    { assert (Hs : node_step (fun x => node_with_link None (node_with_disposed true x)) d) by ns.
      exact (invariant_set None None n _ d s Hi Hd Hs). }
    assert (Hi2 : check_invariant (with_owners O s1)).
    { eapply invariant_irrelevant; [|exact Hi1]. unfold irrelevant. simpl. split_and!; reflexivity. }
    assert (Hi3 : check_invariant (with_edges w (with_owners O s1))) by (apply (invariant_clear None n w _ Hi2 Hw)).
    destruct (n_scope d) as [o|].
    - destruct (Rretire _ IH o _ _ _ (conj Hi3 Hx3) I Hm) as [[_ Hx'] [_ E']].
      split; [done|by eapply ekept_trans].
    - unfold m_return in Hm. simplify_eq. by split.
  Qed.

  Lemma ok2_write_S n v : ok2 (fun _ => True) (write_signal (S f) n v).
  Proof.
    cbn [write_signal]. apply ok2_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek; try apply ok2_stuck.
    destruct (decide _); [apply ok2_return|].
    intros s s' a [Hi Hx] [_ Hd] Hm.
    rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm. rewrite bind_m_modify in Hm.
    set (s1 := set_node n (node_with_value v d) s) in Hm.
    assert (Hi1 : check_invariant_unpublished (Some n) s1).
    { apply (invariant_set_gen (Some n) None n (node_with_value v) d);
        [by apply invariant_weaken|done|done|done|intros; congruence|done|intros; congruence]. }
    assert (F1 : frame s s1) by (apply (frame_set n (node_with_value v) d); [done|done|intros; congruence]).
    destruct (extras_neutral n (node_with_value v) d s Hx Hd ltac:(unfold neutral; simpl; split_and!; first [reflexivity|done])) as [Hx1 E1].
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
    assert (Hfs : fields_same s1 (with_ghost G s1)) by (split; reflexivity).
    assert (Ham : any_mark f n None (with_ghost G s1) s2) by (right; right; right; left; done).
    destruct (extras_marks _ _ _ _ _ (extras_same _ _ Hfs Hx1) Ham) as [Hx2 E2].
    destruct (Rrequest _ IH _ _ _ (conj Hi2 Hx2) I Hm2) as [Hc3 [F3 E3]].
    split; [done|]. split.
    - eapply frame_trans; [exact F1|]. by eapply frame_trans.
    - eapply ekept_trans; [exact E1|]. eapply ekept_trans; [apply (ekept_same _ _ Hfs)|]. by eapply ekept_trans.
  Qed.

  Lemma outside_current s : outside_bodies s = true -> g_current s = None.
  Proof.
    unfold outside_bodies. destruct (g_current s); [|done].
    rewrite andb_false_r. simpl. done.
  Qed.

  Lemma ok2_request_S : ok2 (fun _ => True) (request_flush (S f)).
  Proof.
    cbn [request_flush]. apply ok2_get_at. intros s0.
    destruct (live_queue s0 && _); [|apply ok2_return].
    destruct (outside_bodies s0) eqn:Eo.
    - apply (ok2_weaken (fun s => g_current s = None)); [intros s [_ ->]; by apply outside_current|apply (Rflush _ IH)].
    - destruct (g_flushing s0); [apply ok2_return|ok2_mod].
  Qed.

  Lemma ok2_settle_S : ok2 (fun _ => True) (settle_flush (S f)).
  Proof.
    cbn [settle_flush]. apply ok2_get_at. intros s0.
    destruct (g_flush_owed s0 && _ && outside_bodies s0) eqn:Eo; [|apply ok2_return].
    apply (ok2_weaken (fun s => g_current s = None)); [|apply (Rflush _ IH)].
    intros s [_ ->]. apply outside_current. apply andb_prop in Eo as [_ H]. done.
  Qed.

  Lemma ok2_flush_S : ok2 (fun s => g_current s = None) (run_flush (S f)).
  Proof.
    apply (ok2_of (fun _ => True)); [done|apply (Eflush _ (engine_ok_all (S f)))|].
    intros s s' a Hi Hx Hcur Hm. cbn [run_flush] in Hm. rewrite bind_get in Hm.
    destruct (g_flushing s) eqn:Efl.
    { unfold m_return in Hm. simplify_eq. split; [done|apply ekept_refl]. }
    rewrite bind_m_modify in Hm. cbv beta in Hm.
    set (s1 := with_flush_owed false (with_flushing true s)) in Hm.
    destruct Hx as [Hb Ht].
    destruct Hb as [B1 B2 [E1 E2 E3 E4] B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 B16 B17 B18] eqn:Ebase.
    assert (Hb1 : run_base s1).
    { refine (Build_run_base s1 B1 B2 (Build_mark_env s1 E1 E2 E3 E4) B4 B5 B6 B7 B8 B9 B10 B11 B12 B13 B14 B15 _ B17 B18).
      intros c _ _. done. }
    assert (Hi1 : check_invariant s1).
    { eapply invariant_irrelevant; [|exact Hi]. unfold irrelevant. simpl. split_and!; reflexivity. }
    assert (Hmemo : forall c, c ∈ g_running s -> has_kind c KMemo s).
    { intros c Hc. destruct (B15 c Hc) as [K|K]; [done|]. pose proof (B16 c K Hc). congruence. }
    assert (Hctx1 : flush_ctx s1) by (split_and!; [exact Hcur|done|exact Hmemo]).
    destruct (run2_bind _ _ _ _ _ _ (Rdrain _ IH) (conj Hi1 (conj Hb1 Ht)) Hctx1 Hm) as (s2 & [] & _ & Hc2 & K2 & Hm2).
    pose proof (stable2 _ _ K2 Hctx1) as (_ & _ & Hmemo2).
    unfold m_modify in Hm2. injection Hm2 as <- <-.
    destruct Hc2 as [_ [Hb2 Ht2]]. destruct K2 as [_ [Kf Kc Kw]].
    destruct Hb2 as [C1 C2 [D1 D2 D3 D4] C4 C5 C6 C7 C8 C9 C10 C11 C12 C13 C14 C15 C16 C17 C18].
    split; [split|split].
    - refine (Build_run_base (with_flushing false s2) C1 C2 (Build_mark_env (with_flushing false s2) D1 D2 D3 D4) C4 C5 C6 C7 C8 C9 C10 C11 C12 C13 C14 C15 _ C17 C18).
      intros c Hk Hc. pose proof (has_kind_unique c _ _ s2 Hk (Hmemo2 c Hc)). done.
    - exact Ht2.
    - simpl. by rewrite Efl.
    - exact Kc.
    - exact Kw.
  Qed.

  Lemma ok2_drain_S : ok2 flush_ctx (drain (S f)).
  Proof.
    apply (ok2_of (fun _ => True)); [done|apply (Edrain _ (engine_ok_all (S f)))|].
    intros s s' a Hi Hx Hctx Hm. cbn [drain] in Hm. rewrite bind_get in Hm.
    destruct (g_queue s !! g_queue_head s) as [n|] eqn:Eq.
    - rewrite bind_m_modify in Hm. cbv beta in Hm.
      assert (Hin : n ∈ g_queue s) by (apply list_elem_of_lookup; by exists (g_queue_head s)).
      set (s1 := with_queue_head (S (g_queue_head s)) s) in Hm.
      assert (Hfs : fields_same s s1) by (split; reflexivity).
      assert (Hi1 : check_invariant s1).
      { eapply invariant_irrelevant; [|exact Hi]. unfold irrelevant. simpl. split_and!; reflexivity. }
      assert (Hx1 := extras_same _ _ Hfs Hx).
      assert (Hk : has_kind n KEffect s1) by apply (ci_queue _ _ _ Hi n Hin).
      assert (Hctx1 : effect_ctx n s1) by (split; [done|exact Hctx]).
      destruct (run2_bind _ _ _ _ _ _ (Rexecute _ IH n) (conj Hi1 Hx1) Hctx1 Hm) as (s2 & [] & _ & Hc2 & K2 & Hm2).
      pose proof (stable2 _ _ K2 Hctx1) as [_ Hctx2].
      destruct (Rdrain _ IH _ _ _ Hc2 Hctx2 Hm2) as [[_ Hx3] [_ E3]].
      split; [done|]. eapply ekept_trans; [apply (ekept_same _ _ Hfs)|]. eapply ekept_trans; [apply K2|exact E3].
    - unfold m_modify in Hm. injection Hm as <- <-.
      assert (Hfs : fields_same s (with_queue [] (with_queue_head 0 s))) by (split; reflexivity).
      split; [by eapply extras_same|by apply ekept_same].
  Qed.
End Step.

Lemma runs_ok_all f : runs_ok f.
Proof.
  induction f as [|f IH].
  - split; intros; try (intros s s' a _ _ Hm; done); try (intros s s' a _ _ Hm; destruct_all (option nat); done);
      match goal with H : _ = Some _ |- _ => done end.
  - split.
    + apply ok2_exec_S, IH.
    + apply ok2_read_S, IH.
    + apply ok2_read_node_S, IH.
    + apply ok2_update_S, IH.
    + apply ok2_refresh_or_pull_S, IH.
    + apply ok2_pull_S, IH.
    + apply ok2_refresh_S, IH.
    + apply ok2_walk_S, IH.
    + apply ok2_recompute_S, IH.
    + apply ok2_run_memo_S, IH.
    + apply ok2_execute_S, IH.
    + apply ok2_run_effect_S, IH.
    + apply ok2_body_S, IH.
    + apply ok2_discharge_S, IH.
    + apply ok2_retire_S, IH.
    + apply ok2_dispose_scope_S, IH.
    + apply ok2_teardown_S, IH.
    + apply ok2_cleanup_S, IH.
    + apply ok2_release_S, IH.
    + apply ok2_dispose_S, IH.
    + apply ok2_attach_S, IH.
    + apply ok2_write_S, IH.
    + apply ok2_request_S, IH.
    + apply ok2_settle_S, IH.
    + apply ok2_flush_S, IH.
    + apply ok2_drain_S, IH.
Qed.

(** ** The invariant, named *)

(** [RecomputeScoped]'s window of every live memo has closed. *)
Definition windows_closed (s : state) : Prop :=
  forall m d r, g_nodes s !! m = Some d -> gh_window (g_ghost s) !! m = Some r -> n_disposed d = false -> r < n_runs d.

(** The invariant a run keeps on the present engine. Besides [check_invariant] and [told], [run_base] holds the
    stack discipline, the cleanup windows and the lengths of the owners' cleanup lists. *)
Definition engine_settled (s : state) : Prop := run_core s /\ windows_closed s.

(** The invariant as the design states it, with [wake_ok] over every live source. *)
Record engine_inv (s : state) : Prop := {
  ei_settled : engine_settled s;
  ei_coherent : coherent (g_edges s);
  ei_wake : wake_ok s
}.

Lemma engine_inv_of_settled s : engine_settled s -> wake_ok s -> engine_inv s.
Proof.
  intros Hs Hw. split; [done| |done].
  exact (proj1 (reachable_well_formed _ (me_reachable _ (b_env _ (proj1 (proj2 (proj1 Hs))))))).
Qed.

Lemma windows_kept s s' : run_kept s s' -> windows_closed s -> windows_closed s'.
Proof.
  intros [_ E] H m d r Hd Hw Hl. destruct (decide (r < n_runs d)) as [Hlt|Hge]; [done|exfalso].
  destruct (k_window _ _ E m d r Hd Hw Hl ltac:(lia)) as (d0&r0&Hd0&Hw0&Hl0&Hr0).
  pose proof (H m d0 r0 Hd0 Hw0 Hl0). lia.
Qed.

(**md
From a state satisfying `R` and `P`, a completed step `m` ends in a state satisfying `R`.

- [R:] the property kept.
- [P:] the premise on the starting state.
- [m:] the step.
*)
Definition keeps {A} (R P : state -> Prop) (m : M A) : Prop :=
  forall s s' a, R s -> P s -> m s = Some (s', a) -> R s'.

(** [R] holds across each function of the engine, each under the premise it needs. *)
Record preserved_by (R : state -> Prop) (f : nat) : Prop := {
  Pexec : forall e c, keeps R (fun _ => True) (exec f e c);
  Pread : forall n b, keeps R (fun _ => True) (read f n b);
  Pread_node : forall n, keeps R (fun _ => True) (read_node f n);
  Pupdate : forall n, keeps R (fun _ => True) (update_if_necessary f n);
  Prefresh_or_pull : forall n, keeps R (has_kind n KMemo) (refresh_or_pull f n);
  Ppull : forall n, keeps R (has_kind n KMemo) (pull f n);
  Prefresh : forall n, keeps R (has_kind n KMemo) (refresh f n);
  Pwalk : forall n i, keeps R (fun _ => True) (resolve_walk f n i);
  Precompute : forall n, keeps R (has_kind n KMemo) (recompute f n);
  Prun_memo : forall n, keeps R (has_kind n KMemo) (run_memo f n);
  Pexecute : forall n, keeps R (effect_ctx n) (execute_effect f n);
  Prun_effect : forall n, keeps R (effect_ctx n) (run_effect f n);
  Pbody : forall n, keeps R (body_ctx n) (run_effect_body f n);
  Pdischarge : forall o, keeps R (fun _ => True) (discharge f o);
  Pretire : forall o, keeps R (fun _ => True) (retire f o);
  Pdispose_scope : forall o, keeps R (fun _ => True) (dispose_scope f o);
  Pteardown : forall o ls cs, keeps R (fun _ => True) (teardown f o ls cs);
  Pcleanup : forall o b e, keeps R (fun _ => True) (run_cleanup f o b e);
  Prelease : forall n, keeps R (fun _ => True) (release f n);
  Pdispose : forall n, keeps R (fun _ => True) (dispose_node f n);
  Pattach : forall n o, keeps R (fun _ => True) (attach f n o);
  Pwrite : forall n v, keeps R (fun _ => True) (write_signal f n v);
  Prequest : keeps R (fun _ => True) (request_flush f);
  Psettle : keeps R (fun _ => True) (settle_flush f);
  Pflush : keeps R (fun s => g_current s = None) (run_flush f);
  Pdrain : keeps R flush_ctx (drain f)
}.

Lemma keeps_of {A} P (m : M A) : ok2 P m -> keeps engine_settled P m.
Proof.
  intros H s s' a [Hc Hw] HP Hm. destruct (H _ _ _ Hc HP Hm) as [Hc' K]. split; [done|by eapply windows_kept].
Qed.

(** ** The invariant holds wherever a program goes *)

(**md
**The settled invariant holds at every return of every engine function, at every fuel.**

`engine_settled` is the invariant run-level proofs carry: the [check invariant](Ranvier.Pure.Check.html#check_invariant), the stack discipline, source-list bookkeeping, equal-length owner cleanup lists, an effect in its cleanups being on the stack, and the record of what each computation has been told.

Premises: the invariant holds at the start, and the function's own premise, as listed in `preserved_by`. A function that gets stuck or runs out of fuel returns nothing and is outside this guarantee. This is the form that holds on the present engine: `engine_inv_preserved_statement` adds `wake_ok`, and that form is false.

- [f:] the fuel.
*)
Theorem engine_settled_preserved f : preserved_by engine_settled f.
Proof.
  pose proof (runs_ok_all f) as H. split; intros; try (apply keeps_of; destruct H; eauto; fail).
  intros s s' a [[Hi Hx] Hw] Hc Hm.
  destruct (Rrun_effect _ H n s s' a Hi (proj1 Hx) ltac:(intros b c y dc e _; apply (proj2 Hx b c y dc e I)) Hc Hm) as [Hc' K].
  split; [done|by eapply windows_kept].
  intros s s' a [Hc Hw] HP Hm.
  destruct (Rbody _ H n s s' a Hc HP Hm) as (Hc' & K & _). split; [done|by eapply windows_kept].
Qed.

(**md
**A new graph with the current flags satisfies the settled invariant.**
*)
Theorem engine_settled_init : engine_settled (init current_flags).
Proof.
  assert (Hv : forall c, slot_view (g_edges (init current_flags)) c = ([], 0)) by (intros c; apply slot_view_empty).
  split; [split; [apply check_invariant_init|split]|].
  - split.
    + done.
    + done.
    + split; simpl.
      * apply reachable_empty.
      * intros c d Hd. rewrite lookup_empty in Hd. done.
      * intros c y Hy. change (y ∈ (slot_view (g_edges (init current_flags)) c).1) in Hy. rewrite Hv in Hy. by apply not_elem_of_nil in Hy.
      * intros c d Hd. rewrite lookup_empty in Hd. done.
    + intros n d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros c. rewrite Hv. cbn. lia.
    + intros c _. rewrite Hv. done.
    + intros c d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros c. rewrite Hv. intros i x Hi. done.
    + intros c d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros n d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros c d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros c d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros c d Hc. done.
    + intros c Hc. done.
    + intros c Hc. by apply not_elem_of_nil in Hc.
    + intros c _ Hc. by apply not_elem_of_nil in Hc.
    + intros o x. simpl. destruct (decide (o = root_owner_id)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-]. done.
      * rewrite lookup_insert_ne by done. rewrite lookup_empty. done.
    + split_and!.
      * intros n r Hr. simpl in Hr. rewrite lookup_empty in Hr. done.
      * intros n l Hl. simpl in Hl. rewrite lookup_empty in Hl. done.
      * intros n d Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
      * intros c _. done.
  - intros b c src dc e _ Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
  - intros m d r Hd. simpl in Hd. rewrite lookup_empty in Hd. done.
Qed.

(** ** No lost wake-ups *)

Lemma settled_cover b s src c e :
  engine_settled s -> edge s src c -> g_nodes s !! src = Some e -> stale (n_freshness e) = true -> n_disposed e = false ->
  c ∉ g_running s ->
  exists dc, g_nodes s !! c = Some dc /\
    (stale (n_freshness dc) = true \/ (b = true /\ n_unannounced e = true) \/ c ∈ resolved_set b (g_ghost s)).
Proof.
  intros [[Hi [Hb Ht]] Hw] He He1 Hst Hl Hnr.
  pose proof (me_reachable _ (b_env _ Hb)) as Hr.
  assert (Hin : src ∈ fst <$> source_slots (sources_of (g_edges s) c)).
  { by apply edge_iff_read; [apply reachable_well_formed|]. }
  assert (Hin' : src ∈ (slot_view (g_edges s) c).1) by exact Hin.
  assert (Hpre : src ∈ read_prefix (g_edges s) c).
  { rewrite read_prefix_view, (b_ended _ Hb c Hnr), take_ge; [done|lia]. }
  destruct (ci_slots _ _ _ Hi c src Hin') as [[dc Hdc] _].
  destruct (n_disposed dc) eqn:Edc.
  { rewrite (b_disposed _ Hb c dc Hdc Edc Hnr) in Hin'. by apply not_elem_of_nil in Hin'. }
  exists dc. split; [done|].
  destruct (Ht b c src dc e I Hdc He1 Hpre Hst Hl Edc) as [C|[C|[C|C]]]; auto.
  destruct C as (r&Hwr&Hle). pose proof (Hw c dc r Hdc Hwr Edc). lia.
Qed.

Lemma wake_of_settled s : engine_settled s -> wake_ok_unless_passed s.
Proof.
  intros Hs src c He (e&He1&Hst) (e'&He2&Hl).
  rewrite He1 in He2. injection He2 as <-.
  destruct (decide (c ∈ g_running s)) as [Hrun|Hnr].
  { left. right. left. by apply list_elem_of_In. }
  destruct (settled_cover true s src c e Hs He He1 Hst Hl Hnr) as (dc&Hdc&[C|[C|C]]).
  - left. left. left. by exists dc.
  - left. right. right. unfold n_unannounced_of. rewrite He1. simpl. apply C.
  - by right.
Qed.

Lemma told_of_settled s : engine_settled s -> wake_told s.
Proof.
  intros Hs src c He (e&He1&Hst) (e'&He2&Hl).
  rewrite He1 in He2. injection He2 as <-.
  destruct (decide (c ∈ g_running s)) as [Hrun|Hnr].
  { right. left. by apply list_elem_of_In. }
  destruct (settled_cover false s src c e Hs He He1 Hst Hl Hnr) as (dc&Hdc&[C|[C|C]]).
  - left. left. by exists dc.
  - by destruct C.
  - by right; right.
Qed.

(**md
**Every live stale source has its observers covered, apart from observers a walk resolved over a stale source.**

For any edge from a live, stale source to an observer, after a whole program: the observer is stale or queued, or on the running stack, or the source holds its announcement flag, or the observer is in `gh_passed`. `gh_passed` holds exactly the computations whose check walk ended and went `Clean` while a slot held a live, stale source with no announcement, since their latest run start. Membership is per computation, not per source: once an observer has passed over one source, the exception covers every source of that observer until its next run starts, a different lost wake-up on another source included.

This says nothing of whether an observer read a source's latest value. A skipped notice leaves the source `Clean`, so it has no stale edge to cover, and [`effect_resolves_over_moved_source`](Ranvier.Pure.Witnesses.html#effect_resolves_over_moved_source) loses a re-run that way with this statement holding.

Premise: `engine_settled` holds at the start. A program that gets stuck or runs out of fuel returns nothing, and is outside this guarantee.

[`walk_passed_source_marked`](Ranvier.Pure.Witnesses.html#walk_passed_source_marked) reaches `gh_passed`, and `no_lost_wakeups_statement` without the exception is false.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state, which satisfies `engine_settled`.
- [s':] the state after the run.
- [r:] the outcome of the run.
*)
Theorem no_lost_wakeups_unless_passed f c s s' r :
  engine_settled s -> run_top f c s = Some (s', r) -> wake_ok_unless_passed s'.
Proof.
  intros Hs Hm. apply wake_of_settled. exact (Pexec _ _ (engine_settled_preserved f) [] c _ _ _ Hs I Hm).
Qed.

(**md
**A reader of a live stale source has been told, unless a walk of its went `Clean` over a stale source.**

For any edge from a live, stale source to an observer, after a whole program: the observer is stale or queued, or on the running stack, or in `gh_over`, which holds the computations whose check walk ended and went `Clean` while a slot held a live, stale source, since their latest run start. The source's announcement flag is not a disjunct here, so a reader that read a memo left stale and took no `MarkReaderCheck` breaks this statement: [`reader_mark_off_fires`](Ranvier.Pure.Witnesses.html#reader_mark_off_fires).

Premise: `engine_settled` holds at the start.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state, which satisfies `engine_settled`.
- [s':] the state after the run.
- [r:] the outcome of the run.
*)
Theorem no_wake_lost_unless_resolved f c s s' r :
  engine_settled s -> run_top f c s = Some (s', r) -> wake_told s'.
Proof.
  intros Hs Hm. apply told_of_settled. exact (Pexec _ _ (engine_settled_preserved f) [] c _ _ _ Hs I Hm).
Qed.

(**md
**No lost wake-ups, when no walk has passed over a stale source.**

After a whole program whose final `gh_passed` is empty, every observer of a live, stale source is stale, queued or running, or the source holds its announcement flag. No theorem yet says which programs keep `gh_passed` empty.

Premises: `engine_settled` holds at the start and `nothing_passed` holds at the end. The premise rules out one route to a lost wake-up by name, so this is a statement about every other route to an uncovered edge. It does not say a stale node's next run will happen.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state, which satisfies `engine_settled`.
- [s':] the state after the run, in which nothing has passed.
- [r:] the outcome of the run.
*)
Theorem no_lost_wakeups_when_nothing_passed f c s s' r :
  engine_settled s -> run_top f c s = Some (s', r) -> nothing_passed s' -> wake_ok s'.
Proof.
  intros Hs Hm Hn src x He Hst Hl. destruct (no_lost_wakeups_unless_passed f c s s' r Hs Hm src x He Hst Hl) as [H|H]; [done|].
  rewrite Hn in H. set_solver.
Qed.

(** The headline statement, which the fix phase owes. [walk_passed_source_marked] in [BUGS.md] is its counterexample
    on the present engine. *)
Definition no_lost_wakeups_statement : Prop :=
  forall f c s s' r, engine_inv s -> run_top f c s = Some (s', r) -> wake_ok s'.

(**md
**Open statement, false on the present engine: every program keeps every observer of a live stale source covered.**

`no_lost_wakeups_statement` is what the fix phase owes. The Confirmed bug [`walk_passed_source_marked`](Ranvier.Pure.Witnesses.html#walk_passed_source_marked) falsifies it, and [`no_lost_wakeups_falsified`](Ranvier.Pure.Witnesses.html#no_lost_wakeups_falsified) proves that. Until the fix, later layers use [`no_lost_wakeups_when_nothing_passed`](#no_lost_wakeups_when_nothing_passed).
*)

(** The invariant of the design across every engine function, which the fix phase owes. It fails where the
    headline fails. *)
Definition engine_inv_preserved_statement : Prop := forall f, preserved_by engine_inv f.

(**md
**Open statement, false on the present engine: every engine function keeps the invariant with `wake_ok`.**

`engine_inv_preserved_statement` is what the fix phase owes. [`engine_inv_preserved_falsified`](Ranvier.Pure.Witnesses.html#engine_inv_preserved_falsified) proves it false. Until the fix, later layers use [`engine_settled_preserved`](#engine_settled_preserved).
*)

(**md
**A new graph with the current flags satisfies the invariant with `wake_ok`.**
*)
Theorem engine_inv_init : engine_inv (init current_flags).
Proof.
  split; [apply engine_settled_init|apply coherent_empty|].
  intros src c _ (d&Hd&_). simpl in Hd. rewrite lookup_empty in Hd. done.
Qed.

(** ** A later mark reaches every observer *)

Lemma covered_rises s s' c :
  rtc mark_prim s s' -> stale_or_queued s c \/ In c (g_running s) -> stale_or_queued s' c \/ In c (g_running s').
Proof.
  intros Hr H. pose proof (marks_fixed _ _ Hr) as Hf. rewrite (mf_running _ _ Hf).
  destruct H as [[(d&Hd&Hs)|(d&Hd&Hq)]|H]; [left; left|left; right|by right].
  - destruct (mf_forward _ _ Hf c d Hd) as (d'&Hd'&_&Hle&_&_). exists d'. split; [done|]. by eapply stale_rises.
  - destruct (mf_forward _ _ Hf c d Hd) as (d'&Hd'&_&_&Hq'&_). exists d'. auto.
Qed.

(**md
**A later mark of a stale or clean live memo leaves every observer stale, queued or running.**

From `wake_ok`, when `mark_check` or `mark_dirty` of a live memo returns, each computation with an edge from the memo is stale or queued, or on the running stack. An effect in its cleanups, or disposed mid-body, ignores the mark, and is on the running stack: `engine_settled` carries that.

Premises: the [invariant with `wake_ok`](#engine_inv) holds, and the memo is live. `wake_ok` says nothing of a disposed source, whose reader can be uncovered: see `disposed_stale_source_read`.

- [f:] the fuel.
- [src:] the live memo marked.
- [d:] the node `src` in `s`.
- [s:] the state before the marking, which satisfies `engine_inv`.
- [s':] the state after the marking.
*)
Theorem later_mark_forwarded f src d s s' :
  engine_inv s -> g_nodes s !! src = Some d -> n_kind d = KMemo -> n_disposed d = false ->
  (mark_check f src s = Some s' \/ mark_dirty f src s = Some s') ->
  forall c, edge s src c -> stale_or_queued s' c \/ In c (g_running s').
Proof.
  intros [[[Hi [Hb Ht]] Hw] Hco Hwake] Hd Hk Hl Hm c He.
  pose proof (me_reachable _ (b_env _ Hb)) as Hr.
  assert (Hrt : rtc mark_prim s s').
  { destruct Hm as [Hm|Hm]; [by eapply mark_check_rtc|by eapply mark_dirty_rtc]. }
  assert (Hobs : c ∈ observer_view (observers_of (g_edges s) src)).
  { apply (edge_iff_both_sides s src c Hco). done. }
  assert (Hslot : src ∈ (slot_view (g_edges s) c).1).
  { assert (H : src ∈ fst <$> source_slots (sources_of (g_edges s) c)).
    { by apply edge_iff_read; [apply reachable_well_formed|]. }
    exact H. }
  destruct (decide (c ∈ g_running s)) as [Hrun|Hnr].
  { right. rewrite (mf_running _ _ (marks_fixed _ _ Hrt)). by apply list_elem_of_In. }
  assert (Htakes : takes_marks s c).
  { destruct (ci_slots _ _ _ Hi c src Hslot) as [[dc Hdc] _]. exists dc. split; [done|].
    destruct (n_kind dc) eqn:Ek; [|by left|right].
    - rewrite (me_signal _ (b_env _ Hb) c dc Hdc Ek) in Hslot. by apply not_elem_of_nil in Hslot.
    - split_and!; [done| |].
      + destruct (n_disposed dc) eqn:Edi; [|done]. rewrite (b_disposed _ Hb c dc Hdc Edi Hnr) in Hslot. by apply not_elem_of_nil in Hslot.
      + destruct (n_in_cleanup dc) eqn:Ecl; [|done]. destruct Hnr. eapply (b_cleanup_running _ Hb); [exact Hdc|done]. }
  destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
  { destruct (mark_forwards_on_clean f src d _ s s' Hd Hk Hc eq_refl Hm) as [_ H].
    destruct (H c Hobs Htakes) as (e&He1&He2). left. left. by exists e. }
  destruct (n_unannounced d) eqn:Eu.
  { destruct (mark_forwards_on_unannounced f src d _ s s' (b_flag_u _ Hb) Hd Hk ltac:(by apply stale_of_not_clean) Eu eq_refl Hm) as [_ H].
    destruct (H c Hobs Htakes) as (e&He1&He2). left. left. by exists e. }
  apply (covered_rises s s' c Hrt).
  destruct (Hwake src c He ltac:(exists d; split; [done|by apply stale_of_not_clean]) ltac:(by exists d)) as [H|[H|H]].
  - by left.
  - by right.
  - exfalso. unfold n_unannounced_of in H. rewrite Hd in H. simpl in H. congruence.
Qed.

(** ** The end of a memo's run *)

(**md
The node `x` as `run_memo` leaves it at the start of a run: one more run counted, no failure, status
`StNone`, no announcement and `Clean`.
*)
Definition run_node (x : node) : node :=
  node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone
    (node_with_unannounced false (node_with_freshness Clean x)))).

(**md
The state a run's body starts from: node `n` replaced by `run_node d`, its cursor at the start of its slots, its
ghost run started, and `n` pushed onto the running list.

- [n:] the memo that runs.
- [d:] the node `n` before the run.
- [s:] the state before the run.
*)
Definition run_start (n : node_id) (d : node) (s : state) : state :=
  let s1 := set_node n (run_node d) s in
  let s3 := ghost_run_start n (with_edges (sources_begin_run n (g_edges s1)) s1) in
  with_running (n :: g_running s3) s3.

(**md
The environment a memo's body runs in: the node's captured environment, with the previous value bound to
`prev_var` when the node has published, and without any previous value otherwise.

- [d:] the memo before the run.
*)
Definition run_env (d : node) : env :=
  if n_published d then (prev_var, n_value d) :: n_env d else without_prev (n_env d).

(** What the steps from a run's body to its notice hold of the state. *)
Record run_same (n : node_id) (s s' : state) : Prop := {
  rs_others : forall m, m <> n -> g_nodes s' !! m = g_nodes s !! m;
  rs_queue : g_queue s' = g_queue s;
  rs_current : g_current s' = g_current s;
  rs_flags : g_flags s' = g_flags s;
  rs_node : forall d, g_nodes s !! n = Some d -> exists d', g_nodes s' !! n = Some d' /\
    n_freshness d' = n_freshness d /\ n_kind d' = n_kind d /\ n_disposed d' = n_disposed d /\
    n_in_cleanup d' = n_in_cleanup d /\ n_comparer d' = n_comparer d
}.

Lemma run_same_refl n s : run_same n s s.
Proof. split; try done. intros d Hd. exists d. done. Qed.

Lemma run_same_trans n s1 s2 s3 : run_same n s1 s2 -> run_same n s2 s3 -> run_same n s1 s3.
Proof.
  intros [H1 H2 H3 H4 H5] [J1 J2 J3 J4 J5]. split; try congruence.
  - intros m Hm. rewrite J1, H1; done.
  - intros d Hd. destruct (H5 d Hd) as (d1&Hd1&A1&A2&A3&A4&A5). destruct (J5 d1 Hd1) as (d2&Hd2&B1&B2&B3&B4&B5).
    exists d2. split_and!; try congruence.
Qed.

Lemma run_same_set n u d s :
  g_nodes s !! n = Some d -> n_freshness (u d) = n_freshness d -> n_kind (u d) = n_kind d ->
  n_disposed (u d) = n_disposed d -> n_in_cleanup (u d) = n_in_cleanup d -> n_comparer (u d) = n_comparer d ->
  run_same n s (set_node n (u d) s).
Proof.
  intros Hd H1 H2 H3 H4 H5. split; try done.
  - intros m Hm. simpl. by rewrite lookup_insert_ne.
  - intros e He. rewrite Hd in He. injection He as <-. exists (u d). split_and!; try done. simpl. apply lookup_insert_eq.
Qed.

Lemma bind_inv {A B} (m : M A) (k : A -> M B) s x :
  m_bind m k s = Some x -> exists s1 a, m s = Some (s1, a) /\ k a s1 = Some x.
Proof. unfold m_bind. destruct (m s) as [[s1 a]|]; [eauto|done]. Qed.

(** The state at a memo's run ending, ahead of its notice. [mv] is the run's verdict. *)
Definition run_end (n : node_id) (d : node) (s2 s10 : state) (mv : bool) : Prop :=
  run_same n s2 s10 /\ exists d2 d10, g_nodes s2 !! n = Some d2 /\ g_nodes s10 !! n = Some d10 /\
    (mv = false <-> read_view_of d10 = read_view_of d /\ n_comparer d10 = CmpEq).

(** The notice and the flag after that. *)
Definition run_finish (f : nat) (n : node_id) (mv : bool) : M unit :=
  let* s := m_get in
  let* _ := m_modify (ghost_run_end n (g_current s) mv) in
  let* _ := if mv then m_step (notify_dirty_except f n (g_current s)) else m_return tt in
  let* d3 := get_node n in
  if decide (n_freshness d3 = Clean) then m_return tt else announce n.

Lemma run_memo_shape f n s s' d :
  g_nodes s !! n = Some d -> run_memo (S f) n s = Some (s', tt) ->
  exists s2 r s10 mv, hosted n (exec f (run_env d) (n_body d)) (run_start n d s) = Some (s2, r) /\
    run_end n d s2 s10 mv /\ run_finish f n mv s10 = Some (s', tt).
Proof.
  intros Hd Hm. cbn [run_memo] in Hm.
  rewrite (bind_get_node _ _ _ _ Hd), (bind_modify_node _ _ _ _ _ Hd) in Hm.
  unfold edges, push_running in Hm. rewrite !bind_m_modify in Hm.
  apply bind_inv in Hm as (s2 & r & Eh & Hm).
  exists s2, r.
  destruct (g_nodes s2 !! n) as [d1|] eqn:Hd1; [|unfold m_bind, get_node in Hm; by rewrite Hd1 in Hm].
  rewrite (bind_get_node _ _ _ _ Hd1) in Hm.
  set (result := if n_violated d1 then Fail pure_scope_failure else r) in Hm.
  name_k K6.
  assert (Hs6 : exists s6, K6 tt s6 = Some (s', tt) /\ run_same n s2 s6).
  { destruct result as [v|y].
    - rewrite (bind_modify_node _ _ _ _ _ Hd1) in Hm. eexists. split; [exact Hm|].
      apply (run_same_set n (fun x => node_with_published true (node_with_value v x)) d1); done.
    - rewrite bind_m_return in Hm. eexists. split; [exact Hm|apply run_same_refl]. }
  clear Hm. destruct Hs6 as (s6 & Hm & R6). subst K6. cbv beta in Hm.
  apply bind_edges_opt in Hm as (w & _ & Hm).
  unfold pop_running in Hm. rewrite bind_m_modify in Hm.
  set (s8 := with_running _ (with_edges w s6)) in Hm.
  assert (R8 : run_same n s2 s8).
  { eapply run_same_trans; [exact R6|]. split; try done. intros e He. exists e. done. }
  name_k K9.
  assert (Hs9 : exists s9, K9 tt s9 = Some (s', tt) /\ run_same n s2 s9).
  { destruct result as [v|y].
    - rewrite bind_m_return in Hm. eexists. split; [exact Hm|done].
    - destruct (g_nodes s8 !! n) as [e|] eqn:He; [|unfold m_bind, modify_node in Hm; by rewrite He in Hm].
      rewrite (bind_modify_node _ _ _ _ _ He) in Hm. eexists. split; [exact Hm|].
      eapply run_same_trans; [exact R8|].
      apply (run_same_set n (fun x => node_with_violated false (node_with_status StError (node_with_failure (Some y) x))) e); done. }
  clear Hm. destruct Hs9 as (s9 & Hm & R9). subst K9. cbv beta in Hm.
  destruct (rs_node _ _ _ R9 d1 Hd1) as (d2 & Hd2 & _).
  rewrite (bind_get_node _ _ _ _ Hd2) in Hm.
  name_k K10.
  assert (Hs10 : exists s10 mv, K10 mv s10 = Some (s', tt) /\ run_same n s2 s10 /\
      exists d10, g_nodes s10 !! n = Some d10 /\
        (mv = false <-> read_view_of d10 = read_view_of d /\ n_comparer d10 = CmpEq)).
  { destruct (decide _) as [Hne|Hsame].
    - rewrite bind_m_return in Hm. do 2 eexists. split; [exact Hm|]. split; [done|]. exists d2. split; [done|].
      split; [done|]. intros [Hv _]. unfold read_view_of in Hv. injection Hv as _ H1 H2. exfalso. destruct Hne as [Hn|Hn]; by apply Hn.
    - destruct (n_comparer d2) as [| |y] eqn:Ec.
      + rewrite bind_m_return in Hm. do 2 eexists. split; [exact Hm|]. split; [done|]. exists d2. split; [done|].
        assert (Hs1 : n_status d2 = n_status d).
        { destruct (decide (n_status d2 = n_status d)); [done|]. exfalso. apply Hsame. by left. }
        assert (Hs2 : n_failure d2 = n_failure d).
        { destruct (decide (n_failure d2 = n_failure d)); [done|]. exfalso. apply Hsame. by right. }
        split.
        * intros Hb. apply bool_decide_eq_false in Hb. split; [|done]. unfold read_view_of.
          apply dec_stable in Hb. by rewrite Hb, Hs1, Hs2.
        * intros [Hv _]. apply bool_decide_eq_false. unfold read_view_of in Hv. injection Hv as H0 _ _. by intros Hx.
      + rewrite bind_m_return in Hm. do 2 eexists. split; [exact Hm|]. split; [done|]. exists d2. split; [done|].
        split; [done|]. intros [_ Hx]. congruence.
      + rewrite bind_bind in Hm. rewrite (bind_modify_node _ _ _ _ _ Hd2) in Hm. rewrite bind_m_return in Hm.
        do 2 eexists. split; [exact Hm|].
        split.
        * eapply run_same_trans; [exact R9|].
          apply (run_same_set n (fun x => node_with_status StError (node_with_failure (Some y) (node_with_value (n_value d) x))) d2); done.
        * eexists. split; [simpl; apply lookup_insert_eq|]. split; [done|]. intros [_ Hx]. simpl in Hx. congruence. }
  clear Hm. destruct Hs10 as (s10 & mv & Hm & R10 & d10 & Hd10 & Hmv). subst K10. cbv beta in Hm.
  exists s10, mv. split_and!; [exact Eh| |exact Hm].
  split; [done|]. eauto.
Qed.

Lemma ghost_run_end_fields n r mv s :
  let sg := ghost_run_end n r mv s in
  g_edges sg = g_edges s /\ g_nodes sg = g_nodes s /\ g_queue sg = g_queue s /\ g_flags sg = g_flags s /\ g_current sg = g_current s.
Proof. unfold ghost_run_end. by destruct (g_nodes s !! n). Qed.

(** The states from a run's ending to its return. *)
Lemma run_finish_shape f n mv s10 s' :
  run_finish f n mv s10 = Some (s', tt) ->
  exists sg s11 d4,
    g_edges sg = g_edges s10 /\ g_nodes sg = g_nodes s10 /\ g_queue sg = g_queue s10 /\ g_flags sg = g_flags s10 /\
    g_current sg = g_current s10 /\
    ((mv = false /\ s11 = sg) \/ (mv = true /\ notify_dirty_except f n (g_current s10) sg = Some s11)) /\
    g_nodes s11 !! n = Some d4 /\
    (s' = s11 \/ (n_freshness d4 <> Clean /\ s' = set_node n (node_with_unannounced true d4) s11)) /\
    (n_freshness d4 <> Clean -> f_unannounced (g_flags s11) = true -> s' = set_node n (node_with_unannounced true d4) s11).
Proof.
  unfold run_finish. rewrite bind_get, bind_m_modify. cbv beta.
  destruct (ghost_run_end_fields n (g_current s10) mv s10) as (F1&F2&F3&F4&F5).
  set (sg := ghost_run_end n (g_current s10) mv s10) in *.
  intros Hm. exists sg.
  assert (Hs : exists s11, ((mv = false /\ s11 = sg) \/ (mv = true /\ notify_dirty_except f n (g_current s10) sg = Some s11)) /\
      (let* d3 := get_node n in if decide (n_freshness d3 = Clean) then m_return tt else announce n) s11 = Some (s', tt)).
  { destruct mv.
    - apply bind_step in Hm as (s11 & Hn & Hm). exists s11. split; [by right|exact Hm].
    - rewrite bind_m_return in Hm. exists sg. split; [by left|exact Hm]. }
  destruct Hs as (s11 & Hc & Hm2). exists s11.
  destruct (g_nodes s11 !! n) as [d4|] eqn:Hd4; [|unfold m_bind, get_node in Hm2; by rewrite Hd4 in Hm2].
  exists d4. rewrite (bind_get_node _ _ _ _ Hd4) in Hm2.
  split_and!; [done|done|done|done|done|done|done| |].
  - destruct (decide _) as [Hcl|Hcl].
    + left. unfold m_return in Hm2. by injection Hm2.
    + unfold announce in Hm2. destruct (f_unannounced (g_flags s11)).
      * right. split; [done|]. unfold modify_node in Hm2. rewrite Hd4 in Hm2. by injection Hm2.
      * left. by injection Hm2.
  - intros Hcl Hfl. rewrite decide_False in Hm2 by done. unfold announce in Hm2. rewrite Hfl in Hm2.
    unfold modify_node in Hm2. rewrite Hd4 in Hm2. by injection Hm2.
Qed.

Lemma notify_except_fixed f n r s s' : notify_dirty_except f n r s = Some s' -> mark_fixed s s'.
Proof. intros H. apply marks_fixed. apply (any_mark_rtc f n r). by right; right; right; right. Qed.

Lemma hosted_current {A} n (body : M A) s0 s2 r : hosted n body s0 = Some (s2, r) -> g_current s2 = g_current s0.
Proof.
  unfold hosted, m_bind, m_modify, m_return. destruct (body _) as [[s1 x]|]; [|done]. intros Hm. by simplify_eq.
Qed.

(** ** What a memo's run guarantees *)

(** [p] runs [c] up to a point, then returns. A branch of [p] holds a point in that branch of [c]. Points lie
    between the commands of a body, so a mark set and cleared inside one command is out of reach. *)
Inductive body_prefix : cmd -> cmd -> Prop :=
  | prefix_stop c : body_prefix (CRet (EConst 0%Z)) c
  | prefix_let x e p k : body_prefix p k -> body_prefix (CLet x e p) (CLet x e k)
  | prefix_if e p1 p2 a b : body_prefix p1 a -> body_prefix p2 b -> body_prefix (CIf e p1 p2) (CIf e a b)
  | prefix_read x n p k : body_prefix p k -> body_prefix (CRead x n p) (CRead x n k)
  | prefix_try x ok n p k : body_prefix p k -> body_prefix (CTryRead x ok n p) (CTryRead x ok n k)
  | prefix_untracked x n p k : body_prefix p k -> body_prefix (CReadUntracked x n p) (CReadUntracked x n k)
  | prefix_write n e p k : body_prefix p k -> body_prefix (CWrite n e p) (CWrite n e k)
  | prefix_signal x e p k : body_prefix p k -> body_prefix (CSignal x e p) (CSignal x e k)
  | prefix_memo x m q b p k : body_prefix p k -> body_prefix (CMemo x m q b p) (CMemo x m q b k)
  | prefix_effect x b p k : body_prefix p k -> body_prefix (CEffect x b p) (CEffect x b k)
  | prefix_cleanup b p k : body_prefix p k -> body_prefix (CCleanup b p) (CCleanup b k)
  | prefix_dispose n p k : body_prefix p k -> body_prefix (CDispose n p) (CDispose n k)
  | prefix_batch b p k : body_prefix p k -> body_prefix (CBatch b p) (CBatch b k)
  | prefix_log e p k : body_prefix p k -> body_prefix (CLog e p) (CLog e k).

(** A memo stale at a point in its run's body is stale at the run's end, holding [n_unannounced]. The fix phase
    owes this. [nested_self_recompute_resets_cursor] in [BUGS.md] is its counterexample on the present engine. *)
Definition marks_survive_run_statement : Prop :=
  forall f n s s' s_p r_p d p,
    engine_settled s -> g_nodes s !! n = Some d -> n_kind d = KMemo -> body_prefix p (n_body d) ->
    hosted n (exec f (run_env d) p) (run_start n d s) = Some (s_p, r_p) -> stale_of s_p n ->
    run_memo (S f) n s = Some (s', tt) ->
    stale_of s' n /\ n_unannounced_of s' n = true.

(**md
**Open statement, false on the present engine: a mark received during a run leaves the memo stale at the run's end, holding its announcement.**

A point in the run is a point in the body's commands, stated by running a `body_prefix` of the body. `marks_survive_run_statement` is what the fix phase owes. A mark set and cleared inside one command, such as a batch body, a nested call or the discharge window, is outside the statement. The Confirmed bug `nested_self_recompute_resets_cursor` falsifies it, and [`marks_survive_run_falsified`](Ranvier.Pure.Witnesses.html#marks_survive_run_falsified) proves that: a later self-read runs the memo again inside its run, and that nested run clears the mark. Until the fix, later layers use [`marks_survive_body_end`](#marks_survive_body_end).
*)

(**md
**A memo stale when its body returns is stale when its run returns, holding its announcement.**

This covers only the tail of a run, from the body's return to the run's. If the memo is stale at that point, it is still stale when `run_memo` returns, with `n_unannounced` set, so its next mark tells its observers again. A mark received earlier in the body can be cleared before the body returns: see `marks_survive_run_statement`.

Premises: the [settled invariant](#engine_settled) holds at the start, the node is a memo, and `run_memo` returns.

- [f:] the fuel, which `run_memo` runs at `S f`.
- [n:] the memo that runs.
- [s:] the state before the run, which satisfies `engine_settled`.
- [s':] the state after the run.
- [s2:] the state when the body returns.
- [r:] the outcome of the body.
- [d:] the node `n` in `s`.
*)
Theorem marks_survive_body_end f n s s' s2 r d :
  engine_settled s -> g_nodes s !! n = Some d -> n_kind d = KMemo ->
  hosted n (exec f (run_env d) (n_body d)) (run_start n d s) = Some (s2, r) ->
  run_memo (S f) n s = Some (s', tt) ->
  stale_of s2 n -> stale_of s' n /\ n_unannounced_of s' n = true.
Proof.
  intros [Hc Hw] Hd Hk Eh Hm (d2 & Hd2 & Hst).
  destruct (Rrun_memo _ (runs_ok_all (S f)) n _ _ _ Hc ltac:(by exists d) Hm) as [[_ [Hb' _]] _].
  destruct (run_memo_shape f n s s' d Hd Hm) as (s2' & r' & s10 & mv & Eh' & [R10 _] & Hf).
  rewrite Eh in Eh'. injection Eh' as <- <-.
  destruct (rs_node _ _ _ R10 d2 Hd2) as (d10 & Hd10 & Hfr & _).
  destruct (run_finish_shape f n mv s10 s' Hf) as (sg & s11 & d4 & G1 & G2 & G3 & G4 & G5 & Hn & Hd4 & Hend & Hend2).
  assert (Hfx : mark_fixed sg s11).
  { destruct Hn as [[_ ->]|[_ Hn]]; [apply mark_fixed_refl|by eapply notify_except_fixed]. }
  assert (Hdg : g_nodes sg !! n = Some d10) by (by rewrite G2).
  destruct (mf_forward _ _ Hfx n d10 Hdg) as (e&He&_&Hle&_&_).
  rewrite Hd4 in He. injection He as <-.
  assert (Hst4 : stale (n_freshness d4) = true) by (eapply stale_rises; [exact Hle|congruence]).
  assert (Hnc : n_freshness d4 <> Clean) by (intros Hx; rewrite Hx in Hst4; done).
  assert (Hfl : f_unannounced (g_flags s11) = true).
  { pose proof (b_flag_u _ Hb') as Hf'. destruct Hend as [->|[_ ->]]; exact Hf'. }
  rewrite (Hend2 Hnc Hfl). split.
  - exists (node_with_unannounced true d4). split; [simpl; apply lookup_insert_eq|done].
  - unfold n_unannounced_of. simpl. rewrite lookup_insert_eq. done.
Qed.

(**md
**A run whose value, status and failure hold still marks no observer.**

Each node's freshness and the effect queue are as they were when the body returned. Marks raised during the body, by writes in it or by other nodes' runs, are outside this statement, which covers what the end of the run adds. A memo with `CmpNever` or `CmpThrows` tells its observers whatever the value: that is what those comparers ask for.

Premises: the node is a memo with the `CmpEq` comparer, its stored view at the return of `run_memo` equals the view before the run, and `run_memo` returns.

- [f:] the fuel, which `run_memo` runs at `S f`.
- [n:] the memo that runs.
- [s:] the state before the run.
- [s':] the state after the run.
- [s2:] the state when the body returns.
- [r:] the outcome of the body.
- [d:] the node `n` in `s`.
- [d':] the node `n` in `s'`, whose view equals that of `d`.
*)
Theorem cutoff f n s s' s2 r d d' :
  g_nodes s !! n = Some d ->
  hosted n (exec f (run_env d) (n_body d)) (run_start n d s) = Some (s2, r) ->
  run_memo (S f) n s = Some (s', tt) ->
  g_nodes s' !! n = Some d' -> read_view_of d' = read_view_of d -> n_comparer d' = CmpEq ->
  (forall m, n_freshness <$> g_nodes s' !! m = n_freshness <$> g_nodes s2 !! m) /\ g_queue s' = g_queue s2.
Proof.
  intros Hd Eh Hm Hd' Hv Hcmp.
  destruct (run_memo_shape f n s s' d Hd Hm) as (s2' & r' & s10 & mv & Eh' & [R10 (d2 & d10 & Hd2 & Hd10 & Hmv)] & Hf).
  rewrite Eh in Eh'. injection Eh' as <- <-.
  destruct (run_finish_shape f n mv s10 s' Hf) as (sg & s11 & d4 & G1 & G2 & G3 & G4 & G5 & Hn & Hd4 & Hend & _).
  assert (Hfx : mark_fixed sg s11).
  { destruct Hn as [[_ ->]|[_ Hn]]; [apply mark_fixed_refl|by eapply notify_except_fixed]. }
  assert (Hdg : g_nodes sg !! n = Some d10) by (by rewrite G2).
  destruct (mf_forward _ _ Hfx n d10 Hdg) as (e&He&(_&_&_&_&_&_&_&Hc4)&_&_&Hv4).
  rewrite Hd4 in He. injection He as <-.
  assert (Hd'4 : read_view_of d' = read_view_of d4 /\ n_comparer d' = n_comparer d4).
  { destruct Hend as [->|[_ ->]].
    - rewrite Hd4 in Hd'. by injection Hd' as <-.
    - simpl in Hd'. rewrite lookup_insert_eq in Hd'. injection Hd' as <-. done. }
  destruct Hd'4 as [V1 V2].
  assert (Hmvf : mv = false) by (apply Hmv; split; congruence).
  destruct Hn as [[_ ->]|[Hx _]]; [|congruence].
  assert (Hothers : forall m, m <> n -> g_nodes s' !! m = g_nodes s2 !! m).
  { intros m Hne. rewrite <- (rs_others _ _ _ R10 m Hne), <- G2.
    destruct Hend as [->|[_ ->]]; [done|]. simpl. by rewrite lookup_insert_ne. }
  split.
  - intros m. destruct (decide (m = n)) as [->|Hne]; [|by rewrite Hothers].
    rewrite Hd', Hd2. simpl. f_equal.
    destruct (rs_node _ _ _ R10 d2 Hd2) as (e&He&Hfr&_). rewrite Hd10 in He. injection He as <-.
    assert (d4 = d10) by (rewrite G2, Hd10 in Hd4; by injection Hd4). subst d4.
    destruct Hend as [->|[_ ->]].
    + rewrite G2, Hd10 in Hd'. injection Hd' as <-. done.
    + simpl in Hd'. rewrite lookup_insert_eq in Hd'. injection Hd' as <-. done.
  - destruct Hend as [->|[_ ->]]; rewrite ?G3; [apply R10|simpl; rewrite G3; apply R10].
Qed.

(**md
**A moved run marks every observer `Dirty`, apart from the computation that was current.**

When `run_memo` returns with the memo's stored view changed, or a comparer other than `CmpEq`, each observer that takes marks ends `Dirty`. The exception is the computation current at the run's start: `NotifyDirtyExcept` skips it. On a memo that loses a read: [`check_resolves_over_moved_source`](Ranvier.Pure.Witnesses.html#check_resolves_over_moved_source). On an effect, the same skip loses a re-run: [`effect_resolves_over_moved_source`](Ranvier.Pure.Witnesses.html#effect_resolves_over_moved_source).

Premises: `run_memo` returns, and the observer is not the computation current at the start. An observer takes marks when it is a memo, or an effect outside its disposal and its cleanups.

- [f:] the fuel, which `run_memo` runs at `S f`.
- [n:] the memo that runs.
- [s:] the state before the run.
- [s':] the state after the run.
- [d:] the node `n` in `s`.
- [d':] the node `n` in `s'`, whose view or comparer shows it moved.
*)
Theorem moved_marks_except_running f n s s' d d' :
  g_nodes s !! n = Some d ->
  run_memo (S f) n s = Some (s', tt) ->
  g_nodes s' !! n = Some d' -> (read_view_of d' <> read_view_of d \/ n_comparer d' <> CmpEq) ->
  forall c, c ∈ observer_view (observers_of (g_edges s') n) -> g_current s <> Some c -> takes_marks s' c ->
    ends_dirty s' c.
Proof.
  intros Hd Hm Hd' Hne c Hobs Hcur Ht.
  destruct (run_memo_shape f n s s' d Hd Hm) as (s2 & r & s10 & mv & Eh & [R10 (d2 & d10 & Hd2 & Hd10 & Hmv)] & Hf).
  destruct (run_finish_shape f n mv s10 s' Hf) as (sg & s11 & d4 & G1 & G2 & G3 & G4 & G5 & Hn & Hd4 & Hend & _).
  assert (Hfx : mark_fixed sg s11).
  { destruct Hn as [[_ ->]|[_ Hn]]; [apply mark_fixed_refl|by eapply notify_except_fixed]. }
  assert (Hdg : g_nodes sg !! n = Some d10) by (by rewrite G2).
  destruct (mf_forward _ _ Hfx n d10 Hdg) as (e&He&(_&_&_&_&_&_&_&Hc4)&_&_&Hv4).
  rewrite Hd4 in He. injection He as <-.
  assert (Hd'4 : read_view_of d' = read_view_of d4 /\ n_comparer d' = n_comparer d4).
  { destruct Hend as [->|[_ ->]].
    - rewrite Hd4 in Hd'. by injection Hd' as <-.
    - simpl in Hd'. rewrite lookup_insert_eq in Hd'. injection Hd' as <-. done. }
  destruct Hd'4 as [V1 V2].
  assert (Hmvt : mv = true).
  { destruct mv; [done|]. exfalso. destruct (proj1 Hmv eq_refl) as [A1 A2]. destruct Hne as [H|H]; apply H; congruence. }
  destruct Hn as [[Hx _]|[_ Hn]]; [congruence|].
  assert (HE : g_edges s' = g_edges sg).
  { rewrite <- (mf_edges _ _ Hfx). destruct Hend as [->|[_ ->]]; done. }
  rewrite HE in Hobs.
  assert (Hcur10 : g_current s10 <> Some c).
  { rewrite (rs_current _ _ _ R10), (hosted_current _ _ _ _ _ Eh). exact Hcur. }
  assert (Ht11 : takes_marks s11 c).
  { destruct Ht as (e&He&Hke). destruct Hend as [->|[_ ->]]; [by exists e|].
    simpl in He. destruct (decide (c = n)) as [->|Hcn].
    - rewrite lookup_insert_eq in He. injection He as <-. exists d4. split; [done|exact Hke].
    - rewrite lookup_insert_ne in He by done. by exists e. }
  assert (Htg : takes_marks sg c).
  { destruct Ht11 as (e&He&Hke). destruct (mf_back _ _ Hfx c e He) as (e0&He0&(M1&M2&M3&_)).
    exists e0. split; [done|]. destruct Hke as [Hk|(Hk&Hl&Hcl)]; [left; congruence|right; split_and!; congruence]. }
  destruct (notify_dirty_ends_dirty f n (g_current s10) sg s11 c Hobs Htg (or_intror (conj Hn Hcur10))) as (e&He&Hdirty).
  destruct Hend as [->|[_ ->]]; [by exists e|].
  destruct (decide (c = n)) as [->|Hcn].
  - exists (node_with_unannounced true d4). split; [simpl; apply lookup_insert_eq|]. rewrite Hd4 in He. by injection He as <-.
  - exists e. split; [simpl; by rewrite lookup_insert_ne|done].
Qed.

Lemma mark_dirty_other f m c s s' e :
  m <> c -> mark_dirty f m s = Some s' -> g_nodes s !! c = Some e -> n_freshness e <> Dirty ->
  exists e', g_nodes s' !! c = Some e' /\ n_freshness e' <> Dirty.
Proof.
  intros Hne Hm He Hf. destruct f as [|f]; [done|].
  destruct (g_nodes s !! m) as [d|] eqn:Hd; [|simpl in Hm; rewrite Hd in Hm; simplify_eq; eauto].
  destruct (n_kind d) eqn:Hk.
  - simpl in Hm. rewrite Hd, Hk in Hm. simplify_eq. eauto.
  - exact (memo_mark_dirty_keeps_others_short (S f) m d s s' c e Hd Hk Hm ltac:(done) He Hf).
  - simpl in Hm. rewrite Hd, Hk in Hm. injection Hm as <-. unfold effect_marked.
    destruct (n_disposed d || n_in_cleanup d); [eauto|].
    destruct (n_queued d); simpl; rewrite lookup_insert_ne by done; eauto.
Qed.

(**md
**The notice of a moved run leaves the computation it skips short of `Dirty`.**

`notify_dirty_except` of a source, with a computation to skip, leaves that computation short of `Dirty` if it was short of it. A memo among the other observers can still tell it `Check`. [`skip_removed_fires`](Ranvier.Pure.Witnesses.html#skip_removed_fires) shows this fails when the skip is removed.

- [f:] the fuel.
- [n:] the source whose observers are marked.
- [c:] the current computation, skipped by the notice.
- [s:] the state before the notice.
- [s':] the state after the notice.
- [e:] the node `c` in `s`, which is not `Dirty`.
*)
Theorem notice_skips_current f n c s s' e :
  notify_dirty_except f n (Some c) s = Some s' -> g_nodes s !! c = Some e -> n_freshness e <> Dirty ->
  exists e', g_nodes s' !! c = Some e' /\ n_freshness e' <> Dirty.
Proof.
  intros Hn He Hf.
  refine (walk_keeps (mark_except f (Some c)) (fun s0 => exists e', g_nodes s0 !! c = Some e' /\ n_freshness e' <> Dirty) n _ s s' _ _ Hn).
  - intros m s0 s1 (e0&He0&Hf0) Hm. unfold mark_except in Hm. destruct (decide (Some c = Some m)) as [Heq|Hne].
    + injection Hm as <-. eauto.
    + eapply mark_dirty_other; [|exact Hm|exact He0|exact Hf0]. intros ->. done.
  - eauto.
Qed.

(**md
**A moved run's end leaves the computation current at its start short of `Dirty`.**

If the computation current when `run_memo` started is short of `Dirty` when the run's body returns, it is short of `Dirty` when the run returns. This is the skip of `NotifyDirtyExcept`, the half [`moved_marks_except_running`](#moved_marks_except_running) leaves out. A computation that is the memo itself is outside this statement.

Premises: the current computation is another node, and `run_memo` returns.

- [f:] the fuel, which `run_memo` runs at `S f`.
- [n:] the memo that runs.
- [s:] the state before the run.
- [s':] the state after the run.
- [s2:] the state when the body returns.
- [r:] the outcome of the body.
- [d:] the node `n` in `s`.
- [c:] the current computation, different from `n`.
- [e:] the node `c` in `s2`, which is not `Dirty`.
*)
Theorem moved_run_skips_current f n s s' s2 r d c e :
  g_nodes s !! n = Some d ->
  hosted n (exec f (run_env d) (n_body d)) (run_start n d s) = Some (s2, r) ->
  run_memo (S f) n s = Some (s', tt) ->
  g_current s = Some c -> c <> n ->
  g_nodes s2 !! c = Some e -> n_freshness e <> Dirty ->
  exists e', g_nodes s' !! c = Some e' /\ n_freshness e' <> Dirty.
Proof.
  intros Hd Eh Hm Hcur Hne He Hf.
  destruct (run_memo_shape f n s s' d Hd Hm) as (s2' & r' & s10 & mv & Eh' & [R10 _] & Hfin).
  rewrite Eh in Eh'. injection Eh' as <- <-.
  destruct (run_finish_shape f n mv s10 s' Hfin) as (sg & s11 & d4 & G1 & G2 & G3 & G4 & G5 & Hn & Hd4 & Hend & _).
  assert (Hcur10 : g_current s10 = Some c).
  { rewrite (rs_current _ _ _ R10), (hosted_current _ _ _ _ _ Eh). exact Hcur. }
  assert (Heg : g_nodes sg !! c = Some e) by (by rewrite G2, (rs_others _ _ _ R10 c Hne)).
  assert (H11 : exists e', g_nodes s11 !! c = Some e' /\ n_freshness e' <> Dirty).
  { destruct Hn as [[_ ->]|[_ Hn]]; [eauto|]. rewrite Hcur10 in Hn. exact (notice_skips_current f n c sg s11 e Hn Heg Hf). }
  destruct H11 as (e'&He'&Hf'). exists e'. split; [|done].
  destruct Hend as [->|[_ ->]]; [done|]. simpl. by rewrite lookup_insert_ne.
Qed.

(**md
**After a run, a live computation's source list is its tracked reads since its latest run start.**

The source list holds those reads in order, with a read of the source just read adding nothing, as `sources_add` stores them. A nested run of the same memo starts over: reads of the outer run ahead of it have no slot, as `nested_self_run_ends_clean_and_wrong` shows. So "the run" here is the latest to start, which is the reason this is not `run_end_edges_are_reads_statement`.

Premises: the [settled invariant](#engine_settled) holds at the start, the computation is not on the running stack, `run_memo` or `run_effect` returns, and it is not disposed at the end.

- [f:] the fuel.
- [n:] the memo or effect that runs.
- [s:] the state before the run, which satisfies `engine_settled`.
- [s':] the state after the run.
- [d':] the node `n` in `s'`, which is not disposed.
*)
Theorem run_end_edges_are_latest_reads f n s s' d' :
  engine_settled s -> n ∉ g_running s ->
  ((has_kind n KMemo s /\ run_memo f n s = Some (s', tt)) \/ (effect_ctx n s /\ run_effect f n s = Some (s', tt))) ->
  g_nodes s' !! n = Some d' -> n_disposed d' = false ->
  fst <$> source_slots (sources_of (g_edges s') n) = read_list (reads_of s' n).
Proof.
  intros [Hc Hw] Hnr Hrun Hd' Hl.
  pose proof (runs_ok_all f) as H.
  assert (Hend : run_core s' /\ run_kept s s').
  { destruct Hrun as [[Hk Hm]|[Hk Hm]].
    - exact (Rrun_memo _ H n _ _ _ Hc Hk Hm).
    - destruct Hc as [Hi [Hb Ht]]. eapply (Rrun_effect _ H n); [done|done| |done|done].
      intros b c y dc e _. apply (Ht b c y dc e I). }
  destruct Hend as [[_ [Hb' _]] [(Hrs & _) _]].
  assert (Hnr' : n ∉ g_running s') by (by rewrite Hrs).
  pose proof (b_reads _ Hb' n d' Hd' Hl) as Hr.
  rewrite read_prefix_view, (b_ended _ Hb' n Hnr'), take_ge in Hr; [exact Hr|lia].
Qed.

(**md
**After a run with no nested self-run, a live computation's source list is the reads of that run.**

The source list holds the run's tracked reads in order, with a read of the source just read adding nothing, as `sources_add` stores them. `gh_nested` holds a computation when a run of it started inside its own, so the premise says that did not happen.

Premises: the [settled invariant](#engine_settled) holds at the start, the computation is not on the running stack, `run_memo` or `run_effect` returns, it is not disposed at the end, and it is not in `gh_nested` at the end.

- [f:] the fuel.
- [n:] the memo or effect that runs.
- [s:] the state before the run, which satisfies `engine_settled`.
- [s':] the state after the run.
- [d':] the node `n` in `s'`, which is not disposed.
*)
Corollary run_end_edges_no_nested_run f n s s' d' :
  engine_settled s -> n ∉ g_running s ->
  ((has_kind n KMemo s /\ run_memo f n s = Some (s', tt)) \/ (effect_ctx n s /\ run_effect f n s = Some (s', tt))) ->
  g_nodes s' !! n = Some d' -> n_disposed d' = false -> n ∉ gh_nested (g_ghost s') ->
  fst <$> source_slots (sources_of (g_edges s') n) = read_list (run_reads_of s' n).
Proof.
  intros Hs Hnr Hrun Hd' Hl Hn.
  rewrite (run_end_edges_are_latest_reads f n s s' d' Hs Hnr Hrun Hd' Hl).
  assert (Hset : run_core s').
  { pose proof (runs_ok_all f) as H. destruct Hs as [Hc Hw]. destruct Hrun as [[Hk Hm]|[Hk Hm]].
    - exact (proj1 (Rrun_memo _ H n _ _ _ Hc Hk Hm)).
    - destruct Hc as [Hi [Hb Ht]]. refine (proj1 (Rrun_effect _ H n _ _ _ Hi Hb _ Hk Hm)).
      intros b c y dc e _. apply (Ht b c y dc e I). }
  destruct (b_window _ (proj1 (proj2 Hset))) as (_&_&_&W). by rewrite (W n Hn).
Qed.

(** A live computation's source list after a run is the run's reads, whatever ran inside it. The fix phase owes
    this. [nested_self_recompute_resets_cursor] in [BUGS.md] is its counterexample on the present engine. *)
Definition run_end_edges_are_reads_statement : Prop :=
  forall f n s s' d',
    engine_settled s -> n ∉ g_running s ->
    ((has_kind n KMemo s /\ run_memo f n s = Some (s', tt)) \/ (effect_ctx n s /\ run_effect f n s = Some (s', tt))) ->
    g_nodes s' !! n = Some d' -> n_disposed d' = false ->
    fst <$> source_slots (sources_of (g_edges s') n) = read_list (run_reads_of s' n).

(**md
**Open statement, false on the present engine: after a run, a live computation's source list is the reads of that run.**

The reads are all the tracked reads since the outermost run start, nested runs included. `run_end_edges_are_reads_statement` is what the fix phase owes. The Confirmed bug `nested_self_recompute_resets_cursor` falsifies it, and [`run_end_edges_are_reads_falsified`](Ranvier.Pure.Witnesses.html#run_end_edges_are_reads_falsified) proves that. Until the fix, later layers use [`run_end_edges_are_latest_reads`](#run_end_edges_are_latest_reads) or [`run_end_edges_no_nested_run`](#run_end_edges_no_nested_run).
*)
