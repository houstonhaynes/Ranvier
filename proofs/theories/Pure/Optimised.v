From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Reference Require Semantics.
From Ranvier.Pure Require Import ObserverSet SourceList State Marking Engine.
From Ranvier.Pure Require Refinement Witnesses Runs WholeGraph Scheduler Ownership.

Open Scope nat_scope.

(** * The saturated-write skip

    A signal write skips its notify walk while every observer of the signal is saturated, tracked by
    a graph epoch. This file models the optimised engine as its own transitions beside [Pure/Engine.v],
    sharing every helper of that file, and states what the optimisation keeps. *)

(** ** Saturation *)

(**md
A memo whose `MarkDirty` (Core.fs 4483-4504) changes nothing: `Dirty`, with no announcement due.
*)
Definition memo_saturated (s : state) (d : node) : Prop :=
  n_freshness d = Dirty /\ announce_due s d = false.

(**md
An effect whose `MarkDirty` (Core.fs 4742-4755) changes nothing: disposed, in its cleanups, or `Dirty`
and queued.
*)
Definition effect_saturated (d : node) : Prop :=
  n_disposed d = true \/ n_in_cleanup d = true \/ (n_freshness d = Dirty /\ n_queued d = true).

(**md
A `Dirty` mark leaves node `n` unchanged. Signals and absent nodes count as saturated.
*)
Definition saturated (s : state) (n : node_id) : Prop :=
  match g_nodes s !! n with
  | Some d => match n_kind d with
              | KMemo => memo_saturated s d | KEffect => effect_saturated d | KSignal => True end
  | None => True
  end.

(**md
The stamp of `src` holds the graph's present epoch.
*)
Definition stamp_current (s : state) (src : node_id) : Prop := g_stamps s !! src = Some (g_epoch s).

(** [c] is one of the observers of [src] visited by [notify_with]. *)
Definition observer_of (s : state) (src c : node_id) : Prop :=
  c ∈ observer_view (observers_of (g_edges s) src).

(**md
Every observer of a signal with a current stamp is saturated.
*)
Definition stamp_ok (s : state) : Prop :=
  forall src c, stamp_current s src -> observer_of s src c -> saturated s c.

(**md
`s` with the fields of the skip at their initial values.
*)
Definition strip (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s; g_registered := g_registered s;
     g_skip := SkipOff; g_epoch := 0; g_stamps := ∅ |}.

(**md
The present engine's state `s` and the optimised engine's state `o` are equal apart from the fields of
the skip, and `o` meets `stamp_ok`.
*)
Definition sim (s o : state) : Prop :=
  strip s = strip o /\ g_skip s = SkipOff /\ g_skip o = SkipAll /\ stamp_ok o.

(** ** The optimised transitions *)

(**md
The Core.fs line of each write that can make a saturated node unsaturated.
*)
Inductive advance_point :=
  | A4191 (* memo [freshness <- Clean], [RecomputeScoped] *)
  | A4207 (* memo [freshness <- Clean], [Run] *)
  | A4290 (* memo [unannounced <- true] *)
  | A4807 (* effect [queued <- false] and [freshness <- Clean], [Execute] *)
  | A4825 (* effect [inCleanup <- false] *)
  | ANewEffect (* the [Effect] constructor: a new effect is [Clean] and unqueued *).

(**md
`true` when mode `m` advances the epoch at point `p`. `SkipLeaveDirty` advances only where a node leaves
`Dirty`.
*)
Definition advances (m : skip_mode) (p : advance_point) : bool :=
  match m, p with
  | SkipOff, _ => false
  | (SkipAll | SkipUnchecked), _ => true
  | SkipLeaveDirty, (A4191 | A4207 | A4807) => true
  | SkipLeaveDirty, (A4290 | A4825 | ANewEffect) => false
  end.

(** The epoch's advance at point [p]. *)
Definition advance (p : advance_point) : M unit :=
  m_modify (fun s => if advances (g_skip s) p then with_epoch (S (g_epoch s)) s else s).

(** A node write and the advance of its point, in one step. *)
Definition modify_node_adv (p : advance_point) (n : node_id) (u : node -> node) : M unit :=
  let* _ := modify_node n u in advance p.

(** A new effect, with the advance of [ANewEffect]. *)
Definition add_node_adv (n : node_id) (d : node) : M unit :=
  let* _ := add_node n d in advance ANewEffect.

Definition skip_on (m : skip_mode) : bool := match m with SkipOff => false | _ => true end.

Global Instance stamp_current_dec s src : Decision (stamp_current s src).
Proof. unfold stamp_current. apply _. Defined.

Global Instance observer_of_dec s src c : Decision (observer_of s src c).
Proof. unfold observer_of. apply _. Defined.

Global Instance saturated_dec s n : Decision (saturated s n).
Proof. unfold saturated, memo_saturated, effect_saturated. repeat case_match; apply _. Defined.

(** Every observer of [src] is saturated. *)
Definition all_saturated (src : node_id) (s : state) : bool :=
  forallb (fun c => bool_decide (saturated s c)) (observer_view (observers_of (g_edges s) src)).

(** The record checks [all_saturated], except under [SkipUnchecked]. *)
Definition record_due (src : node_id) (s : state) : bool :=
  match g_skip s with
  | SkipOff => false
  | SkipUnchecked => true
  | _ => all_saturated src s
  end.

(** The skip test, ahead of the walk of a signal write. At fuel [0] the write walks. *)
Definition skip_due (fuel : nat) (src : node_id) (s : state) : bool :=
  match fuel with
  | 0 => false
  | S _ => skip_on (g_skip s) && bool_decide (g_stamps s !! src = Some (g_epoch s))
  end.

(** The record at the end of a signal write's walk, when every observer is saturated. *)
Definition record (src : node_id) (s : state) : state :=
  if record_due src s then with_stamps (<[src := g_epoch s]> (g_stamps s)) s else s.

(**md
The notify of a signal write (Core.fs 3817-3818): no walk while the stamp is current, else
`notify_dirty` followed by the record, which happens only when the walk left every observer saturated.

- [fuel:] the bound on the recursion of the marks.
- [src:] the signal written.
*)
Definition notify_write (fuel : nat) (src : node_id) (s : state) : option state :=
  if skip_due fuel src s then Some s else record src <$> notify_dirty fuel src s.

(** The clear of an observer addition (Core.fs 3775-3780). *)
Definition unstamp (src : node_id) (s : state) : state :=
  if skip_on (g_skip s) then with_stamps (delete src (g_stamps s)) s else s.

(** [sources_add self s w] takes its miss branch, which calls [AddObserver] or [TryAddObserver]. *)
Definition links (self s : node_id) (w : EdgeWorld) : bool :=
  let l := sources_of w self in
  let cur := source_cursor l in
  negb (bool_decide (fst <$> source_slots l !! cur = Some s)) &&
  negb (bool_decide (0 < cur /\ fst <$> source_slots l !! pred cur = Some s)).

(** [Graph.Track], clearing the stamp of [n] when the read adds an observer. *)
Definition otrack (n : node_id) : M unit :=
  fun s =>
    match g_current s with
    | Some c =>
      match sources_add c n (g_edges s) with
      | Some w =>
        let s' := ghost_read c n (read_prefix (g_edges s) c) (with_edges w s) in
        Some (if links c n (g_edges s) then unstamp n s' else s', tt)
      | None => None
      end
    | None => Some (s, tt)
    end.

(** [announce], with the advance of Core.fs 4290. *)
Definition oannounce (n : node_id) : M unit :=
  fun s =>
    if f_unannounced (g_flags s)
    then modify_node_adv A4290 n (node_with_unannounced true) s
    else Some (s, tt).

(**md
The optimised engine. Each function follows its namesake of `Pure/Engine.v` without the `o`, with
three differences: the advances, the notify of a signal write (`notify_write`), and the clear of a
tracked read (`otrack`).

- [fuel:] the bound on nested steps.
- [e:] the local variable environment of the command.
- [c:] the command run.
*)
Fixpoint oexec (fuel : nat) (e : env) (c : cmd) {struct fuel} : M outcome :=
  match fuel with
  | 0 => stuck
  | S f =>
    match c with
    | CRet x => on_value e x (fun v => m_return (Ok v))
    | CThrow y => m_return (Fail y)
    | CLet x v k => on_value e v (fun z => oexec f ((x, z) :: e) k)
    | CIf v a b => on_value e v (fun z => if decide (z = 0%Z) then oexec f e b else oexec f e a)
    | CRead x n k =>
      on_node e n (fun t =>
        let* o := oread f t true in
        let* _ := note_host_read t o in
        match o with
        | Ok v => oexec f ((x, v) :: e) k
        | Fail y => m_return (Fail y)
        end)
    | CTryRead x ok n k =>
      on_node e n (fun t =>
        let* o := oread f t true in
        let* _ := note_host_read t o in
        match o with
        | Ok v => oexec f ((ok, 1%Z) :: (x, v) :: e) k
        | Fail y => oexec f ((ok, 0%Z) :: (x, y) :: e) k
        end)
    | CReadUntracked x n k =>
      on_node e n (fun t =>
        let* o := oread f t false in
        let* _ := note_host_read t o in
        match o with
        | Ok v => oexec f ((x, v) :: e) k
        | Fail y => m_return (Fail y)
        end)
    | CWrite n v k =>
      on_node e n (fun t =>
        on_value e v (fun z =>
          let* _ := owrite_signal f t z in
          oexec f e k))
    | CSignal x v k =>
      on_value e v (fun z =>
        let* n := next_id in
        let* _ := add_node n (signal_node z) in
        oexec f ((x, Z.of_nat n) :: e) k)
    | CMemo x m q b k =>
      let* n := next_id in
      let e' := (x, Z.of_nat n) :: e in
      let* _ := add_node n (memo_node m q b e') in
      let* owner := current_owner in
      match owner with
      | None => m_return (Fail pure_scope_failure)
      | Some o =>
        let* _ := oattach f n o in
        oexec f e' k
      end
    | CEffect x b k =>
      let* n := next_id in
      let e' := (x, Z.of_nat n) :: e in
      let* _ := add_node_adv n (effect_node b e') in
      let* owner := current_owner in
      match owner with
      | None => m_return (Fail pure_scope_failure)
      | Some o =>
        let* _ := oattach f n o in
        let* _ := m_step (mark_dirty f n) in
        let* _ := orequest_flush f in
        oexec f e' k
      end
    | CCleanup b k =>
      let* owner := current_owner in
      match owner with
      | None => m_return (Fail pure_scope_failure)
      | Some o =>
        let* x := get_owner o in
        let* _ := m_modify (ghost_registered (o_host x)) in
        let* _ :=
          if o_disposed x then
            let* _ := detached o (orun_cleanup f o b e) in
            orequest_flush f
          else put_owner o (owner_with_cleanup b e x) in
        oexec f e k
      end
    | CDispose n k =>
      on_node e n (fun t =>
        let* d := get_node t in
        match n_kind d with
        | KSignal => stuck
        | _ =>
          let* _ := odispose_node f t in
          let* _ := if n_disposed d then m_return tt else emit (EvDispose t) in
          oexec f e k
        end)
    | CBatch b k =>
      let* _ := m_modify (fun s => with_batch_depth (S (g_batch_depth s)) s) in
      let* r := oexec f e b in
      let* _ := m_modify (fun s => with_batch_depth (pred (g_batch_depth s)) s) in
      match r with
      | Ok _ =>
        let* _ := orequest_flush f in
        oexec f e k
      | Fail y => m_return (Fail y)
      end
    | CLog v k =>
      on_value e v (fun z =>
        let* l := log_label in
        let* _ := emit (EvLog l z) in
        oexec f e k)
    end
  end

with oread (fuel : nat) (n : node_id) (tracked : bool) {struct fuel} : M outcome :=
  match fuel with
  | 0 => stuck
  | S f => if tracked then oread_node f n else untracked (oread_node f n)
  end

with oread_node (fuel : nat) (n : node_id) {struct fuel} : M outcome :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_kind d with
    | KSignal =>
      let* _ := otrack n in
      m_return (Ok (n_value d))
    | KEffect => stuck
    | KMemo =>
      let* _ :=
        if stale_live d then
          let* _ := orefresh_or_pull f n in
          let* _ := otrack n in
          let* d' := get_node n in
          if stale_live d' then mark_reader f else m_return tt
        else otrack n in
      let* d' := get_node n in
      match n_status d' with
      | StError => m_return (Fail (default 0%Z (n_failure d')))
      | _ => m_return (Ok (n_value d'))
      end
    end
  end

with oupdate_if_necessary (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_kind d with
    | KMemo => if stale_live d then orefresh_or_pull f n else m_return tt
    | _ => m_return tt
    end
  end

with orefresh_or_pull (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if deferring s then orefresh f n else opull f n
  end

with opull (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := m_modify (fun s => with_pull_depth (S (g_pull_depth s)) s) in
    let* _ := orefresh f n in
    let* _ := m_modify (fun s => with_pull_depth (pred (g_pull_depth s)) s) in
    let* s := m_get in
    if bool_decide (g_pull_depth s = 0%nat) && g_flush_owed s then osettle_flush f else m_return tt
  end

with orefresh (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let* _ :=
      if decide (n_freshness d = Check) then
        let* _ := oresolve_walk f n 0 in
        let* d' := get_node n in
        if decide (n_freshness d' = Check)
        then
          let* _ := m_modify (ghost_resolved n) in
          modify_node n (fun x => node_with_unannounced false (node_with_freshness Clean x))
        else m_return tt
      else m_return tt in
    let* d' := get_node n in
    if decide (n_freshness d' = Dirty) then orecompute f n else m_return tt
  end

with oresolve_walk (fuel : nat) (n : node_id) (i : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let* s := m_get in
    if decide (n_freshness d = Check) then
      match source_slots (sources_of (g_edges s) n) !! i with
      | Some (src, _) =>
        let* _ := oupdate_if_necessary f src in
        oresolve_walk f n (S i)
      | None => m_return tt
      end
    else m_return tt
  end

with orecompute (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_scope d with
    | None => orun_memo f n
    | Some o =>
      let* _ := modify_node_adv A4191 n (node_with_freshness Clean) in
      let* _ := m_modify (ghost_dirty_cleared n (n_runs d)) in
      let* _ := odischarge f o in
      let* d' := get_node n in
      if n_disposed d' then m_return tt
      else if decide (n_runs d' = n_runs d) then orun_memo f n
      else if decide (n_freshness d' = Dirty) then orecompute f n
      else m_return tt
    end
  end

with orun_memo (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let previous := n_value d in
    let previous_status := n_status d in
    let previous_failure := n_failure d in
    let* _ := modify_node_adv A4207 n (fun x =>
      node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone
        (node_with_unannounced false (node_with_freshness Clean x))))) in
    let* _ := edges (sources_begin_run n) in
    let* _ := m_modify (ghost_run_start n) in
    let* _ := push_running n in
    let body_env :=
      if n_published d then (prev_var, previous) :: n_env d else without_prev (n_env d) in
    let* r := hosted n (oexec f body_env (n_body d)) in
    let* d1 := get_node n in
    let result := if n_violated d1 then Fail pure_scope_failure else r in
    let* _ :=
      match result with
      | Ok v => modify_node n (fun x => node_with_published true (node_with_value v x))
      | Fail _ => m_return tt
      end in
    let* _ := edges_opt (if n_disposed d1 then sources_clear n else sources_end_run n) in
    let* _ := pop_running in
    let* _ :=
      match result with
      | Ok _ => m_return tt
      | Fail y => modify_node n (fun x =>
          node_with_violated false (node_with_status StError (node_with_failure (Some y) x)))
      end in
    let* d2 := get_node n in
    let* moved :=
      if decide (n_status d2 <> previous_status \/ n_failure d2 <> previous_failure) then m_return true
      else
        match n_comparer d2 with
        | CmpEq => m_return (bool_decide (n_value d2 <> previous))
        | CmpNever => m_return true
        | CmpThrows y =>
          let* _ := modify_node n (fun x =>
            node_with_status StError (node_with_failure (Some y) (node_with_value previous x))) in
          m_return true
        end in
    let* s := m_get in
    let* _ := m_modify (ghost_run_end n (g_current s) moved) in
    let* _ := if moved then m_step (notify_dirty_except f n (g_current s)) else m_return tt in
    let* d3 := get_node n in
    if decide (n_freshness d3 = Clean) then m_return tt else oannounce n
  end

with oexecute_effect (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    if n_disposed d then modify_node n (node_with_queued false)
    else
      let* _ :=
        if decide (n_freshness d = Check) then
          let* _ := oresolve_walk f n 0 in
          let* d' := get_node n in
          if decide (n_freshness d' = Check)
          then
            let* _ := m_modify (ghost_resolved n) in
            modify_node n (node_with_freshness Clean)
          else m_return tt
        else m_return tt in
      let* d' := get_node n in
      let* _ := modify_node_adv A4807 n (fun x => node_with_freshness Clean (node_with_queued false x)) in
      if decide (n_freshness d' = Dirty) then orun_effect f n else m_return tt
  end

with orun_effect (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := push_running n in
    let* _ := edges (sources_begin_run n) in
    let* _ := m_modify (ghost_effect_start n) in
    let* d := get_node n in
    let* _ :=
      match n_scope d with
      | Some o =>
        let* _ := modify_node n (node_with_in_cleanup true) in
        let* _ := odischarge f o in
        modify_node_adv A4825 n (node_with_in_cleanup false)
      | None => m_return tt
      end in
    let* d' := get_node n in
    let* _ := if n_disposed d' then m_return tt else orun_effect_body f n in
    pop_running
  end

with orun_effect_body (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let* _ := modify_node n (fun x => node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone x))) in
    let* _ := emit (EvRun n) in
    let* r := hosted n (oexec f (n_env d) (n_body d)) in
    let* d' := get_node n in
    let* _ := edges_opt (if n_disposed d' then sources_clear n else sources_end_run n) in
    match r with
    | Ok _ => m_return tt
    | Fail y =>
      let* _ := modify_node n (fun x => node_with_status StError (node_with_failure (Some y) x)) in
      emit (EvFail n y)
    end
  end

with odischarge (fuel : nat) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := detached o (odispose_scope f o) in
    let* s := m_get in
    if bool_decide (g_batch_depth s = 0%nat) && negb (g_flushing s) && live_queue s
    then m_modify (with_flush_owed true)
    else m_return tt
  end

with oretire (fuel : nat) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := detached o (
      let* x := get_owner o in
      if o_disposed x then m_return tt
      else
        let* _ := put_owner o (owner_retired x) in
        odispose_scope f o) in
    let* s := m_get in
    if live_queue s then orequest_flush f else m_return tt
  end

with odispose_scope (fuel : nat) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* x := get_owner o in
    let* _ := put_owner o (owner_detached x) in
    oteardown f o (rev (o_children x)) (owner_cleanups x)
  end

with oteardown (fuel : nat) (o : owner_id) (ls : list link) (cs : list (cmd * env * nat)) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    match ls, cs with
    | [], [] => m_return tt
    | l :: ls', [] =>
      let* _ := orelease f (l_child l) in
      oteardown f o ls' []
    | [], (b, e, _) :: cs' =>
      let* _ := orun_cleanup f o b e in
      oteardown f o [] cs'
    | l :: ls', (b, e, a) :: cs' =>
      if decide (a < l_acquired l)%nat then
        let* _ := orelease f (l_child l) in
        oteardown f o ls' cs
      else
        let* _ := orun_cleanup f o b e in
        oteardown f o ls cs'
    end
  end

with orun_cleanup (fuel : nat) (o : owner_id) (b : cmd) (e : env) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* x := get_owner o in
    let* _ := emit (EvCleanup (o_host x)) in
    let* r := oexec f e b in
    match r with
    | Ok _ => m_return tt
    | Fail y =>
      let* _ := record_error o y in
      emit (EvFail (o_host x) y)
    end
  end

with orelease (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := modify_node n (node_with_link None) in
    odispose_node f n
  end

with odispose_node (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    if n_disposed d then m_return tt
    else
      let* _ := modify_node n (fun x => node_with_link None (node_with_disposed true x)) in
      let* _ := match n_link d with Some l => unlink l | None => m_return tt end in
      let* _ := edges_opt (sources_clear n) in
      match n_scope d with
      | Some o => oretire f o
      | None => m_return tt
      end
  end

with oattach (fuel : nat) (n : node_id) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* x := get_owner o in
    if o_disposed x then
      let* _ := orelease f n in
      modify_node n (node_with_link (Some {| l_child := n; l_owner := o; l_generation := None; l_acquired := 0 |}))
    else
      let l := {| l_child := n; l_owner := o; l_generation := Some (o_generation x); l_acquired := o_acquisitions x |} in
      let* _ := put_owner o (owner_with_child l x) in
      modify_node n (node_with_link (Some l))
  end

with owrite_signal (fuel : nat) (n : node_id) (v : val) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_kind d with
    | KSignal =>
      if decide (n_value d = v) then m_return tt
      else
        let* _ := modify_node n (node_with_value v) in
        let* _ := m_modify (ghost_publish n) in
        let* _ := m_step (notify_write f n) in
        orequest_flush f
    | _ => stuck
    end
  end

with orequest_flush (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if live_queue s && bool_decide (g_batch_depth s = 0%nat) then
      if outside_bodies s then orun_flush f
      else if g_flushing s then m_return tt
      else m_modify (with_flush_owed true)
    else m_return tt
  end

with osettle_flush (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if g_flush_owed s && bool_decide (g_batch_depth s = 0%nat) && outside_bodies s then orun_flush f else m_return tt
  end

with orun_flush (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if g_flushing s then m_return tt
    else
      let* _ := m_modify (fun s => with_flush_owed false (with_flushing true s)) in
      let* _ := odrain f in
      m_modify (with_flushing false)
  end

with odrain (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    match g_queue s !! g_queue_head s with
    | Some n =>
      let* _ := m_modify (with_queue_head (S (g_queue_head s))) in
      let* _ := oexecute_effect f n in
      odrain f
    | None => m_modify (fun s => with_queue [] (with_queue_head 0 s))
    end
  end.

(**md
The event trace of running `c` on a new graph in skip mode `m`, or `None` when the run is stuck or runs
out of fuel.

- [fuel:] the bound on nested steps.
- [fl:] the flags of the new graph.
- [m:] the skip mode.
- [c:] the command run.
*)
Definition trace_of_mode (fuel : nat) (fl : flags) (m : skip_mode) (c : cmd) : option (list event) :=
  match oexec fuel [] c (with_skip m (init fl)) with
  | Some (s, _) => Some (g_trace s)
  | None => None
  end.

(**md
The engine in skip mode `m` as a reference engine, as `engine_of` is for the present engine.
*)
Definition engine_of_mode (m : skip_mode) (k : nat) (p : Semantics.program) (h : Semantics.history)
    : option (list event) :=
  Refinement.project p <$> trace_of_mode k current_flags m (Refinement.erase p h).

(**md
The optimised engine as a reference engine.
*)
Definition engine_of_optimised (k : nat) (p : Semantics.program) (h : Semantics.history) : option (list event) :=
  Refinement.project p <$> trace_of_mode k current_flags SkipAll (Refinement.erase p h).

(** ** Under [SkipOff] the optimised engine is the present engine *)

(** [m] leaves the mode [SkipOff]. *)
Definition keeps_off {A} (m : M A) : Prop :=
  forall s s' a, g_skip s = SkipOff -> m s = Some (s', a) -> g_skip s' = SkipOff.

(** [m1] and [m2] agree from every state in mode [SkipOff], and [m1] stays there. *)
Definition eqM {A} (m1 m2 : M A) : Prop :=
  (forall s, g_skip s = SkipOff -> m1 s = m2 s) /\ keeps_off m1.

Lemma eqM_bind {A B} (m1 m2 : M A) (k1 k2 : A -> M B) :
  eqM m1 m2 -> (forall a, eqM (k1 a) (k2 a)) -> eqM (m_bind m1 k1) (m_bind m2 k2).
Proof.
  intros [E1 K1] Hk. split.
  - intros s Hs. unfold m_bind. rewrite <- (E1 s Hs).
    destruct (m1 s) as [[s1 a]|] eqn:Em; [|done].
    apply (proj1 (Hk a)). by eapply K1.
  - intros s s' b Hs Hm. unfold m_bind in Hm. destruct (m1 s) as [[s1 a]|] eqn:Em; [|done].
    eapply (proj2 (Hk a)); [|exact Hm]. by eapply K1.
Qed.

Lemma eqM_refl {A} (m : M A) : keeps_off m -> eqM m m.
Proof. intros K. split; [done|exact K]. Qed.

(** [g] leaves the mode as it is. *)
Definition skip_fixed (g : state -> state) : Prop := forall s, g_skip (g s) = g_skip s.

Lemma keeps_return {A} (a : A) : keeps_off (m_return a).
Proof. intros s s' b Hs Hm. unfold m_return in Hm. by simplify_eq. Qed.

Lemma keeps_stuck {A} : keeps_off (@stuck A).
Proof. by intros ????. Qed.

Lemma keeps_get : keeps_off m_get.
Proof. intros s s' b Hs Hm. unfold m_get in Hm. by simplify_eq. Qed.

Lemma keeps_modify g : skip_fixed g -> keeps_off (m_modify g).
Proof. intros Hg s s' b Hs Hm. unfold m_modify in Hm. simplify_eq. by rewrite Hg. Qed.

Lemma keeps_step (g : state -> option state) :
  (forall s s', g s = Some s' -> g_skip s' = g_skip s) -> keeps_off (m_step g).
Proof.
  intros Hg s s' b Hs Hm. unfold m_step in Hm. destruct (g s) as [s1|] eqn:E; [|done].
  simplify_eq. by rewrite (Hg _ _ E).
Qed.

Lemma keeps_get_node n : keeps_off (get_node n).
Proof. intros s s' b Hs Hm. unfold get_node in Hm. destruct (g_nodes s !! n); by simplify_eq. Qed.

Lemma keeps_get_owner o : keeps_off (get_owner o).
Proof. intros s s' b Hs Hm. unfold get_owner in Hm. destruct (g_owners s !! o); by simplify_eq. Qed.

Lemma keeps_modify_node n u : keeps_off (modify_node n u).
Proof. intros s s' b Hs Hm. unfold modify_node in Hm. destruct (g_nodes s !! n); by simplify_eq. Qed.

Lemma keeps_edges_opt u : keeps_off (edges_opt u).
Proof. intros s s' b Hs Hm. unfold edges_opt in Hm. destruct (u (g_edges s)); by simplify_eq. Qed.

Lemma keeps_next_id : keeps_off next_id.
Proof. intros s s' b Hs Hm. unfold next_id in Hm. by simplify_eq. Qed.

Lemma keeps_log_label : keeps_off log_label.
Proof. intros s s' b Hs Hm. unfold log_label in Hm. repeat case_match; by simplify_eq. Qed.

Lemma keeps_note n o : keeps_off (note_host_read n o).
Proof.
  intros s s' b Hs Hm. unfold note_host_read in Hm. repeat case_match; unfold emit, m_modify in *; by simplify_eq.
Qed.

Lemma keeps_current_owner : keeps_off current_owner.
Proof. intros s s' b Hs Hm. unfold current_owner in Hm. repeat case_match; by simplify_eq. Qed.

Lemma keeps_track n : keeps_off (track n).
Proof.
  intros s s' b Hs Hm. unfold track in Hm. repeat case_match; simplify_eq; [|done].
  unfold ghost_read. case_match; done.
Qed.

Lemma prim_skip s s' : mark_prim s s' -> g_skip s' = g_skip s.
Proof. by intros []. Qed.

Lemma marks_skip s s' : rtc mark_prim s s' -> g_skip s' = g_skip s.
Proof. induction 1 as [|s1 s2 s3 Hp _ IH]; [done|]. rewrite IH. by apply prim_skip. Qed.

Lemma any_mark_skip f n r s s' : any_mark f n r s s' -> g_skip s' = g_skip s.
Proof. intros H. by eapply marks_skip, any_mark_rtc. Qed.

Lemma keeps_mark_reader f : keeps_off (mark_reader f).
Proof.
  intros s s' b Hs Hm. unfold mark_reader in Hm.
  destruct (g_current s) as [c|]; [|by simplify_eq].
  destruct (f_reader_mark _); [|by simplify_eq].
  destruct (mark_check f c s) as [s1|] eqn:E; [|done]. simplify_eq.
  rewrite <- Hs. eapply (any_mark_skip f c None). left. exact E.
Qed.

Lemma keeps_announce n : keeps_off (announce n).
Proof.
  intros s s' b Hs Hm. unfold announce in Hm. case_match; [|by simplify_eq]. by eapply keeps_modify_node.
Qed.

Lemma eqM_on_value e x (k1 k2 : val -> M outcome) :
  (forall v, eqM (k1 v) (k2 v)) -> eqM (on_value e x k1) (on_value e x k2).
Proof.
  intros Hk. split.
  - intros s Hs. unfold on_value. destruct (eval_expr _ _ _) as [[v|y]|]; [by apply (proj1 (Hk v))|done|done].
  - intros s s' b Hs Hm. unfold on_value in Hm. destruct (eval_expr _ _ _) as [[v|y]|]; [|by simplify_eq|done].
    by eapply (proj2 (Hk v)).
Qed.

Lemma eqM_on_node e x (k1 k2 : node_id -> M outcome) :
  (forall t, eqM (k1 t) (k2 t)) -> eqM (on_node e x k1) (on_node e x k2).
Proof.
  intros Hk. apply eqM_on_value. intros z. destruct (decide _); [apply Hk|apply eqM_refl, keeps_stuck].
Qed.

Local Ltac wrap_tac E K :=
  split;
  [ intros s Hs; unfold hosted, untracked, detached, m_bind, m_modify, m_return; simpl;
    rewrite E; [reflexivity|exact Hs]
  | intros s s' a Hs Hm; unfold hosted, untracked, detached, m_bind, m_modify, m_return in Hm; simpl in Hm;
    match type of Hm with context [match ?b ?x with _ => _ end] =>
      destruct (b x) as [[s1 r]|] eqn:?; [|done] end;
    simplify_eq; simpl; eapply K; [|eassumption]; exact Hs ].

Lemma eqM_hosted {A} n (b1 b2 : M A) : eqM b1 b2 -> eqM (hosted n b1) (hosted n b2).
Proof. intros [E K]. wrap_tac E K. Qed.

Lemma eqM_untracked {A} (b1 b2 : M A) : eqM b1 b2 -> eqM (untracked b1) (untracked b2).
Proof. intros [E K]. wrap_tac E K. Qed.

Lemma eqM_detached o (b1 b2 : M unit) : eqM b1 b2 -> eqM (detached o b1) (detached o b2).
Proof. intros [E K]. wrap_tac E K. Qed.

Lemma eqM_advance_r {A} (m : M A) p (k : unit -> M A) :
  eqM m (k tt) -> eqM m (m_bind (advance p) k).
Proof.
  intros [E K]. split; [|done]. intros s Hs. unfold m_bind, advance, m_modify. rewrite Hs. simpl. by apply E.
Qed.

Lemma eqM_then_advance (m : M unit) p : keeps_off m -> eqM m (m_bind m (fun _ => advance p)).
Proof.
  intros K. split; [|done]. intros s Hs. unfold m_bind, advance, m_modify.
  destruct (m s) as [[s1 []]|] eqn:E; [|done]. by rewrite (K _ _ _ Hs E).
Qed.

Lemma eqM_modify_adv p n u : eqM (modify_node n u) (modify_node_adv p n u).
Proof. unfold modify_node_adv. apply eqM_then_advance, keeps_modify_node. Qed.

Lemma eqM_add_node_adv n d : eqM (add_node n d) (add_node_adv n d).
Proof.
  unfold add_node_adv. apply eqM_then_advance. apply keeps_modify.
  intros s. unfold ghost_publish. simpl. case_match; reflexivity.
Qed.

Lemma eqM_track n : eqM (track n) (otrack n).
Proof.
  split; [|apply keeps_track]. intros s Hs. unfold track, otrack.
  destruct (g_current s) as [c|]; [|done]. destruct (sources_add _ _ _) as [w|]; [|done].
  assert (Hg : g_skip (ghost_read c n (read_prefix (g_edges s) c) (with_edges w s)) = SkipOff).
  { unfold ghost_read. case_match; done. }
  destruct (links _ _ _); [|done]. unfold unstamp. by rewrite Hg.
Qed.

Lemma eqM_announce n : eqM (announce n) (oannounce n).
Proof.
  split; [|apply keeps_announce]. intros s Hs. unfold announce, oannounce.
  destruct (f_unannounced _); [|done]. unfold modify_node_adv, m_bind, advance, m_modify, modify_node.
  destruct (g_nodes s !! n); [|done]. simpl. by rewrite Hs.
Qed.

Lemma eqM_notify_write f n : eqM (m_step (notify_dirty f n)) (m_step (notify_write f n)).
Proof.
  split.
  - intros s Hs. unfold m_step, notify_write.
    assert (Hd : skip_due f n s = false) by (unfold skip_due; rewrite Hs; by destruct f).
    rewrite Hd. destruct (notify_dirty f n s) as [s'|] eqn:E; [|done]. simpl.
    unfold record, record_due. rewrite (any_mark_skip f n None s s' ltac:(right; right; right; left; exact E)), Hs. done.
  - apply keeps_step. intros s s' E. eapply (any_mark_skip f n None). right; right; right; left. exact E.
Qed.

(** One record per mutual function: the optimised function at fuel [f] agrees with its namesake from
    every state in mode [SkipOff]. *)
Record osims (f : nat) : Prop := {
  Oexec : forall e c, eqM (exec f e c) (oexec f e c);
  Oread : forall n b, eqM (read f n b) (oread f n b);
  Oread_node : forall n, eqM (read_node f n) (oread_node f n);
  Oupdate : forall n, eqM (update_if_necessary f n) (oupdate_if_necessary f n);
  Orefresh_or_pull : forall n, eqM (refresh_or_pull f n) (orefresh_or_pull f n);
  Opull : forall n, eqM (pull f n) (opull f n);
  Orefresh : forall n, eqM (refresh f n) (orefresh f n);
  Owalk : forall n i, eqM (resolve_walk f n i) (oresolve_walk f n i);
  Orecompute : forall n, eqM (recompute f n) (orecompute f n);
  Orun_memo : forall n, eqM (run_memo f n) (orun_memo f n);
  Oexecute : forall n, eqM (execute_effect f n) (oexecute_effect f n);
  Orun_effect : forall n, eqM (run_effect f n) (orun_effect f n);
  Obody : forall n, eqM (run_effect_body f n) (orun_effect_body f n);
  Odischarge : forall o, eqM (discharge f o) (odischarge f o);
  Oretire : forall o, eqM (retire f o) (oretire f o);
  Odispose_scope : forall o, eqM (dispose_scope f o) (odispose_scope f o);
  Oteardown : forall o ls cs, eqM (teardown f o ls cs) (oteardown f o ls cs);
  Ocleanup : forall o b e, eqM (run_cleanup f o b e) (orun_cleanup f o b e);
  Orelease : forall n, eqM (release f n) (orelease f n);
  Odispose : forall n, eqM (dispose_node f n) (odispose_node f n);
  Oattach : forall n o, eqM (attach f n o) (oattach f n o);
  Owrite : forall n v, eqM (write_signal f n v) (owrite_signal f n v);
  Orequest : eqM (request_flush f) (orequest_flush f);
  Osettle : eqM (settle_flush f) (osettle_flush f);
  Oflush : eqM (run_flush f) (orun_flush f);
  Odrain : eqM (drain f) (odrain f)
}.

Local Ltac skip_fixed_tac :=
  intros ?; cbv beta;
  repeat (unfold ghost_read, ghost_publish, ghost_run_start, ghost_effect_start, ghost_run_end,
          ghost_dirty_cleared, ghost_resolved, ghost_registered);
  repeat case_match; reflexivity.

Local Ltac oih IH :=
  first
    [ apply (Oexec _ IH) | apply (Oread _ IH) | apply (Oread_node _ IH) | apply (Oupdate _ IH)
    | apply (Orefresh_or_pull _ IH) | apply (Opull _ IH) | apply (Orefresh _ IH) | apply (Owalk _ IH)
    | apply (Orecompute _ IH) | apply (Orun_memo _ IH) | apply (Oexecute _ IH) | apply (Orun_effect _ IH)
    | apply (Obody _ IH) | apply (Odischarge _ IH) | apply (Oretire _ IH)
    | apply (Odispose_scope _ IH) | apply (Oteardown _ IH) | apply (Ocleanup _ IH)
    | apply (Orelease _ IH) | apply (Odispose _ IH) | apply (Oattach _ IH) | apply (Owrite _ IH)
    | apply (Orequest _ IH) | apply (Osettle _ IH) | apply (Oflush _ IH) | apply (Odrain _ IH) ].

Local Ltac oleaf :=
  apply eqM_refl;
  first
    [ apply keeps_return | apply keeps_stuck | apply keeps_get | apply keeps_get_node | apply keeps_get_owner
    | apply keeps_modify_node | apply keeps_edges_opt | apply keeps_next_id | apply keeps_log_label
    | apply keeps_note | apply keeps_current_owner | apply keeps_mark_reader
    | (apply keeps_step; intros ?? E; eapply (any_mark_skip _ _ None); right; left; exact E)
    | (apply keeps_step; intros ?? E; eapply any_mark_skip; right; right; right; right; exact E)
    | (apply keeps_modify; skip_fixed_tac) ].

Local Ltac ogo1 IH :=
  first
    [ match goal with |- eqM _ (m_bind (advance _) _) => apply eqM_advance_r end
    | apply eqM_modify_adv | apply eqM_add_node_adv
    | apply eqM_track | apply eqM_announce | apply eqM_notify_write
    | apply eqM_on_node; intros ? | apply eqM_on_value; intros ?
    | apply eqM_hosted | apply eqM_untracked | apply eqM_detached
    | oih IH
    | apply eqM_bind; [|intros ?]
    | match goal with |- eqM (match ?x with _ => _ end) _ => destruct x end
    | match goal with |- eqM (if ?x then _ else _) _ => destruct x end
    | oleaf
    | progress cbv beta ].

Local Ltac ogo IH := repeat ogo1 IH.

Lemma osims_0 : osims 0.
Proof. split; intros; apply eqM_refl, keeps_stuck. Qed.

Lemma osims_S f : osims f -> osims (S f).
Proof.
  intros IH. split; intros.
  all: lazymatch goal with
       | |- eqM (exec _ _ ?c) _ => destruct c; cbn [exec oexec]
       | |- eqM (teardown _ _ ?ls ?cs) _ => destruct ls as [|? ?], cs as [|[[? ?] ?] ?]; cbn [teardown oteardown]
       | |- _ => cbn [read oread read_node oread_node update_if_necessary oupdate_if_necessary
                      refresh_or_pull orefresh_or_pull pull opull refresh orefresh resolve_walk oresolve_walk
                      recompute orecompute run_memo orun_memo execute_effect oexecute_effect run_effect orun_effect
                      run_effect_body orun_effect_body discharge odischarge retire oretire dispose_scope odispose_scope
                      run_cleanup orun_cleanup release orelease dispose_node odispose_node attach oattach
                      write_signal owrite_signal request_flush orequest_flush settle_flush osettle_flush
                      run_flush orun_flush drain odrain]
       end.
  all: ogo IH.
Qed.

Lemma osims_all f : osims f.
Proof. induction f; [apply osims_0|by apply osims_S]. Qed.

Lemma with_skip_off_init fl : with_skip SkipOff (init fl) = init fl.
Proof. reflexivity. Qed.

(**md
**Under `SkipOff` the optimised engine is the present engine.**

The trace of `trace_of_mode` in mode `SkipOff` is the trace of `trace_of`, at every fuel and flags.

- [k:] the fuel.
- [fl:] the flags.
- [c:] the program.
*)
Theorem trace_of_mode_off k fl c : trace_of_mode k fl SkipOff c = trace_of k fl c.
Proof.
  unfold trace_of_mode, trace_of, run_top. rewrite with_skip_off_init.
  by rewrite <- (proj1 (Oexec _ (osims_all k) [] c) (init fl) eq_refl).
Qed.

(**md
**Under `SkipOff` the optimised reference engine is the present one.**

- [k:] the fuel.
- [p:] the program.
- [h:] the history.
*)
Corollary engine_of_mode_off k p h : engine_of_mode SkipOff k p h = Refinement.engine_of current_flags k p h.
Proof. unfold engine_of_mode, Refinement.engine_of. by rewrite trace_of_mode_off. Qed.

(** ** Saturation lemmas *)

Lemma set_node_same n d s : g_nodes s !! n = Some d -> set_node n d s = s.
Proof. intros Hd. destruct s; unfold set_node; simpl in *. f_equal. by apply insert_id. Qed.

(**md
**A `Dirty` mark of a saturated node changes nothing.**

- [s:] the state.
- [c:] the saturated node.
- [f:] the fuel of the mark, less one.
*)
Theorem mark_dirty_saturated_id s c f : saturated s c -> mark_dirty (S f) c s = Some s.
Proof.
  unfold saturated, mark_dirty. destruct (g_nodes s !! c) as [d|] eqn:Hd; [|done].
  destruct (n_kind d) eqn:Hk; [done| |].
  - intros [Hf Hu]. rewrite decide_False by (rewrite Hf; discriminate).
    rewrite Hu, decide_True by exact Hf. done.
  - intros Hs. unfold effect_marked. f_equal.
    destruct (n_disposed d) eqn:Hdi; [done|]. destruct (n_in_cleanup d) eqn:Hc; [done|]. simpl.
    destruct Hs as [?|[?|[Hf Hq]]]; [congruence|congruence|]. rewrite Hq.
    replace (node_marked Dirty (n_unannounced d) true d) with d; [by apply set_node_same|].
    destruct d; simpl in *; by subst.
Qed.

Lemma prim_keeps_saturated s s' c : mark_prim s s' -> saturated s c -> saturated s' c.
Proof.
  intros Hp. unfold saturated.
  destruct Hp as [s n d f u Hd Hk Hf Hu|s n d f Hd Hk Hq Hf|s n d f Hd Hk Hq Hf]; simpl.
  all: destruct (decide (c = n)) as [->|Hne];
    [rewrite lookup_insert_eq, Hd; simpl; rewrite Hk|rewrite lookup_insert_ne by done].
  all: try (destruct (g_nodes s !! c) as [e|]; [|done]; destruct (n_kind e); unfold memo_saturated, announce_due; simpl; auto).
  - unfold memo_saturated, announce_due. simpl. intros [H1 H2]. split.
    + rewrite H1 in Hf. destruct f; unfold freshness_le in Hf; simpl in Hf; [lia|lia|done].
    + destruct Hu as [->| ->]; [exact H2|by rewrite andb_false_r].
  - unfold effect_saturated. simpl. intros [H|[H|[H1 H2]]]; [by left|by right; left|right; right; split; [|done]].
    rewrite H1 in Hf. destruct f; unfold freshness_le in Hf; simpl in Hf; [lia|lia|done].
  - unfold effect_saturated. simpl. intros [H|[H|[H1 H2]]]; [by left|by right; left|right; right; split; [|done]].
    rewrite H1 in Hf. destruct f; unfold freshness_le in Hf; simpl in Hf; [lia|lia|done].
Qed.

Lemma rtc_keeps_saturated s s' c : rtc mark_prim s s' -> saturated s c -> saturated s' c.
Proof. induction 1; eauto using prim_keeps_saturated. Qed.

(**md
**Marks keep every saturated node saturated.**

- [f:] the fuel.
- [n:] the node or source marked.
- [r:] the current computation skipped by `notify_dirty_except`, or `None`.
- [s:] the state before the marking.
- [s':] the state after the marking.
- [c:] a node saturated in `s`.
*)
Theorem marks_keep_saturated f n r s s' c : any_mark f n r s s' -> saturated s c -> saturated s' c.
Proof. intros H. apply rtc_keeps_saturated. by eapply any_mark_rtc. Qed.

(**md
**A `Dirty` mark leaves its node saturated, unless the node is a clean memo holding a due announcement.**

The exception is the memo of `RecomputeScoped` (Core.fs 4191) and the reason the record checks
`all_saturated`.

- [f:] the fuel of the mark, less one.
- [c:] the node marked.
- [s:] the state before the mark.
- [s':] the state after the mark.
*)
Theorem mark_dirty_saturates_or_window f c s s' :
  mark_dirty (S f) c s = Some s' ->
  saturated s' c \/
  exists d, g_nodes s !! c = Some d /\ n_kind d = KMemo /\ n_freshness d = Clean /\ announce_due s d = true.
Proof.
  intros Hm. unfold mark_dirty in Hm. cbv iota beta in Hm.
  destruct (g_nodes s !! c) as [d|] eqn:Hd.
  2: { injection Hm as <-. left. unfold saturated. by rewrite Hd. }
  destruct (n_kind d) eqn:Hk.
  - injection Hm as <-. left. unfold saturated. by rewrite Hd, Hk.
  - destruct (decide (n_freshness d = Clean)) as [Hc|Hc].
    + destruct (announce_due s d) eqn:Hdue; [right; by exists d|left].
      eapply (marks_keep_saturated f c None); [right; right; left; exact Hm|].
      unfold saturated, memo_saturated, announce_due in *. simpl. rewrite lookup_insert_eq. simpl.
      rewrite Hk. split; [done|exact Hdue].
    + destruct (announce_due s d) eqn:Hdue.
      * left. eapply (marks_keep_saturated f c None); [right; right; left; exact Hm|].
        unfold saturated, memo_saturated, announce_due. simpl. rewrite lookup_insert_eq. simpl.
        rewrite Hk. split; [done|apply andb_false_r].
      * left. destruct (decide (n_freshness d = Dirty)) as [Hdi|Hdi].
        -- injection Hm as <-. unfold saturated, memo_saturated. by rewrite Hd, Hk.
        -- injection Hm as <-. unfold saturated, memo_saturated, announce_due in *. simpl.
           rewrite lookup_insert_eq. simpl. rewrite Hk. split; [done|exact Hdue].
  - injection Hm as <-. left. unfold effect_marked, saturated, effect_saturated.
    destruct (n_disposed d) eqn:Hdi; [simpl; rewrite Hd, Hk; by left|].
    destruct (n_in_cleanup d) eqn:Hci; [simpl; rewrite Hd, Hk; by right; left|].
    simpl. destruct (n_queued d); simpl; rewrite lookup_insert_eq; simpl; rewrite Hk; by right; right.
Qed.

Lemma walk_saturated_id f src i s :
  (forall c, observer_of s src c -> saturated s c) -> walk_marks (mark_dirty (S f)) src i s = Some s.
Proof.
  intros Hs. induction i as [|i IH]; cbn [walk_marks]; [done|].
  destruct (decide _) as [_|Hlt]; [exact IH|].
  destruct (observer_items (observers_of (g_edges s) src) !! i) as [[c slot]|] eqn:Hc.
  - rewrite mark_dirty_saturated_id; [exact IH|]. apply Hs. unfold observer_of. apply view_elem. eauto.
  - destruct (lookup_lt_is_Some_2 (observer_items (observers_of (g_edges s) src)) i) as [x Hx]; [lia|]. congruence.
Qed.

(**md
**A stamped signal's walk changes nothing.**

Under `stamp_ok`, the `notify_dirty` of a signal with a current stamp returns its starting state.

- [s:] the state.
- [src:] the signal.
- [f:] the fuel of the walk, less one.
*)
Theorem notify_dirty_stamped_id s src f : stamp_ok s -> stamp_current s src -> notify_dirty (S f) src s = Some s.
Proof. intros Hok Hc. apply walk_saturated_id. intros c Ho. by apply (Hok src c). Qed.

(** ** The unchecked record is unsound inside [RecomputeScoped]

    [RecomputeScoped] (Core.fs 4191) writes [freshness <- Clean] and leaves [unannounced] as it is. A
    memo that is [Clean] with [unannounced] set takes [Dirty] from [MarkDirty] (4483-4498) and keeps
    the flag, so it ends unsaturated. A record after every walk, as [SkipUnchecked] makes, then claims a
    saturation the memo lacks. [SkipAll] checks [all_saturated] and records nothing there. *)

(** A memo in the window of [RecomputeScoped]: [Clean], holding an announcement. *)
Definition window_memo : node := node_marked Clean true false (memo_node Owning CmpEq (CRet (EConst 0%Z)) []).

(** Memo 2 observes signal 1. *)
Definition window_world : EdgeWorld := default empty_world (sources_add 2 1 (sources_begin_run 2 empty_world)).

(** Signal 1 observed by [window_memo], under [SkipUnchecked] with no stamp. *)
Definition window_state : state :=
  {| g_flags := current_flags; g_edges := window_world;
     g_nodes := <[1 := signal_node 0%Z]> {[2 := window_memo]};
     g_owners := ∅; g_queue := []; g_queue_head := 0; g_flushing := false; g_flush_owed := false;
     g_batch_depth := 0; g_pull_depth := 0; g_current := None; g_untracked := false; g_current_owner := None;
     g_next_id := 2; g_trace := []; g_running := []; g_ghost := empty_ghost; g_registered := ∅;
     g_skip := SkipUnchecked; g_epoch := 3; g_stamps := ∅ |}.

Lemma window_mark : (fun s' => bool_decide (saturated s' 2)) <$> mark_dirty 10 2 window_state = Some false.
Proof. vm_compute. reflexivity. Qed.

(**md
**A `Dirty` mark leaves a clean memo holding an announcement unsaturated.**

`mark_dirty_saturates`, a lemma of the plan for the record's soundness, is false.
*)
Theorem mark_dirty_saturates_falsified : ~ (forall f c s s', mark_dirty f c s = Some s' -> saturated s' c).
Proof.
  intros H. pose proof window_mark as W.
  destruct (mark_dirty 10 2 window_state) as [s'|] eqn:E; [|done]. simpl in W. injection W as W.
  apply bool_decide_eq_false_1 in W. exact (W (H _ _ _ _ E)).
Qed.

Lemma window_facts :
  (fun s' => (g_skip s', bool_decide (stamp_current s' 1), bool_decide (observer_of s' 1 2),
              bool_decide (saturated s' 2), skip_due 10 1 s', (fun d => n_unannounced d) <$> g_nodes s' !! 2,
              (fun x => n_unannounced <$> g_nodes x !! 2) <$> notify_dirty 10 1 s'))
    <$> notify_write 10 1 window_state
  = Some (SkipUnchecked, true, true, false, true, Some true, Some (Some false)).
Proof. vm_compute. reflexivity. Qed.

(**md
**An unchecked record after a walk to a clean memo holding an announcement is unsound.**

From `window_state`, under `SkipUnchecked`, the walk of a write records the signal's stamp and leaves
its observer, memo 2, unsaturated: `stamp_ok` fails. The next write skips, where the present engine's
walk spends memo 2's announcement, so the two engines' states differ.
*)
Theorem unchecked_record_unsound :
  exists s', notify_write 10 1 window_state = Some s' /\ g_skip s' = SkipUnchecked /\ stamp_current s' 1 /\
    observer_of s' 1 2 /\ ~ saturated s' 2 /\ ~ stamp_ok s' /\
    notify_write 10 1 s' = Some s' /\ notify_dirty 10 1 s' <> Some s'.
Proof.
  pose proof window_facts as W.
  destruct (notify_write 10 1 window_state) as [s'|] eqn:E; [|done]. simpl in W.
  destruct (g_nodes s' !! 2) as [d|] eqn:Hd; [|done].
  destruct (notify_dirty 10 1 s') as [x|] eqn:Ex; [|done]. simpl in W.
  injection W as Hsk Hst Hob Hsat Hdue Hu Hx.
  apply bool_decide_eq_true_1 in Hst, Hob. apply bool_decide_eq_false_1 in Hsat.
  exists s'. split_and!; try done.
  - intros Hok. apply Hsat. by apply (Hok 1).
  - assert (Hd2 : skip_due 10 1 s' = true) by exact Hdue. unfold notify_write. by rewrite Hd2.
  - intros Heq. rewrite Ex in Heq. injection Heq as Heq. subst x. rewrite Hd in Hx. simpl in Hx. congruence.
Qed.

Lemma window_checked_facts :
  (fun s' => (bool_decide (stamp_current s' 1), skip_due 10 1 s')) <$> notify_write 10 1 (with_skip SkipAll window_state)
  = Some (false, false).
Proof. vm_compute. reflexivity. Qed.

(**md
**The checked record withholds the stamp from the window.**

From `window_state` under `SkipAll`, the write's walk leaves memo 2 unsaturated, so it records no stamp,
and the next write walks.
*)
Theorem checked_record_withheld :
  exists s', notify_write 10 1 (with_skip SkipAll window_state) = Some s' /\ ~ stamp_current s' 1 /\
    skip_due 10 1 s' = false.
Proof.
  pose proof window_checked_facts as W.
  destruct (notify_write 10 1 (with_skip SkipAll window_state)) as [s'|]; [|discriminate W].
  cbn [fmap option_fmap option_map] in W. injection W as H1 H2.
  exists s'. split_and!; [done| |done]. by apply bool_decide_eq_false_1 in H1.
Qed.

(** A memo [M] (node 2) of signal [s] (node 1) whose cleanup writes [s] twice and disposes [M], and
    whose body writes [s] while [s < 1]. The host reads [M] twice. The second read runs the cleanup
    inside [RecomputeScoped], with [M] [Clean] and holding the announcement of its first run. *)
Open Scope Z_scope.
Definition prog_window : cmd :=
  CSignal 0 (EConst 0)
  (CMemo 1 Owning CmpEq
     (CCleanup (CWrite (EVar 0) (EConst 10) (CWrite (EVar 0) (EConst 20) (CDispose (EVar 1) (CRet (EConst 0)))))
        (CRead 2 (EVar 0)
           (CIf (ELt (EVar 2) (EConst 1)) (CWrite (EVar 0) (EAdd (EVar 2) (EConst 1)) (CRet (EVar 2)))
              (CRet (EVar 2)))))
     (CRead 3 (EVar 1) (CRead 4 (EVar 1) (CRet (EConst 0))))).
Close Scope Z_scope.

Lemma prog_window_runs :
  (fun r => (g_trace r.1, n_unannounced <$> g_nodes r.1 !! 2)) <$> exec 1000 [] prog_window (init current_flags) =
    Some ([EvHostRead 2 (Ok 0%Z); EvCleanup 2; EvDispose 2; EvHostRead 2 (Ok 0%Z)], Some false) /\
  (fun r => (g_trace r.1, n_unannounced <$> g_nodes r.1 !! 2))
    <$> oexec 1000 [] prog_window (with_skip SkipUnchecked (init current_flags)) =
    Some ([EvHostRead 2 (Ok 0%Z); EvCleanup 2; EvDispose 2; EvHostRead 2 (Ok 0%Z)], Some true) /\
  (fun r => (g_trace r.1, n_unannounced <$> g_nodes r.1 !! 2))
    <$> oexec 1000 [] prog_window (with_skip SkipAll (init current_flags)) =
    Some ([EvHostRead 2 (Ok 0%Z); EvCleanup 2; EvDispose 2; EvHostRead 2 (Ok 0%Z)], Some false).
Proof. split_and!; vm_compute; reflexivity. Qed.

(**md
**The unchecked record breaks the simulation on a reachable program.**

Under `SkipUnchecked`, `prog_window` ends with the present engine's trace and a state that differs from
the present engine's beyond the fields of the skip: memo 2 keeps the announcement its present-engine copy
spends. `SkipAll` ends in `sim`: `prog_window_checked`.
*)
Theorem unchecked_record_breaks_sim :
  exists s o r1 r2, exec 1000 [] prog_window (init current_flags) = Some (s, r1) /\
    oexec 1000 [] prog_window (with_skip SkipUnchecked (init current_flags)) = Some (o, r2) /\
    g_trace s = g_trace o /\ strip s <> strip o.
Proof.
  destruct prog_window_runs as (W1 & W2 & _).
  destruct (exec 1000 [] prog_window (init current_flags)) as [[s r1]|]; [|discriminate W1].
  destruct (oexec 1000 [] prog_window (with_skip SkipUnchecked (init current_flags))) as [[o r2]|];
    [|discriminate W2].
  cbn [fmap option_fmap option_map fst] in W1, W2. injection W1 as T1 U1. injection W2 as T2 U2.
  exists s, o, r1, r2. split; [reflexivity|]. split; [reflexivity|]. split; [congruence|].
  intros Hst. assert (Hn : g_nodes s = g_nodes o) by exact (f_equal g_nodes Hst).
  rewrite Hn in U1. congruence.
Qed.

(** ** Why the advances at 4825 and 4290 are needed

    Under [SkipLeaveDirty] only the writes that leave [Dirty] advance the epoch. *)

(** Effect [E] (node 2) reads signal [s] (node 1), and its cleanup writes [s]. The host writes [s]
    twice, and each write flushes. *)
Open Scope Z_scope.
Definition prog_cleanup_rewrite : cmd :=
  CSignal 0 (EConst 0)
  (CEffect 1 (CCleanup (CWrite (EVar 0) (EConst 100) (CRet (EConst 0)))
                (CRead 2 (EVar 0) (CLog (EVar 2) (CRet (EConst 0)))))
  (CWrite (EVar 0) (EConst 1) (CWrite (EVar 0) (EConst 2) (CRet (EConst 0))))).

(** [prog_cleanup_rewrite] up to the second write. *)
Definition prog_cleanup_rewrite_head : cmd :=
  CSignal 0 (EConst 0)
  (CEffect 1 (CCleanup (CWrite (EVar 0) (EConst 100) (CRet (EConst 0)))
                (CRead 2 (EVar 0) (CLog (EVar 2) (CRet (EConst 0)))))
  (CWrite (EVar 0) (EConst 1) (CRet (EConst 0)))).
Close Scope Z_scope.

(**md
**Without the advance at 4825 a cleanup's write hides the next write from its effect.**

The cleanup's write records while the effect is in its cleanups, and the effect leaves them with the
stamp current. Under `SkipLeaveDirty` the host's second write skips, and the trace has no second run
of `E`. `SkipOff` and `SkipAll` agree. The state ahead of that write fails `stamp_ok`.
*)
Example leave_dirty_cleanup_skip :
  trace_of_mode 3000 current_flags SkipLeaveDirty prog_cleanup_rewrite =
    Some [EvRun 2; EvLog 2 0%Z; EvCleanup 2; EvRun 2; EvLog 2 100%Z] /\
  trace_of_mode 3000 current_flags SkipOff prog_cleanup_rewrite =
    Some [EvRun 2; EvLog 2 0%Z; EvCleanup 2; EvRun 2; EvLog 2 100%Z; EvCleanup 2; EvRun 2; EvLog 2 100%Z] /\
  trace_of_mode 3000 current_flags SkipAll prog_cleanup_rewrite =
    trace_of_mode 3000 current_flags SkipOff prog_cleanup_rewrite /\
  (fun r => (bool_decide (stamp_current r.1 1), bool_decide (observer_of r.1 1 2), bool_decide (saturated r.1 2)))
    <$> oexec 3000 [] prog_cleanup_rewrite_head (with_skip SkipLeaveDirty (init current_flags)) =
    Some (true, true, false).
Proof. split_and!; vm_compute; reflexivity. Qed.

(** Memo [M] (node 2) reads signal [s] (node 1), writes [s + 1] while [s < 1], and answers [s / 10]
    otherwise. Memo [N] (node 3) reads [M]. The host reads [N] between writes of [s]. *)
Open Scope Z_scope.
Definition prog_self_write_announce_body (k : cmd) : cmd :=
  CSignal 0 (EConst 0)
  (CMemo 1 Pure CmpEq
     (CRead 2 (EVar 0) (CIf (ELt (EVar 2) (EConst 1)) (CWrite (EVar 0) (EAdd (EVar 2) (EConst 1)) (CRet (EConst 0)))
                          (CRet (EDiv (EVar 2) (EConst 10)))))
  (CMemo 5 Pure CmpEq (CRead 6 (EVar 1) (CRet (EVar 6))) k)).

Definition read_n (k : cmd) : cmd := CRead 7 (EVar 5) k.
Definition write_s (v : val) (k : cmd) : cmd := CWrite (EVar 0) (EConst v) k.

Definition prog_self_write_announce : cmd :=
  prog_self_write_announce_body
    (read_n (read_n (write_s 0 (read_n (write_s 20 (read_n (CRet (EConst 0)))))))).

(** [prog_self_write_announce] up to the write of [20]. *)
Definition prog_self_write_announce_head : cmd :=
  prog_self_write_announce_body (read_n (read_n (write_s 0 (read_n (CRet (EConst 0)))))).
Close Scope Z_scope.

(**md
**Without the advance at 4290 a stale memo's announcement is lost.**

`M`'s run, pulled by the host's read of `N`, writes `s`, records, and ends stale with an unchanged
value, so `N` resolves `Clean` over it. Its announcement then makes `M` unsaturated with no advance.
Under `SkipLeaveDirty` the write of 20 skips, which drops the `NotifyCheck` the announcement owes `N`,
and the last host read answers the stale 0. `SkipOff` and `SkipAll` answer 2. The state ahead of that
write fails `stamp_ok`. The plan's version, with an effect reading `M`, shows no difference: each
`Execute` advances at 4807 after the walk that records.
*)
Example leave_dirty_unannounced_skip :
  trace_of_mode 3000 current_flags SkipLeaveDirty prog_self_write_announce =
    Some [EvHostRead 3 (Ok 0%Z); EvHostRead 3 (Ok 0%Z); EvHostRead 3 (Ok 0%Z); EvHostRead 3 (Ok 0%Z)] /\
  trace_of_mode 3000 current_flags SkipOff prog_self_write_announce =
    Some [EvHostRead 3 (Ok 0%Z); EvHostRead 3 (Ok 0%Z); EvHostRead 3 (Ok 0%Z); EvHostRead 3 (Ok 2%Z)] /\
  trace_of_mode 3000 current_flags SkipAll prog_self_write_announce =
    trace_of_mode 3000 current_flags SkipOff prog_self_write_announce /\
  (fun r => (bool_decide (stamp_current r.1 1), bool_decide (observer_of r.1 1 2), bool_decide (saturated r.1 2)))
    <$> oexec 3000 [] prog_self_write_announce_head (with_skip SkipLeaveDirty (init current_flags)) =
    Some (true, true, false).
Proof. split_and!; vm_compute; reflexivity. Qed.

(** ** The simulation *)

(** Every stamp holds the present epoch or an earlier one. *)
Definition stamps_le (o : state) : Prop := forall src e, g_stamps o !! src = Some e -> e <= g_epoch o.

(** [sim] with the mode of [s] left free and bounded stamps: the relation the proof carries. *)
Definition rel (s o : state) : Prop := strip s = strip o /\ g_skip o = SkipAll /\ stamp_ok o /\ stamps_le o.

Definition rel_res {A} (r1 r2 : option (state * A)) : Prop :=
  match r1, r2 with
  | None, None => True
  | Some (s', a), Some (o', b) => a = b /\ rel s' o'
  | _, _ => False
  end.

(** From related states, [m1] on the present engine's state and [m2] on the optimised one agree. *)
Definition simM {A} (m1 m2 : M A) : Prop := forall s o, rel s o -> rel_res (m1 s) (m2 o).

Lemma simM_bind {A B} (m1 m2 : M A) (k1 k2 : A -> M B) :
  simM m1 m2 -> (forall a, simM (k1 a) (k2 a)) -> simM (m_bind m1 k1) (m_bind m2 k2).
Proof.
  intros H Hk s o R. unfold m_bind. specialize (H s o R).
  destruct (m1 s) as [[s1 a]|]; destruct (m2 o) as [[o1 b]|]; simpl in H; try contradiction; [|done].
  destruct H as [<- R1]. by apply Hk.
Qed.

Lemma strip_nodes s o : strip s = strip o -> g_nodes s = g_nodes o.
Proof. intros E. exact (f_equal g_nodes E). Qed.
Lemma strip_edges s o : strip s = strip o -> g_edges s = g_edges o.
Proof. intros E. exact (f_equal g_edges E). Qed.
Lemma strip_flags s o : strip s = strip o -> g_flags s = g_flags o.
Proof. intros E. exact (f_equal g_flags E). Qed.
Lemma strip_current s o : strip s = strip o -> g_current s = g_current o.
Proof. intros E. exact (f_equal g_current E). Qed.
Lemma strip_current_owner s o : strip s = strip o -> g_current_owner s = g_current_owner o.
Proof. intros E. exact (f_equal g_current_owner E). Qed.
Lemma strip_untracked s o : strip s = strip o -> g_untracked s = g_untracked o.
Proof. intros E. exact (f_equal g_untracked E). Qed.

Lemma rel_step s o s' o' :
  rel s o -> strip s' = strip o' -> g_skip o' = g_skip o -> g_epoch o' = g_epoch o -> g_stamps o' = g_stamps o ->
  (forall c, saturated o c -> saturated o' c) -> (forall src c, observer_of o' src c -> observer_of o src c) ->
  rel s' o'.
Proof.
  intros (_ & Hk & Hok & Hle) E Hs He Ht Hsat Hobs. split_and!; [done|congruence| |].
  - intros src c Hc Ho. apply Hsat. apply (Hok src c); [|by apply Hobs].
    unfold stamp_current in *. by rewrite <- He, <- Ht.
  - intros src e H. rewrite He. apply (Hle src e). by rewrite <- Ht.
Qed.

(** Each stamp of [o] is earlier than [e]: none is current at [e]. *)
Lemma rel_advanced s o : strip s = strip o -> g_skip o = SkipAll -> stamps_le o ->
  rel s (with_epoch (S (g_epoch o)) o).
Proof.
  intros E Hk Hle. split_and!; [exact E|exact Hk| |].
  - intros src c Hc. exfalso. unfold stamp_current in Hc. simpl in Hc. pose proof (Hle _ _ Hc). lia.
  - intros src e He. simpl in *. pose proof (Hle _ _ He). lia.
Qed.

(** The state-independent part of a step's result. *)
Definition sres {A} (r : option (state * A)) : option (state * A) := (fun x => (strip x.1, x.2)) <$> r.

(** [m] reads only fields that [strip] keeps. *)
Definition old2 {A} (m : M A) : Prop := forall s o, strip s = strip o -> sres (m s) = sres (m o).

Definition oldM {A} (m : M A) : Prop := forall s, sres (m s) = sres (m (strip s)).

Lemma old2_of {A} (m : M A) : oldM m -> old2 m.
Proof. intros H s o E. by rewrite (H s), (H o), E. Qed.

(** [m] keeps the skip's fields, keeps every node saturated that was, and adds no observer. *)
Definition frame {A} (m : M A) : Prop :=
  forall o o' a, m o = Some (o', a) ->
    g_skip o' = g_skip o /\ g_epoch o' = g_epoch o /\ g_stamps o' = g_stamps o /\
    (forall c, saturated o c -> saturated o' c) /\ (forall src c, observer_of o' src c -> observer_of o src c).

Lemma simM_same {A} (m : M A) : old2 m -> frame m -> simM m m.
Proof.
  intros Hold Hf s o R. pose proof (Hold s o (proj1 R)) as H. unfold rel_res, sres in *.
  destruct (m s) as [[s' a]|]; destruct (m o) as [[o' b]|] eqn:Eo; simpl in H; try discriminate; [|done].
  assert (E : strip s' = strip o') by exact (f_equal (fun r => (default (strip s', a) r).1) H).
  assert (Hab : a = b) by exact (f_equal (fun r => (default (strip s', a) r).2) H).
  split; [done|].
  destruct (Hf _ _ _ Eo) as (F1&F2&F3&F4&F5). exact (rel_step s o s' o' R E F1 F2 F3 F4 F5).
Qed.

(** [m] changes no node, edge, flag or field of the skip. *)
Definition inert {A} (m : M A) : Prop :=
  forall o o' a, m o = Some (o', a) ->
    g_nodes o' = g_nodes o /\ g_edges o' = g_edges o /\ g_flags o' = g_flags o /\
    g_skip o' = g_skip o /\ g_epoch o' = g_epoch o /\ g_stamps o' = g_stamps o.

Lemma simM_inert {A} (m : M A) : old2 m -> inert m -> simM m m.
Proof.
  intros Hold Hin. apply simM_same; [done|].
  intros o o' a Hm. destruct (Hin _ _ _ Hm) as (Hn&He&Hf&?&?&?). split_and!; try done.
  - intros c. unfold saturated, memo_saturated, announce_due. by rewrite Hn, Hf.
  - intros src c. unfold observer_of. by rewrite He.
Qed.

Lemma strip_app (g : state -> state) s o :
  (forall x, strip (g x) = strip (g (strip x))) -> strip s = strip o -> strip (g s) = strip (g o).
Proof. intros H E. by rewrite H, (H o), E. Qed.

Local Ltac oldf_tac :=
  intros [];
  unfold ghost_read, ghost_publish, ghost_run_start, ghost_effect_start, ghost_run_end, ghost_dirty_cleared,
    ghost_resolved, ghost_registered, run_end_unnoticed, slot_holds, read_prefix;
  cbn; repeat case_match; reflexivity.

Lemma strip_owners s o : strip s = strip o -> g_owners s = g_owners o.
Proof. intros E. exact (f_equal g_owners E). Qed.
Lemma strip_next_id s o : strip s = strip o -> g_next_id s = g_next_id o.
Proof. intros E. exact (f_equal g_next_id E). Qed.

Lemma strip_set_node n d s o : strip s = strip o -> strip (set_node n d s) = strip (set_node n d o).
Proof. intros E. change (strip (set_node n d (strip s)) = strip (set_node n d (strip o))). by rewrite E. Qed.

Lemma old2_bind {A B} (m : M A) (k : A -> M B) : old2 m -> (forall a, old2 (k a)) -> old2 (m_bind m k).
Proof.
  intros Hm Hk s o E. unfold m_bind. pose proof (Hm s o E) as H. unfold sres in H.
  destruct (m s) as [[s1 a]|]; destruct (m o) as [[o1 b]|]; simpl in H; try discriminate; [|done].
  assert (E1 : strip s1 = strip o1) by exact (f_equal (fun r => (default (strip s1, a) r).1) H).
  assert (Hab : a = b) by exact (f_equal (fun r => (default (strip s1, a) r).2) H).
  subst b. by apply Hk.
Qed.

Lemma old2_return {A} (a : A) : old2 (m_return a).
Proof. intros s o E. unfold m_return, sres. simpl. by rewrite E. Qed.

Lemma old2_modify (g : state -> state) : (forall x, strip (g x) = strip (g (strip x))) -> old2 (m_modify g).
Proof. intros Hg s o E. unfold m_modify, sres. simpl. f_equal. f_equal. by apply strip_app. Qed.

Lemma old2_get_node n : old2 (get_node n).
Proof.
  intros s o E. unfold get_node, sres. rewrite (strip_nodes _ _ E).
  destruct (g_nodes o !! n); simpl; [by rewrite E|done].
Qed.

Lemma old2_get_owner x : old2 (get_owner x).
Proof.
  intros s o E. unfold get_owner, sres. rewrite (strip_owners _ _ E).
  destruct (g_owners o !! x); simpl; [by rewrite E|done].
Qed.

Lemma old2_put_owner x y : old2 (put_owner x y).
Proof. apply old2_modify. intros []; reflexivity. Qed.

Lemma old2_emit e : old2 (emit e).
Proof. apply old2_modify. intros []; reflexivity. Qed.

Lemma old2_next_id : old2 next_id.
Proof.
  intros s o E. unfold next_id, sres. simpl. rewrite (strip_next_id _ _ E). f_equal. f_equal.
  apply (strip_app (with_next_id (S (g_next_id o)))); [intros []; reflexivity|exact E].
Qed.

Lemma old2_log_label : old2 log_label.
Proof.
  intros s o E. unfold log_label, sres.
  rewrite (strip_current _ _ E), (strip_current_owner _ _ E), (strip_owners _ _ E).
  destruct (g_current o); [simpl; by rewrite E|]. destruct (g_current_owner o); simpl; by rewrite E.
Qed.

Lemma old2_note n r : old2 (note_host_read n r).
Proof.
  intros s o E. unfold note_host_read. rewrite (strip_current _ _ E), (strip_current_owner _ _ E).
  destruct (g_current o); [unfold sres; simpl; by rewrite E|].
  destruct (g_current_owner o); [|unfold sres; simpl; by rewrite E].
  destruct (decide _); [by apply old2_emit|unfold sres; simpl; by rewrite E].
Qed.

Lemma old2_modify_node n u : old2 (modify_node n u).
Proof.
  intros s o E. unfold modify_node, sres. rewrite (strip_nodes _ _ E).
  destruct (g_nodes o !! n); simpl; [|done]. f_equal. f_equal. by apply strip_set_node.
Qed.

Lemma old2_edges u : old2 (edges u).
Proof. apply old2_modify. intros []; reflexivity. Qed.

Lemma old2_edges_opt u : old2 (edges_opt u).
Proof.
  intros s o E. unfold edges_opt, sres. rewrite (strip_edges _ _ E).
  destruct (u (g_edges o)) as [w|]; simpl; [|done]. f_equal. f_equal.
  apply (strip_app (with_edges w)); [intros []; reflexivity|exact E].
Qed.

Lemma old2_push n : old2 (push_running n).
Proof. apply old2_modify. intros []; reflexivity. Qed.

Lemma old2_pop : old2 pop_running.
Proof. apply old2_modify. intros []; reflexivity. Qed.

Lemma old2_unlink l : old2 (unlink l).
Proof.
  unfold unlink. apply old2_bind; [apply old2_get_owner|intros x].
  destruct (decide _); [apply old2_put_owner|apply old2_return].
Qed.

Lemma old2_record_error x y : old2 (record_error x y).
Proof.
  unfold record_error. apply old2_bind; [apply old2_get_owner|intros z].
  destruct (o_sink z); [apply old2_bind; [apply old2_get_owner|intros w]|]; apply old2_put_owner.
Qed.

Lemma old2_current_owner : old2 current_owner.
Proof.
  intros s o E. unfold current_owner, sres.
  rewrite (strip_current_owner _ _ E), (strip_current _ _ E), (strip_nodes _ _ E), (strip_owners _ _ E).
  destruct (g_current_owner o); [cbn [fmap option_fmap option_map fst snd]; by rewrite E|].
  destruct (g_current o) as [h|]; [|done].
  destruct (g_nodes o !! h) as [d|]; [|done].
  destruct (n_scope d).
  { cbn [fmap option_fmap option_map fst snd]. f_equal. f_equal.
    apply (strip_app (with_current_owner _)); [intros []; reflexivity|exact E]. }
  case_match; cbn [fmap option_fmap option_map fst snd]; f_equal; f_equal.
  - by apply strip_set_node.
  - apply (strip_app (fun x => with_current_owner _ (set_node h _ (with_owners _ x)))); [intros []; reflexivity|exact E].
Qed.

Local Ltac old2_tac :=
  first
    [ apply old2_return | apply old2_get_node | apply old2_get_owner | apply old2_put_owner | apply old2_emit
    | apply old2_next_id | apply old2_log_label | apply old2_note | apply old2_push | apply old2_pop
    | apply old2_unlink | apply old2_record_error
    | (apply old2_modify; first [intros []; reflexivity | oldf_tac]) ].

Local Ltac inert_tac :=
  intros ? ? ? Hm;
  unfold unlink, record_error, note_host_read, log_label, next_id, push_running, pop_running, put_owner, emit,
    get_owner, get_node, m_bind, m_modify, m_return, ghost_registered, ghost_run_start, ghost_effect_start,
    ghost_run_end, ghost_dirty_cleared, ghost_resolved, ghost_publish in Hm;
  repeat (case_match; simplify_eq/=); simplify_eq/=; split_and!; reflexivity.

Lemma simM_return {A} (a : A) : simM (m_return a) (m_return a).
Proof. intros s o R. by split. Qed.

Lemma simM_stuck {A} : simM (@stuck A) (@stuck A).
Proof. by intros ???. Qed.

Lemma simM_get {A} (k1 k2 : state -> M A) :
  (forall x, k1 x = k1 (strip x)) -> (forall x, k2 x = k2 (strip x)) -> (forall x, simM (k1 x) (k2 x)) ->
  simM (m_bind m_get k1) (m_bind m_get k2).
Proof. intros H1 H2 H s o R. unfold m_bind, m_get. rewrite H1, H2, <- (proj1 R). by apply H. Qed.

Lemma simM_on_value e x (k1 k2 : val -> M outcome) :
  (forall v, simM (k1 v) (k2 v)) -> simM (on_value e x k1) (on_value e x k2).
Proof.
  intros Hk s o R. unfold on_value. destruct (eval_expr _ _ _) as [[v|y]|]; [by apply Hk|by split|done].
Qed.

Lemma simM_on_node e x (k1 k2 : node_id -> M outcome) :
  (forall t, simM (k1 t) (k2 t)) -> simM (on_node e x k1) (on_node e x k2).
Proof. intros Hk. apply simM_on_value. intros z. destruct (decide _); [apply Hk|apply simM_stuck]. Qed.

Lemma rel_map (g : state -> state) s o :
  (forall x, strip (g x) = strip (g (strip x))) ->
  (forall x, g_nodes (g x) = g_nodes x /\ g_edges (g x) = g_edges x /\ g_flags (g x) = g_flags x /\
     g_skip (g x) = g_skip x /\ g_epoch (g x) = g_epoch x /\ g_stamps (g x) = g_stamps x) ->
  rel s o -> rel (g s) (g o).
Proof.
  intros Hold Hin R. destruct (Hin o) as (Hn&He&Hf&?&?&?). eapply rel_step; [exact R| | done | done | done | |].
  - apply strip_app; [exact Hold|exact (proj1 R)].
  - intros c. unfold saturated, memo_saturated, announce_due. by rewrite Hn, Hf.
  - intros src c. unfold observer_of. by rewrite He.
Qed.

Lemma simM_modify (g : state -> state) :
  (forall x, strip (g x) = strip (g (strip x))) ->
  (forall x, g_nodes (g x) = g_nodes x /\ g_edges (g x) = g_edges x /\ g_flags (g x) = g_flags x /\
     g_skip (g x) = g_skip x /\ g_epoch (g x) = g_epoch x /\ g_stamps (g x) = g_stamps x) ->
  simM (m_modify g) (m_modify g).
Proof. intros Hold Hin s o R. split; [done|]. by apply rel_map. Qed.

Local Ltac map_tac :=
  first
    [ intros []; reflexivity
    | oldf_tac
    | intros ?; split_and!; reflexivity
    | intros ?; unfold ghost_registered, ghost_run_start, ghost_effect_start, ghost_run_end, ghost_dirty_cleared,
        ghost_resolved, ghost_publish; repeat case_match; split_and!; reflexivity ].

(** The wrappers, with the fields they restore as arguments. *)
Definition hosted_at {A} (n : node_id) (c : option node_id) (co : option owner_id) (body : M A) : M A :=
  let* _ := m_modify (fun s => with_current_owner None (with_current (Some n) s)) in
  let* r := body in
  let* _ := m_modify (fun s => with_current_owner co (with_current c s)) in
  m_return r.

Definition untracked_at {A} (flag u : bool) (c : option node_id) (body : M A) : M A :=
  let* _ := m_modify (fun s => with_untracked flag (with_current None s)) in
  let* r := body in
  let* _ := m_modify (fun s => with_untracked u (with_current c s)) in
  m_return r.

Definition detached_at (o : owner_id) (co : option owner_id) (c : option node_id) (body : M unit) : M unit :=
  let* _ := m_modify (fun s => with_batch_depth (S (g_batch_depth s)) (with_current_owner (Some o) (with_current None s))) in
  let* _ := body in
  m_modify (fun s => with_batch_depth (pred (g_batch_depth s)) (with_current_owner co (with_current c s))).

Lemma simM_hosted {A} n (b1 b2 : M A) : simM b1 b2 -> simM (hosted n b1) (hosted n b2).
Proof.
  intros Hb s o R.
  change (rel_res (hosted_at n (g_current s) (g_current_owner s) b1 s) (hosted_at n (g_current o) (g_current_owner o) b2 o)).
  rewrite (strip_current _ _ (proj1 R)), (strip_current_owner _ _ (proj1 R)).
  generalize (g_current o) (g_current_owner o). intros c co. revert s o R.
  change (simM (hosted_at n c co b1) (hosted_at n c co b2)). unfold hosted_at.
  apply simM_bind; [apply simM_modify; map_tac|intros _].
  apply simM_bind; [exact Hb|intros r]. apply simM_bind; [apply simM_modify; map_tac|intros _]. apply simM_return.
Qed.

Lemma simM_untracked {A} (b1 b2 : M A) : simM b1 b2 -> simM (untracked b1) (untracked b2).
Proof.
  intros Hb s o R.
  change (rel_res (untracked_at (match g_current s with Some _ => true | None => g_untracked s end) (g_untracked s) (g_current s) b1 s)
                  (untracked_at (match g_current o with Some _ => true | None => g_untracked o end) (g_untracked o) (g_current o) b2 o)).
  rewrite (strip_current _ _ (proj1 R)), (strip_untracked _ _ (proj1 R)).
  generalize (match g_current o with Some _ => true | None => g_untracked o end). intros fl.
  generalize (g_untracked o) (g_current o). intros u c. revert s o R.
  change (simM (untracked_at fl u c b1) (untracked_at fl u c b2)). unfold untracked_at.
  apply simM_bind; [apply simM_modify; map_tac|intros _].
  apply simM_bind; [exact Hb|intros r]. apply simM_bind; [apply simM_modify; map_tac|intros _]. apply simM_return.
Qed.

Lemma simM_detached o (b1 b2 : M unit) : simM b1 b2 -> simM (detached o b1) (detached o b2).
Proof.
  intros Hb s x R.
  change (rel_res (detached_at o (g_current_owner s) (g_current s) b1 s) (detached_at o (g_current_owner x) (g_current x) b2 x)).
  rewrite (strip_current _ _ (proj1 R)), (strip_current_owner _ _ (proj1 R)).
  generalize (g_current_owner x) (g_current x). intros co c. revert s x R.
  change (simM (detached_at o co c b1) (detached_at o co c b2)). unfold detached_at.
  apply simM_bind; [apply simM_modify; map_tac|intros _].
  apply simM_bind; [exact Hb|intros _]. apply simM_modify; map_tac.
Qed.

(** *** Saturation of a node record *)

(** [saturated] of a stored node. *)
Definition node_sat (fl : flags) (d : node) : Prop :=
  match n_kind d with
  | KMemo => n_freshness d = Dirty /\ f_unannounced fl && n_unannounced d = false
  | KEffect => effect_saturated d
  | KSignal => True
  end.

Lemma saturated_node s n :
  saturated s n = match g_nodes s !! n with Some d => node_sat (g_flags s) d | None => True end.
Proof. reflexivity. Qed.

Lemma sat_keep_insert o o' n d d' :
  g_nodes o !! n = Some d -> g_nodes o' = <[n := d']> (g_nodes o) -> g_flags o' = g_flags o ->
  (node_sat (g_flags o) d -> node_sat (g_flags o) d') -> forall c, saturated o c -> saturated o' c.
Proof.
  intros Hd Hn Hf Hs c. rewrite !saturated_node, Hn, Hf. destruct (decide (c = n)) as [->|Hne].
  - rewrite lookup_insert_eq, Hd. exact Hs.
  - by rewrite lookup_insert_ne.
Qed.

Lemma modify_at n u s o d :
  rel s o -> g_nodes o !! n = Some d -> (node_sat (g_flags o) d -> node_sat (g_flags o) (u d)) ->
  rel_res (modify_node n u s) (modify_node n u o).
Proof.
  intros R Hd Hu. unfold modify_node. rewrite (strip_nodes _ _ (proj1 R)), Hd. split; [done|].
  eapply rel_step; [exact R| |done|done|done| |].
  - change (strip (set_node n (u d) (strip s)) = strip (set_node n (u d) (strip o))). by rewrite (proj1 R).
  - by eapply sat_keep_insert.
  - intros src c H. exact H.
Qed.

Lemma simM_modify_node n u : (forall fl d, node_sat fl d -> node_sat fl (u d)) -> simM (modify_node n u) (modify_node n u).
Proof.
  intros Hu s o R. destruct (g_nodes o !! n) as [d|] eqn:Hd.
  - apply (modify_at n u s o d R Hd). apply Hu.
  - unfold modify_node. by rewrite (strip_nodes _ _ (proj1 R)), Hd.
Qed.

Local Ltac keep_tac :=
  let fl := fresh "fl" in let d := fresh "d" in
  intros fl d; unfold node_sat, effect_saturated; simpl; destruct (n_kind d); intuition (auto || congruence).

(** *** Each node write keeps saturation or advances *)

(** The writes of [Engine.v] that keep every node's saturation. A write keeps it when [node_sat] of
    the new record follows from [node_sat] of the old one. *)
Lemma keeps_value v : forall fl d, node_sat fl d -> node_sat fl (node_with_value v d).
Proof. keep_tac. Qed.
Lemma keeps_published v : forall fl d, node_sat fl d -> node_sat fl (node_with_published v d).
Proof. keep_tac. Qed.
Lemma keeps_status v : forall fl d, node_sat fl d -> node_sat fl (node_with_status v d).
Proof. keep_tac. Qed.
Lemma keeps_failure v : forall fl d, node_sat fl d -> node_sat fl (node_with_failure v d).
Proof. keep_tac. Qed.
Lemma keeps_runs v : forall fl d, node_sat fl d -> node_sat fl (node_with_runs v d).
Proof. keep_tac. Qed.
Lemma keeps_violated v : forall fl d, node_sat fl d -> node_sat fl (node_with_violated v d).
Proof. keep_tac. Qed.
Lemma keeps_link v : forall fl d, node_sat fl d -> node_sat fl (node_with_link v d).
Proof. keep_tac. Qed.
Lemma keeps_scope v : forall fl d, node_sat fl d -> node_sat fl (node_with_scope v d).
Proof. keep_tac. Qed.
Lemma keeps_in_cleanup_set : forall fl d, node_sat fl d -> node_sat fl (node_with_in_cleanup true d).
Proof. keep_tac. Qed.
Lemma keeps_disposed_set : forall fl d, node_sat fl d -> node_sat fl (node_with_link None (node_with_disposed true d)).
Proof. keep_tac. Qed.
Lemma keeps_unqueued_disposed fl d : n_disposed d = true -> node_sat fl d -> node_sat fl (node_with_queued false d).
Proof. intros Hd. unfold node_sat, effect_saturated. simpl. rewrite Hd. destruct (n_kind d); auto. Qed.
Lemma keeps_clean_from_check fl d : n_freshness d = Check -> node_sat fl d -> node_sat fl (node_with_freshness Clean d).
Proof. intros Hc. unfold node_sat, effect_saturated. simpl. rewrite Hc. destruct (n_kind d); intuition congruence. Qed.
Lemma keeps_resolved_memo fl d :
  n_freshness d = Check -> node_sat fl d -> node_sat fl (node_with_unannounced false (node_with_freshness Clean d)).
Proof. intros Hc. unfold node_sat, effect_saturated. simpl. rewrite Hc. destruct (n_kind d); intuition congruence. Qed.
Lemma keeps_signal_node v fl : node_sat fl (signal_node v).
Proof. done. Qed.
Lemma keeps_memo_node m q b e fl : node_sat fl (memo_node m q b e).
Proof. unfold node_sat. simpl. split; [done|apply andb_false_r]. Qed.

(**md
**A node write that keeps `node_sat` keeps every node saturated.**

- [n:] the node written.
- [u:] the write.
- [o:] the state before.
- [o':] the state after.
*)
Theorem keeps_or_advances_modify n u o o' :
  (forall fl d, node_sat fl d -> node_sat fl (u d)) -> modify_node n u o = Some (o', tt) ->
  forall c, saturated o c -> saturated o' c.
Proof.
  intros Hu Hm. unfold modify_node in Hm. destruct (g_nodes o !! n) as [d|] eqn:Hd; [|done].
  injection Hm as <-. eapply sat_keep_insert; [exact Hd|reflexivity|reflexivity|]. apply Hu.
Qed.

(**md
**Under `SkipAll` a write at an advance point advances the epoch.**

- [p:] the advance point.
- [n:] the node written.
- [u:] the write.
- [o:] the state before.
- [o':] the state after.
*)
Theorem keeps_or_advances_modify_adv p n u o o' :
  g_skip o = SkipAll -> modify_node_adv p n u o = Some (o', tt) -> g_epoch o < g_epoch o'.
Proof.
  intros Hk Hm. unfold modify_node_adv, m_bind, advance, m_modify, modify_node in Hm.
  destruct (g_nodes o !! n); [|done]. simpl in Hm. rewrite Hk in Hm. destruct p; injection Hm as <-; simpl; lia.
Qed.

(** *** The advances *)

Lemma simM_modify_adv p n u : simM (modify_node n u) (modify_node_adv p n u).
Proof.
  intros s o R. destruct R as (E & Hk & Hok & Hle).
  unfold modify_node_adv, m_bind, advance, m_modify, modify_node. rewrite (strip_nodes _ _ E).
  destruct (g_nodes o !! n) as [d|]; [|done]. simpl. rewrite Hk. split; [done|].
  apply (rel_advanced _ (set_node n (u d) o)); [|done|exact Hle].
  change (strip (set_node n (u d) (strip s)) = strip (set_node n (u d) (strip o))). by rewrite E.
Qed.

Lemma simM_add_node_adv n d : simM (add_node n d) (add_node_adv n d).
Proof.
  intros s o R. destruct R as (E & Hk & Hok & Hle).
  unfold add_node_adv, add_node, m_bind, advance, m_modify.
  assert (Hg : g_skip (ghost_publish n (set_node n d o)) = SkipAll) by (unfold ghost_publish; case_match; done).
  rewrite Hg. simpl. split; [done|].
  assert (Hs : g_epoch (ghost_publish n (set_node n d o)) = g_epoch o /\ g_stamps (ghost_publish n (set_node n d o)) = g_stamps o)
    by (unfold ghost_publish; case_match; done).
  destruct Hs as [He Ht].
  apply (rel_advanced _ (ghost_publish n (set_node n d o))); [|done|].
  - assert (Hx : forall x, strip (ghost_publish n (set_node n d x)) = strip (ghost_publish n (set_node n d (strip x))))
      by (intros []; unfold ghost_publish; cbn; repeat case_match; reflexivity).
    by rewrite Hx, (Hx o), E.
  - intros src e' H. rewrite Ht in H. rewrite He. by apply (Hle src e').
Qed.

Lemma simM_add_node n d : (forall fl, node_sat fl d) -> simM (add_node n d) (add_node n d).
Proof.
  intros Hd s o R. unfold add_node, m_modify. split; [done|].
  assert (Hx : forall x, strip (ghost_publish n (set_node n d x)) = strip (ghost_publish n (set_node n d (strip x))))
    by (intros []; unfold ghost_publish; cbn; repeat case_match; reflexivity).
  assert (Hn : g_nodes (ghost_publish n (set_node n d o)) = <[n := d]> (g_nodes o) /\
               g_edges (ghost_publish n (set_node n d o)) = g_edges o /\
               g_flags (ghost_publish n (set_node n d o)) = g_flags o /\
               g_skip (ghost_publish n (set_node n d o)) = g_skip o /\
               g_epoch (ghost_publish n (set_node n d o)) = g_epoch o /\
               g_stamps (ghost_publish n (set_node n d o)) = g_stamps o)
    by (unfold ghost_publish; case_match; split_and!; reflexivity).
  destruct Hn as (Hn&He&Hf&?&?&?).
  eapply rel_step; [exact R|by rewrite Hx, (Hx o), (proj1 R)|done|done|done| |].
  - intros c. rewrite !saturated_node, Hn, Hf. destruct (decide (c = n)) as [->|Hne].
    + rewrite lookup_insert_eq. intros _. apply Hd.
    + by rewrite lookup_insert_ne.
  - intros src c. unfold observer_of. by rewrite He.
Qed.

Lemma simM_current_owner : simM current_owner current_owner.
Proof.
  apply simM_same; [apply old2_current_owner|].
  intros o o' a Hm. unfold current_owner in Hm.
  destruct (g_current_owner o) as [x|].
  { injection Hm as <- _. split_and!; try reflexivity; [intros c H; exact H|intros src c H; exact H]. }
  destruct (g_current o) as [h|]; [|done].
  destruct (g_nodes o !! h) as [d|] eqn:Hd; [|done].
  destruct (n_scope d) as [x|].
  { injection Hm as <- _. split_and!; try reflexivity; [intros c H; exact H|intros src c H; exact H]. }
  case_match; injection Hm as <- _; (split_and!; try reflexivity; [|intros src c Hy; exact Hy]);
    (eapply sat_keep_insert; [exact Hd|reflexivity|reflexivity|]); intros Hy; exact Hy.
Qed.

(** *** Edges *)

(** [w'] holds no observer [w] lacks. *)
Definition obs_sub (w w' : EdgeWorld) : Prop :=
  forall src x, x ∈ observer_view (observers_of w' src) -> x ∈ observer_view (observers_of w src).

Lemma obs_sub_trans w1 w2 w3 : obs_sub w1 w2 -> obs_sub w2 w3 -> obs_sub w1 w3.
Proof. intros H1 H2 src x Hx. by apply H1, H2. Qed.

Lemma remove_at_view c h o o' r :
  observers_remove_at c h o = Some (o', r) -> forall x, x ∈ observer_view o' -> x ∈ observer_view o.
Proof.
  unfold observers_remove_at. intros Hr x Hx. apply view_elem in Hx as (i & t & Hi). apply view_elem.
  destruct (observer_items o !! h) as [[c' t']|] eqn:Eh; [|done].
  destruct (decide (c' = c)); [|done]. destruct (decide (h = _)).
  - injection Hr as <- _. simpl in Hi. apply lookup_take_Some in Hi as [Hi _]. eauto.
  - destruct (observer_items o !! pred (length (observer_items o))) as [[m ms]|] eqn:El; [|done].
    injection Hr as <- _. simpl in Hi.
    apply list_lookup_insert_Some in Hi as [(_ & Heq & _)|(_ & Hi)].
    + injection Heq as <- <-. eauto.
    + apply lookup_take_Some in Hi as [Hi _]. eauto.
Qed.

Lemma remove_edge_obs self s h w w' : remove_edge self s h w = Some w' -> obs_sub w w'.
Proof.
  unfold remove_edge. destruct (observers_remove_at self h (observers_of w s)) as [[o' r]|] eqn:E; [|done].
  intros Hw src x Hx. injection Hw as <-.
  assert (Hx' : x ∈ observer_view (observers_of (set_observers s o' w) src)) by (destruct r as [[[m ms] h']|]; exact Hx).
  rewrite observers_of_set_observers in Hx'. destruct (decide (src = s)) as [->|]; [by eapply remove_at_view|done].
Qed.

Lemma remove_slot_obs self i w w' : sources_remove_slot self i w = Some w' -> obs_sub w w'.
Proof.
  unfold sources_remove_slot. destruct (slot_at w self i) as [[s [h|]]|]; intros Hw.
  - destruct (remove_edge self s h w) as [w1|] eqn:E; [|done]. simpl in Hw. injection Hw as <-.
    intros src x Hx. exact (remove_edge_obs _ _ _ _ _ E src x Hx).
  - injection Hw as <-. by intros ??.
  - injection Hw as <-. by intros ??.
Qed.

Lemma trim_loop_obs self n : forall i w w', trim_loop self i n w = Some w' -> obs_sub w w'.
Proof.
  induction n as [|n IH]; intros i w w' Hw; simpl in Hw.
  - injection Hw as <-. by intros ??.
  - destruct (sources_remove_slot self i w) as [w1|] eqn:E; simpl in Hw; [|done].
    eapply obs_sub_trans; [by eapply remove_slot_obs|by eapply IH].
Qed.

Lemma trim_from_obs self from w w' : sources_trim_from self from w = Some w' -> obs_sub w w'.
Proof.
  unfold sources_trim_from. destruct (trim_loop _ _ _ _) as [w1|] eqn:E; simpl; [|done].
  intros Hw. injection Hw as <-. intros src x Hx. exact (trim_loop_obs _ _ _ _ _ E src x Hx).
Qed.

Lemma end_run_obs self w w' : sources_end_run self w = Some w' -> obs_sub w w'.
Proof.
  unfold sources_end_run. destruct (decide _); [apply trim_from_obs|]. intros Hw. injection Hw as <-. by intros ??.
Qed.

Lemma clear_obs self w w' : sources_clear self w = Some w' -> obs_sub w w'.
Proof.
  unfold sources_clear. destruct (sources_trim_from self 0 w) as [w1|] eqn:E; simpl; [|done].
  intros Hw. injection Hw as <-. intros src x Hx. exact (trim_from_obs _ _ _ _ E src x Hx).
Qed.

Lemma sources_link_other c n w src : src <> n -> observers_of (sources_link c n w) src = observers_of w src.
Proof.
  intros Hne. unfold sources_link, sources_append. cbv zeta.
  repeat case_match; rewrite observers_of_set_observers, decide_False by done; reflexivity.
Qed.

Lemma sources_add_obs c n w w' :
  sources_add c n w = Some w' ->
  forall src x, (src <> n \/ links c n w = false) ->
    x ∈ observer_view (observers_of w' src) -> x ∈ observer_view (observers_of w src).
Proof.
  unfold sources_add, links. cbv zeta. intros Hw src x Hsrc Hx.
  destruct (decide (fst <$> source_slots (sources_of w c) !! source_cursor (sources_of w c) = Some n)) as [Hp|Hp].
  - injection Hw as <-. exact Hx.
  - destruct (decide (_ /\ _)) as [Hq|Hq].
    + injection Hw as <-. exact Hx.
    + destruct (sources_end_run c w) as [w1|] eqn:E; simpl in Hw; [|done]. injection Hw as <-.
      destruct Hsrc as [Hne|Hl].
      * apply (end_run_obs _ _ _ E). by rewrite sources_link_other in Hx.
      * rewrite !bool_decide_eq_false_2 in Hl by done. discriminate.
Qed.

Lemma simM_edges u : (forall w, obs_sub w (u w)) -> simM (edges u) (edges u).
Proof.
  intros Hu. apply simM_same; [apply old2_edges|].
  intros o o' a Hm. unfold edges, m_modify in Hm. injection Hm as <- _.
  split_and!; [done|done|done|intros c Hy; exact Hy|intros src c; unfold observer_of; apply Hu].
Qed.

Lemma simM_edges_opt u : (forall w w', u w = Some w' -> obs_sub w w') -> simM (edges_opt u) (edges_opt u).
Proof.
  intros Hu. apply simM_same; [apply old2_edges_opt|].
  intros o o' a Hm. unfold edges_opt in Hm. destruct (u (g_edges o)) as [w|] eqn:E; [|done].
  injection Hm as <- _.
  split_and!; [done|done|done|intros c Hy; exact Hy|intros src c; unfold observer_of; by apply Hu].
Qed.

Lemma simM_track n : simM (track n) (otrack n).
Proof.
  intros s o R. pose proof R as (E & Hk & Hok & Hle). unfold track, otrack.
  rewrite (strip_current _ _ E), (strip_edges _ _ E).
  destruct (g_current o) as [c|]; [|by split].
  destruct (sources_add c n (g_edges o)) as [w|] eqn:Ew; [|done]. split; [done|].
  set (g := fun x => ghost_read c n (read_prefix (g_edges o) c) (with_edges w x)).
  assert (Hg : forall x, strip (g x) = strip (g (strip x)))
    by (intros []; unfold g, ghost_read; cbn; repeat case_match; reflexivity).
  assert (Hin : forall x, g_nodes (g x) = g_nodes x /\ g_edges (g x) = w /\ g_flags (g x) = g_flags x /\
                  g_skip (g x) = g_skip x /\ g_epoch (g x) = g_epoch x /\ g_stamps (g x) = g_stamps x)
    by (intros x; unfold g, ghost_read; case_match; split_and!; reflexivity).
  assert (Hst : strip (g s) = strip (g o)) by (by rewrite Hg, (Hg o), E).
  destruct (Hin o) as (Hn & He & Hf & H1 & H2 & H3).
  change (rel (g s) (if links c n (g_edges o) then unstamp n (g o) else g o)).
  destruct (links c n (g_edges o)) eqn:El.
  - unfold unstamp. rewrite H1, Hk. cbn [skip_on]. split_and!.
    + exact Hst.
    + change (g_skip (g o) = SkipAll). by rewrite H1.
    + intros src x Hc Hx. unfold stamp_current in Hc.
      change (delete n (g_stamps (g o)) !! src = Some (g_epoch (g o))) in Hc. rewrite H2, H3 in Hc.
      destruct (decide (src = n)) as [->|Hne]; [by rewrite lookup_delete_eq in Hc|].
      rewrite lookup_delete_ne in Hc by done.
      assert (Hx' : observer_of o src x).
      { change (observer_of (g o) src x) in Hx. unfold observer_of in *. rewrite He in Hx.
        by eapply (sources_add_obs c n _ _ Ew src x (or_introl Hne)). }
      pose proof (Hok src x Hc Hx') as Hs. change (saturated (g o) x).
      unfold saturated, memo_saturated, announce_due in *. by rewrite Hn, Hf.
    + intros src e' Hx. change (delete n (g_stamps (g o)) !! src = Some e') in Hx. change (e' <= g_epoch (g o)).
      rewrite H3 in Hx. rewrite H2.
      destruct (decide (src = n)) as [->|Hne]; [by rewrite lookup_delete_eq in Hx|].
      rewrite lookup_delete_ne in Hx by done. by apply (Hle src).
  - eapply rel_step; [exact R|exact Hst|done|done|done| |].
    + intros x. unfold saturated, memo_saturated, announce_due. by rewrite Hn, Hf.
    + intros src x Hx. unfold observer_of in *. rewrite He in Hx.
      by eapply (sources_add_obs c n _ _ Ew src x (or_intror El)).
Qed.

(** *** Marks *)

Lemma strip_effect_marked F n d s o : strip s = strip o -> strip (effect_marked F n d s) = strip (effect_marked F n d o).
Proof.
  intros E. unfold effect_marked. destruct (_ || _); [done|]. destruct (n_queued d).
  - by apply strip_set_node.
  - change (strip (enqueue n (set_node n (node_marked F (n_unannounced d) true d) (strip s))) =
            strip (enqueue n (set_node n (node_marked F (n_unannounced d) true d) (strip o)))). by rewrite E.
Qed.

Lemma walk_old (m : node_id -> state -> option state) src :
  (forall c s o, strip s = strip o -> strip <$> m c s = strip <$> m c o) ->
  forall i s o, strip s = strip o -> strip <$> walk_marks m src i s = strip <$> walk_marks m src i o.
Proof.
  intros Hm i. induction i as [|i IH]; intros s o E; cbn [walk_marks].
  - simpl. by rewrite E.
  - rewrite (strip_edges _ _ E). destruct (decide _); [by apply IH|].
    destruct (observer_items (observers_of (g_edges o) src) !! i) as [[c ?]|]; [|done].
    pose proof (Hm c s o E) as H. destruct (m c s) as [s1|]; destruct (m c o) as [o1|]; simpl in H; try discriminate; [|done].
    apply (inj Some) in H. simpl. by apply IH.
Qed.

Lemma notify_old (m : node_id -> state -> option state) src s o :
  (forall c s o, strip s = strip o -> strip <$> m c s = strip <$> m c o) ->
  strip s = strip o -> strip <$> notify_with m src s = strip <$> notify_with m src o.
Proof. intros Hm E. unfold notify_with. rewrite (strip_edges _ _ E). by apply walk_old. Qed.

Lemma mark_check_old f : forall n s o, strip s = strip o -> strip <$> mark_check f n s = strip <$> mark_check f n o.
Proof.
  induction f as [|f IH]; intros n s o E; [done|]. cbn [mark_check].
  rewrite (strip_nodes _ _ E). destruct (g_nodes o !! n) as [d|]; [|simpl; by rewrite E].
  unfold announce_due. rewrite (strip_flags _ _ E).
  destruct (n_kind d).
  - simpl. by rewrite E.
  - destruct (decide _); [apply notify_old; [intros; by apply IH|by apply strip_set_node]|].
    destruct (_ && _); [apply notify_old; [intros; by apply IH|by apply strip_set_node]|simpl; by rewrite E].
  - simpl. f_equal. by apply strip_effect_marked.
Qed.

Lemma mark_dirty_old f n s o : strip s = strip o -> strip <$> mark_dirty f n s = strip <$> mark_dirty f n o.
Proof.
  intros E. destruct f as [|f]; [done|]. cbn [mark_dirty].
  rewrite (strip_nodes _ _ E). destruct (g_nodes o !! n) as [d|]; [|simpl; by rewrite E].
  unfold announce_due. rewrite (strip_flags _ _ E).
  destruct (n_kind d).
  - simpl. by rewrite E.
  - destruct (decide _); [apply notify_old; [intros; by apply mark_check_old|by apply strip_set_node]|].
    destruct (_ && _); [apply notify_old; [intros; by apply mark_check_old|by apply strip_set_node]|].
    destruct (decide _); simpl; [by rewrite E|]. f_equal. by apply strip_set_node.
  - simpl. f_equal. by apply strip_effect_marked.
Qed.

Lemma notify_dirty_old f n s o : strip s = strip o -> strip <$> notify_dirty f n s = strip <$> notify_dirty f n o.
Proof. intros E. apply notify_old; [|done]. intros. by apply mark_dirty_old. Qed.

Lemma notify_except_old f n r s o :
  strip s = strip o -> strip <$> notify_dirty_except f n r s = strip <$> notify_dirty_except f n r o.
Proof.
  intros E. apply notify_old; [|done]. intros c s0 o0 E0. unfold mark_except.
  destruct (decide _); [simpl; by rewrite E0|by apply mark_dirty_old].
Qed.

Lemma rtc_frame o o' : rtc mark_prim o o' ->
  g_skip o' = g_skip o /\ g_epoch o' = g_epoch o /\ g_stamps o' = g_stamps o /\ g_edges o' = g_edges o.
Proof.
  induction 1 as [|x y z Hp _ IH]; [done|].
  destruct Hp; simpl in *; destruct IH as (?&?&?&?); split_and!; congruence.
Qed.

Lemma simM_mark_step (g : state -> option state) :
  (forall s o, strip s = strip o -> strip <$> g s = strip <$> g o) ->
  (forall s s', g s = Some s' -> rtc mark_prim s s') ->
  simM (m_step g) (m_step g).
Proof.
  intros Hold Hr. apply simM_same.
  - intros s o E. unfold m_step, sres. pose proof (Hold s o E) as H.
    destruct (g s); destruct (g o); simpl in *; congruence.
  - intros o o' a Hm. unfold m_step in Hm. destruct (g o) as [o1|] eqn:Eg; [|done]. injection Hm as <- _.
    pose proof (Hr _ _ Eg) as Hrtc. destruct (rtc_frame _ _ Hrtc) as (?&?&?&He). split_and!; try done.
    + intros c. by apply rtc_keeps_saturated.
    + intros src c. unfold observer_of. by rewrite He.
Qed.

Lemma simM_mark_dirty f n : simM (m_step (mark_dirty f n)) (m_step (mark_dirty f n)).
Proof. apply simM_mark_step; [intros; by apply mark_dirty_old|intros ??; apply mark_dirty_rtc]. Qed.

Lemma simM_notify_except f n r : simM (m_step (notify_dirty_except f n r)) (m_step (notify_dirty_except f n r)).
Proof.
  apply simM_mark_step; [intros; by apply notify_except_old|].
  intros ?? H. eapply any_mark_rtc. right; right; right; right. exact H.
Qed.

Lemma simM_mark_reader f : simM (mark_reader f) (mark_reader f).
Proof.
  apply simM_same.
  - intros s o E. unfold mark_reader, sres. rewrite (strip_current _ _ E), (strip_flags _ _ E).
    destruct (g_current o) as [c|]; [|simpl; by rewrite E]. destruct (f_reader_mark _); [|simpl; by rewrite E].
    pose proof (mark_check_old f c s o E) as H.
    destruct (mark_check f c s); destruct (mark_check f c o); simpl in *; congruence.
  - intros o o' a Hm. unfold mark_reader in Hm.
    destruct (g_current o) as [c|]; [|injection Hm as <- _; split_and!; try done; by intros ??].
    destruct (f_reader_mark _); [|injection Hm as <- _; split_and!; try done; by intros ??].
    destruct (mark_check f c o) as [o1|] eqn:Eg; [|done]. injection Hm as <- _.
    pose proof (mark_check_rtc _ _ _ _ Eg) as Hrtc. destruct (rtc_frame _ _ Hrtc) as (?&?&?&He). split_and!; try done.
    + intros x. by apply rtc_keeps_saturated.
    + intros src x. unfold observer_of. by rewrite He.
Qed.

(** *** The notify of a write *)

Lemma all_saturated_spec src s : all_saturated src s = true -> forall c, observer_of s src c -> saturated s c.
Proof.
  unfold all_saturated, observer_of. intros H c Hc. apply forallb_forall with (x := c) in H.
  - by apply bool_decide_eq_true_1 in H.
  - by apply list_elem_of_In.
Qed.

(**md
**A checked record keeps `stamp_ok`.**

After a walk from a state meeting `stamp_ok`, under `SkipAll`, the record leaves `stamp_ok` and the
bound on the stamps holding.

- [f:] the fuel of the walk.
- [src:] the signal written.
- [o:] the state before the walk.
- [o1:] the state after the walk.
*)
Theorem walk_record_sound f src o o1 :
  g_skip o = SkipAll -> stamp_ok o -> stamps_le o -> notify_dirty f src o = Some o1 ->
  stamp_ok (record src o1) /\ stamps_le (record src o1) /\ g_skip (record src o1) = SkipAll /\
  strip (record src o1) = strip o1.
Proof.
  intros Hk Hok Hle Hn.
  assert (Hrtc : rtc mark_prim o o1) by (eapply (any_mark_rtc f src None); right; right; right; left; exact Hn).
  destruct (rtc_frame _ _ Hrtc) as (H1 & H2 & H3 & He).
  assert (Hok1 : stamp_ok o1).
  { intros s0 c Hc Ho. apply (rtc_keeps_saturated o). { exact Hrtc. }
    apply (Hok s0 c); unfold stamp_current, observer_of in *; [by rewrite <- H2, <- H3|by rewrite <- He]. }
  unfold record, record_due. rewrite H1, Hk.
  destruct (all_saturated src o1) eqn:Ha; [|split_and!; [done| |congruence|done]].
  - split_and!; [|  |simpl; congruence|done].
    + intros s0 c Hc Ho. unfold stamp_current in Hc. simpl in Hc.
      destruct (decide (s0 = src)) as [->|Hne].
      * exact (all_saturated_spec src o1 Ha c Ho).
      * rewrite lookup_insert_ne in Hc by done. exact (Hok1 s0 c Hc Ho).
    + intros s0 e Hs. simpl in *. destruct (decide (s0 = src)) as [->|Hne].
      * rewrite lookup_insert_eq in Hs. injection Hs as <-. done.
      * rewrite lookup_insert_ne in Hs by done. rewrite H2. apply (Hle s0). by rewrite <- H3.
  - intros s0 e Hs. rewrite H2. apply (Hle s0). by rewrite <- H3.
Qed.

Lemma simM_notify_write f n : simM (m_step (notify_dirty f n)) (m_step (notify_write f n)).
Proof.
  intros s o R. pose proof R as (E & Hk & Hok & Hle). unfold m_step, notify_write.
  destruct (skip_due f n o) eqn:Hd.
  - destruct f as [|f]; [discriminate|].
    assert (Hc : stamp_current o n).
    { unfold skip_due in Hd. rewrite Hk in Hd. simpl in Hd. by apply bool_decide_eq_true_1 in Hd. }
    pose proof (notify_dirty_old (S f) n s o E) as H. rewrite (notify_dirty_stamped_id o n f Hok Hc) in H.
    destruct (notify_dirty (S f) n s) as [s'|]; simpl in H; [|discriminate].
    apply (inj Some) in H. split; [done|]. split_and!; done.
  - pose proof (notify_dirty_old f n s o E) as H.
    destruct (notify_dirty f n s) as [s'|]; destruct (notify_dirty f n o) as [o1|] eqn:Eo; simpl in H;
      try discriminate; [|done].
    apply (inj Some) in H. split; [done|].
    destruct (walk_record_sound f n o o1 Hk Hok Hle Eo) as (A1 & A2 & A3 & A4).
    split_and!; [congruence|done|done|done].
Qed.

Lemma simM_announce n : simM (announce n) (oannounce n).
Proof.
  intros s o R. unfold announce, oannounce. rewrite (strip_flags _ _ (proj1 R)).
  destruct (f_unannounced (g_flags o)); [apply (simM_modify_adv A4290 n _ s o R)|by split].
Qed.

(** *** Writes that keep saturation only under the guard of their site *)

Lemma simM_execute_head n (K1 K2 : node -> M unit) :
  (forall d, n_disposed d = false -> simM (K1 d) (K2 d)) ->
  simM (let* d := get_node n in if n_disposed d then modify_node n (node_with_queued false) else K1 d)
       (let* d := get_node n in if n_disposed d then modify_node n (node_with_queued false) else K2 d).
Proof.
  intros HK s o R. unfold m_bind at 1 2, get_node. rewrite (strip_nodes _ _ (proj1 R)).
  destruct (g_nodes o !! n) as [d|] eqn:Hd; [|done].
  destruct (n_disposed d) eqn:Hdi; [|by apply HK].
  apply (modify_at n _ s o d R Hd). unfold node_sat, effect_saturated. simpl. rewrite Hdi.
  destruct (n_kind d); auto.
Qed.

Lemma simM_resolved n u :
  (forall fl d, n_freshness d = Check -> node_sat fl d -> node_sat fl (u d)) ->
  simM (let* d' := get_node n in
        if decide (n_freshness d' = Check) then let* _ := m_modify (ghost_resolved n) in modify_node n u
        else m_return tt)
       (let* d' := get_node n in
        if decide (n_freshness d' = Check) then let* _ := m_modify (ghost_resolved n) in modify_node n u
        else m_return tt).
Proof.
  intros Hu s o R. unfold m_bind at 1 3, get_node. rewrite (strip_nodes _ _ (proj1 R)).
  destruct (g_nodes o !! n) as [d|] eqn:Hd; [|done].
  destruct (decide (n_freshness d = Check)) as [Hc|]; [|by split].
  unfold m_bind, m_modify.
  assert (R1 : rel (ghost_resolved n s) (ghost_resolved n o)) by (apply rel_map; [oldf_tac|map_tac|exact R]).
  apply (modify_at n u _ _ d R1); [|by apply Hu].
  unfold ghost_resolved. exact Hd.
Qed.

(** *** The engine functions *)

(** One record per mutual function: the present function at fuel [f] and its optimised namesake agree
    from related states. *)
Record rsims (f : nat) : Prop := {
  Sexec : forall e c, simM (exec f e c) (oexec f e c);
  Sread : forall n b, simM (read f n b) (oread f n b);
  Sread_node : forall n, simM (read_node f n) (oread_node f n);
  Supdate : forall n, simM (update_if_necessary f n) (oupdate_if_necessary f n);
  Srefresh_or_pull : forall n, simM (refresh_or_pull f n) (orefresh_or_pull f n);
  Spull : forall n, simM (pull f n) (opull f n);
  Srefresh : forall n, simM (refresh f n) (orefresh f n);
  Swalk : forall n i, simM (resolve_walk f n i) (oresolve_walk f n i);
  Srecompute : forall n, simM (recompute f n) (orecompute f n);
  Srun_memo : forall n, simM (run_memo f n) (orun_memo f n);
  Sexecute : forall n, simM (execute_effect f n) (oexecute_effect f n);
  Srun_effect : forall n, simM (run_effect f n) (orun_effect f n);
  Sbody : forall n, simM (run_effect_body f n) (orun_effect_body f n);
  Sdischarge : forall o, simM (discharge f o) (odischarge f o);
  Sretire : forall o, simM (retire f o) (oretire f o);
  Sdispose_scope : forall o, simM (dispose_scope f o) (odispose_scope f o);
  Steardown : forall o ls cs, simM (teardown f o ls cs) (oteardown f o ls cs);
  Scleanup : forall o b e, simM (run_cleanup f o b e) (orun_cleanup f o b e);
  Srelease : forall n, simM (release f n) (orelease f n);
  Sdispose : forall n, simM (dispose_node f n) (odispose_node f n);
  Sattach : forall n o, simM (attach f n o) (oattach f n o);
  Swrite : forall n v, simM (write_signal f n v) (owrite_signal f n v);
  Srequest : simM (request_flush f) (orequest_flush f);
  Ssettle : simM (settle_flush f) (osettle_flush f);
  Sflush : simM (run_flush f) (orun_flush f);
  Sdrain : simM (drain f) (odrain f)
}.

Local Ltac sih IH :=
  first
    [ apply (Sexec _ IH) | apply (Sread _ IH) | apply (Sread_node _ IH) | apply (Supdate _ IH)
    | apply (Srefresh_or_pull _ IH) | apply (Spull _ IH) | apply (Srefresh _ IH) | apply (Swalk _ IH)
    | apply (Srecompute _ IH) | apply (Srun_memo _ IH) | apply (Sexecute _ IH) | apply (Srun_effect _ IH)
    | apply (Sbody _ IH) | apply (Sdischarge _ IH) | apply (Sretire _ IH)
    | apply (Sdispose_scope _ IH) | apply (Steardown _ IH) | apply (Scleanup _ IH)
    | apply (Srelease _ IH) | apply (Sdispose _ IH) | apply (Sattach _ IH) | apply (Swrite _ IH)
    | apply (Srequest _ IH) | apply (Ssettle _ IH) | apply (Sflush _ IH) | apply (Sdrain _ IH) ].

Lemma simM_get_node n : simM (get_node n) (get_node n).
Proof. apply simM_inert; [apply old2_get_node|inert_tac]. Qed.
Lemma simM_get_owner x : simM (get_owner x) (get_owner x).
Proof. apply simM_inert; [apply old2_get_owner|inert_tac]. Qed.
Lemma simM_put_owner x y : simM (put_owner x y) (put_owner x y).
Proof. apply simM_inert; [apply old2_put_owner|inert_tac]. Qed.
Lemma simM_emit e : simM (emit e) (emit e).
Proof. apply simM_inert; [apply old2_emit|inert_tac]. Qed.
Lemma simM_next_id : simM next_id next_id.
Proof. apply simM_inert; [apply old2_next_id|inert_tac]. Qed.
Lemma simM_log_label : simM log_label log_label.
Proof. apply simM_inert; [apply old2_log_label|inert_tac]. Qed.
Lemma simM_note n r : simM (note_host_read n r) (note_host_read n r).
Proof. apply simM_inert; [apply old2_note|inert_tac]. Qed.
Lemma simM_push n : simM (push_running n) (push_running n).
Proof. apply simM_inert; [apply old2_push|inert_tac]. Qed.
Lemma simM_pop : simM pop_running pop_running.
Proof. apply simM_inert; [apply old2_pop|inert_tac]. Qed.
Lemma simM_unlink l : simM (unlink l) (unlink l).
Proof. apply simM_inert; [apply old2_unlink|inert_tac]. Qed.
Lemma simM_record_error x y : simM (record_error x y) (record_error x y).
Proof. apply simM_inert; [apply old2_record_error|inert_tac]. Qed.

Local Ltac resolved_tac :=
  let Hc := fresh "Hc" in
  intros ? ? Hc; unfold node_sat, effect_saturated; simpl; rewrite Hc;
  destruct (n_kind _); intuition (auto || congruence).

Local Ltac sgo1 IH :=
  lazymatch goal with
  | |- simM (track _) _ => apply simM_track
  | |- simM (announce _) _ => apply simM_announce
  | |- simM (m_step (notify_dirty _ _)) _ => apply simM_notify_write
  | |- simM (m_step (mark_dirty _ _)) _ => apply simM_mark_dirty
  | |- simM (m_step (notify_dirty_except _ _ _)) _ => apply simM_notify_except
  | |- simM (mark_reader _) _ => apply simM_mark_reader
  | |- simM (modify_node _ _) (modify_node_adv _ _ _) => apply simM_modify_adv
  | |- simM (modify_node _ _) _ => apply simM_modify_node; keep_tac
  | |- simM (add_node _ _) (add_node_adv _ _) => apply simM_add_node_adv
  | |- simM (add_node _ _) _ =>
      apply simM_add_node; intros ?; first [apply keeps_signal_node | apply keeps_memo_node]
  | |- simM (on_node _ _ _) _ => apply simM_on_node; intros ?
  | |- simM (on_value _ _ _) _ => apply simM_on_value; intros ?
  | |- simM (hosted _ _) _ => apply simM_hosted
  | |- simM (untracked _) _ => apply simM_untracked
  | |- simM (detached _ _) _ => apply simM_detached
  | |- simM (m_bind m_get _) _ => apply simM_get; [intros []; reflexivity|intros []; reflexivity|intros ?]
  | |- simM (m_bind (get_node _) _) _ =>
      first [ apply simM_execute_head; intros ? ?
            | apply simM_resolved; resolved_tac
            | apply simM_bind; [apply simM_get_node|intros ?] ]
  | |- simM (m_bind _ _) _ => apply simM_bind; [|intros ?]
  | |- simM (m_return _) _ => apply simM_return
  | |- simM stuck _ => apply simM_stuck
  | |- simM (get_node _) _ => apply simM_get_node
  | |- simM (get_owner _) _ => apply simM_get_owner
  | |- simM (put_owner _ _) _ => apply simM_put_owner
  | |- simM (emit _) _ => apply simM_emit
  | |- simM next_id _ => apply simM_next_id
  | |- simM log_label _ => apply simM_log_label
  | |- simM (note_host_read _ _) _ => apply simM_note
  | |- simM (push_running _) _ => apply simM_push
  | |- simM pop_running _ => apply simM_pop
  | |- simM (unlink _) _ => apply simM_unlink
  | |- simM (record_error _ _) _ => apply simM_record_error
  | |- simM current_owner _ => apply simM_current_owner
  | |- simM (edges _) _ => apply simM_edges; intros w; intros ??; exact id
  | |- simM (edges_opt _) _ =>
      apply simM_edges_opt; intros ??; repeat case_match; first [apply clear_obs | apply end_run_obs]
  | |- simM (m_modify _) _ => apply simM_modify; map_tac
  | |- simM (if ?x then _ else _) _ => destruct x
  | |- simM (match ?x with _ => _ end) _ => destruct x
  | |- _ => first [ sih IH | progress cbv beta zeta ]
  end.

Local Ltac sgo IH := repeat sgo1 IH.

Lemma rsims_0 : rsims 0.
Proof. split; intros; apply simM_stuck. Qed.

Lemma rsims_S f : rsims f -> rsims (S f).
Proof.
  intros IH. split; intros.
  all: lazymatch goal with
       | |- simM (exec _ _ ?c) _ => destruct c; cbn [exec oexec]
       | |- simM (teardown _ _ ?ls ?cs) _ => destruct ls as [|? ?], cs as [|[[? ?] ?] ?]; cbn [teardown oteardown]
       | |- _ => cbn [read oread read_node oread_node update_if_necessary oupdate_if_necessary
                      refresh_or_pull orefresh_or_pull pull opull refresh orefresh resolve_walk oresolve_walk
                      recompute orecompute run_memo orun_memo execute_effect oexecute_effect run_effect orun_effect
                      run_effect_body orun_effect_body discharge odischarge retire oretire dispose_scope odispose_scope
                      run_cleanup orun_cleanup release orelease dispose_node odispose_node attach oattach
                      write_signal owrite_signal request_flush orequest_flush settle_flush osettle_flush
                      run_flush orun_flush drain odrain]
       end.
  all: sgo IH.
Qed.

Lemma rsims_all f : rsims f.
Proof. induction f; [apply rsims_0|by apply rsims_S]. Qed.

(**md
**The optimised engine simulates the present engine.**

From `sim`-related states with bounded stamps, `exec` and `oexec` at every fuel both return `None`, or
return equal outcomes in `sim`-related states with bounded stamps. `rsims_all` states the same of every
other function of the engine, with the mode of the present engine's state left free.

- [f:] the fuel.
- [e:] the local variable environment.
- [c:] the command run.
- [s:] the present engine's state.
- [o:] the optimised engine's state.
*)
Theorem optimised_simulates f e c s o :
  sim s o -> stamps_le o ->
  match exec f e c s, oexec f e c o with
  | None, None => True
  | Some (s', a), Some (o', b) => a = b /\ sim s' o' /\ stamps_le o'
  | _, _ => False
  end.
Proof.
  intros (E & Hs & Hk & Hok) Hle.
  pose proof (Sexec _ (rsims_all f) e c s o (conj E (conj Hk (conj Hok Hle)))) as H.
  pose proof (proj2 (Oexec _ (osims_all f) e c)) as K.
  destruct (exec f e c s) as [[s' a]|] eqn:Es; destruct (oexec f e c o) as [[o' b]|];
    simpl in H; try contradiction; [|done].
  destruct H as [Hab (E' & Hk' & Hok' & Hle')]. split; [done|]. split; [|done].
  split_and!; [done|exact (K _ _ _ Hs Es)|done|done].
Qed.

(**md
**Under `SkipAll` the engine keeps `stamp_ok`.**

From a state meeting `stamp_ok` with bounded stamps, an `oexec` run at any fuel ends in one.

- [f:] the fuel.
- [e:] the local variable environment.
- [c:] the command run.
- [o:] the starting state.
- [o':] the end state.
- [r:] the outcome.
*)
Theorem stamp_ok_preserved f e c o o' r :
  g_skip o = SkipAll -> stamp_ok o -> stamps_le o -> oexec f e c o = Some (o', r) -> stamp_ok o' /\ stamps_le o'.
Proof.
  intros Hk Hok Hle Ho.
  pose proof (Sexec _ (rsims_all f) e c o o (conj eq_refl (conj Hk (conj Hok Hle)))) as H.
  rewrite Ho in H. destruct (exec f e c o) as [[s' a]|]; simpl in H; [|done].
  destruct H as [_ (_ & _ & ? & ?)]. done.
Qed.

Lemma rel_init fl : rel (init fl) (with_skip SkipAll (init fl)).
Proof.
  split_and!; [reflexivity|reflexivity| |].
  - intros src c Hc. unfold stamp_current in Hc. simpl in Hc. by rewrite lookup_empty in Hc.
  - intros src e He. simpl in He. by rewrite lookup_empty in He.
Qed.

(** ** Trace equality and its consequences *)

(**md
**The optimised engine's trace is the present engine's.**

At every fuel and flags, `trace_of_mode` in mode `SkipAll` is `trace_of`. The queue's content and order
agree at every step, so this fixes the effect order too.

- [k:] the fuel.
- [fl:] the flags.
- [c:] the program.
*)
Theorem optimised_trace_eq k fl c : trace_of_mode k fl SkipAll c = trace_of k fl c.
Proof.
  unfold trace_of_mode, trace_of, run_top.
  pose proof (Sexec _ (rsims_all k) [] c _ _ (rel_init fl)) as H.
  destruct (exec k [] c (init fl)) as [[s r]|]; destruct (oexec k [] c (with_skip SkipAll (init fl))) as [[o r']|];
    simpl in H; try contradiction; [|done].
  destruct H as [_ (E & _)]. f_equal. exact (eq_sym (f_equal g_trace E)).
Qed.

(**md
**The optimised reference engine is the present one.**

- [k:] the fuel.
- [p:] the program.
- [h:] the history.
*)
Corollary engine_of_optimised_eq k p h : engine_of_optimised k p h = Refinement.engine_of current_flags k p h.
Proof. unfold engine_of_optimised, Refinement.engine_of. by rewrite optimised_trace_eq. Qed.

(**md
**The optimised engine refines the reference on refinable programs, under `observe_settled`.**

- [k:] the fuel.
*)
Theorem optimised_refines k :
  Refinement.refines_on Refinement.refinable Semantics.observe_settled (engine_of_optimised k).
Proof.
  intros p h t Hwf Hr He. rewrite engine_of_optimised_eq in He.
  destruct (Refinement.weak_refinement_except k p h t Hwf Hr He) as (ord & Hord & H).
  exists ord. split; [done|]. intros h' Hp. destruct (H h' Hp) as (te & fuel & tr & E & Ht).
  exists te, fuel, tr. rewrite engine_of_optimised_eq. by split.
Qed.

(**md
**Exact refinement carries over to the optimised engine under `runs_agree`.**

- [k:] the fuel.
- [p:] the program.
- [h:] the history.
- [t:] the optimised engine's trace for `h`.
- [ord:] the effect order.
*)
Theorem optimised_exact_except k p h t ord :
  Semantics.well_formed_program p -> Refinement.refinable p h -> Semantics.is_effect_order ord ->
  engine_of_optimised k p h = Some t -> Refinement.runs_agree p ord k h ->
  forall h', h' `prefix_of` h ->
    exists te tr, engine_of_optimised k p h' = Some te /\ te `prefix_of` t /\
      Semantics.reference_trace 1 ord p h' = Some tr /\ te = tr.
Proof.
  intros Hwf Hr Ho He Ha h' Hp. rewrite engine_of_optimised_eq in He.
  destruct (Refinement.exact_refinement_except k p h t ord Hwf Hr Ho He Ha h' Hp) as (te & tr & E & Ht).
  exists te, tr. rewrite engine_of_optimised_eq. by split.
Qed.

(**md
**The optimised engine answers wherever the present engine does.**

- [p:] the program.
- [h:] the history.
- [ord:] the reference's effect order.
- [fuel:] the reference's fuel.
- [t0:] the reference's trace.
*)
Theorem engine_of_optimised_answers_on p h ord fuel t0 :
  Semantics.well_formed_program p -> Refinement.refinable p h -> Refinement.bodies_total p ->
  Semantics.reference_trace fuel ord p h = Some t0 ->
  exists k t, engine_of_optimised k p h = Some t.
Proof.
  intros Hwf Hr Hb Ht. destruct (Refinement.engine_of_answers_on p h ord fuel t0 Hwf Hr Hb Ht) as (k & t & E).
  exists k, t. by rewrite engine_of_optimised_eq.
Qed.

(**md
**A flush's order on the optimised engine is the present engine's.**

The conclusion of `engine_order_exact_except` holds of the optimised engine's runs, at a present-engine
state `s1` equal to the optimised one apart from the fields of the skip.

- [k:] the fuel.
- [p:] the program.
- [h:] the history before the batch.
- [b:] the batch.
- [o0:] the state after the engine program for `h`.
- [r0:] its outcome.
- [o1:] the state after the batch's inputs, at batch depth 1.
- [r1:] their outcome.
- [o2:] the state after the flush.
- [a:] the result of the flush request.
- [t:] the events the flush adds to the trace.
*)
Theorem optimised_order_exact_except k p h b o0 r0 o1 r1 o2 a t :
  Semantics.well_formed_program p -> Refinement.refinable p (h ++ [b]) ->
  oexec k [] (Refinement.erase p h) (with_skip SkipAll (init current_flags)) = Some (o0, r0) ->
  oexec k [] (foldr (Refinement.input_cmd p) Refinement.done_cmd b) (with_batch_depth 1 o0) = Some (o1, r1) ->
  orequest_flush k (with_batch_depth 0 o1) = Some (o2, a) ->
  g_trace o2 = g_trace o1 ++ t ->
  exists s1, strip s1 = strip o1 /\
    (pred <$> Refinement.runs_in t) `sublist_of` (pred <$> Refinement.flush_queue k (with_batch_depth 0 s1)) /\
    Refinement.live_queue_of (with_batch_depth 0 s1) `prefix_of`
      (pred <$> Refinement.flush_queue k (with_batch_depth 0 s1)) /\
    (Refinement.queue_steady k (with_batch_depth 0 s1) ->
       pred <$> Refinement.runs_in t =
         Refinement.engine_order (with_batch_depth 0 s1) 0%nat (pred <$> Refinement.runs_in t)).
Proof.
  intros Hwf Hr E0 E1 E2 Tt.
  pose proof (Sexec _ (rsims_all k) [] (Refinement.erase p h) _ _ (rel_init current_flags)) as H0.
  rewrite E0 in H0.
  destruct (exec k [] (Refinement.erase p h) (init current_flags)) as [[s0 x0]|] eqn:F0; simpl in H0; [|done].
  destruct H0 as [-> R0].
  assert (R0' : rel (with_batch_depth 1 s0) (with_batch_depth 1 o0)) by (apply rel_map; [map_tac|map_tac|exact R0]).
  pose proof (Sexec _ (rsims_all k) [] (foldr (Refinement.input_cmd p) Refinement.done_cmd b) _ _ R0') as H1.
  rewrite E1 in H1.
  destruct (exec k [] (foldr (Refinement.input_cmd p) Refinement.done_cmd b) (with_batch_depth 1 s0))
    as [[s1 x1]|] eqn:F1; simpl in H1; [|done].
  destruct H1 as [-> R1].
  assert (R1' : rel (with_batch_depth 0 s1) (with_batch_depth 0 o1)) by (apply rel_map; [map_tac|map_tac|exact R1]).
  pose proof (Srequest _ (rsims_all k) _ _ R1') as H2. rewrite E2 in H2.
  destruct (request_flush k (with_batch_depth 0 s1)) as [[s2 x2]|] eqn:F2; simpl in H2; [|done].
  destruct H2 as [-> R2].
  assert (Ht : g_trace s2 = g_trace s1 ++ t).
  { pose proof (f_equal g_trace (proj1 R2)) as T2. pose proof (f_equal g_trace (proj1 R1)) as T1.
    simpl in T1, T2. by rewrite T2, T1. }
  exists s1. split; [exact (proj1 R1)|].
  exact (Refinement.engine_order_exact_except k p h b s0 r0 s1 r1 s2 a t Hwf Hr F0 F1 F2 Ht).
Qed.

Lemma optimised_run_of k c fl s r :
  run_top k c (init fl) = Some (s, r) ->
  exists o, oexec k [] c (with_skip SkipAll (init fl)) = Some (o, r) /\ strip s = strip o.
Proof.
  intros Es. unfold run_top in Es. pose proof (Sexec _ (rsims_all k) [] c _ _ (rel_init fl)) as H. rewrite Es in H.
  destruct (oexec k [] c (with_skip SkipAll (init fl))) as [[o r']|]; simpl in H; [|done].
  destruct H as [<- R]. exists o. split; [done|exact (proj1 R)].
Qed.

Lemma den_strip k : forall s s' n o, strip s = strip s' -> WholeGraph.den k s n = Some o -> WholeGraph.den k s' n = Some o.
Proof.
  induction k as [|k IH]; intros s s' n o E H; [done|]. cbn [WholeGraph.den] in *.
  rewrite <- (strip_nodes _ _ E). destruct (g_nodes s !! n) as [d|]; [|done].
  destruct (n_kind d); try done. destruct (n_disposed d); [done|].
  eapply WholeGraph.body_eval_mono; [|exact H]. intros t' o' Ht. by apply (IH s s').
Qed.

Lemma shapes_strip x : WholeGraph.shapes (strip x) = WholeGraph.shapes x.
Proof. reflexivity. Qed.

Lemma ran_strip x : WholeGraph.effect_ran_for_last_write x <-> WholeGraph.effect_ran_for_last_write (strip x).
Proof.
  unfold WholeGraph.effect_ran_for_last_write, WholeGraph.denotes.
  split; intros (c & d & t & v & H1 & H2 & H3 & (k & H4) & H5); exists c, d, t, v;
    (split_and!; [exact H1|exact H2|exact H3|exists k|exact H5]).
  - by apply (den_strip k x).
  - by apply (den_strip k (strip x)).
Qed.

Lemma transfer (P : state -> Prop) s o : (forall x, P x <-> P (strip x)) -> strip s = strip o -> P s -> P o.
Proof. intros HP E H. apply (proj2 (HP o)). rewrite <- E. apply (proj1 (HP s)). exact H. Qed.

(**md
**Regression witness for #302 on the optimised engine.**

`witness_302` holds of the optimised engine's end states: without the announcement flag, the effect is
left behind.
*)
Theorem witness_302_optimised :
  exists o_bad o_good r_bad r_good,
    oexec 100000 [] Witnesses.prog_S03 (with_skip SkipAll (init Witnesses.no_unannounced)) = Some (o_bad, r_bad) /\
    oexec 100000 [] Witnesses.prog_S03 (with_skip SkipAll (init current_flags)) = Some (o_good, r_good) /\
    ~ Runs.wake_ok o_bad /\ Runs.wake_ok o_good /\ WholeGraph.effect_ran_for_last_write o_good /\
    ~ WholeGraph.effect_ran_for_last_write o_bad /\
    WholeGraph.well_behaved_program o_bad /\ WholeGraph.effects_only_read o_bad /\ ~ Runs.nothing_passed o_bad.
Proof.
  destruct Witnesses.witness_302 as (s_bad & s_good & r_bad & r_good & Eb & Eg & H1 & H2 & H3 & H4 & H5 & H6 & H7).
  destruct (optimised_run_of _ _ _ _ _ Eb) as (o_bad & Ob & Eb').
  destruct (optimised_run_of _ _ _ _ _ Eg) as (o_good & Og & Eg').
  exists o_bad, o_good, r_bad, r_good. split_and!; [exact Ob|exact Og| | | | | | |].
  - eapply (transfer (fun x => ~ Runs.wake_ok x)); [intros x; reflexivity|exact Eb'|exact H1].
  - eapply (transfer Runs.wake_ok); [intros x; reflexivity|exact Eg'|exact H2].
  - eapply (transfer WholeGraph.effect_ran_for_last_write); [apply ran_strip|exact Eg'|exact H3].
  - eapply (transfer (fun x => ~ WholeGraph.effect_ran_for_last_write x)); [|exact Eb'|exact H4].
    intros x. pose proof (ran_strip x). tauto.
  - eapply (transfer WholeGraph.well_behaved_program); [|exact Eb'|exact H5].
    intros x. unfold WholeGraph.well_behaved_program. rewrite shapes_strip. reflexivity.
  - eapply (transfer WholeGraph.effects_only_read); [|exact Eb'|exact H6].
    intros x. unfold WholeGraph.effects_only_read, WholeGraph.quiet_effect. rewrite shapes_strip. reflexivity.
  - eapply (transfer (fun x => ~ Runs.nothing_passed x)); [intros x; reflexivity|exact Eb'|exact H7].
Qed.

(**md
**Under `SkipAll`, `prog_window` ends in `sim` with the present engine.**

The checked record withholds the stamp inside `RecomputeScoped`, so the second write walks and spends the
announcement, as the present engine does.
*)
Corollary prog_window_checked :
  exists s o r1 r2, exec 1000 [] prog_window (init current_flags) = Some (s, r1) /\
    oexec 1000 [] prog_window (with_skip SkipAll (init current_flags)) = Some (o, r2) /\ sim s o.
Proof.
  assert (S0 : sim (init current_flags) (with_skip SkipAll (init current_flags))).
  { destruct (rel_init current_flags) as (E&Hk&Hok&_). by split_and!. }
  pose proof (optimised_simulates 1000 [] prog_window _ _ S0 (proj2 (proj2 (proj2 (rel_init current_flags))))) as H.
  pose proof prog_window_runs as (W & _).
  destruct (exec 1000 [] prog_window (init current_flags)) as [[s r1]|]; [|discriminate W].
  destruct (oexec 1000 [] prog_window (with_skip SkipAll (init current_flags))) as [[o r2]|]; [|contradiction].
  exists s, o, r1, r2. split_and!; [done|done|exact (proj1 (proj2 H))].
Qed.

(** ** Cleanups *)

Definition is_cleanup (ev : event) : bool := match ev with EvCleanup _ => true | _ => false end.

(** Effect [E] (node 2) reads signal [s] (node 1) and registers a cleanup that registers a cleanup. The host
    writes [s] twice. *)
Open Scope Z_scope.
Definition prog_cleanup_in_teardown : cmd :=
  CSignal 0 (EConst 0)
  (CEffect 1 (CCleanup (CCleanup (CLog (EConst 7) (CRet (EConst 0))) (CRet (EConst 0)))
               (CRead 2 (EVar 0) (CLog (EVar 2) (CRet (EConst 0)))))
  (CWrite (EVar 0) (EConst 1) (CWrite (EVar 0) (EConst 2) (CRet (EConst 0))))).
Close Scope Z_scope.

(**md
**A cleanup registered during its host's teardown runs after the host's next run.**

The plan's `cleanups_before_replacement`, read over every registration, is false on the present engine,
as Core.fs documents (1130-1132): the cleanup the first teardown registers joins the emptied scope, and
the host's second run (`EvRun 2` after the first `EvCleanup 2`) comes before it runs, at the second
teardown. The optimised engine has the same trace (`optimised_trace_eq`).
*)
Example cleanup_registered_in_teardown_runs_late :
  trace_of 3000 current_flags prog_cleanup_in_teardown =
    Some [EvRun 2; EvLog 2 0%Z; EvCleanup 2; EvRun 2; EvLog 2 1%Z; EvCleanup 2; EvCleanup 2; EvLog 2 7%Z;
          EvRun 2; EvLog 2 2%Z].
Proof. vm_compute. reflexivity. Qed.

(**md
**Cleanups on the optimised engine: the present engine's events, and at most one per registration.**

The `EvCleanup` events of the optimised trace are the present engine's. After a run of the optimised
engine on a new graph, each host's `EvCleanup` events number at most its registrations, and each
teardown runs each detached cleanup once (`cleanup_entries`).

- [k:] the fuel.
- [fl:] the flags.
- [c:] the program.
*)
Theorem optimised_cleanups k fl c :
  filter (fun ev => is_cleanup ev = true) <$> trace_of_mode k fl SkipAll c =
    filter (fun ev => is_cleanup ev = true) <$> trace_of k fl c /\
  (forall o r h, oexec k [] c (with_skip SkipAll (init fl)) = Some (o, r) ->
     Scheduler.count_cleanups h (g_trace o) <= Scheduler.registered h o) /\
  Ownership.cleanup_entries k.
Proof.
  destruct (Ownership.cleanup_once_new_graph k fl) as [Hb He].
  split_and!; [by rewrite optimised_trace_eq| |exact He].
  intros o r h Ho. pose proof (Sexec _ (rsims_all k) [] c _ _ (rel_init fl)) as H. rewrite Ho in H.
  destruct (exec k [] c (init fl)) as [[s x]|] eqn:Es; simpl in H; [|done].
  destruct H as [_ (E & _)].
  pose proof (Hb c s x h Es) as B.
  pose proof (f_equal g_trace E) as T. pose proof (f_equal g_registered E) as G. simpl in T, G.
  unfold Scheduler.registered in *. by rewrite <- T, <- G.
Qed.
