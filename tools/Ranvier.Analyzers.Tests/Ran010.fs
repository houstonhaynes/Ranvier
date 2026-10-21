module Ranvier.Analyzers.Tests.Ran010

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private findings (body: string) =
    let source =
        "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\n"
        + body

    run Analyzers.ran010Cli (context (ranvier.Force ()) source)

let private codes body =
    findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN010"
        [
            test "an effect in createMemo's body, with its exact message" {
                Expect.equal
                    (findings "let m () = createMemo (fun _ -> createEffect ignore; 1)\n"
                     |> List.map _.Message)
                    [
                        "`createEffect` creates an owned node inside `createMemo`'s body, which fails every read of the memo. Use `createMemoWith` for a memo that owns the nodes its body creates, or create the node outside the memo."
                    ]
                    "one message"
            }

            test "a memo in createAsync's compute" {
                Expect.equal
                    (codes "let a () = createAsync (fun _ _ -> createMemo (fun _ -> 1) |> ignore; Task.FromResult 1)\n")
                    [ "RAN010" ]
                    "createAsync"
            }

            test "a creator in createEffectOn's compute" {
                Expect.equal (codes "let e () = createEffectOn (fun () -> onCleanup ignore; 1) ignore\n") [ "RAN010" ] "createEffectOn"
            }

            test "a creator in a projection's map" {
                Expect.equal
                    (codes "let p () = createProjection id (fun x -> createSignal 1 |> ignore; createEffect ignore; x) (fun () -> [ 1 ])\n")
                    [ "RAN010" ]
                    "createProjection map"
            }

            test "a creator in a projection row's reader" {
                Expect.equal
                    (codes "let p () = createProjectionWith id (fun item -> fun () -> createEffect ignore; item ()) (fun () -> [ 1 ])\n")
                    [ "RAN010" ]
                    "the reader"
            }

            test "a creator in a lookup's f and affected" {
                Expect.equal
                    (codes "let l () = createLookup (fun s k -> onCleanup ignore; s + k) (fun _ _ -> onCleanup ignore; Seq.empty) (fun () -> 1)\n")
                    [ "RAN010"; "RAN010" ]
                    "f and affected"
            }

            test "a creator in an editable's seed" {
                Expect.equal (codes "let e () = createEditable (fun _ -> onCleanup ignore; 1)\n") [ "RAN010" ] "seed"
            }

            test "a creator in a projection aggregate's folder" {
                Expect.equal
                    (codes "let f (p: Projection<int, int>) = p |> Projection.fold (fun s v -> onCleanup ignore; s + v) 0\n")
                    [ "RAN010" ]
                    "fold"
            }

            test "a creator under untrack" {
                Expect.equal
                    (codes "let m () = createMemo (fun _ -> untrack (fun () -> createEffect ignore); 1)\n")
                    [ "RAN010" ]
                    "untrack is the same run"
            }

            test "a creator inside try ... with" {
                Expect.equal (codes "let m () = createMemo (fun _ -> try createEffect ignore; 1 with _ -> 0)\n") [ "RAN010" ] "caught creator"
            }

            test "the Memo constructor without the owning argument" {
                Expect.equal
                    (codes "let m (g: Graph) = Memo (g, System.Func<int voption, int>(fun _ -> createEffect ignore; 1))\n")
                    [ "RAN010" ]
                    "Memo (graph, compute)"
            }

            test "a pipelined createMemo body is a pure context" {
                Expect.equal (codes "let m () = (fun _ -> createEffect ignore; 1) |> createMemo\n") [ "RAN010" ] "pipe"
            }

            test "a stored callback inside a projection factory" {
                Expect.isEmpty
                    (codes
                        "let p (handlers: ResizeArray<unit -> unit>) = createProjectionWith id (fun item -> handlers.Add (fun () -> createEffect ignore); fun () -> item ()) (fun () -> [ 1 ])
")
                    "the callback runs later, not as the row's reader"
            }

            test "the finding's range is the creator call" {
                let range =
                    (findings "let m () = createMemo (fun _ -> createEffect ignore; 1)\n").Head.Range

                Expect.equal (range.StartLine, range.StartColumn, range.EndColumn) (4, 32, 51) "createEffect ignore"
            }

            test "createMemoWith owns its nodes" {
                Expect.isEmpty (codes "let m () = createMemoWith (fun _ -> createEffect ignore; 1)\n") "owning memo"
            }

            test "a creator in the projection factory body" {
                Expect.isEmpty
                    (codes "let p () = createProjectionWith id (fun item -> createEffect ignore; fun () -> item ()) (fun () -> [ 1 ])\n")
                    "the factory runs once per key"
            }

            test "a creator in a stored callback" {
                Expect.isEmpty
                    (codes "let m (handlers: ResizeArray<unit -> unit>) = createMemo (fun _ -> handlers.Add (fun () -> createEffect ignore); 1)\n")
                    "the callback runs later"
            }

            test "createSignal in a memo" {
                Expect.isEmpty (codes "let m () = createMemo (fun _ -> (createSignal 1).Value)\n") "a signal is not owned"
            }

            test "a user function named createMemo is not a context" {
                Expect.isEmpty
                    (codes "let createMemo (f: unit -> int) = f ()\nlet m () = createMemo (fun () -> createEffect ignore; 1)\n")
                    "own createMemo"
            }

            test "a project without Ranvier is not analyzed" {
                let source = "module Fixture\nlet x = 1\n"
                Expect.isEmpty (run Analyzers.ran010Cli (context (plain.Force ()) source)) "no Ranvier reference"
            }

            test "the fixes rename the comparer, async and policy forms" {
                for before, after in
                    [
                        "createMemoWithComparer HashIdentity.Structural (fun _ -> createEffect ignore; 1)",
                        "createOwningMemoWithComparer HashIdentity.Structural (fun _"
                        "createAsync (fun _ _ -> createEffect ignore; Task.FromResult 1)", "createAsyncWith (fun _ _"
                        "createAsyncWithPolicy FlightPolicy.KeepLatest (fun _ _ -> createEffect ignore; Task.FromResult 1)",
                        "createOwningAsyncWithPolicy FlightPolicy.KeepLatest (fun _ _"
                    ] do
                    let source =
                        "module Fixture\nopen Ranvier\nopen System.Threading.Tasks\nlet m () = "
                        + before
                        + "\n"

                    let fix =
                        (run Analyzers.ran010Cli (context (ranvier.Force ()) source)).Head.Fixes
                        |> List.exactlyOne

                    let repaired = applyFix source fix
                    Expect.stringContains repaired after "renamed"
                    Expect.isEmpty (run Analyzers.ran010Cli (context (ranvier.Force ()) repaired)) "no finding after the fix"
            }

            test "the fix renames createMemo to createMemoWith" {
                let source =
                    "module Fixture\nopen Ranvier\nlet m () = createMemo (fun _ -> createEffect ignore; 1)\n"

                let fix =
                    (run Analyzers.ran010Cli (context (ranvier.Force ()) source)).Head.Fixes
                    |> List.exactlyOne

                let repaired = applyFix source fix
                Expect.stringContains repaired "createMemoWith (fun _" "renamed"
                Expect.isEmpty (run Analyzers.ran010Cli (context (ranvier.Force ()) repaired)) "no finding after the fix"
            }
        ]
