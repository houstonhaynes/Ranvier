/// <summary>The story page's browser entry: hydrates the server's document strictly, or mounts when the page was not served.</summary>
module StoryClient

open Fable.Core
open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

[<Emit("($0.firstChild !== null && $0.firstChild.nodeType === 8)")>]
let private served (host: obj) : bool = jsNative

/// <summary>A hydrated or mounted story and the failures its mount reported.</summary>
[<NoEquality; NoComparison>]
type Mounted =
    {
        Mount: Mounting.Mounted
        Story: Story.Story
        Hydrated: bool
        Errors: ResizeArray<string>
    }

/// <summary>Hydrates <c>host</c>'s served story with strict checks, or mounts the story when <c>host</c> holds no render.</summary>
let attach (graph: Graph) (host: TS.Dom.HTMLElement) : Mounted =
    let errors = ResizeArray<string>()

    let report (failures: exn list) =
        for failure in failures do
            errors.Add failure.Message

        Page.report failures

    let hydrated = served host
    let mutable story = Unchecked.defaultof<Story.Story>

    let publish created =
        story <- created

    let mounted =
        if hydrated then
            Hydration.hydrate
                graph
                None
                report
                { Hydration.HydrationOptions.defaults with
                    Strict = true
                }
                host
                Story.view
                publish
        else
            Mounting.mount graph None report host None (fun context -> Program.run (Component.render Story.view publish) context.Backend)

    {
        Mount = mounted
        Story = story
        Hydrated = hydrated
        Errors = errors
    }

/// <summary>The browser entry: attaches the story to <c>#app</c> and publishes <c>window.__motion</c>.</summary>
let start () : unit -> unit =
    let graph = new Graph ()

    let attached =
        try
            attach graph (Page.host ())
        with error ->
            Page.expose {| errors = [| error.Message |] |}
            raise error

    Page.expose
        {|
            graph = graph
            story = attached.Story
            progress =
                fun () ->
                    graph.Run (fun () ->
                        Api.untrack (fun () ->
                            Array.append
                                (attached.Story.Chapters
                                 |> Array.map (fun chapter -> chapter.Progress.Value))
                                [| attached.Story.Gallery.Progress.Value |]))
            seen =
                fun () ->
                    graph.Run (fun () ->
                        Api.untrack (fun () ->
                            attached.Story.Chapters
                            |> Array.map (fun chapter -> chapter.Seen.Value)))
            hydrated = attached.Hydrated
            errors = fun () -> attached.Errors.ToArray ()
        |}

    fun () ->
        attached.Mount.Dispose ()
        graph.Dispose ()
