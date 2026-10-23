module Ranvier.View.Twin.Tests.InspectTests

open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin
open Ranvier.View.Twin.Tests.Fixtures

type private InnerState = { Label: string }
type private OuterState = { Caption: string }
type private RegionState = { Items: Signal<int array> }

let private inner =
    Component.define (fun _ (label: string) -> { Label = label }) (fun t -> t.span [||] [| t.text (fun state -> state.Label) |])

let private outer =
    Component.define (fun _ (caption: string) -> { Caption = caption }) (fun t -> t.render inner (fun state -> state.Caption))

let private wrapped =
    Component.define (fun _ (caption: string) -> { Caption = caption }) (fun t -> t.div [||] [| t.render inner (fun state -> state.Caption) |])

let private nested =
    Component.define (fun _ (caption: string) -> caption) (fun t -> t.render outer id)

let private regionFirst =
    Component.define (fun _ () -> { Items = Api.createSignal [| 1 |] }) (fun t ->
        t.fragment [| t.each id (fun state -> state.Items.Value) row; t.text "after" |])

let private staticChild =
    Component.define (fun _ (label: string) -> { Label = label }) (fun t -> t.text "static")

let private textChildren =
    Component.define (fun _ () -> ()) (fun t ->
        t.fragment
            [|
                t.text "before"
                t.render staticChild (fun _ -> "first")
                t.render staticChild (fun _ -> "second")
            |])

let private childAfterList =
    Component.define (fun _ (items: int array) -> items) (fun t -> t.div [||] [| t.each id id row; t.render inner (fun _ -> "after list") |])

let private probes (session: Session) =
    let rec walk (node: TwinNode) =
        [
            match Inspect.probeOf (unbox node.Native) with
            | Some (Inspect.InstanceProbe _) -> ()
            | Some probe -> probe
            | None -> ()

            yield! Inspect.instancesOf (unbox node.Native)

            for child in node.Children do
                yield! walk child
        ]

    walk (Bridge.nodeOf session.Host)

[<Tests>]
let tests =
    testList
        "Inspect"
        [
            for name, definition, count in
                [
                    "shared root", outer, 2
                    "wrapped root", wrapped, 2
                    "three levels", nested, 3
                ] do
                testCase $"strict hydration registers nested instances: {name}"
                <| fun () ->
                    use session =
                        Twin.hydrate
                            Server.Options.defaults
                            { Hydration.HydrationOptions.defaults with
                                Strict = true
                            }
                            definition
                            "child"

                    let instances =
                        probes session
                        |> List.choose (function
                            | Inspect.InstanceProbe (_, _, _, path) -> Some path.Text
                            | _ -> None)

                    Expect.equal instances.Length count "every hydrated instance"
                    Expect.equal (List.distinct instances).Length count "distinct hydrated paths"
                    Expect.stringContains (session.Dump ()) "instance InnerState" "the child's instance is visible"

            testCase "strict hydration keeps text separators and list-root instance probes"
            <| fun () ->
                for definition, count in [ textChildren, 3; regionFirst, 2 ] do
                    use session =
                        Twin.hydrate
                            Server.Options.defaults
                            { Hydration.HydrationOptions.defaults with
                                Strict = true
                            }
                            definition
                            ()

                    let instances =
                        probes session
                        |> List.filter (function
                            | Inspect.InstanceProbe _ -> true
                            | _ -> false)

                    Expect.equal instances.Length count "instance registration after hydration"
                    Expect.stringContains (session.Dump ()) "path=r-0" "hydrated paths use the root prefix"

            testCase "a keyed list registers its rows' keys in order"
            <| fun () ->
                let source = ref Unchecked.defaultof<Signal<int array>>

                use session =
                    Twin.mountView TwinMount.defaults (fun context ->
                        source.Value <- Api.createSignal [| 3; 1; 2 |]
                        Program.run (Component.render rows source.Value) context.Backend)

                let keys =
                    probes session
                    |> List.pick (fun p ->
                        match p with
                        | Inspect.ListProbe (_, rows, _) ->
                            Some (
                                rows ()
                                |> Array.map (fun (key, _) -> unbox<int> key)
                            )
                        | _ -> None)

                Expect.equal keys [| 3; 1; 2 |] "keys"

            testCase "an instance registers its definition and value"
            <| fun () ->
                use session = Twin.mount counter 4

                let found =
                    probes session
                    |> List.exists (fun p ->
                        match p with
                        | Inspect.InstanceProbe (_, instance, _, _) -> instance :? CounterState
                        | _ -> false)

                Expect.isTrue found "instance probe"

            testCase "a mount registers its phase, commit mode and identity prefix"
            <| fun () ->
                use session = Twin.mount counter 4

                let mount =
                    probes session
                    |> List.pick (fun p ->
                        match p with
                        | Inspect.MountProbe (phase, queued, flushing, _, _) -> Some (phase (), queued, flushing ())
                        | _ -> None)

                Expect.equal mount (Attached, false, false) "an attached synchronous mount"

            testCase "a disposed mount leaves the registry"
            <| fun () ->
                let session = Twin.mount counter 4
                let root = Bridge.nodeOf session.Host
                let first = root.FirstChild
                (session :> System.IDisposable).Dispose()
                Expect.isNone (Inspect.probeOf (unbox first.Native)) "removed"

            testCase "an instance whose first root is a list keeps both registrations"
            <| fun () ->
                use session = Twin.mount regionFirst ()
                let found = probes session

                Expect.isTrue
                    (found
                     |> List.exists (function
                         | Inspect.ListProbe _ -> true
                         | _ -> false))
                    "list probe"

                Expect.isTrue
                    (found
                     |> List.exists (function
                         | Inspect.InstanceProbe (_, (:? RegionState), _, _) -> true
                         | _ -> false))
                    "instance probe"

            testCase "inlined children register at their first root with distinct paths"
            <| fun () ->
                for definition, count in [ outer, 2; wrapped, 2; nested, 3 ] do
                    use session = Twin.mount definition "child"

                    let instances =
                        probes session
                        |> List.choose (function
                            | Inspect.InstanceProbe (_, value, _, path) -> Some (value, path.Text)
                            | _ -> None)

                    Expect.equal instances.Length count "every instance"

                    Expect.equal
                        (instances
                         |> List.map snd
                         |> List.distinct
                         |> List.length)
                        count
                        "distinct identity paths"

                    Expect.isTrue
                        (instances
                         |> List.exists (fun (value, _) ->
                             match value with
                             | :? InnerState as state -> state.Label = "child"
                             | _ -> false))
                        "child's own instance value"

            testCase "static inlined text children register after text separator comments"
            <| fun () ->
                use session = Twin.mount textChildren ()
                let root = Bridge.nodeOf session.Host

                let labels =
                    root.Children
                    |> Seq.collect (fun node ->
                        let native: Fable.Core.TS.Dom.Node = unbox node.Native

                        Inspect.instancesOf native
                        |> Array.choose (function
                            | Inspect.InstanceProbe (_, (:? InnerState as state), _, _) -> Some (state.Label, native.nodeType, native.textContent)
                            | _ -> None))
                    |> Seq.toList

                Expect.equal labels [ "first", 3., Some "static"; "second", 3., Some "static" ] "both static text roots"

            testCase "hydrated child roots skip a preceding list's server rows"
            <| fun () ->
                let strict =
                    { Hydration.HydrationOptions.defaults with
                        Strict = true
                    }

                use session =
                    Twin.hydrate Server.Options.defaults strict childAfterList [| 1; 2; 3 |]

                let rec childRoots (node: TwinNode) =
                    [
                        let native: Fable.Core.TS.Dom.Node = unbox node.Native

                        for probe in Inspect.instancesOf native do
                            match probe with
                            | Inspect.InstanceProbe (_, (:? InnerState), _, _) -> native.nodeName.ToLowerInvariant ()
                            | _ -> ()

                        for child in node.Children do
                            yield! childRoots child
                    ]

                Expect.equal (childRoots (Bridge.nodeOf session.Host)) [ "span" ] "the child is rooted after the whole list"

            testCase "disposing nested instances removes every registration from their shared root"
            <| fun () ->
                let session = Twin.mount nested "child"
                let root = Bridge.nodeOf session.Host

                let shared =
                    root.Children
                    |> Seq.find (fun node -> (Inspect.instancesOf (unbox node.Native)).Length = 3)

                (session :> System.IDisposable).Dispose()
                Expect.isEmpty (Inspect.instancesOf (unbox shared.Native)) "all instance cleanups ran"
                Expect.isNone (Inspect.probeOf (unbox shared.Native)) "no fallback instance survives"
        ]
