module Ranvier.View.Twin.Tests.SelectorTests

open Expecto
open Fable.Core.TS.Dom
open Ranvier.View.Twin
open Ranvier.View.Testing

let private tree () =
    SelectorMembers.ensure ()
    let document = new TwinDocument ()
    let root = document.CreateElement "div"

    Html.parseInto
        root
        "<ul id=\"l\" class=\"a b\"><li data-k=\"1\">x</li><li data-k=\"22\" class=\"b\">y</li><li><input type=\"checkbox\" checked><button disabled></button></li></ul><p>z</p>"

    document, root.Native

let private markup (html: string) =
    SelectorMembers.ensure ()
    let document = new TwinDocument ()
    let root = document.CreateElement "div"
    Html.parseInto root html
    document, root.Native

let private tags (elements: Element array) =
    elements
    |> Array.map (fun e -> e.localName)
    |> List.ofArray

[<Tests>]
let tests =
    testList
        "Selector"
        [
            testCase "type, id, class and universal selectors"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (Selector.queryAll root "li").Length 3 "type"
                Expect.equal (tags (Selector.queryAll root "#l")) [ "ul" ] "id"
                Expect.equal (Selector.queryAll root ".b").Length 2 "class"
                Expect.equal (Selector.queryAll root "*").Length 7 "universal"

            testCase ":scope matches the query's root, and the element itself in matches"
            <| fun () ->
                let document, root =
                    markup "<div><div><i></i></div></div><section><div><div></div></div></section>"

                use _ = document
                Expect.equal (Selector.queryAll root ":scope > div > div").Length 1 "a child of the root's child"
                let inner = (Selector.queryAll root "i")[0]
                Expect.isTrue (Selector.matches (Selector.parse ":scope") inner) "the element is its own scope"
                Expect.isFalse (Selector.matchesIn root (Selector.parse ":scope") inner) "another scope"

            testCase ":scope in a document's query matches its root element"
            <| fun () ->
                SelectorMembers.ensure ()
                use document = new TwinDocument ()
                Html.parseInto document.Body "<p></p>"
                let root = document.Native :?> Node

                Expect.equal
                    ((Selector.queryAll root ":scope").Length,
                     (Selector.queryAll root ":scope > body").Length,
                     (Selector.queryAll root ":scope > html").Length,
                     (Selector.queryAll root ":scope p").Length)
                    (1, 1, 0, 1)
                    "as jsdom gives"

            testCase "attribute operators"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (Selector.queryAll root "[data-k]").Length 2 "exists"
                Expect.equal (Selector.queryAll root "[data-k=\"1\"]").Length 1 "equals"
                Expect.equal (Selector.queryAll root "[class~=b]").Length 2 "includes"
                Expect.equal (Selector.queryAll root "[data-k^=2]").Length 1 "prefix"
                Expect.equal (Selector.queryAll root "[data-k$='2']").Length 1 "suffix"
                Expect.equal (Selector.queryAll root "[class*=\" b\"]").Length 1 "contains"

            testCase "pseudo-classes"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (tags (Selector.queryAll root ":checked")) [ "input" ] "checked"
                Expect.equal (tags (Selector.queryAll root ":disabled")) [ "button" ] "disabled"
                Expect.equal (Selector.queryAll root "li:first-child").[0].textContent "x" "first-child"
                Expect.equal (Selector.queryAll root "li:last-child").Length 1 "last-child"
                Expect.equal (Selector.queryAll root "li:nth-child(2)").[0].textContent "y" "nth-child"
                Expect.equal (Selector.queryAll root "li:not(.b)").Length 2 "not"

            testCase "combinators and lists keep document order"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal (tags (Selector.queryAll root "ul > li > input")) [ "input" ] "child"
                Expect.equal (tags (Selector.queryAll root "div button")) [ "button" ] "descendant from the root's own tag"
                Expect.equal (tags (Selector.queryAll root "p, ul")) [ "ul"; "p" ] "list in document order"

            testCase "the child combinator requires the parent, not any ancestor"
            <| fun () ->
                let document, root =
                    markup "<div><p><span></span></p></div><ul><li><b><i></i></b></li></ul>"

                use _ = document
                Expect.equal (tags (Selector.queryAll root "div > span")) [] "grandchild"
                Expect.equal (tags (Selector.queryAll root "li > i")) [] "grandchild under li"
                Expect.equal (tags (Selector.queryAll root "p > span")) [ "span" ] "child"
                Expect.equal (tags (Selector.queryAll root "div span")) [ "span" ] "descendant"

            testCase "each combinator in a chain applies between its own two compounds"
            <| fun () ->
                let document, root = markup "<section><div><p><span></span></p></div></section>"
                use _ = document
                Expect.equal (tags (Selector.queryAll root "section > p span")) [] "p is not a child of section"
                Expect.equal (tags (Selector.queryAll root "section > div span")) [ "span" ] "div is"
                Expect.equal (tags (Selector.queryAll root "section p > span")) [ "span" ] "descendant, then child"
                Expect.equal (tags (Selector.queryAll root "section div > span")) [] "span is not a child of div"

            testCase "tabs and line feeds separate compounds and class tokens"
            <| fun () ->
                let document, root = markup "<ul><li class=\"a\tb\">x</li></ul>"
                use _ = document
                Expect.equal (tags (Selector.queryAll root "ul\n>\tli")) [ "li" ] "combinator whitespace"
                Expect.equal (tags (Selector.queryAll root "ul\tli")) [ "li" ] "descendant whitespace"
                Expect.equal (tags (Selector.queryAll root "[class~=b]")) [ "li" ] "includes splits on any whitespace"

            testCase "SVG tag and attribute names match with their case"
            <| fun () ->
                let document, root =
                    markup "<svg viewBox=\"0 0 1 1\"><linearGradient gradientUnits=\"x\"></linearGradient></svg>"

                use _ = document
                Expect.equal (tags (Selector.queryAll root "[viewBox]")) [ "svg" ] "attribute"
                Expect.equal (tags (Selector.queryAll root "linearGradient")) [ "linearGradient" ] "tag"
                Expect.equal (tags (Selector.queryAll root "lineargradient")) [] "tag case"
                Expect.equal (tags (Selector.queryAll root "[gradientUnits=x]")) [ "linearGradient" ] "attribute value"

            testCase "a control in a disabled fieldset is disabled, except in its first legend"
            <| fun () ->
                let document, root =
                    markup "<fieldset disabled><legend><input class=\"in-legend\"></legend><input class=\"inside\"></fieldset>"

                use _ = document

                Expect.equal
                    [
                        for e in Selector.queryAll root ":disabled" -> e.localName + "." + e.className
                    ]
                    [ "fieldset."; "input.inside" ]
                    "disabled"

            testCase "the twin's querySelector is the engine"
            <| fun () ->
                let document, root = tree ()
                use _ = document
                Expect.equal ((root :?> Element).querySelectorAll "li").length 3.0 "querySelectorAll"
                Expect.isSome ((root :?> Element).querySelector "#l") "querySelector"

            testCase "unsupported syntax names the column"
            <| fun () ->
                Expect.throwsC (fun () -> Selector.parse "li:hover" |> ignore) (fun e ->
                    match e with
                    | Selector.SelectorException (selector, column) ->
                        Expect.equal (selector, column) ("li:hover", 2) "0-based column of the pseudo-class colon"
                    | other -> failtestf "unexpected %O" other)
        ]
