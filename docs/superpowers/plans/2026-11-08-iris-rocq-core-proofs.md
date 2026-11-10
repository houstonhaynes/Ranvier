# Iris/Rocq Proofs of Ranvier's Synchronous Core: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Rocq project in `proofs/` that models Ranvier's synchronous core, proves cumulative theorems from `ObserverSet` up to whole-graph refinement, and collects every defect it finds in a ledger.

**Architecture:** Three tiers. A reference semantics states what a graph means, with no engine concepts. A pure tier is a fuel-bounded executable interpreter of the engine, on which whole-graph theorems are proved. A heap tier implements the same operations in HeapLang, with Iris specifications that tie each to a pure transition. The F# library generates expected traces for a scenario set, and the interpreter must reproduce them, which is the model's evidence of matching Core.fs.

**Tech Stack:** Rocq 9.1 from Rocq Platform 2026.07.0, stdpp, Iris and HeapLang at the versions that release bundles; F# scripts run by `dotnet fsi`, for conformance only.

**Spec:** `docs/superpowers/specs/2026-11-08-iris-rocq-core-proofs-design.md`. Read it before any task.

## Global Constraints

- The model pins `src/Ranvier/Core.fs` at commit `d701e6c5`. Read Core.fs at that commit: `git show d701e6c5:src/Ranvier/Core.fs`.
- `src/Ranvier` is unchanged until the fix phase, apart from the worked optimisation on its own branch.
- The reference tier imports nothing from `Pure/` or `Heap/`. Layers import only lower layers.
- No `Admitted`, `admit` or added `Axiom` outside theorems named in `proofs/BUGS.md`.
- A failing theorem is a result. Theorems state intended behaviour and are never weakened only so they pass.
- A later layer takes a failed theorem as a `Section` variable or explicit premise, never as an `Axiom`.
- Only Confirmed ledger entries count as bugs. Where Core.fs documents the behaviour as intended, the entry closes as "spec corrected".
- Every simplification appears in `proofs/CORRESPONDENCE.md` with its reason.
- Comments in `.v` files follow `.claude/rules/comments.md`.
- Shell commands are prefixed with `rtk`. Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Drift: the differential check runs in CI when `src/Ranvier/Core.fs` or `proofs/` changes. A behaviour change fails it. Its agreement is evidence, never a proof.

## Review Focus

Most likely to bite first. Each line's scenario is added to the task named at its end.

1. A failure partway through a flush: an effect body's failure is recorded in its status and later effects still run, and a failure that escapes `Execute`, such as from a cleanup during `Discharge`, runs the `finally` paths and leaves the queue resumable (Task 7, scenarios `S09a`, `S09b`).
2. A memo cleanup writes a source and reads the memo, so a nested run happens inside `RecomputeScoped` and resets the `SourceList` cursor of an outer run (Task 7, `S11`).
3. A body disposes its own computation mid-run: edges are cleared at the end of the run, and later marks do nothing (Task 7, `S10`).
4. A body reads A, B, A, and a later trim swap-removes so that an edge later in the same list moves while being trimmed (Task 4, `sources_trim_moves_own_edge`).
5. `TryAdd` on a list longer than `ScanLimit` with a stale `latest` answers `Unknown`, and the source list must fall back to `holds` without adding a second edge (Task 4, `sources_add_unknown_no_duplicate`).

## How every task runs

**Build:** `rtk bash proofs/build.sh`, which exits 0 on a clean build. **Audit:** `rtk bash proofs/check.sh`, which exits 0 when:

- every theorem in `theories/Audit.v` prints only allowlisted assumptions;
- `Admitted`/`admit`/`Axiom` occur only in theorems listed under `## Open theorems` in `proofs/BUGS.md`;
- regenerating `theories/Conformance/Expected.v` leaves it unchanged.

**Cycle for a proof task.** The first step lists the statements, the check is the audit, and each task's steps below name only what differs:

1. Add the task's theorem names to `theories/Audit.v` and run the audit. Expected: fails, names undefined.
2. Write definitions and statements exactly as the task gives them, adapting only what a proof shows to be a slip in form, never in strength.
3. Prove them. Run the audit. Expected: exit 0.
4. Update `CORRESPONDENCE.md` for each symbol modelled, and `README.md`'s theorem index.
5. Commit, one commit per task, on `claude/iris-rocq-ranvier-project-f3d6ae`.

**When a theorem resists:**

1. Search for a counterexample with `vm_compute` on the interpreter (pure tier) before spending more on the proof.
2. **Counterexample found:** add it to `theories/Pure/Witnesses.v` as a checked `Example`, enter it in `BUGS.md` as Modelled, add its scenario to `Scenarios.fsx`, and run it against the F#.
   - It reproduces: mark it Confirmed, or close it as "spec corrected" if Core.fs documents the behaviour. State the theorem against fixed behaviour; it is an Open theorem.
   - It does not reproduce: the model is wrong. Fix the model and record the correspondence error.
3. Layers above take the theorem as a named premise. Keep going.
4. **No proof and no counterexample after real effort, including trying a strengthened invariant:** that is a stall. Stop and report to the owner. Do not ledger it as a bug.

A redundant run found against exact refinement gets a checked counterexample and the ledger status **Awaiting ruling**. Work continues; the owner rules on the collected list at the end.

## File structure

```
proofs/
  README.md  CORRESPONDENCE.md  BUGS.md  VERSIONS.md
  _CoqProject  build.sh  check.sh
  conformance/Scenarios.fsx     F# → theories/Conformance/Expected.v
  theories/
    Base/Prelude.v              ids, freshness order, shared tactics
    Base/Syntax.v               the command language and its pure expressions
    Reference/Semantics.v       meaning of a program and history
    Reference/Properties.v      determinism, glitch freedom, redundancy
    Pure/ObserverSet.v          layer 1
    Pure/SourceList.v           layer 2, over EdgeWorld
    Pure/State.v                graph state, including the ghost running stack
    Pure/Edges.v                layer 3
    Pure/Marking.v              layer 4
    Pure/Engine.v               mutual interpreter of layers 5 to 8
    Pure/Check.v                layer 5 theorems
    Pure/Runs.v                 layer 6 theorems
    Pure/Scheduler.v            layer 7 theorems
    Pure/Ownership.v            layer 8 theorems
    Pure/WholeGraph.v           layer 9 pure theorems
    Pure/Witnesses.v            regression witnesses and ledger traces
    Pure/Refinement.v           layer 10
    Conformance/Expected.v      generated from the F#, committed
    Conformance/Check.v         the interpreter reproduces Expected.v
    Heap/Ghost.v                resource algebras, is_graph
    Heap/ObserverSet.v  Heap/SourceList.v  Heap/Marking.v
    Heap/Run.v  Heap/Scheduler.v  Heap/Ownership.v  Heap/Adequacy.v
    Audit.v                     Print Assumptions for headline theorems
```

---

### Task 1: Toolchain and scaffold

**Files:**
- Create: `proofs/_CoqProject`, `proofs/build.sh`, `proofs/check.sh`, `proofs/VERSIONS.md`, `proofs/README.md`, `proofs/BUGS.md`, `proofs/CORRESPONDENCE.md`, `proofs/theories/Audit.v`, `proofs/theories/Base/Prelude.v`, `.gitignore` entries for `*.vo`, `*.vok`, `*.vos`, `*.glob`, `.*.aux`, `Makefile.rocq*`

**Interfaces:**
- Produces: `build.sh` and `check.sh` as specified under "How every task runs"; the `Ranvier` logical root (`-Q theories Ranvier`); `BUGS.md` sections `## Suspected`, `## Modelled`, `## Confirmed`, `## Awaiting ruling`, `## Closed`, `## Open theorems`.

- [ ] **Step 1: Download and install on Windows**

Download `signed_Rocq-Platform-release-2026.07.0-version.9.1-Windows-x86_64.exe` (540 MB) from the `rocq-prover/platform` release `2026.07.0` into its own new directory under the scratchpad. Run it for the current user at the Full level. If the installer is interactive, open it and ask the owner to click through.

- [ ] **Step 2: Smoke-test HeapLang from the Windows install**

In a scratchpad directory, compile, through `rocq-shell.bat`'s environment:

```rocq
From iris.heap_lang Require Import proofmode notation.
Lemma smoke `{!heapGS Σ} (l : loc) : l ↦ #1 ⊢ WP !#l {{ v, ⌜v = #1⌝ ∗ l ↦ #1 }}.
Proof. iIntros "H". wp_load. iFrame. done. Qed.
```

Expected: compiles with no error. Also test `make --version` from the same environment and whether Git Bash can reach the tools.

- [ ] **Step 3: Decide the platform**

Windows stands if the smoke test compiles and `make` is reachable from a shell that `build.sh` can run in. Otherwise fall back to WSL Ubuntu 24.04: install opam, create switch `ranvier-proofs`, add `https://rocq-prover.org/opam/released` and the Iris repository, and install `rocq-prover` 9.1 and `coq-iris-heap-lang`. Re-run the smoke test there. Record platform, Rocq, stdpp and Iris versions, and the exact command, in `VERSIONS.md`.

- [ ] **Step 4: Write `build.sh`, `check.sh` and the empty documents**

`build.sh` generates `Makefile.rocq` from `_CoqProject` and runs it in parallel. `check.sh` runs the build, compiles `Audit.v`, and fails on any assumption outside the allowlist, or on the search criteria above. `check.sh` skips conformance regeneration until Task 7 adds it. `Prelude.v` holds `nid := nat`, `oid := nat`, `val := Z`, and:

```rocq
Inductive fresh := Clean | Check | Dirty.
Definition stale (f : fresh) : bool := match f with Clean => false | _ => true end.
Definition fresh_le : fresh -> fresh -> Prop.   (* Clean ≤ Check ≤ Dirty *)
```

- [ ] **Step 5: Verify the audit catches violations**

Add `Axiom bad : False.` and a `Print Assumptions` of a theorem using it. Run `check.sh`; expected: nonzero. Remove them; expected: exit 0. Do the same with a stray `Admitted`.

- [ ] **Step 6: Commit** as `feat(proofs): scaffold the Rocq project`.

---

### Task 2: Command language

**Files:** Create `proofs/theories/Base/Syntax.v`.

**Interfaces:**
- Consumes: `nid`, `val` from `Prelude`.
- Produces, first-order so the heap tier can compile it to HeapLang:

```rocq
Definition var := nat.
Inductive expr := EConst (z : val) | EVar (x : var) | EPrev (* previous-value argument *)
  | EAdd (a b : expr) | ESub (a b : expr) | EMul (a b : expr) | EDiv (a b : expr)
  | ELt (a b : expr) | EEq (a b : expr).

Inductive outcome := Ok (v : val) | Fail (f : val).  (* f is the failure's identity *)

Inductive cmp := CmpEq | CmpNever | CmpThrows (f : val).  (* comparer behaviours *)
Inductive scope_mode := Pure | Owning.

Inductive cmd :=
  | CRet (e : expr) | CThrow (f : val)
  | CLet (x : var) (e : expr) (k : cmd)
  | CIf (e : expr) (t f : cmd)
  | CRead (x : var) (n : expr) (k : cmd)        (* Value: a failed read rethrows *)
  | CTryRead (x ok : var) (n : expr) (k : cmd)  (* TryValue: ok binds 1 or 0 *)
  | CReadU (x : var) (n : expr) (k : cmd)       (* inside untrack *)
  | CWrite (n e : expr) (k : cmd)
  | CSignal (x : var) (e : expr) (k : cmd)
  | CMemo (x : var) (m : scope_mode) (c : cmp) (b : cmd) (k : cmd)
  | CEffect (x : var) (b : cmd) (k : cmd)
  | CCleanup (b : cmd) (k : cmd)
  | CDispose (n : expr) (k : cmd)
  | CBatch (b : cmd) (k : cmd)
  | CLog (e : expr) (k : cmd).                  (* observable event *)

Definition env := list (var * val).
Definition eval_expr : env -> option val -> expr -> option val.
Inductive event := EvRun (n : nid) | EvLog (n : nid) (v : val) | EvCleanup (n : nid).
```

- [ ] **Step 1: Write `Example`s** for `eval_expr`: arithmetic, an unbound variable gives `None`, division by zero gives `None` (a failure of the enclosing body), `EPrev` with `None` gives `None`. Run the build; expected: fails, undefined.
- [ ] **Step 2: Define it** and add `well_behaved_memo : cmd -> bool`, true exactly when the body contains no `EPrev`, `CReadU`, `CWrite`, `CSignal`, `CMemo`, `CEffect`, `CCleanup`, `CDispose`. Add `Example`s for each rejected form.
- [ ] **Step 3: Run the audit.** Expected: exit 0.
- [ ] **Step 4: Commit** as `feat(proofs): add the command language`.

---

### Task 3: Reference semantics

**Files:** Create `proofs/theories/Reference/Semantics.v`, `Reference/Properties.v`.

**Interfaces:**
- Consumes: `Base/*` only. Importing anything from `Pure/` or `Heap/` fails this task.
- Produces:

```rocq
Record rprog := { rsignals : gmap nid val;
                  rmemos : gmap nid cmd;      (* each well_behaved_memo; acyclic *)
                  reffects : gmap nid cmd }.
Definition rhistory := list (list (nid * val)).  (* each inner list is one batch *)
Definition effect_order := list nid -> list nid.  (* a permutation of its argument *)
Definition rwf : rprog -> Prop.
Definition memo_value : rprog -> gmap nid val -> nid -> option outcome.
Definition rtrace : nat (*fuel*) -> effect_order -> rprog -> rhistory -> option (list event).
Definition redundant : rprog -> list event -> nid -> nat -> Prop.
```

An effect runs in a round exactly when a value it read in its last run now differs, and its cleanups run first. Effect writes form further rounds. First version's boundary (author's call, recorded in `CORRESPONDENCE.md`): a static node set. A program whose effect creates or disposes nodes is outside the reference fragment, and keeps only the general engine theorems. `redundant` holds for an effect's run when every value it read is equal to its previous run's. The definition may sharpen during the work; each change is noted in `BUGS.md`.

- [ ] **Step 1: Write theorems and `Example`s:** `rtrace_deterministic` (same inputs, same trace); `memo_value_extensional` (depends only on the signals in its discovered dependencies); `glitch_free_by_definition` (every value in a `EvLog` of an effect's run is computed from one signal snapshot); `Example`s for a diamond (one run per batch), a cutoff (a memo whose value is unchanged wakes no effect), and a batch 0→1→0 (no run).
- [ ] **Step 2: Define the semantics and prove them.** Run the audit. Expected: exit 0. Confirm with `rtk grep "Pure\|Heap" proofs/theories/Reference` that no import exists.
- [ ] **Step 3: Commit** as `feat(proofs): add the reference semantics`.

---

### Task 3a: Observations and the refinement relation

**Files:** Modify `proofs/theories/Base/Syntax.v`, `Reference/Semantics.v`, `Reference/Properties.v`. Later tasks' traces use the extended `event`.

**Interfaces:**
- Produces:

```rocq
(* added to event *)  | EvFail (n : nid) (f : val) | EvDispose (n : nid) | EvHostRead (n : nid) (o : outcome)
Inductive rinput := RWrite (n : nid) (v : val) | RDispose (n : nid) | RRead (n : nid).
Definition rhistory := list (list rinput).   (* replaces Task 3's *)
Record observation := { obs_ty : Type; obs_of : list event -> obs_ty }.
Definition obs_full : observation.      (* the whole trace, run counts included *)
Definition obs_settled : observation.   (* per effect: values of its last run, failures, disposal; all host reads *)
(* One order per history. Each batch prefix is compared, and its engine trace is a prefix of the whole trace,
   so an engine cannot look ahead or drop a batch's observed result. Under obs_settled it may drop a run whose
   observed result is unchanged. *)
Definition refines (O : observation)
    (engine : rprog -> rhistory -> option (list event)) : Prop :=
  forall p h t, rwf p -> engine p h = Some t ->
    exists ord, is_effect_order ord /\
      forall h', prefix h' h ->
        exists te fuel tr, engine p h' = Some te /\ prefix te t /\
          rtrace fuel ord p h' = Some tr /\ obs_of O te = obs_of O tr.
(* refines holds for an engine with no traces, so an engine proof also owes this, for a fuel-indexed engine. *)
Definition engine_answers
    (engine : nat -> rprog -> rhistory -> option (list event)) : Prop :=
  forall p h, rwf p ->
    (forall ord, is_effect_order ord -> exists fuel t, rtrace fuel ord p h = Some t) ->
    exists k t, engine k p h = Some t.
```

`RRead` is a host read, evaluated at its position in the batch, so a dropped write to a signal no effect logs is observable.

A failing effect emits `EvFail`. A disposed effect emits its cleanups and `EvDispose`, then never runs. A disposed memo answers reads with its last value, as Core.fs documents. `refines` imports nothing from `Pure/`, so any engine can use it.

- [ ] **Step 0: Check the literature.** Read the from-scratch consistency result in the Adapton papers, and compare it with the reference semantics, `obs_settled` and `refines`. Record each difference in `README.md`, and adopt a stronger or better-factored statement where one exists. Literature never justifies weakening a statement here.
- [ ] **Step 1: Theorems and `Example`s.** Task 3's theorems still hold on the extended types. `obs_settled_coarser`: `obs_full` equality implies `obs_settled` equality. `refines_full_implies_settled`. `Example`s: an effect that fails in one round emits `EvFail`, and later rounds still run; disposing an effect runs its cleanups once and ends its runs; two traces differing only by a redundant run have equal `obs_settled` and unequal `obs_full`.
- [ ] **Step 2: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 3: Commit** as `feat(proofs): add observations and the refinement relation`.

---

### Task 4: `ObserverSet` and `SourceList` (pure)

**Files:** Create `proofs/theories/Pure/ObserverSet.v`, `Pure/SourceList.v`. Model: Core.fs lines 195-428 and 540-814.

**Interfaces:**
- Produces:

```rocq
Definition scan_limit : nat.              (* EdgeHandle.ScanLimit's value from Core.fs *)
Record obs := { oitems : list (nid * nat); olatest : nat }.
Inductive tried := TAdded (h : nat) | TUnlinked | TUnknown.
Definition obs_add : nid -> nat -> obs -> obs * nat.
Definition obs_try_add : nid -> nat -> obs -> obs * tried.
(* None: the handle holds another observer. Some moved: (observer, slot, new handle) *)
Definition obs_remove_at : nid -> nat -> obs -> option (obs * option (nid * nat * nat)).
Definition obs_view : obs -> list nid.    (* in position order *)
Inductive walk_mut := WAdd (c : nid) (slot : nat) | WRemove (c : nid) (h : nat).
(* the backward re-clamped walk, with arbitrary mutations between visits *)
Inductive walk : nat -> obs -> list (list walk_mut) -> list nid -> obs -> Prop.

Record src := { sslots : list (nid * option nat); scursor : nat }.  (* None = Unlinked *)
Record EdgeWorld := { ew_obs : gmap nid obs; ew_src : gmap nid src }.
Definition sl_begin : nid -> EdgeWorld -> EdgeWorld.
Definition sl_add : nid (*self*) -> nid (*source*) -> EdgeWorld -> option EdgeWorld.
Definition sl_end : nid -> EdgeWorld -> option EdgeWorld.
Definition sl_clear : nid -> EdgeWorld -> option EdgeWorld.
Definition sl_moved_in : nid -> nat -> nat -> EdgeWorld -> EdgeWorld.
```

- [ ] **Step 1: Observer theorems.** `obs_add_position` (the handle is the position, and the view appends); `obs_remove_at_swap` (the last observer moves into the hole; the notice names it; remove at last gives no notice); `obs_remove_mismatch` (a wrong handle gives `None`); `obs_try_add_cases` (at or below `scan_limit`, `TUnknown` is impossible, and `TUnlinked` exactly when held); `walk_covers` (an observer present at every point of a `walk` is in the visited list); `walk_visits_only_seen` (each visited observer was present at its visit).
- [ ] **Step 2: Source-list theorems.** `sl_add_positional` (a match advances the cursor, changes no edge); `sl_add_repeat` (a read of the source just matched changes nothing); `sl_add_truncates` (a miss drops every slot from the cursor, then appends); `sl_no_duplicate_edge` (a source has at most one slot holding `Some`, and it is the source's first slot); `sl_trim_moves_own_edge` (Review Focus 4: for A, B, A with a trim that swap-removes an edge later in the list, the later handle is read after the move, and all edges are removed); `sl_add_unknown_no_duplicate` (Review Focus 5: an above-limit list with stale `latest` ends with exactly one edge for that source).
- [ ] **Step 3: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 4: Commit** as `feat(proofs): model ObserverSet and SourceList`.

---

### Task 5: Graph state and edge coherence

**Files:** Create `proofs/theories/Pure/State.v`, `Pure/Edges.v`.

**Interfaces:**
- Consumes: `EdgeWorld` and Task 4's operations.
- Produces:

```rocq
Inductive kind := KSignal | KMemo | KEffect.
Record ownerS := { o_children : list link; o_cleanups : list cmd; o_gen : nat; (* per Core.fs Owner *) }.
Record node := { n_kind : kind; n_value : val; n_failure : option val; n_status : status;
                 n_fresh : fresh; n_unann : bool; n_runs : nat; n_published : bool;
                 n_disposed : bool; n_queued : bool; n_in_cleanup : bool; n_scope : option oid;
                 n_violated : bool; n_body : cmd; n_env : env; n_mode : scope_mode; n_cmp : cmp }.
Record flags := { f_unannounced : bool; f_reader_mark : bool }.
Definition current_flags : flags := {| f_unannounced := true; f_reader_mark := true |}.
Record state := { g_flags : flags; g_edges : EdgeWorld; g_nodes : gmap nid node;
                  g_owners : gmap oid ownerS; g_queue : list nid; g_head : nat;
                  g_flushing : bool; g_owed : bool; g_batch : nat; g_pull : nat;
                  g_current : option nid; g_untracked : bool; g_owner : option oid;
                  g_next : nat; g_trace : list event;
                  g_running : list nid (* ghost: between a run's start and EndRun *) }.
Definition edge (s : state) (src c : nid) : Prop.
Definition coherent (w : EdgeWorld) : Prop.
```

`coherent w` holds when, for all `c i s h`, slot `i` of `c` holds `(s, Some h)` if and only if position `h` of `s`'s observers holds `(c, i)`. Fill `ownerS` and `status` from Core.fs lines 855-1250 and the `Status` type.

- [ ] **Step 1: Theorems.** `coherent_add`, `coherent_begin`, `coherent_end`, `coherent_clear`, `coherent_moved`, for a move paired with its swap-removal, since a bare `sl_moved_in` breaks coherence (each `sl_*` preserves `coherent` and never returns `None` from a coherent world), and `edge_iff_both_sides`.
- [ ] **Step 2: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 3: Commit** as `feat(proofs): prove edge coherence`.

---

### Task 6: Marking

**Files:** Create `proofs/theories/Pure/Marking.v`. Model: `Memo` 4483-4513, `Effect` 4742-4768, `Signal.NotifyCheck`, the three `Notify*` walks.

**Interfaces:**
- Consumes: `state`, `edge`, `obs_view`.
- Produces: `mark_dirty`, `mark_check : nat -> nid -> state -> option state`; `notify_dirty`, `notify_check : nat -> nid -> state -> option state`; `notify_dirty_except : nat -> nid -> option nid -> state -> option state`; `mark_measure : state -> nat`. Fuel gives the mutual recursion a structural argument. `f_unannounced = false` drops the `elif unannounced` branches, for Task 11's witness.

- [ ] **Step 1: Theorems.**
  - `mark_terminates`: `mark_measure s < fuel` implies `mark_check fuel n s` and `mark_dirty fuel n s` are `Some`, cycles included.
  - `mark_monotone`: `n_fresh` only rises, for every node.
  - `dirty_not_lost`: a `Dirty` node stays `Dirty` under `mark_check`.
  - `mark_preserves_edges`: `g_edges` is unchanged by marking.
  - `mark_forwards_on_clean`: a clean memo with observers `os` passes to `stale`, and every member of `os` ends stale.
  - `mark_forwards_on_unannounced`: the same for a stale memo with `n_unann = true`, which ends `n_unann = false`.
  - `effect_mark_queues`: a non-disposed, non-`in_cleanup` effect ends queued, at most once in `g_queue`.
  - `disposed_effect_ignores_marks`, `in_cleanup_ignores_marks`.
- [ ] **Step 2: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 3: Commit** as `feat(proofs): prove marking`.

---

### Task 7: The engine interpreter and F# conformance

This task proves no theorems. It builds the executable model and tests it against the F#, because every later theorem's claim about Ranvier rests on this match.

**Files:** Create `proofs/theories/Pure/Engine.v`, `proofs/conformance/Scenarios.fsx`, `theories/Conformance/Expected.v`, `theories/Conformance/Check.v`. Modify `proofs/check.sh`. Model: `Memo` 4183-4365, `Effect` 4779-4880, `Signal` 3795-3825, `Graph` 3114-3185, 3296-3342, 3385-3401, 3420-3500, `Owner` 855-1250.

**Interfaces:**
- Consumes: Tasks 2, 4, 5, 6.
- Produces: `node` gains `n_link : option link`, which `Dispose` needs (Core.fs 4081, 4385, 4648, 4896-4898). `o_cleanups`, `o_cleanup_envs` and `o_cleanup_order` have one writer and one reader helper. Owner ids come from `fresh (dom g_owners)` and a fixed root id, not `g_next`, so conformance's node ids stay aligned. `Effect.Run` adds the effect to `g_running` before its `Discharge` (Core.fs 4823), so `wake_ok`'s running disjunct covers the cleanup window. Then:

```rocq
Definition M A := state -> option (state * A).   (* None: out of fuel *)
Fixpoint exec (fuel : nat) (e : env) (c : cmd) : M outcome
with read (fuel : nat) (n : nid) (tracked : bool) : M outcome
with update_if_necessary (fuel : nat) (n : nid) : M unit
with recompute (fuel : nat) (n : nid) : M unit
with run_memo (fuel : nat) (n : nid) : M unit
with execute_effect (fuel : nat) (n : nid) : M unit
with discharge (fuel : nat) (o : oid) : M unit
with dispose_node (fuel : nat) (n : nid) : M unit
with run_flush (fuel : nat) : M unit.
Definition init (fl : flags) : state.
Definition run_top (fuel : nat) (c : cmd) : M outcome.
Definition trace_of (fuel : nat) (fl : flags) (c : cmd) : option (list event).
```

Theorems later quantify over `fuel`, so a `Some` result means "this operation returned".

- [ ] **Step 1: Write `Scenarios.fsx`.** It references the built `Ranvier` assembly, and runs each scenario below on real `Signal`/`Memo`/`Effect` objects at commit `d701e6c5`. Bodies append to a log with creation-index node labels. It writes `theories/Conformance/Expected.v` with one `Definition exp_S01 : list event := [...]` per scenario. The matching programs, `Definition prog_S01 : cmd := ...`, are written by hand in `Conformance/Check.v`.

| Scenario | Shape |
|---|---|
| S01 | Diamond: two memos over one signal, effect reads both |
| S02 | Cutoff: memo's value unchanged, its effect does not run |
| S03 | #302: node marked during its own run; a later mark reaches the effect. Take from the #302 regression test in `d701e6c5` |
| S04 | #290: a reader first links a node dirtied during its own run. Take from the test in `2e8f9690` |
| S05 | Conditional branch flip drops an edge; a write to the old branch wakes nothing |
| S06 | Reads A, B, A; re-run reads A only |
| S07 | Batch 0→1→0 with a direct-reading effect |
| S08 | Effect cleanup writes its own source: no re-queue from the cleanup |
| S09a | Review Focus 1: an effect body throws in a flush; its status holds the failure and the effects behind it run |
| S09b | Review Focus 1: an effect's cleanup throws during its re-run; record whether it escapes the flush, and what the next flush runs |
| S10 | Review Focus 3: a body disposes its own computation; later writes run nothing |
| S11 | Review Focus 2: an owning memo's cleanup writes a source and reads the memo |
| S12 | Owner disposal: reverse acquisition order; each cleanup once; late registration runs immediately |
| S13 | Effect writes a source that it reads, climbing to a fixpoint |
| S14 | Candidate from the spec: self-writing memo, read by an effect |
| S15 | Candidate: `e` reads `[h,x,y]`, `x` reads `[h,y]`, batch writes `h` and `y`'s source |

- [ ] **Step 2: Run it.** Run: `rtk dotnet build src/Ranvier -c Release`, then `rtk dotnet fsi proofs/conformance/Scenarios.fsx`. Expected: `Expected.v` holds the 16 definitions, plus one for each ledger scenario added before this task: `in_cleanup_observer_stays_clean` and an effect that disposes itself mid-body, then reads. `src/Ranvier` shows no diff. Separately, take `walk_covers_duplicate_observer` to the F#: `ObserverSet` is internal, so run it as a `Ranvier.Tests` case with a stub `IComputation` whose `MarkDirty` mutates the set, then settle its `BUGS.md` entry.
- [ ] **Step 3: Write `Conformance/Check.v`:** one `Example check_Snn : trace_of 100000 current_flags prog_Snn = Some exp_Snn. Proof. vm_compute. reflexivity. Qed.` per scenario. Run the build; expected: fails, `Engine` undefined.
- [ ] **Step 4: Implement `Engine.v`** line by line from Core.fs, including `finally` paths and `moved` with `CmpThrows`. `f_reader_mark = false` drops the `MarkReaderCheck` in `TrackCurrent`.
- [ ] **Step 5: Run the build.** Expected: all 16 pass. A mismatch is first a model error; fix `Engine.v`. Only after confirming from Core.fs that the model is faithful, and that the F# behaviour is what the scenario records, is the mismatch a correspondence question to report.
- [ ] **Step 6: Extend `check.sh`** to regenerate `Expected.v` and fail on a diff. Audit. Expected: exit 0.
- [ ] **Step 7: Ledger the candidates.** S07, S14 and S15 record behaviour now; enter each in `BUGS.md` as Suspected with its trace. The remaining reading-derived candidates go in as Suspected, without traces: `RecomputeScoped` setting `Clean` before `Discharge` without clearing `unannounced`; nested self-recompute resetting the `SourceList` cursor; a disposed memo keeping observers.
- [ ] **Step 8: Commit** as `feat(proofs): add the engine interpreter with F# conformance`.

---

### Task 7a: Extraction, differential testing and the CI gate

The implementation link: a Core.fs change that alters behaviour must fail a check. It's evidence, not proof, and every report says so.

**Files:**
- Create: `proofs/theories/Extract.v`, `proofs/differential/engine.ml` and `engine.mli` (generated, committed), `proofs/differential/driver.ml`, `proofs/differential/Generate.fsx`, `proofs/differential/run.sh`, `.github/workflows/proofs-differential.yml`
- Modify: `proofs/check.sh`, `proofs/README.md`, `proofs/VERSIONS.md`

**Interfaces:**
- Consumes: `trace_of`, `cmd`, `event`, `current_flags`.
- Produces: `bash proofs/differential/run.sh [count] [seed]`, exit 0 when the F# and the extracted interpreter agree on every generated program. Generated programs are written to a text form both sides read: one S-expression per program.

`Generate.fsx` holds three things: a seeded generator of `cmd` programs, a printer to the text form, and an interpreter of `cmd` over real `Signal`/`Memo`/`Effect` objects that writes the event trace. `driver.ml` parses the text form, runs extracted `trace_of` and writes the same trace format.

The generator must cover: conditional reads, cutoff comparers, diamonds, batches, writes from effects, cleanups, owning memos, disposal, failing bodies and self-marking memos. It bounds writes from effects so runs terminate: each write-capable effect writes only signals of a higher creation index, or under a guard that stops changing the value. It must also reach `CmpNever`, `CmpThrows`, `CTryRead` and late child attachment. It excludes pure-scope violations and repeated zero divisors, because the F# throws a fresh failure identity each time and the model has one; each exclusion is reported. A program that runs out of fuel on the OCaml side is counted and skipped, and the count is reported.

- [ ] **Step 1: Check the harness can detect a difference.** Write `run.sh`, generator and driver. Run against a deliberately altered copy of the extracted engine, with `n_unann` handling removed. Expected: nonzero, printing the first differing program with both traces. Restore the file.
- [ ] **Step 2: Run the real comparison.** Run: `rtk bash proofs/differential/run.sh 2000 1`. Expected: exit 0, reporting programs compared and skipped. Skipped must be under 5%, or the generator's bounds are too loose.
- [ ] **Step 3: Handle each difference.** Shrink by hand to a small program, add it as a named scenario in `Scenarios.fsx`, and take it through the resist procedure: a model error fixes `Engine.v`; behaviour the theorems would reject goes in the ledger.
- [ ] **Step 4: Extend `check.sh`.** Regenerate `engine.ml` from `Extract.v` and fail on a diff. Run `run.sh 200 1`.
- [ ] **Step 5: Add the CI job.** Triggered on changes to `src/Ranvier/Core.fs` and `proofs/**`. It installs the .NET SDK and OCaml, builds `src/Ranvier`, and runs `run.sh 2000 <run number>`, so each run tries new seeds, and prints the seed on failure. It uses the committed `engine.ml`, so CI needs no Rocq install. Validate the YAML locally with `rtk gh workflow view` where possible; the workflow's first real run happens when the branch is pushed, which this task doesn't do. Say so in the report.
- [ ] **Step 6: Commit** as `feat(proofs): check the extracted model against the F# on generated programs`.

---

### Task 8: Check resolution (layer 5)

**Files:** Create `proofs/theories/Pure/Check.v`.

**Interfaces:**
- Consumes: `Engine`, `coherent`, `ew_reachable`. `coherent` alone admits a computation that read a source with no edge to it, so resolution theorems take `ew_reachable (g_edges s)` and the bridge from slots to edges in `Edges.v`.
- Produces: `moved_since_last_run : state -> nid -> Prop`, which holds when a source of `n` has a value or status different from the one `n`'s last run saw. A ghost per-source version in `state` is acceptable if needed.

- [ ] **Step 1: Theorems.**
  - `resolve_clean_no_move`: if `update_if_necessary` takes `n` from `Check` to `Clean`, then `¬ moved_since_last_run s' n`.
  - `resolve_dirty_stops_walk`: once `n` is `Dirty`, the walk visits no further source.
  - `resolve_clears_unannounced`: resolving to `Clean` ends `n_unann = false`.
  - `signal_update_noop`.
  - `disposed_memo_no_refresh`: a disposed memo's value is unchanged by a read.
- [ ] **Step 2: Prove them, then audit.** Expected: exit 0. Use the failing-theorem procedure for `resolve_clean_no_move` when a source ends a run stale.
- [ ] **Step 3: Commit** as `feat(proofs): prove check resolution`.

---

### Task 9: Runs and no lost wake-ups (layer 6)

The centre of the project. Layers 9 and 10 rest on it.

**Files:** Create `proofs/theories/Pure/Runs.v`.

**Interfaces:**
- Consumes: Tasks 5-8.
- Produces:

```rocq
Definition stale_of (s : state) (n : node_id) : Prop.         (* n_freshness is Check or Dirty *)
Definition stale_or_queued (s : state) (n : node_id) : Prop.  (* stale_of, or n_queued *)
Definition n_unannounced_of (s : state) (n : node_id) : bool.
Definition observer_covered (s : state) (src c : node_id) : Prop :=
  stale_or_queued s c \/ In c (g_running s) \/ n_unannounced_of s src = true.
Definition wake_ok (s : state) : Prop :=
  forall src c, edge s src c -> stale_of s src -> observer_covered s src c.
Definition engine_inv (s : state) : Prop.   (* coherent, world_reachable (g_edges s), wake_ok, stack discipline, id bounds, owner cleanup lists of equal length, n_in_cleanup c -> In c (g_running s) *)
```

- [ ] **Step 1: Theorems.**
  - `engine_inv_preserved`: `engine_inv` is preserved by each of the mutual functions, for all fuel, by induction on fuel. **The headline no lost wake-ups result** is `no_lost_wakeups : engine_inv s -> run_top fuel c s = Some (s', r) -> wake_ok s'`.
  - `later_mark_forwarded`: from `wake_ok`, a later `mark_check` or `mark_dirty` of `src` leaves every observer of `src` stale, queued or running.
  - `marks_survive_run`: a mark received during a run leaves the node stale at run's end, with `n_unannounced = true`.
  - `cutoff`: a run whose value, status and failure are unchanged marks no observer.
  - `moved_marks_except_running`: a moved run marks every observer dirty except the current computation.
  - `run_end_edges_are_reads`: after a non-disposed run, the source list equals the reads of the run, with repeats per `sources_add`.
- [ ] **Step 2: Prove them.** The invariant is likely to need strengthening for mid-run states; strengthen it rather than weaken the conclusion. Candidate from reading: `RecomputeScoped`.
- [ ] **Step 3: Audit.** Expected: exit 0, with `no_lost_wakeups` closed or a ledgered counterexample and the theorem as premise above.
- [ ] **Step 4: Commit** as `feat(proofs): prove no lost wake-ups`.

---

### Task 10: Scheduler and ownership (layers 7, 8)

**Files:** Create `proofs/theories/Pure/Scheduler.v`, `Pure/Ownership.v`.

**Interfaces:**
- Consumes: `engine_inv`, `Engine`.

- [ ] **Step 1: Scheduler theorems.**
  - `queued_at_most_once`: an effect occurs at most once in the live part of `g_queue`, and `n_queued` holds exactly when it occurs or is mid-`Execute`.
  - `flush_no_nest`: `run_flush` entered with `g_flushing = true` changes nothing.
  - `owed_settled`: at top level with no batch, pull or current computation, `run_top` returning leaves `g_flush_owed = false` or an empty live queue.
  - `flush_returns_clean`: a flush that returns with no failing effect has an empty queue, and every non-disposed effect is clean or was re-marked and rerun.
  - `body_failure_contained`: a failing effect body records the failure in its status, and the flush continues.
  - `drain_no_escape`: from the command language, `execute_effect` has no failure path out of the drain: bodies, cleanups, child releases, memo runs and throwing comparers are caught (Core.fs 1171-1184, 4247-4261, 4275-4279, 4866). A flush that returns therefore leaves `g_flushing = false` and an empty live queue. This replaces `escape_leaves_resumable`, which has no form in the model; other `IScheduled` items, such as a pass (Core.fs 3082), stay outside it, and the comment at Core.fs 3140-3142 is stale for `Effect`.
- [ ] **Step 2: Ownership theorems.**
  - `dispose_reverse_order`: children and cleanups run in reverse acquisition order.
  - `cleanup_once`: each cleanup's `EvCleanup` occurs at most once per registration.
  - `disposed_no_edges`: after a disposed computation's run ends, it holds no source edges.
  - `disposed_effect_never_runs`.
  - `disposed_memo_answers`: a disposed memo's read returns its last value without running.
  - `late_registration_immediate`.
  - `stale_link_detach_noop`: the generation check makes a stale link's detach a no-op.
- [ ] **Step 3: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 4: Commit** as `feat(proofs): prove scheduler and ownership`.

---

### Task 11: Whole-graph theorems and regression witnesses (layer 9, pure)

**Files:** Create `proofs/theories/Pure/WholeGraph.v`. Extend `Pure/Witnesses.v`.

**Interfaces:**
- Consumes: Tasks 7-10, `well_behaved_memo`.
- Produces: `well_behaved_program : state -> Prop`, `self_dirtying : state -> node_id -> Prop`, `quiescent : state -> Prop`, `effect_ran_for_last_write : state -> Prop` (the scenario's effect has an `EvLog` of the value from the last write).

- [ ] **Step 1: Witnesses, first.** They validate the theorem statements, so they precede the proofs that depend on those statements.

```rocq
Theorem witness_302 :
  exists s_bad s_good,
    run_top F prog_S03 (init {| f_unannounced := false; f_reader_mark := true |}) = Some (s_bad, _) /\
    run_top F prog_S03 (init current_flags) = Some (s_good, _) /\
    ~ wake_ok s_bad /\ wake_ok s_good /\ effect_ran_for_last_write s_good /\ ~ effect_ran_for_last_write s_bad.
Theorem witness_290 : (* the same for prog_S04 with f_reader_mark := false, stating ~ wake_told s_bad /\ wake_told s_good: wake_ok holds on s_bad, since the source holds its flag (Task 9, reader_mark_off_fires) *).
```

The audit passing here shows `wake_ok` detects #302 and `wake_told` detects #290. `~ wake_ok` on `prog_S03` with `f_unannounced` false was checked at Task 9 with `vm_compute`: the edge from node 6 to node 7. If either witness fails, the definition is too weak: go back to Task 9, which is the anti-goal this guards.

- [ ] **Step 2: Theorems.**
  - `glitch_free_read`: for a node that is not `self_dirtying`, a read returns what its body computes from current signals.
  - `convergence`: under `well_behaved_program` and `quiescent`, every effect's last run saw the latest value of each source it reads. It takes a named premise excluding `effect_resolves_over_moved_source` (an effect that writes, which `well_behaved_memo` does not restrict), pending the owner's ruling.
  - `wake_ok` and `wake_told` both miss `self_marking_wake_lost`: `wake_ok` through the flag and `wake_told` through `gh_over`. That property stays here.
  - `well_behaved_program` keeps `gh_passed` empty, as a theorem, or a counterexample. The route through "a run over equal reads answers an equal value" does not cover `CmpNever`. `gh_passed` is per computation, and a per-pair form looks cheap if a statement needs it.
  - `well_behaved_program` or `self_dirtying` excludes memo cycles, self-reads included. `self_read_check_overflow` in `BUGS.md` falsifies `glitch_free_read` otherwise. `self_dirtying` also covers `nested_self_run`'s Clean memo and `self_marking_through_memo`'s Clean reader.
- [ ] **Step 3: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 4: Commit** as `feat(proofs): prove whole-graph behaviour with regression witnesses`.

---

### Task 12: Refinement (layer 10)

**Files:** Create `proofs/theories/Pure/Refinement.v`.

**Interfaces:**
- Consumes: `Reference/*`, Tasks 7-11.
- Produces: `erase : program -> history -> cmd` (the engine program for a reference program), `engine_order : state -> effect_order` (the exact order, computed from observer positions and marking walks, swap-removal included), `engine_of : flags -> nat -> program -> history -> option (list event)`, fuel-indexed, for `refines` and `engine_answers`.

- [ ] **Step 0: Check the literature notes.** Compare `convergence` and `refines observe_settled` with the Adapton notes from Task 3a in `README.md`. Record any difference there, and adopt a stronger or better-factored statement where one exists. Literature never justifies weakening a statement here.
- [ ] **Step 1: Theorems.**
  - `engine_order_exact`: the order of `EvRun`s in a flush equals `engine_order`, for all well-behaved programs.
  - `weak_refinement : forall k, refines observe_settled (engine_of current_flags k)`, using `convergence`.
  - `exact_refinement : forall k, refines observe_full (engine_of current_flags k)`, with `ord` instantiated by `engine_order`, or a checked counterexample in `Witnesses.v` for each divergence.
  - `engine_of_answers : engine_answers (engine_of current_flags)`.
- [ ] **Step 2: Attempt `exact_refinement` directly.** Expected divergences are S07 and S15, and for `weak_refinement` too, the disposed-memo cases `disposed_unread_memo_value` and `write_then_dispose_memo_current_value`, which `Core.fs` is expected not to pull at disposal. Run those two against the F# first, and take any that reproduces as a named premise until the owner rules. Each goes in as a checked `Example` showing the differing traces, a `redundant` run in the reference tier's terms, and ledger status Awaiting ruling. Then state the strongest theorem that holds as `exact_refinement_except`, with the divergence classes as an explicit premise; the unconditional statement stays in `BUGS.md`.
- [ ] **Step 3: Audit.** Expected: exit 0.
- [ ] **Step 4: Commit** as `feat(proofs): prove refinement to the reference semantics`.

---

### Task 13: Report the pure-tier results

**Files:** Modify `proofs/BUGS.md`, `proofs/README.md`.

- [ ] **Step 1: Run every ledger item in Suspected or Modelled through the procedure** until each is Confirmed, closed, or awaiting a ruling.
- [ ] **Step 2: Write the report**, with the theorem index, the ledger and differential results. Add a ranked list of candidate optimisations for the milestone, each with the observation it needs and whether its obligations fall in the pure or heap tier. Draw candidates from the ledger, from what the proofs show is unnecessary, and from Ranvier's own direction.
- [ ] **Step 3: Measure the top candidates' cost** with the repository's benchmarks, and choose one: it must have a measured cost worth removing, be nontrivial to prove, and be checkable by the differential test. The owner delegated this choice. Record the reasons in the report. A candidate that relies on a ruling still Awaiting ruling can't be chosen. An optimisation that changes `observe_full` needs `observe_settled` as its observation, and the report says so plainly, because that changes run counts.
- [ ] **Step 4: Commit** as `docs(proofs): report pure-tier results`.

---

### Task 13a: The worked optimisation (layer 11)

The optimisation Task 13 chose (`proofs/README.md`, `## Pure-tier report`): a signal write skips its notify walk while every observer of the signal is saturated, tracked by a graph epoch. It changes no event. The controller ruled on the design (D2): any walk may take a record, and the epoch advances at every write that can make a saturated node unsaturated.

**Files:**
- Create: `proofs/theories/Pure/Optimised.v`.
- Modify, on `claude/iris-rocq-ranvier-project-f3d6ae`: `Pure/State.v` (three state fields), `Pure/Engine.v` (advance points, record and skip, all gated on the mode), every `state` record literal outside `Engine.v`, `theories/Extract.v`, `proofs/differential/engine.ml` and `engine.mli` (regenerated), `driver.ml`, `run.sh`, `conformance/Scenarios.fsx`, `Conformance/Expected.v` (regenerated), `Conformance/Check.v`, `Audit.v`, `CORRESPONDENCE.md`, `README.md`.
- Modify, on `claude/notify-skip-epoch` only: `src/Ranvier/Core.fs`, `tests/Ranvier.Tests`.

The fix-phase rule holds: `src/Ranvier` has no diff on `claude/iris-rocq-ranvier-project-f3d6ae`. The F# change lands only on `claude/notify-skip-epoch`.

**Interfaces:**
- Consumes: `Engine`, `Marking`, `engine_inv`, `cleanup_once`, `weak_refinement_except`, `exact_refinement_except`, `engine_order_exact_except`, `engine_of_answers_on`, `witness_302`.
- Produces:

```rocq
(* State.v *)
Inductive skip_mode := SkipOff | SkipLeaveDirty | SkipAll.
(* state gains: g_skip : skip_mode; g_epoch : nat; g_stamps : gmap node_id nat.
   init sets SkipOff, 0 and ∅. *)

(* Optimised.v *)
Definition memo_saturated (s : state) (d : node) : Prop :=
  n_freshness d = Dirty /\ announce_due s d = false.
Definition effect_saturated (d : node) : Prop :=
  n_disposed d = true \/ n_in_cleanup d = true \/ (n_freshness d = Dirty /\ n_queued d = true).
Definition saturated (s : state) (n : node_id) : Prop :=
  match g_nodes s !! n with
  | Some d => match n_kind d with
              | KMemo => memo_saturated s d | KEffect => effect_saturated d | KSignal => True end
  | None => True
  end.
Definition stamp_current (s : state) (src : node_id) : Prop := g_stamps s !! src = Some (g_epoch s).
Definition stamp_ok (s : state) : Prop :=
  forall src c, stamp_current s src -> observer_of s src c -> saturated s c.
Definition strip (s : state) : state.        (* g_skip := SkipOff, g_epoch := 0, g_stamps := ∅ *)
Definition sim (s o : state) : Prop :=
  strip s = strip o /\ g_skip s = SkipOff /\ g_skip o = SkipAll /\ stamp_ok o.
Definition trace_of_mode (fuel : nat) (fl : flags) (m : skip_mode) (c : cmd) : option (list event).
Definition engine_of_optimised (k : nat) (p : program) (h : history) : option (list event) :=
  project p <$> trace_of_mode k current_flags SkipAll (erase p h).
```

`observer_of s src c` holds when `c` is an item of `observer_items (observers_of (g_edges s) src)`, the set `notify_with` walks. The model fields correspond to Core.fs as follows: `n_freshness` to `freshness`, `n_unannounced` to `unannounced`, `n_in_cleanup` to `inCleanup`, `n_queued` to `queued`, `n_disposed` to `disposed`. `announce_due s d` is `f_unannounced` and `n_unannounced d`. `memo_saturated` holds exactly when `mark_dirty` is the identity on a memo, matching `Memo.MarkDirty` (Core.fs 4483-4504). `effect_saturated` holds exactly when `effect_marked Dirty` is the identity, matching `Effect.MarkDirty` (4742-4755).

**The optimised transitions.** All four are gated on `g_skip`. Under `SkipOff`, the engine reads and writes none of the new fields: its states are the present engine's states with the three fields at their initial values, and its traces are those of `engine_of`. This keeps every proof of Tasks 1-12 about the present engine's states valid with no repair beyond record literals.
- *Advance:* `g_epoch` increases by one in the same step as each write that can make a saturated node unsaturated. The candidate points, as an obligation of Step 3 rather than a trusted list:

  | Core.fs | Write | Saturation lost |
  |---|---|---|
  | 4191 | memo `freshness <- Clean` (`RecomputeScoped`) | leaves `Dirty` |
  | 4207 | memo `freshness <- Clean` (`Run`) | leaves `Dirty` |
  | 4290 | memo `unannounced <- true` | announcement due |
  | 4802 / 4807 | effect `queued <- false`, `freshness <- Clean` (`Execute`, live path), one advance after 4807 | leaves `Dirty` and `queued` |
  | 4825 | effect `inCleanup <- false` | leaves cleanup |
  | `Effect` constructor | a new effect, `Clean` and unqueued | the model's id was absent, so counted saturated (added in Task 13a round 2) |

  4786, `queued <- false` on a disposed effect, keeps the effect saturated and gets no advance. Under `SkipLeaveDirty` only 4191, 4207 and 4802/4807 advance. That mode exists only for the Examples of Step 3.

  4191 leaves `unannounced` as it is. A memo that ends a run stale holds `unannounced = true`, so inside its next `RecomputeScoped` discharge it is `Clean` with an announcement, and `MarkDirty` of it sets `Dirty` and keeps the flag: the memo ends unsaturated after the walk. No advance fixes this, since the advance at 4191 precedes the walk. The record checks instead (round 2 ruling, fix 1).
- *Record:* a signal write's `notify_dirty` walk sets `g_stamps !! src := g_epoch` when it returns, and only when the walk left every observer saturated (`all_saturated`; `walk_record_sound`). A memo marked from `Clean` with an announcement is not, so a walk over the 4191 case records nothing. The walk is `notify_dirty` from `CWrite`, the setter (Core.fs 3817-3818). `WriteExcept` (3831) has no model command; in F# it never records. The unconditional record is `SkipUnchecked`, kept for its counterexample (`unchecked_record_unsound`, `unchecked_record_breaks_sim`).
- *Skip:* a `CWrite` of a new value to a signal with `stamp_current` performs no walk, and keeps the value write, the trace events and `RequestFlush`. The skip uses fuel exactly as `notify_dirty` does, returning `None` wherever the walk would, so trace equality holds at every fuel.
- *Clear:* every observer addition to a signal, through `sources_add` and through both the `Add` and the `TryAdd` paths (Core.fs 3775-3780), deletes the signal's stamp. Memos keep no stamp. If a later change gives memos stamps, it must also clear them at 4464 and 4467.

**The simulation.** `sim s o` relates the present engine's state `s` to the optimised engine's state `o`. The two are equal apart from `g_skip`, `g_epoch` and `g_stamps`, and `o` meets `stamp_ok`. The epoch is free under `sim`: only `o`'s epoch matters, through `stamp_ok`. Equal `strip` includes equal `g_queue`, `g_queue_head`, `g_trace` and node records, so the queue's content and order agree at every step. Effect order is proved here as part of trace equality. It does not come from `history_order`, which holds by construction and fixes no order.

- [x] **Step 1: Write this task's detailed plan** into this file: optimised transitions, observation, simulation relation, benchmark, and the heap obligations it implies.
- [ ] **Step 2: Model the optimised engine** as `engine_of_optimised`, by flag or as its own transitions, sharing `Engine.v` wherever behaviour is unchanged.
  - [ ] **2a.** Add the names of Steps 2-3 to `Audit.v`, then run `rtk bash proofs/check.sh`. Expected: fails, names undefined.
  - [ ] **2b.** Add `skip_mode` and the three fields to `state`, with `with_skip`, `with_epoch` and `with_stamps` helpers. The mode lives in `state`, not in `flags`, so the `flags` literals in `Witnesses.v` and `Marking.v` stay as they are. Update each `state` literal (`rtk grep "g_registered :=" proofs/theories`). Run `rtk bash proofs/build.sh`. Expected: exit 0 with every existing proof unchanged. A proof that breaks on the new fields is repaired in form only (record literals, `with_*` unfolding). A repair that changes a proof's argument, rather than its form, goes to the controller first: it means `SkipOff` is not the identity on the new fields.
  - [ ] **2c.** In `Engine.v`, add `advance (p : advance_point)` at each point in the table, with `advance_point` naming the Core.fs line. Add the record after the `CWrite` walk, the skip before it, and the clear in the observer-add path. Under `SkipOff` each new branch is the identity.
  - [ ] **2d.** Define `trace_of_mode` and `observed_mode` (`Extract.v`'s `observed` with a mode). Prove `trace_of_mode_off : trace_of_mode k fl SkipOff c = trace_of k fl c` by unfolding. Prove `engine_of_optimised` well-defined against `engine_of` by the same route.
  - [ ] **2e.** Run the build and `check.sh`. Expected: exit 0, and `Conformance/Check.v`'s 16+ examples unchanged, since they run `SkipOff`.
- [ ] **Step 3: Prove `optimised_refines : forall k, refines <chosen observation> (engine_of_optimised k)`, `engine_answers engine_of_optimised`, and `optimised_cleanups`: each registered cleanup runs exactly once, before its replacement or disposal.** `observe_settled` alone admits an engine that drops a logging cleanup. A counterexample here is the gate working: record it, and revise or reject the optimisation. As delivered, `optimised_cleanups` proves equal `EvCleanup` events and at most one per registration; "before its replacement" is false on the present engine for a registration made during teardown (3g), and its corrected form is an Unproved obligation in `BUGS.md`.
  - [ ] **3a. Saturation lemmas, in `Optimised.v`.**
    - `mark_dirty_saturated_id : saturated s c -> mark_dirty (S f) c s = Some s`.
    - `mark_dirty_saturates_or_window : mark_dirty (S f) c s = Some s' -> saturated s' c \/` `c` was a `Clean` memo with an announcement due. The unconditional `mark_dirty_saturates` is false (`mark_dirty_saturates_falsified`), and the record checks `all_saturated` (`walk_record_sound`).
    - `marks_keep_saturated : any_mark f n r s s' -> saturated s c -> saturated s' c`, for every `c`.
    - `notify_dirty_stamped_id : stamp_ok s -> stamp_current s src -> notify_dirty (S f) src s = Some s`.
  - [ ] **3b. One lemma per engine transition: it keeps saturation of every node, or advances the epoch.** List every state-changing helper of `Engine.v` and `Marking.v` (`set_node` sites by the fields they write, `enqueue`, `with_edges`, `push_running`, `pop_running`, owner updates, observer additions and removals). For each, prove `keeps_or_advances_<helper> : saturated s c -> saturated s' c \/ g_epoch s < g_epoch s'`. An observer addition instead proves that it clears the stamp. Record the table of helpers to lemmas in `CORRESPONDENCE.md`. A helper with neither proof is a missing advance point: add it to the table above and to the F# list in Step 4.
  - [ ] **3c. The invariant.** `stamp_ok_preserved`: under `SkipAll`, `stamp_ok` is preserved by each mutual function of `Engine.v`, for all fuel, by induction on fuel. It follows from 3a and 3b. A walk's record is sound by `mark_dirty_saturates` and `marks_keep_saturated`, because a `notify_dirty` walk over the model's observers runs no body.
  - [ ] **3d. The decision-1 variant fails.** Add these to `Pure/Optimised.v` as checked `Example`s, by `vm_compute`, under `SkipLeaveDirty`:
    - `leave_dirty_cleanup_skip`: effect `E` reads `s`, and its cleanup writes `s`. The host writes `s` twice, with a flush after each. The trace under `SkipLeaveDirty` has no second run of `E`. `SkipOff` and `SkipAll` give the trace with that run, and the two are equal.
    - `leave_dirty_unannounced_skip`: memo `M` reads `s`, and while `s < 1` its body writes `s + 1`. Effect `E` reads `M`. After the first flush, the host writes `s` to a new value. Under `SkipLeaveDirty`, `M`'s body walk records while `M` is still saturated, and 4290 then makes `M` unsaturated with no advance. The host's write is skipped, which drops the `NotifyCheck` that `M`'s announcement owes `E`, so the trace differs from `SkipOff`'s. `SkipOff` and `SkipAll` agree. If `vm_compute` shows no difference, lengthen the host's writes until the lost announcement reaches an event. A program that never shows one goes back to the controller, because the 4290 advance would then be unjustified by a witness.

    Each Example also shows `~ stamp_ok` at the state just before the skipped walk. These show that the advances at 4825 and 4290 are needed.
  - [ ] **3e. The simulation.** `optimised_simulates`: for each mutual function and all fuel, from `sim s o` the two runs both return `None`, or return equal results with `sim` on the end states. Proved by induction on fuel. At a `CWrite` with `stamp_current o src`, the present engine walks and `notify_dirty_stamped_id` makes the walk the identity. Everywhere else the two take the same branch, and `stamp_ok` comes from 3c.
  - [ ] **3f. Trace equality and its consequences.**
    - `optimised_trace_eq : forall k fl c, trace_of_mode k fl SkipAll c = trace_of k fl c`. It holds for all `fl`, so `witness_302_optimised` restates `witness_302` for the optimised engine with `f_unannounced` off.
    - `engine_of_optimised_eq : engine_of_optimised k p h = engine_of current_flags k p h`.
    - `optimised_refines k : refines_on refinable observe_settled (engine_of_optimised k)`, from `weak_refinement_except`. `refines_on refinable` is the form the present engine meets. It is a change of form from the fixed shape's `refines`, not of strength.
    - `optimised_exact_except`: `exact_refinement_except` with `engine_of_optimised`, under the same `runs_agree` premise.
    - `optimised_order_exact_except`: `engine_order_exact_except` for the optimised engine.
    - `engine_of_optimised_answers_on`: `engine_of_answers_on` for the optimised engine.
  - [ ] **3g. `optimised_cleanups`.** The `EvCleanup` events of the optimised trace equal the present engine's, by `optimised_trace_eq`. On that trace, `cleanup_once` gives at most one `EvCleanup` per registration. Prove the rest on the shared trace as `cleanups_before_replacement`: each registration whose host re-runs or is disposed has its `EvCleanup` before the host's next `EvRun` or its `EvDispose`. If that is false on the present engine, it goes through the resist procedure as an Open theorem of the present engine. It is not a counterexample to the optimisation. Outcome: false for a cleanup registered during its host's teardown (`cleanup_registered_in_teardown_runs_late`, scenario S18, Core.fs 1130-1132), spec corrected; the form restricted to registrations outside teardown is an Unproved obligation.
  - [ ] **3h. Audit.** Run `rtk bash proofs/check.sh`. Expected: exit 0, and `optimised_trace_eq`, `optimised_refines` and `optimised_cleanups` print only allowlisted assumptions.
  - [ ] **3i. Extraction and differential.**
    - Extract `observed_mode` and `skip_mode` beside `observed` in `Extract.v`, and regenerate `engine.ml`.
    - `driver.ml` takes a mode argument, `off` by default. `run.sh` passes `RANVIER_SKIP_MODE` through, and CI keeps `off`.
    - Both sides compare the trace through the closing reads and flush, and the two end-state views (`view_of`). The epoch and stamps are not in the view, so equal views still mean equal compared state.
    - On this branch, run `rtk bash proofs/differential/run.sh 2000 9` once with mode `off` and once with `all`, against the present F#. Expected: exit 0 both times, with the same agree counts.
    - Add the two Examples' programs to `Scenarios.fsx` as `S16_cleanup_rewrite` and `S17_self_write_announce`, with `check_` Examples in `Conformance/Check.v` at `SkipOff`. Their `Expected.v` traces are the present F#'s, and the optimised F# must reproduce them in Step 4.
  - [ ] **3j.** Update `CORRESPONDENCE.md` with the new fields, the advance table and the record and skip, and update `README.md`'s theorem index. Commit as `feat(proofs): prove the saturated-write skip refines the engine`.
- [ ] **Step 4: Only then implement it in F#.** Test-first, against the differential check with the extracted optimised engine.
  - [ ] **4a.** `rtk git worktree add .claude/worktrees/notify-skip-epoch -b claude/notify-skip-epoch claude/iris-rocq-ranvier-project-f3d6ae`. All of Step 4 runs there, and the proofs worktree is left alone while its gates run.
  - [ ] **4b. Enumerate the writes.** List every write to `freshness`, `unannounced`, `inCleanup`, `queued` and `disposed` in `Core.fs`, in every `IComputation` implementor (`rtk grep -n "freshness <-\|unannounced <-\|inCleanup <-\|queued <-\|disposed <-" src/Ranvier/Core.fs`; at `d701e6c5`, this includes 5162-5197, 5451-5504, 5568-5937 and 6195-6556). Map each to one of: an advance; "keeps saturation", with the reason; or "implementor does not join the epoch". Put the table in the PR description. A write in a joining implementor with no advance and no reason blocks the step.
  - [ ] **4c. Tests first, in `tests/Ranvier.Tests`, failing on the branch's present F#.**
    - A write to a signal whose 64 memo observers are `Dirty` and unannounced leaves every memo's `MarkDirty` uncalled. Use an internal counter, or `Tracer` under a test-only build.
    - `BatchOfTenWrites`' shape: the nine later writes perform no walk.
    - Regression cases for S16 and S17, `S08`, `S13`, `S14` and #302's test from `d701e6c5`. These pass before the change and must keep passing.
    - A signal observed by a `Binding`, or another non-joining `IComputation`, still walks on every write.
  - [ ] **4d. Implement.**
    - A graph field `epoch : int64`, and an internal `Advance ()` called at each point of 4b.
    - On `Signal`, a field `stamp : int64` initialised to `-1L`, set at the end of `NotifyDirty` from the setter (3817-3818) when every visited observer joins the epoch and every mark left its node saturated, and cleared at 3775-3780. `MarkDirty` returns whether it left the node saturated: `false` for a memo marked from `Clean` with `unannounced` set (the 4191 case). `NotifyDirty` records only if all did. The model checks saturation of each observer at the walk's end instead; since marks keep saturation, a node each mark left saturated is saturated at the end, so the F# records only where the model does.
    - An advance at the `Effect` constructor, or a reason that a new effect is never an observer of a stamped signal.
    - The skip test before 3817.
    - `WriteExcept` (3831) may skip and never records.
    - Joining is a member of the internal `IComputation`, true on core `Memo` and `Effect` only. Before adding it, run `fcs_refactor_impact` on `IComputation`.
    - The skip keeps `Tracer.Write`, `Tracer.Notified` and `RequestFlush`.
  - [ ] **4e. Gates.**
    - `rtk dotnet build Ranvier.slnx -c Release` and the full `Ranvier.Tests` suite, unfiltered.
    - `rtk bash proofs/check.sh`, which regenerates `Expected.v` from the optimised F# and must find no diff.
    - `RANVIER_SKIP_MODE=all rtk bash proofs/differential/run.sh 2000 10`. Expected: exit 0, skipped under 5%.
    - `dotnet fsi build.fsx -- analyze --group library --build`.
  - [ ] **4f.** Commit on `claude/notify-skip-epoch` as `perf(core): skip a signal's notify walk while its observers are saturated`. Do not push. The PR is opened only when the controller confirms, and the controller asks the user before pushing.
- [ ] **Step 5: Benchmark.** Report measured results beside the proof. A proof says nothing about speed, and an optimisation that doesn't help is dropped, however well proved.
  - [ ] **5a.** Build Release on both branches. Confirm optimisation from the assembly, not from BenchmarkDotNet's runtime line, which can show `DEBUG` for a Release F# build. Read `DebuggableAttribute` on `bench/Ranvier.Benchmarks/bin/Release/*/Ranvier.dll` with a scratchpad `.fsx` that loads it by `Assembly.LoadFrom`. Expected: `IsJITOptimizerDisabled = false`. Record the output.
  - [ ] **5b.** Run on each branch, at the same seed set and on the same machine, with `--inProcess` while the nested-worktree project clash persists: `rtk dotnet run --project bench/Ranvier.Benchmarks -c Release --no-build -- --inProcess --filter "*SignalBenchmarks.Write" "*SignalBenchmarks.WriteAndPropagate" "*EffectBenchmarks.*" "*BindingUpdateBenchmarks.WriteAndFlush"`. Restore any tracked file under `docs/.ai/benchmarks/results` that the run overwrites.
  - [ ] **5c.** Report gains and regressions side by side, before and after, with error columns. Expected gains are on `Write` with dirty unread memos and on `BatchOfTenWrites`. Expected regressions are on `WriteAndFlush`, `WriteAndPropagate`, and the `signal-binding` case of `BindingUpdateBenchmarks.WriteAndFlush`, where the skip never fires and the advance, stamp test and joining test only add cost. Signals observed by bindings, the shape Ranvier's UI produces most, gain nothing. Drop the optimisation if any regression on those shapes exceeds its benchmark's error bars, or if `BatchOfTenWrites` gains less than its error bars.
- [x] **Step 6: Report to the owner**, and ask them to review the heap scope in Tasks 14-16 in light of it.
  - [ ] **6a.** The report gives the theorems, the differential counts, the benchmark table and these heap obligations for Tasks 14-16:
    - `is_graph` carries `g_epoch`, each signal's stamp, and `stamp_ok`.
    - Each advance point is one step together with its field write: no step between them can run a walk.
    - The observer-set add specs (`Heap/ObserverSet.v`) clear the owning signal's stamp on `Add` and `TryAdd`.
    - The skip's spec refines `notify_dirty`'s identity under `stamp_current`.
    - The record's spec needs `notify_walk_unmutated` over joining observers.
    - The epoch is an unbounded `Z` in HeapLang. The F# `int64` assumes no wrap, and a Fable build's `int64` cost needs measuring.
    - Under `RANVIER_TRACE` (Debug builds), a skipped walk emits no `Tracer.Mark` events (Core.fs 380). This observation lies outside the model, so trace-log tests may change.
    - UI `Binding`s and other non-joining `IComputation` implementors record nothing and gain nothing.
  - [x] **6b.** Ask the owner whether Tasks 14-16 take these obligations on, or keep the heap tier at the present engine.

**Outcome.** Steps 2-6 done. Proofs on `claude/iris-rocq-ranvier-project-f3d6ae` (`optimised_trace_eq`); F# on `claude/notify-skip-epoch` (`0e5fa0b3`, `8d37379d`, unpushed); benchmark verdict keep (`BatchOfTenWrites` 0.36-0.40, `Write` at 64 saturated observers 0.03; no consistent regression, resolution about 5%). **Owner ruling (2026-11-10): keep the heap tier at the present engine.** Tasks 14-16 prove `Engine.v`'s engine and take on none of the obligations in 6a. If `claude/notify-skip-epoch` merges, its heap correspondence rests on `optimised_trace_eq` and the differential, not on Tasks 14-16.

---

### Task 14: Heap foundations, `ObserverSet`, `SourceList`

**Files:** Create `proofs/theories/Heap/Ghost.v`, `Heap/ObserverSet.v`, `Heap/SourceList.v`.

**Interfaces:**
- Consumes: pure Task 4 types and `state`.
- Produces:

```rocq
Class graphG Σ.    (* authoritative edge map; monotone freshness; pure state agreement *)
Definition is_graph `{!heapGS Σ, !graphG Σ} (γ : gname) (g : val) (s : state) : iProp Σ.
Definition is_observers (l : loc) (o : observer_set) : iProp Σ.
Definition is_source_list (l : loc) (sl : source_list) (held : gset node_id) : iProp Σ.
Definition observers_add_code observers_try_add_code observers_remove_at_code : val.
Definition sources_add_code sources_end_run_code sources_clear_code : val.
```

Specs relate code to the Task 4 functions, e.g.:

```rocq
Lemma observers_remove_at_spec l o c h o' m :
  observers_remove_at c h o = Some (o', m) ->
  {{{ is_observers l o }}} observers_remove_at_code #l #c #h {{{ RET (moved_val m); is_observers l o' }}}.
```

Arrays with grow and blanking per Core.fs; `first`/`rest` is kept in `SourceList`, `latest` and `scan_limit` in `ObserverSet`, `held` abstracted to the `gset` the spec names.

- [ ] **Step 1: Specs.** Add `Ghost.v`, then specs for each code function. Add the walk: `walk_spec`, which for a visitor closure with a mutating spec proves `walk_covers`'s conclusion, in HeapLang, with its `held_once` premise.
- [ ] **Step 2: Prove them, then audit.** Expected: exit 0.
- [ ] **Step 3: Commit** as `feat(proofs): prove the heap edge structures`.

---

### Task 15: Heap marking, runs, scheduler, ownership

> **Deferred (owner ruling, 2026-11-10: "Merge now, fix bugs").** The branch merges after Task 14 and the
> engine-independent ghost obligations (`ec1fee93`). Tasks 15-16 resume against the engine as fixed by the bug
> rulings, which will be tracked as GitHub issues labelled `proofs`; their obligations are in `proofs/theories/Heap/Ghost.v`'s header.
> Task 17 ran in short form: the Status section of `proofs/README.md`.

Split into one commit per layer. A stall here triggers the spec's cut order: heap 8, then 7, then 6.

**Files:** Create `proofs/theories/Heap/Marking.v`, `Heap/Run.v`, `Heap/Scheduler.v`, `Heap/Ownership.v`.

**Interfaces:**
- Consumes: Task 14 and pure `Engine`.
- Produces: `mark_dirty_code`, `mark_check_code`, `read_code`, `run_memo_code`, `execute_effect_code`, `run_flush_code`, `dispose_node_code`, `discharge_code`, `compile_cmd : cmd -> val`, `run_top_code : val -> expr`, `init_heap : state` (HeapLang's state), `outcome_val : outcome -> val`. Spec shape for all, proved by Löb induction through node dispatch:

```rocq
Lemma run_memo_spec γ g n s fuel s' :
  run_memo fuel n s = Some (s', tt) ->
  {{{ is_graph γ g s }}} run_memo_code g #n {{{ RET #(); is_graph γ g s' }}}.
```

- [ ] **Step 1: Marking.** Prove specs, `Heap/Marking.v`. Audit. Commit as `feat(proofs): prove heap marking`.
- [ ] **Step 2: Runs (layers 5, 6).** `compile_cmd` is correct against `exec`. Prove `run_memo_spec`, `execute_effect` and `read` specs, with re-entrant reads and writes, on `Heap/Run.v`. Audit. Commit as `feat(proofs): prove heap runs`.
- [ ] **Step 3: Scheduler.** Audit. Commit as `feat(proofs): prove the heap scheduler`.
- [ ] **Step 4: Ownership.** Doubly linked owner lists, generations as unbounded integers. Audit. Commit as `feat(proofs): prove heap ownership`.

---

### Task 16: Adequacy

**Files:** Create `proofs/theories/Heap/Adequacy.v`. Modify `Audit.v`.

**Interfaces:**
- Consumes: Task 15, `no_lost_wakeups`.

- [ ] **Step 1: Theorem.**

```rocq
Theorem heap_refines_pure Σ (c : cmd) fuel s' r σ' v :
  run_top fuel c (init current_flags) = Some (s', r) ->
  rtc erased_step ([run_top_code (compile_cmd c)], init_heap) ([of_val v], σ') ->
  v = outcome_val r.
```

A closed-program statement with no Iris in it, via `heap_adequacy`, for command-language bodies only. Add a corollary `heap_no_lost_wakeups`, tying the final heap's state to `wake_ok`.

- [ ] **Step 2: Prove it, then audit.** Expected: exit 0.
- [ ] **Step 3: Commit** as `feat(proofs): prove heap adequacy`.

---

### Task 17: Done check and handover

**Files:** Modify `proofs/README.md`, `proofs/CORRESPONDENCE.md`, `proofs/BUGS.md`.

- [ ] **Step 1: Clean build.** `git clean -xfd proofs` (after checking the listing with `-n`), then `rtk bash proofs/check.sh`. Expected: exit 0.
- [ ] **Step 2: Walk the spec's Done list**, pointing at evidence for each item: audit output, `witness_302`, `witness_290`, `exact_refinement` or its counterexamples, a correspondence row per modelled symbol, the differential check's output, `optimised_refines` and its benchmark, and `rtk git diff d701e6c5 --stat -- src/Ranvier` empty on this branch.
- [ ] **Step 3: Report to the owner:** theorem index, ledger by stage, each item Awaiting ruling. The fix phase gets its own plan from the Confirmed list and rulings, test-first in `src/Ranvier`, in the normal PR flow, with the model updated and each Open theorem closed.
- [ ] **Step 4: Commit** as `docs(proofs): complete the proof project report`.
