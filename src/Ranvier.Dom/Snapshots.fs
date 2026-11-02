namespace Ranvier.Dom

open System


/// <summary>What a snapshot paints.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SnapshotSubject =
    /// <summary>The viewport, as a frame paints it.</summary>
    | Viewport
    /// <summary>The element's subtree alone, over a transparent background.</summary>
    | Element of element: Element

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | SnapshotSubject.Viewport -> "Viewport", []
            | SnapshotSubject.Element (caseValue0) ->
                "Element",
                [
                    Some (fun _ -> NativeDisplay.opaque context nested "<Ranvier.Dom.Element>" (box (caseValue0)))
                ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>An area in CSS pixels from the subject's top left; width and height are positive.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SnapshotCrop =
    {
        X: float
        Y: float
        Width: float
        Height: float
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "X", (fun () -> NativeDisplay.float64 context nested (this.X))
                "Y", (fun () -> NativeDisplay.float64 context nested (this.Y))
                "Width", (fun () -> NativeDisplay.float64 context nested (this.Width))
                "Height", (fun () -> NativeDisplay.float64 context nested (this.Height))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>How a snapshot is painted and encoded.</summary>
/// <remarks>
/// <c>Scale</c> is device pixels per CSS pixel from 0.25 to 8, or <c>None</c> for the host's device scale. A crop is
/// expanded to whole CSS pixels and intersected with the subject.
/// </remarks>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type SnapshotOptions =
    {
        Scale: float option
        Crop: SnapshotCrop option
        ColorSpace: SnapshotColorSpace
        Encoding: ImageEncoding
        Quality: float option
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Scale",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c213 d213 p213 (item213: System.Double) -> NativeDisplay.float64 c213 d213 (item213))
                        (this.Scale))
                "Crop",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c215 d215 p215 (item215: SnapshotCrop) ->
                            NativeDisplay.known c215 d215 (item215) (fun (retained: SnapshotCrop) -> retained.NativeDisplay (c215, d215, p215)))
                        (this.Crop))
                "ColorSpace",
                (fun () ->
                    NativeDisplay.known context nested (this.ColorSpace) (fun (retained: SnapshotColorSpace) ->
                        retained.NativeDisplay (context, nested, 3)))
                "Encoding",
                (fun () ->
                    NativeDisplay.known context nested (this.Encoding) (fun (retained: ImageEncoding) ->
                        retained.NativeDisplay (context, nested, 3)))
                "Quality",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c219 d219 p219 (item219: System.Double) -> NativeDisplay.float64 c219 d219 (item219))
                        (this.Quality))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif


/// <summary>Snapshots of a host's viewport or elements (Ranvier.WebCore #98).</summary>
/// <remarks>
/// The subject is painted during <c>Capture</c>; the terminal callback runs on the service's owner context after the
/// call returns. An ordinary browser context has no DOM capture: its service reports <c>Available = false</c> and
/// <c>Capture</c> raises NotSupportedException, never painting another subject in its place.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type SnapshotService =
    inherit IDisposable
    /// <summary>Whether this provider can capture its document at all.</summary>
    abstract Available: bool
    /// <summary>Whether this provider supports the color space.</summary>
    abstract SupportsColorSpace: colorSpace: SnapshotColorSpace -> bool
    /// <summary>Whether this provider supports the encoding.</summary>
    abstract SupportsEncoding: encoding: ImageEncoding -> bool

    /// <summary>Paints and encodes the subject.</summary>
    /// <exception cref="T:System.NotSupportedException">Capture, the color space or the encoding is unavailable.</exception>
    /// <exception cref="T:System.ArgumentOutOfRangeException">The scale, crop or quality is out of range.</exception>
    abstract Capture:
        subject: SnapshotSubject * options: SnapshotOptions * terminal: (ResourceOutcome<SnapshotImage> -> unit) -> ResourceOperation<SnapshotImage>

/// <summary>Defaults and validation shared by snapshot providers.</summary>
module SnapshotContract =
    /// <summary>An sRGB RGBA8 snapshot at the host's device scale.</summary>
    let defaults =
        {
            Scale = None
            Crop = None
            ColorSpace = SnapshotColorSpace.Srgb
            Encoding = ImageEncoding.Rgba8
            Quality = None
        }

    let private finite (value: float) =
        not (Double.IsNaN value || Double.IsInfinity value)

    /// <summary>Raises ArgumentOutOfRangeException for a scale or crop out of range, and ArgumentNullException for a null element.</summary>
    let options (subject: SnapshotSubject) (value: SnapshotOptions) =
        match subject with
        | SnapshotSubject.Element element when isNull (box element) -> nullArg "subject"
        | _ -> ()

        match value.Scale with
        | Some scale when not (scale >= 0.25 && scale <= 8.0) -> raise (ArgumentOutOfRangeException ("options", "Scale must be from 0.25 to 8"))
        | _ -> ()

        match value.Crop with
        | Some crop when
            not (
                finite crop.X
                && finite crop.Y
                && finite crop.Width
                && finite crop.Height
                && crop.Width > 0.0
                && crop.Height > 0.0
            )
            ->
            raise (ArgumentOutOfRangeException ("options", "A crop needs finite coordinates and a positive size"))
        | _ -> ()

        value
