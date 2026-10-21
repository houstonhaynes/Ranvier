/// <summary>The story page without its Motion parts: the server render of the story must equal this twin's.</summary>
module StoryTwin

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Story

let private percent (value: float) =
    sprintf "%.0f%%" (value * 100.)

let view: Component<Story -> unit> =
    Component.define (fun _ (_: Story -> unit) -> create ()) (fun t ->
        frame
            t
            (article (Attrs.className "story") {
                header (Attrs.className "hero") {
                    p (Attrs.className "eyebrow") { "Rendered on .NET · hydrated in the browser" }
                    h1 (Attrs.className "headline") { "Motion, owned by the view." }

                    p (Attrs.className "lede") {
                        "Scroll down. Each section below runs an anime.js timeline scrubbed by the scroll, and writes how far it has come into a signal."
                    }

                    div (Attrs.className "cue", Attrs.aria ("hidden", "true")) { "↓" }
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
                    section (Attrs.className (if index % 2 = 0 then "chapter" else "chapter flipped"), Attrs.data ("chapter", string index)) {
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

                section (Attrs.className "gallery") {
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
                            forEachFixed id (fun (_: Story) -> extras) (div (Attrs.className "tile extra") { fun (label: string) -> label })
                        })
                }

                footer (Attrs.className "story-end") { "Built with Ranvier and anime.js." }
            }))
