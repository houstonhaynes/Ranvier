/// The F# samples of docs/content/view/arms.md, in page order; a sample that is an excerpt is wrapped in a function.
module Arms

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

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

let private pageArm = armOf (h1 () { fun page -> "Page " + string (page ()) })

let pager =
    Component.template (fun _ (page: Signal<int>) -> page) (
        main () { branch (fun (page: Signal<int>) -> page.Value) (fun page -> arm pageArm page) }
    )

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
