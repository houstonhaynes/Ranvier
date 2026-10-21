/// <summary>
/// Nesting rules from the HTML tree-construction algorithm. Markup matched by a rule parses to a different tree than
/// the client builds.
/// </summary>
/// <remarks>
/// <para>
/// <c>ancestors</c> lists the tags of the enclosing elements, nearest first. Where two rules match, the first rule in
/// <c>check</c>'s order is reported.
/// </para>
/// <para>
/// Conservative rejections, parsed as written: <c>input type="hidden"</c> and an empty <c>form</c> in a table part,
/// <c>table</c> in <c>p</c> in quirks mode, and HTML elements inside <c>svg</c> or <c>math</c> (foreign content is
/// checked as HTML).
/// </para>
/// <para>References System only: the Ranvier analyzers compile it (RAN024).</para>
/// </remarks>
module internal Ranvier.View.Foundation.ContentModel

open System
open System.Text

/// <summary>Elements whose content is parsed as a single text node.</summary>
let isRcdata (tagName: string) =
    let tagName = tagName.ToLowerInvariant ()
    tagName = "textarea" || tagName = "title"

/// <summary>The parts of a definition whose parse depends on the elements enclosing the definition.</summary>
/// <remarks>A path lists tags from a root of the definition, joined by <c>/</c>; null marks an absent element.</remarks>
[<NoEquality; NoComparison>]
type RootSummary =
    {
        /// <summary>The tags of the element roots, inlined children's roots included, in document order.</summary>
        RootTags: string array
        /// <summary>Whether a root is a text slot or static text with a non-whitespace character.</summary>
        TextRoot: bool
        /// <summary>
        /// For each of <c>ContentModel.scopes</c>, the path of the first closing element whose ancestors in the
        /// definition all continue the scope's search.
        /// </summary>
        Closers: string array
    }

/// <summary>The ancestors, outermost first, and <c>last</c>, joined by <c>/</c>.</summary>
let path (ancestors: string list) (last: string) =
    String.Join ("/", Array.ofList (List.rev (last :: ancestors)))

let private restructures (rule: string) (ancestors: string list) (last: string) : unit =
    raise (
        InvalidOperationException (
            "The HTML parser restructures this markup ("
            + rule
            + ") at "
            + path ancestors last
        )
    )

let private isTableSection tag =
    tag = "thead" || tag = "tbody" || tag = "tfoot"

let private isTablePart tag =
    tag = "table"
    || tag = "tr"
    || tag = "colgroup"
    || isTableSection tag

let private isHeading tag =
    match tag with
    | "h1"
    | "h2"
    | "h3"
    | "h4"
    | "h5"
    | "h6" -> true
    | _ -> false

/// <summary>The HTML elements of the parser's special category.</summary>
let private isSpecial tag =
    match tag with
    | "address"
    | "applet"
    | "area"
    | "article"
    | "aside"
    | "base"
    | "basefont"
    | "bgsound"
    | "blockquote"
    | "body"
    | "br"
    | "button"
    | "caption"
    | "center"
    | "col"
    | "colgroup"
    | "dd"
    | "details"
    | "dir"
    | "div"
    | "dl"
    | "dt"
    | "embed"
    | "fieldset"
    | "figcaption"
    | "figure"
    | "footer"
    | "form"
    | "frame"
    | "frameset"
    | "h1"
    | "h2"
    | "h3"
    | "h4"
    | "h5"
    | "h6"
    | "head"
    | "header"
    | "hgroup"
    | "hr"
    | "html"
    | "iframe"
    | "img"
    | "input"
    | "keygen"
    | "li"
    | "link"
    | "listing"
    | "main"
    | "marquee"
    | "menu"
    | "meta"
    | "nav"
    | "noembed"
    | "noframes"
    | "noscript"
    | "object"
    | "ol"
    | "p"
    | "param"
    | "plaintext"
    | "pre"
    | "script"
    | "search"
    | "section"
    | "select"
    | "source"
    | "style"
    | "summary"
    | "table"
    | "tbody"
    | "td"
    | "template"
    | "textarea"
    | "tfoot"
    | "th"
    | "thead"
    | "title"
    | "tr"
    | "track"
    | "ul"
    | "wbr"
    | "xmp" -> true
    | _ -> false

/// <summary>Elements whose start tag closes an open <c>p</c>.</summary>
let closesParagraph tag =
    match tag with
    | "address"
    | "article"
    | "aside"
    | "blockquote"
    | "center"
    | "dd"
    | "details"
    | "dialog"
    | "dir"
    | "div"
    | "dl"
    | "dt"
    | "fieldset"
    | "figcaption"
    | "figure"
    | "footer"
    | "form"
    | "h1"
    | "h2"
    | "h3"
    | "h4"
    | "h5"
    | "h6"
    | "header"
    | "hgroup"
    | "hr"
    | "li"
    | "listing"
    | "main"
    | "menu"
    | "nav"
    | "ol"
    | "p"
    | "pre"
    | "search"
    | "section"
    | "summary"
    | "table"
    | "ul" -> true
    | _ -> false

/// <summary>Elements that end the paragraph scope: a <c>p</c> above them stays open.</summary>
let boundsParagraph tag =
    match tag with
    | "button"
    | "td"
    | "th"
    | "caption"
    | "object"
    | "marquee"
    | "applet" -> true
    | _ -> false

/// <summary>Elements that end the anchor scope: an <c>a</c> above them stays open.</summary>
let boundsAnchor tag =
    match tag with
    | "td"
    | "th"
    | "caption"
    | "object"
    | "marquee"
    | "applet" -> true
    | _ -> false

/// <summary>Elements that end the default scope: a <c>button</c> above them stays open.</summary>
let boundsButton tag =
    boundsAnchor tag
    || tag = "table"
    || tag = "html"
    || tag = "template"

/// <summary>Elements above which the search for an open <c>li</c>, <c>dd</c> or <c>dt</c> continues.</summary>
let continuesItem tag =
    not (isSpecial tag)
    || tag = "address"
    || tag = "div"
    || tag = "p"

/// <summary>
/// A parser step: an element of <c>Closes</c> that starts while an element of <c>Target</c> is open closes it, when
/// every element between them is of <c>Continues</c>.
/// </summary>
[<NoEquality; NoComparison>]
type Scope =
    {
        Rule: string
        Closes: string -> bool
        Target: string -> bool
        Continues: string -> bool
    }

let private isItem tag =
    tag = "dd" || tag = "dt"

/// <summary>The scopes, in the order <c>check</c> reports them.</summary>
let scopes: Scope array =
    [|
        {
            Rule = "paragraph-close"
            Closes = closesParagraph
            Target = (fun tag -> tag = "p")
            Continues = fun tag -> not (boundsParagraph tag)
        }
        {
            Rule = "nested-anchor"
            Closes = (fun tag -> tag = "a")
            Target = (fun tag -> tag = "a")
            Continues = fun tag -> not (boundsAnchor tag)
        }
        {
            Rule = "nested-form"
            Closes = (fun tag -> tag = "form")
            Target = (fun tag -> tag = "form")
            Continues = fun _ -> true
        }
        {
            Rule = "nested-button"
            Closes = (fun tag -> tag = "button")
            Target = (fun tag -> tag = "button")
            Continues = fun tag -> not (boundsButton tag)
        }
        {
            Rule = "nested-list-item"
            Closes = (fun tag -> tag = "li")
            Target = (fun tag -> tag = "li")
            Continues = continuesItem
        }
        {
            Rule = "nested-description"
            Closes = isItem
            Target = isItem
            Continues = continuesItem
        }
    |]

let rec private openWithin (scope: Scope) (ancestors: string list) =
    match ancestors with
    | [] -> false
    | tag :: rest ->
        scope.Target tag
        || (scope.Continues tag && openWithin scope rest)

let private fosters parent tag =
    match parent with
    | "table" ->
        not (
            tag = "caption"
            || tag = "colgroup"
            || isTableSection tag
        )
    | "thead"
    | "tbody"
    | "tfoot" -> tag <> "tr"
    | "tr" -> tag <> "td" && tag <> "th"
    | "colgroup" -> tag <> "col"
    | _ -> false

let private selectContent parent tag =
    (parent = "select"
     && tag <> "option"
     && tag <> "optgroup"
     && tag <> "hr")
    || (parent = "optgroup" && tag <> "option")
    || (parent = "option" && tag = "option")

/// <summary>Raises when an element <c>tag</c> under <c>ancestors</c> is restructured by the HTML parser.</summary>
let check (ancestors: string list) (tag: string) : unit =
    match ancestors with
    | [] -> ()
    | parent :: _ ->
        let rule =
            if isRcdata parent then
                "rcdata-content"
            elif tag = "tr" && not (isTableSection parent) then
                "table-row"
            elif
                (isTableSection tag
                 || tag = "caption"
                 || tag = "colgroup")
                && parent <> "table"
            then
                "table-section"
            elif (tag = "td" || tag = "th") && parent <> "tr" then
                "table-cell"
            elif tag = "col" && parent <> "colgroup" then
                "table-column"
            elif fosters parent tag then
                "table-foster"
            else
                match
                    scopes
                    |> Array.tryFind (fun scope -> scope.Closes tag && openWithin scope ancestors)
                with
                | Some scope -> scope.Rule
                | None when isHeading tag && isHeading parent -> "nested-heading"
                | None when selectContent parent tag -> "select-content"
                | None -> null

        if not (isNull rule) then
            restructures rule ancestors tag

/// <summary>Raises when a text child under <c>ancestors</c> is restructured; a text slot is never <c>whitespace</c>.</summary>
let checkText (ancestors: string list) (whitespace: bool) : unit =
    match ancestors with
    | parent :: _ when not whitespace && isTablePart parent -> restructures "table-foster" ancestors "#text"
    | _ -> ()

/// <summary>Raises when a region's markers under <c>ancestors</c> are parsed as text.</summary>
let checkRegion (ancestors: string list) : unit =
    match ancestors with
    | parent :: _ when isRcdata parent -> restructures "rcdata-content" ancestors "#region"
    | _ -> ()

/// <summary>Whether <c>value</c> consists of HTML whitespace: space, tab, line feed, form feed and carriage return.</summary>
let isWhitespace (value: string) =
    value
    |> Seq.forall (fun c ->
        c = ' '
        || c = '\t'
        || c = '\n'
        || c = '\f'
        || c = '\r')

/// <summary>The nearest tag of <c>ancestors</c>, and whether each of <c>scopes</c> has an open target: the inputs of <c>checkSummary</c>.</summary>
let contextKey (ancestors: string list) =
    let key = StringBuilder (List.head ancestors)
    key.Append '|' |> ignore

    for scope in scopes do
        key.Append (if openWithin scope ancestors then '1' else '0')
        |> ignore

    key.ToString ()

/// <summary>Raises when a definition with <c>summary</c>, written under <c>ancestors</c>, is restructured.</summary>
let checkSummary (summary: RootSummary) (ancestors: string list) : unit =
    for tag in summary.RootTags do
        check ancestors tag

    if summary.TextRoot then
        checkText ancestors false

    for index in 0 .. scopes.Length - 1 do
        let closer = summary.Closers[index]

        if
            not (isNull closer)
            && openWithin scopes[index] ancestors
        then
            restructures scopes[index].Rule ancestors closer
