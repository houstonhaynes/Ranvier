module Fixtures.Types

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of text: string * count: int

[<Struct>]
type Point = { X: int; Y: int }

type Panel =
    {
        State: global.Ranvier.Signal<Load>
        Open: global.Ranvier.Signal<bool>
    }
