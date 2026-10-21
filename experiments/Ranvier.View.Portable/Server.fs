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
/// The <c>stateScript</c> element, with <c>nonce</c> (a leading-space attribute string, or empty) written after the id
/// attribute.
/// </summary>
let private stateElement (nonce: string) (id: string) (json: string) : string =
    let escaped =
        json.Replace("<", "\\u003c").Replace(string (char 0x2028), "\\u2028").Replace(string (char 0x2029), "\\u2029")

    "<script type=\"application/json\" id=\""
    + Html.escape true id
    + "\""
    + nonce
    + ">"
    + escaped
    + "</script>"

/// <summary>
/// A <c>&lt;script type="application/json"&gt;</c> element with the escaped <c>id</c>, holding <c>json</c> with each
/// <c>&lt;</c>, U+2028 and U+2029 written as its six-character JSON escape.
/// </summary>
/// <remarks>
/// The parsed element's text runs to its own end tag, and for valid JSON, <c>JSON.parse</c> of its <c>textContent</c>
/// returns the value <c>json</c> encodes. <c>Hydration.readState</c> reads the text back.
/// </remarks>
let stateScript (id: string) (json: string) : string =
    stateElement "" id json

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

/// <summary>A module script of a document, written after the host element and the state elements.</summary>
[<RequireQualifiedAccess>]
type ModuleScript =
    /// <summary>
    /// <c>&lt;script type="module" src&gt;</c> loading the escaped URL. The URL is a trusted author URL, written as given
    /// apart from escaping.
    /// </summary>
    | Src of src: string
    /// <summary>
    /// <c>&lt;script type="module"&gt;</c> holding the text as given. Text containing <c>&lt;/script</c> in any ASCII
    /// case, or <c>&lt;!--</c>, is rejected.
    /// </summary>
    | Inline of text: string

/// <summary>The HTML document <c>renderDocument</c> writes around a component.</summary>
type DocumentShell =
    {
        /// <summary>The <c>lang</c> attribute of <c>&lt;html&gt;</c>; <c>None</c> or <c>Some null</c> omits it.</summary>
        Lang: string option
        /// <summary>The id of the <c>&lt;div&gt;</c> holding the rendered body.</summary>
        HostId: string
        /// <summary>
        /// The state elements after the host, each an id and its JSON as <c>stateScript</c> takes them; a null JSON value
        /// is rejected.
        /// </summary>
        State: (string * string) list
        /// <summary>
        /// The <c>href</c> of each <c>&lt;link rel="stylesheet"&gt;</c>, written after the head entries. Each is a trusted
        /// author URL, written as given apart from escaping.
        /// </summary>
        Stylesheets: string list
        /// <summary>The scripts after the state elements, in order.</summary>
        Scripts: ModuleScript list
        /// <summary>
        /// The <c>nonce</c> attribute of every script, state and stylesheet element, for a
        /// <c>Content-Security-Policy</c> <c>script-src</c> or <c>style-src 'nonce-...'</c>; <c>None</c> or
        /// <c>Some null</c> omits it. A nonce holds ASCII letters, digits and <c>+ / = - _</c>.
        /// </summary>
        Nonce: string option
        /// <summary>The further attributes of <c>&lt;html&gt;</c>, after <c>lang</c>; an <c>id</c> joins the document's ids.</summary>
        HtmlAttributes: (string * string) list
        /// <summary>The attributes of <c>&lt;body&gt;</c>; an <c>id</c> joins the document's ids.</summary>
        BodyAttributes: (string * string) list
    }

module DocumentShell =
    /// <summary>A document with the host id <c>app</c> and every other field empty.</summary>
    let defaults =
        {
            Lang = None
            HostId = "app"
            State = []
            Stylesheets = []
            Scripts = []
            Nonce = None
            HtmlAttributes = []
            BodyAttributes = []
        }

let private validateId (id: string) =
    let whitespace (c: char) =
        c = ' '
        || c = '\t'
        || c = '\n'
        || c = '\u000C'
        || c = '\r'

    if
        String.IsNullOrEmpty id
        || Seq.exists whitespace id
    then
        raise (
            ArgumentException (
                "An element id must be non-empty and contain no ASCII whitespace: \""
                + id
                + "\"",
                "document"
            )
        )

/// <summary>Adds a valid <c>id</c> to <c>ids</c>; an invalid id or one already in <c>ids</c> raises.</summary>
let private addId (ids: ResizeArray<string>) (id: string) =
    validateId id

    if ids.Contains id then
        raise (ArgumentException ("The element id " + id + " is written twice", "document"))

    ids.Add id

let private validateName (name: string) =
    try
        validateAttribute name
    with _ ->
        raise (
            ArgumentException (
                "Attribute names must be lowercase and cannot contain event handlers or raw document content: \""
                + name
                + "\"",
                "document"
            )
        )

let private validateNonce (nonce: string) =
    let valid (c: char) =
        (c >= 'a' && c <= 'z')
        || (c >= 'A' && c <= 'Z')
        || (c >= '0' && c <= '9')
        || c = '+'
        || c = '/'
        || c = '='
        || c = '-'
        || c = '_'

    if
        String.IsNullOrEmpty nonce
        || not (Seq.forall valid nonce)
    then
        raise (ArgumentException ("A nonce must be non-empty and hold only ASCII letters, digits and + / = - _", "document"))

/// <summary>Whether <c>pattern</c>, lowercase ASCII, occurs at <c>index</c> of <c>text</c> in any ASCII case.</summary>
let private occursAt (text: string) (index: int) (pattern: string) =
    let lower (c: char) =
        if c >= 'A' && c <= 'Z' then char (int c + 32) else c

    let mutable matched = index + pattern.Length <= text.Length
    let mutable offset = 0

    while matched && offset < pattern.Length do
        matched <- lower text[index + offset] = pattern[offset]
        offset <- offset + 1

    matched

let private validateInline (text: string) =
    let mutable index = text.IndexOf '<'

    while index >= 0 do
        if
            occursAt text index "</script"
            || occursAt text index "<!--"
        then
            raise (ArgumentException ("Inline module text cannot contain </script or <!--", "document"))

        index <- text.IndexOf ('<', index + 1)

/// <summary>
/// The attributes as <c> name="value"</c> pairs; a name repeated or among <c>reserved</c> raises, and an <c>id</c> value
/// goes through <c>addId</c>.
/// </summary>
let private attributes (ids: ResizeArray<string>) (reserved: string list) (pairs: (string * string) list) : string =
    let seen = ResizeArray<string> reserved

    pairs
    |> List.map (fun (name, value) ->
        validateName name

        if name = "id" then
            addId ids value

        if seen.Contains name then
            raise (ArgumentException ("The attribute " + name + " is written twice", "document"))

        seen.Add name
        " " + name + "=\"" + Html.escape true value + "\"")
    |> String.concat ""

/// <summary>
/// The document's markup before the head entries, between the head entries and the body, and after the body; an
/// invalid document raises <c>ArgumentException</c>.
/// </summary>
let private shell (document: DocumentShell) : string * string * string =
    validateId document.HostId

    // Some null reads as None on .NET, as it does under Fable's erased options.
    let nonce =
        match document.Nonce with
        | Some nonce when not (isNull nonce) ->
            validateNonce nonce
            " nonce=\"" + Html.escape true nonce + "\""
        | _ -> ""

    let ids = ResizeArray<string> [ document.HostId ]

    for id, json in document.State do
        addId ids id

        if isNull json then
            raise (
                ArgumentException (
                    "The state element "
                    + id
                    + " has a null JSON value",
                    "document"
                )
            )

    let lang, reserved =
        match document.Lang with
        | Some lang when not (isNull lang) -> " lang=\"" + Html.escape true lang + "\"", [ "lang" ]
        | _ -> "", []

    let start =
        "<!doctype html><html"
        + lang
        + attributes ids reserved document.HtmlAttributes
        + "><head><meta charset=\"utf-8\">"

    let middle =
        (document.Stylesheets
         |> List.map (fun href ->
             "<link rel=\"stylesheet\" href=\""
             + Html.escape true href
             + "\""
             + nonce
             + ">")
         |> String.concat "")
        + "</head><body"
        + attributes ids [] document.BodyAttributes
        + "><div id=\""
        + Html.escape true document.HostId
        + "\">"

    let scripts =
        document.Scripts
        |> List.map (function
            | ModuleScript.Src src ->
                "<script type=\"module\" src=\""
                + Html.escape true src
                + "\""
                + nonce
                + "></script>"
            | ModuleScript.Inline text ->
                let text = if isNull text then "" else text
                validateInline text

                "<script type=\"module\""
                + nonce
                + ">"
                + text
                + "</script>")
        |> String.concat ""

    let finish =
        "</div>"
        + (document.State
           |> List.map (fun (id, json) -> stateElement nonce id json)
           |> String.concat "")
        + scripts
        + "</body></html>"

    start, middle, finish

/// <summary>
/// Renders as <c>renderPage</c> does, then passes the HTML document holding the page to <c>write</c> in order; an
/// invalid <c>document</c> raises before the render, and a failed render writes nothing.
/// </summary>
/// <remarks>
/// <para>
/// The document is <c>&lt;!doctype html&gt;</c>, then <c>&lt;html&gt;</c> with <c>lang</c> and <c>HtmlAttributes</c>.
/// Its <c>&lt;head&gt;</c> holds <c>&lt;meta charset="utf-8"&gt;</c>, the page's head entries and the stylesheets.
/// Its <c>&lt;body&gt;</c> holds the host <c>&lt;div&gt;</c>, whose only content is the page's body, then the state
/// elements, then the scripts. Each script, state and stylesheet element carries <c>Nonce</c> as its last
/// attribute. Elements are written without whitespace between them.
/// </para>
/// <para>
/// Every attribute value is escaped as <c>Html.escape true</c> escapes it. <c>ArgumentException</c> is raised for an
/// id that is null, empty or contains ASCII whitespace (space, tab, line feed, form feed, carriage return), an id
/// written twice among the host, state, <c>&lt;html&gt;</c> and <c>&lt;body&gt;</c> ids, a null state JSON value, an
/// attribute name <c>validateAttribute</c> rejects, an attribute written twice on an element, a nonce that is empty or holds a character outside ASCII
/// letters, digits and <c>+ / = - _</c>, and inline module text containing <c>&lt;/script</c> in any ASCII case or
/// <c>&lt;!--</c>.
/// </para>
/// </remarks>
/// <exception cref="T:System.ArgumentException">The document is invalid.</exception>
/// <exception cref="T:Ranvier.View.Foundation.Server.ServerPendingException">
/// A read outside every boundary is pending, and the request's cleanup succeeds.
/// </exception>
/// <exception cref="T:System.AggregateException">
/// The request's cleanup fails. The inner exceptions are the render's failure, when it failed, then the cleanup's.
/// </exception>
let renderDocumentTo (options: Options) (document: DocumentShell) (definition: Component<'props>) (props: 'props) (write: string -> unit) : unit =
    let start, middle, finish = shell document
    let sink = HtmlSink ()
    let heads = HeadCollector ()

    do
        use graph = new Graph (options.GraphOptions)
        renderOn graph heads options definition props sink

    let head = heads.ToString ()
    write start

    if head.Length > 0 then
        write head

    write middle
    sink.Segments write
    write finish

/// <summary>Renders the HTML document <c>renderDocumentTo</c> writes, as one string.</summary>
/// <exception cref="T:System.ArgumentException">The document is invalid.</exception>
/// <exception cref="T:Ranvier.View.Foundation.Server.ServerPendingException">
/// A read outside every boundary is pending, and the request's cleanup succeeds.
/// </exception>
/// <exception cref="T:System.AggregateException">
/// The request's cleanup fails. The inner exceptions are the render's failure, when it failed, then the cleanup's.
/// </exception>
let renderDocument (options: Options) (document: DocumentShell) (definition: Component<'props>) (props: 'props) : string =
    let start, middle, finish = shell document
    let page = renderPage options definition props
    start + page.Head + middle + page.Body + finish
