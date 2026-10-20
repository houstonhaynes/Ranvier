# Server HTML and hydration implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render a template component to hydratable HTML on .NET and synchronously under Fable, then attach a client mount to that HTML by adopting its nodes, with an explicit mismatch policy.

**Architecture:** The portable files move into a new `Ranvier.View.Portable` project, which gains a compiled server plan per definition (`ServerPlan`), one plan interpreter (`ServerWriter`) used by both `Html.render` and the new `Server.render`, request-local graphs and owners, positional identity (`Setup.CreateId`), portable property metadata and hydration markers. The DOM project (`Ranvier.View.Foundation`) gains a hydration cursor: `materialise` takes its roots from the server DOM instead of a skeleton clone, regions adopt their rows between their markers, the first commit compares before it writes, and a structural mismatch re-renders the nearest region.

**Tech Stack:** F# on .NET 10 and Fable 5.18 (JavaScript); Ranvier core (`src/Ranvier`, read-only here); jsdom node tests (`npm run test:dom`); Playwright Chromium (`npm run test:browser`); BenchmarkDotNet 0.15.8; Solid 2 (`@solidjs/web` and `@solidjs/compiler` 2.0.0-rc.14, installed).

**Spec:** [`docs/superpowers/specs/2026-10-19-server-html-hydration-design.md`](../specs/2026-10-19-server-html-hydration-design.md) (today at `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\specs\2026-10-19-server-html-hydration-design.md`; Task 1 copies it into the repository). Read it before any task. Decisions 1–5 were ruled by the user on 2026-10-19: D1 synchronous pass, a pending boundary emits its pending view; D2 value differences corrected silently, a structural mismatch re-renders the nearest region, `Strict` raises; D3 a view-owned positional instance path plus a per-region row counter; D4 two projects, portable (with the server) and DOM; D5 a control the user edited before hydration keeps the edit.

## Core and view state

Required by `.claude/rules/view-uses-core.md`. This plan adds no view state beyond the spec's list.

**Core primitives used:**

- `Graph`, `GraphOptions` (`ThreadAffinity = Guarded` by default): one graph per `Server.render`, disposed in a `finally`.
- `Api.createRoot`, `Owner`, `Api.runWithOwner`, `Api.onCleanup`, `MountLifetime` (`Lifetime.fs`): the request root, one child lifetime per template instance, row and boundary attempt on the server; the client mount's owners during hydration are those of `mountWith SynchronousCommits`.
- `Api.untrack`: server setups and slot reads.
- `NotReadyException`: a pending read on the server.
- `Projection.ensureUniqueKeys`: the server's duplicate-key check.
- `createProjectionOnce`, `createMemoWithComparer`, `createEffect`: unchanged client regions; hydration changes only where their first pass finds its nodes.

**View-owned state (the spec's list, nothing more):**

- `ServerPlan`: static segments interleaved with slot, region, child and placeholder operations, with the definition's structural hash and its content-model summary. Template tables and server HTML.
- The content-model rule table (`ContentModel`). Server HTML.
- `HydrationCursor`: the next server node to adopt and the undo log of structural edits. DOM nodes and their layout.
- Hydration markers. DOM nodes.
- `InstancePath`, `IdentityScope` and each region's row counter (`RegionSite.NextRow`). Agreed by the user, 2026-10-19 (Decision 3).

## Global Constraints

- View-side only: no file under `src/` changes. A task that finds a core change necessary stops and raises it with the user (`.claude/rules/view-uses-core.md`).
- Work in `C:\Users\shaya\RiderProjects\Ranvier-fable-ranvier-dom` on branch `poc/fable-ranvier-dom`.
- Module and namespace names do not change when files move: `Ranvier.View.Foundation.Representation`, `Ranvier.View.Foundation.Template`, `Ranvier.View.Foundation.HtmlTemplate` and the rest keep their names in `Ranvier.View.Portable`.
- `Ranvier.View.Portable` references `src/Ranvier` and `Xantham.Fable.Core.TS` 0.2.1 only (`Fable.Core` 5.3.0 arrives through `src/Ranvier`). It never references `src/Fable.Ranvier`, and none of its files contains an `Emit` that names `document`, `window` or `addEventListener` (Task 1's packaging check).
- Server output is byte-identical on .NET and under Fable: every server check lives in `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`, runs under `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj` and under `node --test tests/server.test.mjs`, and asserts literal strings.
- Hydration markers, verbatim: `<!--[r-->` root, `<!--[-->` list region or view hole, `<!--[a:HASH-->` arm (`HASH` eight lowercase hex digits), `<!--[c-->` / `<!--[p-->` / `<!--[e-->` boundary content / pending / error, `<!--]-->` every end, `<!--!-->` text separator, `<!--_-->` empty text slot, `<!--ranvier:row-->` zero-root row.
- Escaping, verbatim: text `&` → `&amp;`, `<` → `&lt;`, `>` → `&gt;`, `\r` → `&#13;`; attribute values always double-quoted, the text set plus `"` → `&quot;`.
- Gates, run at the end of every task, all exit 0: `bash $CLAUDE_JOB_DIR/tmp/pocgates.sh` (today `C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/pocgates.sh`; it runs `dotnet fsi build.fsx -- test`, `npm ci`, `dotnet fsi build.fsx -- test-fable`, the playground's `npm ci`, `npm run test:dom`, `npm run test:browser` and the Ripple compile) and, from the worktree root, `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`.
- Inner loop (not a gate), from `examples/Fable.Ranvier.Playground`: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/server.test.mjs` (Tasks 2–12) or `tests/hydration.test.mjs` (Tasks 13–19).
- Benchmarks run one at a time behind the lock: `mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` before, `rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` after. If `mkdir` fails, another benchmark holds the lock: wait and retry. Never remove a lock you did not create.
- Commits: one conventional line, a blank line, then exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
  ```
  Before each commit: `dotnet fantomas <each changed .fs file>` (pinned 8.0.5; `Generated/` files are skipped by `.fantomasignore`), `git add` of named paths only, `git diff --cached --check` empty. Push with `git push origin HEAD:poc/fable-ranvier-dom`. Never rebase, amend a pushed commit or force-push.
- Leave untracked `examples/Fable.Ranvier.Playground/generated-*` directories and `benchmarks/frameworks/_lines.tmp.mjs` alone. New output directories (`generated-server-dist`, `generated-hydration-dist`) stay untracked.
- Comments follow `.claude/rules/comments.md`. No `FOR-REVIEW` comment is committed.
- Every existing test passes unchanged, except the expectations Tasks 9 and 10 update (listed there) and the Task 2 pins that Tasks 9, 10 and 11 change. Any other existing test that has to change is a plan conflict: stop and report it.

## Rulings on the spec

Each ruling picks an implementation where the spec leaves one open.

1. **Id format.** An id is `prefix + "-" + path + "-" + n`. The root instance's path text is `0` (it has no steps); a step appends `.c<k>` (inlined child), `.r<region>.<row>` (region row) or `.a<k>` (template reached from authored content). The spec's example `a-0.c1.r0.3-0` is prefix `a`, root `0`, child step `c1`, row step `r0.3`, counter `0`. Authored components' `CreateId` returns `prefix-path-a<n>`, `n` counting per mount (client) or per render (server), so it never equals a template id. *If wrong:* one formatting function in `Identity.fs`.
2. **Region numbering.** A region's index is its position among the regions of its owning part (the template, or one inlined child) in document order; its rows' parent path is that part's path. This is the reading that produces the spec's example (a row of region 0 under child 1). Inlined children are numbered in the definition's flattened `Children` order (every depth), as `SkeletonLayout.Children` numbers them. *If wrong:* the region index computation in `compile` and in the plan compiler.
3. **The shared server checks live in `experiments/Ranvier.View.Portable/Checks/`**, not in `tests/`, so the .NET runner compiles the same file Fable compiles; `tests/DomTests.fsproj` links them and `tests/server.test.mjs` runs them. `HydrationChecks.fs` lives in `tests/` as the spec says.
4. **`Html.render` of a template writes the new markers too** (arm hash, boundary view, `<!--_-->`, separators, zero-root rows) through the same writer; it writes no root marker pair. Authored boundaries under `Html.render` keep `<!--[-->`. Decision 6 makes markers unconditional; one writer means one format.
5. **`renderTo` calls its writer only after the render succeeds**, once per buffered segment in order (`StringBuilder.GetChunks` on .NET; under Fable the buffer is one string, written as one segment). A failed render writes nothing.
6. **Static serialised properties follow Decision 5 during hydration**: a static `value`, `checked`, `selected` or textarea value is written only when the server attribute differs from the client's serialisation. The spec's "Static properties are written" holds for `BrowserOnly` static properties. Without this, a static `value` would discard the user's edit that Decision 5 keeps.
7. **A `[c` boundary whose content is pending on the client keeps its hydrated content in place as the pending view**: the content is set up once, adopting the server nodes; its pending bindings leave the server values; the reveal places the same nodes, already in place. The boundary's pending view is not set up in that case. A second detached setup would run the content's setup twice.
8. **`Strict` restores structural edits** (placeholder replacements, separator removals, region clears) before it raises; slot values corrected before the mismatch stay corrected. A non-mismatch failure during hydration takes the same path (Review Focus 5). The mount's nodes are released without being removed.
9. **Test 30's "an authored root raises" is a compile error, not a runtime test**: `hydrate` takes `Component<'props>`, so an authored root cannot be passed.
10. **`stateScript` writes `<` as `\u003c` and U+2028/U+2029 as `\u2028`/`\u2029`** (the spec's text lost the backslashes). `readState` returns `string option`, `None` when the host document has no element with the id.
11. **Void elements with children raise in the server plan only**; the client skeleton keeps dropping them, so no existing client test changes. *If wrong:* one check in `skeletonLayout`.
12. **The content-model table holds the spec's restructurings as nine rules (Task 11)**; "`option` outside `select`/`datalist` loses its wrapper" is read as the `select-content` rule (a wrapper element inside `select` is dropped). The rule on `select` content depends on the parser version (Chromium's customizable-select parser accepts more content than older parsers); its Chromium test asserts the server rejection only.
13. **The .NET first-content benchmark is a new BenchmarkDotNet project, `experiments/Ranvier.View.Portable/ServerBench`.** `SnapshotBench` is not a BenchmarkDotNet project.
14. **A text slot whose reader returns `null` writes the empty-text placeholder**, as `""` does; `writeMarkup` already treats `null` as `""`.

## Review Focus

1. Foreign nodes around the root marker pair (a pretty-printed page puts whitespace text before `<!--[r-->` and a `<script>` after `<!--]-->`): `hydrate` finds the root pair, adopts only what lies between, and leaves the foreign nodes in place after disposal. Test: `foreignSiblings` (Task 13).
2. A text value containing `\r\n`: the server writes `&#13;\n`, the parser keeps both characters, and hydration writes nothing. Test: `carriageReturn` (Task 13).
3. A list row whose first root is itself a region (a row template that starts with `t.each` or `t.matchWith`): the logical walk adopts the row's nested markers as one position and the next row starts after the nested end marker. Test: `rowStartsWithRegion` (Task 15).
4. A list whose client items have the server's count but another order hydrates positionally (spec, Differing selections), and a later reorder still moves rows by the client's keys. Test: `reorderedItems` (Task 15).
5. A setup that throws during hydration (not a mismatch) propagates out of `hydrate` and leaves the server HTML as it was, so the page is not blanked. Test: `setupThrowsDuringHydration` (Task 14).

## File structure

| File | Responsibility | Tasks |
|---|---|---|
| `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj` (new) | portable library | 1, 5, 6 |
| `experiments/Ranvier.View.Portable/{Lifetime,Scope,Representation,Typed,Template}.fs`, `Generated/HtmlTemplate.fs` (moved) | portable model and server writer | 1, 4, 6, 7, 8, 9, 10, 11 |
| `experiments/Ranvier.View.Portable/Identity.fs` (new) | `InstancePath`, `IdentityScope` | 6 |
| `experiments/Ranvier.View.Portable/Server.fs` (new) | `Server.render`, request scope, `stateScript` | 5, 9, 18 |
| `experiments/Ranvier.View.Portable/Checks/{PackagingChecks,ServerFixtures,ServerChecks}.fs` (new) | shared .NET and Fable checks | 1–12, 18 |
| `experiments/Ranvier.View.Portable/ServerBench/` (new) | BenchmarkDotNet first-content benchmark | 3, 12 |
| `experiments/Ranvier.View.Foundation/DomEvents.fs` (new) | `listenDirect` | 1 |
| `experiments/Ranvier.View.Foundation/TemplateDom.fs` | compiled hash and expectations, cursor, adoption, compare-then-write | 6, 7, 13, 15, 16 |
| `experiments/Ranvier.View.Foundation/Mounting.fs` | identity sites, adopted range, hydrating regions, mismatch catch | 6, 13, 14, 15, 16, 17 |
| `experiments/Ranvier.View.Foundation/Hydration.fs` (new) | `hydrate`, `hydrateWith`, `readState` | 13, 14, 18 |
| `experiments/Ranvier.View.Foundation/RepresentationChecks.{fsproj,fsx}` | references the portable project, runs the shared checks | 1, 2 |
| `tools/Ranvier.Dom.Generate/Program.fs`, `tests.py`, `README.md` | template output path, property metadata table | 1, 7 |
| `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, `package.json`, `range.html`, `server.test.mjs` (new), `hydration.test.mjs` (new), `hydration.html` (new), `hydration.spec.mjs` (new), `HydrationChecks.fs` (new), `ServerPerf.fs` (new), `HydrationPerf.fs` (new) | test and benchmark harness | 1, 2, 8, 12, 13, 20 |
| `examples/Fable.Ranvier.Playground/benchmarks/frameworks/{server-render.mjs,server-solid.jsx,hydration-bench.mjs,hydration-page.mjs,hydration-solid.jsx}` (new), `run.mjs`, `keyed-rows.mjs`, `vite.config.mjs`, `page.mjs` | Solid 2 comparisons; moved source paths | 1, 12, 20 |
| `bench/Ranvier.Counters/{Ranvier.Counters.fsproj,ServerWorkloads.fs,Scenarios.fs}`, `fable/Ranvier.Counters/{Ranvier.Counters.fsproj,Scenarios.fs}` | server-render counter scenario | 12 |
| `docs/research/2026-10-19-server-html-hydration.md` (new), `docs/research/framework-performance/investigation/server-html/` (new) | measurements, mutation checks | 3–12, 19, 20 |

## Spec coverage

| Spec item | Task |
|---|---|
| Decision 4, packaging test | 1 |
| Testing 1–2 (pins) | 2 |
| Performance: .NET first content (before S1, after each server phase) | 3, 4, 5, 7, 10, 12 |
| Server plan, merged statics, `CompareExchange` publication, escaping fast path, names at definition time; Testing 3–5 | 4 |
| `Server.render`/`renderTo`, request graph, owners, disposal, concurrency; Testing 10–12, 19 | 5 |
| Deterministic identity, Decision 3; Testing 16 (server and plain mount) | 6 |
| Portable properties and fallback; Testing 6–8 | 7 |
| Forms, leading line feed; Testing 9 | 8 |
| Pending and errors against boundaries, Decision 1; Testing 13–15 | 9 |
| Root, arm, placeholder, separator and zero-root markers, structural hash | 10 |
| Malformed HTML normalisation, void and `template` checks; Testing 17–18 | 11 |
| Node first content against Solid 2, counter scenarios | 12 |
| Hydration entry, cursor, logical walk, claims, compare-then-write, events; Testing 20–24, 30 | 13 |
| Mismatch policy at the root, `Strict`; Testing 29 | 14 |
| List rows, separators, zero-root rows, nested regions, region-level mismatch; Testing 25, 27 | 15 |
| Arms, hash, view holes, differing selections; Testing 26 | 16 |
| Boundaries; Testing 28 | 17 |
| State transfer, `CreateId` continuation, forms and normalisation in Chromium; Testing 16 (client), 31–33 | 18 |
| Mutation checks | 19 |
| Hydration benchmarks, status | 20 |

---

### Task 1: Split the portable project and add the packaging check

**Files:**
- Create: `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj`, `experiments/Ranvier.View.Portable/Checks/PackagingChecks.fs`, `experiments/Ranvier.View.Foundation/DomEvents.fs`
- Move (`git mv`): `experiments/Ranvier.View.Foundation/{Lifetime,Scope,Representation,Typed,Template}.fs` and `Generated/HtmlTemplate.fs` to `experiments/Ranvier.View.Portable/` (same relative names)
- Modify: `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj`, `RepresentationChecks.fsproj`, `RepresentationChecks.fsx` (the `#if INTERACTIVE` block and a runner at the end), `experiments/Ranvier.View.Portable/Typed.fs` (after the move: delete `listenDirect`), `experiments/Ranvier.View.Foundation/Mounting.fs`, `experiments/Ranvier.View.Foundation/Browser.fs` and `examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs` (each `Typed.listenDirect`), `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, `Ranvier.slnx`, `.fantomasignore`, `tools/Ranvier.Dom.Generate/Program.fs`, `tools/Ranvier.Dom.Generate/tests.py`, `tools/Ranvier.Dom.Generate/README.md`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/run.mjs`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-rows.mjs`
- Create (copies): `docs/superpowers/specs/2026-10-19-server-html-hydration-design.md`, `docs/superpowers/plans/2026-10-19-server-html-hydration.md`

**Interfaces:**
- Produces:
  - Project `Ranvier.View.Portable` (assembly `Ranvier.View.Portable`), compile order `Lifetime.fs`, `Scope.fs`, `Representation.fs`, `Typed.fs`, `Template.fs`, `Generated/HtmlTemplate.fs`; `InternalsVisibleTo` for `Ranvier.View.Foundation`, `RepresentationChecks`, `ServerBench`.
  - `module Ranvier.View.Foundation.DomEvents` with `listenDirect (node: obj) (eventName: string) (handler: obj -> unit) : unit -> unit` (the `Emit` moved verbatim from `Typed.fs`).
  - `module PackagingChecks` with `cases: (string * (unit -> unit)) array` (.NET only; not linked into Fable).

- [ ] **Step 1: Copy the spec and this plan into the repository**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
cp C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/specs/2026-10-19-server-html-hydration-design.md docs/superpowers/specs/
cp C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/plans/2026-10-19-server-html-hydration.md docs/superpowers/plans/
```

- [ ] **Step 2: Write the failing packaging check**

Create `experiments/Ranvier.View.Portable/Checks/PackagingChecks.fs`:

```fsharp
/// <summary>Checks that the portable project compiles without a browser.</summary>
module PackagingChecks

open System
open System.IO
open System.Xml.Linq

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private portable =
    Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let private project () =
    XDocument.Load (Path.Combine (portable, "Ranvier.View.Portable.fsproj"))

let private items (name: string) =
    [
        for element in (project ()).Descendants (XName.Get name) do
            element.Attribute(XName.Get "Include").Value
    ]

let private browserReferences () =
    equal [] (items "ProjectReference" |> List.filter (fun path -> path.Contains "Fable.Ranvier"))

let private browserEmits () =
    let browser (line: string) =
        line.Contains "Emit("
        && (line.Contains "document" || line.Contains "window" || line.Contains "addEventListener")

    let offending =
        [
            for file in items "Compile" do
                for line in File.ReadAllLines (Path.Combine (portable, file)) do
                    if browser line then
                        file + ": " + line.Trim ()
        ]

    equal [] offending

let cases: (string * (unit -> unit)) array =
    [|
        "the portable project references src/Ranvier and type bindings only", browserReferences
        "the portable project's emits touch neither document, window nor addEventListener", browserEmits
    |]
```

In `RepresentationChecks.fsproj` add `<Compile Include="../Ranvier.View.Portable/Checks/PackagingChecks.fs" Link="Checks/PackagingChecks.fs" />` before `RepresentationChecks.fsx`. Append to `RepresentationChecks.fsx`, after its last line:

```fsharp
let runCases (cases: (string * (unit -> unit)) array) =
    for name, check in cases do
        check ()
        printfn "PASS %s" name

runCases PackagingChecks.cases
```

- [ ] **Step 3: Run it and confirm it fails**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: FAIL with `FileNotFoundException` naming `Ranvier.View.Portable.fsproj`.

- [ ] **Step 4: Create the portable project and move the files**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/experiments
mkdir -p Ranvier.View.Portable/Generated
for f in Lifetime.fs Scope.fs Representation.fs Typed.fs Template.fs Generated/HtmlTemplate.fs; do git mv Ranvier.View.Foundation/$f Ranvier.View.Portable/$f; done
```

Create `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Lifetime.fs" />
    <Compile Include="Scope.fs" />
    <Compile Include="Representation.fs" />
    <Compile Include="Typed.fs" />
    <Compile Include="Template.fs" />
    <Compile Include="Generated/HtmlTemplate.fs" />
    <ProjectReference Include="../../src/Ranvier/Ranvier.fsproj" />
    <PackageReference Include="Xantham.Fable.Core.TS" Version="0.2.1" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
    <InternalsVisibleTo Include="Ranvier.View.Foundation" />
    <InternalsVisibleTo Include="RepresentationChecks" />
    <InternalsVisibleTo Include="ServerBench" />
  </ItemGroup>
</Project>
```

Delete `listenDirect` and its doc comment from `Typed.fs`. Create `experiments/Ranvier.View.Foundation/DomEvents.fs`:

```fsharp
module Ranvier.View.Foundation.DomEvents

open Fable.Core

/// <summary>Adds a direct event listener and returns its removal.</summary>
[<Emit("(() => { $0.addEventListener($1, $2); return () => $0.removeEventListener($1, $2); })()")>]
let listenDirect (node: obj) (eventName: string) (handler: obj -> unit) : unit -> unit = jsNative
```

Replace `Typed.listenDirect` with `DomEvents.listenDirect` in `Mounting.fs`, `Browser.fs` and `BindingBaselineMounting.fs` (the frozen baseline changes in this qualifier only).

In `Ranvier.View.Foundation.fsproj`: remove the six moved `Compile` items, add `<Compile Include="DomEvents.fs" />` first, add `<ProjectReference Include="../Ranvier.View.Portable/Ranvier.View.Portable.fsproj" />`.

In `RepresentationChecks.fsproj`: remove the `Representation.fs`, `Typed.fs`, `Template.fs` and `Generated/HtmlTemplate.fs` items, replace the `Fable.Ranvier` reference with `<ProjectReference Include="../Ranvier.View.Portable/Ranvier.View.Portable.fsproj" />`. In `RepresentationChecks.fsx`'s `#if INTERACTIVE` block, change `#load "Representation.fs"` to `#load "../Ranvier.View.Portable/Representation.fs"`.

In `tests/DomTests.fsproj`: change the six links to `../../../experiments/Ranvier.View.Portable/<file>` (keep each `Link`), and add `<Compile Include="../../../experiments/Ranvier.View.Foundation/DomEvents.fs" Link="DomEvents.fs" />` after the `Typed.fs` link.

In `Ranvier.slnx`: add a folder `/experiments/Ranvier.View.Portable/` holding `<Project Path="experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj" />`.

In `.fantomasignore`: add `experiments/Ranvier.View.Portable/Generated/`.

In `tools/Ranvier.Dom.Generate/Program.fs`: when `args.Length = 0`, write the template output to `experiments/Ranvier.View.Portable/Generated/HtmlTemplate.fs` (full path from `__SOURCE_DIRECTORY__`, as `output` is built); with arguments, keep the sibling rule `templateOutput` uses today. In `tests.py`, read `../../experiments/Ranvier.View.Portable/Template.fs`. In the generator `README.md`, replace the `Generated/HtmlTemplate.fs` location and the `.fantomasignore` sentence with the portable paths.

In `benchmarks/frameworks/run.mjs` lines 98–99 and `keyed-rows.mjs` line 185, hash `Representation.fs`, `Typed.fs`, `Lifetime.fs`, `Template.fs` from `../../experiments/Ranvier.View.Portable/` and the rest from `../../experiments/Ranvier.View.Foundation/` as today.

- [ ] **Step 5: Run the checks and the builds**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every existing `PASS` line, then `PASS the portable project references src/Ranvier and type bindings only` and `PASS the portable project's emits touch neither document, window nor addEventListener`.

Run: `dotnet build experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj -c Release`
Expected: build succeeded, 0 errors.

Run (from `examples/Fable.Ranvier.Playground`): `npm run test:dom`
Expected: PASS, the same test count as before the task.

- [ ] **Step 6: Confirm the generator writes the moved file unchanged**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
npm ci --prefix tools/Ranvier.Dom.Generate --ignore-scripts
dotnet run --project tools/Ranvier.Dom.Generate/Ranvier.Dom.Generate.fsproj -c Release
git status --short experiments/Ranvier.View.Portable/Generated experiments/Ranvier.View.Foundation/Generated
python tools/Ranvier.Dom.Generate/tests.py
```

Expected: `git status` lists no modification of either generated file; `tests.py` exits 0.

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/DomEvents.fs experiments/Ranvier.View.Portable/Typed.fs experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/Browser.fs experiments/Ranvier.View.Portable/Checks/PackagingChecks.fs tools/Ranvier.Dom.Generate/Program.fs
git add -A experiments/Ranvier.View.Portable experiments/Ranvier.View.Foundation
git add examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs examples/Fable.Ranvier.Playground/benchmarks/frameworks/run.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/keyed-rows.mjs Ranvier.slnx .fantomasignore tools/Ranvier.Dom.Generate/Program.fs tools/Ranvier.Dom.Generate/tests.py tools/Ranvier.Dom.Generate/README.md docs/superpowers/specs/2026-10-19-server-html-hydration-design.md docs/superpowers/plans/2026-10-19-server-html-hydration.md
git status --short experiments | grep -v "^[RAM] " || true
git diff --cached --check
git commit -F - <<'EOF'
refactor(view): move the portable model into Ranvier.View.Portable and check it compiles without a browser

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

The `git status` line must print nothing: `bin/` and `obj/` are ignored, and every other change under `experiments/` is staged.

---

### Task 2: Pin today's server output for every template fixture

**Files:**
- Create: `experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs`, `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`, `examples/Fable.Ranvier.Playground/tests/server.test.mjs`
- Modify: `experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`, `RepresentationChecks.fsx` (one line), `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj`, `examples/Fable.Ranvier.Playground/package.json`

**Interfaces:**
- Consumes: `Html.render`, `Component.define`, `Component.render`, the template catalogue.
- Produces:
  - `module ServerFixtures` (public fixtures every later task reuses): `greeting: Component<GreetingProps>`, `named`, `clicking`, `nullTitle`, `holed: Component<HoleProps>`, `staged: Component<Stage>`, `decades: Component<int>`, `listed: Component<Listed array>`, `carded: Component<GreetingProps>`, `besideText`, `flagsTemplate: Component<Flags>`, `staticTemplate: Component<unit>`, `pair: Component<GreetingProps>` (two roots), `guarded: Component<unit>` with `mutable boundarySource: AsyncSource<string>`, `emptyText: Component<string>`, `voidChildren: Component<unit>`, `rowInTable: Component<unit>`, `componentPage: Component<int>`, `keyedPage: Component<int>`; the types `GreetingProps`, `HoleProps`, `Stage`, `Listed`, `Flags`, `KeyedLine`, `ComponentLine`; `renderOmit (definition: Component<'props>) (props: 'props) : string` = `Html.render Omit (Program.run (Component.render definition props))`; `renderComponentPage: int -> string`; `renderKeyedPage: int -> string`.
  - `module ServerChecks` with `cases: (string * (unit -> unit)) array` and, under `#if !FABLE_COMPILER`, `dotnetCases: (string * (unit -> unit)) array`; helpers `equal`, `failure: (unit -> unit) -> exn`.

No production code changes. The tests pass on first run: they record what the writer produces today.

- [ ] **Step 1: Write the fixtures**

Create `experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs`:

```fsharp
/// <summary>Template fixtures shared by the server checks, the hydration checks and the first-content benchmarks.</summary>
module ServerFixtures

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

type GreetingProps = { Title: string; Name: string }

let greeting =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div
            [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |]
            [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |])

let greetingProps = { Title = "T &"; Name = "<Ada>" }

let named =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |] [| t.span [||] [| t.text (fun r -> r.Name) |] |])

let clicking =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.button [| t.Events.on ("click", fun _ _ -> ()) |] [||])

let nullTitle =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.div [| t.Attrs.title (fun _ -> (null: string)) |] [||])

type HoleProps = { Caption: string; Body: Program.View }

let holed =
    Component.define (fun _ (props: HoleProps) -> props) (fun t ->
        t.div [| t.Attrs.className "card" |] [| t.text (fun r -> r.Caption); t.view (fun r -> r.Body) |])

type Stage =
    | Waiting
    | Working of int
    | Complete of string

let waitingArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "waiting" |])

let workingArm =
    Component.define (fun _ (progress: unit -> int) -> progress) (fun t ->
        t.span [||] [| t.text (fun progress -> "working " + string (progress ())) |])

let completeArm =
    Component.define (fun _ (result: unit -> string) -> result) (fun t -> t.span [||] [| t.text (fun result -> "complete " + result ()) |])

let staged =
    Component.define (fun _ (stage: Stage) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith id (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

let decadeArm =
    Component.define (fun _ (decade: unit -> int) -> decade) (fun t -> t.span [||] [| t.text (fun decade -> string (decade ())) |])

let decades =
    Component.define (fun _ (value: int) -> value) (fun t ->
        t.div [||] [| t.branch (fun value -> value / 10) (fun decade -> t.arm decadeArm decade) |])

type Listed = { Id: int; Text: string }

let listedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.li [||] [| t.text (fun item -> (item ()).Text) |])

let listed =
    Component.define (fun _ (items: Listed array) -> items) (fun t -> t.ul [||] [| t.each (fun (item: Listed) -> item.Id) id listedRow |])

let listedItems = [| { Id = 1; Text = "a" }; { Id = 2; Text = "<b>" } |]

type BadgeProps = { Count: int }

let badge =
    Component.define (fun _ (props: BadgeProps) -> props) (fun t ->
        t.span [| t.Attrs.title (fun badge -> string badge.Count) |] [| t.text (fun badge -> string badge.Count) |])

let carded =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div [||] [| t.text (fun r -> r.Name); t.render badge (fun r -> { Count = r.Title.Length }) |])

let textual =
    Component.define (fun _ (value: string) -> value) (fun t -> t.text (fun value -> value))

let besideText =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.span [||] [| t.text "a"; t.render textual (fun r -> r.Name) |])

type Flags =
    {
        Name: string
        Nickname: string option
        Busy: bool
        Expanded: bool option
        State: string
    }

let flagsTemplate =
    Component.define (fun _ (props: Flags) -> props) (fun t ->
        t.button
            [|
                t.Attrs.title (fun r -> r.Name)
                t.Attrs.id (fun r -> r.Nickname)
                t.Attrs.Button.disabled (fun r -> r.Busy)
                t.Attrs.ariaExpanded (fun r -> r.Expanded)
                t.Attrs.ariaBusy (fun r -> r.Busy)
                t.Attrs.data ("state", fun r -> Some r.State)
            |]
            [| t.text "go" |])

let flagCases =
    [
        { Name = "a"; Nickname = None; Busy = false; Expanded = None; State = "x" }
        { Name = "b"; Nickname = Some "n"; Busy = true; Expanded = Some false; State = "y" }
        { Name = "c"; Nickname = Some "m"; Busy = false; Expanded = Some true; State = "z" }
        { Name = null; Nickname = None; Busy = false; Expanded = None; State = "n" }
    ]

let staticTemplate =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.button [| t.Attrs.Button.disabled false; t.Attrs.ariaExpanded false; t.Attrs.id (None: string option); t.Attrs.title "t" |] [||]
                t.button [| t.Attrs.Button.disabled true; t.Attrs.ariaExpanded true; t.Attrs.id (Some "i"); t.Attrs.data ("k", "v") |] [||]
            |])

let pair =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.fragment
            [|
                t.div [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |] [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |]
                t.span [||] [| t.text (fun r -> r.Name) |]
            |])

/// <summary>The source the <c>guarded</c> fixture's content reads; assign it inside a graph before rendering.</summary>
let mutable boundarySource: AsyncSource<string> = Unchecked.defaultof<_>

let boundaryPending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let boundaryFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let boundaryContent =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.span [||] [| t.text (fun _ -> boundarySource.Value) |])

let guarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary boundaryPending boundaryFailed boundaryContent |])

let emptyText =
    Component.define (fun _ (value: string) -> value) (fun t -> t.span [||] [| t.text (fun value -> value) |])

let voidChildren =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.element (HtmlTag "br") [||] [| t.text "x" |] |])

let rowInTable =
    Component.define (fun _ () -> ()) (fun t -> t.table [||] [| t.tr [||] [| t.td [||] [| t.text "x" |] |] |])

type ComponentLine =
    {
        Index: int
        Label: Signal<string>
        Clicks: Signal<int>
    }

/// <summary>The row of the framework comparison's component workload.</summary>
let componentRow =
    Component.define
        (fun _ (item: unit -> int) ->
            let index = item ()
            { Index = index; Label = Api.createSignal ("row " + string index); Clicks = Api.createSignal 0 })
        (fun t ->
            t.div
                [| t.Attrs.id (fun r -> "row-" + string r.Index); t.Attrs.className "panel"; t.Attrs.title (t.from (fun r -> r.Label)) |]
                [|
                    t.input [| t.Props.Input.value (t.from (fun r -> r.Label)) |]
                    t.button [| t.Events.onClick (fun r _ -> r.Clicks.Value <- r.Clicks.Value + 1) |] [| t.text (fun r -> string r.Clicks.Value) |]
                |])

let componentPage =
    Component.define (fun _ (rows: int) -> Array.init rows (fun index -> index + 1)) (fun t ->
        t.element (HtmlTag "section") [||] [| t.each id id componentRow |])

type KeyedLine = { Id: int; Label: string }

/// <summary>The row of the js-framework-benchmark keyed workload.</summary>
let keyedRow =
    Component.define (fun _ (item: unit -> KeyedLine) -> item ()) (fun t ->
        t.tr
            [||]
            [|
                t.td [| t.Attrs.className "col-md-1" |] [| t.text (fun (row: KeyedLine) -> string row.Id) |]
                t.td [| t.Attrs.className "col-md-4" |] [| t.a [| t.Events.onClick (fun _ _ -> ()) |] [| t.text (fun (row: KeyedLine) -> row.Label) |] |]
                t.td
                    [| t.Attrs.className "col-md-1" |]
                    [| t.a [| t.Events.onClick (fun _ _ -> ()) |] [| t.span [| t.Attrs.className "glyphicon glyphicon-remove"; t.Attrs.attribute ("aria-hidden", "true") |] [||] |] |]
                t.td [| t.Attrs.className "col-md-6" |] [||]
            |])

let keyedPage =
    Component.define (fun _ (rows: int) -> Array.init rows (fun index -> { Id = index + 1; Label = "row " + string (index + 1) })) (fun t ->
        t.table [| t.Attrs.className "table" |] [| t.tbody [||] [| t.each (fun (row: KeyedLine) -> row.Id) id keyedRow |] |])

let renderOmit (definition: Component<'props>) (props: 'props) =
    Html.render Omit (Program.run (Component.render definition props))

/// <summary>The first content of the component workload at <c>rows</c> rows, rendered as one request.</summary>
let renderComponentPage (rows: int) : string =
    use graph = new Graph ()
    graph.Run (fun () -> renderOmit componentPage rows)

/// <summary>The first content of the keyed workload at <c>rows</c> rows, rendered as one request.</summary>
let renderKeyedPage (rows: int) : string =
    use graph = new Graph ()
    graph.Run (fun () -> renderOmit keyedPage rows)
```

- [ ] **Step 2: Write the pins**

Create `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`:

```fsharp
/// <summary>Server HTML checks, run on .NET by RepresentationChecks and under Fable by tests/server.test.mjs.</summary>
module ServerChecks

open System
open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open ServerFixtures

let equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>The exception <c>action</c> raises; fails when it returns.</summary>
let failure (action: unit -> unit) : exn =
    let mutable caught: exn = null

    try
        action ()
    with error ->
        caught <- error

    if isNull caught then
        failwith "Expected an exception"

    caught

let private templatePins () =
    equal """<div class="panel" title="T &amp;">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div>""" (renderOmit greeting greetingProps)
    equal """<div class="panel" title="T &amp;"><span>&lt;Ada&gt;</span></div>""" (renderOmit named greetingProps)
    equal "<button></button>" (renderOmit clicking greetingProps)
    equal "<div></div>" (renderOmit nullTitle greetingProps)

    equal
        """<div class="card">A<!--[--><em>&lt;b&gt;</em><!--]--></div>"""
        (renderOmit holed { Caption = "A"; Body = Program.element (HtmlTag "em") [||] [| Program.text "<b>" |] })

    equal """<div class="card">B<!--[--><!--]--></div>""" (renderOmit holed { Caption = "B"; Body = Program.fragment [||] })
    equal "<ul><!--[--><li>a</li><li>&lt;b&gt;</li><!--]--></ul>" (renderOmit listed listedItems)
    equal "<ul><!--[--><!--]--></ul>" (renderOmit listed [||])
    equal """<div>&lt;Ada&gt;<span title="3">3</span></div>""" (renderOmit carded greetingProps)
    equal "<span>a<!--!-->&lt;Ada&gt;</span>" (renderOmit besideText greetingProps)

    equal
        [
            """<button title="a" aria-busy="false" data-state="x">go</button>"""
            """<button title="b" id="n" disabled="" aria-expanded="false" aria-busy="true" data-state="y">go</button>"""
            """<button title="c" id="m" aria-expanded="true" aria-busy="false" data-state="z">go</button>"""
            """<button aria-busy="false" data-state="n">go</button>"""
        ]
        [ for flags in flagCases -> renderOmit flagsTemplate flags ]

    equal
        """<div><button aria-expanded="false" title="t"></button><button disabled="" aria-expanded="true" id="i" data-k="v"></button></div>"""
        (renderOmit staticTemplate ())

    equal """<div class="panel" title="T &amp;">Hello <!--!-->&lt;Ada&gt;<!--!-->!</div><span>&lt;Ada&gt;</span>""" (renderOmit pair greetingProps)

let private armPins () =
    equal "<div><!--[--><span>working 3</span><!--]--></div>" (renderOmit staged (Working 3))
    equal "<div><!--[--><span>complete &lt;ok&gt;</span><!--]--></div>" (renderOmit staged (Complete "<ok>"))
    equal "<div><!--[--><span>4</span><!--]--></div>" (renderOmit decades 42)

let private boundaryPins () =
    use graph = new Graph ()

    graph.Run (fun () ->
        Api.createRoot (fun _ ->
            boundarySource <- Api.createAsyncSource<string> ()
            equal "<div><!--[--><span>loading</span><!--]--></div>" (renderOmit guarded ())
            boundarySource.Settle "<A>"
            equal "<div><!--[--><span>&lt;A&gt;</span><!--]--></div>" (renderOmit guarded ())
            boundarySource.Fail (Exception "<bad>")
            equal "<div><!--[--><span>&lt;bad&gt;</span><!--]--></div>" (renderOmit guarded ())))

let private edgePins () =
    equal "<span></span>" (renderOmit emptyText "")
    equal "<div><br></div>" (renderOmit voidChildren ())
    equal "<table><tr><td>x</td></tr></table>" (renderOmit rowInTable ())

let cases: (string * (unit -> unit)) array =
    [|
        "server plan matches the current writer for every template fixture", templatePins
        "server plan matches the current writer for arms", armPins
        "server plan matches the current writer for boundaries", boundaryPins
        "empty text, void children and a tr in a table render as recorded", edgePins
    |]

#if !FABLE_COMPILER
let dotnetCases: (string * (unit -> unit)) array = [||]
#endif
```

- [ ] **Step 3: Register the checks on both runtimes**

In `RepresentationChecks.fsproj`, before `RepresentationChecks.fsx`, add `<Compile Include="../Ranvier.View.Portable/Checks/ServerFixtures.fs" Link="Checks/ServerFixtures.fs" />` and `<Compile Include="../Ranvier.View.Portable/Checks/ServerChecks.fs" Link="Checks/ServerChecks.fs" />`. Append to `RepresentationChecks.fsx`: `runCases ServerChecks.cases` and `runCases ServerChecks.dotnetCases`.

In `tests/DomTests.fsproj`, after the `OwnedAsyncChecks.fs` line, add:

```xml
    <Compile Include="../../../experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs" Link="Checks/ServerFixtures.fs" />
    <Compile Include="../../../experiments/Ranvier.View.Portable/Checks/ServerChecks.fs" Link="Checks/ServerChecks.fs" />
```

Create `examples/Fable.Ranvier.Playground/tests/server.test.mjs`:

```js
import test from "node:test";
import { cases } from "../generated-tests/experiments/Ranvier.View.Portable/Checks/ServerChecks.js";

for (const [name, check] of cases) test(name, () => check());
```

In `package.json`, append ` tests/server.test.mjs` to the `test:dom` file list.

- [ ] **Step 4: Run the pins on both runtimes**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the four new `PASS` lines. A failure means today's writer differs from the recorded string: stop and report it.

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/server.test.mjs`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs
git add experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/tests/server.test.mjs examples/Fable.Ranvier.Playground/package.json
git diff --cached --check
git commit -F - <<'EOF'
test(view): pin today's template server HTML on .NET and under Fable

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 3: First-content benchmark harness and today's baseline

**Files:**
- Create: `experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj`, `experiments/Ranvier.View.Portable/ServerBench/ServerBench.fs`, `docs/research/2026-10-19-server-html-hydration.md`
- Create (benchmark output): `docs/research/framework-performance/investigation/server-html/dotnet-before-s1/` (BenchmarkDotNet JSON), `docs/research/framework-performance/investigation/server-html/bytes-before-s1.json`
- Modify: `Ranvier.slnx`

**Interfaces:**
- Consumes: `ServerFixtures.renderComponentPage`, `ServerFixtures.renderKeyedPage` (Task 2).
- Produces: `ServerBench` executable; `--bytes` prints one JSON object per line `{"fixture":"component"|"keyed","rows":N,"bytes":B,"bytesPerRow":B/N}`. Every later server task records `dotnet-after-<task>` with the command of Step 3.

- [ ] **Step 1: Create the project**

`ServerBench.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Checks/ServerFixtures.fs" Link="ServerFixtures.fs" />
    <Compile Include="ServerBench.fs" />
    <ProjectReference Include="../Ranvier.View.Portable.fsproj" />
    <PackageReference Include="BenchmarkDotNet" Version="0.15.8" />
    <PackageReference Update="FSharp.Core" Version="10.1.401" />
  </ItemGroup>
</Project>
```

`ServerBench.fs`:

```fsharp
module ServerBench

open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running

[<MemoryDiagnoser>]
type ComponentPage() =
    [<Params(1, 16, 128)>]
    member val Rows = 0 with get, set

    [<Benchmark>]
    member this.Render() =
        ServerFixtures.renderComponentPage this.Rows

[<MemoryDiagnoser>]
type KeyedPage() =
    [<Params(1000, 10000)>]
    member val Rows = 0 with get, set

    [<Benchmark>]
    member this.Render() =
        ServerFixtures.renderKeyedPage this.Rows

[<EntryPoint>]
let main args =
    if Array.contains "--bytes" args then
        for fixture, render, rows in [ "component", ServerFixtures.renderComponentPage, [ 1; 16; 128 ]; "keyed", ServerFixtures.renderKeyedPage, [ 1000; 10000 ] ] do
            for count in rows do
                let bytes = System.Text.Encoding.UTF8.GetByteCount (render count)
                printfn "{\"fixture\":\"%s\",\"rows\":%d,\"bytes\":%d,\"bytesPerRow\":%f}" fixture count bytes (float bytes / float count)
    else
        BenchmarkSwitcher.FromTypes([| typeof<ComponentPage>; typeof<KeyedPage> |]).Run args |> ignore

    0
```

Add `<Project Path="experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj" />` to the portable folder of `Ranvier.slnx`.

- [ ] **Step 2: Check the harness runs**

Run: `dotnet run -c Release --project experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj -- --bytes`
Expected: five JSON lines; `component` rows 1 has `bytes` above 100.

- [ ] **Step 3: Record the baseline**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
OUT=docs/research/framework-performance/investigation/server-html
mkdir -p $OUT
dotnet run -c Release --project experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj -- --filter '*' --exporters json --artifacts $OUT/dotnet-before-s1
dotnet run -c Release --project experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj -- --bytes > $OUT/bytes-before-s1.json
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Expected: `$OUT/dotnet-before-s1/results/` holds one `*-report-full-compressed.json` per benchmark class with `Mean` and `BytesAllocatedPerOperation` for each `Rows`.

- [ ] **Step 4: Start the research note**

Create `docs/research/2026-10-19-server-html-hydration.md`:

```markdown
# Server HTML and hydration: measurements and mutation checks

Date: 2026-10-19. Branch `poc/fable-ranvier-dom`. Plan: [server HTML and hydration](../superpowers/plans/2026-10-19-server-html-hydration.md).

## First content on .NET

Render time and allocated bytes per render, BenchmarkDotNet 0.15.8, Release. Each row is one `results/*-report-full-compressed.json` under `framework-performance/investigation/server-html/`.

| Phase | Fixture | Rows | Mean | Allocated | HTML bytes per row |
|---|---|---|---|---|---|
```

Add one table row per fixture and row count from the `dotnet-before-s1` results, with phase `before S1` (Mean in µs, Allocated in KB, bytes per row from `bytes-before-s1.json`).

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/ServerBench/ServerBench.fs
git add experiments/Ranvier.View.Portable/ServerBench/ServerBench.fsproj experiments/Ranvier.View.Portable/ServerBench/ServerBench.fs Ranvier.slnx docs/research/2026-10-19-server-html-hydration.md docs/research/framework-performance/investigation/server-html
git diff --cached --check
git commit -F - <<'EOF'
perf(view): add the .NET first-content benchmark and record today's writer

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 4: Server plan, one writer, escaping fast path (S1)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Representation.fs` (`Html.escape`; new `HtmlSink`; `Html.render`'s object expression becomes the class `HtmlBackend`; `TemplateDefinition.RenderHtml`)
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (`elementNode`, `TemplateComponent` fields, `TemplateBuilder.arm`, `writeMarkup` loses its `Server` mode, new plan compiler, `ServerScope`, `HtmlScope`, `ServerWriter`, `Component<'props>.RenderHtml`)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`
- Create (benchmark output): `docs/research/framework-performance/investigation/server-html/dotnet-after-s1/`, `bytes-after-s1.json`; modify `docs/research/2026-10-19-server-html-hydration.md`

**Interfaces:**
- Consumes: `ServerFixtures`, `ServerChecks.equal`, `ServerChecks.failure` (Task 2).
- Produces (module `Ranvier.View.Foundation.Representation`):
  - `Html.escape (attribute: bool) (value: string) : string`, unchanged signature; returns `value` itself when it holds no character to escape.
  - `[<Sealed>] type HtmlSink()` with `Append: string -> unit`, `Mark: unit -> int`, `Truncate: int -> unit` (drops everything appended after the mark), `Segments: (string -> unit) -> unit` (writes the buffer in order) and `ToString`.
  - `type HtmlBackend(policy: BrowserPolicy, sink: HtmlSink)` (in the `Backend` recursive group), inheriting `Backend`, with every member of today's object expression, plus `member Policy: BrowserPolicy`, `member Sink: HtmlSink`, `member Append: string -> unit` (applies the pending leading-line-feed rule, then appends), `member val internal Writer: obj = null with get, set` (the active `ServerWriter`). `Template(definition, props)` calls `definition.RenderHtml (this, props)`. `Boundary` marks the sink, renders the content, and on failure truncates to the mark before rendering the pending or error view.
  - `TemplateDefinition.RenderHtml: HtmlBackend * obj -> unit` (replaces `Backend * Setup * obj * bool * (string -> unit) -> unit`).
  - `Html.render policy build` = a new `HtmlSink`, `build (HtmlBackend (policy, sink))`, `sink.ToString ()`.
- Produces (module `Ranvier.View.Foundation.Template`):
  - `type internal ServerOp = Static of string | TextOp of Slot * leadingLineFeed: bool | AttributeOp of string * Slot | BrowserOp of description: string | RegionOp of index: int * Region | EnterOp of child: int | LeaveOp`
  - `type internal ServerPlan = { Ops: ServerOp array; Children: InlinedChild array }`
  - `serverPlan (definition: TemplateComponent) : ServerPlan` (internal): compiled on first use, published with `Interlocked.CompareExchange` on .NET (plain assignment under Fable), the same object afterwards.
  - `type internal ServerScope` with `abstract Nest: (ServerScope -> Setup -> unit) -> unit` (runs the body with the scope and `Setup` of a new nested instance) and `abstract Attempt: (ServerScope -> unit) -> unit` (runs the body in a new nested scope; when it raises, that scope's cleanups run before the exception propagates).
  - `type internal HtmlScope(backend: HtmlBackend)`: `Nest` runs the body inside `backend.Component`, so an instance's cleanups run after its markup is written (today's `Html.render` semantics); `Attempt` runs the body in this scope.
  - `type internal ServerWriter(backend: HtmlBackend)` with `member Write: scope: ServerScope * definition: TemplateComponent * props: obj -> unit`.
  - `TemplateComponent.ArmReader` is published with `Interlocked.CompareExchange` (plain assignment under Fable).

Interpretation contract of `ServerWriter.Write` (Task 6 adds positions, Task 7 properties, Task 9 boundaries, Task 10 markers):

| Op | Writes |
|---|---|
| `Static s` | `s` through `backend.Append` |
| `TextOp (slot, lf)` | `Html.escape false` of the slot's value read from the current part (`null` as `""`); with `lf`, a value starting with `\n` gets one more `\n` before it |
| `AttributeOp (name, slot)` | ` name="<escaped>"` for `Some`, nothing for `None` |
| `BrowserOp d` | under `Reject` raises `InvalidOperationException ("Browser capability requires explicit Omit policy: " + d)`; under `Omit` nothing |
| `RegionOp (_, region)` | items copied from `region.Items`, `Projection.ensureUniqueKeys region.Key.Invoke items`; `ItemRow`/`ArmRow`: `<!--[-->`, each row written by this writer (`scope.Nest` per row), `<!--]-->`; `ViewRow`: `<!--[-->`, the view run against `backend`, `<!--]-->`; `BoundaryRow`: `backend.Boundary views` |
| `EnterOp k` | sets up child `k` in a `scope.Nest` with the props read from its owning part; the child becomes the current part |
| `LeaveOp` | restores the enclosing part |

Plan compilation walks `flatten` as `writeMarkup` does: separators `<!--!-->` between adjacent text nodes, static attributes in order then attribute slots in order, `BrowserOp` before the element's open tag for each property, property slot and listener (description `"<tag> property"` or `"<tag> listener <event>"`), raw-text elements raise `InvalidOperationException "Raw-text elements require an explicit HTML serialization policy"` at compile, `pre`/`textarea`/`listing` set `leadingLineFeed` on a first-child text slot and double a first-child static text's leading `\n` in the static segment. Adjacent `Static` segments are merged into one.

- [ ] **Step 1: Write the failing tests**

In `ServerChecks.fs`, insert before `let cases`:

```fsharp
let private escapeCorpus () =
    equal "&lt;&amp;&gt;&#13;\"'" (Html.escape false "<&>\r\"'")
    equal "&lt;&amp;&gt;&#13;&quot;'" (Html.escape true "<&>\r\"'")
    equal "\uD83D\uDE00 plain" (Html.escape false "\uD83D\uDE00 plain")
    equal "&lt;!--]--&gt;" (Html.escape false "<!--]-->")
    equal "&lt;/script&gt;" (Html.escape true "</script>")
    equal "a&amp;&amp;b" (Html.escape false "a&&b")
    equal "" (Html.escape true "")

let private namesAtDefinition () =
    let define (part: TemplateBuilder<unit> -> TemplateView<unit>) () =
        Component.define (fun _ () -> ()) part |> ignore

    let rejected =
        [
            failure (define (fun t -> t.div [| t.Attrs.attribute ("onclick", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("srcdoc", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("Title", "x") |] [||]))
            failure (define (fun t -> t.div [| t.Attrs.attribute ("", "x") |] [||]))
            failure (define (fun t -> t.element (HtmlTag "DIV") [||] [||]))
            failure (define (fun t -> t.element (HtmlTag "") [||] [||]))
        ]

    equal [ true; true; true; true; true; true ] [ for error in rejected -> error.Message.Contains "names must" ]

let private sharedPlan () =
    equal true (obj.ReferenceEquals (serverPlan greeting, serverPlan greeting))

    match (serverPlan staticTemplate).Ops with
    | [| Static _ |] -> ()
    | ops -> failwithf "Expected one merged static segment; got %d operations" ops.Length

let private browserRejection () =
    let error = failure (fun () -> Html.render Reject (Program.run (Component.render clicking greetingProps)) |> ignore)
    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "<button> listener click")
```

Add to `cases`:

```fsharp
        "escape corpus is byte-identical on .NET and Fable", escapeCorpus
        "attribute and tag names outside the validated set fail at definition time", namesAtDefinition
        "a definition's server plan is compiled once and merges adjacent static markup", sharedPlan
        "a browser-only part under Reject names the element and the part", browserRejection
```

Replace `dotnetCases` with:

```fsharp
#if !FABLE_COMPILER
let private escapeKeepsReference () =
    let text = "plain text without markup"
    equal true (obj.ReferenceEquals (text, Html.escape false text))
    equal true (obj.ReferenceEquals (text, Html.escape true text))

/// <summary>Runs <c>count</c> calls of <c>body</c> on pool threads released together.</summary>
let private together (count: int) (body: unit -> 'value) : 'value array =
    use barrier = new System.Threading.Barrier (count)

    [|
        for _ in 1..count ->
            System.Threading.Tasks.Task.Run (fun () ->
                if not (barrier.SignalAndWait (TimeSpan.FromSeconds 10.)) then
                    failwith "Barrier timed out"

                body ())
    |]
    |> Array.map _.Result

let private concurrentPublication () =
    let fresh = Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "fresh" |])
    let plans = together 8 (fun () -> serverPlan fresh)
    equal true (plans |> Array.forall (fun plan -> obj.ReferenceEquals (plan, plans[0])))
    let arm = Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.span [||] [||])
    let readers = together 8 (fun () -> (TemplateBuilder<unit>().arm arm 1).Reader)
    equal true (readers |> Array.forall (fun reader -> obj.ReferenceEquals (reader, readers[0])))

let dotnetCases: (string * (unit -> unit)) array =
    [|
        "escape returns the input string when nothing needs escaping", escapeKeepsReference
        "concurrent first renders publish one plan and one arm reader", concurrentPublication
    |]
#endif
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the compile fails: `The value or constructor 'serverPlan' is not defined`.

- [ ] **Step 3: Implement the escaping fast path and the HTML backend class**

In `Representation.fs`: rewrite `Html.escape` as one scan for the first character to escape (`&`, `<`, `>`, `\r`, and `"` when `attribute`), returning `value` when there is none; otherwise copy the prefix and write the rest with a `StringBuilder` (.NET) or a `ResizeArray<string>` joined once (Fable). Add `HtmlSink` (a `StringBuilder` on .NET with `Mark = Length` and `Truncate = Length <- mark`; a `ResizeArray<string>` under Fable with `Mark = Count` and `Truncate = RemoveRange`). Move the object expression of `Html.render` into `HtmlBackend` with the members listed in Interfaces; `Html.render` builds one and returns `sink.ToString ()`.

- [ ] **Step 4: Implement the plan, the scopes and the writer**

In `Template.fs`: `elementNode` calls `name tag |> ignore` before building the node. Give `TemplateComponent` two `[<DefaultValue>] val mutable internal` fields, `serverPlan: obj` and `armReader: Func<(unit -> obj), obj>`, published as Interfaces says; `ArmReader` becomes a read of `armReader`. Remove `MarkupMode.Server` and `renderHtml`; `writeMarkup` keeps the skeleton path. Add the plan compiler, `serverPlan`, `ServerScope`, `HtmlScope` and `ServerWriter` with the interpretation contract above. `Component<'props>.RenderHtml (backend, props)` takes `backend.Writer` (creating and storing a `ServerWriter backend` when null) and calls `Write (HtmlScope backend, this, props)`.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line, including the Task 2 pins unchanged and the six new cases.

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/server.test.mjs tests/template.test.mjs tests/boundary.test.mjs tests/representation.test.mjs`
Expected: PASS.

- [ ] **Step 6: Record the benchmark after S1**

Run Task 3 Step 3 with `dotnet-before-s1` and `bytes-before-s1` replaced by `dotnet-after-s1` and `bytes-after-s1`, and add the rows with phase `after S1` to the research note's table.

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Representation.fs experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs
git add experiments/Ranvier.View.Portable/Representation.fs experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs docs/research/2026-10-19-server-html-hydration.md docs/research/framework-performance/investigation/server-html
git diff --cached --check
git commit -F - <<'EOF'
feat(view): write template server HTML from a compiled plan with an allocation-free escape path

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 5: `Server.render` with a request graph and request-owned instances (S2)

**Files:**
- Create: `experiments/Ranvier.View.Portable/Server.fs`
- Modify: `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj` (compile `Server.fs` after `Generated/HtmlTemplate.fs`), `experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs` (`renderComponentPage`, `renderKeyedPage`), `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (link `Server.fs` after the `HtmlTemplate.fs` link)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`
- Create (benchmark output): `dotnet-after-s2/`, `bytes-after-s2.json`

**Interfaces:**
- Consumes: `ServerWriter`, `ServerScope`, `HtmlBackend`, `HtmlSink` (Task 4); `MountLifetime.Root`, `MountLifetime.Child`, `MountLifetime.OnCleanup`, `MountLifetime.Dispose` (`Lifetime.fs`).
- Produces (module `Ranvier.View.Foundation.Server`):
  - `type Options = { Policy: BrowserPolicy; IdPrefix: string; GraphOptions: GraphOptions }` and `Options.defaults = { Policy = Reject; IdPrefix = "r"; GraphOptions = GraphOptions.Default }`.
  - `render (options: Options) (definition: Component<'props>) (props: 'props) : string` — `<!--[r-->`, the instance, `<!--]-->`.
  - `renderTo (options: Options) (definition: Component<'props>) (props: 'props) (write: string -> unit) : unit` — after a successful render, `write` receives the output's segments in order; a failed render calls `write` never.
  - `renderOn (graph: Graph) (options: Options) (definition: Component<'props>) (props: 'props) (sink: HtmlSink) : unit` (internal) — renders into `sink` inside `graph`, disposing the request root before it returns or raises; `render` and `renderTo` create `new Graph (options.GraphOptions)` and dispose it after `renderOn`.
  - `type internal RequestScope(lifetime: MountLifetime)` implementing `ServerScope`: `Nest` creates `lifetime.Child (ignore, ...)` and runs the body inside it with a `Setup` whose `OnCleanup` registers on that child while its setup runs and raises `InvalidOperationException "Server component setup is completed"` afterwards; `Attempt` creates a child, runs the body inside it, and on an exception disposes the child, then reraises.

Disposal and errors: cleanup errors reported by the request root's lifetime are collected. After disposal, a render that raised `e` raises `e` when no cleanup failed, otherwise `AggregateException (e :: cleanupErrors)`; a successful render whose cleanups failed raises `AggregateException ("Server render cleanup failed", cleanupErrors)`. The whole render runs inside `graph.Run` and `Api.untrack`. (Task 9 turns a pending read outside every boundary into `ServerPendingException`.)

- [ ] **Step 1: Write the failing tests**

In `ServerChecks.fs`, insert before `let cases`:

```fsharp
let private requestRoot () =
    let log = ResizeArray<string>()
    let owners = ResizeArray<Owner>()
    let graphs = ResizeArray<Graph>()

    let child =
        Component.define
            (fun setup (name: string) ->
                owners.Add (Api.getOwner ())
                setup.OnCleanup (fun () -> log.Add ("cleanup " + name))
                Api.createSignal name)
            (fun t -> t.span [||] [| t.text (fun (name: Signal<string>) -> name.Value) |])

    let parent =
        Component.define
            (fun setup () ->
                graphs.Add Graph.Current
                owners.Add (Api.getOwner ())
                setup.OnCleanup (fun () -> log.Add "cleanup parent"))
            (fun t -> t.div [||] [| t.render child (fun _ -> "a"); t.render child (fun _ -> "b") |])

    equal "<!--[r--><div><span>a</span><span>b</span></div><!--]-->" (Server.render Server.Options.defaults parent ())
    equal ("cleanup parent", [ "cleanup a"; "cleanup b"; "cleanup parent" ]) (Seq.last log, List.sort (List.ofSeq log))
    equal true (owners |> Seq.forall _.IsDisposed)
    equal true graphs[0].Root.IsDisposed

let private setupFailureDisposes () =
    let log = ResizeArray<string>()
    let owners = ResizeArray<Owner>()

    let cleaned =
        Component.define
            (fun setup () ->
                owners.Add (Api.getOwner ())
                setup.OnCleanup (fun () -> log.Add "first"))
            (fun t -> t.span [||] [||])

    let throwing =
        Component.define (fun _ () -> failwith "second failed") (fun t -> t.span [||] [||])

    let parent =
        Component.define (fun setup () -> setup.OnCleanup (fun () -> log.Add "parent")) (fun t ->
            t.div [||] [| t.render cleaned ignore; t.render throwing ignore |])

    let error = failure (fun () -> Server.render Server.Options.defaults parent () |> ignore)
    equal ("second failed", [ "first"; "parent" ], true) (error.Message, List.ofSeq log, owners |> Seq.forall _.IsDisposed)

let private cleanupAfterSetupRejected () =
    let mutable captured: Setup option = None

    let capturing =
        Component.define (fun setup () -> captured <- Some setup) (fun t -> t.span [||] [||])

    Server.render Server.Options.defaults capturing () |> ignore
    let error = failure (fun () -> captured.Value.OnCleanup ignore)
    equal true (error :? InvalidOperationException)

let private duplicateKeys () =
    let items = [| { Id = 1; Text = "a" }; { Id = 1; Text = "b" } |]
    let expected = failure (fun () -> Projection.ensureUniqueKeys (fun (item: obj) -> box (unbox<Listed> item).Id) (Array.map box items))
    let actual = failure (fun () -> Server.render { Server.Options.defaults with Policy = Omit } listed items |> ignore)
    equal (true, expected.Message) (actual :? InvalidOperationException, actual.Message)

let private renderToSegments () =
    let written = ResizeArray<string>()
    Server.renderTo Server.Options.defaults named greetingProps written.Add
    equal (Server.render Server.Options.defaults named greetingProps) (String.concat "" written)
    let failed = ResizeArray<string>()
    let throwing = Component.define (fun _ () -> failwith "setup") (fun t -> t.span [||] [||])
    failure (fun () -> Server.renderTo Server.Options.defaults throwing () failed.Add) |> ignore
    equal 0 failed.Count
```

Add to `cases`:

```fsharp
        "a request owns every instance and disposes them child-first after the render", requestRoot
        "setup failure disposes the request", setupFailureDisposes
        "OnCleanup after setup raises on the server", cleanupAfterSetupRejected
        "duplicate keys fail the server render with the core's exception", duplicateKeys
        "renderTo writes the rendered segments after success and nothing after failure", renderToSegments
```

Add to `dotnetCases` (before its closing `|]`), with these functions inserted after `concurrentPublication`:

```fsharp
let private concurrentRequests () =
    use barrier = new System.Threading.Barrier (2)
    let graphs = System.Collections.Concurrent.ConcurrentBag<Graph>()

    let waiting =
        Component.define
            (fun _ (name: string) ->
                graphs.Add Graph.Current

                if not (barrier.SignalAndWait (TimeSpan.FromSeconds 10.)) then
                    failwith "Barrier timed out"

                Api.createSignal name)
            (fun t -> t.span [||] [| t.text (fun (name: Signal<string>) -> name.Value) |])

    let request name =
        System.Threading.Tasks.Task.Run (fun () -> Server.render Server.Options.defaults waiting name)

    let left, right = request "left", request "right"
    equal ("<!--[r--><span>left</span><!--]-->", "<!--[r--><span>right</span><!--]-->") (left.Result, right.Result)
    equal [ true; true ] [ for graph in graphs -> graph.Root.IsDisposed ]
    equal false (obj.ReferenceEquals (Seq.head graphs, Seq.last graphs))

/// <summary>Reads and writes a signal of a graph outside the request from a request on a pool thread.</summary>
let private sharedSignal (affinity: ThreadAffinity) =
    use outer = new Graph (GraphOptions.Default.WithThreadAffinity affinity)
    let shared = outer.Run (fun () -> Api.createSignal "s")
    let reading = Component.define (fun _ () -> shared) (fun t -> t.span [||] [| t.text (fun (s: Signal<string>) -> s.Value) |])
    let writing = Component.define (fun _ (value: string) -> shared.Value <- value) (fun t -> t.span [||] [||])

    let onPool (body: unit -> string) =
        try
            Ok (System.Threading.Tasks.Task.Run(body).Result)
        with :? AggregateException as error ->
            Error error.InnerException

    equal (Ok "<!--[r--><span>s</span><!--]-->") (onPool (fun () -> Server.render Server.Options.defaults reading ()))

    match affinity, onPool (fun () -> Server.render Server.Options.defaults writing "w") with
    | ThreadAffinity.Guarded, Error error -> equal true (error :? InvalidOperationException)
    | ThreadAffinity.Unchecked, Ok _ -> equal (Ok "<!--[r--><span>w</span><!--]-->") (onPool (fun () -> Server.render Server.Options.defaults reading ()))
    | _, outcome -> failwithf "Unexpected outcome %A under %O" outcome affinity
```

```fsharp
        "two concurrent requests render their own props", concurrentRequests
        "a module-level signal read by two requests is shared: a Guarded graph rejects a request's write",
        fun () -> sharedSignal ThreadAffinity.Guarded
        "a module-level signal read by two requests is shared: an Unchecked graph takes a request's write",
        fun () -> sharedSignal ThreadAffinity.Unchecked
```

`equal (Ok ...) (onPool ...)` compares `Result<string, exn>` values; the `Ok` side carries the string only.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the compile fails: `The namespace or module 'Server' is not defined`.

- [ ] **Step 3: Implement `Server.fs`**

`module Ranvier.View.Foundation.Server` with the members of Interfaces. `renderOn` runs in `graph.Run`: `MountLifetime.Root (graph, collect, ignore)` where `collect` appends to the request's error list; appends `<!--[r-->`; `ServerWriter(HtmlBackend (options.Policy, sink)).Write (RequestScope root, definition, box props)` inside `Api.untrack`; appends `<!--]-->`; disposes `root` in a `finally`; then raises as the disposal paragraph above says. Document `render` with the spec's summary and a `<remarks>` that names the shared-signal behaviour pinned above: a signal of a graph outside the request is read as it is; a write from the request thread raises under `Guarded` and is shared state under `Unchecked`; under Fable `render` is synchronous only.

- [ ] **Step 4: Measure the requests with `Server.render`**

In `ServerFixtures.fs` replace the bodies of `renderComponentPage` and `renderKeyedPage` with `Server.render { Server.Options.defaults with Policy = Omit } componentPage rows` and `Server.render { Server.Options.defaults with Policy = Omit } keyedPage rows`.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line. If `sharedSignal Guarded` reports another outcome than `InvalidOperationException`, the core's affinity behaviour differs from the spec's reading: stop and report the observed outcome.

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/server.test.mjs`
Expected: PASS.

- [ ] **Step 6: Record the benchmark after S2**

Run Task 3 Step 3 with `dotnet-after-s2` and `bytes-after-s2`, and add rows with phase `after S2`. The bytes grow by the 18 bytes of the root marker pair.

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs
git add experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj docs/research/2026-10-19-server-html-hydration.md docs/research/framework-performance/investigation/server-html
git diff --cached --check
git commit -F - <<'EOF'
feat(view): render a template as one request with its own graph and owners

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 6: Positional identity and `Setup.CreateId` on server and client (S2, Decision 3)

**Files:**
- Create: `experiments/Ranvier.View.Portable/Identity.fs`, `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`, `examples/Fable.Ranvier.Playground/tests/hydration.test.mjs`
- Modify: `experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj` (compile `Identity.fs` after `Scope.fs`), `Representation.fs` (`Setup`, `HtmlBackend`), `Template.fs` (`ServerWriter.Write`, `HtmlScope`), `Server.fs` (`RequestScope`, `renderOn`), `Checks/ServerFixtures.fs`, `Checks/ServerChecks.fs`
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`CompiledRegion.Index`, `MountServices`, `materialise`, `setUp`, `setUpChild`), `Mounting.fs` (`RowBuilder`, `RegionSite.NextRow`, `MountState` constructor, `Component`, `Template`, `MountRow`, `RegionOf` and the region members, `mountWith`), `Browser.fs`, `examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs` (their `Setup` records)
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (link `Identity.fs` after the `Scope.fs` link; compile `HydrationChecks.fs` after the `Checks/ServerChecks.fs` link, which follows `OwnedAsyncChecks.fs`), `package.json` (`test:dom` gains `tests/hydration.test.mjs`)

**Interfaces:**
- Produces (namespace `Ranvier.View.Foundation`, file `Identity.fs`):
  - `module InstanceStep` with literals `Root = 0`, `Child = 1`, `Row = 2`, `Authored = 3`.
  - `[<Sealed; AllowNullLiteral>] type InstancePath` with `static member Root: InstancePath` (text `0`), `member Child: k: int -> InstancePath` (`<text>.c<k>`), `member Row: region: int * row: int -> InstancePath` (`<text>.r<region>.<row>`), `member Authored: k: int -> InstancePath` (`<text>.a<k>`), `member Text: string` (built once).
  - `[<Sealed>] type IdentityScope(prefix: string)` with `member Prefix: string`; `member Enter: parent: InstancePath * kind: int * a: int * b: int -> unit` (opens the setup window of the instance one step below `parent`: `InstanceStep.Root` ignores `parent`; `Child` uses `a`; `Row` uses `a` as region and `b` as row; `Authored` uses `a`); `member Leave: unit -> unit`; `member Path: unit -> InstancePath` (the open window's path, allocated on the first call within the window); `member CreateId: unit -> string` (`prefix + "-" + path.Text + "-" + string n`, `n` from 0 within the window; outside a window raises `InvalidOperationException "CreateId is valid only while a component's setup runs"`); `member CreateAuthoredId: path: InstancePath -> string` (`prefix + "-" + path.Text + "-a" + string n`, `n` counting across the scope).
- Produces (Representation): `type Setup = { OnCleanup: (unit -> unit) -> unit; CreateId: unit -> string }`. `HtmlBackend` gains `member val Identity: IdentityScope = IdentityScope "r" with get, set` and `member val internal Path: InstancePath = InstancePath.Root with get, set` (the path authored content sits under); its `Component` gives `CreateId = fun () -> this.Identity.CreateAuthoredId this.Path`.
- Produces (Template): `ServerWriter.Write: scope: ServerScope * definition: TemplateComponent * props: obj * parent: InstancePath * kind: int * a: int * b: int -> unit` (replaces Task 4's three-argument `Write`). The writer brackets each setup with `backend.Identity.Enter`/`Leave`; a row of region `i` of part `p` is written at `(pathOf p, InstanceStep.Row, i, rowIndex)`, `rowIndex` counting the region's rows from 0; an inlined child `k` at `(instancePath, InstanceStep.Child, k, 0)`; a view row sets `backend.Path` to the row's path while its view runs; a template reached from authored content (`RenderHtml` with no writer position set) is written at `(backend.Path, InstanceStep.Authored, n, 0)`, `n` counting per writer. `RequestScope` gives `CreateId = backend.Identity.CreateId`; `renderOn` sets `backend.Identity <- IdentityScope options.IdPrefix`.
- Produces (TemplateDom): `CompiledRegion.Index: int` (the region's position among its owning part's regions); `MountServices.Identity: IdentityScope`; `MountServices.Region` gains two trailing arguments `InstancePath * int` (the owning part's path and the region index); a region's row materialiser becomes `Func<(unit -> obj), MountServices, int, unit>` (the third argument is the row index); `materialise (definition: TemplateDefinition) (props: obj) (services: MountServices) (parent: InstancePath) (kind: int) (a: int) (b: int)`.
- Produces (Mounting): `RegionSite.NextRow: unit -> int`; `MountState` gains constructor parameters `identity: IdentityScope, parentPath: InstancePath, kind: int, a: int, b: int` (the position its root template takes); `mountWith` passes `IdentityScope "r"`, `null`, `InstanceStep.Root`, `0`, `0`; `MountState.MountRow(current: unit -> obj, render: (unit -> obj) -> Backend -> unit, scope: ViewScope, parentPath: InstancePath, region: int, row: int) : Row` (the nested mount's root template takes `(parentPath, InstanceStep.Row, region, row)`).

Paths depend on structure only. On the client `materialise` calls `Enter` before the setup and `Leave` after it, and calls `Path ()` before `Leave` only when the compiled form has regions or inlined children, so a row without them allocates no path.

- [ ] **Step 1: Write the identity fixture and the failing tests**

Append to `ServerFixtures.fs`:

```fsharp
/// <summary>The ids issued while the identity fixtures set up, in setup order.</summary>
let identityLog = ResizeArray<string>()

let idLeaf =
    Component.define (fun setup () -> identityLog.Add (setup.CreateId ())) (fun t -> t.element (HtmlTag "i") [||] [||])

let idCell =
    Component.define
        (fun setup (item: unit -> int) ->
            identityLog.Add (setup.CreateId ())
            item)
        (fun t -> t.element (HtmlTag "b") [||] [| t.text (fun item -> string (item ())) |])

let idRow =
    Component.define
        (fun setup (item: unit -> int) ->
            identityLog.Add (setup.CreateId ())
            item)
        (fun t -> t.li [||] [| t.render idLeaf ignore; t.each id (fun (item: unit -> int) -> [| item () * 10 |]) idCell |])

let idArm =
    Component.define
        (fun setup (count: unit -> int) ->
            identityLog.Add (setup.CreateId ())
            count)
        (fun t -> t.span [||] [| t.text (fun count -> string (count ())) |])

let identified =
    Component.define
        (fun setup (items: int array) ->
            identityLog.Add (setup.CreateId ())
            identityLog.Add (setup.CreateId ())
            items)
        (fun t ->
            t.div
                [||]
                [|
                    t.render idLeaf ignore
                    t.ul [||] [| t.each id id idRow |]
                    t.render idLeaf ignore
                    t.matchWith (fun (items: int array) -> items.Length) (fun count -> t.arm idArm count)
                |])

/// <summary>The ids <c>identified</c> issues for the items 7 and 8 under <c>prefix</c>, sorted.</summary>
let identityIds (prefix: string) =
    [
        "0-0"; "0-1"; "0.c0-0"; "0.c1-0"
        "0.r0.0-0"; "0.r0.0.c0-0"; "0.r0.0.r0.0-0"
        "0.r0.1-0"; "0.r0.1.c0-0"; "0.r0.1.r0.0-0"
        "0.r1.0-0"
    ]
    |> List.map (fun id -> prefix + "-" + id)
    |> List.sort
```

In `ServerChecks.fs`, insert before `let cases`:

```fsharp
let private serverIdentity () =
    identityLog.Clear ()
    Server.render { Server.Options.defaults with IdPrefix = "a" } identified [| 7; 8 |] |> ignore
    equal (identityIds "a") (List.sort (List.ofSeq identityLog))

let private createIdAfterSetup () =
    let mutable captured: Setup option = None
    let capturing = Component.define (fun setup () -> captured <- Some setup) (fun t -> t.span [||] [||])
    Server.render Server.Options.defaults capturing () |> ignore
    equal true (failure (fun () -> captured.Value.CreateId () |> ignore) :? InvalidOperationException)

let private authoredIds () =
    let ids = ResizeArray<string>()

    let authored =
        Program.createComponent (fun setup ->
            ids.Add (setup.CreateId ())
            Program.text "x")

    Html.render Reject (Program.run (Program.fragment [| authored; authored |])) |> ignore
    identityLog.Clear ()
    Html.render Reject (Program.run (Component.render idLeaf ())) |> ignore
    equal ([ "r-0-a0"; "r-0-a1" ], [ "r-0-0" ]) (List.ofSeq ids, List.ofSeq identityLog)
```

Add to `cases`:

```fsharp
        "CreateId follows the instance path for inlined children before and after regions, nested rows and arms", serverIdentity
        "CreateId after setup raises", createIdAfterSetup
        "authored components receive ids distinct from template ids", authoredIds
```

Create `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`:

```fsharp
/// <summary>Hydration checks: jsdom through tests/hydration.test.mjs, Chromium through tests/hydration.spec.mjs.</summary>
module HydrationChecks

open System
open global.Ranvier
open Fable.Core
open Fable.Core.JsInterop
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open ServerFixtures

[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private host () : HTMLElement =
    globalThis.document.createElement "main"

let private strictReport (errors: exn list) =
    failwithf "Unexpected report %A" [ for error in errors -> error.Message ]

let private mount graph (host: HTMLElement) view =
    Mounting.mount graph None strictReport host None (fun context -> Program.run view context.Backend)

let private clientIdentity () =
    use graph = new Graph ()
    identityLog.Clear ()
    let mounted = mount graph (host ()) (Component.render identified [| 7; 8 |])
    equal (identityIds "r") (List.sort (List.ofSeq identityLog))
    mounted.Dispose ()

let private clientCreateIdAfterSetup () =
    use graph = new Graph ()
    let mutable captured: Setup option = None
    let capturing = Component.define (fun setup () -> captured <- Some setup) (fun t -> t.span [||] [||])
    let mounted = mount graph (host ()) (Component.render capturing ())
    let mutable rejected = false

    try
        captured.Value.CreateId () |> ignore
    with :? InvalidOperationException ->
        rejected <- true

    equal true rejected
    mounted.Dispose ()

let cases: (string * (unit -> unit)) array =
    [|
        "a client mount issues the server's ids for the same structure", clientIdentity
        "CreateId after a client setup raises", clientCreateIdAfterSetup
    |]

let browserCases: (string * (unit -> unit)) array = [||]
```

Create `examples/Fable.Ranvier.Playground/tests/hydration.test.mjs`:

```js
import test, { after } from "node:test";
import { JSDOM } from "jsdom";
import { cases } from "../generated-tests/HydrationChecks.js";

const dom = new JSDOM("<!doctype html><body></body>");
for (const name of ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event"]) globalThis[name] = name === "window" ? dom.window : dom.window[name];
globalThis.document = dom.window.document;
after(() => { dom.window.close(); for (const name of ["window", "document", "FocusEvent", "MutationObserver", "Node", "Event"]) delete globalThis[name]; });
for (const [name, check] of cases) test(name, () => check());
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the compile fails: `The record label 'CreateId' is not defined`.

- [ ] **Step 3: Implement `Identity.fs` and the portable sites**

Write `Identity.fs` to the Interfaces block. Add `CreateId` to `Setup`; give `HtmlBackend` its `Identity` and `Path`; extend `ServerWriter.Write` and number regions per part and inlined children in `ServerPlan.Children` order (`RegionOp`'s index is already the per-part index from Task 4's compiler; check it counts per part); `RequestScope` and `renderOn` as Interfaces says.

- [ ] **Step 4: Implement the client sites**

In `TemplateDom.compile`, set `CompiledRegion.Index` by counting earlier region targets with the same owner. In `materialise`, bracket `setUp` with `services.Identity.Enter (parent, kind, a, b)` and `Leave ()`, taking `Path ()` before `Leave` when `compiled.Regions.Length > 0 || compiled.Children.Length > 0`; bracket each `setUpChild` with `Enter (path, InstanceStep.Child, k, 0)`; give each part that owns a region its path (`path` for part 0, `path.Child (k - 1)` for part `k`), and pass `(partPath, region.Index)` to `services.Region`; build each row materialiser to call `materialise rowDefinition (rowProps.Invoke current) rowServices partPath InstanceStep.Row region.Index row`. In `Mounting.fs`, `RowBuilder.Build` takes the row index from `site.NextRow ()` and passes it to the materialiser; `RowBuilder`'s and `MountState`'s template `Setup` records give `CreateId = identity.CreateId`; `MountState.Component` gives `CreateId = fun () -> identity.CreateAuthoredId mountPath` (`mountPath` built once from the constructor's position); `MountState.Template` calls `materialise definition props this parentPath kind a b`; `MountRow` passes the boundary's or hole's row position (`partPath`, `InstanceStep.Row`, region index, `site.NextRow ()`) to the nested `MountState`. In `Browser.fs` and `BindingBaselineMounting.fs`, each backend holds one `IdentityScope "r"` and gives `CreateId = fun () -> identity.CreateAuthoredId InstancePath.Root`.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/server.test.mjs tests/hydration.test.mjs tests/template.test.mjs tests/boundary.test.mjs`
Expected: PASS.

- [ ] **Step 6: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 7: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Identity.fs experiments/Ranvier.View.Portable/Representation.fs experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/Browser.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Portable/Identity.fs experiments/Ranvier.View.Portable/Ranvier.View.Portable.fsproj experiments/Ranvier.View.Portable/Representation.fs experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Portable/Checks/ServerFixtures.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/Browser.fs examples/Fable.Ranvier.Playground/benchmarks/BindingBaselineMounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs examples/Fable.Ranvier.Playground/tests/hydration.test.mjs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/package.json
git diff --cached --check
git commit -F - <<'EOF'
feat(view): issue positional instance ids through Setup.CreateId on server and client

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 7: Portable property metadata and the browser-only fallback (S3)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (new `PropertySerialization`, `PortableProperty`, `PropertyHtml`; `SlotTarget.PropertyTarget`, `Part.StaticProperty`, `TemplateNode.ElementNode`'s property array, `TemplateParts.property`/`readProperty`/`sourceProperty`, `PropertyBuilder` overloads, `expandNode`, plan compiler, writer)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`Compiled.Properties`, `tables`, `write`, `bind`)
- Modify: `tools/Ranvier.Dom.Generate/Program.fs` (`propertyMembers`, new `portableProperties` table); regenerate `experiments/Ranvier.View.Portable/Generated/HtmlTemplate.fs`
- Modify: `tools/Ranvier.Dom.Generate/tests.py` (two assertions)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`
- Create (benchmark output): `dotnet-after-s3/`, `bytes-after-s3.json`

**Interfaces:**
- Produces (module `Ranvier.View.Foundation.Template`):

```fsharp
/// <summary>How a property part appears in server HTML.</summary>
[<NoEquality; NoComparison>]
type PropertySerialization =
    /// <summary>The attribute the property reflects; the writer turns a value into the attribute value, <c>None</c> omits it.</summary>
    | AsAttribute of name: string * write: Func<obj, string option>
    /// <summary>The element's escaped child text: a textarea's value.</summary>
    | AsChildText
    /// <summary>A property that exists in the browser only.</summary>
    | BrowserOnly

/// <summary>A property writer with its server serialisation.</summary>
[<NoEquality; NoComparison>]
type PortableProperty = { Write: Func<obj, obj, unit>; Html: PropertySerialization }
```

  - `module PropertyHtml` with `text (name: string) : PropertySerialization` (a string value; `null` omits), `number (name: string)` (a float written as `string (int64 v)` when integral, `string v` otherwise), `presence (name: string)` (a bool: `Some ""` or `None`), `optional (name: string)` (a `string option` as is), `childText = AsChildText`, `browserOnly = BrowserOnly`.
  - `SlotTarget.PropertyTarget of PortableProperty`; `Part.StaticProperty of PortableProperty * obj`; `ElementNode`'s third field `(PortableProperty * obj) array`.
  - `TemplateParts.property (html: PropertySerialization) (write: 'native -> 'value -> unit) (value: 'value)`, `TemplateParts.readProperty html write read`, `TemplateParts.sourceProperty html write source` (one new leading argument each).
  - `PropertyBuilder.propertyStatic(write, value)`, `propertyWith(write, read, same)` and `propertyFrom(write, source, same)` keep their signatures and use `BrowserOnly`; three new overloads take a trailing `html: PropertySerialization`.
  - New ops: `PropertyOp of PortableProperty * Slot` and `StaticPropertyOp of PortableProperty * obj` (` name="<escaped>"`, or the browser fallback for `BrowserOnly`); `ChildTextOp of PortableProperty * Slot * leadingLineFeed: bool` and `StaticChildTextOp of PortableProperty * obj * leadingLineFeed: bool` (written after the open tag). Task 4's `BrowserOp` stays for listeners only.
- Produces (TemplateDom): `Compiled.Properties: (int * PortableProperty * obj) array`; `write` calls `property.Write.Invoke`.
- Produces (generator): every `TemplateParts.property`/`readProperty`/`sourceProperty` call passes `PropertyHtml.<kind> "<attribute>"` from the table below, `PropertyHtml.browserOnly` otherwise. Generation fails when a table entry matches no emitted member.

| Member | Owner (`ownerName`) | Serialisation |
|---|---|---|
| `className` | `Dom.Element` | `text "class"` |
| `id` | `Dom.Element` | `text "id"` |
| `title` | `Dom.HTMLElement` | `text "title"` |
| `tabIndex` | `Dom.HTMLOrSVGElement` | `number "tabindex"` |
| `htmlFor` | `Dom.HTMLLabelElement` | `text "for"` |
| `disabled` | `Dom.HTMLButtonElement`, `Dom.HTMLFieldSetElement`, `Dom.HTMLInputElement`, `Dom.HTMLOptGroupElement`, `Dom.HTMLOptionElement`, `Dom.HTMLSelectElement`, `Dom.HTMLTextAreaElement` | `presence "disabled"` |
| `required` | `Dom.HTMLInputElement`, `Dom.HTMLSelectElement`, `Dom.HTMLTextAreaElement` | `presence "required"` |
| `readOnly` | `Dom.HTMLInputElement`, `Dom.HTMLTextAreaElement` | `presence "readonly"` |
| `multiple` | `Dom.HTMLInputElement`, `Dom.HTMLSelectElement` | `presence "multiple"` |
| `checked` | `Dom.HTMLInputElement` | `presence "checked"` |
| `selected` | `Dom.HTMLOptionElement` | `presence "selected"` |
| `value` | `Dom.HTMLButtonElement`, `Dom.HTMLDataElement`, `Dom.HTMLInputElement`, `Dom.HTMLOptionElement` | `text "value"` |
| `value` | `Dom.HTMLLIElement`, `Dom.HTMLMeterElement`, `Dom.HTMLProgressElement` | `number "value"` |
| `value` | `Dom.HTMLTextAreaElement` | `childText` |
| `aria*` (every member of `Dom.ARIAMixin` whose name starts with `aria`) | `Dom.ARIAMixin` | `optional ("aria-" + memberName.Substring(4).ToLowerInvariant ())` |

Order on an element: static attributes, then static property serialisations, then attribute and property slots in part order. An element whose static attribute names the attribute a serialised property writes raises `InvalidOperationException` at plan compile, naming the element and the attribute; so does a textarea with both a child-text property and children.

- [ ] **Step 1: Write the failing tests**

In `ServerChecks.fs`, insert before `let cases`:

```fsharp
let private serialised =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [| t.Props.className (fun _ -> "a<b"); t.Props.tabIndex (fun _ -> 2.0); t.Props.ariaBusy (fun _ -> Some "true") |]
            [|
                t.input [| t.Props.Input.value (fun _ -> "v\""); t.Props.Input.``checked`` (fun _ -> true); t.Props.Input.disabled (fun _ -> false) |]
                t.button [| t.Props.Button.disabled true |] [||]
            |])

let private browserOnlyProperty =
    Component.define (fun _ () -> ()) (fun t -> t.div [| t.Props.accessKey (fun _ -> "k") |] [| t.text "x" |])

let private serialisedProperties () =
    equal
        """<!--[r--><div class="a&lt;b" tabindex="2" aria-busy="true"><input value="v&quot;" checked=""><button disabled=""></button></div><!--]-->"""
        (Server.render Server.Options.defaults serialised ())

    equal "<!--[r--><div>x</div><!--]-->" (Server.render { Server.Options.defaults with Policy = Omit } browserOnlyProperty ())
    let error = failure (fun () -> Server.render Server.Options.defaults browserOnlyProperty () |> ignore)
    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "<div> property")

let private browserPartsUninvoked () =
    let mutable invoked = 0
    let hook = Program.element Div [| browserModifier (fun _ -> invoked <- invoked + 1; ignore) |] [||]

    let parts =
        Component.define (fun _ () -> ()) (fun t ->
            t.div [||] [| t.button [| t.Events.onClick (fun _ _ -> invoked <- invoked + 1) |] [||]; t.view (fun _ -> hook) |])

    equal "<!--[r--><div><button></button><!--[--><div></div><!--]--></div><!--]-->" (Server.render { Server.Options.defaults with Policy = Omit } parts ())
    failure (fun () -> Server.render Server.Options.defaults parts () |> ignore) |> ignore
    equal 0 invoked

let private duplicateAttributeSource () =
    let both =
        Component.define (fun _ () -> ()) (fun t -> t.div [| t.Attrs.className "x"; t.Props.className (fun _ -> "y") |] [||])

    let error = failure (fun () -> Server.render Server.Options.defaults both () |> ignore)
    equal (true, true) (error :? InvalidOperationException, error.Message.Contains "class")
```

Add to `cases`:

```fsharp
        "serialised properties write their attributes; browser-only properties raise under Reject and write nothing under Omit", serialisedProperties
        "listeners and browser hooks are never invoked on the server", browserPartsUninvoked
        "a property and a static attribute writing the same attribute fail at plan compile", duplicateAttributeSource
```

In `tools/Ranvier.Dom.Generate/tests.py`, after the `aria-expanded` assertion add:

```python
    assert "TemplateParts.readProperty (PropertyHtml.text \"class\")" in ttext
    assert "TemplateParts.readProperty PropertyHtml.childText (fun (node: 'native) (value: string) -> (node :> Dom.HTMLTextAreaElement).``value`` <- value) read" in ttext
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: FAIL at `serialisedProperties`: `Browser capability requires explicit Omit policy: <div> property`.

- [ ] **Step 3: Add the metadata types and thread them through the parts**

Implement the Template and TemplateDom items of Interfaces. `expandNode` passes `property.Write` where it passed the writer; `TemplateDom.tables` collects `PortableProperty` values.

- [ ] **Step 4: Emit the metadata from the generator and regenerate**

In `Program.fs`, add the table above as `portableProperties: (string * string * string) list` of `(memberName, ownerName, serialisation)` where `serialisation` is the F# expression text (for example `"PropertyHtml.presence \"disabled\""`), with `aria*` resolved when the `ARIAMixin` members are enumerated; `propertyMembers` looks up `(memberName, ownerName)` and emits `TemplateParts.property {html} {write} value` (and the two other calls) with `PropertyHtml.browserOnly` when absent. After emission, raise `fail "Unused portable property entry: <member>/<owner>"` for an entry no member used. Then:

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet run --project tools/Ranvier.Dom.Generate/Ranvier.Dom.Generate.fsproj -c Release
python tools/Ranvier.Dom.Generate/tests.py
python tools/Ranvier.Dom.Generate/check-types.py
git diff --stat experiments/Ranvier.View.Foundation/Generated/Html.fs
```

Expected: both scripts exit 0; `Html.fs` is unchanged (the diff prints nothing); `HtmlTemplate.fs` changes in its `TemplateParts.property`, `readProperty` and `sourceProperty` lines only.

- [ ] **Step 5: Compile and write properties on the server**

In the plan compiler, emit `StaticPropertyOp`/`PropertyOp` (or the child-text ops) in the order Interfaces gives, `BrowserOp`-equivalent fallback for `BrowserOnly` with description `"<tag> property"`, and the two compile-time checks.

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run test:dom`
Expected: PASS; `TemplateCatalogueChecks` and `GeneratedChecks` unchanged.

- [ ] **Step 7: Record the benchmark after S3**

Run Task 3 Step 3 with `dotnet-after-s3` and `bytes-after-s3`; add rows with phase `after S3`. The component rows grow by their input's `value` attribute.

- [ ] **Step 8: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 9: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs tools/Ranvier.Dom.Generate/Program.fs
git add experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Generated/HtmlTemplate.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs tools/Ranvier.Dom.Generate/Program.fs tools/Ranvier.Dom.Generate/tests.py docs/research/2026-10-19-server-html-hydration.md docs/research/framework-performance/investigation/server-html
git diff --cached --check
git commit -F - <<'EOF'
feat(view): serialise reflected template properties in server HTML and reject browser-only parts under Reject

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 8: Forms and the leading line feed (S3)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (plan compiler: child-text ops and the leading-line-feed rule for `textarea`)
- Create: `examples/Fable.Ranvier.Playground/tests/hydration.html`, `examples/Fable.Ranvier.Playground/tests/hydration.spec.mjs`
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`, `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs` (`browserCases`)

**Interfaces:**
- Consumes: `PropertyHtml`, `ChildTextOp`, `StaticChildTextOp` (Task 7); `HydrationChecks.browserCases` (Task 6).
- Produces: `tests/hydration.html` exposing `window.hydrationBrowserCases`; `tests/hydration.spec.mjs` running them in Chromium.

- [ ] **Step 1: Write the failing tests**

In `ServerChecks.fs`, insert before `let cases`:

```fsharp
type FormState = { Text: string; On: bool; Choice: string }

let formFixture =
    Component.define (fun _ (state: FormState) -> state) (fun t ->
        t.form
            [||]
            [|
                t.textarea [| t.Props.TextArea.value (fun r -> r.Text) |] [||]
                t.input [| t.Attrs.Input.``type`` "checkbox"; t.Props.Input.``checked`` (fun r -> r.On) |]
                t.select
                    [| t.Props.Select.value (fun r -> r.Choice) |]
                    [|
                        t.option [| t.Attrs.Option.value "a"; t.Props.Option.selected (fun r -> r.Choice = "a") |] [| t.text "A" |]
                        t.option [| t.Attrs.Option.value "b"; t.Props.Option.selected (fun r -> r.Choice = "b") |] [| t.text "B" |]
                    |]
                t.pre [||] [| t.text (fun r -> r.Text) |]
                t.pre [||] [| t.text "\nstatic" |]
            |])

let private forms () =
    equal
        "<!--[r--><form><textarea>\n\nline &lt;1&gt;</textarea><input type=\"checkbox\" checked=\"\"><select><option value=\"a\">A</option><option value=\"b\" selected=\"\">B</option></select><pre>\n\nline &lt;1&gt;</pre><pre>\n\nstatic</pre></form><!--]-->"
        (Server.render { Server.Options.defaults with Policy = Omit } formFixture { Text = "\nline <1>"; On = true; Choice = "b" })
```

Add to `cases`: `"textarea, checkbox and select write their server state; select.value is browser-only", forms`.

Replace `browserCases` in `HydrationChecks.fs` with:

```fsharp
/// <summary>The text and value of the first element matching <c>selector</c> after the browser parses <c>html</c>.</summary>
let private parsed (html: string) (selector: string) : string * string =
    let container = globalThis.document.createElement "div"
    container.innerHTML <- html
    let element = container.querySelector selector |> unbox<HTMLElement>
    element.textContent, (if isNull element?value then "" else string element?value)

let private leadingLineFeeds () =
    let html =
        Server.render { Server.Options.defaults with Policy = Omit } ServerChecks.formFixture { ServerChecks.FormState.Text = "\nline"; On = false; Choice = "a" }

    equal ("\nline", "\nline") (parsed html "textarea")
    equal "\nline" (fst (parsed html "pre"))
    equal "\nstatic" (fst (parsed html "pre + pre"))

let browserCases: (string * (unit -> unit)) array =
    [| "textarea value and pre text keep a leading line feed after parsing", leadingLineFeeds |]
```

Create `tests/hydration.html`:

```html
<!doctype html>
<html><head><meta charset="utf-8"><title>Hydration checks</title></head><body>
<script type="module">
import { cases, browserCases } from "../generated-tests/HydrationChecks.js";
window.hydrationCases = cases;
window.hydrationBrowserCases = browserCases;
</script>
</body></html>
```

Create `tests/hydration.spec.mjs`:

```js
import { test, expect } from "@playwright/test";

for (const [label, key] of [["hydration checks in Chromium", "hydrationCases"], ["hydration browser checks in Chromium", "hydrationBrowserCases"]]) {
  test(label, async ({ page }) => {
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.goto("/tests/hydration.html");
    await page.waitForFunction(key => window[key], key);
    const results = await page.evaluate(key => window[key].map(([name, check]) => {
      try { check(); return { name, passed: true }; }
      catch (error) { return { name, passed: false, error: String(error) }; }
    }), key);
    expect(results.length).toBeGreaterThan(0);
    expect(results.filter(result => !result.passed)).toEqual([]);
    expect(errors).toEqual([]);
  });
}
```

`FormState` field `Text` collides with `Listed.Text` in `ServerFixtures`; the record expression in `leadingLineFeeds` names the type through its first field.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: FAIL at `forms`: the textarea's text has one leading line feed.

- [ ] **Step 3: Apply the leading-line-feed rule to child text**

In the plan compiler, `ChildTextOp`/`StaticChildTextOp` on a `textarea` carry `leadingLineFeed = true`, and the writer doubles a leading `\n` of their value as `TextOp` does.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run test:browser`
Expected: PASS, including `hydration browser checks in Chromium`.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs examples/Fable.Ranvier.Playground/tests/hydration.html examples/Fable.Ranvier.Playground/tests/hydration.spec.mjs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): write form state in server HTML and keep leading line feeds through parsing

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 9: Boundary attempts, view markers and `ServerPendingException` (S4)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (`ServerWriter`: boundary regions, view placement), `experiments/Ranvier.View.Portable/Server.fs` (`ServerPendingException`, pending conversion)
- Modify (expectations): `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs` (`boundaryPins`), `examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs` (`templateServer`, lines 1128–1132)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`

**Interfaces:**
- Consumes: `ServerScope.Attempt`, `HtmlSink.Mark`/`Truncate` (Tasks 4–5); `ServerWriter.Write` with positions (Task 6).
- Produces:
  - `exception ServerPendingException of Pending: exn` in `Server` — `Pending` is the core's `NotReadyException`; `Message` is `"A read outside every boundary is pending"`. `renderOn` raises it after disposal when the render raised `NotReadyException`.
  - `ServerWriter.View: scope: ServerScope * render: (Backend -> unit) * parent: InstancePath * region: int * row: int -> unit` — runs `render backend` with the position the next template it reaches takes; `Component<'props>.RenderHtml` consumes that position (and falls back to Task 6's authored step when none is pending).
  - A `BoundaryRow` region writes: mark; `<!--[c-->`; the content view in `scope.Attempt`, at row 0 with status `{ IsPending = fun () -> false }`. When the attempt raised `NotReadyException`: truncate to the mark, `<!--[p-->`, the pending view at row 1 under `scope`. Any other exception `e`: truncate, `<!--[e-->`, the error view with `{ Error = e; Retry = ignore }` at row 1 under `scope`. Then `<!--]-->`. A failure inside the pending or error view propagates to the enclosing attempt.

- [ ] **Step 1: Write the failing tests and change the boundary pins**

In `ServerChecks.fs`, change the three `boundaryPins` expectations to `<div><!--[p--><span>loading</span><!--]--></div>`, `<div><!--[c--><span>&lt;A&gt;</span><!--]--></div>` and `<div><!--[e--><span>&lt;bad&gt;</span><!--]--></div>`. Make the same three changes in `BoundaryChecks.fs` `templateServer`. Insert before `let cases`:

```fsharp
type private ContentOutcome =
    | ContentPends
    | ContentFails
    | ContentReads

let private attemptLog = ResizeArray<string>()
let mutable private contentOutcome = ContentPends

let private attemptPending =
    Component.define (fun _ () -> attemptLog.Add "pending setup") (fun t -> t.span [||] [| t.text "loading" |])

let private attemptFailed =
    Component.define
        (fun _ (failure: BoundaryFailure) ->
            attemptLog.Add "error setup"
            failure.Retry ()
            failure)
        (fun t -> t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let private attemptContent =
    Component.define
        (fun setup (_: BoundaryStatus) ->
            attemptLog.Add "content setup"
            setup.OnCleanup (fun () -> attemptLog.Add "content cleanup")
            let source = Api.createAsyncSource<string> ()

            match contentOutcome with
            | ContentReads -> source.Settle "ready"
            | ContentFails -> source.Fail (Exception "<failed>")
            | ContentPends -> ()

            source)
        (fun t -> t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private attempted =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary attemptPending attemptFailed attemptContent |])

let private boundaryAttempts () =
    let render outcome =
        contentOutcome <- outcome
        attemptLog.Clear ()
        let html = Server.render Server.Options.defaults attempted ()
        html, List.ofSeq attemptLog

    equal
        ("<!--[r--><div><!--[p--><span>loading</span><!--]--></div><!--]-->", [ "content setup"; "content cleanup"; "pending setup" ])
        (render ContentPends)

    equal
        ("<!--[r--><div><!--[e--><span>&lt;failed&gt;</span><!--]--></div><!--]-->", [ "content setup"; "content cleanup"; "error setup" ])
        (render ContentFails)

    equal ("<!--[r--><div><!--[c--><span>ready</span><!--]--></div><!--]-->", [ "content setup"; "content cleanup" ]) (render ContentReads)

let private failingPending =
    Component.define (fun _ () -> failwith "pending failed") (fun t -> t.span [||] [||])

let private innerContent =
    Component.define (fun _ (_: BoundaryStatus) -> Api.createAsyncSource<string> ()) (fun t ->
        t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private outerContent =
    Component.define (fun _ (_: BoundaryStatus) -> ()) (fun t -> t.boundary failingPending attemptFailed innerContent)

let private nestedBoundaries =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary attemptPending attemptFailed outerContent |])

let private pendingViewFailure () =
    equal
        "<!--[r--><div><!--[e--><span>pending failed</span><!--]--></div><!--]-->"
        (Server.render Server.Options.defaults nestedBoundaries ())

let private unguarded =
    Component.define
        (fun setup () ->
            setup.OnCleanup (fun () -> attemptLog.Add "unguarded cleanup")
            Api.createAsyncSource<string> ())
        (fun t -> t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private pendingOutsideBoundaries () =
    attemptLog.Clear ()

    match failure (fun () -> Server.render Server.Options.defaults unguarded () |> ignore) with
    | Server.ServerPendingException pending ->
        let isNotReady =
            match pending with
            | NotReadyException _ -> true
            | _ -> false

        equal (true, [ "unguarded cleanup" ]) (isNotReady, List.ofSeq attemptLog)
    | other -> failwithf "Expected ServerPendingException; got %O" other
```

Add to `cases`:

```fsharp
        "a boundary's abandoned content attempt runs its cleanups before the pending or error view is written", boundaryAttempts
        "boundary markers record content, pending and error views; a failure inside the pending view goes to the enclosing boundary", pendingViewFailure
        "a pending read outside every boundary raises ServerPendingException after disposal", pendingOutsideBoundaries
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the compile fails: `The pattern discriminator 'ServerPendingException' is not defined`.

- [ ] **Step 3: Implement the attempts and the conversion**

Implement the Interfaces items in `Template.fs` and `Server.fs`. `HtmlScope.Attempt` runs the body in the same scope, so `Html.render` keeps today's boundary semantics with the new markers.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/server.test.mjs tests/boundary.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git add experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/BoundaryChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): render server boundaries in disposable attempts and record the chosen view in their markers

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 10: Arm, placeholder, separator and zero-root markers with the structural hash (S4)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (`fnv1a`, `structuralHash`, `ServerPlan` fields, writer)
- Modify (expectations): `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs` (`armPins`, the `emptyText` line of `edgePins`), `experiments/Ranvier.View.Foundation/RepresentationChecks.fsx` (lines 327, 328, 341, 397), `examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs` (lines 1257, 1346)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`
- Create (benchmark output): `dotnet-after-s4/` and `bytes-after-s4.json` are recorded in Task 12, after Task 11.

**Interfaces:**
- Produces (module `Ranvier.View.Foundation.Template`):
  - `fnv1a (value: string) : string` — 32-bit FNV-1a over the UTF-16 code units, eight lowercase hex digits. Identical on .NET and under Fable with this multiply:

```fsharp
let fnv1a (value: string) =
    let mutable hash = 2166136261u

    for index in 0 .. value.Length - 1 do
        hash <- hash ^^^ uint32 value[index]
        // hash * 16777619, as shifts and adds that stay exact under Fable.
        hash <- hash + (hash <<< 1) + (hash <<< 4) + (hash <<< 7) + (hash <<< 8) + (hash <<< 24)

    hash.ToString "x8"
```

  `hash.ToString "x8"` is .NET; under Fable use `(hash |> int64).ToString("x").PadLeft(8, '0')` or the JavaScript `toString(16)` with padding (`#if FABLE_COMPILER`).
  - `structuralHash (definition: TemplateComponent) : string` — `fnv1a` of `(skeletonLayout definition.Root).Markup + "|" + kinds`, `kinds` one letter per region target in document order: `l` (`ItemRow`), `a` (`ArmRow`), `v` (`ViewRow`), `b` (`BoundaryRow`). Cached in the plan.
  - `ServerPlan` gains `Hash: string`, `RootCount: int`, `FirstIsText: bool`, `LastIsText: bool` (the first and last flattened roots are a text node or a text slot).
  - Writer changes: a text slot whose value is `""` or `null` writes `<!--_-->`; a list region writes `<!--!-->` between two rows when the earlier row's plan has `LastIsText` and the later row's `FirstIsText`; a row whose plan has `RootCount = 0` writes `<!--ranvier:row-->`; an arm region writes `<!--[a:` + the selected arm's `Hash` + `-->` as its start marker.

- [ ] **Step 1: Write the failing tests and change the expectations**

In `ServerChecks.fs` `armPins`, replace `<!--[-->` with `<!--[a:6a39ec02-->` in all three expectations (`workingArm`, `completeArm` and `decadeArm` are a span over one text slot). In `edgePins`, the first line becomes `equal "<span><!--_--></span>" (renderOmit emptyText "")`. In `RepresentationChecks.fsx` lines 327, 328, 341 and 397, replace `<!--[-->` with `<!--[a:6a39ec02-->` (`adminArm` is also a span over one text slot). In `TemplateChecks.fs` line 1257, the expectation becomes `"<div><!--[a:" + structuralHash workingArm + "--><span>working 3</span><!--]--></div>"`, and line 1346 `"<div><!--[a:" + structuralHash decadeArm + "--><span>4</span><!--]--></div>"`.

Insert before `let cases`:

```fsharp
let private hashVectors () =
    equal ("811c9dc5", "e40c292c", "25dbbe5a") (fnv1a "", fnv1a "a", fnv1a "<span>x</span>|")
    let x = Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "x" |])
    let y = Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "y" |])
    equal ("25dbbe5a", "6a39ec02", false) (structuralHash x, structuralHash workingArm, structuralHash x = structuralHash y)

let private textRow =
    Component.define (fun _ (item: unit -> string) -> item) (fun t -> t.text (fun item -> item ()))

let private emptyRow =
    Component.define (fun _ (_: unit -> string) -> ()) (fun t -> t.fragment [||])

let private texts =
    Component.define (fun _ (items: string array) -> items) (fun t -> t.div [||] [| t.each id id textRow |])

let private empties =
    Component.define (fun _ (items: string array) -> items) (fun t -> t.div [||] [| t.each id id emptyRow |])

let private markers () =
    let render definition props =
        Server.render Server.Options.defaults definition props

    equal "<!--[r--><div><!--[-->a<!--!-->b<!--!--><!--_--><!--]--></div><!--]-->" (render texts [| "a"; "b"; "" |])
    equal "<!--[r--><div><!--[--><!--ranvier:row--><!--ranvier:row--><!--]--></div><!--]-->" (render empties [| "a"; "b" |])
    equal "<!--[r--><div><!--[a:6a39ec02--><span>working 3</span><!--]--></div><!--]-->" (render staged (Working 3))
    equal "<!--[r--><span><!--_--></span><!--]-->" (render emptyText null)
```

Add to `cases`:

```fsharp
        "the structural hash is FNV-1a over the canonical skeleton, equal on .NET and Fable", hashVectors
        "server HTML carries arm, empty-text, separator and zero-root markers", markers
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the compile fails: `The value or constructor 'fnv1a' is not defined`.

- [ ] **Step 3: Implement the hash and the markers**

Implement the Interfaces items. `RootCount`, `FirstIsText` and `LastIsText` come from `skeletonLayout`'s root count and the first and last `Item` of `flatten [| root |]`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run test:dom`
Expected: PASS. Any other failing expectation is a plan conflict: stop and report it.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
git add experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs experiments/Ranvier.View.Foundation/RepresentationChecks.fsx examples/Fable.Ranvier.Playground/tests/TemplateChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): write arm hashes and empty-text, separator and zero-root markers in server HTML

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 11: Reject markup the HTML parser restructures (S4)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Template.fs` (new `ContentModel` module before the plan compiler; plan compiler; `ServerPlan.Summary`; writer's row check)
- Modify (expectations): `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs` (`edgePins`: the void-children and table lines)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`

**Interfaces:**
- Produces (module `Ranvier.View.Foundation.Template`):
  - `module internal ContentModel` with `check (ancestors: string list) (tag: string) : unit` (`ancestors` nearest first; raises `InvalidOperationException ("The HTML parser restructures this markup (" + rule + ") at " + path)`, `path` the ancestors outermost first and the tag joined by `/`) and `checkText (ancestors: string list) (whitespace: bool) : unit` (path ends in `#text`).
  - `ServerPlan.Summary: RootSummary` with `RootTags: string array`, `TextRoot: bool` (a root that is a text slot or non-whitespace static text), `Anchors: bool`, `Forms: bool`, `ClosesParagraph: bool` (an element of the p-closing set reachable from a root without crossing a p-scope boundary); and `ServerPlan.CheckedContexts`, a set of context keys guarded by `lock`.
  - The writer keeps the open element tags as `ancestors`. Before the first row of a definition is written in a context (key: the nearest ancestor, and whether `a`, `form` and an unbounded `p` are among the ancestors), it checks the definition's `Summary` against that context once and records the key.
  - Plan compile raises `InvalidOperationException ("A void element has children at " + path)` for a void element with children, and `InvalidOperationException ("Template elements are not supported in templates at " + path)` for a `template` element.

The rules, with the rule names the messages carry:

| Rule | Rejected | Reason in the parser |
|---|---|---|
| `table-row` | `tr` whose parent is not `thead`, `tbody` or `tfoot` | `tbody` is implied, or the tag is dropped |
| `table-section` | `thead`, `tbody`, `tfoot`, `caption` or `colgroup` whose parent is not `table` | the tag is dropped |
| `table-cell` | `td` or `th` whose parent is not `tr` | `tr` is implied, or the tag is dropped |
| `table-column` | `col` whose parent is not `colgroup` | `colgroup` is implied, or the tag is dropped |
| `table-foster` | a text slot, non-whitespace static text, or an element other than `caption`, `colgroup`, `thead`, `tbody`, `tfoot` (under `table`), `tr` (under `thead`/`tbody`/`tfoot`), `td`/`th` (under `tr`), as a child of `table`, `thead`, `tbody`, `tfoot` or `tr` | foster parenting |
| `paragraph-close` | `address`, `article`, `aside`, `blockquote`, `details`, `dialog`, `div`, `dl`, `fieldset`, `figcaption`, `figure`, `footer`, `form`, `h1`–`h6`, `header`, `hgroup`, `hr`, `main`, `menu`, `nav`, `ol`, `p`, `pre`, `search`, `section`, `table` or `ul` with a `p` ancestor and no `button`, `td`, `th`, `caption`, `object`, `marquee` or `applet` between them | the open `p` is closed |
| `nested-anchor` | `a` with an `a` ancestor | the outer `a` is closed |
| `nested-form` | `form` with a `form` ancestor | the inner tag is dropped |
| `select-content` | a child of `select` other than `option`, `optgroup` and `hr`, or a child of `optgroup` other than `option` | the wrapper is dropped (parser-version dependent) |

- [ ] **Step 1: Write the failing tests and change the edge pins**

In `edgePins`, replace the void-children and table lines with:

```fsharp
    equal true ((failure (fun () -> renderOmit voidChildren () |> ignore)).Message.Contains "A void element has children at div/br")
    equal true ((failure (fun () -> renderOmit rowInTable () |> ignore)).Message.Contains "(table-row) at table/tr")
```

Insert before `let cases`:

```fsharp
let private restructured (build: TemplateBuilder<unit> -> TemplateView<unit>) =
    let definition = Component.define (fun _ () -> ()) build
    (failure (fun () -> Server.render Server.Options.defaults definition () |> ignore)).Message

let private contentModel () =
    let cases =
        [
            (fun (t: TemplateBuilder<unit>) -> t.table [||] [| t.tr [||] [| t.td [||] [||] |] |]), "(table-row) at table/tr"
            (fun t -> t.div [||] [| t.tbody [||] [||] |]), "(table-section) at div/tbody"
            (fun t -> t.table [||] [| t.tbody [||] [| t.td [||] [||] |] |]), "(table-cell) at table/tbody/td"
            (fun t -> t.table [||] [| t.element (HtmlTag "col") [||] [||] |]), "(table-column) at table/col"
            (fun t -> t.table [||] [| t.text "x" |]), "(table-foster) at table/#text"
            (fun t -> t.table [||] [| t.tbody [||] [| t.tr [||] [| t.div [||] [||] |] |] |]), "(table-foster) at table/tbody/tr/div"
            (fun t -> t.p [||] [| t.span [||] [| t.div [||] [||] |] |]), "(paragraph-close) at p/span/div"
            (fun t -> t.a [||] [| t.span [||] [| t.a [||] [||] |] |]), "(nested-anchor) at a/span/a"
            (fun t -> t.form [||] [| t.div [||] [| t.form [||] [||] |] |]), "(nested-form) at form/div/form"
            (fun t -> t.select [||] [| t.div [||] [||] |]), "(select-content) at select/div"
        ]

    equal [ for _, expected in cases -> true ] [ for build, expected in cases -> (restructured build).Contains expected ]

    let accepted =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [||]
                [|
                    t.p [||] [| t.button [||] [| t.div [||] [||] |] |]
                    t.table [||] [| t.tbody [||] [| t.text " "; t.tr [||] [| t.td [||] [| t.text "x" |] |] |] |]
                |])

    equal
        "<!--[r--><div><p><button><div></div></button></p><table><tbody> <tr><td>x</td></tr></tbody></table></div><!--]-->"
        (Server.render Server.Options.defaults accepted ())

let private tableRow =
    Component.define (fun _ (_: unit -> int) -> ()) (fun t -> t.tr [||] [| t.td [||] [||] |])

let private rowsInTable =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.table [||] [| t.each id id tableRow |])

let private rowsInBody =
    Component.define (fun _ (rows: int array) -> rows) (fun t -> t.table [||] [| t.tbody [||] [| t.each id id tableRow |] |])

let private dynamicContentModel () =
    equal true ((failure (fun () -> Server.render Server.Options.defaults rowsInTable [| 1 |] |> ignore)).Message.Contains "(table-row) at table/tr")
    equal "<!--[r--><table><tbody><!--[--><tr><td></td></tr><!--]--></tbody></table><!--]-->" (Server.render Server.Options.defaults rowsInBody [| 1 |])

let private templateElement () =
    let message = restructured (fun t -> t.div [||] [| t.element (HtmlTag "template") [||] [||] |])
    equal true (message.Contains "Template elements are not supported in templates at div/template")
```

Add to `cases`:

```fsharp
        "content-model violations fail at plan compile with the rule and the element path", contentModel
        "a row definition with tr roots inside a table region fails on first render", dynamicContentModel
        "void elements with children and template elements fail at plan compile", templateElement
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: FAIL at `edgePins`: `Expected an exception`.

- [ ] **Step 3: Implement the rules and the checks**

Write `ContentModel` from the table, the static checks in the plan compiler (with the ancestors of each element within its definition; a definition's roots are checked when it is written), the `Summary`, and the writer's once-per-context row check.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run test:dom`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs
git add experiments/Ranvier.View.Portable/Template.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): reject template markup the HTML parser would restructure

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 12: First-content benchmarks against Solid 2 and the server counter scenario (S5)

**Files:**
- Create: `examples/Fable.Ranvier.Playground/benchmarks/frameworks/server-solid.jsx`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/server-render.mjs`, `bench/Ranvier.Counters/ServerWorkloads.fs`
- Modify: `bench/Ranvier.Counters/Ranvier.Counters.fsproj`, `bench/Ranvier.Counters/Scenarios.fs`, `fable/Ranvier.Counters/Ranvier.Counters.fsproj`, `fable/Ranvier.Counters/Scenarios.fs`, `docs/research/2026-10-19-server-html-hydration.md`
- Create (benchmark output): `docs/research/framework-performance/investigation/server-html/{dotnet-after-s4/,bytes-after-s4.json,node-first-content.json}`

**Interfaces:**
- Consumes: `ServerFixtures.renderComponentPage`, `ServerFixtures.renderKeyedPage` (Task 5), Fable-compiled to `generated-tests/experiments/Ranvier.View.Portable/Checks/ServerFixtures.js`.
- Produces: `server-render.mjs`; output schema `{ measuredAt, baseCommit, dirty, node, cpu, platform, unit: "microseconds per render", verified, samples: [{ fixture, rows, mode, replicate, round, us }], summaries: [{ fixture, rows, mode, medianUs, byProcessUs, bytesPerRow }] }`; counter scenario `server-render` with engine `Ranvier`.

- [ ] **Step 1: Record the .NET benchmark after S4**

Run Task 3 Step 3 with `dotnet-after-s4` and `bytes-after-s4`; add rows with phase `after S4`.

- [ ] **Step 2: Write the Solid 2 server fixtures**

`server-solid.jsx` renders the same markup as `componentPage` and `keyedPage`:

```jsx
import { renderToString } from "@solidjs/web";
import { createSignal, For } from "solid-js";

function ComponentRow(props) {
  const [label] = createSignal(`row ${props.index}`);
  const [clicks, setClicks] = createSignal(0);
  return <div id={`row-${props.index}`} class="panel" title={label()}><input value={label()} /><button onClick={() => setClicks(clicks() + 1)}>{clicks()}</button></div>;
}

function KeyedRow(props) {
  return <tr><td class="col-md-1">{props.row.id}</td><td class="col-md-4"><a onClick={() => {}}>{props.row.label}</a></td><td class="col-md-1"><a onClick={() => {}}><span class="glyphicon glyphicon-remove" aria-hidden="true" /></a></td><td class="col-md-6" /></tr>;
}

export const renderComponentPage = rows => renderToString(() => <section><For each={Array.from({ length: rows }, (_, i) => i + 1)}>{index => <ComponentRow index={index} />}</For></section>);
export const renderKeyedPage = rows => renderToString(() => <table class="table"><tbody><For each={Array.from({ length: rows }, (_, i) => ({ id: i + 1, label: `row ${i + 1}` }))}>{row => <KeyedRow row={row} />}</For></tbody></table>);
```

- [ ] **Step 3: Write the runner**

`server-render.mjs`, usage `node benchmarks/frameworks/server-render.mjs [--build] [--label=<name>] [--out=<dir>]`. It follows `boundary-reveal.mjs`'s structure (argument parsing, `--build` running `dotnet fable tests/DomTests.fsproj --outDir generated-tests --configuration Release --noCache` through `rtk`, three processes, 15 rounds after 3 warmups, the mode order rotated per round and process, medians per process, the `dirty` and environment fields), with these differences:

- It compiles `server-solid.jsx` with `transform` from `@solidjs/compiler` (`generate: "ssr", hydratable: true`) into `generated-server-dist/server-solid.mjs` and imports it; it imports `renderComponentPage`/`renderKeyedPage` from `../../generated-tests/experiments/Ranvier.View.Portable/Checks/ServerFixtures.js`.
- Each process is a child `node --expose-gc` running the same file with `--worker=<replicate>`; the worker calls `globalThis.gc()` before each sample.
- Fixtures and counts per sample: component rows 1/16/128 with 256/64/16 renders, keyed rows 1000/10000 with 4/1 renders; a sample is microseconds per render.
- `verified` holds, per fixture, rows and mode, the count of `class="panel"` (component) or `<tr>` (keyed) in the output, which must equal `rows`, and the output bytes; `bytesPerRow` comes from it.
- Output: `docs/research/framework-performance/investigation/server-html/<label>.json`, default label `node-first-content`.

- [ ] **Step 4: Add the counter scenario**

Create `bench/Ranvier.Counters/ServerWorkloads.fs`:

```fsharp
/// <summary>Server render workloads: each operation renders one 100-row keyed table as one request.</summary>
module CounterBench.ServerWorkloads

/// <summary>A workload of <c>n</c> server renders of the keyed fixture at 100 rows.</summary>
let serverRender (n: int) : Workloads.Workload =
    {
        Run =
            fun () ->
                for _ in 1..n do
                    ServerFixtures.renderKeyedPage 100 |> ignore
        Teardown = ignore
    }
```

In `bench/Ranvier.Counters/Ranvier.Counters.fsproj`, after `OnceWorkloads.fs`: `<Compile Include="..\..\experiments\Ranvier.View.Portable\Checks\ServerFixtures.fs" Link="ServerFixtures.fs" />` and `<Compile Include="ServerWorkloads.fs" />`; add `<ProjectReference Include="..\..\experiments\Ranvier.View.Portable\Ranvier.View.Portable.fsproj" />`. In `fable/Ranvier.Counters/Ranvier.Counters.fsproj`, the same two compile items (linking `..\..\bench\Ranvier.Counters\ServerWorkloads.fs`) and the same reference. In both `Scenarios.fs`, after the `keyed-*` loop:

```fsharp
        yield! scenario "server-render" "render a 100-row keyed table as one request" (5 * scale) [ "Ranvier", workload ServerWorkloads.serverRender ]
```

Run, behind the benchmark lock: `dotnet run -c Release --project bench/Ranvier.Counters -- --only server-render --no-pmc --repeat 1`
Expected: the report prints a `server-render` row for engine `Ranvier` with a positive figure per operation.

- [ ] **Step 5: Measure against Solid 2**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/server-render.mjs --build
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Expected: `server-html/node-first-content.json` exists; `verified` holds 10 entries, each with its row count equal to `rows`.

- [ ] **Step 6: Record the results**

Append to the research note a section `## First content under Node against Solid 2` with one row per fixture and row count: Ranvier and Solid 2 median µs per render, their ratio, and bytes per row of each. State that Solid 2's output carries its hydration keys and Ranvier's its markers.

- [ ] **Step 7: Run the gates**

Run the gate commands from Global Constraints, and `dotnet test bench/Ranvier.Counters.Tests`. Expected: every command exits 0.

- [ ] **Step 8: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas bench/Ranvier.Counters/ServerWorkloads.fs bench/Ranvier.Counters/Scenarios.fs fable/Ranvier.Counters/Scenarios.fs
git add bench/Ranvier.Counters/ServerWorkloads.fs bench/Ranvier.Counters/Ranvier.Counters.fsproj bench/Ranvier.Counters/Scenarios.fs fable/Ranvier.Counters/Ranvier.Counters.fsproj fable/Ranvier.Counters/Scenarios.fs examples/Fable.Ranvier.Playground/benchmarks/frameworks/server-solid.jsx examples/Fable.Ranvier.Playground/benchmarks/frameworks/server-render.mjs docs/research/2026-10-19-server-html-hydration.md docs/research/framework-performance/investigation/server-html
git diff --cached --check
git commit -F - <<'EOF'
perf(view): measure first content against Solid 2's renderToString and add a server-render counter scenario

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 13: Hydrate a single template by adoption (H1)

**Files:**
- Create: `experiments/Ranvier.View.Foundation/Hydration.fs`
- Modify: `experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj` (compile `Hydration.fs` after `Mounting.fs`), `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (link it after the `Mounting.fs` link)
- Modify: `experiments/Ranvier.View.Foundation/TemplateDom.fs` (exception, cursor, logical walk, `Compiled.Expect`, `Compiled.Hash`, `MountServices.Hydration`, `materialise`, `commit`, `bind`)
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`OwnedRange` over adopted markers, `AddAdopted`, `Adopt`; `MountState` hydration parameters, `Run`, release without removal, `RegionOf`; `RowBuilder.Hydration`)
- Test: `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`

**Interfaces:**
- Consumes: `Server.render`, `Server.Options` (Task 5); markers (Task 10); `structuralHash` (Task 10); `PropertySerialization` (Task 7).
- Produces (module `Ranvier.View.Foundation.TemplateDom`):
  - `exception HydrationMismatchException of Path: string * Expected: string * Found: string` (public), message `"Hydration mismatch at " + Path + ": expected " + Expected + ", found " + Found`. `Path` is `root` for a missing root marker, `#<k>` for the k-th root of an instance, and otherwise the dynamic node's child-index path joined by `/` as `validateParsed` writes it. `Expected` is `"<tag>"`, `"a text node"`, `"a region marker"` or a marker's text; `Found` is the found node's name in lowercase, a marker's text, or `absent`.
  - `[<Sealed; AllowNullLiteral>] type internal HydrationCursor(next: Node, strict: bool)` with `member val Next: Node` (the next server node to adopt; null past the last), `member Strict: bool`, `member Undo: (unit -> unit) -> unit` (records the inverse of a structural edit), `member Restore: unit -> unit` (runs the inverses, most recent first).
  - `isStartMarker (node: Node) : bool` (a comment whose data starts with `[`), `matchingEnd (start: Node) : Node` (the sibling `]` comment at depth 0; raises `HydrationMismatchException (…, "<!--]-->", "absent")` when the siblings end first), `logicalNext (node: Node) : Node` (`matchingEnd node` for a start marker, `node.nextSibling` otherwise), all internal.
  - `type internal Expectation = ExpectElement of tag: string | ExpectText | ExpectRegion of RegionKind`; `Compiled.Expect: Expectation array` (one per collected node); `Compiled.Hash: string` (`structuralHash` of the definition, empty for child tables).
  - `MountServices.Hydration: HydrationCursor` — null outside hydration.
  - `materialise`, when `services.Hydration` is not null: after the setup, takes `RootCount` logical siblings from `cursor.Next` as the roots, checks each against the skeleton root at its index (element of the same tag; a text root accepts a text node or `<!--_-->`; a start marker accepts a start marker; another comment accepts a comment with the same data), replacing a `<!--_-->` root with an empty text node; moves `cursor.Next` to the logical next sibling of the last root; runs `compiled.Walk` over the roots (a `FirstChild` at the start of a multi-root walk is the first root; `NextSibling` is `logicalNext`), checking each collected node against `Expect` and replacing a `<!--_-->` text target; appends each root through `services.Append`, which claims it in place; passes each region's matching end marker as its `finish`. Every replacement is recorded with `cursor.Undo`.
  - `commit` on a slot's first value while `services.Hydration` is not null compares before it writes: a text target writes only when `node.data` differs; an attribute target only when `getAttribute` differs; a property target with `AsAttribute (name, write)` only when `getAttribute name` differs from `write.Invoke value`; `AsChildText` only when the textarea's `defaultValue` differs; `BrowserOnly` always. The slot's previous value is the client's value in every case. `bind` applies the same rule to static properties (ruling 6).
- Produces (module `Ranvier.View.Foundation.Mounting`): `MountState` gains constructor parameters `hydration: HydrationCursor, rootStart: Node, rootEnd: Node` (null outside hydration; `mountWith` passes nulls); with a cursor, its range uses `rootStart`/`rootEnd` as its boundary entries and claims adopted roots without moving them, `Run` adopts instead of attaching, and the cursor is cleared when `Run` returns. A failing `Run` with a cursor runs `cursor.Restore ()`, releases its nodes without removing them, and rethrows. Until Tasks 15–17, every region whose `Run` has a cursor clears its server nodes between its markers (recorded with `Undo`) and mounts with no cursor.
- Produces (module `Ranvier.View.Foundation.Hydration`):
  - `type HydrationOptions = { IdPrefix: string; Strict: bool }`
  - `hydrateWith (mode: CommitMode) (graph: Graph) (parentOwner: Owner option) (report: exn list -> unit) (options: HydrationOptions) (host: Node) (definition: Component<'props>) (props: 'props) : Mounted` — `QueuedCommits` raises `NotSupportedException "Hydration supports synchronous commits only"`; finds the first `<!--[r-->` child of `host` and its matching end, builds a `MountState` with `IdentityScope options.IdPrefix`, a cursor at `rootStart.nextSibling` and the two markers, and runs `context.Backend.Template (definition, box props)`. A host without a root marker raises `HydrationMismatchException ("root", "<!--[r-->", "absent")`.
  - `hydrate` = `hydrateWith SynchronousCommits`.

- [ ] **Step 1: Write the failing tests**

In `HydrationChecks.fs`, insert after `clientCreateIdAfterSetup`:

```fsharp
let private strictOptions: Hydration.HydrationOptions = { IdPrefix = "r"; Strict = true }
let private lenientOptions: Hydration.HydrationOptions = { IdPrefix = "r"; Strict = false }
let private omit = { Server.Options.defaults with Policy = Omit }

/// <summary>A detached host holding the server HTML of <c>definition</c> for <c>props</c>.</summary>
let private serve (definition: Component<'props>) (props: 'props) : HTMLElement =
    let host = host ()
    host.innerHTML <- Server.render omit definition props
    host

let private hydrate graph options (host: HTMLElement) (definition: Component<'props>) (props: 'props) =
    Hydration.hydrate graph None strictReport options host definition props

/// <summary>Every node under <c>root</c>, in document order.</summary>
[<Emit("(() => { const w = document.createTreeWalker($0, 0xFFFFFFFF); const a = []; while (w.nextNode()) a.push(w.currentNode); return a; })()")>]
let private nodesOf (root: Node) : Node array = jsNative

[<Emit("Array.from($0.querySelectorAll($1))")>]
let private all (root: Node) (selector: string) : Node array = jsNative

[<Emit("$0.childNodes[$1]")>]
let private childAt (node: Node) (index: int) : Node = jsNative

let private sameNodes (before: Node array) (after: Node array) =
    before.Length = after.Length
    && Array.forall2 (fun (left: Node) (right: Node) -> obj.ReferenceEquals (left, right)) before after

/// <summary>Records mutations under <c>root</c>; the returned function stops and returns their count.</summary>
[<Emit("(() => { const o = new MutationObserver(() => {}); o.observe($0, { subtree: true, childList: true, attributes: true, characterData: true }); return () => { const n = o.takeRecords().length; o.disconnect(); return n; }; })()")>]
let private recordMutations (root: Node) : unit -> int = jsNative

[<Emit("$0.dispatchEvent(new FocusEvent('focus'))")>]
let private focus (node: Node) : unit = jsNative

let private element (host: HTMLElement) (selector: string) =
    host.querySelector selector |> unbox<HTMLElement>

type private CardState = { Title: Signal<string>; Clicks: Signal<int> }

let private card =
    Component.define
        (fun _ (title: string) -> { Title = Api.createSignal title; Clicks = Api.createSignal 0 })
        (fun t ->
            t.div
                [| t.Attrs.title (t.from (fun r -> r.Title)) |]
                [|
                    t.text "Title: "
                    t.text (t.from (fun r -> r.Title))
                    t.button [| t.Events.onClick (fun r _ -> r.Clicks.Value <- r.Clicks.Value + 1) |] [| t.text (fun r -> string r.Clicks.Value) |]
                    t.input [| t.Events.on ("focus", fun r _ -> r.Clicks.Value <- r.Clicks.Value + 10); t.Attrs.Input.value (fun r -> r.Title.Value) |]
                |])

let private adoptsEveryNode () =
    use graph = new Graph ()
    let host = serve card "a"
    equal """<!--[r--><div title="a">Title: <!--!-->a<button>0</button><input value="a"></div><!--]-->""" host.innerHTML
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host card "a"
    equal true (sameNodes before (nodesOf host))
    mounted.Dispose ()

let private updatesAndDisposes () =
    use graph = new Graph ()
    let host = serve card "a"
    let mounted = hydrate graph strictOptions host card "a"
    let button = element host "button"
    button.click ()
    equal "1" button.textContent
    focus (unbox<Node> (host.querySelector "input"))
    equal "11" button.textContent
    mounted.Dispose ()
    equal 0. host.childNodes.length
    button.click ()
    equal "11" button.textContent

let private matchingValuesWriteNothing () =
    use graph = new Graph ()
    let host = serve card "a"
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host card "a"
    equal 0 (mutations ())
    mounted.Dispose ()

let private differingValuesCorrected () =
    use graph = new Graph ()
    let host = serve card "a"
    let mounted = hydrate graph strictOptions host card "b"
    let input = host.querySelector "input" |> unbox<HTMLInputElement>
    equal (Some "b", "Title: b0", Some "b") ((element host "div").getAttribute "title", (element host "div").textContent, input.getAttribute "value")
    mounted.Dispose ()

let private appending =
    Component.define (fun _ (text: string) -> Api.createSignal text) (fun t ->
        t.span [| t.Events.onClick (fun (text: Signal<string>) _ -> text.Value <- text.Value + "x") |] [| t.text (fun (text: Signal<string>) -> text.Value) |])

let private emptyTextHydrates () =
    use graph = new Graph ()
    let host = serve appending ""
    equal "<!--[r--><span><!--_--></span><!--]-->" host.innerHTML
    let mounted = hydrate graph strictOptions host appending ""
    let span = element host "span"
    equal (1., 3.) (span.childNodes.length, span.firstChild.Value.nodeType)
    span.click ()
    equal "x" span.textContent
    mounted.Dispose ()

let private shapes () =
    let check (definition: Component<'props>) (props: 'props) (text: string) =
        use graph = new Graph ()
        let host = serve definition props
        let before = nodesOf host
        let mounted = hydrate graph strictOptions host definition props
        equal (true, text) (sameNodes before (nodesOf host), host.textContent)
        mounted.Dispose ()
        equal 0. host.childNodes.length

    check (Component.define (fun _ () -> ()) (fun t -> t.fragment [||])) () ""
    check textual "one"
    check pair greetingProps "Hello <Ada>!<Ada>"
    check (Component.define (fun _ () -> ()) (fun t -> t.fragment [| t.text "a"; t.span [||] [| t.text "b" |] |])) () "ab"

let private regionsMountWithContent () =
    use graph = new Graph ()
    let host = serve listed listedItems
    let mounted = hydrate graph strictOptions host listed listedItems
    equal ("a<b>", 2) ((element host "ul").textContent, (all host "li").Length)
    mounted.Dispose ()

let private queuedRaises () =
    use graph = new Graph ()
    let host = serve card "a"
    let mutable rejected = false

    try
        Hydration.hydrateWith (Scheduling.QueuedCommits (Scheduling.CollectAndContinue, ignore)) graph None strictReport strictOptions host card "a"
        |> ignore
    with :? NotSupportedException ->
        rejected <- true

    equal true rejected

let private foreignSiblings () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "\n  <p>before</p>" + Server.render omit card "a" + "<script type=\"application/json\">{}</script>\n"
    let paragraph, script = host.querySelector "p", host.querySelector "script"
    let mounted = hydrate graph strictOptions host card "a"
    (element host "button").click ()
    equal "Title: a1" (element host "div").textContent
    mounted.Dispose ()

    equal
        (true, true, true)
        (obj.ReferenceEquals (paragraph, host.querySelector "p"), obj.ReferenceEquals (script, host.querySelector "script"), isNull (box (host.querySelector "div")))

let private carriageReturn () =
    use graph = new Graph ()
    let host = serve card "a\r\nb"
    let mutations = recordMutations host
    let mounted = hydrate graph strictOptions host card "a\r\nb"
    equal (0, "Title: a\r\nb0") (mutations (), (element host "div").textContent)
    mounted.Dispose ()
```

Add to `cases`:

```fsharp
        "hydrating server HTML adopts every node", adoptsEveryNode
        "a hydrated template updates, dispatches delegated and direct events, and disposes like a mount", updatesAndDisposes
        "hydration writes no text or attribute when values match", matchingValuesWriteNothing
        "hydration corrects differing values without a report", differingValuesCorrected
        "empty text slots hydrate through their placeholder and update", emptyTextHydrates
        "empty, one-node, multi-root and fragment templates hydrate", shapes
        "a template with regions hydrates with the client's region content", regionsMountWithContent
        "queued-commit hydration raises NotSupportedException", queuedRaises
        "hydration adopts only the root marker pair and leaves foreign siblings in place", foreignSiblings
        "a text value with CR LF hydrates without writes", carriageReturn
```

- [ ] **Step 2: Run them and confirm they fail**

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs`
Expected: the compile fails: `The value, namespace, type or module 'Hydration' is not defined`.

- [ ] **Step 3: Implement the cursor, the logical walk and the adoption in `TemplateDom.fs`**

The walk over adopted roots, given as the algorithm the tests rely on:

```fsharp
let private collectAdopted (roots: Node array) (compiled: Compiled) (state: obj array) (cursor: HydrationCursor) =
    let walk = compiled.Walk
    // A multi-root walk starts at the virtual fragment holding the roots; a single-root walk starts at the root.
    let mutable node: Node = if compiled.RootCount = 1 then roots[0] else null
    let mutable count = 0

    for index in 0 .. walk.Length - 1 do
        let op = at walk index

        if op = FirstChild then
            node <- if isNull node then (if roots.Length = 0 then null else roots[0]) else unbox node.firstChild
        elif op = NextSibling then
            node <- logicalNext node
        elif op = Parent then
            node <- unbox node.parentNode
        else
            node <- adoptTarget cursor (at compiled.Expect count) count node
            store state (StateNodes + count) (box node)
            count <- count + 1
```

`adoptTarget` checks the node against its expectation (raising `HydrationMismatchException` as Interfaces formats it), replaces a `<!--_-->` text target with an empty text node through `replaceChild` (recording the inverse), and returns the node to store. Region kinds: `ListRows` and `ViewHole` expect the data `[`; `SelectedArm` data starting with `[a:`; `BoundaryContent` data `[c`, `[p` or `[e`.

- [ ] **Step 4: Implement adoption in `Mounting.fs` and the entry point**

`OwnedRange` takes its boundary nodes from the constructor (the default constructor keeps creating `ranvier:start`/`ranvier:end`); `AddAdopted (node)` adds a claimed entry without inserting; `Adopt (parent)` validates that the entries are children of `parent` in order and sets the placement. `release` with the keep flag set unclaims every entry and generated node without removing it. `MountState.Run` with a cursor calls `range.Adopt host` where it calls `range.Attach`. `RegionOf` with a cursor clears between the markers, records the inverse, and mounts the region with the cursor withheld from its rows (`RowBuilder.Hydration` returns null; `MountRow` passes null). Write `Hydration.fs` to the Interfaces block.

- [ ] **Step 5: Run the tests and confirm they pass**

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs tests/template.test.mjs tests/range.test.mjs tests/boundary.test.mjs`
Expected: PASS.

- [ ] **Step 6: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0; `npm run test:browser` runs the hydration cases in Chromium through `hydration.spec.mjs`.

- [ ] **Step 7: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Hydration.fs experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Foundation/Hydration.fs experiments/Ranvier.View.Foundation/Ranvier.View.Foundation.fsproj experiments/Ranvier.View.Foundation/TemplateDom.fs experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj
git diff --cached --check
git commit -F - <<'EOF'
feat(view): hydrate a template by adopting its server nodes and comparing values before the first write

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 14: Root-level mismatch policy and `Strict` (H1, Decision 2)

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Hydration.fs`
- Test: `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`

**Interfaces:**
- Consumes: `HydrationMismatchException`, `HydrationCursor.Restore`, the keep-nodes release (Task 13).
- Produces: `hydrateWith` catches a `HydrationMismatchException` raised by `Run` (found directly or among `AggregateException` inner exceptions at any depth, through `TemplateDom.findMismatch (error: exn) : HydrationMismatchException option`, internal). Under `Strict` it rethrows (the host holds what the server wrote). Otherwise it removes the nodes between the root markers, mounts with `mountWith SynchronousCommits graph parentOwner report host (Some rootEnd) build`, calls `report [ mismatch ]` once, and returns that mount. A host without a root marker under `Strict` raises; otherwise the client mounts at the end of the host, leaving its children in place, and reports once. Any other exception propagates after the restore (Review Focus 5).

- [ ] **Step 1: Write the failing tests**

In `HydrationChecks.fs`, insert after `carriageReturn`:

```fsharp
let private otherRoot =
    Component.define (fun _ (title: string) -> title) (fun t -> t.span [||] [| t.text (fun title -> title) |])

let private singleMismatch (reports: ResizeArray<exn list>) =
    match List.ofSeq reports with
    | [ [ TemplateDom.HydrationMismatchException _ ] ] -> true
    | _ -> false

let private rootMismatch () =
    use graph = new Graph ()
    let host = serve card "a"
    let before = host.innerHTML

    let strictOutcome =
        try
            hydrate graph strictOptions host otherRoot "a" |> ignore
            None
        with TemplateDom.HydrationMismatchException (_, expected, found) ->
            Some (expected, found)

    equal (Some ("<span>", "div"), before) (strictOutcome, host.innerHTML)
    let reports = ResizeArray<exn list>()
    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host otherRoot "a"
    equal (true, "a", true) (singleMismatch reports, host.textContent, isNull (box (host.querySelector "div")))
    mounted.Dispose ()
    equal true (isNull (box (host.querySelector "span")))

let private linkCard =
    Component.define (fun _ (title: string) -> Api.createSignal title) (fun t ->
        t.div
            [| t.Attrs.title (t.from id) |]
            [| t.text "Title: "; t.text (t.from id); t.a [| t.Events.onClick (fun _ _ -> ()) |] [| t.text "0" |]; t.input [||] |])

let private nestedMismatchNamesNodes () =
    use graph = new Graph ()
    let host = serve card "a"

    let outcome =
        try
            hydrate graph strictOptions host linkCard "a" |> ignore
            None
        with TemplateDom.HydrationMismatchException (_, expected, found) ->
            Some (expected, found)

    equal (Some ("<a>", "button")) outcome

let private missingRootMarker () =
    use graph = new Graph ()
    let host = host ()
    host.innerHTML <- "<div>plain</div>"

    let strictPath =
        try
            hydrate graph strictOptions host card "a" |> ignore
            None
        with TemplateDom.HydrationMismatchException (path, _, _) ->
            Some path

    equal (Some "root", "<div>plain</div>") (strictPath, host.innerHTML)
    let reports = ResizeArray<exn list>()
    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host card "a"
    equal (true, "plainTitle: a0") (singleMismatch reports, host.textContent)
    mounted.Dispose ()
    equal "<div>plain</div>" host.innerHTML

let private setupThrowsDuringHydration () =
    use graph = new Graph ()
    let host = serve card "a"
    let before = host.innerHTML
    let throwing = Component.define (fun _ (_: string) -> failwith "boom") (fun t -> t.div [||] [||])

    let message =
        try
            hydrate graph lenientOptions host throwing "a" |> ignore
            ""
        with error ->
            error.Message

    equal ("boom", before) (message, host.innerHTML)
```

Add to `cases`:

```fsharp
        "a structural mismatch outside every region re-renders the root and reports once; Strict raises and leaves the host as the server wrote it", rootMismatch
        "a mismatch names the expected and the found node", nestedMismatchNamesNodes
        "a host without a root marker raises under Strict and mounts fresh otherwise", missingRootMarker
        "a setup failure during hydration propagates and leaves the server HTML in place", setupThrowsDuringHydration
```

- [ ] **Step 2: Run them and confirm they fail**

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs`
Expected: FAIL at `rootMismatch` (lenient hydration raises instead of re-rendering) and at `missingRootMarker`.

- [ ] **Step 3: Implement the policy in `Hydration.fs`**

As Interfaces says. The root re-render runs after `Run`'s own disposal and restore, so the cleared nodes are the server's.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Hydration.fs experiments/Ranvier.View.Foundation/TemplateDom.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Foundation/Hydration.fs experiments/Ranvier.View.Foundation/TemplateDom.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): re-render the root after a structural hydration mismatch and raise it under Strict

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 15: Hydrate list regions; region-level mismatch (H2)

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`RowBuilder`, `RegionSite.Hydrating`, `RegionSite.Reset`, `ListRegion`, `RegionOf`)
- Test: `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`

**Interfaces:**
- Consumes: `HydrationCursor`, `logicalNext`, `findMismatch` (Tasks 13–14); `RegionSite.NextRow` (Task 6).
- Produces:
  - `RegionSite.Hydrating: bool` (settable): true during the first pass of a region whose mount has a cursor; `RowBuilder.Hydration` returns the mount's cursor only then.
  - `RegionSite.Reset: unit -> unit`: disposes the region's owner, clears its laid-out and held rows, and sets its errors back to the first-pass state. The row counter keeps counting.
  - `RowBuilder.Build` while hydrating: before every row after the first, a `<!--!-->` at the cursor is removed (recorded with `Undo`); `materialise` adopts the row; a row with no roots adopts the `<!--ranvier:row-->` comment at the cursor (a mismatch when absent); the row's nodes stay in place.
  - `ListRegion`'s first pass while hydrating: `cursor.Next` is saved and set to `regionStart.nextSibling`; after the pass, `cursor.Next` must be the end marker (a mismatch naming the extra node otherwise); `site.LaidOut` receives the rows in order without `Row.Layout`; `cursor.Next` is restored.
  - `RegionOf` while hydrating a `ListRows` region: on a `HydrationMismatchException` (through `findMismatch`) and not `Strict`, `site.Reset ()`, removes the server nodes between the markers, calls the mount's `report [ mismatch ]` once, and runs the region again without the cursor. Under `Strict` the mismatch propagates.

- [ ] **Step 1: Write the failing tests**

In `HydrationChecks.fs`, insert after `setupThrowsDuringHydration`:

```fsharp
let private a = { Id = 1; Text = "a" }
let private b = { Id = 2; Text = "b" }
let private c = { Id = 3; Text = "c" }

let mutable private liveItems: Signal<Listed array> = Unchecked.defaultof<_>

let private live (items: Listed array) =
    let signal = Api.createSignal items
    liveItems <- signal
    signal

let private liveList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul [||] [| t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) listedRow |])

let private listsHydrate () =
    use graph = new Graph ()
    let host = serve liveList [| a; b |]
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host liveList [| a; b |]
    equal true (sameNodes before (nodesOf host))
    let first, second = (all host "li")[0], (all host "li")[1]
    graph.Run (fun () -> liveItems.Value <- [| b; a |])
    equal ("ba", true) ((element host "ul").textContent, sameNodes [| second; first |] (all host "li"))
    graph.Run (fun () -> liveItems.Value <- [| b; a; c |])
    equal "bac" (element host "ul").textContent
    graph.Run (fun () -> liveItems.Value <- [| b; c |])
    equal "bc" (element host "ul").textContent
    graph.Run (fun () -> liveItems.Value <- [||])
    equal "<!--[--><!--]-->" (element host "ul").innerHTML
    mounted.Dispose ()

let private innerCell =
    Component.define (fun _ (text: unit -> string) -> text) (fun t -> t.element (HtmlTag "b") [||] [| t.text (fun text -> text ()) |])

let private nestedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.li [||] [| t.each id (fun (item: unit -> Listed) -> [| (item ()).Text + "1"; (item ()).Text + "2" |]) innerCell |])

let private nestedList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.ul [||] [| t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) nestedRow |])

let private nestedRegions () =
    use graph = new Graph ()
    let host = serve nestedList [| a; b |]
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host nestedList [| a; b |]
    equal (true, "a1a2b1b2") (sameNodes before (nodesOf host), (element host "ul").textContent)
    graph.Run (fun () -> liveItems.Value <- [| { a with Text = "z" }; b |])
    equal "z1z2b1b2" (element host "ul").textContent
    mounted.Dispose ()

let mutable private liveTexts: Signal<string array> = Unchecked.defaultof<_>

let private liveStrings (items: string array) =
    let signal = Api.createSignal items
    liveTexts <- signal
    signal

let private textRow =
    Component.define (fun _ (item: unit -> string) -> item) (fun t -> t.text (fun item -> item ()))

let private textList =
    Component.define (fun _ (items: string array) -> liveStrings items) (fun t ->
        t.div [||] [| t.each id (fun (items: Signal<string array>) -> items.Value) textRow |])

let private textRowSeparators () =
    use graph = new Graph ()
    let host = serve textList [| "a"; "b" |]
    equal "<!--[r--><div><!--[-->a<!--!-->b<!--]--></div><!--]-->" host.innerHTML
    let mounted = hydrate graph strictOptions host textList [| "a"; "b" |]
    equal ("<!--[-->ab<!--]-->", 4.) ((element host "div").innerHTML, (element host "div").childNodes.length)
    graph.Run (fun () -> liveTexts.Value <- [| "b"; "a"; "c" |])
    equal "bac" (element host "div").textContent
    mounted.Dispose ()

let private emptyRow =
    Component.define (fun _ (_: unit -> string) -> ()) (fun t -> t.fragment [||])

let private emptyList =
    Component.define (fun _ (items: string array) -> liveStrings items) (fun t ->
        t.div [||] [| t.each id (fun (items: Signal<string array>) -> items.Value) emptyRow |])

let private zeroRootRows () =
    use graph = new Graph ()
    let host = serve emptyList [| "a"; "b" |]
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host emptyList [| "a"; "b" |]
    equal true (sameNodes before (nodesOf host))
    graph.Run (fun () -> liveTexts.Value <- [| "a"; "b"; "c" |])
    equal 5. (element host "div").childNodes.length
    graph.Run (fun () -> liveTexts.Value <- [| "b"; "c" |])
    equal (4., true) ((element host "div").childNodes.length, obj.ReferenceEquals (before[4], childAt (element host "div") 1))
    mounted.Dispose ()

let private rowCountMismatch () =
    for serverItems, clientItems in [ [| a; b |], [| a; b; c |]; [| a; b; c |], [| a |] ] do
        use graph = new Graph ()
        let host = serve liveList serverItems
        let start = (element host "ul").firstChild.Value
        let reports = ResizeArray<exn list>()
        let mounted = Hydration.hydrate graph None reports.Add lenientOptions host liveList clientItems
        let expected = String.concat "" [ for item in clientItems -> item.Text ]
        equal (true, expected, true) (singleMismatch reports, (element host "ul").textContent, obj.ReferenceEquals (start, (element host "ul").firstChild.Value))
        mounted.Dispose ()

    use graph = new Graph ()
    let host = serve liveList [| a; b |]
    let before = host.innerHTML

    let raised =
        try
            hydrate graph strictOptions host liveList [| a |] |> ignore
            false
        with TemplateDom.HydrationMismatchException _ ->
            true

    equal (true, before) (raised, host.innerHTML)

let private regionFirstRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t ->
        t.fragment [| t.each id (fun (item: unit -> Listed) -> [| (item ()).Text |]) innerCell; t.span [||] [| t.text "end" |] |])

let private regionFirstList =
    Component.define (fun _ (items: Listed array) -> live items) (fun t ->
        t.div [||] [| t.each (fun (item: Listed) -> item.Id) (fun (items: Signal<Listed array>) -> items.Value) regionFirstRow |])

let private rowStartsWithRegion () =
    use graph = new Graph ()
    let host = serve regionFirstList [| a; b |]
    equal "<!--[r--><div><!--[--><!--[--><b>a</b><!--]--><span>end</span><!--[--><b>b</b><!--]--><span>end</span><!--]--></div><!--]-->" host.innerHTML
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host regionFirstList [| a; b |]
    equal true (sameNodes before (nodesOf host))
    graph.Run (fun () -> liveItems.Value <- [| b; a |])
    equal "bendaend" (element host "div").textContent
    mounted.Dispose ()

let private reorderedItems () =
    use graph = new Graph ()
    let host = serve liveList [| a; b |]
    let serverRows = all host "li"
    let mounted = hydrate graph strictOptions host liveList [| b; a |]
    equal ("ba", true) ((element host "ul").textContent, sameNodes serverRows (all host "li"))
    graph.Run (fun () -> liveItems.Value <- [| a; b |])
    let rows = all host "li"
    equal ("ab", true, true) ((element host "ul").textContent, obj.ReferenceEquals (rows[0], serverRows[1]), obj.ReferenceEquals (rows[1], serverRows[0]))
    mounted.Dispose ()
```

Add to `cases`:

```fsharp
        "list regions hydrate, then reorder, append, remove and clear like mounted lists", listsHydrate
        "nested list regions hydrate and update", nestedRegions
        "text-adjacent row separators are removed during hydration", textRowSeparators
        "zero-root rows hydrate and keep their place", zeroRootRows
        "a list with fewer or more client items than server rows re-renders the region and reports once", rowCountMismatch
        "a row whose first root is a region hydrates as one logical position", rowStartsWithRegion
        "a same-count list with reordered items hydrates positionally and later moves keep keyed identity", reorderedItems
```

`regionsMountWithContent` (Task 13) keeps passing: its rows are now adopted instead of recreated.

- [ ] **Step 2: Run them and confirm they fail**

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs`
Expected: FAIL at `listsHydrate` (`sameNodes` is false: the rows were recreated).

- [ ] **Step 3: Implement list hydration**

As Interfaces says. `ListRegion` reads `site.Hydrating` once at the start of its first `reconcile`; the hydrating pass skips the window validation and the flush of released rows (it releases none).

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs tests/template.test.mjs tests/range.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): hydrate list rows in place and re-render a list region after a structural mismatch

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 16: Hydrate arms by structural hash; view holes re-render (H2)

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`ArmRegion`, `RegionOf`), `experiments/Ranvier.View.Foundation/TemplateDom.fs` (`compiledOf`)
- Test: `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`

**Interfaces:**
- Consumes: `Compiled.Hash` (Task 13); `RegionSite.Hydrating`, `RegionSite.Reset`, the region-level catch (Task 15).
- Produces:
  - `TemplateDom.compiledOf (definition: TemplateDefinition) : Compiled` (internal): the definition's compiled form, compiling it on first use (the lines that open `materialise` today).
  - `ArmRegion`'s first selection while hydrating: the start marker's data must equal `"[a:" + (compiledOf arm.Definition).Hash`, or a `HydrationMismatchException (…, "<!--[a:" + hash + "-->", "<!--" + data + "-->")` is raised and handled by Task 15's catch (extended to `SelectedArm`); the arm adopts its row from `regionStart.nextSibling`, stays in place (no `Row.MoveBefore`), and the cursor must then be at the end marker.
  - `ViewHole` regions keep Task 13's behaviour: the server nodes are removed and the view mounts fresh, with no report.

- [ ] **Step 1: Write the failing tests**

In `HydrationChecks.fs`, insert after `reorderedItems`:

```fsharp
let mutable private liveStage: Signal<Stage> = Unchecked.defaultof<_>

let private liveStaged =
    Component.define
        (fun _ (stage: Stage) ->
            let signal = Api.createSignal stage
            liveStage <- signal
            signal)
        (fun t ->
            t.div
                [||]
                [|
                    t.matchWith (fun (stage: Signal<Stage>) -> stage.Value) (function
                        | Waiting -> t.arm waitingArm ()
                        | Working progress -> t.arm workingArm progress
                        | Complete result -> t.arm completeArm result)
                |])

let private armsHydrate () =
    use graph = new Graph ()
    let host = serve liveStaged (Working 3)
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host liveStaged (Working 3)
    equal true (sameNodes before (nodesOf host))
    let span = host.querySelector "span"
    graph.Run (fun () -> liveStage.Value <- Working 4)
    equal ("working 4", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    graph.Run (fun () -> liveStage.Value <- Complete "x")
    equal "complete x" (element host "div").textContent
    mounted.Dispose ()

let private armPayloadCorrected () =
    use graph = new Graph ()
    let host = serve liveStaged (Working 3)
    let span = host.querySelector "span"
    let mounted = hydrate graph strictOptions host liveStaged (Working 4)
    equal ("working 4", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    mounted.Dispose ()

let private differentArm () =
    use graph = new Graph ()
    let host = serve liveStaged (Working 3)
    let reports = ResizeArray<exn list>()
    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host liveStaged Waiting
    equal (true, "waiting") (singleMismatch reports, (element host "div").textContent)
    mounted.Dispose ()
    let host = serve liveStaged (Working 3)

    let raised =
        try
            hydrate graph strictOptions host liveStaged Waiting |> ignore
            false
        with TemplateDom.HydrationMismatchException _ ->
            true

    equal true raised

let private xArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "x" |])

let private yArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "y" |])

let private flagged =
    Component.define (fun _ (flag: bool) -> flag) (fun t ->
        t.div [||] [| t.branch (fun (flag: bool) -> flag) (fun flag -> if flag then t.arm xArm () else t.arm yArm ()) |])

let private equalShapes () =
    use graph = new Graph ()
    let host = serve flagged true
    let reports = ResizeArray<exn list>()
    let mounted = Hydration.hydrate graph None reports.Add lenientOptions host flagged false
    equal (true, "y") (singleMismatch reports, (element host "div").textContent)
    mounted.Dispose ()

let private viewHolesRerender () =
    use graph = new Graph ()
    let host = serve holed { Caption = "A"; Body = Program.element (HtmlTag "em") [||] [| Program.text "server" |] }
    let serverEm = host.querySelector "em"
    let mounted = hydrate graph strictOptions host holed { Caption = "A"; Body = Program.element (HtmlTag "em") [||] [| Program.text "client" |] }
    equal ("Aclient", false) ((element host "div").textContent, obj.ReferenceEquals (serverEm, host.querySelector "em"))
    mounted.Dispose ()
```

Add to `cases`:

```fsharp
        "arms hydrate under an equal hash and keep their nodes across payload updates", armsHydrate
        "an arm of the same definition with another payload hydrates in place and corrects its values", armPayloadCorrected
        "a different selected arm re-renders the region and reports once; Strict raises", differentArm
        "two arms with equal shape and different static text are distinguished by hash", equalShapes
        "view holes remove the server content and mount the view fresh", viewHolesRerender
```

- [ ] **Step 2: Run them and confirm they fail**

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs`
Expected: FAIL at `armsHydrate` (`sameNodes` is false) and at `differentArm` (no report).

- [ ] **Step 3: Implement arm hydration**

As Interfaces says.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs tests/template.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/TemplateDom.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Foundation/Mounting.fs experiments/Ranvier.View.Foundation/TemplateDom.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): hydrate arms whose structural hash matches and re-render the region otherwise

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 17: Hydrate boundaries (H3)

**Files:**
- Modify: `experiments/Ranvier.View.Foundation/Mounting.fs` (`ViewBoundary`, `BoundaryRegion`, `MountRow` with a cursor)
- Test: `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs`

**Interfaces:**
- Consumes: the cursor and the region-level catch (Tasks 13–16).
- Produces: `MountRow` gains a trailing `cursor: HydrationCursor` (null outside hydration) — with a cursor, the nested mount adopts its nodes in place, inserting its two range comments around them. `ViewBoundary` reads the start marker's view (`c`, `p`, `e`) when its region hydrates:
  - `[c`: the content mounts with the cursor at `regionStart.nextSibling`; the cursor must then be at the end marker. With nothing waiting, the content is revealed in place. With a pending read, the content's nodes stay between the markers and the pending view is not set up (ruling 7); the reveal places the same nodes.
  - `[p`: the content mounts without the cursor (detached, as in a mount). When it is ready at once, the server nodes between the markers are removed and the content revealed. Otherwise the pending view mounts with the cursor, adopting the server's pending nodes in place.
  - `[e`: the server nodes between the markers are removed (recorded with `Undo`) and the boundary mounts as in a mount.

- [ ] **Step 1: Write the failing tests**

In `HydrationChecks.fs`, insert after `viewHolesRerender`:

```fsharp
type private Outcome =
    | Settles of string
    | Pends
    | Fails

let mutable private outcome = Pends
let mutable private contentSource: AsyncSource<string> = Unchecked.defaultof<_>
let private boundaryLog = ResizeArray<string>()

let private hydratedPending =
    Component.define (fun _ () -> boundaryLog.Add "pending setup") (fun t -> t.span [||] [| t.text "loading" |])

let private hydratedFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let private hydratedContent =
    Component.define
        (fun _ (_: BoundaryStatus) ->
            boundaryLog.Add "content setup"
            let source = Api.createAsyncSource<string> ()
            contentSource <- source

            match outcome with
            | Settles text -> source.Settle text
            | Fails -> source.Fail (Exception "failed")
            | Pends -> ()

            source)
        (fun t -> t.span [||] [| t.text (fun (source: AsyncSource<string>) -> source.Value) |])

let private bounded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary hydratedPending hydratedFailed hydratedContent |])

/// <summary>The server HTML of <c>bounded</c> with the content's source in <c>server</c>, then the client's outcome set to <c>client</c>.</summary>
let private serveBoundary server client =
    outcome <- server
    let host = serve bounded ()
    outcome <- client
    boundaryLog.Clear ()
    host

let private contentHydratesInPlace () =
    use graph = new Graph ()
    let host = serveBoundary (Settles "server") (Settles "server")
    equal "<!--[r--><div><!--[c--><span>server</span><!--]--></div><!--]-->" host.innerHTML
    let before = nodesOf host
    let mounted = hydrate graph strictOptions host bounded ()
    equal (true, [ "content setup" ]) (sameNodes before (nodesOf host), List.ofSeq boundaryLog)
    mounted.Dispose ()

let private serverContentStaysPending () =
    use graph = new Graph ()
    let host = serveBoundary (Settles "server") Pends
    let span = host.querySelector "span"
    let mounted = hydrate graph strictOptions host bounded ()
    equal ("server", [ "content setup" ]) ((element host "div").textContent, List.ofSeq boundaryLog)
    graph.Run (fun () -> contentSource.Settle "client")
    equal ("client", true) ((element host "div").textContent, obj.ReferenceEquals (span, host.querySelector "span"))
    mounted.Dispose ()

let private serverPendingHydrates () =
    use graph = new Graph ()
    let host = serveBoundary Pends Pends
    equal "<!--[r--><div><!--[p--><span>loading</span><!--]--></div><!--]-->" host.innerHTML
    let before = nodesOf host
    let loading = host.querySelector "span"
    let mounted = hydrate graph strictOptions host bounded ()
    equal (true, [ "content setup"; "pending setup" ]) (sameNodes before (nodesOf host), List.ofSeq boundaryLog)
    graph.Run (fun () -> contentSource.Settle "client")
    equal ("client", true) ((element host "div").textContent, isNull (box (unbox<Node> loading).parentNode))
    mounted.Dispose ()

let private serverErrorRerenders () =
    use graph = new Graph ()
    let host = serveBoundary Fails (Settles "client")
    equal "<!--[r--><div><!--[e--><span>failed</span><!--]--></div><!--]-->" host.innerHTML
    let failed = host.querySelector "span"
    let mounted = hydrate graph strictOptions host bounded ()
    equal ("client", false) ((element host "div").textContent, obj.ReferenceEquals (failed, host.querySelector "span"))
    mounted.Dispose ()
```

Add to `cases`:

```fsharp
        "boundaries: content hydrates in place when ready", contentHydratesInPlace
        "boundaries: server content stays as the pending view until reveal", serverContentStaysPending
        "boundaries: a server pending view hydrates and the content reveals", serverPendingHydrates
        "boundaries: a server error view re-renders the boundary", serverErrorRerenders
```

- [ ] **Step 2: Run them and confirm they fail**

Run (from `examples/Fable.Ranvier.Playground`): `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs`
Expected: FAIL at `contentHydratesInPlace` (`sameNodes` is false) and at `serverContentStaysPending` (the pending view replaced the server content).

- [ ] **Step 3: Implement boundary hydration**

As Interfaces says. `Reveal` already moves the content before the end marker; for content whose nodes are in place the move keeps their order.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `npm run compile:tests -- --define RANVIER_VIEW_COUNTERS && node --test tests/hydration.test.mjs tests/boundary.test.mjs`
Expected: PASS.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Foundation/Mounting.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): hydrate boundaries from the view their server marker records

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 18: State transfer, identity after hydration, forms and normalisation in Chromium (H4)

**Files:**
- Modify: `experiments/Ranvier.View.Portable/Server.fs` (`stateScript`), `experiments/Ranvier.View.Foundation/Hydration.fs` (`readState`)
- Test: `experiments/Ranvier.View.Portable/Checks/ServerChecks.fs`, `examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs` (`cases` and `browserCases`)

**Interfaces:**
- Produces:
  - `Server.stateScript (id: string) (json: string) : string` — `<script type="application/json" id="` + `Html.escape true id` + `">` + `json` with `<` replaced by `\u003c`, U+2028 by `\u2028` and U+2029 by `\u2029` (six characters each) + `</script>`.
  - `Hydration.readState (id: string) : string option` — the `textContent` of `document.getElementById id`; `None` when absent.

- [ ] **Step 1: Write the failing tests**

In `ServerChecks.fs`, insert before `let cases`:

```fsharp
let private stateScriptEscapes () =
    equal
        "<script type=\"application/json\" id=\"s&quot;\">{\"a\":\"\\u003c/script>\\u2028\"}</script>"
        (Server.stateScript "s\"" "{\"a\":\"</script>\u2028\"}")
```

Add to `cases`: `"stateScript escapes the script end and line separators", stateScriptEscapes`.

In `HydrationChecks.fs`, insert after `serverErrorRerenders`:

```fsharp
let private stateRoundTrip () =
    let json = "{\"a\":\"</script><!--\",\"b\":\"\u2028\u2029\"}"
    let container = host ()
    globalThis.document.body.appendChild container |> ignore
    container.innerHTML <- Server.stateScript "state" json
    let read = Hydration.readState "state"
    let same = read |> Option.map (fun text -> JS.JSON.stringify (JS.JSON.parse text) = JS.JSON.stringify (JS.JSON.parse json))
    equal (Some true, None) (same, Hydration.readState "absent")
    container.remove ()

let mutable private liveIds: Signal<int array> = Unchecked.defaultof<_>

let private liveIdentified =
    Component.define
        (fun setup (items: int array) ->
            identityLog.Add (setup.CreateId ())
            identityLog.Add (setup.CreateId ())
            let signal = Api.createSignal items
            liveIds <- signal
            signal)
        (fun t ->
            t.div
                [||]
                [|
                    t.render idLeaf ignore
                    t.ul [||] [| t.each id (fun (items: Signal<int array>) -> items.Value) idRow |]
                    t.render idLeaf ignore
                    t.matchWith (fun (items: Signal<int array>) -> items.Value.Length) (fun count -> t.arm idArm count)
                |])

let private identityContinues () =
    use graph = new Graph ()
    identityLog.Clear ()
    let host = host ()
    host.innerHTML <- Server.render { omit with IdPrefix = "a" } liveIdentified [| 7; 8 |]
    let server = List.sort (List.ofSeq identityLog)
    identityLog.Clear ()
    let options: Hydration.HydrationOptions = { IdPrefix = "a"; Strict = true }
    let mounted = Hydration.hydrate graph None strictReport options host liveIdentified [| 7; 8 |]
    equal (identityIds "a", identityIds "a") (server, List.sort (List.ofSeq identityLog))
    identityLog.Clear ()
    graph.Run (fun () -> liveIds.Value <- [| 7; 8; 9 |])
    equal [ "a-0.r0.2-0"; "a-0.r0.2.c0-0"; "a-0.r0.2.r0.0-0" ] (List.sort (List.ofSeq identityLog))
    mounted.Dispose ()
```

Add to `cases`:

```fsharp
        "state script round-trips JSON containing </script> and U+2028", stateRoundTrip
        "CreateId is equal on server and hydrated client, and rows created after hydration continue their region's counter", identityContinues
```

Insert before `browserCases`:

```fsharp
let private valued =
    Component.define (fun _ (value: string) -> Api.createSignal value) (fun t -> t.input [| t.Props.Input.value (t.from id) |])

let private attached (host: HTMLElement) =
    globalThis.document.body.appendChild host |> ignore
    host

let private inputEditKept () =
    use graph = new Graph ()
    let host = attached (serve valued "server")
    let input = host.querySelector "input" |> unbox<HTMLInputElement>
    input.value <- "typed"
    let mounted = hydrate graph strictOptions host valued "server"
    equal "typed" input.value
    mounted.Dispose ()
    host.remove ()

let private formsHydrate () =
    use graph = new Graph ()
    let state = { ServerChecks.FormState.Text = "t"; On = true; Choice = "b" }
    let host = serve ServerChecks.formFixture state
    let mounted = hydrate graph strictOptions host ServerChecks.formFixture state
    let checkbox = host.querySelector "input" |> unbox<HTMLInputElement>
    let textarea = host.querySelector "textarea" |> unbox<HTMLTextAreaElement>
    let select = host.querySelector "select" |> unbox<HTMLSelectElement>
    equal (true, "t", "b") (checkbox.``checked``, textarea.value, select.value)
    mounted.Dispose ()

let private focusSurvives () =
    use graph = new Graph ()
    let host = attached (serve valued "server")
    let input = host.querySelector "input" |> unbox<HTMLInputElement>
    input.focus ()
    let mounted = hydrate graph strictOptions host valued "server"
    equal true (obj.ReferenceEquals (box globalThis.document.activeElement, box input))
    mounted.Dispose ()
    host.remove ()

type private Shape =
    | El of string * Shape list
    | Tx of string

let rec private build (shape: Shape) : Node =
    match shape with
    | Tx text -> globalThis.document.createTextNode text :> Node
    | El (tag, children) ->
        let created = globalThis.document.createElement tag

        for child in children do
            created.appendChild (build child) |> ignore

        created :> Node

/// <summary>The serialisation of a DOM built node by node, and of the same markup after the parser reads it in a div.</summary>
let private parsedAgain (shape: Shape) =
    let built = globalThis.document.createElement "div"
    built.appendChild (build shape) |> ignore
    let reparsed = globalThis.document.createElement "div"
    reparsed.innerHTML <- built.innerHTML
    built.innerHTML, reparsed.innerHTML

let private rejectedMarkupRestructures () =
    let shapes =
        [
            "table-row", El ("table", [ El ("tr", [ El ("td", []) ]) ])
            "table-section", El ("div", [ El ("tbody", []) ])
            "table-cell", El ("table", [ El ("tbody", [ El ("td", []) ]) ])
            "table-column", El ("table", [ El ("col", []) ])
            "table-foster", El ("table", [ Tx "x" ])
            "table-foster", El ("table", [ El ("tbody", [ El ("tr", [ El ("div", []) ]) ]) ])
            "paragraph-close", El ("p", [ El ("span", [ El ("div", []) ]) ])
            "nested-anchor", El ("a", [ El ("span", [ El ("a", []) ]) ])
            "nested-form", El ("form", [ El ("div", [ El ("form", []) ]) ])
        ]

    let unchanged =
        [
            for rule, shape in shapes do
                let built, reparsed = parsedAgain shape

                if built = reparsed then
                    rule
        ]

    equal [] unchanged

[<Emit("$0.replace(/<!--.*?-->/g, '')")>]
let private withoutComments (html: string) : string = jsNative

let private acceptedFixturesParse () =
    let check (definition: Component<'props>) (props: 'props) =
        use graph = new Graph ()
        let parsed = globalThis.document.createElement "div"
        parsed.innerHTML <- Server.render omit definition props
        let client = host ()
        let mounted = mount graph client (Component.render definition props)
        equal (withoutComments client.innerHTML) (withoutComments parsed.innerHTML)
        mounted.Dispose ()

    check greeting greetingProps
    check named greetingProps
    check pair greetingProps
    check listed listedItems
    check carded greetingProps
    check besideText greetingProps
    check staged (Working 3)
    check keyedPage 3
```

Replace `browserCases` with:

```fsharp
let browserCases: (string * (unit -> unit)) array =
    [|
        "textarea value and pre text keep a leading line feed after parsing", leadingLineFeeds
        "an input edited before hydration keeps its value when the server and client values agree", inputEditKept
        "checkbox, textarea and select hydrate their server state", formsHydrate
        "focus inside a hydrated input survives hydration", focusSurvives
        "each content-model rule's markup parses differently from the client DOM", rejectedMarkupRestructures
        "every accepted fixture's server HTML parses to the client DOM's serialisation", acceptedFixturesParse
    |]
```

Fixtures with serialised properties (`formFixture`, `componentPage`) are left out of `acceptedFixturesParse`: the client writes their values as properties, which `innerHTML` does not show. The `select-content` rule has no Chromium case (ruling 12).

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: the compile fails: `The value, constructor, namespace or type 'stateScript' is not defined`.

- [ ] **Step 3: Implement `stateScript` and `readState`**

As Interfaces says.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj`
Expected: every `PASS` line.

Run (from `examples/Fable.Ranvier.Playground`): `npm run test:dom && npm run test:browser`
Expected: PASS, including both Chromium hydration tests. A rule listed by `rejectedMarkupRestructures` parses without restructuring in this Chromium: stop and report the rule.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Foundation/Hydration.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git add experiments/Ranvier.View.Portable/Server.fs experiments/Ranvier.View.Foundation/Hydration.fs experiments/Ranvier.View.Portable/Checks/ServerChecks.fs examples/Fable.Ranvier.Playground/tests/HydrationChecks.fs
git diff --cached --check
git commit -F - <<'EOF'
feat(view): transfer application state through a JSON script and check forms and normalisation in Chromium

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 19: Mutation checks

**Files:**
- No committed source change. Each mutant is applied, tested and reverted.
- Modify: `docs/research/2026-10-19-server-html-hydration.md` (mutation section)

**Interfaces:**
- Consumes: every task above.

For each mutant: apply the edit, run `dotnet run --project experiments/Ranvier.View.Foundation/RepresentationChecks.fsproj` from the worktree root and `npm run test:dom` from `examples/Fable.Ranvier.Playground`, confirm that the named test fails, then restore the file with `git checkout -- <file>`. A mutant that survives is a missing test: add the test to the owning task's test file, commit it as `test(view): ...`, and rerun.

- [ ] **Step 1: Logical-sibling skip** — in `TemplateDom.logicalNext`, return `node.nextSibling` for every node. Expected failures: `a row whose first root is a region hydrates as one logical position`, `nested list regions hydrate and update`.
- [ ] **Step 2: Arm hash check** — in `ArmRegion`'s hydrating selection, delete the comparison of the marker data with the hash. Expected failure: `two arms with equal shape and different static text are distinguished by hash`.
- [ ] **Step 3: Empty-text placeholder** — in `ServerWriter`, write the escaped `""` instead of `<!--_-->`. Expected failures: `server HTML carries arm, empty-text, separator and zero-root markers`, `empty text slots hydrate through their placeholder and update`.
- [ ] **Step 4: Row separator** — in `ServerWriter`'s list region, delete the `<!--!-->` between text rows. Expected failures: `server HTML carries arm, empty-text, separator and zero-root markers`, `text-adjacent row separators are removed during hydration`.
- [ ] **Step 5: Region-level mismatch catch** — in `RegionOf`, rethrow every mismatch. Expected failure: `a list with fewer or more client items than server rows re-renders the region and reports once`.
- [ ] **Step 6: Attempt-owner disposal** — in `RequestScope.Attempt`, rethrow without disposing the attempt's lifetime. Expected failure: `a boundary's abandoned content attempt runs its cleanups before the pending or error view is written`.
- [ ] **Step 7: Escape fast path** — in `Html.escape`, always build a new string. Expected failure: `escape returns the input string when nothing needs escaping` (.NET).
- [ ] **Step 8: Record the results**

Append to `docs/research/2026-10-19-server-html-hydration.md`:

```markdown
## Mutation checks

| Mutant | Killed by |
|---|---|
| Logical-sibling skip removed | <test names from Step 1> |
| Arm hash check removed | <test name from Step 2> |
| Empty-text placeholder removed | <test names from Step 3> |
| Row separator removed | <test names from Step 4> |
| Region-level mismatch catch removed | <test name from Step 5> |
| Attempt-owner disposal removed | <test name from Step 6> |
| Escape fast path removed | <test name from Step 7> |
```

Fill each cell with the failing test names the runs printed for that mutant.

- [ ] **Step 9: Confirm the tree is clean and commit**

Run: `git status --short | grep -v generated-` — expected: only `docs/research/2026-10-19-server-html-hydration.md` and the files the user's status already listed as untracked.

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
git add docs/research/2026-10-19-server-html-hydration.md
git diff --cached --check
git commit -F - <<'EOF'
docs(research): record the server HTML and hydration mutation checks

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```

---

### Task 20: Hydration benchmarks against mount and Solid 2, and status (H5)

**Files:**
- Create: `examples/Fable.Ranvier.Playground/tests/HydrationPerf.fs`, `examples/Fable.Ranvier.Playground/benchmarks/frameworks/hydration-solid.jsx`, `hydration-page.mjs`, `hydration-bench.mjs`
- Modify: `examples/Fable.Ranvier.Playground/tests/DomTests.fsproj` (compile `HydrationPerf.fs` after `HydrationChecks.fs`, so it follows the `ServerFixtures.fs` link), `benchmarks/frameworks/page.mjs` (`import "./hydration-page.mjs";`), `benchmarks/frameworks/vite.config.mjs` (compile `hydration-solid.jsx` with `generate: "dom", hydratable: true`)
- Modify: `docs/research/2026-10-19-server-html-hydration.md`, `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md` (open item 7 and Task 9 checkboxes)
- Create (benchmark output): `docs/research/framework-performance/investigation/server-html/hydration.json`

**Interfaces:**
- Produces (module `HydrationPerf`): for `fixture` in `component` (rows 1/16/128, `ServerFixtures.componentPage`) and `keyed` (rows 1000/10000, `ServerFixtures.keyedPage`): `serverHtml (fixture: string) (rows: int) : string` (`Server.render` with `Omit`), `hydrateFixture (fixture: string) (host: HTMLElement) (rows: int) : unit -> unit` and `mountFixture (fixture: string) (host: HTMLElement) (rows: int) : unit -> unit` (each returns the disposal, which also disposes its graph).
- Produces (`window.hydrationBench` in `hydration-page.mjs`): `sample(mode, fixture, rows, count)` with `mode` in `ranvier-hydrate`, `ranvier-mount`, `solid-hydrate`: fills `count` fresh hosts with the fixture's server HTML outside the timed region (none for `ranvier-mount`), then times the calls and returns milliseconds per call; `firstClick(mode, fixture, rows)`: milliseconds from the call to the end of the first handled click on the first row's button or link; `retain(mode, fixture, rows, count)` as `boundary-page.mjs` defines it.

- [ ] **Step 1: Write the fixtures and the page**

`HydrationPerf.fs` uses `Hydration.hydrate` with `{ IdPrefix = "r"; Strict = true }` and `Mounting.mount`, each in its own `new Graph ()`. `hydration-solid.jsx` holds the components of `server-solid.jsx` (Task 12) and exports `hydrateComponentPage(host, rows)` and `hydrateKeyedPage(host, rows)` through `hydrate` from `@solidjs/web`. `hydration-bench.mjs --build` also renders Solid 2's server HTML for every fixture and row count in Node (with `server-solid.jsx` compiled `generate: "ssr", hydratable: true`) into `generated-hydration-dist/solid-html.json`, with the hydration bootstrap markup `@solidjs/web`'s server build exports for 2.0.0-rc.14; the page inserts that markup into `document.head` once and fetches the JSON.

- [ ] **Step 2: Write the runner**

`hydration-bench.mjs` follows `boundary-reveal.mjs`: the same server launch, three Chromium processes, 15 rounds after 3 warmups, modes rotated per round and process, a forced GC (`HeapProfiler.collectGarbage`) before each sample, verification before timing (hydrated and mounted hosts hold `rows` rows; a hydrated host's nodes are the server's), first-click timings, and retained heap per row after 32 hydrations against 32 mounts. Output `server-html/hydration.json` with `summaries: [{ fixture, rows, mode, medianUs, byProcessUs }]`, `firstClick`, `heaps` and the `hydrateVersusMount` ratio per fixture and row count.

- [ ] **Step 3: Measure**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom/examples/Fable.Ranvier.Playground
mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
node benchmarks/frameworks/hydration-bench.mjs --build
rmdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock
```

Expected: `server-html/hydration.json` exists, every `verified` entry passed, and `summaries` hold all three modes for every fixture and row count.

- [ ] **Step 4: Record the results and the status**

Append to the research note a section `## Hydration in Chromium` with one row per fixture and row count: hydrate, mount and Solid 2 hydrate median µs, hydrate/mount ratio, first click µs per mode, retained bytes per row after hydration and after mount. State whether hydration time is at or below mount time for every fixture (the spec's target); other ratios are reported, not gated. In `docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md`, replace open item 7 with `7. Done: server HTML and hydration ([plan](2026-10-19-server-html-hydration.md), [measurements](../../research/2026-10-19-server-html-hydration.md)).` and tick Task 9's five checkboxes, each followed by one line naming the task of this plan that evidences it.

- [ ] **Step 5: Run the gates**

Run the gate commands from Global Constraints. Expected: every command exits 0.

- [ ] **Step 6: Commit and push**

```bash
cd C:/Users/shaya/RiderProjects/Ranvier-fable-ranvier-dom
dotnet fantomas examples/Fable.Ranvier.Playground/tests/HydrationPerf.fs
git add examples/Fable.Ranvier.Playground/tests/HydrationPerf.fs examples/Fable.Ranvier.Playground/tests/DomTests.fsproj examples/Fable.Ranvier.Playground/benchmarks/frameworks/hydration-solid.jsx examples/Fable.Ranvier.Playground/benchmarks/frameworks/hydration-page.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/hydration-bench.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/page.mjs examples/Fable.Ranvier.Playground/benchmarks/frameworks/vite.config.mjs docs/research/2026-10-19-server-html-hydration.md docs/research/framework-performance/investigation/server-html/hydration.json docs/superpowers/plans/2026-10-06-fable-ranvier-framework.md
git diff --cached --check
git commit -F - <<'EOF'
perf(view): measure hydration against mount and Solid 2's hydrate and record the server HTML status

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DgoUpTaqPJBUQ7p2oFznr2
EOF
git push origin HEAD:poc/fable-ranvier-dom
```
