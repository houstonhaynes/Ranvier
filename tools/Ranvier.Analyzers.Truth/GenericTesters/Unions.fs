module Truth.GenericTesters

type Outcome<'T> =
    | Completed of value: 'T
    | Canceled of error: exn

let finished (outcome: Outcome<int>) =
    outcome.IsCompleted
