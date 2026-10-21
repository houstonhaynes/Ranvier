module Truth.Testers

type Policy =
    | Queue
    | Drop of int

let queued (policy: Policy) =
    policy.IsQueue
