module Ranvier.View.WebCore.Tests.ObjectTests

open System
open System.Runtime.CompilerServices
open Expecto
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View.WebCore
open Ranvier.View.WebCore.Tests.Support

[<MethodImpl(MethodImplOptions.NoInlining)>]
let private createElements (document: Document) (count: int) =
    for _ in 1..count do
        document.createElement "span" |> ignore

let private collect (host: WebCoreHost) =
    GC.Collect ()
    GC.WaitForPendingFinalizers ()
    host.Cycle ()

[<Tests>]
let tests =
    testList
        "Objects"
        [
            hostCase "one native node has one managed object"
            <| fun host ->
                let body = host.Document.body
                Expect.isTrue (obj.ReferenceEquals (body, host.Document.body)) "body read twice"
                let child = host.Document.createElement "p"
                body.appendChild child |> ignore
                Expect.isTrue (obj.ReferenceEquals (child, body.lastChild.Value)) "the appended child read back"
                Expect.isTrue (obj.ReferenceEquals (body, child.parentNode.Value)) "the parent read back"

            hostCase "an element gets the class of its tag"
            <| fun host ->
                let document = host.Document
                Expect.isTrue (box (document.createElement "input") :? HTMLInputElement) "input"
                Expect.isTrue (box (document.createElement "template") :? HTMLTemplateElement) "template"
                Expect.isTrue (box document.head :? HTMLHeadElement) "head"
                Expect.isTrue (box (document.createElement "x-custom") :? HTMLElement) "an unknown HTML tag"
                Expect.isTrue (box (document.createTextNode "t") :? Text) "text"
                Expect.isTrue (box (document.createDocumentFragment ()) :? DocumentFragment) "fragment"

            hostCase "a collected object releases its reference on the next cycle"
            <| fun host ->
                collect host
                let released = host.Session.Released
                createElements host.Document 100
                collect host
                Expect.isGreaterThanOrEqual (host.Session.Released - released) 100 "the 100 unclaimed elements"

            hostCase "an owner keeps the objects it claimed until its disposal"
            <| fun host ->
                collect host
                use graph = new Graph ()

                let owner =
                    graph.Run (fun () ->
                        createRoot (fun owner ->
                            createElements host.Document 50
                            owner))

                let released = host.Session.Released
                collect host
                Expect.equal (host.Session.Released - released) 0 "claimed objects survive a collection"
                graph.Run (fun () -> owner.Dispose ())
                collect host
                Expect.isGreaterThanOrEqual (host.Session.Released - released) 50 "released after the owner's disposal"

            testCase "host disposal releases the references of live objects"
            <| fun () ->
                onHostThread (fun () ->
                    let host = WebCoreHost.Create (100, 100)
                    let element = host.Document.createElement "div"
                    Expect.notEqual (box element :?> NativeObject).Handle 0n "a live handle"
                    (host :> IDisposable).Dispose()
                    Expect.equal (box element :?> NativeObject).Handle 0n "released at disposal")

            hostCase "an unmapped member raises NotSupportedException naming it"
            <| fun host ->
                let element = host.Document.createElement "div"

                Expect.throwsC (fun () -> element.onclick |> ignore) (fun e ->
                    Expect.isTrue (e :? NotSupportedException) "the exception type"
                    Expect.stringContains e.Message "GlobalEventHandlers.onclick is not available in the native backend" "the message")

            hostCase "a DOM exception carries its name"
            <| fun host ->
                let parent = host.Document.createElement "div"
                let stranger = host.Document.createElement "span"

                Expect.throwsC (fun () -> parent.removeChild stranger |> ignore) (fun e ->
                    match e with
                    | WebCoreDomException (name, _) -> Expect.equal name "NotFoundError" "the DOM name"
                    | other -> failtestf "expected WebCoreDomException, got %O" other)

            hostCase "generated members read and write the page"
            <| fun host ->
                let input = host.Document.createElement "input" :?> HTMLInputElement
                input.value <- "typed"
                Expect.equal input.value "typed" "HTMLInputElement.value"
                input.className <- "a b"
                Expect.isTrue (input.classList.contains "b") "Element.classList"
                (input :> ElementCSSInlineStyle).style.color <- "red"
                Expect.equal (input.getAttribute "style") (Some "color: red;") "a CSS property through setProperty"
                let template = host.Document.createElement "template" :?> HTMLTemplateElement
                template.innerHTML <- "<li>one</li><li>two</li>"
                Expect.equal template.content.childNodes.length 2.0 "template content"
                Expect.equal template.innerHTML "<li>one</li><li>two</li>" "template innerHTML"
                let attributes = (input :> Element).attributes
                Expect.equal attributes.length 2.0 "attribute count"
                Expect.equal (attributes.item 0.0).Value.name "class" "first attribute"
        ]
