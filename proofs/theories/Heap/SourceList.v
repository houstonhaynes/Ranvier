From stdpp Require Import prelude gmap.
From Ranvier.Base Require Import Prelude.
From Ranvier.Pure Require Import ObserverSet SourceList.
From iris.heap_lang Require Import proofmode notation.
From iris.heap_lang.lib Require Import array.
From Ranvier.Heap Require Import ObserverSet.

(** * The source list in HeapLang

A source list is a block of six fields: the cell of position 0 (field 0, F#'s [first] and [firstHandle]),
the array of the later positions (field 1, [rest]), its capacity (field 2), [count] (field 3), [cursor]
(field 4) and [held] (field 5). A cell is the pair of the source's id and the edge handle, the .NET
[SourceSlot] struct, and cells past [count] are blank. [held] is [NONE] or a list of the slots' sources.

Nodes are named by id, and a resolver closure maps an id to the locations of the node's observer set and
source list: the F# object reference. *)

Definition handle_val (mh : option nat) : val :=
  match mh with Some h => #(Z.of_nat h) | None => #(-1) end.

Definition slot_val (p : node_id * option nat) : val := (#(Z.of_nat p.1), handle_val p.2).

(** The cell of a blanked position: no source and [EdgeHandle.Unlinked]. *)
Definition blank_slot : val := (#(-1), #(-1)).

(**md
The cell of position `j` of the slots `sl`, blank past the end.
*)
Definition slot_fun (sl : list (node_id * option nat)) (j : nat) : val :=
  default blank_slot (slot_val <$> sl !! j).

Fixpoint list_val (xs : list node_id) : val :=
  match xs with [] => NONEV | x :: r => SOMEV (#(Z.of_nat x), list_val r) end.

(**md
The field `hv` represents the index `held` of the slots `sl`: `NONE` with `held` empty, or, once the list is
longer than `scan_limit`, a list of the slots' sources, which `held` is the set of.

- [hv:] the value of the `held` field.
- [held:] the set of sources the index holds.
- [sl:] the slots.
*)
Definition held_rep (hv : val) (held : gset node_id) (sl : list (node_id * option nat)) : Prop :=
  (hv = NONEV /\ held = ∅) \/
  (scan_limit < length sl /\ held = list_to_set (fst <$> sl) /\
   exists xs, hv = SOMEV (list_val xs) /\ list_to_set xs = held).

(** [f] with position [i] replaced by [v]. *)
Definition fupd (f : nat -> val) (i : nat) (v : val) (j : nat) : val := if decide (j = i) then v else f j.

(** [sourceAt i] and [handleAt i] as one cell. *)
Definition sources_cell_code : val :=
  λ: "ls" "i", if: "i" = #0 then ! "ls" else ! (! ("ls" +ₗ #1) +ₗ ("i" - #1)).

Definition sources_set_cell_code : val :=
  λ: "ls" "i" "v", if: "i" = #0 then "ls" <- "v" else (! ("ls" +ₗ #1) +ₗ ("i" - #1)) <- "v".

(** [MovedIn]: [setHandle]. *)
Definition sources_moved_in_code : val :=
  λ: "ls" "slot" "h", sources_set_cell_code "ls" "slot" (Fst (sources_cell_code "ls" "slot"), "h").

(** [grow]: the capacity after position 0 doubles, to at least 4. *)
Definition sources_grow_code : val :=
  λ: "ls",
    let: "cap" := ! ("ls" +ₗ #2) in
    let: "size" := if: #4 < "cap" * #2 then "cap" * #2 else #4 in
    let: "b" := AllocN "size" blank_slot in
    array_copy_to "b" (! ("ls" +ₗ #1)) "cap";;
    ("ls" +ₗ #1) <- "b";;
    ("ls" +ₗ #2) <- "size".

(** [heldBefore], from position [i]. *)
Definition sources_held_before_code : val :=
  rec: "hb" "ls" "i" "until" "s" :=
    if: "i" < "until" then
      if: Fst (sources_cell_code "ls" "i") = "s" then #true else "hb" "ls" ("i" + #1) "until" "s"
    else #false.

(** The index of [holds]: the sources of the positions below [i], prepended to [acc]. *)
Definition held_build_code : val :=
  rec: "build" "ls" "i" "acc" :=
    if: "i" = #0 then "acc"
    else let: "j" := "i" - #1 in "build" "ls" "j" (SOME (Fst (sources_cell_code "ls" "j"), "acc")).

Definition held_mem_code : val :=
  rec: "mem" "x" "s" :=
    match: "x" with
      NONE => #false
    | SOME "p" => if: Fst "p" = "s" then #true else "mem" (Snd "p") "s"
    end.

(** [holds]. *)
Definition sources_holds_code : val :=
  λ: "ls" "until" "s",
    if: "until" ≤ #(Z.of_nat scan_limit) then sources_held_before_code "ls" #0 "until" "s"
    else
      (match: ! ("ls" +ₗ #5) with
         NONE => ("ls" +ₗ #5) <- SOME (held_build_code "ls" "until" NONE)
       | SOME <> => #()
       end);;
      match: ! ("ls" +ₗ #5) with NONE => #false | SOME "x" => held_mem_code "x" "s" end.

(** The loop of [trimFrom] from position [i] up to [n]. Each handle is read when its position is reached. *)
Definition sources_trim_loop : val :=
  rec: "loop" "res" "self" "ls" "i" "n" :=
    if: "i" < "n" then
      let: "c" := sources_cell_code "ls" "i" in
      (if: #0 ≤ Snd "c" then
         let: "mv" := observers_remove_at_code (Fst ("res" (Fst "c"))) "self" (Snd "c") in
         match: "mv" with
           NONE => #()
         | SOME "p" => sources_moved_in_code (Snd ("res" (Fst (Fst "p")))) (Snd (Fst "p")) (Snd "p")
         end
       else #());;
      sources_set_cell_code "ls" "i" blank_slot;;
      "loop" "res" "self" "ls" ("i" + #1) "n"
    else #().

(** [trimFrom]. *)
Definition sources_trim_from_code : val :=
  λ: "res" "self" "ls" "from",
    ("ls" +ₗ #5) <- NONE;;
    sources_trim_loop "res" "self" "ls" "from" (! ("ls" +ₗ #3));;
    ("ls" +ₗ #3) <- "from".

(** The end of [Append]: a built [held] index gains the source appended. *)
Definition sources_index_add_code : val :=
  λ: "ls" "s", match: ! ("ls" +ₗ #5) with NONE => #() | SOME "x" => ("ls" +ₗ #5) <- SOME (SOME ("s", "x")) end.

(** The part of [Append] after its trim. *)
Definition sources_link_code : val :=
  λ: "res" "self" "ls" "s",
    let: "slot" := ! ("ls" +ₗ #3) in
    let: "lo" := Fst ("res" "s") in
    if: "slot" = #0 then
      "ls" <- ("s", #(-1));;
      ("ls" +ₗ #3) <- #1;;
      ("ls" +ₗ #4) <- #1;;
      let: "h" := observers_add_code "lo" "self" #0 in
      "ls" <- ("s", "h")
    else
      (if: "slot" - #1 = ! ("ls" +ₗ #2) then sources_grow_code "ls" else #());;
      sources_set_cell_code "ls" "slot" ("s", #(-1));;
      ("ls" +ₗ #3) <- "slot" + #1;;
      ("ls" +ₗ #4) <- "slot" + #1;;
      let: "tried" := observers_try_add_code "lo" "self" "slot" in
      let: "h" :=
        if: "tried" = #(-2) then
          if: sources_holds_code "ls" "slot" "s" then #(-1) else observers_add_code "lo" "self" "slot"
        else "tried" in
      sources_set_cell_code "ls" "slot" ("s", "h");;
      sources_index_add_code "ls" "s".

(** [Append]. *)
Definition sources_append_code : val :=
  λ: "res" "self" "ls" "s",
    (if: ! ("ls" +ₗ #4) < ! ("ls" +ₗ #3) then sources_trim_from_code "res" "self" "ls" (! ("ls" +ₗ #4))
     else #());;
    sources_link_code "res" "self" "ls" "s".

(** [Add], on the source list of [self]. *)
Definition sources_add_code : val :=
  λ: "res" "self" "s",
    let: "ls" := Snd ("res" "self") in
    let: "cur" := ! ("ls" +ₗ #4) in
    if: ("cur" < ! ("ls" +ₗ #3)) && (Fst (sources_cell_code "ls" "cur") = "s") then
      ("ls" +ₗ #4) <- "cur" + #1
    else if: (#0 < "cur") && (Fst (sources_cell_code "ls" ("cur" - #1)) = "s") then #()
    else sources_append_code "res" "self" "ls" "s".

(** [BeginRun]. *)
Definition sources_begin_run_code : val :=
  λ: "res" "self", (Snd ("res" "self") +ₗ #4) <- #0.

(** [EndRun]. *)
Definition sources_end_run_code : val :=
  λ: "res" "self",
    let: "ls" := Snd ("res" "self") in
    if: ! ("ls" +ₗ #4) < ! ("ls" +ₗ #3) then sources_trim_from_code "res" "self" "ls" (! ("ls" +ₗ #4)) else #().

(** [Clear]. *)
Definition sources_clear_code : val :=
  λ: "res" "self",
    let: "ls" := Snd ("res" "self") in
    sources_trim_from_code "res" "self" "ls" #0;;
    ("ls" +ₗ #4) <- #0.

(** [new SourceList ()]: position 0 blank and an array of capacity 0. *)
Definition sources_new_code : val :=
  λ: <>,
    let: "ls" := AllocN #6 blank_slot in
    ("ls" +ₗ #1) <- "ls";; ("ls" +ₗ #2) <- #0;; ("ls" +ₗ #3) <- #0;; ("ls" +ₗ #4) <- #0;;
    ("ls" +ₗ #5) <- NONE;; "ls".

(** ** Cells and worlds *)

Lemma slot_fun_lookup sl j p : sl !! j = Some p -> slot_fun sl j = slot_val p.
Proof. intros Hj. rewrite /slot_fun Hj //. Qed.

Lemma slot_fun_ge sl j : length sl <= j -> slot_fun sl j = blank_slot.
Proof. intros Hj. rewrite /slot_fun lookup_ge_None_2 //. Qed.

Lemma rest_lookup (f : nat -> val) cap j :
  j < cap -> ((fun k => f (S k)) <$> seq 0 cap) !! j = Some (f (S j)).
Proof. intros Hj. rewrite list_lookup_fmap lookup_seq_lt //. Qed.

Lemma rest_ext (f g : nat -> val) cap :
  (forall j, j < cap -> f (S j) = g (S j)) ->
  (fun k => f (S k)) <$> seq 0 cap = (fun k => g (S k)) <$> seq 0 cap.
Proof. intros H. apply list_fmap_ext. intros i x Hx. apply lookup_seq in Hx as [-> ?]. apply H. lia. Qed.

Lemma rest_insert (f : nat -> val) cap j v :
  j < cap -> <[j := v]> ((fun k => f (S k)) <$> seq 0 cap) = (fun k => fupd f (S j) v (S k)) <$> seq 0 cap.
Proof.
  intros Hj. apply list_eq. intros i. rewrite list_lookup_fmap.
  destruct (decide (i < cap)) as [Hi|Hi].
  - rewrite lookup_seq_lt //=. rewrite /fupd. destruct (decide (i = j)) as [->|Hne].
    + rewrite list_lookup_insert_eq; [|rewrite length_fmap length_seq; lia]. rewrite decide_True //.
    + rewrite list_lookup_insert_ne // rest_lookup // decide_False //. lia.
  - rewrite lookup_seq_ge; [|lia]. rewrite lookup_ge_None_2 //. rewrite length_insert length_fmap length_seq. lia.
Qed.

Lemma lookup_dom_observers w s h x :
  observer_items (observers_of w s) !! h = Some x -> s ∈ dom (world_observers w).
Proof.
  rewrite /observers_of. destruct (world_observers w !! s) eqn:E.
  - intros _. by apply elem_of_dom_2 in E.
  - simpl. by rewrite lookup_nil.
Qed.

Lemma lookup_dom_sources w c i x : slot_at w c i = Some x -> c ∈ dom (world_sources w).
Proof.
  rewrite /slot_at /sources_of. destruct (world_sources w !! c) eqn:E.
  - intros _. by apply elem_of_dom_2 in E.
  - simpl. by rewrite lookup_nil.
Qed.

(**md
Every node with observers or sources stored in `w` is in the table's domain.
*)
Definition world_in (tbl : gmap node_id (loc * loc)) (w : EdgeWorld) : Prop :=
  dom (world_observers w) ⊆ dom tbl /\ dom (world_sources w) ⊆ dom tbl.

Lemma world_in_set_observers tbl w n o :
  n ∈ dom tbl -> world_in tbl w -> world_in tbl (set_observers n o w).
Proof. intros Hn [Ho Hs]. split; [|done]. rewrite /set_observers /= dom_insert_L. set_solver. Qed.

Lemma world_in_set_sources tbl w n l :
  n ∈ dom tbl -> world_in tbl w -> world_in tbl (set_sources n l w).
Proof. intros Hn [Ho Hs]. split; [done|]. rewrite /set_sources /= dom_insert_L. set_solver. Qed.

Lemma fst_alter_handle (sl : list (node_id * option nat)) i mh :
  fst <$> alter (fun p => (p.1, mh)) i sl = fst <$> sl.
Proof.
  apply list_eq. intros j. rewrite !list_lookup_fmap. destruct (decide (j = i)) as [->|Hne].
  - rewrite list_lookup_alter_eq. by destruct (sl !! i).
  - rewrite list_lookup_alter_ne; [done|congruence].
Qed.

Lemma length_same_fst w w' c :
  (forall j, fst <$> slot_at w' c j = fst <$> slot_at w c j) ->
  length (source_slots (sources_of w' c)) = length (source_slots (sources_of w c)).
Proof.
  intros H. rewrite -(length_fmap fst (source_slots (sources_of w' c))) -(length_fmap fst (source_slots (sources_of w c))).
  f_equal. apply list_eq. intros j. rewrite !list_lookup_fmap. apply H.
Qed.

(** A world can be incoherent and still have its trim succeed in the pure model: here observer 2 is recorded
    at slot 3, and 2 has no slots. The removal moves it, and [MovedIn] past the end leaves the world as it is. The heap
    [MovedIn] writes the cell, as F# does, so the specifications below take [edge_match]. *)
Definition moved_past_list_world : EdgeWorld :=
  set_sources 1 {| source_slots := [(5, Some 0)]; source_cursor := 1 |}
    (set_observers 5 {| observer_items := [(1, 0); (2, 3)]; observer_latest := 1 |} empty_world).

Example moved_past_list_clears :
  (fun w => (source_slots (sources_of w 1), source_slots (sources_of w 2), observer_items (observers_of w 5))) <$>
    sources_clear 1 moved_past_list_world = Some ([], [], [(2, 3)]).
Proof. vm_compute. reflexivity. Qed.

Example moved_past_list_incoherent : ~ edge_match moved_past_list_world.
Proof.
  intros Hm. specialize (Hm 2 3 5 1). vm_compute in Hm. destruct Hm as [_ H]. by specialize (H eq_refl).
Qed.

Section heap.
Context `{!heapGS Σ}.

(**md
The fields of a source list at `ls`: position `j`'s cell is `f j` for `j` up to `cap`, with `count`, `cursor`
and the `held` field `hv`.

- [ls:] the location of the source list's fields.
- [f:] the cell of each position.
- [cap:] the capacity of the array after position 0.
- [count:] the `count` field.
- [cursor:] the `cursor` field.
- [hv:] the `held` field.
*)
Definition src_rep (ls : loc) (f : nat -> val) (cap count cursor : nat) (hv : val) : iProp Σ :=
  ∃ a : loc, ls ↦ f 0 ∗ (ls +ₗ 1) ↦ #a ∗ (ls +ₗ 2) ↦ #(Z.of_nat cap) ∗ (ls +ₗ 3) ↦ #(Z.of_nat count) ∗
    (ls +ₗ 4) ↦ #(Z.of_nat cursor) ∗ (ls +ₗ 5) ↦ hv ∗ a ↦∗ ((fun k => f (S k)) <$> seq 0 cap).

(**md
The source list at `ls` holds the slots of `sl` with its cursor, and its `held` field represents `held`. Its cursor
is unconstrained, the state inside `Clear` between the trim and the cursor's reset.

- [ls:] the source list.
- [sl:] the slots and cursor represented.
- [held:] the set of sources the `held` index holds.
*)
Definition source_list_rep (ls : loc) (sl : source_list) (held : gset node_id) : iProp Σ :=
  ∃ (cap : nat) (hv : val),
    ⌜length (source_slots sl) <= S cap⌝ ∗ ⌜held_rep hv held (source_slots sl)⌝ ∗
    src_rep ls (slot_fun (source_slots sl)) cap (length (source_slots sl)) (source_cursor sl) hv.

(**md
The source list at `ls` represents `sl`, whose cursor is within its slots, with `held` the set of sources of
its index.

- [ls:] the source list.
- [sl:] the slots and cursor represented.
- [held:] the set of sources the `held` index holds, empty while it is `null`.
*)
Definition is_source_list (ls : loc) (sl : source_list) (held : gset node_id) : iProp Σ :=
  ⌜source_cursor sl <= length (source_slots sl)⌝ ∗ source_list_rep ls sl held.

(**md
`res` maps each node of `tbl` to the locations of its observer set and its source list.

- [tbl:] the node table.
- [res:] the resolver closure.
*)
Definition resolver (tbl : gmap node_id (loc * loc)) (res : val) : iProp Σ :=
  □ ∀ (n : node_id) (lo ls : loc), ⌜tbl !! n = Some (lo, ls)⌝ -∗
    WP res #(Z.of_nat n) {{ v, ⌜v = (#lo, #ls)%V⌝ }}.

Definition obs_part (tbl : gmap node_id (loc * loc)) (w : EdgeWorld) : iProp Σ :=
  [∗ map] n ↦ p ∈ tbl, is_observers p.1 (observers_of w n).

Definition src_part (tbl : gmap node_id (loc * loc)) (w : EdgeWorld) : iProp Σ :=
  [∗ map] n ↦ p ∈ tbl, ∃ held, is_source_list p.2 (sources_of w n) held.

(**md
The heap holds the edge world `w`: each node of `tbl` has its observer set and its source list at the
locations `tbl` names, and every node `w` stores is in `tbl`.

- [tbl:] the node table.
- [w:] the edge world represented.
*)
Definition is_world (tbl : gmap node_id (loc * loc)) (w : EdgeWorld) : iProp Σ :=
  ⌜world_in tbl w⌝ ∗ obs_part tbl w ∗ src_part tbl w.

Lemma src_rep_ext ls f g cap count cursor hv :
  (forall j, j < S cap -> f j = g j) ->
  src_rep ls f cap count cursor hv ⊣⊢ src_rep ls g cap count cursor hv.
Proof.
  intros Hfg. rewrite /src_rep (rest_ext f g cap); [|intros j Hj; apply Hfg; lia].
  rewrite (Hfg 0); [done|lia].
Qed.

Lemma obs_part_acc tbl w n p :
  tbl !! n = Some p ->
  obs_part tbl w ⊢ is_observers p.1 (observers_of w n) ∗
                   ∀ o, is_observers p.1 o -∗ obs_part tbl (set_observers n o w).
Proof.
  intros Hn. rewrite /obs_part (big_sepM_delete _ tbl n p) //. iIntros "[$ Hrest]" (o) "Ho".
  rewrite (big_sepM_delete _ tbl n p) //. rewrite observers_of_set_observers decide_True //. iFrame.
  iApply (big_sepM_impl with "Hrest"). iIntros "!>" (k q Hk) "H".
  apply lookup_delete_Some in Hk as [Hk _]. rewrite observers_of_set_observers decide_False //.
Qed.

Lemma src_part_acc tbl w n p :
  tbl !! n = Some p ->
  src_part tbl w ⊢ (∃ held, is_source_list p.2 (sources_of w n) held) ∗
                   ∀ l, (∃ held, is_source_list p.2 l held) -∗ src_part tbl (set_sources n l w).
Proof.
  intros Hn. rewrite /src_part (big_sepM_delete _ tbl n p) //. iIntros "[$ Hrest]" (l) "Hl".
  rewrite (big_sepM_delete _ tbl n p) //. rewrite sources_of_set_sources decide_True //. iFrame.
  iApply (big_sepM_impl with "Hrest"). iIntros "!>" (k q Hk) "H".
  apply lookup_delete_Some in Hk as [Hk _]. rewrite sources_of_set_sources decide_False //.
Qed.

Lemma src_part_split tbl w n p :
  tbl !! n = Some p ->
  src_part tbl w ⊣⊢ (∃ held, is_source_list p.2 (sources_of w n) held) ∗ src_part (delete n tbl) w.
Proof. intros Hn. rewrite /src_part (big_sepM_delete _ tbl n p) //. Qed.

Lemma src_part_delete_set tbl w n l :
  src_part (delete n tbl) (set_sources n l w) ⊣⊢ src_part (delete n tbl) w.
Proof.
  rewrite /src_part. apply big_sepM_proper. intros k q Hk.
  apply lookup_delete_Some in Hk as [Hk _]. rewrite sources_of_set_sources decide_False //.
Qed.

Lemma obs_part_conv tbl w1 w2 :
  (forall n, observers_of w1 n = observers_of w2 n) -> obs_part tbl w1 ⊢ obs_part tbl w2.
Proof. intros H. rewrite /obs_part. apply big_sepM_mono. intros n p _. by rewrite H. Qed.

Lemma src_part_delete_conv tbl c w1 w2 :
  (forall n, n ≠ c -> sources_of w1 n = sources_of w2 n) ->
  src_part (delete c tbl) w1 ⊢ src_part (delete c tbl) w2.
Proof.
  intros H. rewrite /src_part. apply big_sepM_mono. intros n p Hn.
  apply lookup_delete_Some in Hn as [Hn _]. by rewrite H.
Qed.

(** ** Cell access *)

(**md
**Reading a cell returns the cell of its position.**

- [ls:] the source list.
- [f:] the cells.
- [cap:] the capacity.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field.
- [i:] the position read, at most `cap`.
*)
Lemma sources_cell_spec (ls : loc) f cap count cursor hv (i : nat) :
  i < S cap ->
  {{{ src_rep ls f cap count cursor hv }}} sources_cell_code #ls #(Z.of_nat i)
  {{{ RET f i; src_rep ls f cap count cursor hv }}}.
Proof.
  intros Hi. iIntros (Φ) "(%a & H0 & H1 & H2 & H3 & H4 & H5 & Ha) HΦ". wp_lam. wp_pures.
  destruct i as [|j].
  - wp_pures. wp_load. iModIntro. iApply "HΦ". iExists a. iFrame.
  - rewrite bool_decide_eq_false_2; [|intros [= ?]; lia]. wp_pures. wp_load. wp_pures.
    rewrite (_ : (Z.of_nat (S j) - 1)%Z = Z.of_nat j); [|lia].
    wp_apply (wp_load_offset with "Ha"); [apply rest_lookup; lia|]. iIntros "Ha".
    iApply "HΦ". iExists a. iFrame.
Qed.

(**md
**Writing a cell replaces the cell of its position.**

- [ls:] the source list.
- [f:] the cells before the write.
- [cap:] the capacity.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field.
- [i:] the position written, at most `cap`.
- [v:] the cell written.
*)
Lemma sources_set_cell_spec (ls : loc) f cap count cursor hv (i : nat) (v : val) :
  i < S cap ->
  {{{ src_rep ls f cap count cursor hv }}} sources_set_cell_code #ls #(Z.of_nat i) v
  {{{ RET #(); src_rep ls (fupd f i v) cap count cursor hv }}}.
Proof.
  intros Hi. iIntros (Φ) "(%a & H0 & H1 & H2 & H3 & H4 & H5 & Ha) HΦ". wp_lam. wp_pures.
  destruct i as [|j].
  - wp_pures. wp_store. iModIntro. iApply "HΦ". iExists a.
    rewrite (_ : fupd f 0 v 0 = v); [|rewrite /fupd; case_decide; congruence].
    rewrite (rest_ext (fupd f 0 v) f); [iFrame|]. intros k _. rewrite /fupd; case_decide; congruence.
  - rewrite bool_decide_eq_false_2; [|intros [= ?]; lia]. wp_pures. wp_load. wp_pures.
    rewrite (_ : (Z.of_nat (S j) - 1)%Z = Z.of_nat j); [|lia].
    wp_apply (wp_store_offset with "Ha"); [apply lookup_lt_is_Some; rewrite length_fmap length_seq; lia|].
    iIntros "Ha". iApply "HΦ". iExists a. rewrite rest_insert; [|lia].
    rewrite (_ : fupd f (S j) v 0 = f 0); [|rewrite /fupd; case_decide; congruence]. iFrame.
Qed.

(**md
**`MovedIn` gives a position its new handle.**

- [ls:] the source list.
- [f:] the cells before the call.
- [cap:] the capacity.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field.
- [i:] the position, at most `cap`.
- [v1:] the source of the position's cell.
- [v2:] the handle it replaces.
- [h:] the new handle.
*)
Lemma sources_moved_in_rep_spec (ls : loc) f cap count cursor hv (i : nat) v1 v2 (h : nat) :
  i < S cap -> f i = PairV v1 v2 ->
  {{{ src_rep ls f cap count cursor hv }}} sources_moved_in_code #ls #(Z.of_nat i) #(Z.of_nat h)
  {{{ RET #(); src_rep ls (fupd f i (v1, #(Z.of_nat h))%V) cap count cursor hv }}}.
Proof.
  intros Hi Hf. iIntros (Φ) "H HΦ". wp_lam. wp_pures.
  wp_apply (sources_cell_spec with "H"); [done|]. iIntros "H". rewrite Hf. wp_pures.
  wp_apply (sources_set_cell_spec with "H"); [done|]. done.
Qed.

(**md
**`MovedIn` sets the handle of a slot.**

The slots after it are `set_handle`'s, and the cursor and index are unchanged.

- [ls:] the source list.
- [sl:] the list before the call.
- [held:] its index.
- [slot:] the slot, within the list.
- [h:] the new handle.
*)
Lemma sources_moved_in_spec (ls : loc) (sl : source_list) held (slot h : nat) :
  slot < length (source_slots sl) ->
  {{{ is_source_list ls sl held }}} sources_moved_in_code #ls #(Z.of_nat slot) #(Z.of_nat h)
  {{{ RET #(); is_source_list ls {| source_slots := alter (fun p => (p.1, Some h)) slot (source_slots sl);
                                    source_cursor := source_cursor sl |} held }}}.
Proof.
  intros Hs. iIntros (Φ) "(%Hc & %cap & %hv & %Hcap & %Hheld & H) HΦ".
  destruct (lookup_lt_is_Some_2 (source_slots sl) slot Hs) as [[src mh] Hp].
  wp_apply (sources_moved_in_rep_spec with "H"); [lia|by rewrite (slot_fun_lookup _ _ _ Hp)|].
  iIntros "H". iApply "HΦ". rewrite /is_source_list /source_list_rep /= length_alter.
  iSplit; [done|]. iExists cap, hv. iSplit; [done|]. iSplit.
  { iPureIntro. rewrite /held_rep length_alter fst_alter_handle //. }
  rewrite (src_rep_ext _ _ (slot_fun (alter (fun p => (p.1, Some h)) slot (source_slots sl)))); [done|].
  intros j _. rewrite /fupd /slot_fun. destruct (decide (j = slot)) as [->|Hne].
  - rewrite list_lookup_alter_eq Hp /=. done.
  - rewrite list_lookup_alter_ne; [done|congruence].
Qed.

(**md
**`grow` moves the later positions into a larger array.**

The cells past the capacity have to be blank, as they are past the count.

- [ls:] the source list.
- [f:] the cells.
- [cap:] the capacity before the call.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field.
*)
Lemma sources_grow_spec (ls : loc) f cap count cursor hv :
  (forall j, S cap <= j -> f j = blank_slot) ->
  {{{ src_rep ls f cap count cursor hv }}} sources_grow_code #ls
  {{{ (cap' : nat), RET #(); ⌜cap < cap'⌝ ∗ src_rep ls f cap' count cursor hv }}}.
Proof.
  intros Hb. iIntros (Φ) "(%a & H0 & H1 & H2 & H3 & H4 & H5 & Ha) HΦ". wp_lam. wp_load. wp_pures.
  set (size := max 4 (cap * 2)). assert (Hsz : cap < size /\ 4 <= size) by (subst size; lia).
  wp_bind (If _ _ _). iApply (wp_wand _ _ _ (fun v => ⌜v = #(Z.of_nat size)⌝)%I).
  { case_bool_decide; wp_pures; iPureIntro; subst size; f_equal; f_equal; lia. }
  iIntros (v ->). wp_pures. wp_alloc b as "Hb"; [lia|]. rewrite Nat2Z.id. wp_pures. wp_load.
  rewrite (_ : replicate size blank_slot = replicate cap blank_slot ++ replicate (size - cap) blank_slot);
    [|rewrite -replicate_add; f_equal; lia].
  iDestruct (array_app with "Hb") as "[Hb1 Hb2]".
  wp_apply (wp_array_copy_to with "[$Hb1 $Ha]"); [rewrite length_replicate //|rewrite length_fmap length_seq //|].
  iIntros "[Hb1 _]". wp_store. wp_store. iModIntro. iApply ("HΦ" $! size). iSplit; [iPureIntro; lia|].
  iExists b. iFrame.
  rewrite (_ : seq 0 size = seq 0 cap ++ seq cap (size - cap)); [|rewrite -seq_app; f_equal; lia].
  rewrite fmap_app. iApply array_app. rewrite length_fmap length_seq length_replicate. iFrame.
  rewrite (_ : (fun k => f (S k)) <$> seq cap (size - cap) = replicate (size - cap) blank_slot); [done|].
  apply list_eq. intros i. rewrite list_lookup_fmap. destruct (decide (i < size - cap)).
  - rewrite lookup_seq_lt //= lookup_replicate_2 //= Hb //. lia.
  - rewrite lookup_seq_ge; [|lia]. symmetry. apply lookup_replicate_None. lia.
Qed.

(** ** The [held] index *)

(**md
**`heldBefore` answers whether a source from position `i` up to `n` is `s`.**

- [ls:] the source list.
- [f:] the cells, holding the sources `srcs` below `n`.
- [cap:] the capacity.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field.
- [srcs:] the sources scanned.
- [n:] the bound, the length of `srcs`.
- [s:] the source looked for.
- [i:] the first position scanned.
*)
Lemma sources_held_before_spec (ls : loc) f cap count cursor hv (srcs : list node_id) (n s i : nat) :
  n = length srcs -> n <= S cap ->
  (forall j x, srcs !! j = Some x -> exists v, f j = (#(Z.of_nat x), v)%V) ->
  {{{ src_rep ls f cap count cursor hv }}}
    sources_held_before_code #ls #(Z.of_nat i) #(Z.of_nat n) #(Z.of_nat s)
  {{{ RET #(bool_decide (s ∈ drop i srcs)); src_rep ls f cap count cursor hv }}}.
Proof.
  intros -> Hcap Hf. iIntros (Φ) "H HΦ". iLöb as "IH" forall (i).
  wp_rec. wp_pures. destruct (decide (i < length srcs)) as [Hi|Hi].
  - rewrite (bool_decide_eq_true_2 (Z.of_nat i < Z.of_nat (length srcs))%Z); [|lia]. wp_pures.
    destruct (lookup_lt_is_Some_2 srcs i Hi) as [x Hx]. destruct (Hf i x Hx) as [v Hv].
    wp_apply (sources_cell_spec with "H"); [lia|]. iIntros "H". rewrite Hv. wp_pures.
    rewrite (drop_S _ _ _ Hx).
    destruct (decide (x = s)) as [->|Hne].
    + rewrite (bool_decide_eq_true_2 (#(Z.of_nat s) = #(Z.of_nat s))) //. wp_pures. iModIntro.
      rewrite bool_decide_eq_true_2; [by iApply "HΦ"|]. apply elem_of_cons. by left.
    + rewrite (bool_decide_eq_false_2 (#(Z.of_nat x) = #(Z.of_nat s))); [|intros [= ?]; lia]. wp_pures.
      rewrite (_ : (Z.of_nat i + 1)%Z = Z.of_nat (S i)); [|lia].
      iApply ("IH" with "H"). iNext.
      rewrite (bool_decide_ext (s ∈ x :: _) (s ∈ drop (S i) srcs)); [done|].
      rewrite elem_of_cons. split; [|by right]. intros [->|]; [done|done].
  - rewrite (bool_decide_eq_false_2 (Z.of_nat i < Z.of_nat (length srcs))%Z); [|lia]. wp_pures.
    iModIntro. rewrite drop_ge; [|lia]. by iApply "HΦ".
Qed.

(**md
**Building the index prepends the sources of the positions below `i`.**

- [ls:] the source list.
- [f:] the cells, holding the sources `srcs`.
- [cap:] the capacity.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field.
- [srcs:] the sources of the list.
- [i:] the number of positions taken.
- [acc:] the list prepended to.
*)
Lemma held_build_spec (ls : loc) f cap count cursor hv (srcs : list node_id) (i : nat) (acc : list node_id) :
  i <= length srcs -> length srcs <= S cap ->
  (forall j x, srcs !! j = Some x -> exists v, f j = (#(Z.of_nat x), v)%V) ->
  {{{ src_rep ls f cap count cursor hv }}} held_build_code #ls #(Z.of_nat i) (list_val acc)
  {{{ RET list_val (take i srcs ++ acc); src_rep ls f cap count cursor hv }}}.
Proof.
  intros Hi Hcap Hf. iIntros (Φ) "H HΦ". iLöb as "IH" forall (i acc Hi).
  wp_rec. wp_pures. destruct i as [|j].
  - wp_pures. iModIntro. by iApply "HΦ".
  - rewrite bool_decide_eq_false_2; [|intros [= ?]; lia]. wp_pures.
    rewrite (_ : (Z.of_nat (S j) - 1)%Z = Z.of_nat j); [|lia].
    destruct (lookup_lt_is_Some_2 srcs j ltac:(lia)) as [x Hx]. destruct (Hf j x Hx) as [v Hv].
    wp_apply (sources_cell_spec with "H"); [lia|]. iIntros "H". rewrite Hv. wp_pures.
    iApply ("IH" $! j (x :: acc) with "[%] H"); [lia|]. iNext.
    rewrite (take_S_r _ _ _ Hx) -app_assoc. done.
Qed.

(**md
**Membership in the index is membership in its list.**

- [xs:] the list.
- [s:] the source looked for.
*)
Lemma held_mem_spec (xs : list node_id) (s : nat) :
  {{{ True }}} held_mem_code (list_val xs) #(Z.of_nat s) {{{ RET #(bool_decide (s ∈ xs)); True }}}.
Proof.
  induction xs as [|x xs IH]; iIntros (Φ) "_ HΦ"; wp_rec; wp_pures.
  - iModIntro. by iApply "HΦ".
  - destruct (decide (x = s)) as [->|Hne].
    + rewrite (bool_decide_eq_true_2 (#(Z.of_nat s) = #(Z.of_nat s))) //. wp_pures. iModIntro.
      rewrite bool_decide_eq_true_2; [by iApply "HΦ"|]. apply elem_of_cons. by left.
    + rewrite (bool_decide_eq_false_2 (#(Z.of_nat x) = #(Z.of_nat s))); [|intros [= ?]; lia]. wp_pures.
      wp_apply IH; [done|]. iIntros "_".
      rewrite (bool_decide_ext (s ∈ xs) (s ∈ x :: xs)); [by iApply "HΦ"|].
      rewrite elem_of_cons. split; [by right|]. intros [->|]; [done|done].
Qed.

(**md
**`holds` decides whether a source has a slot before `until`.**

At most `scan_limit` slots are scanned. Above that, the `held` index is built from the slots when it is `null`,
and answers. The index the call leaves represents the same slots.

- [ls:] the source list.
- [f:] the cells, which hold the slots of `sl` at the positions below its length.
- [cap:] the capacity.
- [count:] the count.
- [cursor:] the cursor.
- [hv:] the `held` field before the call.
- [held:] the index it represents.
- [sl:] the slots before `until`, as many as `until`.
- [s:] the source looked for.
*)
Lemma sources_holds_spec (ls : loc) f cap count cursor hv held (sl : list (node_id * option nat)) (s : nat) :
  length sl <= S cap -> held_rep hv held sl ->
  (forall j p, sl !! j = Some p -> f j = slot_val p) ->
  {{{ src_rep ls f cap count cursor hv }}}
    sources_holds_code #ls #(Z.of_nat (length sl)) #(Z.of_nat s)
  {{{ hv' held', RET #(bool_decide (s ∈ fst <$> sl)); ⌜held_rep hv' held' sl⌝ ∗
      src_rep ls f cap count cursor hv' }}}.
Proof.
  intros Hcap Hheld Hf. iIntros (Φ) "H HΦ".
  assert (Hf' : forall j x, (fst <$> sl) !! j = Some x -> exists v, f j = (#(Z.of_nat x), v)%V).
  { intros j x Hj. rewrite list_lookup_fmap in Hj.
    destruct (sl !! j) as [[y mh]|] eqn:E; simplify_eq/=. exists (handle_val mh). by rewrite (Hf _ _ E). }
  wp_lam. wp_pures. destruct (decide (length sl <= scan_limit)) as [Hs|Hs].
  - rewrite (bool_decide_eq_true_2 (Z.of_nat (length sl) ≤ Z.of_nat scan_limit)%Z); [|lia]. wp_pures.
    wp_apply (sources_held_before_spec _ _ _ _ _ _ (fst <$> sl) (length sl) s 0 with "H");
      [by rewrite length_fmap|lia|done|].
    iIntros "H". rewrite drop_0. iApply "HΦ". by iFrame.
  - rewrite (bool_decide_eq_false_2 (Z.of_nat (length sl) ≤ Z.of_nat scan_limit)%Z); [|lia]. wp_pures.
    destruct Hheld as [[-> ->]|(Hlong & Hheq & xs & -> & Hxs)].
    + iDestruct "H" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_load. wp_pures.
      iAssert (src_rep ls f cap count cursor NONEV) with "[H0 H1 H2 H3 H4 H5 Ha]" as "H"; [iExists a; iFrame|].
      wp_apply (held_build_spec _ _ _ _ _ _ (fst <$> sl) (length sl) [] with "H");
        [rewrite length_fmap //|rewrite length_fmap; lia|done|].
      iIntros "H". rewrite app_nil_r take_ge; [|rewrite length_fmap //].
      iDestruct "H" as (a') "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. wp_load. wp_pures.
      wp_apply held_mem_spec; [done|]. iIntros "_".
      iApply ("HΦ" $! (SOMEV (list_val (fst <$> sl))) (list_to_set (fst <$> sl))). iSplit.
      * iPureIntro. right. split_and!; [lia|done|]. by exists (fst <$> sl).
      * iExists a'. iFrame.
    + iDestruct "H" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_load. wp_pures. wp_load. wp_pures.
      wp_apply held_mem_spec; [done|]. iIntros "_".
      rewrite (bool_decide_ext (s ∈ xs) (s ∈ fst <$> sl)).
      * iApply ("HΦ" $! (SOMEV (list_val xs)) held). iSplit.
        -- iPureIntro. right. split_and!; [lia|done|]. by exists xs.
        -- iExists a. iFrame.
      * rewrite -(elem_of_list_to_set (C := gset node_id) s xs) -(elem_of_list_to_set (C := gset node_id) s (fst <$> sl)).
        rewrite Hxs Hheq. done.
Qed.

(** ** Trimming *)

(** The cells of a trim that has blanked the positions from [from] up to [i]. *)
Definition trim_fun (sl : list (node_id * option nat)) (from i j : nat) : val :=
  if decide (from <= j < i) then blank_slot else slot_fun sl j.

Lemma trim_step_blank (f : nat -> val) sl sl' from i cap :
  from <= i ->
  (forall j, j < S cap -> f j = trim_fun sl from i j) ->
  (forall k, k ≠ i -> sl' !! k = sl !! k) ->
  forall j, j < S cap -> fupd f i blank_slot j = trim_fun sl' from (S i) j.
Proof.
  intros Hfi Hf H j Hj. rewrite /fupd. destruct (decide (j = i)) as [->|Hne].
  - rewrite /trim_fun decide_True //. lia.
  - rewrite Hf // /trim_fun /slot_fun (H j Hne).
    destruct (decide (from <= j < i)), (decide (from <= j < S i)); try done; lia.
Qed.

Lemma trim_step_moved (f : nat -> val) sl sl' from i ms v cap :
  from <= i ->
  (forall j, j < S cap -> f j = trim_fun sl from i j) ->
  ms ≠ i -> ~ (from <= ms < i) -> slot_fun sl' ms = v ->
  (forall k, k ≠ i -> k ≠ ms -> sl' !! k = sl !! k) ->
  forall j, j < S cap -> fupd (fupd f ms v) i blank_slot j = trim_fun sl' from (S i) j.
Proof.
  intros Hfi Hf Hmi Hms Hv H j Hj. rewrite /fupd. destruct (decide (j = i)) as [->|Hne].
  - rewrite /trim_fun decide_True //. lia.
  - destruct (decide (j = ms)) as [->|Hjm].
    + rewrite /trim_fun decide_False; [done|lia].
    + rewrite Hf // /trim_fun /slot_fun (H j Hne Hjm).
      destruct (decide (from <= j < i)), (decide (from <= j < S i)); try done; lia.
Qed.

Lemma slots_set_handle_self c i mh w :
  source_slots (sources_of (set_handle c i mh w) c) = alter (fun p => (p.1, mh)) i (source_slots (sources_of w c)).
Proof. rewrite /set_handle sources_of_set_sources decide_True //. Qed.

Lemma sources_set_handle_other c c' i mh w :
  c' ≠ c -> sources_of (set_handle c i mh w) c' = sources_of w c'.
Proof. intros Hne. rewrite /set_handle sources_of_set_sources decide_False //. Qed.

Lemma trim_fun_done sl from n j :
  length sl = n -> from <= n -> trim_fun sl from n j = slot_fun (take from sl) j.
Proof.
  intros Hl Hf. rewrite /trim_fun /slot_fun. destruct (decide (from <= j < n)).
  - rewrite lookup_take_ge //. lia.
  - destruct (decide (j < from)).
    + rewrite lookup_take_lt //.
    + rewrite lookup_take_ge; [|lia]. rewrite lookup_ge_None_2 //. lia.
Qed.

Lemma trim_fun_start sl from j : trim_fun sl from from j = slot_fun sl j.
Proof. rewrite /trim_fun decide_False //. lia. Qed.

(**md
**The loop of `trimFrom` removes slot by slot, as `trim_loop` does.**

The positions from `from` up to `i` are blank, and their slots in `w` are unlinked. Each later position's handle is read
when the loop reaches it, its edge is removed with the `MovedIn` of the moved observer delivered, and the
position is blanked.

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation trimmed.
- [lo:] its observer set.
- [ls:] its source list.
- [from:] the first position blanked.
- [i:] the next position.
- [n:] the count, the number of slots.
- [cap:] the capacity.
- [cursor:] the cursor.
- [f:] the cells.
- [w:] the world at position `i`.
*)
Lemma sources_trim_loop_spec (res : val) tbl (self : node_id) (lo ls : loc) (from i n cap cursor : nat) f
    (w : EdgeWorld) :
  tbl !! self = Some (lo, ls) -> edge_match w -> world_in tbl w ->
  length (source_slots (sources_of w self)) = n -> from <= i <= n -> n <= S cap ->
  (forall j, from <= j < i -> ~ owns w self j) ->
  (forall j, j < S cap -> f j = trim_fun (source_slots (sources_of w self)) from i j) ->
  {{{ resolver tbl res ∗ obs_part tbl w ∗ src_part (delete self tbl) w ∗ src_rep ls f cap n cursor NONEV }}}
    sources_trim_loop res #(Z.of_nat self) #ls #(Z.of_nat i) #(Z.of_nat n)
  {{{ w' f', RET #(); ⌜trim_loop self i (n - i) w = Some w'⌝ ∗ ⌜edge_match w'⌝ ∗ ⌜world_in tbl w'⌝ ∗
      ⌜length (source_slots (sources_of w' self)) = n⌝ ∗
      ⌜source_cursor (sources_of w' self) = source_cursor (sources_of w self)⌝ ∗
      ⌜forall j, j < S cap -> f' j = trim_fun (source_slots (sources_of w' self)) from n j⌝ ∗
      obs_part tbl w' ∗ src_part (delete self tbl) w' ∗ src_rep ls f' cap n cursor NONEV }}}.
Proof.
  intros Hself Hm Hin Hlen Hi Hcap Hno Hf.
  remember (n - i) as k eqn:Hk. revert i f w Hm Hin Hlen Hi Hno Hf Hk.
  induction k as [|k IH]; intros i f w Hm Hin Hlen Hi Hno Hf Hk;
    iIntros (Φ) "(#Hres & Hobs & Hsrc & Hrep) HΦ"; wp_rec; wp_pures.
  { assert (i = n) as -> by lia. rewrite (bool_decide_eq_false_2 (Z.of_nat n < Z.of_nat n)%Z); [|lia].
    wp_pures. iModIntro. iApply ("HΦ" $! w f). iFrame. iPureIntro. done. }
  rewrite (bool_decide_eq_true_2 (Z.of_nat i < Z.of_nat n)%Z); [|lia]. wp_pures.
  set (sl := source_slots (sources_of w self)) in *.
  destruct (lookup_lt_is_Some_2 sl i ltac:(lia)) as [[s mh] Hsi].
  wp_apply (sources_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
  rewrite (_ : f i = (#(Z.of_nat s), handle_val mh)%V);
    [|rewrite Hf; [|lia]; rewrite /trim_fun decide_False; [|lia]; by rewrite (slot_fun_lookup _ _ _ Hsi)].
  wp_pures.
  destruct (remove_slot_ok self i w Hm) as (w' & Hrs & Hm' & Htr).
  pose proof Htr as (Hfst & Hown & Hnone & Hcur).
  assert (Hlen' : length (source_slots (sources_of w' self)) = n) by (rewrite (length_same_fst w w') //).
  assert (Hno' : forall j, from <= j < S i -> ~ owns w' self j).
  { intros j Hj. destruct (decide (j = i)) as [->|Hne]; [apply Hnone; lia|].
    rewrite (Hown self j); [apply Hno; lia|]. right; left; lia. }
  assert (Htl : trim_loop self i (n - i) w = sources_remove_slot self i w ≫= trim_loop self (S i) (n - S i)).
  { rewrite (_ : n - i = S (n - S i)); [done|lia]. }
  assert (Hselfd : self ∈ dom tbl) by (by apply elem_of_dom_2 in Hself).
  assert (Hslot : slot_at w self i = Some (s, mh)) by done.
  destruct mh as [h|]; simpl.
  - (* The slot holds an edge. *)
    wp_pures. rewrite (bool_decide_eq_true_2 (0 ≤ Z.of_nat h)%Z); [|lia]. wp_pures.
    pose proof (proj1 (Hm _ _ _ _) Hslot) as Hob.
    assert (Hsd : s ∈ dom tbl) by (apply (proj1 Hin); eapply lookup_dom_observers; eauto).
    pose proof Hsd as [[los lss] Hst]%elem_of_dom.
    wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|]. iIntros (v ->). wp_pures.
    destruct (observers_remove_at_ok _ _ _ _ Hob) as [[o' nt] Hr].
    iDestruct (obs_part_acc _ _ s (los, lss) Hst with "Hobs") as "[Ho Hobs]".
    wp_apply (observers_remove_at_spec with "Ho"); [exact Hr|]. iIntros "Ho".
    iSpecialize ("Hobs" with "Ho").
    destruct (Pure.ObserverSet.observers_remove_at_spec _ _ _ _ _ Hr) as (_ & _ & _ & Hnt).
    pose proof Hrs as Hrs2. rewrite /sources_remove_slot /remove_edge Hslot Hr /= in Hrs2.
    destruct nt as [[[m ms] h']|].
    + destruct Hnt as [[_ ?]|(m' & ms' & Hhl & Hlast & Heq & _)]; [done|]. injection Heq as <- <- ->.
      injection Hrs2 as <-.
      pose proof (proj2 (Hm _ _ _ _) Hlast) as Hmslot.
      assert (Hmd : m ∈ dom tbl) by (apply (proj2 Hin); eapply lookup_dom_sources; eauto).
      pose proof Hmd as [[lom lsm] Hmt]%elem_of_dom.
      wp_pures. wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|].
      iIntros (v ->). wp_pures.
      destruct (decide (m = self)) as [->|Hmself].
      * rewrite Hself in Hmt. injection Hmt as <- <-.
        assert (Hms : sl !! ms = Some (s, Some (pred (length (observer_items (observers_of w s)))))) by exact Hmslot.
        assert (Hmsn : ms < n) by (apply lookup_lt_Some in Hms; lia).
        assert (Hmsi : ms ≠ i) by (intros ->; rewrite Hsi in Hms; injection Hms as ?; lia).
        assert (Hmsf : ~ (from <= ms < i)) by (intros Hr'; apply (Hno ms Hr'); eexists _, _; exact Hms).
        wp_apply (sources_moved_in_rep_spec with "Hrep"); [lia| |].
        { rewrite Hf; [|lia]. rewrite /trim_fun decide_False //. by rewrite (slot_fun_lookup _ _ _ Hms). }
        iIntros "Hrep". wp_pures. wp_apply (sources_set_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
        wp_pures. rewrite (_ : (Z.of_nat i + 1)%Z = Z.of_nat (S i)); [|lia].
        set (W2 := sources_moved_in self ms h (set_observers s o' w)) in *.
        assert (Hin' : world_in tbl (set_handle self i None W2)).
        { apply world_in_set_sources; [done|]. apply world_in_set_sources; [done|].
          apply world_in_set_observers; [by apply elem_of_dom_2 in Hst|done]. }
        assert (Hf' : forall j, j < S cap ->
                  fupd (fupd f ms (#(Z.of_nat s), #(Z.of_nat h))%V) i blank_slot j =
                  trim_fun (source_slots (sources_of (set_handle self i None W2) self)) from (S i) j).
        { apply (trim_step_moved f sl); [lia|done|done|done| |].
          - rewrite /slot_fun slots_set_handle_self list_lookup_alter_ne; [|done].
            rewrite /W2 /sources_moved_in slots_set_handle_self list_lookup_alter_eq.
            change (source_slots (sources_of (set_observers s o' w) self)) with sl. rewrite Hms. done.
          - intros q Hq1 Hq2. rewrite slots_set_handle_self list_lookup_alter_ne; [|done].
            rewrite /W2 /sources_moved_in slots_set_handle_self list_lookup_alter_ne; [|done]. done. }
        assert (Hco : forall q, observers_of (set_observers s o' w) q = observers_of (set_handle self i None W2) q)
          by (intros q; done).
        assert (Hcs : forall q, q ≠ self -> sources_of w q = sources_of (set_handle self i None W2) q).
        { intros q Hq. rewrite sources_set_handle_other // /W2 /sources_moved_in sources_set_handle_other //. }
        iDestruct (obs_part_conv _ _ _ Hco with "Hobs") as "Hobs".
        iDestruct (src_part_delete_conv _ _ _ _ Hcs with "Hsrc") as "Hsrc".
        wp_apply (IH (S i) _ _ Hm' Hin' Hlen' ltac:(lia) Hno' Hf' ltac:(lia) with "[$Hres $Hobs $Hsrc $Hrep]").
        iIntros (wf f') "(%Htf & %Hmf & %Hinf & %Hlenf & %Hcurf & %Hff & Hobs & Hsrc & Hrep)".
        iApply ("HΦ" $! wf f'). iFrame. iPureIntro.
        split_and!; [|done|done|done| |done].
        { rewrite Hrs /=. exact Htf. }
        { rewrite Hcurf. apply Hcur. }
      * iDestruct (src_part_acc (delete self tbl) _ m (lom, lsm) with "Hsrc") as "[[%held Hl] Hsrc]";
          [rewrite lookup_delete_ne; [done|congruence]|].
        wp_apply (sources_moved_in_spec with "Hl"); [by apply lookup_lt_Some in Hmslot|].
        iIntros "Hl". iSpecialize ("Hsrc" with "[Hl]"); [iExists held; iFrame|].
        wp_pures. wp_apply (sources_set_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
        wp_pures. rewrite (_ : (Z.of_nat i + 1)%Z = Z.of_nat (S i)); [|lia].
        set (W2 := sources_moved_in m ms h (set_observers s o' w)) in *.
        assert (Hin' : world_in tbl (set_handle self i None W2)).
        { apply world_in_set_sources; [done|]. apply world_in_set_sources; [by apply elem_of_dom_2 in Hmt|].
          apply world_in_set_observers; [by apply elem_of_dom_2 in Hst|done]. }
        assert (Hf' : forall j, j < S cap -> fupd f i blank_slot j =
                  trim_fun (source_slots (sources_of (set_handle self i None W2) self)) from (S i) j).
        { apply (trim_step_blank f sl); [lia|done|]. intros q Hq.
          rewrite slots_set_handle_self list_lookup_alter_ne; [|done].
          rewrite /W2 /sources_moved_in sources_set_handle_other //. }
        assert (Hco : forall q, observers_of (set_observers s o' w) q = observers_of (set_handle self i None W2) q)
          by (intros q; done).
        assert (Hcs : forall q, q ≠ self ->
                  sources_of (set_sources m {| source_slots := alter (fun p => (p.1, Some h)) ms
                                                 (source_slots (sources_of w m));
                                               source_cursor := source_cursor (sources_of w m) |} w) q =
                  sources_of (set_handle self i None W2) q).
        { intros q Hq. rewrite sources_set_handle_other //. }
        iDestruct (obs_part_conv _ _ _ Hco with "Hobs") as "Hobs".
        iDestruct (src_part_delete_conv _ _ _ _ Hcs with "Hsrc") as "Hsrc".
        wp_apply (IH (S i) _ _ Hm' Hin' Hlen' ltac:(lia) Hno' Hf' ltac:(lia) with "[$Hres $Hobs $Hsrc $Hrep]").
        iIntros (wf f') "(%Htf & %Hmf & %Hinf & %Hlenf & %Hcurf & %Hff & Hobs & Hsrc & Hrep)".
        iApply ("HΦ" $! wf f'). iFrame. iPureIntro.
        split_and!; first [done | (rewrite Hrs /=; exact Htf) | (rewrite Hcurf; apply Hcur)].
    + injection Hrs2 as <-.
      wp_pures. wp_apply (sources_set_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
      wp_pures. rewrite (_ : (Z.of_nat i + 1)%Z = Z.of_nat (S i)); [|lia].
      assert (Hin' : world_in tbl (set_handle self i None (set_observers s o' w))).
      { apply world_in_set_sources; [done|]. apply world_in_set_observers; [by apply elem_of_dom_2 in Hst|done]. }
      assert (Hf' : forall j, j < S cap -> fupd f i blank_slot j =
                trim_fun (source_slots (sources_of (set_handle self i None (set_observers s o' w)) self)) from (S i) j).
      { apply (trim_step_blank f sl); [lia|done|]. intros q Hq. rewrite slots_set_handle_self list_lookup_alter_ne //. }
      assert (Hco : forall q, observers_of (set_observers s o' w) q =
                              observers_of (set_handle self i None (set_observers s o' w)) q)
        by (intros q; done).
      assert (Hcs : forall q, q ≠ self -> sources_of w q = sources_of (set_handle self i None (set_observers s o' w)) q).
      { intros q Hq. rewrite sources_set_handle_other //. }
      iDestruct (obs_part_conv _ _ _ Hco with "Hobs") as "Hobs".
      iDestruct (src_part_delete_conv _ _ _ _ Hcs with "Hsrc") as "Hsrc".
      wp_apply (IH (S i) _ _ Hm' Hin' Hlen' ltac:(lia) Hno' Hf' ltac:(lia) with "[$Hres $Hobs $Hsrc $Hrep]").
      iIntros (wf f') "(%Htf & %Hmf & %Hinf & %Hlenf & %Hcurf & %Hff & Hobs & Hsrc & Hrep)".
      iApply ("HΦ" $! wf f'). iFrame. iPureIntro.
      split_and!; first [done | (rewrite Hrs /=; exact Htf) | (rewrite Hcurf; apply Hcur)].
  - (* The slot holds no edge. *)
    wp_pures.
    pose proof Hrs as Hrs2. rewrite /sources_remove_slot Hslot /= in Hrs2. injection Hrs2 as <-.
    wp_apply (sources_set_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
    wp_pures. rewrite (_ : (Z.of_nat i + 1)%Z = Z.of_nat (S i)); [|lia].
    assert (Hf' : forall j, j < S cap -> fupd f i blank_slot j = trim_fun sl from (S i) j).
    { apply (trim_step_blank f sl); [lia|done|done]. }
    wp_apply (IH (S i) _ _ Hm' Hin Hlen' ltac:(lia) Hno' Hf' ltac:(lia) with "[$Hres $Hobs $Hsrc $Hrep]").
    iIntros (wf f') "(%Htf & %Hmf & %Hinf & %Hlenf & %Hcurf & %Hff & Hobs & Hsrc & Hrep)".
    iApply ("HΦ" $! wf f'). iFrame. iPureIntro.
    split_and!; first [done | (rewrite Hrs /=; exact Htf) | (rewrite Hcurf; apply Hcur)].
Qed.

(**md
**`trimFrom` removes the edges of the slots from `from` on, as `sources_trim_from` does.**

The `held` index is dropped, and the count becomes `from`. The cursor is kept.

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation trimmed.
- [lo:] its observer set.
- [ls:] its source list.
- [from:] the first slot dropped, within the list.
- [w:] the world before the trim.
- [held:] the index before the trim.
*)
Lemma sources_trim_from_spec (res : val) tbl (self : node_id) (lo ls : loc) (from : nat) (w : EdgeWorld) held :
  tbl !! self = Some (lo, ls) -> edge_match w -> world_in tbl w ->
  from <= length (source_slots (sources_of w self)) ->
  {{{ resolver tbl res ∗ obs_part tbl w ∗ src_part (delete self tbl) w ∗ source_list_rep ls (sources_of w self) held }}}
    sources_trim_from_code res #(Z.of_nat self) #ls #(Z.of_nat from)
  {{{ w', RET #(); ⌜sources_trim_from self from w = Some w'⌝ ∗ ⌜edge_match w'⌝ ∗ ⌜world_in tbl w'⌝ ∗
      ⌜length (source_slots (sources_of w' self)) = from⌝ ∗
      ⌜source_cursor (sources_of w' self) = source_cursor (sources_of w self)⌝ ∗
      obs_part tbl w' ∗ src_part (delete self tbl) w' ∗ source_list_rep ls (sources_of w' self) ∅ }}}.
Proof.
  intros Hself Hm Hin Hfrom. iIntros (Φ) "(#Hres & Hobs & Hsrc & (%cap & %hv & %Hcap & %Hheld & Hrep)) HΦ".
  set (n := length (source_slots (sources_of w self))) in *.
  wp_lam. wp_pures. iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. wp_load. wp_pures.
  iAssert (src_rep ls (slot_fun (source_slots (sources_of w self))) cap n (source_cursor (sources_of w self)) NONEV)
    with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hrep"; [iExists a; iFrame|].
  wp_apply (sources_trim_loop_spec res tbl self lo ls from from n cap (source_cursor (sources_of w self))
              (slot_fun (source_slots (sources_of w self))) w Hself Hm Hin eq_refl ltac:(lia) Hcap
              ltac:(intros j Hj; lia) ltac:(intros j _; by rewrite trim_fun_start)
              with "[$Hres $Hobs $Hsrc $Hrep]").
  iIntros (wf f') "(%Htf & %Hmf & %Hinf & %Hlenf & %Hcurf & %Hff & Hobs & Hsrc & Hrep)".
  wp_pures. iDestruct "Hrep" as (a') "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. iModIntro.
  set (w' := set_sources self {| source_slots := take from (source_slots (sources_of wf self));
                                 source_cursor := source_cursor (sources_of wf self) |} wf).
  assert (Ht : sources_trim_from self from w = Some w') by (rewrite /sources_trim_from Htf /=; done).
  destruct (sources_trim_from_ok self from w Hm) as (w'' & Ht'' & Hm'' & _).
  assert (w'' = w') as -> by congruence.
  iApply ("HΦ" $! w'). iSplit; [done|]. iSplit; [done|].
  iSplit; [iPureIntro; apply world_in_set_sources; [by apply elem_of_dom_2 in Hself|done]|].
  iSplit; [iPureIntro; rewrite /w' sources_of_set_sources decide_True //= length_take; lia|].
  iSplit; [iPureIntro; rewrite /w' sources_of_set_sources decide_True //=|].
  iSplitL "Hobs"; [done|]. iSplitL "Hsrc"; [by rewrite /w' src_part_delete_set|].
  iExists cap, NONEV. rewrite /w' sources_of_set_sources decide_True //= length_take.
  rewrite (_ : from `min` length (source_slots (sources_of wf self)) = from); [|lia].
  iSplit; [iPureIntro; lia|]. iSplit; [iPureIntro; by left|].
  rewrite -(src_rep_ext ls f'); [|intros j Hj; rewrite Hff // trim_fun_done //; lia].
  iExists a'. rewrite Hcurf. iFrame.
Qed.

Lemma snoc_cells (sl : list (node_id * option nat)) (p : node_id * option nat) v j :
  fupd (fupd (slot_fun sl) (length sl) v) (length sl) (slot_val p) j = slot_fun (sl ++ [p]) j.
Proof.
  rewrite /fupd /slot_fun. destruct (decide (j = length sl)) as [->|Hne].
  - rewrite lookup_app_r // Nat.sub_diag //.
  - destruct (decide (j < length sl)).
    + rewrite lookup_app_l //.
    + rewrite !lookup_ge_None_2 //; rewrite ?length_app /=; lia.
Qed.

Lemma append_parts tbl (self s : node_id) (mh : option nat) (o : observer_set) (w : EdgeWorld) (ls : loc) :
  obs_part tbl (set_observers s o w) ∗ src_part (delete self tbl) w ∗
  (∃ held, is_source_list ls {| source_slots := source_slots (sources_of w self) ++ [(s, mh)];
                                source_cursor := S (length (source_slots (sources_of w self))) |} held)
  ⊢ obs_part tbl (sources_append self s mh o w) ∗ src_part (delete self tbl) (sources_append self s mh o w) ∗
    ∃ held, is_source_list ls (sources_of (sources_append self s mh o w) self) held.
Proof.
  iIntros "(Hobs & Hsrc & Hl)". iSplitL "Hobs".
  { iApply (obs_part_conv with "Hobs"). intros q. rewrite /sources_append !observers_of_set_observers //. }
  iSplitL "Hsrc".
  { iApply (src_part_delete_conv with "Hsrc"). intros q Hq.
    rewrite /sources_append sources_of_set_observers sources_of_set_sources decide_False //. }
  rewrite /sources_append sources_of_set_observers sources_of_set_sources decide_True //.
Qed.

(**md
**The end of `Append` leaves the list with the new slot, its index extended when built.**

- [ls:] the source list.
- [f:] the cells, those of `sl` followed by the new slot.
- [cap:] the capacity.
- [hv:] the `held` field.
- [held:] its index over `sl`.
- [sl:] the slots before the append.
- [s:] the source appended.
- [mh:] the new slot's handle.
*)
Lemma sources_index_add_spec (ls : loc) f cap hv held (sl : list (node_id * option nat)) (s : node_id) (mh : option nat) :
  length sl < S cap -> held_rep hv held sl ->
  (forall j, j < S cap -> f j = slot_fun (sl ++ [(s, mh)]) j) ->
  {{{ src_rep ls f cap (S (length sl)) (S (length sl)) hv }}} sources_index_add_code #ls #(Z.of_nat s)
  {{{ held', RET #(); is_source_list ls {| source_slots := sl ++ [(s, mh)]; source_cursor := S (length sl) |} held' }}}.
Proof.
  intros Hcap Hheld Hf. iIntros (Φ) "Hrep HΦ". iRevert "Hrep".
  rewrite (src_rep_ext ls f (slot_fun (sl ++ [(s, mh)]))); [|exact Hf]. iIntros "Hrep".
  wp_lam. wp_pures. iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)".
  assert (Hlen : length (sl ++ [(s, mh)]) = S (length sl)) by (rewrite length_app /=; lia).
  destruct Hheld as [[-> ->]|(Hl & Hheq & xs & -> & Hxs)].
  - wp_load. wp_pures. iModIntro. iApply ("HΦ" $! ∅). rewrite /is_source_list /source_list_rep /= Hlen.
    iSplit; [done|]. iExists cap, NONEV. iSplit; [iPureIntro; lia|]. iSplit; [iPureIntro; by left|].
    iExists a. iFrame.
  - wp_load. wp_pures. wp_store. iModIntro. iApply ("HΦ" $! ({[s]} ∪ held)).
    rewrite /is_source_list /source_list_rep /= Hlen.
    iSplit; [done|]. iExists cap, (SOMEV (list_val (s :: xs))). iSplit; [iPureIntro; lia|]. iSplit.
    + iPureIntro. right. split_and!; [rewrite Hlen; lia| |].
      * rewrite fmap_app list_to_set_app_L Hheq /=. set_solver.
      * exists (s :: xs). split; [done|]. rewrite /= Hxs. done.
    + iExists a. cbn [list_val]. iFrame.
Qed.

(**md
**The rest of `Append` links the source as `sources_link` does.**

The slot appended is the count. Slot 0 adds the observer unconditionally. A later slot tries `TryAdd`, and on
`EdgeHandle.Unknown` falls back to `holds` and then `Add`. The array grows when full, and the cursor ends after
the new slot.

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation reading.
- [s:] the source read.
- [lo:] the observer set of `self`.
- [ls:] the source list of `self`.
- [w:] the world after the trim.
- [held:] the index of the list.
*)
Lemma sources_link_spec (res : val) tbl (self s : node_id) (lo ls : loc) (w : EdgeWorld) held :
  tbl !! self = Some (lo, ls) -> s ∈ dom tbl ->
  {{{ resolver tbl res ∗ obs_part tbl w ∗ src_part (delete self tbl) w ∗ source_list_rep ls (sources_of w self) held }}}
    sources_link_code res #(Z.of_nat self) #ls #(Z.of_nat s)
  {{{ RET #(); obs_part tbl (sources_link self s w) ∗ src_part (delete self tbl) (sources_link self s w) ∗
      ∃ held', is_source_list ls (sources_of (sources_link self s w) self) held' }}}.
Proof.
  intros Hself Hs. iIntros (Φ) "(#Hres & Hobs & Hsrc & (%cap & %hv & %Hcap & %Hheld & Hrep)) HΦ".
  pose proof Hs as [[los lss] Hst]%elem_of_dom.
  set (sl := source_slots (sources_of w self)) in *.
  set (o := observers_of w s) in *.
  wp_lam. wp_pures. iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_load. wp_pures.
  wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|]. iIntros (v ->). wp_pures.
  iDestruct (obs_part_acc _ _ s (los, lss) Hst with "Hobs") as "[Ho Hobs]".
  destruct (decide (length sl = 0)) as [H0|H0].
  - (* Slot 0. *)
    assert (Hsl : sl = []) by (by apply nil_length_inv).
    assert (Hlink : sources_link self s w =
                    sources_append self s (Some (observers_add self 0 o).2) (observers_add self 0 o).1 w).
    { rewrite /sources_link. cbv zeta. rewrite -/sl -/o H0. case_decide; [done|congruence]. }
    rewrite Hlink H0. wp_pures. wp_store. wp_store. wp_store. wp_pures.
    wp_apply (observers_add_spec los o self 0 with "Ho"). iIntros "Ho". wp_pures. wp_store. iModIntro.
    iSpecialize ("Hobs" with "Ho"). iApply "HΦ". iApply append_parts. iFrame "Hobs Hsrc".
    destruct Hheld as [[-> ->]|(Hl & _)]; [|rewrite Hsl /= in Hl; lia].
    iExists ∅. rewrite /is_source_list /source_list_rep -/sl Hsl /=. iSplit; [iPureIntro; lia|].
    iExists cap, NONEV. iSplit; [iPureIntro; lia|]. iSplit; [iPureIntro; by left|].
    iExists a. rewrite /slot_fun /=. iFrame.
  - (* A later slot. *)
    rewrite (bool_decide_eq_false_2 (#(Z.of_nat (length sl)) = #0)); [|intros [= ?]; lia]. wp_pures.
    wp_load. wp_pures.
    iAssert (src_rep ls (slot_fun sl) cap (length sl) (source_cursor (sources_of w self)) hv)
      with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hrep"; [iExists a; iFrame|].
    wp_bind (If _ _ _).
    iApply (wp_wand _ _ _ (fun _ => ∃ cap', ⌜length sl < S cap'⌝ ∗
              src_rep ls (slot_fun sl) cap' (length sl) (source_cursor (sources_of w self)) hv)%I with "[Hrep]").
    { destruct (decide (length sl - 1 = cap)) as [Heq|Hne].
      - rewrite (bool_decide_eq_true_2 (#(Z.of_nat (length sl) - 1) = #(Z.of_nat cap))); [|f_equal; f_equal; lia].
        wp_pures. wp_apply (sources_grow_spec with "Hrep"); [intros j Hj; apply slot_fun_ge; lia|].
        iIntros (cap') "[%Hc' Hrep]". iExists cap'. iFrame. iPureIntro. lia.
      - rewrite (bool_decide_eq_false_2 (#(Z.of_nat (length sl) - 1) = #(Z.of_nat cap))); [|intros [= ?]; lia].
        wp_pures. iModIntro. iExists cap. iFrame. iPureIntro. lia. }
    iIntros (?) "(%cap' & %Hc' & Hrep)". wp_pures.
    wp_apply (sources_set_cell_spec with "Hrep"); [lia|]. iIntros "Hrep". wp_pures.
    iDestruct "Hrep" as (a') "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. wp_store.
    rewrite (_ : (Z.of_nat (length sl) + 1)%Z = Z.of_nat (S (length sl))); [|lia].
    set (f1 := fupd (slot_fun sl) (length sl) (#(Z.of_nat s), #(-1))%V).
    iAssert (src_rep ls f1 cap' (S (length sl)) (S (length sl)) hv) with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hrep";
      [iExists a'; iFrame|].
    wp_pures. wp_apply (observers_try_add_spec with "Ho"). iIntros "Ho".
    assert (Hunf : sources_link self s w =
              match observers_try_add self (length sl) o with
              | (o', TAdded h) => sources_append self s (Some h) o' w
              | (o', TUnlinked) => sources_append self s None o' w
              | (o', TUnknown) =>
                if decide (s ∈ fst <$> sl) then sources_append self s None o' w
                else sources_append self s (Some (observers_add self (length sl) o).2) (observers_add self (length sl) o).1 w
              end).
    { rewrite /sources_link. cbv zeta. rewrite -/sl -/o. case_decide; [lia|done]. }
    rewrite Hunf -/o.
    pose proof (Pure.ObserverSet.observers_try_add_spec self (length sl) o) as Hcases.
    destruct (observers_try_add self (length sl) o) as [o1 t] eqn:Ht. simpl.
    assert (Hfin : forall mh, (forall j, j < S cap' ->
               fupd f1 (length sl) (slot_val (s, mh)) j = slot_fun (sl ++ [(s, mh)]) j)).
    { intros mh j _. apply snoc_cells. }
    destruct t as [h| |].
    + wp_pures. rewrite (bool_decide_eq_false_2 (#(Z.of_nat h) = #(-2))); [|intros [= ?]; lia]. wp_pures.
      wp_apply (sources_set_cell_spec _ _ _ _ _ _ _ (slot_val (s, Some h)) with "Hrep"); [lia|].
      iIntros "Hrep". wp_pures.
      wp_apply (sources_index_add_spec with "Hrep"); [lia|exact Hheld|apply Hfin|].
      iIntros (held') "Hl". iSpecialize ("Hobs" with "Ho"). iApply "HΦ". iApply append_parts. iFrame.
    + wp_pures.
      wp_apply (sources_set_cell_spec _ _ _ _ _ _ _ (slot_val (s, None)) with "Hrep"); [lia|].
      iIntros "Hrep". wp_pures.
      wp_apply (sources_index_add_spec with "Hrep"); [lia|exact Hheld|apply Hfin|].
      iIntros (held') "Hl". iSpecialize ("Hobs" with "Ho"). iApply "HΦ". iApply append_parts. iFrame.
    + destruct Hcases as [[Heq _]|[(h & Heq & _)|(Heq & _)]]; simplify_eq. wp_pures.
      wp_apply (sources_holds_spec _ _ _ _ _ _ _ sl s with "Hrep"); [lia|exact Hheld| |].
      { intros j p Hj. rewrite /f1 /fupd decide_False; [by apply slot_fun_lookup|].
        apply lookup_lt_Some in Hj. lia. }
      iIntros (hv' held') "[%Hheld' Hrep]".
      destruct (decide (s ∈ fst <$> sl)) as [Hin|Hnin].
      * rewrite bool_decide_eq_true_2 //. wp_pures.
        wp_apply (sources_set_cell_spec _ _ _ _ _ _ _ (slot_val (s, None)) with "Hrep"); [lia|].
        iIntros "Hrep". wp_pures.
        wp_apply (sources_index_add_spec with "Hrep"); [lia|exact Hheld'|apply Hfin|].
        iIntros (held'') "Hl". iSpecialize ("Hobs" with "Ho"). iApply "HΦ". iApply append_parts. iFrame.
      * rewrite bool_decide_eq_false_2 //. wp_pures.
        wp_apply (observers_add_spec with "Ho"). iIntros "Ho". wp_pures.
        wp_apply (sources_set_cell_spec _ _ _ _ _ _ _ (slot_val (s, Some (observers_add self (length sl) o).2))
                    with "Hrep"); [lia|].
        iIntros "Hrep". wp_pures.
        wp_apply (sources_index_add_spec with "Hrep"); [lia|exact Hheld'|apply Hfin|].
        iIntros (held'') "Hl". iSpecialize ("Hobs" with "Ho"). iApply "HΦ". iApply append_parts. iFrame.
Qed.

(** ** Source-list operations on the world *)

Lemma world_in_link tbl self s w :
  self ∈ dom tbl -> s ∈ dom tbl -> world_in tbl w -> world_in tbl (sources_link self s w).
Proof.
  intros Hself Hs Hin. destruct (sources_link_cases self s w) as [[-> _]|[-> _]];
    apply world_in_set_observers; try done; by apply world_in_set_sources.
Qed.

Lemma sources_add_edge_match self s w w' :
  edge_match w -> sources_add self s w = Some w' -> edge_match w'.
Proof.
  intros Hm. rewrite /sources_add. cbv zeta. case_decide.
  - intros [= <-] c i s' h. rewrite slot_at_advance observers_of_set_sources. apply Hm.
  - case_decide; [by intros [= <-]|].
    destruct (sources_end_run_ok self w Hm) as (w1 & -> & Hm1 & _). simpl.
    intros [= <-]. by apply edge_match_link.
Qed.

Lemma world_join tbl w self lo ls held :
  tbl !! self = Some (lo, ls) -> world_in tbl w ->
  obs_part tbl w ∗ src_part (delete self tbl) w ∗ is_source_list ls (sources_of w self) held ⊢ is_world tbl w.
Proof.
  iIntros (Ht Hin) "(Hobs & Hsrc & Hl)". iSplit; [done|]. iFrame "Hobs".
  rewrite (src_part_split _ _ self (lo, ls) Ht). iFrame.
Qed.

(**md
**`EndRun` trims as `sources_end_run` does.**

Premise: the two ends of every edge agree (`edge_match`). The two ends still agree afterwards.

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation whose run ends.
- [w:] the world before the call.
- [w':] the world `sources_end_run` gives.
*)
Lemma sources_end_run_spec (res : val) tbl (self : node_id) (w w' : EdgeWorld) :
  edge_match w -> self ∈ dom tbl -> sources_end_run self w = Some w' ->
  {{{ resolver tbl res ∗ is_world tbl w }}} sources_end_run_code res #(Z.of_nat self)
  {{{ RET #(); ⌜edge_match w'⌝ ∗ is_world tbl w' }}}.
Proof.
  intros Hm Hself Hr.
  assert (Hm' : edge_match w').
  { destruct (sources_end_run_ok self w Hm) as (w'' & Hr' & Hm'' & _). rewrite Hr in Hr'. by injection Hr' as ->. }
  iIntros (Φ) "(#Hres & %Hin & Hobs & Hsrc) HΦ".
  pose proof Hself as [[lo ls] Ht]%elem_of_dom.
  wp_lam. wp_pures. wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|]. iIntros (v ->). wp_pures.
  iDestruct (src_part_split _ _ self (lo, ls) Ht with "Hsrc") as "[(%held & %Hc & %cap & %hv & %Hcap & %Hheld & Hrep) Hsrc]".
  iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_load. wp_load. wp_pures.
  rewrite /sources_end_run in Hr.
  destruct (decide (source_cursor (sources_of w self) < length (source_slots (sources_of w self)))) as [Hlt|Hge].
  - rewrite (bool_decide_eq_true_2 (Z.of_nat (source_cursor (sources_of w self)) <
                                     Z.of_nat (length (source_slots (sources_of w self))))%Z); [|lia].
    wp_pures. wp_load.
    iAssert (source_list_rep ls (sources_of w self) held) with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hl";
      [iExists cap, hv; iSplit; [done|]; iSplit; [done|]; iExists a; iFrame|].
    wp_apply (sources_trim_from_spec res tbl self lo ls _ w held Ht Hm Hin with "[$Hres $Hobs $Hsrc $Hl]"); [lia|].
    iIntros (w1) "(%Ht1 & %Hm1 & %Hin1 & %Hlen1 & %Hcur1 & Hobs & Hsrc & Hl)".
    rewrite Ht1 in Hr. injection Hr as <-.
    iApply "HΦ". iSplit; [done|]. iApply (world_join _ _ self lo ls ∅ Ht Hin1). iFrame. iPureIntro. lia.
  - rewrite (bool_decide_eq_false_2 (Z.of_nat (source_cursor (sources_of w self)) <
                                      Z.of_nat (length (source_slots (sources_of w self))))%Z); [|lia].
    wp_pures. iModIntro. injection Hr as <-.
    iApply "HΦ". iSplit; [done|]. iApply (world_join _ _ self lo ls held Ht Hin). iFrame "Hobs Hsrc". iSplit; [done|].
    iExists cap, hv. iSplit; [done|]. iSplit; [done|]. iExists a. iFrame.
Qed.

(**md
**`Clear` removes every edge as `sources_clear` does.**

Premise and result: `edge_match`, as for `EndRun`.

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation cleared.
- [w:] the world before the call.
- [w':] the world `sources_clear` gives.
*)
Lemma sources_clear_spec (res : val) tbl (self : node_id) (w w' : EdgeWorld) :
  edge_match w -> self ∈ dom tbl -> sources_clear self w = Some w' ->
  {{{ resolver tbl res ∗ is_world tbl w }}} sources_clear_code res #(Z.of_nat self)
  {{{ RET #(); ⌜edge_match w'⌝ ∗ is_world tbl w' }}}.
Proof.
  intros Hm Hself Hr.
  assert (Hm' : edge_match w').
  { destruct (sources_clear_ok self w Hm) as (w'' & Hr' & Hm'' & _). rewrite Hr in Hr'. by injection Hr' as ->. }
  iIntros (Φ) "(#Hres & %Hin & Hobs & Hsrc) HΦ".
  pose proof Hself as [[lo ls] Ht]%elem_of_dom.
  wp_lam. wp_pures. wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|]. iIntros (v ->). wp_pures.
  iDestruct (src_part_split _ _ self (lo, ls) Ht with "Hsrc") as "[(%held & %Hc & Hl) Hsrc]".
  wp_apply (sources_trim_from_spec res tbl self lo ls 0 w held Ht Hm Hin with "[$Hres $Hobs $Hsrc $Hl]"); [lia|].
  iIntros (w1) "(%Ht1 & %Hm1 & %Hin1 & %Hlen1 & %Hcur1 & Hobs & Hsrc & (%cap & %hv & %Hcap & %Hheld & Hrep))".
  wp_pures. iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. iModIntro.
  rewrite /sources_clear Ht1 /= in Hr. injection Hr as <-.
  iApply "HΦ". iSplit; [done|]. iApply (world_join _ _ self lo ls ∅ Ht).
  { by apply world_in_set_sources. }
  rewrite sources_of_set_sources decide_True //= src_part_delete_set. iFrame "Hobs Hsrc".
  iSplit; [iPureIntro; simpl; lia|]. iExists cap, hv. iSplit; [done|]. iSplit; [done|]. iExists a. iFrame.
Qed.

(**md
**`BeginRun` moves the cursor to the start, as `sources_begin_run` does.**

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation whose run begins.
- [w:] the world before the call.
*)
Lemma sources_begin_run_spec (res : val) tbl (self : node_id) (w : EdgeWorld) :
  self ∈ dom tbl ->
  {{{ resolver tbl res ∗ is_world tbl w }}} sources_begin_run_code res #(Z.of_nat self)
  {{{ RET #(); is_world tbl (sources_begin_run self w) }}}.
Proof.
  intros Hself. iIntros (Φ) "(#Hres & %Hin & Hobs & Hsrc) HΦ".
  pose proof Hself as [[lo ls] Ht]%elem_of_dom.
  wp_lam. wp_pures. wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|]. iIntros (v ->). wp_pures.
  iDestruct (src_part_split _ _ self (lo, ls) Ht with "Hsrc") as "[(%held & %Hc & %cap & %hv & %Hcap & %Hheld & Hrep) Hsrc]".
  iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. iModIntro.
  iApply "HΦ". iApply (world_join _ _ self lo ls held Ht).
  { by apply world_in_set_sources. }
  rewrite /sources_begin_run sources_of_set_sources decide_True //= src_part_delete_set. iFrame "Hobs Hsrc".
  iSplit; [iPureIntro; simpl; lia|]. iExists cap, hv. iSplit; [done|]. iSplit; [done|]. iExists a. iFrame.
Qed.

(**md
**`Add` follows `sources_add`: a match at the cursor, a repeat of the source just matched, or a trim and a link.**

Premise and result: `edge_match`, as for `EndRun`.

- [res:] the resolver.
- [tbl:] the node table.
- [self:] the computation reading.
- [s:] the source read.
- [w:] the world before the read.
- [w':] the world `sources_add` gives.
*)
Lemma sources_add_spec (res : val) tbl (self s : node_id) (w w' : EdgeWorld) :
  edge_match w -> self ∈ dom tbl -> s ∈ dom tbl -> sources_add self s w = Some w' ->
  {{{ resolver tbl res ∗ is_world tbl w }}} sources_add_code res #(Z.of_nat self) #(Z.of_nat s)
  {{{ RET #(); ⌜edge_match w'⌝ ∗ is_world tbl w' }}}.
Proof.
  intros Hm Hself Hs Hadd.
  assert (Hm' : edge_match w') by (eapply sources_add_edge_match; eauto).
  iIntros (Φ) "(#Hres & %Hin & Hobs & Hsrc) HΦ".
  pose proof Hself as [[lo ls] Ht]%elem_of_dom.
  wp_lam. wp_pures. wp_bind (App (Val res) _). iApply (wp_wand with "[]"); [by iApply "Hres"|].
  iIntros (v ->). wp_pures.
  iDestruct (src_part_split _ _ self (lo, ls) Ht with "Hsrc") as "[(%held & %Hc & %cap & %hv & %Hcap & %Hheld & Hrep) Hsrc]".
  set (sl := source_slots (sources_of w self)) in *. set (cur := source_cursor (sources_of w self)) in *.
  iDestruct "Hrep" as (a) "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_load. wp_pures. wp_load. wp_pures.
  iAssert (src_rep ls (slot_fun sl) cap (length sl) cur hv) with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hrep";
    [iExists a; iFrame|].
  wp_bind (If _ _ #false).
  iApply (wp_wand _ _ _ (fun v => ⌜v = #(bool_decide (fst <$> sl !! cur = Some s))⌝ ∗
            src_rep ls (slot_fun sl) cap (length sl) cur hv)%I with "[Hrep]").
  { destruct (decide (cur < length sl)) as [Hlt|Hge].
    - rewrite (bool_decide_eq_true_2 (Z.of_nat cur < Z.of_nat (length sl))%Z); [|lia]. wp_pures.
      destruct (lookup_lt_is_Some_2 sl cur Hlt) as [[x mh] Hx].
      wp_apply (sources_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
      rewrite (slot_fun_lookup _ _ _ Hx) /slot_val /=. wp_pures. iFrame. iPureIntro. rewrite Hx /=.
      destruct (decide (x = s)) as [->|Hne].
      + rewrite !bool_decide_eq_true_2 //.
      + rewrite !bool_decide_eq_false_2 //; intros [= ?]; lia.
    - rewrite (bool_decide_eq_false_2 (Z.of_nat cur < Z.of_nat (length sl))%Z); [|lia]. wp_pures.
      iFrame. iPureIntro. rewrite lookup_ge_None_2 //. lia. }
  iIntros (v) "[-> Hrep]".
  destruct (decide (fst <$> sl !! cur = Some s)) as [Hp|Hp].
  { (* A match at the cursor. *)
    rewrite bool_decide_eq_true_2 //. wp_pures.
    assert (Hw' : w' = set_sources self {| source_slots := sl; source_cursor := S cur |} w).
    { revert Hadd. rewrite /sources_add. cbv zeta. rewrite -/sl -/cur. case_decide; [|contradiction].
      intros [= <-]. done. }
    subst w'. iDestruct "Hrep" as (a') "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_store. iModIntro.
    assert (Hcl : cur < length sl).
    { destruct (sl !! cur) eqn:E; [by apply lookup_lt_Some in E|done]. }
    iApply "HΦ". iSplit; [done|]. iApply (world_join _ _ self lo ls held Ht); [by apply world_in_set_sources|].
    rewrite sources_of_set_sources decide_True //= src_part_delete_set. iFrame "Hobs Hsrc".
    iSplit; [iPureIntro; simpl; lia|]. iExists cap, hv. iSplit; [done|]. iSplit; [done|]. iExists a'.
    rewrite (_ : (Z.of_nat cur + 1)%Z = Z.of_nat (S cur)); [|lia]. iFrame. }
  rewrite bool_decide_eq_false_2 //. wp_pures.
  wp_bind (If _ _ #false).
  iApply (wp_wand _ _ _ (fun v => ⌜v = #(bool_decide ((0 < cur)%nat /\ fst <$> sl !! pred cur = Some s))⌝ ∗
            src_rep ls (slot_fun sl) cap (length sl) cur hv)%I with "[Hrep]").
  { destruct (decide (0 < cur)) as [Hpos|Hz].
    - rewrite (bool_decide_eq_true_2 (0 < Z.of_nat cur)%Z); [|lia]. wp_pures.
      rewrite (_ : (Z.of_nat cur - 1)%Z = Z.of_nat (pred cur)); [|lia].
      destruct (lookup_lt_is_Some_2 sl (pred cur) ltac:(lia)) as [[x mh] Hx].
      wp_apply (sources_cell_spec with "Hrep"); [lia|]. iIntros "Hrep".
      rewrite (slot_fun_lookup _ _ _ Hx) /slot_val /=. wp_pures. iFrame. iPureIntro. rewrite Hx /=.
      destruct (decide (x = s)) as [->|Hne].
      + do 2 f_equal. apply bool_decide_ext. split; [intros _; split; [lia|done]|done].
      + do 2 f_equal. apply bool_decide_ext. split; [intros [= ?]; lia|intros [_ [= ?]]; congruence].
    - rewrite (bool_decide_eq_false_2 (0 < Z.of_nat cur)%Z); [|lia]. wp_pures.
      iFrame. iPureIntro. rewrite bool_decide_eq_false_2 //. lia. }
  iIntros (v) "[-> Hrep]".
  destruct (decide (0 < cur /\ fst <$> sl !! pred cur = Some s)) as [Hq|Hq].
  { (* A repeat of the source just matched. *)
    rewrite bool_decide_eq_true_2 //. wp_pures. iModIntro.
    assert (w' = w) as ->.
    { revert Hadd. rewrite /sources_add. cbv zeta. rewrite -/sl -/cur.
      case_decide; [contradiction|]. case_decide; [|contradiction]. by intros [= <-]. }
    iApply "HΦ". iSplit; [done|]. iApply (world_join _ _ self lo ls held Ht Hin). iFrame "Hobs Hsrc".
    iSplit; [done|]. iExists cap, hv. iSplit; [done|]. iSplit; [done|]. done. }
  rewrite bool_decide_eq_false_2 //. wp_pures.
  (* A miss: [Append]. *)
  wp_lam. wp_pures. iDestruct "Hrep" as (a') "(H0 & H1 & H2 & H3 & H4 & H5 & Ha)". wp_load. wp_load. wp_pures.
  assert (Hadd' : sources_end_run self w ≫= (fun w1 => Some (sources_link self s w1)) = Some w').
  { revert Hadd. rewrite /sources_add. cbv zeta. rewrite -/sl -/cur.
    case_decide; [contradiction|]. case_decide; [contradiction|]. done. }
  rewrite /sources_end_run -/sl -/cur in Hadd'. revert Hadd'.
  destruct (decide (cur < length sl)) as [Hlt|Hge]; intros Hadd'.
  - rewrite (bool_decide_eq_true_2 (Z.of_nat cur < Z.of_nat (length sl))%Z); [|lia]. wp_pures. wp_load.
    iAssert (source_list_rep ls (sources_of w self) held) with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hl";
      [iExists cap, hv; iSplit; [done|]; iSplit; [done|]; iExists a'; iFrame|].
    wp_apply (sources_trim_from_spec res tbl self lo ls cur w held Ht Hm Hin with "[$Hres $Hobs $Hsrc $Hl]");
      [subst sl cur; lia|].
    iIntros (w1) "(%Ht1 & %Hm1 & %Hin1 & %Hlen1 & %Hcur1 & Hobs & Hsrc & Hl)". wp_pures.
    rewrite Ht1 /= in Hadd'. injection Hadd' as <-.
    wp_apply (sources_link_spec res tbl self s lo ls w1 ∅ Ht Hs with "[$Hres $Hobs $Hsrc $Hl]").
    iIntros "(Hobs & Hsrc & [%held' Hl])". iApply "HΦ". iSplit; [done|].
    iApply (world_join _ _ self lo ls held' Ht); [apply world_in_link; done|]. iFrame.
  - rewrite (bool_decide_eq_false_2 (Z.of_nat cur < Z.of_nat (length sl))%Z); [|lia]. wp_pures.
    simpl in Hadd'. injection Hadd' as <-.
    iAssert (source_list_rep ls (sources_of w self) held) with "[H0 H1 H2 H3 H4 H5 Ha]" as "Hl";
      [iExists cap, hv; iSplit; [done|]; iSplit; [done|]; iExists a'; iFrame|].
    wp_apply (sources_link_spec res tbl self s lo ls w held Ht Hs with "[$Hres $Hobs $Hsrc $Hl]").
    iIntros "(Hobs & Hsrc & [%held' Hl])". iApply "HΦ". iSplit; [done|].
    iApply (world_join _ _ self lo ls held' Ht); [apply world_in_link; done|]. iFrame.
Qed.

(**md
**`new SourceList ()` is the empty list.**

- [ls:] the location returned.
*)
Lemma sources_new_spec :
  {{{ True }}} sources_new_code #() {{{ ls, RET #ls; is_source_list ls empty_source_list ∅ }}}.
Proof.
  iIntros (Φ) "_ HΦ". wp_lam. wp_alloc ls as "Hl"; [lia|].
  rewrite (_ : Z.to_nat 6 = 6) // /=. rewrite !array_cons array_nil !Loc.add_assoc /=.
  iDestruct "Hl" as "(H0 & H1 & H2 & H3 & H4 & H5 & _)".
  wp_pures. wp_store. wp_pures. wp_store. wp_pures. wp_store. wp_pures. wp_store. wp_pures. wp_store.
  iModIntro. iApply "HΦ". iSplit; [done|]. iExists 0, NONEV. iSplit; [iPureIntro; simpl; lia|].
  iSplit; [iPureIntro; by left|]. iExists ls. rewrite /slot_fun /= array_nil. iFrame.
Qed.

End heap.
