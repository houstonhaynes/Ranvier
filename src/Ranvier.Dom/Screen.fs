namespace Ranvier.Dom

open System

/// <summary>A ScreenOrientation type, numbered as the native <c>RV_SCREEN_*</c> values.</summary>
type ScreenOrientationType =
    | PortraitPrimary = 0
    | PortraitSecondary = 1
    | LandscapePrimary = 2
    | LandscapeSecondary = 3

/// <summary>The screen a document reports through Screen and ScreenOrientation, in CSS pixels.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ScreenInfo =
    {
        Width: int
        Height: int
        AvailLeft: int
        AvailTop: int
        AvailWidth: int
        AvailHeight: int
        ColorDepth: int
        PixelDepth: int
        Orientation: ScreenOrientationType
        Angle: int
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Width", (fun () -> NativeDisplay.int32 context nested (this.Width))
                "Height", (fun () -> NativeDisplay.int32 context nested (this.Height))
                "AvailLeft", (fun () -> NativeDisplay.int32 context nested (this.AvailLeft))
                "AvailTop", (fun () -> NativeDisplay.int32 context nested (this.AvailTop))
                "AvailWidth", (fun () -> NativeDisplay.int32 context nested (this.AvailWidth))
                "AvailHeight", (fun () -> NativeDisplay.int32 context nested (this.AvailHeight))
                "ColorDepth", (fun () -> NativeDisplay.int32 context nested (this.ColorDepth))
                "PixelDepth", (fun () -> NativeDisplay.int32 context nested (this.PixelDepth))
                "Orientation", (fun () -> NativeDisplay.manual context nested (box (this.Orientation)) (fun () -> (this.Orientation).ToString()))
                "Angle", (fun () -> NativeDisplay.int32 context nested (this.Angle))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The outcome of a ScreenOrientation lock request.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ScreenLockOutcome =
    | Locked
    | Unsupported of reason: string
    | Failed of name: string * detail: string

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | ScreenLockOutcome.Locked -> "Locked", []
            | ScreenLockOutcome.Unsupported (caseValue0) -> "Unsupported", [ Some (fun _ -> NativeDisplay.text context nested (caseValue0)) ]
            | ScreenLockOutcome.Failed (caseValue0, caseValue1) ->
                "Failed",
                [
                    Some (fun _ -> NativeDisplay.text context nested (caseValue0))
                    Some (fun _ -> NativeDisplay.text context nested (caseValue1))
                ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The screen of one document: its values, their changes and orientation locking.</summary>
/// <remarks>
/// A change is delivered when a read would return a different ScreenInfo: the screen, its work area, depth or
/// orientation changed. Orientation locking is Unsupported on the native host; a browser reports its own result.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ScreenService =
    inherit IDisposable
    /// <summary>The current screen.</summary>
    abstract Read: unit -> ScreenInfo
    /// <summary>Calls <c>handler</c> with the new screen after each change, until the subscription is disposed.</summary>
    abstract Subscribe: handler: (ScreenInfo -> unit) -> IDisposable
    /// <summary>Requests an orientation lock, named as the OrientationLockType enumeration (<c>portrait</c>, ...).</summary>
    abstract Lock: orientation: string -> Async<ScreenLockOutcome>
    /// <summary>Releases an orientation lock; a provider without locking does nothing.</summary>
    abstract Unlock: unit -> unit

/// <summary>Values and checks shared by screen providers.</summary>
module ScreenContract =
    /// <summary>The OrientationLockType names.</summary>
    let lockTypes =
        [|
            "any"
            "natural"
            "landscape"
            "portrait"
            "portrait-primary"
            "portrait-secondary"
            "landscape-primary"
            "landscape-secondary"
        |]

    /// <summary>Raises ArgumentNullException for a null name and ArgumentException for a name outside OrientationLockType.</summary>
    let lockType (name: string) =
        if isNull name then
            nullArg "orientation"

        if not (Array.contains name lockTypes) then
            invalidArg "orientation" $"'{name}' is not an OrientationLockType."

        name

    /// <summary>The OrientationType name of <c>orientation</c>.</summary>
    let orientationName (orientation: ScreenOrientationType) =
        match orientation with
        | ScreenOrientationType.PortraitPrimary -> "portrait-primary"
        | ScreenOrientationType.PortraitSecondary -> "portrait-secondary"
        | ScreenOrientationType.LandscapePrimary -> "landscape-primary"
        | ScreenOrientationType.LandscapeSecondary -> "landscape-secondary"
        | _ -> raise (ArgumentOutOfRangeException "orientation")

    /// <summary>The orientation named <c>name</c>, as OrientationType spells it.</summary>
    let orientationOf (name: string) =
        match name with
        | "portrait-primary" -> ScreenOrientationType.PortraitPrimary
        | "portrait-secondary" -> ScreenOrientationType.PortraitSecondary
        | "landscape-primary" -> ScreenOrientationType.LandscapePrimary
        | "landscape-secondary" -> ScreenOrientationType.LandscapeSecondary
        | _ -> invalidArg "name" $"'{name}' is not an OrientationType."

    /// <summary>The lock outcome of a provider without orientation locking.</summary>
    let unsupportedLock: ScreenLockOutcome =
        ScreenLockOutcome.Unsupported "Screen orientation locking is not supported."

    /// <summary>Raises ArgumentNullException for a null handler.</summary>
    let handler (value: ScreenInfo -> unit) =
        if isNull (box value) then
            nullArg "handler"

        value
