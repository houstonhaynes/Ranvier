/// <summary>Validated CI history records and the arm64 instruction timeline.</summary>
module CounterBench.History

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization.Metadata
open System.Globalization

/// <summary>A per-side median for an available timing metric across five processes.</summary>
type RecordedMedian =
    {
        Leg: string
        Side: string
        Runtime: string
        Scenario: string
        Engine: string
        Metric: string
        Value: float
    }

/// <summary>An accepted Ranvier instruction regression and its applicable scoped reasons.</summary>
type AcceptedRegression =
    {
        Leg: string
        Runtime: string
        Scenario: string
        Engine: string
        Metric: string
        Reasons: Compare.AcceptanceEntry list
    }

/// <summary>Commit metadata, raw AB artifacts with CPU and versions, timing medians, and scoped acceptance provenance.</summary>
type RecordedRun =
    {
        Commit: string
        Parent: string
        Date: DateTime
        Subject: string
        Accepted: string list
        Reasons: string list
        Overrides: Compare.AcceptanceEntry list
        AcceptedRegressions: AcceptedRegression[]
        Medians: RecordedMedian[]
        Runs: AbRun[]
    }

let private require condition message =
    if not condition then
        invalidArg "history" message

let private gate (run: AbRun) =
    if run.Leg = "arm64-counters" then
        "instructions"
    else
        "cpu_ns"

/// <summary>Validates five-round artifacts and commit identities, then records accepted gate regressions and available timing medians.</summary>
let create commit parent date subject messages (runs: AbRun[]) =
    require
        (not (String.IsNullOrWhiteSpace commit)
         && not (String.IsNullOrWhiteSpace parent))
        "Commit and parent are required."

    require (not (isNull subject)) "Subject is required."
    require (not (isNull runs) && runs.Length > 0) "At least one AB artifact is required."
    require ((runs |> Array.map _.Leg |> Set.ofArray).Count = runs.Length) "Duplicate benchmark leg."

    for run in runs do
        Compare.validate (gate run) run
        require (run.Head = commit && run.Base = parent) "Recorded commit/parent differ from AB head/base."

    let known =
        runs
        |> Array.map Compare.scenarioNames
        |> Set.unionMany

    let acceptance = Compare.acceptance false messages known

    require
        acceptance.Unknown.IsEmpty
        ("Unknown acceptance scenarios: "
         + String.concat ", " acceptance.Unknown)

    let regressions =
        runs
        |> Array.collect (fun run ->
            let arm = run.Leg = "arm64-counters"

            let verdict =
                Compare.verdict (gate run) (if arm then 0.02 else 0.15) (if arm then 20. else 0.) arm acceptance run

            verdict.Findings
            |> Array.choose (fun f ->
                if
                    arm
                    && f.Metric = "instructions"
                    && f.Engine.StartsWith ("Ranvier", StringComparison.Ordinal)
                    && f.Kind = Compare.Regressed
                    && f.Accepted
                then
                    Some
                        {
                            Leg = run.Leg
                            Runtime = f.Runtime
                            Scenario = f.Scenario
                            Engine = f.Engine
                            Metric = f.Metric
                            Reasons =
                                acceptance.Entries
                                |> List.filter (fun e ->
                                    List.contains "all" e.Names
                                    || List.contains f.Scenario e.Names)
                        }
                else
                    None))

    let medians =
        runs
        |> Array.collect (fun run ->
            run.Processes
            |> Array.collect (fun p ->
                p.Scenarios
                |> Array.collect (fun s ->
                    s.Metrics
                    |> Array.map (fun m -> (p.Side, p.Runtime, s.Scenario, s.Engine, m.Name), m.Value)))
            |> Array.groupBy fst
            |> Array.map (fun ((side, rt, scenario, engine, metric), xs) ->
                {
                    Leg = run.Leg
                    Side = side
                    Runtime = rt
                    Scenario = scenario
                    Engine = engine
                    Metric = metric
                    Value = xs |> Array.map snd |> Compare.median
                }))

    {
        Commit = commit
        Parent = parent
        Date = date
        Subject = subject
        Accepted =
            regressions
            |> Array.map _.Scenario
            |> Array.distinct
            |> Array.sort
            |> List.ofArray
        Reasons =
            regressions
            |> Array.collect (fun r -> r.Reasons |> List.map _.Reason |> List.toArray)
            |> Array.distinct
            |> List.ofArray
        Overrides = acceptance.Entries
        AcceptedRegressions = regressions
        Medians = medians
        Runs = runs
    }

let private escape (s: string) =
    s
        .Replace("\\", "\\\\")
        .Replace("|", "\\|")
        .Replace("*", "\\*")
        .Replace("_", "\\_")
        .Replace("`", "\\`")
        .Replace("[", "\\[")
        .Replace("]", "\\]")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\r\n", " ")
        .Replace("\r", " ")
        .Replace("\n", " ")

/// <summary>Renders head-side arm64 instruction medians, ordered by Gregorian date then commit; unavailable cells contain a dash.</summary>
let timeline (runs: RecordedRun[]) =
    let runs = runs |> Array.sortBy (fun r -> r.Date, r.Commit)

    let values (r: RecordedRun) =
        r.Medians
        |> Array.filter (fun m ->
            m.Leg = "arm64-counters"
            && m.Side = "head"
            && m.Metric = "instructions")

    let columns =
        runs
        |> Array.collect values
        |> Array.map (fun m -> m.Scenario, (m.Runtime, m.Engine))
        |> Array.distinct

    let sb = StringBuilder ()

    let line (s: string) =
        sb.Append(s).Append('\n') |> ignore

    line "# Instruction counts by commit (arm64, instructions per operation, median of 5 processes)"
    line ""

    for scenario in
        columns
        |> Array.map fst
        |> Array.distinct
        |> Array.sort do
        let cols =
            columns
            |> Array.filter (fun (s, _) -> s = scenario)
            |> Array.map snd
            |> Array.sort

        line ("## " + escape scenario)
        line ""

        line (
            "| Commit | Date | Subject | "
            + (cols
               |> Array.map (fun (rt, e) -> escape (rt + " " + e))
               |> String.concat " | ")
            + " |"
        )

        line (
            "|---|---|---|"
            + String.replicate cols.Length "---:|"
        )

        for r in runs do
            let cells =
                cols
                |> Array.map (fun (rt, e) ->
                    values r
                    |> Array.tryFind (fun m ->
                        m.Scenario = scenario
                        && m.Runtime = rt
                        && m.Engine = e)
                    |> Option.map (fun m -> m.Value.ToString ("N0", CultureInfo.InvariantCulture))
                    |> Option.defaultValue "—")

            let accepted =
                if List.contains scenario r.Accepted then
                    " (accepted)"
                else
                    ""

            line (
                "| `"
                + escape (r.Commit.Substring (0, min 7 r.Commit.Length))
                + "` | "
                + r.Date.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + " | "
                + escape r.Subject
                + accepted
                + " | "
                + String.concat " | " cells
                + " |"
            )

        line ""

    sb.ToString ()

let private options allowed repeated (args: string list) =
    let rec loop acc (remaining: string list) =
        match remaining with
        | [] -> acc
        | key :: value :: rest when
            Set.contains key allowed
            && (key = "--subject"
                || not (value.StartsWith ("--", StringComparison.Ordinal)))
            ->
            require (key = repeated || not (Map.containsKey key acc)) ("Repeated option " + key)
            loop (Map.change key (fun old -> Some (defaultArg old [] @ [ value ])) acc) rest
        | key :: _ -> invalidArg "args" ("Unknown option or missing value: " + key)

    loop Map.empty args

// Records that predate `ScenarioFigures.Compiled` read it as false.
let private resolver =
    let r = DefaultJsonTypeInfoResolver ()

    r.Modifiers.Add (fun info ->
        if info.Type = typeof<ScenarioFigures> then
            for p in info.Properties do
                if p.Name = "Compiled" then
                    p.IsRequired <- false)

    r

let private json =
    JsonSerializerOptions (WriteIndented = true, RespectRequiredConstructorParameters = true, TypeInfoResolver = resolver)

let private read<'T> path =
    let value = JsonSerializer.Deserialize<'T>(File.ReadAllText path, json)
    require (not (isNull (box value))) ("Empty JSON: " + path)
    value

/// <summary>Writes a record or timeline; unknown options, repeated singletons, missing values and malformed artifacts raise errors.</summary>
let main args =
    let parse keys repeated rest =
        let o = options (Set.ofList keys) repeated rest

        for key in keys do
            require (Map.containsKey key o) ("Required option " + key)

        o

    match args with
    | "record" :: rest ->
        let o =
            parse [ "--ab"; "--commit"; "--parent"; "--date"; "--subject"; "--message"; "--out" ] "--ab" rest

        let one key =
            o[key] |> List.exactlyOne

        let date =
            DateTimeOffset.Parse(one "--date", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime

        let messages = File.ReadAllText(one "--message").Split('\u0000')
        let runs = o["--ab"] |> List.map read<AbRun> |> List.toArray

        let record =
            create (one "--commit") (one "--parent") date (one "--subject") messages runs

        File.WriteAllText (one "--out", JsonSerializer.Serialize (record, json))
        0
    | "timeline" :: rest ->
        let o = parse [ "--runs"; "--out" ] "" rest

        let one key =
            o[key] |> List.exactlyOne

        let runs =
            Directory.GetFiles (one "--runs", "*.json")
            |> Array.map (fun path ->
                let r = read<RecordedRun> path
                let validated = create r.Commit r.Parent r.Date r.Subject [] r.Runs
                require (r.Medians = validated.Medians) "Recorded medians differ from raw figures."
                r)

        File.WriteAllText (one "--out", timeline runs)
        0
    | _ -> invalidArg "args" "History requires record or timeline."
