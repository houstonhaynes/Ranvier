namespace Ranvier.Dom

open System

/// <summary>Whether a font set has faces loading.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type FontSetStatus =
    | Loading
    | Loaded

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | FontSetStatus.Loading -> "Loading", []
            | FontSetStatus.Loaded -> "Loaded", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A CSS Font Loading event of a document's font set.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type FontSetEvent =
    /// <summary>A face started loading while none was loading.</summary>
    | Loading
    /// <summary>No face is left loading; one family per face that loaded in the period.</summary>
    | LoadingDone of families: string list
    /// <summary>Follows <c>LoadingDone</c> when faces failed in the period; one family per failed face.</summary>
    | LoadingError of families: string list

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | FontSetEvent.Loading -> "Loading", []
            | FontSetEvent.LoadingDone values ->
                "LoadingDone",
                [
                    Some (fun _ -> NativeDisplay.list context nested (fun c d _ value -> NativeDisplay.text c d value) values)
                ]
            | FontSetEvent.LoadingError values ->
                "LoadingError",
                [
                    Some (fun _ -> NativeDisplay.list context nested (fun c d _ value -> NativeDisplay.text c d value) values)
                ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The source of a constructed font face.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type FontFaceSource =
    /// <summary>A CSS <c>src</c> descriptor value, such as <c>url(...) format(...)</c>; URLs resolve against the document.</summary>
    | Css of src: string
    /// <summary>Font bytes, copied when the face is constructed.</summary>
    | Bytes of data: byte array

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | FontFaceSource.Css (caseValue0) -> "Css", [ Some (fun _ -> NativeDisplay.text context nested (caseValue0)) ]
            | FontFaceSource.Bytes (caseValue0) ->
                "Bytes",
                [
                    Some (fun _ ->
                        NativeDisplay.array
                            context
                            nested
                            (fun c92 d92 p92 (item92: System.Byte) -> NativeDisplay.uint8 c92 d92 (item92))
                            (caseValue0))
                ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The <c>FontFaceDescriptors</c> of a constructed face, as CSS descriptor text; <c>null</c> keeps the IDL default.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type FontFaceDescriptors =
    {
        Style: string
        Weight: string
        Stretch: string
        UnicodeRange: string
        FeatureSettings: string
        Display: string
        SizeAdjust: string
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Style", (fun () -> NativeDisplay.text context nested (this.Style))
                "Weight", (fun () -> NativeDisplay.text context nested (this.Weight))
                "Stretch", (fun () -> NativeDisplay.text context nested (this.Stretch))
                "UnicodeRange", (fun () -> NativeDisplay.text context nested (this.UnicodeRange))
                "FeatureSettings", (fun () -> NativeDisplay.text context nested (this.FeatureSettings))
                "Display", (fun () -> NativeDisplay.text context nested (this.Display))
                "SizeAdjust", (fun () -> NativeDisplay.text context nested (this.SizeAdjust))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

    static member Default =
        {
            Style = null
            Weight = null
            Stretch = null
            UnicodeRange = null
            FeatureSettings = null
            Display = null
            SizeAdjust = null
        }

/// <summary>The font set (<c>document.fonts</c>) of one document or host.</summary>
/// <remarks>
/// Terminal callbacks and event handlers run on the set's owner context, never inside the call that registers them.
/// Disposing the set cancels its pending operations and ends its subscriptions.
/// </remarks>
type FontSet =
    inherit IDisposable
    /// <summary>The set's status after a style update.</summary>
    abstract Status: FontSetStatus

    /// <summary>
    /// True when every face matching the CSS font shorthand <c>font</c> for <c>text</c> has loaded, and when none matches.
    /// Starts no load. An invalid shorthand is <c>Error "SyntaxError"</c>.
    /// </summary>
    abstract Check: font: string * text: string -> Result<bool, string>

    /// <summary>
    /// Loads the faces matching <c>font</c> for <c>text</c>. Succeeds with the number of matching faces once all have
    /// loaded; fails with <c>LoadFailed "NetworkError"</c> when one fails and <c>LoadFailed "SyntaxError"</c> for an
    /// invalid shorthand.
    /// </summary>
    abstract Load: font: string * text: string * terminal: (ResourceOutcome<int> -> unit) -> ResourceOperation<int>

    /// <summary>
    /// Succeeds once the document has finished loading and no face is loading, including loads started after the call.
    /// </summary>
    abstract Ready: terminal: (ResourceOutcome<unit> -> unit) -> ResourceOperation<unit>

    /// <summary>Delivers the set's events to <c>handler</c> until the result is disposed.</summary>
    abstract Subscribe: handler: (FontSetEvent -> unit) -> IDisposable

    /// <summary>The document's <c>FontFaceSet</c>: <c>has</c>, <c>size</c>, <c>add</c>, <c>delete</c> and <c>clear</c>.</summary>
    abstract Faces: FontFaceSet

    /// <summary>A snapshot of the set's faces in iteration order: CSS-connected faces, then added faces in insertion order.</summary>
    abstract Values: unit -> FontFace list

    /// <summary>
    /// A new face of the set's document, not yet in the set. An invalid descriptor or <c>src</c>, or bytes that are not a
    /// font, give a face whose status is <c>Error</c>; a byte source has settled when the call returns.
    /// </summary>
    abstract Create: family: string * source: FontFaceSource * descriptors: FontFaceDescriptors -> FontFace

    /// <summary>
    /// <c>FontFace.load()</c>: starts the face's load. Succeeds with the face once loaded; fails with
    /// <c>DecodeFailed "SyntaxError"</c> for an invalid descriptor, <c>src</c> or font data, and with
    /// <c>LoadFailed "NetworkError"</c> for a URL source that fails. A settled face keeps its outcome.
    /// </summary>
    abstract LoadFace: face: FontFace * terminal: (ResourceOutcome<FontFace> -> unit) -> ResourceOperation<FontFace>

    /// <summary><c>FontFace.loaded</c>: settles as <c>LoadFace</c> does, without starting a load.</summary>
    abstract FaceLoaded: face: FontFace * terminal: (ResourceOutcome<FontFace> -> unit) -> ResourceOperation<FontFace>

/// <summary>Ties resource operations and subscriptions to an abort signal.</summary>
module ResourceSignals =
    /// <summary>
    /// Cancels <c>operation</c> when <c>signal</c> aborts, at once when it has already aborted. Disposing the result
    /// unregisters it.
    /// </summary>
    let cancelOn (signal: AbortSignal) (operation: ResourceOperation<'T>) : IDisposable =
        if signal.Aborted then
            operation.Cancel ()

            { new IDisposable with
                member _.Dispose() = ()
            }
        else
            signal.OnAbort (fun _ -> operation.Cancel ())

    /// <summary>
    /// Disposes <c>subscription</c> when <c>signal</c> aborts, at once when it has already aborted. Disposing the result
    /// disposes the subscription and unregisters it.
    /// </summary>
    let disposeOn (signal: AbortSignal) (subscription: IDisposable) : IDisposable =
        if signal.Aborted then
            subscription.Dispose ()
            subscription
        else
            let registration = signal.OnAbort (fun _ -> subscription.Dispose ())

            { new IDisposable with
                member _.Dispose() =
                    registration.Dispose ()
                    subscription.Dispose ()
            }

/// <summary>Validation and event bookkeeping shared by font set providers.</summary>
module FontSetContract =
    let validate (font: string) (text: string) =
        if isNull font then
            nullArg "font"

        if isNull text then
            nullArg "text"

    let validateFace (family: string) (source: FontFaceSource) =
        if isNull family then
            nullArg "family"

        match source with
        | FontFaceSource.Css src when isNull src -> nullArg "source"
        | FontFaceSource.Bytes data when isNull data -> nullArg "source"
        | _ -> ()

    /// <summary>The descriptors given, as IDL member names and values.</summary>
    let descriptorPairs (descriptors: FontFaceDescriptors) =
        [
            "style", descriptors.Style
            "weight", descriptors.Weight
            "stretch", descriptors.Stretch
            "unicodeRange", descriptors.UnicodeRange
            "featureSettings", descriptors.FeatureSettings
            "display", descriptors.Display
            "sizeAdjust", descriptors.SizeAdjust
        ]
        |> List.filter (fun (_, value) -> not (isNull value))

    /// <summary>The failure of a face whose loaded promise rejected with the DOMException <c>name</c>.</summary>
    let faceFailure (name: string) =
        if name = "SyntaxError" then
            ResourceFailure.DecodeFailed name
        else
            ResourceFailure.LoadFailed name

    /// <summary>The CSS Font Loading "switch to loaded" events for the faces that finished in a loading period.</summary>
    let completed (loaded: string list) (failed: string list) =
        [
            FontSetEvent.LoadingDone loaded
            if not failed.IsEmpty then
                FontSetEvent.LoadingError failed
        ]
