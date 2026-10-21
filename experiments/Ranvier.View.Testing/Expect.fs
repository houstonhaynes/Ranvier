/// <summary>Assertions on the matches of a selector under a root; a failure raises <c>ExpectationException</c>.</summary>
module Ranvier.View.Testing.Expect

open Fable.Core.TS.Dom

exception ExpectationException of message: string with
    override this.Message = this.message

let private fail (root: Node) (detail: string) =
    raise (ExpectationException (detail + Query.excerptOf root))

let text (root: Node) (selector: string) (expected: string) : unit =
    let actual = Query.textOf (Query.get root selector)

    if actual <> expected then
        fail root $"'{selector}' text: expected \"{expected}\", actual \"{actual}\""

let count (root: Node) (selector: string) (expected: int) : unit =
    let actual = (Query.queryAll root selector).Length

    if actual <> expected then
        fail root $"'{selector}' count: expected {expected}, actual {actual}"

/// <summary>The attribute of the single match is <c>expected</c>; <c>None</c> expects it absent.</summary>
let attribute (root: Node) (selector: string) (name: string) (expected: string option) : unit =
    let actual = (Query.get root selector).getAttribute name

    if actual <> expected then
        let show = Option.defaultValue "(absent)"
        fail root $"'{selector}' [{name}]: expected {show expected}, actual {show actual}"

let value (root: Node) (selector: string) (expected: string) : unit =
    let actual = (unbox<HTMLInputElement>(Query.get root selector)).value

    if actual <> expected then
        fail root $"'{selector}' value: expected \"{expected}\", actual \"{actual}\""

/// <summary>The serialisation of the root's children equals <c>expected</c>.</summary>
let html (root: Node) (expected: string) : unit =
    let actual = (unbox<Element> root).innerHTML

    if actual <> expected then
        fail root $"html: expected {expected}, actual {actual}"
