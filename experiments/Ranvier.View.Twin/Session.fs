namespace Ranvier.View.Twin

open System
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Dom.Scheduling

[<RequireQualifiedAccess>]
type Commits =
    | Synchronous
    | Queued of CommitFailure

/// <summary>What a session does with the failures the mount passes to <c>report</c>.</summary>
[<RequireQualifiedAccess>]
type Reports =
    /// <summary>Collects them in <c>Session.Reports</c>.</summary>
    | Collect
    /// <summary>Raises <c>TwinReportException</c> from the call during which they arrived.</summary>
    | Raise

/// <summary>The failures a mount reported during one call under <c>Reports.Raise</c>.</summary>
exception TwinReportException of reports: exn list list with
    override this.Message =
        "The mount reported: "
        + String.Join (
            "; ",
            this.reports
            |> List.concat
            |> List.map (fun e -> e.Message)
        )

type TwinMount =
    {
        Commits: Commits
        Reports: Reports
        GraphOptions: GraphOptions
        /// <summary>The host element's HTML; the view mounts at its end.</summary>
        Host: string
        /// <summary>The head's HTML before the mount.</summary>
        Head: string
    }

module TwinMount =
    let defaults =
        {
            Commits = Commits.Synchronous
            Reports = Reports.Raise
            GraphOptions =
                { GraphOptions.Default with
                    Dispatcher = Some (ManualDispatcher () :> IGraphDispatcher)
                }
            Host = "<div></div>"
            Head = ""
        }

[<Sealed>]
type Session internal (host: TwinHost, element: TwinElement, graph: Graph, policy: Reports) =
    let collected = ResizeArray<exn list>()
    let pending = ResizeArray<exn list>()
    let mutable mounted: Mounting.Mounted = Unchecked.defaultof<_>

    member internal _.Report(errors: exn list) =
        match policy with
        | Reports.Collect -> collected.Add errors
        | Reports.Raise -> pending.Add errors

    member internal _.Attach(value: Mounting.Mounted) =
        mounted <- value

    member internal _.RaisePending() =
        if pending.Count > 0 then
            let reports = List.ofSeq pending
            pending.Clear ()
            raise (TwinReportException reports)

    member internal this.AfterDispatch() =
        host.Clock.Settle graph

        match policy with
        | Reports.Collect ->
            for error in host.Document.ListenerErrors do
                collected.Add [ error ]

            host.Document.ListenerErrors.Clear ()
        | Reports.Raise -> ()

        this.RaisePending ()

    member _.Document = host.Document
    member _.Host: Fable.Core.TS.Dom.HTMLElement = unbox element.Native
    member _.Graph = graph
    member _.Clock = host.Clock
    member _.Mounted = mounted
    member _.Reports: exn list list = List.ofSeq collected

    /// <summary>The host's children in jsdom's serialisation, markers included.</summary>
    member _.Html() =
        Html.inner element

    member _.Text() =
        element.TextContent

    /// <summary>The text dump of the mounted view under <c>options</c>, by default <c>Debug.DumpOptions.defaults</c>.</summary>
    member _.Dump(?options: Debug.DumpOptions) =
        let options = defaultArg options Debug.DumpOptions.defaults
        Debug.textWith options (Debug.capture options mounted)

    /// <summary>Runs <c>action</c> inside the graph and settles the clock.</summary>
    member this.Act(action: unit -> unit) =
        graph.Run action
        host.Clock.Settle graph
        this.RaisePending ()

    /// <summary>Advances the clock by <c>milliseconds</c>, running the timers and frames that fall due, and settles it.</summary>
    member this.AdvanceBy(milliseconds: float) =
        host.Clock.AdvanceBy milliseconds
        host.Clock.Settle graph
        this.RaisePending ()

    interface IDisposable with
        member _.Dispose() =
            try
                try
                    if not (isNull (box mounted)) then
                        mounted.Dispose ()
                finally
                    (graph :> IDisposable).Dispose()
            finally
                (host.Document :> IDisposable).Dispose()

[<RequireQualifiedAccess>]
module Twin =
    let private start (options: TwinMount) =
        let host = TwinHost.Create ()

        try
            Html.parseInto host.Document.Head options.Head
            Html.parseInto host.Document.Body options.Host

            let element =
                match
                    host.Document.Body.Children
                    |> List.tryFind (fun n -> n :? TwinElement)
                with
                | Some e -> e :?> TwinElement
                | None -> invalidArg "options" "TwinMount.Host must hold an element"

            let graph = new Graph (options.GraphOptions)
            let session = new Session (host, element, graph, options.Reports)
            host.Document.AfterDispatch <- session.AfterDispatch
            host.Document.RaiseListenerErrors <- (options.Reports = Reports.Raise)
            host, element, graph, session
        with _ ->
            (host.Document :> IDisposable).Dispose()
            reraise ()

    /// <summary>Mounts authored content through <c>Mounting.mountWith</c>.</summary>
    let mountView (options: TwinMount) (build: Mounting.BuildContext -> unit) : Session =
        let host, element, graph, session = start options

        try
            let mode =
                match options.Commits with
                | Commits.Synchronous -> SynchronousCommits
                | Commits.Queued failure -> QueuedCommits (failure, host.Clock.Post)

            session.Attach (Mounting.mountWith mode graph None session.Report element.Native None build)
            session.RaisePending ()
            session
        with _ ->
            (session :> IDisposable).Dispose()
            reraise ()

    let mountWith (options: TwinMount) (definition: Component<'props>) (props: 'props) : Session =
        mountView options (fun context -> Program.run (Component.render definition props) context.Backend)

    let mount (definition: Component<'props>) (props: 'props) : Session =
        mountWith TwinMount.defaults definition props

    /// <summary>
    /// Parses the server HTML <c>body</c> into the host, calls <c>parsed</c> with the host element, and hydrates it with
    /// <c>Hydration.hydrateWith</c>. Queued commits raise <c>NotSupportedException</c>: hydration commits synchronously.
    /// </summary>
    let hydrateHtmlWith
        (options: TwinMount)
        (hydration: Hydration.HydrationOptions)
        (body: string)
        (parsed: Fable.Core.TS.Dom.HTMLElement -> unit)
        (definition: Component<'props>)
        (props: 'props)
        : Session =
        match options.Commits with
        | Commits.Queued _ -> raise (NotSupportedException "Hydration supports synchronous commits only")
        | Commits.Synchronous -> ()

        let _, element, graph, session = start options

        try
            Html.parseInto element body
            parsed (unbox element.Native)
            session.Attach (Hydration.hydrateWith SynchronousCommits graph None session.Report hydration element.Native definition props)
            session.RaisePending ()
            session
        with _ ->
            (session :> IDisposable).Dispose()
            reraise ()

    /// <summary>Parses the server HTML <c>body</c> into the host and hydrates it with <c>Hydration.hydrateWith</c>.</summary>
    let hydrateHtml options hydration body (definition: Component<'props>) (props: 'props) : Session =
        hydrateHtmlWith options hydration body ignore definition props

    /// <summary>
    /// Renders with <c>Server.renderPage</c>, writes the body into the host and the head into the document, and hydrates
    /// with <c>Hydration.hydrateWith SynchronousCommits</c>.
    /// </summary>
    let hydrate (server: Server.Options) (hydration: Hydration.HydrationOptions) (definition: Component<'props>) (props: 'props) : Session =
        let page = Server.renderPage server definition props

        hydrateHtml
            { TwinMount.defaults with
                Head = page.Head
            }
            hydration
            page.Body
            definition
            props
