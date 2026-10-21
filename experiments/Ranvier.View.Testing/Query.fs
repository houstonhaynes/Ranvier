module Ranvier.View.Testing.Query

open Fable.Core.TS.Dom

/// <summary>A query that requires one match found <c>count</c>; <c>excerpt</c> is a dump of the root.</summary>
exception QueryException of selector: string * count: int * excerpt: string with
    override this.Message =
        $"Expected one match for '{this.selector}', found {this.count}.{this.excerpt}"

/// <summary>The dump excerpt appended to query and expectation failures.</summary>
let mutable internal excerptOf: Node -> string = fun _ -> ""

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
