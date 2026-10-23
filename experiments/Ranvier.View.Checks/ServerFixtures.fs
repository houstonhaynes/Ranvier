/// <summary>Template fixtures shared by the server checks, the hydration checks and the first-content benchmarks.</summary>
module ServerFixtures

open global.Ranvier
open Ranvier.View

type GreetingProps = { Title: string; Name: string }

let greeting =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div
            [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |]
            [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |])

let greetingProps = { Title = "T &"; Name = "<Ada>" }

let named =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |] [| t.span [||] [| t.text (fun r -> r.Name) |] |])

let clicking =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.button [| t.Events.on ("click", fun _ _ -> ()) |] [||])

let nullTitle =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.div [| t.Attrs.title (fun _ -> (null: string)) |] [||])

type HoleProps = { Caption: string; Body: Program.View }

let holed =
    Component.define (fun _ (props: HoleProps) -> props) (fun t ->
        t.div [| t.Attrs.className "card" |] [| t.text (fun r -> r.Caption); t.view (fun r -> r.Body) |])

type Stage =
    | Waiting
    | Working of int
    | Complete of string

let waitingArm =
    Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.span [||] [| t.text "waiting" |])

let workingArm =
    Component.define (fun _ (progress: unit -> int) -> progress) (fun t ->
        t.span [||] [| t.text (fun progress -> "working " + string (progress ())) |])

let completeArm =
    Component.define (fun _ (result: unit -> string) -> result) (fun t -> t.span [||] [| t.text (fun result -> "complete " + result ()) |])

let staged =
    Component.define (fun _ (stage: Stage) -> stage) (fun t ->
        t.div
            [||]
            [|
                t.matchWith id (function
                    | Waiting -> t.arm waitingArm ()
                    | Working progress -> t.arm workingArm progress
                    | Complete result -> t.arm completeArm result)
            |])

let decadeArm =
    Component.define (fun _ (decade: unit -> int) -> decade) (fun t -> t.span [||] [| t.text (fun decade -> string (decade ())) |])

let decades =
    Component.define (fun _ (value: int) -> value) (fun t ->
        t.div [||] [| t.branch (fun value -> value / 10) (fun decade -> t.arm decadeArm decade) |])

type Listed = { Id: int; Text: string }

let listedRow =
    Component.define (fun _ (item: unit -> Listed) -> item) (fun t -> t.li [||] [| t.text (fun item -> (item ()).Text) |])

let listed =
    Component.define (fun _ (items: Listed array) -> items) (fun t -> t.ul [||] [| t.each (fun (item: Listed) -> item.Id) id listedRow |])

let listedItems = [| { Id = 1; Text = "a" }; { Id = 2; Text = "<b>" } |]

type BadgeProps = { Count: int }

let badge =
    Component.define (fun _ (props: BadgeProps) -> props) (fun t ->
        t.span [| t.Attrs.title (fun badge -> string badge.Count) |] [| t.text (fun badge -> string badge.Count) |])

let carded =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.div
            [||]
            [|
                t.text (fun r -> r.Name)
                t.render badge (fun r -> { Count = r.Title.Length })
            |])

let textual =
    Component.define (fun _ (value: string) -> value) (fun t -> t.text (fun value -> value))

let besideText =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t -> t.span [||] [| t.text "a"; t.render textual (fun r -> r.Name) |])

type Flags =
    {
        Name: string
        Nickname: string option
        Busy: bool
        Expanded: bool option
        State: string
    }

let flagsTemplate =
    Component.define (fun _ (props: Flags) -> props) (fun t ->
        t.button
            [|
                t.Attrs.title (fun r -> r.Name)
                t.Attrs.id (fun r -> r.Nickname)
                t.Attrs.Button.disabled (fun r -> r.Busy)
                t.Attrs.ariaExpanded (fun r -> r.Expanded)
                t.Attrs.ariaBusy (fun r -> r.Busy)
                t.Attrs.data ("state", fun r -> Some r.State)
            |]
            [| t.text "go" |])

let flagCases =
    [
        {
            Name = "a"
            Nickname = None
            Busy = false
            Expanded = None
            State = "x"
        }
        {
            Name = "b"
            Nickname = Some "n"
            Busy = true
            Expanded = Some false
            State = "y"
        }
        {
            Name = "c"
            Nickname = Some "m"
            Busy = false
            Expanded = Some true
            State = "z"
        }
        {
            Name = null
            Nickname = None
            Busy = false
            Expanded = None
            State = "n"
        }
    ]

let staticTemplate =
    Component.define (fun _ () -> ()) (fun t ->
        t.div
            [||]
            [|
                t.button
                    [|
                        t.Attrs.Button.disabled false
                        t.Attrs.ariaExpanded false
                        t.Attrs.id (None: string option)
                        t.Attrs.title "t"
                    |]
                    [||]
                t.button
                    [|
                        t.Attrs.Button.disabled true
                        t.Attrs.ariaExpanded true
                        t.Attrs.id (Some "i")
                        t.Attrs.data ("k", "v")
                    |]
                    [||]
            |])

let pair =
    Component.define (fun _ (props: GreetingProps) -> props) (fun t ->
        t.fragment
            [|
                t.div
                    [| t.Attrs.className "panel"; t.Attrs.title (fun r -> r.Title) |]
                    [| t.text "Hello "; t.text (fun r -> r.Name); t.text (fun _ -> "!") |]
                t.span [||] [| t.text (fun r -> r.Name) |]
            |])

/// <summary>The source the <c>guarded</c> fixture's content reads; assign it inside a graph before rendering.</summary>
let mutable boundarySource: AsyncSource<string> = Unchecked.defaultof<_>

let boundaryPending =
    Component.define (fun _ () -> ()) (fun t -> t.span [||] [| t.text "loading" |])

let boundaryFailed =
    Component.define (fun _ (failure: BoundaryFailure) -> failure) (fun t -> t.span [||] [| t.text (fun failure -> failure.Error.Message) |])

let boundaryContent =
    Component.define (fun _ (status: BoundaryStatus) -> status) (fun t -> t.span [||] [| t.text (fun _ -> boundarySource.Value) |])

let guarded =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.boundary boundaryPending boundaryFailed boundaryContent |])

let emptyText =
    Component.define (fun _ (value: string) -> value) (fun t -> t.span [||] [| t.text (fun value -> value) |])

/// <summary>A row whose only root is a text slot.</summary>
let textRow =
    Component.define (fun _ (item: unit -> string) -> item) (fun t -> t.text (fun item -> item ()))

/// <summary>A row with no roots.</summary>
let emptyRow =
    Component.define (fun _ (_: unit -> string) -> ()) (fun t -> t.fragment [||])

let voidChildren =
    Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.element (HtmlTag "br") [||] [| t.text "x" |] |])

let rowInTable =
    // A fixture for the server render's content-model refusal.
    // fsharpanalyzer: ignore-line-next RAN024
    Component.define (fun _ () -> ()) (fun t -> t.table [||] [| t.tr [||] [| t.td [||] [| t.text "x" |] |] |])

type ComponentLine =
    {
        Index: int
        Label: Signal<string>
        Clicks: Signal<int>
    }

/// <summary>The row of the framework comparison's component workload.</summary>
let componentRow =
    Component.define
        (fun _ (item: unit -> int) ->
            let index = item ()

            {
                Index = index
                Label = Api.createSignal ("row " + string index)
                Clicks = Api.createSignal 0
            })
        (fun t ->
            t.div
                [|
                    t.Attrs.id (fun r -> "row-" + string r.Index)
                    t.Attrs.className "panel"
                    t.Attrs.title (t.from (fun r -> r.Label))
                |]
                [|
                    t.input [| t.Props.Input.value (t.from (fun r -> r.Label)) |]
                    t.button [| t.Events.onClick (fun r _ -> r.Clicks.Value <- r.Clicks.Value + 1) |] [| t.text (fun r -> string r.Clicks.Value) |]
                |])

let componentPage =
    Component.define (fun _ (rows: int) -> Array.init rows (fun index -> index + 1)) (fun t ->
        t.element (HtmlTag "section") [||] [| t.each id id componentRow |])

type KeyedLine = { Id: int; Label: string }

/// <summary>The row of the js-framework-benchmark keyed workload.</summary>
let keyedRow =
    Component.define (fun _ (item: unit -> KeyedLine) -> item ()) (fun t ->
        t.tr
            [||]
            [|
                t.td [| t.Attrs.className "col-md-1" |] [| t.text (fun (row: KeyedLine) -> string row.Id) |]
                t.td
                    [| t.Attrs.className "col-md-4" |]
                    [|
                        t.a [| t.Events.onClick (fun _ _ -> ()) |] [| t.text (fun (row: KeyedLine) -> row.Label) |]
                    |]
                t.td
                    [| t.Attrs.className "col-md-1" |]
                    [|
                        t.a
                            [| t.Events.onClick (fun _ _ -> ()) |]
                            [|
                                t.span
                                    [|
                                        t.Attrs.className "glyphicon glyphicon-remove"
                                        t.Attrs.attribute ("aria-hidden", "true")
                                    |]
                                    [||]
                            |]
                    |]
                t.td [| t.Attrs.className "col-md-6" |] [||]
            |])

let keyedPage =
    Component.define
        (fun _ (rows: int) ->
            Array.init rows (fun index ->
                {
                    Id = index + 1
                    Label = "row " + string (index + 1)
                }))
        (fun t -> t.table [| t.Attrs.className "table" |] [| t.tbody [||] [| t.each (fun (row: KeyedLine) -> row.Id) id keyedRow |] |])

let renderOmit (definition: Component<'props>) (props: 'props) =
    Html.render Omit (Program.run (Component.render definition props))

/// <summary>The first content of the component workload at <c>rows</c> rows, rendered as one request.</summary>
let renderComponentPage (rows: int) : string =
    Server.render
        { Server.Options.defaults with
            Policy = Omit
        }
        componentPage
        rows

/// <summary>The first content of the keyed workload at <c>rows</c> rows, rendered as one request.</summary>
let renderKeyedPage (rows: int) : string =
    Server.render
        { Server.Options.defaults with
            Policy = Omit
        }
        keyedPage
        rows

#if !FABLE_COMPILER
/// <summary>A string log per thread, for checks that run on parallel threads.</summary>
[<Sealed>]
type ThreadLog() =
    let logs =
        new System.Threading.ThreadLocal<ResizeArray<string>> (fun () -> ResizeArray<string>())

    member _.Add(item: string) =
        logs.Value.Add item

    member _.Clear() =
        logs.Value.Clear ()

    interface System.Collections.Generic.IEnumerable<string> with
        member _.GetEnumerator() : System.Collections.Generic.IEnumerator<string> =
            (logs.Value :> seq<string>).GetEnumerator()

        member _.GetEnumerator() : System.Collections.IEnumerator =
            (logs.Value :> System.Collections.IEnumerable).GetEnumerator()
#endif

/// <summary>The ids issued while the identity fixtures set up, in setup order.</summary>
#if FABLE_COMPILER
let identityLog = ResizeArray<string>()
#else
let identityLog = ThreadLog ()
#endif

/// <summary>An identity fixture's instance: the first id its setup issued, shown as the element's id, and its props.</summary>
[<Sealed>]
type Issued<'props>(id: string, props: 'props) =
    member _.Id = id
    member _.Props = props

let private issue (setup: Setup) (props: 'props) =
    let id = setup.CreateId ()
    identityLog.Add id
    Issued (id, props)

let idLeaf =
    Component.define (fun setup () -> issue setup ()) (fun t -> t.i [| t.Attrs.id (fun (r: Issued<unit>) -> r.Id) |] [||])

let idCell =
    Component.define (fun setup (item: unit -> int) -> issue setup item) (fun t ->
        t.b [| t.Attrs.id (fun r -> r.Id) |] [| t.text (fun r -> string (r.Props ())) |])

let idRow =
    Component.define (fun setup (item: unit -> int) -> issue setup item) (fun t ->
        t.li [| t.Attrs.id (fun r -> r.Id) |] [| t.render idLeaf ignore; t.each id (fun r -> [| r.Props () * 10 |]) idCell |])

let idArm =
    Component.define (fun setup (count: unit -> int) -> issue setup count) (fun t ->
        t.span [| t.Attrs.id (fun r -> r.Id) |] [| t.text (fun r -> string (r.Props ())) |])

let identified =
    Component.define
        (fun setup (items: int array) ->
            let issued = issue setup items
            identityLog.Add (setup.CreateId ())
            issued)
        (fun t ->
            t.div
                [| t.Attrs.id (fun r -> r.Id) |]
                [|
                    t.render idLeaf ignore
                    t.ul [||] [| t.each id (fun r -> r.Props) idRow |]
                    t.render idLeaf ignore
                    t.matchWith (fun r -> r.Props.Length) (fun count -> t.arm idArm count)
                |])

/// <summary>The ids <c>identified</c> issues for the items 7 and 8 under <c>prefix</c>, sorted.</summary>
let identityIds (prefix: string) =
    [
        "0-0"
        "0-1"
        "0.c0-0"
        "0.c1-0"
        "0.r0.0-0"
        "0.r0.0.c0-0"
        "0.r0.0.r0.0-0"
        "0.r0.1-0"
        "0.r0.1.c0-0"
        "0.r0.1.r0.0-0"
        "0.r1.0-0"
    ]
    |> List.map (fun id -> prefix + "-" + id)
    |> List.sort

/// <summary>A view hole whose view renders <c>idLeaf</c> twice.</summary>
let idHole =
    Component.define (fun setup () -> issue setup ()) (fun t ->
        t.div
            [| t.Attrs.id (fun r -> r.Id) |]
            [|
                t.view (fun _ -> Program.fragment [| Component.render idLeaf (); Component.render idLeaf () |])
            |])

/// <summary>The ids <c>idHole</c> issues, in document order: the view's first template takes the row's position.</summary>
let holeIds = [ "r-0-0"; "r-0.r0.0-0"; "r-0.r0.0.a0-0" ]

/// <summary>
/// A parent whose setup keeps its <c>Setup</c>, and whose inlined child calls the kept <c>CreateId</c> during its own
/// setup. Each call builds new definitions.
/// </summary>
let capturedCreateId () : Component<unit> =
    let captured: Setup option ref = ref None

    let caller =
        Component.define (fun _ () -> captured.Value.Value.CreateId () |> ignore) (fun t -> t.span [||] [||])

    Component.define (fun setup () -> captured.Value <- Some setup) (fun t -> t.div [||] [| t.render caller ignore |])

/// <summary>An inlined child that inlines <c>idLeaf</c> and owns a list of <c>idCell</c> rows.</summary>
let idBranch =
    Component.define (fun setup (items: int array) -> issue setup items) (fun t ->
        t.span [| t.Attrs.id (fun r -> r.Id) |] [| t.render idLeaf ignore; t.each id (fun r -> r.Props) idCell |])

/// <summary><c>idBranch</c> inlined before a list of the instance's own.</summary>
let idNested =
    Component.define (fun setup (items: int array) -> issue setup items) (fun t ->
        t.div
            [| t.Attrs.id (fun r -> r.Id) |]
            [|
                t.render idBranch (fun r -> r.Props)
                t.div [||] [| t.each id (fun r -> r.Props) idCell |]
            |])

/// <summary>
/// The ids <c>idNested</c> issues for the items 1 and 2, in document order: the inlined children are numbered in
/// pre-order, and the inlined child's rows sit under its own path.
/// </summary>
let nestedIds =
    [
        "r-0-0"
        "r-0.c0-0"
        "r-0.c1-0"
        "r-0.c0.r0.0-0"
        "r-0.c0.r0.1-0"
        "r-0.r0.0-0"
        "r-0.r0.1-0"
    ]

/// <summary>Two view holes, each rendering <c>idLeaf</c> twice.</summary>
let idPair =
    let pair =
        Program.fragment [| Component.render idLeaf (); Component.render idLeaf () |]

    Component.define (fun setup () -> issue setup ()) (fun t ->
        t.div [| t.Attrs.id (fun r -> r.Id) |] [| t.view (fun _ -> pair); t.view (fun _ -> pair) |])

/// <summary>A view hole rendering <c>idLeaf</c>, then <c>idPair</c>, then <c>idLeaf</c>.</summary>
let idHoles =
    Component.define (fun setup () -> issue setup ()) (fun t ->
        t.div
            [| t.Attrs.id (fun r -> r.Id) |]
            [|
                t.view (fun _ ->
                    Program.fragment
                        [|
                            Component.render idLeaf ()
                            Component.render idPair ()
                            Component.render idLeaf ()
                        |])
            |])

/// <summary>
/// The ids <c>idHoles</c> issues, in document order: each view row counts its authored steps from 0, and the outer
/// row's count resumes after the inner rows.
/// </summary>
let holesIds =
    [
        "r-0-0"
        "r-0.r0.0-0"
        "r-0.r0.0.a0-0"
        "r-0.r0.0.a0.r0.0-0"
        "r-0.r0.0.a0.r0.0.a0-0"
        "r-0.r0.0.a0.r1.0-0"
        "r-0.r0.0.a0.r1.0.a0-0"
        "r-0.r0.0.a1-0"
    ]

/// <summary>Whether the content of <c>idBoundary</c> settles its source during setup; otherwise the source stays pending.</summary>
let mutable idContentReady = true

/// <summary>Whether the content of <c>idBoundary</c> fails its source during setup, whatever <c>idContentReady</c> holds.</summary>
let mutable idContentFails = false

let private idPending =
    Component.define (fun setup () -> issue setup ()) (fun t -> t.i [| t.Attrs.id (fun (r: Issued<unit>) -> r.Id) |] [||])

let private idFailed =
    Component.define (fun setup (failure: BoundaryFailure) -> issue setup failure) (fun t -> t.span [| t.Attrs.id (fun r -> r.Id) |] [||])

let private idContent =
    Component.define
        (fun setup (_: BoundaryStatus) ->
            let source = Api.createAsyncSource<string>()

            if idContentFails then
                source.Fail (System.Exception "failed")
            elif idContentReady then
                source.Settle "ready"

            issue setup source)
        (fun t -> t.b [| t.Attrs.id (fun r -> r.Id) |] [| t.text (fun r -> r.Props.Value) |])

/// <summary>A boundary whose content, pending view and error view each show the id their setup issued.</summary>
let idBoundary =
    Component.define (fun setup () -> issue setup ()) (fun t -> t.div [| t.Attrs.id (fun r -> r.Id) |] [| t.boundary idPending idFailed idContent |])

/// <summary>The ids <c>idBoundary</c> issues in setup order when its content is ready: the content takes row 0.</summary>
let boundaryContentIds = [ "r-0-0"; "r-0.r0.0-0" ]

/// <summary>The ids <c>idBoundary</c> issues in setup order when its content is pending: the pending view takes row 1.</summary>
let boundaryPendingIds = [ "r-0-0"; "r-0.r0.0-0"; "r-0.r0.1-0" ]

/// <summary>The ids <c>idBoundary</c> issues in setup order when its content fails: the error view takes row 1.</summary>
let boundaryErrorIds = [ "r-0-0"; "r-0.r0.0-0"; "r-0.r0.1-0" ]
