# Server HTML and hydration: measurements and mutation checks

Date: 2026-10-19. Branch `poc/fable-ranvier-dom`. Plan: [server HTML and hydration](../superpowers/plans/2026-10-19-server-html-hydration.md).

## First content on .NET

Render time and allocated bytes per render, BenchmarkDotNet 0.15.8, Release, default job with `MemoryDiagnoser`. The fixtures are `ServerFixtures.renderComponentPage` and `ServerFixtures.renderKeyedPage`.

Machine: AMD Ryzen 9 9900X (12 cores), Windows 11 25H2, .NET SDK 10.0.402, .NET 10.0.13 X64 RyuJIT x86-64-v4.

### Harness

Every server task re-runs these two commands from the repository root and adds rows labelled `after <task>`. `OUT` is a local directory that is not committed; BenchmarkDotNet artifacts and the byte JSON stay there.

```bash
OUT=<local output directory>/server-html/<label>
dotnet run -c Release --project experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj -- --filter '*' --exporters json --artifacts $OUT/dotnet
dotnet run -c Release --project experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj -- --bytes > $OUT/bytes.json
```

`--bytes` prints one JSON object per line: `{"fixture":"component"|"keyed","rows":N,"bytes":B,"bytesPerRow":B/N}`, with `bytes` the UTF-8 length of the rendered page. The timing tables read `Mean`, `Error`, `StdDev` and `Allocated` from `$OUT/dotnet/results/*-report-github.md`.

Raw artifacts for each phase were written to `server-html/<phase>/` under the local benchmark output directory (`before-s1`, `after-s1`, `task5` for after S2 and `task-7` for after S3; local, not committed).

BenchmarkDotNet's environment block tags `[Host]` with `DEBUG`: the tag describes the F# host assembly that launches the run, while the timed child process is built with `-c Release` and `Optimize=true`.

Any "before S1 / after Sx" comparison in this note is cross-session, not a gate.

### Results

Mean, Error and StdDev are in µs.

| Phase | Fixture | Rows | Mean (µs) | Error (µs) | StdDev (µs) | Allocated | HTML bytes | HTML bytes per row |
|---|---|---|---|---|---|---|---|---|
| before S1 | component | 1 | 1.019 | 0.0203 | 0.0579 | 5.62 KB | 110 | 110.0 |
| before S1 | component | 16 | 8.405 | 0.1638 | 0.2243 | 41.43 KB | 1249 | 78.1 |
| before S1 | component | 128 | 60.464 | 0.4580 | 0.4060 | 310.13 KB | 9931 | 77.6 |
| before S1 | keyed | 1000 | 1115 | 7.8 | 6.9 | 3.86 MB | 203846 | 203.8 |
| before S1 | keyed | 10000 | 16749 | 332.7 | 771.1 | 38.72 MB | 2057848 | 205.8 |
| after S1 | component | 1 | 0.652 | 0.0128 | 0.0120 | 4.02 KB | 110 | 110.0 |
| after S1 | component | 16 | 5.521 | 0.2269 | 0.6689 | 25.66 KB | 1249 | 78.1 |
| after S1 | component | 128 | 40.765 | 0.8082 | 1.1591 | 188.48 KB | 9931 | 77.6 |
| after S1 | keyed | 1000 | 395.0 | 9.1 | 26.4 | 1.38 MB | 203846 | 203.8 |
| after S1 | keyed | 10000 | 14635 | 281.5 | 276.5 | 13.92 MB | 2057848 | 205.8 |
| after S2 | component | 1 | 1.589 | 0.0312 | 0.0383 | 6.47 KB | 127 | 127.0 |
| after S2 | component | 16 | 9.935 | 0.0919 | 0.0768 | 39.52 KB | 1266 | 79.1 |
| after S2 | component | 128 | 67.000 | 1.3151 | 2.0858 | 287.27 KB | 9948 | 77.7 |
| after S2 | keyed | 1000 | 578.2 | 15.04 | 44.12 | 2.11 MB | 203863 | 203.9 |
| after S2 | keyed | 10000 | 15504 | 43.05 | 33.61 | 21.27 MB | 2057865 | 205.8 |
| after S3 | component | 1 | 1.162 | 0.0118 | 0.0105 | 7.37 KB | 141 | 141.0 |
| after S3 | component | 16 | 7.298 | 0.0536 | 0.0447 | 44.62 KB | 1497 | 93.6 |
| after S3 | component | 128 | 51.519 | 1.0080 | 1.3107 | 326.34 KB | 11888 | 92.9 |
| after S3 | keyed | 1000 | 442.1 | 8.79 | 14.93 | 2.24 MB | 203863 | 203.9 |
| after S3 | keyed | 10000 | 15301 | 179.09 | 167.52 | 22.57 MB | 2057865 | 205.8 |
| after S4 | component | 1 | 1.199 | 0.0192 | 0.0180 | 7.42 KB | 141 | 141.0 |
| after S4 | component | 16 | 7.369 | 0.0551 | 0.0515 | 44.79 KB | 1497 | 93.6 |
| after S4 | component | 128 | 50.556 | 0.5061 | 0.4734 | 327.39 KB | 11888 | 92.9 |
| after S4 | keyed | 1000 | 435.1 | 8.64 | 19.86 | 2.25 MB | 203863 | 203.9 |
| after S4 | keyed | 10000 | 15434 | 66.75 | 62.44 | 22.65 MB | 2057865 | 205.8 |

Every row is cross-session, not a gate. The keyed 10000 rows are noisy: each render writes a 2 MB page, nearly every operation triggers a Gen2 collection (984 per 1000 after S1), and the before-S1 StdDev is 771 µs. After S1, the .NET escape scan is `SearchValues<char>` with `IndexOfAny`; the HTML bytes are identical to before S1.

After S2, the fixtures render through `Server.render`: each render creates a request graph, a request root and one owner per instance, and disposes them. The HTML grows by the 17 bytes of the root marker pair (`<!--[r-->` is 9 bytes, `<!--]-->` is 8). The fixed cost of the graph and root shows at one component row (0.652 to 1.589 µs); the per-instance owners show in the keyed rows (395 to 578 µs and 1.38 to 2.11 MB at 1000 rows).

After S3, a reflected property writes its attribute: each component row's `input` gains its `value` attribute (14 bytes at one row), and the keyed pages are unchanged. The after-S3 rows come from a later session than the after-S2 rows, with the positional ids of Task 6 in between, so the timing difference is not a regression measure.

After S4, a page carries arm hashes, boundary markers and text separators where its template has arms, boundaries and adjacent text slots. Neither fixture has any of these, so the HTML bytes equal the after-S3 bytes. The after-S4 rows are cross-session, not a gate. Their raw files are committed: `framework-performance/investigation/server-html/dotnet-after-s4/` (BenchmarkDotNet results and log) and `bytes-after-s4.json`. The log's `[Host]` line reads `DEBUG`; the benchmarked job is the Release build `DefaultJob` (`-c Release`, `Optimize=true`).

## First content under Node against Solid 2

Microseconds per render under Node: Ranvier's `ServerFixtures.renderComponentPage` and `renderKeyedPage` (Fable, Release, compiled without `RANVIER_VIEW_COUNTERS`) against the same markup rendered by Solid 2's `renderToString` (`solid-js`, `@solidjs/web` and `@solidjs/compiler` 2.0.0-rc.14; `generate: "ssr"`, `hydratable: true`). Ranvier and Solid 2 run in one session: each of three `node --expose-gc` processes runs 15 rounds after 3 warmups per fixture and row count, the mode order rotated per round and process, with a `gc()` before each sample. Before a fixture's rounds, each mode renders for at least 500 ms and at least 32 times. A sample repeats batches of 256, 64 or 16 renders at 1, 16 or 128 component rows, and 4 or 1 renders at 1000 or 10000 keyed rows, until it has run for at least 25 ms; the same rule holds for both modes, and each sample records its render count.

`NODE_ENV` is unset (recorded as `nodeEnv: null`). `solid-js` and `@solidjs/web` resolve under Node's `node` export condition to their default, non-development server builds (`dist/server.js`). Both sides drop event handlers from the HTML: Solid 2's SSR output compiles `onClick` away, and Ranvier's Omit policy omits listeners.

Command, from `examples/Fable.Ranvier.Playground`: `node benchmarks/frameworks/server-render.mjs --build [--out=<dir>]`. Output schema: `measuredAt`, `baseCommit`, `dirty`, `node`, `nodeEnv`, `cpu`, `platform`, `unit`, `verified` (per fixture, rows and mode, the count of `class="panel"` or `<tr` elements, equal to `rows`, and the UTF-8 bytes), `samples` (each with its `renders`) and `summaries`. The run recorded here is committed as `framework-performance/investigation/server-html/node-first-content.json`; base commit cf88e88 with the warm-up change to `server-render.mjs` uncommitted.

Machine: AMD Ryzen 9 9900X, Windows 11 25H2 (10.0.26200), Node v26.7.0.

| Fixture | Rows | Ranvier median (µs) | Ranvier p25–p75 | Ranvier per process | Solid 2 median (µs) | Solid 2 p25–p75 | Solid 2 per process | Ranvier / Solid 2 | Ranvier bytes per row | Solid 2 bytes per row |
|---|---|---|---|---|---|---|---|---|---|---|
| component | 1 | 6.32 | 5.68–6.73 | 6.14 / 6.29 / 6.56 | 6.45 | 6.35–6.59 | 6.53 / 6.52 / 6.37 | 0.98 | 141.0 | 122.0 |
| component | 16 | 33.93 | 32.93–36.21 | 33.64 / 33.97 / 34.38 | 11.72 | 11.00–12.38 | 11.55 / 11.37 / 11.99 | 2.90 | 93.6 | 99.9 |
| component | 128 | 227.1 | 211.1–235.2 | 225.8 / 224.3 / 230.5 | 46.73 | 46.36–47.26 | 46.93 / 46.69 / 46.73 | 4.86 | 92.9 | 102.1 |
| keyed | 1000 | 1311 | 951–1425 | 1311 / 1068 / 1318 | 208.7 | 203.4–212.8 | 208.5 / 205.9 / 212.0 | 6.28 | 203.9 | 213.8 |
| keyed | 10000 | 14670 | 12373–15350 | 14847 / 13534 / 14856 | 2231 | 2196–2276 | 2264 / 2215 / 2255 | 6.58 | 205.8 | 216.6 |

Each median is over 45 samples (3 processes × 15 rounds). At one component row the two render in the same time (ratio 0.98); from 16 rows on Solid 2 is faster, by 2.9 times at 16 component rows rising to 6.6 times at 10000 keyed rows.

The ratio rises mostly because Solid 2's fixed cost per render amortises. The marginal cost per row, from the medians: component rows (16 to 128) cost Ranvier 1.73 µs and Solid 2 0.31 µs (5.5 times); keyed rows (1000 to 10000) cost Ranvier 1.48 µs and Solid 2 0.23 µs (6.6 times). Subtracting the first 15 marginal rows from the one-row median leaves a fixed cost of about 4.5 µs for Ranvier and 6.1 µs for Solid 2. The ratio therefore approaches the per-row ratio as rows grow.

Solid 2's per-process medians lie within 6 % of each other at every fixture. Ranvier's lie within 7 % for the component pages, and spread 23 % at 1000 keyed rows (1068 against 1318 µs) and 10 % at 10000; its keyed samples are wide (p25–p75 of 951–1425 µs at 1000 rows). The ordering holds in every process.

Solid 2's output carries its hydration keys: an unquoted `_hk=` attribute on each template root (`<section _hk=0>`, `<div _hk=100 …>`, `<tr _hk=100>`). Ranvier's output carries its markers: the root pair `<!--[r-->` … `<!--]-->` and the list region pair `<!--[-->` … `<!--]-->`. Solid 2 pays its key on every row, Ranvier its markers once per page, so Solid 2's page is smaller at one component row (122 against 141 bytes) and larger from 16 rows on (9.9 bytes per keyed row at 1000 rows, 10.9 at 10000). Ranvier's bytes under Node equal its bytes on .NET.

Ranvier under Node renders 1000 keyed rows in 1311 µs, against 435 µs on .NET in the after-S4 row above; the two harnesses differ, so the comparison is cross-runtime, not a gate.

The `server-render` counter scenario (`bench/Ranvier.Counters` and `fable/Ranvier.Counters`) renders a 100-row keyed table per operation. On .NET it allocates 249,336 bytes per operation; under Fable its heap-used delta, measured in a region without garbage collection, is 307–308 KB per operation (1,535,000 bytes over 5 operations, 3,081,672 over 10). These counter figures predate the per-row changes below.

## Per-row server cost

Task 12b lowered the per-row cost of `Server.render` on both targets with the output unchanged: every run's `verified` entries equal the before run's, and the .NET `--bytes` output is identical before and after. The harnesses are the two above, unchanged: BenchmarkDotNet first content for .NET and `server-render.mjs` (Fable Release, without `RANVIER_VIEW_COUNTERS`) for Node.

### Changes

Each change is one commit on `poc/fable-ranvier-dom`, in this order, measured against the run before it:

1. A child `MountLifetime` is created without `graph.Run` and `runWithOwner` when the graph is ambient and the parent owner is current. On .NET, `graph.Run` allocated an activation and wrote an `AsyncLocal` per child. When it was measured, every server row took this path. Since change 6, only boundary attempts and client mounts take it.
2. The Fable `HtmlSink` builds one concatenated string; it replaced a list of parts joined at `ToString`. Under Fable `renderTo` writes the page as one segment.
3. A server instance whose plan has no inlined children gets its part array as a one-element literal. Under Fable, `Array.zeroCreate` compiles to `Array.prototype.fill` over a holey array.
4. Reverted. `MountLifetime` read its parent's phase by pattern match, because under Fable `=` on the union compiled to structural equality. It was measured before change 6, and since change 6 no server row constructs a `MountLifetime`, so the measured gain no longer applies.
5. A `RequestScope` builds one `Setup` per instance. At the time, every scope of a request also shared one `CreateId` closure. A later cleanup returned to one `identity.CreateId` closure per scope, because `ownSetup` replaces that field for every instance.
6. Each server instance runs under its own root owner, created under the enclosing instance's owner and adopted by the request root or the innermost boundary attempt, instead of under a `MountLifetime` of its own. A boundary attempt is still a `MountLifetime`, nested under the enclosing instance's owner.
7. An attribute slot is written from its precomputed ` name="` opening in three appends instead of one concatenated string.

Tried and dropped (no median moved beyond the spread): an inlined character test in the Fable escape scan, and writing a serialised property in five appends (on .NET 35.9 to 35.0 µs at 128 component rows, against an error of 0.49 µs; no Node gain).

### .NET

BenchmarkDotNet 0.15.8, Release, default job with `MemoryDiagnoser`; means in µs. "before" is base `e2ab66f` (this session's re-run of after S4) and "after" is `efa4f71`, which still includes the later-reverted change 4. Every row is cross-session against the tables above, not a gate. The rows after changes 1, 5, 6 and 7 are the runs after those changes. Changes 2–4 changed Fable-only or Fable-motivated code and were measured under Node only.

| Phase | Fixture | Rows | Mean (µs) | StdDev (µs) | Allocated |
|---|---|---|---|---|---|
| before 12b | component | 1 | 1.230 | 0.0537 | 7.42 KB |
| before 12b | component | 16 | 7.395 | 0.0793 | 44.79 KB |
| before 12b | component | 128 | 50.107 | 0.4206 | 327.39 KB |
| before 12b | keyed | 1000 | 424.1 | 12.28 | 2.25 MB |
| before 12b | keyed | 10000 | 15431 | 56.70 | 22.65 MB |
| after change 1 | component | 128 | 45.157 | 0.2779 | 299.38 KB |
| after change 1 | keyed | 1000 | 407.2 | 6.58 | 2.04 MB |
| after change 5 | component | 128 | 44.793 | 0.4575 | 295.45 KB |
| after change 5 | keyed | 1000 | 409.0 | 6.12 | 2.01 MB |
| after change 6 | component | 128 | 40.355 | 0.3564 | 267.91 KB |
| after change 6 | keyed | 1000 | 350.2 | 8.31 | 1.81 MB |
| after change 7 | component | 128 | 35.905 | 0.4612 | 238.91 KB |
| after change 7 | keyed | 1000 | 366.3 | 13.83 | 1.81 MB |
| after 12b | component | 1 | 0.994 | 0.0113 | 6.36 KB |
| after 12b | component | 16 | 5.490 | 0.0509 | 33.38 KB |
| after 12b | component | 128 | 38.731 | 0.5816 | 238.91 KB |
| after 12b | keyed | 1000 | 341.3 | 7.01 | 1.81 MB |
| after 12b | keyed | 10000 | 14605 | 367.8 | 18.2 MB |

The "after change 7" and "after 12b" runs measure the same code; their component means at 128 rows differ by 8 % (35.9 against 38.7 µs), the drift between runs on this machine. The allocations are deterministic: per component row they fall from about 2.5 KB to about 1.8 KB, and per keyed row from about 2.3 KB to about 1.8 KB, of which about 0.8 KB is the page's characters in the builder and the final string.

### Node, one session after the changes

Same harness, settings and machine as the Node section above. Ranvier and Solid 2 in this table run in one session (`efa4f71`, clean tree, change 4 still applied); the before table is a separate session (`e2ab66f`, clean tree) and comparisons between the two are cross-session, not a gate.

| Fixture | Rows | Ranvier median (µs) | Ranvier p25–p75 | Ranvier per process | Solid 2 median (µs) | Solid 2 p25–p75 | Solid 2 per process | Ranvier / Solid 2 | Ranvier bytes per row | Solid 2 bytes per row |
|---|---|---|---|---|---|---|---|---|---|---|
| component | 1 | 4.49 | 3.93–4.92 | 4.54 / 4.52 / 4.46 | 6.31 | 6.21–6.45 | 6.30 / 6.30 / 6.40 | 0.71 | 141.0 | 122.0 |
| component | 16 | 17.23 | 14.00–20.04 | 16.61 / 16.60 / 18.02 | 10.65 | 10.40–11.52 | 11.06 / 10.46 / 10.71 | 1.62 | 93.6 | 99.9 |
| component | 128 | 114.3 | 104.8–123.7 | 106.4 / 121.0 / 113.8 | 42.46 | 40.03–46.00 | 46.19 / 41.60 / 39.66 | 2.69 | 92.9 | 102.1 |
| keyed | 1000 | 464.2 | 297.9–571.8 | 464.2 / 350.2 / 538.0 | 203.3 | 196.8–205.5 | 205.3 / 204.4 / 191.7 | 2.28 | 203.9 | 213.8 |
| keyed | 10000 | 5855 | 3809–6764 | 5855 / 4220 / 6220 | 2188 | 2148–2251 | 2218 / 2191 / 2143 | 2.68 | 205.8 | 216.6 |

The before session (`e2ab66f`), Ranvier medians against Solid 2's in the same session: component 6.27 against 6.38 (0.98), 33.59 against 11.02 (3.05), 213.1 against 39.93 (5.34); keyed 1231 against 205.5 (5.99), 14082 against 2235 (6.30).

Marginal cost per row, from the medians; the before and after columns come from separate sessions, cross-session, not a gate:

| Rows | Ranvier before | Solid 2 before | Ratio before | Ranvier after | Solid 2 after | Ratio after |
|---|---|---|---|---|---|---|
| Component (16 to 128) | 1.60 µs | 0.26 µs | 6.2 | 0.87 µs | 0.28 µs | 3.1 |
| Keyed (1000 to 10000) | 1.43 µs | 0.23 µs | 6.3 | 0.60 µs | 0.22 µs | 2.7 |

Ranvier now renders one component row faster than Solid 2 (4.5 against 6.3 µs); from 16 rows on Solid 2 stays faster, by 1.6 to 2.7 times. Ranvier's keyed samples remain wide (p25–p75 of 298–572 µs at 1000 rows); its per-process medians spread from 350 to 538 µs at 1000 rows and from 4220 to 6220 µs at 10000. Ranvier under Node renders 1000 keyed rows in 464 µs, against 341 µs on .NET; cross-runtime, not a gate.

Raw files: `framework-performance/investigation/server-html/task-12b/` holds `node-before.json`, `node-after.json`, `bytes-before.json`, `bytes-after.json`, and `dotnet-before/` and `dotnet-after/` (BenchmarkDotNet results and log), with local paths replaced by `<repo>` and `<home>`.

## Mutation checks

Base `248c5ff`. Each mutant was applied alone, then `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj` (.NET) and `npm run test:dom` (jsdom, 954 tests) ran, and the file was restored with `git checkout`. Unmutated, both pass: 96 of 96 PASS lines and 954 of 954. The .NET runner stops at its first failing case, so its column names that case only; the jsdom column names every failing test. No mutant survived, so no test was added.

| Mutant | Edit | Killed by |
|---|---|---|
| Logical-sibling skip removed | `TemplateDom.logicalNext` returns the next sibling for every node | jsdom (4): `a region occupies two logical positions: the nodes and roots after a region's end marker are adopted`; `a row whose first roots are a region's markers hydrates in place`; `a row root's unclosed region raises at the root's position`; `an unclosed region raises at its logical path and an unclosed root re-renders its content`. .NET: none (the walk is DOM-only) |
| Arm hash check removed | `ArmRegion`'s `checkHash` never raises | jsdom (6): `two arms with equal shape and different static text are distinguished by hash`; `a different selected arm re-renders the region and reports once; Strict raises`; `a client arm the server plan rejects is a hash mismatch: the region re-renders and reports once; Strict raises`; `arms in hydrated rows adopt under an equal hash, and a different arm re-renders only its region; Strict raises`; `a failure of a recovered list's or arm's fresh mount raises from hydrate after the mismatch's one report and restores the host`; the `journey: a hydrated page takes events and two-way input, …` journey. .NET: none |
| Empty-text placeholder removed | `ServerWriter.writeText` writes `Html.escape false ""` in place of `<!--_-->` | jsdom (8): `server HTML carries arm, empty-text, separator and zero-root markers`; `empty text slots hydrate through their placeholder and update`; `empty text renders as recorded; void children and a tr in a table fail at plan compile`; `a structural mismatch names the path, the expected and the found node, and leaves the server HTML as it was`; `a rollback failure is reported with the mismatch and the root re-renders`; `an authored or expanded boundary in a view hole runs its content in an attempt and writes <!--[--> for every view`; `a template expanded through Program.ops writes the serialised properties the compiled plan writes`; `a template expanded through Program.ops writes raw content as the compiled plan does, under Reject`. .NET: `an authored or expanded boundary in a view hole runs its content in an attempt and writes <!--[--> for every view` |
| Row separator removed | `ServerWriter`'s list region writes no `<!--!-->` between text rows | jsdom (2): `server HTML carries arm, empty-text, separator and zero-root markers`; `text-adjacent row separators are removed during hydration`. .NET: `server HTML carries arm, empty-text, separator and zero-root markers` |
| Region-level mismatch catch removed | `RegionOf` installs no `site.Recover`, so every region mismatch fails the hydration | jsdom (8): `a list with fewer or more client items than server rows re-renders the region, reports once and reports a later failure`; `a different selected arm re-renders the region and reports once; Strict raises`; `arms in hydrated rows adopt under an equal hash, and a different arm re-renders only its region; Strict raises`; `rows in a hydrated arm adopt and reorder, and a row-count mismatch re-renders only the list; Strict raises`; `boundaries: a structural mismatch in a hydrating view re-renders that view and reports once; Strict raises`; `a mismatch inside a row's nested region re-renders only that region and reports once per region; Strict raises and restores the host`; `a failure of a recovered list's or arm's fresh mount raises from hydrate after the mismatch's one report and restores the host`; the `journey: a hydrated page takes events and two-way input, …` journey. .NET: none |
| Attempt-owner disposal removed | `RequestScope.Attempt` rethrows without disposing the attempt's lifetime | jsdom (2): `a boundary's abandoned content attempt runs its cleanups before the pending or error view is written`; `an authored or expanded boundary in a view hole runs its content in an attempt and writes <!--[--> for every view`. .NET: `a boundary's abandoned content attempt runs its cleanups before the pending or error view is written` |
| Escape fast path removed | the .NET `escapeText` builds a new string when nothing needs escaping | .NET: `escape returns the input string when nothing needs escaping`. jsdom: none (the edit is in the .NET branch) |
| `PlacementPass.Begin` no-op | `Begin` leaves the container and the placed flag of the previous pass | jsdom (86), each failing with "A pass placement container was requested after its Place" or with a pass's hooks missing; among them `template synchronous: a row added by a list pass runs its hook after the pass lays it out, and a batch that adds and removes a row creates none`, `template queued: an arm whose setup selects another arm is replaced before its hooks run, and each placed arm's hook runs once, in selection order`, `each sets up a replacing row before the replaced row's cleanup`, `CreateId is equal on server and hydrated client, and rows created after hydration continue their region's counter`. .NET: none |

The plan expected `nested list regions hydrate and update` to kill the first mutant; that test passes under it, and the four tests above kill it. The region-level catch now lives in `RegionSite.Settle`, reached through the `Recover` that `RegionOf` installs, so mutant 5 removes that installation.

## Hydration in Chromium

Ranvier's `hydrate` of its own `Server.render` output (Omit policy, `{ IdPrefix = "r"; Strict = true }`) against Ranvier's client mount of the same page and against Solid 2's `hydrate` of its own `renderToString` output. The fixtures are `ServerFixtures.componentPage` (1, 16 and 128 rows) and `ServerFixtures.keyedPage` (1000 and 10000 rows) through `HydrationPerf`, and the same trees in Solid 2 (`server-solid.jsx`, compiled `generate: "ssr", hydratable: true` in Node for the server HTML and `generate: "dom", hydratable: true` in the bundle). Each Ranvier call creates a new `Graph`, and its disposal disposes the graph. The mount side runs `Mounting.mount` with `Program.run (Component.render page rows)`, while `hydrate` calls `Backend.Template` directly; the extra `Program.run` call is one indirection per mount and is counted in mount's time.

Harness: `hydration-bench.mjs` follows `boundary-reveal.mjs`. Three Chromium processes each verify every fixture and mode, then run 60 samples per mode after 3 warmups per mode, for each fixture and row count (`--rounds`; the cyclic rotation defaults to Task 20's 15). The default rotation, `balanced`, orders each process's samples by a de Bruijn sequence of the three modes, so in every 9 samples each mode follows each mode, itself included, once; each process relabels the modes by its index. Each sample records the mode sampled before it, and `byPrevious` gives each mode's median per predecessor. `--rotation=cyclic` keeps Task 20's order, which runs the three modes in their cyclic order each round, starting one mode later per round and per process. Before each sample the page fills its hosts (attached to the body) with the server HTML, parsed as a document parse places it (`Range.createContextualFragment`), and the runner forces a collection (`HeapProfiler.collectGarbage`); the fill is outside the timed region, and `ranvier-mount` hosts stay empty. A sample times 64, 32 or 8 calls at 1, 16 or 128 component rows and 2 or 1 calls at 1000 or 10000 keyed rows, one host per call, and records milliseconds per call. The first-click timing is one call followed by a click on the first row's button (component) or link (keyed) and the commit of its update (`flush()` for Solid 2), in the same order as the samples, with a collection before each. Retained heap is the GC-stabilised JavaScript heap of 32 live hydrated (or mounted) hosts, their server DOM included, divided by their rows.

Verification, before timing in each process: every host holds `rows` rows with the first row's content; after hydration every element and every text node is the server's (Ranvier and Solid 2); a Ranvier host is empty after disposal. All 45 entries passed in each run. Solid 2 hydrates once per document: a macrotask after a hydration ends it sets `_$HY.done`, and a later `hydrate` renders fresh over the server nodes, which the verification caught; the first hydration's event replay also sets `_$HY.completed` and `_$HY.events` to null. `hydration-solid.jsx` restores the bootstrap's initial values (`done` false, `completed` a new `WeakSet`, `events` an empty array) before each call, so each call is a page's first hydration.

Command, from `examples/Fable.Ranvier.Playground`, under the benchmark lock: `node benchmarks/frameworks/hydration-bench.mjs --build`. The balanced run recorded here is that command (Fable Release without `RANVIER_VIEW_COUNTERS`, then the Vite bundle into `generated-hydration-dist`), measured 2026-10-20 at 19:50–20:02 under the benchmark and browser locks with 60 measured samples per mode and process, after the per-call hydration fix below. Output: `framework-performance/investigation/server-html/hydration.json`, base commit `6530d77` (the fix is `6af5b43`, the balanced rotation `e7b2e92`, the matched gate `6cf757f`) with the 60-sample default uncommitted (listed in `dirty`). The cyclic run (`hydration-cyclic.json`, 19:05–19:10, `--rotation=cyclic --label=hydration-cyclic`, 15 samples per mode and process) and the 15-sample balanced tables below are history; the 15-sample balanced run's file was replaced by the 60-sample run. The first run, at `42d4c30` with the Task 20 benchmark files uncommitted, is the "before" column of the comparison below. Machine: AMD Ryzen 9 9900X, Windows 11 25H2, Node v26.7.0, Chromium 153.0.8010.12.

#### The gate: balanced rotation, 60 samples per (mode, predecessor)

Medians over 180 samples per mode (3 processes × 60), with per-process medians and the sample range; first click is the median over the same counts, heap over 3 processes.

| Fixture | Rows | Ranvier hydrate (µs) | Ranvier mount (µs) | Solid 2 hydrate (µs) | Pooled hydrate / mount | First click: hydrate / mount / Solid 2 (µs) | Retained bytes per row: hydrate / mount / Solid 2 |
|---|---|---|---|---|---|---|---|
| component | 1 | 53.1 (53.4 / 45.2 / 63.7; 19.1–118.8) | 52.3 (51.8 / 47.7 / 65.2; 22.0–108.4) | 17.1 (17.0 / 15.5 / 20.9; 13.4–61.8) | 1.015 | 525 / 535 / 230 | 10442 / 10733 / 3909 |
| component | 16 | 172.5 (167.3 / 161.6 / 252.2; 85.9–341.3) | 183.4 (173.1 / 171.3 / 269.4; 111.3–366.9) | 83.4 (78.1 / 80.0 / 126.4; 67.8–178.1) | 0.940 | 765 / 810 / 315 | 1770 / 1801 / 1617 |
| component | 128 | 914 (891 / 817 / 1586; 643–2065) | 1008 (981 / 926 / 1869; 717–2759) | 608 (606 / 456 / 1031; 418–1396) | 0.907 | 1800 / 1925 / 870 | 1199 / 1197 / 1403 |
| keyed | 1000 | 4917 (4515 / 4670 / 8885; 3882–11022) | 5143 (4950 / 4800 / 10245; 3892–12117) | 4880 (4225 / 4363 / 9013; 3603–10738) | 0.956 | 6460 / 7005 / 4835 | 807 / 806 / 1130 |
| keyed | 10000 | 31630 (30320 / 31610 / 32670; 27950–86265) | 50325 (42845 / 50935 / 54310; 40015–95935) | 55960 (53775 / 55175 / 58790; 39630–106080) | 0.629 | 38650 / 51715 / 44700 | 811 / 809 / 1095 |

The third process ran 1.5 to 2 times slower than the other two from the 16-row fixture on, in all three modes alike (machine load). Each matched cell mixes the three processes the same way for both modes, so the ratios are taken as measured.

Predecessor-matched hydrate / mount, each cell the ratio of the median of 60 hydrate samples to the median of 60 mount samples (µs in parentheses, hydrate / mount):

| Fixture | Rows | After hydrate | After mount | After Solid 2 | Max: gate |
|---|---|---|---|---|---|
| component | 1 | 0.890 (39.1 / 44.0) | 1.006 (52.7 / 52.3) | 0.965 (58.2 / 60.3) | **1.006, fails** |
| component | 16 | 0.932 (168.7 / 181.1) | 0.962 (170.8 / 177.5) | 0.881 (177.3 / 201.2) | 0.962, holds |
| component | 128 | 0.866 (845 / 976) | 0.904 (923 / 1021) | 0.916 (949 / 1036) | 0.916, holds |
| keyed | 1000 | 0.951 (4900 / 5150) | 0.970 (4925 / 5075) | 0.932 (4963 / 5325) | 0.970, holds |
| keyed | 10000 | 0.607 (32145 / 52990) | 0.614 (31505 / 51325) | 0.719 (31630 / 43980) | 0.719, holds |

**With 60 samples per cell, the spec's target holds from 16 component rows on and fails at 1 component row by 0.6 %.** After a mount sample, hydrate's median is 52.7 µs against mount's 52.3 µs; after a hydrate or a Solid 2 sample, hydrate is below mount (0.890, 0.965). Other ratios, not gated: Ranvier's hydrate against Solid 2's is 3.11 at 1 component row, 2.07 at 16, 1.50 at 128, 1.01 at 1000 keyed rows and 0.57 at 10000.

#### History: balanced rotation, 15 samples per (mode, predecessor)

The 15-sample balanced run of 18:59–19:04 (base `f37f0ae`); the 60-sample run replaced its file. Medians over 45 samples (3 processes × 15 per mode), with per-process medians and the sample range; first click and heap are medians over the same counts (heap: 3 processes).

| Fixture | Rows | Ranvier hydrate (µs) | Ranvier mount (µs) | Solid 2 hydrate (µs) | Pooled hydrate / mount | First click: hydrate / mount / Solid 2 (µs) | Retained bytes per row: hydrate / mount / Solid 2 |
|---|---|---|---|---|---|---|---|
| component | 1 | 51.2 (60.5 / 49.7 / 49.9; 21.4–69.3) | 50.9 (51.3 / 50.5 / 51.0; 23.5–75.0) | 16.6 (16.0 / 16.6 / 17.0; 14.0–23.7) | 1.008 | 480 / 515 / 230 | 10881 / 1940 / 1576 |
| component | 16 | 167.2 (179.8 / 167.2 / 154.7; 84.5–202.3) | 180.0 (182.8 / 179.7 / 184.2; 111.2–245.8) | 82.2 (84.1 / 82.7 / 78.0; 68.9–105.5) | 0.929 | 800 / 790 / 325 | 1810 / 1692 / 124 |
| component | 128 | 911 (924 / 911 / 881; 674–1111) | 1061 (1004 / 1105 / 1066; 721–1189) | 609 (616 / 609 / 599; 445–774) | 0.859 | 1545 / 1870 / 850 | 1197 / 1196 / 1402 |
| keyed | 1000 | 4743 (4817 / 4623 / 4873; 3935–6750) | 4960 (5002 / 4850 / 4907; 4655–7003) | 4832 (4830 / 4665 / 4907; 3608–5510) | 0.956 | 6430 / 6980 / 4705 | 815 / 806 / 1131 |
| keyed | 10000 | 30120 (30265 / 30770 / 29470; 27925–36380) | 49060 (42745 / 49630 / 48970; 40575–55545) | 52695 (54255 / 51935 / 51820; 36515–65425) | 0.614 | 36115 / 49700 / 40195 | 811 / 811 / 1099 |

The gate statistic is the predecessor-matched ratio: hydrate's median over mount's median among samples that follow the same mode (`matched` in `hydrateVersusMount`), and the spec's target holds when the largest of the three is at most 1 (`maxMatchedRatio`, `atOrBelowMount`). The pooled ratio above is reported and not gated, because each mode's pooled samples split into a fast group after a Ranvier sample and a slow group after Solid 2. Each matched cell is a median of 15 samples (5 per process).

| Fixture | Rows | After hydrate | After mount | After Solid 2 | Max: gate | Round 2 run (18:27): after hydrate / mount / Solid 2, max | Cyclic run: after Solid 2 (the only shared predecessor) |
|---|---|---|---|---|---|---|---|
| component | 1 | 0.853 | 1.474 | 0.995 | **1.474, fails** | 0.840 / 1.008 / 0.968, 1.008 | 1.003 |
| component | 16 | 1.007 | 1.192 | 0.918 | **1.192, fails** | 1.108 / 1.065 / 0.877, 1.108 | 0.892 |
| component | 128 | 1.031 | 0.833 | 0.872 | **1.031, fails** | 0.892 / 0.819 / 0.828, 0.892 | 0.867 |
| keyed | 1000 | 0.942 | 0.957 | 0.935 | 0.957, holds | 0.909 / 0.973 / 0.979, 0.979 | 0.966 |
| keyed | 10000 | 0.614 | 0.605 | 0.733 | 0.733, holds | 0.596 / 0.599 / 0.715, 0.715 | 0.714 |

**With 15 samples per cell the matched gate failed at 1, 16 and 128 component rows in this run, and held at both keyed sizes.** The library is the same in both balanced runs, and so is the bundle. The same cell moves by more than its distance from 1 between runs: at 1 row after mount 1.008 and 1.474, at 16 rows after hydrate 1.108 and 1.007, at 128 rows after hydrate 0.892 and 1.031. Two further balanced repeats (outside the repository) gave maxima of 1.319 and 0.980 at 1 row and 1.281 and 1.254 at 16. A dedicated predecessor-matched run with four times the samples per cell (one ordered pair at a time, 20 rounds per pair, 3 processes) gives hydrate / mount at 16 rows of 0.80, 0.86 and 0.85 after hydrate, 0.90, 0.86 and 0.86 after mount, and 0.82, 0.86 and 0.83 after Solid 2. A Chromium CPU profile of 300 warm 32-call samples at 16 rows (no collection between them) gives 30.5 µs per hydrate call against 56.4 µs per mount call. Per row, hydrate spends its time in the server-node walk (`collectAdopted` 9.7 %, `adoptTarget` 8.1 %, `adoptRoots` 5.5 %, `matchingEnd` 4.9 %) and the value comparisons (`getAttribute` 4.5 %, `writeHydrating` 2.9 %). Mount spends its time in `cloneNode` (17.7 %), the insertion (`insertBefore` 10.8 %, `appendChild` 8.1 %) and `setAttribute` (5.6 %). The profile shows no per-row work in hydrate outside the walk and the comparisons.

The pooled ratios and Task 20's cyclic rotation, against the first run (each column its own session: the ratios are same-session, the raw times are not compared across columns):

| Fixture | Rows | Cyclic at `0757132` (first run): hydrate / mount (µs), pooled ratio | Cyclic, fixed: hydrate / mount (µs), pooled ratio | Balanced, fixed: hydrate / mount (µs), pooled ratio |
|---|---|---|---|---|
| component | 1 | 73.8 / 62.3, 1.18 | 54.4 / 34.1, 1.596 | 51.2 / 50.9, 1.008 |
| component | 16 | 196.7 / 195.8, 1.005 | 173.9 / 188.1, 0.924 | 167.2 / 180.0, 0.929 |
| component | 128 | 1055 / 1106, 0.95 | 909 / 1004, 0.906 | 911 / 1061, 0.859 |
| keyed | 1000 | 5613 / 5773, 0.97 | 4770 / 4950, 0.964 | 4743 / 4960, 0.956 |
| keyed | 10000 | 37395 / 51410, 0.73 | 30060 / 50390, 0.597 | 30120 / 49060, 0.614 |

The two rotations' pooled ratios differ by more than the run-to-run spread only at 1 component row: a Ranvier hydrate sample makes the next Ranvier sample of either mode faster, and the cyclic rotation puts mount after hydrate in two rounds of three but never hydrate after hydrate (below).

Other ratios, reported and not gated (balanced run): Ranvier's hydrate against Solid 2's is 3.08 at 1 component row, 2.03 at 16, 1.50 at 128, 0.98 at 1000 keyed rows and 0.57 at 10000. At 1 component row the first click is 0.48 ms for hydrate, 0.52 ms for mount and 0.23 ms for Solid 2. Retained heap per row is within 1.1 % for hydrate and mount from 128 rows on (815 against 806 bytes at 1000 keyed rows). At one component row the per-host cost dominates (about 11 KB per hydrated host). The 1- and 16-row heaps have process outliers in every mode: mount 12076 / 1218 / 1940 bytes at 1 row, Solid 2 1576 / 3400 / 460 at 1 row and 124 / 59 / 1616 at 16.

### Lenient against Strict, and a re-run after the rollback-log commit

Not a gate. `hydration-bench.mjs` now ends each process with a lenient block: Strict hydration and lenient hydration (`Hydration.HydrationOptions.defaults`, `Strict = false`) of the same server HTML, 30 measured pairs per fixture after 3 warm-up pairs, the pair's order alternating by round (`lenientVersusStrict` in the output). One run, 2026-10-20 at 21:36–21:47 under the benchmark and browser locks, `--build`, base `7d0078c` with the benchmark files uncommitted (`dirty`), output outside the repository; the gate's file above is unchanged.

| Fixture | Rows | Strict (µs) | Lenient (µs) | Lenient / Strict (per process) |
|---|---|---|---|---|
| component | 16 | 146.9 | 149.2 | 1.016 (1.011 / 1.003 / 1.014) |
| keyed | 1000 | 4645 | 4593 | 0.989 (1.012 / 0.977 / 0.998) |

Lenient hydration is within 2 % of Strict at both sizes. The same run's predecessor-matched gate, after `ef5ec56` (a hydrated root discards its rollback log once `Run` succeeds):

| Fixture | Rows | After hydrate | After mount | After Solid 2 | Max: gate |
|---|---|---|---|---|---|
| component | 1 | 0.991 | 1.032 | 1.003 | **1.032, fails** |
| component | 16 | 0.935 | 0.979 | 0.897 | 0.979, holds |
| component | 128 | 0.893 | 0.895 | 0.926 | 0.926, holds |
| keyed | 1000 | 0.968 | 0.954 | 0.944 | 0.968, holds |
| keyed | 10000 | 0.597 | 0.610 | 0.708 | 0.708, holds |

The gate's verdict per fixture is the same as in the run above: it fails at 1 component row and holds from 16 on.

### The per-call cost of hydrate

Phase timers on the 1-row page (temporary instrumentation, removed), in the benchmark's sampling, put the fixed cost in the head guard and the root setup: `DocumentHead.keep` took about 3.7 µs per call before the fix, as much as 11 µs after the forced collection the benchmark runs before each sample, because it copied every head child's attribute list into arrays on every call. The fix:

- `DocumentHead.keep` records the head's edits with a `MutationObserver` (pooled, observing from the call to the end of `hydrateWith`) and copies only the marked elements' attribute lists, so its cost is set by the edits instead of the head's size. `Restore` applies the inverse of each recorded edit, newest first, and puts the marked elements' attributes back in order. A document without a window keeps the copy.
- `removeUnadopted` returns at once when the head holds no marked element.
- Adoption allocates less per instance: the root marker search, the root's mount state and its fresh mount are functions instead of closures; root and target checks raise directly; an element's name matches by `localName` when neither side has a prefix; and a target's mismatch path is replayed from the walk only when a mismatch is raised.

After the fix the same timers give, per 1-row call: keep 1.2 µs, release 0.5 µs, the unadopted check 0.3 µs, and the root setup before the build 1.3 µs; the build and its commit inside `Run` take 0.6 µs less for hydrate than for mount.

### Order dependence at small sizes

Not a gate. Each sample follows the previous mode's sample in the same page, and the sample just before it changes its time. Medians (µs) by the mode sampled just before, with sample counts, from `byPrevious` in the 15-sample balanced run and the cyclic run (the 60-sample medians are in the gate table above):

| Rotation | Rows | Hydrate after hydrate | Hydrate after mount | Hydrate after Solid 2 | Mount after hydrate | Mount after mount | Mount after Solid 2 |
|---|---|---|---|---|---|---|---|
| balanced | 1 | 32.6 (15) | 54.9 (15) | 57.0 (15) | 38.2 (15) | 37.3 (15) | 57.3 (15) |
| balanced | 16 | 162.2 (15) | 160.3 (15) | 176.3 (15) | 161.1 (15) | 134.5 (15) | 192.0 (15) |
| balanced | 128 | 886.3 (15) | 921.9 (15) | 924.4 (15) | 859.4 (15) | 1106.3 (15) | 1060.6 (15) |
| cyclic | 1 | – | 52.0 (15) | 55.2 (30) | 28.6 (30) | – | 55.1 (15) |
| cyclic | 16 | – | 170.5 (15) | 175.3 (30) | 185.8 (30) | – | 196.6 (15) |
| cyclic | 128 | – | 906.3 (15) | 922.5 (30) | 977.5 (30) | – | 1063.7 (15) |

The cyclic order `ranvier-hydrate`, `ranvier-mount`, `solid-hydrate` puts mount after hydrate in two rounds of three and hydrate after mount in one, never after itself; the balanced rotation gives every ordered pair 5 samples per process. Under it, each mode's 1-row samples split into a fast group and a slow group; the fast group mostly follows a Ranvier sample (in this run, hydrate after mount is in the slow group), and the pooled median falls between the groups, which is why the pooled 1- and 16-row ratios move between runs. A dedicated run (one process, 15 rounds per ordered pair, the benchmark's fill and forced collection before every sample) gives at 1 component row, for the fixed build: mount after hydrate 27.6 and 31.1, mount after mount 39.7 and 38.4, mount after Solid 2 42.3 and 41.8; hydrate after hydrate 26.4 and 36.3, after mount 39.8 and 39.9, after Solid 2 41.3 and 39.9. At 16 rows: mount after hydrate 111.2, after mount 173.6, after Solid 2 193.7; hydrate after hydrate 74.8, after mount 151.1, after Solid 2 169.2. A Ranvier hydrate sample makes the next Ranvier sample of either mode faster; a mount or Solid 2 sample does not. Filling the hosts without hydrating them does not, and neither V8's memory reducer, incremental marking nor the initial heap size changes it (`--no-memory-reducer`, `--no-incremental-marking`, `--initial-old-space-size=512`). With a Solid 2 or mount predecessor the two modes are within 4 % of each other at 1 row (after Solid 2, hydrate 41.3 and 39.9 against mount 42.3 and 41.8; after mount, hydrate 39.8 and 39.9 against mount 39.7 and 38.4); after a hydrate sample, hydrate takes 26.4 and 36.3 against mount's 27.6 and 31.1. Hydrate is below mount at 16 rows after every predecessor. At `0757132` the same run gives at 1 row: mount after hydrate 40.4 and 31.4, after mount 39.5 and 41.6; hydrate after hydrate 35.4 and 36.6, after mount 44.1 and 42.9. The mechanism is not identified.

### Keyed mount across the positional-id commits

Not a gate, cross-session: the existing keyed `create1k` and `create10k` operations (`keyed-rows.mjs --ops=create1k,create10k --modes=ranvier,solid --no-alloc`, 3 processes × 15 rounds) at `ae691d8` (before positional ids), `ab9e924` (positional id windows, Task 6), `144fb2c` (the `Setup` guard's two allocations per instance, Task 6 fix) and `42d4c30` (this task's base), each built in a temporary worktree in its own session, then `ae691d8` again. The harness files are identical at all four commits. Medians in ms, per-process medians in parentheses; Ranvier / Solid 2 is the ratio of the two medians in the same session.

| Commit | create1k Ranvier | create1k Solid 2 | Ranvier / Solid 2 | create10k Ranvier | create10k Solid 2 | Ranvier / Solid 2 |
|---|---|---|---|---|---|---|
| ae691d8 | 5.78 (5.86 / 5.78 / 5.68) | 4.00 | 1.45 | 50.80 (51.43 / 50.68 / 50.81) | 37.05 | 1.37 |
| ab9e924 | 6.60 (7.34 / 6.45 / 6.53) | 4.43 | 1.49 | 53.89 (53.28 / 53.82 / 54.26) | 38.21 | 1.41 |
| 144fb2c | 6.95 (7.38 / 6.80 / 6.69) | 4.28 | 1.62 | 54.35 (54.93 / 53.42 / 53.95) | 37.91 | 1.43 |
| 42d4c30 | 8.18 (8.65 / 8.18 / 7.98) | 4.69 | 1.74 | 59.05 (60.77 / 56.05 / 63.43) | 41.53 | 1.42 |
| ae691d8, again | 9.42 (14.55 / 9.42 / 7.67) | 6.69 | 1.41 | 64.93 (77.31 / 59.76 / 55.51) | 43.94 | 1.48 |

The second `ae691d8` session ran 63 % slower than the first at create1k and 28 % slower at create10k, and Solid 2 moved with it, so the raw medians across sessions do not separate the commits. Within each session, Ranvier / Solid 2 at create1k rises from 1.45 (`ae691d8`) to 1.49 (`ab9e924`), 1.62 (`144fb2c`) and 1.74 (`42d4c30`), against 1.41 for the second `ae691d8` session; at create10k it stays between 1.37 and 1.48 at every commit. Raw files: `framework-performance/investigation/server-html/keyed-mount/`.

## Status

Server HTML and hydration (plan Tasks 1–20) is complete on `poc/fable-ranvier-dom`:

- Server: a portable project without browser dependencies; one escaping writer with a fast path; `Server.render` and `renderPage` with a request graph, request-owned instances and request-local scopes; positional ids through `Setup.CreateId`; portable property serialisation with a browser-only Reject/Omit policy; forms and the leading line feed; boundary attempts with `[c`/`[p`/`[e` views and `ServerPendingException`; arm hashes, placeholders, separators and zero-root markers; rejection of markup the HTML parser restructures; state transfer through `Server.stateScript`.
- Hydration: adoption of a template's server nodes, root and region mismatch policies with `Strict`, list rows, arms by structural hash, view holes re-rendered, boundaries (pending server content shown as an inert copy and revealed in place), head adoption, identity continued after hydration, Chromium normalisation checks, the journeys, and the mutation checks above.
- Measurement: first content on .NET and under Node against Solid 2, and hydration in Chromium (this section).

Open items:

1. Hydrate against mount at 1 component row, a measured finding. The gate is the largest predecessor-matched hydrate / mount ratio under the balanced rotation, with 60 samples per (mode, predecessor). It holds from 16 component rows on (0.962 at 16, 0.916 at 128, 0.970 and 0.719 at the keyed sizes) and fails at 1 row by 0.6 %: 1.006 after a mount sample (52.7 against 52.3 µs), against 0.890 after hydrate and 0.965 after Solid 2. With 15 samples per cell, the earlier gate runs failed at 1, 16 and 128 rows; their cells moved between runs of the same bundle by more than their distance from 1 (the 16-row maximum was 1.108, 1.281, 1.254 and 1.192 in four runs). Per row in a warm profile, hydrate's time is the server-node walk and the value comparisons, and mount's is the clone, the insertion and the attribute writes; at 16 rows a warm hydrate call takes 30.5 µs against 56.4 µs for mount. Tried in the perf round: recording the head's edits instead of copying the head, the unadopted-element guard, and fewer allocations per adopted instance (all kept, `6af5b43`). No per-row work outside the walk was found to cut. The 1-row pooled ratio under Task 20's cyclic rotation (1.596) reflects that order putting mount after hydrate in two rounds of three; why a Ranvier sample speeds up the next is not identified.
2. O1, disposed-mount retention: a disposed mount stays reachable while its graph, or a settled source it read, is alive. This holds for client mounts, ready hydration and, since design 2a, the detached nodes of pending `[c` content. It predates 2a (present at `2a2d43b`) and needs its own triage.
3. Server render per row: under Node, Solid 2 renders 1.6 to 2.7 times faster from 16 rows on (after Task 12b, above). F6, the server-only owner follow-up parked since Task 5 (the `RunRelease` closures, the `TeardownWindow` lists and the `Nest` allocations of a request's owners, kept because the brief requires `MountLifetime.Child`), is a candidate cause; it is still parked, to revisit with this item.
4. Keyed create against Solid 2 at 1000 rows: the in-session ratio rose from 1.45 to 1.74 between `ae691d8` and `42d4c30` (above); not a gate, and not attributed to a commit.
5. Resolved by the fix for #126 (#129): a spent slot of the scheduler queue and a region's row builder no longer keep a recovered region's server rows reachable.
6. Test infrastructure, parked and still open:
   - mixed line endings in the repository (Task 1 Minor 4), pre-existing;
   - `SetMinThreads` hardening for `concurrentTeardown` (Task 5 re-review N2), optional;
   - the Chromium case "Fable watch replaces the app" takes 15.8 s against a 20 s budget and times out under load from concurrent worktrees (Task 5b M2), as does "microtask mode batches a burst"; the budget is unchanged;
   - an unterminated trailing `<!--` in raw HTML can join a following `[` marker; raw HTML is trusted verbatim, so this stays without a fix.
