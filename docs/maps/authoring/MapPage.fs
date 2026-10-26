namespace Ranvier.Docs.Maps.Authoring

open System
open System.Net
open System.Text
open System.Text.RegularExpressions

/// <summary>How a component cell's code compiles.</summary>
[<RequireQualifiedAccess>]
type PageCellKind =
    /// <summary>Module-level declarations, compiled for later cells; the cell has no host.</summary>
    | Declarations
    /// <summary>An expression of type <c>Component&lt;unit&gt;</c>, rendered as the cell.</summary>
    | Expression
    /// <summary>Module-level declarations, then the named <c>Component&lt;unit&gt;</c> value they declare.</summary>
    | Render of name: string
    /// <summary>A <c>map</c> fence's generated scenario and its flags.</summary>
    | Map of MapFenceOutput * MapFlags
    /// <summary>A cell displayed as source without a rendered preview or browser entry.</summary>
    | CodeOnly of PageCellKind

/// <summary>A component fence of a documentation page.</summary>
type PageCell =
    {
        /// <summary>Unique within the page: <c>c1</c>, <c>c2</c>, or the fence's <c>id=</c>.</summary>
        Id: string
        Kind: PageCellKind
        /// <summary>The fence's code, as the page shows it.</summary>
        Code: string
        /// <summary>The body line the code starts on, counting from 1.</summary>
        Line: int
    }

/// <summary>The generated F# module of a page and the cells it renders.</summary>
type PageUnit =
    {
        /// <summary>Names the page's module, entry and bundle: <c>p</c> and ten lowercase hex digits.</summary>
        Key: string
        /// <summary>The page's source file, which diagnostics point at.</summary>
        Source: string option
        /// <summary>The file line of body line 1.</summary>
        BodyLine: int
        Cells: PageCell list
        /// <summary>The generated module.</summary>
        Code: string
        /// <summary>The page body line each run of module lines came from; <c>Body</c> counts from 1.</summary>
        Spans: MapSpan list
    }

/// <summary>
/// Generated pages for component fences: an F# module of page definitions per documentation page, the exporter that
/// renders their server HTML and browser entries, and the host markup the pages carry.
/// </summary>
[<RequireQualifiedAccess>]
module MapPage =
    let private identifier (id: string) =
        Regex.Replace (id, @"[^A-Za-z0-9_]", "_")

    /// <summary>The module of a page's definitions.</summary>
    let moduleName (key: string) =
        "Ranvier.Docs.MapPages.P" + key.Substring 1

    /// <summary>The file holding a page's module.</summary>
    let fileName (key: string) =
        "P" + key.Substring 1 + ".fs"

    /// <summary>The id of a cell's host element, which is also its id prefix.</summary>
    let hostId (key: string) (cell: string) =
        $"rvm-%s{key}-%s{cell}"

    /// <summary>The component value of a rendered cell.</summary>
    let componentName (cell: string) =
        "Cell_" + identifier cell

    /// <summary>The page definition of a rendered cell.</summary>
    let definitionName (cell: string) =
        "page_" + identifier cell

    /// <summary>The browser entry module of a rendered cell.</summary>
    let entryName (key: string) (cell: string) =
        "P" + key.Substring 1 + "_" + identifier cell

    /// <summary>The cells with a host, in page order.</summary>
    let rendered (unit: PageUnit) =
        unit.Cells
        |> List.filter (fun cell ->
            match cell.Kind with
            | PageCellKind.Declarations
            | PageCellKind.CodeOnly _ -> false
            | _ -> true)

    /// <summary>The module of a page's cells, with a page definition per rendered cell.</summary>
    /// <param name="key">The page's key.</param>
    /// <param name="source">The page's source file.</param>
    /// <param name="bodyLine">The file line of body line 1.</param>
    /// <param name="cells">The page's cells, in page order.</param>
    let unit (key: string) (source: string option) (bodyLine: int) (cells: PageCell list) : PageUnit =
        let lines = ResizeArray<string>()
        let spans = ResizeArray<MapSpan>()
        // The cell a line without a span of its own is blamed on: the header's is the first cell.
        let mutable blamed = List.tryHead cells

        let add (line: string) =
            let next = lines.Count + 1

            match blamed with
            | Some cell when
                not (
                    spans
                    |> Seq.exists (fun s ->
                        next >= s.Generated
                        && next < s.Generated + s.Length)
                )
                ->
                spans.Add
                    {
                        Generated = next
                        Length = 1
                        Body = cell.Line - 1
                        Indent = Int32.MaxValue
                    }
            | _ -> ()

            lines.Add line

        let span body indent length =
            spans.Add
                {
                    Generated = lines.Count + 1
                    Length = length
                    Body = body
                    Indent = indent
                }

        // A line written for a cell is blamed on the fence's opening line.
        let pinned (cell: PageCell) (line: string) =
            span (cell.Line - 1) Int32.MaxValue 1
            add line

        // A line inside a string literal is copied as written, so the literal's value is unchanged.
        let code (indent: int) (cell: PageCell) =
            let source = cell.Code.Split '\n' |> Array.map _.TrimEnd('\r')
            let literal = MapFence.literalLines cell.Code
            let pad = String (' ', indent)

            source
            |> Array.iteri (fun index line ->
                if literal[index] then
                    span (cell.Line + index) 0 1
                    add line
                else
                    span (cell.Line + index) indent 1
                    add (if line.Trim () = "" then "" else pad + line))

        add $"module %s{moduleName key}"
        add ""
        add "open Ranvier"

        if
            cells
            |> List.exists (fun cell ->
                match cell.Kind with
                | PageCellKind.Map _
                | PageCellKind.CodeOnly (PageCellKind.Map _) -> true
                | _ -> false)
        then
            add "open Ranvier.Docs.Maps"

        for cell in cells do
            blamed <- Some cell
            add ""

            let rec compileCell kind =
                match kind with
                | PageCellKind.CodeOnly inner -> compileCell inner
                | PageCellKind.Declarations -> code 0 cell
                | PageCellKind.Expression ->
                    pinned cell $"let %s{componentName cell.Id}: Ranvier.View.Template.Component<unit> ="
                    code 4 cell
                | PageCellKind.Render name ->
                    code 0 cell
                    add ""
                    pinned cell $"let %s{componentName cell.Id}: Ranvier.View.Template.Component<unit> = %s{name}"
                | PageCellKind.Map (output, _) ->
                    let start = lines.Count

                    for s in output.Spans do
                        spans.Add
                            { s with
                                Generated = start + s.Generated
                                Body =
                                    if s.Indent = Int32.MaxValue then
                                        cell.Line - 1
                                    else
                                        cell.Line + s.Body - 1
                            }

                    for line in output.Code.Split '\n' do
                        add (line.TrimEnd '\r')

                    add ""
                    pinned cell $"let %s{componentName cell.Id}: Ranvier.View.Template.Component<unit> = %s{output.Render}"

            compileCell cell.Kind

        for cell in cells do
            blamed <- Some cell

            match cell.Kind with
            | PageCellKind.Declarations
            | PageCellKind.CodeOnly _ -> ()
            | _ ->
                let host = hostId key cell.Id
                add ""

                for line in
                    [
                        $"let %s{definitionName cell.Id}: Ranvier.View.Page.Definition<unit> ="
                        "    {"
                        $"        Component = %s{componentName cell.Id}"
                        "        InitialProps = ignore"
                        "        Document ="
                        "            { Ranvier.View.Server.DocumentShell.defaults with"
                        $"                HostId = \"%s{host}\""
                        "            }"
                        "        Options ="
                        "            { Ranvier.View.Server.Options.defaults with"
                        "                Policy = Ranvier.View.Representation.Omit"
                        $"                IdPrefix = \"%s{host}\""
                        "            }"
                        "        State = None"
                        "    }"
                    ] do
                    pinned cell line

        {
            Key = key
            Source = source
            BodyLine = bodyLine
            Cells = cells
            Code = String.concat "\n" lines + "\n"
            Spans = List.ofSeq spans
        }

    /// <summary>The flags of every map cell of <c>units</c>, by page key and cell id.</summary>
    let flags (units: PageUnit list) : Map<string * string, MapFlags> =
        units
        |> List.collect (fun unit ->
            unit.Cells
            |> List.choose (fun cell ->
                match cell.Kind with
                | PageCellKind.Map (_, flags) -> Some ((unit.Key, cell.Id), flags)
                | _ -> None))
        |> Map.ofList

    /// <summary>The page body line and column of a module line and column, when the line came from a cell.</summary>
    /// <remarks>A line generated for a cell maps to the fence's opening line, column 1.</remarks>
    let locate (spans: MapSpan list) (line: int) (column: int) =
        spans
        |> List.tryFind (fun s ->
            line >= s.Generated
            && line < s.Generated + s.Length)
        |> Option.map (fun s ->
            if s.Indent = Int32.MaxValue then
                s.Body, 1
            else
                s.Body + line - s.Generated, max 1 (column - s.Indent))

    /// <summary>The markup of a cell's host element.</summary>
    let host (inline': bool) (key: string) (cell: string) =
        let tag = if inline' then "span" else "div"
        let modifier = if inline' then " rv-map-host--inline" else ""
        let page = WebUtility.HtmlEncode key
        let id = WebUtility.HtmlEncode cell

        $"<%s{tag} class=\"rv-map-host%s{modifier}\" id=\"%s{WebUtility.HtmlEncode (hostId key cell)}\" data-ranvier-map-page=\"%s{page}\" data-ranvier-map-cell=\"%s{id}\"></%s{tag}>"

    let private literal (text: string) =
        "\""
        + text.Replace("\\", "\\\\").Replace("\"", "\\\"")
        + "\""

    /// <summary>
    /// The exporter's program: for each rendered cell it writes the server HTML of the definition's body to
    /// <c>fragments/&lt;host id&gt;.html</c> and its <c>Page.clientSource</c> entry to <c>entries/&lt;entry&gt;.fs</c>.
    /// </summary>
    /// <remarks>
    /// The program takes the output directory as its argument. A failed cell is written to standard error as a line of
    /// <c>ranvier-map</c>, the key, the cell and the message, separated by tabs; the program then exits with 1.
    /// </remarks>
    let exporter (units: PageUnit list) =
        let rows =
            [
                for unit in units do
                    for cell in rendered unit do
                        let declaration = moduleName unit.Key + "." + definitionName cell.Id

                        $"        %s{literal unit.Key}, %s{literal cell.Id}, %s{literal declaration}, (fun () -> %s{declaration})"
            ]

        let builder = StringBuilder ()

        let line (text: string) =
            builder.Append(text).Append '\n' |> ignore

        for text in
            [
                "module Ranvier.Docs.MapExport.Program"
                ""
                "open System"
                "open System.IO"
                "open Ranvier.View"
                ""
                "let private pages: (string * string * string * (unit -> Page.Definition<unit>)) list ="
                "    ["
            ] do
            line text

        for row in rows do
            line row

        for text in
            [
                "    ]"
                ""
                "[<EntryPoint>]"
                "let main argv ="
                "    let root = argv[0]"
                "    let fragments = Path.Combine (root, \"fragments\")"
                "    let entries = Path.Combine (root, \"entries\")"
                "    Directory.CreateDirectory fragments |> ignore"
                "    Directory.CreateDirectory entries |> ignore"
                "    let mutable failed = false"
                ""
                "    for key, cell, declaration, definition in pages do"
                "        try"
                "            let page = definition ()"
                "            let html = (Server.renderPage page.Options page.Component (page.InitialProps ())).Body"
                "            File.WriteAllText (Path.Combine (fragments, page.Document.HostId + \".html\"), html)"
                "            let name = \"P\" + key.Substring 1 + \"_\" + Text.RegularExpressions.Regex.Replace (cell, \"[^A-Za-z0-9_]\", \"_\")"
                "            File.WriteAllText (Path.Combine (entries, name + \".fs\"), Page.clientSource (\"Ranvier.Docs.MapEntries.\" + name) declaration)"
                "        with ex ->"
                "            failed <- true"
                "            let mutable cause = ex"
                ""
                "            while not (isNull cause.InnerException) do"
                "                cause <- cause.InnerException"
                ""
                "            let message = cause.Message.Replace('\\r', ' ').Replace('\\n', ' ')"
                "            eprintfn \"ranvier-map\\t%s\\t%s\\t%s\" key cell message"
                ""
                "    if failed then 1 else 0"
            ] do
            line text

        builder.ToString ()

    /// <summary>
    /// The JavaScript entry of a page: a dynamic import of each rendered cell's compiled entry, each started on its own,
    /// so a cell whose startup throws leaves the page's other cells running. A startup failure is written to the console.
    /// </summary>
    /// <param name="entry">The path of a cell's compiled entry relative to the page entry, when it compiled.</param>
    /// <param name="unit">The page.</param>
    /// <returns>The entry, or None when a rendered cell has no compiled entry.</returns>
    let pageEntry (entry: PageCell -> string option) (unit: PageUnit) =
        let paths = rendered unit |> List.map entry

        if paths |> List.exists Option.isNone then
            None
        else
            let starts =
                paths
                |> List.map (fun path -> $"  () => import(%s{literal path.Value}),\n")
                |> String.concat ""

            Some (
                "const starts = [\n"
                + starts
                + "];\n"
                + "for (const start of starts) start().catch(error => console.error(error));\n"
            )

    /// <summary>The markup a host shows in place of a cell whose page failed to build: the diagnostics, as text.</summary>
    let failureCard (diagnostics: string) =
        "<div class=\"rv-map-failed\" role=\"alert\"><p class=\"rv-map-failed__title\">This example did not build.</p><pre class=\"rv-map-failed__text\">"
        + WebUtility.HtmlEncode diagnostics
        + "</pre></div>"

    let private hostElement =
        Regex (
            "<(?<tag>div|span)(?<attributes>[^>]*?\\sid=\"?(?<id>rvm-(?<key>p[0-9a-f]{10})-[A-Za-z0-9_-]+)\"?(?:\\s[^>]*)?)>\\s*</(?:div|span)>",
            RegexOptions.Compiled
        )

    /// <summary>
    /// Fills each empty host element with its server HTML and adds one module script per page before <c>&lt;/body&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Accepts minified HTML: attribute values may be unquoted. A host without a fragment stays empty, and a page without
    /// a script URL gets no script.
    /// </remarks>
    /// <param name="fragment">The server HTML of a host id.</param>
    /// <param name="script">The bundle URL of a page key.</param>
    /// <param name="html">The page's HTML.</param>
    let fill (fragment: string -> string option) (script: string -> string option) (html: string) =
        let keys = Collections.Generic.List<string>()

        let filled =
            hostElement.Replace (
                html,
                MatchEvaluator (fun matched ->
                    let key = matched.Groups["key"].Value

                    if not (keys.Contains key) then
                        keys.Add key

                    match fragment matched.Groups["id"].Value with
                    | Some body ->
                        let tag = matched.Groups["tag"].Value
                        let attributes = matched.Groups["attributes"].Value
                        $"<%s{tag}%s{attributes}>%s{body}</%s{tag}>"
                    | None -> matched.Value)
            )

        let scripts =
            keys
            |> Seq.choose script
            |> Seq.filter (fun url -> not (html.Contains ("src=\"" + WebUtility.HtmlEncode url + "\"")))
            |> Seq.map (fun url -> $"<script type=\"module\" src=\"%s{WebUtility.HtmlEncode url}\"></script>")
            |> String.concat ""

        if scripts = "" then
            filled
        else
            let close = filled.LastIndexOf "</body>"

            if close < 0 then
                filled + scripts
            else
                filled.Insert (close, scripts)
