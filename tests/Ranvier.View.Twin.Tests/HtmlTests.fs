module Ranvier.View.Twin.Tests.HtmlTests

open Expecto
open Ranvier.View.Twin

let private roundTrip (html: string) =
    use document = new TwinDocument ()
    Html.inner (Html.parse document html)

let private rejects (html: string) (construct: string) =
    use document = new TwinDocument ()

    Expect.throwsC (fun () -> Html.parse document html |> ignore) (fun e ->
        match e with
        | Html.TwinParseException (found, line, column) ->
            Expect.stringContains found construct "construct"
            Expect.isGreaterThan line 0 "line"
            Expect.isGreaterThan column 0 "column"
        | other -> failtestf "unexpected %O" other)

/// <summary>
/// Markup the HTML parser restructures, with the construct the twin's rejection names; restructured.test.mjs checks
/// each against jsdom.
/// </summary>
let restructured: (string * string) list =
    System.Text.Json.JsonSerializer.Deserialize<string array array>(
        System.IO.File.ReadAllText (System.IO.Path.Combine (__SOURCE_DIRECTORY__, "restructured.json"))
    )
    |> Array.map (fun pair -> pair[0], pair[1])
    |> List.ofArray

[<Tests>]
let tests =
    testList
        "Html"
        [
            testCase "xmp, iframe, noembed and noframes hold their content as one text node"
            <| fun () ->
                for tag in [ "xmp"; "iframe"; "noembed"; "noframes" ] do
                    use document = new TwinDocument ()
                    let fragment = Html.parse document $"<{tag}><b>x</b> &amp;</{tag}>"
                    let element = fragment.FirstChild

                    Expect.equal [ for c in element.Children -> c.Kind ] [ NodeKind.Text ] $"{tag} has one text child"

                    Expect.equal element.TextContent "<b>x</b> &amp;" $"{tag} text is raw"

            testCase "a form start tag in a fragment parsed inside a form is rejected"
            <| fun () ->
                use document = new TwinDocument ()
                let form = document.CreateElement "form"
                let div = document.CreateElement "div"
                form.AppendChild div |> ignore

                Expect.throwsC (fun () -> Html.parseInto div "<form><i>q</i></form>") (fun e ->
                    match e with
                    | Html.TwinParseException (construct, _, _) -> Expect.stringContains construct "nested <form>" "construct"
                    | other -> failtestf "unexpected %O" other)

            testCase "markers, attributes and void elements round trip"
            <| fun () ->
                let html =
                    "<!--[r--><div class=\"panel\" title=\"T &amp;\">Hello <input value=\"x\"><!--]--></div>"

                Expect.equal (roundTrip html) html "round trip"

            testCase "unquoted and single-quoted attribute values parse"
            <| fun () -> Expect.equal (roundTrip "<p a=1 b='2'></p>") "<p a=\"1\" b=\"2\"></p>" "normalised quotes"

            testCase "tags and attribute names are lowercased"
            <| fun () -> Expect.equal (roundTrip "<DIV ID=\"a\"></DIV>") "<div id=\"a\"></div>" "lowercased"

            testCase "character references decode"
            <| fun () -> Expect.equal (roundTrip "<p>&lt;&gt;&amp;&quot;&apos;&nbsp;&#65;&#x42;</p>") "<p>&lt;&gt;&amp;\"'&nbsp;AB</p>" "decoded"

            testCase "raw text and RCDATA keep their content"
            <| fun () ->
                Expect.equal (roundTrip "<script>if (a < b) {}</script>") "<script>if (a < b) {}</script>" "script raw"
                Expect.equal (roundTrip "<textarea>&lt;b&gt;</textarea>") "<textarea>&lt;b&gt;</textarea>" "textarea decodes"

            testCase "the leading line feed of pre and textarea is dropped"
            <| fun () ->
                Expect.equal (roundTrip "<pre>\nx</pre>") "<pre>x</pre>" "pre"
                Expect.equal (roundTrip "<textarea>\ny</textarea>") "<textarea>y</textarea>" "textarea"

            testCase "template content parses into the content fragment"
            <| fun () ->
                use document = new TwinDocument ()
                let fragment = Html.parse document "<template><b>x</b></template>"
                let template = fragment.FirstChild :?> TwinElement
                Expect.isEmpty template.Children "no direct children"
                Expect.equal (Html.inner template.Content) "<b>x</b>" "content"
                Expect.equal (Html.inner fragment) "<template><b>x</b></template>" "serialised"

            testCase "serialisers escape the jsdom and server sets"
            <| fun () ->
                use document = new TwinDocument ()
                let p = document.CreateElement "p"
                p.SetAttribute ("title", "a&b\"c <d>\re")

                p.AppendChild (document.CreateText "x&y<z> \"q\r")
                |> ignore

                let host = document.CreateElement "div"
                host.AppendChild p |> ignore
                Expect.equal (Html.inner host) "<p title=\"a&amp;b&quot;c&nbsp;<d>\re\">x&amp;y&lt;z&gt;&nbsp;\"q\r</p>" "jsdom"
                Expect.equal (Html.server host) "<p title=\"a&amp;b&quot;c &lt;d&gt;&#13;e\">x&amp;y&lt;z&gt; \"q&#13;</p>" "server"
                Expect.equal (Html.server (Html.parse document (Html.server host))) (Html.server host) "server round trip"

            testCase "carriage returns in the input are normalised to line feeds"
            <| fun () -> Expect.equal (roundTrip "<p>a\r\nb\rc</p>") "<p>a\nb\nc</p>" "normalised"

            testCase "server text with a leading line feed in pre keeps its doubled line feed"
            <| fun () ->
                use document = new TwinDocument ()
                let s = "<pre>\n\nx</pre>"
                Expect.equal (Html.server (Html.parse document s)) s "Server writes a second line feed"

            testCase "an implied end tag is rejected"
            <| fun () -> rejects "<p>a<div>b</div></p>" "implied end tag"

            testCase "a table without tbody is rejected"
            <| fun () -> rejects "<table><tr><td>a</td></tr></table>" "<table> without <tbody>"

            testCase "text inside a table is rejected as foster parenting"
            <| fun () -> rejects "<table>x</table>" "foster parenting"

            testCase "a mismatched end tag is rejected"
            <| fun () -> rejects "<b><i>x</b></i>" "end tag </b>"

            testCase "the full named reference table decodes, legacy names included"
            <| fun () ->
                Expect.equal (roundTrip "<p>&copy; &hellip; &copy</p>") "<p>© … ©</p>" "named"

                Expect.equal
                    (roundTrip "<p>&zzz; &notin; &notx;</p>")
                    "<p>&amp;zzz; ∉ ¬x;</p>"
                    "unknown names stay literal; the longest legacy prefix decodes"

            testCase "an ampersand before = or an alphanumeric in an attribute stays literal"
            <| fun () -> Expect.equal (roundTrip "<a href=\"?a=1&b=2&copy=3\"></a>") "<a href=\"?a=1&amp;b=2&amp;copy=3\"></a>" "attribute"

            testCase "numeric references in the C1 range map as the HTML parser maps them"
            <| fun () -> Expect.equal (roundTrip "<p>&#128;&#x9f;</p>") "<p>€Ÿ</p>" "windows-1252"

            testCase "SVG tag and attribute names keep their case"
            <| fun () ->
                let html =
                    "<svg viewBox=\"0 0 1 1\" preserveAspectRatio=\"none\"><linearGradient gradientUnits=\"x\"></linearGradient></svg>"

                Expect.equal (roundTrip (html.ToLowerInvariant ())) html "adjusted"

            testCase "a self-closing SVG child closes itself"
            <| fun () -> Expect.equal (roundTrip "<svg><path d=\"M0\"/><g></g></svg>") "<svg><path d=\"M0\"></path><g></g></svg>" "foreign />"

            testCase "a comment ending in --!> ends there"
            <| fun () -> Expect.equal (roundTrip "<!--x--!><!--[-->y") "<!--x--><!--[-->y" "comment end"

            testCase "table parts outside a table are rejected and allowed in a template"
            <| fun () ->
                rejects "<div><col></div>" "table part (<col>)"
                rejects "<tr><td>a</td></tr>" "table part (<tr>)"
                Expect.equal (roundTrip "<template><tr><td>a</td></tr></template>") "<template><tr><td>a</td></tr></template>" "template"

            testCase "a document shell parses into the document's own elements"
            <| fun () ->
                use document = new TwinDocument ()

                Html.parseDocument document "<!doctype html><html lang=\"en\"><head><title>T</title></head><body class=\"b\"><p>x</p></body></html>"

                Expect.equal (document.Html.GetAttribute "lang") "en" "html attributes"
                Expect.equal (Html.inner document.Head) "<title>T</title>" "head"
                Expect.equal (Html.inner document.Body) "<p>x</p>" "body"
                Expect.equal (document.Body.GetAttribute "class") "b" "body attributes"

            testCase "document-level tags outside a document parse are rejected"
            <| fun () -> rejects "<body><p>x</p></body>" "document-level tag <body>"

            testCase "markup the HTML parser restructures is rejected"
            <| fun () ->
                for html, construct in restructured do
                    rejects html construct

            testCase "markup inside the subset that keeps its nesting is accepted"
            <| fun () ->
                for html, expected in
                    [
                        "<p><span>x</span></p>", "<p><span>x</span></p>"
                        "<div><p>a</p><div>b</div></div>", "<div><p>a</p><div>b</div></div>"
                        "<button><div>x</div></button>", "<button><div>x</div></button>"
                        "<ul><li><ul><li>x</li></ul></li></ul>", "<ul><li><ul><li>x</li></ul></li></ul>"
                        "<svg><foreignObject><div>x</div></foreignObject></svg>", "<svg><foreignObject><div>x</div></foreignObject></svg>"
                        "<h1><span>x</span></h1><h2>y</h2>", "<h1><span>x</span></h1><h2>y</h2>"
                    ] do
                    Expect.equal (roundTrip html) expected html

            testCase "raw text ends only at a complete end tag"
            <| fun () ->
                Expect.equal (roundTrip "<script>a</scripty>b</script>") "<script>a</scripty>b</script>" "</scripty> is text"
                rejects "<script>a</script x>" "unterminated </script>"

            testCase "a numeric reference with leading zeros decodes"
            <| fun () -> Expect.equal (roundTrip "<p>&#0000000065;</p>") "<p>A</p>" "leading zeros"
        ]
