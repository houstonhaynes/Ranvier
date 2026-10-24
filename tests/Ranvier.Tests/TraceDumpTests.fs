module Ranvier.Tests.TraceDumpTests

open Expecto
open Ranvier

#if RANVIER_TRACE
type private Point = { X: int; Y: int }

type private Unregistered = { Name: string }

let private pointCodec: TraceCodec =
    {
        Name = "point"
        Version = 2
        Encode =
            fun value ->
                match value with
                | :? Point as p -> Some (string p.X + "," + string p.Y)
                | _ -> None
        Decode =
            fun data ->
                let parts = data.Split ','
                box { X = int parts[0]; Y = int parts[1] }
    }

let private codecs = TraceCodecs.ofList [ pointCodec ]

/// <summary>
/// <c>write</c> of a labelled graph whose writes record an int, a string, a bool, a float, a codec-encoded record,
/// an unregistered record and null.
/// </summary>
/// <remarks>The fixture holds the creation sites: an edit above the last line regenerates it. Each creation is one line, as Debug and Release give a split one different sites.</remarks>
let private dumpOf (write: Graph -> 'T) : 'T =
    use g = new Graph ()
    use _ = g.Activate ()
    let n = Trace.named "n" (fun () -> createSignal 1)
    let s = Trace.named "s" (fun () -> createSignal "a\"b")
    let b = Trace.named "b" (fun () -> createSignal true)
    let f = Trace.named "f" (fun () -> createSignal 2.5)
    let p = Trace.named "p" (fun () -> createSignal { X = 1; Y = 2 })
    let u = Trace.named "u" (fun () -> createSignal { Name = "u" })
    let o = Trace.named "o" (fun () -> createSignal (null: string))

    let point () =
        { X = n.Value; Y = p.Value.Y }

    let m = Trace.named "m" (fun () -> createMemo (fun _ -> point ()))

    Trace.named "e" (fun () ->
        createEffect (fun () ->
            m.Value |> ignore
            s.Value |> ignore
            b.Value |> ignore
            f.Value |> ignore
            u.Value |> ignore
            o.Value |> ignore))

    n.Value <- 2
    s.Value <- "c\"d"
    b.Value <- false
    f.Value <- 0.5
    p.Value <- { X = 3; Y = 4 }
    u.Value <- { Name = "v" }
    o.Value <- "x"
    o.Value <- null
    write g

#if !FABLE_COMPILER
/// <summary>The path of the committed trace fixture <c>name</c>.</summary>
let internal fixture (name: string) =
    System.IO.Path.Combine (__SOURCE_DIRECTORY__, "fixtures", "trace", name)

/// <summary>
/// Asserts that <c>text</c> equals the committed fixture <c>name</c>, or writes the fixture when
/// <c>RANVIER_WRITE_FIXTURES</c> is <c>1</c>.
/// </summary>
let internal matchesFixture (name: string) (text: string) =
    let path = fixture name

    if System.Environment.GetEnvironmentVariable "RANVIER_WRITE_FIXTURES" = "1" then
        System.IO.Directory.CreateDirectory (System.IO.Path.GetDirectoryName path)
        |> ignore

        System.IO.File.WriteAllText (path, text)

    Expect.isTrue (System.IO.File.Exists path) ("the fixture is committed: " + path)
    Expect.equal text (System.IO.File.ReadAllText path) ("the text equals the fixture " + name)

let private formsOf (events: TraceEvent[]) =
    events
    |> Array.map (fun e ->
        if e.Kind = TraceEventKind.TimedState then
            TracePayload.Null
        else
            TraceCodecs.encode codecs e.Payload)
#endif
#endif

[<Tests>]
let tests =
    testList
        "TraceDump"
        [
#if RANVIER_TRACE
            test "dumpTextWith writes schema 3 with every payload form" {
                let text = dumpOf (fun g -> Trace.dumpTextWith g codecs)
                let lines = text.Split '\n'
                Expect.stringStarts lines[0] "{\"schema\":3,\"target\":\"net\",\"graph\":1," "the header names schema 3"

                for form in
                    [
                        "\"payload\":{\"form\":\"exact\",\"type\":\"int\",\"value\":2}"
                        "\"payload\":{\"form\":\"exact\",\"type\":\"string\",\"value\":\"c\\\"d\"}"
                        "\"payload\":{\"form\":\"exact\",\"type\":\"bool\",\"value\":false}"
                        "\"payload\":{\"form\":\"exact\",\"type\":\"float\",\"value\":0.5}"
                        "\"payload\":{\"form\":\"encoded\",\"codec\":\"point\",\"version\":2,\"data\":\"3,4\"}"
                        "\"payload\":{\"form\":\"lossy\",\"text\":\"{ Name = \\\"v\\\" }\"}"
                        "\"payload\":null"
                    ] do
                    Expect.stringContains text form form
            }

            test "dumpTextWith writes the snapshot and event fields of dumpText" {
                let schema1, schema3 =
                    dumpOf (fun g -> Trace.dumpText g, Trace.dumpTextWith g codecs)

                let lines1 = schema1.Split '\n'
                let lines3 = schema3.Split '\n'
                Expect.equal lines3.Length lines1.Length "one line per event"
                Expect.equal lines3[0] (lines1[0].Replace("{\"schema\":1,", "{\"schema\":3,")) "the header"
                Expect.equal lines3[1] lines1[1] "the snapshot"

                let fields (line: string) =
                    match line.IndexOf ",\"payload\":" with
                    | -1 -> line
                    | i -> line.Substring (0, i)

                Expect.equal (Array.map fields lines3[1..]) (Array.map fields lines1[1..]) "the event fields ahead of the payload"
            }

            test "strict dumpTextWith raises for a payload with no codec" {
                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        dumpOf (fun g -> Trace.dumpTextWith g (TraceCodecs.strict codecs))
                        |> ignore)
                    "the unregistered record raises"
            }

            test "dumpHeader and eventLines concatenate to dumpTextWith, with and without a checkpoint" {
                let encode = Some (TraceCodecs.encode codecs)

                let parts (g: Graph) =
                    let checkpoint = Trace.checkpointOf g
                    let events = Trace.events g

                    TraceModel.dumpHeader encode "net" checkpoint.Name checkpoint.Snapshot events
                    + TraceModel.eventLines encode events

                let whole, expected = dumpOf (fun g -> parts g, Trace.dumpTextWith g codecs)
                Expect.equal whole expected "without a checkpoint"

                let cut, expectedCut =
                    dumpOf (fun g ->
                        let write =
                            Trace.events g
                            |> Array.find (fun e -> e.Kind = TraceEventKind.Write)

                        Trace.checkpoint g write.Seq "cut"
                        parts g, Trace.dumpTextWith g codecs)

                Expect.stringContains cut "\"checkpoint\":\"cut\"" "the header names the checkpoint"
                Expect.equal cut expectedCut "after a checkpoint"

                let header =
                    dumpOf (fun g ->
                        let checkpoint = Trace.checkpointOf g
                        let events = Trace.events g
                        TraceModel.dumpHeader encode "net" checkpoint.Name checkpoint.Snapshot events[..0])

                Expect.isTrue (expected.StartsWith header) "a prefix holding the first event gives the same header"
            }

#if !FABLE_COMPILER
            // .NET only: TraceModel.parseDump is .NET only, and the fixtures are files.
            test "dumpText writes the committed schema 1 fixture byte for byte" { matchesFixture "dump-text-schema-1.jsonl" (dumpOf Trace.dumpText) }

            test "dumpTextWith writes the committed schema 3 fixture byte for byte" {
                matchesFixture "dump-text-schema-3.jsonl" (dumpOf (fun g -> Trace.dumpTextWith g codecs))
            }

            test "the committed schema 1 and 3 fixtures parse to the program's events and payload forms" {
                let events = dumpOf Trace.events
                let forms = formsOf events

                for name, schema in [ "dump-text-schema-1.jsonl", 1; "dump-text-schema-3.jsonl", 3 ] do
                    let dump = TraceModel.parseDump (System.IO.File.ReadAllText (fixture name))
                    Expect.equal dump.Schema schema name
                    Expect.equal dump.Gaps [] "a fixture holds no gap"
                    Expect.equal (dump.Events |> Array.map _.Seq) (events |> Array.map _.Seq) "the events"

                    if schema = 3 then
                        Expect.equal dump.Payloads forms "the payload forms"

                let decoded =
                    TraceContract.parseDumpWith codecs (System.IO.File.ReadAllText (fixture "dump-text-schema-3.jsonl"))

                Expect.contains (decoded.Events |> Array.map _.Payload) (box { X = 3; Y = 4 }) "a codec's payload decodes"
            }

            test "a schema 3 dump parses to the forms written" {
                let text, forms, events =
                    dumpOf (fun g -> Trace.dumpTextWith g codecs, formsOf (Trace.events g), Trace.events g)

                let dump = TraceModel.parseDump text
                Expect.equal dump.Schema 3 "the schema"
                Expect.equal dump.Payloads forms "the payload forms"
                Expect.equal (dump.Events |> Array.map _.Seq) (events |> Array.map _.Seq) "the events"
            }

            test "a gap line parses into Gaps, and why across the gap ends in BeforeCheckpoint" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal 0
                createEffect (fun () -> s.Value |> ignore)
                s.Value <- 1

                let start =
                    Trace.events g
                    |> Array.findBack (fun e -> e.Kind = TraceEventKind.RunStart && e.Cause <> 0)

                let cut = start.Cause

                let gapLine =
                    (TraceExportRecord.text (TraceExportRecord.Gap (cut, cut))).TrimEnd '\n'

                let text =
                    (Trace.dumpTextWith g codecs).Split '\n'
                    |> Array.map (fun line ->
                        if line.StartsWith ("{\"seq\":" + string cut + ",") then
                            gapLine
                        else
                            line)
                    |> String.concat "\n"

                for dump in [ TraceModel.parseDump text; TraceContract.parseDumpWith codecs text ] do
                    Expect.equal dump.Gaps [ cut, cut ] "the gap"
                    Expect.isFalse (dump.Events |> Array.exists (fun e -> e.Seq = cut)) "the gap holds no event"
                    Expect.equal dump.Payloads.Length dump.Events.Length "a form per event"

                    let why = TraceModel.why dump.Events dump.Checkpoint start.Node start.Arg
                    Expect.equal why.Root (Some (BeforeCheckpoint null)) "a cause inside the gap"
            }

            test "dumpTextWith writes numbers in the invariant culture" {
                let culture =
                    System.Globalization.CultureInfo.InvariantCulture.Clone () :?> System.Globalization.CultureInfo

                culture.NumberFormat.NegativeSign <- "−"
                let previous = System.Globalization.CultureInfo.CurrentCulture
                System.Globalization.CultureInfo.CurrentCulture <- culture

                try
                    use g = new Graph ()
                    use _ = g.Activate ()
                    let n = createSignal 1
                    let p = createSignal { X = 1; Y = 2 }
                    n.Value <- -5
                    p.Value <- { X = 3; Y = 4 }
                    let negative = TraceCodecs.ofList [ { pointCodec with Version = -2 } ]
                    let text = Trace.dumpTextWith g negative
                    Expect.stringContains text "\"type\":\"int\",\"value\":-5}" "the negative int"
                    Expect.stringContains text "\"version\":-2," "the negative codec version"
                    let dump = TraceContract.parseDumpWith negative text
                    Expect.contains (dump.Events |> Array.map _.Payload) (box -5) "the int reads back"
                finally
                    System.Globalization.CultureInfo.CurrentCulture <- previous
            }

            test "parseDumpWith decodes each payload through the codecs" {
                let text, events = dumpOf (fun g -> Trace.dumpTextWith g codecs, Trace.events g)
                let dump = TraceContract.parseDumpWith codecs text

                let expected =
                    events
                    |> Array.map (fun e ->
                        match e.Payload with
                        | :? Unregistered as u -> box (TraceModel.valueText u)
                        | payload -> payload)

                Expect.sequenceEqual (dump.Events |> Array.map _.Payload) expected "the decoded payloads"
                Expect.contains (dump.Events |> Array.map _.Payload) (box { X = 3; Y = 4 }) "a decoded record"

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceContract.parseDumpWith (TraceCodecs.strict codecs) text
                        |> ignore)
                    "a strict list raises for a lossy payload"

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceContract.parseDumpWith (TraceCodecs.ofList []) text
                        |> ignore)
                    "an unregistered codec raises"
            }

            test "schema 1 and 2 payloads read as Lossy text, and a null payload as Null" {
                let text = dumpOf Trace.dumpText

                for schema in [ 1; 2 ] do
                    let dump =
                        TraceModel.parseDump (text.Replace ("{\"schema\":1,", "{\"schema\":" + string schema + ","))

                    Expect.equal dump.Schema schema "the schema"
                    let forms = Set.ofArray dump.Payloads

                    for form in
                        [
                            TracePayload.Null
                            TracePayload.Lossy "2"
                            TracePayload.Lossy "false"
                            TracePayload.Lossy "0.5"
                            TracePayload.Lossy "c\"d"
                            TracePayload.Lossy "{ X = 3; Y = 4 }"
                        ] do
                        Expect.isTrue (forms.Contains form) (string form)

                    for form in forms do
                        match form with
                        | TracePayload.Null
                        | TracePayload.Lossy _ -> ()
                        | other -> failtest $"a schema {schema} payload claims a type: {other}"

                    Expect.sequenceEqual
                        (dump.Events |> Array.map _.Payload)
                        ((TraceModel.parseDump text).Events
                         |> Array.map _.Payload)
                        "the event payloads stay text"
            }

            test "parseDumpWith keeps schema 1 payloads as their text, and a strict list raises for them" {
                use g = new Graph ()
                use _ = g.Activate ()
                let s = createSignal "a"
                let f = createSignal 0.5
                let w = createSignal 0L
                s.Value <- "42"
                s.Value <- "true"
                f.Value <- 1.0
                w.Value <- 42L
                let text = Trace.dumpText g
                let dump = TraceContract.parseDumpWith (TraceCodecs.ofList []) text

                let payloads =
                    dump.Events
                    |> Array.filter (fun e -> e.Kind = TraceEventKind.Write && e.Flag = 1)
                    |> Array.map _.Payload

                Expect.sequenceEqual payloads [| box "42"; box "true"; box "1"; box (TraceModel.valueText (box 42L)) |] "each payload stays its text"

                Expect.throwsT<TraceCodecException>
                    (fun () ->
                        TraceContract.parseDumpWith (TraceCodecs.strict (TraceCodecs.ofList [])) text
                        |> ignore)
                    "a schema 1 payload proves no type"
            }

            test "parseDump raises FormatException for schema 0 and 4" {
                let text = dumpOf Trace.dumpText

                for schema in [ 0; 4 ] do
                    Expect.throwsT<System.FormatException>
                        (fun () ->
                            TraceModel.parseDump (text.Replace ("{\"schema\":1,", "{\"schema\":" + string schema + ","))
                            |> ignore)
                        ("schema " + string schema)
            }
#endif
#endif
        ]
