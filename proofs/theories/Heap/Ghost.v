From stdpp Require Import prelude gmap.
From iris.algebra Require Import auth gmap excl_auth agree numbers.
From iris.algebra.lib Require Import gmap_view.
From iris.base_logic.lib Require Import own.
From Ranvier.Base Require Import Prelude.
From Ranvier.Pure Require Import ObserverSet SourceList State.
From iris.heap_lang Require Import proofmode notation.
From Ranvier.Heap Require Import ObserverSet SourceList.

(** * Ghost state of the heap graph

One ghost name holds four parts: the authoritative edge map, with a fragment per node; the monotone freshness
of each node; the agreement with the pure [state]; and the monotone map from each node to the locations of its
observer set and source list.

Freshness is monotone through a ghost epoch per node. The code of a node is [3 * epoch + rank], where the
rank is [Clean] 0, [Check] 1 or [Dirty] 2. A node's epoch advances by one at a freshness fall, a step that
lowers its rank, and is unchanged at every other step ([epoch_rule]). The code only grows, so a lower bound
taken at one state holds at every later one, and while the epoch is unchanged the bound is a lower bound on
the freshness itself ([is_graph_fresh_epoch]).

The graph value is a location holding the resolver closure. A new node extends the closure by
[resolver_extend], and its locations join the monotone location map, so a location read at one state is the
node's location at every later one ([graph_loc_agree]).

Obligations of the heap layers above this file (marking, runs, scheduler, ownership):

1. The node fields and the effect queue join [is_graph_at].
2. [is_graph_at] is sealed, with [is_graph_open] and [graph_add_node_spec] the only lemmas that change
   freshness codes, so the epoch protocol holds by construction.
3. Every freshness write that can fall steps the ghost state. [epoch_rule] compares the freshness at
   [is_graph_open] with the freshness at its close, so a fall and a rise between the two advance no epoch.
4. The walk has a specification indexed by its cursor, giving each visit's place in the order, for an exact
   specification against [mark_check].
5. If node disposal or id reuse is modelled, the epochs of ids outside the domain are constrained, or a lower
   bound on an absent id is excluded.

The adequacy obligations are in Task 16 (Adequacy) of the plan,
docs/superpowers/plans/2026-11-08-iris-rocq-core-proofs.md. *)

Definition edge_entry : Type := observer_set * source_list.

(**md
The entry of node `n` in the edge world `w`: its observer set and its source list.
*)
Definition edge_entry_of (w : EdgeWorld) (n : node_id) : edge_entry := (observers_of w n, sources_of w n).

Definition graphUR : ucmra :=
  prodUR (gmap_viewUR node_id (agreeR (leibnizO edge_entry)))
         (prodUR (authUR (gmapUR node_id max_natR))
                 (prodUR (excl_authUR (leibnizO State.state))
                         (authUR (gmapUR node_id (agreeR (leibnizO (loc * loc))))))).

(**md
The ghost state of a heap graph: the authoritative edge map, monotone freshness, the agreement with the pure
state and the monotone location map.
*)
Class graphG Σ := GraphG { graph_inG :: inG Σ graphUR }.

Definition graphΣ : gFunctors := #[GFunctor graphUR].

Global Instance subG_graphΣ {Σ} : subG graphΣ Σ -> graphG Σ.
Proof. solve_inG. Qed.

(**md
The edge entry of each node of `tbl`: its observer set and source list in `w`.
*)
Definition edge_map (tbl : gmap node_id (loc * loc)) (w : EdgeWorld) : gmap node_id edge_entry :=
  map_imap (fun n _ => Some (edge_entry_of w n)) tbl.

Definition locs_ra (tbl : gmap node_id (loc * loc)) : gmap node_id (agreeR (leibnizO (loc * loc))) :=
  to_agree <$> tbl.

(**md
The freshness code of a node at epoch `e` with freshness `f`.
*)
Definition fresh_code (e : nat) (f : freshness) : nat := 3 * e + freshness_rank f.

(** The epoch of node [n] in [ep], 0 when absent. *)
Definition epoch (ep : gmap node_id nat) (n : node_id) : nat := default 0 (ep !! n).

(**md
The freshness code of each node of `nodes`, with the epochs `ep`.
*)
Definition fresh_map (ep : gmap node_id nat) (nodes : gmap node_id node) : gmap node_id max_nat :=
  map_imap (fun n d => Some (MaxNat (fresh_code (epoch ep n) (n_freshness d)))) nodes.

(**md
The freshness of `d'` is below that of `d`: a freshness fall.
*)
Definition fell (d d' : node) : bool :=
  bool_decide (freshness_rank (n_freshness d') < freshness_rank (n_freshness d)).

(**md
The epoch protocol, from the nodes `m` at the epochs `ep` to the nodes `m'` at the epochs `ep'`: each node of
both maps advances its epoch by one when its freshness falls, and keeps it otherwise.

- [m:] the nodes before the step.
- [m':] the nodes after it.
- [ep:] the epochs before the step.
- [ep':] the epochs after it.
*)
Definition epoch_rule (m m' : gmap node_id node) (ep ep' : gmap node_id nat) : Prop :=
  forall n d d', m !! n = Some d -> m' !! n = Some d' ->
    epoch ep' n = if fell d d' then S (epoch ep n) else epoch ep n.

(**md
**A freshness code orders epoch first, then freshness.**

- [e:] the epoch of the first code.
- [f:] the freshness of the first code.
- [e':] the epoch of the second code.
- [f':] the freshness of the second code.
*)
Lemma fresh_code_le e f e' f' :
  fresh_code e f <= fresh_code e' f' <-> e < e' \/ (e = e' /\ freshness_le f f').
Proof.
  rewrite /fresh_code /freshness_le.
  assert (freshness_rank f < 3 /\ freshness_rank f' < 3) by (destruct f, f'; simpl; lia). lia.
Qed.

(**md
**A step that keeps every node's freshness at or above its own keeps the epochs.**

- [m:] the nodes before the step.
- [m':] the nodes after it.
- [ep:] the epochs.
*)
Lemma epoch_rule_rise m m' ep :
  (forall n d d', m !! n = Some d -> m' !! n = Some d' -> freshness_le (n_freshness d) (n_freshness d')) ->
  epoch_rule m m' ep ep.
Proof.
  intros Hle n d d' Hd Hd'. specialize (Hle n d d' Hd Hd'). rewrite /fell bool_decide_eq_false_2 //.
  unfold freshness_le in Hle. lia.
Qed.

(**md
**Under the epoch protocol every freshness code grows.**

- [m:] the nodes before the step.
- [m':] the nodes after it, a superset of `m`'s.
- [ep:] the epochs before the step.
- [ep':] the epochs after it.
*)
Lemma fresh_map_grows m m' ep ep' :
  epoch_rule m m' ep ep' -> dom m ⊆ dom m' ->
  forall n k, fresh_map ep m !! n = Some (MaxNat k) ->
    exists k', fresh_map ep' m' !! n = Some (MaxNat k') /\ k <= k'.
Proof.
  intros Hr Hdom n k Hk. rewrite /fresh_map map_lookup_imap in Hk.
  destruct (m !! n) as [d|] eqn:Hd; simpl in Hk; [|done]. injection Hk as <-.
  assert (n ∈ dom m') as [d' Hd']%elem_of_dom by (apply Hdom; by apply elem_of_dom_2 in Hd).
  rewrite /fresh_map map_lookup_imap Hd' /=. eexists; split; [reflexivity|].
  rewrite (Hr n d d' Hd Hd') /fresh_code /fell.
  assert (freshness_rank (n_freshness d) < 3) by (destruct (n_freshness d); simpl; lia).
  case_bool_decide; lia.
Qed.

Section ghost.
Context `{!graphG Σ}.

Definition graph_edges_auth (γ : gname) (m : gmap node_id edge_entry) : iProp Σ :=
  own γ ((gmap_view_auth (V := agreeR (leibnizO edge_entry)) (DfracOwn 1) (to_agree <$> m), ε) : graphUR).

(**md
The exclusive fragment of node `n`'s entry `e` in the edge map of `γ`.
*)
Definition graph_edge (γ : gname) (n : node_id) (e : edge_entry) : iProp Σ :=
  own γ ((gmap_view_frag (V := agreeR (leibnizO edge_entry)) n (DfracOwn 1) (to_agree e), ε) : graphUR).

Definition graph_fresh_auth (γ : gname) (m : gmap node_id max_nat) : iProp Σ :=
  own γ ((ε, (● m, ε)) : graphUR) ∗ own γ ((ε, (◯ m, ε)) : graphUR).

(**md
Node `n`'s freshness code in `γ` is at least `k`.
*)
Definition graph_fresh_lb (γ : gname) (n : node_id) (k : nat) : iProp Σ :=
  own γ ((ε, (◯ {[n := MaxNat k]}, ε)) : graphUR).

Definition graph_state_auth (γ : gname) (s : State.state) : iProp Σ :=
  own γ ((ε, (ε, (●E (s : leibnizO State.state), ε))) : graphUR).

(**md
The client's half of the agreement: the pure state is `s`.
*)
Definition graph_state (γ : gname) (s : State.state) : iProp Σ :=
  own γ ((ε, (ε, (◯E (s : leibnizO State.state), ε))) : graphUR).

Definition graph_locs_auth (γ : gname) (tbl : gmap node_id (loc * loc)) : iProp Σ :=
  own γ ((ε, (ε, (ε, ● locs_ra tbl))) : graphUR) ∗ own γ ((ε, (ε, (ε, ◯ locs_ra tbl))) : graphUR).

(**md
Node `n`'s observer set and source list are at the locations `p`, at every state of `γ`.
*)
Definition graph_loc (γ : gname) (n : node_id) (p : loc * loc) : iProp Σ :=
  own γ ((ε, (ε, (ε, ◯ ({[n := to_agree p]} : gmap node_id (agreeR (leibnizO (loc * loc))))))) : graphUR).

Global Instance graph_fresh_lb_persistent γ n k : Persistent (graph_fresh_lb γ n k).
Proof. apply _. Qed.

Global Instance graph_loc_persistent γ n p : Persistent (graph_loc γ n p).
Proof. apply _. Qed.

Lemma locs_ra_valid tbl : ✓ locs_ra tbl.
Proof. intros i. rewrite /locs_ra lookup_fmap. by destruct (tbl !! i). Qed.

Lemma graph_ghost_alloc (s : State.state) (m : gmap node_id max_nat) (tbl : gmap node_id (loc * loc)) :
  ⊢ |==> ∃ γ, graph_edges_auth γ ∅ ∗ graph_fresh_auth γ m ∗ graph_state_auth γ s ∗ graph_state γ s ∗
    graph_locs_auth γ tbl.
Proof.
  iMod (own_alloc (((gmap_view_auth (V := agreeR (leibnizO edge_entry)) (DfracOwn 1)
                      (to_agree <$> (∅ : gmap node_id edge_entry)), ε) : graphUR) ⋅
                   (((ε, (● m, ε)) : graphUR) ⋅ (((ε, (◯ m, ε)) : graphUR) ⋅
                    (((ε, (ε, (●E (s : leibnizO State.state), ε))) : graphUR) ⋅
                     (((ε, (ε, (◯E (s : leibnizO State.state), ε))) : graphUR) ⋅
                      (((ε, (ε, (ε, ● locs_ra tbl))) : graphUR) ⋅ ((ε, (ε, (ε, ◯ locs_ra tbl))) : graphUR))))))))
    as (γ) "H".
  { rewrite -!pair_op. split; simpl; [rewrite !right_id; apply gmap_view_auth_valid|]. split; simpl.
    - rewrite !left_id !right_id. apply auth_both_valid_discrete. split; [done|].
      intros i. destruct (m !! i : option max_natR) eqn:E; rewrite E; done.
    - split; simpl; [rewrite !left_id !right_id; apply excl_auth_valid|].
      rewrite !left_id. apply auth_both_valid_discrete. split; [done|]. apply locs_ra_valid. }
  rewrite !own_op. iDestruct "H" as "(H1 & H2 & H3 & H4 & H5 & H6 & H7)". iExists γ. iFrame. done.
Qed.

Lemma graph_edge_alloc γ m n e :
  m !! n = None -> graph_edges_auth γ m ==∗ graph_edges_auth γ (<[n := e]> m) ∗ graph_edge γ n e.
Proof.
  intros Hn. iIntros "H". rewrite /graph_edges_auth /graph_edge.
  iMod (own_update _ _ (((gmap_view_auth (V := agreeR (leibnizO edge_entry)) (DfracOwn 1)
                           (to_agree <$> <[n := e]> m), ε) : graphUR) ⋅
                        ((gmap_view_frag (V := agreeR (leibnizO edge_entry)) n (DfracOwn 1) (to_agree e), ε) : graphUR))
          with "H") as "[$ $]"; [|done].
  rewrite -pair_op fmap_insert. apply prod_update; simpl.
  - apply gmap_view_alloc; [by rewrite lookup_fmap Hn|done|done].
  - by rewrite left_id.
Qed.

Lemma graph_edges_alloc_all γ (m : gmap node_id edge_entry) :
  graph_edges_auth γ ∅ ==∗ graph_edges_auth γ m ∗ [∗ map] n ↦ e ∈ m, graph_edge γ n e.
Proof.
  induction m as [|n e m Hn IH] using map_ind; iIntros "H".
  - iModIntro. iFrame. by rewrite big_sepM_empty.
  - iMod (IH with "H") as "[H Hm]". iMod (graph_edge_alloc _ _ n e Hn with "H") as "[$ He]".
    iModIntro. rewrite big_sepM_insert //. iFrame.
Qed.

Lemma graph_edge_agree γ m n e : graph_edges_auth γ m -∗ graph_edge γ n e -∗ ⌜m !! n = Some e⌝.
Proof.
  iIntros "Ha Hf". iDestruct (own_valid_2 with "Ha Hf") as %[Hv _].
  simpl in Hv. apply gmap_view_both_valid in Hv as (_ & _ & Hl).
  rewrite lookup_fmap in Hl. apply fmap_Some_equiv in Hl as (e' & Hm & He).
  apply (inj to_agree), leibniz_equiv in He. iPureIntro. subst e'. exact Hm.
Qed.

Lemma graph_edge_update γ m n e e' :
  graph_edges_auth γ m -∗ graph_edge γ n e ==∗ graph_edges_auth γ (<[n := e']> m) ∗ graph_edge γ n e'.
Proof.
  iIntros "Ha Hf". iCombine "Ha Hf" as "H". rewrite /graph_edges_auth /graph_edge.
  iMod (own_update _ _ (((gmap_view_auth (V := agreeR (leibnizO edge_entry)) (DfracOwn 1)
                           (to_agree <$> <[n := e']> m), ε) : graphUR) ⋅
                        ((gmap_view_frag (V := agreeR (leibnizO edge_entry)) n (DfracOwn 1) (to_agree e'), ε) : graphUR))
          with "H") as "[$ $]"; [|done].
  rewrite -!pair_op fmap_insert. apply prod_update; simpl; [|done].
  by apply gmap_view_replace.
Qed.

(** The fragments of the nodes [X] move from the entries [f] to the entries [f']. *)
Lemma graph_edges_set γ m (X : gset node_id) (f f' : node_id -> edge_entry) :
  graph_edges_auth γ m -∗ ([∗ set] n ∈ X, graph_edge γ n (f n)) ==∗
  ∃ m', ⌜forall n, n ∈ X -> is_Some (m !! n)⌝ ∗
        ⌜forall n, m' !! n = if decide (n ∈ X) then Some (f' n) else m !! n⌝ ∗
        graph_edges_auth γ m' ∗ [∗ set] n ∈ X, graph_edge γ n (f' n).
Proof.
  induction X as [|x Y Hx IH] using set_ind_L; iIntros "Ha Hf".
  - iModIntro. iExists m. iFrame. iSplit; [iPureIntro; set_solver|].
    iSplit; [iPureIntro; intros n; case_decide; [set_solver|done]|]. by iApply big_sepS_empty.
  - rewrite !big_sepS_insert //. iDestruct "Hf" as "[Hx Hf]".
    iMod (IH with "Ha Hf") as (m1) "(%Hdom & %Hm1 & Ha & Hf)".
    iDestruct (graph_edge_agree with "Ha Hx") as %Hl.
    iMod (graph_edge_update _ _ _ _ (f' x) with "Ha Hx") as "[Ha Hx]".
    iModIntro. iExists _. iFrame "Ha". rewrite big_sepS_insert //. iFrame. iPureIntro. split.
    + intros n [->%elem_of_singleton|Hn]%elem_of_union; [|by apply Hdom].
      revert Hl. rewrite Hm1. case_decide; [done|]. intros ->. done.
    + intros n. destruct (decide (n = x)) as [->|Hne].
      * rewrite lookup_insert_eq. case_decide; [done|set_solver].
      * rewrite lookup_insert_ne // Hm1. repeat case_decide; set_solver.
Qed.

Lemma graph_fresh_lb_get γ m n k : m !! n = Some (MaxNat k) -> graph_fresh_auth γ m -∗ graph_fresh_lb γ n k.
Proof.
  iIntros (Hn) "[_ Hf]". iApply (own_mono with "Hf"). apply prod_included. split; [done|].
  apply prod_included. split; [|done]. simpl. apply auth_frag_mono.
  apply singleton_included_l. exists (MaxNat k). rewrite Hn. split; done.
Qed.

Lemma graph_fresh_lb_valid γ m n k :
  graph_fresh_auth γ m -∗ graph_fresh_lb γ n k -∗ ⌜exists k', m !! n = Some (MaxNat k') /\ k <= k'⌝.
Proof.
  iIntros "[Ha _] Hf". iDestruct (own_valid_2 with "Ha Hf") as %[_ [Hv _]]. simpl in Hv.
  apply auth_both_valid_discrete in Hv as [Hin _].
  apply singleton_included_l in Hin as ([k'] & Hl & Hle). apply leibniz_equiv in Hl.
  apply Some_included_total, max_nat_included in Hle. simpl in Hle. iPureIntro. eauto.
Qed.

(** The codes [m] grow to [m']. *)
Local Lemma graph_fresh_grow γ m m' :
  (forall n k, m !! n = Some (MaxNat k) -> exists k', m' !! n = Some (MaxNat k') /\ k <= k') ->
  graph_fresh_auth γ m ==∗ graph_fresh_auth γ m'.
Proof.
  intros Hle. iIntros "[Ha Hf]". iCombine "Ha Hf" as "H".
  iMod (own_update _ _ (((ε, (● m', ε)) : graphUR) ⋅ ((ε, (◯ m', ε)) : graphUR)) with "H") as "[$ $]"; [|done].
  rewrite -!pair_op. apply prod_update; simpl; [done|]. apply prod_update; simpl; [|done].
  assert (Hop : m' ⋅ m = m').
  { apply map_eq. intros i. rewrite lookup_op.
    destruct (m !! i : option max_natR) as [[k]|] eqn:E; rewrite E.
    - destruct (Hle i k E) as (k' & Hk' & Hk).
      rewrite (Hk' : (m' !! i : option max_natR) = Some (MaxNat k')).
      rewrite -Some_op max_nat_op. do 2 f_equal. lia.
    - by rewrite right_id. }
  apply auth_update. rewrite -Hop. apply op_local_update_discrete. intros _. rewrite Hop.
  intros i. destruct (m' !! i : option max_natR) eqn:E; rewrite E; done.
Qed.

Lemma graph_state_agree γ s s' : graph_state_auth γ s -∗ graph_state γ s' -∗ ⌜s = s'⌝.
Proof.
  iIntros "Ha Hf". iDestruct (own_valid_2 with "Ha Hf") as %[_ [_ [Hv _]]]. simpl in Hv.
  by apply excl_auth_agree_L in Hv.
Qed.

Lemma graph_state_update γ s s' :
  graph_state_auth γ s -∗ graph_state γ s ==∗ graph_state_auth γ s' ∗ graph_state γ s'.
Proof.
  iIntros "Ha Hf". iCombine "Ha Hf" as "H".
  iMod (own_update _ _ (((ε, (ε, (●E (s' : leibnizO State.state), ε))) : graphUR) ⋅
                        ((ε, (ε, (◯E (s' : leibnizO State.state), ε))) : graphUR)) with "H") as "[$ $]"; [|done].
  rewrite -!pair_op. apply prod_update; simpl; [done|]. apply prod_update; simpl; [done|].
  apply prod_update; simpl; [|done]. apply excl_auth_update.
Qed.

Lemma graph_loc_get γ tbl n p : tbl !! n = Some p -> graph_locs_auth γ tbl -∗ graph_loc γ n p.
Proof.
  iIntros (Hn) "[_ Hf]". iApply (own_mono with "Hf"). apply prod_included. split; [done|].
  apply prod_included. split; [done|]. apply prod_included. split; [done|]. apply auth_frag_mono.
  apply (proj2 (singleton_included_l (locs_ra tbl) n (to_agree p))).
  exists (to_agree p). rewrite /locs_ra lookup_fmap Hn. split; done.
Qed.

(**md
**A location of the monotone map is the node's location in the table.**

- [γ:] the ghost name.
- [tbl:] the table of the graph's current state.
- [n:] the node.
- [p:] the locations taken from the map.
*)
Lemma graph_loc_agree γ tbl n p : graph_locs_auth γ tbl -∗ graph_loc γ n p -∗ ⌜tbl !! n = Some p⌝.
Proof.
  iIntros "[Ha _] Hf". iDestruct (own_valid_2 with "Ha Hf") as %[_ [_ [_ Hv]]]. simpl in Hv.
  apply auth_both_valid_discrete in Hv as [Hin _].
  apply (singleton_included_l (locs_ra tbl) n (to_agree p)) in Hin as (y & Hl & Hle). rewrite /locs_ra lookup_fmap in Hl.
  apply fmap_Some_equiv in Hl as (p' & Hm & Hy). rewrite Hy in Hle. iPureIntro.
  apply Some_included in Hle as [Hle|Hle].
  - apply (inj to_agree), leibniz_equiv in Hle. subst p'. exact Hm.
  - apply (to_agree_included (A := leibnizO (loc * loc))), leibniz_equiv in Hle. subst p'. exact Hm.
Qed.

Lemma graph_locs_insert γ tbl n p :
  tbl !! n = None -> graph_locs_auth γ tbl ==∗ graph_locs_auth γ (<[n := p]> tbl).
Proof.
  intros Hn. iIntros "[Ha Hf]". iCombine "Ha Hf" as "H".
  iMod (own_update _ _ (((ε, (ε, (ε, ● locs_ra (<[n := p]> tbl)))) : graphUR) ⋅ ((ε, (ε, (ε, ◯ locs_ra (<[n := p]> tbl)))) : graphUR))
          with "H") as "[$ $]"; [|done].
  rewrite -!pair_op. apply prod_update; simpl; [done|]. apply prod_update; simpl; [done|].
  apply prod_update; simpl; [done|]. apply auth_update.
  assert (Hins : locs_ra (<[n := p]> tbl) = <[n := to_agree (p : leibnizO (loc * loc))]> (locs_ra tbl)).
  { apply map_eq. intros i. rewrite /locs_ra lookup_fmap. destruct (decide (i = n)) as [->|Hne].
    - rewrite !lookup_insert_eq. done.
    - rewrite !lookup_insert_ne // lookup_fmap //. }
  rewrite Hins. apply alloc_local_update; [|done]. rewrite /locs_ra lookup_fmap Hn //.
Qed.

End ghost.

(** ** Resolvers over a growing table *)

(**md
The resolver `res` extended by node `n` at the locations `p`.
*)
Definition resolver_extend : val := λ: "res" "n" "p", λ: "m", if: "m" = "n" then "p" else "res" "m".

Section graph.
Context `{!heapGS Σ, !graphG Σ}.

(**md
**Extending a resolver resolves the new node and every node it resolved.**

- [tbl:] the table resolved by `res`.
- [res:] the resolver.
- [n:] the new node.
- [lo:] the location of the new node's observer set.
- [ls:] the location of the new node's source list.
*)
Lemma resolver_extend_spec tbl (res : val) (n : node_id) (lo ls : loc) :
  {{{ resolver tbl res }}} resolver_extend res #(Z.of_nat n) (#lo, #ls)%V
  {{{ r, RET r; resolver (<[n := (lo, ls)]> tbl) r }}}.
Proof.
  iIntros (Φ) "#Hres HΦ". wp_lam. wp_pures. iModIntro. iApply "HΦ". iModIntro.
  iIntros (m lo' ls' Hm). wp_pures. case_bool_decide as Heq.
  - assert (m = n) as -> by (injection Heq; lia). rewrite lookup_insert_eq in Hm. simplify_eq. by wp_pures.
  - rewrite lookup_insert_ne in Hm; [|intros ->; done]. wp_pures. by iApply "Hres".
Qed.

(**md
The heap tier covers the present engine: the fields of the saturated-write skip hold their initial values.
*)
Definition skip_ok (s : State.state) : Prop := g_skip s = SkipOff /\ g_epoch s = 0 /\ g_stamps s = ∅.

(**md
The graph `g` holds the pure state `s` at the epochs `ep`. `g` is a location holding the resolver of a node
table, whose domain is the nodes of `s`; the heap holds the edge world of `s`, which is coherent; the edge map
of `γ` holds each node's entry, the freshness of `γ` holds each node's code at its epoch, `γ` agrees on `s`, and
the location map of `γ` is the table.

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the pure state.
- [ep:] the epoch of each node.
*)
Definition is_graph_at (γ : gname) (g : val) (s : State.state) (ep : gmap node_id nat) : iProp Σ :=
  ∃ (gl : loc) (res : val) (tbl : gmap node_id (loc * loc)),
    ⌜g = #gl⌝ ∗ ⌜skip_ok s⌝ ∗ ⌜edge_match (g_edges s)⌝ ∗ ⌜dom tbl = dom (g_nodes s)⌝ ∗
    gl ↦ res ∗ resolver tbl res ∗ is_world tbl (g_edges s) ∗
    graph_edges_auth γ (edge_map tbl (g_edges s)) ∗ graph_fresh_auth γ (fresh_map ep (g_nodes s)) ∗
    graph_state_auth γ s ∗ graph_locs_auth γ tbl.

(**md
`is_graph_at` at some epochs.

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the pure state.
*)
Definition is_graph (γ : gname) (g : val) (s : State.state) : iProp Σ := ∃ ep, is_graph_at γ g s ep.

(**md
**A graph holding the pure state agrees with the client's state.**

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state the graph holds.
- [s':] the client's state.
*)
Lemma is_graph_agree γ g s s' : is_graph γ g s -∗ graph_state γ s' -∗ ⌜s = s'⌝.
Proof.
  iIntros "(%ep & %gl & %res & %tbl & _ & _ & _ & _ & _ & _ & _ & _ & _ & Ha & _) Hf".
  iApply (graph_state_agree with "Ha Hf").
Qed.

(**md
**A graph's edge world is coherent.**

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [ep:] the epochs.
*)
Lemma is_graph_edge_match γ g s ep : is_graph_at γ g s ep -∗ ⌜edge_match (g_edges s)⌝.
Proof. iIntros "(%gl & %res & %tbl & _ & _ & $ & _)". Qed.

(**md
**A node's edge fragment is its observer set and source list in the graph's world.**

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [n:] the node.
- [e:] the fragment's entry.
*)
Lemma is_graph_edge γ g s n e :
  is_graph γ g s -∗ graph_edge γ n e -∗ ⌜e = edge_entry_of (g_edges s) n⌝.
Proof.
  iIntros "(%ep & %gl & %res & %tbl & _ & _ & _ & _ & _ & _ & _ & Ha & _) Hf".
  iDestruct (graph_edge_agree with "Ha Hf") as %Hl.
  rewrite /edge_map map_lookup_imap in Hl. destruct (tbl !! n); simpl in Hl; by simplify_eq.
Qed.

(**md
**Every node of a graph has its locations in the monotone map.**

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [ep:] the epochs.
- [n:] a node of `s`.
*)
Lemma is_graph_loc γ g s ep n :
  n ∈ dom (g_nodes s) -> is_graph_at γ g s ep -∗ ∃ p, graph_loc γ n p.
Proof.
  iIntros (Hn) "(%gl & %res & %tbl & _ & _ & _ & %Hdom & _ & _ & _ & _ & _ & _ & Hl)".
  rewrite -Hdom in Hn. apply elem_of_dom in Hn as [p Hp]. iExists p. by iApply (graph_loc_get with "Hl").
Qed.

(**md
**A freshness lower bound taken from a graph holds at its later states.**

The bound is the node's code at its epoch, and every later code is at least it: the freshness has risen, or the
epoch has advanced.

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [ep:] the epochs.
- [n:] the node.
- [d:] the node's record.
*)
Lemma is_graph_fresh_lb γ g s ep n d :
  g_nodes s !! n = Some d ->
  is_graph_at γ g s ep -∗ graph_fresh_lb γ n (fresh_code (epoch ep n) (n_freshness d)).
Proof.
  iIntros (Hn) "(%gl & %res & %tbl & _ & _ & _ & _ & _ & _ & _ & _ & Hf & _)".
  iApply (graph_fresh_lb_get with "Hf"). rewrite /fresh_map map_lookup_imap Hn //.
Qed.

(**md
**A freshness lower bound is below the node's current code.**

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [ep:] the epochs.
- [n:] the node.
- [d:] the node's record.
- [k:] the lower bound.
*)
Lemma is_graph_fresh_valid γ g s ep n d k :
  g_nodes s !! n = Some d ->
  is_graph_at γ g s ep -∗ graph_fresh_lb γ n k -∗ ⌜k <= fresh_code (epoch ep n) (n_freshness d)⌝.
Proof.
  iIntros (Hn) "(%gl & %res & %tbl & _ & _ & _ & _ & _ & _ & _ & _ & Hf & _) Hlb".
  iDestruct (graph_fresh_lb_valid with "Hf Hlb") as %(k' & Hl & Hle).
  rewrite /fresh_map map_lookup_imap Hn /= in Hl. by simplify_eq.
Qed.

(**md
**At an unchanged epoch, a freshness lower bound bounds the freshness.**

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [ep:] the epochs.
- [n:] the node.
- [d:] the node's record.
- [f:] the freshness of the bound.
*)
Lemma is_graph_fresh_epoch γ g s ep n d f :
  g_nodes s !! n = Some d ->
  is_graph_at γ g s ep -∗ graph_fresh_lb γ n (fresh_code (epoch ep n) f) -∗ ⌜freshness_le f (n_freshness d)⌝.
Proof.
  iIntros (Hn) "Hg Hlb". iDestruct (is_graph_fresh_valid with "Hg Hlb") as %Hle; [done|].
  iPureIntro. apply fresh_code_le in Hle as [Hlt|[_ Hle]]; [lia|done].
Qed.

(**md
**A graph opens to its heap, and closes at a later state under the epoch protocol.**

The opened graph gives its resolver, its table and the edge world in the heap. It closes at a state `s'` with the
same nodes, whose edge world in the heap is coherent, at epochs `ep'` that follow the epoch protocol. The
fragments of the nodes `X` move to their entries at `s'`, and every other node keeps its entry.

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state.
- [ep:] the epochs.
*)
Lemma is_graph_open γ g s ep :
  is_graph_at γ g s ep -∗
  ∃ (gl : loc) (res : val) (tbl : gmap node_id (loc * loc)),
    ⌜g = #gl⌝ ∗ ⌜edge_match (g_edges s)⌝ ∗ ⌜dom tbl = dom (g_nodes s)⌝ ∗
    gl ↦ res ∗ resolver tbl res ∗ is_world tbl (g_edges s) ∗ graph_locs_auth γ tbl ∗
    ∀ (s' : State.state) (ep' : gmap node_id nat) (X : gset node_id),
      ⌜skip_ok s'⌝ -∗ ⌜edge_match (g_edges s')⌝ -∗ ⌜dom (g_nodes s') = dom (g_nodes s)⌝ -∗
      ⌜epoch_rule (g_nodes s) (g_nodes s') ep ep'⌝ -∗
      ⌜forall n, n ∉ X -> edge_entry_of (g_edges s') n = edge_entry_of (g_edges s) n⌝ -∗
      gl ↦ res -∗ is_world tbl (g_edges s') -∗ graph_locs_auth γ tbl -∗ graph_state γ s -∗
      ([∗ set] n ∈ X, graph_edge γ n (edge_entry_of (g_edges s) n)) ==∗
      is_graph_at γ g s' ep' ∗ graph_state γ s' ∗ [∗ set] n ∈ X, graph_edge γ n (edge_entry_of (g_edges s') n).
Proof.
  iIntros "(%gl & %res & %tbl & -> & %Hskip & %Hm & %Hdom & Hgl & #Hres & Hw & He & Hf & Ha & Hl)".
  iExists gl, res, tbl. iFrame "Hgl Hres Hw Hl". iSplit; [done|]. iSplit; [done|]. iSplit; [done|].
  iIntros (s' ep' X Hskip' Hm' Hdom' Hrule HX) "Hgl Hw Hl Hs HXf".
  iMod (graph_edges_set _ _ X (edge_entry_of (g_edges s)) (edge_entry_of (g_edges s')) with "He HXf")
    as (m') "(%HXd & %Hm'l & He & HXf)".
  iMod (graph_fresh_grow _ _ (fresh_map ep' (g_nodes s')) with "Hf") as "Hf".
  { apply fresh_map_grows; [done|]. by rewrite Hdom'. }
  iMod (graph_state_update _ _ s' with "Ha Hs") as "[Ha Hs]".
  assert (Hm'e : m' = edge_map tbl (g_edges s')).
  { apply map_eq. intros n. rewrite Hm'l /edge_map !map_lookup_imap. case_decide as Hn.
    - destruct (HXd n Hn) as [e He]. rewrite map_lookup_imap in He.
      destruct (tbl !! n); simpl in *; done.
    - rewrite (HX n Hn). done. }
  rewrite Hm'e. iModIntro. iFrame. iFrame "#". iPureIntro.
  split_and!; [done|done|done|by rewrite Hdom'].
Qed.

(**md
**A heap holding an edge world starts a graph.**

The client receives the state's agreement and the edge fragment of every node of the table.

- [gl:] the location holding the resolver.
- [res:] the resolver of the table.
- [tbl:] the node table, whose domain is the nodes of `s`.
- [s:] the pure state, whose skip fields hold their initial values and whose edge world is coherent.
*)
Lemma is_graph_alloc (gl : loc) (res : val) tbl (s : State.state) :
  skip_ok s -> edge_match (g_edges s) -> dom tbl = dom (g_nodes s) ->
  gl ↦ res ∗ resolver tbl res ∗ is_world tbl (g_edges s) ⊢ |==> ∃ γ, is_graph γ #gl s ∗ graph_state γ s ∗
    [∗ map] n ↦ e ∈ edge_map tbl (g_edges s), graph_edge γ n e.
Proof.
  iIntros (Hskip Hm Hdom) "(Hgl & #Hres & Hw)".
  iMod (graph_ghost_alloc s (fresh_map ∅ (g_nodes s)) tbl) as (γ) "(He & Hf & Ha & Hs & Hl)".
  iMod (graph_edges_alloc_all _ (edge_map tbl (g_edges s)) with "He") as "[He Hfr]".
  iModIntro. iExists γ. iSplitR "Hs Hfr"; [|iFrame]. iExists ∅, gl, res, tbl. iFrame "#". iFrame. done.
Qed.

(** ** Node allocation *)

(** [graph_add_node_code g n lo ls] makes the observer set at [lo] and the source list at [ls] node [n]'s. *)
Definition graph_add_node_code : val :=
  λ: "g" "n" "lo" "ls", "g" <- resolver_extend (! "g") "n" ("lo", "ls").

Lemma fresh_map_insert ep m n d :
  m !! n = None -> fresh_map ep (<[n := d]> m) = <[n := MaxNat (fresh_code (epoch ep n) (n_freshness d))]> (fresh_map ep m).
Proof.
  intros Hn. apply map_eq. intros i. rewrite /fresh_map map_lookup_imap.
  destruct (decide (i = n)) as [->|Hne].
  - by rewrite !lookup_insert_eq.
  - rewrite !lookup_insert_ne // map_lookup_imap //.
Qed.

Lemma edge_map_insert tbl w n p :
  edge_map (<[n := p]> tbl) w = <[n := edge_entry_of w n]> (edge_map tbl w).
Proof.
  apply map_eq. intros i. rewrite /edge_map map_lookup_imap.
  destruct (decide (i = n)) as [->|Hne].
  - by rewrite !lookup_insert_eq.
  - rewrite !lookup_insert_ne // map_lookup_imap //.
Qed.

(**md
**A new node joins the graph with an empty observer set and an empty source list.**

The table, the resolver, the heap's edge world, the edge map, the freshness map and the location map gain the
node. The node's epoch is its epoch in `ep`. The client receives the node's edge fragment and its locations.

- [γ:] the ghost name.
- [g:] the graph value.
- [s:] the state before the allocation.
- [s':] the state after it: `s` with node `n` holding `d`, the same edge world and skip fields at their
  initial values.
- [ep:] the epochs.
- [n:] the new node.
- [d:] its record.
- [lo:] the location of its observer set.
- [ls:] the location of its source list.
*)
Lemma graph_add_node_spec γ g s s' ep (n : node_id) d (lo ls : loc) :
  g_nodes s !! n = None -> g_nodes s' = <[n := d]> (g_nodes s) -> g_edges s' = g_edges s -> skip_ok s' ->
  {{{ is_graph_at γ g s ep ∗ graph_state γ s ∗ is_observers lo empty_observer_set ∗
      is_source_list ls empty_source_list ∅ }}}
    graph_add_node_code g #(Z.of_nat n) #lo #ls
  {{{ RET #(); is_graph_at γ g s' ep ∗ graph_state γ s' ∗
      graph_edge γ n (empty_observer_set, empty_source_list) ∗ graph_loc γ n (lo, ls) }}}.
Proof.
  intros Hn Hnodes Hedges Hskip'. iIntros (Φ) "(Hg & Hs & Ho & Hsl) HΦ".
  iDestruct "Hg" as (gl res tbl) "(-> & %Hskip & %Hm & %Hdom & Hgl & #Hres & Hw & He & Hf & Ha & Hl)".
  assert (Ht : tbl !! n = None) by (apply not_elem_of_dom; rewrite Hdom; by apply not_elem_of_dom).
  iDestruct "Hw" as (Hin) "[Hobs Hsrc]".
  assert (Hw : world_observers (g_edges s) !! n = None /\ world_sources (g_edges s) !! n = None).
  { destruct Hin as [Hio His]. split; apply not_elem_of_dom; intros Hd; apply (not_elem_of_dom tbl n); auto. }
  assert (He0 : edge_entry_of (g_edges s) n = (empty_observer_set, empty_source_list)).
  { rewrite /edge_entry_of /observers_of /sources_of. destruct Hw as [-> ->]. done. }
  wp_lam. wp_pures. wp_load. wp_apply (resolver_extend_spec with "Hres"). iIntros (r) "#Hr". wp_store.
  iMod (graph_edge_alloc _ _ n (edge_entry_of (g_edges s) n) with "He") as "[He Hen]".
  { rewrite /edge_map map_lookup_imap Ht //. }
  iMod (graph_fresh_grow _ _ (fresh_map ep (g_nodes s')) with "Hf") as "Hf".
  { intros i k Hk. rewrite Hnodes fresh_map_insert //. exists k. split; [|done].
    rewrite lookup_insert_ne //. intros ->. rewrite /fresh_map map_lookup_imap Hn in Hk. done. }
  iMod (graph_state_update _ _ s' with "Ha Hs") as "[Ha Hs]".
  iMod (graph_locs_insert _ _ n (lo, ls) with "Hl") as "Hl"; [done|].
  iDestruct (graph_loc_get _ _ n (lo, ls) with "Hl") as "#Hloc"; [by rewrite lookup_insert_eq|].
  iModIntro. iApply "HΦ". rewrite He0. iFrame "Hen Hloc Hs". iExists gl, r, (<[n := (lo, ls)]> tbl).
  rewrite Hedges edge_map_insert He0. iFrame "#". iFrame. iSplit; [done|]. iSplit; [done|]. iSplit; [done|].
  iSplit; [iPureIntro; rewrite Hnodes !dom_insert_L Hdom //|].
  iSplit.
  - iPureIntro. destruct Hin as [Hio His]. rewrite /world_in dom_insert_L. split; set_solver.
  - rewrite /obs_part /src_part !big_sepM_insert //. iSplitL "Ho Hobs".
    + rewrite /observers_of. destruct Hw as [-> _]. simpl. iFrame.
    + rewrite /sources_of. destruct Hw as [_ ->]. simpl. iFrame "Hsrc". iExists ∅. iFrame.
Qed.

End graph.
