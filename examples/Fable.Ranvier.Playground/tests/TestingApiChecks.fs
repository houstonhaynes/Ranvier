module TestingApiChecks

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Testing

type private FormState =
    {
        Name: Signal<string>
        Agreed: Signal<bool>
        Key: Signal<string>
    }

let private targetOf (e: Event) : HTMLInputElement =
    unbox<HTMLInputElement> e.target.Value

let private form =
    Component.define
        (fun _ () ->
            {
                Name = Api.createSignal ""
                Agreed = Api.createSignal false
                Key = Api.createSignal ""
            })
        (fun t ->
            t.div
                [||]
                [|
                    t.input
                        [|
                            t.Attrs.className "name"
                            t.Events.on ("input", fun s e -> s.Name.Value <- (targetOf e).value)
                        |]
                    t.input
                        [|
                            t.Attrs.className "agree"
                            t.Attrs.Input.``type`` "checkbox"
                            t.Events.on ("change", fun s e -> s.Agreed.Value <- (targetOf e).``checked``)
                        |]
                    t.input
                        [|
                            t.Attrs.className "keys"
                            t.Events.on ("keydown", fun s e -> s.Key.Value <- (unbox<KeyboardEvent> e).key)
                        |]
                    t.p
                        [||]
                        [|
                            t.text (fun s ->
                                s.Name.Value
                                + "|"
                                + (if s.Agreed.Value then "yes" else "no")
                                + "|"
                                + s.Key.Value)
                        |]
                |])

// Connected to the document: a checkbox fires input and change only when it is connected.
let private mounted () =
    let host = (Conformance.document ()).createElement "main"

    (Conformance.document ()).body.appendChild host
    |> ignore

    let graph = new Graph ()

    let mount =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) host None (fun context ->
            Program.run (Component.render form ()) context.Backend)

    host :> Node, graph, mount

let private interactions () =
    let root, graph, mount = mounted ()
    Interact.input root "input.name" "Ada"
    Interact.check root "input.agree" true
    Interact.key root "input.keys" "Enter"
    Expect.text root "p" "Ada|yes|Enter"
    Expect.count root "input" 3
    Expect.attribute root "input.agree" "type" (Some "checkbox")
    Expect.value root "input.name" "Ada"
    mount.Dispose ()
    graph.Dispose ()
    (unbox<Element> root).remove()

let private failures () =
    let root, graph, mount = mounted ()

    let raised =
        try
            Query.get root "li" |> ignore
            false
        with Query.QueryException (selector, count, _) ->
            selector = "li" && count = 0

    if not raised then
        failwith "Query.get did not raise for zero matches"

    let failed =
        try
            Expect.text root "p" "nope"
            false
        with Expect.ExpectationException message ->
            message.Contains "nope"

    if not failed then
        failwith "Expect.text did not raise"

    mount.Dispose ()
    graph.Dispose ()
    (unbox<Element> root).remove()

let private byText () =
    let root, graph, mount = mounted ()
    Interact.input root "input.name" "Zed"
    let p = Query.byText root "Zed|no|"

    if p.localName <> "p" then
        failwith "byText found the wrong element"

    mount.Dispose ()
    graph.Dispose ()
    (unbox<Element> root).remove()

let private selectorFixture =
    "<section><div><p><span>1</span></p><span>2</span></div></section><ul><li><b><i>3</i></b></li><li><i>4</i></li></ul>"

/// <summary>Each selector with the texts of its matches in document order.</summary>
let private selectorExpectations =
    [
        "div > span", [ "2" ]
        "div span", [ "1"; "2" ]
        "section > p span", []
        "section > div span", [ "1"; "2" ]
        "section p > span", [ "1" ]
        "li > i", [ "4" ]
        "li i", [ "3"; "4" ]
        "ul > li > b > i", [ "3" ]
        "li:first-child i", [ "3" ]
    ]

let private selectorsAgree () =
    let host = (Conformance.document ()).createElement "main"
    host.innerHTML <- selectorFixture

    let texts (elements: Element seq) =
        [ for e in elements -> Query.textOf e ]

    for selector, expected in selectorExpectations do
        let engine = texts (Query.queryAll host selector)
        let all = host.querySelectorAll selector

        let native =
            texts [ for i in 0 .. int all.length - 1 -> unbox<Element>(all.item (float i)) ]

        if engine <> expected || native <> expected then
            failwithf "%s: expected %A, engine %A, querySelectorAll %A" selector expected engine native

let private values () =
    let host = (Conformance.document ()).createElement "main"

    (Conformance.document ()).body.appendChild host
    |> ignore

    host.innerHTML <- "<textarea></textarea><select><option value=\"a\">A</option><option value=\"b\">B</option></select>"
    let root = host :> Node
    let inputs = ResizeArray<string>()
    let area = Query.get root "textarea"

    let listener: EventListenerOrEventListenerObject =
        Fable.Core.U2.Case1 (fun (_: Event) -> inputs.Add (unbox<HTMLTextAreaElement> area).value)

    (area :> EventTarget).addEventListener("input", listener)
    Interact.input root "textarea" "typed"
    Expect.value root "textarea" "typed"

    if List.ofSeq inputs <> [ "typed" ] then
        failwithf "the textarea's input listener saw %A" (List.ofSeq inputs)

    Interact.select root "select" "b"
    Expect.value root "select" "b"
    host.remove ()

let private fixtureHost (html: string) =
    let host = (Conformance.document ()).createElement "main"

    (Conformance.document ()).body.appendChild host
    |> ignore

    host.innerHTML <- html
    host

let private failureOf (attempt: unit -> unit) : string =
    try
        attempt ()
        "no failure"
    with
    | Expect.ExpectationException message -> message
    | Query.QueryException (selector, count, excerpt) -> $"query '{selector}' matched {count}{excerpt}"

// Each failure message is an artefact compared across hosts.
let private failureMessages () =
    let host =
        fixtureHost "<p class=\"x\">a</p><p class=\"x\">a</p><input value=\"v\"><span title=\"t\">s</span>"

    let root = host :> Node

    let messages =
        [
            "text", (fun () -> Expect.text root "span" "nope")
            "count", (fun () -> Expect.count root "p" 3)
            "absent attribute", (fun () -> Expect.attribute root "span" "lang" (Some "en"))
            "present attribute", (fun () -> Expect.attribute root "span" "title" None)
            "value", (fun () -> Expect.value root "input" "w")
            "no value", (fun () -> Expect.value root "span" "w")
            "html", (fun () -> Expect.html root "<b></b>")
            "no match", (fun () -> Query.get root "li" |> ignore)
            "two matches", (fun () -> Query.get root "p" |> ignore)
            "ambiguous text", (fun () -> Query.byText root "a" |> ignore)
        ]
        |> List.map (fun (name, attempt) ->
            let message = failureOf attempt
            Conformance.recordIfRunning ("failure: " + name) message
            name, message)

    let has name (part: string) =
        let message = snd (List.find (fun (n, _) -> n = name) messages)

        if not (message.Contains part) then
            failwithf "%s: %s does not hold %s" name message part

    has "text" "expected \"nope\", actual \"s\""
    has "count" "expected 3, actual 2"
    has "absent attribute" "expected en, actual (absent)"
    has "present attribute" "expected (absent), actual t"
    has "value" "expected \"w\", actual \"v\""
    has "no value" "'span' matched a <span>, which has no value"
    has "html" "html: expected <b></b>"
    has "no match" "query 'li' matched 0"
    has "two matches" "query 'p' matched 2"
    has "ambiguous text" "matched 2"
    host.remove ()

let private unreachableStates () =
    let host =
        fixtureHost "<select><option value=\"a\">A</option></select><input type=\"radio\" name=\"r\" checked>"

    let root = host :> Node
    let events = ResizeArray<string>()
    let select = Query.get root "select"

    for name in [ "input"; "change" ] do
        select.addEventListener (name, listener = Fable.Core.U2.Case1 (fun (_: Event) -> events.Add name))

    let unknown = failureOf (fun () -> Interact.select root "select" "zzz")
    let radio = failureOf (fun () -> Interact.check root "input" false)

    if not (unknown.Contains "no option with the value \"zzz\"") then
        failwithf "select: %s" unknown

    if not (radio.Contains "cannot be unchecked") then
        failwithf "check: %s" radio

    if events.Count <> 0 then
        failwithf "select fired %A" (List.ofSeq events)

    if (unbox<HTMLSelectElement> select).value <> "a" then
        failwithf "select changed the value to %s" (unbox<HTMLSelectElement> select).value

    host.remove ()

let private textAndEvents () =
    let host =
        fixtureHost "<p>  two\n   words </p><input class=\"k\"><button>b</button>"

    let root = host :> Node

    if (Query.byText root "two words").localName <> "p" then
        failwith "byText normalises whitespace"

    let keys = ResizeArray<string>()
    let input = Query.get root "input.k"

    for name in [ "keydown"; "keyup" ] do
        input.addEventListener (name, listener = Fable.Core.U2.Case1 (fun (e: Event) -> keys.Add (e.``type`` + ":" + (unbox<KeyboardEvent> e).key)))

    Interact.key root "input.k" "a"

    if List.ofSeq keys <> [ "keydown:a"; "keyup:a" ] then
        failwithf "keys %A" (List.ofSeq keys)

    let button = Query.get root "button"
    button.addEventListener ("custom", listener = Fable.Core.U2.Case1 (fun (e: Event) -> e.preventDefault ()))

    let cancelled =
        Interact.dispatch
            root
            "button"
            {
                Type = "custom"
                Kind = EventKind.Plain
                Bubbles = true
                Cancelable = true
                Init = []
            }

    let accepted =
        Interact.dispatch
            root
            "button"
            {
                Type = "other"
                Kind = EventKind.Plain
                Bubbles = true
                Cancelable = true
                Init = []
            }

    if (cancelled, accepted) <> (false, true) then
        failwithf "dispatch returned %A" (cancelled, accepted)

    host.remove ()

type private Counted = { Count: Signal<int> }

let private counted =
    Component.define (fun _ () -> { Count = Api.createSignal 4 }) (fun t ->
        t.button [| t.Attrs.className "single" |] [| t.text (fun (c: Counted) -> string c.Count.Value) |])

let private fragmentCounted =
    Component.define (fun _ () -> { Count = Api.createSignal 5 }) (fun t ->
        t.fragment
            [|
                t.p [||] [| t.text "fragment label" |]
                t.button [| t.Attrs.className "fragment" |] [| t.text (fun (c: Counted) -> string c.Count.Value) |]
            |])

let private excerptInMessages () =
    use graph = new Graph ()
    let target = (Conformance.document ()).createElement "main"

    let page =
        Component.define (fun _ () -> ()) (fun t ->
            t.div
                [||]
                (Array.append
                    (Array.init 60 (fun _ -> t.p [||] [| t.text "earlier sibling" |]))
                    [| t.render counted (fun _ -> ()); t.render fragmentCounted (fun _ -> ()) |]))

    let mounted =
        Mounting.mount graph None (fun errors -> failwithf "%A" errors) target None (fun context ->
            Program.run (Component.render page ()) context.Backend)

    let message =
        failureOf (fun () -> Expect.text (target :> Node) "button.single" "nope")

    let button = Query.get target "button.single"
    let queryMessage = failureOf (fun () -> Query.get button ".missing" |> ignore)

    let fragmentMessage =
        failureOf (fun () -> Expect.text target "button.fragment" "nope")

    mounted.Dispose ()
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
    for name, failure in [ "expectation excerpt", message; "query excerpt", queryMessage ] do
        if
            not (failure.Contains "instance Counted")
            || not (failure.Contains "=\"4\"")
        then
            failwithf "no focused excerpt in %s" failure

        if failure.Contains "earlier sibling" then
            failwithf "excerpt starts outside the failing instance: %s" failure

        Conformance.recordIfRunning name failure

    if
        not (fragmentMessage.Contains "fragment label")
        || not (fragmentMessage.Contains "=\"5\"")
        || fragmentMessage.Contains "earlier sibling"
    then
        failwithf "no fragment instance excerpt in %s" fragmentMessage

    Conformance.recordIfRunning "fragment expectation excerpt" fragmentMessage
#else
    if message.Contains "instance" then
        failwithf "an excerpt in %s" message
#endif

let cases: Conformance.Case array =
    Conformance.ofChecks
        [|
            "interactions drive input, check and key and the expectations read the result", interactions
            "a query or expectation that fails raises with the selector", failures
            "byText returns the innermost element with the text", byText
            "input, select and value read and write textareas and selects", values
            "each expectation and query failure names what it expected and found", failureMessages
            "an expectation failure under a mount carries the dump excerpt in inspect builds", excerptInMessages
            "select with an unknown value and unchecking a radio raise", unreachableStates
            "byText normalises whitespace; key fires keydown and keyup; dispatch returns whether the event was accepted", textAndEvents
            "the selector engine and querySelectorAll agree on child and descendant combinators", selectorsAgree
        |]
