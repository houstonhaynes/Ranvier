/// <summary>Prints server HTML per fixture and state; under Fable with a DOM, also mounts, updates and hydrates.</summary>
module Ranvier.Arms.Probe.Main

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template
open Ranvier.Arms.Probe.Fixtures

let states =
    [
        Idle
        Loading 0
        Loading 40
        Loaded ("done", 3)
        Loaded ("none", 0)
        Loaded ("big", 120)
    ]

let fixtures: (string * Component<Signal<Load>>) list =
    [
#if !ENTRY_LENS && !ENTRY_PLUGIN
        "explicit", explicitLoader
#endif
#if !ENTRY_LENS && !ENTRY_PLUGIN && !ENTRY_EXPLICIT
        "explicitRich", explicitRich
#endif
#if !ENTRY_EXPLICIT && !ENTRY_PLUGIN
        "lens", lensLoader
#endif
#if !ENTRY_EXPLICIT && !ENTRY_LENS
        "match", matchLoader
#if !ENTRY_PLUGIN
        "matchRich", matchRich
        "explicitRows", explicitRows
        "matchRows", matchRows
        "matchPair", matchPair
        "matchUnmarked", matchUnmarked
#endif
#endif
    ]

let private omit =
    { Server.Options.defaults with
        Policy = Representation.Omit
    }

let serverLines () =
    [
        for name, definition in fixtures do
            for state in states do
                let graph = new Graph ()
                let signal = graph.Run (fun () -> Api.createSignal state)
                yield sprintf "%s|%A|%s|%s" name state (structuralHash definition) (Server.render omit definition signal)
    ]

#if FABLE_COMPILER
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View.Foundation.Mounting
open Ranvier.View.Foundation.Scheduling

[<Emit("typeof document !== 'undefined'")>]
let private hasDom () : bool = jsNative

[<Emit("globalThis.servedHtml")>]
let private servedHtml () : obj = jsNative

[<Emit("$0 === $1")>]
let private same (left: obj) (right: obj) : bool = jsNative

[<Emit("document.createElement('div')")>]
let private host () : HTMLElement = jsNative

[<Emit("$0.querySelector($1)")>]
let private query (_: HTMLElement) (_: string) : obj = jsNative

[<Emit("$0.querySelector('.loader').firstElementChild")>]
let private armNode (_: HTMLElement) : obj = jsNative

/// <summary>Mounts the fixture, writes each step and records the markup and whether the arm's first element survived.</summary>
let reactive (definition: Component<Signal<Load>>) =
    use graph = new Graph ()
    let signal = graph.Run (fun () -> Api.createSignal (Loading 10))
    let target = host ()
    let reports = ResizeArray<string>()

    let report (errors: exn list) =
        for error in errors do
            reports.Add error.Message

    let mounted =
        mountWith SynchronousCommits graph None report target None (fun context ->
            Representation.Program.run (Component.render definition signal) context.Backend)

    let first () =
        armNode target

    let rowNode () =
        query target "li"

    let steps =
        [
            Loading 70
            Loaded ("a", 1)
            Loaded ("b", 2)
            Loaded ("c", 0)
            Loaded ("d", 150)
            Loaded ("e", 5)
            Loaded ("f", 3)
            Idle
        ]

    let shots = ResizeArray [ target.innerHTML ]

    for step in steps do
        let before, beforeRow = first (), rowNode ()
        graph.Run (fun () -> signal.Value <- step)

        shots.Add (
            sprintf
                "%s kept=%b row=%b"
                target.innerHTML
                (same before (first ()))
                (not (isNull beforeRow)
                 && same beforeRow (rowNode ()))
        )

    mounted.Dispose ()
    shots.Add (sprintf "reports=%A" (List.ofSeq reports))
    List.ofSeq shots

[<Emit("performance.now()")>]
let private now () : float = jsNative

[<Emit("globalThis.benchArms === true")>]
let private benching () : bool = jsNative

/// <summary>Milliseconds to mount 500 loaders, then for 20 rounds of payload writes to all of them, then 20 rounds of case switches.</summary>
let bench (definition: Component<Signal<Load>>) =
    use graph = new Graph ()

    let signals =
        Array.init 500 (fun i -> graph.Run (fun () -> Api.createSignal (Loaded ("row", i))))

    let target = host ()
    let started = now ()

    let mounts =
        signals
        |> Array.map (fun signal ->
            mountWith SynchronousCommits graph None ignore target None (fun context ->
                Representation.Program.run (Component.render definition signal) context.Backend))

    let mounted = now ()

    for round in 1..20 do
        graph.Run (fun () ->
            for i, signal in Array.indexed signals do
                signal.Value <- Loaded ("row", i + round))

    let updated = now ()

    for round in 1..20 do
        graph.Run (fun () ->
            for i, signal in Array.indexed signals do
                signal.Value <- (if round % 2 = 0 then Loaded ("row", i) else Loading i))

    let switched = now ()

    for mount in mounts do
        mount.Dispose ()

    sprintf "mount=%.1f payload=%.1f switch=%.1f" (mounted - started) (updated - mounted) (switched - updated)

/// <summary>Hydrates the fixture under Strict from the server HTML of <c>Loaded ("done", 3)</c>, then writes two steps.</summary>
let hydrated (html: string) (definition: Component<Signal<Load>>) =
    use graph = new Graph ()
    let signal = graph.Run (fun () -> Api.createSignal (Loaded ("done", 3)))
    let target = host ()
    target.innerHTML <- html
    let served = query target "pre"

    let mounted =
        Hydration.hydrate graph None (fun errors -> failwithf "report %A" errors) { IdPrefix = "r"; Strict = true } target definition signal

    let adopted = same served (query target "pre")
    graph.Run (fun () -> signal.Value <- Loaded ("later", 4))
    let after = target.innerHTML
    mounted.Dispose ()
    sprintf "adopted=%b %s" adopted after
#endif

#if !FABLE_COMPILER
/// <summary>Milliseconds for the first and the mean of 200 further quotation lowerings of a five-target site.</summary>
let loweringCost () =
    let lower () =
        Arms.Match (
            read,
            function
            | Idle
            | Loading 0 -> Ranvier.View.Foundation.Sugar.Elements.span () { "Not started" }
            | Loading percent -> Ranvier.View.Foundation.Sugar.Elements.b () { fun _ -> string percent }
            | Loaded (_, 0) -> Ranvier.View.Foundation.Sugar.Elements.span () { "Empty" }
            | Loaded (text, count) when count > 99 -> Ranvier.View.Foundation.Sugar.Elements.b () { fun _ -> text }
            | Loaded (text, count) -> Ranvier.View.Foundation.Sugar.Elements.pre () { fun _ -> text + string count }
        )
        |> ignore

    let watch = System.Diagnostics.Stopwatch.StartNew ()
    lower ()
    let first = watch.Elapsed.TotalMilliseconds
    watch.Restart ()

    for _ in 1..200 do
        lower ()

    first, watch.Elapsed.TotalMilliseconds / 200.0

/// <summary>The .NET rejection of a pattern variable read as static text.</summary>
let buildTimeRead () =
    try
        Arms.Match (
            read,
            function
            | Loaded (text, _) -> Ranvier.View.Foundation.Sugar.Elements.span () { text }
            | _ -> Ranvier.View.Foundation.Sugar.Elements.span () { "Other" }
        )
        |> ignore

        "accepted"
    with Quoted.BuildTimeRead name ->
        "BuildTimeRead " + name
#endif

[<EntryPoint>]
let main _ =
#if !FABLE_COMPILER
    let first, mean = loweringCost ()
    printfn "lowering|first=%.2fms mean=%.3fms" first mean
    printfn "bad|%s" (buildTimeRead ())
#endif
    for line in serverLines () do
        printfn "server|%s" line
#if FABLE_COMPILER
    if hasDom () && benching () then
        for round in 1..5 do
            for name, definition in fixtures do
                if
                    name = "explicit"
                    || name = "lens"
                    || name = "match"
                then
                    printfn "bench|%d|%s|%s" round name (bench definition)
    elif hasDom () then
        for name, definition in fixtures do
            for index, shot in List.indexed (reactive definition) do
                printfn "mount|%s|%d|%s" name index shot

        let served = servedHtml ()

        if not (isNull served) then
            // The served HTML is the loader's, so only the loader-shaped fixtures hydrate it.
            for name, definition in fixtures do
                if
                    not (
                        name.EndsWith "Rows"
                        || name = "matchPair"
                        || name = "matchUnmarked"
                    )
                then
                    printfn "hydrate|%s|%s" name (hydrated (unbox served) definition)
#endif
    0
