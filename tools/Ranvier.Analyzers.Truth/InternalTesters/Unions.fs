module Truth.InternalTesters

type internal Policy =
    | Queue
    | Drop of int

let internal queued (policy: Policy) =
    policy.IsQueue
