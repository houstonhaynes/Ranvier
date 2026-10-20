# View sugar design (plan Task 10)

> **Status 2026-10-20:** implemented on `feat/view-sugar`; section 9 records the measurements. The text below is the approved design and its amendments.
>
> **Approved 2026-10-19** with every recommended answer to its open questions. The sections below were written before the implementation as design sketches; the CE shapes were then proved with the F# compiler.
>
> **Amended 2026-10-20 after the compiler probe** (delivery step 1, run at `ca03e98`):
>
> - The sugar lives in `experiments/Ranvier.View.Portable`, the assembly that now holds `Template.fs` (§4.1, Q10).
> - It is `namespace Ranvier.View.Foundation.Sugar` with two `[<AutoOpen>]` modules, `Builders` (handwritten) and `Elements` (generated) (§4, §4.1, Q10).
> - Tag expressions are the `Tag` union cases where they exist (`Div`, `Button`, `Input`, `Select`, `Option`), otherwise `HtmlTag "<tag>"` (§4.1, §4.3).
> - Every `ElementBuilder` member has an explicit `TemplateView<'inst> array` return type, and text yields lower through `TemplateParts.text`, `readText` and `sourceText` (§4.1).
> - A bare lambda yield needs `t.text` (or an annotated parameter) when its body needs the instance type to type-check (§4.1, §4.2 examples, §4.5).
> - `forEachFixed`, whose row instance is the item itself, is part of the slice (§4.2, §4.3, Q4).
> - The empty arm is created on the first `show`, in a `static let mutable` field set under `lock` on .NET and by plain assignment under Fable; `show` takes it once per call, while `build` runs (§4.2).
> - The sugar's compile errors are FS0041 (wrong-element part, outer builder in a row, `Program.View` in a body), FS0001 (another element's part) and FS0708 (children on a void tag) (§4.5, §5, §6.5).
> - Criterion 2 counts owners, effects and edges with the core `RANVIER_COUNTERS` snapshot; `RANVIER_VIEW_COUNTERS` holds list and boundary counts only (§1, §6.2).
> - The definition-in-region-read check is active only under `DEBUG && FABLE_COMPILER`, wraps the item reads of `matchWith`, `branch` and `each` in `Template.fs`, and is tested by its own Debug compile (§5, §6.4).
> - Sugared definitions render through `Server.render` as their explicit twins do, on .NET and under Fable, and hydrate their twins' server HTML (§1, §4.5, §6.1, §6.2).

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
| Measured cost (Where we stand) | 383 KiB retained and 471 KiB allocated per 128-row mount | 112 KiB retained (0.97× Solid 2) and 142 KiB allocated. Mount is 1.13–1.17× Solid 2. Keyed rows retain 994 B/row at 1k (856 B/row in the build of commit `df3bb0b`, measured 2026-10-20). |

The template path is the fast path. Its main ergonomic cost is ceremony around dynamic structure. Each `each` row and each `matchWith` arm has to be a separate module-level `Component.define` with a hand-written identity setup (`KeyedRowsPerf.fs` has `keyedRow`, `plainArm`, `countedArm` and `armRow`). The two-array element syntax is also noisy compared with Oxpecker.Solid's `div(class'="x") { ... }`.

### Success criteria

1. **Same tree.** For every sugar form, `Template.skeletonLayout` of the sugared definition equals that of its explicit twin: same `Markup`, `Targets` paths, `Owners`, `RootCount` and `Children`. Server HTML from `Server.render` matches on .NET and under Fable, and a sugared definition hydrates its explicit twin's server HTML with no report.
2. **Same mounted resources.** Owners, effects, observer edges, listener registrations and native DOM writes match exactly per instance, at mount, update and disposal. Owners, effects and edges are read from the core `Counters.Snapshot ()` under `RANVIER_COUNTERS`; listener registrations by wrapping `EventTarget.prototype.addEventListener`; native DOM writes from `MutationObserver` records (attribute, child-list and character-data writes). `RANVIER_VIEW_COUNTERS` holds only the list and boundary counts.
3. **No per-instance overhead.**
   - `run.mjs --template-comparison`, with a sugared copy of the benchmark row as one more adapter, has ranges that overlap the explicit adapter's in every phase and width.
   - `keyed-rows.mjs` with a sugared row shows overlapping ranges for every operation.
   - `keyed-heap.mjs` shows the same retained bytes per row (994 B at 1k at the time of writing; 856 B at commit `df3bb0b`).
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

All sugar is opt-in: `open Ranvier.View.Foundation.Sugar`. The namespace holds two `[<AutoOpen>]` modules, `Builders` (handwritten: `ElementBuilder` and the control flow) and `Elements` (generated: one member per tag), and the `open` brings both into scope. `open Ranvier.View.Foundation` brings neither. The explicit `t.div parts children` members stay and are the documented equivalent of every sugar form.

### 4.1 Elements as CEs

For each tag, the generator (`tools/Ranvier.Dom.Generate`) emits a PascalCase extension member on `TemplateBuilder<'inst>` that takes `[<ParamArray>]` parts.

- A **normal tag** returns an `ElementBuilder<'native, 'inst>`. Its CE body is the child list.
- A **void tag** returns a `TemplateView<'inst>` directly and has no CE body. Children on a void tag are therefore a compile error.

```fsharp
type TemplateBuilder<'inst> with
    member _.Div([<ParamArray>] parts: Part<Dom.HTMLDivElement, 'inst> array) =
        ElementBuilder<Dom.HTMLDivElement, 'inst>(Div, parts)

    member _.Input([<ParamArray>] parts: Part<Dom.HTMLInputElement, 'inst> array) : TemplateView<'inst> =
        TemplateParts.element Input parts [||]
```

The tag expression is the generator's `tagExpression`, the one the lowercase member uses: a `Tag` union case where one exists (`Div`, `Button`, `Input`, `Select`, `Option`), otherwise `HtmlTag "<tag>"`. The PascalCase name is the tag with its first letter upper-cased (`Tbody`, `H1`, `Textarea`).

The handwritten builder is in `TemplateSugar.fs`, in `experiments/Ranvier.View.Portable` after `Generated/HtmlTemplate.fs`. `Template.fs`, `Generated/HtmlTemplate.fs` and `Server.fs` live in that assembly, so the .NET server and Fable run one implementation. It must sit in that assembly because the `TemplateView` constructor and `TemplateParts` are internal. The `slot` and `sourceSlot` functions are private to `Template.fs`, so the text yields lower through three internal functions added to `TemplateParts`: `text`, `readText` and `sourceText`. They build the same nodes as `TemplateBuilder.text`'s overloads, which stay unchanged.

Every member has an explicit `TemplateView<'inst> array` return type. Without it, the members that construct `TemplateView (...)` generalise over the view's instance type (`TemplateView<'a> array`).

```fsharp
[<Sealed>]
type ElementBuilder<'native, 'inst> internal (tag: Tag, parts: Part<'native, 'inst> array) =
    member _.Yield(view: TemplateView<'inst>) : TemplateView<'inst> array = [| view |]
    member _.Yield(value: string) : TemplateView<'inst> array = [| TemplateParts.text value |]
    member _.Yield(read: 'inst -> string) : TemplateView<'inst> array = [| TemplateParts.readText read |]
    member _.Yield(source: Source<'inst, string>) : TemplateView<'inst> array = [| TemplateParts.sourceText source |]
    member _.YieldFrom(views: TemplateView<'inst> array) : TemplateView<'inst> array = views
    member _.Zero() : TemplateView<'inst> array = [||]
    member _.Combine(first: TemplateView<'inst> array, rest: TemplateView<'inst> array) : TemplateView<'inst> array = Array.append first rest
    member _.Delay(body: unit -> TemplateView<'inst> array) : TemplateView<'inst> array = body ()
    /// <summary>Repeats the body for each item while the template is built; the result is static structure.</summary>
    member _.For(items: seq<'item>, body: 'item -> TemplateView<'inst> array) : TemplateView<'inst> array = items |> Seq.collect body |> Array.ofSeq
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

Inside a CE body, a string literal yields static text, an `'inst -> string` yields a text slot, and a `Source` yields a direct text slot. These are exactly the three `t.text` overloads. A bare lambda line is type-checked before the `Yield` overload is chosen, so its parameter type comes from its body alone. A lambda whose body infers the instance type by itself (the `r.Local` read above, since `Local` labels one record type) is a text slot. A lambda whose body needs the instance type, for example a read of a record label that a later record also declares, is written `t.text (fun ...)` or with an annotated parameter. A lambda that ignores its parameter (`fun _ -> "x"`) is a text slot, not static text. `if` and `for` in a body run while the template is built. They can depend only on definition-time values, because the instance is out of scope there.

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

    /// <summary>One row per item; a row's instance is the item it was created for, and items with equal keys keep their row.</summary>
    member t.forEachFixed (key: 'item -> 'key) (items: 'inst -> 'item array) (row: TemplateBuilder<'item> -> TemplateView<'item>) =
        t.each key items (Component.define (fun _ (current: unit -> 'item) -> current ()) row)

    /// <summary>Renders <c>content</c> while <c>condition</c> holds; a change to false runs its cleanups and removes its nodes.</summary>
    member t.show (condition: 'inst -> bool) (content: TemplateBuilder<'inst> -> TemplateView<'inst>) =
        let shown = Component.define (fun _ (current: unit -> 'inst) -> current ()) content
        let hidden = emptyArm ()
        t.matchWith (fun instance -> if condition instance then ValueSome instance else ValueNone) (function
            | ValueSome instance -> t.arm shown instance
            | ValueNone -> t.arm hidden ())
```

`show`'s content has the **same instance type** as the enclosing template. The arm's setup returns the outer instance, which never changes, so the content reads the outer instance's signals directly. `showElse condition content fallback` uses the same lowering with two content definitions.

The empty arm is one `Component<unit -> unit>` with an empty fragment, shared by every `show`. It is created on the first `show` call, not at module initialisation: a module-level value initialised by a call keeps `Component.define` in every bundle that imports the builders module. `show` takes it once per call, while the enclosing `build` runs, never inside the arms function. It is held in a private type:

```fsharp
[<Sealed>]
type private EmptyArm private () =
    static let mutable shared: Component<unit -> unit> = Unchecked.defaultof<_>
    static member Get() =
        if isNull (box shared) then
#if FABLE_COMPILER
            shared <- Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||])
#else
            lock typeof<EmptyArm> (fun () ->
                if isNull (box shared) then
                    shared <- Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||]))
#endif
        shared
```

On .NET, concurrent first uses of `show` on server threads will share one empty arm, so its arm key is stable across requests.

The keyed-row and arm fixtures from `KeyedRowsPerf.fs`, which use four module-level definitions today, become two:

```fsharp
let private keyedList =
    Component.define (fun _ (rows: Signal<KeyedRow array>) -> rows) (fun t ->
        t.Table(t.Attrs.className "table") {
            t.Tbody() {
                t.forEach (fun (row: KeyedRow) -> row.Id) (fun rows -> rows.Value) (fun r ->
                    r.Tr() {
                        r.Td(r.Attrs.className "col-md-1") { r.text (fun row -> string (row ()).Id) }
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
                        r.Td() { r.text (fun row -> string (row ()).Id) }
                        r.Td() {
                            r.matchWith (fun row -> (row ()).Mode.Value) (function
                                | Plain -> r.arm plain ()
                                | Counted count -> r.arm counted count)
                        }
                    })
            }
        })
```

`plain` and `counted` are bound in the build function, which runs once per definition, so their identity is stable. The explicit `keyedRow` sets up a snapshot instance with `item ()`. `forEach`'s row instance is instead the live reader `unit -> 'item`, so a replaced item with an equal key renders its new value. `forEachFixed` is the snapshot variant (open question 4): its row instance is the item the row was created for, as `keyedRow`'s is, which suits records of signals. Both `Id` cells use `r.text`, because `KeyedRow` and `ArmRow` both declare `Id` and a bare lambda would resolve `.Id` to the later record (§4.1).

The other members keep their explicit form: `matchWith`, `branch`, `each` with a named definition, `boundary`, `view`, `render` and `isolated`.

### 4.3 Lowering summary

| Sugar | Lowers to | Extra work per instance |
|---|---|---|
| `t.Div(parts) { children }` | `TemplateParts.element Div parts children` (a union case where one exists, otherwise `HtmlTag "<tag>"`) | none (definition time only) |
| string, reader or `Source` yield | `TemplateParts.text`, `readText` or `sourceText`: `TextNode`, a `TextSlotNode` with `RenderMode`, or a `TextSlotNode` with `DirectMode` | none |
| `for x in xs do` in a body | children repeated while the template is built | none |
| `t.armOf build` | `Component.define (fun _ p -> p) build` | none |
| `t.forEach key items row` | `t.each key items (Component.define (fun _ c -> c) row)` | none: same region and same row as explicit |
| `t.forEachFixed key items row` | `t.each key items (Component.define (fun _ c -> c ()) row)` | none: same region and same row as explicit |
| `t.show cond content` | `t.matchWith` with a `ValueOption` selector, the content definition and the shared empty arm | the same as a hand-written `matchWith` with an empty arm: two markers, the arm memo, and one `Arm` record and one `ValueSome` per selector evaluation |

The compiled path (`TemplateDom.compile` and `materialise`) and the expanded path (`expandWith`, used for queued mounts and other representations) both receive ordinary `TemplateNode` trees. No backend changes.

### 4.4 Costs

- **Runtime per instance:** none by design. Criteria 1–3 verify it.
- **Runtime per definition, once:** an `ElementBuilder` per element, one Delay closure and one `Array.append` per child, a `TemplateBuilder` with its three sub-builders per inline definition, and the first-mount `compile` per inline definition, as for any definition. `show` adds one `Component` per use site, and the first `show` creates the shared empty arm. All of this happens at module initialisation.
- **Bundle:** one `ElementBuilder` class, three or four control-flow functions, and one small module-level function per tag actually used, since Fable emits extension members as module-level functions and Vite drops the unused ones. The estimate is a few hundred bytes gzip for a typical app; it is to be measured (criterion 4).
- **`show` against a specialised region:** `show` pays `matchWith`'s arm machinery for a boolean. A dedicated `RegionKind` would be cheaper, but it would be a runtime change and a separate decision (open question 9).

### 4.5 Fable and F# compatibility

The probe ran on F# 10 (FSharp.Core 10.1.401), net10.0 Release and Fable 5.18.0 Release, with jsdom for the DOM and hydration cases.

| Assumption | Risk | Proof | Result (probe 2026-10-20) |
|---|---|---|---|
| `t.Div(args) { ... }` parses as a CE on a method application, as Oxpecker's `div(...) { }` does | low | a compiler probe in the SageFs session | Proved. An empty body `t.Span() { }` compiles through `Zero`. `for`, `if`, `yield!` and adjacent text keep document order, with `<!--!-->` between adjacent texts. |
| `[<ParamArray>]` parts resolve each overloaded `t.Attrs.*` against `Part<HTMLDivElement,'inst>`, as array literals do today | medium | the probe, plus a negative check (input-only part on `Div`) in `check-types.py` | Proved. An input-only part on `Div` fails with FS0041 (the explicit form reports FS0193). |
| Overloaded `Yield` tells apart `string`, `'inst -> string` and `Source` in implicit yields | medium: a bare lambda line needs a known `'inst` | the probe. Fallback: `t.text` inside the body. | Partly refuted. `string`, `Source` and `TemplateView` yields work. A bare lambda works only when its body infers the instance type by itself; otherwise it fails with FS0001, and `t.text` with the same lambda compiles (§4.1). |
| Fable emits ParamArray, extension members and the builder without `curry` or `partialApply` | low | inspection of the emitted JS (criterion 4) | Proved: 0 matches in the emitted sugar and probe modules. ParamArray becomes a rest parameter, and extension members become module-level functions. |
| `'native` stays fixed by the tag through the builder | low | negative compile checks | Proved: another element's part fails with FS0001. Children on a void tag fail with FS0708 (the explicit form reports FS0003). |
| A zero-root arm mounts in a `matchWith` region (`emptyArm`) | medium: zero-node rows are tested for `each`, not for arms | a TemplateChecks case | Proved on the .NET and Fable server render, a jsdom mount with live toggling, and hydration. A hidden `show` writes `<!--[a:<hash>--><!--ranvier:row--><!--]-->`, equal to an explicit empty arm's. |

The probe also proved that sugared definitions are ordinary `Component` values: `Mounting.mount`, `Hydration.hydrate` and `Server.render` take them unchanged. For the benchmark row, the keyed list, the arm list and `show`, explicit and sugared twins render equal server HTML on .NET and under Fable, with equal `structuralHash` values, and a sugared definition hydrates its explicit twin's server HTML under `Strict` with no report.

## 5. Error handling

- **Compile time:** wrong-element parts, children on void tags, and a mismatched instance type in nested builders are type errors. Negative examples go in `check-types.py`. The codes are:

  | Mistake | Code |
  |---|---|
  | An element-specific property written inline on another element (`t.Div(t.Props.Input.value ...)`) | FS0041 |
  | A part bound with another element's type (`Part<Dom.HTMLButtonElement, _>` passed to `t.Span`) | FS0001 |
  | Children on a void tag (`t.Input() { "x" }`) | FS0708 |
  | The outer builder's `t.text` inside a row body | FS0041 |
  | A `Program.View` in a body | FS0041 |
  | A bare lambda yield whose body resolves a label to another record | FS0001 |
- **Definition time:** unchanged. Invalid attribute names (`validateAttribute`), raw-text tags with children, and non-dynamic `isolated` raise while `build` runs, which is module initialisation. The stack points at the definition.
- **A definition created during an update.** This is the hazard in §2: for example, `t.armOf` called inside a `matchWith` arms function. In Fable builds with `DEBUG` (`#if DEBUG && FABLE_COMPILER`; Fable defines `DEBUG` under `--configuration Debug` only), `Template.fs` keeps a depth counter, `RegionReads.depth`, around the item reads of `matchWith`, `branch` and `each`. The compiled, expanded and server paths all go through those reads. `Component.define` raises `InvalidOperationException` ("a template definition was created while a region was read; bind it in the template's build function") when the counter is nonzero. The check is off on .NET: F# initialises a library file's top-level values on first access, so a module-level definition first touched inside an arms function would raise falsely. Release builds compile the check out. A test asserts that the Release JS has no counter, and the Debug test runs against its own Debug compile of the test project, since `test:dom` compiles Release.
- **Runtime:** unchanged. Duplicate keys, reader failures, setter failures and boundary reporting follow the existing contracts, because the lowered regions are the same regions.
- **Authored path:** no CE is added, so no new snapshot hazard. The existing docs keep the statement that setup reads are snapshots.

## 6. Testing strategy

1. **Structural equivalence (Node/jsdom, new `SugarChecks.fs` registered in `DomTests.fsproj`):** each sugar form against its explicit twin. Compares `skeletonLayout` field by field, `structuralHash`, and `Server.render` output on .NET (RepresentationChecks) and under Fable, from one set of fixtures in `Ranvier.View.Portable` linked into both.
2. **Behavioural equivalence:** the twins are mounted side by side and driven with the same updates. The DOM, setup counts, cleanup order and counter snapshots must be equal: owners, effects and edges from the core `Counters.Snapshot ()` under `RANVIER_COUNTERS`, listener registrations from a wrapped `addEventListener`, and native DOM writes from `MutationObserver` records. Cases:
   - `show` toggling with cleanups
   - `forEach` with reorder, removal, reinsertion, duplicate keys, and a replaced item under an equal key
   - `forEachFixed` with the same operations, against an explicit twin with snapshot rows
   - `armOf` arms switching and updating their payload
   - hydration under `Strict` of a sugared definition onto its explicit twin's server HTML, followed by an update
   - nesting: `show` inside `forEach` inside a `matchWith` arm, as plan Task 10 asks ("nested selection and continuations")
   - a boundary around sugared content
   - queued commit mode, which uses the expanded path
3. **CE semantics:** document order of children with mixed `yield!`, `for` and `if`. A definition-time `for` creates static nodes only, so the effect count stays the same.
4. **Error tests:** the DEBUG definition-in-update check fires in a Fable Debug compile, and is absent from Release output.
5. **Compiler-negative tests:** added to `check-types.py`, one per row of §5's table, each pinned to its code (FS0041, FS0001 or FS0708), plus positive cases for element bodies and for `t.text` with a shared label.
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
10. **Where should the sugar live?** Recommended: `experiments/Ranvier.View.Portable/TemplateSugar.fs` plus generated members in the sibling generated file `Generated/HtmlTemplateSugar.fs`, under the opt-in `namespace Ranvier.View.Foundation.Sugar` with `[<AutoOpen>] module Builders` and `[<AutoOpen>] module Elements`. It needs internal access to the template constructors, so it stays in the assembly that holds `Template.fs` until package extraction. (Amended 2026-10-20; the approved answer named `experiments/Ranvier.View.Foundation`, which held `Template.fs` at the time.)
11. **Sequencing against Tasks 7–9?** Recommended: run the compiler probe and the handwritten slice (§7 steps 1–2) once the user approves. Generation and measurement wait for the Task 8 API freeze.

## 9. Measurements

Measured on 2026-10-20 (Ryzen 9 9900X, Chromium 153, Solid 2.0.0-rc.14). Tables, ranges and file links are in the [research note](../../research/2026-10-20-view-sugar.md).

**Criterion 3, no per-instance overhead.** Three template-comparison runs: the sugared row's ranges overlap the explicit row's in all 9 phase-and-width rows of each run (one `update@128` row was disjoint by 0.2 us in the first attempt of run 2 and overlapped in the repeat). Keyed rows: 14 of 14 operations overlap in the rerun; 13 of 14 overlapped in the first run, and the exception, `clear10k`, had the sugared range below the explicit one. Retained heap at 10k is 839.3 to 860.0 B/row for both adapters. At 1k the explicit adapter retains 856.1 B/row in 4 of 4 processes and the sugared adapter 795.3 B/row in 3 of 4 and 856.1 in one; the whole 61 B/row difference is one fixed 60.8 KB of string bytes (about 6 B/row at 10k), the sugar never retains more, and the origin of the string is not identified. The 128-row mount of the template row allocates 1.538 MB per 10 mounts in both modes (4 processes, alternating order; 154 KB per mount), and neither that sample nor the `create1k` keyed sample contains an `ElementBuilder`, `TemplateBuilder$1_*`, `Builders_` or `Delay` function. The sugared keyed fixtures use `forEachFixed`, which expands to a snapshot row like the explicit `keyedRow`.

**Criterion 4, bundle.** The explicit benchmark entry holds no sugar code (checked on an unminified build), and the playground bundle is byte-identical to the base build. The sugared benchmark entry adds 2,807 B raw, 582 B gzip and 394 B Brotli, which is within the 1 kB gzip budget of open question 8; the delta is net of the explicit rows the sugared bundle omits. The emitted sugar modules contain 0 `curry`, `uncurry` and `partialApply` (`grep -c` over the generated `HtmlTemplateSugar.js`, `TemplateSugar.js`, `FrameworkSugarPerf.js`, `KeyedRowsSugarPerf.js` and `PlaygroundSugar.js`).

**Criterion 6, authoring gain.** The keyed-row template shrinks from 39 to 22 lines, the arm template from 31 to 22, and the playground demo from 103 to 89 lines with the setup counted on both sides (98 to 83 with it excluded on both). The benchmark fixtures go from 6 `Component.define` calls to 2; the module-level row and arm definitions are gone.

## References

- Oxpecker.Solid README: CE children `ul() { li() { ... } }`, attributes as named arguments, the `[<SolidComponent>]` attribute driving `Oxpecker.Solid.FablePlugin`. https://github.com/Lanayx/Oxpecker/blob/main/src/Oxpecker.Solid/README.md
- Oxpecker.Solid.FablePlugin on NuGet. https://www.nuget.org/packages/Oxpecker.Solid.FablePlugin
- The Fable plugin API (`PluginAttribute.FableMinimumVersion`, `MemberDeclarationPluginAttribute.Transform`/`TransformCall`, `ScanForPluginsAttribute`). https://github.com/fable-compiler/Fable/blob/main/src/Fable.AST/Plugins.fs
- Feliz: one property list per element, with children under `prop.children` and the `[<ReactComponent>]` plugin. https://zaid-ajaj.github.io/Feliz/
