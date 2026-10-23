module HydrationPage

open Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.Wire
open Ranvier.Wire.Thoth.Json.Core

type Props = { Greeting: string; Start: int }

let fromQuery (greeting: string) (start: string) : Props =
    let name =
        if System.String.IsNullOrWhiteSpace greeting then
            "Hello from F#"
        else
            let length = min 200 greeting.Length

            let length =
                if
                    length > 0
                    && length < greeting.Length
                    && System.Char.IsHighSurrogate greeting[length - 1]
                    && System.Char.IsLowSurrogate greeting[length]
                then
                    length - 1
                else
                    length

            greeting.Substring (0, length)

    let count =
        match System.Int32.TryParse start with
        | true, value when value >= 0 && value <= 1000000 -> value
        | _ -> 3

    { Greeting = name; Start = count }

let private propsCodec: Codec<Props> =
    objectCodec {
        let! greeting = Codec.field "greeting" _.Greeting Codec.string
        and! start = Codec.field "start" _.Start Codec.int
        return { Greeting = greeting; Start = start }
    }

let private wire = WireCodec.ofCodec propsCodec

type Counter =
    {
        Count: Signal<int>
        Greeting: string
    }

    static member Create(_: Setup, props: Props) =
        {
            Count = createSignal props.Start
            Greeting = props.Greeting
        }

    static member View =
        main (Attrs.className "card") {
            p (Attrs.className "eyebrow") { "RANVIER · F# ON BOTH SIDES" }
            h1 (Attrs.id "greeting") { fun c -> c.Greeting }
            p () { "Rendered in F#, interactive in your browser." }

            div (Attrs.className "counter") {
                p () {
                    "Count: "
                    span (Attrs.id "count", Attrs.attribute ("aria-live", "polite")) { fun c -> string c.Count.Value }
                }

                button (Attrs.id "increment", Events.onClick (fun c _ -> c.Count.Value <- c.Count.Value + 1)) { "Add one" }
            }

            p (Attrs.className "explanation") {
                "The greeting and starting count arrive in the server's HTML. Hydration keeps those elements and connects the button."
            }

            p (Attrs.className "explanation") {
                "On the documentation site, this example uses saved server output. Run it locally to try request-specific greetings and counts."
            }
        }

let private counter = Component.ofMembers<Counter, _>()

let page: Page.Definition<Props> =
    {
        Component =
            Component.define (fun _ props -> props) (fun t ->
                t.fragment
                    [|
                        t.Document.title "Ranvier · F# server hydration"
                        t.Document.meta
                            [|
                                t.Attrs.attribute ("name", "viewport")
                                t.Attrs.attribute ("content", "width=device-width, initial-scale=1")
                            |]
                        t.render counter id
                    |])
        InitialProps = fun () -> fromQuery "" ""
        Options =
            { Server.Options.defaults with
                Policy = Omit
            }
        Document =
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                Stylesheets = [ "./style.css" ]
                Scripts = [ Server.ModuleScript.Src "./assets/client.js" ]
            }
        State =
            Some
                {
                    Id = "hydration-props"
                    Encode = wire.Encode
                    Decode = fun json -> wire.Decode json |> Result.mapError _.Message
                }
    }

let render (props: Props) =
    Page.render
        { page with
            InitialProps = fun () -> props
        }
