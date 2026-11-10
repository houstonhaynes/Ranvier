From stdpp Require Import prelude gmap.
From Stdlib Require Import ZArith.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList State Marking.

Open Scope nat_scope.

(** ** The monad *)

(**md
A step of the engine: a partial state transformer returning a value of type `A`. `None` is exhausted fuel
or a stuck body, so a `Some` result means the operation returned.
*)
Definition M A := state -> option (state * A).

Definition m_return {A} (a : A) : M A := fun s => Some (s, a).

Definition m_bind {A B} (m : M A) (k : A -> M B) : M B :=
  fun s => match m s with Some (s', a) => k a s' | None => None end.

(**md
The step that always yields `None`.
*)
Definition stuck {A} : M A := fun _ => None.

Notation "'let*' x ':=' m 'in' k" := (m_bind m (fun x => k))
  (at level 200, x name, m at level 100, k at level 200).

Definition m_get : M state := fun s => Some (s, s).
Definition m_modify (f : state -> state) : M unit := fun s => Some (f s, tt).
Definition m_step (f : state -> option state) : M unit :=
  fun s => match f s with Some s' => Some (s', tt) | None => None end.

(** ** State updates *)

Definition with_edges (v : EdgeWorld) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := v; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_owners (v : gmap owner_id owner_state) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := v;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_queue (v : list node_id) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := v; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_queue_head (v : nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := v; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_flushing (v : bool) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := v; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_flush_owed (v : bool) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := v;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_batch_depth (v : nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := v; g_pull_depth := g_pull_depth s; g_current := g_current s; g_untracked := g_untracked s;
     g_current_owner := g_current_owner s; g_next_id := g_next_id s; g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_pull_depth (v : nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := v; g_current := g_current s; g_untracked := g_untracked s;
     g_current_owner := g_current_owner s; g_next_id := g_next_id s; g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_current (v : option node_id) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := v; g_untracked := g_untracked s;
     g_current_owner := g_current_owner s; g_next_id := g_next_id s; g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_untracked (v : bool) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s; g_untracked := v;
     g_current_owner := g_current_owner s; g_next_id := g_next_id s; g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_current_owner (v : option owner_id) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := v; g_next_id := g_next_id s; g_trace := g_trace s;
     g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_next_id (v : nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := v; g_trace := g_trace s;
     g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_trace (v : list event) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s; g_trace := v;
     g_running := g_running s; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_running (v : list node_id) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := v; g_ghost := g_ghost s;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_ghost (v : ghost) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := v;
     g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_registered (v : gmap node_id nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s; g_registered := v;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_skip (v : skip_mode) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s; g_registered := g_registered s;
     g_skip := v; g_epoch := g_epoch s; g_stamps := g_stamps s |}.

Definition with_epoch (v : nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s; g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := v; g_stamps := g_stamps s |}.

Definition with_stamps (v : gmap node_id nat) (s : state) : state :=
  {| g_flags := g_flags s; g_edges := g_edges s; g_nodes := g_nodes s; g_owners := g_owners s;
     g_queue := g_queue s; g_queue_head := g_queue_head s; g_flushing := g_flushing s; g_flush_owed := g_flush_owed s;
     g_batch_depth := g_batch_depth s; g_pull_depth := g_pull_depth s; g_current := g_current s;
     g_untracked := g_untracked s; g_current_owner := g_current_owner s; g_next_id := g_next_id s;
     g_trace := g_trace s; g_running := g_running s; g_ghost := g_ghost s; g_registered := g_registered s;
     g_skip := g_skip s; g_epoch := g_epoch s; g_stamps := v |}.

Definition node_with_value (v : val) (d : node) : node :=
  {| n_kind := n_kind d; n_value := v; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d;
     n_link := n_link d |}.

Definition node_with_failure (v : option val) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := v; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d;
     n_link := n_link d |}.

Definition node_with_status (v : status) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := v;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d;
     n_link := n_link d |}.

Definition node_with_freshness (v : freshness) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := v; n_unannounced := n_unannounced d; n_runs := n_runs d; n_published := n_published d;
     n_disposed := n_disposed d; n_queued := n_queued d; n_in_cleanup := n_in_cleanup d;
     n_scope := n_scope d; n_violated := n_violated d; n_body := n_body d; n_env := n_env d;
     n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition node_with_unannounced (v : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := v; n_runs := n_runs d; n_published := n_published d;
     n_disposed := n_disposed d; n_queued := n_queued d; n_in_cleanup := n_in_cleanup d;
     n_scope := n_scope d; n_violated := n_violated d; n_body := n_body d; n_env := n_env d;
     n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition node_with_runs (v : nat) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := v; n_published := n_published d;
     n_disposed := n_disposed d; n_queued := n_queued d; n_in_cleanup := n_in_cleanup d;
     n_scope := n_scope d; n_violated := n_violated d; n_body := n_body d; n_env := n_env d;
     n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition node_with_published (v : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d; n_published := v;
     n_disposed := n_disposed d; n_queued := n_queued d; n_in_cleanup := n_in_cleanup d;
     n_scope := n_scope d; n_violated := n_violated d; n_body := n_body d; n_env := n_env d;
     n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition node_with_disposed (v : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := v; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d;
     n_link := n_link d |}.

Definition node_with_queued (v : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := v;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d;
     n_link := n_link d |}.

Definition node_with_in_cleanup (v : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := v; n_scope := n_scope d; n_violated := n_violated d; n_body := n_body d;
     n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition node_with_scope (v : option owner_id) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := v; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d;
     n_link := n_link d |}.

Definition node_with_violated (v : bool) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := v; n_body := n_body d;
     n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d; n_link := n_link d |}.

Definition node_with_link (v : option link) (d : node) : node :=
  {| n_kind := n_kind d; n_value := n_value d; n_failure := n_failure d; n_status := n_status d;
     n_freshness := n_freshness d; n_unannounced := n_unannounced d; n_runs := n_runs d;
     n_published := n_published d; n_disposed := n_disposed d; n_queued := n_queued d;
     n_in_cleanup := n_in_cleanup d; n_scope := n_scope d; n_violated := n_violated d;
     n_body := n_body d; n_env := n_env d; n_mode := n_mode d; n_comparer := n_comparer d; n_link := v |}.

Definition emit (e : event) : M unit := m_modify (fun s => with_trace (g_trace s ++ [e]) s).

Definition get_node (n : node_id) : M node :=
  fun s => match g_nodes s !! n with Some d => Some (s, d) | None => None end.

Definition modify_node (n : node_id) (u : node -> node) : M unit :=
  fun s => match g_nodes s !! n with Some d => Some (set_node n (u d) s, tt) | None => None end.

Definition get_owner (o : owner_id) : M owner_state :=
  fun s => match g_owners s !! o with Some x => Some (s, x) | None => None end.

Definition put_owner (o : owner_id) (x : owner_state) : M unit :=
  m_modify (fun s => with_owners (<[o := x]> (g_owners s)) s).

Definition edges (u : EdgeWorld -> EdgeWorld) : M unit :=
  m_modify (fun s => with_edges (u (g_edges s)) s).

Definition edges_opt (u : EdgeWorld -> option EdgeWorld) : M unit :=
  fun s => match u (g_edges s) with Some w => Some (with_edges w s, tt) | None => None end.

(** [g_running] between a run's start and its [EndRun]. *)
Definition push_running (n : node_id) : M unit := m_modify (fun s => with_running (n :: g_running s) s).
Definition pop_running : M unit := m_modify (fun s => with_running (tail (g_running s)) s).

(** ** Ghost state *)

Definition stale_live (d : node) : bool := stale (n_freshness d) && negb (n_disposed d).

(** The sources of [c] read in its latest run: the slots before the cursor. *)
Definition read_prefix (w : EdgeWorld) (c : node_id) : list node_id :=
  fst <$> take (source_cursor (sources_of w c)) (source_slots (sources_of w c)).

(** A tracked read of [n] by [c], where [pre] is [c]'s read prefix ahead of the read. A read of a running
    node takes a view its run has yet to publish. *)
Definition ghost_read (c n : node_id) (pre : list node_id) (s : state) : state :=
  match g_nodes s !! n with
  | Some d =>
    let g := g_ghost s in
    let older := if decide (n ∈ pre) then default [] (gh_seen g !! (c, n)) else [] in
    with_ghost {| gh_published := gh_published g; gh_seen := <[(c, n) := read_view_of d :: older]> (gh_seen g);
                  gh_unnoticed := if decide (n ∈ g_running s) then {[c]} ∪ gh_unnoticed g
                                  else gh_unnoticed g;
                  gh_window := gh_window g;
                  gh_reads := <[c := default [] (gh_reads g !! c) ++ [n]]> (gh_reads g);
                  gh_passed := gh_passed g; gh_over := gh_over g;
                  gh_run_reads := <[c := default [] (gh_run_reads g !! c) ++ [n]]> (gh_run_reads g);
                  gh_nested := gh_nested g |} s
  | None => s
  end.

(** The start of a memo's run, which empties the slots before its cursor. A run started inside a run of the
    same memo leaves [gh_run_reads] as it was. *)
Definition ghost_run_start (n : node_id) (s : state) : state :=
  let g := g_ghost s in
  with_ghost {| gh_published := gh_published g; gh_seen := gh_seen g; gh_unnoticed := gh_unnoticed g ∖ {[n]};
                gh_window := gh_window g; gh_reads := <[n := []]> (gh_reads g);
                gh_passed := gh_passed g ∖ {[n]}; gh_over := gh_over g ∖ {[n]};
                gh_run_reads := if decide (n ∈ g_running s) then gh_run_reads g else <[n := []]> (gh_run_reads g);
                gh_nested := if decide (n ∈ g_running s) then {[n]} ∪ gh_nested g else gh_nested g ∖ {[n]} |} s.

(** The start of an effect's run, which empties the slots before its cursor. *)
Definition ghost_effect_start (n : node_id) (s : state) : state :=
  let g := g_ghost s in
  with_ghost {| gh_published := gh_published g; gh_seen := gh_seen g; gh_unnoticed := gh_unnoticed g;
                gh_window := gh_window g; gh_reads := <[n := []]> (gh_reads g);
                gh_passed := gh_passed g ∖ {[n]}; gh_over := gh_over g ∖ {[n]};
                gh_run_reads := <[n := []]> (gh_run_reads g); gh_nested := gh_nested g ∖ {[n]} |} s.

(** A view told to every observer: a creation or a write. *)
Definition ghost_publish (n : node_id) (s : state) : state :=
  match g_nodes s !! n with
  | Some d =>
    let g := g_ghost s in
    with_ghost {| gh_published := <[n := read_view_of d]> (gh_published g); gh_seen := gh_seen g;
                  gh_unnoticed := gh_unnoticed g; gh_window := gh_window g; gh_reads := gh_reads g;
                  gh_passed := gh_passed g; gh_over := gh_over g; gh_run_reads := gh_run_reads g;
                  gh_nested := gh_nested g |} s
  | None => s
  end.

(** The observers the end of [n]'s run leaves unnoticed. A moved run skips [r], which matters when [r]
    read [n] earlier in its run. A run with no move sends no notice, which matters when its view differs
    from the published one: a nested run published another. *)
Definition run_end_unnoticed (n : node_id) (r : option node_id) (moved : bool) (d : node) (s : state) : gset node_id :=
  if moved then
    match r with
    | Some c => if decide (n ∈ read_prefix (g_edges s) c) then {[c]} else ∅
    | None => ∅
    end
  else if decide (gh_published (g_ghost s) !! n = Some (read_view_of d)) then ∅
  else list_to_set (observer_view (observers_of (g_edges s) n)).

(** The end of [n]'s run, ahead of its notice. *)
Definition ghost_run_end (n : node_id) (r : option node_id) (moved : bool) (s : state) : state :=
  match g_nodes s !! n with
  | Some d =>
    let g := g_ghost s in
    with_ghost {| gh_published := <[n := read_view_of d]> (gh_published g); gh_seen := gh_seen g;
                  gh_unnoticed := run_end_unnoticed n r moved d s ∪ gh_unnoticed g;
                  gh_window := gh_window g; gh_reads := gh_reads g; gh_passed := gh_passed g; gh_over := gh_over g;
                  gh_run_reads := gh_run_reads g; gh_nested := gh_nested g |} s
  | None => s
  end.

(** [RecomputeScoped] clears a [Dirty] ahead of its discharge, with [runs] as [n_runs]. *)
Definition ghost_dirty_cleared (n : node_id) (runs : nat) (s : state) : state :=
  let g := g_ghost s in
  with_ghost {| gh_published := gh_published g; gh_seen := gh_seen g; gh_unnoticed := {[n]} ∪ gh_unnoticed g;
                gh_window := <[n := runs]> (gh_window g); gh_reads := gh_reads g; gh_passed := gh_passed g;
                gh_over := gh_over g; gh_run_reads := gh_run_reads g; gh_nested := gh_nested g |} s.

(** A slot of [n] holds a source satisfying [p]. *)
Definition slot_holds (p : node -> bool) (s : state) (n : node_id) : bool :=
  existsb (fun src => match g_nodes s !! src with Some e => p e | None => false end)
    (fst <$> source_slots (sources_of (g_edges s) n)).

Definition add_if (b : bool) (n : node_id) (x : gset node_id) : gset node_id := if b then {[n]} ∪ x else x.

(** A walk ending in [Check], ahead of [n] going [Clean]. *)
Definition ghost_resolved (n : node_id) (s : state) : state :=
  let g := g_ghost s in
  with_ghost {| gh_published := gh_published g; gh_seen := gh_seen g; gh_unnoticed := gh_unnoticed g;
                gh_window := gh_window g; gh_reads := gh_reads g;
                gh_passed := add_if (slot_holds (fun e => stale_live e && negb (n_unannounced e)) s n) n (gh_passed g);
                gh_over := add_if (slot_holds stale_live s n) n (gh_over g);
                gh_run_reads := gh_run_reads g; gh_nested := gh_nested g |} s.

(** A cleanup registered with an owner of host [h]. *)
Definition ghost_registered (h : node_id) (s : state) : state :=
  with_registered (<[h := S (default 0 (g_registered s !! h))]> (g_registered s)) s.

(** ** Owners *)

(** The id of the graph's root owner. *)
Definition root_owner_id : owner_id := 0.

(** A new scope, disposed from the start when its host already is. *)
Definition new_owner (host : node_id) (sink : option owner_id) (disposed : bool) : owner_state :=
  {| o_children := []; o_cleanups := []; o_cleanup_envs := []; o_cleanup_order := [];
     o_acquisitions := 0; o_generation := if disposed then 1 else 0; o_disposed := disposed; o_sink := sink;
     o_errors := []; o_host := host |}.

Definition owner_with_child (l : link) (x : owner_state) : owner_state :=
  {| o_children := o_children x ++ [l]; o_cleanups := o_cleanups x;
     o_cleanup_envs := o_cleanup_envs x; o_cleanup_order := o_cleanup_order x;
     o_acquisitions := S (o_acquisitions x); o_generation := o_generation x; o_disposed := o_disposed x;
     o_sink := o_sink x; o_errors := o_errors x; o_host := o_host x |}.

(** [Owner.AddCleanup] on a live scope: the writer of the three cleanup lists. *)
Definition owner_with_cleanup (b : cmd) (e : env) (x : owner_state) : owner_state :=
  {| o_children := o_children x; o_cleanups := o_cleanups x ++ [b];
     o_cleanup_envs := o_cleanup_envs x ++ [e]; o_cleanup_order := o_cleanup_order x ++ [o_acquisitions x];
     o_acquisitions := S (o_acquisitions x); o_generation := o_generation x; o_disposed := o_disposed x;
     o_sink := o_sink x; o_errors := o_errors x; o_host := o_host x |}.

(** The reader of the three cleanup lists: each cleanup with its environment and acquisition count,
    newest first. *)
Fixpoint zip_cleanups (bs : list cmd) (es : list env) (os : list nat) : list (cmd * env * nat) :=
  match bs, es, os with
  | b :: bs', e :: es', o :: os' => (b, e, o) :: zip_cleanups bs' es' os'
  | _, _, _ => []
  end.

Definition owner_cleanups (x : owner_state) : list (cmd * env * nat) :=
  rev (zip_cleanups (o_cleanups x) (o_cleanup_envs x) (o_cleanup_order x)).

(** The start of [DisposeScope]: both lists detached, and a new generation. *)
Definition owner_detached (x : owner_state) : owner_state :=
  {| o_children := []; o_cleanups := []; o_cleanup_envs := []; o_cleanup_order := [];
     o_acquisitions := 0; o_generation := S (o_generation x); o_disposed := o_disposed x;
     o_sink := o_sink x; o_errors := o_errors x; o_host := o_host x |}.

Definition owner_retired (x : owner_state) : owner_state :=
  {| o_children := o_children x; o_cleanups := o_cleanups x;
     o_cleanup_envs := o_cleanup_envs x; o_cleanup_order := o_cleanup_order x;
     o_acquisitions := o_acquisitions x; o_generation := o_generation x; o_disposed := true;
     o_sink := o_sink x; o_errors := o_errors x; o_host := o_host x |}.

Definition owner_with_error (f : val) (x : owner_state) : owner_state :=
  {| o_children := o_children x; o_cleanups := o_cleanups x;
     o_cleanup_envs := o_cleanup_envs x; o_cleanup_order := o_cleanup_order x;
     o_acquisitions := o_acquisitions x; o_generation := o_generation x; o_disposed := o_disposed x;
     o_sink := o_sink x; o_errors := o_errors x ++ [f]; o_host := o_host x |}.

Definition owner_without (l : link) (x : owner_state) : owner_state :=
  {| o_children :=
       filter (fun m => ~ (l_child m = l_child l /\ l_acquired m = l_acquired l)) (o_children x);
     o_cleanups := o_cleanups x; o_cleanup_envs := o_cleanup_envs x;
     o_cleanup_order := o_cleanup_order x; o_acquisitions := o_acquisitions x; o_generation := o_generation x;
     o_disposed := o_disposed x; o_sink := o_sink x; o_errors := o_errors x; o_host := o_host x |}.

(** [Owner.Unlink]: a link from an earlier generation changes nothing. *)
Definition unlink (l : link) : M unit :=
  let* x := get_owner (l_owner l) in
  if decide (l_generation l = Some (o_generation x)) then put_owner (l_owner l) (owner_without l x) else m_return tt.

(** [Owner.RecordError]. Every scope sinks into the root, and the root has no sink. *)
Definition record_error (o : owner_id) (f : val) : M unit :=
  let* x := get_owner o in
  match o_sink x with
  | Some p => let* y := get_owner p in put_owner p (owner_with_error f y)
  | None => put_owner o (owner_with_error f x)
  end.

(** The node an [EvLog], [EvCleanup] or [EvFail] names: the running computation, else the host of the
    current owner, which is a cleanup's node. The root owner's host is 0, and node ids start at 1. *)
Definition log_label : M node_id :=
  fun s =>
    match g_current s, g_current_owner s with
    | Some n, _ => Some (s, n)
    | None, Some o => Some (s, default 0 (o_host <$> g_owners s !! o))
    | None, None => Some (s, 0)
    end.

(** The failure of a node created under a pure memo. F# raises a new exception on each. *)
Definition pure_scope_failure : val := (-2)%Z.

(** The variable holding the previous value in a memo body's environment, and in the environments of
    closures it creates. Programs use smaller variables. *)
Definition prev_var : var := 4096%nat.

(** The environment of an unpublished memo's run, which drops the enclosing body's previous value. *)
Definition without_prev (e : env) : env := filter (fun p : var * val => p.1 <> prev_var) e.

(** ** Contexts *)

(** [Graph.Detached]. *)
Definition detached (o : owner_id) (body : M unit) : M unit :=
  fun s0 =>
    let body' :=
      let* _ := m_modify (fun s => with_batch_depth (S (g_batch_depth s)) (with_current_owner (Some o) (with_current None s))) in
      let* _ := body in
      m_modify (fun s => with_batch_depth (pred (g_batch_depth s)) (with_current_owner (g_current_owner s0) (with_current (g_current s0) s))) in
    body' s0.

(** [Graph.RunHosted]. *)
Definition hosted {A} (n : node_id) (body : M A) : M A :=
  fun s0 =>
    let body' :=
      let* _ := m_modify (fun s => with_current_owner None (with_current (Some n) s)) in
      let* r := body in
      let* _ := m_modify (fun s => with_current_owner (g_current_owner s0) (with_current (g_current s0) s)) in
      m_return r in
    body' s0.

(** [Graph.RunUntracked]. *)
Definition untracked {A} (body : M A) : M A :=
  fun s0 =>
    let flag := match g_current s0 with Some _ => true | None => g_untracked s0 end in
    let body' :=
      let* _ := m_modify (fun s => with_untracked flag (with_current None s)) in
      let* r := body in
      let* _ := m_modify (fun s => with_untracked (g_untracked s0) (with_current (g_current s0) s)) in
      m_return r in
    body' s0.

(** [Graph.Deferring]. *)
Definition deferring (s : state) : bool :=
  match g_current s with
  | Some _ => true
  | None => bool_decide (0 < g_pull_depth s)%nat
  end.

Definition live_queue (s : state) : bool := bool_decide (g_queue_head s < length (g_queue s))%nat.

(** The tests of [RequestFlush] and [SettleFlush] beside the batch depth. *)
Definition outside_bodies (s : state) : bool :=
  bool_decide (g_pull_depth s = 0%nat) &&
  match g_current s with Some _ => false | None => true end &&
  negb (g_untracked s).

(** [Graph.Track]. *)
Definition track (n : node_id) : M unit :=
  fun s =>
    match g_current s with
    | Some c =>
      match sources_add c n (g_edges s) with
      | Some w => Some (ghost_read c n (read_prefix (g_edges s) c) (with_edges w s), tt)
      | None => None
      end
    | None => Some (s, tt)
    end.

(** [Graph.MarkReaderCheck], under [f_reader_mark]. *)
Definition mark_reader (fuel : nat) : M unit :=
  fun s =>
    match g_current s with
    | Some c =>
      if f_reader_mark (g_flags s)
      then match mark_check fuel c s with Some s' => Some (s', tt) | None => None end
      else Some (s, tt)
    | None => Some (s, tt)
    end.

Definition on_value (e : env) (x : expr) (k : val -> M outcome) : M outcome :=
  fun s =>
    match eval_expr e (env_lookup e prev_var) x with
    | Some (Ok v) => k v s
    | Some (Fail f) => Some (s, Fail f)
    | None => None
    end.

Definition on_node (e : env) (x : expr) (k : node_id -> M outcome) : M outcome :=
  on_value e x (fun z => if decide (0 <= z)%Z then k (Z.to_nat z) else stuck).

(** The end of a run left stale, under [f_unannounced]. *)
Definition announce (n : node_id) : M unit :=
  fun s => if f_unannounced (g_flags s) then modify_node n (node_with_unannounced true) s else Some (s, tt).

(** A host read: outside every computation and cleanup. *)
Definition note_host_read (n : node_id) (o : outcome) : M unit :=
  fun s =>
    match g_current s, g_current_owner s with
    | None, Some r =>
      if decide (r = root_owner_id) then emit (EvHostRead n o) s else Some (s, tt)
    | _, _ => Some (s, tt)
    end.

(** [Graph.NextId]. *)
Definition next_id : M node_id :=
  fun s => Some (with_next_id (S (g_next_id s)) s, S (g_next_id s)).

Definition signal_node (v : val) : node :=
  {| n_kind := KSignal; n_value := v; n_failure := None; n_status := StNone; n_freshness := Clean;
     n_unannounced := false; n_runs := 0; n_published := false; n_disposed := false; n_queued := false;
     n_in_cleanup := false; n_scope := None; n_violated := false; n_body := CRet (EConst 0%Z);
     n_env := []; n_mode := Pure; n_comparer := CmpEq; n_link := None |}.

(** The fields of [Memo] at construction: [Dirty] and [Uninitialized], with the default value. *)
Definition memo_node (m : scope_mode) (c : comparer) (b : cmd) (e : env) : node :=
  {| n_kind := KMemo; n_value := 0%Z; n_failure := None; n_status := StUninitialized; n_freshness := Dirty;
     n_unannounced := false; n_runs := 0; n_published := false; n_disposed := false; n_queued := false;
     n_in_cleanup := false; n_scope := None; n_violated := false; n_body := b;
     n_env := e; n_mode := m; n_comparer := c; n_link := None |}.

(** The fields of [Effect] at construction: [Clean] and [Uninitialized]. *)
Definition effect_node (b : cmd) (e : env) : node :=
  {| n_kind := KEffect; n_value := 0%Z; n_failure := None; n_status := StUninitialized; n_freshness := Clean;
     n_unannounced := false; n_runs := 0; n_published := false; n_disposed := false; n_queued := false;
     n_in_cleanup := false; n_scope := None; n_violated := false; n_body := b;
     n_env := e; n_mode := Owning; n_comparer := CmpEq; n_link := None |}.

Definition add_node (n : node_id) (d : node) : M unit := m_modify (fun s => ghost_publish n (set_node n d s)).

(** [Graph.CurrentOwner]. [None] is a pure memo's refusal: the [InvalidOperationException] of its
    [IScopeHost.Scope], with [n_violated] set. *)
Definition current_owner : M (option owner_id) :=
  fun s =>
    match g_current_owner s, g_current s with
    | Some o, _ => Some (s, Some o)
    | None, None => None
    | None, Some h =>
      match g_nodes s !! h with
      | None => None
      | Some d =>
        match n_scope d with
        | Some o => Some (with_current_owner (Some o) s, Some o)
        | None =>
          if match n_kind d, n_mode d with KMemo, Pure => true | _, _ => false end
          then Some (set_node h (node_with_violated true d) s, None)
          else
            let o : owner_id := stdpp.base.fresh (dom (g_owners s)) in
            let s1 := with_owners (<[o := new_owner h (Some root_owner_id) (n_disposed d)]> (g_owners s)) s in
            Some (with_current_owner (Some o) (set_node h (node_with_scope (Some o) d) s1), Some o)
        end
      end
    end.

(** ** The interpreter *)

(**md
Runs the command `c` as a step of the engine. This function and the functions defined with it
(`read`, `refresh`, `recompute`, `run_memo`, `execute_effect`, `run_flush` and the rest) each follow the
member of `Core.fs` that its comment names. An F# exception inside a body is a `Fail` outcome, so the
`finally` paths are the code after the body returns.

- [fuel:] the bound on nested steps. It reaching `0` gives `None`.
- [e:] the local variable environment of the command.
- [c:] the command run.
*)
Fixpoint exec (fuel : nat) (e : env) (c : cmd) {struct fuel} : M outcome :=
  match fuel with
  | 0 => stuck
  | S f =>
    match c with
    | CRet x => on_value e x (fun v => m_return (Ok v))
    | CThrow y => m_return (Fail y)
    | CLet x v k => on_value e v (fun z => exec f ((x, z) :: e) k)
    | CIf v a b => on_value e v (fun z => if decide (z = 0%Z) then exec f e b else exec f e a)
    | CRead x n k =>
      on_node e n (fun t =>
        let* o := read f t true in
        let* _ := note_host_read t o in
        match o with
        | Ok v => exec f ((x, v) :: e) k
        | Fail y => m_return (Fail y)
        end)
    | CTryRead x ok n k =>
      on_node e n (fun t =>
        let* o := read f t true in
        let* _ := note_host_read t o in
        match o with
        | Ok v => exec f ((ok, 1%Z) :: (x, v) :: e) k
        | Fail y => exec f ((ok, 0%Z) :: (x, y) :: e) k
        end)
    | CReadUntracked x n k =>
      on_node e n (fun t =>
        let* o := read f t false in
        let* _ := note_host_read t o in
        match o with
        | Ok v => exec f ((x, v) :: e) k
        | Fail y => m_return (Fail y)
        end)
    | CWrite n v k =>
      on_node e n (fun t =>
        on_value e v (fun z =>
          let* _ := write_signal f t z in
          exec f e k))
    | CSignal x v k =>
      on_value e v (fun z =>
        let* n := next_id in
        let* _ := add_node n (signal_node z) in
        exec f ((x, Z.of_nat n) :: e) k)
    | CMemo x m q b k =>
      let* n := next_id in
      let e' := (x, Z.of_nat n) :: e in
      let* _ := add_node n (memo_node m q b e') in
      let* owner := current_owner in
      match owner with
      | None => m_return (Fail pure_scope_failure)
      | Some o =>
        let* _ := attach f n o in
        exec f e' k
      end
    | CEffect x b k =>
      let* n := next_id in
      let e' := (x, Z.of_nat n) :: e in
      let* _ := add_node n (effect_node b e') in
      let* owner := current_owner in
      match owner with
      | None => m_return (Fail pure_scope_failure)
      | Some o =>
        let* _ := attach f n o in
        let* _ := m_step (mark_dirty f n) in
        let* _ := request_flush f in
        exec f e' k
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
            let* _ := detached o (run_cleanup f o b e) in
            request_flush f
          else put_owner o (owner_with_cleanup b e x) in
        exec f e k
      end
    | CDispose n k =>
      on_node e n (fun t =>
        let* d := get_node t in
        match n_kind d with
        | KSignal => stuck
        | _ =>
          let* _ := dispose_node f t in
          let* _ := if n_disposed d then m_return tt else emit (EvDispose t) in
          exec f e k
        end)
    | CBatch b k =>
      let* _ := m_modify (fun s => with_batch_depth (S (g_batch_depth s)) s) in
      let* r := exec f e b in
      let* _ := m_modify (fun s => with_batch_depth (pred (g_batch_depth s)) s) in
      match r with
      | Ok _ =>
        let* _ := request_flush f in
        exec f e k
      | Fail y => m_return (Fail y)
      end
    | CLog v k =>
      on_value e v (fun z =>
        let* l := log_label in
        let* _ := emit (EvLog l z) in
        exec f e k)
    end
  end

(** [Signal.Value], [Memo.Value], or the same inside [Graph.Untrack]. *)
with read (fuel : nat) (n : node_id) (tracked : bool) {struct fuel} : M outcome :=
  match fuel with
  | 0 => stuck
  | S f => if tracked then read_node f n else untracked (read_node f n)
  end

with read_node (fuel : nat) (n : node_id) {struct fuel} : M outcome :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_kind d with
    | KSignal =>
      let* _ := track n in
      m_return (Ok (n_value d))
    | KEffect => stuck
    | KMemo =>
      (* TrackCurrent *)
      let* _ :=
        if stale_live d then
          let* _ := refresh_or_pull f n in
          let* _ := track n in
          let* d' := get_node n in
          if stale_live d' then mark_reader f else m_return tt
        else track n in
      let* d' := get_node n in
      match n_status d' with
      | StError => m_return (Fail (default 0%Z (n_failure d')))
      | _ => m_return (Ok (n_value d'))
      end
    end
  end

(** [Memo.EnsureCurrent]. A signal has nothing to bring current. *)
with update_if_necessary (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_kind d with
    | KMemo => if stale_live d then refresh_or_pull f n else m_return tt
    | _ => m_return tt
    end
  end

with refresh_or_pull (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if deferring s then refresh f n else pull f n
  end

(** [Memo.Pull]: [EnterPull], [Refresh] and [ExitPull]. *)
with pull (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := m_modify (fun s => with_pull_depth (S (g_pull_depth s)) s) in
    let* _ := refresh f n in
    let* _ := m_modify (fun s => with_pull_depth (pred (g_pull_depth s)) s) in
    let* s := m_get in
    if bool_decide (g_pull_depth s = 0%nat) && g_flush_owed s then settle_flush f else m_return tt
  end

(** [Memo.Refresh], with [ResolveCheck]. *)
with refresh (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let* _ :=
      if decide (n_freshness d = Check) then
        let* _ := resolve_walk f n 0 in
        let* d' := get_node n in
        if decide (n_freshness d' = Check)
        then
          let* _ := m_modify (ghost_resolved n) in
          modify_node n (fun x => node_with_unannounced false (node_with_freshness Clean x))
        else m_return tt
      else m_return tt in
    let* d' := get_node n in
    if decide (n_freshness d' = Dirty) then recompute f n else m_return tt
  end

(** The loop of [ResolveCheck] and of [Effect.Execute]. The slot count and the freshness are read at
    each step. *)
with resolve_walk (fuel : nat) (n : node_id) (i : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let* s := m_get in
    if decide (n_freshness d = Check) then
      match source_slots (sources_of (g_edges s) n) !! i with
      | Some (src, _) =>
        let* _ := update_if_necessary f src in
        resolve_walk f n (S i)
      | None => m_return tt
      end
    else m_return tt
  end

(** [Memo.Recompute] and [Memo.RecomputeScoped]. *)
with recompute (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    match n_scope d with
    | None => run_memo f n
    | Some o =>
      let* _ := modify_node n (node_with_freshness Clean) in
      let* _ := m_modify (ghost_dirty_cleared n (n_runs d)) in
      let* _ := discharge f o in
      let* d' := get_node n in
      if n_disposed d' then m_return tt
      else if decide (n_runs d' = n_runs d) then run_memo f n
      else if decide (n_freshness d' = Dirty) then recompute f n
      else m_return tt
    end
  end

(** [Memo.Run]. *)
with run_memo (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let previous := n_value d in
    let previous_status := n_status d in
    let previous_failure := n_failure d in
    let* _ := modify_node n (fun x =>
      node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone
        (node_with_unannounced false (node_with_freshness Clean x))))) in
    let* _ := edges (sources_begin_run n) in
    let* _ := m_modify (ghost_run_start n) in
    let* _ := push_running n in
    let body_env :=
      if n_published d then (prev_var, previous) :: n_env d else without_prev (n_env d) in
    let* r := hosted n (exec f body_env (n_body d)) in
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
    if decide (n_freshness d3 = Clean) then m_return tt else announce n
  end

(** [Effect.Execute]. *)
with execute_effect (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    if n_disposed d then modify_node n (node_with_queued false)
    else
      let* _ :=
        if decide (n_freshness d = Check) then
          let* _ := resolve_walk f n 0 in
          let* d' := get_node n in
          if decide (n_freshness d' = Check)
          then
            let* _ := m_modify (ghost_resolved n) in
            modify_node n (node_with_freshness Clean)
          else m_return tt
        else m_return tt in
      let* d' := get_node n in
      let* _ := modify_node n (fun x => node_with_freshness Clean (node_with_queued false x)) in
      if decide (n_freshness d' = Dirty) then run_effect f n else m_return tt
  end

(** [Effect.Run]. *)
with run_effect (fuel : nat) (n : node_id) {struct fuel} : M unit :=
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
        let* _ := discharge f o in
        modify_node n (node_with_in_cleanup false)
      | None => m_return tt
      end in
    let* d' := get_node n in
    let* _ := if n_disposed d' then m_return tt else run_effect_body f n in
    pop_running
  end

(** [Effect.RunBody]. *)
with run_effect_body (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* d := get_node n in
    let* _ := modify_node n (fun x => node_with_runs (S (n_runs x)) (node_with_failure None (node_with_status StNone x))) in
    let* _ := emit (EvRun n) in
    let* r := hosted n (exec f (n_env d) (n_body d)) in
    let* d' := get_node n in
    let* _ := edges_opt (if n_disposed d' then sources_clear n else sources_end_run n) in
    match r with
    | Ok _ => m_return tt
    | Fail y =>
      let* _ := modify_node n (fun x => node_with_status StError (node_with_failure (Some y) x)) in
      emit (EvFail n y)
    end
  end

(** [Graph.Discharge]. *)
with discharge (fuel : nat) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := detached o (dispose_scope f o) in
    let* s := m_get in
    if bool_decide (g_batch_depth s = 0%nat) && negb (g_flushing s) && live_queue s
    then m_modify (with_flush_owed true)
    else m_return tt
  end

(** [Graph.Retire], with [Owner.Dispose]. *)
with retire (fuel : nat) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := detached o (
      let* x := get_owner o in
      if o_disposed x then m_return tt
      else
        let* _ := put_owner o (owner_retired x) in
        dispose_scope f o) in
    let* s := m_get in
    if live_queue s then request_flush f else m_return tt
  end

(** [Owner.DisposeScope]. *)
with dispose_scope (fuel : nat) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* x := get_owner o in
    let* _ := put_owner o (owner_detached x) in
    teardown f o (rev (o_children x)) (owner_cleanups x)
  end

(** The loop of [DisposeScope]: children and cleanups, newest acquisition first. *)
with teardown (fuel : nat) (o : owner_id) (ls : list link) (cs : list (cmd * env * nat)) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    match ls, cs with
    | [], [] => m_return tt
    | l :: ls', [] =>
      let* _ := release f (l_child l) in
      teardown f o ls' []
    | [], (b, e, _) :: cs' =>
      let* _ := run_cleanup f o b e in
      teardown f o [] cs'
    | l :: ls', (b, e, a) :: cs' =>
      if decide (a < l_acquired l)%nat then
        let* _ := release f (l_child l) in
        teardown f o ls' cs
      else
        let* _ := run_cleanup f o b e in
        teardown f o ls cs'
    end
  end

(** A cleanup: its event, its body, and [RecordError] for its failure. *)
with run_cleanup (fuel : nat) (o : owner_id) (b : cmd) (e : env) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* x := get_owner o in
    let* _ := emit (EvCleanup (o_host x)) in
    let* r := exec f e b in
    match r with
    | Ok _ => m_return tt
    | Fail y =>
      let* _ := record_error o y in
      emit (EvFail (o_host x) y)
    end
  end

(** [IOwned.Release]. *)
with release (fuel : nat) (n : node_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* _ := modify_node n (node_with_link None) in
    dispose_node f n
  end

(** [Memo.Dispose] and [Effect.Dispose]. *)
with dispose_node (fuel : nat) (n : node_id) {struct fuel} : M unit :=
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
      | Some o => retire f o
      | None => m_return tt
      end
  end

(** [Owner.AttachLinked]: on a disposed owner, the node is released at once and holds a link with no
    generation. *)
with attach (fuel : nat) (n : node_id) (o : owner_id) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* x := get_owner o in
    if o_disposed x then
      let* _ := release f n in
      modify_node n (node_with_link (Some {| l_child := n; l_owner := o; l_generation := None; l_acquired := 0 |}))
    else
      let l := {| l_child := n; l_owner := o; l_generation := Some (o_generation x); l_acquired := o_acquisitions x |} in
      let* _ := put_owner o (owner_with_child l x) in
      modify_node n (node_with_link (Some l))
  end

(** The setter of [Signal.Value]. *)
with write_signal (fuel : nat) (n : node_id) (v : val) {struct fuel} : M unit :=
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
        let* _ := m_step (notify_dirty f n) in
        request_flush f
    | _ => stuck
    end
  end

(** [Graph.RequestFlush]. *)
with request_flush (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if live_queue s && bool_decide (g_batch_depth s = 0%nat) then
      if outside_bodies s then run_flush f
      else if g_flushing s then m_return tt
      else m_modify (with_flush_owed true)
    else m_return tt
  end

(** [Graph.SettleFlush]. *)
with settle_flush (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if g_flush_owed s && bool_decide (g_batch_depth s = 0%nat) && outside_bodies s then run_flush f else m_return tt
  end

(** [Graph.RunFlush]. *)
with run_flush (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    if g_flushing s then m_return tt
    else
      let* _ := m_modify (fun s => with_flush_owed false (with_flushing true s)) in
      let* _ := drain f in
      m_modify (with_flushing false)
  end

(** The loop of [RunFlush]. A clean drain resets the queue. *)
with drain (fuel : nat) {struct fuel} : M unit :=
  match fuel with
  | 0 => stuck
  | S f =>
    let* s := m_get in
    match g_queue s !! g_queue_head s with
    | Some n =>
      let* _ := m_modify (with_queue_head (S (g_queue_head s))) in
      let* _ := execute_effect f n in
      drain f
    | None => m_modify (fun s => with_queue [] (with_queue_head 0 s))
    end
  end.

(** A new graph, with the root owner as the current owner. *)
Definition init (fl : flags) : state :=
  {| g_flags := fl; g_edges := empty_world; g_nodes := ∅;
     g_owners := <[root_owner_id := new_owner 0 None false]> ∅; g_queue := []; g_queue_head := 0;
     g_flushing := false; g_flush_owed := false; g_batch_depth := 0; g_pull_depth := 0; g_current := None;
     g_untracked := false; g_current_owner := Some root_owner_id; g_next_id := 0; g_trace := []; g_running := [];
     g_ghost := empty_ghost; g_registered := ∅; g_skip := SkipOff; g_epoch := 0; g_stamps := ∅ |}.

(**md
Runs the top-level command `c` in an empty environment.

- [fuel:] the bound on nested steps.
- [c:] the command run.
*)
Definition run_top (fuel : nat) (c : cmd) : M outcome := exec fuel [] c.

(**md
The event trace of running `c` on a new graph, or `None` when the run is stuck or runs out of fuel.

- [fuel:] the bound on nested steps.
- [fl:] the flags of the new graph.
- [c:] the command run.
*)
Definition trace_of (fuel : nat) (fl : flags) (c : cmd) : option (list event) :=
  match run_top fuel c (init fl) with
  | Some (s, _) => Some (g_trace s)
  | None => None
  end.
