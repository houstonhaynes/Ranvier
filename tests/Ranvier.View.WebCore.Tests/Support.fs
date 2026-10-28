module Ranvier.View.WebCore.Tests.Support

open System
open System.Collections.Concurrent
open System.Runtime.ExceptionServices
open System.Threading
open Expecto
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.WebCore

// WebCore accepts calls only on the thread that first created a host, so every test body runs on this one.
let private work = new BlockingCollection<unit -> unit> ()

let private thread =
    lazy
        (let worker =
            Thread (
                (fun () ->
                    for item in work.GetConsumingEnumerable () do
                        item ()),
                64 * 1024 * 1024
            )

         worker.IsBackground <- true
         worker.Name <- "WebCore host"
         worker.Start ()
         worker)

/// <summary>Runs <c>body</c> on the WebCore host thread and returns its result or rethrows its exception.</summary>
let onHostThread (body: unit -> 'T) : 'T =
    thread.Force () |> ignore
    let mutable result = Unchecked.defaultof<'T>
    let mutable failure: ExceptionDispatchInfo = null
    use finished = new ManualResetEventSlim ()

    work.Add (fun () ->
        try
            try
                result <- body ()
            with error ->
                failure <- ExceptionDispatchInfo.Capture error
        finally
            finished.Set ())

    finished.Wait ()

    if not (isNull failure) then
        failure.Throw ()

    result

/// <summary>A test case whose body runs on the host thread against a new page; the page must end with no errors.</summary>
let hostCase (name: string) (body: WebCoreHost -> unit) =
    testCase name (fun () ->
        onHostThread (fun () ->
            use host = WebCoreHost.Create (320, 240)
            body host
            Expect.isEmpty (List.ofSeq host.Errors) "listener, microtask and timer errors"))

/// <summary>A mount of authored content into a new <c>div</c> at the end of the body; disposal leaves the graph.</summary>
type Mount =
    {
        Graph: Graph
        Mounted: Mounting.Mounted
        Container: HTMLElement
    }

    member this.Html = this.Container.innerHTML

    member this.Text: string = this.Container.textContent

    /// <summary>Runs <c>action</c> in the graph, then a loop turn, as an app's event would.</summary>
    member this.Act(host: WebCoreHost, action: unit -> unit) =
        this.Graph.Run action
        host.Cycle ()

    interface IDisposable with
        member this.Dispose() =
            this.Graph.Run (fun () -> this.Mounted.Dispose ())

let mountView (host: WebCoreHost) (graph: Graph) (build: Mounting.BuildContext -> unit) : Mount =
    let container = host.Document.createElement "div"
    host.Document.body.appendChild container |> ignore

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "The mount reported %A" errors) container None build

    {
        Graph = graph
        Mounted = mounted
        Container = container
    }

let mount (host: WebCoreHost) (graph: Graph) (definition: Component<'props>) (props: 'props) : Mount =
    mountView host graph (fun context -> Program.run (Component.render definition props) context.Backend)

/// <summary>The bounds of an element in CSS pixels of the viewport.</summary>
let rectOf (element: Element) =
    let mutable x = 0.0
    let mutable y = 0.0
    let mutable width = 0.0
    let mutable height = 0.0

    RanvierWebCoreAbi.Native.rv_element_rect ((box element :?> NativeObject).Handle, &x, &y, &width, &height)
    |> Interop.check "rv_element_rect"

    x, y, width, height

/// <summary>Clicks the centre of an element through native mouse input.</summary>
let clickOn (host: WebCoreHost) (element: Element) =
    let x, y, width, height = rectOf element

    host.Click (int (x + width / 2.0), int (y + height / 2.0))
    |> ignore
