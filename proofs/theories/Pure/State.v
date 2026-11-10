From stdpp Require Import prelude gmap.
From Ranvier.Base Require Import Prelude Syntax.
From Ranvier.Pure Require Import ObserverSet SourceList.

(**md
The kind of a node: a signal, a memo or an effect.
*)
Inductive kind := KSignal | KMemo | KEffect.

(** A node's stored [Status]. [n_freshness] determines [Status.Stale]. *)
Inductive status := StNone | StPending | StError | StUninitialized.

Global Instance status_eq_dec : EqDecision status.
Proof. solve_decision. Defined.

(** [OwnerLink]. [l_generation] is the generation the link joined, and [None] once the link has left the
    list, the F# [-1]. *)
Record link := { l_child : node_id; l_owner : owner_id; l_generation : option nat; l_acquired : nat }.

(** [Owner]. [o_children] is stored head to tail. [o_cleanups], [o_cleanup_envs] and [o_cleanup_order]
    are parallel and stored earliest first, and [o_cleanup_envs] holds each cleanup's captured
    environment. Teardown runs both from the end. [o_host] is the node whose scope this is, and labels
    the events of the owner's cleanups. *)
Record owner_state := {
  o_children : list link;
  o_cleanups : list cmd;
  o_cleanup_envs : list env;
  o_cleanup_order : list nat;
  o_acquisitions : nat;
  o_generation : nat;
  o_disposed : bool;
  o_sink : option owner_id;
  o_errors : list val;
  o_host : node_id
}.

(**md
A node of the graph. `n_kind` selects signal, memo or effect. `n_value`, `n_failure` and `n_status` are
what a read answers from, and `n_freshness` is the mark that decides whether a read must recompute.
`n_body`, `n_env`, `n_mode` and `n_comparer` are the body of a memo or effect, its captured
environment, its scope mode and its comparer. `n_scope` is the owner opened for the node's own
creations, and `n_link` is the link by which the node's owner holds it. `n_runs` counts runs.
*)
Record node := { n_kind : kind; n_value : val; n_failure : option val; n_status : status;
                 n_freshness : freshness; n_unannounced : bool; n_runs : nat; n_published : bool;
                 n_disposed : bool; n_queued : bool; n_in_cleanup : bool; n_scope : option owner_id;
                 n_violated : bool; n_body : cmd; n_env : env; n_mode : scope_mode; n_comparer : comparer;
                 n_link : option link }.

(** [f_unannounced] enables a stale run's end setting [n_unannounced] and the [elif unannounced] branches of
    marking, and [f_reader_mark] the [MarkReaderCheck] of a read. Clearing one gives the engine before that fix. *)
Record flags := { f_unannounced : bool; f_reader_mark : bool }.

Definition current_flags : flags := {| f_unannounced := true; f_reader_mark := true |}.

(** What a read of a node answers from: its value, status and failure. *)
Definition read_view : Type := val * status * option val.

Definition read_view_of (d : node) : read_view := (n_value d, n_status d, n_failure d).

(** Ghost state of check resolution. By reading [Engine.v], only its [ghost_] functions touch this field,
    and no engine decision reads it. *)
Record ghost := {
  gh_published : gmap node_id read_view; (* a node's view as its observers were last told it *)
  gh_seen : gmap (node_id * node_id) (list read_view); (* a computation's reads of a source, newest first *)
  gh_unnoticed : gset node_id; (* computations whose reads can differ from [gh_published] with no [Dirty] *)
  gh_window : gmap node_id nat; (* [n_runs] at the latest [RecomputeScoped] clearing a memo's [Dirty] *)
  gh_reads : gmap node_id (list node_id); (* a computation's tracked reads since its latest run start, in order *)
  gh_passed : gset node_id; (* computations a walk ended [Clean] over a live, stale, unannounced source, since their latest run start *)
  gh_over : gset node_id; (* computations a walk ended [Clean] over a live, stale source, since their latest run start *)
  gh_run_reads : gmap node_id (list node_id); (* a computation's tracked reads since its outermost run start, in order *)
  gh_nested : gset node_id (* computations with a run started inside their own, since their outermost run start *)
}.

Definition empty_ghost : ghost :=
  {| gh_published := ∅; gh_seen := ∅; gh_unnoticed := ∅; gh_window := ∅; gh_reads := ∅; gh_passed := ∅; gh_over := ∅;
     gh_run_reads := ∅; gh_nested := ∅ |}.

(**md
The saturated-write skip of `Pure/Optimised.v`. `SkipOff` is the present engine. `SkipAll` advances the
graph epoch at every write that can make a saturated observer unsaturated, and a walk records only when
it leaves every observer saturated. `SkipLeaveDirty` advances only where a node leaves `Dirty`, and
`SkipUnchecked` records after every walk. Both exist for the counterexamples of `Pure/Optimised.v`.
*)
Inductive skip_mode := SkipOff | SkipLeaveDirty | SkipUnchecked | SkipAll.

Global Instance skip_mode_eq_dec : EqDecision skip_mode.
Proof. solve_decision. Defined.

(**md
The whole state of a graph: the edge world (`g_edges`), the nodes, the owners, the effect queue with its
head, the flush and batch bookkeeping, the running computation and owner, the id counter, the event
trace (`g_trace`), the ghost fields `g_running`, `g_ghost` and `g_registered`, which no engine
decision reads, and the fields of the saturated-write skip: its mode (`g_skip`), the graph epoch
(`g_epoch`) and each signal's stamp (`g_stamps`). The last three are read only by the optimised engine of `Pure/Optimised.v`.
*)
Record state := { g_flags : flags; g_edges : EdgeWorld; g_nodes : gmap node_id node;
                  g_owners : gmap owner_id owner_state; g_queue : list node_id; g_queue_head : nat;
                  g_flushing : bool; g_flush_owed : bool; g_batch_depth : nat; g_pull_depth : nat;
                  g_current : option node_id; g_untracked : bool; g_current_owner : option owner_id;
                  g_next_id : nat; g_trace : list event;
                  g_running : list node_id (* ghost: between a run's start and EndRun *);
                  g_ghost : ghost;
                  g_registered : gmap node_id nat (* ghost: the cleanups registered per host *);
                  g_skip : skip_mode; g_epoch : nat; g_stamps : gmap node_id nat }.

(**md
`c` observes `src`: a slot of `c` and a position of `src`'s observers each hold the other.

- [s:] the state.
- [src:] the observed source.
- [c:] the observing computation.
*)
Definition edge (s : state) (src c : node_id) : Prop :=
  exists i h, slot_at (g_edges s) c i = Some (src, Some h) /\
              observer_items (observers_of (g_edges s) src) !! h = Some (c, i).
