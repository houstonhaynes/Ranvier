# Iris/Rocq proofs of Ranvier's synchronous core

## Intent

The goal is to machine-check the semantics of Ranvier's reactive core, so that every optimisation has a formal contract to preserve. Never losing a wake-up is one of those semantics, and the theorem the whole-graph results rest on.

Ranvier's core propagates marks only on state transitions. That is fast, and it depends on a global
fact nobody can see in one place: everything that may need waking has already been told. #290 and #302
were both breaks in that fact. In #302, a node marked during its own run ended stale, a reader resolved
its `Check` to `Clean` while the node ran, and every later mark on the node was swallowed.

This project replaces "we found another one with a test" with machine-checked proofs, built up
from the primitive data structures to whole-graph behaviour. The owner's words for the class of bug to catch: bugs
"like a node marked during its own run swallows its next wake up".

### The longer purpose

The owner's stated future purpose is to simplify and enable advanced proofs for later Ranvier
directions, in particular the research into deterministic, lock-free parallel reactivity: epoch-versioned
propagation, atomic readiness counters, versioned adjacency snapshots and deterministic publication.
That research keeps the sequential engine as the semantic reference, and names four proof
obligations: observational equivalence to serial execution, glitch freedom at publication, safe
concurrent dependency and lifetime modification, and per-subsystem progress guarantees.

This project proves nothing about a parallel engine. It builds what those proofs would stand on:

| Future obligation | What this project leaves in place |
|-------------------|-----------------------------------|
| Equivalence to serial execution | The reference semantics, and the sequential engine's proof against it. A parallel engine proves against the same statement |
| Glitch freedom at publication | Stated on the reference semantics, independent of any scheduler |
| Safe concurrent modification | Abstract specifications for the edge structures, which versioned snapshots could implement; monotone ghost state |
| Progress of non-blocking subsystems | Nothing beyond HeapLang's atomics being available. Out of scope |

### Verification as the gate on optimisation

The owner's direction, decided after the plan was first written: verification is the mechanism that makes
aggressive optimisation safe, so it is not a separate activity from optimising. An optimised engine `O` is a candidate only once it
refines the reference semantics under a chosen observation:

    for all programs and histories in the fragment: Obs(O) = Obs(reference)

Performance is then minimised subject to that constraint. Benchmarks supply the performance evidence and refinement the
correctness evidence, and neither stands in for the other. A failed proof obligation alone does not show a defect;
a checked counterexample does. This project therefore has to demonstrate the whole loop on one real optimisation,
with its link to the executable F#, and proving the current engine alone does not complete it.

## Goals

1. A Rocq project in the repository whose layers each build only on the layers below.
2. A central theorem, **no lost wake-ups**: every edge from a stale node leads to an observer that is
   stale, is running, or will be told by the node's next mark. Precisely, a stale node with a clean
   observer holds `unannounced`, so any later mark is forwarded. The theorem does not claim a later
   mark will arrive.
3. Whole-graph theorems on top of it: glitch-free reads, flush behaviour and ownership.
4. Two regression witnesses: machine-checked traces showing the model without `unannounced` (#302)
   and without the stale-read reader mark (#290) loses a wake-up, while the current model does not.
   They show that the theorem statements can detect this class of bug.
5. A **bug ledger** collecting every defect found. Fixes follow once the proof project is complete.
6. A **reference semantics**: what a reactive graph means for a fixed ordered input history, with no
   mention of freshness, marks, queues or threads. The sequential engine is proved to refine it.
7. **A single refinement relation**, `refines Obs engine`, over a chosen observation, so the next engine proves
   the same statement rather than a new one.
8. **An implementation link that can fail.** The interpreter is extracted and run against the F# on generated
   programs, in CI, so a Core.fs change that alters behaviour fails a check. This is strong testing, not proof.
9. **One worked optimisation**: a real optimisation of Ranvier, chosen by the builder (the owner delegated the choice), proved to refine the reference
   semantics and connected to its F# implementation by the differential check, with benchmark results reported beside it.

## Non-goals (first version)

- Async memos, async sources, streams and boundaries. They are a later layer; #303 changed them the
  same way as `Memo`, so the `Memo` proof should set the pattern. The status channel's `Pending` is out
  with them.
- Projections, lookups, passes (`SchedulePass`, `RunPasses`), journals, tracing, counters, `Timed`.
- Thread affinity and `Serialised` entry. The model is single-threaded, as the graph's guarded modes
  require of callers.
- The Fable compilation target and its representation differences.
- Proved performance or work bounds, and machine-checked links between the model and the F# or its IL.
- Modelling or proving a parallel engine, epochs, atomics, weak memory or progress. Later work may use
  this project; it does not build any of that speculatively.
- A verified-transformation framework or a formal intermediate representation. They wait for the worked
  optimisation, whose proof should shape them.
- Changing `src/Ranvier` during the proof phases, apart from the worked optimisation, which lands on its own branch.

## Anti-goals

Any of these makes the project a failure even if everything compiles:

- Proving the model correct by making it unlike Core.fs. Each simplification is listed in the
  correspondence table, with its reason.
- `Admitted`, `admit` or added `Axiom`s in a layer reported as complete.
- Theorems so weak they would have passed with #302 present. Goal 4 guards against this.
- Reporting a stuck proof as a bug. A ledger entry requires evidence (see Bug ledger).

## What the proofs do and do not establish

Rocq cannot read F#. The proofs cover a hand-written model of the algorithm, so they establish the
model's correctness, and Core.fs's only as far as the model matches it. Every theorem's claim about
Ranvier passes through the correspondence table, and the differential check is the evidence for that table: agreement on
generated programs, which is not a proof of equivalence.

**Decided:** the link stays a proved model plus hand-written F#, strengthened by the differential check. The owner expects better tooling at Clef, and the link is revisited then. Generated code, a verified translation of the F# and verified transformations are not part of this project.

## Architecture: three tiers

**Reference tier.** The specification any engine must meet, sequential or parallel. For a well-behaved
program, meaning deterministic memo bodies that do not write signals, it defines the
observable outcome of an ordered input history. A memo's value is its body evaluated over current
signal values, with dependencies discovered by that evaluation. Effect runs, logged values, cleanups, failures and
disposal are the observable events. The tier takes the order of effects within a flush as a parameter, so it
mentions no observer position. Today's exact order is a separate result below.

An **observation** selects what must be preserved from a trace. The tier defines at least `observe_full`, the whole trace
including run counts, and `observe_settled`, the values each effect last saw plus failures and disposal. Refinement is
one relation, `refines Obs engine`. The choice of observation decides what counts as an optimisation: removing a
redundant run violates `observe_full` and preserves `observe_settled`.

The host can dispose nodes in a history. Node creation, and disposal from inside an effect, stay outside this tier.
Programs outside the well-behaved fragment still get the general safety theorems of the lower tiers,
such as no lost wake-ups, but no reference-tier meaning and no convergence theorem.

The first version's well-behaved fragment (author's call) excludes memo bodies that use the
previous-value argument, own nodes or cleanups, write signals, dispose nodes or create effects, and
any dependency cycle or self-read. Those exclusions are recorded in the correspondence table. Tracked and
untracked reads are in the command language, and a memo body's untracked read is excluded from the
fragment. Owning and previous-value memos are the first candidates to extend it, because Ranvier uses them.

**Pure tier.** A Rocq state machine over abstract graph states: nodes, freshness, `unannounced`,
`queued`, edges and owner trees, with operations as transitions. Bodies are programs in a small
command language (read, write, create, register cleanup, dispose), so a body can re-enter the graph.
Whole-graph theorems are stated and proved here, where they are readable and need no heap reasoning.

**Heap tier.** The actual algorithm in HeapLang, Iris's language, with arrays, handles, swap-removal,
and doubly linked owner lists. Separation-logic specifications relate each operation to a pure-tier
transition through ghost state, so heap code inherits the pure theorems. Iris earns its place here:
aliasing during walks, higher-order bodies, and recursion through node dispatch.

Iris's weakest preconditions establish partial correctness, so termination of marking is proved in the
pure tier. A flush may legitimately diverge when an effect keeps rewriting its own source, so flush
theorems are conditional on the flush returning.

### Rules that keep later proofs possible

- **The reference tier imports nothing from the engine tiers.** A parallel engine must be able to refine
  it without inheriting freshness, marks or the queue.
- **Reusable specifications stay abstract.** The observer and source structures are specified by the
  ordered edge lists they represent, with the array representation behind that specification, so another
  representation can meet the same specification. Order is part of the specification because the engine's
  effect order depends on it. A set view is derived from it for results where order plays no part.
- **Heap-tier state lives in resource algebras**, not plain predicates threaded by hand: authoritative
  edge maps and monotone freshness. These are the encodings concurrent proofs need, and they cost the
  sequential proofs little.
- **Theorem statements name no single-thread assumption they do not use.** Where sequential execution
  is what makes a proof work, the statement or its comment says so.

HeapLang is sequentially consistent. A later proof about .NET release/acquire publication would need a
weak-memory Iris logic, so the reference and pure tiers should carry over, and the heap tier may not.

## Layers

Each layer has a pure part, a heap part, or both, and imports only lower layers.

| # | Layer | F# source | Key results |
|---|-------|-----------|-------------|
| 0 | Foundations | — | Node identities, freshness order, ghost state, shared tactics |
| 1 | `ObserverSet` | `ObserverSet` | Handles are positions; add, try-add, swap-remove; **walk coverage**: a walk visits every observer present throughout it, under arbitrary removal and addition during the walk |
| 2 | `SourceList` | `SourceList` | Positional matching, truncation; one edge per source; slots and handles stay consistent through `MovedIn` |
| 3 | Edge coherence | both | Source-side and observer-side records describe the same edge set after every operation |
| 4 | Marking | `MarkDirty`, `MarkCheck`, `NotifyDirty`, `NotifyDirtyExcept`, `NotifyCheck` | Freshness only rises under marks; marking terminates, cycles included; `Dirty` is never lost to `Check`; the announcement invariant holds |
| 5 | Check resolution | `ResolveCheck`, `Refresh`, `UpdateIfNecessary` | Resolving to `Clean` means no source moved since the last run |
| 6 | Runs | `Memo.Run`, `RecomputeScoped`, `TrackCurrent`, `Effect.Execute/Run` | Marks received during a run survive it; cutoff; re-entrant reads and writes; `unannounced` discharges its debt; **no lost wake-ups** |
| 7 | Scheduler | `Schedule`, `RunFlush`, `RequestFlush`, `SettleFlush`, `RunBatch`, `EnterPull`, `ExitPull` | An effect is queued at most once; flushes do not nest; owed flushes are settled; a returned flush leaves the queue empty and effects clean; a throwing effect does not strand the queue |
| 8 | Ownership | `Owner`, `OwnerLink`, `Discharge`, `Retire`, node `Dispose` | Reverse acquisition teardown; each cleanup runs once; a disposed computation holds no source edges once its run has ended; a disposed effect ignores marks; a disposed memo still answers reads, as Core.fs documents; late registration runs immediately |
| 9 | Whole graph | all | **Glitch-free reads**: outside the self-dirtying case, a read returns the value its body would compute from current signals. **Convergence**, for well-behaved programs: at quiescence, every effect has run against the latest values it reads. Regression witnesses for #290 and #302. Iris adequacy turns specs into statements about closed programs with no Iris in them |
| R | Reference semantics | none | Written first, alongside layer 0, and imported only by layer 10. Determinism for a fixed history; glitch freedom as a property of the definition |
| 10 | Refinement | all | **Engine order characterised**: the exact effect order in a flush, as a function of history, including swap-removal's effect on it. **Weak refinement**: `refines observe_settled`: for a well-behaved program, at every batch each effect's last logged values, failures and disposal, and all host reads, match the reference; `convergence` carries that every effect has run against the latest values it reads. **Exact refinement**: `refines observe_full`: the engine's trace, run counts included, equals the reference tier's under the characterised order, or a checked counterexample shows where it does not |
| 11 | Worked optimisation | the chosen optimisation | The optimised engine satisfies `refines` under the observation chosen for it, and passes the differential check against its F# implementation |

Layer 6 is the centre of the project. Layers 9 and 10 rest on it.

### Eliciting behaviour

The owner's direction: the prover exists to bring out behaviour a parallel engine would have to address, not
to hide it. Two consequences:

- **The exact order is stated as it stands today.** The characterisation shows what a parallel engine must
  reproduce or deliberately change. Later engines can refine the reference tier under a different order.
- **Exact refinement is not weakened to make it provable.** A redundant run, such as an effect re-running on
  identical values after a batch writes 0, 1, 0, or a diamond re-marking an effect mid-run, is a potential
  bug. It is recorded with a checked counterexample, and the owner rules on each one. What counts as redundant
  is expected to be formalised during the work, and the definition lives in the reference tier.

**A failing proof is a result.** The owner expects failures, and finding them is the project's purpose. Theorems
state the intended behaviour, and are never weakened only so they pass. A failure does not block the
work: it goes in the ledger, and the layers above continue with the intended theorem as a named hypothesis
(a `Section` variable or explicit premise, not an `Axiom`), so `Print Assumptions` and the theorem
statement both show what each result depends on.

### Modelling decisions

Proposed by the author of this document; the table's reasons are open to challenge.

| Core.fs | Model | Reason |
|---------|-------|--------|
| Interface dispatch | Tagged node kinds (signal, memo, effect) | No behaviour depends on dispatch itself |
| Generic values, `IEqualityComparer` | A value type with decidable equality | A throwing comparer is a failure outcome, modelled as below |
| Exceptions from bodies | Bodies return a success-or-failure value; `finally` paths are explicit | HeapLang has no exceptions. `Failure.Moved` is modelled as part of the cutoff |
| `first`/`rest` split, `latest`, `held`, `ScanLimit` | `latest` and the scan limit kept in the heap tier; `held` abstracted to a set | The hint and limit decide which of `TryAdd`'s answers occurs, which matters for edge coherence |
| `ForwardingEdge` | Omitted | Used by non-core collection code |
| Generation wraparound (`&&& 0x7FFFFFFF`) | Unbounded integers | Recorded as an assumption: 2³¹ teardowns of one owner with a stale link |
| `Effect.Publishes`, `Memo.Adopt`, `NotifyRemoved`, `WriteExcept` | Omitted | They serve out-of-scope collections |

## Project layout

`proofs/` at the repository root:

- `proofs/README.md`: how to build, layer map, theorem index.
- `proofs/theories/`: `.v` files under one logical root, by layer.
- `proofs/CORRESPONDENCE.md`: model element → Core.fs symbol, with the pinned commit, and every
  simplification.
- `proofs/BUGS.md`: the bug ledger.
- `proofs/differential/`: the extracted interpreter, program generator and comparison.
- Dependency pins and a build entry point, chosen once the toolchain is settled.

The model pins Core.fs at commit `d701e6c5`. Comments in Rocq files follow `.claude/rules/comments.md`.

## Toolchain

Rocq and Iris are absent on this machine. Try Windows first: Rocq Platform's installer, whose Full level
lists `coq-iris-heap-lang`. Fall back to opam in WSL Ubuntu 24.04 if HeapLang is missing or
the build cannot run from Windows. The owner stated this preference. Exact Rocq and Iris versions are
left to the builder, pinned to a released, mutually compatible pair.

## Bug ledger

A suspected bug enters `proofs/BUGS.md` in stages:

1. **Suspected:** a proof obligation cannot be met, or a reading raises doubt.
2. **Modelled:** a Rocq-checked trace reaches the bad state in the model.
3. **Confirmed:** a test against the F# fails in the way the trace predicts. Only this stage counts as a
   bug. A trace that does not reproduce is a correspondence error, and fixes the model.

The proof of a theorem that a confirmed bug falsifies is stated against the fixed behaviour, and
remains open until the fix. Those theorems are named in the ledger, and are the only permitted exception
to "no admits", until the fix phase closes them.

A failing test shows the F# differs from a stated result, not which of them is wrong. When Core.fs documents
the behaviour as intended, the code wins: the entry closes as **spec corrected**, the theorem is restated,
and the change is recorded. Undocumented behaviour stays a bug candidate for the owner to rule on. A
known starting candidate is a memo that writes its own source: a run can end stale with an unchanged
value, and effects then stop with it stale and no further mark due.

Fix phase, after every layer is complete or its open theorems are ledgered: each confirmed bug is fixed
test-first in `src/Ranvier`, in the repository's normal PR flow, with the model updated to match and
its theorem closed.

## Done

- `proofs/` builds from clean, with one documented command. Failing theorems do not break the build: each
  is either a checked counterexample or a ledgered open theorem.
- `Print Assumptions` on each headline theorem shows no axioms beyond Rocq's and Iris's own.
- A search finds no `Admitted` or `admit` outside the ledgered theorems.
- Both regression witnesses check.
- Exact refinement is proved, or each divergence from it has a checked counterexample in the ledger.
- The correspondence table covers every modelled symbol.
- `src/Ranvier` is unchanged until the fix phase, apart from the worked optimisation on its own branch.
- The differential check passes on the generated program set, and runs in CI.
- The worked optimisation is proved and connected, with its benchmark results reported.

## Drift (decided)

A CI job runs the differential check when `src/Ranvier/Core.fs` or `proofs/` changes. A Core.fs change that
alters behaviour fails it, and is resolved by updating the model and its proofs. A change the generated programs do not
distinguish passes, so the check narrows drift and does not eliminate it. The model's pin moves with each update.

## Open

- **Redundant runs:** the formal definition, and the owner's ruling on each case found. A redundant run whose re-run discharges a failing cleanup does not preserve `observe_settled`, so the statement above is checked only for cleanup-free runs until the owner rules.
- **The optimisation for the milestone:** chosen by the builder at the pure-tier report. It must have a measured cost worth removing in Ranvier today, be nontrivial to prove, and be checkable by the differential test. The reasons go in the report.

## Depth and order of work (author's call; the owner stated no preference)

Reference and pure tiers first, with the differential check once the interpreter exists. Then the worked optimisation
in the pure tier. Heap proofs follow, bottom-up through layer 8, and start with what the chosen optimisation needs:
an optimisation about representation or concurrency has its proof obligations in the heap tier, and one about the
algorithm may not. The owner reviews the heap scope after the milestone. Adequacy covers bodies written in
the command language, not arbitrary HeapLang closures. A theorem that fails is not a stall. If work
stalls, meaning a proof remains neither finished nor refuted, stop and report to the owner;
the proposed cut order is heap layers 8, then 7, then 6, with the pure-tier results standing. Fixes wait
until the proof project is complete, as the owner instructed.

The Adapton results are read at the start of Task 3a, while the reference semantics is cheap to change. The worked optimisation stays after the engine proofs, because proving that it refines the reference needs most of the same invariants.

## Left to the builder

- File and module structure within a layer, ghost-state encodings, tactic style.
- Exact Rocq and Iris versions, and the build tool.
- The command language's precise shape, provided bodies can read, write, create, register cleanups,
  dispose, and re-enter.
- Order of work within a layer, and whether pure or heap goes first.
