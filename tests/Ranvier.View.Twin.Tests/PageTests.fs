module Ranvier.View.Twin.Tests.PageTests

open System
open Expecto
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Twin

let private definition view initial : Page.Definition<_> =
    {
        Component = view
        InitialProps = initial
        Document =
            { Server.DocumentShell.defaults with
                HostId = "page-host"
            }
        Options =
            { Server.Options.defaults with
                Policy = Representation.Omit
                IdPrefix = "page"
            }
        State = None
    }

let private numberCodec: Page.StateCodec<int> =
    {
        Id = "page-state"
        Encode = string
        Decode =
            fun json ->
                match Int32.TryParse json with
                | true, value -> Result.Ok value
                | _ -> Result.Error "Expected an integer"
    }

[<Tests>]
let tests =
    testList
        "Page declarations"
        [
            testCase "render evaluates props once and escapes encoded state"
            <| fun () ->
                let calls = ref 0

                let view =
                    Component.define (fun _ (text: string) -> text) (fun t -> t.div [||] [| t.text id |])

                let page =
                    { definition view (fun () ->
                          calls.Value <- calls.Value + 1
                          "value") with
                        State =
                            Some
                                {
                                    Id = "page-state"
                                    Encode = (fun _ -> "\"</script>\u2028\u2029\"")
                                    Decode = (fun _ -> Result.Ok "value")
                                }
                    }

                let html = Page.render page
                Expect.equal calls.Value 1 "one initial value"
                Expect.stringContains html "<div id=\"page-host\">" "declared host"
                Expect.stringContains html "value</div>" "initial value rendered"
                Expect.stringContains html "\"\\u003c/script>\\u2028\\u2029\"</script>" "script text safely escaped"

            testCase "client decodes state and reuses server nodes"
            <| fun () ->
                let calls = ref 0

                let page =
                    { definition Fixtures.counter (fun () ->
                          calls.Value <- calls.Value + 1
                          7) with
                        State = Some numberCodec
                    }

                use document = (TwinHost.Create ()).Document
                Html.parseDocument document (Page.render page)
                let host = document.ElementById "page-host"

                let button =
                    host.Children
                    |> Seq.find (fun node -> node.Kind = NodeKind.Element)

                use started = PageClient.start page

                let after =
                    host.Children
                    |> Seq.find (fun node -> node.Kind = NodeKind.Element)

                Expect.isTrue (obj.ReferenceEquals (button, after)) "server button adopted"
                Expect.equal calls.Value 1 "client used state instead of calling initial factory"
                (button.Native :?> Fable.Core.TS.Dom.HTMLElement).click()
                Expect.equal host.TextContent "8" "hydrated event updates graph"
                started.Dispose ()
                Expect.isTrue started.Graph.Root.IsDisposed "owned graph disposed"
                Expect.equal host.TextContent "" "mounted nodes removed"
                started.Dispose ()

            testCase "stateless client calls its initial factory once and preserves id prefix"
            <| fun () ->
                let calls = ref 0

                let view =
                    Component.define (fun setup () -> setup.CreateId ()) (fun t -> t.div [| t.Attrs.id id |] [||])

                let page = definition view (fun () -> calls.Value <- calls.Value + 1)
                use document = (TwinHost.Create ()).Document
                Html.parseDocument document (Page.render page)
                let before = Html.inner (document.ElementById "page-host")
                use started = PageClient.start page
                Expect.equal calls.Value 2 "once for server and once for client"
                Expect.equal (Html.inner (document.ElementById "page-host")) before "identity unchanged"

            testCase "missing host and required state fail explicitly"
            <| fun () ->
                let page =
                    { definition Fixtures.counter (fun () -> failwith "factory must not run") with
                        State = Some numberCodec
                    }

                use document = (TwinHost.Create ()).Document

                Expect.throwsC (fun () -> PageClient.start page |> ignore) (fun error ->
                    Expect.stringContains error.Message "Page host is missing: page-host" "host diagnostic")

                Html.parseInto document.Body "<div id=\"page-host\"></div>"

                Expect.throwsC (fun () -> PageClient.start page |> ignore) (fun error ->
                    Expect.stringContains error.Message "Page state is missing: page-state" "state diagnostic")

            testCase "invalid state reports decoder diagnostic"
            <| fun () ->
                let page =
                    { definition Fixtures.counter (fun () -> 7) with
                        State = Some numberCodec
                    }

                use document = (TwinHost.Create ()).Document
                Html.parseDocument document ((Page.render page).Replace(">7</script>", ">bad</script>"))

                Expect.throwsC (fun () -> PageClient.start page |> ignore) (fun error ->
                    Expect.stringContains error.Message "Page state page-state: Expected an integer" "decoder diagnostic")

            testCase "missing hydration markers fail without mounting"
            <| fun () ->
                let page = definition Fixtures.counter (fun () -> 7)
                use document = (TwinHost.Create ()).Document
                Html.parseInto document.Body "<div id=\"page-host\"><p>foreign</p></div>"
                let host = document.ElementById "page-host"
                Expect.throws (fun () -> PageClient.start page |> ignore) "strict hydration"
                Expect.equal (Html.inner host) "<p>foreign</p>" "host unchanged"

            testCase "cleanup failure still disposes the owned graph"
            <| fun () ->
                let failing = ref false
                let cleanups = ref 0

                let view =
                    Component.define
                        (fun setup () ->
                            setup.OnCleanup (fun () ->
                                cleanups.Value <- cleanups.Value + 1

                                if failing.Value then
                                    failwith "cleanup"))
                        (fun t -> t.div [||] [||])

                let page = definition view ignore
                use document = (TwinHost.Create ()).Document
                Html.parseDocument document (Page.render page)
                let started = PageClient.start page
                failing.Value <- true

                try
                    started.Dispose ()
                with _ ->
                    ()

                Expect.equal cleanups.Value 2 "server and client cleanup both ran"
                Expect.isTrue started.Graph.Root.IsDisposed "graph disposed in finally"
                started.Dispose ()

            testCase "failed client setup disposes its graph"
            <| fun () ->
                let failing = ref false
                let captured = ref Unchecked.defaultof<Graph>

                let view =
                    Component.define
                        (fun _ () ->
                            if failing.Value then
                                captured.Value <- Graph.Current
                                failwith "setup")
                        (fun t -> t.div [||] [||])

                let page = definition view ignore
                use document = (TwinHost.Create ()).Document
                Html.parseDocument document (Page.render page)
                failing.Value <- true
                Expect.throws (fun () -> PageClient.start page |> ignore) "setup failed"
                Expect.isTrue captured.Value.Root.IsDisposed "failed startup released its graph"

            testCase "generated startup uses qualified F# declarations and rejects injected source"
            <| fun () ->
                Expect.equal
                    (Page.clientSource "Generated.Kanban" "Motion.Kanban.page")
                    "module Generated.Kanban\n\nlet page = Ranvier.View.Dom.PageClient.start Motion.Kanban.page\n"
                    "deterministic F# source"

                for bad in
                    [
                        ""
                        "A..B"
                        "A; failwith \"injected\""
                        "A\nB"
                        "1Bad"
                        "A.type"
                        "module"
                        "null"
                        "asr"
                        "land"
                        "lor"
                        "lsl"
                        "lsr"
                        "lxor"
                        "mod"
                        "A.mod"
                        "A.``quoted``"
                        "_"
                    ] do
                    Expect.throwsT<ArgumentException> (fun () -> Page.clientSource bad "Motion.page" |> ignore) "bad module rejected"
                    Expect.throwsT<ArgumentException> (fun () -> Page.clientSource "Generated.Page" bad |> ignore) "bad declaration rejected"
        ]
