/// <summary>
/// A scroll-driven landing page, portable: the .NET server renders it and the browser hydrates it. Scroll timelines write
/// each section's progress into signals, the headline is split and staggered, the gallery staggers across its grid, and
/// tiles created on the client appear. The server renders the whole document under <c>Omit</c>, and the listeners attach
/// on hydration.
/// </summary>
module Story

open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Ranvier.View.Anime

/// <summary>A section's scroll progress, in 0..1, and whether it is between its thresholds.</summary>
[<NoEquality; NoComparison>]
type Chapter =
    {
        Progress: Signal<float>
        Seen: Signal<bool>
    }

/// <summary>The words of a chapter.</summary>
type Copy =
    {
        Title: string
        Text: string
        Mark: string
    }

[<NoEquality; NoComparison>]
type Story =
    {
        Chapters: Chapter array
        Gallery: Chapter
        /// <summary>Whether the tiles created on the client are shown.</summary>
        More: Signal<bool>
    }

let chapters =
    [|
        {
            Title = "Owned"
            Text =
                "Every animation is created when its element is placed and reverted when its owner is disposed. Nothing outlives the view that started it."
            Mark = "01"
        }
        {
            Title = "Reactive"
            Text =
                "Signals drive animations, and animations publish their progress back as signals, so the rail at the top is text bound to those signals."
            Mark = "02"
        }
        {
            Title = "Portable"
            Text =
                "This page was rendered by the .NET server. The motion parts add no markup, so the browser adopts every node and animates from there."
            Mark = "03"
        }
    |]

/// <summary>The labels of the gallery's tiles, and of the tiles the extras button shows.</summary>
let tiles =
    [|
        "Enter"
        "Exit"
        "Flip"
        "Follow"
        "Draw"
        "Morph"
        "Scroll"
        "Drag"
        "Split"
        "Stagger"
        "Policy"
        "Hydrate"
    |]

let extras = [| "Spring"; "Timeline"; "Scrub"; "Track" |]

let private chapter () =
    {
        Progress = Api.createSignal 0.
        Seen = Api.createSignal false
    }

/// <summary>A story with every progress at 0, in the current graph's scope.</summary>
let create () : Story =
    {
        Chapters = Array.init chapters.Length (fun _ -> chapter ())
        Gallery = chapter ()
        More = Api.createSignal false
    }

[<Emit("Array.from($0.querySelectorAll($1))")>]
let private within (root: obj) (selector: string) : TS.Dom.Element array = jsNative

let private percent (value: float) =
    sprintf "%.0f%%" (value * 100.)

let private scrubbed =
    ScrollParams (enter = "bottom top", leave = "top bottom", sync = 0.25)

let private bound (read: Story -> Chapter) (story: Story) =
    let chapter = read story

    {
        Progress = ValueSome chapter.Progress
        InView = ValueSome chapter.Seen
    }

// A chapter's copy slides in from the side its art leaves, and the art turns and settles, scrubbed by the scroll.
let private chapterTimeline (index: int) (_: Story) (section: TS.Dom.Element) =
    let side = if index % 2 = 0 then -1. else 1.

    Anime
        .timeline(TimelineParams (autoplay = false))
        .add(
            within section ".chapter-copy",
            Anim (x = Value.fromTo (Value.op_Implicit (side * 80.), 0), opacity = Value.fromTo (0, 1), duration = 500, ease = Ease.outCubic)
        )
        .add(
            within section ".chapter-art",
            Anim (
                rotate = Value.fromTo (Value.op_Implicit (side * -24.), 0),
                scale = Value.fromTo (0.7, 1),
                opacity = Value.fromTo (0.2, 1),
                duration = 500,
                ease = Ease.outBack
            ),
            At.withPrevious
        )

let private galleryTimeline (_: Story) (section: TS.Dom.Element) =
    Anime.animate (
        within section ".tile",
        Anim (
            y = Value.fromTo (60, 0),
            scale = Value.fromTo (0.6, 1),
            opacity = Value.fromTo (0, 1),
            delay = Value.stagger (60, grid = Grid.size (4, 3), from = StaggerFrom.center),
            duration = 500,
            ease = Ease.outQuart,
            autoplay = false
        )
    )

let private rise (_: Story) (element: TS.Dom.Element) =
    Anime.animate (element, Anim (y = Value.fromTo (14, 0), opacity = Value.fromTo (0, 1), duration = 600, ease = Ease.outQuart))

let private bob (_: Story) (element: TS.Dom.Element) =
    Anime.animate (element, Anim (y = 8, duration = 900, loop = Loop.forever, alternate = true, ease = Ease.inOutSine))

let private letters (_: Story) (splitter: TextSplitter) =
    Anime.animate (
        splitter.chars,
        Anim (
            y = Value.fromTo (28, 0),
            opacity = Value.fromTo (0, 1),
            rotate = Value.fromTo (8, 0),
            delay = Value.stagger 22,
            duration = 640,
            ease = Ease.outQuart
        )
    )

let private popIn (_: string) (element: TS.Dom.Element) =
    Anime.animate (element, Anim (scale = Value.fromTo (0.4, 1), opacity = Value.fromTo (0, 1), duration = 420, ease = Ease.outBack))

/// <summary>
/// The page around the story's article: the document's title and viewport entries, the site header and the introduction.
/// </summary>
let frame (t: TemplateBuilder<Story>) (story: TemplateView<Story>) : TemplateView<Story> =
    div (Attrs.className "frame") {
        t.Document.title (fun _ -> "Story · Ranvier Motion")

        t.Document.meta
            [|
                t.Attrs.attribute ("name", "viewport")
                t.Attrs.attribute ("content", "width=device-width, initial-scale=1")
            |]

        header (Attrs.className "site-header") {
            a (Attrs.className "brand", Attrs.attribute ("href", "../")) {
                span (Attrs.className "brand-mark") { () }
                "Ranvier Motion"
            }

            nav () {
                for page, title in
                    [
                        "kanban", "Kanban"
                        "dashboard", "Dashboard"
                        "story", "Story"
                        "physics", "Physics"
                        "routes", "Routes"
                    ] do
                    if page = "story" then
                        a (Attrs.attribute ("href", "../story/"), Attrs.aria ("current", "page")) { title }
                    else
                        a (Attrs.attribute ("href", "../" + page + "/")) { title }
            }
        }

        section (Attrs.className "intro") {
            span (Attrs.className "tag") { "SSR · hydrate · scroll · split · stagger · appear" }
            h1 () { "Story" }

            p () {
                "Rendered by Server.renderDocument on .NET and hydrated strictly in the browser. Scroll timelines write their progress into signals, the headline is split into letters, and the gallery staggers out from its centre."
            }
        }

        story
    }

/// <summary>The story's component; its props receive each instance once set up.</summary>
let view: Component<Story -> unit> =
    Component.define
        (fun _ (publish: Story -> unit) ->
            let story = create ()
            publish story
            story)
        (fun t ->
            frame
                t
                (article (Attrs.className "story") {
                    header (Attrs.className "hero") {
                        p (Attrs.className "eyebrow", Motion.appear rise) { "Rendered on .NET · hydrated in the browser" }

                        h1 (Attrs.className "headline", Motion.split (SplitParams (chars = true, words = true)) letters) {
                            "Motion, owned by the view."
                        }

                        p (Attrs.className "lede") {
                            "Scroll down. Each section below runs an anime.js timeline scrubbed by the scroll, and writes how far it has come into a signal."
                        }

                        div (Attrs.className "cue", Attrs.aria ("hidden", "true"), Motion.on bob) { "↓" }
                    }

                    nav (Attrs.className "rail", Attrs.aria ("label", "Progress")) {
                        for index in 0 .. chapters.Length - 1 do
                            div (
                                Attrs.className (fun (s: Story) ->
                                    if s.Chapters[index].Seen.Value then
                                        "rail-item seen"
                                    else
                                        "rail-item"),
                                Attrs.attribute ("style", fun (s: Story) -> Some (sprintf "--p:%.3f" s.Chapters[index].Progress.Value))
                            ) {
                                span (Attrs.className "rail-mark") { chapters[index].Mark }
                                span (Attrs.className "rail-title") { chapters[index].Title }
                                span (Attrs.className "rail-value") { fun (s: Story) -> percent s.Chapters[index].Progress.Value }
                            }
                    }

                    for index in 0 .. chapters.Length - 1 do
                        section (
                            Attrs.className (if index % 2 = 0 then "chapter" else "chapter flipped"),
                            Attrs.data ("chapter", string index),
                            Motion.scroll scrubbed (bound (fun s -> s.Chapters[index])) (chapterTimeline index)
                        ) {
                            div (Attrs.className "chapter-copy") {
                                span (Attrs.className "chapter-mark") { chapters[index].Mark }
                                h2 () { chapters[index].Title }
                                p () { chapters[index].Text }
                            }

                            div (Attrs.className "chapter-art", Attrs.aria ("hidden", "true")) {
                                div (Attrs.className "orb") { () }
                                div (Attrs.className "ring") { () }
                            }
                        }

                    section (Attrs.className "gallery", Motion.scroll scrubbed (bound (fun s -> s.Gallery)) galleryTimeline) {
                        h2 () { "Twelve parts, one stagger" }
                        p (Attrs.className "gallery-note") { fun (s: Story) -> "Gallery " + percent s.Gallery.Progress.Value }

                        div (Attrs.className "tiles") {
                            for label in tiles do
                                div (Attrs.className "tile") { label }
                        }
                    }

                    section (Attrs.className "more") {
                        h2 () { "Four more, created on the client" }

                        button (Attrs.className "primary more-toggle", Events.onClick (fun (s: Story) _ -> s.More.Value <- not s.More.Value)) {
                            fun (s: Story) ->
                                if s.More.Value then
                                    "Hide the extras"
                                else
                                    "Show the extras"
                        }

                        show
                            (fun (s: Story) -> s.More.Value)
                            (div (Attrs.className "tiles extras") {
                                forEachFixed
                                    id
                                    (fun (_: Story) -> extras)
                                    (div (Attrs.className "tile extra", Motion.appear popIn) { fun (label: string) -> label })
                            })
                    }

                    footer (Attrs.className "story-end") { "Built with Ranvier and anime.js." }
                }))
