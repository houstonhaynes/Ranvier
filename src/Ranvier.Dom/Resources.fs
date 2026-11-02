namespace Ranvier.Dom

open System
open System.Threading

/// <summary>An image encoding, shared by snapshots and canvas export.</summary>
/// <remarks>
/// <c>Rgba8</c> is unpremultiplied RGBA8 rows, <c>Width * 4</c> bytes each, in the image's color space. PNG ignores
/// quality. JPEG and WEBP take a quality from 0 to 1; <c>None</c>, NaN or a value outside [0, 1] selects the encoder's
/// default, as <c>toBlob</c> does, and WEBP at exactly 1 is lossless.
/// </remarks>
[<RequireQualifiedAccess>]
type ImageEncoding =
    | Rgba8
    | Png
    | Jpeg
    | Webp

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun _ ->
            match this with
            | ImageEncoding.Rgba8 -> "Rgba8", []
            | ImageEncoding.Png -> "Png", []
            | ImageEncoding.Jpeg -> "Jpeg", []
            | ImageEncoding.Webp -> "Webp", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The color space of a snapshot's pixels.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SnapshotColorSpace =
    | Srgb
    | DisplayP3

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | SnapshotColorSpace.Srgb -> "Srgb", []
            | SnapshotColorSpace.DisplayP3 -> "DisplayP3", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A captured image; the operation owns its data until it is disposed.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SnapshotImage =
    {
        PixelWidth: int
        PixelHeight: int
        Format: ImageEncoding
        PixelColorSpace: SnapshotColorSpace
        Data: byte array
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "PixelWidth", (fun () -> NativeDisplay.int32 context nested (this.PixelWidth))
                "PixelHeight", (fun () -> NativeDisplay.int32 context nested (this.PixelHeight))
                "Format",
                (fun () ->
                    NativeDisplay.known context nested (this.Format) (fun (retained: ImageEncoding) -> retained.NativeDisplay (context, nested, 3)))
                "PixelColorSpace",
                (fun () ->
                    NativeDisplay.known context nested (this.PixelColorSpace) (fun (retained: SnapshotColorSpace) ->
                        retained.NativeDisplay (context, nested, 3)))
                "Data",
                (fun () ->
                    NativeDisplay.array
                        context
                        nested
                        (fun c211 d211 p211 (item211: System.Byte) -> NativeDisplay.uint8 c211 d211 (item211))
                        (this.Data))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>How a service's successful-response byte cache takes part in a request.</summary>
/// <remarks>The cache belongs to one service; browser HTTP caches and engine-wide caches are outside it.</remarks>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceCachePolicy =
    /// <summary>Reuses a successful response this service already holds for the URL.</summary>
    | UseCache
    /// <summary>Bypasses the held response and replaces it on success.</summary>
    | Reload
    /// <summary>Bypasses the held response and leaves the cache unchanged.</summary>
    | NoStore

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun _ ->
            match this with
            | ResourceCachePolicy.UseCache -> "UseCache", []
            | ResourceCachePolicy.Reload -> "Reload", []
            | ResourceCachePolicy.NoStore -> "NoStore", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>An absolute http, https or data URL with its cache policy.</summary>
type ResourceRequest =
    {
        Url: string
        CachePolicy: ResourceCachePolicy
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Url", (fun () -> NativeDisplay.text context nested this.Url)
                "CachePolicy",
                (fun () -> NativeDisplay.known context nested this.CachePolicy (fun value -> value.NativeDisplay (context, nested, 3)))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A response whose bytes the caller owns; services copy them at every ownership boundary.</summary>
type ResourceResponse =
    {
        ContentType: string
        Bytes: byte array
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "ContentType", (fun () -> NativeDisplay.text context nested this.ContentType)
                "Bytes", (fun () -> NativeDisplay.array context nested (fun c d _ value -> NativeDisplay.uint8 c d value) this.Bytes)
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>Why an operation ended without a value.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceFailure =
    /// <summary>The provider, transport or access check failed.</summary>
    | LoadFailed of message: string
    /// <summary>The bytes arrived but the engine cannot use them as the requested kind.</summary>
    | DecodeFailed of message: string
    /// <summary>The caller cancelled or disposed the operation.</summary>
    | Cancelled
    /// <summary>The source, target or document the operation observed was replaced or became inactive.</summary>
    | Invalidated of reason: string
    /// <summary>The engine rejected the operation with the named DOMException, such as AbortError.</summary>
    | Aborted of name: string

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun n0 ->
            match this with
            | ResourceFailure.LoadFailed value -> "LoadFailed", [ Some (fun _ -> NativeDisplay.text context n0 value) ]
            | ResourceFailure.DecodeFailed value -> "DecodeFailed", [ Some (fun _ -> NativeDisplay.text context n0 value) ]
            | ResourceFailure.Cancelled -> "Cancelled", []
            | ResourceFailure.Invalidated value -> "Invalidated", [ Some (fun _ -> NativeDisplay.text context n0 value) ]
            | ResourceFailure.Aborted value -> "Aborted", [ Some (fun _ -> NativeDisplay.text context n0 value) ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A decoded image the operation owns until it is disposed.</summary>
/// <remarks><c>SourceUrl</c> is the requested URL; <c>TransportUrl</c> is the per-operation URL the engine loaded.</remarks>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceImage =
    {
        Width: int
        Height: int
        SourceUrl: string
        TransportUrl: string
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Width", (fun () -> NativeDisplay.int32 context nested (this.Width))
                "Height", (fun () -> NativeDisplay.int32 context nested (this.Height))
                "SourceUrl", (fun () -> NativeDisplay.text context nested (this.SourceUrl))
                "TransportUrl", (fun () -> NativeDisplay.text context nested (this.TransportUrl))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>Decode readiness of an existing image element, with the source URL the element currently uses.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceImageInfo =
    {
        Width: int
        Height: int
        SourceUrl: string
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Width", (fun () -> NativeDisplay.int32 context nested (this.Width))
                "Height", (fun () -> NativeDisplay.int32 context nested (this.Height))
                "SourceUrl", (fun () -> NativeDisplay.text context nested (this.SourceUrl))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A font family registered with the document while the operation is held.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceFont =
    {
        Family: string
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested -> [ "Family", (fun () -> NativeDisplay.text context nested (this.Family)) ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The single terminal outcome of an operation.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceOutcome<'T> =
    | Succeeded of value: 'T
    | Failed of failure: ResourceFailure

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun n0 ->
            match this with
            | ResourceOutcome.Succeeded value ->
                "Succeeded",
                [
                    Some (fun _ ->
                        match box value with
                        | :? string as text -> NativeDisplay.text context n0 text
                        | :? int as number -> NativeDisplay.int32 context n0 number
                        | :? bool as flag -> NativeDisplay.boolean context n0 flag
                        | :? (byte array) as bytes -> NativeDisplay.array context n0 (fun c d _ value -> NativeDisplay.uint8 c d value) bytes
                        | :? ResourceResponse as response -> response.NativeDisplay (context, n0, 3)
                        | :? ResourceImage as image -> image.NativeDisplay (context, n0, 3)
                        | :? ResourceImageInfo as info -> info.NativeDisplay (context, n0, 3)
                        | :? ResourceFont as font -> font.NativeDisplay (context, n0, 3)
                        | :? SnapshotImage as snapshot -> snapshot.NativeDisplay (context, n0, 3)
                        | :? ImageBitmap as bitmap -> NativeDisplay.opaque context n0 "<Ranvier.Dom.ImageBitmap>" (box bitmap)
                        | other -> NativeDisplay.opaque context n0 "<'T>" other)
                ]
            | ResourceOutcome.Failed failure ->
                "Failed",
                [
                    Some (fun p -> NativeDisplay.known context n0 failure (fun value -> value.NativeDisplay (context, n0, p)))
                ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>An operation's state as seen on its owner context.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResourceState<'T> =
    | Pending
    | Settled of outcome: ResourceOutcome<'T>

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun n0 ->
            match this with
            | ResourceState.Pending -> "Pending", []
            | ResourceState.Settled outcome ->
                "Settled",
                [
                    Some (fun p -> NativeDisplay.known context n0 outcome (fun value -> value.NativeDisplay (context, n0, p)))
                ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>
/// Starts a request and reports its result through <c>complete</c>, from any thread, synchronously or later, once or
/// repeatedly. The service uses only the first result. Disposing the returned handle withdraws the request.
/// </summary>
type ResourceProvider = ResourceRequest -> (Result<ResourceResponse, string> -> unit) -> IDisposable

/// <summary>A pending or settled operation. Cancel and Dispose are idempotent and run on the owner context.</summary>
/// <remarks>The terminal callback runs exactly once, including for cancellation; Dispose then releases any value.</remarks>
type ResourceOperation<'T> =
    inherit IDisposable
    abstract Snapshot: ResourceState<'T>
    abstract Cancel: unit -> unit

/// <summary>Loads resources and observes image and font readiness for one document or host.</summary>
/// <remarks>Terminal callbacks run on the service's owner context.</remarks>
type ResourceService =
    inherit IDisposable
    abstract Load: request: ResourceRequest * terminal: (ResourceOutcome<ResourceResponse> -> unit) -> ResourceOperation<ResourceResponse>
    abstract LoadImage: request: ResourceRequest * terminal: (ResourceOutcome<ResourceImage> -> unit) -> ResourceOperation<ResourceImage>
    abstract DecodeImage: target: Element * terminal: (ResourceOutcome<ResourceImageInfo> -> unit) -> ResourceOperation<ResourceImageInfo>

    abstract LoadFont:
        family: string * request: ResourceRequest * terminal: (ResourceOutcome<ResourceFont> -> unit) -> ResourceOperation<ResourceFont>

    /// <summary>Removes this service's cached successful responses.</summary>
    abstract ClearCache: unit -> unit

/// <summary>
/// Settles an operation once. <c>Settle</c> may be called from any thread and any number of times; the first call wins
/// and its effects run through <c>post</c> on the owner context. <c>Cancel</c> and <c>Dispose</c> run on the owner context.
/// </summary>
/// <param name="post">Runs an action on the owner context.</param>
/// <param name="terminal">Receives the one terminal outcome on the owner context.</param>
/// <param name="withdraw">Stops pending engine or provider work after cancellation; called at most once.</param>
/// <param name="release">Releases a delivered value when the operation is disposed; called at most once.</param>
type ResourceArbiter<'T>(post: (unit -> unit) -> unit, terminal: ResourceOutcome<'T> -> unit, withdraw: unit -> unit, release: 'T -> unit) =
    let mutable decided = 0
    let mutable state = ResourceState<'T>.Pending
    let mutable disposed = false
    let mutable released = false
    let mutable withdrawn = false

    let claim () =
#if FABLE_COMPILER
        if decided = 0 then
            decided <- 1
            true
        else
            false
#else
        Interlocked.CompareExchange (&decided, 1, 0) = 0
#endif

    let releaseValue () =
        match state with
        | ResourceState.Settled (ResourceOutcome.Succeeded value) when not released ->
            released <- true
            release value
        | _ -> ()

    let stopWork () =
        if not withdrawn then
            withdrawn <- true
            withdraw ()

    let deliver outcome =
        state <- ResourceState.Settled outcome
        terminal outcome

        if disposed then
            releaseValue ()

    /// <summary>Offers an outcome; returns true when it is the first.</summary>
    member _.Settle(outcome: ResourceOutcome<'T>) =
        if claim () then
            post (fun () -> deliver outcome)
            true
        else
            false

    member _.Snapshot = state

    member _.Cancel() =
        if claim () then
            stopWork ()
            deliver (ResourceOutcome.Failed ResourceFailure.Cancelled)

    member this.Dispose() =
        if not disposed then
            this.Cancel ()
            disposed <- true
            releaseValue ()

    interface ResourceOperation<'T> with
        member this.Snapshot = this.Snapshot

        member this.Cancel() =
            this.Cancel ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()
