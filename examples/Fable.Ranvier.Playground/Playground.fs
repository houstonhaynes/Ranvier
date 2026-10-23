module Playground

open Ranvier.View

let page: Page.Definition<unit> =
    {
        Component =
            Component.define (fun _ () -> ()) (fun t ->
                t.fragment
                    [|
                        t.Document.title "Fable.Ranvier DOM playground"
                        t.Document.meta
                            [|
                                t.Attrs.attribute ("name", "viewport")
                                t.Attrs.attribute ("content", "width=device-width, initial-scale=1")
                            |]
                        t.Document.meta
                            [|
                                t.Attrs.attribute ("name", "description")
                                t.Attrs.attribute ("content", "An F# DOM playground using Fable, Ranvier and Xantham.")
                            |]
                        t.render App.``component`` (fun () -> ())
                    |])
        InitialProps = fun () -> ()
        Options =
            { Server.Options.defaults with
                Policy = Omit
            }
        Document =
            { Server.DocumentShell.defaults with
                Lang = Some "en"
                Stylesheets = [ "./style.css" ]
                Scripts = [ Server.ModuleScript.Src "/generated/generated-pages/Index.js" ]
            }
        State = None
    }
