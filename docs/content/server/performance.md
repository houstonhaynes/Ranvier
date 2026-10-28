---
title: Performance
---

These fixture measurements come from one AMD Ryzen 9 9900X / Windows 11 machine and one revision.
Use them to identify costs, then rerun against your application. The
[research note](https://github.com/ranvier-org/Ranvier/blob/master/docs/research/2026-10-19-server-html-hydration.md)
and [raw results](https://github.com/ranvier-org/Ranvier/tree/master/docs/research/framework-performance/investigation/server-html)
record the method and data.

## The fixtures

| Fixture | Content |
| --- | --- |
| `component` | 1, 16 or 128 rows, each a panel with an id, a class, a title read from a signal, an input whose value reads the same signal, and a button with a click counter. |
| `keyed` | A table of 1,000 or 10,000 keyed rows with an id cell, a label link and a remove link. |

Both are in [`ServerFixtures.fs`](https://github.com/ranvier-org/Ranvier/blob/master/experiments/Ranvier.View.Checks/ServerFixtures.fs).

## Server render on .NET

BenchmarkDotNet, Release, one `Server.render` of the page per operation:

| Fixture | Rows | Mean | Allocated |
| --- | --- | --- | --- |
| component | 1 | 0.99 µs | 6.4 KB |
| component | 16 | 5.5 µs | 33 KB |
| component | 128 | 38.7 µs | 239 KB |
| keyed | 1,000 | 341 µs | 1.8 MB |
| keyed | 10,000 | 14.6 ms | 18.2 MB |

- A render's fixed cost is its graph and request root: about 1 µs.
- Each row costs about 0.3 µs and 1.8 KB, of which about 0.8 KB is the page's own characters. The rest
  is the row's owner and setup.
- At 10,000 rows nearly every render triggers a Gen2 collection; the 2 MB page dominates.

## Server render under Node

The same fixtures compiled with Fable in Release, against Solid 2 (2.0.0-rc.14) `renderToString` of
the same markup, medians in one session:

| Fixture | Rows | Ranvier | Solid 2 | Ranvier / Solid 2 |
| --- | --- | --- | --- | --- |
| component | 1 | 4.5 µs | 6.3 µs | 0.71 |
| component | 16 | 17.2 µs | 10.7 µs | 1.62 |
| component | 128 | 114 µs | 42.5 µs | 2.69 |
| keyed | 1,000 | 464 µs | 203 µs | 2.28 |
| keyed | 10,000 | 5.9 ms | 2.2 ms | 2.68 |

Ranvier renders a one-component page faster, and Solid 2 renders long pages 1.6 to 2.7 times faster:
Ranvier pays for a per-instance owner on every row, about 0.6 to 0.9 µs per row under Node.

**Page size.** Ranvier writes its markers once per page and once per region, where Solid 2 writes a
hydration key on every row. Ranvier's page is larger at one component (141 against 122 bytes) and
smaller from 16 rows on (about 204 against 214 bytes per keyed row).

## Hydration in Chromium

`Hydration.hydrate` of the server's own HTML against `Mounting.mount` of the same page into an empty
host, and against Solid 2's `hydrate`, medians over 180 samples:

| Fixture | Rows | Hydrate | Mount | Hydrate / mount | Solid 2 hydrate |
| --- | --- | --- | --- | --- | --- |
| component | 1 | 53 µs | 52 µs | 1.02 | 17 µs |
| component | 16 | 173 µs | 183 µs | 0.94 | 83 µs |
| component | 128 | 914 µs | 1.0 ms | 0.91 | 608 µs |
| keyed | 1,000 | 4.9 ms | 5.1 ms | 0.96 | 4.9 ms |
| keyed | 10,000 | 31.6 ms | 50.3 ms | 0.63 | 56.0 ms |

- Hydrating costs the same as mounting at one component and less from 16 rows on: a hydrated row walks
  the server's nodes and compares values, where a mounted row clones, inserts and writes attributes.
- Retained heap per row is within 1.1 % for hydrate and mount from 128 rows on (about 810 bytes per
  keyed row).
- Lenient hydration of matching HTML is within 2 % of `Strict`.

## Sugar

The [sugar](../view/sugar.md) mounts the same owners, effects, listeners and DOM writes as the explicit
form for every style except `Component.ofClass`, which builds one two-element array per instance setup
under Fable. Unused sugar adds 0 bytes to a bundle; the bundle cost of each style is in the view sugar
README's [Costs](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/README.md#costs)
section.

## Guidance

- **Per-row cost is per-instance cost.** Every row, arm and child component is an instance with an
  owner, a setup and a render effect. A row that needs no state of its own still pays for one; keep row
  templates flat and share definitions.
- **One render effect per instance.** All of an instance's bindings re-run together when any of their
  dependencies changes. A binding that changes far more often than its siblings is a candidate for
  `t.isolated` (see [Templates and bindings](../view/templates.md#how-bindings-re-run)).
- **Hydrate long pages.** Hydration took 0.91 of a mount's time at 128 component rows and 0.63 at
  10,000 keyed rows, and the same time at one component.
- **Settle data before rendering** when the server should send content rather than a pending view.

## Running the benchmarks

From the repository root, for the .NET server render:

```bash
dotnet run -c Release --project experiments/Ranvier.View.Checks/ServerBench/ServerBench.fsproj -- --filter '*'
dotnet run -c Release --project experiments/Ranvier.View.Checks/ServerBench/ServerBench.fsproj -- --bytes
```

`--bytes` prints one JSON object per fixture with its page size in bytes and bytes per row. From
`examples/Fable.Ranvier.Playground`, after `npm ci`:

```bash
node benchmarks/frameworks/server-render.mjs --build
node benchmarks/frameworks/hydration-bench.mjs --build
```

Run one benchmark at a time on an otherwise idle machine; the Chromium runs need Playwright's Chromium
(`npx playwright install chromium`).
