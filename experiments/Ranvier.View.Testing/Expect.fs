/// <summary>Assertions on the matches of a selector under a root; a failure raises <c>ExpectationException</c>.</summary>
module Ranvier.View.Testing.Expect

open Fable.Core.TS.Dom

exception ExpectationException of message: string with
    override this.Message = this.message

let private fail (root: Node) (detail: string) =
    raise (ExpectationException (detail + Query.excerptOf root))

/// <summary>The single match of <c>selector</c>; raises when it is no input, textarea or select.</summary>
let internal control (root: Node) (selector: string) : Element =
    let target = Query.get root selector

    match target.localName with
    | "input"
    | "textarea"
    | "select" -> target
    | tag -> fail target $"'{selector}' matched a <{tag}>, which has no value"

let text (root: Node) (selector: string) (expected: string) : unit =
    let target = Query.get root selector
    let actual = Query.textOf target

    if actual <> expected then
        fail target $"'{selector}' text: expected \"{expected}\", actual \"{actual}\""

let count (root: Node) (selector: string) (expected: int) : unit =
    let actual = (Query.queryAll root selector).Length

    if actual <> expected then
        fail root $"'{selector}' count: expected {expected}, actual {actual}"

/// <summary>The attribute of the single match is <c>expected</c>; <c>None</c> expects it absent.</summary>
let attribute (root: Node) (selector: string) (name: string) (expected: string option) : unit =
    let target = Query.get root selector
    let actual = target.getAttribute name

    if actual <> expected then
        let show = Option.defaultValue "(absent)"
        fail target $"'{selector}' [{name}]: expected {show expected}, actual {show actual}"

let value (root: Node) (selector: string) (expected: string) : unit =
    let target = control root selector
    let actual = Query.valueOf target

    if actual <> expected then
        fail target $"'{selector}' value: expected \"{expected}\", actual \"{actual}\""

/// <summary>The serialisation of the root's children equals <c>expected</c>.</summary>
let html (root: Node) (expected: string) : unit =
    let actual = (unbox<Element> root).innerHTML

    if actual <> expected then
        fail root $"html: expected {expected}, actual {actual}"
