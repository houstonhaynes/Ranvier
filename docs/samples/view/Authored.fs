/// The F# samples of docs/content/view/authored.md, in page order; a sample that is an excerpt is wrapped in a function.
module Authored

open global.Ranvier
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Authoring

module H = Ranvier.View.Foundation.Generated.Html
module Html = Ranvier.View.Foundation.Generated.Html.Program
module Attr = Ranvier.View.Foundation.Generated.Html.Attrs

let rowsView (rows: Signal<(int * string) array>) : Program.View =
    Html.section
        [| Attr.classStatic "rows" |]
        [|
            Html.button
                [|
                    H.Button.onClick (fun _ ->
                        let next = rows.Value.Length + 1
                        rows.Value <- Array.append rows.Value [| next, "Item " + string next |])
                |]
                [| text "Add row" |]
            keyedArray fst (fun () -> rows.Value) (fun current ->
                Html.div [| Attr.idStatic ("row-" + string (fst (current ()))) |] [| reactiveText (fun () -> snd (current ())) |])
        |]

let snapshot (rows: Signal<(int * string) array>) : string =
    Ranvier.View.Foundation.Representation.Html.render Omit (Program.run (rowsView rows))

open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

type Card = { Caption: string; Body: Program.View }

let card =
    Component.define (fun _ (card: Card) -> card) (fun t ->
        div (Attrs.className "card") {
            h3 () { fun c -> c.Caption }
            t.view (fun c -> c.Body)
        })
