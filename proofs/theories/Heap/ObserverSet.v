From stdpp Require Import prelude.
From Ranvier.Base Require Import Prelude.
From Ranvier.Pure Require Import ObserverSet.
From iris.heap_lang Require Import proofmode notation.
From iris.heap_lang.lib Require Import array assert.

(** * The observer set in HeapLang

An observer set is a block of four fields: the array (field 0), [count] (field 1), [latest] (field 2) and
the array's capacity (field 3, [items.Length] in F#). A cell is the pair of the observer's id and its slot,
the .NET [ObserverSlot] struct. Cells past [count] are blank. *)

(** The cell of a blanked position: no observer and [EdgeHandle.Unlinked]. *)
Definition blank_cell : val := (#(-1), #(-1)).

Definition observer_cell (p : node_id * nat) : val := (#(Z.of_nat p.1), #(Z.of_nat p.2)).

(**md
The array contents for the observers `xs` in an array of capacity `cap`: a cell per observer, then blank cells.
*)
Definition item_cells (xs : list (node_id * nat)) (cap : nat) : list val :=
  (observer_cell <$> xs) ++ replicate (cap - length xs) blank_cell.

(** [TryAdd]'s answer: the handle, [EdgeHandle.Unlinked] or [EdgeHandle.Unknown]. *)
Definition tried_val (t : tried) : val :=
  match t with TAdded h => #(Z.of_nat h) | TUnlinked => #(-1) | TUnknown => #(-2) end.

(** The [MovedIn] notice of a removal: the moved observer, its slot and its new handle. *)
Definition moved_val (m : option (node_id * nat * nat)) : val :=
  match m with
  | None => NONEV
  | Some (c, s, h) => SOMEV (#(Z.of_nat c), #(Z.of_nat s), #(Z.of_nat h))
  end.

(** [new ObserverSet ()]: an array of capacity 0, with the record's own location in the array field. *)
Definition observers_new_code : val :=
  λ: <>, let: "l" := AllocN #4 #0 in "l" <- "l";; "l".

(** [grow]: a first observer gets capacity 1, and later ones double the count, to at least 4. *)
Definition observers_grow_code : val :=
  λ: "l",
    let: "n" := ! ("l" +ₗ #1) in
    let: "size" := if: "n" = #0 then #1 else if: #4 < "n" * #2 then "n" * #2 else #4 in
    let: "b" := AllocN "size" blank_cell in
    array_copy_to "b" (! "l") "n";;
    "l" <- "b";;
    ("l" +ₗ #3) <- "size".

(** [Add]. *)
Definition observers_add_code : val :=
  λ: "l" "c" "slot",
    (if: ! ("l" +ₗ #1) = ! ("l" +ₗ #3) then observers_grow_code "l" else #());;
    let: "h" := ! ("l" +ₗ #1) in
    (! "l" +ₗ "h") <- ("c", "slot");;
    ("l" +ₗ #1) <- "h" + #1;;
    ("l" +ₗ #2) <- "h";;
    "h".

(** [TryAdd]'s scan: whether a position from [i] up to [n] holds [c]. *)
Definition observers_scan_code : val :=
  rec: "scan" "a" "i" "n" "c" :=
    if: "i" < "n" then
      if: Fst (! ("a" +ₗ "i")) = "c" then #true else "scan" "a" ("i" + #1) "n" "c"
    else #false.

(** [TryAdd]. *)
Definition observers_try_add_code : val :=
  λ: "l" "c" "slot",
    let: "n" := ! ("l" +ₗ #1) in
    if: "n" = #0 then observers_add_code "l" "c" "slot"
    else
      let: "lt" := ! ("l" +ₗ #2) in
      if: ("lt" < "n") && (Fst (! (! "l" +ₗ "lt")) = "c") then #(-1)
      else if: "n" ≤ #(Z.of_nat scan_limit) then
        if: observers_scan_code (! "l") #0 "n" "c" then #(-1) else observers_add_code "l" "c" "slot"
      else #(-2).

(** [RemoveAt]. A handle holding another observer fails the assertion, the [InvalidOperationException]. The
    result is the [MovedIn] notice, which the caller delivers. *)
Definition observers_remove_at_code : val :=
  λ: "l" "c" "h",
    let: "n" := ! ("l" +ₗ #1) in
    let: "a" := ! "l" in
    (assert: ("h" < "n") && (Fst (! ("a" +ₗ "h")) = "c"));;
    let: "last" := "n" - #1 in
    if: "h" = "last" then
      ("a" +ₗ "last") <- blank_cell;;
      ("l" +ₗ #1) <- "last";;
      NONE
    else
      let: "mv" := ! ("a" +ₗ "last") in
      ("a" +ₗ "h") <- "mv";;
      ("a" +ₗ "last") <- blank_cell;;
      ("l" +ₗ #1) <- "last";;
      (if: ! ("l" +ₗ #2) = "last" then ("l" +ₗ #2) <- "h" else #());;
      SOME (Fst "mv", Snd "mv", "h").

(** The walk of [NotifyDirty], [NotifyDirtyExcept] and [NotifyCheck] from cursor [n], the F# [i + 1]. Each
    step re-clamps the cursor to the count and calls [visit] on the observer below it. *)
Definition observers_walk_loop : val :=
  rec: "loop" "l" "visit" "n" :=
    let: "k" := ! ("l" +ₗ #1) in
    let: "m" := if: "n" ≤ "k" then "n" else "k" in
    if: "m" = #0 then #()
    else
      let: "i" := "m" - #1 in
      "visit" (Fst (! (! "l" +ₗ "i")));;
      "loop" "l" "visit" "i".

Definition observers_walk_code : val :=
  λ: "l" "visit", observers_walk_loop "l" "visit" (! ("l" +ₗ #1)).

(** ** Cells *)

Lemma item_cells_length xs cap : length xs <= cap -> length (item_cells xs cap) = cap.
Proof. intros. rewrite /item_cells length_app length_fmap length_replicate. lia. Qed.

Lemma item_cells_lookup xs cap i :
  item_cells xs cap !! i =
    if decide (i < length xs) then observer_cell <$> xs !! i
    else if decide (i < cap) then Some blank_cell else None.
Proof.
  rewrite /item_cells. destruct (decide (i < length xs)) as [Hi|Hi].
  - rewrite lookup_app_l ?length_fmap // list_lookup_fmap //.
  - rewrite lookup_app_r ?length_fmap; [|lia].
    destruct (decide (i < cap)) as [Hc|Hc].
    + apply lookup_replicate_2. lia.
    + apply lookup_replicate_None. lia.
Qed.

Lemma item_cells_lookup_some xs cap i p :
  xs !! i = Some p -> item_cells xs cap !! i = Some (observer_cell p).
Proof.
  intros Hi. rewrite item_cells_lookup decide_True; [by rewrite Hi|]. by eapply lookup_lt_Some.
Qed.

Lemma item_cells_snoc xs cap p :
  length xs < cap -> <[length xs := observer_cell p]> (item_cells xs cap) = item_cells (xs ++ [p]) cap.
Proof.
  intros Hlt. apply list_eq. intros i.
  destruct (decide (i = length xs)) as [->|Hne].
  - rewrite list_lookup_insert_eq; [|rewrite item_cells_length; lia].
    rewrite item_cells_lookup length_app /= decide_True; [|lia]. rewrite lookup_app_r // Nat.sub_diag //.
  - rewrite list_lookup_insert_ne // !item_cells_lookup length_app /=.
    repeat case_decide; try lia; try done. rewrite lookup_app_l //.
Qed.

Lemma item_cells_pop xs cap k :
  length xs = S k -> length xs <= cap ->
  <[k := blank_cell]> (item_cells xs cap) = item_cells (take k xs) cap.
Proof.
  intros Hl Hc. apply list_eq. intros i.
  destruct (decide (i = k)) as [->|Hne].
  - rewrite list_lookup_insert_eq; [|rewrite item_cells_length; lia].
    rewrite item_cells_lookup length_take. repeat case_decide; try lia. done.
  - rewrite list_lookup_insert_ne // !item_cells_lookup length_take.
    repeat case_decide; try lia; try done. rewrite lookup_take_lt //. lia.
Qed.

Lemma item_cells_insert xs cap i p :
  i < length xs -> <[i := observer_cell p]> (item_cells xs cap) = item_cells (<[i := p]> xs) cap.
Proof.
  intros Hi. apply list_eq. intros j.
  rewrite (item_cells_lookup (<[i:=p]> xs)) length_insert.
  destruct (decide (j = i)) as [->|Hne].
  - rewrite list_lookup_insert_eq; [|rewrite /item_cells length_app length_fmap; lia].
    rewrite decide_True // list_lookup_insert_eq //.
  - rewrite list_lookup_insert_ne // item_cells_lookup list_lookup_insert_ne //.
Qed.

Lemma item_cells_full xs : item_cells xs (length xs) = observer_cell <$> xs.
Proof. rewrite /item_cells Nat.sub_diag app_nil_r //. Qed.

(** ** Mutation points *)

Lemma mutation_points_join (P : observer_set -> Prop) ms r o o1 :
  apply_mutations ms o = Some o1 ->
  Forall P (mutation_points ms o) -> Forall P (mutation_points r o1) ->
  Forall P (mutation_points (ms ++ r) o).
Proof.
  revert o. induction ms as [|m ms IH]; intros o Ha H1 H2; simpl in *.
  - by simplify_eq.
  - destruct (apply_mutation m o) as [o2|] eqn:E; simpl in *; [|done].
    inversion H1 as [|?? Ho H1']; subst. constructor; [done|]. by eapply IH.
Qed.

Section heap.
Context `{!heapGS Σ}.

(**md
The observer set at `l` holds the observers of `o` in order, with `observer_latest o` as `latest`. The array's
capacity is at least the count, and the cells past the count are blank.

- [l:] the location of the observer set's fields.
- [o:] the observer set represented.
*)
Definition is_observers (l : loc) (o : observer_set) : iProp Σ :=
  ∃ (a : loc) (cap : nat),
    ⌜length (observer_items o) <= cap⌝ ∗
    l ↦ #a ∗ (l +ₗ 1) ↦ #(Z.of_nat (length (observer_items o))) ∗
    (l +ₗ 2) ↦ #(Z.of_nat (observer_latest o)) ∗ (l +ₗ 3) ↦ #(Z.of_nat cap) ∗
    a ↦∗ item_cells (observer_items o) cap.

Lemma fields4 l v0 v1 v2 v3 :
  l ↦∗ [v0; v1; v2; v3] ⊣⊢ l ↦ v0 ∗ (l +ₗ 1) ↦ v1 ∗ (l +ₗ 2) ↦ v2 ∗ (l +ₗ 3) ↦ v3.
Proof. rewrite !array_cons array_nil !Loc.add_assoc /=. by rewrite right_id. Qed.

(**md
**`new ObserverSet ()` is the empty set.**

- [l:] the location returned.
*)
Lemma observers_new_spec :
  {{{ True }}} observers_new_code #() {{{ l, RET #l; is_observers l empty_observer_set }}}.
Proof.
  iIntros (Φ) "_ HΦ". wp_lam. wp_alloc l as "Hl"; [lia|].
  rewrite (_ : Z.to_nat 4 = 4) // /=. iDestruct (fields4 with "Hl") as "(H0 & H1 & H2 & H3)".
  wp_pures. wp_store. iModIntro. iApply "HΦ". iExists l, 0. rewrite /item_cells /= array_nil.
  iFrame. iPureIntro. lia.
Qed.

(**md
**`grow` moves the observers into a larger array.**

Called when the count equals the capacity. The new capacity is greater than the count, and its cells past
the count are blank.

- [l:] the observer set's fields.
- [a:] the full array.
- [xs:] the observers, as many as the capacity.
*)
Lemma observers_grow_spec (l a : loc) (xs : list (node_id * nat)) :
  {{{ l ↦ #a ∗ (l +ₗ 1) ↦ #(Z.of_nat (length xs)) ∗ (l +ₗ 3) ↦ #(Z.of_nat (length xs)) ∗
      a ↦∗ item_cells xs (length xs) }}}
    observers_grow_code #l
  {{{ (b : loc) (cap : nat), RET #(); ⌜length xs < cap⌝ ∗ l ↦ #b ∗ (l +ₗ 1) ↦ #(Z.of_nat (length xs)) ∗
      (l +ₗ 3) ↦ #(Z.of_nat cap) ∗ b ↦∗ item_cells xs cap }}}.
Proof.
  iIntros (Φ) "(Hl & Hn & Hcap & Ha) HΦ". wp_lam. wp_load. wp_pures.
  set (size := if decide (length xs = 0) then 1 else max 4 (length xs * 2)).
  wp_bind (If _ _ _). iApply (wp_wand _ _ _ (fun v => ⌜v = #(Z.of_nat size)⌝)%I).
  { case_bool_decide as H0; wp_pures.
    - assert (length xs = 0) by (injection H0; lia). iPureIntro. subst size. rewrite decide_True //.
    - assert (length xs ≠ 0) by (intros E; apply H0; rewrite E //).
      case_bool_decide as H4; wp_pures; iPureIntro; subst size; rewrite decide_False //;
        f_equal; f_equal; lia. }
  iIntros (v ->). wp_pures.
  wp_alloc b as "Hb"; [subst size; case_decide; lia|].
  rewrite Nat2Z.id. wp_pures. wp_load.
  rewrite item_cells_full.
  assert (Hsz : length xs < size) by (subst size; case_decide; lia).
  rewrite (_ : replicate size blank_cell =
               replicate (length xs) blank_cell ++ replicate (size - length xs) blank_cell);
    [|rewrite -replicate_add; f_equal; lia].
  iDestruct (array_app with "Hb") as "[Hb1 Hb2]".
  wp_apply (wp_array_copy_to with "[$Hb1 $Ha]"); [rewrite length_replicate //|rewrite length_fmap //|].
  iIntros "[Hb1 _]". wp_store. wp_store. iModIntro. iApply "HΦ". iFrame. iSplit; [done|].
  iApply array_app. rewrite length_fmap length_replicate. iFrame.
Qed.

(**md
**`Add` appends the observer at the handle it returns.**

The handle is the old count, and the set after it is `observers_add`'s.

- [l:] the observer set.
- [o:] the observers before the add.
- [c:] the observer added.
- [slot:] the slot recorded with it.
*)
Lemma observers_add_spec (l : loc) (o : observer_set) (c : node_id) (slot : nat) :
  {{{ is_observers l o }}}
    observers_add_code #l #(Z.of_nat c) #(Z.of_nat slot)
  {{{ RET #(Z.of_nat (observers_add c slot o).2); is_observers l (observers_add c slot o).1 }}}.
Proof.
  iIntros (Φ) "Ho HΦ". iDestruct "Ho" as (a cap Hcap) "(Ha & Hn & Hlt & Hc & Hcells)".
  destruct o as [xs latest]; simpl in *.
  wp_lam. wp_pures. wp_load. wp_load. wp_pures.
  wp_bind (If _ _ _).
  iApply (wp_wand _ _ _ (fun _ => ∃ (b : loc) (cap' : nat), ⌜length xs < cap'⌝ ∗ l ↦ #b ∗
             (l +ₗ 1) ↦ #(Z.of_nat (length xs)) ∗ (l +ₗ 3) ↦ #(Z.of_nat cap') ∗ b ↦∗ item_cells xs cap')%I
           with "[Ha Hn Hc Hcells]").
  { case_bool_decide as Heq; wp_pures.
    - assert (cap = length xs) as -> by (injection Heq; lia).
      wp_apply (observers_grow_spec with "[$Ha $Hn $Hc $Hcells]").
      iIntros (b cap') "(% & ? & ? & ? & ?)". iExists b, cap'. by iFrame.
    - iModIntro. iExists a, cap. iFrame. iPureIntro.
      assert (cap ≠ length xs) by (intros E; apply Heq; rewrite E //). lia. }
  iIntros (?) "(%b & %cap' & %Hlt' & Ha & Hn & Hc & Hcells)". wp_pures. wp_load. wp_load. wp_pures.
  wp_apply (wp_store_offset with "Hcells").
  { apply lookup_lt_is_Some. rewrite item_cells_length; lia. }
  iIntros "Hcells". rewrite (item_cells_snoc xs cap' (c, slot)) //.
  wp_pures. wp_store. wp_pures. wp_store. iModIntro.
  rewrite /observers_add /=. iApply "HΦ". iExists b, cap'. rewrite length_app /=. iFrame.
  rewrite Nat2Z.inj_add. iFrame. iPureIntro. lia.
Qed.

(**md
**`TryAdd`'s scan answers whether an observer from position `i` on is `c`.**

- [a:] the array.
- [cells:] its contents, holding a cell per observer of `xs`.
- [xs:] the observers.
- [c:] the observer looked for.
- [i:] the first position scanned.
*)
Lemma observers_scan_spec (a : loc) (cells : list val) (xs : list (node_id * nat)) (c i : nat) :
  (forall j p, xs !! j = Some p -> cells !! j = Some (observer_cell p)) ->
  {{{ a ↦∗ cells }}}
    observers_scan_code #a #(Z.of_nat i) #(Z.of_nat (length xs)) #(Z.of_nat c)
  {{{ RET #(bool_decide (c ∈ fst <$> drop i xs)); a ↦∗ cells }}}.
Proof.
  intros Hcells. iIntros (Φ) "Ha HΦ". iLöb as "IH" forall (i).
  wp_rec. wp_pures. destruct (decide (i < length xs)) as [Hi'|Hi].
  - rewrite (bool_decide_eq_true_2 (Z.of_nat i < Z.of_nat (length xs))%Z); [|lia]. wp_pures.
    destruct (lookup_lt_is_Some_2 xs i Hi') as [[c' s'] Hp].
    wp_apply (wp_load_offset with "Ha"); [by apply Hcells|]. iIntros "Ha". wp_pures.
    rewrite (drop_S _ _ _ Hp) /=.
    destruct (decide (c' = c)) as [->|He].
    + rewrite (bool_decide_eq_true_2 (#(Z.of_nat c) = #(Z.of_nat c))) //. wp_pures.
      iModIntro. rewrite bool_decide_eq_true_2; [by iApply "HΦ"|]. apply elem_of_cons. by left.
    + rewrite (bool_decide_eq_false_2 (#(Z.of_nat c') = #(Z.of_nat c))); [|intros [= ?]; lia]. wp_pures.
      rewrite (_ : (Z.of_nat i + 1)%Z = Z.of_nat (S i)); [|lia].
      iApply ("IH" with "Ha"). iNext.
      rewrite (bool_decide_ext (c ∈ c' :: _) (c ∈ fst <$> drop (S i) xs)); [done|].
      rewrite elem_of_cons. split; [|by right]. intros [->|]; [done|done].
  - rewrite (bool_decide_eq_false_2 (Z.of_nat i < Z.of_nat (length xs))%Z); [|lia]. wp_pures.
    iModIntro. rewrite drop_ge; [|lia]. by iApply "HΦ".
Qed.

(**md
**`TryAdd` answers as `observers_try_add` does.**

The answer is the new handle, `EdgeHandle.Unlinked` or `EdgeHandle.Unknown`, and the set after it is
`observers_try_add`'s.

- [l:] the observer set.
- [o:] the observers before the call.
- [c:] the observer offered.
- [slot:] the slot recorded if it is added.
*)
Lemma observers_try_add_spec (l : loc) (o : observer_set) (c : node_id) (slot : nat) :
  {{{ is_observers l o }}}
    observers_try_add_code #l #(Z.of_nat c) #(Z.of_nat slot)
  {{{ RET tried_val (observers_try_add c slot o).2; is_observers l (observers_try_add c slot o).1 }}}.
Proof.
  iIntros (Φ) "Ho HΦ". wp_lam. wp_pures.
  iDestruct "Ho" as (a cap Hcap) "(Ha & Hn & Hlt & Hc & Hcells)".
  wp_load. wp_pures. unfold observers_try_add.
  destruct (decide (length (observer_items o) = 0)) as [H0|H0].
  { rewrite bool_decide_eq_true_2; [|by rewrite H0]. wp_pures.
    wp_apply (observers_add_spec with "[Ha Hn Hlt Hc Hcells]"); [iExists a, cap; by iFrame|].
    iIntros "Ho". by iApply "HΦ". }
  rewrite bool_decide_eq_false_2; [|intros [= ?]; lia]. wp_pures. wp_load. wp_pures.
  wp_bind (If _ _ #false).
  iApply (wp_wand _ _ _ (fun v => ⌜v = #(bool_decide (fst <$> observer_items o !! observer_latest o = Some c))⌝ ∗
             l ↦ #a ∗ a ↦∗ item_cells (observer_items o) cap)%I with "[Ha Hcells]").
  { destruct (decide (observer_latest o < length (observer_items o))) as [Hlt'|Hlt'].
    - rewrite bool_decide_eq_true_2; [|lia]. wp_pures.
      destruct (lookup_lt_is_Some_2 (observer_items o) (observer_latest o)) as [[c' s'] Hp]; [lia|].
      wp_load. wp_pures. wp_apply (wp_load_offset with "Hcells"); [by apply item_cells_lookup_some|].
      iIntros "Hcells". wp_pures. iFrame. iPureIntro. rewrite Hp /=.
      destruct (decide (c' = c)) as [->|Hne].
      + rewrite !bool_decide_eq_true_2 //.
      + rewrite !bool_decide_eq_false_2 //; intros [= ?]; lia.
    - rewrite bool_decide_eq_false_2; [|lia]. wp_pures.
      iFrame. iPureIntro. rewrite lookup_ge_None_2 /=; [|lia]. done. }
  iIntros (v) "(-> & Ha & Hcells)".
  destruct (decide (fst <$> observer_items o !! observer_latest o = Some c)) as [Hl|Hl].
  { rewrite bool_decide_eq_true_2 //. wp_pures. iModIntro. iApply "HΦ". iExists a, cap. by iFrame. }
  rewrite bool_decide_eq_false_2 //. wp_pures.
  destruct (decide (length (observer_items o) <= scan_limit)) as [Hs|Hs].
  - rewrite bool_decide_eq_true_2; [|lia]. wp_pures. wp_load.
    wp_apply (observers_scan_spec a _ (observer_items o) c 0 with "Hcells").
    { intros j p Hj. by apply item_cells_lookup_some. }
    iIntros "Hcells". rewrite drop_0. fold (observer_view o).
    destruct (decide (c ∈ observer_view o)) as [Hin|Hin].
    + rewrite bool_decide_eq_true_2 //. wp_pures. iModIntro. iApply "HΦ". iExists a, cap. by iFrame.
    + rewrite bool_decide_eq_false_2 //. wp_pures.
      wp_apply (observers_add_spec with "[Ha Hn Hlt Hc Hcells]"); [iExists a, cap; by iFrame|].
      iIntros "Ho". destruct (observers_add c slot o) as [o' h] eqn:E. by iApply "HΦ".
  - rewrite bool_decide_eq_false_2; [|lia]. wp_pures. iModIntro. iApply "HΦ". iExists a, cap. by iFrame.
Qed.

(**md
**`RemoveAt` swap-removes as `observers_remove_at` does.**

When the handle holds `c`, the code returns the `MovedIn` notice of the pure result and leaves the set it
names. Cells past the new count are blank.

- [l:] the observer set.
- [o:] the observers before the removal.
- [c:] the observer removed.
- [h:] its handle.
- [o':] the set after the removal.
- [m:] the notice: the moved observer, its slot and its new handle.
*)
Lemma observers_remove_at_spec (l : loc) (o : observer_set) (c : node_id) (h : nat) (o' : observer_set)
    (m : option (node_id * nat * nat)) :
  observers_remove_at c h o = Some (o', m) ->
  {{{ is_observers l o }}}
    observers_remove_at_code #l #(Z.of_nat c) #(Z.of_nat h)
  {{{ RET moved_val m; is_observers l o' }}}.
Proof.
  intros Hr. iIntros (Φ) "Ho HΦ". wp_lam. wp_pures.
  iDestruct "Ho" as (a cap Hcap) "(Ha & Hn & Hlt & Hc & Hcells)".
  destruct o as [xs latest]. unfold observers_remove_at in Hr. simpl in *.
  destruct (xs !! h) as [[c' s]|] eqn:Eh; [|done].
  destruct (decide (c' = c)) as [->|]; [|done].
  pose proof (lookup_lt_Some _ _ _ Eh) as Hh.
  wp_load. wp_load. wp_pures.
  wp_apply wp_assert. wp_pures. rewrite bool_decide_eq_true_2; [|lia]. wp_pures.
  wp_apply (wp_load_offset with "Hcells"); [by apply item_cells_lookup_some|]. iIntros "Hcells".
  wp_pures. rewrite bool_decide_eq_true_2 //. iModIntro. iSplit; [done|]. iNext. wp_pures.
  remember (pred (length xs)) as last eqn:Elast. assert (Hlen : length xs = S last) by lia.
  rewrite (_ : (Z.of_nat (length xs) - 1)%Z = Z.of_nat last); [|lia].
  destruct (decide (h = last)) as [->|Hne].
  - simplify_eq. set (k := pred (length xs)) in *. rewrite bool_decide_eq_true_2 //. wp_pures.
    wp_apply (wp_store_offset with "Hcells").
    { apply lookup_lt_is_Some. rewrite item_cells_length; lia. }
    iIntros "Hcells". wp_pures. wp_store. wp_pures. iModIntro. iApply "HΦ". iExists a, cap.
    rewrite (item_cells_pop xs cap k) // /= length_take. iFrame.
    rewrite (_ : k `min` length xs = k); [|lia]. iFrame. iPureIntro. lia.
  - destruct (xs !! last) as [[mc ms]|] eqn:El; [|done]. simplify_eq.
    set (last := pred (length xs)) in *.
    rewrite bool_decide_eq_false_2; [|intros [= ?]; lia]. wp_pures.
    wp_apply (wp_load_offset with "Hcells"); [by apply item_cells_lookup_some|]. iIntros "Hcells".
    wp_pures. wp_apply (wp_store_offset with "Hcells").
    { apply lookup_lt_is_Some. rewrite item_cells_length; lia. }
    iIntros "Hcells". wp_pures. wp_apply (wp_store_offset with "Hcells").
    { apply lookup_lt_is_Some. rewrite length_insert item_cells_length; lia. }
    iIntros "Hcells". wp_pures. wp_store. wp_load. wp_pures.
    assert (Hcells : <[last:=blank_cell]> (<[h:=observer_cell (mc, ms)]> (item_cells xs cap)) =
                     item_cells (<[h:=(mc, ms)]> (take last xs)) cap).
    { rewrite list_insert_insert_ne; [|done]. rewrite item_cells_pop // item_cells_insert //.
      rewrite length_take. lia. }
    rewrite Hcells.
    case_bool_decide as Hle; wp_pures.
    + wp_store. wp_pures. iModIntro. iApply "HΦ". iExists a, cap. rewrite /= length_insert length_take.
      rewrite decide_True; [|injection Hle; lia].
      rewrite (_ : last `min` length xs = last); [|lia]. iFrame. iPureIntro. lia.
    + iModIntro. iApply "HΦ". iExists a, cap. rewrite /= length_insert length_take.
      rewrite decide_False; [|intros ->; done].
      rewrite (_ : last `min` length xs = last); [|lia]. iFrame. iPureIntro. lia.
Qed.

(** ** The walk *)

(**md
The visitor of a walk over the set at `l`. Each call receives the set, the resource `I`, and the position and
slot at which the set holds the observer visited. It may mutate the
set by `observers_add` and `observers_remove_at`, keeps `P` at every point it passes through, and returns
`V` of the observer visited.

- [l:] the observer set walked.
- [visit:] the closure called on each observer.
- [I:] the resource the visitor needs besides the set.
- [P:] the property of the set kept at every mutation point.
- [V:] what a visit returns of its observer.
*)
Definition visitor_spec (l : loc) (visit : val) (I : observer_set -> iProp Σ) (P : observer_set -> Prop)
    (V : node_id -> iProp Σ) : iProp Σ :=
  ∀ (o : observer_set) (d : node_id) (i slot : nat),
    {{{ is_observers l o ∗ I o ∗ ⌜P o⌝ ∗ ⌜observer_items o !! i = Some (d, slot)⌝ }}} visit #(Z.of_nat d)
    {{{ ms o1, RET #(); ⌜apply_mutations ms o = Some o1⌝ ∗ ⌜Forall P (mutation_points ms o)⌝ ∗
        is_observers l o1 ∗ I o1 ∗ V d }}}.

(**md
**The walk's loop from cursor `n` is the pure walk from `n`.**

- [l:] the observer set walked.
- [visit:] the visitor.
- [I:] the visitor's resource.
- [P:] the property kept at every point.
- [V:] what a visit returns.
- [o:] the set at the loop's start.
- [n:] the cursor, the F# index plus one.
*)
Lemma observers_walk_loop_spec (l : loc) (visit : val) I P V (o : observer_set) (n : nat) :
  {{{ visitor_spec l visit I P V ∗ is_observers l o ∗ I o ∗ ⌜P o⌝ }}}
    observers_walk_loop #l visit #(Z.of_nat n)
  {{{ muts vis o', RET #(); ⌜walk n o muts vis o'⌝ ∗ ⌜Forall P (mutation_points (concat muts) o)⌝ ∗
      is_observers l o' ∗ I o' ∗ [∗ list] d ∈ vis, V d }}}.
Proof.
  iIntros (Φ) "(#Hvis & Ho & HI & %HP) HΦ". iLöb as "IH" forall (n o HP).
  wp_rec. wp_pures. iDestruct "Ho" as (a cap Hcap) "(Ha & Hn & Hlt & Hc & Hcells)".
  wp_load. wp_pures.
  set (k := length (observer_items o)).
  wp_bind (If _ _ _). iApply (wp_wand _ _ _ (fun v => ⌜v = #(Z.of_nat (min n k))⌝)%I).
  { case_bool_decide; wp_pures; iPureIntro; f_equal; f_equal; lia. }
  iIntros (v ->). wp_pures.
  destruct (min n k) as [|i] eqn:Hmin.
  - wp_pures. iModIntro. iApply ("HΦ" $! [] [] o). iSplit; [iPureIntro; by constructor|].
    iSplit; [iPureIntro; by constructor|]. iSplitR "HI"; [iExists a, cap; by iFrame|]. by iFrame.
  - rewrite bool_decide_eq_false_2; [|intros [= ?]; lia]. wp_pures.
    rewrite (_ : (Z.of_nat (S i) - 1)%Z = Z.of_nat i); [|lia].
    destruct (lookup_lt_is_Some_2 (observer_items o) i) as [[d slot] Hd]; [subst k; lia|].
    wp_load. wp_pures. wp_apply (wp_load_offset with "Hcells"); [by apply item_cells_lookup_some|].
    iIntros "Hcells". wp_pures.
    wp_apply ("Hvis" with "[Ha Hn Hlt Hc Hcells HI]").
    { iFrame "HI". iSplitL; [iExists a, cap; by iFrame|]. iSplit; [done|]. iPureIntro. exact Hd. }
    iIntros (ms o1) "(%Hms & %HPms & Ho1 & HI1 & HV)". wp_pures.
    assert (HP1 : P o1).
    { clear -Hms HPms. revert o Hms HPms. induction ms as [|m r IH]; intros o Hms HPms; simpl in *.
      - simplify_eq. by inversion HPms.
      - destruct (apply_mutation m o) as [o2|]; simpl in *; [|done].
        inversion HPms; subst. by eapply IH. }
    iApply ("IH" $! i o1 HP1 with "Ho1 HI1"). iNext.
    iIntros (muts vis o') "(%Hw & %HPw & Ho' & HI' & HVs)".
    iApply ("HΦ" $! (ms :: muts) (d :: vis) o'). iFrame. iPureIntro. split.
    + econstructor; [done|done|done|done].
    + simpl. by eapply mutation_points_join.
Qed.

(**md
**A walk is the pure walk.**

Each visit leaves `P` at every point it passes through. The walk relates the starting count and set to the
mutations of each visit, the observers visited and the final set, and every visited observer's `V` is returned.

- [l:] the observer set walked.
- [visit:] the visitor.
- [I:] the visitor's resource.
- [P:] the property kept at every point.
- [V:] what a visit returns.
- [o:] the set at the start of the walk.
*)
Lemma walk_trace_spec (l : loc) (visit : val) I P V (o : observer_set) :
  {{{ visitor_spec l visit I P V ∗ is_observers l o ∗ I o ∗ ⌜P o⌝ }}}
    observers_walk_code #l visit
  {{{ muts vis o', RET #(); ⌜walk (length (observer_items o)) o muts vis o'⌝ ∗
      ⌜Forall P (mutation_points (concat muts) o)⌝ ∗ is_observers l o' ∗ I o' ∗ [∗ list] d ∈ vis, V d }}}.
Proof.
  iIntros (Φ) "(#Hvis & Ho & HI & %HP) HΦ". wp_lam. wp_pures.
  iDestruct "Ho" as (a cap Hcap) "(Ha & Hn & Hlt & Hc & Hcells)". wp_load.
  wp_apply (observers_walk_loop_spec with "[Ha Hn Hlt Hc Hcells HI]").
  { iFrame "#". iFrame "HI". iSplitL; [iExists a, cap; by iFrame|done]. }
  done.
Qed.

(**md
**A walk visits every observer held exactly once throughout.**

The heap form of [`walk_covers`](Ranvier.Pure.ObserverSet.html#walk_covers). When each visit keeps `c` in
exactly one position at every point it passes through, and `c` starts in exactly one position, the walk calls
the visitor on `c`, so `V c` is among the results.

- [l:] the observer set walked.
- [visit:] the visitor, whose mutations keep `held_once c`.
- [I:] the visitor's resource.
- [V:] what a visit returns.
- [o:] the set at the start of the walk.
- [c:] the observer held once throughout.
*)
Theorem walk_spec (l : loc) (visit : val) I V (o : observer_set) (c : node_id) :
  {{{ visitor_spec l visit I (held_once c) V ∗ is_observers l o ∗ I o ∗ ⌜held_once c o⌝ }}}
    observers_walk_code #l visit
  {{{ vis o', RET #(); ⌜c ∈ vis⌝ ∗ is_observers l o' ∗ I o' ∗ [∗ list] d ∈ vis, V d }}}.
Proof.
  iIntros (Φ) "H HΦ". wp_apply (walk_trace_spec with "H").
  iIntros (muts vis o') "(%Hw & %HP & Ho' & HI' & HV)". iApply "HΦ". iFrame. iPureIntro.
  by eapply walk_covers.
Qed.

End heap.
