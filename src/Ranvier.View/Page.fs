/// <summary>A page declaration shared by server rendering and browser startup.</summary>
module Ranvier.View.Page

open System
open Ranvier.View.Template

/// <summary>The JSON state element used to transfer a page's initial props to its client.</summary>
type StateCodec<'props> =
    {
        Id: string
        /// <summary>Encodes valid JSON; document rendering escapes script-sensitive characters.</summary>
        Encode: 'props -> string
        /// <summary>Decodes the state element's text, returning a diagnostic on invalid state.</summary>
        Decode: string -> Result<'props, string>
    }

/// <summary>The component, initial props, document shell and hydration settings of a page.</summary>
type Definition<'props> =
    {
        Component: Component<'props>
        /// <summary>Called once per server render, and once at client startup when State is None.</summary>
        InitialProps: unit -> 'props
        Document: Server.DocumentShell
        Options: Server.Options
        /// <summary>When present, the client requires this state element and uses its decoded props.</summary>
        State: StateCodec<'props> option
    }

/// <summary>Renders the declaration's initial props and their optional JSON state into an HTML document.</summary>
/// <remarks>The props factory runs before the request graph is created; allocate reactive state in component setup.</remarks>
let render (definition: Definition<'props>) : string =
    let props = definition.InitialProps ()

    let document =
        match definition.State with
        | None -> definition.Document
        | Some codec ->
            { definition.Document with
                State =
                    definition.Document.State
                    @ [ (codec.Id, codec.Encode props) ]
            }

    Server.renderDocument definition.Options document definition.Component props

let private keywords =
    set
        [
            "abstract"
            "and"
            "as"
            "assert"
            "asr"
            "base"
            "begin"
            "class"
            "default"
            "delegate"
            "do"
            "done"
            "downcast"
            "downto"
            "elif"
            "else"
            "end"
            "exception"
            "extern"
            "false"
            "finally"
            "fixed"
            "for"
            "fun"
            "function"
            "global"
            "if"
            "in"
            "inherit"
            "inline"
            "interface"
            "internal"
            "lazy"
            "land"
            "lor"
            "lsl"
            "lsr"
            "lxor"
            "let"
            "match"
            "member"
            "module"
            "mod"
            "mutable"
            "namespace"
            "new"
            "not"
            "null"
            "of"
            "open"
            "or"
            "override"
            "private"
            "public"
            "rec"
            "return"
            "sig"
            "static"
            "struct"
            "then"
            "to"
            "true"
            "try"
            "type"
            "upcast"
            "use"
            "val"
            "void"
            "when"
            "while"
            "with"
            "yield"
            "atomic"
            "break"
            "checked"
            "component"
            "const"
            "constraint"
            "constructor"
            "continue"
            "eager"
            "event"
            "external"
            "functor"
            "include"
            "method"
            "mixin"
            "object"
            "parallel"
            "process"
            "protected"
            "pure"
            "sealed"
            "tailcall"
            "trait"
            "virtual"
            "volatile"
            "_"
        ]

let private validateSymbol argument (symbol: string) =
    let letter c =
        (c >= 'a' && c <= 'z')
        || (c >= 'A' && c <= 'Z')
        || c = '_'

    let continuation c =
        letter c || (c >= '0' && c <= '9') || c = '\''

    let validSegment (segment: string) =
        segment.Length > 0
        && letter segment[0]
        && Seq.forall continuation segment
        && not (keywords.Contains segment)

    if
        String.IsNullOrEmpty symbol
        || not (symbol.Split '.' |> Array.forall validSegment)
    then
        invalidArg argument "Expected dot-separated F# identifiers using ASCII letters, digits, underscores or apostrophes, excluding keywords"

/// <summary>Generates F# startup source that retains the disposable handle returned by PageClient.start.</summary>
/// <remarks>Compile the generated module after the declaration with Fable; Fable resolves its module imports.</remarks>
/// <exception cref="T:System.ArgumentException">A name is empty or contains an invalid or reserved identifier segment.</exception>
let clientSource (moduleName: string) (declarationName: string) : string =
    validateSymbol "moduleName" moduleName
    validateSymbol "declarationName" declarationName

    "module "
    + moduleName
    + "\n\nlet page = Ranvier.View.Dom.PageClient.start "
    + declarationName
    + "\n"
