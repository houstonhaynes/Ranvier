namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary>The <c>prefers-color-scheme</c> value a host supplies.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ColorSchemePreference =
    | Light
    | Dark

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | ColorSchemePreference.Light -> "Light", []
            | ColorSchemePreference.Dark -> "Dark", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The <c>prefers-reduced-motion</c> value a host supplies.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type MotionPreference =
    | NoPreference
    | Reduce

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | MotionPreference.NoPreference -> "NoPreference", []
            | MotionPreference.Reduce -> "Reduce", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The <c>prefers-contrast</c> value a host supplies.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ContrastPreference =
    | NoPreference
    | More

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | ContrastPreference.NoPreference -> "NoPreference", []
            | ContrastPreference.More -> "More", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The accuracy of pointing devices, as <c>pointer</c> and <c>any-pointer</c> report it.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type PointerAccuracy =
    | NoPointer
    | Coarse
    | Fine
    /// <summary>Coarse and fine devices together; <c>any-pointer</c> only.</summary>
    | CoarseAndFine

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | PointerAccuracy.NoPointer -> "NoPointer", []
            | PointerAccuracy.Coarse -> "Coarse", []
            | PointerAccuracy.Fine -> "Fine", []
            | PointerAccuracy.CoarseAndFine -> "CoarseAndFine", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>
/// The user preferences and input devices a host supplies to media query evaluation. <c>None</c> keeps the engine's
/// own value: light, no motion or contrast preference, no pointing device, no hover and text at its page size.
/// </summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type MediaPreferences =
    {
        ColorScheme: ColorSchemePreference option
        ReducedMotion: MotionPreference option
        Contrast: ContrastPreference option
        /// <summary>The primary pointing device, for <c>pointer</c>.</summary>
        Pointer: PointerAccuracy option
        /// <summary>Whether the primary pointing device hovers, for <c>hover</c>.</summary>
        Hover: bool option
        /// <summary>Every available pointing device, for <c>any-pointer</c>.</summary>
        AnyPointer: PointerAccuracy option
        AnyHover: bool option
        /// <summary>The platform text scale, from 0.25 to 8: laid-out text is this many times its font size.</summary>
        TextScale: float option
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "ColorScheme",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c121 d121 p121 (item121: ColorSchemePreference) ->
                            NativeDisplay.known c121 d121 (item121) (fun (retained: ColorSchemePreference) ->
                                retained.NativeDisplay (c121, d121, p121)))
                        (this.ColorScheme))
                "ReducedMotion",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c123 d123 p123 (item123: MotionPreference) ->
                            NativeDisplay.known c123 d123 (item123) (fun (retained: MotionPreference) -> retained.NativeDisplay (c123, d123, p123)))
                        (this.ReducedMotion))
                "Contrast",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c125 d125 p125 (item125: ContrastPreference) ->
                            NativeDisplay.known c125 d125 (item125) (fun (retained: ContrastPreference) ->
                                retained.NativeDisplay (c125, d125, p125)))
                        (this.Contrast))
                "Pointer",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c127 d127 p127 (item127: PointerAccuracy) ->
                            NativeDisplay.known c127 d127 (item127) (fun (retained: PointerAccuracy) -> retained.NativeDisplay (c127, d127, p127)))
                        (this.Pointer))
                "Hover",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c129 d129 p129 (item129: System.Boolean) -> NativeDisplay.boolean c129 d129 (item129))
                        (this.Hover))
                "AnyPointer",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c131 d131 p131 (item131: PointerAccuracy) ->
                            NativeDisplay.known c131 d131 (item131) (fun (retained: PointerAccuracy) -> retained.NativeDisplay (c131, d131, p131)))
                        (this.AnyPointer))
                "AnyHover",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c133 d133 p133 (item133: System.Boolean) -> NativeDisplay.boolean c133 d133 (item133))
                        (this.AnyHover))
                "TextScale",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c135 d135 p135 (item135: System.Double) -> NativeDisplay.float64 c135 d135 (item135))
                        (this.TextScale))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

    static member Default =
        {
            ColorScheme = None
            ReducedMotion = None
            Contrast = None
            Pointer = None
            Hover = None
            AnyPointer = None
            AnyHover = None
            TextScale = None
        }

/// <summary>A <c>MediaQueryList</c> owned by its holder: disposing it removes its subscriptions and releases it.</summary>
type MediaQuery =
    inherit IDisposable
    /// <summary><c>MediaQueryList.media</c>, the serialized query.</summary>
    abstract Media: string
    abstract Matches: bool
    /// <summary>
    /// Adds an observer of the list's <c>change</c> events, called with the new <c>matches</c>, and returns an
    /// idempotent removal handle. A removed observer receives no later change.
    /// </summary>
    abstract Subscribe: observer: (bool -> unit) -> IDisposable

/// <summary>The supply side of a host's media preferences.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type MediaPreferenceSupply =
    {
        /// <summary>Supplies every preference at once; the engine evaluates them at its next rendering update.</summary>
        Supply: MediaPreferences -> unit
        /// <summary>The preferences last supplied.</summary>
        Current: unit -> MediaPreferences
        /// <summary>The media features whose user preference the engine has no input for.</summary>
        Unavailable: string list
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Supply", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Supply)))
                "Current", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Current)))
                "Unavailable",
                (fun () ->
                    NativeDisplay.list
                        context
                        nested
                        (fun c119 d119 p119 (item119: System.String) -> NativeDisplay.text c119 d119 (item119))
                        (this.Unavailable))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The window of one document and, when the host can supply them, its media preferences.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type MediaQueryProvider =
    {
        /// <summary><c>window.matchMedia</c>.</summary>
        MatchMedia: string -> ViewportMediaQuery
        /// <summary><c>None</c> when preferences come from the platform, as in a browser.</summary>
        Preferences: MediaPreferenceSupply option
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "MatchMedia", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.MatchMedia)))
                "Preferences",
                (fun () ->
                    NativeDisplay.option
                        context
                        nested
                        3
                        (fun c138 d138 p138 (item138: MediaPreferenceSupply) ->
                            NativeDisplay.known c138 d138 (item138) (fun (retained: MediaPreferenceSupply) ->
                                retained.NativeDisplay (c138, d138, p138)))
                        (this.Preferences))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>Media query evaluation, change subscription and host preference supply of one document.</summary>
/// <remarks>
/// Observers run on the dispatching thread. Within one rendering update each list whose matches changed notifies its
/// observers once. Disposing the service disposes every query it created.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type MediaQueryService =
    inherit IDisposable
    /// <summary><c>window.matchMedia</c> as an owned query.</summary>
    abstract Match: query: string -> MediaQuery
    /// <summary>Whether the host supplies preferences; a browser's come from the platform.</summary>
    abstract CanSupply: bool
    /// <summary>
    /// Supplies the host's preferences. Raises NotSupportedException when <c>CanSupply</c> is false, and
    /// ArgumentException for <c>CoarseAndFine</c> as the primary pointer or a text scale outside 0.25 to 8.
    /// </summary>
    abstract Supply: preferences: MediaPreferences -> unit
    /// <summary>The preferences last supplied; raises NotSupportedException when <c>CanSupply</c> is false.</summary>
    abstract Supplied: unit -> MediaPreferences
    /// <summary>The media features whose user preference the host cannot supply; empty in a browser.</summary>
    abstract Unavailable: string list

/// <summary>Creates media query services.</summary>
[<RequireQualifiedAccess>]
module MediaQueries =
    /// <summary>Raises ArgumentException for preferences no host can supply.</summary>
    let validate (preferences: MediaPreferences) =
        if isNull (box preferences) then
            nullArg "preferences"

        if preferences.Pointer = Some PointerAccuracy.CoarseAndFine then
            invalidArg "preferences" "The primary pointer is one device: CoarseAndFine applies to AnyPointer only"

        match preferences.TextScale with
        | Some scale when not (scale >= 0.25 && scale <= 8.0) -> invalidArg "preferences" "TextScale is from 0.25 to 8"
        | _ -> ()

    /// <summary>A service over <c>provider</c>.</summary>
    let create (provider: MediaQueryProvider) : MediaQueryService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable disposed = false
        let closers = Dictionary<int, unit -> unit>()
        let mutable nextQuery = 0

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof MediaQueryService))

        let supply () =
            match provider.Preferences with
            | Some supply -> supply
            | None -> raise (NotSupportedException "This host's media preferences come from the platform")

        let open' (query: string) : MediaQuery =
            let list = provider.MatchMedia query
            let observers = List<int * (bool -> unit)>()
            let mutable next = 0
            let mutable closed = false

            let listener (_: Event) =
                if not closed then
                    let matches = list.Matches

                    for _, observer in observers.ToArray () do
                        if not closed then
                            observer matches

            list.Target.addEventListener ("change", listener)
            let key = nextQuery
            nextQuery <- nextQuery + 1

            let close () =
                if not closed then
                    closed <- true
                    observers.Clear ()
                    closers.Remove key |> ignore

                    try
                        list.Target.removeEventListener ("change", listener)
                        list.Dispose ()
                    with :? ObjectDisposedException ->
                        ()

            closers[key] <- close

            { new MediaQuery with
                member _.Media =
                    if closed then
                        raise (ObjectDisposedException (nameof MediaQuery))

                    list.Media

                member _.Matches =
                    if closed then
                        raise (ObjectDisposedException (nameof MediaQuery))

                    list.Matches

                member _.Subscribe observer =
                    if closed then
                        raise (ObjectDisposedException (nameof MediaQuery))

                    if isNull (box observer) then
                        nullArg "observer"

                    let id = next
                    next <- next + 1
                    observers.Add ((id, observer))

                    { new IDisposable with
                        member _.Dispose() =
                            observers.RemoveAll (fun (key, _) -> key = id)
                            |> ignore
                    }

                member _.Dispose() = close ()
            }

        { new MediaQueryService with
            member _.Match query =
                live ()

                if isNull query then
                    nullArg "query"

                open' query

            member _.CanSupply = provider.Preferences.IsSome

            member _.Supply preferences =
                live ()
                validate preferences
                (supply ()).Supply preferences

            member _.Supplied() =
                live ()
                (supply ()).Current()

            member _.Unavailable =
                match provider.Preferences with
                | Some supply -> supply.Unavailable
                | None -> []

            member _.Dispose() =
                if not disposed then
                    disposed <- true

                    for close in Seq.toArray closers.Values do
                        close ()
        }
