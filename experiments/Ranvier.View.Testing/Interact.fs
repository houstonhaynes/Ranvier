/// <summary>
/// User interactions on the single match of a selector under a root. After each interaction a running conformance
/// case records the root's <c>innerHTML</c>, then the listener failures when there are any.
/// </summary>
/// <remarks>
/// A twin session settles its clock and graph after each interaction. Under jsdom, and on a bare twin host, queued
/// commits apply when the caller drains the harness (<c>RunMicrotasks</c>) or awaits.
/// </remarks>
module Ranvier.View.Testing.Interact

open Fable.Core.TS.Dom

let private record (root: Node) (step: string) =
    Conformance.recordIfRunning step (unbox<Element> root).innerHTML
    Conformance.recordListenerErrors ()

let private fire (target: Element) (spec: EventSpec) : bool =
    target.dispatchEvent (EventSpec.create (Conformance.documentOf target) spec)

let private plain name bubbles cancelable =
    {
        Type = name
        Kind = Plain
        Bubbles = bubbles
        Cancelable = cancelable
        Init = []
    }

let private clickOn (target: Element) =
    (unbox<HTMLElement> target).click()

/// <summary>Calls <c>click ()</c> on the single match: activation behaviour, then a bubbling <c>click</c>.</summary>
let click (root: Node) (selector: string) : unit =
    clickOn (Query.get root selector)
    record root ("click " + selector)

/// <summary>Fires a cancelable <c>beforeinput</c>; unless it is cancelled, writes <c>value</c> and fires a bubbling <c>input</c>.</summary>
let input (root: Node) (selector: string) (value: string) : unit =
    let target = Expect.control root selector
    let init = [ "data", box value; "inputType", box "insertText" ]

    if
        fire
            target
            {
                Type = "beforeinput"
                Kind = Input
                Bubbles = true
                Cancelable = true
                Init = init
            }
    then
        Query.setValue target value

        fire
            target
            {
                Type = "input"
                Kind = Input
                Bubbles = true
                Cancelable = false
                Init = init
            }
        |> ignore

    record root ("input " + selector)

/// <summary>Fires a bubbling <c>change</c>.</summary>
let change (root: Node) (selector: string) : unit =
    fire (Query.get root selector) (plain "change" true false)
    |> ignore

    record root ("change " + selector)

/// <summary>
/// Clicks the single match when its <c>checked</c> state differs from <c>isChecked</c>; raises
/// <c>ExpectationException</c> when the state still differs, as for a radio that a click cannot uncheck.
/// </summary>
let check (root: Node) (selector: string) (isChecked: bool) : unit =
    let target = Query.get root selector

    if
        (unbox<HTMLInputElement> target).``checked``
        <> isChecked
    then
        clickOn target

    if
        (unbox<HTMLInputElement> target).``checked``
        <> isChecked
    then
        let state = if isChecked then "checked" else "unchecked"
        raise (Expect.ExpectationException $"'{selector}' cannot be {state} by a click")

    record root ("check " + selector)

/// <summary>
/// Sets the value of the single match, then fires bubbling <c>input</c> and <c>change</c>; raises
/// <c>ExpectationException</c>, before any event, when no option has the value.
/// </summary>
let select (root: Node) (selector: string) (value: string) : unit =
    let target = Query.get root selector

    let offered =
        Query.queryAll target "option"
        |> Array.exists (fun option -> (unbox<HTMLOptionElement> option).value = value)

    if not offered then
        raise (Expect.ExpectationException $"'{selector}' has no option with the value \"{value}\"")

    Query.setValue target value

    fire target (plain "input" true false) |> ignore
    fire target (plain "change" true false) |> ignore
    record root ("select " + selector)

/// <summary>Fires cancelable, bubbling <c>keydown</c> and <c>keyup</c> with <c>key</c>.</summary>
let key (root: Node) (selector: string) (key: string) : unit =
    let target = Query.get root selector

    for name in [ "keydown"; "keyup" ] do
        fire
            target
            {
                Type = name
                Kind = Keyboard
                Bubbles = true
                Cancelable = true
                Init = [ "key", box key ]
            }
        |> ignore

    record root ("key " + selector)

/// <summary>Dispatches <c>spec</c> on the single match; returns false when a listener cancelled it.</summary>
let dispatch (root: Node) (selector: string) (spec: EventSpec) : bool =
    let accepted = fire (Query.get root selector) spec
    record root (spec.Type + " " + selector)
    accepted
