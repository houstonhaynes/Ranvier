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
            testCase "meter high clamps to the maximum and the low boundary"
            <| fun () ->
                use document = new TwinDocument ()
                let meter = document.CreateElement "meter"
                let native = meter.Native :?> HTMLMeterElement
                meter.SetAttribute ("max", "0.5")
                meter.SetAttribute ("high", "2")
                Expect.equal native.high 0.5 "clamped to max"
                meter.SetAttribute ("low", "0.4")
                meter.SetAttribute ("high", "0.1")
                Expect.equal native.high 0.4 "raised to low"

            testCase "document.title strips and collapses the title element's whitespace"
            <| fun () ->
                use document = new TwinDocument ()
                let native = document.Native :?> Document
                let title = document.Head.AppendChild (document.CreateElement "title")
                title.TextContent <- "\n  Lamp \t · \r\n Shop  "
                Expect.equal native.title "Lamp · Shop" "stripped and collapsed"
                Expect.equal title.TextContent "\n  Lamp \t · \r\n Shop  " "the text is unchanged"

            testCase "document.title reads the first title element in tree order and only its Text children"
            <| fun () ->
                use document = new TwinDocument ()
                let native = document.Native :?> Document
                let title = document.Body.AppendChild (document.CreateElement "title")

                title.AppendChild (document.CreateText "a")
                |> ignore

                let skipped = title.AppendChild (document.CreateElement "b")

                skipped.AppendChild (document.CreateText "skipped")
                |> ignore

                title.AppendChild (document.CreateText " b")
                |> ignore

                Expect.equal native.title "a b" "a title in the body, its element children skipped"
                native.title <- "c"
                Expect.equal (Html.inner document.Head, Html.inner title) ("", "c") "the setter writes that title"

            testCase "implementation.createHTMLDocument makes a separate titled document"
            <| fun () ->
                use document = new TwinDocument ()
                let native = document.Native :?> Document
                let other = native.implementation.createHTMLDocument "Other"
                Expect.equal other.title "Other" "titled"
                Expect.isFalse (obj.ReferenceEquals (other.head, native.head)) "its own head"

            testCase "aria properties reflect their aria attributes and null removes them"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                let native = div.Native :?> ARIAMixin
                native.ariaRowIndexText <- Some "two"
                Expect.equal (div.GetAttribute "aria-rowindextext") "two" "written"
                Expect.equal native.ariaRowIndexText (Some "two") "read"
                native.ariaRowIndexText <- None
                Expect.isFalse (div.HasAttribute "aria-rowindextext") "removed"

            testCase "mutation observers see each change kind, and a whole-content replacement as one change"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"
                document.Body.AppendChild div |> ignore
                let changes = ResizeArray<string * string>()

                use _ =
                    document.ObserveMutations (fun (kind, target) -> changes.Add ((kind, (unbox<Node> target.Native).nodeName)))

                (div.Native :?> Element).innerHTML <- "<b>x</b><i>y</i>"
                div.SetAttribute ("title", "t")
                (unbox<CharacterData> div.FirstChild.FirstChild.Native).data <- "z"

                Expect.equal (List.ofSeq changes) [ "childList", "DIV"; "attributes", "DIV"; "characterData", "#text" ] "one change per operation"

            testCase "createElementNS with a qualified name gives the element a prefix"
            <| fun () ->
                use document = new TwinDocument ()
                let native = document.Native :?> Document

                let element =
                    native.createElementNS (namespaceURI = Some "http://www.w3.org/1999/xhtml", qualifiedName = "x:b")

                Expect.equal (element.localName, element.prefix, element.tagName, element.outerHTML) ("b", Some "x", "X:B", "<x:b></x:b>") "prefixed"

            testCase "setSelectionRange clamps to the value and a value write moves the selection to the end"
            <| fun () ->
                use document = new TwinDocument ()

                let input =
                    document.CreateElement "input"
                    |> fun e -> e.Native :?> HTMLInputElement

                input.value <- "abcd"
                Expect.equal (input.selectionStart, input.selectionEnd) (Some 4., Some 4.) "at the end after a write"
                input.setSelectionRange (1., 9.)
                Expect.equal (input.selectionStart, input.selectionEnd) (Some 1., Some 4.) "clamped"
                input.setSelectionRange (3., 2.)
                Expect.equal (input.selectionStart, input.selectionEnd) (Some 2., Some 2.) "a start past the end moves to it"

            testCase "selectedIndex in a multiple select, past a disabled option and through an optgroup"
            <| fun () ->
                use document = new TwinDocument ()
                let multiple = document.CreateElement "select"
                multiple.SetAttribute ("multiple", "")
                Html.parseInto multiple "<option>a</option><option selected>b</option><option selected>c</option>"
                let many = multiple.Native :?> HTMLSelectElement
                Expect.equal many.selectedIndex 1. "the first selected option"
                many.selectedIndex <- 0.
                let third = multiple.Children |> List.item 2 :?> TwinElement
                Expect.equal (many.selectedIndex, many.value, third.Selected) (0., "a", false) "selecting one deselects the others"
                let single = document.CreateElement "select"
                Html.parseInto single "<option disabled>a</option><optgroup><option>b</option><option>c</option></optgroup>"
                let one = single.Native :?> HTMLSelectElement
                Expect.equal (one.selectedIndex, one.value) (1., "b") "the first enabled option, inside the optgroup"

            testCase "selectedIndex reads and selects an option by position"
            <| fun () ->
                use document = new TwinDocument ()
                let select = document.CreateElement "select"
                Html.parseInto select "<option>a</option><option selected>b</option><option>c</option>"
                let native = select.Native :?> HTMLSelectElement
                Expect.equal native.selectedIndex 1. "the selected option"
                native.selectedIndex <- 2.
                Expect.equal (native.selectedIndex, native.value) (2., "c") "written"
                native.selectedIndex <- -1.
                Expect.equal (native.selectedIndex, native.value) (-1., "") "none"

            testCase "SVG and MathML elements take their namespace's interfaces"
            <| fun () ->
                use document = new TwinDocument ()
                let svg = document.CreateElement "div"
                Html.parseInto svg "<svg><g></g><a></a></svg><math><mi>x</mi></math>"
                let g = svg.FirstChild.FirstChild
                Expect.isTrue (g.Native :? SVGGElement) "svg g"
                Expect.isFalse (g.Native :? HTMLElement) "not an HTML element"
                Expect.isTrue (svg.FirstChild.LastChild.Native :? SVGAElement) "svg a"
                Expect.isTrue (svg.LastChild.FirstChild.Native :? MathMLElement) "math mi"

            testCase "meter high and popover reflect their attributes"
            <| fun () ->
                use document = new TwinDocument ()
                let meter = document.CreateElement "meter"
                let native = meter.Native :?> HTMLMeterElement
                Expect.equal native.high 1.0 "the maximum by default"
                native.high <- 0.25
                Expect.equal (meter.GetAttribute "high", native.high) ("0.25", 0.25) "written"
                let span = document.CreateElement "span"
                let element = span.Native :?> HTMLElement
                Expect.isNone element.popover "absent"
                element.popover <- Some ""
                Expect.equal element.popover (Some "auto") "empty is auto"
                span.SetAttribute ("popover", "other")
                Expect.equal element.popover (Some "manual") "unknown is manual"
                element.popover <- None
                Expect.isFalse (span.HasAttribute "popover") "removed"

            testCase "replaceWith keeps a node among its own replacements and does nothing without a parent"
            <| fun () ->
                use document = new TwinDocument ()
                let parent = document.CreateElement "div"
                Html.parseInto parent "<a></a><b></b><i></i>"
                let a = parent.FirstChild
                (a.Native :?> ChildNode).replaceWith [| Fable.Core.U2.Case2 a.Native |]
                Expect.equal (Html.inner parent) "<a></a><b></b><i></i>" "self"

                (a.Native :?> ChildNode).replaceWith [| Fable.Core.U2.Case2 a.NextSibling.Native; Fable.Core.U2.Case2 a.Native |]

                Expect.equal (Html.inner parent) "<b></b><a></a><i></i>" "the next sibling, then self"
                let lone = document.CreateElement "u"
                (lone.Native :?> ChildNode).replaceWith [| Fable.Core.U2.Case1 "t" |]
                Expect.isNull lone.Parent "parentless"

            testCase "list items are read without rebuilding the list"
            <| fun () ->
                use document = new TwinDocument ()
                let div = document.CreateElement "div"

                for i in 1..2000 do
                    div.AppendChild (document.CreateElement "i")
                    |> ignore

                let list = div.Native.childNodes
                let before = ListBuilds.Count

                for i in 0 .. int list.length - 1 do
                    list.item (float i) |> ignore

                Expect.equal (ListBuilds.Count - before) 1 "one build for the whole iteration"

                div.AppendChild (document.CreateElement "b")
                |> ignore

                Expect.equal list.length 2001.0 "the cache follows a mutation"
                Expect.equal (unbox<Element>(list.item 2000.)).localName "b" "the appended item"

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
                    Func<Func<obj array, obj>, obj, obj array, obj>(fun original _ args ->
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
