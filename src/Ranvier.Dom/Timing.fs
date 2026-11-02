namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary>A portable acyclic timing payload, limited to 256 levels; containers are copied by the service.</summary>
[<RequireQualifiedAccess>]
type TimingDetail =
    | Null
    | Boolean of bool
    | Number of float
    | Text of string
    | Array of TimingDetail array
    | Object of (string * TimingDetail) array

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | TimingDetail.Null -> "Null", []
            | TimingDetail.Boolean (caseValue0) -> "Boolean", [ Some (fun _ -> NativeDisplay.boolean context nested (caseValue0)) ]
            | TimingDetail.Number (caseValue0) -> "Number", [ Some (fun _ -> NativeDisplay.float64 context nested (caseValue0)) ]
            | TimingDetail.Text (caseValue0) -> "Text", [ Some (fun _ -> NativeDisplay.text context nested (caseValue0)) ]
            | TimingDetail.Array (caseValue0) ->
                "Array",
                [
                    Some (fun _ ->
                        NativeDisplay.array
                            context
                            nested
                            (fun c237 d237 p237 (item237: TimingDetail) ->
                                NativeDisplay.known c237 d237 (item237) (fun (retained: TimingDetail) -> retained.NativeDisplay (c237, d237, p237)))
                            (caseValue0))
                ]
            | TimingDetail.Object (caseValue0) ->
                "Object",
                [
                    Some (fun _ ->
                        NativeDisplay.array
                            context
                            nested
                            (fun c239 d239 p239 (item239: (System.String * TimingDetail)) ->
                                NativeDisplay.known c239 d239 (item239) (fun (tuple240: (System.String * TimingDetail)) ->
                                    let (v240_0, v240_1) = tuple240 in

                                    NativeDisplay.tuple c239 d239 (box tuple240) (fun n240 ->
                                        [
                                            NativeDisplay.text c239 n240 (v240_0)
                                            NativeDisplay.known c239 n240 (v240_1) (fun (retained: TimingDetail) ->
                                                retained.NativeDisplay (c239, n240, 3))
                                        ])))
                            (caseValue0))
                ])
#endif

/// <summary>The kind of a user-created performance entry.</summary>
[<RequireQualifiedAccess>]
type TimingEntryKind =
    | Mark
    | Measure

/// <summary>A performance entry snapshot with millisecond timestamps relative to TimeOrigin.</summary>
type TimingEntry =
    {
        Name: string
        Kind: TimingEntryKind
        StartTime: float
        Duration: float
        Detail: TimingDetail
    }

/// <summary>An omitted endpoint, a millisecond timestamp, or the named mark with the latest timestamp and insertion order on ties.</summary>
[<RequireQualifiedAccess>]
type TimingEndpoint =
    | Omitted
    | Timestamp of float
    | Mark of string

/// <summary>The optional timestamp and portable payload for a mark.</summary>
type TimingMarkOptions =
    {
        StartTime: float option
        Detail: TimingDetail
    }

    /// <summary>Uses the current timestamp and a null detail.</summary>
    static member Default =
        {
            StartTime = None
            Detail = TimingDetail.Null
        }

/// <summary>Measure endpoints and duration; at most two of Start, End and Duration may be present.</summary>
type TimingMeasureOptions =
    {
        Start: TimingEndpoint
        End: TimingEndpoint
        Duration: float option
        Detail: TimingDetail
    }

    /// <summary>Measures from the origin to the current timestamp.</summary>
    static member Default =
        {
            Start = TimingEndpoint.Omitted
            End = TimingEndpoint.Omitted
            Duration = None
            Detail = TimingDetail.Null
        }

/// <summary>A timing failure with its shared Web-compatible error name.</summary>
exception TimingException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>A monotonic millisecond clock with a fixed Unix-epoch millisecond origin.</summary>
type TimingClock =
    /// <summary>The fixed Unix-epoch origin in milliseconds.</summary>
    abstract TimeOrigin: float
    /// <summary>The nondecreasing milliseconds elapsed since TimeOrigin.</summary>
    abstract Now: unit -> float

/// <summary>A borrowed user-timing domain; disposal invalidates the provider while preserving its domain entries.</summary>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type TimingService =
    inherit IDisposable
    /// <summary>The domain's fixed Unix-epoch millisecond origin.</summary>
    abstract TimeOrigin: float
    /// <summary>Returns nondecreasing elapsed milliseconds in this domain.</summary>
    abstract Now: unit -> float
    /// <summary>Records a mark and returns a copied entry; duplicate names remain distinct.</summary>
    abstract Mark: name: string * options: TimingMarkOptions -> TimingEntry
    /// <summary>Records a measure; missing marks and invalid options leave the entry buffer unchanged.</summary>
    /// <remarks>Checks name, options and portable detail before resolving named endpoints.</remarks>
    abstract Measure: name: string * options: TimingMeasureOptions -> TimingEntry
    /// <summary>Copies user entries ordered by startTime, preserving insertion order on ties.</summary>
    abstract GetEntries: unit -> TimingEntry array
    /// <summary>Removes all marks.</summary>
    abstract ClearMarks: unit -> unit
    /// <summary>Removes every mark with the supplied name.</summary>
    abstract ClearMarks: name: string -> unit
    /// <summary>Removes all measures.</summary>
    abstract ClearMeasures: unit -> unit
    /// <summary>Removes every measure with the supplied name.</summary>
    abstract ClearMeasures: name: string -> unit

/// <summary>Validation and copying for shared timing providers.</summary>
module internal TimingContract =
    /// <summary>Raises a timing exception carrying the supplied Web-compatible name.</summary>
    let error name detail =
        raise (TimingException (name, detail))

    /// <summary>Checks a finite, nonnegative millisecond timestamp.</summary>
    let timestamp value =
        if Double.IsNaN value || Double.IsInfinity value then
            error "TypeError" "A timestamp must be finite"

        if value < 0. then
            error "TypeError" "A timestamp must be nonnegative"

        value

    /// <summary>Checks that a shared timing name is a nonnull string.</summary>
    let name (value: string) =
        if isNull value then
            error "TypeError" "A timing name is null"

        value

    /// <summary>Rejects legacy navigation-timing endpoints, which have no shared host equivalent.</summary>
    let markName value =
        name value |> ignore

        match value with
        | "connectEnd"
        | "connectStart"
        | "domComplete"
        | "domContentLoadedEventEnd"
        | "domContentLoadedEventStart"
        | "domInteractive"
        | "domLoading"
        | "domainLookupEnd"
        | "domainLookupStart"
        | "fetchStart"
        | "loadEventEnd"
        | "loadEventStart"
        | "navigationStart"
        | "redirectEnd"
        | "redirectStart"
        | "requestStart"
        | "responseEnd"
        | "responseStart"
        | "secureConnectionStart"
        | "unloadEventEnd"
        | "unloadEventStart" -> error "NotSupportedError" "Legacy navigation timing names require a browser navigation domain"
        | _ -> value

    /// <summary>Copies an acyclic portable payload; malformed values and container cycles are rejected.</summary>
    let copyDetail detail =
        let active = HashSet<obj>(HashIdentity.Reference)

        let objectField (field: obj) : string * TimingDetail =
            if isNull field then
                error "DataCloneError" "A detail object field is null"

            unbox field

        let container value action =
            if isNull value then
                error "DataCloneError" "A detail container is null"

            if not (active.Add value) then
                error "DataCloneError" "A detail tree contains a cycle"

            try
                action ()
            finally
                active.Remove value |> ignore

        let rec copy depth value =
            if depth > 256 then
                error "DataCloneError" "A detail tree exceeds 256 levels"

            if isNull (box value) then
                error "DataCloneError" "A detail value is null"

            match value with
            | TimingDetail.Null
            | TimingDetail.Boolean _
            | TimingDetail.Number _ -> value
            | TimingDetail.Text text ->
                if isNull text then
                    error "DataCloneError" "A detail text is null"

                TimingDetail.Text text
            | TimingDetail.Array values -> container (box values) (fun () -> TimingDetail.Array (Array.map (copy (depth + 1)) values))
            | TimingDetail.Object fields ->
                container (box fields) (fun () ->
                    let names = HashSet<string>()

                    fields
                    |> Array.map (fun field ->
                        let key, item = objectField (box field)

                        if isNull key || not (names.Add key) then
                            error "DataCloneError" "Object keys must be unique strings"

                        key, copy (depth + 1) item)
                    |> TimingDetail.Object)

        copy 0 detail

    /// <summary>Copies an entry and its portable payload.</summary>
    let copyEntry (entry: TimingEntry) =
        { entry with
            Detail = copyDetail entry.Detail
        }

    /// <summary>Checks mark options and their explicit timestamp before insertion.</summary>
    let markOptions (options: TimingMarkOptions) =
        if isNull (box options) then
            error "TypeError" "Mark options are null"

        options.StartTime
        |> Option.iter (timestamp >> ignore)

    /// <summary>Validates a measure's endpoint combination and explicit numeric inputs.</summary>
    let measureOptions (options: TimingMeasureOptions) =
        if isNull (box options) then
            error "TypeError" "Measure options are null"

        let checkEndpoint value =
            if isNull (box value) then
                error "TypeError" "A timing endpoint is null"

            match value with
            | TimingEndpoint.Omitted -> ()
            | TimingEndpoint.Timestamp time -> timestamp time |> ignore
            | TimingEndpoint.Mark text -> markName text |> ignore

        checkEndpoint options.Start
        checkEndpoint options.End

        options.Duration
        |> Option.iter (timestamp >> ignore)

        let hasStart = options.Start <> TimingEndpoint.Omitted
        let hasEnd = options.End <> TimingEndpoint.Omitted

        if options.Duration.IsSome && (hasStart = hasEnd) then
            error "TypeError" "Duration requires exactly one endpoint"

/// <summary>A synchronized user-entry buffer driven by an injected monotonic clock.</summary>
type TimingTimeline(clock: TimingClock) =
    let gate = obj ()
    let entries = ResizeArray<TimingEntry>()

    do
        if isNull (box clock) then
            nullArg (nameof clock)

    let origin = TimingContract.timestamp clock.TimeOrigin

    let now () =
        TimingContract.timestamp (clock.Now ())

    let endpoint =
        function
        | TimingEndpoint.Omitted -> invalidOp "An omitted endpoint has no timestamp"
        | TimingEndpoint.Timestamp value -> value
        | TimingEndpoint.Mark name ->
            entries
            |> Seq.mapi (fun index entry -> index, entry)
            |> Seq.filter (fun (_, entry) ->
                entry.Kind = TimingEntryKind.Mark
                && entry.Name = name)
            |> Seq.sortByDescending (fun (index, entry) -> entry.StartTime, index)
            |> Seq.tryHead
            |> Option.map snd
            |> Option.map (fun entry -> entry.StartTime)
            |> Option.defaultWith (fun () -> TimingContract.error "SyntaxError" ("Unknown mark: " + name))

    let add name kind start duration detail =
        let entry: TimingEntry =
            {
                Name = name
                Kind = kind
                StartTime = start
                Duration = duration
                Detail = detail
            }

        entries.Add entry
        TimingContract.copyEntry entry

    let clear kind name =
        name
        |> Option.iter (TimingContract.name >> ignore)

        entries.RemoveAll (fun entry ->
            entry.Kind = kind
            && (name.IsNone || Some entry.Name = name))
        |> ignore

    /// <summary>Creates a disposable view over this buffer; disposing a view preserves all entries.</summary>
    member _.CreateService() : TimingService =
        let mutable disposed = false

        let run action =
            lock gate (fun () ->
                if disposed then
                    TimingContract.error "InvalidStateError" "TimingService is disposed"
                else
                    action ())

        { new TimingService with
            member _.TimeOrigin = run (fun () -> origin)
            member _.Now() = run now

            member _.Mark(name, options) =
                run (fun () ->
                    TimingContract.markName name |> ignore
                    TimingContract.markOptions options
                    let detail = TimingContract.copyDetail options.Detail

                    let start =
                        options.StartTime
                        |> Option.map TimingContract.timestamp
                        |> Option.defaultWith now

                    add name TimingEntryKind.Mark start 0. detail)

            member _.Measure(name, options) =
                run (fun () ->
                    TimingContract.name name |> ignore
                    TimingContract.measureOptions options
                    let detail = TimingContract.copyDetail options.Detail

                    let finish =
                        match options.End, options.Start, options.Duration with
                        | value, _, _ when value <> TimingEndpoint.Omitted -> endpoint value
                        | TimingEndpoint.Omitted, start, Some duration -> endpoint start + duration
                        | _ -> now ()

                    let start =
                        match options.Start, options.Duration with
                        | value, _ when value <> TimingEndpoint.Omitted -> endpoint value
                        | TimingEndpoint.Omitted, Some duration -> finish - duration
                        | _ -> 0.

                    add name TimingEntryKind.Measure start (finish - start) detail)

            member _.GetEntries() =
                run (fun () ->
                    entries
                    |> Seq.mapi (fun index entry -> index, entry)
                    |> Seq.sortBy (fun (index, entry) -> entry.StartTime, index)
                    |> Seq.map (snd >> TimingContract.copyEntry)
                    |> Seq.toArray)

            member _.ClearMarks() =
                run (fun () -> clear TimingEntryKind.Mark None)

            member _.ClearMarks(name: string) =
                run (fun () -> clear TimingEntryKind.Mark (Some (TimingContract.name name)))

            member _.ClearMeasures() =
                run (fun () -> clear TimingEntryKind.Measure None)

            member _.ClearMeasures(name: string) =
                run (fun () -> clear TimingEntryKind.Measure (Some (TimingContract.name name)))

            member _.Dispose() =
                lock gate (fun () -> disposed <- true)
        }
