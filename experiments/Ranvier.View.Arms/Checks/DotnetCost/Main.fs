/// <summary>
/// The .NET costs of Arms.Match, measured in a fresh process: the first five-target site's lowering before any other
/// Arms.Match runs, the mean of 200 more, and server renders of a 1,000-row site against its armOf twin.
/// </summary>
/// <remarks>
/// <c>--timing</c> fails the run when the mean lowering exceeds 4 ms. <c>--prepare</c> calls <c>Arms.Prepare</c> first,
/// and after the measurements twice more and on four threads at once, each thread also lowering and rendering a site;
/// the run fails when one raises or renders other HTML.
/// <c>--twin-first</c> builds the armOf twin before the first lowering, so the first figure excludes the view's own
/// cold start.
/// </remarks>
module DotnetCost.Main

open System
open System.Diagnostics
open global.Ranvier
open Ranvier.View
open Ranvier.View.Representation
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of text: string * count: int

let private read (state: Signal<Load>) =
    state.Value

let private fiveTargets () =
    Arms.Match (
        read,
        function
        | Idle
        | Loading 0 -> span () { "Not started" }
        | Loading percent -> b () { fun _ -> string percent }
        | Loaded (_, 0) -> span () { "Empty" }
        | Loaded (text, count) when count > 99 -> b () { fun _ -> text }
        | Loaded (text, count) -> pre () { fun _ -> text + string count }
    )

let private rowsSite () =
    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            Arms.Match (
                read,
                function
                | Loaded (owner, count) ->
                    ul () {
                        forEach
                            snd
                            (fun _ -> Array.init count (fun index -> owner, index))
                            (li () { fun (row: unit -> string * int) -> fst (row ()) + string (snd (row ())) })
                    }
                | _ -> span () { "Other" }
            )
        })

let private rowsTwin () =
    let rowsArm =
        armOf (
            ul () {
                forEach
                    snd
                    (fun (payload: unit -> string * int) ->
                        let owner, count = payload ()
                        Array.init count (fun index -> owner, index))
                    (li () { fun (row: unit -> string * int) -> fst (row ()) + string (snd (row ())) })
            }
        )

    let otherArm = armOf (span () { "Other" })

    Component.template
        (fun _ (state: Signal<Load>) -> state)
        (div () {
            matchWith read (function
                | Loaded (owner, count) -> arm rowsArm (owner, count)
                | _ -> arm otherArm ())
        })

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

/// <summary>Mean milliseconds per server render of the site with 1,000 rows, over <c>renders</c> renders after 20 warm-up renders.</summary>
let private renderCost (site: Component<Signal<Load>>) (renders: int) =
    use graph = new Graph ()
    let state = graph.Run (fun () -> Api.createSignal (Loaded ("r", 1000)))

    for _ in 1..20 do
        Server.render omit site state |> ignore

    let watch = Stopwatch.StartNew ()

    for _ in 1..renders do
        Server.render omit site state |> ignore

    watch.Elapsed.TotalMilliseconds / float renders

/// <summary>The failures of four threads each calling <c>Arms.Prepare</c> at once, then lowering and rendering a 3-row site.</summary>
let private preparedConcurrently () =
    Arms.Prepare ()
    Arms.Prepare ()

    let html (site: Component<Signal<Load>>) =
        use graph = new Graph ()
        let state = graph.Run (fun () -> Api.createSignal (Loaded ("r", 3)))
        Server.render omit site state

    let expected = html (rowsTwin ())
    let failures = Collections.Concurrent.ConcurrentBag<string>()
    use barrier = new Threading.Barrier 4

    let threads =
        [|
            for _ in 1..4 ->
                Threading.Thread (fun () ->
                    barrier.SignalAndWait ()

                    try
                        Arms.Prepare ()
                        let rendered = html (rowsSite ())

                        if rendered <> expected then
                            failures.Add rendered
                    with error ->
                        failures.Add error.Message)
        |]

    for thread in threads do
        thread.Start ()

    for thread in threads do
        thread.Join ()

    List.ofSeq failures

[<EntryPoint>]
let main args =
    if args |> Array.contains "--twin-first" then
        let w = Stopwatch.StartNew ()
        rowsTwin () |> ignore
        printfn "twin|cold-first=%.2fms" w.Elapsed.TotalMilliseconds

    if args |> Array.contains "--prepare" then
        let w = Stopwatch.StartNew ()
        Arms.Prepare ()
        printfn "prepare|%.2fms" w.Elapsed.TotalMilliseconds

    let jitBefore = Runtime.JitInfo.GetCompilationTime(false).TotalMilliseconds
    let watch = Stopwatch.StartNew ()
    fiveTargets () |> ignore
    let first = watch.Elapsed.TotalMilliseconds

    printfn
        "first-site|jit=%.2fms"
        (Runtime.JitInfo.GetCompilationTime(false).TotalMilliseconds
         - jitBefore)

    watch.Restart ()

    for _ in 1..200 do
        fiveTargets () |> ignore

    let mean = watch.Elapsed.TotalMilliseconds / 200.0
    printfn "lowering|cold-first=%.2fms mean=%.3fms" first mean
    let site, twin = rowsSite (), rowsTwin ()

    for round in 1..3 do
        printfn "render1000|round=%d arms=%.3fms twin=%.3fms" round (renderCost site 200) (renderCost twin 200)

    let prepareFailures =
        if args |> Array.contains "--prepare" then
            let failures = preparedConcurrently ()
            printfn "prepare-concurrent|failures=%d" failures.Length
            failures
        else
            []

    if not prepareFailures.IsEmpty then
        eprintfn "Arms.Prepare failed: %A" prepareFailures
        1
    elif args |> Array.contains "--timing" && mean > 4.0 then
        eprintfn "lowering over budget: mean %.3f ms (budget 4)" mean
        1
    else
        0
