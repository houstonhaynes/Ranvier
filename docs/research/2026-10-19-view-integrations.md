# Mounted hooks, integrations, raw HTML and client document metadata

Date: 2026-10-19. Branch `poc/fable-ranvier-dom`. Task 7 of the [framework plan](../superpowers/plans/2026-10-06-fable-ranvier-framework.md#task-7--typed-dom-events-lifecycle-and-owned-async-helpers): mounted access hooks on one placement mechanism, `widget` and `lend` integrations, raw HTML, client-side document metadata, a generated listener inside an explicit batch, and the playground screen migration.

## Mutation checks

Each mutation was applied alone to the commit named (`78ea5b9`; the owner-cleanup row to the commit that adds the owner cleanup, the next three rows to the commit that adds `widget` and `lend`, the root-exemption row to its first fix round, the raw-HTML rows and the row-root row to the commit that adds raw HTML, and the late-report row to its first fix round), compiled, run against the named test file, and reverted.

| Mutation | Location | Test file | Killed by |
|---|---|---|---|
| Effects created at registration | `Placement.Add` | `placement.test.mjs` | tree synchronous: a mounted hook runs after the element is in the host and its cleanup runs before the element leaves |
| Content never placed | `ViewBoundary.Reveal` | `placement.test.mjs` | tree synchronous: hooks under a hidden boundary content run at reveal |
| Content placed at setup | `ViewBoundary.MountContent` | `placement.test.mjs` | tree synchronous: hooks under a hidden boundary content run at reveal |
| Nested mount placed by its own `Run` | `MountState.MountRow` | `placement.test.mjs` | tree synchronous: hooks run in setup order: an instance, its inlined children, then region rows |
| Pass never placed | `ListRegion` | `placement.test.mjs` | tree synchronous: hooks run in setup order: an instance, its inlined children, then region rows |
| Failure `try` dropped | `PlacedHook.register` | `placement.test.mjs` | tree synchronous: a hook failure during mountWith unwinds the mount and rethrows |
| `contentContainer.Discard ()` dropped | `ViewBoundary.TearDown` | `placement.test.mjs` | tree synchronous: a content torn down before its reveal leaves the held-hook count at its prior value |
| Owner cleanup of an unrevealed content dropped | `ViewBoundary.Start` | `placement.test.mjs` | tree synchronous: a content disposed while pending leaves the held-hook count at its prior value and runs no hook |
| No-children check removed | `Template.elementNode` | `integration.test.mjs` | an element with a widget part and template children fails at definition time |
| Release check removed | `PlacedHook.register` | `integration.test.mjs` | tree synchronous: an integration that moves its element is reported after its cleanup |
| Attach at setup | `MountState.Placed` | `integration.test.mjs` | tree synchronous: a widget renders into its element once placed and its destroy runs while the element is attached |
| Root exemption removed | `PlacedHook.register` | `integration.test.mjs` | tree synchronous: a lent mount root moved with its mount to another host is not reported |
| Raw-HTML equality skip removed | `Template.contentSame` | `unsafe-html.test.mjs` | template synchronous: raw HTML replaces the element's content and is re-parsed only when the markup changes |
| Raw-HTML no-children check removed | `Template.elementNode` | `unsafe-html.test.mjs` | an element with raw HTML and template children fails at definition time; void, raw-text, template, textarea and title elements reject raw HTML |
| Raw HTML not counted as a content part | `Template.elementNode` | `unsafe-html.test.mjs` | an element with raw HTML and template children fails at definition time; void, raw-text, template, textarea and title elements reject raw HTML |
| Raw-HTML parser-context check disabled | `Template.elementNode` | `unsafe-html.test.mjs` | an element with raw HTML and template children fails at definition time; void, raw-text, template, textarea and title elements reject raw HTML |
| Root exemption removed, row roots | `PlacedHook.register` | `integration.test.mjs` | tree synchronous: a lent row root moved by a keyed reorder and with its mount to another host is not reported |
| Late-report check removed | `CheckSupport.strictCases` | `integration.test.mjs` | a strict report raised after a case returns fails the next case |

Dropping either `contentContainer.Discard ()` call alone changes no hook's behaviour: a torn-down or disposed content's container is never placed again, and the core disposes an effect created under a disposed owner as it attaches. `Discard` releases the held closures, which the held-hook count (`PlacementCounters.held`, defined under `RANVIER_VIEW_COUNTERS`) observes.

## Measurements

Results: [`framework-performance/investigation/view-integrations/`](framework-performance/investigation/view-integrations/). Baselines at the commit of the batch-contract tests, which changed no runtime code (`keyed-before.json` and `template-before.json` record `c9d0a48` with that commit's changes uncommitted: the measured tree is `d5fa26b`, which is `c9d0a48` plus those changes, apart from `Html.coverage.json`, a generator data file that was also modified then, is not compiled, and was left out of the commit); after-measurements at the commit of the document entries (`c58e99f`), first as committed, with a placement container per pass, then with lazy containers applied uncommitted (committed as `c1d2033`). The runners record the commit and, for keyed rows, the uncommitted paths.

### Templates without placed, content or head parts

Ranges are the minimum and maximum of the three per-process medians (`byProcessMs` for keyed rows, `byProcessUs` for the template comparison), in the unit of the row's benchmark. Width-1 template rows show two decimals; every overlap judgment compares the unrounded values. Ranvier modes only: `ranvier` for keyed rows; `ranvier` and `ranvier-template` for the template comparison.

| Benchmark | Operation | Before (per-process range) | After, containers per pass | Overlap | After, lazy containers | Overlap |
|---|---|---|---|---|---|---|
| keyed-rows (ms) | create1k | 5.395–5.495 | 5.440–6.095 | yes | 5.440–5.580 | yes |
| keyed-rows (ms) | create10k | 47.935–48.750 | 48.250–50.095 | yes | 48.365–48.765 | yes |
| keyed-rows (ms) | replace1k | 4.615–4.820 | 4.715–5.005 | yes | 4.480–4.710 | yes |
| keyed-rows (ms) | update10th1k | 0.055–0.055 | 0.055–0.065 | yes | 0.055–0.065 | yes |
| keyed-rows (ms) | swap1k | 0.120–0.150 | 0.135–0.155 | yes | 0.135–0.145 | yes |
| keyed-rows (ms) | remove1k | 0.125–0.140 | 0.140–0.165 | yes | 0.145–0.145 | no, slower |
| keyed-rows (ms) | remove10k | 0.605–0.610 | 0.600–0.610 | yes | 0.595–0.625 | yes |
| keyed-rows (ms) | clear1k | 0.850–0.895 | 0.855–0.955 | yes | 0.850–0.870 | yes |
| keyed-rows (ms) | clear10k | 5.970–6.195 | 6.040–6.110 | yes | 5.995–6.360 | yes |
| keyed-rows (ms) | append1k | 3.420–3.480 | 3.500–3.710 | no, slower | 3.445–3.495 | yes |
| keyed-rows (ms) | armsCreate1k | 9.720–10.135 | 10.070–10.585 | yes | 10.010–10.615 | yes |
| keyed-rows (ms) | armsSwitch1k | 4.130–4.230 | 4.675–4.770 | no, slower | 4.115–4.230 | yes |
| keyed-rows (ms) | armsPayload1k | 0.685–0.700 | 0.675–0.710 | yes | 0.665–0.730 | yes |
| keyed-rows (ms) | armsClear1k | 1.420–1.545 | 1.460–1.515 | yes | 1.410–1.680 | yes |
| template comparison (µs) | `ranvier` width 1 mount | 8.22–8.48 | 8.26–8.50 | yes | 8.22–8.79 | yes |
| template comparison (µs) | `ranvier-template` width 1 mount | 5.37–5.90 | 5.59–5.90 | yes | 5.53–6.11 | yes |
| template comparison (µs) | `ranvier` width 1 update | 1.00–1.03 | 0.98–1.03 | yes | 1.02–1.12 | yes |
| template comparison (µs) | `ranvier-template` width 1 update | 1.00–1.06 | 0.97–1.00 | yes | 1.00–1.08 | yes |
| template comparison (µs) | `ranvier` width 1 dispose | 1.45–2.25 | 1.35–1.43 | no, faster | 1.50–2.01 | yes |
| template comparison (µs) | `ranvier-template` width 1 dispose | 1.41–2.05 | 1.25–1.33 | no, faster | 1.33–1.39 | no, faster |
| template comparison (µs) | `ranvier` width 16 mount | 63.6–71.0 | 64.8–65.4 | yes | 65.3–70.5 | yes |
| template comparison (µs) | `ranvier-template` width 16 mount | 43.2–43.8 | 41.8–43.0 | no, faster | 41.1–45.8 | yes |
| template comparison (µs) | `ranvier` width 16 update | 13.3–13.7 | 13.3–13.3 | yes | 13.3–14.1 | yes |
| template comparison (µs) | `ranvier-template` width 16 update | 14.0–14.2 | 13.7–13.9 | no, faster | 13.6–14.7 | yes |
| template comparison (µs) | `ranvier` width 16 dispose | 10.4–12.1 | 9.2–11.2 | yes | 9.3–14.1 | yes |
| template comparison (µs) | `ranvier-template` width 16 dispose | 4.8–7.1 | 4.5–5.0 | yes | 4.4–8.0 | yes |
| template comparison (µs) | `ranvier` width 128 mount | 506.7–546.9 | 515.6–533.1 | yes | 518.6–559.1 | yes |
| template comparison (µs) | `ranvier-template` width 128 mount | 307.3–325.8 | 310.3–321.3 | yes | 300.8–328.3 | yes |
| template comparison (µs) | `ranvier` width 128 update | 105.9–108.6 | 103.1–109.9 | yes | 104.6–110.6 | yes |
| template comparison (µs) | `ranvier-template` width 128 update | 110.8–111.9 | 109.8–113.9 | yes | 108.6–113.6 | yes |
| template comparison (µs) | `ranvier` width 128 dispose | 68.3–84.8 | 80.6–85.2 | yes | 71.1–84.1 | yes |
| template comparison (µs) | `ranvier-template` width 128 dispose | 28.1–30.8 | 28.8–30.6 | yes | 27.7–31.7 | yes |

With a container per pass, `append1k` and `armsSwitch1k` ranges lie above their baselines. Containers are now lazy: a list or arm region keeps one `PlacementPass`, which allocates the pass's container at the first request (a hook, a head entry or a nested region of a row built by the pass, or an authored row) and places only a container it allocated. The lazy measurement brings both operations back within their baselines; `remove1k` then lies one timer step (5 µs) above its baseline range.

Same-session runs ([`keyed-same-session.json`](https://github.com/shayanhabibi/Ranvier/blob/d784929cd9c67eed51bb319d1f3c2b993dcd9b54/docs/research/framework-performance/investigation/view-integrations/keyed-same-session.json)) show disjoint ranges of this size between repeated runs of one build. Session `ab` alternates a build of `d5fa26b` (`ctl`) with the lazy build (`new`); session `ab2` alternates containers per pass (`pp`) with the lazy build (`lazy`). `d5fa26b` itself measures `remove1k` at 0.140–0.165 ms and `armsSwitch1k` at 4.000–4.870 ms. Runs of one build are disjoint in 19 of their 112 operation pairs (8 run pairs of one build × 14 operations). Lazy runs against `d5fa26b` or containers-per-pass runs are disjoint in 41 of their 224 operation pairs (16 run pairs × 14 operations): 32 with the lazy run faster and 9 with it slower, those 9 separated by at most 0.115 ms (`clear10k`, 2 % of its median). Containers per pass and lazy containers measure alike within that spread.

| Operation | ab ctl1 | ab new1 | ab ctl2 | ab new2 | ab2 pp1 | ab2 lazy1 | ab2 pp2 | ab2 lazy2 |
|---|---|---|---|---|---|---|---|---|
| create1k | 5.475–5.830 | 5.495–5.825 | 5.570–5.715 | 5.450–5.800 | 5.600–6.080 | 5.350–5.555 | 5.415–5.530 | 5.460–6.105 |
| create10k | 48.930–49.770 | 48.000–49.160 | 48.550–48.930 | 48.555–49.570 | 48.565–49.345 | 48.090–48.865 | 48.085–48.650 | 47.710–48.740 |
| replace1k | 4.710–4.925 | 4.670–5.150 | 4.750–5.020 | 4.430–4.630 | 4.655–4.875 | 4.685–4.705 | 4.660–4.750 | 4.590–4.915 |
| update10th1k | 0.055–0.070 | 0.060–0.060 | 0.055–0.060 | 0.050–0.055 | 0.050–0.065 | 0.055–0.055 | 0.050–0.055 | 0.055–0.055 |
| swap1k | 0.145–0.160 | 0.140–0.155 | 0.135–0.135 | 0.130–0.135 | 0.140–0.145 | 0.135–0.145 | 0.125–0.135 | 0.130–0.145 |
| remove1k | 0.145–0.165 | 0.130–0.165 | 0.140–0.145 | 0.135–0.145 | 0.135–0.145 | 0.140–0.140 | 0.140–0.145 | 0.140–0.145 |
| remove10k | 0.595–0.620 | 0.595–0.635 | 0.595–0.605 | 0.595–0.625 | 0.595–0.615 | 0.605–0.620 | 0.605–0.620 | 0.595–0.615 |
| clear1k | 0.895–0.920 | 0.855–0.900 | 0.870–0.895 | 0.845–0.930 | 0.845–0.875 | 0.835–0.870 | 0.845–0.940 | 0.825–0.880 |
| clear10k | 5.975–6.190 | 6.145–6.320 | 6.005–6.060 | 5.980–6.105 | 6.040–6.200 | 6.005–6.175 | 5.985–6.030 | 6.105–6.240 |
| append1k | 3.510–3.590 | 3.445–3.485 | 3.465–3.585 | 3.455–3.540 | 3.425–3.475 | 3.390–3.450 | 3.445–3.490 | 3.435–3.490 |
| armsCreate1k | 10.015–10.380 | 9.780–10.890 | 9.840–10.390 | 10.020–10.175 | 9.845–10.325 | 10.030–10.435 | 10.015–10.505 | 9.735–10.490 |
| armsSwitch1k | 4.300–4.630 | 4.315–4.480 | 4.000–4.870 | 4.115–4.155 | 4.225–4.555 | 4.050–4.145 | 4.030–4.565 | 4.100–4.775 |
| armsPayload1k | 0.700–0.825 | 0.695–0.735 | 0.695–0.800 | 0.680–0.690 | 0.675–0.710 | 0.670–0.680 | 0.670–0.695 | 0.675–0.690 |
| armsClear1k | 1.515–1.925 | 1.555–1.690 | 1.455–1.770 | 1.395–1.445 | 1.365–1.470 | 1.435–1.460 | 1.445–1.495 | 1.410–1.555 |

| Mutation | Location | Test file | Result |
|---|---|---|---|
| A pass's container never placed | `PlacementPass.Place` | `placement.test.mjs` | killed (56 failures), first by tree synchronous: hooks run in setup order: an instance, its inlined children, then region rows |
| One container for every pass of a region | `PlacementPass.Begin` | every `test:dom` file | survives: a later pass reconciles inside its region's computation, which defers a hook effect created there until the pass ends, so the earlier pass's placed container runs it at the same point |

### One mounted hook per row, against Solid 2's `onSettled`

Medians from `summaries` with the per-process range in parentheses; hook costs from `hookCost` (hooked minus plain) with the range of the per-process differences. Lazy containers; `verified` holds 36 entries (12 per process). Reported, not gated.

`placement-hooks.json` records `baseCommit` `c58e99f` and no list of uncommitted paths. It measured the lazy-container build: the run (`measuredAt` 13:15:44Z) was the `--build` run that followed restoring the lazy `Mounting.fs` and `Placement.fs` (after 13:11Z), whose content is that of `c1d2033`, with the benchmark files of this section uncommitted. A placement run with containers per pass (12:57Z) is not committed; its hook costs were 7.0, 17.5 and 61.9 µs at widths 1, 16 and 128.

| Width | Ranvier hooked (µs) | Ranvier plain (µs) | Ranvier hook cost (µs) | Solid 2 hooked (µs) | Solid 2 plain (µs) | Solid 2 hook cost (µs) |
|---|---|---|---|---|---|---|
| 1 | 12.3 (12.0–13.4) | 5.9 (5.9–6.1) | 6.4 (6.1–7.3) | 4.7 (4.5–5.7) | 2.5 (2.4–2.5) | 2.2 (2.0–3.2) |
| 16 | 35.0 (34.1–35.6) | 17.5 (17.2–18.0) | 17.5 (16.9–17.7) | 18.1 (17.5–18.3) | 8.3 (8.1–8.7) | 9.8 (8.8–10.0) |
| 128 | 186.9 (179.4–233.8) | 120.6 (114.4–123.1) | 66.3 (56.2–119.4) | 103.1 (100.6–107.5) | 55.6 (53.8–58.8) | 47.5 (45.6–49.4) |

The 128-row Ranvier hooked range includes one process at 233.8 µs; the other two measure 179.4 and 180.0 µs.

Fairness caveats:

- The adapter's settle check calls `querySelectorAll("[data-placed]")` on every hooked list for both frameworks, once when every hook has run at mount and once per extra microtask turn otherwise; it is part of the hooked time.
- Ranvier's hook returns `ignore` as its cleanup, which registers one owner cleanup per row; Solid 2's `onSettled` callback returns none.
- `sample` waits at most 20 microtask turns per list and does not check the count afterwards; only `verify` asserts that every hooked row is marked.
- No GC is forced between samples, so a collection can land inside one; this is a plausible cause of the 233.8 µs process.
- Each Ranvier list creates and disposes its own `Graph`, in both the hooked and the plain scenario, so the cost cancels in the hook cost but is part of each absolute time.
