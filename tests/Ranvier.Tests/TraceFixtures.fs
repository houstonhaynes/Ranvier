/// .NET only: the committed schema 2 dump and contract document fixtures. The schema 1 and 3 dumps are pinned in
/// TraceDumpTests.fs. tools/trace-fixtures.ps1 regenerates every fixture.
module Ranvier.Tests.TraceFixtures

open System
open Expecto
open Ranvier
open Ranvier.Tests.TimedTestClock
open Ranvier.Tests.TraceDumpTests

#if RANVIER_TRACE
type private Point = { X: int; Y: int }

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

let private lastSeq (g: Graph) =
    match Trace.events g with
    | [||] -> (Trace.checkpointOf g).Snapshot.Seq
    | events -> events[events.Length - 1].Seq

/// <summary><c>write</c> of a labelled graph with a debounced node, which selects dump schema 2.</summary>
/// <remarks>The fixture holds the creation sites: an edit above the last line regenerates it. Each creation is one line, as Debug and Release give a split one different sites.</remarks>
let private timedOf (write: Graph -> 'T) : 'T =
    use g = new Graph ()
    use _ = g.Activate ()
    let clock = ManualClock ()
    let input = Trace.named "input" (fun () -> createSignal 0)

    let options = { Clock = clock; Comparer = None }
    let window = TimeSpan.FromMilliseconds 100.

    let read () =
        input.Value

    let output = Trace.named "output" (fun () -> debounceWith options window read g)

    Trace.named "e" (fun () -> createEffect (fun () -> output.Value |> ignore))
    input.Value <- 1
    clock.AdvanceTo 50.
    input.Value <- 2
    clock.AdvanceTo 150.
    write g

/// <summary>
/// The six contract documents of a labelled graph checkpointed between writes: a codec-encoded memo, a lossy folded
/// value, a memo waiting on an async source, and the lookup of a checkpointed node.
/// </summary>
/// <remarks>The committed fixtures hold this program's creation sites: an edit above its last line regenerates them.</remarks>
let private documentsOf () =
    use g = new Graph ()
    use _ = g.Activate ()
    let x = Trace.named "x" (fun () -> createSignal 1)
    let p = Trace.named "p" (fun () -> createSignal { X = 0; Y = 0 })

    let m =
        Trace.named "m" (fun () -> createMemo (fun _ -> { X = x.Value; Y = p.Value.Y }))

    Trace.named "e" (fun () -> createEffect (fun () -> m.Value |> ignore))
    x.Value <- 2
    Trace.checkpoint g (lastSeq g) "cut"
    let n = Trace.named "n" (fun () -> createSignal 0)
    let source = Trace.named "source" (fun () -> createAsyncSource<int>())
    let w = Trace.named "w" (fun () -> createMemo (fun _ -> source.Value + n.Value))
    w.TryValue |> ignore
    p.Value <- { X = 0; Y = 5 }
    n.Value <- 4
    let state = Trace.state g codecs

    [
        "state.json", TraceJson.state state, box state
        "why.json", TraceJson.why state.Seq (Trace.whyWith g codecs m), box (Trace.whyWith g codecs m)
        "why-not.json", TraceJson.whyNot state.Seq (Trace.whyNot g w), box (Trace.whyNot g w)
        "waiting.json", TraceJson.waiting state.Seq (Trace.waitingOn g w), box (Trace.waitingOn g w)
        "runs.json", TraceJson.runs state.Seq (Trace.runs g codecs m), box (Trace.runs g codecs m)
        "lookup.json", TraceJson.lookup state.Seq (Trace.lookupNode g x), box (Trace.lookupNode g x)
    ]

/// <summary>The schema, kind, seq and body of the document <c>text</c> of fixture <c>name</c>.</summary>
let private read (name: string) (text: string) : int * string * int * obj =
    let envelope (d: TraceDocument<'T>) =
        d.Schema, d.Kind, d.Seq, box d.Body

    match name with
    | "state.json" -> envelope (TraceJson.readState text)
    | "why.json" -> envelope (TraceJson.readWhy text)
    | "why-not.json" -> envelope (TraceJson.readWhyNot text)
    | "waiting.json" -> envelope (TraceJson.readWaiting text)
    | "runs.json" -> envelope (TraceJson.readRuns text)
    | "lookup.json" -> envelope (TraceJson.readLookup text)
    | _ -> failwithf "no reader for %s" name

[<Tests>]
let tests =
    testList
        "Trace fixtures"
        [
            test "dumpText writes the committed schema 2 fixture byte for byte" { matchesFixture "dump-text-schema-2.jsonl" (timedOf Trace.dumpText) }

            test "the committed schema 2 fixture parses to the program's events" {
                let events = timedOf Trace.events

                let dump =
                    TraceModel.parseDump (IO.File.ReadAllText (fixture "dump-text-schema-2.jsonl"))

                Expect.equal dump.Schema 2 "the schema"
                Expect.equal (dump.Events |> Array.map _.Seq) (events |> Array.map _.Seq) "the events"

                Expect.exists dump.Events (fun e -> e.Kind = TraceEventKind.TimedState) "a timed state event"
            }

            test "dumpHeader and eventLines concatenate to dumpTextWith for a timed node" {
                let encode = Some (TraceCodecs.encode codecs)

                let whole, expected =
                    timedOf (fun g ->
                        let checkpoint = Trace.checkpointOf g
                        let events = Trace.events g

                        TraceModel.dumpHeader encode "net" checkpoint.Name checkpoint.Snapshot events
                        + TraceModel.eventLines encode events,
                        Trace.dumpTextWith g codecs)

                Expect.stringContains expected "\"remaining\":" "a timed state line"
                Expect.equal whole expected "the concatenation"
            }

            test "each contract document writes its committed fixture byte for byte" {
                for name, text, _ in documentsOf () do
                    matchesFixture name text
            }

            test "each committed contract document reads back to the program's value" {
                let kinds =
                    Map
                        [
                            "state.json", "state"
                            "why.json", "why"
                            "why-not.json", "whyNot"
                            "waiting.json", "waiting"
                            "runs.json", "runs"
                            "lookup.json", "lookup"
                        ]

                for name, _, value in documentsOf () do
                    let schema, kind, _, body = read name (IO.File.ReadAllText (fixture name))
                    Expect.equal (schema, kind) (TraceContract.Schema, kinds[name]) name
                    Expect.equal body value name
            }

            test "the contract document fixtures cover a checkpoint, a codec and every payload form held" {
                let text (name: string) =
                    IO.File.ReadAllText (fixture name)

                Expect.stringContains (text "state.json") "\"checkpoint\":\"cut\"" "the checkpoint"
                Expect.stringContains (text "state.json") "{\"case\":\"Encoded\",\"codec\":\"point\",\"version\":2" "a codec's value"
                Expect.stringContains (text "state.json") "{\"case\":\"Lossy\"" "a folded value"
                Expect.stringContains (text "state.json") "{\"case\":\"Exact\"" "an exact value"
                Expect.stringContains (text "lookup.json") "\"case\":\"Checkpointed\"" "a checkpointed node"
                Expect.stringContains (text "waiting.json") "\"sources\":[" "the waiting memo's source"
                Expect.isFalse ((text "waiting.json").Contains "\"sources\":[]") "the waiting memo names its source"
            }
        ]
#endif
