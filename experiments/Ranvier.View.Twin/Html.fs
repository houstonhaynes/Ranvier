/// <summary>The twin's HTML: a parser for the subset Server writes and template markup holds, and two serialisers.</summary>
/// <remarks>
/// Accepted markup preserves its authored tree and serialises as jsdom serialises it.
/// Markup requiring HTML tree restructuring raises <c>TwinParseException</c> naming the construct.
/// </remarks>
module Ranvier.View.Twin.Html

open System
open System.Collections.Generic
open System.Text

/// <summary>Markup outside the parser's subset; <c>construct</c> names it, at a 1-based line and column.</summary>
exception TwinParseException of construct: string * line: int * column: int with
    override this.Message =
        $"The DOM twin's parser does not accept {this.construct} (line {this.line}, column {this.column}); run this markup under jsdom"

let private voids =
    HashSet<string>
        [
            "area"
            "base"
            "br"
            "col"
            "embed"
            "hr"
            "img"
            "input"
            "link"
            "meta"
            "source"
            "track"
            "wbr"
        ]

let private rawText =
    HashSet<string> [ "script"; "style"; "xmp"; "iframe"; "noembed"; "noframes" ]

let private escapableRaw = HashSet<string> [ "textarea"; "title" ]

let private serialisedRaw =
    HashSet<string> [ "style"; "script"; "xmp"; "iframe"; "noembed"; "noframes"; "plaintext" ]

let private leadingLineFeed = HashSet<string> [ "pre"; "listing"; "textarea" ]

let private closesParagraph =
    HashSet<string>
        [
            "address"
            "article"
            "aside"
            "blockquote"
            "details"
            "div"
            "dl"
            "fieldset"
            "figcaption"
            "figure"
            "footer"
            "form"
            "h1"
            "h2"
            "h3"
            "h4"
            "h5"
            "h6"
            "header"
            "hr"
            "main"
            "menu"
            "nav"
            "ol"
            "p"
            "pre"
            "section"
            "table"
            "ul"
            "li"
            "dd"
            "dt"
            "center"
            "dialog"
            "dir"
            "hgroup"
            "search"
            "summary"
            "listing"
            "plaintext"
            "xmp"
        ]

let private selfClosing = HashSet<string> [ "option"; "tr"; "td"; "th" ]

let private headings = HashSet<string> [ "h1"; "h2"; "h3"; "h4"; "h5"; "h6" ]

// The boundaries of HTML's "has an element in scope"; button scope adds button.
let private scopeBoundaries =
    HashSet<string>
        [
            "applet"
            "caption"
            "html"
            "table"
            "td"
            "th"
            "marquee"
            "object"
            "template"
        ]

let private foreignScopeBoundaries =
    HashSet<string>
        [
            "mi"
            "mo"
            "mn"
            "ms"
            "mtext"
            "annotation-xml"
            "foreignObject"
            "desc"
            "title"
        ]

// HTML's special elements, which end the search for an open li, dd or dt.
let private special =
    HashSet<string>
        [
            "address"
            "applet"
            "area"
            "article"
            "aside"
            "base"
            "basefont"
            "bgsound"
            "blockquote"
            "body"
            "br"
            "button"
            "caption"
            "center"
            "col"
            "colgroup"
            "dd"
            "details"
            "dir"
            "div"
            "dl"
            "dt"
            "embed"
            "fieldset"
            "figcaption"
            "figure"
            "footer"
            "form"
            "frame"
            "frameset"
            "h1"
            "h2"
            "h3"
            "h4"
            "h5"
            "h6"
            "head"
            "header"
            "hgroup"
            "hr"
            "html"
            "iframe"
            "img"
            "input"
            "keygen"
            "li"
            "link"
            "listing"
            "main"
            "marquee"
            "menu"
            "meta"
            "nav"
            "noembed"
            "noframes"
            "noscript"
            "object"
            "ol"
            "p"
            "param"
            "plaintext"
            "pre"
            "script"
            "search"
            "section"
            "select"
            "source"
            "style"
            "summary"
            "table"
            "tbody"
            "td"
            "template"
            "textarea"
            "tfoot"
            "th"
            "thead"
            "title"
            "tr"
            "track"
            "ul"
            "wbr"
            "xmp"
        ]

// The start tags that end SVG or MathML content in the HTML parser.
let private foreignBreakout =
    HashSet<string>
        [
            "b"
            "big"
            "blockquote"
            "body"
            "br"
            "center"
            "code"
            "dd"
            "div"
            "dl"
            "dt"
            "em"
            "embed"
            "h1"
            "h2"
            "h3"
            "h4"
            "h5"
            "h6"
            "head"
            "hr"
            "i"
            "img"
            "li"
            "listing"
            "menu"
            "meta"
            "nobr"
            "ol"
            "p"
            "pre"
            "ruby"
            "s"
            "small"
            "span"
            "strong"
            "strike"
            "sub"
            "sup"
            "table"
            "tt"
            "u"
            "ul"
            "var"
            "font"
        ]

let private mathTextIntegration =
    HashSet<string> [ "mi"; "mo"; "mn"; "ms"; "mtext" ]

let private documentLevel = HashSet<string> [ "html"; "head"; "body" ]

let private tableParts =
    HashSet<string> [ "caption"; "colgroup"; "col"; "thead"; "tbody"; "tfoot"; "tr"; "td"; "th" ]

let private tableContent =
    dict
        [
            "table",
            set
                [
                    "caption"
                    "colgroup"
                    "thead"
                    "tbody"
                    "tfoot"
                    "template"
                    "script"
                    "style"
                ]
            "colgroup", set [ "col"; "template" ]
            "tbody", set [ "tr"; "template"; "script"; "style" ]
            "thead", set [ "tr"; "template"; "script"; "style" ]
            "tfoot", set [ "tr"; "template"; "script"; "style" ]
            "tr", set [ "td"; "th"; "template"; "script"; "style" ]
        ]

let private adjusted (names: string list) =
    let table = Dictionary<string, string>()

    for name in names do
        table[name.ToLowerInvariant ()] <- name

    table

/// <summary>The SVG element names the HTML parser writes in mixed case.</summary>
let private svgTags =
    adjusted
        [
            "altGlyph"
            "altGlyphDef"
            "altGlyphItem"
            "animateColor"
            "animateMotion"
            "animateTransform"
            "clipPath"
            "feBlend"
            "feColorMatrix"
            "feComponentTransfer"
            "feComposite"
            "feConvolveMatrix"
            "feDiffuseLighting"
            "feDisplacementMap"
            "feDistantLight"
            "feDropShadow"
            "feFlood"
            "feFuncA"
            "feFuncB"
            "feFuncG"
            "feFuncR"
            "feGaussianBlur"
            "feImage"
            "feMerge"
            "feMergeNode"
            "feMorphology"
            "feOffset"
            "fePointLight"
            "feSpecularLighting"
            "feSpotLight"
            "feTile"
            "feTurbulence"
            "foreignObject"
            "glyphRef"
            "linearGradient"
            "radialGradient"
            "textPath"
        ]

/// <summary>The SVG attribute names the HTML parser writes in mixed case.</summary>
let private svgAttributes =
    adjusted
        [
            "attributeName"
            "attributeType"
            "baseFrequency"
            "baseProfile"
            "calcMode"
            "clipPathUnits"
            "diffuseConstant"
            "edgeMode"
            "filterUnits"
            "glyphRef"
            "gradientTransform"
            "gradientUnits"
            "kernelMatrix"
            "kernelUnitLength"
            "keyPoints"
            "keySplines"
            "keyTimes"
            "lengthAdjust"
            "limitingConeAngle"
            "markerHeight"
            "markerUnits"
            "markerWidth"
            "maskContentUnits"
            "maskUnits"
            "numOctaves"
            "pathLength"
            "patternContentUnits"
            "patternTransform"
            "patternUnits"
            "pointsAtX"
            "pointsAtY"
            "pointsAtZ"
            "preserveAlpha"
            "preserveAspectRatio"
            "primitiveUnits"
            "refX"
            "refY"
            "repeatCount"
            "repeatDur"
            "requiredExtensions"
            "requiredFeatures"
            "specularConstant"
            "specularExponent"
            "spreadMethod"
            "startOffset"
            "stdDeviation"
            "stitchTiles"
            "surfaceScale"
            "systemLanguage"
            "tableValues"
            "targetX"
            "targetY"
            "textLength"
            "viewBox"
            "viewTarget"
            "xChannelSelector"
            "yChannelSelector"
            "zoomAndPan"
        ]

let private mathAttributes = adjusted [ "definitionURL" ]

/// <summary>The characters that numeric references in the 0x80-0x9F range stand for, as the HTML parser maps them.</summary>
let private windows1252 =
    dict
        [
            0x80, 0x20AC
            0x82, 0x201A
            0x83, 0x0192
            0x84, 0x201E
            0x85, 0x2026
            0x86, 0x2020
            0x87, 0x2021
            0x88, 0x02C6
            0x89, 0x2030
            0x8A, 0x0160
            0x8B, 0x2039
            0x8C, 0x0152
            0x8E, 0x017D
            0x91, 0x2018
            0x92, 0x2019
            0x93, 0x201C
            0x94, 0x201D
            0x95, 0x2022
            0x96, 0x2013
            0x97, 0x2014
            0x98, 0x02DC
            0x99, 0x2122
            0x9A, 0x0161
            0x9B, 0x203A
            0x9C, 0x0153
            0x9E, 0x017E
            0x9F, 0x0178
        ]

type private Reader(text: string) =
    let mutable index = 0
    member _.Index = index
    member _.AtEnd = index >= text.Length
    member _.Peek = if index < text.Length then text[index] else '\000'

    member _.PeekAt k =
        if index + k < text.Length then text[index + k] else '\000'

    member _.Slice(start: int, length: int) =
        if index + start + length <= text.Length then
            text.Substring (index + start, length)
        else
            null

    member _.Advance n =
        index <- index + n

    member _.StartsWith(s: string) =
        String.CompareOrdinal (text, index, s, 0, s.Length) = 0

    member _.StartsWithIgnoreCase(s: string) =
        index + s.Length <= text.Length
        && String.Compare (text, index, s, 0, s.Length, StringComparison.OrdinalIgnoreCase) = 0

    member _.Position =
        let mutable line, column = 1, 1

        for i in 0 .. index - 1 do
            if text[i] = '\n' then
                line <- line + 1
                column <- 1
            else
                column <- column + 1

        line, column

    member this.Fail(construct: string) =
        let line, column = this.Position
        raise (TwinParseException (construct, line, column))

let private isAsciiLetter c =
    (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

let private isSpace c =
    c = ' ' || c = '\t' || c = '\n' || c = '\f'

/// <summary>
/// Reads a character reference at <c>&amp;</c>, as the HTML tokenizer does: the longest named reference that prefixes
/// the input, or a numeric reference; anything else, and a legacy name followed by <c>=</c> or an alphanumeric inside an
/// attribute value, stays a literal <c>&amp;</c>.
/// </summary>
let private reference (reader: Reader) (out: StringBuilder) (inAttribute: bool) =
    let codePoint (value: int) =
        if
            value = 0
            || value > 0x10FFFF
            || (value >= 0xD800 && value <= 0xDFFF)
        then
            "�"
        else
            match windows1252.TryGetValue value with
            | true, mapped -> Char.ConvertFromUtf32 mapped
            | false, _ -> Char.ConvertFromUtf32 value

    let literal () =
        out.Append '&' |> ignore
        reader.Advance 1

    if reader.PeekAt 1 = '#' then
        let hex = reader.PeekAt 2 = 'x' || reader.PeekAt 2 = 'X'
        let start = if hex then 3 else 2
        let digits = StringBuilder ()
        let mutable k = start

        while (if hex then
                   Uri.IsHexDigit (reader.PeekAt k)
               else
                   Char.IsAsciiDigit (reader.PeekAt k)) do
            digits.Append (reader.PeekAt k) |> ignore
            k <- k + 1

        if digits.Length = 0 then
            literal ()
        else
            let significant = digits.ToString().TrimStart '0'

            let value =
                if significant.Length > 8 then 0x110000
                elif significant.Length = 0 then 0
                else Convert.ToInt32 (significant, (if hex then 16 else 10))

            out.Append (codePoint value) |> ignore
            reader.Advance (if reader.PeekAt k = ';' then k + 1 else k)
    elif Char.IsAsciiLetterOrDigit (reader.PeekAt 1) then
        let mutable length = 0

        while length < 32
              && Char.IsAsciiLetterOrDigit (reader.PeekAt (1 + length)) do
            length <- length + 1

        let candidate =
            reader.Slice (1, length)
            + (if reader.PeekAt (1 + length) = ';' then ";" else "")

        let mutable size = candidate.Length
        let mutable found = null

        while isNull found && size > 0 do
            match Entities.table.TryGetValue (candidate.Substring (0, size)) with
            | true, value -> found <- value
            | false, _ -> size <- size - 1

        if isNull found then
            literal ()
        else
            let next = reader.PeekAt (1 + size)

            if
                inAttribute
                && not (candidate.Substring(0, size).EndsWith ";")
                && (next = '=' || Char.IsAsciiLetterOrDigit next)
            then
                literal ()
            else
                out.Append found |> ignore
                reader.Advance (1 + size)
    else
        literal ()

let private normalise (html: string) =
    html.Replace("\r\n", "\n").Replace('\r', '\n')

let private parseNodes (parent: TwinNode) (html: string) (documentMode: bool) =
    let document = parent.Document
    let reader = Reader (normalise html)
    let stack = Stack<TwinNode>()
    stack.Push parent

    let container (node: TwinNode) =
        match node with
        | :? TwinElement as e when e.LocalName = "template" -> e.Content
        | _ -> node

    let current () =
        match stack.Peek () with
        | :? TwinElement as e -> e
        | _ -> null

    let currentName () =
        match current () with
        | null -> ""
        | e -> e.LocalName

    // The open elements of this parse, innermost first; the parse root is not among them.
    let openElements () =
        stack
        |> Seq.take (stack.Count - 1)
        |> Seq.choose (fun n ->
            match n with
            | :? TwinElement as e -> Some e
            | _ -> None)
        |> List.ofSeq

    // The form element pointer a fragment parse starts with: set when the parse root has a form ancestor, as the
    // context element's nearest form sets it; a template boundary ends the search.
    let contextForm =
        let rec walk (node: TwinNode) =
            match node with
            | null -> false
            | :? TwinElement as e when e.Namespace = "html" && e.LocalName = "form" -> true
            | :? TwinElement as e when e.LocalName = "template" -> false
            | _ -> walk node.Parent

        not documentMode && walk parent

    // HTML's "has an element in scope", in button scope when `button` is set.
    let inScope (target: string) (button: bool) =
        let rec walk (elements: TwinElement list) =
            match elements with
            | [] -> false
            | e :: _ when e.Namespace = "html" && e.LocalName = target -> true
            | e :: _ when
                e.Namespace = "html"
                && scopeBoundaries.Contains e.LocalName
                ->
                false
            | e :: _ when
                button
                && e.Namespace = "html"
                && e.LocalName = "button"
                ->
                false
            | e :: _ when
                e.Namespace <> "html"
                && foreignScopeBoundaries.Contains e.LocalName
                ->
                false
            | _ :: rest -> walk rest

        walk (openElements ())

    // The open li (or dd and dt) that a new one implicitly closes, found before a special element other than
    // address, div and p.
    let openListItem (names: string list) =
        let rec walk (elements: TwinElement list) =
            match elements with
            | [] -> None
            | e :: _ when
                e.Namespace = "html"
                && List.contains e.LocalName names
                ->
                Some e.LocalName
            | e :: _ when
                e.Namespace = "html"
                && special.Contains e.LocalName
                && not (List.contains e.LocalName [ "address"; "div"; "p" ])
                ->
                None
            | _ :: rest -> walk rest

        walk (openElements ())

    let namespaceFor (name: string) =
        match current () with
        | _ when name = "svg" -> "svg"
        | _ when name = "math" -> "math"
        | null -> "html"
        | e when
            e.Namespace = "svg"
            && not (List.contains e.LocalName [ "foreignObject"; "desc"; "title" ])
            ->
            "svg"
        | e when
            e.Namespace = "math"
            && e.LocalName <> "annotation-xml"
            ->
            "math"
        | _ -> "html"

    let append (node: TwinNode) =
        (container (stack.Peek ())).AppendChild node
        |> ignore

    let appendText (text: string) =
        if text.Length > 0 then
            let blank = text |> Seq.forall isSpace

            match tableContent.TryGetValue (currentName ()) with
            | true, _ when not blank -> reader.Fail "foster parenting (text inside a table)"
            | _ when
                documentMode
                && blank
                && (obj.ReferenceEquals (stack.Peek (), document.Html)
                    || obj.ReferenceEquals (stack.Peek (), document))
                ->
                ()
            | _ ->
                match (container (stack.Peek ())).LastChild with
                | :? TwinCharacterData as last when last.Kind = NodeKind.Text -> last.Data <- last.Data + text
                | _ -> append (document.CreateText text)

    let readUntil (stop: string) (decode: bool) (dropLeadingLineFeed: bool) =
        let out = StringBuilder ()

        if dropLeadingLineFeed && reader.Peek = '\n' then
            reader.Advance 1

        // The end tag is complete only when a space, `/` or `>` follows its name.
        let atEnd () =
            reader.StartsWithIgnoreCase stop
            && (let next = reader.PeekAt stop.Length
                isSpace next || next = '/' || next = '>')

        while not (reader.AtEnd || atEnd ()) do
            if decode && reader.Peek = '&' then
                reference reader out false
            else
                out.Append reader.Peek |> ignore
                reader.Advance 1

        if reader.AtEnd then
            reader.Fail $"an unclosed {stop.Substring 2}"
        else
            out.ToString ()

    let readName () =
        let out = StringBuilder ()

        while not (
            reader.AtEnd
            || isSpace reader.Peek
            || reader.Peek = '/'
            || reader.Peek = '>'
            || reader.Peek = '='
        ) do
            out.Append (Char.ToLowerInvariant reader.Peek)
            |> ignore

            reader.Advance 1

        out.ToString ()

    let skipSpace () =
        while isSpace reader.Peek do
            reader.Advance 1

    let readAttributeValue () =
        let out = StringBuilder ()

        match reader.Peek with
        | '"'
        | '\'' as quote ->
            reader.Advance 1

            while not (reader.AtEnd || reader.Peek = quote) do
                if reader.Peek = '&' then
                    reference reader out true
                else
                    out.Append reader.Peek |> ignore
                    reader.Advance 1

            if reader.AtEnd then
                reader.Fail "an unterminated attribute value"

            reader.Advance 1
        | _ ->
            while not (
                reader.AtEnd
                || isSpace reader.Peek
                || reader.Peek = '>'
            ) do
                if reader.Peek = '&' then
                    reference reader out true
                else
                    out.Append reader.Peek |> ignore
                    reader.Advance 1

        out.ToString ()

    // The attributes up to the end of the tag, in order, the first of a repeated name winning; and whether the tag
    // ends with `/>`.
    let readAttributes (name: string) =
        let attributes = ResizeArray<string * string>()
        skipSpace ()

        while not (
            reader.AtEnd
            || reader.Peek = '>'
            || reader.StartsWith "/>"
        ) do
            let attribute = readName ()
            skipSpace ()

            let value =
                if reader.Peek = '=' then
                    reader.Advance 1
                    skipSpace ()
                    readAttributeValue ()
                else
                    ""

            if attribute.Length = 0 then
                if reader.Peek = '/' then
                    reader.Advance 1
                else
                    reader.Fail "an empty attribute name"
            elif not (attributes.Exists (fun (n, _) -> n = attribute)) then
                attributes.Add ((attribute, value))

            skipSpace ()

        if reader.AtEnd then
            reader.Fail $"an unterminated <{name}> tag"

        let selfClosed = reader.Peek = '/'
        reader.Advance (if selfClosed then 2 else 1)
        attributes, selfClosed

    let closeRaw (name: string) =
        reader.Advance (name.Length + 2)
        skipSpace ()

        if reader.Peek <> '>' then
            reader.Fail $"an unterminated </{name}> tag"

        reader.Advance 1

    let appendRaw (element: TwinElement) (text: string) =
        if text.Length > 0 then
            element.AppendChild (document.CreateText text)
            |> ignore

    let documentTag (name: string) =
        let attributes, _ = readAttributes name

        let target: TwinElement =
            match name with
            | "html" -> document.Html
            | "head" -> document.Head
            | _ -> document.Body

        for attribute, value in attributes do
            if not (target.HasAttribute attribute) then
                target.SetAttribute (attribute, value)

        match name with
        | "html" -> ()
        | _ -> stack.Push target

    let startTag () =
        reader.Advance 1
        let name = readName ()

        if documentLevel.Contains name then
            if documentMode then
                documentTag name
            else
                reader.Fail $"the document-level tag <{name}> outside a document parse"
        else
            match current () with
            | null -> ()
            | e when
                e.Namespace = "math"
                && mathTextIntegration.Contains e.LocalName
                && name <> "mglyph"
                && name <> "malignmark"
                ->
                reader.Fail "an element inside a MathML text integration point"
            | e when
                e.Namespace <> "html"
                && foreignBreakout.Contains name
                && namespaceFor name <> "html"
                ->
                reader.Fail $"the HTML element <{name}> inside foreign content"
            | _ -> ()

            let ns = namespaceFor name
            let parentName = currentName ()

            if ns = "html" then
                match name with
                | "li" ->
                    match openListItem [ "li" ] with
                    | Some open' -> reader.Fail $"an implied end tag (<{name}> inside <{open'}>)"
                    | None -> ()
                | "dd"
                | "dt" ->
                    match openListItem [ "dd"; "dt" ] with
                    | Some open' -> reader.Fail $"an implied end tag (<{name}> inside <{open'}>)"
                    | None -> ()
                | _ -> ()

                if closesParagraph.Contains name && inScope "p" true then
                    reader.Fail $"an implied end tag (<p> closed by <{name}>)"

                if
                    headings.Contains name
                    && headings.Contains parentName
                then
                    reader.Fail $"an implied end tag (<{parentName}> closed by <{name}>)"

                match name with
                | "a" when
                    openElements ()
                    |> List.exists (fun e -> e.Namespace = "html" && e.LocalName = "a")
                    ->
                    reader.Fail "a nested <a>"
                | "form" when
                    (contextForm
                     && not (
                         openElements ()
                         |> List.exists (fun e -> e.Namespace = "html" && e.LocalName = "template")
                     ))
                    || openElements ()
                       |> List.exists (fun e -> e.Namespace = "html" && e.LocalName = "form")
                    ->
                    reader.Fail "a nested <form>"
                | "button" when inScope "button" false -> reader.Fail "a nested <button>"
                | "nobr" when inScope "nobr" false -> reader.Fail "a nested <nobr>"
                | _ -> ()

                if selfClosing.Contains name && parentName = name then
                    reader.Fail $"an implied end tag (<{name}> inside <{name}>)"

                match tableContent.TryGetValue parentName with
                | true, allowed when not (allowed.Contains name) ->
                    if parentName = "table" && name = "tr" then
                        reader.Fail "a <table> without <tbody>"
                    else
                        reader.Fail $"foster parenting (<{name}> inside <{parentName}>)"
                | true, _ -> ()
                | false, _ ->
                    if
                        tableParts.Contains name
                        && parentName <> "template"
                    then
                        reader.Fail $"a table part (<{name}>) outside a table"

            let localName =
                match ns with
                | "svg" ->
                    match svgTags.TryGetValue name with
                    | true, mixed -> mixed
                    | false, _ -> name
                | _ -> name

            let element =
                if ns = "html" then
                    document.CreateElement name
                else
                    document.CreateElementIn (ns, localName)

            let attributes, selfClosed = readAttributes name

            for attribute, value in attributes do
                let attribute =
                    match ns with
                    | "svg" ->
                        match svgAttributes.TryGetValue attribute with
                        | true, mixed -> mixed
                        | false, _ -> attribute
                    | "math" ->
                        match mathAttributes.TryGetValue attribute with
                        | true, mixed -> mixed
                        | false, _ -> attribute
                    | _ -> attribute

                element.SetAttribute (attribute, value)

            append element

            if ns <> "html" then
                if not selfClosed then
                    stack.Push element
            elif rawText.Contains name then
                appendRaw element (readUntil $"</{name}" false false)
                closeRaw name
            elif escapableRaw.Contains name then
                appendRaw element (readUntil $"</{name}" true (name = "textarea"))
                closeRaw name
            elif not (voids.Contains name) then
                stack.Push element

                if
                    leadingLineFeed.Contains name
                    && reader.Peek = '\n'
                then
                    reader.Advance 1

    let endTag () =
        reader.Advance 2
        let name = readName ()
        skipSpace ()

        if reader.Peek <> '>' then
            reader.Fail $"an unterminated </{name}> tag"

        reader.Advance 1

        if documentMode && documentLevel.Contains name then
            if name <> "html" && currentName () = name then
                stack.Pop () |> ignore
            elif name <> "html" then
                reader.Fail $"the end tag </{name}> that does not close <{currentName ()}>"
        else
            if voids.Contains name then
                reader.Fail $"the end tag </{name}> of a void element"

            if
                stack.Count = 1
                || not (String.Equals (currentName (), name, StringComparison.OrdinalIgnoreCase))
            then
                reader.Fail $"the end tag </{name}> that does not close <{currentName ()}>"

            stack.Pop () |> ignore

    let comment () =
        reader.Advance 4

        if reader.StartsWith ">" || reader.StartsWith "->" then
            reader.Fail "an abruptly closed comment"

        let out = StringBuilder ()

        while not (
            reader.AtEnd
            || reader.StartsWith "-->"
            || reader.StartsWith "--!>"
        ) do
            out.Append reader.Peek |> ignore
            reader.Advance 1

        if reader.AtEnd then
            reader.Fail "an unclosed comment"

        reader.Advance (if reader.StartsWith "--!>" then 4 else 3)
        append (document.CreateComment (out.ToString ()))

    let text = StringBuilder ()

    let flush () =
        appendText (text.ToString ())
        text.Clear () |> ignore

    while not reader.AtEnd do
        if reader.StartsWith "<!--" then
            flush ()
            comment ()
        elif reader.StartsWithIgnoreCase "<!doctype html>" then
            reader.Advance 15
        elif reader.StartsWith "<!" || reader.StartsWith "<?" then
            reader.Fail "a bogus comment"
        elif
            reader.StartsWith "</"
            && isAsciiLetter (reader.PeekAt 2)
        then
            flush ()
            endTag ()
        elif
            reader.Peek = '<'
            && isAsciiLetter (reader.PeekAt 1)
        then
            flush ()
            startTag ()
        elif reader.Peek = '&' then
            reference reader text false
        else
            text.Append reader.Peek |> ignore
            reader.Advance 1

    flush ()

    if stack.Count > 1 then
        reader.Fail $"an unclosed <{currentName ()}>"

/// <summary>Appends the nodes of <c>html</c> to <c>parent</c>, or to its content when <c>parent</c> is a template.</summary>
let parseInto (parent: TwinNode) (html: string) : unit =
    parseNodes parent html false

let parse (document: TwinDocument) (html: string) : TwinFragment =
    let fragment = document.CreateFragment ()
    parseNodes fragment html false
    fragment

/// <summary>
/// Parses a whole document, such as Server's document shell, into <c>document</c>: the attributes of
/// <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and <c>&lt;body&gt;</c> go to the document's own elements, and their
/// contents replace the head's and the body's children.
/// </summary>
let parseDocument (document: TwinDocument) (html: string) : unit =
    for child in document.Head.Children do
        document.Head.RemoveChild child |> ignore

    for child in document.Body.Children do
        document.Body.RemoveChild child |> ignore

    parseNodes document.Html html true

let private jsdomText (text: string) =
    text.Replace("&", "&amp;").Replace(" ", "&nbsp;").Replace("<", "&lt;").Replace(">", "&gt;")

let private jsdomAttribute (value: string) =
    value.Replace("&", "&amp;").Replace(" ", "&nbsp;").Replace("\"", "&quot;")

let rec private write
    (out: StringBuilder)
    (text: string -> string)
    (attribute: string -> string)
    (server: bool)
    (parentName: string)
    (node: TwinNode)
    =
    match node with
    | :? TwinElement as e ->
        out.Append('<').Append e.QualifiedName |> ignore

        for name, value in e.Attributes do
            out.Append(' ').Append(name).Append("=\"").Append(attribute value).Append '"'
            |> ignore

        out.Append '>' |> ignore

        if not (voids.Contains e.LocalName) then
            let children =
                (if e.LocalName = "template" then
                     e.Content
                 else
                     e :> TwinNode)
                    .Children

            match children with
            | (:? TwinCharacterData as first) :: _ when
                server
                && leadingLineFeed.Contains e.LocalName
                && first.Kind = NodeKind.Text
                && first.Data.StartsWith "\n"
                ->
                out.Append '\n' |> ignore
            | _ -> ()

            for child in children do
                write out text attribute server e.LocalName child

            out.Append("</").Append(e.QualifiedName).Append '>'
            |> ignore
    | :? TwinCharacterData as c when c.Kind = NodeKind.Comment ->
        out.Append("<!--").Append(c.Data).Append "-->"
        |> ignore
    | :? TwinCharacterData as c ->
        out.Append (
            if serialisedRaw.Contains parentName then
                c.Data
            else
                text c.Data
        )
        |> ignore
    | _ ->
        for child in node.Children do
            write out text attribute server parentName child

let private serialise text attribute server (nodes: TwinNode list) (parentName: string) =
    let out = StringBuilder ()

    for node in nodes do
        write out text attribute server parentName node

    out.ToString ()

let private nameOf (node: TwinNode) =
    match node with
    | :? TwinElement as e -> e.LocalName
    | _ -> ""

let private childrenOf (node: TwinNode) =
    match node with
    | :? TwinElement as e when e.LocalName = "template" -> e.Content.Children
    | _ -> node.Children

/// <summary>The node in jsdom's serialisation: <c>outerHTML</c> for an element, the data of a text node escaped as text.</summary>
let outer (node: TwinNode) : string =
    serialise jsdomText jsdomAttribute false [ node ] (nameOf node.Parent)

/// <summary>The node's children in jsdom's serialisation, as <c>innerHTML</c> returns them.</summary>
let inner (node: TwinNode) : string =
    serialise jsdomText jsdomAttribute false (childrenOf node) (nameOf node)

/// <summary>
/// The node's children in Server's escaping, with its doubled leading line feed in <c>pre</c>, <c>textarea</c> and <c>listing</c>.
/// </summary>
let server (node: TwinNode) : string =
    serialise (Ranvier.View.Representation.escapeHtml false) (Ranvier.View.Representation.escapeHtml true) true (childrenOf node) (nameOf node)
