/// <summary>Hydration: a client mount that adopts the nodes of a template component's server HTML.</summary>
module Ranvier.View.Foundation.Hydration

open System
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Scheduling
open Ranvier.View.Foundation.Mounting

/// <summary>The client's settings for hydrating server HTML.</summary>
type HydrationOptions =
    {
        /// <summary>The root prefix of the ids <c>Setup.CreateId</c> returns; must match the server's.</summary>
        IdPrefix: string
        /// <summary>Whether a structural mismatch raises from <c>hydrate</c>.</summary>
        Strict: bool
    }

module HydrationOptions =
    /// <summary>The prefix of <c>Server.Options.defaults</c>, with mismatches re-rendered and reported.</summary>
    let defaults = { IdPrefix = "r"; Strict = false }

[<Emit("$0.firstChild")>]
let private firstChildOf (node: Node) : Node = jsNative

[<Emit("$0.nextSibling")>]
let private following (node: Node) : Node = jsNative

[<Emit("$0 == null")>]
let private absent (node: Node) : bool = jsNative

[<Emit("$0.nodeType === 8 && $0.data === '[r'")>]
let private isRootMarker (node: Node) : bool = jsNative

/// <summary>The first <c>&lt;!--[r--&gt;</c> child of <c>host</c>, or null.</summary>
let private rootMarker (host: Node) : Node =
    let mutable node = firstChildOf host

    while not (absent node) && not (isRootMarker node) do
        node <- following node

    node

[<Emit("$0.remove()")>]
let private removeNode (node: Node) : unit = jsNative

/// <summary>Removes the siblings after <c>start</c> up to <c>finish</c>, or to the last sibling when <c>finish</c> is null.</summary>
let private clearAfter (start: Node) (finish: Node) =
    let mutable node = following start

    while not (isNull (box node))
          && not (obj.ReferenceEquals (node, finish)) do
        let next = following node
        removeNode node
        node <- next

/// <summary>
/// The synchronous mount state of a root in <c>host</c>; a null <c>cursor</c> mounts fresh, otherwise it adopts the
/// server nodes between <c>rootStart</c> and <c>rootEnd</c>.
/// </summary>
let private rootState
    (graph: Graph)
    (owner: Owner)
    (report: exn list -> unit)
    (options: HydrationOptions)
    (host: Node)
    (cursor: TemplateDom.HydrationCursor)
    (rootStart: Node)
    (rootEnd: Node)
    =
    MountState (
        Synchronous,
        graph,
        null,
        owner,
        report,
        host,
        Unchecked.defaultof<_>,
        Unchecked.defaultof<_>,
        IdentityScope options.IdPrefix,
        null,
        InstanceStep.Root,
        0,
        0,
        cursor,
        rootStart,
        rootEnd
    )

/// <summary>
/// Mounts <c>definition</c> fresh in <c>host</c> before <c>before</c>, running <c>atEnd</c> at the end of its build;
/// disposing the mount also removes <c>markers</c>.
/// </summary>
let private freshRoot
    (graph: Graph)
    (owner: Owner)
    (report: exn list -> unit)
    (options: HydrationOptions)
    (atEnd: unit -> unit)
    (host: Node)
    (definition: Component<'props>)
    (props: 'props)
    (before: Node option)
    (markers: Node list)
    : Mounted =
    let created =
        rootState graph owner report options host null Unchecked.defaultof<_> Unchecked.defaultof<_>

    let mounted =
        created.Run (
            before,
            fun context ->
                context.Backend.Template (definition, box props)
                created.AtBuildEnd atEnd
        )

    if not markers.IsEmpty then
        created.Root.OnCleanup (fun () -> List.iter removeNode markers)

    mounted

/// <summary>
/// Hydrates the root of <c>host</c> as <c>hydrateWith</c> does, the document's head aside; before a re-rendered root
/// mounts fresh, <c>served</c> sets the head back as the server wrote it. The mount that <c>adoptHost</c> returns runs
/// <c>atEnd</c> at the end of its build, and a failure of <c>atEnd</c> is a failure of that mount.
/// </summary>
let private adoptHost
    (served: unit -> unit)
    (atEnd: unit -> unit)
    (mode: CommitMode)
    (graph: Graph)
    (parentOwner: Owner option)
    (report: exn list -> unit)
    (options: HydrationOptions)
    (host: Node)
    (definition: Component<'props>)
    (props: 'props)
    : Mounted =
    match mode with
    | QueuedCommits _ -> raise (NotSupportedException "Hydration supports synchronous commits only")
    | SynchronousCommits ->
        let owner =
            match parentOwner with
            | Some owner -> owner
            | None -> Unchecked.defaultof<_>

        let rootStart = rootMarker host

        if absent rootStart then
            let error = TemplateDom.mismatch "root" "<!--[r-->" "absent"

            if options.Strict then
                raise error

            report [ error ]
            freshRoot graph owner report options atEnd host definition props None []
        else
            let rootEnd =
                try
                    TemplateDom.rootEnd rootStart
                with TemplateDom.HydrationMismatchException _ as error when not options.Strict ->
                    report [ error ]
                    clearAfter rootStart Unchecked.defaultof<_>
                    Unchecked.defaultof<_>

            if absent rootEnd then
                freshRoot graph owner report options atEnd host definition props None [ rootStart ]
            else
                let first = following rootStart
                let cursor = TemplateDom.HydrationCursor (first, options.Strict)
                let adopting = rootState graph owner report options host cursor rootStart rootEnd

                try
                    adopting.Run (
                        None,
                        fun context ->
                            context.Backend.Template (definition, box props)

                            if not (obj.ReferenceEquals (cursor.Next, rootEnd)) then
                                let mutable position = 0
                                let mutable node = first

                                while not (obj.ReferenceEquals (node, cursor.Next)) do
                                    node <- TemplateDom.logicalNext node
                                    position <- position + 1

                                raise (TemplateDom.mismatch ("#" + string position) "<!--]-->" (TemplateDom.describe cursor.Next))

                            adopting.AtBuildEnd atEnd
                    )
                with error when not options.Strict ->
                    match TemplateDom.findMismatch error with
                    | Some found ->
                        served ()

                        report (
                            (found :> exn)
                            :: TemplateDom.failuresBesides found error
                        )

                        clearAfter rootStart rootEnd
                        freshRoot graph owner report options atEnd host definition props (Some rootEnd) [ rootStart; rootEnd ]
                    | None -> reraise ()

/// <summary>
/// Mounts <c>definition</c> for <c>props</c> over the server HTML between the first <c>&lt;!--[r--&gt;</c> child of
/// <c>host</c> and its matching end marker, adopting the server's nodes. The result behaves as a mount by
/// <c>mountWith SynchronousCommits</c>: its slots compare their first value with the server's nodes and write only a
/// difference; its lists adopt the server's rows in order, its arms adopt theirs when the server's arm hash equals the
/// selected definition's, its boundaries adopt the view their start marker records, and its view holes mount fresh
/// between the server's region markers.
/// </summary>
/// <remarks>
/// <para>Nodes outside a closed root marker pair stay in place and outside the mount's range.</para>
/// <para>
/// Document entries adopt the head elements marked with their key, as in every mount, and a title entry writes the
/// document's title only when it differs. Once the mount is built, every unadopted marked element of the document's
/// head is removed, those served for other roots included; an entry under pending content keeps its adopted element.
/// </para>
/// <para>
/// A boundary's content and its pending view hydrate between <c>ranvier:start</c> and <c>ranvier:end</c> comments,
/// inserted around their adopted nodes. Server content (<c>&lt;!--[c--&gt;</c>) hydrates in place and keeps its bound
/// nodes when it is ready by the end of the build. When one of its reads is still pending at the end of the build, its
/// bound nodes are held detached, and a copy of the server nodes, taken before the content's first commits, stays
/// between the markers as the inert pending view; the reveal replaces the copy with the bound nodes. Under a server
/// pending view (<c>&lt;!--[p--&gt;</c>), the content mounts detached, and the pending view hydrates while the content
/// is pending; ready content replaces the server's nodes at once. A server error view (<c>&lt;!--[e--&gt;</c>) is
/// removed and the boundary mounts fresh. A failure of the content's setup shows the error view, as in a mount.
/// </para>
/// <para>
/// A structural mismatch inside a list or arm region, or inside a boundary's hydrating view, re-renders the innermost
/// such region or view containing it, and the rest of the hydration continues. The mismatch is passed to
/// <c>report</c> once, first in a list with the pass's other failures; the server nodes between the region's markers
/// are removed and the region or view mounts fresh. The meta and link entries of the region or view then own new head
/// elements in place of the adopted ones. A failure of a region's fresh mount is a failure of the hydration attempt;
/// a failure of a boundary view's fresh mount goes to the boundary, as in a mount.
/// </para>
/// <para>
/// A structural mismatch outside every list region, arm region and boundary view sets the head back as the server
/// wrote it and is passed to <c>report</c> once, first in a list that also holds the failures of the attempt's
/// rollback. The server nodes between the root markers are then removed and the root mounts fresh before the end
/// marker. Without a root marker, the root mounts fresh at the end of <c>host</c>, after its children. Without the end
/// marker, every node after the root marker, foreign siblings included, is removed and the root mounts fresh at the
/// end of <c>host</c>. Ids issued by the fresh mount start again under <c>options.IdPrefix</c>. Disposing the fresh
/// mount also removes the root markers.
/// </para>
/// <para>
/// A failure of the fresh mount propagates with the server content already removed: the root markers are left empty
/// as <c>&lt;!--[r--&gt;&lt;!--]--&gt;</c>, or, without the end marker, <c>&lt;!--[r--&gt;</c> is the host's last child.
/// The document's head is then as the server wrote it.
/// </para>
/// <para>
/// Under <c>options.Strict</c>, and for every other failure of the hydration attempt, the call disposes what it created
/// and raises; the host and the document's head are then as the server wrote them. A failure of <c>report</c> also
/// propagates with the host and the head as the server wrote them.
/// </para>
/// </remarks>
/// <exception cref="T:System.NotSupportedException"><c>mode</c> is <c>QueuedCommits</c>.</exception>
/// <exception cref="T:Ranvier.View.Foundation.TemplateDom.HydrationMismatchException">
/// <c>options.Strict</c> is set, and the host has no root marker or the server HTML differs from the template's structure.
/// </exception>
let hydrateWith
    (mode: CommitMode)
    (graph: Graph)
    (parentOwner: Owner option)
    (report: exn list -> unit)
    (options: HydrationOptions)
    (host: Node)
    (definition: Component<'props>)
    (props: 'props)
    : Mounted =
    let document = DocumentHead.documentOf host
    let served = DocumentHead.keep document

    try
        try
            adoptHost
                (fun () -> served.Restore ())
                (fun () -> DocumentHead.removeUnadopted document)
                mode
                graph
                parentOwner
                report
                options
                host
                definition
                props
        with _ ->
            served.Restore ()
            reraise ()
    finally
        served.Release ()

[<Emit("document.getElementById($0)")>]
let private elementById (id: string) : Element = jsNative

[<Emit("$0.textContent")>]
let private textOf (element: Element) : string = jsNative

/// <summary>
/// The <c>textContent</c> of the global <c>document</c>'s element with id <c>id</c>, such as the JSON a
/// <c>Server.stateScript</c> element holds; <c>None</c> when the id is absent from that document.
/// </summary>
let readState (id: string) : string option =
    let element = elementById id

    if isNull (box element) then None else Some (textOf element)

/// <summary>Hydrates as <c>hydrateWith SynchronousCommits</c> does.</summary>
let hydrate graph parentOwner report options host (definition: Component<'props>) (props: 'props) : Mounted =
    hydrateWith SynchronousCommits graph parentOwner report options host definition props
