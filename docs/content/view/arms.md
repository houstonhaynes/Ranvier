---
title: Conditional arms
---

An **arm** is a component selected by a value of the instance. The region shows one arm at a time:
when the selection changes to another arm, the previous arm's cleanups run, its nodes leave, and the
new arm is set up in its place. When the selection keeps the same arm with a new payload, the arm
stays mounted and its bindings read the new payload.

The samples use the opens from [Templates and bindings](templates.md).

## `show` and `showElse`

```fsharp
type Disclosure = { Open: Signal<bool>; Body: string }

let disclosure =
    Component.template (fun _ (d: Disclosure) -> d) (
        div () {
            button (Events.onClick (fun d _ -> d.Open.Value <- not d.Open.Value)) { "Details" }
            show (fun d -> d.Open.Value) (p () { fun d -> d.Body })
        }
    )

let status =
    Component.template (fun _ (online: Signal<bool>) -> online) (
        span () {
            showElse (fun (online: Signal<bool>) -> online.Value) (b () { "online" }) (i () { "offline" })
        }
    )
```

`show condition content` renders `content` while `condition` holds. The content's instance is the
enclosing instance. A change to `false` runs the content's cleanups and removes its nodes; a change
back to `true` sets the content up again.

`showElse condition content fallback` renders one of two views. Each change of the condition remounts.

## `matchWith` over a union

```fsharp
type Load =
    | Idle
    | Loading of percent: int
    | Loaded of string

let private idleArm = armOf (span () { "Not started" })
let private loadingArm = armOf (progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun p -> Some (string (p ())))) { })
let private loadedArm = armOf (pre () { fun text -> text () })

let loader =
    Component.template (fun _ (state: Signal<Load>) -> state) (
        div (Attrs.className "loader") {
            matchWith (fun (state: Signal<Load>) -> state.Value) (function
                | Idle -> arm idleArm ()
                | Loading percent -> arm loadingArm percent
                | Loaded text -> arm loadedArm text)
        }
    )
```

- `armOf view` defines an arm whose instance is a reader of its payload, `unit -> 'payload`. Its
  payload type comes from the `arm` call that selects it.
- `arm definition payload` selects that arm with a payload.
- `matchWith read arms` reads a value from the instance and maps it to an arm. Writing `arms` as a
  `function` match lets the compiler check that every case has an arm.

Arms are compared by definition. `Loading 10` followed by `Loading 70` keeps the progress element and
updates its `value`; `Loading 70` followed by `Loaded "done"` remounts.

**Bind arm definitions with `let`**, at module level or before the template, as above. An `armOf`
written inside the `arms` function, or held in a `static member` property, creates a new definition on
every evaluation and remounts the arm each time. Fable Debug builds raise "a template definition was
created while a region was read" for a definition created inside the arms function.

## `branch` over a key

```fsharp
let private pageArm = armOf (h1 () { fun page -> "Page " + string (page ()) })

let pager =
    Component.template (fun _ (page: Signal<int>) -> page) (
        main () { branch (fun (page: Signal<int>) -> page.Value) (fun page -> arm pageArm page) }
    )
```

`branch key arms` remounts the arm whenever the key changes under `=`, even when the selected
definition stays the same. Use it when a new key must start with fresh setup state, such as a page
whose form state must not carry over to the next page.

## `Arms.Match`: a plain match (opt-in)

`Arms.Match` takes an ordinary F# `match` over a union, with pattern variables, nested patterns, guards and
or-patterns, and builds the same arms as `armOf` and `matchWith`: the same server HTML, hashes and hydration, on .NET
and under Fable. It lives in its own project; an app opts in with two lines:

```xml
<ProjectReference Include="../../experiments/Ranvier.View.Arms/Ranvier.View.Arms.fsproj" />
<Import Project="../../experiments/Ranvier.View.Arms/Ranvier.View.Arms.targets" />
```

The `.targets` adds the Fable plugin that lowers each call. The plugin assembly must be built before `dotnet fickl`
runs: `dotnet build experiments/Ranvier.View.Arms.FablePlugin -c Release`.

```fsharp
open Ranvier.View.Arms

let loaderMatch =
    Component.template (fun _ (state: Signal<Load>) -> state) (
        div (Attrs.className "loader") {
            Arms.Match (
                (fun (state: Signal<Load>) -> state.Value),
                function
                | Idle -> span () { "Not started" }
                | Loading percent -> progress (Attrs.attribute ("max", "100"), Attrs.attribute ("value", fun _ -> Some (string percent))) { }
                | Loaded "" -> span () { "Empty" }
                | Loaded text -> pre () { fun _ -> text }
            )
        }
    )
```

**A pattern variable is read inside a reader**: a lambda over the instance, such as `fun _ -> text`. Inside it, the
variable holds the payload of the arm currently mounted, so `Loaded "a"` followed by `Loaded "b"` keeps the `<pre>`
and updates its text. Event handlers (`fun s e -> ...`) and hooks over the instance are readers too, and read the
payload at event time.

A pattern variable used anywhere else is rejected, because a case body is built once and shared by every instance.
`| Loaded text -> span () { text }` fails the Fable compile at `text`, and on .NET raises `BuildTimeReadException`
when the site is built:

```
Arms.Match: 'text' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> text).
```

**Rows.** A `forEach` row reads its own item, not the instance, so a pattern variable inside a row reader is rejected
(AM002). Pass it through the items reader, which runs over the instance:

```fsharp
type Folder =
    | NoFiles
    | Files of owner: string * names: string array

let private folderView =
    Component.template (fun _ (folder: Signal<Folder>) -> folder) (
        ul () {
            Arms.Match (
                (fun (folder: Signal<Folder>) -> folder.Value),
                function
                | NoFiles -> li () { "No files" }
                | Files (owner, names) ->
                    forEach snd (fun _ -> names |> Array.map (fun name -> owner, name)) (li () { fun (row: unit -> string * string) -> fst (row ()) + "/" + snd (row ()) })
            )
        }
    )
```

**What remounts.** Each row of the match (each *target*) is one arm. The same target keeps its arm and replaces its
payload; another target remounts, including a guard, a literal pattern or an `if` at the top of a case body that
selects another branch. `Loaded "x"` to `Loaded ""` above remounts from the `<pre>` to the "Empty" `<span>`.

**Costs.** Under Fable the first site of a bundle adds the runtime, at most 400 B gzip, and each further site at most
60 B. On .NET each evaluation of the `Arms.Match` call lowers the quotation, about 1 ms per site once the process is
warm: bind sites with `let`, as arm definitions are bound above. A per-call reader runs interpreted for its first 64
calls and compiled after that. The first site of a process pays the JIT of the quotation machinery, about 130 ms; call
`Arms.Prepare ()` at start-up (on the calling thread or the thread pool) and the first site costs about 10 ms, and
publish servers with `-p:PublishReadyToRun=true` to bring it to about 7 ms. Trimmed and NativeAOT apps use
`matchWith` with `armOf`; `Arms.Match` carries `RequiresUnreferencedCode` and `RequiresDynamicCode`, so a trimmed
publish warns at each call.

### AM005

```
This construct is deprecated. Arms.Match needs the Ranvier.View.Arms Fable plugin; import Ranvier.View.Arms.targets in the project compiled by Fable.
```

A compile error (FS0101) at each `Arms.Match` call of a project that `dotnet fickl` compiles without importing
`Ranvier.View.Arms.targets`. Import the `.targets`. A project that imports it but whose plugin does not run fails when
the site is built, with a JavaScript `TypeError`.

### AM006

```
Arms.Match: no arm of this site is mounted for the instance this reader received. A lambda over the instance type inside a case body runs with the arm's instance.
```

A Release Fable build raises the short form `Ranvier AM006: shayanhabibi.github.io/Ranvier/view/arms/#am006`;
Debug builds and .NET raise the text above (`ArmsMatchException` on .NET). A lambda over the instance type inside a
case body received another value of that type, as a row whose item type equals the instance type does. Give the row
a type of its own.

The other diagnostics, AM001 to AM004 and AM007, are listed in [Troubleshooting](troubleshooting.md).

## Server HTML

An arm region's start marker carries the selected arm's structural hash:

```html
<div class="loader"><!--[a:220727aa--><span>Not started</span><!--]--></div>
```

[Hydration](../server/hydration.md) adopts the server's arm when the client selects an arm with the
same hash; otherwise it re-renders that region alone and reports the mismatch. An arm with no nodes
renders the placeholder comment `<!--ranvier:row-->`.

## The explicit form

An arm definition is a component whose props are `unit -> 'payload`; `t.arm`, `t.matchWith` and
`t.branch` take it:

```fsharp
let idleExplicit =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "Not started" |])

let loadedExplicit =
    Component.define (fun _ (text: unit -> string) -> text) (fun t -> t.pre [||] [| t.text (fun (text: unit -> string) -> text ()) |])

let loaderExplicit =
    Component.define (fun _ (state: Signal<Load>) -> state) (fun t ->
        t.div
            [| t.Attrs.className "loader" |]
            [|
                t.matchWith (fun (state: Signal<Load>) -> state.Value) (function
                    | Idle
                    | Loading _ -> t.arm idleExplicit ()
                    | Loaded text -> t.arm loadedExplicit text)
            |])
```

`armOf view` is `Component.define (fun _ payload -> payload) (fun _ -> view)`. `show` is `matchWith`
over a `ValueOption` with a shared arm of zero nodes for the hidden case, and `showElse` is `matchWith`
over a `Choice`.
