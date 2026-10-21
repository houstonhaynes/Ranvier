/// The F# samples of docs/content/view/collections.md, in page order; a sample that is an excerpt is wrapped in a function.
module Collections

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type Todo = { Id: int; Label: Signal<string>; Done: Signal<bool> }

let todoList =
    Component.template (fun _ (todos: Signal<Todo array>) -> todos) (
        ul (Attrs.className "todos") {
            forEachFixed (fun (todo: Todo) -> todo.Id) (fun (todos: Signal<_ array>) -> todos.Value) (
                li (Attrs.className (fun todo -> if todo.Done.Value then "done" else "open")) {
                    from (fun todo -> todo.Label)
                }
            )
        }
    )

type Score = { Player: string; Points: int }

let scoreboard =
    Component.template (fun _ (scores: Signal<Score array>) -> scores) (
        table () {
            tbody () {
                forEach (fun (score: Score) -> score.Player) (fun (scores: Signal<_ array>) -> scores.Value) (
                    tr () {
                        td () { fun (row: unit -> Score) -> (row ()).Player }
                        td () { fun (row: unit -> Score) -> string (row ()).Points }
                    }
                )
            }
        }
    )

let scoreRow =
    Component.define (fun _ (row: unit -> Score) -> row) (fun t ->
        t.tr
            [||]
            [|
                t.td [||] [| t.text (fun (row: unit -> Score) -> (row ()).Player) |]
                t.td [||] [| t.text (fun (row: unit -> Score) -> string (row ()).Points) |]
            |])

let scoreboardExplicit =
    Component.define (fun _ (scores: Signal<Score array>) -> scores) (fun t ->
        t.table
            [||]
            [|
                t.tbody
                    [||]
                    [| t.each (fun (score: Score) -> score.Player) (fun (scores: Signal<Score array>) -> scores.Value) scoreRow |]
            |])
