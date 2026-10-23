module Ranvier.View.Testing.Query

open Fable.Core
open Fable.Core.TS.Dom

#if FABLE_COMPILER
/// <summary>The value of a form control: an input, a textarea or a select.</summary>
[<Emit("$0.value")>]
let internal valueOf (element: Element) : string = jsNative

/// <summary>Writes the value of a form control: an input, a textarea or a select.</summary>
[<Emit("$0.value = $1")>]
let internal setValue (element: Element) (value: string) : unit = jsNative
#else
/// <summary>The value of a form control: an input, a textarea or a select.</summary>
let internal valueOf (element: Element) : string =
    match box element with
    | :? HTMLInputElement as input -> input.value
    | :? HTMLTextAreaElement as area -> area.value
    | :? HTMLSelectElement as select -> select.value
    | _ -> raise (System.NotSupportedException $"A <{element.localName}> has no value")

/// <summary>Writes the value of a form control: an input, a textarea or a select.</summary>
let internal setValue (element: Element) (value: string) : unit =
    match box element with
    | :? HTMLInputElement as input -> input.value <- value
    | :? HTMLTextAreaElement as area -> area.value <- value
    | :? HTMLSelectElement as select -> select.value <- value
    | _ -> raise (System.NotSupportedException $"A <{element.localName}> has no value")
#endif

/// <summary>A query that requires one match found <c>count</c>; <c>excerpt</c> is a dump of the root.</summary>
exception QueryException of selector: string * count: int * excerpt: string with
    override this.Message =
        $"Expected one match for '{this.selector}', found {this.count}.{this.excerpt}"

/// <summary>Up to 40 lines of the nearest instance, appended to query and expectation failures.</summary>
#if RANVIER_VIEW_INSPECT || !FABLE_COMPILER
let internal excerptOf (root: Node) : string =
    Ranvier.View.Dom.Debug.excerpt root
#else
let internal excerptOf (_: Node) : string = ""
#endif

let query (root: Node) (selector: string) : Element option =
    Selector.query root selector

let queryAll (root: Node) (selector: string) : Element array =
    Selector.queryAll root selector

/// <summary>The single match; raises <c>QueryException</c> with the match count and a dump of the root otherwise.</summary>
let get (root: Node) (selector: string) : Element =
    match queryAll root selector with
    | [| element |] -> element
    | found -> raise (QueryException (selector, found.Length, excerptOf root))

/// <summary>The element's <c>textContent</c>, empty when it is null.</summary>
let textOf (element: Element) : string =
    match element.textContent with
    | null -> ""
    | text -> text

let private normalised (text: string) =
    System.String.Join (" ", text.Split ([| ' '; '\t'; '\n'; '\r'; '\f' |], System.StringSplitOptions.RemoveEmptyEntries))

/// <summary>
/// The innermost element whose whitespace-normalised <c>textContent</c> equals <c>text</c>; raises
/// <c>QueryException</c> unless exactly one element qualifies.
/// </summary>
let byText (root: Node) (text: string) : Element =
    let candidates =
        queryAll root "*"
        |> Array.filter (fun e -> normalised (textOf e) = text)

    let innermost =
        candidates
        |> Array.filter (fun e ->
            not (
                candidates
                |> Array.exists (fun other ->
                    not (obj.ReferenceEquals (other, e))
                    && e.contains (other :> Node))
            ))

    match innermost with
    | [| element |] -> element
    | found -> raise (QueryException ($"text \"{text}\"", found.Length, excerptOf root))
