> Probe report of 2026-10-20 at df3bb0b; the probe code was not committed. The decisions it informed are in the [view sugar v2 spec](../superpowers/specs/2026-10-20-view-sugar-v2-design.md).
# View sugar probe 3: declaring components — report

I ran this spike on 2026-10-20 in `C:\Users\shaya\RiderProjects\Ranvier-sugar-probe2` (detached at `df3bb0b`, on top of probe 2's staged free-tag prototype). Toolchain: SDK 10.0.402, F# 10, FSharp.Core 10.1.401, Fable 5.18.0 (Release and Debug), jsdom for the DOM and hydration checks. No timed or Chromium runs were needed, so neither lock was taken.

Nothing is committed. Probe 3's files are staged beside probe 2's, and probe 2's files are unchanged apart from two compile-list entries. I did not try a commit: it would have swept up probe 2's staged files, and the pre-commit hook rejects unformatted files in any case.

## Answers in one line

| Style | .NET | Fable 5.18 | Demo annotations (today: 10) | Verdict |
|---|---|---|---|---|
| Explicit `Component.define` + `t.` (today) | yes | yes | 10 written, 0 needed (probe 2) | ship; documented equivalent |
| `Component.template setup view`, free tags (probe 2) | yes | yes | **0** | ship |
| **Record + static `Create`/`View`**, `Declare.ofMembers` (SRTP) | yes | yes | **0** | **ship; rank 1** |
| Class: constructor is setup, static `View`, `Declare.ofClass` (SRTP) | yes | yes | **8** | ship; rank 3 |
| Record + `IComponent` (static abstract members), `Declare.ofType` | yes, 0 annotations | **fails**: compiler exception | 0 | drop |
| Class + `IComponent` | yes | **fails** | 8 | drop |
| Instance-member interface (`member this.View`) | rejected, FS0001 | — | — | drop (negative case, confirmed) |
| `component { setup …; view … }` CE | compiles | not compiled | same as without the CE (I15: FS0072) | drop: no gain over (1) |
| Component as a tag: `render def reader` / `renderOf def props` | yes | yes | 0 for a record parent | ship |
| Component as a tag: a per-component tag class with overloaded constructors | yes | yes, with the private-primary-constructor workaround | 0 for a record parent | optional |

- **Nested Elmish:** works for selectors, parent→child props over a parent selector, child→parent callbacks, server rendering and Strict hydration. **It is not owned as it stands:** after the child is disposed, a held `dispatch` still runs the child's `update` and commands and reaches the parent. A 15-line owned helper fixes this (tested).
- **Myriad:** possible, and Fable's project cracking does run it. It is **not worth a build-time dependency.** The best helper already costs one line per component, Myriad 1.1.0 needs two packaging workarounds before it generates anything, and it cannot remove the one real cost, the class annotations.

## Prototype

- `experiments/Ranvier.View.Portable/ComponentDecl.fs`, in `namespace Ranvier.View.Foundation.Free`, compiled after `FreeElements.fs`:
  - `IComponent<'self,'props>` with `static abstract Create: Setup * 'props -> 'self` and `static abstract View: TemplateView<'self>`, under `#nowarn "3535"`.
  - `IInstanceView<'self>`, the negative case.
  - `ComponentTag<'props,'inst>`, an abstract `VoidElement<'inst>` whose view is `render definition props`.
  - `[<AutoOpen>] ComponentDeclarations`, holding free `render` and `renderOf`, plus `[<RequireQualifiedAccess>] module Declare` with `ofClass`, `ofMembers`, `ofType` (.NET only), `ofTypeNotInline` (.NET only) and `tag`.
- `examples/Fable.Ranvier.Playground/tests/DeclFixtures.fs`:
  - the demo in all four styles;
  - the mixed parent/child tree and its explicit twin;
  - the nested MVU pair;
  - `OwnedMvu`;
  - the misuse fixtures.
- `examples/Fable.Ranvier.Playground/tests/DeclChecks.fs` with `tests/decl.test.mjs` (`DECL_DIR=generated-tests-debug` runs it against the Debug compile).
- `probe/DeclProbe`: a .NET exe for hashes and server HTML. `-p:Inference=true` adds `Inference.fs`, the compile-probe cases I1–I17.
- `DomTests.fsproj` gains the three compile entries and a `ProjectReference` to `src/Ranvier.Elmish`. `Ranvier.View.Portable.fsproj` gains `ComponentDecl.fs` and `InternalsVisibleTo DeclProbe`.

```fsharp
let inline ofClass ([<InlineIfLambda>] create: Setup * 'props -> ^T) : Component<'props> =
    Component.define (fun setup props -> create (setup, props)) (fun _ -> (^T: (static member View: TemplateView< ^T >) ()))

let inline ofMembers< ^T, 'props
    when ^T: (static member Create: Setup * 'props -> ^T) and ^T: (static member View: TemplateView< ^T >)> () =
    Component.define
        (fun setup props -> (^T: (static member Create: Setup * 'props -> ^T) (setup, props)))
        (fun _ -> (^T: (static member View: TemplateView< ^T >) ()))

let render (definition: Component<'props>) (props: 'inst -> 'props) : TemplateView<'inst> = TemplateBuilder<'inst>().render definition props
let renderOf (definition: Component<'props>) (props: 'props) : TemplateView<'inst> = TemplateBuilder<'inst>().render definition (fun _ -> props)
```

`ofClass` takes the constructor as a first-class function (`Declare.ofClass Counter`). It cannot use a constructor constraint: `^T: (new: Setup * 'props -> ^T)` fails with FS0700, "'new' constraints must take one argument of type 'unit'" (I10).

## Question 1: helpers, no code generation

### 1.1 Class as component

```fsharp
type DemoClass(setup: Setup, state: DemoState) =
    member _.State = state
    member val Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
    static member View : TemplateView<DemoClass> = section (...) { ... }   // full view in DeclFixtures.fs
let demoClass = Declare.ofClass DemoClass
```

- **Compiles on .NET and under Fable.** On .NET the `structuralHash` is `21e93577`, the same as `demoExplicit`, and the server HTML is equal. Under Fable the layout signature, hash and server HTML are equal. Mounted next to the explicit screen and driven with the same clicks and typing, synchronous and queued, it shows identical markup and reports.
- **Emitted JS:**
  - One JS class, `DemoClass`, with a constructor and `DemoClass__get_State` / `DemoClass__get_Doubled` getter functions.
  - **0** `curry`, `uncurry` or `partialApply` in `DeclFixtures.js` and `ComponentDecl.js`.
  - `DemoClass_$reflection` is emitted, as Fable emits it for every type, but nothing references it.
  - Two small costs remain:
    - every member read is a getter call, `Memo$1__get_Value(DemoClass__get_Doubled(demo_1))`, where a record reads a field, `demo_1.Doubled`;
    - the setup builds one tuple array per instance: `const tupledArg = [setup_1, props]; return DemoClass_$ctor_…(tupledArg[0], tupledArg[1])`.
- **Build runs once per definition.** The static `View` getter of the pair's children ran once each (`Builds.counterClass = 1`, `counterRecord = 1`) after two trees that hold three instances of each were mounted (`DECL static Views build once per definition`; .NET prints the same). The getter is a property, so reading `Counter.View` anywhere else rebuilds the view. Only `Declare.*` should read it.
- **The Debug check still fires.** `Declare.ofClass ArmClass`, written inside a `matchWith` arms function, raises "a template definition was created while a region was read; bind it in the template's build function" in the Fable Debug compile. In Release it mounts, with a new definition per read. `Declare.ofMembers` behaves the same way. Both helpers lower to `Component.define`, which holds the check.
- **Annotations:** 8 of the demo's 10 instance lambdas. See 1.4 for why.

### 1.1b Interface on a record or class, and the SRTP fallback

**Static abstract members work on .NET and fail on Fable 5.18.0.** `Declare.ofType<DemoRecord, _> ()` compiles on .NET with **no FS3535/FS3536 warning at the use or implementation site**; only the declaring file needs `#nowarn "3535"`. It has 0 annotations and hash `21e93577`. Fable fails with a compiler exception, for the inline helper and the non-inline one alike:

```
ComponentDecl.fs(1,1): error EXCEPTION: Unexpected static interface/override call: Ranvier.View.Foundation.Free.IComponent`2.Create (L56,49-L56,73)
   at Fable.Transforms.FSharp2Fable.Util.callAttachedMember(...)
DeclFixtures.fs(1,1): error EXCEPTION: Unexpected static interface/override call: Ranvier.View.Foundation.Free.IComponent`2.Create (L52,49-L52,73)
   at ... Fable.Transforms.FSharp2Fable.Util.inlineExpr(...)
```

**The SRTP form with the same member names works on both** (`Declare.ofMembers`). The record declares `static member Create(_: Setup, props: P)` and `static member View` with no interface. On Fable it emits a direct reference: `Component_define(DemoMembers_Create_Z6522CBC4, (_arg) => DemoMembers_get_View())`. That is one function reference, with no tuple and no wrapper lambda.

**An interface cannot sit on top as documentation either.** SRTP does not see explicit interface implementations: `Declare.ofMembers<DemoRecord, _> ()` on the `IComponent` record fails with FS0001, "lacks the required member 'Create'" (I11). Keeping the interface would mean writing every member twice.

**The instance-member interface is rejected and would cost an instance before build.**
- `Declare.ofType<I5, _> ()` on a record that implements only `IInstanceView<I5>` fails with FS0001, "The type 'I5' is not compatible with the type 'IComponent<I5,'a>'" (I5).
- To make it work, `build` would need an instance, and there is none at definition time. The options are a dummy `Unchecked.defaultof`, which reads `null` on .NET, or building the view per instance, which breaks build-once and definition identity (spec §2).

The other negative cases:
- `Declare.ofClass` on a class without `View`: FS0001, "does not support the type 'I6', because the latter lacks the required member 'get_View'".
- `ofMembers` on a record without `Create`: the same message, naming `Create` (I7).

### 1.2 Component as a tag

These forms live in the free-tag CE (`PanelRecord`, `PanelClass`, Inference I1–I4):

| Form | Reader props | Fixed props | Per-component code | Unannotated in a record parent | Unannotated in a class parent |
|---|---|---|---|---|---|
| `render counter (fun p -> {...})` / `renderOf counter {...}` | yes | yes (`renderOf`) | none | yes | no (I1: FS0041 on `Yield`) |
| tag function `let counterOf props = render counter props` | yes | no (I4: FS0001 "expected `'a -> CounterProps`") | 1 line | yes | no |
| tag class with overloaded constructors, `counterOfRecord (fun p -> …)` / `counterOfRecord { Start = 3; … }` | yes (I2) | yes (I3) | 4 lines | yes | no (annotated in `PanelClass`) |

- `t.render`, `render`, `renderOf` and both tag forms produce one tree. In the mixed tree, the explicit `t.render` root (`mixed`), the free root (`mixedFree`) and the all-explicit twin (`MixedExplicit.root`) share hash `9f5e7312` and server HTML on .NET and under Fable.
- **A free function cannot be overloaded,** so reader and fixed props under one name need a class.
- **Fable 5.18.0 miscompiles the obvious tag class.** I wrote it with no primary constructor:

  ```fsharp
  new(props) = { inherit ComponentTag<_,_>(counterRecord, props) }
  ```

  Fable emits `export class counterOfRecord$1 { constructor() {} }`, with no `extends`, and a `$ctor` that calls a nonexistent `ComponentTag$2_$ctor_Z64FF0587(...)` and then `return new counterOfRecord$1()`. Node fails with "does not provide an export named 'ComponentTag$2_$ctor_Z64FF0587'". This is the same family as probe 2's ParamArray bug, and the same fix works: a private primary constructor plus public overloads. It emits `class counterOfRecord$1 extends ComponentTag$2 { constructor(props, primary) { super(counterRecord, props); } }`.

  ```fsharp
  type counterOfRecord<'i> private (props: 'i -> CounterProps, primary: unit) =
      inherit ComponentTag<CounterProps, 'i>(counterRecord, props)
      new(props: 'i -> CounterProps) = counterOfRecord<'i>(props, ())
      new(props: CounterProps) = counterOfRecord<'i>((fun _ -> props), ())
  ```

### 1.3 A `component { … }` CE

**It does not beat (1).** I prototyped `comp { setup (fun _ (start: int) -> Box2 start); view (span () { fun b -> string b.Tally.Value }) }` in `Inference.fs` (I12–I17). It fails with FS0072 at the lambda (I15), exactly as `Component.template` does with the same body (I16). The setup's instance type reaches the `view` argument only when the argument is a non-overloaded call such as `text (fun b -> ...)` (I14), and that holds without a CE as well. Probe 2's two orders of checking (the builder before its body, and each implicit yield on its own first) happen inside the view expression, where no outer CE changes them. A CE would add a builder type and two custom operations for the same `Component.define` call.

### 1.4 Annotations, hydration, server path

The per-style lambda annotations are the coordinator's requested column. The demo has 10 instance lambdas: parity, count, doubled, increment, disabled, reset, burst, value, onInput and greeting. I count 10 `(demo: DemoInstance)` in `demoSugar`; probe 2 reported 9.

| Style | Demo | Counter child (3 lambdas) | Panel parent (3 instance lambdas) |
|---|---|---|---|
| today's `t.` sugar / explicit | 10 written (0 needed with `t.`) | 3 written in the explicit twin | — |
| record + static members (`ofMembers`) | **0** | **0** | **0** |
| record + `IComponent` (.NET only) | 0 | — | — |
| class (`ofClass`) | **8** | 3 | 3 |
| class + `IComponent` (.NET only) | 8 | — | — |

- **Records keep zero annotations.** Inside the record's own static members, a lambda's first label read (`demo.State`, `c.Count`, `p.Title`) resolves to the record being declared, because it is the latest declaration that has that label. It survives a shared label, too: `Caption` in both `CounterProps` and the parent `I8` resolves to `I8` inside `I8` (I8 compiles).
- **Why classes need annotations.** A class member resolves only once the type is known. The free tags carry no instance type (probe 2, 1.1), so `fun demo -> demo.State...` resolves `State` by record label to `PlaygroundSugar.DemoInstance` instead. The error lands on the whole view: FS0193 "`TemplateView<DemoInstance>` is not compatible with `TemplateView<DemoClass>`" when `View` is annotated, and FS0001 at `Declare.ofClass` when it is not.
- **The two class lambdas that need no annotation** are non-overloaded `Events.onClick` / `Events.onInput` that follow an annotated sibling in the same ParamArray (Reset's `onClick` after `Props.Button.disabled`, and `onInput` after `Props.Input.value`). This is probe 2's H5/G5 rule.
- **`View: TemplateView<DemoClass>`** is optional once the lambdas are annotated (I9). It only moves the error message to the right line.
- **Hydration (`Strict`, no report):**

  | Fixture | Adopted server nodes | Notes |
  |---|---|---|
  | Demo, every style | 38 of 38 | from the explicit HTML and from its own; equal markup after an increment and typing |
  | Mixed tree, `mixed` and `mixedFree` | 39 of 39 | from either HTML; equal markup after two rounds of clicking every button |
  | MVU pair | 11 of 18 | the child sits in a `show` arm, and arms mount fresh in today's hydration |

- **Server path:** unchanged. Every helper returns a plain `Component.define` result, `Server.render` is called on it unmodified, and the HTML equals the twin's on .NET (`DeclProbe` output) and under Fable.

## The user's ruling: styles mix

The mixed tree mixes the styles in both directions.
- A `t.render` root renders a record parent (`PanelRecord`) and a class parent (`PanelClass`).
- `PanelRecord` renders the **class** child `CounterClass` three ways: `render`, the tag function `counterOfClass`, and `renderOf` with fixed props.
- `PanelClass` renders the **record** child `CounterRecord` three ways: `render`, and the tag class `counterOfRecord` with a reader and with fixed props.
- A second root, `mixedFree`, renders both parents as tags.

Both roots match the all-explicit twin on hash (`9f5e7312`), server HTML (.NET and Fable), mounted markup and reports through clicks (synchronous and queued), and Strict hydration from either side's HTML. 19 of 19 `decl.test.mjs` cases pass in Release. In Debug, the two misuse cases fail as intended.

**Recommended documentation ranking:**

1. **Record + static `Create` and `View`, with `Component.ofMembers`.** Setup and view live on one type, there are 0 annotations, the JS is the smallest, and it works on .NET and Fable.
2. **`Component.template setup view` (or `Component.define setup (fun _ -> view)`)** for small or one-off components. It also has 0 annotations.
3. **Class with `Component.ofClass`**, for people who want constructor-as-setup and private state. Document the annotation rule: annotate every instance lambda with the class type.
4. **Explicit `Component.define` with `t.`**, the documented equivalent of every form.
- **Tags:** `render`/`renderOf` first. Then an optional hand-written tag function, or a tag class with the private-primary-constructor shape.
- **Dropped:** the `IComponent` interface (Fable 5.18 compiler exception), the instance-view interface (rejected; there is no instance before build), and the component CE (no gain).

## Nested Elmish loops

I tested one pair in the rank-1 style (`DeclFixtures.fs`, section "Nested MVU loops"). The parent's setup runs `Mvu.create`. The child's setup runs `Mvu.withCmd`, whose initial command keeps its `dispatch` in `Loops.held`, as a timer or subscription would. The views read `Select` memos and the events call `Dispatch`. The results are from `DECL nested MVU loops`, Release, synchronous:

| Check | Result |
|---|---|
| The child's setup runs once | 1 |
| Parent `Rename` (selected step unchanged) | child step-slot reads **+0**, setups still 1 |
| Parent `Bump` (step 1→2) via props `{ Step = p.CurrentStep; … }`, a parent selector memo | child shows `2`, step-slot reads +1 |
| Child `Add`: the command calls `props.Report sum` = `p.Loop.Dispatch (ChildReported sum)` | parent shows `2`. The child's `Msg` stays its own: the parent has one `ChildReported of int` case and does no lifting |
| Hide the child (`show` arm switch) | child DOM removed; child selector runs after that **+0** |
| **Held child `dispatch Tick`, then `Add`, after disposal** | **update and commands still run**: the parent's `.last` goes from `2` to **`204`** |
| Parent loop after the child's disposal | `Rename "c"` renders |
| Show again | a new child setup (2) with a fresh model (`0`) |
| Server render, Strict hydration | no report. After hydration `Add` gives `1`/`1`; `Bump` then `Add` gives `2`/`3`/`3` |

- **Ownership.** The `Select` memos are owned by the child's scope and stop on disposal. The **loop is not owned:**
  - `Mvu`'s model is a `Signal` on the graph, not on the owner;
  - its commands receive the raw `Dispatch`;
  - a held dispatch therefore keeps updating a dead model and keeps running commands, including outbound callbacks into a live parent.

  This is a property of `Mvu` itself, not of any declaration style.
- **The fix, proved** (`DECL an owned Mvu drops a disposed child's held dispatch`). `OwnedMvu.withCmd setup init update` sets an `alive` flag through `setup.OnCleanup`, gates every command's `dispatch` and returns a gated `dispatch`. After hiding, the held `Add` reaches nothing (`reports` +0).
- **State transfer.** It is not needed for a deterministic `init`: the server and the client both run setup with the same model. It would be needed if the server's model differed from `init` at render time. `Server.render` runs setup, and therefore `withCmd`'s initial commands, synchronously, and an asynchronous command's result is not awaited. I did not try that case.
- **Referencing `Ranvier.Elmish` under Fable as it stands** works. I added a `ProjectReference` to `src/Ranvier.Elmish/Ranvier.Elmish.fsproj` (multi-targeted `net10.0;netstandard2.1`) to `DomTests.fsproj`. Fable cracked it and compiled `Mvu.fs` from source (`createMemo` under `FABLE_COMPILER`), and `Mvu.create`/`withCmd` found `Graph.Current` inside setup. I did not add the reference to `Ranvier.View.Portable.fsproj` itself; the playground tests link Portable's sources.
- **An `Mvu` helper in each style** has the same shape everywhere: the owned constructor above, called from setup.
  - **Record:** `static member Create(setup, props) = let loop, dispatch = OwnedMvu.withCmd setup (init props) (update props) in { Dispatch = dispatch; Total = loop.Select _.Sum }` (`OwnedChild` in the fixtures).
  - **Class:** `type Child(setup, props) = let loop, dispatch = OwnedMvu.withCmd setup (init props) (update props)`, followed by `member _.Dispatch = dispatch` and `member val Total = loop.Select _.Sum`.
  - **`Component.template`:** the same call in the setup lambda.

  A dedicated `Component.mvu init update view` would have to fix an instance shape (`{ Model selectors; Dispatch }`). Selectors are per-view choices, so I would ship only `OwnedMvu`-style constructors in `Ranvier.Elmish` (or a View-side bridge) and no Mvu-specific declaration style.

## Question 2: Myriad

### 2.1 Toolchain, tried in `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\myriad-probe`

- **Version.** The current release is **Myriad.Sdk / Myriad.Plugins / Myriad 1.1.0**, published on nuget.org on 2026-09-24. Myriad.Core 1.1.0 depends on **Fantomas.Core 7.0.5 → Fantomas.FCS 7.0.5**, the parser Myriad uses. The tool ships `tools/net8.0` and `tools/net9.0`, so it needs an installed .NET 8 or 9 runtime; this machine has both. Myriad.Plugins pins `FSharp.Core (= 9.0.101)`, which gives NU1608 against 10.1.402.
- **Parsing.** With `LangVersion` `latest` on SDK 10.0.402, the parser read an input file that uses F# 8's `_.Name`. I did not test syntax that is new in F# 10.
- **Two packaging defects in 1.1.0. Each one fails quietly or late:**
  1. `Myriad.Sdk.props` declares `<PackageDownload Include="Myriad" …>`, but NuGet restores with `ExcludeRestorePackageImports=true`, so the tool package is never downloaded. The build fails with `MSB3954: Failed to compute hash for file '…\myriad\1.1.0\tools\net8.0\any\Myriad.dll'`. Workaround: add the `PackageDownload` to the project.
  2. The SDK defaults to the **net8.0** tool, while `Myriad.Plugins.props` registers the **net9.0** plugin. The net8.0 tool then writes a header-only `Generated.fs` (FS0222 downstream). Run by hand with the net8.0 plugin, it generates correctly. Workaround: `<MyriadSdk_Generator_Exe>$(NuGetPackageRoot)myriad/1.1.0/tools/net9.0/any/Myriad.dll</MyriadSdk_Generator_Exe>`.
- **Behaviour once both are worked around:**
  - The .NET build generates the `Fields` plugin output and runs (`Ada`).
  - **`dotnet fable` from a clean state** (no `obj`, no `Generated.fs`) generated the file during project cracking and compiled it: `Generated.js` imports `Input.js`, and `node out/Program.js` prints `Ada`. Fable 5.18's cracker runs targets up to `CoreCompile`, and Myriad hooks `BeforeTargets="CoreCompile"`, so the file does not have to exist before `fable` runs.
  - Two gaps remain:
    - Deleting `Generated.fs` with unchanged inputs does not regenerate it on the next .NET build, because the stamp is up to date.
    - `fable watch` re-cracks only when the project changes, so editing the input file would not regenerate. I derived this and did not run it, since background commands were out of scope.

### 2.2 Reach

Myriad receives the untyped AST of its input file and writes a separate output file. Its inline mode appends to the input; it cannot edit existing code.

- **It could generate:**
  - `let counter = Component.ofMembers<Counter, CounterProps> ()` or `Component.define Counter.Create (fun _ -> Counter.View)`, from a `[<Component>]` record. The props type comes from `Create`'s parameter annotation.
  - The 4-line tag class per component, with Fable's private-primary-constructor shape, which gives `counter { Start = 3 }` and `counter (fun p -> …)` under one lowercase name.
  - Props builders (`create`/`map`, as the `Fields` plugin does).
- **It cannot:**
  - Remove the class annotations. They are a type-checking cost, and fixing them means rewriting lambda bodies in the user's input, which Myriad's output model cannot do and which also needs types to know which lambdas take the instance.
  - Rewrite instance captures (rejected by spec §2 as ownership inference).
  - Do anything that needs the instance type, such as validating that `View` reads only the declared instance.

### 2.3 Against option C (Fable plugin)

Myriad's output is plain F#, which removes C's main cost: the .NET server path and Fable compile the same code. What remains of C's costs:
- **Version coupling:** to the Fantomas.FCS parser instead of the Fable AST. New F# syntax in an input file may fail to parse until Myriad updates Fantomas.
- **Golden-output tests** for the generator.
- **A build step every user inherits:**
  - a net8/9 runtime requirement;
  - two packaging workarounds in 1.1.0;
  - an FSharp.Core pin warning;
  - an extra compile item per input file;
  - stale output in watch mode;
  - editor errors until the first build writes the file.

C's other costs go away under Myriad: it rewrites nothing in bodies, and it does no precomputation.

### 2.4 Verdict

**No.** After Question 1, the best helper costs one line per component (`let counter = Component.ofMembers<Counter, _> ()`) and nothing per use (`render counter …`/`renderOf counter …`). Myriad would add the lowercase overloaded tag name. That is 4 hand-written lines per component, and only where an author wants it. That gain does not justify a build-time dependency with today's packaging defects for every user. Revisit only if tags-by-name become the documented default and the Myriad packaging is fixed upstream.

## The recommended form, in full

### The demo screen: record + static members (`DeclFixtures.demoMembers`)

0 annotations; hash `21e93577` equal to `demoExplicit`; server HTML, mounted traces and Strict hydration equal.

```fsharp
type DemoMembers =
    { State: DemoState; Doubled: Memo<int> }

    static member Create(_: Setup, state: DemoState) =
        {
            State = state
            Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
        }

    static member View =
        section (
            Attrs.className "demo-grid",
            Attrs.data ("testid", "demo-root"),
            Attrs.data ("parity", (fun demo -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
        ) {
            article (Attrs.className "panel counter-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "01 / Counter" }
                    span (Attrs.className "tag") { "signal → text" }
                }

                output (Attrs.className "count", Attrs.data ("testid", "count"), Attrs.aria ("live", "polite")) {
                    fun demo -> string demo.State.Count.Value
                }

                div (Attrs.className "derived") {
                    span () { "Doubled" }
                    output (Attrs.data ("testid", "doubled")) { fun demo -> string demo.Doubled.Value }
                }

                div (Attrs.className "actions") {
                    button (
                        Attrs.className "primary",
                        Attrs.data ("testid", "increment"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "reset"),
                        Attrs.attribute ("type", "button"),
                        Props.Button.disabled (fun demo -> demo.State.Count.Value = 0),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "burst"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ ->
                            Api.batch (fun () ->
                                for _ in 1..100 do
                                    demo.State.Count.Value <- demo.State.Count.Value + 1))
                    ) {
                        "Burst +100"
                    }
                }

                p (Attrs.className "note") { "The output changes. The DOM nodes stay." }
            }

            article (Attrs.className "panel name-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "02 / Input" }
                    span (Attrs.className "tag") { "event → signal" }
                }

                label (Attrs.attribute ("for", "name-field")) { "Your name" }

                input (
                    Attrs.id "name-field",
                    Attrs.attribute ("type", "text"),
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.data ("testid", "name"),
                    Props.Input.value (fun demo -> demo.State.Name.Value),
                    Events.onInput (fun demo event -> demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                p (Attrs.className "greeting", Attrs.data ("testid", "greeting"), Attrs.aria ("live", "polite")) {
                    fun demo -> $"Hello, {demo.State.Name.Value}"
                }

                p (Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        }

let demoMembers = Declare.ofMembers<DemoMembers, _> ()
```

### The parent/child pair, recommended form (record child, record parent; both tag forms)

```fsharp
type CounterProps = { Start: int; Caption: string }

type CounterRecord =
    { Count: Signal<int>; Caption: string }

    static member Create(_: Setup, props: CounterProps) =
        {
            Count = Api.createSignal props.Start
            Caption = props.Caption
        }

    static member View =
        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counterRecord = Declare.ofMembers<CounterRecord, _> ()

type PanelProps = { Title: string }

type PanelRecord =
    { Title: string; Hits: Signal<int> }

    static member Create(_: Setup, props: PanelProps) =
        {
            Title = props.Title
            Hits = Api.createSignal 0
        }

    static member View =
        section (Attrs.className "panel") {
            h2 () { fun p -> p.Title }
            render counterRecord (fun p -> { Start = 1; Caption = p.Title })
            render counterRecord (fun p -> { Start = 2; Caption = p.Title + "!" })
            renderOf counterRecord { Start = 3; Caption = "fixed" }
        }

let panelRecord = Declare.ofMembers<PanelRecord, _> ()
```

The fixture itself (`DeclFixtures.fs`) crosses the styles: `PanelRecord` renders `counterClass` (the class child), and `PanelClass` renders `counterRecord` through the tag class. The record-to-record pair above is the recommended spelling of the same tree, with the same structure; I did not compile it separately. For reference, the class child in full:

```fsharp
type CounterClass(setup: Setup, props: CounterProps) =
    member val Count = Api.createSignal props.Start
    member _.Caption = props.Caption

    static member View: TemplateView<CounterClass> =
        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun (c: CounterClass) -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun (c: CounterClass) _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun (c: CounterClass) -> string c.Count.Value
            }
        }

let counterClass = Declare.ofClass CounterClass
```

## Recommendation

1. **Ship `Component.ofMembers` and `Component.ofClass`** (named `Declare.*` in the probe), each lowering to `Component.define`, plus free `render` and `renderOf`. Rank them for the docs as above.
2. **Do not ship `IComponent`** while Fable 5.18 rejects static abstract calls. Re-test on a later Fable; if it lands, `ofType` is a one-line addition with the same member names, so records written for `ofMembers` would only need the `interface … with` lines.
3. **Ship an owned `Mvu` constructor** (`OwnedMvu.withCmd`, or a `Setup`-taking overload in `Ranvier.Elmish`) before documenting nested loops. Without it, a disposed component's loop keeps running.
4. **No Myriad and no component CE.**

The cost if this is wrong:
- **Ranking classes third when users prefer them:** they meet 8 annotations per demo-sized screen, as FS0193 on the whole view when they forget one.
- **Skipping Myriad when tags-by-name matter:** authors hand-write 4 lines per tag. That is reversible, because generated code would lower to the same helpers.

## DRAFT spec amendment (view-sugar spec, `docs/superpowers/specs/2026-10-19-view-sugar-design.md`)

> **DRAFT, not approved.** Amended after the component-declaration probe (2026-10-20, report `2026-10-20-component-decl-probe-report.md`). It assumes the free-tag amendment from probe 2.

- **Header amendments list.** Add: "Components may be declared as a record with static `Create`/`View` (`Component.ofMembers`), a class whose constructor is setup (`Component.ofClass`), `Component.template`, or explicitly. All lower to `Component.define` (§4.6). Interfaces with static abstract members, a component CE and Myriad generation were measured and rejected (§4.6, §8 Q13–Q14)."
- **§3 C.** Add a paragraph on Myriad as a C variant:
  - Its output is plain F#, which removes the server/browser divergence.
  - The remaining costs are the parser coupling to Fantomas.FCS, a build step every user inherits (net8/9 runtime, two 1.1.0 packaging workarounds, stale output under `fable watch`), and no reach into instance typing.
  - Rejected for now.
- **New §4.6 "Declaring components".**
  - `Component.ofMembers<'T, _> ()` resolves `static member Create: Setup * 'props -> 'T` and `static member View: TemplateView<'T>` statically. Fable emits a direct function reference.
  - `Component.ofClass Ctor` takes the constructor as a function. A constructor constraint with arguments is FS0700. Fable allocates one 2-element array per instance setup, and member reads are getter calls.
  - `View` is a property getter read once by the helper. Reading it elsewhere rebuilds the view.
  - Inference rule:

    > Inside a record's own static members, a lambda's label read resolves to that record, so record components need no annotations. A class component needs `(x: T)` on every instance lambda, except a non-overloaded event entry after an annotated sibling.

  - Tags: free `render definition reader` and `renderOf definition props`. A per-component tag class needs a private primary constructor with public overloads, because Fable 5.18.0 drops the base call of `new(..) = { inherit Base(..) }`.
  - The Debug definition-in-read check covers the helpers, because they call `Component.define`.
- **§4.5 rows.**
  - "Static abstract interface members under Fable 5.18": refuted, compiler exception "Unexpected static interface/override call".
  - "SRTP sees explicit interface implementations": refuted, FS0001.
  - "A component CE flows the instance type to the view": refuted, FS0072.
  - "A class without a primary constructor inheriting a base, under Fable 5.18": miscompiled; workaround above.
- **§6.** Add `decl.test.mjs`: every style against the explicit twin (layout, hash, server HTML, mounted traces and Strict hydration), the mixed tree, build-once, and the Debug misuse cases.
- **§8 Q13.** "Which component declarations ship?" Recommended: explicit, `Component.template`, `ofMembers`, `ofClass`; ranked `ofMembers`, `template`, `ofClass`, explicit.
- **§8 Q14.** "Code generation for components?" Recommended: no (Myriad verdict).
- **Elmish note (Ranvier.Elmish, not this spec).** `Mvu` created in a component's setup must be owned by the component. Selectors already are; the loop is not. Add a `Setup`-aware constructor that gates dispatch and command dispatch on `OnCleanup`.

## Not tried

- **Bundle size** of the helpers and a tag class (no `free-size.mjs` run). The emitted JS was only inspected.
- **Moving the helpers** into probe 2's `Free.Component` module (`Component.ofMembers`). I kept a separate `Declare` module to leave probe 2's staged files untouched, so whether two `Component` modules merge cleanly is unchecked.
- **`Declare.ofMembers<T>` with one type argument.** It needs `_` for the props today.
- **The record-to-record pair** as written above. The compiled fixture crosses record and class instead.
- **A removed keyed row** as the disposal case for the MVU child. Only an arm switch (`show`) was tested.
- **MVU state transfer** with an asynchronous initial command on the server.
- **`fable watch` regeneration with Myriad**, F# 10-only syntax in a Myriad input file, and a machine without the .NET 8 or 9 runtime.
- **IWSAM on a newer Fable.**
- **Timings.** Runtime cost is the `Component.define` path by construction. Only trees, traces and hydration were compared.
- **Formatting** the probe files with fantomas.
