/// <summary>The golden dump fixtures: each case mounts a view and records its <c>Debug</c> text and its host's HTML.</summary>
module DumpChecks

#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Testing
open CheckSupport

type CardItem = { Id: int; Title: string }

type CardState = { Title: Signal<string> }

type Board =
    {
        Columns: Memo<int array>
        Adding: Signal<bool>
        Cards: Signal<CardItem array>
        Editing: Signal<bool>
    }

/// <summary>The async source the current case's boundary content reads.</summary>
let private query () =
    Conformance.local "DumpChecks.query" (fun () -> ref (Unchecked.defaultof<AsyncSource<string>>))

// A card row whose exit holds its root until the mount is disposed.
let private card =
    Component.define
        (fun _ (item: unit -> CardItem) ->
            {
                Title = Api.createSignal (item ()).Title
            })
        (fun t ->
            t.li
                [|
                    t.Attrs.className "card"
                    t.Hooks.exit (System.Func<_, _, _, _>(fun (_: CardState) (_: HTMLLIElement) _ -> ignore))
                |]
                [| t.text (fun (s: CardState) -> s.Title.Value) |])

let private editPanel =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.form [||] [| t.text "edit" |])

let private viewPanel =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.section [||] [| t.text "view" |])

let private loading =
    Component.define (fun _ () -> ()) (fun t -> t.p [||] [| t.text "Loading" |])

let private failedView =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.p [||] [| t.text (fun (f: BoundaryFailure) -> f.Error.Message) |])

let private loaded =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.p [||] [| t.text (fun (_: BoundaryStatus) -> (query ()).Value.Value) |])

let private items =
    [|
        { Id = 1; Title = "Write spec" }
        { Id = 2; Title = "Review" }
        { Id = 3; Title = "Ship" }
        { Id = 4; Title = "Rest" }
    |]

let private board =
    Component.define
        (fun _ () ->
            let cards = Api.createSignal items

            {
                Columns = Api.createMemo (fun _ -> cards.Value |> Array.map (fun c -> c.Id))
                Adding = Api.createSignal false
                Cards = cards
                Editing = Api.createSignal true
            })
        (fun t ->
            t.div
                [| t.Attrs.className "board" |]
                [|
                    t.h2 [||] [| t.text "Cards: "; t.text (fun (b: Board) -> string b.Cards.Value.Length) |]
                    t.ul [||] [| t.each (fun (c: CardItem) -> c.Id) (fun (b: Board) -> b.Cards.Value) card |]
                    t.branch (fun (b: Board) -> if b.Editing.Value then "edit" else "view") (fun key ->
                        if key = "edit" then
                            t.arm editPanel ()
                        else
                            t.arm viewPanel ())
                    t.boundary loading failedView loaded
                |])

let private options = Debug.DumpOptions.defaults

#if !FABLE_COMPILER
let private checkJson (dump: Debug.Dump) =
    use document = System.Text.Json.JsonDocument.Parse (Debug.json dump)

    let rec check (expected: Debug.DumpNode) (actual: System.Text.Json.JsonElement) =
        let property name =
            actual.GetProperty (name: string)

        let equal name value =
            if (property name).GetString() <> value then
                failwith ("JSON field differs: " + name)

        let children (values: Debug.DumpNode array) =
            let json =
                (property "children").EnumerateArray()
                |> Seq.toArray

            if json.Length <> values.Length then
                failwith "JSON child count differs"

            Array.iter2 check values json

        match expected with
        | Debug.Element (tag, attrs, props, nested) ->
            equal "kind" "element"
            equal "tag" tag

            for name, expected in [ "attributes", attrs; "properties", props ] do
                let pairs =
                    (property name).EnumerateArray()
                    |> Seq.map (fun pair -> pair[0].GetString(), pair[1].GetString())
                    |> Seq.toArray

                if pairs <> expected then
                    failwith ("JSON pairs differ: " + name)

            children nested
        | Debug.Text (value, bound) ->
            equal "kind" "text"
            equal "value" value

            if (property "bound").GetBoolean() <> bound then
                failwith "JSON binding differs"
        | Debug.Instance (instance, nested) ->
            equal "kind" "instance"
            equal "name" instance.Name
            equal "path" instance.Path
            equal "hash" instance.Hash

            let fields =
                (property "fields").EnumerateArray()
                |> Seq.toArray

            if fields.Length <> instance.Fields.Length then
                failwith "JSON field count differs"

            Array.iter2
                (fun (field: Debug.FieldDump) (json: System.Text.Json.JsonElement) ->
                    for key, expected in
                        [
                            "name", field.Name
                            "kind", field.Kind
                            "value", field.Value
                            "status", field.Status
                        ] do
                        if json.GetProperty(key).GetString() <> expected then
                            failwith ("JSON instance field differs: " + key))
                instance.Fields
                fields

            children nested
        | Debug.List (path, rows, _, stale) ->
            equal "kind" "list"
            equal "path" path

            if (property "stale").GetBoolean() <> stale then
                failwith "JSON list status differs"

            let actualRows = (property "rows").EnumerateArray() |> Seq.toArray

            if actualRows.Length <> rows.Length then
                failwith "JSON row count differs"

            Array.iter2
                (fun (key, nodes) (row: System.Text.Json.JsonElement) ->
                    if row.GetProperty("key").GetString() <> key then
                        failwith "JSON row key differs"

                    Array.iter2
                        check
                        nodes
                        (row.GetProperty("children").EnumerateArray()
                         |> Seq.toArray))
                rows
                actualRows
        | Debug.Arm (path, key, definition, nested, _) ->
            equal "kind" "arm"
            equal "path" path
            equal "key" key
            equal "definition" definition
            children nested
        | Debug.Boundary (path, state, nested) ->
            equal "kind" "boundary"
            equal "path" path
            equal "state" state.State
            equal "reason" state.Reason
            children nested
        | Debug.Hole (path, nested) ->
            equal "kind" "hole"
            equal "path" path
            children nested
        | Debug.Leaving nested ->
            equal "kind" "leaving"
            check nested (property "node")
        | Debug.LeavingRow (key, nested) ->
            equal "kind" "leaving"
            equal "key" key
            check nested (property "node")
        | Debug.Truncated _ -> equal "kind" "truncated"

    if
        document.RootElement.GetProperty("schema").GetString()
        <> "ranvier-view-dump/1"
    then
        failwith "JSON schema differs"

    Array.iter2
        check
        dump.Root
        (document.RootElement.GetProperty("root").EnumerateArray()
         |> Seq.toArray)
#endif

#if !FABLE_COMPILER
let private observedNodes (target: Node) =
    let nodes = ResizeArray<INode>()
    nodes.Add ((query ()).Value :> INode)

    let rec visit (node: Node) =
        for probe in Inspect.instancesOf node do
            match probe with
            | Inspect.InstanceProbe (_, value, _, _) when
                not (isNull value)
                && Microsoft.FSharp.Reflection.FSharpType.IsRecord (value.GetType ())
                ->
                for field in Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields value do
                    match field with
                    | :? INode as source -> nodes.Add source
                    | _ -> ()
            | _ -> ()

        let mutable child = node.firstChild

        while child.IsSome do
            let current = unbox<Node> child.Value
            visit current
            child <- current.nextSibling

    visit target

    nodes
    |> Seq.distinctBy (fun node -> node.Id)
    |> Seq.toArray

let private observerState (node: INode) =
    let flags =
        System.Reflection.BindingFlags.Instance
        ||| System.Reflection.BindingFlags.Public
        ||| System.Reflection.BindingFlags.NonPublic

    let observers = node.GetType().GetField("observers", flags).GetValue node
    node.Id, node.Status, unbox<int>(observers.GetType().GetProperty("Count", flags).GetValue observers)
#endif

let private record (name: string) (graph: Graph) (target: HTMLElement) =
#if !FABLE_COMPILER
    let nodes = observedNodes target
    let before = Array.map observerState nodes
#endif
    let captured = Debug.captureNode options graph target
#if !FABLE_COMPILER
    checkJson captured
#endif
    Conformance.recordIfRunning name (Debug.textWith options captured)
    Conformance.recordIfRunning (name + " json") (Debug.json captured)
    Conformance.recordIfRunning (name + " html") target.innerHTML
#if !FABLE_COMPILER
    if Array.map observerState nodes <> before then
        failwith "capturing and formatting changed a fixture node's status or observer count"
#endif

let private mounted (view: Program.View) (check: Graph -> HTMLElement -> Mounting.Mounted -> unit) =
    use graph = new Graph ()
    (query ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())
    let target = host ()
    let mounted = mountTemplate false graph strict target view
    mounted.Flush ()

    try
        check graph target mounted
    finally
        mounted.Dispose ()

let private boardOf (target: HTMLElement) : Board =
    let rec find (node: Node) =
        match Inspect.probeOf node with
        | Some (Inspect.InstanceProbe (_, instance, _, _)) when (instance :? Board) -> Some (unbox<Board> instance)
        | _ ->
            let mutable child = node.firstChild
            let mutable found = None

            while found.IsNone && child.IsSome do
                found <- find (unbox<Node> child.Value)
                child <- (unbox<Node> child.Value).nextSibling

            found

    (find target).Value

let private boardDump () =
    mounted (Component.render board ()) (fun graph target mounted ->
        // Removing the second card leaves its row held by its exit.
        let state = boardOf target
        graph.Run (fun () -> state.Cards.Value <- items |> Array.filter (fun c -> c.Id <> 2))
        mounted.Flush ()
        record "board" graph target
        let captured = Debug.capture options mounted
        Conformance.recordIfRunning "mounted board" (Debug.text captured)
        Conformance.recordIfRunning "mounted board json" (Debug.json captured))

let private armDump () =
    mounted (Component.render board ()) (fun graph target mounted ->
        record "edit" graph target
        graph.Run (fun () -> (boardOf target).Editing.Value <- false)
        mounted.Flush ()
        record "view" graph target)

let private boundaryDump () =
    mounted (Component.render board ()) (fun graph target mounted ->
        record "pending" graph target
        graph.Run (fun () -> (query ()).Value.Settle "Loaded")
        mounted.Flush ()
        record "ready" graph target)

let private failedBoundaryDump () =
    mounted (Component.render board ()) (fun graph target mounted ->
        graph.Run (fun () -> (query ()).Value.Fail(exn "offline"))
        mounted.Flush ()
        record "failed" graph target)

type Holed = { Body: Program.View }

let private holed =
    Component.define
        (fun _ () ->
            {
                Body = Component.render viewPanel ignore
            })
        (fun t -> t.div [| t.Attrs.className "holder" |] [| t.view (fun (h: Holed) -> h.Body) |])

let private holeDump () =
    mounted (Component.render holed ()) (fun graph target _ -> record "hole" graph target)

let private headed =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.Document.title "Board (4)"
                t.Document.meta
                    [|
                        t.Attrs.attribute ("name", "description")
                        t.Attrs.attribute ("content", "Four cards")
                    |]
                t.text "page"
            |])

let private headDump () =
    mounted (Component.render headed ()) (fun graph target _ -> record "head" graph target)

type ValueMode =
    | Viewing
    | Editing of int

type MixedValues =
    {
        N: int
        Ints: int array
        Big: int64
        Mode: ValueMode
        Character: char
        Id: System.Guid
        Decimal: decimal
        Items: ResizeArray<int>
        Map: Map<string, int>
        Set: Set<int>
        List: int list
        Optional: int option
        Anonymous: obj
    }

let private valueDump () =
    let definition =
        Component.define
            (fun _ () ->
                {
                    N = 4
                    Ints = [| 1; 2; 3 |]
                    Big = 9007199254740993L
                    Mode = Editing 2
                    Character = 'x'
                    Id = System.Guid.Empty
                    Decimal = 1.5m
                    Items = ResizeArray [ 1; 2 ]
                    Map = Map.ofList [ "a", 1 ]
                    Set = Set.ofList [ 1; 2 ]
                    List = [ 1; 2 ]
                    Optional = Some 3
                    Anonymous = box {| Label = "anonymous" |}
                })
            (fun t -> t.p [||] [| t.text "values" |])

    mounted (Component.render definition ()) (fun graph target _ -> record "values" graph target)

let private heldRowsDump () =
    mounted (Component.render board ()) (fun graph target mounted ->
        let state = boardOf target
        graph.Run (fun () -> state.Cards.Value <- items[1..])
        mounted.Flush ()
        record "leading exit" graph target
        graph.Run (fun () -> state.Cards.Value <- [||])
        mounted.Flush ()
        record "empty held list" graph target)

type OpaqueDump() =
    member _.cases() : string array =
        failwith "called cases"

    member _.fields: obj array = failwith "read fields"
    member _.message: string = failwith "read message"

let private opaqueValue () : obj =
#if FABLE_COMPILER
    JsInterop.emitJsExpr
        ()
        "(() => { class OpaqueDump { cases() { throw Error('called cases'); } get message() { throw Error('read message'); } get head() { throw Error('read head'); } get [Symbol.iterator]() { throw Error('read iterator'); } } const value = new OpaqueDump(); value.fields = []; value.tag = 0; Object.defineProperty(value, 'constructor', {get() { throw Error('read constructor'); }}); return value; })()"
#else
    box (OpaqueDump ())
#endif

let private opaqueDump () =
    let definition =
        Component.define (fun _ () -> {| Value = opaqueValue () |}) (fun t -> t.p [||] [||])

    mounted (Component.render definition ()) (fun graph target _ ->
        let text = Debug.text (Debug.captureNode options graph target)

        if not (text.Contains "OpaqueDump") then
            failwith "opaque value lost its type name"

        record "opaque" graph target)

let benchmarkBoardHydration (now: unit -> float) =
    use graph = new Graph ()
    (query ()).Value <- graph.Run (fun () -> Api.createAsyncSource<string>())

    let body =
        Server.render
            { Server.Options.defaults with
                Policy = Omit
            }
            board
            ()

    let target = host ()
    target.innerHTML <- body
    let started = now ()

    let mounted =
        Hydration.hydrate graph None strict Hydration.HydrationOptions.defaults target board ()

    let elapsed = now () - started

    try
        if
            target.textContent
            <> "Cards: 4Write specReviewShipResteditLoading"
        then
            failwith "The hydrated Board differs from its fixture"

        elapsed
    finally
        mounted.Dispose ()

        target.parentNode
        |> Option.iter (fun parent -> parent.removeChild target |> ignore)

let cases: Conformance.Case array =
    Conformance.ofChecks
        [|
            "the board dump shows its instance, a list with a leaving row, the edit arm and a pending boundary", boardDump
            "an arm dump shows the selected key and definition before and after a switch", armDump
            "a boundary dump shows the pending and then the ready state", boundaryDump
            "a boundary dump shows the failed state with its error", failedBoundaryDump
            "a view hole dump shows the view mounted in the hole", holeDump
            "the head section lists the marked head elements", headDump
            "value kinds and values agree between both hosts", valueDump
            "held rows before live rows and after clearing remain visible", heldRowsDump
            "opaque values execute neither getters nor union-shaped methods", opaqueDump
        |]
#endif
