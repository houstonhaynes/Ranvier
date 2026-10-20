/// <summary>A definition created inside an arms function: rejected in Fable Debug builds, remounted on every update in Release.</summary>
module SugarDebugChecks

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open CheckSupport

type private Picked =
    {
        Pick: Signal<int>
        Log: ResizeArray<string>
    }

let private noting (log: ResizeArray<string>) =
    fun (_: unit -> unit) (_: HTMLSpanElement) ->
        log.Add "mounted"
        ignore

/// <summary>Creates its arm inside the arms function, once per selector evaluation.</summary>
let private hazard =
    Component.define (fun _ (picked: Picked) -> picked) (fun t ->
        t.Div () {
            t.matchWith (fun (p: Picked) -> p.Pick.Value, p.Log) (fun (_, log) ->
                let arm =
                    t.armOf (fun (a: TemplateBuilder<unit -> unit>) -> a.Span (a.Hooks.mounted (noting log)) { "arm" })

                t.arm arm ())
        })

/// <summary>Creates its arm inside the key's arms function, once per key evaluation.</summary>
let private branchHazard =
    Component.define (fun _ (picked: Picked) -> picked) (fun t ->
        t.Div () {
            t.branch (fun (p: Picked) -> p.Pick.Value) (fun _ ->
                let arm = t.armOf (fun (a: TemplateBuilder<unit -> unit>) -> a.Span () { "arm" })
                t.arm arm ())
        })

/// <summary>Creates a definition inside the items function, once per items evaluation.</summary>
let private eachHazard =
    Component.define (fun _ (picked: Picked) -> picked) (fun t ->
        let row =
            Component.define (fun _ (item: unit -> int) -> item) (fun r -> r.Span () { "row" })

        t.Div () {
            t.each
                id
                (fun (p: Picked) ->
                    Component.define (fun _ () -> ()) (fun d -> d.Span () { "hidden" })
                    |> ignore

                    [| p.Pick.Value |])
                row
        })

let private boundLog = ResizeArray<string>()

/// <summary>An arm bound in the build function mounts once, however often the selector re-evaluates.</summary>
let private bound =
    Component.define (fun _ (picked: Picked) -> picked) (fun t ->
        let arm =
            t.armOf (fun (a: TemplateBuilder<unit -> unit>) -> a.Span (a.Hooks.mounted (noting boundLog)) { "arm" })

        t.Div () { t.matchWith (fun (p: Picked) -> p.Pick.Value) (fun _ -> t.arm arm ()) })

let private picks (definition: Component<Picked>) (log: ResizeArray<string>) =
    use graph = new Graph ()
    let picked = graph.Run (fun () -> { Pick = Api.createSignal 1; Log = log })
    let target = host ()

    let mounted =
        Mounting.mount graph None strict target None (fun context -> Program.run (Component.render definition picked) context.Backend)

    for value in 2..3 do
        graph.Run (fun () -> picked.Pick.Value <- value)

    mounted.Dispose ()
    log.Count

let private boundArmMountsOnce () =
    boundLog.Clear ()
    equal 1 (picks bound boundLog)

#if DEBUG
let private message =
    "a template definition was created while a region was read; bind it in the template's build function"

let private raisesWhenMounted (render: Component<Picked> -> Picked -> Program.View) (definition: Component<Picked>) =
    use graph = new Graph ()

    let picked =
        graph.Run (fun () ->
            {
                Pick = Api.createSignal 1
                Log = ResizeArray ()
            })

    let reports = ResizeArray<exn list>()

    let thrown =
        try
            let mounted =
                Mounting.mount graph None reports.Add (host ()) None (fun context -> Program.run (render definition picked) context.Backend)

            mounted.Dispose ()
            []
        with error ->
            [ string error ]

    let seen = thrown @ List.concat (messages reports)

    equal
        true
        (seen
         |> List.exists (fun text -> text.Contains message))

let private definitionInRegionRaises () =
    raisesWhenMounted Component.render hazard

let private branchDefinitionRaises () =
    raisesWhenMounted Component.render branchHazard

let private eachDefinitionRaises () =
    raisesWhenMounted Component.render eachHazard

let private expandedDefinitionRaises () =
    raisesWhenMounted (Component.renderWith Program.ops) hazard

let private serverDefinitionInRegionRaises () =
    use graph = new Graph ()

    let picked =
        graph.Run (fun () ->
            {
                Pick = Api.createSignal 1
                Log = ResizeArray ()
            })

    let thrown =
        try
            Server.render
                { Server.Options.defaults with
                    Policy = Omit
                }
                hazard
                picked
            |> ignore

            ""
        with error ->
            string error

    equal true (thrown.Contains message)

let cases: (string * (unit -> unit)) array =
    [|
        "a definition created in an arms function raises in a Debug build", definitionInRegionRaises
        "a definition created in a branch arms function raises in a Debug build", branchDefinitionRaises
        "a definition created in an each items function raises in a Debug build", eachDefinitionRaises
        "a definition created in an arms function raises through the expanded path in a Debug build", expandedDefinitionRaises
        "a definition created in an arms function raises in a Debug server render", serverDefinitionInRegionRaises
        "an arm bound in the build function mounts once in a Debug build", boundArmMountsOnce
    |]
#else
let private armInArmsRemountsInRelease () =
    equal 3 (picks hazard (ResizeArray ()))

let cases: (string * (unit -> unit)) array =
    [|
        "a definition created in an arms function remounts on every update in Release", armInArmsRemountsInRelease
        "an arm bound in the build function mounts once in Release", boundArmMountsOnce
    |]
#endif
