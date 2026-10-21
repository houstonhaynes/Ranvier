/// <summary>
/// The server render of every README sample and of the presets equals the render of its twin without Motion parts,
/// under the default <c>Reject</c> policy. Run on .NET with <c>dotnet run</c>.
/// </summary>
module ServerChecks

open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime
open Fixtures

let private boardTwinOf (selected: bool) =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal selected
            })
        (section () {
            ul (Attrs.className "cards") { forEach (fun card -> card.Id) (fun b -> b.Cards.Value) (li () { fun current -> (current ()).Title }) }
            show (fun b -> b.Selected.Value) (p () { "Selected" })
        })

let private selectedBoard =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal true
            })
        (section () {
            ul (Attrs.className "cards", Motion.flip (LayoutParams (duration = 350, ease = Ease.outQuart))) {
                forEach
                    (fun card -> card.Id)
                    (fun b -> b.Cards.Value)
                    (li (
                        Motion.presence (fun _ el -> Anime.animate (el, Anim (opacity = Value.fromTo (0, 1), duration = 240))) (fun _ el ->
                            Anime.animate (el, Anim (opacity = 0, duration = 180)))
                    ) {
                        fun current -> (current ()).Title
                    })
            }

            show (fun b -> b.Selected.Value) (p (Motion.fade) { "Selected" })
        })

let private bannerTwin =
    Component.template
        (fun _ (text: string) -> { Text = text })
        (header () {
            h1 () { fun b -> b.Text }
            span () { "*" }
        })

let private toastsTwin =
    Component.template
        (fun _ () -> { Messages = Api.createSignal [||] })
        (ol () { forEach id (fun (t: Toast) -> t.Messages.Value) (li () { fun message -> message () }) })

type Notes = { Notes: string array }

let private notes =
    Component.template
        (fun _ () -> { Notes = [| "a"; "b" |] })
        (ol () {
            forEachFixed
                id
                (fun (n: Notes) -> n.Notes)
                (li (
                    Motion.enter (fun _ el -> Anime.animate (el, Anim (x = Value.fromTo (40, 0), duration = 200))),
                    Motion.exit (fun _ el -> Anime.animate (el, Anim (x = 40, duration = 200)))
                ) {
                    fun (note: string) -> note
                })
        })

let private notesTwin =
    Component.template
        (fun _ () -> { Notes = [| "a"; "b" |] })
        (ol () { forEachFixed id (fun (n: Notes) -> n.Notes) (li () { fun (note: string) -> note }) })

let private gaugeTwin =
    Component.template
        (fun _ (load: float) ->
            {
                Load = Api.createSignal load
                Label = Api.createSignal "idle"
            })
        (div () {
            div (Attrs.className "bar") { () }
            span () { fun g -> g.Label.Value }
        })

let private storyTwin =
    Component.template
        (fun _ () ->
            {
                Progress = Api.createSignal 0.
                Seen = Api.createSignal false
                Puck = Api.createSignal (struct (0., 0.))
            })
        (article () {
            h2 () { "Ranvier" }
            section () { fun s -> sprintf "%.0f %%" (s.Progress.Value * 100.) }
            div (Attrs.className "puck") { "drag" }
        })

let private chartTwin =
    Component.template
        (fun _ () ->
            {
                Fraction = Api.createSignal 0.
                Line = Api.createSignal "M0 0 L100 0"
            })
        (svg () {
            path () { () }
            path () { () }
        })

type Shelf = { Books: string array }

let private presets =
    Component.template
        (fun _ () -> { Books = [| "a"; "b" |] })
        (div () {
            ul (Motion.flip (LayoutParams (duration = 300))) {
                forEachFixed id (fun (s: Shelf) -> s.Books) (li (Motion.rise) { fun (book: string) -> book })
            }

            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li (Motion.scale) { fun (book: string) -> book }) }
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li (Motion.collapse) { fun (book: string) -> book }) }
        })

let private presetsTwin =
    Component.template
        (fun _ () -> { Books = [| "a"; "b" |] })
        (div () {
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li () { fun (book: string) -> book }) }
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li () { fun (book: string) -> book }) }
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li () { fun (book: string) -> book }) }
        })

let private cards = [| { Id = 1; Title = "One" }; { Id = 2; Title = "Two" } |]

let private render (definition: Component<'props>) (props: 'props) =
    Server.render Server.Options.defaults definition props

/// <summary>Each comparison by name: the render with Motion parts and the render of its twin.</summary>
let comparisons: (string * (unit -> string * string)) array =
    [|
        "board", (fun () -> render board cards, render (boardTwinOf false) cards)
        "selected board", (fun () -> render selectedBoard cards, render (boardTwinOf true) cards)
        "banner", (fun () -> render banner "Hello", render bannerTwin "Hello")
        "toasts", (fun () -> render toasts (), render toastsTwin ())
        "notes", (fun () -> render notes (), render notesTwin ())
        "gauge", (fun () -> render gauge 0.5, render gaugeTwin 0.5)
        "story", (fun () -> render story (), render storyTwin ())
        "chart", (fun () -> render chart (), render chartTwin ())
        "presets", (fun () -> render presets (), render presetsTwin ())
    |]

#if !FABLE_COMPILER
[<EntryPoint>]
let main _ =
    let failures =
        [
            for name, compare in comparisons do
                let withMotion, twin = compare ()

                if withMotion = twin then
                    printfn "ok %s" name
                else
                    printfn "FAIL %s\n  motion: %s\n  twin:   %s" name withMotion twin
                    yield name
        ]

    printfn "%d of %d server renders equal their twins" (comparisons.Length - failures.Length) comparisons.Length
    if failures.IsEmpty then 0 else 1
#endif
