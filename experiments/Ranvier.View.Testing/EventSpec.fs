namespace Ranvier.View.Testing

open Fable.Core
open Fable.Core.TS.Dom

type EventKind =
    | Plain
    | Mouse
    | Pointer
    | Keyboard
    | Input
    | Focus

/// <summary>An event to dispatch: its type, constructor family, flags and the extra fields of its init dictionary.</summary>
type EventSpec =
    {
        Type: string
        Kind: EventKind
        Bubbles: bool
        Cancelable: bool
        Init: (string * obj) list
    }

[<RequireQualifiedAccess>]
module EventSpec =
    let constructorName (kind: EventKind) =
        match kind with
        | Plain -> "Event"
        | Mouse -> "MouseEvent"
        | Pointer -> "PointerEvent"
        | Keyboard -> "KeyboardEvent"
        | Input -> "InputEvent"
        | Focus -> "FocusEvent"

#if FABLE_COMPILER
    [<Emit("new (globalThis[$0])($1, $2)")>]
    let private construct (name: string) (eventType: string) (init: obj) : Event = jsNative

    /// <summary>The event under the host's constructors; a jsdom runner exposes them on <c>globalThis</c>.</summary>
    let create (_: Document) (spec: EventSpec) : Event =
        let init =
            JsInterop.createObj (
                ("bubbles", box spec.Bubbles)
                :: ("cancelable", box spec.Cancelable)
                :: spec.Init
            )

        construct (constructorName spec.Kind) spec.Type init
#else
    /// <summary>The creator of events on .NET; the DOM twin installs one per document.</summary>
    type IEventSource =
        abstract Create: EventSpec -> Event

    [<Sealed; AbstractClass>]
    type private Slot =
        [<System.ThreadStatic; DefaultValue>]
        static val mutable private current: IEventSource

        [<System.ThreadStatic; DefaultValue>]
        static val mutable private depth: int

        static member Current
            with get () = Slot.current
            and set (value: IEventSource) = Slot.current <- value

        static member Depth
            with get () = Slot.depth
            and set (value: int) = Slot.depth <- value

    /// <summary>
    /// Installs <c>source</c> for the current thread until the returned value is disposed. Disposal restores the
    /// previous source; it raises <c>InvalidOperationException</c> on another thread or while a later installation is
    /// current.
    /// </summary>
    let install (source: IEventSource) : System.IDisposable =
        let previous = Slot.Current
        let thread = System.Environment.CurrentManagedThreadId
        Slot.Current <- source
        Slot.Depth <- Slot.Depth + 1
        let depth = Slot.Depth
        let disposed = ref false

        { new System.IDisposable with
            member _.Dispose() =
                if not disposed.Value then
                    let current = System.Environment.CurrentManagedThreadId

                    if current <> thread then
                        raise (System.InvalidOperationException $"An event source installed on thread {thread} was disposed on thread {current}")

                    if Slot.Depth <> depth then
                        raise (System.InvalidOperationException "An event source was disposed while a later installation is current")

                    disposed.Value <- true
                    Slot.Depth <- depth - 1
                    Slot.Current <- previous
        }

    let create (_: Document) (spec: EventSpec) : Event =
        match box Slot.Current with
        | null -> raise (System.InvalidOperationException "No event source is installed on this thread; create a TwinDocument first")
        | source -> (source :?> IEventSource).Create spec
#endif
