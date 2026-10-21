module Ranvier.Analyzers.Tests.Ran012

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source =
        "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\n"
        + body

    run Analyzers.ran012Cli (context (ranvier.Force ()) source)

let private codes body =
    findings body |> List.map _.Code

let private asyncMemo (inner: string) =
    "let load (userId: Signal<int>) (previous: Task<int>) =\n    createAsync (fun _ _ ->\n        task {\n"
    + inner
    + "        })\n"

[<Tests>]
let tests =
    testList
        "RAN012"
        [
            test "a read after do! Task.Yield, with its exact message" {
                Expect.equal
                    (findings (asyncMemo "            do! Task.Yield ()\n            return userId.Value\n")
                     |> List.map _.Message)
                    [
                        "`userId.Value` is read after the first `let!`/`do!` of this async memo; a read after an await that suspends is untracked, and a change to `userId` starts no flight. Read it before the first await and bind it to a local."
                    ]
                    "one message"
            }

            test "a read after let!" {
                Expect.equal (codes (asyncMemo "            let! _ = previous\n            return userId.Value\n")) [ "RAN012" ] "let!"
            }

            test "a read in an and! continuation" {
                Expect.equal
                    (codes (asyncMemo "            let! a = previous\n            and! b = previous\n            return a + b + userId.Value\n"))
                    [ "RAN012" ]
                    "and!"
            }

            test "a read after the await of createAsyncWithPolicy" {
                let body =
                    "let load (userId: Signal<int>) =\n    createAsyncWithPolicy FlightPolicy.KeepLatest (fun _ _ ->\n        task {\n            do! Task.Yield ()\n            return userId.Value\n        })\n"

                Expect.equal (codes body) [ "RAN012" ] "policy form"
            }

            test "a read before the first await" {
                Expect.isEmpty
                    (codes (asyncMemo "            let id = userId.Value\n            do! Task.Yield ()\n            return id\n"))
                    "tracked"
            }

            test "Peek after the await" {
                Expect.isEmpty (codes (asyncMemo "            do! Task.Yield ()\n            return userId.Peek\n")) "untracked by design"
            }

            test "an await outside an async memo" {
                Expect.isEmpty
                    (codes "let t (s: Signal<int>) =\n    task {\n        do! Task.Yield ()\n        return s.Value\n    }\n")
                    "no async memo"
            }

            test "a read in the body of an effect created after the await belongs to the effect" {
                Expect.isEmpty
                    (codes (
                        asyncMemo
                            "            do! Task.Yield ()
            createEffect (fun () -> userId.Value |> ignore)
            return 1
"
                    ))
                    "the effect tracks its own reads"
            }

            test "a read under untrack after the await" {
                Expect.isEmpty
                    (codes (
                        asyncMemo
                            "            do! Task.Yield ()
            return untrack (fun () -> userId.Value)
"
                    ))
                    "untracked by intent"
            }

            test "a read in createRoot's body after the await" {
                Expect.equal
                    (codes (
                        asyncMemo
                            "            do! Task.Yield ()
            return createRoot (fun _ -> userId.Value)
"
                    ))
                    [ "RAN012" ]
                    "a root tracks nothing"
            }

            test "a read in createEffectOn's act after the await" {
                Expect.equal
                    (codes (
                        asyncMemo
                            "            do! Task.Yield ()
            createEffectOn (fun () -> 1) (fun _ -> userId.Value |> ignore)
            return 1
"
                    ))
                    [ "RAN012" ]
                    "act is untracked"
            }

            test "a read in an onCleanup callback after the await" {
                Expect.equal
                    (codes (
                        asyncMemo
                            "            do! Task.Yield ()
            onCleanup (fun () -> userId.Value |> ignore)
            return 1
"
                    ))
                    [ "RAN012" ]
                    "a cleanup tracks nothing"
            }

            test "a read after an await inside if" {
                let body =
                    "let load (userId: Signal<int>) (flag: bool) =
    createAsync (fun _ _ ->
        task {
            if flag then
                do! Task.Yield ()

            return userId.Value
        })
"

                Expect.equal (codes body) [ "RAN012" ] "the branch awaits"
            }

            test "a read after an await inside for" {
                let body =
                    "let load (userId: Signal<int>) =
    createAsync (fun _ _ ->
        task {
            for _ in 1..2 do
                do! Task.Yield ()

            return userId.Value
        })
"

                Expect.equal (codes body) [ "RAN012" ] "the loop awaits"
            }

            test "a read after a branch without an await" {
                let body =
                    "let load (userId: Signal<int>) (flag: bool) (log: ResizeArray<int>) =
    createAsync (fun _ _ ->
        task {
            if flag then
                log.Add 1

            return userId.Value
        })
"

                Expect.isEmpty (codes body) "no await before the read"
            }

            test "an unawaited inner task in a branch is no await" {
                Expect.isEmpty
                    (codes (
                        asyncMemo
                            "            if userId.Peek > 0 then\n                (task { do! Task.Yield () }) |> ignore\n            return userId.Value\n"
                    ))
                    "the inner task is not awaited"
            }

            test "a read before the await in a loop body runs after the previous iteration's await" {
                Expect.equal
                    (codes (
                        asyncMemo
                            "            for _ in 1..3 do\n                let n = userId.Value\n                do! Task.Delay n\n            return 0\n"
                    ))
                    [ "RAN012" ]
                    "loop body"
            }

            test "a read in a loop body without an await" {
                Expect.isEmpty
                    (codes (
                        asyncMemo
                            "            let mutable total = 0\n            for _ in 1..3 do\n                total <- total + userId.Value\n            do! Task.Yield ()\n            return total\n"
                    ))
                    "before the first await"
            }

            test "the finding's range is the read" {
                let range =
                    (findings (asyncMemo "            do! Task.Yield ()\n            return userId.Value\n")).Head.Range

                Expect.equal (range.StartLine, range.StartColumn, range.EndColumn) (8, 19, 31) "userId.Value"
            }

            test "a user Value property after an await is not a reactive read" {
                let body =
                    "type Box() =\n    member _.Value = 1\nlet load (box: Box) =\n    createAsync (fun _ _ ->\n        task {\n            do! Task.Yield ()\n            return box.Value\n        })\n"

                Expect.isEmpty (codes body) "user type"
            }
        ]
