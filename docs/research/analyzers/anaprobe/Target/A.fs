module A

type Policy =
    | Queue
    | Drop of int

    member this.IsUserDefined = true

let mutable counter = 0
