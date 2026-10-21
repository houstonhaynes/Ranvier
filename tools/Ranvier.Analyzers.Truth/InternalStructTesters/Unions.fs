module Truth.InternalStructTesters

[<Struct>]
type internal Outcome =
    | Completed of value: int
    | Canceled of error: exn

let internal finished (outcome: Outcome) =
    outcome.IsCompleted
