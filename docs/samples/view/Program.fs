/// Renders the server-side samples and compares the HTML with the HTML their pages quote.
module Program

open global.Ranvier
open Ranvier.View

let private omit = { Server.Options.defaults with Policy = Omit }

let private failures = ResizeArray<string>()

let private expect (name: string) (expected: string) (actual: string) =
    if expected <> actual then
        failures.Add (sprintf "%s:\n  expected %s\n  actual   %s" name expected actual)

let private root (body: string) = "<!--[r-->" + body + "<!--]-->"

[<EntryPoint>]
let main _ =
    use graph = new Graph ()

    graph.Run (fun () ->
        let counterHtml =
            root """<div class="counter"><span class="label">Clicks</span><button type="button">0</button></div>"""

        expect "getting-started counter" counterHtml (Server.render omit Counter.counter { Start = 0; Label = "Clicks" })
        expect "getting-started explicit" counterHtml (Server.render omit CounterExplicit.counter { Start = 0; Label = "Clicks" })
        expect "server/index" counterHtml (Server.render omit Counter.counter { Start = 0; Label = "Clicks" })

        expect
            "templates greeting"
            (root """<p class="greeting">Hello, <!--!-->&lt;Ada&gt;<!--!-->! <!--!-->&lt;ADA&gt;</p>""")
            (Server.render omit Templates.greeting (createSignal "<Ada>"))

        expect
            "templates explicit greeting"
            (root """<p class="greeting">Hello, <!--!-->&lt;Ada&gt;<!--!-->! <!--!-->&lt;ADA&gt;</p>""")
            (Server.render omit Templates.Explicit.greeting (createSignal "<Ada>"))

        expect
            "templates field"
            (root """<form><input value=""><button disabled="">Save</button></form>""")
            (Server.render omit Templates.field { Text = createSignal ""; Busy = createSignal false })

        expect
            "components labelled"
            (root """<div><label for="r-0-0">Name</label><input id="r-0-0"></div>""")
            (Server.render omit Components.labelled "Name")

        expect
            "collections todoList"
            (root """<ul class="todos"><!--[--><li class="done">Write</li><li class="open">Ship</li><!--]--></ul>""")
            (Server.render
                omit
                Collections.todoList
                (createSignal
                    [|
                        { Id = 1; Label = createSignal "Write"; Done = createSignal true }
                        { Id = 2; Label = createSignal "Ship"; Done = createSignal false }
                    |]))

        expect
            "arms loader"
            (root """<div class="loader"><!--[a:220727aa--><span>Not started</span><!--]--></div>""")
            (Server.render omit Arms.loader (createSignal Arms.Idle))

        expect
            "arms Arms.Match loader"
            (Server.render omit Arms.loader (createSignal (Arms.Loaded "done")))
            (Server.render omit Arms.loaderMatch (createSignal (Arms.Loaded "done")))

        expect
            "arms Arms.Match idle"
            (root """<div class="loader"><!--[a:220727aa--><span>Not started</span><!--]--></div>""")
            (Server.render omit Arms.loaderMatch (createSignal Arms.Idle))

        let name = createAsyncSource<string> ()
        let profile = Boundaries.profileView { Name = name }

        expect
            "boundaries pending"
            (root """<section><!--[p--><p class="loading">Loading…</p><!--]--></section>""")
            (Server.render omit profile ())

        name.Settle "Ada"

        expect
            "boundaries ready"
            (root """<section><!--[c--><article><h2>Ada</h2></article><!--]--></section>""")
            (Server.render omit profile ())

        let failing = createAsyncSource<string> ()
        failing.Fail (exn "offline")

        expect
            "boundaries failed"
            (root """<section><!--[e--><p class="error">offline<button>Retry</button></p><!--]--></section>""")
            (Server.render omit (Boundaries.profileView { Name = failing }) ())

        let page = DocumentHead.renderArticle ()
        expect "document-head body" (root """<article><h1>Tides</h1></article>""") page.Body

        expect
            "document-head head"
            """<title data-ranvier-head="title">Tides · Journal</title><meta data-ranvier-head="meta:name=description" name="description" content="On &quot;moons&quot;"><link data-ranvier-head="link:canonical" rel="canonical" href="/articles/tides">"""
            page.Head

        expect
            "authored snapshot"
            """<section class="rows"><button>Add row</button><div id="row-1">Alpha</div></section>"""
            (Authored.snapshot (createSignal [| 1, "Alpha" |]))

        expect
            "state-transfer script"
            """<script type="application/json" id="counter-props">{"start":2,"label":"\u003c/script>"}</script>"""
            (StateTransfer.stateElement { Start = 2; Label = "</script>" })

        expect
            "deployment page"
            ("""<!doctype html><html lang="en"><head><meta charset="utf-8"></head><body><div id="app">"""
             + counterHtml
             + """</div><script type="application/json" id="counter-props">{"start":0,"label":"Clicks"}</script><script type="module" src="/main.js"></script></body></html>""")
            (ServerPage.page { Start = 0; Label = "Clicks" })


        expect "templates rule root" (root """<hr class="rule">""") (Server.render omit Templates.rule ())
        expect "templates avatar" (root """<span class="avatar"><!--[a:b1b2a8ad--><img alt="avatar"><!--]--></span>""") (Server.render omit Templates.avatar (createSignal true))
        expect "components soldOut" (root """<span class="badge">sold out</span>""") (Server.render omit Components.soldOut ())

        let unread = createAsyncSource<string> ()

        let unguarded =
            Template.Component.define (fun _ () -> ()) (fun t -> t.p [||] [| t.text (fun () -> unread.Value) |])

        expect
            "rendering pending"
            "pending"
            (match Rendering.tryRender unguarded with
             | Error _ -> "pending"
             | Ok html -> html)

        let tasks: MotionParts.Task array =
            [|
                { Id = 1; Title = "Write" }
                { Id = 2; Title = "Ship" }
            |]

        let taskListHtml =
            root """<section><ul class="tasks"><!--[--><li>Write</li><li>Ship</li><!--]--></ul><div class="progress"></div></section>"""

        expect "motion-parts taskList" taskListHtml (Server.render omit MotionParts.taskList tasks)
        expect "motion-parts taskList under Reject" taskListHtml (Server.render Server.Options.defaults MotionParts.taskList tasks)

        CounterHtml.rendersStartValue ())

    if failures.Count = 0 then
        printfn "All view samples render the HTML their pages quote."
        0
    else
        for failure in failures do
            eprintfn "%s" failure

        1
