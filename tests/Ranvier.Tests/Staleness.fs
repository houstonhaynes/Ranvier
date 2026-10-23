module Ranvier.Tests.Staleness

open Expecto
open Ranvier

let private status (node: INode) =
    node.Status

[<Tests>]
let tests =
    testList
        "Staleness"
        [
            test "a memo reports Stale after a source write, and reading the status runs nothing" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let mutable runs = 0

                let m =
                    Make.Memo (
                        g,
                        fun _ ->
                            runs <- runs + 1
                            s.Value * 2
                    )

                m.Value |> ignore
                Expect.equal (status m &&& Status.Stale) Status.None "clean after a read"
                s.Value <- 5
                Expect.equal (status m &&& Status.Stale) Status.Stale "stale after the write"
                Expect.equal runs 1 "reading the status ran nothing"
                Expect.equal m.Peek 2 "Peek keeps the previous value"
                Expect.equal m.Value 10 "the next read recomputes"
                Expect.equal (status m &&& Status.Stale) Status.None "clean again"
                Expect.equal runs 2 "one recompute"
            }

            test "a memo never read reports Stale beside Uninitialized" {
                let g = new Graph ()
                let m = Make.Memo (g, fun _ -> 1)
                Expect.equal (status m) (Status.Uninitialized ||| Status.Stale) "never run"
            }

            test "an effect node reports Stale inside the batch that wrote its source" {
                let g = new Graph ()
                let s = Signal (g, 1)
                let mutable seen = 0
                let effect = g.Run (fun () -> Api.createEffectNode (fun () -> seen <- s.Value))
                Expect.equal (status effect &&& Status.Stale) Status.None "clean after its first run"

                let inside =
                    g.Run (fun () ->
                        Api.batch (fun () ->
                            s.Value <- 2
                            status effect &&& Status.Stale, seen))

                Expect.equal inside (Status.Stale, 1) "stale and not yet run inside the batch"
                Expect.equal (status effect &&& Status.Stale, seen) (Status.None, 2) "ran after the batch"
            }

            test "a signal never reports Stale" {
                let g = new Graph ()
                let s = Signal (g, 1)
                s.Value <- 2
                Expect.equal (status s) Status.None "a source has no freshness"
            }
        ]
