namespace Ranvier.Dom

open System
open System.Collections.Generic

/// <summary><c>ResizeObserverOptions.box</c>: the box whose size changes are reported.</summary>
[<RequireQualifiedAccess>]
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResizeObserverBox =
    /// <summary><c>"content-box"</c>, the default.</summary>
    | ContentBox
    /// <summary><c>"border-box"</c>.</summary>
    | BorderBox
    /// <summary><c>"device-pixel-content-box"</c>: the content box in device pixels.</summary>
    | DevicePixelContentBox

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, precedence: int) =
        NativeDisplay.union context depth precedence (box this) (fun nested ->
            match this with
            | ResizeObserverBox.ContentBox -> "ContentBox", []
            | ResizeObserverBox.BorderBox -> "BorderBox", []
            | ResizeObserverBox.DevicePixelContentBox -> "DevicePixelContentBox", [])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A <c>ResizeObserverSize</c>: a box size in the element's writing mode.</summary>
/// <remarks>Inline is the width in a horizontal writing mode and the height in a vertical one.</remarks>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResizeObserverSize =
    {
        InlineSize: float
        BlockSize: float
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "InlineSize", (fun () -> NativeDisplay.float64 context nested (this.InlineSize))
                "BlockSize", (fun () -> NativeDisplay.float64 context nested (this.BlockSize))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary><c>ResizeObserverEntry.contentRect</c> in CSS pixels: the content box at its offset in the padding box.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResizeObserverRect =
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

/// <summary>A copied <c>ResizeObserverEntry</c>, valid after its delivery returns.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResizeObserverEntry =
    {
        Target: Element
        ContentRect: ResizeObserverRect
        /// <summary><c>contentBoxSize</c> in CSS pixels, one size per fragment.</summary>
        ContentBoxSize: ResizeObserverSize array
        /// <summary><c>borderBoxSize</c> in CSS pixels, one size per fragment.</summary>
        BorderBoxSize: ResizeObserverSize array
        /// <summary><c>devicePixelContentBoxSize</c>; empty where the engine lacks the device-pixel content box.</summary>
        DevicePixelContentBoxSize: ResizeObserverSize array
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Target", (fun () -> NativeDisplay.opaque context nested "<Ranvier.Dom.Element>" (box (this.Target)))
                "ContentRect",
                (fun () ->
                    NativeDisplay.known context nested (this.ContentRect) (fun (retained: ResizeObserverRect) ->
                        retained.NativeDisplay (context, nested, 3)))
                "ContentBoxSize",
                (fun () ->
                    NativeDisplay.array
                        context
                        nested
                        (fun c157 d157 p157 (item157: ResizeObserverSize) ->
                            NativeDisplay.known c157 d157 (item157) (fun (retained: ResizeObserverSize) ->
                                retained.NativeDisplay (c157, d157, p157)))
                        (this.ContentBoxSize))
                "BorderBoxSize",
                (fun () ->
                    NativeDisplay.array
                        context
                        nested
                        (fun c159 d159 p159 (item159: ResizeObserverSize) ->
                            NativeDisplay.known c159 d159 (item159) (fun (retained: ResizeObserverSize) ->
                                retained.NativeDisplay (c159, d159, p159)))
                        (this.BorderBoxSize))
                "DevicePixelContentBoxSize",
                (fun () ->
                    NativeDisplay.array
                        context
                        nested
                        (fun c161 d161 p161 (item161: ResizeObserverSize) ->
                            NativeDisplay.known c161 d161 (item161) (fun (retained: ResizeObserverSize) ->
                                retained.NativeDisplay (c161, d161, p161)))
                        (this.DevicePixelContentBoxSize))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>One engine observer of a <c>ResizeObservationProvider</c>.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResizeObserverHandle =
    {
        /// <summary><c>ResizeObserver.observe</c> with a box the provider supports.</summary>
        Observe: Element -> ResizeObserverBox -> unit
        Unobserve: Element -> unit
        Disconnect: unit -> unit
        /// <summary>Disconnects and frees the engine observer; its callback will not run after this returns.</summary>
        Release: unit -> unit
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Observe", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Observe)))
                "Unobserve", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Unobserve)))
                "Disconnect", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Disconnect)))
                "Release", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Release)))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>The engine side of resize observation for one document.</summary>
#if !FABLE_COMPILER
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
#endif
type ResizeObservationProvider =
    {
        Supports: ResizeObserverBox -> bool
        /// <summary>Creates an engine observer that calls the function once per notification, with its entries in
        /// observation order. An exception the function raises is reported by the provider and leaves other
        /// observers delivering.</summary>
        Create: (ResizeObserverEntry array -> unit) -> ResizeObserverHandle
    }

#if !FABLE_COMPILER
    member internal this.NativeDisplay(context: NativeDisplay.Context, depth: int, _precedence: int) =
        NativeDisplay.record context depth (box this) (fun nested ->
            [
                "Supports", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Supports)))
                "Create", (fun () -> NativeDisplay.opaque context nested "<function>" (box (this.Create)))
            ])

    override this.ToString() =
        if System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported then
            sprintf "%+A" this
        else
            NativeDisplay.format (fun context depth precedence -> this.NativeDisplay (context, depth, precedence))
#endif

/// <summary>A <c>ResizeObserver</c>; disposing it disconnects and frees it.</summary>
/// <remarks>
/// Each target is reported when first observed, also at 0 by 0, and again when its observed box changes size, during
/// the rendering update. Observing an observed target with another box replaces its observation and reports it again;
/// with the same box it changes nothing. Unobserve and Disconnect drop pending notifications.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ResizeObserver =
    inherit IDisposable
    /// <summary>Observes <c>target</c>'s <c>box</c>, the content box by default.</summary>
    /// <remarks>Raises NotSupportedException for a box the service does not support.</remarks>
    abstract Observe: target: Element * ?box: ResizeObserverBox -> unit
    abstract Unobserve: target: Element -> unit
    /// <summary>Unobserves every target; the observer observes again afterwards.</summary>
    abstract Disconnect: unit -> unit

/// <summary>Creates resize observers of one document.</summary>
/// <remarks>Disposing the service disposes every observer it created.</remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type ResizeObservationService =
    inherit IDisposable
    /// <summary>Whether <c>Observe</c> accepts <c>box</c>.</summary>
    abstract Supports: box: ResizeObserverBox -> bool
    /// <summary>A new observer delivering each notification's entries to <c>callback</c>.</summary>
    /// <remarks>
    /// The observer is disposed when <c>signal</c> aborts; a signal that has already aborted returns a disposed
    /// observer.
    /// </remarks>
    abstract Create: callback: (ResizeObserverEntry array -> unit) * ?signal: AbortSignal -> ResizeObserver

/// <summary>Creates resize observation services over a provider.</summary>
[<RequireQualifiedAccess>]
module ResizeObservation =
    /// <summary>A service over <c>provider</c>.</summary>
    let create (provider: ResizeObservationProvider) : ResizeObservationService =
        if isNull (box provider) then
            nullArg "provider"

        let mutable disposed = false
        let observers = List<IDisposable>()

        let live () =
            if disposed then
                raise (ObjectDisposedException (nameof ResizeObservationService))

        let observer (callback: ResizeObserverEntry array -> unit) (signal: AbortSignal option) =
            let mutable released = false
            let mutable handle = None
            let mutable registration: IDisposable = null

            let self =
                { new ResizeObserver with
                    member _.Observe(target, ?observed) =
                        if released then
                            raise (ObjectDisposedException (nameof ResizeObserver))

                        if isNull (box target) then
                            nullArg "target"

                        let observed = defaultArg observed ResizeObserverBox.ContentBox

                        if not (provider.Supports observed) then
                            raise (NotSupportedException "The engine does not support the requested box")

                        handle.Value.Observe target observed

                    member _.Unobserve target =
                        if released then
                            raise (ObjectDisposedException (nameof ResizeObserver))

                        if isNull (box target) then
                            nullArg "target"

                        handle.Value.Unobserve target

                    member _.Disconnect() =
                        if released then
                            raise (ObjectDisposedException (nameof ResizeObserver))

                        handle.Value.Disconnect ()

                    member this.Dispose() =
                        if not released then
                            released <- true
                            observers.Remove this |> ignore

                            if not (isNull registration) then
                                registration.Dispose ()

                            match handle with
                            | Some engine -> engine.Release ()
                            | None -> ()
                }

            match signal with
            | Some signal when signal.Aborted -> released <- true
            | _ ->
                handle <-
                    Some (
                        provider.Create (fun entries ->
                            if not released then
                                callback entries)
                    )

                observers.Add self |> ignore

                match signal with
                | Some signal -> registration <- signal.OnAbort (fun _ -> self.Dispose ())
                | None -> ()

            self

        { new ResizeObservationService with
            member _.Supports observed =
                live ()
                provider.Supports observed

            member _.Create(callback, ?signal) =
                live ()

                if isNull (box callback) then
                    nullArg "callback"

                observer callback signal

            member _.Dispose() =
                if not disposed then
                    disposed <- true

                    for observer in Seq.toArray observers do
                        observer.Dispose ()
        }
