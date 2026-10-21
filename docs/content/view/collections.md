---
title: Lists and keyed rows
---

A list renders one **row** per item of an array read from the instance. Rows are keyed: when the array
changes, an item whose key is still present keeps its row, its DOM nodes and its state, and moves to
its new position. New keys create rows; missing keys dispose theirs.

The samples use the opens from [Templates and bindings](templates.md).

## `forEachFixed`: a row per item

```fsharp
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
```

`forEachFixed key items row` takes the key of an item, the reader of the items, and the row view. The
row's instance is **the item the row was created for**. A later item with the same key keeps the row
and its original item; a row follows changes through the signals the item holds (`Label` and
`Done` above).

## `forEach`: a row that follows its item

```fsharp
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
```

With `forEach` the row's instance is a reader, `unit -> 'item`, that returns the current item for the
row's key. Writing a new array with `{ Player = "ada"; Points = 4 }` in place of
`{ Player = "ada"; Points = 3 }` keeps Ada's row and re-runs its bindings. Use `forEach` for
immutable items, and `forEachFixed` for items that hold their own signals.

## Keys

- Keys are compared with `=`. A key function must be pure.
- An item keeps its key while it is in the list; to change an item's key, replace the item.
- Duplicate keys fail before any row changes.
- The items reader is tracked: the list re-runs when a signal it read changes.

The analyzers report this at build time: [RAN021](../analyzers/ran021.md).

## Rows and the outer instance

A row's bindings receive the row's instance only. A row reads the enclosing instance's state through
the item itself, or through a signal or memo that the item or the definition holds. A read of the
outer instance inside a row fails to compile (FS0041 through a text yield, FS0193 through a part).

The annotations in the samples follow the [inference rule](sugar.md#the-inference-rule): the items
reader reads a `Signal`, a class, and its parameter carries the annotation; the key lambda is annotated where the
item's labels are also declared by a later record.

## Server HTML

A list renders between region markers:

```html
<ul class="todos"><!--[--><li class="done">Write</li><li class="open">Ship</li><!--]--></ul>
```

[Hydration](../server/hydration.md) adopts the server's rows in order. A client list whose item count
or row structure differs re-renders that list alone and reports the mismatch.

## The explicit form

`each key items row` takes a row definition whose props are `unit -> 'item`:

```fsharp
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
```

`forEach key items row` is `each key items` over a row definition whose setup returns the reader;
`forEachFixed` is the same with a setup that calls the reader once. A row definition passed to `each`
can be a full component with its own setup, which runs once per row.

For collection state itself (keyed projections, editable keyed sources and change readers) see
[Collections](../guide/collections.fsx).
