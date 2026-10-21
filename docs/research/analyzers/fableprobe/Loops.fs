module Loops

let mutable childSetups = 0
let mutable items: int list = []
type Rec = { mutable Count: int }
let state = { Count = 0 }

type Box() =
    static let mutable shared = 0

    static member Shared
        with get () = shared
        and set v = shared <- v

let setup () =
    childSetups <- childSetups + 1
    items <- 1 :: items
    state.Count <- state.Count + 1
    Box.Shared <- Box.Shared + 1
