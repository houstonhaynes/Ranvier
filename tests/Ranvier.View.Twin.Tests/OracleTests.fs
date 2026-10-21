/// <summary>
/// AngleSharp as a test-only oracle for the twin's parser and jsdom serialisation (spec decision 2): every fixture the
/// twin's parser accepts serialises as AngleSharp serialises it, after AngleSharp's leading line feed rule.
/// </summary>
module Ranvier.View.Twin.Tests.OracleTests

open System.Text.RegularExpressions
open Expecto
open Ranvier.View
open Ranvier.View.Twin

let private serverFixtures: string list =
    let options = Server.Options.defaults

    [
        Server.render options ServerFixtures.greeting ServerFixtures.greetingProps
        Server.render options ServerFixtures.named ServerFixtures.greetingProps
        Server.render options ServerFixtures.nullTitle ServerFixtures.greetingProps
        Server.render options ServerFixtures.staged ServerFixtures.Waiting
        Server.render options ServerFixtures.staged (ServerFixtures.Working 3)
        Server.render options ServerFixtures.staged (ServerFixtures.Complete "<done>")
        Server.render options ServerFixtures.decades 42
        Server.render options ServerFixtures.listed ServerFixtures.listedItems
        Server.render options ServerFixtures.carded ServerFixtures.greetingProps
        Server.render options ServerFixtures.pair ServerFixtures.greetingProps
        Server.render options ServerFixtures.staticTemplate ()
        Server.render options ServerFixtures.emptyText ""
    ]

let private markupFixtures: string list =
    [
        "<!--[r--><div class=\"panel\" title=\"T &amp;\">Hello <input value=\"x\"><!--]--></div>"
        "<p a=1 b='2'></p>"
        "<DIV ID=\"a\"></DIV>"
        "<p>&lt;&gt;&amp;&quot;&apos;&nbsp;&#65;&#x42;</p>"
        "<script>if (a < b) {}</script>"
        "<textarea>&lt;b&gt;</textarea>"
        "<pre>\nx</pre>"
        "<pre>\n\nx</pre>"
        "<textarea>\ny</textarea>"
        "<p>a\r\nb\rc</p>"
        "<p>&copy; &hellip; &copy &zzz; &notin; &notx;</p>"
        "<a href=\"?a=1&b=2&copy=3\"></a>"
        "<p>&#128;&#x9f;</p>"
        "<svg viewbox=\"0 0 1 1\" preserveaspectratio=\"none\"><lineargradient gradientunits=\"x\"></lineargradient></svg>"
        "<svg><path d=\"M0\"/><g></g></svg>"
        "<!--x--!><!--[-->y"
        "<ul><li>a</li><li>b</li></ul>"
        "<select><option value=\"a\">A</option><option selected>B</option></select>"
        "<table><tbody><tr><td>a</td></tr></tbody></table>"
        "<p><span>x</span></p>"
        "<div><p>a</p><div>b</div></div>"
        "<button><div>x</div></button>"
        "<ul><li><ul><li>x</li></ul></li></ul>"
        "<svg><foreignObject><div>x</div></foreignObject></svg>"
        "<h1><span>x</span></h1><h2>y</h2>"
        "<script>a</scripty>b</script>"
        "<p title=\"a b\">c d</p>"
    ]

/// <summary>AngleSharp's serialisation with its leading line feed in <c>pre</c>, <c>textarea</c> and <c>listing</c> dropped, as parse5's.</summary>
let private normalise (html: string) =
    Regex.Replace (html, @"<(pre|textarea|listing)(\s[^>]*)?>\n(?=\n)", "<$1$2>")

let private angleSharpInner (html: string) =
    let parser = AngleSharp.Html.Parser.HtmlParser ()
    use document = parser.ParseDocument ""
    let div = document.CreateElement "div"
    div.InnerHtml <- html
    normalise div.InnerHtml

let private twinInner (html: string) : string option =
    use document = new TwinDocument ()
    let div = document.CreateElement "div"

    try
        Html.parseInto div html
        Some (Html.inner div)
    with Html.TwinParseException _ ->
        None

[<Tests>]
let tests =
    testList
        "AngleSharp oracle"
        [
            for fixture in markupFixtures @ serverFixtures do
                testCase $"the twin accepts {fixture} and agrees with AngleSharp"
                <| fun () ->
                    match twinInner fixture with
                    | Some twin -> Expect.equal twin (angleSharpInner fixture) "serialisation"
                    | None -> failtestf "the twin rejected %s, which lies inside its subset" fixture

            testCase "AngleSharp restructures each fixture the twin rejects as restructured markup"
            <| fun () ->
                for fixture, construct in HtmlTests.restructured do
                    Expect.isNone (twinInner fixture) $"rejected: {fixture}"

                    // A MathML text integration point changes namespaces, not the shape. AngleSharp's fragment parser
                    // keeps a breakout tag inside SVG or MathML, where jsdom moves it out after the foreign element.
                    if
                        not (construct.Contains "integration point")
                        && not (construct.Contains "foreign content")
                    then
                        Expect.notEqual (angleSharpInner fixture) fixture $"restructured by AngleSharp: {fixture}"

            testCase "the twin accepts every server fixture"
            <| fun () ->
                for fixture in serverFixtures do
                    Expect.isSome (twinInner fixture) fixture
        ]
