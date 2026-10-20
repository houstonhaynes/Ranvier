# View sugar

`Ranvier.View.Foundation.Sugar` is the short spelling of the template path: free lowercase tags, control flow over views, and component declarations that keep setup and view together. Every form lowers to one `Component.define` call and the `TemplateBuilder` operations, so the explicit form is the documented equivalent of each, and `Mounting.mount`, `Hydration.hydrate` and `Server.render` accept the result unchanged on .NET and under Fable. The design and its measurements are in the [view sugar v2 spec](../../docs/superpowers/specs/2026-10-20-view-sugar-v2-design.md).

## Opening the sugar

```fsharp
open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

let note =
    Component.template (fun _ () -> ()) (div (Attrs.className "x") { span () { "hi" } })
```

Every sample on this page assumes these five `open` lines. `open Ranvier.View.Foundation` brings in the generated members of `Attrs`, `Props` and `Events` (`HtmlTemplate`, auto-opened), and `global.Ranvier` brings in `Signal`, `Memo` and `Api`. The sugar namespace holds two auto-opened modules: `Builders` (the entry points, control flow, `render`, `renderOf`, `ComponentTag` and the `Component` declaration helpers) and `Elements` (one class per HTML tag).

**`Generated.Html` requires qualified access.** `open Ranvier.View.Foundation.Generated.Html` is a compile error (FS0892). The authored path is reached through a module alias or a qualified path, and an alias compiles beside the sugar's tags in one file:

```fsharp
module H = Ranvier.View.Foundation.Generated.Html

let authored = H.Program.div [||] [||]
let templated = Component.template (fun _ () -> ()) (div () { "template" })
```

**Scope rules.** Two rules hold in every file that opens the sugar:

- **A file uses either the sugar or `Generated.Html.Authoring`.** Both declare `Attrs` and `Props`, and the later `open` decides which one a short name reaches. With `Authoring` opened after the sugar, `div (Attrs.className "x") { }` fails with FS0041 "No overloads match for method 'div`1'". With `Authoring` opened before the sugar, the file compiles, and the sugar's `Attrs` and `Props` shadow `Authoring`'s, which become unreachable by their short names.
- **A local value named like a tag shadows the tag.** `let p = "note"` followed by `p () { "x" }` fails with FS0003. One-letter tags (`a`, `b`, `i`, `p`, `q`, `s`, `u`) meet this most often; a one-letter lambda parameter shadows the tag only inside its own body.

## Tags

Each of the 112 HTML tags is a generic class named by its raw lowercase tag name: `div`, `span`, `input`, `select`, `template`, `a`. Two names take a trailing apostrophe:

- **`base'`**, because `base` is an F# keyword. Written without the apostrophe inside a body, `head () { base () }` is a parse error reported as FS0583, FS0604 and FS0010 twice; as a binding, `let tag = base ()`, it is a single FS0010.
- **`option'`**, because a generic `option<'i>` would shadow FSharp.Core's `'T option`.

A tag takes its parts in parentheses, `div ()` or `div (Attrs.className "x", Events.onClick handler)`, and its children in braces. `Attrs`, `Props`, `Events` and `Hooks` carry every generated member: `Attrs.className`, `Attrs.data`, `Attrs.aria`, `Attrs.attribute`, `Props.Input.value`, `Props.Button.disabled`, `Events.onClick`, `Events.onInput`, `Hooks.mounted`. An element-specific member is checked against its tag: `span (Props.Input.value "x")` fails with FS0041.

**Void tags take no body.** `input`, `br`, `img`, `meta`, `base'` and the other void elements are written `input (parts)`; `input () { "x" }` fails with FS0708.

**A void tag outside a body is used through `View`.** Inside a body a void tag is yielded as it is. Every other position that takes a `TemplateView` (the content of `show` or `showElse`, an `armOf` view, a `fragment` child, a row view, a template root) takes the tag's `View`, `(img (parts)).View`. Without it, `show condition (img ())` fails with FS0193. A [component tag](#declaring-components) is used the same way.

```fsharp
type Gallery =
    { Covered: Signal<bool>; Photos: string array }

let gallery =
    Component.template
        (fun _ (photos: string array) -> { Covered = Api.createSignal true; Photos = photos })
        (div (Attrs.className "gallery") {
            show (fun g -> g.Covered.Value) (img (Attrs.title "cover")).View
            forEachFixed id (fun g -> g.Photos) (img (Attrs.title (fun (photo: string) -> photo))).View
            fragment [| (hr ()).View; (br ()).View |]
        })

let divider = Component.template (fun _ () -> ()) (hr ()).View
```

**A body yields:**

| Item | Renders |
|---|---|
| a string, `"Total"` | static text |
| a reader, `fun c -> string c.Count.Value` | a text slot, updated when the signals it reads change |
| a `Source`, `from (fun c -> c.Name)` | a text slot bound directly to the signal or memo |
| a view: a tag, `text read`, `fragment [| ... |]`, a control-flow form, `render` | the view |

`text read` is the same text slot as a yielded reader. **`for` and `if` run while the template is built:** a body's `for` repeats its children once per item, an `if` includes its children or leaves them out, and the result is static structure. A list or condition that changes at runtime uses the [control-flow forms](#control-flow).

```fsharp
type Profile =
    { Name: Signal<string>; Visits: Signal<int> }

let sections = [ "Overview"; "History" ]
let compact = false

let profile =
    Component.template
        (fun _ (name: string) -> { Name = Api.createSignal name; Visits = Api.createSignal 0 })
        (section (Attrs.className "profile") {
            h2 () { from (fun v -> v.Name) }

            p () {
                "Visits: "
                fun v -> string v.Visits.Value
            }

            input (Attrs.attribute ("type", "text"), Props.Input.value (from (fun v -> v.Name)))

            button (Attrs.attribute ("type", "button"), Events.onClick (fun v _ -> v.Visits.Value <- v.Visits.Value + 1)) {
                "Visit"
            }

            nav () {
                for title in sections do
                    a (Attrs.attribute ("href", "#" + title)) { title }
            }

            if not compact then
                p (Attrs.className "note") { "Each visit is counted once." }
        })
```

## Inference rule

> A lambda gets its instance type from its own body. A record label read resolves to the latest record in scope that declares the label. Annotate a lambda that reads a label also declared by a later record, and every lambda that reads a member of a class instance: a class component, `Signal`, `Memo`, or any other class.

| Lambda | Annotation |
|---|---|
| reads a record field whose label only that record declares (`fun r -> string r.Local.Value`) | none |
| reads a record field inside that record's own static members ([Declaring components](#declaring-components)) | none |
| reads a label a later record also declares (`fun (row: KeyedRow) -> string row.Id`, where `ArmRow` also declares `Id`) | the record type |
| reads a class member, including an item list held in a `Signal` (`fun (rows: Signal<_ array>) -> rows.Value`) | the class type |
| a non-overloaded `Events.*` entry after an annotated sibling in the same tag's parts | none |

A missing annotation is reported at the element, as FS0001, FS0193 or FS0041, rather than at the lambda.

**`t.` carries the instance type.** Inside `Component.define setup (fun t -> ...)`, `t` is the template builder of the instance type, so `t.Attrs`, `t.Events`, `t.text` and the explicit `t.div parts children` members type their lambdas top down, and they mix with free tags. A view that would need many annotations can be written with `t.`. In this sample, `Id` is also declared by the later `Marker`; `t.text` resolves it to `Entry`, where a free reader would need `fun (e: Entry) -> string e.Id`:

```fsharp
type Entry = { Id: int; Label: string }
type Marker = { Id: int; Colour: string }

let entry =
    Component.define (fun _ (e: Entry) -> e) (fun t ->
        li (t.Attrs.className "entry") {
            span () { t.text (fun e -> string e.Id) }
            span () { fun e -> e.Label }
        })
```

`t.isolated`, `t.view`, `t.Unsafe` and `t.Document` are `TemplateBuilder` members; inside `Component.define setup (fun t -> …)` they are used as parts or children of free tags.

## Control flow

Each form is a free function that takes views. It creates its row or arm definition when it is called, which is while the enclosing view is built, and lowers to the template region of the same name.

| Form | Renders |
|---|---|
| `forEach key items row` | one row per item; the row view's instance is `unit -> 'item`, a reader of its current item, and items with equal keys keep their row |
| `forEachFixed key items row` | one row per item; the row view's instance is the item the row was created for (a snapshot), and items with equal keys keep their row |
| `show condition content` | `content` while `condition` holds; a change to false runs its cleanups and removes its nodes |
| `showElse condition content fallback` | `content` while `condition` holds and `fallback` otherwise; each change of the condition remounts |
| `matchWith read arms` | the arm that `arms` selects for the union value; `arm definition payload` builds an arm from a definition made by `armOf view`. The same definition updates its payload; another definition remounts |
| `branch key arms` | the arm selected for a key; a key change remounts |
| `each key items row` | as `forEach`, with the row given as a definition, `Component<unit -> 'item>` |
| `boundary pending failed content` | `content` once every binding under it has read a value, `pending` until then, and `failed` after a failure under the content; all three are definitions |

```fsharp
type Todo = { Id: int; Title: Signal<string> }

type Status =
    | Saved
    | Saving of int

type Board =
    { Todos: Signal<Todo array>; Status: Signal<Status>; Expanded: Signal<bool> }

let private saved = armOf (span () { "Saved" })
let private saving = armOf (span () { fun pending -> "Saving " + string (pending ()) })

let board =
    Component.template
        (fun _ (todos: Todo array) ->
            { Todos = Api.createSignal todos; Status = Api.createSignal Saved; Expanded = Api.createSignal true })
        (section () {
            matchWith (fun b -> b.Status.Value) (function
                | Saved -> arm saved ()
                | Saving count -> arm saving count)

            show (fun b -> b.Expanded.Value) (
                ul () {
                    forEach (fun todo -> todo.Id) (fun b -> b.Todos.Value) (li () { fun current -> (current ()).Title.Value })
                }
            )

            ol () { forEachFixed (fun todo -> todo.Id) (fun b -> b.Todos.Value) (li () { from (fun todo -> todo.Title) }) }
        })
```

`saving`'s payload type is taken from `arm saving count`.

**Arms are bound with `let`,** at module level or before the view, as `saved` and `saving` are above. A `static member` property holding an `armOf` definition creates a new definition on every read of its getter.

**Rows reject outer-instance reads.** Every part and child inside a row unifies with the row's instance type, so a read of the enclosing instance inside a row is a compile error: FS0041 through `text`, FS0193 through a part or a pre-bound outer part.

**A definition created while a region is read** raises in a Fable Debug build: "a template definition was created while a region was read; bind it in the template's build function". Two sugar spellings reach it:

- a `static member` arm property read inside an arms function, `matchWith read (fun _ -> arm Holder.Arm ())`;
- a declaration helper called inside an arms function, `matchWith read (fun v -> arm (Component.ofMembers<ArmRecord, _> ()) v)`, and the same with `Component.ofClass`, `Component.template` or `armOf`.

In a Release build both mount, and the arm remounts on every update with a new definition. Binding the definition with `let` outside the arms function gives one definition and one mount.

## Declaring components

Four styles ship, ranked below. Each returns a plain `Component<'props>` from one `Component.define` call, so the styles mix freely: a record parent renders a class child, a class parent renders a record child, and an explicit root renders both.

**1. A record with static `Create` and `View`, `Component.ofMembers`.** Setup and view live on one type with 0 annotations: inside a record's own static members, a label read resolves to that record, including a label shared with the props record (`Caption`).

```fsharp
type CounterProps = { Start: int; Caption: string }

type CounterRecord =
    { Count: Signal<int>; Caption: string }

    static member Create(_: Setup, props: CounterProps) =
        { Count = Api.createSignal props.Start; Caption = props.Caption }

    static member View =
        div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        }

let counterRecord = Component.ofMembers<CounterRecord, _> ()
```

`Create` is the setup and runs once per instance; `View` is the template. The props type argument is written `_` and taken from `Create`. A type without `Create` or `View` fails with FS0001 "lacks the required member".

**2. `Component.template setup view`,** for small and one-off components, with 0 annotations when the instance is a record. The view argument is evaluated once, where the definition is bound.

```fsharp
let counterTemplate =
    Component.template
        (fun _ (props: CounterProps) -> { Count = Api.createSignal props.Start; Caption = props.Caption })
        (div (Attrs.className "counter") {
            span (Attrs.className "caption") { fun c -> c.Caption }

            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) {
                fun c -> string c.Count.Value
            }
        })
```

**3. A class with constructor setup and static `View`, `Component.ofClass`,** for constructor-as-setup and private state. Every instance lambda is annotated with the class type; the one exception is a non-overloaded `Events.*` entry after an annotated sibling. A missing annotation surfaces as FS0193 on the whole view when `View` is annotated, and as FS0001 at `Component.ofClass` when it is not, so annotating `View` moves the error to the right line. The style has a per-instance cost under Fable (see [Costs](#costs)).

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

let counterClass = Component.ofClass CounterClass
```

**4. Explicit `Component.define setup (fun t -> ...)`,** the documented equivalent of every style and the form with top-down typing through `t.`:

```fsharp
let counterExplicit =
    Component.define
        (fun _ (props: CounterProps) -> { Count = Api.createSignal props.Start; Caption = props.Caption })
        (fun t ->
            t.div
                [| t.Attrs.className "counter" |]
                [|
                    t.span [| t.Attrs.className "caption" |] [| t.text (fun c -> c.Caption) |]
                    t.button
                        [| t.Attrs.attribute ("type", "button"); t.Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1) |]
                        [| t.text (fun c -> string c.Count.Value) |]
                |])
```

**Only the helpers read `View`.** `View` is a property, so each read builds a new view with new definitions inside it; `Component.ofMembers` and `Component.ofClass` read it once per definition.

**Components as tags.** `render definition (fun p -> props)` inlines a child with props read from the parent instance, untracked, once per instance; `renderOf definition props` takes fixed props. In a class parent, `render`'s props lambda is annotated like any other instance lambda. One name for both forms is a class over `ComponentTag`, four lines per component:

```fsharp
type counterOf<'i> private (props: 'i -> CounterProps, primary: unit) =
    inherit ComponentTag<CounterProps, 'i>(counterRecord, props)
    new(props: 'i -> CounterProps) = counterOf<'i>(props, ())
    new(props: CounterProps) = counterOf<'i>((fun _ -> props), ())

type PanelProps = { Title: string }

type PanelRecord =
    { Title: string; Hits: Signal<int> }

    static member Create(_: Setup, props: PanelProps) =
        { Title = props.Title; Hits = Api.createSignal 0 }

    static member View =
        section (Attrs.className "panel") {
            h2 () { fun p -> p.Title }
            render counterRecord (fun p -> { Start = 1; Caption = p.Title })
            render counterClass (fun p -> { Start = 2; Caption = p.Title + "!" })
            renderOf counterTemplate { Start = 3; Caption = "fixed" }
            counterOf (fun p -> { Start = 4; Caption = p.Title + "?" })
            counterOf { Start = 5; Caption = "tag" }
        }

let panelRecord = Component.ofMembers<PanelRecord, _> ()
```

Fable 5.18.0 miscompiles a class with no primary constructor and a class with a ParamArray primary constructor; a private primary constructor with public overloads avoids both, and the generated tags use the same shape.

A component tag is a void element: inside a body it is yielded as it is, and in any other view position it is used through `View`, as a void tag is:

```fsharp
type Hint = { Visible: Signal<bool> }

let private counterArm = armOf (counterOf { Start = 6; Caption = "arm" }).View

let hint =
    Component.template (fun _ () -> { Visible = Api.createSignal true }) (
        div () {
            show (fun h -> h.Visible.Value) (counterOf { Start = 7; Caption = "hint" }).View
            matchWith (fun h -> h.Visible.Value) (fun _ -> arm counterArm ())
        }
    )

let counterRoot = Component.template (fun _ () -> ()) (counterOf { Start = 8; Caption = "root" }).View
```

## Nested Elmish

Each component runs its own `Mvu` loop (`Ranvier.Elmish`):

- **Each component's setup owns an `Mvu`.** `Create`, the class constructor or the `Component.template` setup calls `Mvu.create` or `Mvu.withCmd`; the instance holds the loop and the `Select` memos the view reads.
- **Props reader down.** The parent passes a props reader over its own selector memo. A parent message that leaves the selected value unchanged causes no child read.
- **Callback up.** The child keeps its own `Msg` type and reports through a callback in its props. The parent has one case for the report (`ChildReported of int`) and lifts no child messages.

```fsharp
open Ranvier.Elmish

type ParentMsg =
    | Bump
    | Toggle
    | ChildReported of int

type ParentModel = { Step: int; Shown: bool; Last: int }

type ChildMsg = | Add

type ChildModel = { Sum: int }

type ChildProps = { Step: Memo<int>; Report: int -> unit }

type MvuChild =
    { Loop: Mvu<ChildModel, ChildMsg>; Total: Memo<int>; StepOf: Memo<int> }

    static member Create(_: Setup, props: ChildProps) =
        let update msg model =
            match msg with
            | Add ->
                let sum = model.Sum + props.Step.Value
                { Sum = sum }, [ fun _ -> props.Report sum ]

        let loop = Mvu.withCmd ({ Sum = 0 }, []) update

        { Loop = loop; Total = loop.Select (fun m -> m.Sum); StepOf = props.Step }

    static member View =
        div (Attrs.className "child") {
            span (Attrs.className "step") { fun c -> string c.StepOf.Value }
            span (Attrs.className "total") { fun c -> string c.Total.Value }
            button (Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Loop.Dispatch Add)) { "Add" }
        }

let mvuChild = Component.ofMembers<MvuChild, _> ()

type MvuParent =
    { Loop: Mvu<ParentModel, ParentMsg>; CurrentStep: Memo<int>; Visible: Memo<bool>; LastReport: Memo<int> }

    static member Create(_: Setup, _: unit) =
        let loop =
            Mvu.create { Step = 1; Shown = true; Last = 0 } (fun msg model ->
                match msg with
                | Bump -> { model with Step = model.Step + 1 }
                | Toggle -> { model with Shown = not model.Shown }
                | ChildReported sum -> { model with Last = sum })

        { Loop = loop
          CurrentStep = loop.Select (fun m -> m.Step)
          Visible = loop.Select (fun m -> m.Shown)
          LastReport = loop.Select (fun m -> m.Last) }

    static member View =
        section (Attrs.className "parent") {
            span (Attrs.className "last") { fun p -> string p.LastReport.Value }
            button (Attrs.attribute ("type", "button"), Events.onClick (fun p _ -> p.Loop.Dispatch Bump)) { "Bump" }

            show
                (fun p -> p.Visible.Value)
                (render mvuChild (fun p -> { Step = p.CurrentStep; Report = fun sum -> p.Loop.Dispatch (ChildReported sum) }))
        }

let mvuParent = Component.ofMembers<MvuParent, _> ()
```

**Disposal.** An `Mvu` drops every message once its component is disposed, including messages sent through a `dispatch` held by a timer or subscription and through the `dispatch` passed to its commands, so a hidden child stops reporting to a live parent. Creating an `Mvu` inside a pure body, such as a `createMemo` body, throws; a component creates its loop in setup.

## Costs

**Per instance:** zero for the free tags, the control flow, `Component.template`, `Component.ofMembers`, `render`, `renderOf` and `ComponentTag`; each mounts the same owners, effects, edges, listeners and DOM writes as its explicit twin. `Component.ofClass` is the exception: under Fable it builds one 2-element array per instance setup, and every member read is a getter call. In the 128-row allocation sample its fixture modules allocate no more than the explicit row's (−4.4 B/row, inside the explicit row's 38.3 B/row spread); `Core.js` shows +29 B/row (reported as info). An under-warmed run measured the array at about 24 B/row.

**Per definition, once:** one tag-class instance per element, one builder per part and per control-flow call, all while the definition is built.

**Bundle,** gzip over the like-for-like explicit entry (each entry holds one benchmark row plus a keyed and an arm fixture):

| Entry | Δ gzip |
|---|--:|
| free tags, `Component.template` row | +840 B |
| `Component.ofMembers` row | +824 B |
| `Component.ofClass` row | +892 B |
| all three rows, against an explicit entry holding their three twins | +869 B |

Unused sugar costs 0 B. The emitted sugar modules contain 0 `curry`, `uncurry` or `partialApply`. The tables and raw files are in the [measurements note](../../docs/research/2026-10-20-view-sugar-v2.md).
