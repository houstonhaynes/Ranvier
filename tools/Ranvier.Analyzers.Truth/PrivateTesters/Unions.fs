module Truth.PrivateTesters

type private Policy =
    | Queue
    | Drop of int

let private queued (policy: Policy) =
    policy.IsQueue

let run () =
    queued Queue
