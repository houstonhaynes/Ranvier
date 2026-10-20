# View sugar design (plan Task 10)

> **Approved 2026-10-19** with every recommended answer to its open questions. Nothing below is implemented, compiled or measured. API names and signatures are design sketches. The plan requires the CE shapes to be proved with the F# compiler before they are accepted.

Date: 2026-10-19. Branch `poc/fable-ranvier-dom` at `f1e7a7d`, in the `Ranvier-fable-ranvier-dom` worktree. Inputs: [plan Task 10 and "Where we stand"](../plans/2026-10-06-fable-ranvier-framework.md), the [DSL design](2026-10-08-ranvier-view-dsl-design.md) (performance and sugar contract), the [component templates design](2026-10-13-component-templates-design.md), contract C10, variant V06 and risk G14. Code read: `experiments/Ranvier.View.Foundation/{Representation,Typed,Authoring,Components,Template,TemplateDom,Mounting,Scope}.fs`, `Generated/{Html,HtmlTemplate}.fs`, `examples/Fable.Ranvier.Playground/{ComponentsExample.fs,App.fs}`, `tests/{KeyedRowsPerf,FrameworkPerf}.fs`.

## 1. Purpose

Task 10 adds optional sugar: Oxpecker-style or Feliz-style element wrappers, computation-expression (CE) control flow, and possibly a Fable plugin. The sugar has to lower to the existing explicit operations and keep their policies. It must not add per-instance cost, and it must never be required for correctness (C10).

Views are written in two ways today.

| | Authored path | Template path |
|---|---|---|
| Entry point | `createComponent (fun ctx props -> Html.div [| mods |] [| children |])` (`Authoring.fs`, `Generated/Html.fs` `Program`) | `Component.define setup (fun t -> t.div [| parts |] [| children |])` (`Template.fs`, `Generated/HtmlTemplate.fs`) |
| When the view tree is built | Every mount. Setup returns a new `Program.View` closure tree for each instance. | Once per definition. `build` runs inside `Component.define`, and `TemplateDom.compile` runs on the first mount and is cached in `definition.Compiled`. |
| Reactive inputs | Overloads that take a value, `unit -> 'T`, `Signal<'T>` or `Memo<'T>` (`Attrs.title source`) | A value, an `'inst -> 'T` reader, or a `Source` built with `t.from` |
| Control flow | `keyedArray`, `branch` and `boundary` functions | `t.each`, `t.matchWith`, `t.branch`, `t.boundary` and `t.view`. Arms and rows are separate `Component` definitions. |
| Measured cost (Where we stand) | 383 KiB retained and 471 KiB allocated per 128-row mount | 112 KiB retained (0.97× Solid 2) and 142 KiB allocated. Mount is 1.13–1.17× Solid 2. Keyed rows retain 994 B/row at 1k. |

The template path is the fast path. Its main ergonomic cost is ceremony around dynamic structure. Each `each` row and each `matchWith` arm has to be a separate module-level `Component.define` with a hand-written identity setup (`KeyedRowsPerf.fs` has `keyedRow`, `plainArm`, `countedArm` and `armRow`). The two-array element syntax is also noisy compared with Oxpecker.Solid's `div(class'="x") { ... }`.

### Success criteria

1. **Same tree.** For every sugar form, `Template.skeletonLayout` of the sugared definition equals that of its explicit twin: same `Markup`, `Targets` paths, `Owners`, `RootCount` and `Children`. Server HTML from `renderHtml` matches on .NET and under Fable.
2. **Same mounted resources.** Owners, effects, observer edges, listener registrations and native DOM writes match exactly per instance under the `RANVIER_VIEW_COUNTERS` counters, at mount, update and disposal.
3. **No per-instance overhead.**
   - `run.mjs --template-comparison`, with a sugared copy of the benchmark row as one more adapter, has ranges that overlap the explicit adapter's in every phase and width.
   - `keyed-rows.mjs` with a sugared row shows overlapping ranges for every operation.
   - `keyed-heap.mjs` shows the same retained bytes per row (994 B at 1k at the time of writing).
   - An allocation sample of a 128-row mount contains no sugar types (`ElementBuilder`, Delay closures).
4. **Bundle.** Zero bytes when the sugar is not used, because Vite tree-shakes it. When it is used, the increase is reported raw, gzip and Brotli; the proposed budget is 1 kB gzip or less (open question 8). Emitted sugar modules contain 0 `curry`, `uncurry` or `partialApply`, the same check the catalogue plan used.
5. **Correctness without sugar.** A complete application with no sugar stays in the test suite permanently. `ComponentsExample.fs` and the explicit `KeyedRowsPerf.fs` stay unchanged.
6. **Authoring gain, measured.** The keyed-row and arm benchmark fixtures and one playground screen are rewritten with sugar. The spec reports the line and definition counts before and after. The rewrite must remove the separate module-level row and arm definitions.

## 2. Constraints the sugar must not break

- **Construction cost lands at a different time on each path.** On the template path, a wrapper's allocations happen once per definition, at module initialisation. On the authored path they happen on every mount. In the [representation probe](../../research/2026-10-09-view-representation.md), builder sugar kept its object, its Delay closures and its helper calls in the emitted JS. In the [authoring check-in](../../research/authoring-performance/2026-10-12-gc-correction.md), overloaded construction ran at 1.11–1.55× explicit in the browser, with mounted resources identical. Builder sugar is therefore free on the template path and a real mount cost on the authored path.
- **Definition identity is component identity.** `matchWith` keys arms by definition, and `TemplateDom.materialise` compiles a definition on its first mount. A definition created during a region read gets a new identity on every evaluation. The arm then remounts and recompiles on every update.
- **Ordinary `if` and `match` stay ordinary evaluation** (brief C05, G14). Reactive structure uses named operations.
- **No ownership inference and no implicit caching.** The component-templates design rejected implicit caching of `createComponent` closures because those closures capture instance values.
- **The queued commit mode renders templates through `definition.Expand`.** Sugar must lower into `TemplateNode` values that both the compiled and the expanded paths already handle.
- **Typing stays as it is.** A tag fixes `'native`, so an element-specific part on another element is a compile error. Event handlers keep their native event types.

## 3. Approaches

### A. Library sugar over the template builder (recommended)

Generated CE element methods in the style of Oxpecker, `t.Div(parts...) { children }`, plus control-flow members that declare their row and arm definitions inline: `t.show`, `t.forEach` and `t.armOf`. All of this lowers to the existing `TemplateParts` and `Component.define`. The authored path is left unchanged in this slice.

- **Gain:** removes the module-level row and arm definitions and both array brackets. Per-instance cost is zero by construction, because the sugar only runs while `build` runs. Template typing makes the G14 hazard unwritable in most cases: the instance exists only inside part lambdas, so an `if` over instance data does not type-check.
- **Cost:** about 112 generated element methods plus one builder class. Users learn one more spelling. Authored-path users get nothing new.

### B. One sugar vocabulary over both paths (Feliz-style)

A single CE or prop-list surface, generic over the output (`Program.View` or `TemplateView<'inst>`). It could be Feliz's single list (`Html.div [ attr.className "x"; Html.span "y" ]`, where attributes and children share one item type and are separated at build time), or the CE from A applied to `Html.Program` as well.

- **Gain:** one way to write both paths.
- **Cost:**
  - On the authored path, every builder allocation and every partition of attributes from children runs on every mount. That path is already 1.97× Solid 2 on mount and 3.46× on allocation.
  - On the authored path, an `if count.Value > 0 then ...` inside a CE compiles and silently takes a setup-time snapshot (G14).
  - A single list loses the void/non-void split in the type: `Html.input` with children would type-check.
  - The generated surface doubles again.

### C. Fable plugin

A `MemberDeclarationPluginAttribute` (Fable.AST: `Transform: PluginHelper * File * MemberDecl -> MemberDecl`, discovered through `[<assembly: ScanForPlugins>]`), similar to Oxpecker.Solid's `[<SolidComponent>]` and Feliz's `[<ReactComponent>]`. It would either rewrite authored-style code into template definitions, or precompute each template's skeleton markup, paths and slot tables as literals at compile time.

- **Gain:**
  - Authored-style code could get template-path cost.
  - Startup avoids `skeletonLayout` and the `<template>` parse validation, once per definition.
- **Cost:**
  - The plugin runs only under Fable. The .NET server path (`renderHtml`, `Expand`) would run different code from the browser, so the two paths could disagree. Every rewrite would need golden-output and behavioural-equivalence tests.
  - Rewriting authored code means lifting instance captures out of closures, which is the ownership inference and implicit caching the specs rejected.
  - The plugin is tied to the Fable AST and its versions (Fable 5.18.0 is pinned).
  - Precomputation saves only per-definition startup work, which nothing has measured yet.

### Recommendation

Adopt **A**. Keep the authored path explicit; open question 1 covers authored-path sugar. Defer **C**: the only plugin role consistent with the specs is precomputing skeletons, and it needs a startup measurement that justifies it (open question 6). Reject **B**'s single-list form, and adopt its authored-path CE only if `bench:authoring` shows construction within noise.

## 4. Recommended design

All sugar is opt-in: `open Ranvier.View.Foundation.Sugar`. The explicit `t.div parts children` members stay and are the documented equivalent of every sugar form.

### 4.1 Elements as CEs

For each tag, the generator (`tools/Ranvier.Dom.Generate`) emits a PascalCase extension member on `TemplateBuilder<'inst>` that takes `[<ParamArray>]` parts.

- A **normal tag** returns an `ElementBuilder<'native, 'inst>`. Its CE body is the child list.
- A **void tag** returns a `TemplateView<'inst>` directly and has no CE body. Children on a void tag are therefore a compile error.

```fsharp
type TemplateBuilder<'inst> with
    member _.Div([<ParamArray>] parts: Part<Dom.HTMLDivElement, 'inst> array) =
        ElementBuilder<Dom.HTMLDivElement, 'inst>(HtmlTag "div", parts)

    member _.Input([<ParamArray>] parts: Part<Dom.HTMLInputElement, 'inst> array) : TemplateView<'inst> =
        TemplateParts.element (HtmlTag "input") parts [||]
```

The handwritten builder, in `TemplateSugar.fs` after `Template.fs`. It must sit in the Foundation assembly because the `TemplateView` and `TextSlotNode` constructors are internal.

```fsharp
[<Sealed>]
type ElementBuilder<'native, 'inst> internal (tag: Tag, parts: Part<'native, 'inst> array) =
    member _.Yield(view: TemplateView<'inst>) = [| view |]
    member _.Yield(value: string) = [| TemplateView (TextNode value) |]
    member _.Yield(read: 'inst -> string) = [| TemplateView (TextSlotNode (slot TextTarget RenderMode read primitiveSame)) |]
    member _.Yield(source: Source<'inst, string>) = [| TemplateView (TextSlotNode (sourceSlot TextTarget source primitiveSame)) |]
    member _.YieldFrom(views: TemplateView<'inst> array) = views
    member _.Zero() : TemplateView<'inst> array = [||]
    member _.Combine(first: TemplateView<'inst> array, rest: TemplateView<'inst> array) = Array.append first rest
    member _.Delay(body: unit -> TemplateView<'inst> array) = body ()
    /// <summary>Repeats the body for each item while the template is built; the result is static structure.</summary>
    member _.For(items: seq<'item>, body: 'item -> TemplateView<'inst> array) = items |> Seq.collect body |> Array.ofSeq
    member _.Run(children: TemplateView<'inst> array) : TemplateView<'inst> = TemplateParts.element tag parts children
```

The benchmark row from `FrameworkPerf.fs`, written explicitly and then with sugar. Both must produce the same `skeletonLayout`.

```fsharp
// Explicit (today)
(fun t ->
    t.div
        [| t.Attrs.id (fun r -> "row-" + string r.Index); t.Attrs.className "panel"; t.Attrs.title (t.from (fun r -> r.Source)) |]
        [|
            t.input [| t.Props.Input.value (t.from (fun r -> r.Source)) |]
            t.button
                [| t.Props.Button.disabled (t.from (fun r -> r.Disabled)); t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1) |]
                [| t.text (fun r -> string r.Local.Value) |]
        |])

// Sugar
(fun t ->
    t.Div(t.Attrs.id (fun r -> "row-" + string r.Index), t.Attrs.className "panel", t.Attrs.title (t.from (fun r -> r.Source))) {
        t.Input(t.Props.Input.value (t.from (fun r -> r.Source)))
        t.Button(t.Props.Button.disabled (t.from (fun r -> r.Disabled)), t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)) {
            fun r -> string r.Local.Value
        }
    })
```

Inside a CE body, a string literal yields static text, an `'inst -> string` yields a text slot, and a `Source` yields a direct text slot. These are exactly the three `t.text` overloads. `if` and `for` in a body run while the template is built. They can depend only on definition-time values, because the instance is out of scope there.

### 4.2 Control flow with inline definitions

Each member creates its row or arm definition once, while the enclosing `build` runs, and lowers to an existing region.

```fsharp
type TemplateBuilder<'inst> with
    /// <summary>A definition whose instance is its props, created once with the enclosing template.</summary>
    member _.armOf(build: TemplateBuilder<unit -> 'payload> -> TemplateView<unit -> 'payload>) : Component<unit -> 'payload> =
        Component.define (fun _ payload -> payload) build

    /// <summary>One row per item; rows read their current item, and items with equal keys keep their row.</summary>
    member t.forEach (key: 'item -> 'key) (items: 'inst -> 'item array) (row: TemplateBuilder<unit -> 'item> -> TemplateView<unit -> 'item>) =
        t.each key items (Component.define (fun _ current -> current) row)

    /// <summary>Renders <c>content</c> while <c>condition</c> holds; a change to false runs its cleanups and removes its nodes.</summary>
    member t.show (condition: 'inst -> bool) (content: TemplateBuilder<'inst> -> TemplateView<'inst>) =
        let shown = Component.define (fun _ (current: unit -> 'inst) -> current ()) content
        t.matchWith (fun instance -> if condition instance then ValueSome instance else ValueNone) (function
            | ValueSome instance -> t.arm shown instance
            | ValueNone -> t.arm Sugar.emptyArm ())
```

`show`'s content has the **same instance type** as the enclosing template. The arm's setup returns the outer instance, which never changes, so the content reads the outer instance's signals directly. `Sugar.emptyArm` is one module-level `Component<unit -> unit>` with an empty fragment, shared by every `show`. `showElse condition content fallback` uses the same lowering with two content definitions.

The keyed-row and arm fixtures from `KeyedRowsPerf.fs`, which use four module-level definitions today, become two:

```fsharp
let private keyedList =
    Component.define (fun _ (rows: Signal<KeyedRow array>) -> rows) (fun t ->
        t.Table(t.Attrs.className "table") {
            t.Tbody() {
                t.forEach (fun (row: KeyedRow) -> row.Id) (fun rows -> rows.Value) (fun r ->
                    r.Tr() {
                        r.Td(r.Attrs.className "col-md-1") { fun row -> string (row ()).Id }
                        r.Td(r.Attrs.className "col-md-4") {
                            r.A(r.Events.onClick (fun _ _ -> clicks <- clicks + 1)) { r.from (fun row -> (row ()).Label) }
                        }
                    })
            }
        })

let private armList =
    Component.define (fun _ (rows: Signal<ArmRow array>) -> rows) (fun t ->
        let plain = t.armOf (fun a -> a.Span(a.Attrs.className "a") { "A" })
        let counted = t.armOf (fun (a: TemplateBuilder<unit -> int>) -> a.B() { fun payload -> string (payload ()) })

        t.Table() {
            t.Tbody() {
                t.forEach (fun (row: ArmRow) -> row.Id) (fun rows -> rows.Value) (fun r ->
                    r.Tr() {
                        r.Td() { fun row -> string (row ()).Id }
                        r.Td() {
                            r.matchWith (fun row -> (row ()).Mode.Value) (function
                                | Plain -> r.arm plain ()
                                | Counted count -> r.arm counted count)
                        }
                    })
            }
        })
```

`plain` and `counted` are bound in the build function, which runs once per definition, so their identity is stable. The explicit `keyedRow` sets up a snapshot instance with `item ()`. `forEach`'s row instance is instead the live reader `unit -> 'item`, so a replaced item with an equal key renders its new value. A snapshot variant is open question 4.

The other members keep their explicit form: `matchWith`, `branch`, `each` with a named definition, `boundary`, `view`, `render` and `isolated`.

### 4.3 Lowering summary

| Sugar | Lowers to | Extra work per instance |
|---|---|---|
| `t.Div(parts) { children }` | `TemplateParts.element (HtmlTag "div") parts children` | none (definition time only) |
| string, reader or `Source` yield | `TextNode`, a `TextSlotNode` with `RenderMode`, or a `TextSlotNode` with `DirectMode` | none |
| `for x in xs do` in a body | children repeated while the template is built | none |
| `t.armOf build` | `Component.define (fun _ p -> p) build` | none |
| `t.forEach key items row` | `t.each key items (Component.define (fun _ c -> c) row)` | none: same region and same row as explicit |
| `t.show cond content` | `t.matchWith` with a `ValueOption` selector and two arm definitions | the same as a hand-written `matchWith` with an empty arm: two markers, the arm memo, and one `Arm` record and one `ValueSome` per selector evaluation |

The compiled path (`TemplateDom.compile` and `materialise`) and the expanded path (`expandWith`, used for queued mounts and other representations) both receive ordinary `TemplateNode` trees. No backend changes.

### 4.4 Costs

- **Runtime per instance:** none by design. Criteria 1–3 verify it.
- **Runtime per definition, once:** an `ElementBuilder` per element, one Delay closure and one `Array.append` per child, a `TemplateBuilder` with its three sub-builders per inline definition, and the first-mount `compile` per inline definition, as for any definition. `show` adds one `Component` per use site. All of this happens at module initialisation.
- **Bundle:** one `ElementBuilder` class, three or four control-flow functions, and one small module-level function per tag actually used, since Fable emits extension members as module-level functions and Vite drops the unused ones. The estimate is a few hundred bytes gzip for a typical app; it is to be measured (criterion 4).
- **`show` against a specialised region:** `show` pays `matchWith`'s arm machinery for a boolean. A dedicated `RegionKind` would be cheaper, but it would be a runtime change and a separate decision (open question 9).

### 4.5 Fable and F# compatibility (to prove)

| Assumption | Risk | Proof |
|---|---|---|
| `t.Div(args) { ... }` parses as a CE on a method application, as Oxpecker's `div(...) { }` does | low | a compiler probe in the SageFs session |
| `[<ParamArray>]` parts resolve each overloaded `t.Attrs.*` against `Part<HTMLDivElement,'inst>`, as array literals do today | medium | the probe, plus a negative check (input-only part on `Div`) in `check-types.py` |
| Overloaded `Yield` tells apart `string`, `'inst -> string` and `Source` in implicit yields | medium: a bare lambda line needs a known `'inst` | the probe. Fallback: `t.text` inside the body. |
| Fable emits ParamArray, extension members and the builder without `curry` or `partialApply` | low | inspection of the emitted JS (criterion 4) |
| `'native` stays fixed by the tag through the builder | low | negative compile checks |
| A zero-root arm mounts in a `matchWith` region (`emptyArm`) | medium: zero-node rows are tested for `each`, not for arms | a TemplateChecks case |

## 5. Error handling

- **Compile time:** wrong-element parts, children on void tags, and a mismatched instance type in nested builders are type errors. Negative examples go in `check-types.py`.
- **Definition time:** unchanged. Invalid attribute names (`validateAttribute`), raw-text tags with children, and non-dynamic `isolated` raise while `build` runs, which is module initialisation. The stack points at the definition.
- **A definition created during an update.** This is the hazard in §2: for example, `t.armOf` called inside a `matchWith` arms function. In builds with the `DEBUG` symbol, `TemplateDom` keeps a depth counter around region item reads, and `Component.define` raises `InvalidOperationException` ("a template definition was created while a region was read; bind it in the template's build function") when the counter is nonzero. Release builds compile the check out. A test asserts that the Release JS has no counter.
- **Runtime:** unchanged. Duplicate keys, reader failures, setter failures and boundary reporting follow the existing contracts, because the lowered regions are the same regions.
- **Authored path:** no CE is added, so no new snapshot hazard. The existing docs keep the statement that setup reads are snapshots.

## 6. Testing strategy

1. **Structural equivalence (Node/jsdom, new `SugarChecks.fs` registered in `DomTests.fsproj`):** each sugar form against its explicit twin. Compares `skeletonLayout` field by field, and `renderHtml` output on .NET (RepresentationChecks) and under Fable.
2. **Behavioural equivalence:** the twins are mounted side by side and driven with the same updates. The DOM, setup counts, cleanup order and counter snapshots (owners, effects, edges, listeners, writes) must be equal. Cases:
   - `show` toggling with cleanups
   - `forEach` with reorder, removal, reinsertion, duplicate keys, and a replaced item under an equal key
   - `armOf` arms switching and updating their payload
   - nesting: `show` inside `forEach` inside a `matchWith` arm, as plan Task 10 asks ("nested selection and continuations")
   - a boundary around sugared content
   - queued commit mode, which uses the expanded path
3. **CE semantics:** document order of children with mixed `yield!`, `for` and `if`. A definition-time `for` creates static nodes only, so the effect count stays the same.
4. **Error tests:** the DEBUG definition-in-update check fires, and is absent from Release output.
5. **Compiler-negative tests:** added to `check-types.py`.
6. **Performance:**
   - add a `ranvier-template-sugar` adapter to `run.mjs --template-comparison`
   - add a sugared row to `keyed-rows.mjs` and `keyed-heap.mjs`
   - use paired, alternating runs as in the catalogue note
   - take one allocation sample of a 128-row mount
   - build the bundle with and without the sugared adapter
   Run these only when no other benchmark is using the machine.
7. **Permanent no-sugar app:** `ComponentsExample.fs`, `App.fs` and the explicit fixtures stay as they are.

## 7. Delivery order

1. A compiler probe of §4.5 in a SageFs session. No files.
2. `TemplateSugar.fs`: `ElementBuilder`, `armOf`, `forEach`, `show` and `showElse`, with structural and behavioural checks over handwritten `Div`, `Span`, `Tr` and `Td`.
3. Generator output for all 112 tags, with byte-identical regeneration and negative checks.
4. Fixture rewrites and measurements (criteria 3, 4 and 6).
5. Plugin decision (open question 6), after Task 8's startup numbers.

The plan puts sugar after Tasks 7–9. Steps 1–2 depend only on `Template.fs` as it is now. Steps 3–4 should wait until the template API is frozen in Task 8.

## 8. Open questions (recommended answers)

1. **Should the authored path get sugar in this task?** Recommended: no. Revisit a children CE for `Html.Program` only if `bench:authoring` shows construction within noise of explicit arrays and identical resources. The authored path already has the Feliz-like overloaded `Attrs` and `Props`.
2. **Element syntax: Oxpecker-style `t.Div(parts...) { children }`, a Feliz single list, or no element sugar at all (only control flow)?** Recommended: the Oxpecker style with ParamArray parts. It keeps the void/non-void split and element typing in the types, and the explicit two-array form stays.
3. **Names: `show`, `showElse`, `forEach`, `armOf`, PascalCase tags?** Recommended: these names, with new names rather than overloads of `each` and `matchWith`. Overloading `each` on a `Component` versus a lambda would make inference fragile, and the PascalCase and lowercase pair makes the sugar visible in review.
4. **Should `forEach` rows read their item live (`unit -> 'item`) or as a snapshot?** Recommended: live by default, because it is correct when an equal key arrives with a new value. Add `forEachFixed`, whose row instance is `'item`, for records of signals, as `KeyedRowsPerf` uses them. Measure both on `keyed-heap.mjs`.
5. **Should Oxpecker-style named arguments be supported (`t.Div(className = "x")`)?** Recommended: no. Each argument would need one type across the value, reader and `Source` forms, which means `op_Implicit` conversions whose F# inference and Fable behaviour are unproved. ParamArray parts give most of the brevity.
6. **Fable plugin: now, later or never?** Recommended: later, and only to precompute skeleton markup and paths, and only if Task 8 measures per-definition `compile` startup above about 1 ms for the playground's definitions. The plugin must never change semantics. The .NET server path keeps running the library code.
7. **Should the definition-in-update check run only in DEBUG?** Recommended: yes, compiled out of Release, consistent with the round-trip assertion in the templates design.
8. **Bundle budget for the sugar layer?** Recommended: 0 B when unused, and at most 1 kB gzip when used by the benchmark adapter set, with the Brotli figure reported.
9. **Should `show` get a dedicated region kind?** Recommended: not now. Lower to `matchWith` first, and specialise only if a `show`-heavy benchmark shows a measurable gap against an equivalent Solid `<Show>`.
10. **Where should the sugar live?** Recommended: `experiments/Ranvier.View.Foundation/TemplateSugar.fs` plus generated members in `Generated/HtmlTemplate.fs` (or a sibling generated file), all under an opt-in `Sugar` module. It needs internal access to the template constructors, so it stays in the Foundation assembly until package extraction.
11. **Sequencing against Tasks 7–9?** Recommended: run the compiler probe and the handwritten slice (§7 steps 1–2) once the user approves. Generation and measurement wait for the Task 8 API freeze.

## References

- Oxpecker.Solid README: CE children `ul() { li() { ... } }`, attributes as named arguments, the `[<SolidComponent>]` attribute driving `Oxpecker.Solid.FablePlugin`. https://github.com/Lanayx/Oxpecker/blob/main/src/Oxpecker.Solid/README.md
- Oxpecker.Solid.FablePlugin on NuGet. https://www.nuget.org/packages/Oxpecker.Solid.FablePlugin
- The Fable plugin API (`PluginAttribute.FableMinimumVersion`, `MemberDeclarationPluginAttribute.Transform`/`TransformCall`, `ScanForPluginsAttribute`). https://github.com/fable-compiler/Fable/blob/main/src/Fable.AST/Plugins.fs
- Feliz: one property list per element, with children under `prop.children` and the `[<ReactComponent>]` plugin. https://zaid-ajaj.github.io/Feliz/
