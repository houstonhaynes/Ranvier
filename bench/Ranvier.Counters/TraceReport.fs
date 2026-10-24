/// <summary>The Markdown report of a <c>perf trace</c> run: traced against untraced figures on .NET and Node.js.</summary>
module CounterBench.TraceReport

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open CounterBench.TraceGrowth

/// <summary>A Ranvier scenario's bytes per operation in the untraced and the traced build of one runtime.</summary>
type AllocationRow =
    {
        /// <summary><c>dotnet</c> or <c>node</c>.</summary>
        Runtime: string
        Scenario: string
        Engine: string
        Untraced: float
        Traced: float
        /// <summary>
        /// False when a collection ran inside a measured region of either variant. A collection inside a region can
        /// shrink the heap, so the row's bytes per operation fall below the allocated bytes, down to negative figures.
        /// </summary>
        GcFree: bool
    }

let private invariant = CultureInfo.InvariantCulture

let private isRanvier (engine: string) =
    engine.StartsWith ("Ranvier", StringComparison.Ordinal)

/// <summary>The Ranvier rows of a counter report's .NET and <c>Fable</c> sections, tagged by runtime.</summary>
let private bytesByCase (report: JsonElement) =
    let mutable fable = Unchecked.defaultof<JsonElement>

    [|
        let case (runtime: string) (row: JsonElement) (bytes: float) =
            {
                Runtime = runtime
                Scenario = row.GetProperty("Scenario").GetString()
                Engine = row.GetProperty("Engine").GetString()
                Untraced = bytes
                Traced = bytes
                GcFree = row.GetProperty("GcFree").GetBoolean()
            }

        for row in report.GetProperty("Rows").EnumerateArray() do
            yield case "dotnet" row (row.GetProperty("BytesPerOp").GetDouble())

        if
            report.TryGetProperty ("Fable", &fable)
            && fable.ValueKind = JsonValueKind.Object
        then
            for row in fable.GetProperty("Rows").EnumerateArray() do
                yield case "node" row (row.GetProperty("Bytes").GetProperty("Median").GetDouble())
    |]
    |> Array.filter (fun row -> isRanvier row.Engine)

/// <summary>
/// The Ranvier cases present in both counter reports, as written by the counter bench, in the untraced report's order.
/// </summary>
let allocationRows (untraced: JsonElement) (traced: JsonElement) : AllocationRow[] =
    let tracedRows =
        bytesByCase traced
        |> Array.map (fun row -> (row.Runtime, row.Scenario, row.Engine), row)
        |> Map.ofArray

    bytesByCase untraced
    |> Array.choose (fun row ->
        tracedRows
        |> Map.tryFind (row.Runtime, row.Scenario, row.Engine)
        |> Option.map (fun tracedRow ->
            { row with
                Traced = tracedRow.Traced
                GcFree = row.GcFree && tracedRow.GcFree
            }))

/// <summary>The median milliseconds of a keyed-list operation in an untraced and a traced Chromium bundle.</summary>
type ChromiumTiming =
    {
        Operation: string
        /// <summary>The adapter of <c>keyed-rows.mjs</c>: <c>ranvier</c> or <c>ranvier-sugar</c>.</summary>
        Mode: string
        UntracedMs: float
        TracedMs: float
    }

/// <summary>A mounted keyed list's JavaScript heap bytes per row in an untraced and a traced Chromium bundle.</summary>
type ChromiumHeap =
    {
        /// <summary>
        /// <c>retained heap</c>: used V8 heap per row after forced collections, from <c>keyed-rows.mjs</c>.
        /// <c>new JS objects</c>: self size per row of the objects new between two heap snapshots, from
        /// <c>keyed-heap.mjs</c>.
        /// </summary>
        Measure: string
        Mode: string
        Rows: int
        Untraced: float
        Traced: float
    }

/// <summary>The Chromium figures of a <c>perf trace</c> run.</summary>
type Chromium =
    {
        Version: string
        Timings: ChromiumTiming[]
        Heaps: ChromiumHeap[]
    }

/// <summary>The operations and modes present in both <c>keyed-rows.mjs</c> results, in the untraced result's order.</summary>
let chromiumTimings (untraced: JsonElement) (traced: JsonElement) : ChromiumTiming[] =
    let medians (result: JsonElement) =
        [|
            for row in result.GetProperty("summaries").EnumerateArray() ->
                (row.GetProperty("operation").GetString(), row.GetProperty("mode").GetString()), row.GetProperty("medianMs").GetDouble()
        |]

    let tracedMs = medians traced |> Map.ofArray

    medians untraced
    |> Array.choose (fun ((operation, mode), ms) ->
        tracedMs
        |> Map.tryFind (operation, mode)
        |> Option.map (fun tracedMs ->
            {
                Operation = operation
                Mode = mode
                UntracedMs = ms
                TracedMs = tracedMs
            }))

let private median (values: float[]) =
    let sorted = Array.sort values
    sorted[sorted.Length / 2]

/// <summary>
/// The median across replicates of each mode and row count present in both results. A <c>keyed-rows.mjs</c> result
/// gives <c>retained heap</c> rows; a <c>keyed-heap.mjs</c> result gives <c>new JS objects</c> rows.
/// </summary>
let chromiumHeaps (untraced: JsonElement) (traced: JsonElement) : ChromiumHeap[] =
    let mutable rows = Unchecked.defaultof<JsonElement>

    let measure (result: JsonElement) =
        if result.TryGetProperty ("heaps", &rows) then
            "retained heap",
            [|
                for row in rows.EnumerateArray () ->
                    (row.GetProperty("mode").GetString(), row.GetProperty("count").GetInt32()), row.GetProperty("retainedBytesPerRow").GetDouble()
            |]
        else
            "new JS objects",
            [|
                for row in result.GetProperty("results").EnumerateArray() ->
                    (row.GetProperty("mode").GetString(), row.GetProperty("rows").GetInt32()), row.GetProperty("jsBytesPerRow").GetDouble()
            |]

    let medians samples =
        samples
        |> Array.groupBy fst
        |> Array.map (fun (key, group) -> key, median (Array.map snd group))

    let name, untracedSamples = measure untraced
    let _, tracedSamples = measure traced
    let tracedBytes = medians tracedSamples |> Map.ofArray

    medians untracedSamples
    |> Array.choose (fun ((mode, count), bytes) ->
        tracedBytes
        |> Map.tryFind (mode, count)
        |> Option.map (fun tracedBytes ->
            {
                Measure = name
                Mode = mode
                Rows = count
                Untraced = bytes
                Traced = tracedBytes
            }))

let private number (format: string) (value: float) =
    value.ToString (format, invariant)

/// <summary>
/// The report: <c>versions</c>, the traced-to-untraced allocation ratio of each row, each workload's growth on each
/// runtime, the export and tail-read timings of each traced checkpoint, the Chromium timings and heaps, then each
/// BenchmarkDotNet table of <c>benchmarks</c> under its title.
/// </summary>
let markdown
    (versions: (string * string)[])
    (allocations: AllocationRow[])
    (growth: GrowthRun[])
    (chromium: Chromium option)
    (benchmarks: (string * string)[])
    : string =
    let text = StringBuilder ()

    let line (s: string) =
        text.Append(s).Append("\n") |> ignore

    line "# Traced and untraced baseline"
    line ""
    line "| Version | Value |"
    line "| --- | --- |"

    for name, value in versions do
        line $"| %s{name} | %s{value} |"

    line ""
    line "## Allocations per operation"
    line ""
    line "Bytes per operation from the counter bench, and the traced build's ratio to the untraced build. A row that is"
    line "not GC-free ran a collection inside a measured region, and its figures understate the allocated bytes."
    line ""
    line "| Runtime | Scenario | Engine | Untraced B/op | Traced B/op | Traced - untraced | Ratio | GC-free |"
    line "| --- | --- | --- | ---: | ---: | ---: | ---: | --- |"

    for row in allocations do
        let ratio =
            if row.Untraced = 0. || not row.GcFree then
                "n/a"
            else
                number "0.00" (row.Traced / row.Untraced)

        let figures =
            [ row.Untraced; row.Traced; row.Traced - row.Untraced ]
            |> List.map (number "0.#")
            |> String.concat " | "

        let gcFree = if row.GcFree then "yes" else "no"
        line $"| %s{row.Runtime} | %s{row.Scenario} | %s{row.Engine} | %s{figures} | %s{ratio} | %s{gcFree} |"

    let untracedSlope (runtime: string) (workload: string) =
        growth
        |> Array.tryFind (fun run -> run.Runtime = runtime && not run.Traced)
        |> Option.bind (fun run ->
            run.Summaries
            |> Array.tryFind (fun s -> s.Workload = workload))
        |> Option.map _.RetainedSlope
        |> Option.defaultValue 0.

    let tracedRuns = growth |> Array.filter _.Traced

    line ""
    line "## Trace history growth"
    line ""
    line "Least-squares slopes against update count. The log's retained bytes per update are the traced slope minus the"
    line "untraced slope; its bytes per event divide that by the event slope."
    line ""
    line "| Runtime | Workload | Events/update | Retained B/update, untraced | Retained B/update, traced | Log B/update | Log B/event |"
    line "| --- | --- | ---: | ---: | ---: | ---: | ---: |"

    for run in tracedRuns do
        for s in run.Summaries do
            let baseline = untracedSlope run.Runtime s.Workload
            let log = s.RetainedSlope - baseline
            let perEvent = if s.EventSlope = 0. then 0. else log / s.EventSlope

            let cells =
                [ s.EventsPerUpdate; baseline; s.RetainedSlope; log; perEvent ]
                |> List.map (number "0.0")
                |> String.concat " | "

            line $"| %s{run.Runtime} | %s{s.Workload} | %s{cells} |"

    line ""
    line "## Export and incremental reads"
    line ""
    line "Fastest of three runs, in milliseconds, on the traced build. The tail read copies the full log and slices the"
    line $"events of the last %d{TailUpdates} updates; Trace.eventsSince reads the same events from a seq cursor. Events"
    line "counts the retained log, which memo-checkpointed truncates at each read position."
    line ""
    line "| Runtime | Workload | Updates | Events | Trace.events | Trace.snapshot | Trace.dumpText | Tail read | Trace.eventsSince | Tail events |"
    line "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"

    for run in tracedRuns do
        for c in run.Checkpoints do
            let timings =
                [ c.EventsMs; c.SnapshotMs; c.DumpTextMs; c.TailReadMs; c.SinceMs ]
                |> List.map (number "0.000")
                |> String.concat " | "

            line $"| %s{run.Runtime} | %s{c.Workload} | %d{c.Updates} | %d{c.Events} | %s{timings} | %d{c.TailEvents} |"

    match chromium with
    | None -> ()
    | Some chromium ->
        line ""
        line "## Chromium keyed rows"
        line ""
        line $"Chromium %s{chromium.Version}, the Playground's framework harness. Each bundle runs in its own browser processes,"
        line "so a ratio divides the traced median by the untraced median, not paired samples."
        line ""
        line "| Operation | Mode | Untraced ms | Traced ms | Ratio |"
        line "| --- | --- | ---: | ---: | ---: |"

        for t in chromium.Timings do
            let ratio =
                if t.UntracedMs = 0. then
                    "n/a"
                else
                    number "0.00" (t.TracedMs / t.UntracedMs)

            let figures =
                [ t.UntracedMs; t.TracedMs ]
                |> List.map (number "0.000")
                |> String.concat " | "

            line $"| %s{t.Operation} | %s{t.Mode} | %s{figures} | %s{ratio} |"

        line ""
        line "| Measure | Mode | Rows | Untraced B/row | Traced B/row | Traced - untraced | Ratio |"
        line "| --- | --- | ---: | ---: | ---: | ---: | ---: |"

        for h in chromium.Heaps do
            let ratio =
                if h.Untraced = 0. then
                    "n/a"
                else
                    number "0.00" (h.Traced / h.Untraced)

            let figures =
                [ h.Untraced; h.Traced; h.Traced - h.Untraced ]
                |> List.map (number "0")
                |> String.concat " | "

            line $"| %s{h.Measure} | %s{h.Mode} | %d{h.Rows} | %s{figures} | %s{ratio} |"

    if benchmarks.Length > 0 then
        line ""
        line "## BenchmarkDotNet, category Trace"

        for title, table in benchmarks do
            line ""
            line $"### %s{title}"
            line ""
            line (table.Trim ())

    text.ToString ()

/// <summary>The single <c>.json</c> file in <c>directory</c>.</summary>
let private onlyJson (directory: string) =
    match Directory.GetFiles (directory, "*.json") with
    | [| file |] -> file
    | files -> failwith $"Expected one counter report in %s{directory}; found %d{files.Length}."

/// <summary>
/// Reads a <c>perf trace</c> output directory and writes its <c>report.md</c>, returning the report's path. The
/// directory holds <c>versions.json</c>, one counter report in each of <c>counters/untraced</c> and
/// <c>counters/traced</c>, <c>growth/&lt;runtime&gt;-&lt;variant&gt;.json</c>, BenchmarkDotNet's
/// <c>benchmarks/results</c>, and optionally <c>chromium/rows-&lt;variant&gt;.json</c> and
/// <c>chromium/heap-&lt;variant&gt;.json</c>.
/// </summary>
let write (directory: string) =
    let versions =
        use document =
            JsonDocument.Parse (File.ReadAllText (Path.Combine (directory, "versions.json")))

        [|
            for property in document.RootElement.EnumerateObject () -> property.Name, string property.Value
        |]

    let counters (variant: string) =
        JsonDocument.Parse(File.ReadAllText (onlyJson (Path.Combine (directory, "counters", variant)))).RootElement

    let growth =
        [|
            for runtime in [ "dotnet"; "node" ] do
                for variant in [ "untraced"; "traced" ] do
                    let file = Path.Combine (directory, "growth", $"%s{runtime}-%s{variant}.json")

                    if File.Exists file then
                        yield read file
        |]

    let chromium =
        let file (name: string) =
            Path.Combine (directory, "chromium", name + ".json")

        if File.Exists (file "rows-untraced") then
            let element name =
                JsonDocument.Parse(File.ReadAllText (file name)).RootElement

            let rows = element "rows-untraced"
            let tracedRows = element "rows-traced"

            let objects =
                if File.Exists (file "heap-untraced") then
                    chromiumHeaps (element "heap-untraced") (element "heap-traced")
                else
                    [||]

            Some
                {
                    Version = rows.GetProperty("chromium").GetString()
                    Timings = chromiumTimings rows tracedRows
                    Heaps = Array.append (chromiumHeaps rows tracedRows) objects
                }
        else
            None

    let results = Path.Combine (directory, "benchmarks", "results")

    let benchmarks =
        if Directory.Exists results then
            Directory.GetFiles (results, "*Tracing*-report-github.md")
            |> Array.sort
            |> Array.map (fun file ->
                let title =
                    Path.GetFileName(file).Replace("Ranvier.Benchmarks.Tracing.", "").Replace("-report-github.md", "")

                title, File.ReadAllText file)
        else
            [||]

    let report =
        markdown versions (allocationRows (counters "untraced") (counters "traced")) growth chromium benchmarks

    let path = Path.Combine (directory, "report.md")
    File.WriteAllText (path, report)
    path
