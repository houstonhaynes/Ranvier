/// <summary>The board page's shared initial state and document declaration.</summary>
module KanbanPage

open Ranvier.Wire
open Ranvier.Wire.Thoth.Json.Core

let private cardCodec: Codec<Kanban.Card> =
    objectCodec {
        let! id = Codec.field "id" _.Id Codec.int
        and! title = Codec.field "title" _.Title Codec.string
        and! column = Codec.field "column" _.Column Codec.int

        return
            {
                Id = id
                Title = title
                Column = column
            }
    }

let private modelCodec: Codec<Kanban.Model> =
    objectCodec {
        let! cards = Codec.field "cards" _.Cards (Codec.array cardCodec)
        and! nextId = Codec.field "nextId" _.NextId Codec.int
        and! seed = Codec.field "seed" _.Seed Codec.int
        and! history = Codec.field "history" _.History (Codec.list (Codec.array cardCodec))

        return
            {
                Cards = cards
                NextId = nextId
                Seed = seed
                History = history
            }
    }

let private modelWire = WireCodec.ofCodec modelCodec

/// <summary>The board initialized from the model serialized in its document.</summary>
let page =
    PageLayout.declaration
        "kanban"
        (fun () -> Kanban.init)
        (PageLayout.pageView "kanban" Kanban.createBoard Kanban.view Kanban.activate)
        (Some
            {
                Id = "kanban-state"
                Encode = modelWire.Encode
                Decode = fun text -> modelWire.Decode text |> Result.mapError string
            })
