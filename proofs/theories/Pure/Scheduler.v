From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith Sorted.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Edges Marking Engine Check Runs.

Open Scope nat_scope.

(** ** Counts over the trace and the owners *)

(**md
The number of `EvCleanup` events of host `h` in the trace `l`.
*)
Fixpoint count_cleanups (h : node_id) (l : list event) : nat :=
  match l with
  | [] => 0
  | EvCleanup m :: r => (if decide (m = h) then 1 else 0) + count_cleanups h r
  | _ :: r => count_cleanups h r
  end.

(**md
The number of `EvRun` events of node `n` in the trace `l`.
*)
Fixpoint count_runs (n : node_id) (l : list event) : nat :=
  match l with
  | [] => 0
  | EvRun m :: r => (if decide (m = n) then 1 else 0) + count_runs n r
  | _ :: r => count_runs n r
  end.

Lemma count_cleanups_app h a b : count_cleanups h (a ++ b) = count_cleanups h a + count_cleanups h b.
Proof. induction a as [|e a IH]; [done|]. destruct e; simpl; rewrite ?IH; lia. Qed.

Lemma count_runs_app n a b : count_runs n (a ++ b) = count_runs n a + count_runs n b.
Proof. induction a as [|e a IH]; [done|]. destruct e; simpl; rewrite ?IH; lia. Qed.

(** The cleanups an owner of host [h] holds. *)
Definition owner_pending (h : node_id) (y : owner_state) : nat :=
  if decide (o_host y = h) then length (o_cleanups y) else 0.

(**md
The cleanups held by all owners of host `h` in `m`.
*)
Definition owners_pending (h : node_id) (m : gmap owner_id owner_state) : nat :=
  sum_list ((fun p => owner_pending h p.2) <$> map_to_list m).

Lemma owners_pending_delete h m o y :
  m !! o = Some y -> owners_pending h m = owner_pending h y + owners_pending h (delete o m).
Proof.
  intros Hy. unfold owners_pending.
  rewrite <- (sum_list_perm _ _ (fmap_Permutation _ _ _ (map_to_list_delete m o y Hy))).
  reflexivity.
Qed.

Lemma owners_pending_insert h m o y y' :
  m !! o = Some y ->
  owners_pending h (<[o := y']> m) + owner_pending h y = owners_pending h m + owner_pending h y'.
Proof.
  intros Hy. rewrite (owners_pending_delete h m o y Hy).
  rewrite (owners_pending_delete h (<[o := y']> m) o y') by apply lookup_insert_eq.
  rewrite delete_insert_eq. lia.
Qed.

Lemma owners_pending_fresh h m o y :
  m !! o = None -> owners_pending h (<[o := y]> m) = owners_pending h m + owner_pending h y.
Proof.
  intros Hn. rewrite (owners_pending_delete h (<[o := y]> m) o y) by apply lookup_insert_eq.
  rewrite delete_insert_eq, delete_id by done. lia.
Qed.

(** Events of a host's cleanups, with the cleanups its owners still hold. *)
Definition cleanup_score (h : node_id) (s : state) : nat :=
  count_cleanups h (g_trace s) + owners_pending h (g_owners s).

(** The cleanups registered with owners of host [h]. *)
Definition registered (h : node_id) (s : state) : nat := default 0 (g_registered s !! h).

(** [cleanup_score] rises by at most the registrations made from [s] to [s'], with an allowance of [w]. *)
Definition clean_le (w : node_id -> nat) (s s' : state) : Prop :=
  forall h, cleanup_score h s' + registered h s <= cleanup_score h s + registered h s' + w h.

(** ** Acquisition order *)

(**md
Link `a` was acquired before link `b`.
*)
Definition link_before (a b : link) : Prop := l_acquired a < l_acquired b.

(** An owner's children and cleanups in acquisition order: strictly increasing, each below the next count, and no
    acquisition shared between the two lists. The three cleanup lists have one length. *)
Record acq_ok (y : owner_state) : Prop := {
  ac_children : StronglySorted link_before (o_children y);
  ac_order : StronglySorted lt (o_cleanup_order y);
  ac_children_lt : Forall (fun l => l_acquired l < o_acquisitions y) (o_children y);
  ac_order_lt : Forall (fun a => a < o_acquisitions y) (o_cleanup_order y);
  ac_disjoint : Forall (fun l => l_acquired l ∉ o_cleanup_order y) (o_children y);
  ac_envs : length (o_cleanup_envs y) = length (o_cleanups y);
  ac_lengths : length (o_cleanup_order y) = length (o_cleanups y)
}.

Lemma ss_snoc {A} (R : A -> A -> Prop) l a :
  StronglySorted R l -> Forall (fun b => R b a) l -> StronglySorted R (l ++ [a]).
Proof.
  induction 1 as [|b l Hs IH Hf]; intros Ha; simpl.
  - apply SSorted_cons; [apply SSorted_nil|apply Forall_nil_2].
  - apply Forall_cons in Ha as [Hb Hl]. apply SSorted_cons; [by apply IH|].
    apply Forall_app_2; [done|]. by apply Forall_singleton.
Qed.

Lemma ss_filter {A} (R : A -> A -> Prop) (Q : A -> Prop) `{!forall a, Decision (Q a)} l :
  StronglySorted R l -> StronglySorted R (filter Q l).
Proof.
  induction 1 as [|b l Hs IH Hf]; [apply SSorted_nil|].
  rewrite filter_cons. destruct (decide (Q b)); [|done].
  apply SSorted_cons; [done|]. apply Forall_forall. intros c Hc.
  apply list_elem_of_filter in Hc as [_ Hc]. by eapply Forall_forall in Hf.
Qed.

Lemma forall_filter {A} (P Q : A -> Prop) `{!forall a, Decision (Q a)} l :
  Forall P l -> Forall P (filter Q l).
Proof.
  intros Hl. apply Forall_forall. intros c Hc.
  apply list_elem_of_filter in Hc as [_ Hc]. by eapply Forall_forall in Hl.
Qed.

Lemma forall_lt_S (l : list nat) k : Forall (fun a => a < k) l -> Forall (fun a => a < S k) l.
Proof. intros Hl. eapply Forall_impl; [exact Hl|]. intros. simpl in *. lia. Qed.

Lemma acq_new h sink disposed : acq_ok (new_owner h sink disposed).
Proof. split; simpl; first [apply SSorted_nil|apply Forall_nil_2|done]. Qed.

Lemma acq_detached y : acq_ok (owner_detached y).
Proof. split; simpl; first [apply SSorted_nil|apply Forall_nil_2|done]. Qed.

Lemma acq_retired y : acq_ok y -> acq_ok (owner_retired y).
Proof. intros []. by split. Qed.

Lemma acq_error f y : acq_ok y -> acq_ok (owner_with_error f y).
Proof. intros []. by split. Qed.

Lemma acq_without l y : acq_ok y -> acq_ok (owner_without l y).
Proof.
  intros []. split; simpl; try done.
  - by apply ss_filter.
  - by apply forall_filter.
  - by apply forall_filter.
Qed.

Lemma acq_child l y : acq_ok y -> l_acquired l = o_acquisitions y -> acq_ok (owner_with_child l y).
Proof.
  intros [H1 H2 H3 H4 H5 H6 H7] Hl. split; simpl; try done.
  - apply ss_snoc; [done|]. eapply Forall_impl; [exact H3|]. intros a Ha. unfold link_before. simpl in *. lia.
  - apply Forall_app_2; [by eapply Forall_impl; [exact H3|]; intros; simpl in *; lia|]. apply Forall_singleton. lia.
  - by apply forall_lt_S.
  - apply Forall_app_2; [done|]. apply Forall_singleton. rewrite Hl. intros Hin.
    eapply Forall_forall in H4; [|exact Hin]. lia.
Qed.

Lemma acq_cleanup b e y : acq_ok y -> acq_ok (owner_with_cleanup b e y).
Proof.
  intros [H1 H2 H3 H4 H5 H6 H7]. split; simpl; try done.
  - by apply ss_snoc.
  - by eapply Forall_impl; [exact H3|]; intros; simpl in *; lia.
  - apply Forall_app_2; [by apply forall_lt_S|]. apply Forall_singleton. lia.
  - apply Forall_forall. intros l Hl Hin. apply elem_of_app in Hin as [Hin|Hin].
    + by eapply Forall_forall in H5; [|exact Hl].
    + apply list_elem_of_singleton in Hin. eapply Forall_forall in H3; [|exact Hl]. lia.
  - rewrite !length_app. simpl. lia.
  - rewrite !length_app. simpl. lia.
Qed.

(** ** The scheduler's invariant *)

(** A computation's body or an untracked read is in progress, or a batch or a stale read is open. *)
Definition in_body (s : state) : Prop :=
  0 < g_pull_depth s \/ 0 < g_batch_depth s \/ is_Some (g_current s) \/ g_untracked s = true.

(** The state fields beside the context. [x] is the effect between its dequeue and [queued <- false] in
    [Effect.Execute]. *)
Record sched_data (x : option node_id) (s : state) : Prop := {
  sd_head : g_queue_head s <= length (g_queue s);
  sd_next : forall n : node_id, is_Some (g_nodes s !! n) -> n <= g_next_id s;
  sd_pending : forall n, n ∈ pending s -> has_kind n KEffect s;
  sd_agree : forall n d, g_nodes s !! n = Some d ->
    entries_of n (pending s) <= 1 /\ (n_queued d = true <-> n ∈ pending s \/ x = Some n);
  sd_x : forall n, x = Some n -> is_Some (g_nodes s !! n) /\ n ∉ pending s;
  sd_stale : forall n d, g_nodes s !! n = Some d -> n_kind d = KEffect -> n_disposed d = false ->
    stale (n_freshness d) = true -> n_queued d = true;
  sd_acq : forall o y, g_owners s !! o = Some y -> acq_ok y;
  sd_sink : forall o y p, g_owners s !! o = Some y -> o_sink y = Some p -> is_Some (g_owners s !! p);
  sd_root : is_Some (g_owners s !! root_owner_id)
}.

(**md
The context facts of `sched_inv`: an effect `x` in progress means a flush is running, an owed flush is not
running, and a body or untracked read in progress has a stale read open or a flush running.

- [x:] the effect between its dequeue and the end of its run, if any.
- [s:] the state.
*)
Record sched_ctx (x : option node_id) (s : state) : Prop := {
  sc_x : is_Some x -> g_flushing s = true;
  sc_owed : g_flush_owed s = true -> g_flushing s = false;
  sc_body : is_Some (g_current s) \/ g_untracked s = true -> 0 < g_pull_depth s \/ g_flushing s = true
}.

(**md
**What the scheduler's invariant says.**

- **The queue.** The head lies within the queue, and each pending entry names an effect. Each node has at most one pending entry, and its `queued` flag is set exactly when it has one or it is the effect `x`, between its dequeue and `queued <- false`. `x` has no pending entry, and a flush is running.
- **Effects.** A live, stale effect has its `queued` flag set.
- **Flushes.** A flush that is owed is not running, and nothing but an open stale read, batch or body leaves one owed. A body or an untracked read in progress has a stale read open or a flush running.
- **Owners.** Each owner's children and cleanups are in acquisition order. Each owner's sink exists, and so does the root.
- **Ids.** Every node id is at most `g_next_id`.

This says nothing of whether a value is up to date.
*)
Definition sched_inv (x : option node_id) (s : state) : Prop :=
  sched_data x s /\ sched_ctx x s /\ (g_flush_owed s = true -> in_body s).

(** What a call returns as it found it, and how the flush it owes relates to the batch it was in. *)
Record sched_bal (s s' : state) : Prop := {
  r_current : g_current s' = g_current s;
  r_untracked : g_untracked s' = g_untracked s;
  r_batch : g_batch_depth s' = g_batch_depth s;
  r_pull : g_pull_depth s' = g_pull_depth s;
  r_flushing : g_flushing s' = g_flushing s;
  r_owed : g_flush_owed s' = true -> g_flush_owed s = true \/ g_batch_depth s = 0
}.

(** What only moves forward from a call's start to its end. *)
Record data_rel (s s' : state) : Prop := {
  r_nodes : forall n d, g_nodes s !! n = Some d -> exists d', g_nodes s' !! n = Some d' /\
    n_kind d' = n_kind d /\ (n_disposed d = true -> n_disposed d' = true) /\
    (n_kind d = KEffect -> n_disposed d = true -> n_runs d' = n_runs d);
  r_runs : forall n d, g_nodes s !! n = Some d -> n_kind d = KEffect -> n_disposed d = true ->
    count_runs n (g_trace s') = count_runs n (g_trace s);
  r_trace : g_trace s `prefix_of` g_trace s';
  r_hosts : forall o y, g_owners s !! o = Some y -> exists y', g_owners s' !! o = Some y' /\ o_host y' = o_host y
}.

Definition zero (_ : node_id) : nat := 0.

(**md
What every call preserves: `sched_bal`, `data_rel` and a `clean_le` bound with weight zero for every host.
*)
Definition post (s s' : state) : Prop := sched_bal s s' /\ data_rel s s' /\ clean_le zero s s'.

(** The fields [sched_data] and [data_rel] read. *)
Record data_same (s s' : state) : Prop := {
  ds_nodes : g_nodes s' = g_nodes s;
  ds_owners : g_owners s' = g_owners s;
  ds_queue : g_queue s' = g_queue s;
  ds_head : g_queue_head s' = g_queue_head s;
  ds_next : g_next_id s' = g_next_id s;
  ds_trace : g_trace s' = g_trace s;
  ds_reg : g_registered s' = g_registered s
}.

Lemma data_of_same x s s' : data_same s s' -> sched_data x s -> sched_data x s'.
Proof.
  intros [H1 H2 H3 H4 H5 H6 H7] H. destruct H.
  split; unfold pending, has_kind in *; rewrite ?H1, ?H2, ?H3, ?H4, ?H5; done.
Qed.

Lemma rel_of_same s s' : data_same s s' -> data_rel s s'.
Proof.
  intros [H1 H2 H3 H4 H5 H6 H7]. split; rewrite ?H1, ?H2, ?H6.
  - intros n d Hd. by exists d.
  - done.
  - done.
  - intros o y Hy. by exists y.
Qed.

Lemma clean_of_same s s' : data_same s s' -> clean_le zero s s'.
Proof.
  intros [H1 H2 H3 H4 H5 H6 H7] h. unfold cleanup_score, registered, zero. rewrite H2, H6, H7. lia.
Qed.

Lemma data_rel_refl s : data_rel s s.
Proof. apply rel_of_same. by split. Qed.

Lemma data_rel_trans s1 s2 s3 : data_rel s1 s2 -> data_rel s2 s3 -> data_rel s1 s3.
Proof.
  intros [A1 A2 A3 A4] [B1 B2 B3 B4]. split.
  - intros n d Hd. destruct (A1 n d Hd) as (d2&Hd2&Hk&Hi&Hr).
    destruct (B1 n d2 Hd2) as (d3&Hd3&Hk3&Hi3&Hr3). exists d3. split_and!; try congruence; auto.
    intros He Hz. rewrite Hr3, Hr; auto. congruence.
  - intros n d Hd Hk Hz. destruct (A1 n d Hd) as (d2&Hd2&Hk2&Hi&_).
    rewrite (B2 n d2 Hd2); [|congruence|auto]. eauto.
  - by etrans.
  - intros o y Hy. destruct (A4 o y Hy) as (y2&Hy2&H2). destruct (B4 o y2 Hy2) as (y3&Hy3&H3).
    exists y3. split; [done|congruence].
Qed.

Lemma clean_trans w1 w2 s1 s2 s3 :
  clean_le w1 s1 s2 -> clean_le w2 s2 s3 -> clean_le (fun h => w1 h + w2 h) s1 s3.
Proof. intros A B h. specialize (A h). specialize (B h). lia. Qed.

Lemma bal_refl s : sched_bal s s.
Proof. split; auto. Qed.

Lemma bal_trans s1 s2 s3 : sched_bal s1 s2 -> sched_bal s2 s3 -> sched_bal s1 s3.
Proof.
  intros [A1 A2 A3 A4 A5 A6] [B1 B2 B3 B4 B5 B6]. split; try congruence.
  intros H. destruct (B6 H) as [G|G]; [auto|right; congruence].
Qed.

Lemma post_refl s : post s s.
Proof.
  split_and!; [apply bal_refl|apply data_rel_refl|]. intros h. unfold zero. lia.
Qed.

Lemma post_trans s1 s2 s3 : post s1 s2 -> post s2 s3 -> post s1 s3.
Proof.
  intros (A1&A2&A3) (B1&B2&B3). split_and!; [by eapply bal_trans|by eapply data_rel_trans|].
  intros h. pose proof (clean_trans _ _ _ _ _ A3 B3 h) as H. unfold zero in *. lia.
Qed.

(** ** Calls *)

(**md
From a state satisfying `sched_inv x` and `P`, a completed step `m` ends in a state satisfying `sched_inv y`,
related to the start by `post`.

- [x:] the effect in progress before the step.
- [y:] the effect in progress after the step.
- [P:] the premise on the starting state.
- [m:] the step.
*)
Definition sok {A} (x y : option node_id) (P : state -> Prop) (m : M A) : Prop :=
  forall s s' a, sched_inv x s -> P s -> m s = Some (s', a) -> sched_inv y s' /\ post s s'.

Local Notation T := (fun _ : state => True).

(**md
`P` survives every `post` step.
*)
Class SStable (P : state -> Prop) := sstable : forall s s', post s s' -> P s -> P s'.

Global Instance sstable_true : SStable T.
Proof. by intros ???. Qed.

Global Instance sstable_kind n k : SStable (has_kind n k).
Proof.
  intros s s' (_&H&_) (d&Hd&Hk). destruct (r_nodes _ _ H n d Hd) as (d'&Hd'&Hk'&_). exists d'. split; [done|congruence].
Qed.

(** A stale read is open or a flush is running. *)
Definition run_ctx (s : state) : Prop := 0 < g_pull_depth s \/ g_flushing s = true.

Global Instance sstable_ctx : SStable run_ctx.
Proof.
  intros s s' (H&_) Hc. unfold run_ctx in *. by rewrite (r_pull _ _ H), (r_flushing _ _ H).
Qed.

(**md
A flush is running.
*)
Definition flushing_on (s : state) : Prop := g_flushing s = true.

Global Instance sstable_flushing : SStable flushing_on.
Proof. intros s s' (H&_) Hc. unfold flushing_on in *. by rewrite (r_flushing _ _ H). Qed.

(**md
Owner `o` exists in `s`.
*)
Definition owner_exists (o : owner_id) (s : state) : Prop := is_Some (g_owners s !! o).

Global Instance sstable_owner o : SStable (owner_exists o).
Proof.
  intros s s' (_&H&_) [y Hy]. destruct (r_hosts _ _ H o y Hy) as (y'&Hy'&_). by exists y'.
Qed.

Global Instance sstable_and P Q : SStable P -> SStable Q -> SStable (fun s => P s /\ Q s).
Proof. intros HP HQ s s' H [A B]. split; [by eapply HP|by eapply HQ]. Qed.

Lemma sok_any {A} x y P (m : M A) : sok x y T m -> sok x y P m.
Proof. intros H s s' a Hi _ Hm. by eapply H. Qed.

Lemma sok_weaken {A} x y (P Q : state -> Prop) (m : M A) : (forall s, Q s -> P s) -> sok x y P m -> sok x y Q m.
Proof. intros HPQ H s s' a Hi HQ Hm. eapply H; eauto. Qed.

Lemma sok_bind {A B} x y z P `{!SStable P} (m : M A) (k : A -> M B) :
  sok x y P m -> (forall a, sok y z P (k a)) -> sok x z P (m_bind m k).
Proof.
  intros Hm Hk s s' b Hi HP Hr. unfold m_bind in Hr.
  destruct (m s) as [[s1 a]|] eqn:E; [|done].
  destruct (Hm _ _ _ Hi HP E) as [Hi1 F1].
  destruct (Hk a _ _ _ Hi1 (sstable _ _ F1 HP) Hr) as [Hi2 F2].
  split; [done|]. by eapply post_trans.
Qed.

Lemma sok_bind0 {A B} x P `{!SStable P} (m : M A) (k : A -> M B) :
  sok x x P m -> (forall a, sok x x P (k a)) -> sok x x P (m_bind m k).
Proof. intros. eapply sok_bind; eauto. Qed.

(** The bind of a step that takes a fact about its start state. *)
Lemma sok_bind_at {A B} x y z P `{!SStable P} (Q : state -> Prop) (m : M A) (k : A -> M B) :
  sok x y (fun s => P s /\ Q s) m -> (forall a, sok y z P (k a)) -> sok x z (fun s => P s /\ Q s) (m_bind m k).
Proof.
  intros Hm Hk s s' b Hi HP Hr. unfold m_bind in Hr.
  destruct (m s) as [[s1 a]|] eqn:E; [|done].
  destruct (Hm _ _ _ Hi HP E) as [Hi1 F1].
  destruct (Hk a _ _ _ Hi1 (sstable _ _ F1 (proj1 HP)) Hr) as [Hi2 F2].
  split; [done|]. by eapply post_trans.
Qed.

Lemma sok_return {A} x P (a : A) : sok x x P (m_return a).
Proof. intros s s' b Hi _ Hm. unfold m_return in Hm. simplify_eq. split; [done|apply post_refl]. Qed.

Lemma sok_stuck {A} x y P : sok x y P (@stuck A).
Proof. intros s s' b _ _ Hm. done. Qed.

Lemma sok_get x P : sok x x P m_get.
Proof. intros s s' b Hi _ Hm. unfold m_get in Hm. simplify_eq. split; [done|apply post_refl]. Qed.

Lemma sok_get_node x P n : sok x x P (get_node n).
Proof.
  intros s s' b Hi _ Hm. unfold get_node in Hm. destruct (g_nodes s !! n); simplify_eq.
  split; [done|apply post_refl].
Qed.

Lemma sok_get_node_at {B} x y P n (k : node -> M B) :
  (forall d, sok x y (fun s => P s /\ g_nodes s !! n = Some d) (k d)) ->
  sok x y P (m_bind (get_node n) k).
Proof.
  intros H s s' b Hi HP Hm. unfold m_bind, get_node in Hm.
  destruct (g_nodes s !! n) as [d|] eqn:E; [|done].
  eapply (H d); eauto.
Qed.

Lemma sok_get_owner x P o : sok x x P (get_owner o).
Proof.
  intros s s' b Hi _ Hm. unfold get_owner in Hm. destruct (g_owners s !! o); simplify_eq.
  split; [done|apply post_refl].
Qed.

Lemma sok_get_owner_at {B} x y P o (k : owner_state -> M B) :
  (forall z, sok x y (fun s => P s /\ g_owners s !! o = Some z) (k z)) ->
  sok x y P (m_bind (get_owner o) k).
Proof.
  intros H s s' b Hi HP Hm. unfold m_bind, get_owner in Hm.
  destruct (g_owners s !! o) as [z|] eqn:E; [|done].
  eapply (H z); eauto.
Qed.

(** A change to fields the invariant does not read. *)
Record core_same (s s' : state) : Prop := {
  cs_data : data_same s s';
  cs_current : g_current s' = g_current s;
  cs_untracked : g_untracked s' = g_untracked s;
  cs_batch : g_batch_depth s' = g_batch_depth s;
  cs_pull : g_pull_depth s' = g_pull_depth s;
  cs_flushing : g_flushing s' = g_flushing s;
  cs_owed : g_flush_owed s' = g_flush_owed s
}.

Lemma sched_same x s s' : core_same s s' -> sched_inv x s -> sched_inv x s' /\ post s s'.
Proof.
  intros [Hd H1 H2 H3 H4 H5 H6] (Ha&[B1 B2 B3]&Hc). unfold sched_inv, post. split_and!.
  - by eapply data_of_same.
  - split; rewrite ?H1, ?H2, ?H4, ?H5, ?H6; done.
  - unfold in_body in *. rewrite H1, H2, H3, H4, H6. done.
  - split; try congruence. intros H. left. congruence.
  - by apply rel_of_same.
  - by apply clean_of_same.
Qed.

Lemma sok_same {A} x P (m : M A) : (forall s s' a, m s = Some (s', a) -> core_same s s') -> sok x x P m.
Proof. intros H s s' a Hi _ Hm. eapply sched_same; [|done]. by eapply H. Qed.

Lemma sok_modify_same x P (g : state -> state) : (forall s, core_same s (g s)) -> sok x x P (m_modify g).
Proof. intros H. apply sok_same. intros s s' a Hm. unfold m_modify in Hm. simplify_eq. apply H. Qed.

Local Ltac csame := repeat split.

Lemma sok_edges_opt x P u : sok x x P (edges_opt u).
Proof.
  apply sok_same. intros s s' a Hm. unfold edges_opt in Hm. destruct (u _); simplify_eq. csame.
Qed.

Lemma sok_track x P n : sok x x P (track n).
Proof.
  apply sok_same. intros s s' a Hm. unfold track in Hm. destruct (g_current s) as [c|]; [|simplify_eq; csame].
  destruct (sources_add _ _ _); simplify_eq. unfold ghost_read. simpl.
  destruct (g_nodes s !! n); csame.
Qed.

Lemma sok_log_label x P : sok x x P log_label.
Proof.
  apply sok_same. intros s s' a Hm. unfold log_label in Hm.
  destruct (g_current s), (g_current_owner s); simplify_eq; csame.
Qed.

(**md
`false` for `EvCleanup` and `EvRun`, `true` for every other event.
*)
Definition plain_event (e : event) : bool :=
  match e with EvCleanup _ | EvRun _ => false | _ => true end.

Lemma step_emit x e s :
  plain_event e = true -> sched_inv x s -> sched_inv x (with_trace (g_trace s ++ [e]) s) /\ post s (with_trace (g_trace s ++ [e]) s).
Proof.
  intros He (Ha&[B1 B2 B3]&Hc). unfold sched_inv, post. split_and!.
  - destruct Ha. split; unfold pending, has_kind in *; simpl; done.
  - by split.
  - done.
  - split; auto.
  - split; simpl.
    + intros n d Hd. by exists d.
    + intros. rewrite count_runs_app. destruct e; simpl in He; try done; simpl; lia.
    + by eexists.
    + intros o y Hy. by exists y.
  - intros h. unfold cleanup_score, registered, zero. simpl. rewrite count_cleanups_app.
    destruct e; simpl in He; try done; simpl; lia.
Qed.

Lemma sok_emit x P e : plain_event e = true -> sok x x P (emit e).
Proof.
  intros He s s' a Hi _ Hm. unfold emit, m_modify in Hm. simplify_eq. by apply step_emit.
Qed.

Lemma sok_note x P n o : sok x x P (note_host_read n o).
Proof.
  intros s s' a Hi HP Hm. unfold note_host_read in Hm.
  destruct (g_current s); [simplify_eq; split; [done|apply post_refl]|].
  destruct (g_current_owner s) as [r|]; [|simplify_eq; split; [done|apply post_refl]].
  destruct (decide (r = root_owner_id)); [|simplify_eq; split; [done|apply post_refl]].
  exact (sok_emit x T (EvHostRead n o) eq_refl _ _ _ Hi I Hm).
Qed.

Lemma sok_on_value x P e v (k : val -> M outcome) :
  (forall z, sok x x P (k z)) -> sok x x P (on_value e v k).
Proof.
  intros Hk s s' b Hi HP Hm. unfold on_value in Hm.
  destruct (eval_expr e (env_lookup e prev_var) v) as [[z|y]|]; simplify_eq.
  - by eapply Hk.
  - split; [done|apply post_refl].
Qed.

Lemma sok_on_node x P e v (k : node_id -> M outcome) :
  (forall t, sok x x P (k t)) -> sok x x P (on_node e v k).
Proof.
  intros Hk. apply sok_on_value. intros z. destruct (decide (0 <= z)%Z); [apply Hk|apply sok_stuck].
Qed.

(** ** One node *)

(** A write of a node the invariant allows. *)
Definition nstep (u : node -> node) (d : node) : Prop :=
  n_kind (u d) = n_kind d /\ n_queued (u d) = n_queued d /\
  (n_disposed d = true -> n_disposed (u d) = true) /\
  (n_kind d = KEffect -> n_disposed (u d) = false -> stale (n_freshness (u d)) = true ->
     n_disposed d = false /\ stale (n_freshness d) = true) /\
  (n_kind d = KEffect -> n_disposed d = true -> n_runs (u d) = n_runs d).

Lemma data_set x n d d' s :
  sched_data x s -> g_nodes s !! n = Some d -> n_kind d' = n_kind d -> n_queued d' = n_queued d ->
  (n_kind d = KEffect -> n_disposed d' = false -> stale (n_freshness d') = true ->
     n_queued d = true \/ (n_disposed d = false /\ stale (n_freshness d) = true)) ->
  sched_data x (set_node n d' s).
Proof.
  intros H Hd Hk Hq Hs. destruct H as [A1 A2 A3 A4 A5 A6 A7 A8 A9].
  assert (Hn : forall m, is_Some (g_nodes (set_node n d' s) !! m) -> is_Some (g_nodes s !! m)).
  { intros m. simpl. destruct (decide (m = n)) as [->|Hne]; [rewrite Hd; eauto|by rewrite lookup_insert_ne]. }
  assert (Hp : pending (set_node n d' s) = pending s) by done.
  split; rewrite ?Hp; simpl; try done.
  - intros m Hm. apply A2, Hn, Hm.
  - intros m Hm. eapply has_kind_set; [exact Hd|done|by apply A3].
  - intros m e. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-]. rewrite Hq. by apply (A4 n d).
    + rewrite lookup_insert_ne by done. apply A4.
  - intros m Hm. destruct (A5 m Hm) as [H1 H2]. split; [|done].
    apply is_Some_set; [rewrite Hd; eauto|done].
  - intros m e. destruct (decide (m = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros [=<-] Hek Hl Hst. rewrite Hq.
      rewrite Hk in Hek. destruct (Hs Hek Hl Hst) as [H1|[H1 H2]]; [done|]. by apply (A6 n d).
    + rewrite lookup_insert_ne by done. apply A6.
Qed.

Lemma post_set n d d' s :
  g_nodes s !! n = Some d -> n_kind d' = n_kind d -> (n_disposed d = true -> n_disposed d' = true) ->
  (n_kind d = KEffect -> n_disposed d = true -> n_runs d' = n_runs d) ->
  post s (set_node n d' s).
Proof.
  intros Hd Hk Hi Hr. split_and!.
  - split; auto.
  - split; simpl.
    + intros m e He. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. by exists d'.
      * rewrite lookup_insert_ne by done. by exists e.
    + done.
    + done.
    + intros o y Hy. by exists y.
  - intros h. unfold cleanup_score, registered, zero. simpl. lia.
Qed.

Lemma step_node x n u d s :
  nstep u d -> g_nodes s !! n = Some d -> sched_inv x s -> sched_inv x (set_node n (u d) s) /\ post s (set_node n (u d) s).
Proof.
  intros (N1&N2&N3&N4&N5) Hd (Ha&[B1 B2 B3]&Hc). split; [split_and!|].
  - apply (data_set x n d); auto.
  - by split.
  - exact Hc.
  - by apply (post_set n d).
Qed.

Lemma sok_modify_at x P n u d :
  nstep u d -> sok x x (fun s => P s /\ g_nodes s !! n = Some d) (modify_node n u).
Proof.
  intros Hu s s' a Hi [_ Hd] Hm. unfold modify_node in Hm. rewrite Hd in Hm. simplify_eq. by apply step_node.
Qed.

Lemma sok_modify x P n u : (forall d, nstep u d) -> sok x x P (modify_node n u).
Proof.
  intros Hu s s' a Hi _ Hm. unfold modify_node in Hm.
  destruct (g_nodes s !! n) as [d|] eqn:Hd; simplify_eq. by apply step_node.
Qed.

Lemma sok_modify_kind x P n u k :
  (forall s, P s -> has_kind n k s) -> (forall d, n_kind d = k -> nstep u d) -> sok x x P (modify_node n u).
Proof.
  intros HP Hu s s' a Hi Hs Hm. unfold modify_node in Hm.
  destruct (HP s Hs) as (d&Hd&Hk). rewrite Hd in Hm. simplify_eq. apply step_node; auto.
Qed.

(** The new node of a creation. *)
Lemma sok_new_node {B} x y P `{!SStable P} (D : node_id -> node) (k : node_id -> M B) :
  (forall n, n_queued (D n) = false) -> (forall n, stale (n_freshness (D n)) = true -> n_kind (D n) <> KEffect) ->
  (forall n, sok x y P (k n)) ->
  sok x y P (m_bind next_id (fun n => m_bind (add_node n (D n)) (fun _ => k n))).
Proof.
  intros Hq Hst Hk s s' b Hi HP HM. unfold m_bind, next_id, add_node, m_modify in HM. simpl in HM.
  remember (S (g_next_id s) : node_id) as n eqn:Hnn.
  assert (Hnat : (n : nat) = S (g_next_id s)) by exact Hnn.
  set (s1 := ghost_publish n (set_node n (D n) (with_next_id n s))) in HM.
  destruct Hi as (Ha&[B1 B2 B3]&Hc). destruct Ha as [A1 A2 A3 A4 A5 A6 A7 A8 A9].
  assert (Hfresh : g_nodes s !! n = None).
  { destruct (g_nodes s !! n) eqn:E; [|done]. pose proof (A2 n (mk_is_Some _ _ E)). lia. }
  assert (Hs1 : g_nodes s1 = <[n := D n]> (g_nodes s)).
  { unfold s1, ghost_publish. simpl. by rewrite lookup_insert_eq. }
  assert (Hrest : exists g, s1 = with_ghost g (set_node n (D n) (with_next_id n s))).
  { unfold s1, ghost_publish. simpl. rewrite lookup_insert_eq. by eexists. }
  destruct Hrest as (g&Hg).
  assert (Hpend : pending s1 = pending s) by (rewrite Hg; done).
  assert (Hne : forall m, is_Some (g_nodes s !! m) -> m <> n).
  { intros m Hm ->. rewrite Hfresh in Hm. by destruct Hm. }
  assert (Hup : forall m e, g_nodes s !! m = Some e -> g_nodes s1 !! m = Some e).
  { intros m e He. rewrite Hs1. rewrite lookup_insert_ne; [done|]. intros Heq. exact (Hne m (mk_is_Some _ _ He) (eq_sym Heq)). }
  assert (Hdata : sched_data x s1).
  { split; rewrite ?Hpend.
    - rewrite Hg. done.
    - intros m. rewrite Hg. simpl. destruct (decide (m = n)) as [->|Hmn]; [lia|].
      rewrite lookup_insert_ne by done. intros Hm. pose proof (A2 m Hm). lia.
    - intros m Hm. destruct (A3 m Hm) as (e&He&Hk'). exists e. split; [by apply Hup|done].
    - intros m e. rewrite Hs1. destruct (decide (m = n)) as [->|Hmn].
      + rewrite lookup_insert_eq. intros [=<-]. rewrite Hq.
        assert (Hnp : n ∉ pending s).
        { intros Hin. destruct (A3 n Hin) as (e'&He'&_). congruence. }
        split.
        * rewrite entries_absent by done. lia.
        * split; [done|]. intros [Hx|Hx]; [done|]. destruct (A5 n Hx) as [[z Hz] _]. congruence.
      + rewrite lookup_insert_ne by done. intros He.
        exact (A4 m e He).
    - intros m Hm. destruct (A5 m Hm) as [H1 H2]. split; [|done]. destruct H1 as [z Hz]. eexists. by apply Hup.
    - intros m e. rewrite Hs1. destruct (decide (m = n)) as [->|Hmn].
      + rewrite lookup_insert_eq. intros [=<-] Hek _ Hst'. by destruct (Hst n Hst').
      + rewrite lookup_insert_ne by done. apply A6.
    - rewrite Hg. exact A7.
    - rewrite Hg. exact A8.
    - rewrite Hg. exact A9. }
  assert (Hpost : post s s1).
  { split_and!.
    - rewrite Hg. split; auto.
    - split.
      + intros m e He. exists e. split_and!; auto.
      + intros. rewrite Hg. done.
      + rewrite Hg. done.
      + intros o z Hy. exists z. rewrite Hg. done.
    - intros h. unfold cleanup_score, registered, zero. rewrite Hg. simpl. lia. }
  assert (Hin : sched_inv x s1).
  { split_and!; [done| |].
    - rewrite Hg. by split.
    - rewrite Hg. exact Hc. }
  destruct (Hk n _ _ _ Hin (sstable _ _ Hpost HP) HM) as [Hi2 F2].
  split; [done|]. by eapply post_trans.
Qed.

(** ** Marks *)

Lemma prim_step x s s' : mark_prim s s' -> sched_inv x s -> sched_inv x s' /\ post s s'.
Proof.
  intros Hp Hi. pose proof Hi as (Ha&[B1 B2 B3]&Hc). destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hq Hf|s n d f Hd Hk Hq Hf].
  - apply (step_node x n (node_marked f u (n_queued d)) d); [|done|done].
    unfold nstep. simpl. split_and!; try done; congruence.
  - split; [split_and!|].
    + apply (data_set x n d); [done|done|done|simpl; done|intros _ _ _; by left].
    + by split.
    + exact Hc.
    + by apply (post_set n d).
  - set (s1 := enqueue n (set_node n (node_marked f (n_unannounced d) true d) s)).
    destruct Ha as [A1 A2 A3 A4 A5 A6 A7 A8 A9].
    destruct (A4 n d Hd) as [_ Hiff].
    assert (Hnp : n ∉ pending s) by (intros Hin; rewrite Hq in Hiff; destruct Hiff as [_ G]; by discriminate G; left).
    assert (Hxn : x <> Some n) by (intros Hx; rewrite Hq in Hiff; destruct Hiff as [_ G]; by discriminate G; right).
    assert (Hup : forall m e, g_nodes s !! m = Some e -> m <> n -> g_nodes s1 !! m = Some e).
    { intros m e He Hne. simpl. by rewrite lookup_insert_ne. }
    unfold sched_inv, post. split_and!.
    + split; simpl.
      * rewrite length_app. lia.
      * intros m. destruct (decide (m = n)) as [->|Hne]; [intros; exact (A2 n (mk_is_Some _ _ Hd))|].
        rewrite lookup_insert_ne by done. apply A2.
      * intros m Hm. apply elem_of_enqueued in Hm as [Hm| ->].
        -- eapply has_kind_set; [exact Hd|done|by apply A3].
        -- exists (node_marked f (n_unannounced d) true d). split; [simpl; apply lookup_insert_eq|done].
      * intros m e. destruct (decide (m = n)) as [->|Hne].
        -- rewrite lookup_insert_eq. intros [=<-]. split.
           ++ by apply entries_after_enqueue.
           ++ split; [intros _; left; by apply elem_after_enqueue|done].
        -- rewrite lookup_insert_ne by done. intros He. destruct (A4 m e He) as [H1 H2]. change (pending _) with (drop (g_queue_head s) (g_queue s ++ [n])). split.
           ++ rewrite entries_other by done. done.
           ++ rewrite H2. split.
              ** intros [H|H]; [left|by right].
                 rewrite drop_app_le by done. apply elem_of_app. by left.
              ** intros [H|H]; [|by right]. apply elem_of_enqueued in H as [H|H]; [by left|done].
      * intros m Hm. destruct (A5 m Hm) as [H1 H2]. assert (m <> n) by (intros ->; done). split.
        -- rewrite lookup_insert_ne by done. done.
        -- intros G. apply elem_of_enqueued in G as [G|G]; done.
      * intros m e. destruct (decide (m = n)) as [->|Hne].
        -- rewrite lookup_insert_eq. intros [=<-]. done.
        -- rewrite lookup_insert_ne by done. apply A6.
      * done.
      * done.
      * done.
    + by split.
    + exact Hc.
    + split; auto.
    + split; simpl.
      * intros m e He. destruct (decide (m = n)) as [->|Hne].
        -- rewrite lookup_insert_eq. rewrite Hd in He. injection He as <-. eexists. by split_and!.
        -- rewrite lookup_insert_ne by done. by exists e.
      * done.
      * done.
      * intros o y Hy. by exists y.
    + intros h. unfold cleanup_score, registered, zero. simpl. lia.
Qed.

Lemma marks_step x s s' : rtc mark_prim s s' -> sched_inv x s -> sched_inv x s' /\ post s s'.
Proof.
  induction 1 as [s|s1 s2 s3 Hp _ IH]; intros Hi; [split; [done|apply post_refl]|].
  destruct (prim_step x _ _ Hp Hi) as [Hi2 F2]. destruct (IH Hi2) as [Hi3 F3].
  split; [done|]. by eapply post_trans.
Qed.

Lemma sok_mark x P (g : state -> option state) :
  (forall s s', g s = Some s' -> rtc mark_prim s s') -> sok x x P (m_step g).
Proof.
  intros Hg s s' a Hi _ Hm. unfold m_step in Hm. destruct (g s) as [s1|] eqn:E; simplify_eq.
  eapply marks_step; [by eapply Hg|done].
Qed.

Lemma sok_mark_reader x P f : sok x x P (mark_reader f).
Proof.
  intros s s' a Hi _ Hm. unfold mark_reader in Hm.
  destruct (g_current s) as [c|]; [|simplify_eq; split; [done|apply post_refl]].
  destruct (f_reader_mark _); [|simplify_eq; split; [done|apply post_refl]].
  destruct (mark_check f c s) as [s1|] eqn:E; simplify_eq.
  eapply marks_step; [by eapply mark_check_rtc|done].
Qed.

(** ** Owners *)

Lemma is_Some_insert_owner (m : gmap owner_id owner_state) o y p : is_Some (m !! p) -> is_Some (<[o := y]> m !! p).
Proof.
  intros H. destruct (decide (p = o)) as [->|Hne]; [rewrite lookup_insert_eq; by eexists|by rewrite lookup_insert_ne].
Qed.

Lemma sink_insert (m : gmap owner_id owner_state) o y :
  (forall o' z p, m !! o' = Some z -> o_sink z = Some p -> is_Some (m !! p)) ->
  (forall p, o_sink y = Some p -> is_Some (m !! p)) ->
  forall o' z p, <[o := y]> m !! o' = Some z -> o_sink z = Some p -> is_Some (<[o := y]> m !! p).
Proof.
  intros H Hy o' z p Hz Hs. apply is_Some_insert_owner. destruct (decide (o' = o)) as [->|Hne].
  - rewrite lookup_insert_eq in Hz. injection Hz as <-. eauto.
  - rewrite lookup_insert_ne in Hz by done. eauto.
Qed.

Lemma step_put x o y0 y s :
  sched_inv x s -> g_owners s !! o = Some y0 -> o_host y = o_host y0 -> o_cleanups y = o_cleanups y0 ->
  o_sink y = o_sink y0 -> acq_ok y ->
  sched_inv x (with_owners (<[o := y]> (g_owners s)) s) /\ post s (with_owners (<[o := y]> (g_owners s)) s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc) Hy Hh Hl Hsk Ha'. unfold sched_inv, post. split_and!.
  - destruct Ha. split; unfold pending, has_kind in *; simpl; try done.
    + intros o' z. destruct (decide (o' = o)) as [->|Hne].
      * rewrite lookup_insert_eq. by intros [=<-].
      * rewrite lookup_insert_ne by done. eauto.
    + apply sink_insert; [done|]. intros p. rewrite Hsk. eauto.
    + by apply is_Some_insert_owner.
  - by split.
  - exact Hc.
  - split; auto.
  - split; simpl.
    + intros n d Hd. by exists d.
    + done.
    + done.
    + intros o' z Hz. destruct (decide (o' = o)) as [->|Hne].
      * rewrite lookup_insert_eq. rewrite Hy in Hz. injection Hz as <-. by exists y.
      * rewrite lookup_insert_ne by done. by exists z.
  - intros h. unfold cleanup_score, registered, zero. simpl.
    pose proof (owners_pending_insert h _ o y0 y Hy) as Hi.
    assert (owner_pending h y = owner_pending h y0) by (unfold owner_pending; by rewrite Hh, Hl). lia.
Qed.

Lemma step_owner_fresh x o y s :
  sched_inv x s -> g_owners s !! o = None -> o_cleanups y = [] -> acq_ok y ->
  (forall p, o_sink y = Some p -> is_Some (g_owners s !! p)) ->
  sched_inv x (with_owners (<[o := y]> (g_owners s)) s) /\ post s (with_owners (<[o := y]> (g_owners s)) s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc) Hy Hl Ha' Hsk. unfold sched_inv, post. split_and!.
  - destruct Ha. split; unfold pending, has_kind in *; simpl; try done.
    + intros o' z. destruct (decide (o' = o)) as [->|Hne].
      * rewrite lookup_insert_eq. by intros [=<-].
      * rewrite lookup_insert_ne by done. eauto.
    + by apply sink_insert.
    + by apply is_Some_insert_owner.
  - by split.
  - exact Hc.
  - split; auto.
  - split; simpl.
    + intros n d Hd. by exists d.
    + done.
    + done.
    + intros o' z Hz. assert (o' <> o) by (intros ->; congruence).
      rewrite lookup_insert_ne by done. by exists z.
  - intros h. unfold cleanup_score, registered, zero. simpl.
    rewrite (owners_pending_fresh h _ o y Hy).
    assert (owner_pending h y = 0) by (unfold owner_pending; rewrite Hl; by destruct (decide _)). lia.
Qed.

Lemma sok_current_owner x P : sok x x P current_owner.
Proof.
  intros s s' a Hi _ Hm. unfold current_owner in Hm.
  destruct (g_current_owner s) as [o|].
  { simplify_eq. split; [done|apply post_refl]. }
  destruct (g_current s) as [h|]; [|done].
  destruct (g_nodes s !! h) as [d|] eqn:Hd; [|done].
  destruct (n_scope d) as [o|] eqn:Hs.
  { simplify_eq. apply sched_same; [|done]. repeat split. }
  destruct (match n_kind d, n_mode d with KMemo, Pure => true | _, _ => false end).
  { simplify_eq. apply (step_node x h (node_with_violated true) d); [|done|done].
    unfold nstep; cbn; split_and!; intros; try split; done. }
  simplify_eq.
  set (o := stdpp.base.fresh (dom (g_owners s))) in *.
  assert (Hf : g_owners s !! o = None).
  { apply not_elem_of_dom. apply is_fresh. }
  assert (Hroot : forall p, o_sink (new_owner h (Some root_owner_id) (n_disposed d)) = Some p -> is_Some (g_owners s !! p)).
  { intros p Hp. simpl in Hp. injection Hp as <-. pose proof Hi as (Ha0&_&_). exact (sd_root _ _ Ha0). }
  destruct (step_owner_fresh x o (new_owner h (Some root_owner_id) (n_disposed d)) s Hi Hf eq_refl (acq_new _ _ _) Hroot) as [H1 F1].
  set (s1 := with_owners _ s) in H1, F1 |- *.
  assert (Hd1 : g_nodes s1 !! h = Some d) by exact Hd.
  destruct (step_node x h (node_with_scope (Some o)) d s1 ltac:(unfold nstep; cbn; split_and!; intros; try split; done) Hd1 H1) as [H2 F2].
  destruct (sched_same x (set_node h (node_with_scope (Some o) d) s1) (with_current_owner (Some o) (set_node h (node_with_scope (Some o) d) s1)) ltac:(repeat split) H2) as [H3 F3].
  split; [done|]. eapply post_trans; [exact F1|]. by eapply post_trans.
Qed.

(** ** Contexts *)

(**md
`post` with the cleanup weight `w`: `sched_bal`, `data_rel` and `clean_le w`.

- [w:] the weight per host in the `clean_le` bound.
*)
Definition full (w : node_id -> nat) (s s' : state) : Prop := sched_bal s s' /\ data_rel s s' /\ clean_le w s s'.

Lemma full_trans w1 w2 s1 s2 s3 : full w1 s1 s2 -> full w2 s2 s3 -> full (fun h => w1 h + w2 h) s1 s3.
Proof.
  intros (A1&A2&A3) (B1&B2&B3). split_and!; [by eapply bal_trans|by eapply data_rel_trans|by eapply clean_trans].
Qed.

Lemma full_mono w1 w2 s s' : (forall h, w1 h <= w2 h) -> full w1 s s' -> full w2 s s'.
Proof. intros H (A1&A2&A3). split_and!; [done|done|]. intros h. specialize (A3 h). specialize (H h). lia. Qed.

Lemma full_zero w s s' : post s s' -> full w s s'.
Proof. intros (A1&A2&A3). split_and!; [done|done|]. intros h. specialize (A3 h). unfold zero in A3. lia. Qed.

Lemma wrap_rel w s s0 s1 s' :
  data_same s s0 -> data_same s1 s' -> data_rel s0 s1 -> clean_le w s0 s1 -> data_rel s s' /\ clean_le w s s'.
Proof.
  intros H0 H1 Hr Hc. split.
  - eapply data_rel_trans; [by apply rel_of_same|]. eapply data_rel_trans; [exact Hr|by apply rel_of_same].
  - intros h. specialize (Hc h). pose proof (clean_of_same _ _ H0 h) as A. pose proof (clean_of_same _ _ H1 h) as B.
    unfold zero in A, B. lia.
Qed.

Lemma sok_hosted {A} x P n (body : M A) :
  (forall s, P s -> run_ctx s) -> sok x x T body -> sok x x P (hosted n body).
Proof.
  intros HP Hb s s' a Hi Hs Hm. unfold hosted, m_bind, m_modify, m_return in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  set (s0 := with_current_owner None (with_current (Some n) s)) in E.
  destruct Hi as (Ha&[B1 B2 B3]&Hc).
  assert (D0 : data_same s s0) by repeat split.
  assert (Hi0 : sched_inv x s0).
  { unfold sched_inv. split_and!.
    - by eapply data_of_same.
    - split; [exact B1|exact B2|intros _; exact (HP s Hs)].
    - intros _. right. right. left. simpl. by eexists. }
  destruct (Hb _ _ _ Hi0 I E) as [(Ha1&[C1 C2 C3]&Hc1) (Bal&Dr&Cl)].
  set (s2 := with_current_owner (g_current_owner s) (with_current (g_current s) s1)).
  assert (D1 : data_same s1 s2) by repeat split.
  destruct (wrap_rel _ _ _ _ _ D0 D1 Dr Cl) as [R1 R2].
  destruct Bal as [Q1 Q2 Q3 Q4 Q5 Q6]. simpl in Q2, Q3, Q4, Q5, Q6.
  unfold sched_inv, post. split_and!.
  - by eapply data_of_same.
  - split; simpl; [exact C1|exact C2|]. rewrite Q2, Q4, Q5. exact B3.
  - simpl. intros Ho. destruct (HP s Hs) as [Hp|Hf].
    + left. simpl. by rewrite Q4.
    + exfalso. pose proof (C2 Ho). rewrite Q5 in H. congruence.
  - split; simpl; try done.
  - done.
  - done.
Qed.

Lemma sok_untracked {A} x (body : M A) : sok x x T body -> sok x x T (untracked body).
Proof.
  intros Hb s s' a Hi _ Hm. unfold untracked, m_bind, m_modify, m_return in Hm.
  destruct (body _) as [[s1 r]|] eqn:E; [|done]. simplify_eq.
  set (flag := match g_current s with Some _ => true | None => g_untracked s end) in E.
  set (s0 := with_untracked flag (with_current None s)) in E.
  destruct Hi as (Ha&[B1 B2 B3]&Hc).
  assert (D0 : data_same s s0) by repeat split.
  assert (Hflag : flag = true -> is_Some (g_current s) \/ g_untracked s = true).
  { unfold flag. destruct (g_current s); [intros _; left; by eexists|auto]. }
  assert (Hi0 : sched_inv x s0).
  { unfold sched_inv. split_and!.
    - by eapply data_of_same.
    - split; [exact B1|exact B2|]. simpl. intros [H|H]; [by destruct H|]. by apply B3, Hflag.
    - intros Ho. destruct (Hc Ho) as [H|[H|[H|H]]].
      + by left.
      + by right; left.
      + right; right; right. simpl. unfold flag. by destruct H as [? ->].
      + right; right; right. simpl. unfold flag. destruct (g_current s); [done|exact H]. }
  destruct (Hb _ _ _ Hi0 I E) as [(Ha1&[C1 C2 C3]&Hc1) (Bal&Dr&Cl)].
  set (s2 := with_untracked (g_untracked s) (with_current (g_current s) s1)).
  assert (D1 : data_same s1 s2) by repeat split.
  destruct (wrap_rel _ _ _ _ _ D0 D1 Dr Cl) as [R1 R2].
  destruct Bal as [Q1 Q2 Q3 Q4 Q5 Q6]. simpl in Q1, Q2, Q3, Q4, Q5, Q6.
  unfold sched_inv, post. split_and!.
  - by eapply data_of_same.
  - split; simpl; [exact C1|exact C2|]. rewrite Q4, Q5. exact B3.
  - simpl. intros Ho. destruct (Hc1 Ho) as [H|[H|[H|H]]].
    + by left.
    + by right; left.
    + rewrite Q1 in H. by destruct H.
    + rewrite Q2 in H. destruct (g_current s) as [c|] eqn:Ec.
      * right; right; left. simpl. by eexists.
      * right; right; right. simpl. unfold flag in H. exact H.
  - split; simpl; try done.
  - done.
  - done.
Qed.

Definition detached_state (o : owner_id) (s : state) : state :=
  with_batch_depth (S (g_batch_depth s)) (with_current_owner (Some o) (with_current None s)).

Definition detached_end (s s1 : state) : state :=
  with_batch_depth (pred (g_batch_depth s1)) (with_current_owner (g_current_owner s) (with_current (g_current s) s1)).

Lemma detached_unfold o (body : M unit) s :
  detached o body s = match body (detached_state o s) with
                      | Some (s1, _) => Some (detached_end s s1, tt)
                      | None => None
                      end.
Proof. unfold detached, m_bind, m_modify. simpl. by destruct (body _) as [[? []]|]. Qed.

Lemma detached_in x o s : sched_inv x s -> sched_inv x (detached_state o s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc). unfold sched_inv. split_and!.
  - eapply data_of_same; [|exact Ha]. repeat split.
  - split; [exact B1|exact B2|]. simpl. intros [H|H]; [by destruct H|]. auto.
  - intros _. right; left. simpl. lia.
Qed.

Lemma detached_out x w o s s1 :
  sched_inv x s -> sched_inv x s1 -> full w (detached_state o s) s1 ->
  sched_inv x (detached_end s s1) /\ full w s (detached_end s s1).
Proof.
  intros (Ha&[B1 B2 B3]&Hc) (Ha1&[C1 C2 C3]&Hc1) (Bal&Dr&Cl).
  assert (D0 : data_same s (detached_state o s)) by repeat split.
  assert (D1 : data_same s1 (detached_end s s1)) by repeat split.
  destruct (wrap_rel _ _ _ _ _ D0 D1 Dr Cl) as [R1 R2].
  destruct Bal as [Q1 Q2 Q3 Q4 Q5 Q6]. simpl in Q1, Q2, Q3, Q4, Q5, Q6.
  unfold sched_inv, full. split_and!.
  - by eapply data_of_same.
  - split; simpl; [exact C1|exact C2|]. rewrite Q2, Q4, Q5. exact B3.
  - simpl. intros Ho. destruct (Q6 Ho) as [H|H]; [|lia].
    destruct (Hc H) as [G|[G|[G|G]]]; unfold in_body; simpl.
    + left. by rewrite Q4.
    + right; left. rewrite Q3. simpl. lia.
    + right; right; left. done.
    + right; right; right. by rewrite Q2.
  - split; simpl; try done.
    + rewrite Q3. simpl. lia.
    + intros Ho. destruct (Q6 Ho) as [H|H]; [auto|lia].
  - done.
  - done.
Qed.

Lemma sok_detached x o (body : M unit) : sok x x T body -> sok x x T (detached o body).
Proof.
  intros Hb s s' a Hi _ Hm. rewrite detached_unfold in Hm.
  destruct (body _) as [[s1 []]|] eqn:E; [|done]. simplify_eq.
  destruct (Hb _ _ _ (detached_in x o s Hi) I E) as [H1 F1].
  destruct (detached_out x zero o s s1 Hi H1 (full_zero _ _ _ F1)) as [H2 F2].
  by split.
Qed.

Lemma sok_batch {A B} x (body : M A) (k : A -> M B) :
  sok x x T body -> (forall r, sok x x T (k r)) ->
  sok x x T (let* _ := m_modify (fun s => with_batch_depth (S (g_batch_depth s)) s) in
             let* r := body in
             let* _ := m_modify (fun s => with_batch_depth (pred (g_batch_depth s)) s) in
             k r).
Proof.
  intros Hb Hk s s' a Hi _ Hm. unfold m_bind, m_modify in Hm.
  set (s0 := with_batch_depth (S (g_batch_depth s)) s) in Hm.
  destruct (body s0) as [[s1 r]|] eqn:E; [|done].
  set (s2 := with_batch_depth (pred (g_batch_depth s1)) s1) in Hm.
  destruct Hi as (Ha&[B1 B2 B3]&Hc).
  assert (D0 : data_same s s0) by repeat split.
  assert (Hi0 : sched_inv x s0).
  { unfold sched_inv. split_and!.
    - by eapply data_of_same.
    - by split.
    - intros _. right; left. simpl. lia. }
  destruct (Hb _ _ _ Hi0 I E) as [(Ha1&[C1 C2 C3]&Hc1) (Bal&Dr&Cl)].
  assert (D1 : data_same s1 s2) by repeat split.
  destruct (wrap_rel _ _ _ _ _ D0 D1 Dr Cl) as [R1 R2].
  destruct Bal as [Q1 Q2 Q3 Q4 Q5 Q6]. simpl in Q1, Q2, Q3, Q4, Q5, Q6.
  assert (Hi2 : sched_inv x s2).
  { unfold sched_inv. split_and!.
    - by eapply data_of_same.
    - split; simpl; [exact C1|exact C2|]. rewrite Q1, Q2, Q4, Q5. exact B3.
    - simpl. intros Ho. destruct (Q6 Ho) as [H|H]; [|lia].
      destruct (Hc H) as [G|[G|[G|G]]]; unfold in_body; simpl.
      + left. by rewrite Q4.
      + right; left. rewrite Q3. simpl. lia.
      + right; right; left. by rewrite Q1.
      + right; right; right. by rewrite Q2. }
  assert (F2 : post s s2).
  { unfold post. split_and!; [|done|done]. split; simpl; try done.
    - rewrite Q3. simpl. lia.
    - intros Ho. destruct (Q6 Ho) as [H|H]; [auto|lia]. }
  destruct (Hk r _ _ _ Hi2 I Hm) as [Hi3 F3].
  split; [done|]. by eapply post_trans.
Qed.

(** ** The queue's steps *)

Lemma entries_zero_absent n l : entries_of n l = 0 -> n ∉ l.
Proof.
  unfold entries_of. intros H Hin. apply length_zero_iff_nil in H.
  assert (Hf : n ∈ filter (fun x => x = n) l) by (apply list_elem_of_filter; auto).
  rewrite H in Hf. by apply not_elem_of_nil in Hf.
Qed.

Lemma entries_cons_self n l : entries_of n (n :: l) = S (entries_of n l).
Proof. unfold entries_of. by rewrite filter_cons_True. Qed.

Lemma entries_cons_other m n l : m <> n -> entries_of m (n :: l) = entries_of m l.
Proof. intros H. unfold entries_of. rewrite filter_cons_False; [done|]. intros ->. done. Qed.

Lemma data_pop n s :
  sched_data None s -> g_queue s !! g_queue_head s = Some n ->
  sched_data (Some n) (with_queue_head (S (g_queue_head s)) s).
Proof.
  intros [A1 A2 A3 A4 A5 A6 A7 A8 A9] Hq.
  assert (Hp : pending s = n :: drop (S (g_queue_head s)) (g_queue s)).
  { unfold pending. apply drop_S. done. }
  assert (Hp' : pending (with_queue_head (S (g_queue_head s)) s) = drop (S (g_queue_head s)) (g_queue s)) by done.
  assert (Hn : n ∈ pending s) by (rewrite Hp; left).
  destruct (A3 n Hn) as (dn&Hdn&Hkn).
  destruct (A4 n dn Hdn) as [Hle _]. rewrite Hp, entries_cons_self in Hle.
  assert (Hna : n ∉ drop (S (g_queue_head s)) (g_queue s)) by (apply entries_zero_absent; lia).
  split; rewrite ?Hp'; simpl; try done.
  - apply lookup_lt_Some in Hq. lia.
  - intros m Hm. apply A3. rewrite Hp. by right.
  - intros m e He. destruct (A4 m e He) as [H1 H2]. rewrite Hp in H1, H2.
    destruct (decide (m = n)) as [->|Hne].
    + split; [lia|]. rewrite H2. split; intros _; [by right|by left; left].
    + rewrite entries_cons_other in H1 by done. split; [done|]. rewrite H2. split.
      * intros [H|H]; [|done]. apply elem_of_cons in H as [H|H]; [done|by left].
      * intros [H|H]; [left; by right|congruence].
  - intros m Hm. injection Hm as <-. split; [by eexists|done].
Qed.

Lemma step_pop n s :
  sched_inv None s -> g_queue s !! g_queue_head s = Some n -> g_flushing s = true ->
  sched_inv (Some n) (with_queue_head (S (g_queue_head s)) s) /\ post s (with_queue_head (S (g_queue_head s)) s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc) Hq Hf. unfold sched_inv, post. split_and!.
  - by apply data_pop.
  - by split.
  - exact Hc.
  - split; auto.
  - split; simpl.
    + intros m d Hd. by exists d.
    + done.
    + done.
    + intros o y Hy. by exists y.
  - intros h. unfold cleanup_score, registered, zero. simpl. lia.
Qed.

Lemma step_reset s :
  sched_inv None s -> g_queue s !! g_queue_head s = None ->
  sched_inv None (with_queue [] (with_queue_head 0 s)) /\ post s (with_queue [] (with_queue_head 0 s)).
Proof.
  intros (Ha&[B1 B2 B3]&Hc) Hq. destruct Ha as [A1 A2 A3 A4 A5 A6 A7 A8 A9].
  assert (Hp : pending s = []).
  { unfold pending. apply drop_ge. by apply lookup_ge_None_1. }
  unfold sched_inv, post. split_and!.
  - split; simpl; try done.
    + intros m Hm. unfold pending in Hm. simpl in Hm. by apply not_elem_of_nil in Hm.
    + intros m d Hd. destruct (A4 m d Hd) as [_ H2]. rewrite Hp in H2. unfold pending. simpl. split; [unfold entries_of; simpl; lia|].
      rewrite H2. done.
  - by split.
  - exact Hc.
  - split; auto.
  - split; simpl.
    + intros m d Hd. by exists d.
    + done.
    + done.
    + intros o y Hy. by exists y.
  - intros h. unfold cleanup_score, registered, zero. simpl. lia.
Qed.

(** [queued <- false] in [Execute]. *)
Lemma sok_unflag_at P n u d :
  n_kind (u d) = n_kind d -> n_queued (u d) = false ->
  (n_disposed d = true -> n_disposed (u d) = true) ->
  (n_kind d = KEffect -> n_disposed (u d) = false -> stale (n_freshness (u d)) = false) ->
  (n_kind d = KEffect -> n_disposed d = true -> n_runs (u d) = n_runs d) ->
  sok (Some n) None (fun s => P s /\ g_nodes s !! n = Some d) (modify_node n u).
Proof.
  intros H1 H2 H3 H4 H5 s s' a (Ha&[B1 B2 B3]&Hc) [_ Hd] Hm. unfold modify_node in Hm. rewrite Hd in Hm. simplify_eq.
  destruct Ha as [A1 A2 A3 A4 A5 A6 A7 A8 A9]. destruct (A5 n eq_refl) as [_ Hnp].
  unfold sched_inv. split_and!; [| |exact Hc|].
  - split; simpl; try done.
    + intros m Hm. destruct (decide (m = n)) as [->|Hne]; [exact (A2 n (mk_is_Some _ _ Hd))|].
      rewrite lookup_insert_ne in Hm by done. by apply A2.
    + intros m Hm. eapply has_kind_set; [exact Hd|done|by apply A3].
    + intros m e. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-]. destruct (A4 n d Hd) as [G _]. split; [done|].
        rewrite H2. split; [done|]. intros [G1|G1]; [done|done].
      * rewrite lookup_insert_ne by done. intros He. destruct (A4 m e He) as [G1 G2]. split; [done|].
        rewrite G2. split; intros [G|G]; [by left|congruence|by left|done].
    + intros m e. destruct (decide (m = n)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hk Hl Hst. rewrite H1 in Hk. rewrite (H4 Hk Hl) in Hst. done.
      * rewrite lookup_insert_ne by done. apply A6.
  - split; simpl; [by intros [? ?]|exact B2|exact B3].
  - by apply (post_set n d).
Qed.

Local Ltac nst' := unfold nstep; cbn; split_and!; intros; cbn in *; first [congruence|(try split; done)].

(**md
Node `n` is a memo and a stale read is open or a flush is running.
*)
Definition memo_ctx (n : node_id) (s : state) : Prop := has_kind n KMemo s /\ run_ctx s.
(**md
Node `n` is an effect and a flush is running.
*)
Definition eff_ctx (n : node_id) (s : state) : Prop := has_kind n KEffect s /\ flushing_on s.

Global Instance sstable_memo_ctx n : SStable (memo_ctx n).
Proof. apply _. Qed.
Global Instance sstable_eff_ctx n : SStable (eff_ctx n).
Proof. apply _. Qed.

Lemma sok_modify_memo x n u : (forall d, n_kind d = KMemo -> nstep u d) -> sok x x (memo_ctx n) (modify_node n u).
Proof. intros H. eapply sok_modify_kind; [intros s Hs; exact (proj1 Hs)|exact H]. Qed.

Lemma sok_announce x P n : sok x x P (announce n).
Proof.
  intros s s' a Hi HP Hm. unfold announce in Hm. destruct (f_unannounced _).
  - eapply (sok_modify x P n (node_with_unannounced true)); [|done|done|exact Hm].
    intros d. nst'.
  - simplify_eq. split; [done|apply post_refl].
Qed.

(** ** Registering a cleanup *)

Lemma registered_ghost h k s :
  registered h (ghost_registered k s) = registered h s + (if decide (k = h) then 1 else 0).
Proof.
  unfold registered, ghost_registered. simpl. destruct (decide (k = h)) as [->|Hne].
  - rewrite lookup_insert_eq. simpl. lia.
  - rewrite lookup_insert_ne by done. lia.
Qed.

Lemma step_ghost x k s : sched_inv x s -> sched_inv x (ghost_registered k s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc). unfold sched_inv. split_and!.
  - destruct Ha. unfold ghost_registered. split; unfold pending, has_kind in *; simpl; done.
  - by split.
  - exact Hc.
Qed.

Lemma registered_ghost_all k s : forall h, registered h s <= registered h (ghost_registered k s).
Proof. intros h. rewrite registered_ghost. lia. Qed.

Lemma cleanup_score_ghost h k s : cleanup_score h (ghost_registered k s) = cleanup_score h s.
Proof. done. Qed.

Lemma step_register x o z b e s :
  sched_inv x s -> g_owners s !! o = Some z ->
  sched_inv x (with_owners (<[o := owner_with_cleanup b e z]> (g_owners (ghost_registered (o_host z) s))) (ghost_registered (o_host z) s)) /\
  post s (with_owners (<[o := owner_with_cleanup b e z]> (g_owners (ghost_registered (o_host z) s))) (ghost_registered (o_host z) s)).
Proof.
  intros Hi Hz. pose proof (step_ghost x (o_host z) s Hi) as Hi1. pose proof Hi as (Ha&[B1 B2 B3]&Hc).
  unfold sched_inv, post. split_and!.
  - destruct Ha. unfold ghost_registered. split; unfold pending, has_kind in *; simpl; try done.
    + intros o' y. destruct (decide (o' = o)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-]. apply acq_cleanup. eauto.
      * rewrite lookup_insert_ne by done. eauto.
    + apply sink_insert; [done|]. intros p Hp. eauto.
    + by apply is_Some_insert_owner.
  - by split.
  - exact Hc.
  - split; auto.
  - split; simpl.
    + intros n d Hd. by exists d.
    + done.
    + done.
    + intros o' y Hy. destruct (decide (o' = o)) as [->|Hne].
      * rewrite lookup_insert_eq. rewrite Hz in Hy. injection Hy as <-. by eexists.
      * rewrite lookup_insert_ne by done. by exists y.
  - intros h. unfold cleanup_score, zero.
    assert (Hreg : registered h (with_owners (<[o := owner_with_cleanup b e z]> (g_owners (ghost_registered (o_host z) s))) (ghost_registered (o_host z) s)) = registered h (ghost_registered (o_host z) s)) by done.
    rewrite Hreg, registered_ghost. simpl.
    pose proof (owners_pending_insert h _ o z (owner_with_cleanup b e z) Hz) as Hp.
    unfold owner_pending in Hp. simpl in Hp. rewrite length_app in Hp. simpl in Hp.
    destruct (decide (o_host z = h)); lia.
Qed.

(** ** The engine keeps the invariant *)

(** One when [o]'s host is [h]. *)
Definition hw (s : state) (o : owner_id) (h : node_id) : nat :=
  match g_owners s !! o with
  | Some y => if decide (o_host y = h) then 1 else 0
  | None => 0
  end.

Lemma hw_kept s s' o h : data_rel s s' -> owner_exists o s -> hw s' o h = hw s o h.
Proof.
  intros R [y Hy]. destruct (r_hosts _ _ R o y Hy) as (y'&Hy'&Hh). unfold hw. by rewrite Hy, Hy', Hh.
Qed.

(**md
Every engine function at fuel `f` satisfies `sok` with the contexts and premises it needs.

- [f:] the fuel.
*)
Record sched_ok (f : nat) : Prop := {
  Sexec : forall x e c, sok x x T (exec f e c);
  Sread : forall x n b, sok x x T (read f n b);
  Sread_node : forall x n, sok x x T (read_node f n);
  Supdate : forall x n, sok x x T (update_if_necessary f n);
  Srop : forall x n, sok x x (has_kind n KMemo) (refresh_or_pull f n);
  Spull : forall x n, sok x x (has_kind n KMemo) (pull f n);
  Srefresh : forall x n, sok x x (memo_ctx n) (refresh f n);
  Swalk : forall x n i, sok x x T (resolve_walk f n i);
  Srecompute : forall x n, sok x x (memo_ctx n) (recompute f n);
  Srun_memo : forall x n, sok x x (memo_ctx n) (run_memo f n);
  Sexecute : forall n, sok (Some n) None (eff_ctx n) (execute_effect f n);
  Srun_effect : forall n, sok None None (eff_ctx n) (run_effect f n);
  Sbody : forall n, sok None None (fun s => eff_ctx n s /\ live_of s n) (run_effect_body f n);
  Sdischarge : forall x o, sok x x run_ctx (discharge f o);
  Sretire : forall x o, sok x x T (retire f o);
  Sdispose_scope : forall x o, sok x x T (dispose_scope f o);
  Steardown : forall x o ls cs s s' a, sched_inv x s -> owner_exists o s -> teardown f o ls cs s = Some (s', a) ->
    sched_inv x s' /\ full (fun h => length cs * hw s o h) s s';
  Scleanup : forall x o b e s s' a, sched_inv x s -> owner_exists o s -> run_cleanup f o b e s = Some (s', a) ->
    sched_inv x s' /\ full (hw s o) s s';
  Srelease : forall x n, sok x x T (release f n);
  Sdispose : forall x n, sok x x T (dispose_node f n);
  Sattach : forall x n o, sok x x T (attach f n o);
  Swrite : forall x n v, sok x x T (write_signal f n v);
  Srequest : forall x, sok x x T (request_flush f);
  Ssettle : forall x s s' a, sched_data x s -> sched_ctx x s -> settle_flush f s = Some (s', a) ->
    sched_inv x s' /\ post s s';
  Sflush : forall x, sok x x T (run_flush f);
  Sdrain : sok None None flushing_on (drain f)
}.

Lemma sok_push x P n : sok x x P (push_running n).
Proof. apply sok_modify_same. intros s. repeat split. Qed.

Lemma sok_pop x P : sok x x P pop_running.
Proof. apply sok_modify_same. intros s. repeat split. Qed.

Lemma sok_edges x P u : sok x x P (edges u).
Proof. apply sok_modify_same. intros s. repeat split. Qed.

Lemma sok_mark_dirty x P f n : sok x x P (m_step (mark_dirty f n)).
Proof. apply sok_mark. intros s s' H. by eapply mark_dirty_rtc. Qed.

Lemma sok_notify_dirty x P f n : sok x x P (m_step (notify_dirty f n)).
Proof.
  apply sok_mark. intros s s' H. apply (any_mark_rtc f n None). unfold any_mark. tauto.
Qed.

Lemma sok_notify_except x P f n r : sok x x P (m_step (notify_dirty_except f n r)).
Proof.
  apply sok_mark. intros s s' H. apply (any_mark_rtc f n r). unfold any_mark. tauto.
Qed.

Lemma sok_put_then {B} x o y0 y (k : M B) :
  o_host y = o_host y0 -> o_cleanups y = o_cleanups y0 -> o_sink y = o_sink y0 -> (acq_ok y0 -> acq_ok y) ->
  sok x x T k -> sok x x (fun s => T s /\ g_owners s !! o = Some y0) (m_bind (put_owner o y) (fun _ => k)).
Proof.
  intros Hh Hl Hsk Ha Hk s s' a Hi [_ Hy] Hm. unfold m_bind, put_owner, m_modify in Hm.
  pose proof Hi as (Ha0&_&_).
  destruct (step_put x o y0 y s Hi Hy Hh Hl Hsk (Ha (sd_acq _ _ Ha0 o y0 Hy))) as [H1 F1].
  destruct (Hk _ _ _ H1 I Hm) as [H2 F2]. split; [done|]. by eapply post_trans.
Qed.

Lemma sok_unlink x P l : sok x x P (unlink l).
Proof.
  apply sok_any. unfold unlink. apply sok_get_owner_at. intros z.
  destruct (decide _); [|apply sok_return].
  intros s s' a Hi [_ Hy] Hm. unfold put_owner, m_modify in Hm. simplify_eq.
  pose proof Hi as (Ha0&_&_).
  apply (step_put x (l_owner l) z); [done|done|done|done|done|].
  apply acq_without. exact (sd_acq _ _ Ha0 _ z Hy).
Qed.

Lemma sok_record x P o y : sok x x P (record_error o y).
Proof.
  apply sok_any. unfold record_error. apply sok_get_owner_at. intros z.
  destruct (o_sink z) as [p|].
  - apply sok_weaken with (P := T); [done|]. apply sok_get_owner_at. intros w.
    intros s s' a Hi [_ Hy] Hm. unfold put_owner, m_modify in Hm. simplify_eq.
    pose proof Hi as (Ha0&_&_).
    apply (step_put x p w); [done|done|done|done|done|].
    apply acq_error. exact (sd_acq _ _ Ha0 _ w Hy).
  - intros s s' a Hi [_ Hy] Hm. unfold put_owner, m_modify in Hm. simplify_eq.
    pose proof Hi as (Ha0&_&_).
    apply (step_put x o z); [done|done|done|done|done|].
    apply acq_error. exact (sd_acq _ _ Ha0 _ z Hy).
Qed.

Lemma step_owe x s :
  sched_inv x s -> g_batch_depth s = 0 -> g_flushing s = false -> in_body s ->
  sched_inv x (with_flush_owed true s) /\ post s (with_flush_owed true s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc) Hb Hf Hin. unfold sched_inv, post. split_and!.
  - eapply data_of_same; [|exact Ha]. repeat split.
  - by split.
  - intros _. exact Hin.
  - split; auto.
  - apply rel_of_same. repeat split.
  - apply clean_of_same. repeat split.
Qed.

Lemma sok_owe x :
  sok x x run_ctx (let* s := m_get in
    if bool_decide (g_batch_depth s = 0%nat) && negb (g_flushing s) && live_queue s
    then m_modify (with_flush_owed true) else m_return tt).
Proof.
  intros s s' a Hi Hc Hm. rewrite bind_get in Hm.
  destruct (bool_decide _ && _ && _) eqn:E.
  - unfold m_modify in Hm. simplify_eq. apply andb_true_iff in E as [E _]. apply andb_true_iff in E as [E1 E2].
    apply bool_decide_eq_true in E1. apply negb_true_iff in E2.
    apply step_owe; [done|done|done|]. destruct Hc as [H|H]; [by left|congruence].
  - unfold m_return in Hm. simplify_eq. split; [done|apply post_refl].
Qed.

Lemma bind_get_owner {B} o y (k : owner_state -> M B) s :
  g_owners s !! o = Some y -> m_bind (get_owner o) k s = k y s.
Proof. intros H. unfold m_bind, get_owner. by rewrite H. Qed.

Lemma run_flush_clear_owed f s :
  g_flushing s = false -> run_flush f s = run_flush f (with_flush_owed false s).
Proof. intros H. destruct f as [|f]; [done|]. cbn [run_flush]. rewrite !bind_get. simpl. rewrite H. reflexivity. Qed.

Lemma data_rel_ghost k s : data_rel s (ghost_registered k s).
Proof.
  split; simpl.
  - intros n d Hd. by exists d.
  - done.
  - done.
  - intros o y Hy. by exists y.
Qed.

(** The node at the start of an effect's body, from [Effect.RunBody]. *)
Definition body_node (d : node) : node :=
  node_with_runs (S (n_runs d)) (node_with_failure None (node_with_status StNone d)).

(** The state at the start of an effect's body. *)
Definition body_start (n : node_id) (d : node) (s : state) : state :=
  with_trace (g_trace s ++ [EvRun n]) (set_node n (body_node d) s).

(** [run_effect_body] after the body's start. *)
Definition body_rest (f : nat) (n : node_id) (d : node) : M unit :=
  let* r := hosted n (exec f (n_env d) (n_body d)) in
  let* d' := get_node n in
  let* _ := edges_opt (if n_disposed d' then sources_clear n else sources_end_run n) in
  match r with
  | Ok _ => m_return tt
  | Fail y =>
    let* _ := modify_node n (fun x => node_with_status StError (node_with_failure (Some y) x)) in
    emit (EvFail n y)
  end.

Lemma body_unfold f n d s :
  g_nodes s !! n = Some d -> run_effect_body (S f) n s = body_rest f n d (body_start n d s).
Proof.
  intros Hd. cbn [run_effect_body]. rewrite (bind_get_node _ _ _ _ Hd), (bind_modify_node _ _ _ _ _ Hd).
  unfold emit. rewrite bind_m_modify. reflexivity.
Qed.

Lemma step_body_start n d s :
  sched_inv None s -> g_nodes s !! n = Some d -> n_disposed d = false ->
  sched_inv None (body_start n d s) /\ post s (body_start n d s).
Proof.
  intros Hi Hd Hl.
  assert (Hu : nstep body_node d).
  { unfold nstep, body_node; cbn; split_and!; intros; cbn in *; try split; try done; congruence. }
  destruct (step_node None n body_node d s Hu Hd Hi) as [H1 F1].
  set (s1 := set_node n (body_node d) s) in H1, F1.
  assert (Hd1 : g_nodes s1 !! n = Some (body_node d)) by (simpl; apply lookup_insert_eq).
  assert (H2 : sched_inv None (with_trace (g_trace s1 ++ [EvRun n]) s1) /\ post s1 (with_trace (g_trace s1 ++ [EvRun n]) s1)).
  { destruct H1 as (Ha&[B1 B2 B3]&Hc0). unfold sched_inv, post. split_and!.
    - destruct Ha. split; unfold pending, has_kind in *; simpl; done.
    - by split.
    - exact Hc0.
    - split; auto.
    - split; simpl.
      + intros m e He. by exists e.
      + intros m e He Hk Hz. rewrite count_runs_app. simpl.
        destruct (decide (n = m)) as [->|Hne]; [|lia].
        exfalso. rewrite lookup_insert_eq in He. injection He as <-. unfold body_node in Hz. simpl in Hz. congruence.
      + by eexists.
      + intros o y Hy. by exists y.
    - intros h. unfold cleanup_score, registered, zero. simpl. rewrite count_cleanups_app. simpl. lia. }
  destruct H2 as [H2 F2]. split; [exact H2|]. eapply post_trans; [exact F1|exact F2].
Qed.

Local Ltac csm := intros ?; unfold ghost_publish, ghost_run_end, ghost_run_start, ghost_effect_start,
  ghost_dirty_cleared, ghost_resolved; try destruct (g_nodes _ !! _); repeat split.

Local Ltac sih IH :=
  first
    [ apply (Sexec _ IH) | apply (Sread _ IH) | apply (Sread_node _ IH) | apply (Supdate _ IH)
    | apply (Srop _ IH) | apply (Spull _ IH) | apply (Srefresh _ IH) | apply (Swalk _ IH)
    | apply (Srecompute _ IH) | apply (Srun_memo _ IH) | apply (Srun_effect _ IH)
    | apply (Sretire _ IH) | apply (Sdispose_scope _ IH)
    | apply (Srelease _ IH) | apply (Sdispose _ IH) | apply (Sattach _ IH) | apply (Swrite _ IH)
    | apply (Srequest _ IH) | apply (Sflush _ IH) ].

Local Ltac sgo1 IH :=
  first
    [ apply sok_return | apply sok_stuck | apply sok_get | apply sok_get_node | apply sok_get_owner
    | apply sok_note | apply sok_log_label | apply sok_track | apply sok_mark_reader
    | apply sok_edges_opt | apply sok_edges | apply sok_push | apply sok_pop
    | apply sok_current_owner | apply sok_announce | apply sok_unlink | apply sok_record
    | apply sok_mark_dirty | apply sok_notify_dirty | apply sok_notify_except
    | (apply sok_emit; reflexivity)
    | (apply sok_on_node; intros ?) | (apply sok_on_value; intros ?)
    | (apply sok_new_node; [apply _|intros ?; reflexivity|intros ? Hx; cbn in *; congruence|intros ?])
    | (apply sok_modify; intros ?; solve [nst'])
    | (apply sok_modify_memo; intros ? ?; solve [nst'])
    | (apply sok_modify_same; solve [csm])
    | sih IH
    | (apply sok_any; sih IH)
    | (apply (sok_weaken _ _ run_ctx); [intros ? [_ G]; exact G|apply (Sdischarge _ IH)])
    | (apply (sok_weaken _ _ run_ctx); [intros ? [_ G]; right; exact G|apply (Sdischarge _ IH)])
    | (apply sok_hosted; [intros ? [_ G]; exact G|sih IH])
    | (apply sok_hosted; [intros ? [_ G]; right; exact G|sih IH])
    | (apply sok_any; apply sok_detached; sih IH)
    | (apply sok_any; apply sok_untracked; sih IH)
    | (apply sok_batch; [sih IH|intros ?])
    | (apply sok_bind0; [apply _| |intros ?])
    | match goal with |- sok _ _ _ (match ?x with _ => _ end) => destruct x end ].

Local Ltac sgo IH := repeat sgo1 IH.

Section Step.
  Variable f : nat.
  Hypothesis IH : sched_ok f.

  Lemma sok_register_S {B} x P `{!SStable P} o z b e (k : M B) :
    sok x x P k ->
    sok x x (fun s => P s /\ g_owners s !! o = Some z)
      (let* _ := m_modify (ghost_registered (o_host z)) in
       let* _ := (if o_disposed z then (let* _ := detached o (run_cleanup f o b e) in request_flush f)
                  else put_owner o (owner_with_cleanup b e z)) in
       k).
  Proof.
    intros Hk s s' a Hi [HP Hz] Hm. rewrite bind_m_modify in Hm.
    pose proof (step_ghost x (o_host z) s Hi) as Hi1.
    set (s1 := ghost_registered (o_host z) s) in Hm, Hi1.
    assert (Hbranch : forall s2 u, (if o_disposed z then (let* _ := detached o (run_cleanup f o b e) in request_flush f)
                  else put_owner o (owner_with_cleanup b e z)) s1 = Some (s2, u) -> sched_inv x s2 /\ post s s2).
    { intros s2 u Hb. destruct (o_disposed z).
      - unfold m_bind in Hb. destruct (detached o (run_cleanup f o b e) s1) as [[s3 []]|] eqn:Ed; [|done].
        rewrite detached_unfold in Ed.
        destruct (run_cleanup f o b e (detached_state o s1)) as [[s4 []]|] eqn:Ec; [|done]. simplify_eq.
        assert (Hz1 : g_owners s1 !! o = Some z) by exact Hz.
        assert (Hex : owner_exists o (detached_state o s1)) by (exists z; exact Hz).
        destruct (Scleanup _ IH x o b e _ _ _ (detached_in x o s1 Hi1) Hex Ec) as [H4 F4].
        destruct (detached_out x _ o s1 s4 Hi1 H4 F4) as [H5 F5].
        destruct (Srequest _ IH x _ _ _ H5 I Hb) as [H6 F6].
        split; [done|]. eapply post_trans; [|exact F6].
        destruct F5 as (A1&A2&A3). unfold post. split_and!.
        + destruct A1. split; simpl in *; try done.
        + eapply data_rel_trans; [|exact A2]. apply data_rel_ghost.
        + intros h. specialize (A3 h). unfold hw in A3.
          change (g_owners (detached_state o s1)) with (g_owners s) in A3. rewrite Hz in A3.
          unfold zero. change (cleanup_score h s1) with (cleanup_score h s) in A3.
          assert (Hreg : registered h s1 = registered h s + (if decide (o_host z = h) then 1 else 0))
            by apply registered_ghost.
          destruct (decide (o_host z = h)); lia.
      - unfold put_owner, m_modify in Hb. simplify_eq. exact (step_register x o z b e s Hi Hz). }
    unfold m_bind in Hm.
    destruct (_ s1) as [[s2 u]|] eqn:E; [|done].
    destruct (Hbranch s2 u eq_refl) as [H2 F2].
    destruct (Hk _ _ _ H2 (sstable _ _ F2 HP) Hm) as [H3 F3].
    split; [done|]. by eapply post_trans.
  Qed.

  Lemma sok_exec_S x e c : sok x x T (exec (S f) e c).
  Proof.
    destruct c; cbn [exec]; try solve [sgo IH].
    apply sok_bind0; [apply _|apply sok_current_owner|intros [o|]]; [|apply sok_return].
    apply sok_get_owner_at. intros z.
    apply (sok_register_S x T o z). sih IH.
  Qed.

  Lemma sok_read_S x n b : sok x x T (read (S f) n b).
  Proof. destruct b; cbn [read]; [sih IH|]. apply sok_untracked. sih IH. Qed.

  Lemma sok_read_node_S x n : sok x x T (read_node (S f) n).
  Proof.
    cbn [read_node]. apply sok_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek.
    - apply (sok_weaken _ _ T); [done|]. sgo IH.
    - apply (sok_weaken _ _ (has_kind n KMemo)); [intros s [_ Hd]; by exists d|]. sgo IH.
    - apply sok_stuck.
  Qed.

  Lemma sok_update_S x n : sok x x T (update_if_necessary (S f) n).
  Proof.
    cbn [update_if_necessary]. apply sok_get_node_at. intros d.
    destruct (n_kind d) eqn:Ek; try apply sok_return.
    destruct (stale_live d); [|apply sok_return].
    apply (sok_weaken _ _ (has_kind n KMemo)); [intros s [_ Hd]; by exists d|]. sih IH.
  Qed.

  Lemma sok_rop_S x n : sok x x (has_kind n KMemo) (refresh_or_pull (S f) n).
  Proof.
    intros s s' a Hi Hk Hm. cbn [refresh_or_pull] in Hm. rewrite bind_get in Hm.
    destruct (deferring s) eqn:Ed.
    - eapply (Srefresh _ IH); [done| |exact Hm]. split; [done|].
      pose proof Hi as (_&[_ _ B3]&_). unfold deferring in Ed. destruct (g_current s) as [c|] eqn:Ec.
      + apply B3. left. by eexists.
      + left. apply bool_decide_eq_true in Ed. done.
    - eapply (Spull _ IH); [done|done|exact Hm].
  Qed.

  Lemma sok_pull_S x n : sok x x (has_kind n KMemo) (pull (S f) n).
  Proof.
    intros s s' a Hi Hk Hm. cbn [pull] in Hm. rewrite bind_m_modify in Hm.
    set (s0 := with_pull_depth (S (g_pull_depth s)) s) in Hm.
    pose proof Hi as (Ha&[B1 B2 B3]&Hc).
    assert (D0 : data_same s s0) by repeat split.
    assert (Hi0 : sched_inv x s0).
    { unfold sched_inv. split_and!.
      - by eapply data_of_same.
      - split; [exact B1|exact B2|intros _; left; simpl; lia].
      - intros _. left. simpl. lia. }
    unfold m_bind in Hm. destruct (refresh f n s0) as [[s1 []]|] eqn:E; [|done].
    assert (Hm0 : memo_ctx n s0).
    { split; [|left; simpl; lia]. destruct Hk as (d&Hd&Hkd). by exists d. }
    destruct (Srefresh _ IH x n _ _ _ Hi0 Hm0 E) as [(Ha1&[C1 C2 C3]&Hc1) (Bal&Dr&Cl)].
    set (s2 := with_pull_depth (pred (g_pull_depth s1)) s1) in Hm.
    assert (D1 : data_same s1 s2) by repeat split.
    destruct (wrap_rel _ _ _ _ _ D0 D1 Dr Cl) as [R1 R2].
    destruct Bal as [Q1 Q2 Q3 Q4 Q5 Q6]. simpl in Q1, Q2, Q3, Q4, Q5, Q6.
    assert (Hd2 : sched_data x s2) by (by eapply data_of_same).
    assert (Hc2 : sched_ctx x s2).
    { split; simpl; [exact C1|exact C2|]. rewrite Q1, Q2, Q4, Q5. simpl. exact B3. }
    assert (F2 : post s s2).
    { unfold post. split_and!; [|done|done]. split; simpl; try done.
      rewrite Q4. simpl. done. }
    unfold m_get in Hm. simpl in Hm.
    assert (Hp2 : g_pull_depth s2 = g_pull_depth s) by (simpl; rewrite Q4; done).
    change (g_flush_owed s1) with (g_flush_owed s2) in Hm.
    destruct (bool_decide (pred (g_pull_depth s1) = 0%nat) && g_flush_owed s2) eqn:Es.
    - destruct (Ssettle _ IH x _ _ _ Hd2 Hc2 Hm) as [H3 F3].
      split; [done|]. by eapply post_trans.
    - unfold m_return in Hm. simplify_eq. split; [|done].
      unfold sched_inv. split_and!; [done|done|]. intros Ho.
      apply andb_false_iff in Es as [Es|Es]; [|exfalso; simpl in *; congruence].
      apply bool_decide_eq_false in Es. left. simpl. lia.
  Qed.

  Lemma sok_refresh_S x n : sok x x (memo_ctx n) (refresh (S f) n).
  Proof. cbn [refresh]. sgo IH. Qed.

  Lemma sok_walk_S x n i : sok x x T (resolve_walk (S f) n i).
  Proof. cbn [resolve_walk]. sgo IH. Qed.

  Lemma sok_recompute_S x n : sok x x (memo_ctx n) (recompute (S f) n).
  Proof. cbn [recompute]. sgo IH. Qed.

  Lemma sok_run_memo_S x n : sok x x (memo_ctx n) (run_memo (S f) n).
  Proof. cbn [run_memo]. sgo IH. Qed.

  Lemma sok_execute_S n : sok (Some n) None (eff_ctx n) (execute_effect (S f) n).
  Proof.
    cbn [execute_effect]. apply sok_get_node_at. intros d.
    destruct (n_disposed d) eqn:Edis.
    - apply sok_unflag_at; cbn; auto. intros _ Hz. congruence.
    - apply sok_weaken with (P := eff_ctx n); [intros s [H _]; exact H|].
      apply (sok_bind _ (Some n)); [apply _| |intros _].
      + destruct (decide (n_freshness d = Check)); [|apply sok_return].
        apply (sok_bind _ (Some n)); [apply _|apply sok_any; apply (Swalk _ IH)|intros _].
        apply (sok_bind _ (Some n)); [apply _|apply sok_get_node|intros d'].
        destruct (decide _); [|apply sok_return].
        apply (sok_bind _ (Some n)); [apply _|apply sok_modify_same; solve [csm]|intros _].
        apply sok_modify. intros dn. nst'.
      + apply sok_get_node_at. intros d'.
        apply (sok_bind_at (Some n) None None (eff_ctx n)).
        * apply sok_unflag_at; cbn; auto.
        * intros _. destruct (decide _); [sih IH|apply sok_return].
  Qed.

  Lemma sok_run_effect_S n : sok None None (eff_ctx n) (run_effect (S f) n).
  Proof.
    cbn [run_effect].
    apply sok_bind0; [apply _|apply sok_push|intros _].
    apply sok_bind0; [apply _|apply sok_edges|intros _].
    apply sok_bind0; [apply _|apply sok_modify_same; solve [csm]|intros _].
    apply sok_bind0; [apply _|apply sok_get_node|intros d].
    apply sok_bind0; [apply _|sgo IH|intros _].
    apply sok_get_node_at. intros d'.
    apply (sok_bind_at _ None None (eff_ctx n)); [|intros _; apply sok_pop].
    destruct (n_disposed d') eqn:Edis; [apply sok_return|].
    intros s s' a Hi [Hc Hd] Hm.
    eapply (Sbody _ IH); [done|split; [done|exists d'; auto]|exact Hm].
  Qed.

  Lemma sok_body_rest n d : sok None None (eff_ctx n) (body_rest f n d).
  Proof. unfold body_rest. sgo IH. Qed.

  Lemma sok_body_S n : sok None None (fun s => eff_ctx n s /\ live_of s n) (run_effect_body (S f) n).
  Proof.
    intros s s' a Hi [Hc (d0&Hd0&Hl)] Hm. rewrite (body_unfold _ _ _ _ Hd0) in Hm.
    destruct (step_body_start n d0 s Hi Hd0 Hl) as [H2 F2].
    destruct (sok_body_rest n d0 _ _ _ H2 (sstable _ _ F2 Hc) Hm) as [H3 F3].
    split; [done|]. by eapply post_trans.
  Qed.

  Lemma sok_discharge_S x o : sok x x run_ctx (discharge (S f) o).
  Proof.
    cbn [discharge]. apply sok_bind0; [apply _|apply sok_any; apply sok_detached; sih IH|intros _]. apply sok_owe.
  Qed.

  Lemma sok_retire_S x o : sok x x T (retire (S f) o).
  Proof.
    cbn [retire]. apply sok_bind0; [apply _| |intros _].
    - apply sok_detached. apply sok_get_owner_at. intros z. destruct (o_disposed z).
      + apply sok_return.
      + apply sok_put_then; [done|done|done|apply acq_retired|]. sih IH.
    - sgo IH.
  Qed.

  Lemma sok_dispose_scope_S x o : sok x x T (dispose_scope (S f) o).
  Proof.
    intros s s' a Hi _ Hm. cbn [dispose_scope] in Hm.
    destruct (g_owners s !! o) as [z|] eqn:Hz; [|unfold m_bind, get_owner in Hm; by rewrite Hz in Hm].
    rewrite (bind_get_owner _ _ _ _ Hz) in Hm. unfold m_bind, put_owner, m_modify in Hm.
    set (s1 := with_owners (<[o := owner_detached z]> (g_owners s)) s) in Hm.
    pose proof Hi as (Ha&[B1 B2 B3]&Hc).
    assert (Hz1 : g_owners s1 !! o = Some (owner_detached z)) by (simpl; apply lookup_insert_eq).
    assert (Hi1 : sched_inv x s1).
    { unfold sched_inv. split_and!; [|by split|exact Hc].
      destruct Ha. split; unfold pending, has_kind in *; simpl; try done.
      - intros o' y. destruct (decide (o' = o)) as [->|Hne].
        + rewrite lookup_insert_eq. intros [=<-]. apply acq_detached.
        + rewrite lookup_insert_ne by done. eauto.
      - apply sink_insert; [done|]. intros p Hp. simpl in Hp. eauto.
      - by apply is_Some_insert_owner. }
    destruct (Steardown _ IH x o _ _ _ _ _ Hi1 (mk_is_Some _ _ Hz1) Hm) as [H2 (A1&A2&A3)].
    split; [done|]. unfold post. split_and!.
    - destruct A1. split; simpl in *; try done.
    - eapply data_rel_trans; [|exact A2]. split; simpl.
      + intros n d Hd. by exists d.
      + done.
      + done.
      + intros o' y Hy. destruct (decide (o' = o)) as [->|Hne].
        * rewrite lookup_insert_eq. rewrite Hz in Hy. injection Hy as <-. by eexists.
        * rewrite lookup_insert_ne by done. by exists y.
    - intros h. specialize (A3 h). unfold hw in A3. rewrite Hz1 in A3. simpl in A3.
      pose proof (owners_pending_insert h _ o z (owner_detached z) Hz) as Hp.
      assert (Hdet : owner_pending h (owner_detached z) = 0).
      { unfold owner_pending. simpl. by destruct (decide _). }
      assert (Hlen : length (owner_cleanups z) <= length (o_cleanups z)).
      { unfold owner_cleanups. rewrite length_rev. generalize (o_cleanup_envs z) (o_cleanup_order z).
        induction (o_cleanups z) as [|b l IHl]; intros [|e es] [|k os]; simpl; try lia. specialize (IHl es os). lia. }
      unfold owner_pending in Hp. simpl in Hp.
      unfold cleanup_score, zero in *. simpl in A3. assert (registered h s1 = registered h s) as Hr by done.
      rewrite Hr in A3. destruct (decide (o_host z = h)); simpl in *; lia.
  Qed.

  Lemma sok_teardown_S x o ls cs s s' a :
    sched_inv x s -> owner_exists o s -> teardown (S f) o ls cs s = Some (s', a) ->
    sched_inv x s' /\ full (fun h => length cs * hw s o h) s s'.
  Proof.
    intros Hi Hex Hm. cbn [teardown] in Hm.
    assert (Hrel : forall l ls', (let* _ := release f (l_child l) in teardown f o ls' cs) s = Some (s', a) ->
       sched_inv x s' /\ full (fun h => length cs * hw s o h) s s').
    { intros l ls' Hr. unfold m_bind in Hr. destruct (release f _ s) as [[s1 []]|] eqn:E; [|done].
      destruct (Srelease _ IH x _ _ _ _ Hi I E) as [H1 F1].
      destruct (Steardown _ IH x o ls' cs _ _ _ H1 (sstable _ _ F1 Hex) Hr) as [H2 F2].
      split; [done|]. eapply full_mono; [|eapply full_trans; [apply (full_zero (fun _ => 0)); exact F1|exact F2]].
      intros h. cbn beta. rewrite (hw_kept _ _ o h (proj1 (proj2 F1)) Hex). lia. }
    assert (Hcl : forall b e t ls' cs', cs = (b, e, t) :: cs' ->
       (let* _ := run_cleanup f o b e in teardown f o ls' cs') s = Some (s', a) ->
       sched_inv x s' /\ full (fun h => length cs * hw s o h) s s').
    { intros b e t ls' cs' Hcs Hr. unfold m_bind in Hr. destruct (run_cleanup f o b e s) as [[s1 []]|] eqn:E; [|done].
      destruct (Scleanup _ IH x o b e _ _ _ Hi Hex E) as [H1 F1].
      assert (F1' : post s s1 \/ True) by auto.
      assert (Hex1 : owner_exists o s1).
      { destruct Hex as [y Hy]. destruct (r_hosts _ _ (proj1 (proj2 F1)) o y Hy) as (y'&Hy'&_). by exists y'. }
      destruct (Steardown _ IH x o ls' cs' _ _ _ H1 Hex1 Hr) as [H2 F2].
      split; [done|]. eapply full_mono; [|eapply full_trans; [exact F1|exact F2]].
      intros h. rewrite Hcs. cbn beta. rewrite (hw_kept _ _ o h (proj1 (proj2 F1)) Hex). simpl. lia. }
    destruct ls as [|l ls'], cs as [|[[b e] t] cs']; cbn in Hm.
    - unfold m_return in Hm. simplify_eq. split; [done|]. apply full_zero, post_refl.
    - eapply Hcl; [reflexivity|exact Hm].
    - eapply Hrel. exact Hm.
    - destruct (decide _).
      + eapply Hrel. exact Hm.
      + eapply Hcl; [reflexivity|exact Hm].
  Qed.

  Lemma sok_cleanup_S x o b e s s' a :
    sched_inv x s -> owner_exists o s -> run_cleanup (S f) o b e s = Some (s', a) ->
    sched_inv x s' /\ full (hw s o) s s'.
  Proof.
    intros Hi [z Hz] Hm. cbn [run_cleanup] in Hm. rewrite (bind_get_owner _ _ _ _ Hz) in Hm.
    unfold emit in Hm. rewrite bind_m_modify in Hm.
    set (s1 := with_trace (g_trace s ++ [EvCleanup (o_host z)]) s) in Hm.
    assert (H1 : sched_inv x s1 /\ full (hw s o) s s1).
    { destruct Hi as (Ha&[B1 B2 B3]&Hc). unfold sched_inv, full. split_and!.
      - destruct Ha. split; unfold pending, has_kind in *; simpl; done.
      - by split.
      - exact Hc.
      - split; auto.
      - split; simpl.
        + intros m d Hd. by exists d.
        + intros. rewrite count_runs_app. simpl. lia.
        + by eexists.
        + intros o' y Hy. by exists y.
      - intros h. unfold cleanup_score, registered, hw. simpl. rewrite Hz, count_cleanups_app. simpl.
        destruct (decide _); lia. }
    destruct H1 as [H1 F1].
    assert (Hrest : sok x x T
       (let* r := exec f e b in
        match r with
        | Ok _ => m_return tt
        | Fail y =>
          let* _ := record_error o y in
          emit (EvFail (o_host z) y)
        end)).
    { sgo IH. }
    destruct (Hrest _ _ _ H1 I Hm) as [H2 F2].
    split; [done|]. eapply full_mono; [|eapply full_trans; [exact F1|apply (full_zero (fun _ => 0)); exact F2]].
    intros h. simpl. lia.
  Qed.

  Lemma sok_release_S x n : sok x x T (release (S f) n).
  Proof. cbn [release]. sgo IH. Qed.

  Lemma sok_dispose_S x n : sok x x T (dispose_node (S f) n).
  Proof. cbn [dispose_node]. sgo IH. Qed.

  Lemma sok_attach_S x n o : sok x x T (attach (S f) n o).
  Proof.
    cbn [attach]. apply sok_get_owner_at. intros z. destruct (o_disposed z).
    - apply (sok_weaken _ _ T); [done|]. sgo IH.
    - apply sok_put_then; [done|done|done|intros Ha; by apply acq_child|]. sgo IH.
  Qed.

  Lemma sok_write_S x n v : sok x x T (write_signal (S f) n v).
  Proof. cbn [write_signal]. sgo IH. Qed.

  Lemma sok_request_S x : sok x x T (request_flush (S f)).
  Proof.
    intros s s' a Hi _ Hm. cbn [request_flush] in Hm. rewrite bind_get in Hm.
    destruct (live_queue s && _) eqn:E; [|unfold m_return in Hm; simplify_eq; split; [done|apply post_refl]].
    apply andb_true_iff in E as [_ E]. apply bool_decide_eq_true in E.
    destruct (outside_bodies s) eqn:Eo; [by eapply (Sflush _ IH)|].
    destruct (g_flushing s) eqn:Ef; [unfold m_return in Hm; simplify_eq; split; [done|apply post_refl]|].
    unfold m_modify in Hm. simplify_eq. apply step_owe; [done|done|done|].
    unfold outside_bodies in Eo. apply andb_false_iff in Eo as [Eo|Eo]; [apply andb_false_iff in Eo as [Eo|Eo]|].
    - left. apply bool_decide_eq_false in Eo. lia.
    - right; right; left. destruct (g_current s); [by eexists|done].
    - right; right; right. by apply negb_false_iff in Eo.
  Qed.

  Lemma sok_settle_S x s s' a :
    sched_data x s -> sched_ctx x s -> settle_flush (S f) s = Some (s', a) -> sched_inv x s' /\ post s s'.
  Proof.
    intros Hd Hc Hm. cbn [settle_flush] in Hm. rewrite bind_get in Hm.
    destruct (g_flush_owed s && _ && _) eqn:E.
    - apply andb_true_iff in E as [E E3]. apply andb_true_iff in E as [E1 E2].
      pose proof (sc_owed _ _ Hc E1) as Hnf.
      rewrite run_flush_clear_owed in Hm by done.
      set (s0 := with_flush_owed false s) in Hm.
      assert (Hi0 : sched_inv x s0).
      { unfold sched_inv. split_and!.
        - eapply data_of_same; [|exact Hd]. repeat split.
        - destruct Hc. by split.
        - done. }
      destruct (Sflush _ IH x _ _ _ Hi0 I Hm) as [H1 F1].
      split; [done|]. eapply post_trans; [|exact F1].
      unfold post. split_and!.
      + split; auto.
      + apply rel_of_same. repeat split.
      + apply clean_of_same. repeat split.
    - unfold m_return in Hm. simplify_eq. split; [|apply post_refl].
      unfold sched_inv. split_and!; [done|done|]. intros Ho. rewrite Ho in E. simpl in E.
      apply andb_false_iff in E as [E|E].
      + apply bool_decide_eq_false in E. right; left. lia.
      + unfold outside_bodies in E. apply andb_false_iff in E as [E|E]; [apply andb_false_iff in E as [E|E]|].
        * left. apply bool_decide_eq_false in E. lia.
        * right; right; left. destruct (g_current s'); [by eexists|done].
        * right; right; right. by apply negb_false_iff in E.
  Qed.

  Lemma sok_flush_S x : sok x x T (run_flush (S f)).
  Proof.
    intros s s' a Hi _ Hm. cbn [run_flush] in Hm. rewrite bind_get in Hm.
    destruct (g_flushing s) eqn:Ef; [unfold m_return in Hm; simplify_eq; split; [done|apply post_refl]|].
    rewrite bind_m_modify in Hm.
    pose proof Hi as (Ha&[B1 B2 B3]&Hc).
    assert (Hx : x = None).
    { destruct x; [|done]. exfalso. pose proof (B1 ltac:(by eexists)). congruence. }
    subst x.
    set (s0 := with_flush_owed false (with_flushing true s)) in Hm.
    assert (D0 : data_same s s0) by repeat split.
    assert (Hi0 : sched_inv None s0).
    { unfold sched_inv. split_and!.
      - by eapply data_of_same.
      - split; simpl; [done|done|intros _; by right].
      - done. }
    unfold m_bind in Hm. destruct (drain f s0) as [[s1 []]|] eqn:E; [|done].
    destruct (Sdrain _ IH _ _ _ Hi0 eq_refl E) as [(Ha1&[C1 C2 C3]&Hc1) (Bal&Dr&Cl)].
    unfold m_modify in Hm. simplify_eq.
    set (s2 := with_flushing false s1).
    assert (D1 : data_same s1 s2) by repeat split.
    destruct (wrap_rel _ _ _ _ _ D0 D1 Dr Cl) as [R1 R2].
    destruct Bal as [Q1 Q2 Q3 Q4 Q5 Q6]. simpl in Q1, Q2, Q3, Q4, Q5, Q6.
    assert (Ho1 : g_flush_owed s1 = false).
    { destruct (g_flush_owed s1) eqn:Eo; [|done]. pose proof (C2 eq_refl). congruence. }
    unfold sched_inv, post. split_and!.
    - by eapply data_of_same.
    - split; simpl; [done|done|]. rewrite Q1, Q2, Q4. rewrite Ef in B3. exact B3.
    - simpl. congruence.
    - split; simpl; try congruence.
    - done.
    - done.
  Qed.

  Lemma sok_drain_S : sok None None flushing_on (drain (S f)).
  Proof.
    intros s s' a Hi Hf Hm. cbn [drain] in Hm. rewrite bind_get in Hm.
    destruct (g_queue s !! g_queue_head s) as [n|] eqn:Hq.
    - rewrite bind_m_modify in Hm.
      destruct (step_pop n s Hi Hq Hf) as [H1 F1].
      set (s1 := with_queue_head (S (g_queue_head s)) s) in H1, F1, Hm.
      unfold m_bind in Hm. destruct (execute_effect f n s1) as [[s2 []]|] eqn:E; [|done].
      assert (Hk : has_kind n KEffect s1).
      { pose proof Hi as (Ha&_&_). apply (sstable _ _ F1). apply (sd_pending _ _ Ha).
        unfold pending. rewrite (drop_S _ n _ Hq). left. }
      assert (Hf1 : flushing_on s1) by exact Hf.
      destruct (Sexecute _ IH n _ _ _ H1 (conj Hk Hf1) E) as [H2 F2].
      assert (Hf2 : flushing_on s2) by (eapply sstable; [exact F2|exact Hf1]).
      destruct (Sdrain _ IH _ _ _ H2 Hf2 Hm) as [H3 F3].
      split; [done|]. eapply post_trans; [exact F1|]. by eapply post_trans.
    - unfold m_modify in Hm. simplify_eq. by apply step_reset.
  Qed.
End Step.

(**md
**Every engine function keeps the scheduler's invariant, at every fuel.**

When a function returns, [the scheduler's invariant](#sched_inv) still holds with the same in-progress effect, except that `execute_effect` of `n` starts with `n` in progress and ends with none. The current computation, the untracked flag, the batch and pull depths and the flushing flag are back at their starting values. A flush newly owed was owed from batch depth 0. Nodes keep their kinds, a disposed node stays disposed, a disposed effect's run count and `EvRun` count stay put, the trace only extends, owners keep their hosts, and the cleanup events and held cleanups of a host rise by at most its registrations. `run_cleanup` and `teardown` are allowed one more per cleanup they were handed, which the detached list held before.

Premises: the invariant holds at the start, and the function's own premise, as listed in `sched_ok`. A function that gets stuck or runs out of fuel returns nothing, and is outside this guarantee.

- [f:] the fuel.
*)
Theorem sched_ok_all f : sched_ok f.
Proof.
  induction f as [|f IH].
  - split; intros; try (intros s s' a _ _ Hm; done); done.
  - split.
    + apply sok_exec_S, IH.
    + apply sok_read_S, IH.
    + apply sok_read_node_S, IH.
    + apply sok_update_S, IH.
    + apply sok_rop_S, IH.
    + apply sok_pull_S, IH.
    + apply sok_refresh_S, IH.
    + apply sok_walk_S, IH.
    + apply sok_recompute_S, IH.
    + apply sok_run_memo_S, IH.
    + apply sok_execute_S, IH.
    + apply sok_run_effect_S, IH.
    + apply sok_body_S, IH.
    + apply sok_discharge_S, IH.
    + apply sok_retire_S, IH.
    + apply sok_dispose_scope_S, IH.
    + intros. eapply sok_teardown_S; eauto.
    + intros. eapply sok_cleanup_S; eauto.
    + apply sok_release_S, IH.
    + apply sok_dispose_S, IH.
    + apply sok_attach_S, IH.
    + apply sok_write_S, IH.
    + apply sok_request_S, IH.
    + intros. eapply sok_settle_S; eauto.
    + apply sok_flush_S, IH.
    + apply sok_drain_S, IH.
Qed.

(** The state at the start of a cleanup's body. *)
Definition cleanup_start (z : owner_state) (s : state) : state :=
  with_trace (g_trace s ++ [EvCleanup (o_host z)]) s.

Lemma step_cleanup_start x z s : sched_inv x s -> sched_inv x (cleanup_start z s).
Proof.
  intros (Ha&[B1 B2 B3]&Hc). unfold sched_inv. split_and!.
  - destruct Ha. split; unfold pending, has_kind in *; simpl; done.
  - by split.
  - exact Hc.
Qed.

(** The first event of a cleanup's run. *)
Lemma cleanup_emits f x o b e y s s' a :
  sched_inv x s -> g_owners s !! o = Some y -> run_cleanup (S f) o b e s = Some (s', a) ->
  (g_trace s ++ [EvCleanup (o_host y)]) `prefix_of` g_trace s'.
Proof.
  intros Hi Hz Hm. cbn [run_cleanup] in Hm. rewrite (bind_get_owner _ _ _ _ Hz) in Hm.
  unfold emit in Hm. rewrite bind_m_modify in Hm.
  pose proof (step_cleanup_start x y s Hi) as H1.
  pose proof (sched_ok_all f) as IH.
  assert (Hrest : sok x x T
     (let* r := exec f e b in
      match r with
      | Ok _ => m_return tt
      | Fail z =>
        let* _ := record_error o z in
        emit (EvFail (o_host y) z)
      end)).
  { sgo IH. }
  destruct (Hrest _ _ _ H1 I Hm) as [_ (_&F&_)]. exact (r_trace _ _ F).
Qed.

(** The disposal of a node that exists sets its flag, on every path. *)
Lemma dispose_node_disposes f x n s s' a :
  sched_inv x s -> is_Some (g_nodes s !! n) -> dispose_node f n s = Some (s', a) ->
  exists d, g_nodes s' !! n = Some d /\ n_disposed d = true.
Proof.
  intros Hi [d0 Hd0] Hm. destruct f as [|f]; [done|]. cbn [dispose_node] in Hm.
  rewrite (bind_get_node _ _ _ _ Hd0) in Hm.
  destruct (n_disposed d0) eqn:Hz.
  { unfold m_return in Hm. simplify_eq. by exists d0. }
  rewrite (bind_modify_node _ _ _ _ _ Hd0) in Hm.
  set (u := fun x => node_with_link None (node_with_disposed true x)) in Hm.
  assert (Hu : nstep u d0) by (unfold nstep, u; cbn; split_and!; intros; cbn in *; try split; done).
  destruct (step_node x n u d0 s Hu Hd0 Hi) as [H1 F1].
  pose proof (sched_ok_all f) as IH.
  assert (Hrest : sok x x T
     (let* _ := match n_link d0 with Some l => unlink l | None => m_return tt end in
      let* _ := edges_opt (sources_clear n) in
      match n_scope d0 with
      | Some o => retire f o
      | None => m_return tt
      end)).
  { sgo IH. }
  destruct (Hrest _ _ _ H1 I Hm) as [_ (_&R&_)].
  destruct (r_nodes _ _ R n (u d0) ltac:(simpl; apply lookup_insert_eq)) as (d'&Hd'&_&Hi'&_).
  exists d'. split; [done|]. by apply Hi'.
Qed.

(** ** The invariant holds wherever a program goes *)

(**md
**A new graph satisfies the scheduler's invariant.**

[The invariant](#sched_inv) holds of `init` under any flags, with no effect in progress.

- [fl:] the flags of the new graph.
*)
Theorem sched_inv_init fl : sched_inv None (init fl).
Proof.
  unfold sched_inv. split_and!.
  - split; simpl; try done.
    + intros n [d Hd]. simpl in Hd. rewrite lookup_empty in Hd. done.
    + intros n Hn. by apply not_elem_of_nil in Hn.
    + intros o y. destruct (decide (o = root_owner_id)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-]. apply acq_new.
      * rewrite lookup_insert_ne by done. rewrite lookup_empty. done.
    + intros o y p. destruct (decide (o = root_owner_id)) as [->|Hne].
      * rewrite lookup_insert_eq. intros [=<-] Hp. done.
      * rewrite lookup_insert_ne by done. rewrite lookup_empty. done.
  - split; simpl; try done.
    + intros [H|H]; [by destruct H|done].
  - done.
Qed.

(**md
**A whole program keeps the scheduler's invariant.**

If [the invariant](#sched_inv) holds at the start, with no effect in progress, and `run_top` returns, it holds in the final state.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state.
- [s':] the state after the run.
- [r:] the outcome of the run.
*)
Theorem sched_inv_run_top f c s s' r :
  sched_inv None s -> run_top f c s = Some (s', r) -> sched_inv None s'.
Proof. intros Hi Hm. exact (proj1 (Sexec _ (sched_ok_all f) None [] c _ _ _ Hi I Hm)). Qed.

(** ** The queue *)

(** Each node's pending entries and its [queued] flag, with [x] the effect between its dequeue and [queued <- false]. *)
Definition queue_agrees (x : option node_id) (s : state) : Prop :=
  forall n d, g_nodes s !! n = Some d ->
    entries_of n (pending s) <= 1 /\ (n_queued d = true <-> n ∈ pending s \/ x = Some n).

Lemma queue_agrees_once s : queue_agrees None s -> forall n, queue_once s n.
Proof.
  intros H n d Hd. destruct (H n d Hd) as [H1 H2]. split; [done|].
  intros Hf Hin. rewrite Hf in H2. destruct H2 as [_ G]. discriminate G. by left.
Qed.

(**md
**An effect has at most one live queue entry, and its `queued` flag says whether it has one.**

After a program, every node has at most one entry in the live part of `g_queue`, and its `queued` flag is set exactly when it has one. So `queue_once` holds of every node. During `resolve_walk` of an effect `n` in `Execute`, the same holds with one exception: `n`'s flag is set with no entry, since it has been dequeued. `execute_effect` ends with that exception gone.

Premise: [the scheduler's invariant](#sched_inv) holds at the start, and for the second and third parts its in-progress effect is `n`. That invariant includes this conclusion, so the theorem is a preservation statement; `queued_at_most_once_new_graph` discharges the premise for a program on a new graph.

- [f:] the fuel.
*)
Theorem queued_at_most_once f :
  (forall c s s' r, sched_inv None s -> run_top f c s = Some (s', r) ->
     queue_agrees None s' /\ forall n, queue_once s' n) /\
  (forall n i s s' a, sched_inv (Some n) s -> resolve_walk f n i s = Some (s', a) -> queue_agrees (Some n) s') /\
  (forall n s s' a, sched_inv (Some n) s -> has_kind n KEffect s -> g_flushing s = true ->
     execute_effect f n s = Some (s', a) -> queue_agrees None s').
Proof.
  split_and!.
  - intros c s s' r Hi Hm. pose proof (sched_inv_run_top f c s s' r Hi Hm) as (Ha&_).
    pose proof (sd_agree _ _ Ha) as H. split; [exact H|]. by apply queue_agrees_once.
  - intros n i s s' a Hi Hm. destruct (Swalk _ (sched_ok_all f) (Some n) n i _ _ _ Hi I Hm) as [(Ha&_) _].
    exact (sd_agree _ _ Ha).
  - intros n s s' a Hi Hk Hf Hm. destruct (Sexecute _ (sched_ok_all f) n _ _ _ Hi (conj Hk Hf) Hm) as [(Ha&_) _].
    exact (sd_agree _ _ Ha).
Qed.

(**md
**On a new graph, an effect has at most one live queue entry, and its `queued` flag says whether it has one.**

After a program run from `init` under any flags, every node has at most one entry in the live part of `g_queue`, and its `queued` flag is set exactly when it has one.

- [f:] the fuel.
- [fl:] the flags of the new graph.
- [c:] the command run.
- [s':] the state after the run.
- [r:] the outcome of the run.
*)
Corollary queued_at_most_once_new_graph f fl c s' r :
  run_top f c (init fl) = Some (s', r) -> queue_agrees None s' /\ forall n, queue_once s' n.
Proof. intros Hm. exact (proj1 (queued_at_most_once f) c _ s' r (sched_inv_init fl) Hm). Qed.

(** ** Flushes *)

(**md
**A flush entered during a flush changes nothing.**

Given any positive fuel, `run_flush` with `g_flushing` set returns the state it was given.

- [f:] the fuel, which `run_flush` runs at `S f`.
- [s:] the state, in which a flush is running and which `run_flush` returns unchanged.
*)
Theorem flush_no_nest f s : g_flushing s = true -> run_flush (S f) s = Some (s, tt).
Proof. intros H. cbn [run_flush]. rewrite bind_get, H. done. Qed.

(**md
**A program at top level leaves no flush owed.**

If `run_top` starts with no batch, no stale read and no current computation, and returns, then `g_flush_owed` is false at the end, whether or not an effect is queued.

Premise: [the scheduler's invariant](#sched_inv) holds at the start, with no effect in progress. This says nothing of effects still queued: [`read_move_leaves_effect_queued`](Ranvier.Pure.Witnesses.html#read_move_leaves_effect_queued) and [`failed_batch_leaves_queue`](Ranvier.Pure.Witnesses.html#failed_batch_leaves_queue) each end with an effect queued and no flush owed.

- [f:] the fuel.
- [c:] the command run.
- [s:] the starting state, outside any batch, pull or body.
- [s':] the state after the run.
- [r:] the outcome of the run.
*)
Theorem owed_settled f c s s' r :
  sched_inv None s -> g_batch_depth s = 0 -> g_pull_depth s = 0 -> g_current s = None ->
  run_top f c s = Some (s', r) -> g_flush_owed s' = false.
Proof.
  intros Hi Hb Hp Hc Hm.
  destruct (Sexec _ (sched_ok_all f) None [] c _ _ _ Hi I Hm) as [(_&[_ C2 C3]&C4) (Bal&_)].
  destruct (g_flush_owed s') eqn:Eo; [|done]. exfalso.
  pose proof (C2 eq_refl) as Hnf.
  destruct (C4 eq_refl) as [G|[G|[G|G]]].
  - rewrite (r_pull _ _ Bal), Hp in G. lia.
  - rewrite (r_batch _ _ Bal), Hb in G. lia.
  - rewrite (r_current _ _ Bal), Hc in G. by destruct G.
  - destruct (C3 (or_intror G)) as [H|H]; [|congruence]. rewrite (r_pull _ _ Bal), Hp in H. lia.
Qed.

Lemma stale_false_clean f : stale f = false -> f = Clean.
Proof. by destruct f. Qed.

Lemma drain_resets f s s1 a : drain f s = Some (s1, a) -> g_queue s1 = [] /\ g_queue_head s1 = 0.
Proof.
  revert s s1 a. induction f as [|f IH]; intros s s1 a Hd; [done|].
  cbn [drain] in Hd. rewrite bind_get in Hd. destruct (g_queue s !! g_queue_head s) as [n|].
  - rewrite bind_m_modify in Hd. unfold m_bind in Hd.
    destruct (execute_effect f n _) as [[s2 []]|]; [|done]. by eapply IH.
  - unfold m_modify in Hd. by simplify_eq.
Qed.

(**md
**A flush that returns leaves an empty queue and every live effect `Clean` and unqueued.**

When `run_flush` starts outside a flush and returns, `g_queue` is empty and its head is 0, and each effect that is not disposed is `Clean` with `queued` unset. This needs no premise on whether a body failed.

`Clean` is a mark: the effect has none left to act on. It does not say the effect read up-to-date values, and this conclusion holds on three Confirmed lost wake-ups: `self_marking_wake_lost` ([`self_marking_cleanup_left_behind`](Ranvier.Pure.Witnesses.html#self_marking_cleanup_left_behind)), [`walk_passed_source_marked`](Ranvier.Pure.Witnesses.html#walk_passed_source_marked) and [`effect_resolves_over_moved_source`](Ranvier.Pure.Witnesses.html#effect_resolves_over_moved_source). It does not say a marked effect ran either: [`flush_reruns_dirty`](#flush_reruns_dirty) holds the rerun. A disposed effect can stay stale. A flush that gets stuck or runs out of fuel returns nothing, and is outside this guarantee.

Premise: [the scheduler's invariant](#sched_inv) holds at the start, with no effect in progress.

- [f:] the fuel.
- [s:] the state before the flush, in which no flush is running.
- [s':] the state after the flush.
- [a:] the result of the flush.
*)
Theorem flush_returns_clean f s s' a :
  sched_inv None s -> g_flushing s = false -> run_flush f s = Some (s', a) ->
  g_queue s' = [] /\ g_queue_head s' = 0 /\
  forall n d, g_nodes s' !! n = Some d -> n_kind d = KEffect -> n_disposed d = false ->
    n_freshness d = Clean /\ n_queued d = false.
Proof.
  intros Hi Hf Hm.
  destruct f as [|f]; [done|]. cbn [run_flush] in Hm. rewrite bind_get, Hf in Hm. rewrite bind_m_modify in Hm.
  unfold m_bind in Hm. destruct (drain f _) as [[s1 []]|] eqn:E; [|done].
  unfold m_modify in Hm. simplify_eq.
  destruct (drain_resets _ _ _ _ E) as [Hq Hh].
  destruct (Sflush _ (sched_ok_all (S f)) None s (with_flushing false s1) tt Hi I) as [(Ha&_) _].
  { cbn [run_flush]. rewrite bind_get, Hf, bind_m_modify. unfold m_bind. rewrite E. done. }
  split_and!; [done|done|].
  intros n d Hd Hk Hl.
  destruct (sd_agree _ _ Ha n d Hd) as [_ H2].
  assert (Hp : pending (with_flushing false s1) = []) by (unfold pending; simpl; rewrite Hq; apply drop_nil).
  assert (Hq0 : n_queued d = false).
  { destruct (n_queued d) eqn:Eq; [|done]. destruct H2 as [G _]. rewrite Hp in G.
    destruct (G eq_refl) as [G1|G1]; [by apply not_elem_of_nil in G1|done]. }
  split; [|done]. apply stale_false_clean.
  destruct (stale (n_freshness d)) eqn:Es; [|done].
  pose proof (sd_stale _ _ Ha n d Hd Hk Hl Es). congruence.
Qed.

Lemma count_runs_prefix n a b : a `prefix_of` b -> count_runs n a <= count_runs n b.
Proof. intros [k ->]. rewrite count_runs_app. lia. Qed.

Lemma body_runs f n s s' a :
  sched_inv None s -> eff_ctx n s -> live_of s n -> run_effect_body f n s = Some (s', a) ->
  S (count_runs n (g_trace s)) <= count_runs n (g_trace s').
Proof.
  intros Hi Hc (d&Hd&Hl) Hm. destruct f as [|f]; [done|]. rewrite (body_unfold _ _ _ _ Hd) in Hm.
  destruct (step_body_start n d s Hi Hd Hl) as [H2 F2].
  destruct (sok_body_rest f (sched_ok_all f) n d _ _ _ H2 (sstable _ _ F2 Hc) Hm) as [_ (_&R&_)].
  pose proof (count_runs_prefix n _ _ (r_trace _ _ R)) as Hp.
  unfold body_start in Hp. cbn [g_trace with_trace] in Hp. rewrite count_runs_app in Hp. simpl in Hp.
  destruct (decide (n = n)); [lia|done].
Qed.

Lemma run_effect_runs f n s s' a :
  sched_inv None s -> eff_ctx n s -> run_effect f n s = Some (s', a) ->
  S (count_runs n (g_trace s)) <= count_runs n (g_trace s') \/
  exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true.
Proof.
  intros Hi Hc Hm. destruct f as [|f]; [done|]. cbn [run_effect] in Hm.
  unfold push_running, edges in Hm. rewrite !bind_m_modify in Hm.
  pose proof (sched_ok_all f) as IH.
  set (s2 := ghost_effect_start n (with_edges (sources_begin_run n (g_edges (with_running (n :: g_running s) s))) (with_running (n :: g_running s) s))) in Hm.
  assert (Hc2 : core_same s s2) by (unfold s2, ghost_effect_start; repeat split).
  destruct (sched_same None s s2 Hc2 Hi) as [H2 F2].
  pose proof (sstable _ _ F2 Hc) as Hc2'.
  destruct (proj1 Hc2') as (d&Hd&_).
  rewrite (bind_get_node _ _ _ _ Hd) in Hm.
  apply bind_inv in Hm as (s3&[]&Ep&Hm).
  assert (Hscope : sok None None (eff_ctx n)
    (match n_scope d with
     | Some o =>
       let* _ := modify_node n (node_with_in_cleanup true) in
       let* _ := discharge f o in
       modify_node n (node_with_in_cleanup false)
     | None => m_return tt
     end)).
  { sgo IH. }
  destruct (Hscope _ _ _ H2 Hc2' Ep) as [H3 F3].
  pose proof (sstable _ _ F3 Hc2') as Hc3.
  destruct (proj1 Hc3) as (d3&Hd3&_).
  rewrite (bind_get_node _ _ _ _ Hd3) in Hm.
  apply bind_inv in Hm as (s4&[]&Eb&Hm).
  unfold pop_running, m_modify in Hm. simplify_eq. cbn [g_trace g_nodes with_running].
  assert (Hle : count_runs n (g_trace s) <= count_runs n (g_trace s3)).
  { apply count_runs_prefix. etrans; [exact (r_trace _ _ (proj1 (proj2 F2)))|exact (r_trace _ _ (proj1 (proj2 F3)))]. }
  destruct (n_disposed d3) eqn:Ez.
  - unfold m_return in Eb. simplify_eq. right. exists d3. done.
  - left. pose proof (body_runs _ n _ _ _ H3 Hc3 (ex_intro _ d3 (conj Hd3 Ez)) Eb). lia.
Qed.

(** The end of [Execute] for an effect that is [Dirty] at this point. *)
Lemma execute_tail f n d s s' a :
  sched_inv (Some n) s -> eff_ctx n s -> g_nodes s !! n = Some d -> n_freshness d = Dirty ->
  (let* d' := get_node n in
   let* _ := modify_node n (fun x => node_with_freshness Clean (node_with_queued false x)) in
   if decide (n_freshness d' = Dirty) then run_effect f n else m_return tt) s = Some (s', a) ->
  S (count_runs n (g_trace s)) <= count_runs n (g_trace s') \/
  exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true.
Proof.
  intros Hi Hc Hd HD Hm.
  rewrite (bind_get_node _ _ _ _ Hd) in Hm.
  set (u := fun x => node_with_freshness Clean (node_with_queued false x)) in Hm.
  destruct (sok_unflag_at (eff_ctx n) n u d eq_refl eq_refl (fun H => H) (fun _ _ => eq_refl) (fun _ _ => eq_refl)
     s (set_node n (u d) s) tt Hi (conj Hc Hd)) as [H1 F1].
  { unfold modify_node. by rewrite Hd. }
  rewrite (bind_modify_node _ _ _ _ _ Hd) in Hm.
  destruct (decide (n_freshness d = Dirty)) as [_|E]; [|done].
  pose proof (sstable _ _ F1 Hc) as Hc1.
  assert (Hcount : count_runs n (g_trace (set_node n (u d) s)) = count_runs n (g_trace s)) by done.
  rewrite <- Hcount. exact (run_effect_runs _ n _ _ _ H1 Hc1 Hm).
Qed.

(**md
**A live effect that is `Dirty` at its dequeue, or raised to `Dirty` by its walk, runs again, unless it is disposed first.**

**`Dirty` at the dequeue.** When `execute_effect` of a live, `Dirty` effect returns, its `EvRun` count has risen by at least one, or the effect is disposed.

**`Check` at the dequeue.** When its `resolve_walk` leaves it `Dirty`, and `execute_effect` returns, its `EvRun` count has risen by at least one from the walk's end, or the effect is disposed.

The disposal is a cleanup of its discharge or of a run in its walk: `run_effect` tests for it ahead of the body. This is the rerun guarantee beside [`flush_returns_clean`](#flush_returns_clean), which holds of an engine that clears marks and never runs. A walk that leaves the effect in `Check` is outside it: [`effect_resolves_over_moved_source`](Ranvier.Pure.Witnesses.html#effect_resolves_over_moved_source) resolves `Clean` with no run over a source that moved.

Premises: [the scheduler's invariant](#sched_inv) holds with `n` in progress, `n` is an effect, and a flush is running.

- [f:] the fuel.
- [n:] the effect executed.
*)
Theorem flush_reruns_dirty f n :
  (forall d s s' a,
     sched_inv (Some n) s -> has_kind n KEffect s -> g_flushing s = true ->
     g_nodes s !! n = Some d -> n_disposed d = false -> n_freshness d = Dirty ->
     execute_effect f n s = Some (s', a) ->
     S (count_runs n (g_trace s)) <= count_runs n (g_trace s') \/
     exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true) /\
  (forall d d1 s s1 s' a,
     sched_inv (Some n) s -> has_kind n KEffect s -> g_flushing s = true ->
     g_nodes s !! n = Some d -> n_disposed d = false -> n_freshness d = Check ->
     resolve_walk f n 0 s = Some (s1, tt) -> g_nodes s1 !! n = Some d1 -> n_freshness d1 = Dirty ->
     execute_effect (S f) n s = Some (s', a) ->
     S (count_runs n (g_trace s1)) <= count_runs n (g_trace s') \/
     exists d', g_nodes s' !! n = Some d' /\ n_disposed d' = true).
Proof.
  split.
  - intros d s s' a Hi Hk Hf Hd Hz HD Hm. destruct f as [|f]; [done|]. cbn [execute_effect] in Hm.
    rewrite (bind_get_node _ _ _ _ Hd), Hz in Hm.
    destruct (decide (n_freshness d = Check)) as [E|_]; [congruence|].
    rewrite bind_m_return in Hm.
    exact (execute_tail f n d s s' a Hi (conj Hk Hf) Hd HD Hm).
  - intros d d1 s s1 s' a Hi Hk Hf Hd Hz HC Hw Hd1 HD Hm. cbn [execute_effect] in Hm.
    rewrite (bind_get_node _ _ _ _ Hd), Hz in Hm.
    destruct (decide (n_freshness d = Check)) as [_|E]; [|done].
    rewrite bind_bind, (bind_some _ _ _ _ _ Hw), bind_bind, (bind_get_node _ _ _ _ Hd1) in Hm.
    destruct (decide (n_freshness d1 = Check)) as [E|_]; [congruence|].
    rewrite bind_m_return in Hm.
    destruct (Swalk _ (sched_ok_all f) (Some n) n 0 _ _ _ Hi I Hw) as [H1 F1].
    assert (Hc1 : eff_ctx n s1) by (eapply sstable; [exact F1|exact (conj Hk Hf)]).
    exact (execute_tail f n d1 s1 s' a H1 Hc1 Hd1 HD Hm).
Qed.

Lemma edges_total (b : bool) n w :
  coherent w -> exists w', (if b then sources_clear n else sources_end_run n) w = Some w'.
Proof.
  intros Hc. destruct b.
  - destruct (coherent_clear n w Hc) as (w'&H&_). eauto.
  - destruct (coherent_end n w Hc) as (w'&H&_). eauto.
Qed.

Lemma bind_edges_ok {B} u (k : unit -> M B) s w :
  u (g_edges s) = Some w -> m_bind (edges_opt u) k s = k tt (with_edges w s).
Proof. intros H. unfold m_bind, edges_opt. by rewrite H. Qed.

(**md
**A failing effect body returns, with its failure in the effect's status.**

When an effect's body ends in `Fail y`, `run_effect_body` returns, the effect's status is `StError` and its failure is `y`, and the only event after the body's is `EvFail n y`. The queue and its head are as the body left them. This answers `Effect.RunBody`'s handler (Core.fs 4866). [`drain_continues`](#drain_continues) is what happens next.

Premises: the body's result is `Fail y`, from the state `run_effect_body` starts it in, and the effect exists at the body's end. The edges there are coherent, a premise this theorem takes and does not derive: `engine_settled` holds it at a function's return, and no theorem states it at a body's end.

- [f:] the fuel.
- [n:] the effect whose body fails.
- [s:] the state before the body.
- [s2:] the state when the body returns.
- [d:] the node `n` in `s`.
- [d2:] the node `n` in `s2`.
- [y:] the failure the body returns.
*)
Theorem body_failure_contained f n s s2 d d2 y :
  g_nodes s !! n = Some d ->
  hosted n (exec f (n_env d) (n_body d)) (body_start n d s) = Some (s2, Fail y) ->
  g_nodes s2 !! n = Some d2 -> coherent (g_edges s2) ->
  exists s' d', run_effect_body (S f) n s = Some (s', tt) /\
    g_nodes s' !! n = Some d' /\ n_status d' = StError /\ n_failure d' = Some y /\
    g_trace s' = g_trace s2 ++ [EvFail n y] /\ g_queue s' = g_queue s2 /\ g_queue_head s' = g_queue_head s2.
Proof.
  intros Hd Hb Hd2 Hco. rewrite (body_unfold _ _ _ _ Hd). unfold body_rest.
  rewrite (bind_some _ _ _ _ _ Hb), (bind_get_node _ _ _ _ Hd2).
  destruct (edges_total (n_disposed d2) n _ Hco) as (w&Hw).
  rewrite (bind_edges_ok _ _ _ _ Hw).
  assert (Hd3 : g_nodes (with_edges w s2) !! n = Some d2) by exact Hd2.
  rewrite (bind_modify_node _ _ _ _ _ Hd3). unfold emit, m_modify.
  do 2 eexists. split_and!; [reflexivity|simpl; apply lookup_insert_eq|reflexivity..].
Qed.

(**md
**The flush goes on after an effect.**

When the entry at the head of the queue is `n`, `drain` consumes it, and once `execute_effect` of `n` returns, the rest of `drain` is the same call on the state it returned. This holds of every result `execute_effect` returns, whatever the effect's body did: [`body_failure_contained`](#body_failure_contained) gives the return for a failing body.

- [f:] the fuel, which `drain` runs at `S f`.
- [n:] the effect at the queue head.
- [s:] the state before the dequeue.
- [s1:] the state after `n` executes.
*)
Theorem drain_continues f n s s1 :
  g_queue s !! g_queue_head s = Some n ->
  execute_effect f n (with_queue_head (S (g_queue_head s)) s) = Some (s1, tt) ->
  drain (S f) s = drain f s1.
Proof.
  intros Hq He. cbn [drain]. rewrite bind_get, Hq, bind_m_modify.
  by rewrite (bind_some _ _ _ _ _ He).
Qed.

(**md
**A failing cleanup returns, with its failure recorded on the owner's sink.**

When a cleanup's body ends in `Fail y`, `run_cleanup` returns, the failure is appended to the errors of the owner's sink, or of the owner when it has none, and the only event after the body's is `EvFail` of the host. This answers `Owner.DisposeScope`'s handler (Core.fs 1171-1184). [`teardown_continues`](#teardown_continues) is what happens next.

Premises: [the scheduler's invariant](#sched_inv) holds, the owner exists, and the body's result is `Fail y`, from the state `run_cleanup` starts it in.

- [f:] the fuel.
- [x:] the effect in progress for `sched_inv`, if any.
- [o:] the owner whose cleanup runs.
- [b:] the cleanup body.
- [e:] the cleanup's environment.
- [z:] the owner state of `o` in `s`.
- [y:] the failure the cleanup returns.
- [s:] the state before the cleanup.
- [s2:] the state when the cleanup body returns.
*)
Theorem cleanup_failure_contained f x o b e z y s s2 :
  sched_inv x s -> g_owners s !! o = Some z ->
  exec f e b (cleanup_start z s) = Some (s2, Fail y) ->
  exists s' t w0 w, run_cleanup (S f) o b e s = Some (s', tt) /\
    g_owners s2 !! t = Some w0 /\ g_owners s' !! t = Some w /\ o_errors w = o_errors w0 ++ [y] /\
    g_trace s' = g_trace s2 ++ [EvFail (o_host z) y].
Proof.
  intros Hi Hz Hb.
  pose proof (step_cleanup_start x z s Hi) as H1.
  destruct (Sexec _ (sched_ok_all f) x e b _ _ _ H1 I Hb) as [(Ha2&_) (_&R&_)].
  destruct (r_hosts _ _ R o z Hz) as (z2&Hz2&_).
  cbn [run_cleanup]. rewrite (bind_get_owner _ _ _ _ Hz). unfold emit. rewrite bind_m_modify.
  unfold cleanup_start in Hb. rewrite (bind_some _ _ _ _ _ Hb). unfold record_error.
  rewrite bind_bind, (bind_get_owner _ _ _ _ Hz2).
  destruct (o_sink z2) as [p|] eqn:Es.
  - destruct (sd_sink _ _ Ha2 o z2 p Hz2 Es) as [w0 Hw0].
    rewrite bind_bind, (bind_get_owner _ _ _ _ Hw0). unfold put_owner, m_modify, m_bind.
    eexists. exists p, w0, (owner_with_error y w0).
    split_and!; [reflexivity|exact Hw0|simpl; apply lookup_insert_eq|reflexivity|reflexivity].
  - unfold put_owner, m_modify, m_bind.
    eexists. exists o, z2, (owner_with_error y z2).
    split_and!; [reflexivity|exact Hz2|simpl; apply lookup_insert_eq|reflexivity|reflexivity].
Qed.

(**md
**A teardown goes on after each step.**

Once a cleanup or a child's release returns, the rest of `teardown` is the same call on the state it returned, with that step gone. Together with [`cleanup_failure_contained`](#cleanup_failure_contained), a failing cleanup leaves the steps behind it to run. A child's release has no outcome of its own: its failure is a cleanup of the child's scope, which the same theorem covers. The scenario `flush_child_release` runs that on the F#.

- [f:] the fuel.
- [o:] the owner being torn down.
*)
Theorem teardown_continues f o :
  (forall b e t cs s s1, run_cleanup f o b e s = Some (s1, tt) ->
     teardown (S f) o [] ((b, e, t) :: cs) s = teardown f o [] cs s1) /\
  (forall l ls b e t cs s s1, ~ t < l_acquired l -> run_cleanup f o b e s = Some (s1, tt) ->
     teardown (S f) o (l :: ls) ((b, e, t) :: cs) s = teardown f o (l :: ls) cs s1) /\
  (forall l ls cs s s1, release f (l_child l) s = Some (s1, tt) -> (cs = [] \/ exists b e t r, cs = (b, e, t) :: r /\ t < l_acquired l) ->
     teardown (S f) o (l :: ls) cs s = teardown f o ls cs s1).
Proof.
  split_and!.
  - intros b e t cs s s1 Hr. cbn [teardown]. by rewrite (bind_some _ _ _ _ _ Hr).
  - intros l ls b e t cs s s1 Ht Hr. cbn [teardown].
    destruct (decide (t < l_acquired l)); [done|]. by rewrite (bind_some _ _ _ _ _ Hr).
  - intros l ls cs s s1 Hr [->|(b&e&t&r&->&Ht)]; cbn [teardown].
    + by rewrite (bind_some _ _ _ _ _ Hr).
    + destruct (decide (t < l_acquired l)); [|done]. by rewrite (bind_some _ _ _ _ _ Hr).
Qed.

(**md
**A failing late cleanup returns, with its failure recorded.**

A cleanup registered on a disposed scope runs under `detached`. When its body ends in `Fail y`, the `detached` call returns with the failure appended to the errors of the owner's sink, or of the owner when it has none. This answers `Owner.RunLate` (Core.fs 1025-1033). `exec` then goes on with the command after `CCleanup`, by [`late_registration_immediate`](Ranvier.Pure.Ownership.html#late_registration_immediate).

Premises: as for [`cleanup_failure_contained`](#cleanup_failure_contained), from the state `detached` starts the cleanup in.

- [f:] the fuel.
- [x:] the effect in progress for `sched_inv`, if any.
- [o:] the disposed owner a cleanup is registered with.
- [b:] the cleanup body.
- [e:] the cleanup's environment.
- [z:] the owner state of `o` in `s`.
- [y:] the failure the cleanup returns.
- [s:] the state before the cleanup.
- [s2:] the state when the cleanup body returns.
*)
Theorem late_cleanup_contained f x o b e z y s s2 :
  sched_inv x s -> g_owners s !! o = Some z ->
  exec f e b (cleanup_start z (detached_state o s)) = Some (s2, Fail y) ->
  exists s' t w0 w, detached o (run_cleanup (S f) o b e) s = Some (s', tt) /\
    g_owners s2 !! t = Some w0 /\ g_owners s' !! t = Some w /\ o_errors w = o_errors w0 ++ [y].
Proof.
  intros Hi Hz Hb.
  destruct (cleanup_failure_contained f x o b e z y _ s2 (detached_in x o s Hi) Hz Hb) as (s3&t&w0&w&Hr&H0&H1&H2&_).
  rewrite detached_unfold, Hr. do 4 eexists. split_and!; [reflexivity|exact H0|exact H1|exact H2].
Qed.

Lemma measure_set n u d s :
  g_nodes s !! n = Some d -> mark_score u = mark_score d -> mark_measure (set_node n u s) = mark_measure s.
Proof.
  intros Hd Hs. unfold mark_measure. simpl. pose proof (nodes_score_insert _ n d u Hd). lia.
Qed.

Lemma run_finish_tail n s d :
  g_nodes s !! n = Some d ->
  exists s' d', (let* d3 := get_node n in if decide (n_freshness d3 = Clean) then m_return tt else announce n) s = Some (s', tt) /\
    g_nodes s' !! n = Some d' /\ read_view_of d' = read_view_of d.
Proof.
  intros Hd. rewrite (bind_get_node _ _ _ _ Hd). destruct (decide _).
  - do 2 eexists. split_and!; [reflexivity|exact Hd|done].
  - unfold announce. destruct (f_unannounced _).
    + unfold modify_node. rewrite Hd. eexists. exists (node_with_unannounced true d). split_and!; [reflexivity|simpl; apply lookup_insert_eq|reflexivity].
    + do 2 eexists. split_and!; [reflexivity|exact Hd|done].
Qed.

Lemma run_finish_some f n mv s d :
  g_nodes s !! n = Some d -> mark_measure s < f ->
  exists s' d', run_finish f n mv s = Some (s', tt) /\ g_nodes s' !! n = Some d' /\ read_view_of d' = read_view_of d.
Proof.
  intros Hd Hb. unfold run_finish. rewrite bind_get, bind_m_modify.
  set (s1 := ghost_run_end n (g_current s) mv s).
  assert (Hs1 : g_nodes s1 = g_nodes s) by (unfold s1, ghost_run_end; by destruct (g_nodes s !! n)).
  assert (Hd1 : g_nodes s1 !! n = Some d) by (by rewrite Hs1).
  destruct mv.
  - assert (Hb1 : mark_measure s1 < f) by (unfold mark_measure; by rewrite Hs1).
    destruct (notify_terminates f n (g_current s) s1 Hb1) as (_&_&[s2 Hn]).
    assert (Hrt : rtc mark_prim s1 s2).
    { apply (any_mark_rtc f n (g_current s)). unfold any_mark. tauto. }
    destruct (mf_forward _ _ (marks_fixed _ _ Hrt) n d Hd1) as (d2&Hd2&_&_&_&Hv).
    unfold m_bind at 1, m_step. rewrite Hn.
    destruct (run_finish_tail n s2 d2 Hd2) as (s'&d'&H1&H2&H3).
    exists s', d'. split_and!; [exact H1|exact H2|congruence].
  - rewrite bind_m_return. destruct (run_finish_tail n s1 d Hd1) as (s'&d'&H1&H2&H3). eauto.
Qed.

(**md
**A failing memo run returns, with a failure in the memo's status.**

When a memo's run fails with `y`, `run_memo` returns and the memo's status is `StError`. Its failure is `y`. The one exception is a memo that already held `StError` and `y`: Core.fs 4266-4274 then consults the comparer, and a comparer that throws `z` replaces the failure with `z`, as Core.fs 4275-4279 does. A reader takes the failure from the status. This answers `Memo.Run`'s handlers (Core.fs 4247-4261).

The run fails with `y` in two ways, both in the premise on `r`: the body's result is `Fail y`, or the body breached a pure memo's scope, which sets `n_violated` and makes `y` the fixed `pure_scope_failure` whatever the body returned (Core.fs 4248-4250). [`memo_scope_violation_returns`](#memo_scope_violation_returns) and [`memo_comparer_replaces_failure`](#memo_comparer_replaces_failure) run those two paths.

Premises: `r` is the body's result, from the state `run_memo` starts it in, and the memo exists at the body's end. The edges there are coherent, and the fuel exceeds `mark_measure` there, so the notice has a result. This theorem takes both and does not derive them.

- [f:] the fuel.
- [n:] the memo whose body runs.
- [s:] the state before the run.
- [s2:] the state when the body returns.
- [d:] the node `n` in `s`.
- [d1:] the node `n` in `s2`.
- [r:] the outcome of the body.
- [y:] the failure of the body.
*)
Theorem memo_failure_contained f n s s2 d d1 r y :
  g_nodes s !! n = Some d ->
  hosted n (exec f (run_env d) (n_body d)) (run_start n d s) = Some (s2, r) ->
  g_nodes s2 !! n = Some d1 ->
  (if n_violated d1 then Fail pure_scope_failure else r) = Fail y ->
  coherent (g_edges s2) -> mark_measure s2 < f ->
  exists s' d', run_memo (S f) n s = Some (s', tt) /\
    g_nodes s' !! n = Some d' /\ n_status d' = StError /\
    (n_failure d' = Some y \/
     exists z, n_comparer d1 = CmpThrows z /\ n_status d = StError /\ n_failure d = Some y /\ n_failure d' = Some z).
Proof.
  intros Hd Hb Hd1 Hres Hco Hfu.
  destruct (edges_total (n_disposed d1) n _ Hco) as (w&Hw).
  set (u := fun x => node_with_violated false (node_with_status StError (node_with_failure (Some y) x))).
  set (s9 := set_node n (u d1) (with_running (tail (g_running s2)) (with_edges w s2))).
  assert (Hd9 : g_nodes s9 !! n = Some (u d1)) by (simpl; apply lookup_insert_eq).
  assert (Hb9 : mark_measure s9 < f).
  { assert (Hd7 : g_nodes (with_running (tail (g_running s2)) (with_edges w s2)) !! n = Some d1) by exact Hd1.
    unfold s9. rewrite (measure_set n (u d1) d1 _ Hd7 eq_refl). exact Hfu. }
  assert (Hshape : exists mv s10 d10, run_memo (S f) n s = run_finish f n mv s10 /\ g_nodes s10 !! n = Some d10 /\
      mark_measure s10 < f /\ n_status d10 = StError /\
      (n_failure d10 = Some y \/
       exists z, n_comparer d1 = CmpThrows z /\ n_status d = StError /\ n_failure d = Some y /\ n_failure d10 = Some z)).
  { cbn [run_memo]. rewrite (bind_get_node _ _ _ _ Hd), (bind_modify_node _ _ _ _ _ Hd).
    unfold edges, push_running. rewrite !bind_m_modify.
    erewrite bind_some; [|exact Hb]. cbv beta.
    rewrite (bind_get_node _ _ _ _ Hd1), Hres. cbn iota.
    rewrite bind_m_return. rewrite (bind_edges_ok _ _ _ _ Hw).
    unfold pop_running. rewrite bind_m_modify.
    assert (Hd8 : g_nodes (with_running (tail (g_running (with_edges w s2))) (with_edges w s2)) !! n = Some d1) by exact Hd1.
    rewrite (bind_modify_node _ _ _ _ _ Hd8).
    change (set_node n _ (with_running (tail (g_running (with_edges w s2))) (with_edges w s2))) with s9.
    rewrite (bind_get_node _ _ _ _ Hd9).
    destruct (decide _) as [Hne|Hsame].
    - rewrite bind_m_return. exists true, s9, (u d1). split_and!; [reflexivity|done|done|done|by left].
    - destruct (n_comparer (u d1)) as [| |z] eqn:Ec.
      + rewrite bind_m_return. eexists. exists s9, (u d1). split_and!; [reflexivity|done|done|done|by left].
      + rewrite bind_m_return. exists true, s9, (u d1). split_and!; [reflexivity|done|done|done|by left].
      + rewrite bind_bind, (bind_modify_node _ _ _ _ _ Hd9), bind_m_return.
        set (t := fun x => node_with_status StError (node_with_failure (Some z) (node_with_value (n_value d) x))).
        exists true, (set_node n (t (u d1)) s9), (t (u d1)). split_and!.
        * reflexivity.
        * simpl. apply lookup_insert_eq.
        * rewrite (measure_set n (t (u d1)) (u d1) _ Hd9 eq_refl). exact Hb9.
        * done.
        * right. exists z. split_and!; [exact Ec| | |done].
          -- destruct (decide (n_status d = StError)) as [|N]; [done|].
             exfalso. apply Hsame. left. intros E. apply N. symmetry. exact E.
          -- destruct (decide (n_failure d = Some y)) as [|N]; [done|].
             exfalso. apply Hsame. right. intros E. apply N. symmetry. exact E. }
  destruct Hshape as (mv&s10&d10&->&Hd10&Hb10&H4&H5).
  destruct (run_finish_some f n mv s10 d10 Hd10 Hb10) as (s'&d'&H1&H2&H3).
  exists s', d'. unfold read_view_of in H3. injection H3 as _ G1 G2. split_and!; [exact H1|exact H2|congruence|].
  rewrite G2. exact H5.
Qed.

(**md
**A throwing comparer returns, with its failure in the memo's status.**

When a memo's comparer is consulted and throws `z`, `run_memo` returns, the memo's status is `StError`, its failure is `z`, and its value is the one from before the run. A reader takes the failure from the status. This answers `Memo.Run`'s handler of the comparison (Core.fs 4275-4279).

Premises: the body's result is `Ok v`, from the state `run_memo` starts it in. The memo exists at the body's end with no scope violation, and with the status and failure it had before the run, since otherwise the comparer is not consulted. The edges there are coherent, and the fuel exceeds `mark_measure` there. This theorem takes both and does not derive them.

- [f:] the fuel.
- [n:] the memo whose comparer throws.
- [s:] the state before the run.
- [s2:] the state when the body returns.
- [d:] the node `n` in `s`.
- [d1:] the node `n` in `s2`.
- [v:] the value the body returns.
- [z:] the failure the comparer throws.
*)
Theorem comparer_throw_contained f n s s2 d d1 v z :
  g_nodes s !! n = Some d ->
  hosted n (exec f (run_env d) (n_body d)) (run_start n d s) = Some (s2, Ok v) ->
  g_nodes s2 !! n = Some d1 -> n_violated d1 = false -> n_comparer d1 = CmpThrows z ->
  n_status d1 = n_status d -> n_failure d1 = n_failure d ->
  coherent (g_edges s2) -> mark_measure s2 < f ->
  exists s' d', run_memo (S f) n s = Some (s', tt) /\
    g_nodes s' !! n = Some d' /\ n_status d' = StError /\ n_failure d' = Some z /\ n_value d' = n_value d.
Proof.
  intros Hd Hb Hd1 Hv Hq Hst Hfa Hco Hfu.
  destruct (edges_total (n_disposed d1) n _ Hco) as (w&Hw).
  set (u := fun x => node_with_published true (node_with_value v x)).
  set (s6 := set_node n (u d1) s2).
  assert (Hw6 : (if n_disposed d1 then sources_clear n else sources_end_run n) (g_edges s6) = Some w) by exact Hw.
  set (s8 := with_running (tail (g_running s6)) (with_edges w s6)).
  assert (Hd8 : g_nodes s8 !! n = Some (u d1)) by (simpl; apply lookup_insert_eq).
  set (t := fun x => node_with_status StError (node_with_failure (Some z) (node_with_value (n_value d) x))).
  set (s10 := set_node n (t (u d1)) s8).
  assert (Hd10 : g_nodes s10 !! n = Some (t (u d1))) by (simpl; apply lookup_insert_eq).
  assert (Hshape : run_memo (S f) n s = run_finish f n true s10).
  { cbn [run_memo]. rewrite (bind_get_node _ _ _ _ Hd), (bind_modify_node _ _ _ _ _ Hd).
    unfold edges, push_running. rewrite !bind_m_modify.
    erewrite bind_some; [|exact Hb]. cbv beta.
    rewrite (bind_get_node _ _ _ _ Hd1), Hv. cbn iota.
    rewrite (bind_modify_node _ _ _ _ _ Hd1). fold s6.
    rewrite (bind_edges_ok _ _ _ _ Hw6).
    unfold pop_running. rewrite bind_m_modify. fold s8.
    rewrite bind_m_return, (bind_get_node _ _ _ _ Hd8).
    destruct (decide _) as [Hne|_].
    { exfalso. unfold u in Hne. simpl in Hne. rewrite Hst, Hfa in Hne. destruct Hne as [G|G]; by apply G. }
    assert (Hq' : n_comparer (u d1) = CmpThrows z) by exact Hq.
    rewrite Hq'. rewrite bind_bind, (bind_modify_node _ _ _ _ _ Hd8), bind_m_return. reflexivity. }
  rewrite Hshape.
  assert (Hb10 : mark_measure s10 < f).
  { unfold s10. rewrite (measure_set n (t (u d1)) (u d1) _ Hd8 eq_refl).
    assert (Hm8 : mark_measure s8 = mark_measure s6) by done. rewrite Hm8.
    unfold s6. rewrite (measure_set n (u d1) d1 _ Hd1 eq_refl). exact Hfu. }
  destruct (run_finish_some f n true s10 (t (u d1)) Hd10 Hb10) as (s'&d'&H1&H2&H3).
  exists s', d'. split_and!; [exact H1|exact H2| | |].
  - unfold read_view_of in H3. injection H3 as _ G _. exact G.
  - unfold read_view_of in H3. injection H3 as _ _ G. exact G.
  - unfold read_view_of in H3. injection H3 as G _ _. exact G.
Qed.

(**md
**A flush that returns ends with `g_flushing` false and an empty live queue.**

When `run_flush` starts outside a flush and returns, `g_flushing` is false, `pending` is empty and the head is 0. This statement has a returned flush as its premise, so it alone would hold of an engine turning a `Fail` into no result. These theorems close that. Each containment theorem takes a sub-body's result, a `Fail` or for the comparer an `Ok`, and concludes that the enclosing function returns, with the failure recorded where the F# records it. `teardown_continues` takes a step's return and goes on from it.

| Core.fs | Site | Theorem |
|---|---|---|
| 1025-1033 | `Owner.RunLate` | [`late_cleanup_contained`](#late_cleanup_contained) |
| 1171-1184 | `Owner.DisposeScope` | [`cleanup_failure_contained`](#cleanup_failure_contained), [`teardown_continues`](#teardown_continues) |
| 4247-4261 | `Memo.Run`, a body or a scope violation | [`memo_failure_contained`](#memo_failure_contained) |
| 4275-4279 | `Memo.Run`, a comparer | [`comparer_throw_contained`](#comparer_throw_contained) |
| 4866 | `Effect.RunBody` | [`body_failure_contained`](#body_failure_contained) |

[`drain_continues`](#drain_continues) takes the drain on from there, and [`flush_survives_body`](#flush_survives_body) and the four examples after it run each failure with an effect behind it.

`returns` excludes the fatal path: [`self_read_check_overflow`](Ranvier.Pure.Witnesses.html#self_read_check_resolves_itself) is a read with no result in the model, and a stack overflow that ends the process in the F#. This covers `Effect`. Other `IScheduled` items, such as a pass, are outside the model. `Effect.Execute` contains each of the command language's failures, so the comment at Core.fs 3140-3142, on an effect that throws, does not apply to `Effect` here.

- [f:] the fuel.
- [s:] the state before the flush, in which no flush is running.
- [s':] the state after the flush.
- [a:] the result of the flush.
*)
Theorem drain_no_escape f s s' a :
  g_flushing s = false -> run_flush f s = Some (s', a) ->
  g_flushing s' = false /\ pending s' = [] /\ g_queue_head s' = 0.
Proof.
  intros Hf Hm. destruct f as [|f]; [done|].
  cbn [run_flush] in Hm. rewrite bind_get, Hf, bind_m_modify in Hm.
  unfold m_bind in Hm. destruct (drain f _) as [[s1 []]|] eqn:E; [|done].
  unfold m_modify in Hm. simplify_eq.
  destruct (drain_resets _ _ _ _ E) as [Hq Hh]. split_and!; simpl; try done.
  unfold pending. simpl. rewrite Hq. apply drop_nil.
Qed.

(** ** Each failure, with a later effect *)

Local Notation V := EVar.
Local Notation K := EConst.
Local Notation Nop := (CRet (EConst 0%Z)).

(** An effect over a signal, a failing effect over it, and a write. A write's walk runs backward over the observers,
    so the failing effect runs first and the other is behind it in the queue. *)
Definition flush_case (failing : cmd) : cmd :=
  CSignal 1 (K 0%Z) (
  CEffect 2 (CRead 10 (V 1) (CLog (V 10) Nop)) (
  CEffect 3 failing (
  CWrite (V 1) (K 1%Z) Nop))).

(** A memo over a signal, an effect over the signal, an effect that reads the memo, and a write. *)
Definition memo_case (q : comparer) (body : cmd) : cmd :=
  CSignal 1 (K 0%Z) (
  CMemo 2 Pure q body (
  CEffect 3 (CRead 10 (V 1) (CLog (V 10) Nop)) (
  CEffect 4 (CRead 10 (V 2) (CLog (V 10) Nop)) (
  CWrite (V 1) (K 1%Z) Nop)))).

(** A pure memo's body breaches its scope, and a read answers the fixed failure. Scenario [memo_scope_violation] checks
    this trace on the F#, reporting its [InvalidOperationException] as [pure_scope_failure]. *)
Definition memo_scope_violation : cmd :=
  CSignal 1 (K 0%Z) (
  CMemo 2 Pure CmpEq (CCleanup Nop Nop) (
  CTryRead 20 21 (V 2) Nop)).

Example memo_scope_violation_returns :
  trace_of 100000 current_flags memo_scope_violation = Some [EvHostRead 2 (Fail pure_scope_failure)].
Proof. vm_compute. reflexivity. Qed.

(** A memo fails with 7 on each run, and its throwing comparer is consulted on the second, over the unchanged failure.
    Scenario [memo_comparer_failure] checks this trace on the F#. *)
Definition memo_comparer_failure : cmd :=
  CSignal 1 (K 0%Z) (
  CMemo 2 Pure (CmpThrows 9%Z) (CRead 10 (V 1) (CThrow 7%Z)) (
  CTryRead 20 21 (V 2) (CWrite (V 1) (K 1%Z) (CTryRead 22 23 (V 2) Nop)))).

Example memo_comparer_replaces_failure :
  trace_of 100000 current_flags memo_comparer_failure =
    Some [EvHostRead 2 (Fail 7%Z); EvHostRead 2 (Fail 9%Z)].
Proof. vm_compute. reflexivity. Qed.

Definition flush_child_release : cmd :=
  flush_case (CRead 10 (V 1) (CEffect 11 (CCleanup (CThrow 8%Z) Nop) Nop)).

Definition flush_comparer : cmd :=
  memo_case (CmpThrows 9%Z) (CRead 10 (V 1) (CRet (K 0%Z))).

Definition flush_memo_failure : cmd :=
  memo_case CmpEq (CRead 10 (V 1) (CIf (V 10) (CThrow 9%Z) (CRet (V 10)))).

(** A body fails on its second run, and the effect behind it runs. Scenarios [S09a], and [S09b] for the next. *)
Example flush_survives_body :
  trace_of 100000 current_flags (flush_case (CRead 10 (V 1) (CIf (V 10) (CThrow 7%Z) Nop))) =
    Some [EvRun 2; EvLog 2 0%Z; EvRun 3; EvRun 3; EvFail 3 7%Z; EvRun 2; EvLog 2 1%Z].
Proof. vm_compute. reflexivity. Qed.

(** A cleanup fails at the discharge, and the body and the effect behind it run. *)
Example flush_survives_cleanup :
  trace_of 100000 current_flags (flush_case (CRead 10 (V 1) (CCleanup (CThrow 8%Z) Nop))) =
    Some [EvRun 2; EvLog 2 0%Z; EvRun 3; EvCleanup 3; EvFail 3 8%Z; EvRun 3; EvRun 2; EvLog 2 1%Z].
Proof. vm_compute. reflexivity. Qed.

(** A child's release fails through the child's cleanup. Scenario [flush_child_release]. *)
Example flush_survives_child_release :
  trace_of 100000 current_flags flush_child_release =
    Some [EvRun 2; EvLog 2 0%Z; EvRun 3; EvRun 4; EvCleanup 4; EvFail 4 8%Z; EvRun 3; EvRun 2; EvLog 2 1%Z; EvRun 5].
Proof. vm_compute. reflexivity. Qed.

(** A memo's run fails, its reader fails, and the effect behind it runs. Scenario [flush_memo_failure]. *)
Example flush_survives_memo_run :
  trace_of 100000 current_flags flush_memo_failure =
    Some [EvRun 3; EvLog 3 0%Z; EvRun 4; EvLog 4 0%Z; EvRun 4; EvFail 4 9%Z; EvRun 3; EvLog 3 1%Z].
Proof. vm_compute. reflexivity. Qed.

(** A comparer throws, the memo's reader fails, and the effect behind it runs. Scenario [flush_comparer]. *)
Example flush_survives_comparer :
  trace_of 100000 current_flags flush_comparer =
    Some [EvRun 3; EvLog 3 0%Z; EvRun 4; EvLog 4 0%Z; EvRun 4; EvFail 4 9%Z; EvRun 3; EvLog 3 1%Z].
Proof. vm_compute. reflexivity. Qed.
