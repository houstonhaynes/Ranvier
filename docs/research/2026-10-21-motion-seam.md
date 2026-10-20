# Motion seam: placed-hook kinds, departures and reflow

Date: 2026-10-21. Steps 3–7 of `docs/superpowers/specs/2026-10-21-motion-design.md`: the `Motion`, `Exit` and `Reflow` placed-hook kinds, the placement context, held exits (`Departures.fs`) and reflow measurement in region passes. No anime.js code is involved; the tests drive the hooks with plain callbacks (`examples/Fable.Ranvier.Playground/tests/DepartureChecks.fs`).

## Where the registry lives

`Departures.fs` is in `experiments/Ranvier.View.Portable` (after `Typed.fs`), not in the Foundation as the spec places it. The mount paths reach the registry only through `Departures.seam`, a plain object whose `Enabled` and `ReflowEnabled` flags are unset and whose operations (`Retire`, `NextLive`, `Holds`, `Within`, `End`, `Before`) are null until the first registration. The registrations are called from the payloads of `HookBuilder.exit` and `HookBuilder.reflow`, so a bundle that never uses either hook holds the seam object and its call sites, and none of the registry. `HookBuilder` is Portable code, so the registry it calls is Portable code too; on .NET the seam is null and never read.

Each placed hook is registered with its container and runs once the container is placed, whatever its kind. `PlacedHook.register` passes every hook the flags of its context (1 hydrated, 2 inserted), which `HookBuilder.motion` turns into a `MotionContext`. An exit hook's payload registers its exit and returns, as its cleanup, the mark that the exit follows (`pending`). A reflow hook's payload registers its measurement and returns the withdrawal as its cleanup.

The pending mark is what keeps a held root's content. Core teardown runs an owner's cleanups and child owners in reverse acquisition order. The exit hook's effect is acquired when the container is placed, after the row's nested regions were built, so its cleanup runs before those regions tear down. A nested mount (a list row or arm under queued commits, a boundary view or a view hole in either mode) whose first node is `Within` a pending or leaving root then leaves its nodes in place, unclaimed: they leave with the held root through its `finish`, the mount's release or the forced path.

## Two paths carry template rows

A sugar template mounts through one of two paths, chosen by the mount's commit mode:

- **Synchronous commits:** `TemplateDom.materialise`. List rows and arms are template rows, built by `RowBuilder` into the region's staging fragment and removed by `Row.Remove`.
- **Queued commits:** `TemplateComponent.Expand` over the mount's `Backend`. List rows and arms are nested mounts (`MountRow`), and their roots leave through the nested mount's `OwnedRange.Release`.

The spec places exits on template rows only. Under queued commits a sugar template has no template rows, so the seam also covers the expanded path: `MountState.Placed` dispatches the three kinds as `TemplateDom.bind` does, and a list row's or arm's nested mount (its inherited container is a region pass's, `Placement.Row`) retires its roots into the enclosing mount's held list on a live release. Boundary views and view holes do not hold their own roots, but keep their nodes under a held root.

## Sibling-walk audit (Risk 1)

Every sibling walk, bulk removal and boundary placement in `experiments/Ranvier.View.Foundation`, with the case that covers it or the ruling that leaves it unchanged. Case names are those of `DepartureChecks.cases`, each run under synchronous and queued commits unless marked.

| Site | What it walks | Held nodes | Coverage |
|---|---|---|---|
| `Row.ValidateWindow` start cursor and row stepping | region contents from `regionStart` or the previous row | skipped with `Departures.seam.NextLive` once an exit is registered | "reorders and inserts validate while roots are held", "a key re-added while its old root leaves gets a new row", "moving the mount carries its held roots" |
| `Row.Remove` | a row's roots, `First` to `Last` | the retire point: each root is offered to `Departures.seam.Retire`; a failure is raised after every root is handled | "an exit holds its row's root until finish", "a raising exit on a row's first root removes every root and keeps the region valid" |
| `OwnedRange.Release` (nested list-row and arm mounts) | the range's entries and generated descendants | the retire point on the expanded path; a retiring release leaves the generated descendants (region markers inside roots) to their roots | queued variants of every exit case; "a held root keeps its nested lists, arms and boundaries until finish" |
| `MountState.release` of a nested mount under a held root | the nested mount's range, released rows and held list | kept in place (`Within`); the held list moves to the enclosing mount on a live release | "a held root keeps its nested lists, arms and boundaries until finish", "a root held inside a removed row leaves with the row and runs until disposal" |
| `RegionSite.FlushReleased` cleared path → `Row.ClearBetween` → `deleteBetween` / `clearChildren` | everything between the markers | taken only when no node between the markers is leaving or has an exit (`seam.Holds`) | "clearing a list with exits holds every root" |
| `Row.MoveBefore` | a live row's roots | none: a held node never sits between a live row's `First` and `Last`; rows are inserted before a live row's `First` or `regionEnd` | ruling 1 |
| `Row.Layout` anchors and staged insert | rows of `next`, anchored at `next[bEnd].First` or `regionEnd` | stay where they are; rows move around them | "reorders and inserts validate while roots are held" |
| `ArmRegion` `Row.MoveBefore (row, parent, regionEnd)` | the new arm's roots | the old arm's held root stays before the new arm | "an arm switch leaves the old and new arm roots in order" |
| `OwnedRange.intactAt`, `ValidateDestination`, `Move` | entries and region spans | inside a region span, walked or moved with it | "moving the mount carries its held roots" |
| `MountState.release` | — | ends every held exit (cancel, then remove) before the range is released | "disposing the mount cancels and removes every held root" |
| `clearAdopted`, `positionAfter`, `RowBuilder` separator and row-marker adoption, `TemplateDom.logicalNext` | server nodes at the hydration cursor | none: they run in a region's hydrating first pass, before any row of the region can retire | ruling 2; "a hydrated row's root with an exit is held when its row is retired" (synchronous) covers retirement after hydration |
| `ViewBoundary` `copyServed`, `clearServed` → `Row.ClearBetween`, `place` → `fillsFragment` | the boundary's markers and its view rows | none: boundary views do not hold roots, and a list inside a view keeps its held roots inside the view's own nodes; under a held row root the view keeps its nodes | ruling 3; "a held root keeps its nested lists, arms and boundaries until finish" |
| `HoleRegion` `Row.MoveBefore` | the hole's view row | none: holes do not hold roots; under a held row root the view keeps its nodes | ruling 3 |
| `TemplateDom.materialise` root and region-marker walks | a fresh clone | none | not applicable |

Rulings:

1. Ruling: `Row.MoveBefore` keeps its plain `nextOrNull` walk — a held root is outside every live row's `First`..`Last` span, because retirement removes the row from the laid-out rows before any later layout — cost if wrong: a move would carry a held root with a live row, which the next validation would report as external mutation.
2. Ruling: hydration walks keep `nextOrNull` — they run only inside a region's hydrating first pass, and a held root exists only after a later pass retires a row — cost if wrong: a recovered hydration mismatch would delete a leaving root early (its exit's `finish` then does nothing).
3. Ruling: boundary views and view holes do not hold their roots — the spec excludes exits on boundary content, and holding a view root between boundary markers would break `clearServed` — cost if wrong: an exit on a boundary view root ends with the view instead of animating.

## Rulings

- Ruling: `MotionContext.Hydrated` is read from the services' hydration cursor at bind time (template path) or the mount's cursor (expanded path), not from `root` — a non-root element of an adopted row is also hydrated, and a row created after hydration in a hydrated mount is not — cost if wrong: `Motion.appear` would play or skip on the wrong rows.
- Ruling: an exit registers when its container is placed, like every placed hook — the runtime then needs no exit-specific dispatch, which keeps the registry out of bundles without exits — cost if wrong: a row retired before its hooks run leaves at once, with no exit.
- Ruling: the Debug root check ("An exit hook requires a row or arm root element") runs when the instance binds, while a template row's roots are still children of the staging fragment, and also rejects an exit whose container is not a region pass's (a mount root, a boundary view, a hole); the root test is exact on the template path and permissive on the expanded path, where an element receives its modifiers before it is appended — cost if wrong: a misplaced exit below a row root under queued commits is silently inert.
- Ruling: `Departures.fs` lives in the Portable project, beside `HookBuilder`, instead of the Foundation — only a registry reached from the hook payloads stays out of bundles that do not use the hooks — cost if wrong: none at run time; the file moves with the view either way.
- Ruling: a forced removal (the mount, or an enclosing mount, tears down) passes no held list, and the node's exit entry stays in the `WeakMap` until the node is collected — cost if wrong: none; a removed node is never retired again.
- Ruling: a root held inside a row that is then removed leaves with that row (or stays inside it, when that row is held too); its exit keeps running until `finish`, the safety timeout or the mount's disposal, which cancels it — under both commit modes: a nested mount's live release moves its held exits to the enclosing mount instead of ending them — cost if wrong: an exit callback runs for up to 10 s on a detached node.
- Ruling: an exit that raises finishes at once (its node leaves); `Row.Remove` handles every other root of the row, then raises, and the error is reported with the pass; the region stays valid — cost if wrong: none beyond the report.
- Ruling: `MotionContext.ExitPending ()` reads whether the element has a registered exit that has not started, for the "paused, not reverted" interplay of step 8; it is true in the motion hook's cleanup whichever order the two hooks have on the element, and also during a forced teardown, where the paused animation is removed with the node — cost if wrong: a forced teardown pauses instead of reverting an animation on a node that is about to be removed.
- Ruling: a failing reflow `Before` closes its measurement and the pass runs on; the error is raised from the posted `After` instead, so it is reported once, after the pass's commits, with the mount's scope. A failing `After` is reported once with the mount's scope: caught in `MountState.Post`'s synchronous effect, or through its commit slot under queued commits. An earlier version let a failing `Before` escape the reconcile effect, which stopped the list for the rest of the mount without a report (found on re-review) — cost if wrong: a reflow failure surfaces one commit later than the pass that caused it.
- Ruling: `Departures.register` clears a node's pending mark before registering its exit — a hydration abandon keeps server nodes in place, and a pending mark left on a kept node would make a later adoption of it keep its nested mounts' nodes — cost if wrong: none.

Rulings from the review of 2026-10-21:

- Ruling: the bundle budget is re-baselined from ≤ 150 B to ≤ 450 B gzip for the full feature set, expanded and queued path included — the attribution below shows that roughly 400 B is the cost of the touched sites, with no behaviour removed — cost if wrong: about 300 B more on every page than the original budget.
- Ruling: `Departures.fs` in Portable is accepted — `HookBuilder` compiles before the Foundation, and registering from the payloads keeps the registry out of bundles without the hooks — cost if wrong: none at run time.
- Ruling: `Hydrated` from the hydration cursor is accepted — `root` is per root node, so inner elements of an adopted row would replay `appear` — cost if wrong: `appear` plays or skips on the wrong rows.
- Ruling: an exit registered at container placement is accepted — it matches every other placed hook; only a row created and retired before its container is placed leaves without an exit — cost if wrong: such a row skips its exit.
- Ruling: exits and reflows on the queued (expanded) path are accepted on condition that held roots keep their nested content and inner exits behave as under synchronous commits; both are now tested in both modes — cost if wrong: about 40 B gzip and the nested-mount plumbing.
- Ruling: `MountState.leaving` holds each held root's end action (cancel, then remove) instead of `Node * cancel` tuples — removal by reference needs an identity, and F# tuples compare structurally under Fable — cost if wrong: none.

## Seam gate (step 7)

Baseline: `268c357` (master `642d889` plus the spec), measured in a detached worktree of that commit on the same machine (AMD Ryzen 9 9900X, Node 26.7.0, Chromium 153.0.8010.12, Fable 5.18.0).

### Bundle delta

`benchmarks/frameworks/sugar-size.mjs` (rolldown, minified, gzip level 9) against the baseline. Final code, after the review's slimming (flags computed in `PlacedHook.register`, one stepping function per `ValidateWindow`, the cleared guard and the held-exit loop moved into the registry, reflow posted through `seam.Before` and one `MountState.Post`, `Inserted` kept by `PlacementPass.Begin`) and the review's fixes (pending mark, nested content kept, held exits moved to the enclosing mount, error collection in `Row.Remove`):

| Entry | Baseline raw / gzip / brotli | Seam raw / gzip / brotli | Delta gzip |
|---|---|---|---|
| explicit | 212381 / 61180 / 53654 | 213495 / 61627 / 54035 | **+447** |
| explicit-combined | 213763 / 61361 / 53756 | 214878 / 61814 / 54145 | +453 |
| sugared | 216416 / 62020 / 54249 | 217531 / 62461 / 54657 | +441 |
| members | 216566 / 62005 / 54317 | 217647 / 62432 / 54613 | +427 |
| class | 216656 / 62073 / 54377 | 217737 / 62497 / 54753 | +424 |
| combined | 218051 / 62230 / 54495 | 219132 / 62673 / 54767 | +443 |

The budget is ≤ 450 B gzip (re-baselined, see Rulings). History: +1236 B with the registry in the Foundation; +587 B behind the seam; +416 B in the reviewer's slimmed prototype; +462 B with the review's fixes added; +438 B after deriving a nested mount's row status from its inherited container (`Placement.Row`, replacing the `departs` flag and `MountRow` parameter), leaving a retiring release's generated descendants to their roots, and dropping a redundant `Outermost` test (a disposed ancestor disposes the enclosing mount first); +447 B after the re-review's reflow failure handling (`Before` caught in the registry, synchronous `After` failures reported to the scope). The members, class and combined rows are those of the +438 B build.

Attribution, measured by the reviewer on the slimmed seam (each part removed on top of the others):

| Part | Delta gzip |
|---|---|
| Reflow region code | 126 before slimming, about 70 after |
| Expanded-path hold | 37 |
| Server-omission branches | 20 |
| `Inserted` | 42, about 34 through `Begin` |
| Validation stepping | 9 |
| Cleared guard | 20 |
| Retire, held list and their release | 98 |
| Kinds, payload `Func` change, flags, row-mount plumbing and the seam object | 128 |

Earlier experiments on the first seam: removing the expanded-path hold saved 44 B, and also removing reflow saved 166 B in total.

### Keyed rows

`keyed-rows.mjs --build`, 3 processes × 15 rounds, per-process medians (ms). Every operation's range overlaps between baseline and seam except `clear10k` for `ranvier-sugar`, where the seam is faster (baseline 7.03–9.69, seam 6.28–6.48). No measurable regression, so the Risk 4 fallback (a separate `Part` case for the new kinds) is not applied.

| Operation | ranvier baseline → seam | ranvier-sugar baseline → seam |
|---|---|---|
| create1k | 6.50 [6.36–6.93] → 6.83 [6.22–7.42] | 6.58 [6.50–7.19] → 6.79 [6.37–7.44] |
| create10k | 53.60 [53.02–54.28] → 53.07 [52.24–56.71] | 53.71 [52.83–54.48] → 52.88 [52.67–54.12] |
| replace1k | 5.79 [5.58–5.83] → 5.58 [5.34–6.26] | 5.58 [5.49–5.81] → 5.63 [5.34–6.34] |
| update10th1k | 0.065 [0.065–0.070] → 0.065 [0.055–0.075] | 0.065 [0.060–0.070] → 0.065 [0.065–0.075] |
| swap1k | 0.140 [0.135–0.145] → 0.150 [0.145–0.165] | 0.155 [0.150–0.155] → 0.150 [0.135–0.175] |
| remove1k | 0.155 [0.140–0.170] → 0.170 [0.160–0.205] | 0.150 [0.145–0.175] → 0.165 [0.160–0.190] |
| remove10k | 0.655 [0.655–0.660] → 0.645 [0.635–0.655] | 0.630 [0.615–0.640] → 0.640 [0.630–0.665] |
| clear1k | 0.965 [0.925–0.995] → 0.990 [0.940–1.005] | 0.965 [0.935–1.015] → 0.970 [0.955–1.030] |
| clear10k | 6.89 [6.57–7.95] → 6.82 [6.42–7.24] | 9.12 [7.03–9.69] → 6.34 [6.28–6.48] |
| append1k | 3.90 [3.77–3.93] → 3.89 [3.82–3.97] | 3.66 [3.64–3.79] → 3.63 [3.56–3.81] |
| armsCreate1k | 11.63 [11.16–12.38] → 11.33 [11.30–11.56] | 11.86 [11.54–12.12] → 11.42 [11.26–11.99] |
| armsSwitch1k | 5.67 [5.65–5.84] → 5.54 [5.43–5.73] | 5.66 [5.40–5.88] → 5.43 [5.25–5.57] |
| armsPayload1k | 0.785 [0.770–0.805] → 0.790 [0.775–0.805] | 0.805 [0.770–0.870] → 0.795 [0.780–0.825] |
| armsClear1k | 1.89 [1.66–2.00] → 1.73 [1.66–1.88] | 1.86 [1.66–2.06] → 1.68 [1.63–1.81] |

After the review's slimming and fixes, `keyed-rows.mjs --build` against the same baseline: every Ranvier range overlaps, or is disjoint on the faster side (`create10k` both modes, `replace1k` sugar, `remove10k`, `clear10k` sugar, `append1k`: the seam 1.5–5.4 % faster, with Solid itself 0.7–9.4 % faster in the same run). No operation is slower beyond its range.

| Operation | ranvier final | ranvier-sugar final |
|---|---|---|
| create1k | 6.36 [6.18–6.41] | 6.29 [6.26–6.56] |
| create10k | 51.97 [51.44–52.72] | 51.85 [51.51–52.40] |
| replace1k | 5.59 [5.37–5.74] | 5.36 [5.19–5.42] |
| update10th1k | 0.065 [0.060–0.065] | 0.065 [0.060–0.065] |
| swap1k | 0.140 [0.130–0.155] | 0.150 [0.140–0.160] |
| remove1k | 0.155 [0.145–0.170] | 0.160 [0.145–0.175] |
| remove10k | 0.645 [0.635–0.650] | 0.645 [0.635–0.650] |
| clear1k | 0.935 [0.915–0.945] | 0.945 [0.945–0.965] |
| clear10k | 6.87 [6.80–8.28] | 6.32 [6.27–6.36] |
| append1k | 3.69 [3.68–3.72] | 3.56 [3.49–3.70] |
| armsCreate1k | 11.36 [11.07–11.69] | 11.37 [11.16–11.95] |
| armsSwitch1k | 5.51 [5.26–5.72] | 5.58 [5.22–5.71] |
| armsPayload1k | 0.800 [0.785–0.850] | 0.815 [0.750–0.865] |
| armsClear1k | 1.87 [1.74–1.97] | 1.85 [1.71–2.00] |

### Template comparison

`run.mjs --template-comparison`, run five times in the order baseline, seam, seam, baseline, seam (3 processes each). In the first pair alone, 10 of the 45 Ranvier cells had disjoint ranges, and Solid, Ripple and React, which the seam does not touch, drifted by the same 5–10 % on update and dispose. Pooled over all runs (6 baseline and 9 seam process medians per cell), every one of the 45 Ranvier cells (widths 1, 16, 128 × mount, update, dispose × five Ranvier modes) overlaps; median changes lie within −8.2 % to +8.7 %, with the template modes within −1.7 % to +5.6 %. The per-cell table is in the job's `motion-seam/template-pooled.txt`.

### Counters

The view counts are asserted by the `DepartureChecks` case "keyed and arm lists with no motion hook count the work they counted before the seam" (compiled under `RANVIER_VIEW_COUNTERS` in `test:dom`, run before any hook of the file registers, and asserting that the seam stays disabled). The core counts were compared once with `generated-tests-counters` (core counters and `RANVIER_VIEW_COUNTERS`) after one script of keyed-list operations (create 1000, update every 10th, swap, remove, append 100, clear, create 200, dispose) and arm-list operations (create 200, switch, payload, clear, dispose), with no motion, exit or reflow hook. The baseline and the seam give identical counts:

```
core: SignalsCreated 3410, MemosCreated 400, EffectsCreated 2102, OwnersCreated 2308, EdgesAdded 2304, EdgesRemoved 2304,
      ObserverInserts 2304, ObserverRemoves 2304, MemoRecomputes 1000, EffectRuns 2810, Flushes 13, FactoryRowsCreated 0,
      OnceRowsCreated 1500
view: ListCounters lookups 1502, validatedRows 210, increasingRuns 0; PlacementCounters held 0; BoundaryCounters wakes 0
```
