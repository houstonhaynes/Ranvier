/// <summary>Helpers shared by the view check files.</summary>
module CheckSupport

open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom.Scheduling
open Ranvier.View.Dom.Mounting
open Ranvier.View.Testing

#if FABLE_COMPILER
[<Global>]
let private globalThis: TS.Es.GlobalThis = jsNative
#endif

// The running conformance case's document, or the page's outside a case.
let private document () : Document =
#if FABLE_COMPILER
    if Conformance.isRunning () then
        Conformance.document ()
    else
        globalThis.document
#else
    Conformance.document ()
#endif

let equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>A detached div to mount into.</summary>
let host () : HTMLElement =
    (document ()).createElement "div"

let modeOf queued =
    if queued then
        QueuedCommits (CollectAndContinue, ignore)
    else
        SynchronousCommits

let private pageReports = ResizeArray<string list>()

// The reports strict received: the running case's, or the page's outside a case.
let private strictReports () =
    if Conformance.isRunning () then
        Conformance.local "strictReports" (fun () -> ResizeArray<string list>())
    else
        pageReports

/// <summary>
/// A report callback that raises on every report. Under <c>strictCases</c>, the report also fails its test when the
/// mount absorbs the raise, as it does for a report made during <c>Mounted.Dispose</c>.
/// </summary>
let strict (errors: exn list) =
    let reported = [ for error in errors -> error.Message ]
    (strictReports ()).Add reported
    failwithf "Unexpected report %A" reported

/// <summary>
/// The cases, each failing when its run passes any report to <c>strict</c>. A case also fails, without running, when
/// <c>strict</c> received a report after the previous case returned. A final case, "no late report", fails when
/// <c>strict</c> received a report after the last case returned.
/// </summary>
let strictCases (cases: (string * (unit -> unit)) array) =
    let lateReport (message: string) =
        let reports = strictReports ()

        if reports.Count > 0 then
            let reported = List.ofSeq reports
            reports.Clear ()
            failwithf "%s %A" message reported

    Array.append
        [|
            for name, check in cases ->
                name,
                fun () ->
                    lateReport "Late report from the previous case"
                    check ()
                    lateReport "Unexpected report"
        |]
        [| "no late report", (fun () -> lateReport "Late report after the last case") |]

/// <summary>The messages of each recorded report, in report order.</summary>
let messages (reports: ResizeArray<exn list>) =
    [ for errors in reports -> errors |> List.map _.Message ]

/// <summary>Mounts <c>view</c> as a root mount at the end of <c>target</c>.</summary>
let mountView (_: Operations<'view>) (run: 'view -> Backend -> unit) queued graph report (target: HTMLElement) (view: 'view) =
    mountWith (modeOf queued) graph None report target None (fun context -> run view context.Backend)

/// <summary>Mounts the template view <c>view</c> as a root mount at the end of <c>target</c>.</summary>
let mountTemplate queued graph report (target: HTMLElement) (view: Program.View) =
    mountView Program.ops Program.run queued graph report target view

let span (ops: Operations<'view>) (modifiers: Typed.Modifier<HTMLSpanElement> array) children =
    Typed.element ops (HtmlTag "span") modifiers children

let attachToBody (node: Node) : unit =
    (document ()).body.appendChild node |> ignore

let activeElement () : obj =
    match (document ()).activeElement with
    | Some element -> box element
    | None -> null

/// <summary>
/// Replaces the children of <c>target</c> with <c>html</c> parsed by <c>Range.createContextualFragment</c> in the
/// context of <c>target</c>, with the nodes a document parse of the served page gives.
/// </summary>
#if FABLE_COMPILER
[<Emit("(() => { const r = $0.ownerDocument.createRange(); r.selectNodeContents($0); $0.replaceChildren(r.createContextualFragment($1)); })()")>]
let loadServed (target: HTMLElement) (html: string) : unit = jsNative
#else
let loadServed (target: HTMLElement) (html: string) : unit =
    target.innerHTML <- html
#endif

/// <summary>The template variant of a check, under synchronous and queued commits.</summary>
let templated (name: string) (check: bool -> unit -> unit) =
    [|
        for queued in [ false; true ] do
            yield sprintf "template %s: %s" (if queued then "queued" else "synchronous") name, check queued
    |]

/// <summary>The tree and program variants of a check, under synchronous and queued commits.</summary>
let both (name: string) (tree: bool -> unit) (program: bool -> unit) =
    [|
        for queued in [ false; true ] do
            let mode = if queued then "queued" else "synchronous"
            yield sprintf "tree %s: %s" mode name, (fun () -> tree queued)
            yield sprintf "program %s: %s" mode name, (fun () -> program queued)
    |]
