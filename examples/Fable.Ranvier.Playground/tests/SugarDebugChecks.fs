/// <summary>A definition created inside an arms function: rejected in Fable Debug builds, remounted on every update in Release.</summary>
module SugarDebugChecks

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

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
    Component.template
        (fun _ (picked: Picked) -> picked)
        (div () {
            matchWith (fun (p: Picked) -> p.Pick.Value, p.Log) (fun (_, log) ->
                let created = armOf (span (Hooks.mounted (noting log)) { "arm" })
                arm created ())
        })

/// <summary>Creates its arm inside the key's arms function, once per key evaluation.</summary>
let private branchHazard =
    Component.template
        (fun _ (picked: Picked) -> picked)
        (div () {
            branch (fun (p: Picked) -> p.Pick.Value) (fun _ ->
                let created = armOf (span () { "arm" })
                arm created ())
        })

let private eachRow =
    Component.define (fun _ (item: unit -> int) -> item) (fun _ -> span () { "row" })

/// <summary>Creates a definition inside the items function, once per items evaluation.</summary>
let private eachHazard =
    Component.template
        (fun _ (picked: Picked) -> picked)
        (div () {
            each
                id
                (fun (p: Picked) ->
                    Component.template (fun _ () -> ()) (span () { "hidden" })
                    |> ignore

                    [| p.Pick.Value |])
                eachRow
        })

let private boundLog = ResizeArray<string>()

let private boundArm = armOf (span (Hooks.mounted (noting boundLog)) { "arm" })

/// <summary>An arm bound at module level mounts once, however often the selector re-evaluates.</summary>
let private bound =
    Component.template (fun _ (picked: Picked) -> picked) (div () { matchWith (fun (p: Picked) -> p.Pick.Value) (fun _ -> arm boundArm ()) })

let private propertyLog = ResizeArray<string>()

/// <summary>A static property whose getter creates a definition on each read.</summary>
type private ArmHolder =
    static member Arm: Component<unit -> unit> =
        armOf (span (Hooks.mounted (noting propertyLog)) { "held" })

/// <summary>Reads <c>ArmHolder.Arm</c> inside the arms function, once per selector evaluation.</summary>
let private propertyHazard =
    Component.template (fun _ (picked: Picked) -> picked) (div () { matchWith (fun (p: Picked) -> p.Pick.Value) (fun _ -> arm ArmHolder.Arm ()) })

/// <summary>The setups run by <c>ArmRecord.Create</c> and the <c>ArmClass</c> constructor: one per mount.</summary>
let mutable private armSetups = 0

type ArmRecord =
    {
        Read: unit -> int
    }

    static member Create(_: Setup, read: unit -> int) =
        armSetups <- armSetups + 1
        { Read = read }

    static member View = i () { fun a -> string (a.Read ()) }

type ArmClass(_setup: Setup, read: unit -> int) =
    do armSetups <- armSetups + 1
    member _.Read = read
    static member View: TemplateView<ArmClass> = i () { fun (a: ArmClass) -> string (a.Read ()) }

/// <summary>Calls <c>Component.ofMembers</c> inside the arms function, once per selector evaluation.</summary>
let private membersHazard =
    Component.template
        (fun _ (picked: Picked) -> picked)
        (div () { matchWith (fun (p: Picked) -> p.Pick.Value) (fun v -> arm (Component.ofMembers<ArmRecord, _>()) v) })

/// <summary>Calls <c>Component.ofClass</c> inside the arms function, once per selector evaluation.</summary>
let private classHazard =
    Component.template
        (fun _ (picked: Picked) -> picked)
        (div () { matchWith (fun (p: Picked) -> p.Pick.Value) (fun v -> arm (Component.ofClass ArmClass) v) })

// Opened after the fixtures: CheckSupport.span shadows the span tag.
open CheckSupport

/// <summary>Mounts <c>definition</c>, writes the picks 2 and 3, applies <c>observe</c> to the host and disposes.</summary>
let private picking (definition: Component<Picked>) (log: ResizeArray<string>) (observe: HTMLElement -> 'result) =
    use graph = new Graph ()
    let picked = graph.Run (fun () -> { Pick = Api.createSignal 1; Log = log })
    let target = host ()

    let mounted =
        Mounting.mount graph None strict target None (fun context -> Program.run (Component.render definition picked) context.Backend)

    for value in 2..3 do
        graph.Run (fun () -> picked.Pick.Value <- value)

    let observed = observe target
    mounted.Dispose ()
    observed

let private picks (definition: Component<Picked>) (log: ResizeArray<string>) =
    picking definition log ignore
    log.Count

let private boundArmMountsOnce () =
    boundLog.Clear ()
    equal 1 (picks bound boundLog)

/// <summary>A list whose rows have an exit on an element inside the row root.</summary>
let private innerExit =
    Component.template
        (fun _ (items: int array) -> items)
        (ul () {
            forEach
                id
                (fun (items: int array) -> items)
                (li () { b (Hooks.exit (System.Func<_, _, _, _>(fun (_: unit -> int) (_: HTMLElement) _ -> ignore))) { "x" } })
        })

/// <summary>A template whose root element, a root of the mount, has an exit.</summary>
let private rootExit =
    Component.template
        (fun _ (items: int array) -> items)
        (ul (Hooks.exit (System.Func<_, _, _, _>(fun (_: int array) (_: HTMLUListElement) _ -> ignore))) { "x" })

/// <summary>The messages of mounting <c>definition</c> on one row: raised during the mount or reported.</summary>
let private exitMessages (definition: Component<int array>) =
    use graph = new Graph ()
    let reports = ResizeArray<exn list>()

    let thrown =
        try
            let mounted =
                Mounting.mount graph None reports.Add (host ()) None (fun context ->
                    Program.run (Component.render definition [| 1 |]) context.Backend)

            mounted.Dispose ()
            []
        with error ->
            [ error.Message ]

    thrown @ List.concat (messages reports)

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

let private staticMemberArmRaises () =
    raisesWhenMounted Component.render propertyHazard

let cases: (string * (unit -> unit)) array =
    [|
        "a definition created in an arms function raises in a Debug build", definitionInRegionRaises
        "a definition created in a branch arms function raises in a Debug build", branchDefinitionRaises
        "a definition created in an each items function raises in a Debug build", eachDefinitionRaises
        "a definition created in an arms function raises through the expanded path in a Debug build", expandedDefinitionRaises
        "a definition created in an arms function raises in a Debug server render", serverDefinitionInRegionRaises
        "an arm bound outside the arms function mounts once in a Debug build", boundArmMountsOnce
        "a static member arm read in an arms function raises in a Debug build", staticMemberArmRaises
        "Component.ofMembers in an arms function raises in a Debug build", (fun () -> raisesWhenMounted Component.render membersHazard)
        "Component.ofClass in an arms function raises in a Debug build", (fun () -> raisesWhenMounted Component.render classHazard)
        "an exit hook below a row root raises in a Debug build",
        (fun () ->
            equal
                true
                (exitMessages innerExit
                 |> List.exists (fun text -> text.Contains "An exit hook requires a row or arm root element")))
        "an exit hook on a mount root raises in a Debug build",
        (fun () ->
            equal
                true
                (exitMessages rootExit
                 |> List.exists (fun text -> text.Contains "An exit hook requires a row or arm root element")))
    |]
#else
let private armInArmsRemountsInRelease () =
    equal 3 (picks hazard (ResizeArray ()))

let private staticMemberArmRemountsInRelease () =
    propertyLog.Clear ()
    equal 3 (picks propertyHazard propertyLog)

/// <summary>The arm setups and the host's text after the picks 2 and 3.</summary>
let private remounts (definition: Component<Picked>) =
    armSetups <- 0
    let text = picking definition (ResizeArray ()) (fun target -> target.textContent)
    armSetups, text

let cases: (string * (unit -> unit)) array =
    [|
        "a definition created in an arms function remounts on every update in Release", armInArmsRemountsInRelease
        "an arm bound outside the arms function mounts once in Release", boundArmMountsOnce
        "a static member arm remounts on every update in Release", staticMemberArmRemountsInRelease
        "Component.ofMembers in an arms function remounts on every update and shows the last payload in Release",
        (fun () -> equal (3, "3") (remounts membersHazard))
        "Component.ofClass in an arms function remounts on every update and shows the last payload in Release",
        (fun () -> equal (3, "3") (remounts classHazard))
        "an exit hook below a row root mounts without a report in Release", (fun () -> equal [] (exitMessages innerExit))
        "an exit hook on a mount root mounts without a report in Release", (fun () -> equal [] (exitMessages rootExit))
    |]
#endif
