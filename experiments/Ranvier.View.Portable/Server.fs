/// <summary>Server rendering of a template component as one request with its own graph and owners.</summary>
module Ranvier.View.Foundation.Server

open System
open global.Ranvier
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

/// <summary>The settings of one server render.</summary>
type Options =
    {
        /// <summary>Whether a browser-only part raises (<c>Reject</c>) or is dropped (<c>Omit</c>).</summary>
        Policy: BrowserPolicy
        /// <summary>The root prefix of the ids <c>Setup.CreateId</c> returns; must match the client's.</summary>
        IdPrefix: string
        /// <summary>The options of the request graph.</summary>
        GraphOptions: GraphOptions
    }

module Options =
    let defaults =
        {
            Policy = Reject
            IdPrefix = "r"
            GraphOptions = GraphOptions.Default
        }

/// <summary>The failure of a render whose read outside every boundary is pending; <c>Pending</c> is the core's <c>NotReadyException</c>.</summary>
exception ServerPendingException of Pending: exn with
    override _.Message = "A read outside every boundary is pending"

/// <summary>A <c>ServerPendingException</c> whose <c>Message</c> is the override's text on .NET and under Fable.</summary>
let private serverPending (pending: exn) : exn =
    let error = ServerPendingException pending
#if FABLE_COMPILER
    // Fable reads Message from the JavaScript error's message property.
    Fable.Core.JsInterop.(?<-) error "message" "A read outside every boundary is pending"
#endif
    error

/// <summary>
/// The owners of a request: each nested instance runs under its own owner, a root under <c>owner</c>; <c>lifetime</c>
/// reports its teardown errors. The instance's <c>Setup</c> registers cleanups on that owner until the instance is
/// written. <c>lifetime</c> is the request root or the innermost boundary attempt, and <c>owner</c> is its owner or an
/// owner it adopted. A failure to dispose an abandoned attempt, and each cleanup failure of an authored component under
/// the scope, goes to <c>report</c>.
/// </summary>
/// <remarks>
/// <c>Nest</c> and <c>Attempt</c> run only while the request graph is ambient, inside the <c>graph.Run</c> of
/// <c>renderOn</c>.
/// </remarks>
[<Sealed>]
type internal RequestScope(lifetime: MountLifetime, owner: Owner, identity: IdentityScope, report: exn -> unit) =
    inherit ServerScope()
    let mutable completed = false

    // The Setup of the instance owned by this scope: OnCleanup registers on `owner`, and raises after Complete.
    let setup =
        {
            OnCleanup =
                fun cleanup ->
                    if completed then
                        raise (InvalidOperationException "Server component setup is completed")

                    if owner.IsDisposed then
                        raise (InvalidOperationException "Renderer scope is disposed")

                    owner.OnCleanup (Action cleanup)
            CreateId = identity.CreateId
        }

    new(lifetime: MountLifetime, identity: IdentityScope, report: exn -> unit) = RequestScope (lifetime, lifetime.Owner, identity, report)

    override _.Nest body =
        if owner.IsDisposed then
            raise (InvalidOperationException "Renderer scope is disposed")

        Api.runWithOwner owner (fun () ->
            Api.createRoot (fun child ->
                lifetime.Adopt child
                let scope = RequestScope (lifetime, child, identity, report)

                try
                    body scope scope.InstanceSetup
                finally
                    scope.Complete ()))

    member private _.InstanceSetup = setup

    member private _.Complete() =
        completed <- true

    override _.Attempt body =
        let attempt: MountLifetime ref = ref null

        try
            lifetime.ChildUnder (
                owner,
                ignore,
                fun child ->
                    attempt.Value <- child
                    body (RequestScope (child, child.Owner, identity, report))
            )
            |> ignore
        with _ ->
            if not (isNull attempt.Value) then
                try
                    attempt.Value.Dispose ()
                with disposal ->
                    report disposal

            reraise ()

    override _.TakeCleanupFailures errors =
        for error in errors do
            report error

        true

/// <summary>
/// Renders <c>definition</c> into <c>sink</c> inside <c>graph</c> under a new request root, and disposes the root before
/// returning or raising; a <c>NotReadyException</c> raised by the render is raised as <c>ServerPendingException</c>.
/// Document entries go to <c>heads</c>; a null <c>heads</c> applies the policy to them.
/// </summary>
let internal renderOn
    (graph: Graph)
    (heads: HeadCollector)
    (options: Options)
    (definition: Component<'props>)
    (props: 'props)
    (sink: HtmlSink)
    : unit =
    let cleanupErrors = ResizeArray<exn>()

    let failure =
        graph.Run (fun () ->
            Api.untrack (fun () ->
                let root =
                    MountLifetime.Root (graph, (fun errors -> cleanupErrors.AddRange errors), ignore)

                let failure =
                    try
                        try
                            let backend = HtmlBackend (options.Policy, sink)
                            backend.Heads <- heads
                            backend.Identity <- IdentityScope options.IdPrefix
                            let writer = ServerWriter backend
                            backend.Writer <- writer
                            sink.Append "<!--[r-->"

                            writer.Write (
                                RequestScope (root, backend.Identity, cleanupErrors.Add),
                                definition,
                                box props,
                                null,
                                InstanceStep.Root,
                                0,
                                0
                            )

                            sink.Append "<!--]-->"
                            None
                        with
                        | NotReadyException _ as pending -> Some (serverPending pending)
                        | error -> Some error
                    finally
                        try
                            root.Dispose ()
                        with error ->
                            cleanupErrors.Add error

                failure))

    let aggregate (summary: string) (errors: exn list) =
        AggregateException (
            summary
            + ": "
            + String.concat "; " (errors |> List.map _.Message),
            List.toArray errors
        )

    match failure, List.ofSeq cleanupErrors with
    | None, [] -> ()
    | Some error, [] ->
#if FABLE_COMPILER
        raise error
#else
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw error
#endif
    | Some error, errors -> raise (aggregate "Server render failed" (error :: errors))
    | None, errors -> raise (aggregate "Server render cleanup failed" errors)

/// <summary>
/// Renders a template component as one hydratable root; raises <c>ServerPendingException</c> when a read outside every
/// boundary is pending, and the read's failure when it fails.
/// </summary>
/// <remarks>
/// <para>
/// The output is <c>&lt;!--[r--&gt;</c>, the instance, <c>&lt;!--]--&gt;</c>. The render runs untracked in a new graph
/// built from <c>options.GraphOptions</c>; every instance runs under its own owner of the request root, and the root
/// and the graph are disposed before <c>render</c> returns or raises.
/// </para>
/// <para>
/// A signal of a graph outside the request is read as it is. A write to it from the request thread raises under
/// <c>ThreadAffinity.Guarded</c> and changes state shared with every other reader under <c>ThreadAffinity.Unchecked</c>.
/// Under Fable <c>render</c> is synchronous only.
/// </para>
/// </remarks>
/// <exception cref="T:Ranvier.View.Foundation.Server.ServerPendingException">
/// A read outside every boundary is pending, and the request's cleanup succeeds.
/// </exception>
/// <exception cref="T:System.AggregateException">
/// The request's cleanup fails. The inner exceptions are the render's failure, when it failed, then the cleanup's.
/// </exception>
let render (options: Options) (definition: Component<'props>) (props: 'props) : string =
    let sink = HtmlSink ()
    use graph = new Graph (options.GraphOptions)
    renderOn graph null options definition props sink
    sink.ToString ()

/// <summary>Renders as <c>render</c> does, then passes the output's segments to <c>write</c> in order; a failed render writes nothing.</summary>
/// <exception cref="T:Ranvier.View.Foundation.Server.ServerPendingException">
/// A read outside every boundary is pending, and the request's cleanup succeeds.
/// </exception>
/// <exception cref="T:System.AggregateException">
/// The request's cleanup fails. The inner exceptions are the render's failure, when it failed, then the cleanup's.
/// </exception>
let renderTo (options: Options) (definition: Component<'props>) (props: 'props) (write: string -> unit) : unit =
    let sink = HtmlSink ()

    do
        use graph = new Graph (options.GraphOptions)
        renderOn graph null options definition props sink

    sink.Segments write

/// <summary>
/// A <c>&lt;script type="application/json"&gt;</c> element with the escaped <c>id</c>, holding <c>json</c> with each
/// <c>&lt;</c>, U+2028 and U+2029 written as its six-character JSON escape.
/// </summary>
/// <remarks>
/// The parsed element's text runs to its own end tag, and for valid JSON, <c>JSON.parse</c> of its <c>textContent</c>
/// returns the value <c>json</c> encodes. <c>Hydration.readState</c> reads the text back.
/// </remarks>
let stateScript (id: string) (json: string) : string =
    let escaped =
        json.Replace("<", "\\u003c").Replace(string (char 0x2028), "\\u2028").Replace(string (char 0x2029), "\\u2029")

    "<script type=\"application/json\" id=\""
    + Html.escape true id
    + "\">"
    + escaped
    + "</script>"

/// <summary>A rendered page: the body as <c>render</c> returns it, and the document head entries' HTML.</summary>
type Page = { Body: string; Head: string }

/// <summary>Renders as <c>render</c> does, and collects the request's document head entries whatever the policy.</summary>
/// <remarks>
/// <para>
/// Entries activate in the order of a client mount built in one synchronous call with <c>SynchronousCommits</c>. The
/// head lists one element per entry key in the order the keys first activated, a key's element being its last
/// activated entry, and one element per unkeyed link. A title is <c>&lt;title&gt;</c> with escaped text; a meta or
/// link is a void element with its static attributes, then its attribute slots, each in definition order. A slot
/// returning <c>None</c> is omitted.
/// </para>
/// <para>
/// Every element carries <c>data-ranvier-head</c> with its key, the empty string for an unkeyed link, and the
/// elements are concatenated without separators.
/// </para>
/// </remarks>
/// <exception cref="T:Ranvier.View.Foundation.Server.ServerPendingException">
/// A read outside every boundary is pending, and the request's cleanup succeeds.
/// </exception>
/// <exception cref="T:System.AggregateException">
/// The request's cleanup fails. The inner exceptions are the render's failure, when it failed, then the cleanup's.
/// </exception>
let renderPage (options: Options) (definition: Component<'props>) (props: 'props) : Page =
    let sink = HtmlSink ()
    let heads = HeadCollector ()

    do
        use graph = new Graph (options.GraphOptions)
        renderOn graph heads options definition props sink

    {
        Body = sink.ToString ()
        Head = heads.ToString ()
    }
