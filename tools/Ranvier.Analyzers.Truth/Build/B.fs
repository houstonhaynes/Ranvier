module B

let counted () =
    let seen = A.counter
    A.bump ()
    A.counter - seen

let suppressed () =
    // No write separates the snapshot from its use, so Fable's inlining leaves the value unchanged.
    // fsharpanalyzer: ignore-line-next RAN001
    let seen = A.counter
    A.counter - seen
