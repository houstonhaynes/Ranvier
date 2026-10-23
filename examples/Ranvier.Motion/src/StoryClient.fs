/// <summary>Story mount adapters and controls exposed to the browser tests.</summary>
module StoryClient

open Fable.Core
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom

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

/// <summary>Publishes the hydrated story's browser controls.</summary>
let activate (graph: Graph) (story: Story.Story) : unit -> unit =
    Page.expose
        {|
            graph = graph
            story = story
            progress =
                fun () ->
                    graph.Run (fun () ->
                        Api.untrack (fun () ->
                            Array.append
                                (story.Chapters
                                 |> Array.map (fun chapter -> chapter.Progress.Value))
                                [| story.Gallery.Progress.Value |]))
            seen =
                fun () ->
                    graph.Run (fun () ->
                        Api.untrack (fun () ->
                            story.Chapters
                            |> Array.map (fun chapter -> chapter.Seen.Value)))
            hydrated = true
            errors = fun () -> [||]: string array
        |}

    ignore
