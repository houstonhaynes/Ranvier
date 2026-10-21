module Ranvier.View.Twin.Tests.BridgeTests

open System
open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Twin

[<Tests>]
let tests =
    testList
        "Bridge"
        [
            testCase "a node has one bridged object"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                Expect.isTrue (obj.ReferenceEquals (div.Native, div.Native)) "same object"
                Expect.equal (Bridge.nodeOf div.Native) (div :> TwinNode) "back to the node"

            testCase "the interface follows the tag"
            <| fun () ->
                use document = new TwinDocument ()
                Expect.isTrue ((document.CreateElement "input").Native :? HTMLInputElement) "input"
                Expect.isTrue ((document.CreateElement "template").Native :? HTMLTemplateElement) "template"
                Expect.isTrue ((document.CreateElement "button").Native :? HTMLButtonElement) "button"
                Expect.isTrue ((document.CreateElement "section").Native :? HTMLElement) "section"
                Expect.isTrue ((document.CreateText "x").Native :? Text) "text"
                Expect.isTrue ((document.CreateComment "x").Native :? Comment) "comment"
                Expect.isTrue ((document.CreateElement "input").Native :? ARIAMixin) "inherited interfaces"

            testCase "nullable returns are options and numbers are floats"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native
                Expect.isNone div.firstChild "no child"
                Expect.equal div.nodeType 1.0 "nodeType"
                let text = document.CreateText "t"
                div.appendChild text.Native |> ignore
                Expect.isTrue (obj.ReferenceEquals (div.firstChild.Value, text.Native)) "Some child"

            testCase "generated property writers reach the form state"
            <| fun () ->
                use document = new TwinDocument ()
                let input = document.CreateElement "input"
                (input.Native :?> HTMLInputElement).value <- "x"
                (input.Native :?> HTMLInputElement).``checked`` <- true
                Expect.equal (input.Value, input.Checked) ("x", true) "written"

            testCase "an unimplemented member names its interface and member"
            <| fun () ->
                use document = new TwinDocument ()
                let element = (document.CreateElement "div").Native :?> HTMLElement

                Expect.throwsC (fun () -> element.offsetWidth |> ignore) (fun e ->
                    Expect.isTrue (e :? NotSupportedException) "NotSupportedException"
                    Expect.equal e.Message "The DOM twin does not implement HTMLElement.offsetWidth" "message")

            testCase "innerHTML parses and serialises"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native :?> Element
                div.innerHTML <- "<b>x</b><!--m-->"
                Expect.equal div.innerHTML "<b>x</b><!--m-->" "round trip"
                Expect.equal div.childNodes.length 2.0 "children"

            testCase "served members are recorded by interface on the calling thread"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"

                let served =
                    Bridge.servedDuring (fun () ->
                        div.Native.nodeType |> ignore
                        div.Native.childNodes.item 0. |> ignore)

                Expect.equal served [ "Node.childNodes"; "Node.nodeType"; "NodeListOf.item" ] "recorded without arity suffixes"

            testCase "a member serves the interfaces that declare it, and only those"
            <| fun () ->
                use document = new TwinDocument ()

                let element tag =
                    (document.CreateElement tag).Native

                let unsupported name (read: unit -> obj) =
                    Expect.throwsC (fun () -> read () |> ignore) (fun e ->
                        Expect.isTrue (e :? NotSupportedException) $"{name}: NotSupportedException, got {e.GetType().Name}"
                        Expect.equal e.Message $"The DOM twin does not implement {name}" "message")

                Expect.equal (element "textarea" :?> HTMLTextAreaElement).``type`` "textarea" "textarea type"
                Expect.equal (element "input" :?> HTMLInputElement).``type`` "text" "input type"
                unsupported "HTMLSelectElement.type" (fun () -> box (element "select" :?> HTMLSelectElement).``type``)
                unsupported "HTMLLIElement.value" (fun () -> box (element "li" :?> HTMLLIElement).value)
                unsupported "HTMLAnchorElement.type" (fun () -> box (element "a" :?> HTMLAnchorElement).``type``)
                unsupported "HTMLOutputElement.value" (fun () -> box (element "output" :?> HTMLOutputElement).value)

            testCase "the document's textContent is null and its writer does nothing"
            <| fun () ->
                use document = new TwinDocument ()

                document.Body.AppendChild (document.CreateText "x")
                |> ignore

                let native = document.Native
                Expect.isNone native.textContent "null"
                native.textContent <- Some "y"
                Expect.equal document.Body.TextContent "x" "unchanged"

            testCase "an interception wraps a method of one node or of every node until it is removed"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                let other = document.CreateElement "div"
                let seen = ResizeArray<string>()

                let wrap label =
                    Func<Func<obj array, obj>, obj array, obj>(fun original args ->
                        seen.Add (label + ":" + (unbox<Node> args[0]).nodeName)
                        original.Invoke args)

                let one = Bridge.intercept document div "appendChild" (wrap "one")
                let all = Bridge.intercept document null "appendChild" (wrap "all")

                div.Native.appendChild ((document.CreateElement "b").Native)
                |> ignore

                other.Native.appendChild ((document.CreateElement "i").Native)
                |> ignore

                Expect.equal (List.ofSeq seen) [ "all:B"; "one:B"; "all:I" ] "the later interception runs first"
                Expect.equal [ for c in div.Children -> (c :?> TwinElement).LocalName ] [ "b" ] "the original ran"
                one ()
                all ()
                seen.Clear ()

                div.Native.appendChild ((document.CreateElement "u").Native)
                |> ignore

                Expect.isEmpty seen "removed"

            testCase "live lists are the same object on every read"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native :?> Element
                Expect.isTrue (obj.ReferenceEquals (div.childNodes, div.childNodes)) "childNodes"
                Expect.isTrue (obj.ReferenceEquals (div.children, div.children)) "children"
                Expect.isTrue (obj.ReferenceEquals (div.attributes, div.attributes)) "attributes"
                div.innerHTML <- "<b></b><i></i>"
                Expect.equal div.childNodes.length 2.0 "live"

            testCase "addEventListener and removeEventListener serve the event-handler mixins of an element"
            <| fun () ->
                use document = new TwinDocument ()
                let button = document.CreateElement "button"
                document.Body.AppendChild button |> ignore
                let native = button.Native :?> HTMLButtonElement
                let clicks = ref 0
                // A ref cell keeps one listener object: the optimiser may otherwise rebuild the closure at each use.
                let listener =
                    ref (Fable.Core.U2.Case1 (fun (_: Event) -> clicks.Value <- clicks.Value + 1))

                native.addEventListener ("click", listener = listener.Value)
                native.click ()
                native.removeEventListener ("click", listener = listener.Value)
                native.click ()
                Expect.equal clicks.Value 1 "added, then removed"

            testCase "event and selector members serve a document created without a host"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                document.Body.AppendChild div |> ignore
                Expect.isSome ((document.Body.Native :?> Element).querySelector "div") "querySelector"
                (div.Native :?> HTMLElement).click()

            testCase "a call from another thread raises"
            <| fun () ->
                use document = new TwinDocument ()
                let div = (document.CreateElement "div").Native
                let failure = ref (null: exn)

                let thread =
                    Threading.Thread (fun () ->
                        try
                            div.nodeType |> ignore
                        with e ->
                            failure.Value <- e)

                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure.Value :? InvalidOperationException) "InvalidOperationException"
        ]
