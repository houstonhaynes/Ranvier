namespace Ranvier.View.WebCore

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open Fable.Core.TS.Dom
open Ranvier.View
open RanvierWebCoreAbi

/// <summary>The wrapper classes of new node and event handles.</summary>
module internal Factories =
    let node (session: Session) (handle: nativeint) : NativeObject =
        match Native.rv_node_type handle with
        | 1 -> Classes.Element (session, handle, Interop.text "Node.nodeName" (fun b c l -> Native.rv_node_get_string (handle, 0, b, c, l)))
        | 3 -> TextObject (session, handle)
        | 4 -> CDATASectionObject (session, handle)
        | 7 -> ProcessingInstructionObject (session, handle)
        | 10 -> DocumentTypeObject (session, handle)
        | 8 -> CommentObject (session, handle)
        | 9 -> DocumentObject (session, handle)
        | 11 ->
            let mutable mode = 0

            if NativeDom.rv_dom_shadow_root_get_mode (handle, &mode) = 0 then
                ShadowRootObject (session, handle)
            else
                DocumentFragmentObject (session, handle)
        | 2 -> AttrObject (session, handle)
        | _ -> NodeObject (session, handle)

    let event (session: Session) (handle: nativeint) : NativeObject =
        match Native.rv_event_interface handle with
        | 1 -> UIEventObject (session, handle)
        | 2 ->
            let mutable mode = 0u

            if NativeDom.rv_dom_wheel_event_get_delta_mode (handle, &mode) = 0 then
                WheelEventObject (session, handle)
            else
                MouseEventObject (session, handle)
        | 3 -> PointerEventObject (session, handle)
        | 4 -> KeyboardEventObject (session, handle)
        | 5 -> InputEventObject (session, handle)
        | 6 -> FocusEventObject (session, handle)
        | _ -> EventObject (session, handle)

/// <summary>Posts work to a host's loop, which runs it on the host thread during <c>WebCoreHost.Cycle</c>.</summary>
[<Sealed>]
type internal HostContext(posted: ConcurrentQueue<struct (SendOrPostCallback * obj)>, thread: int) =
    inherit SynchronizationContext()

    override _.Post(callback, state) =
        posted.Enqueue (struct (callback, state))

    override this.Send(callback, state) =
        if Environment.CurrentManagedThreadId = thread then
            callback.Invoke state
        else
            raise (NotSupportedException "A WebCore host runs sent work only on its own thread")

    override this.CreateCopy() = this

/// <summary>
/// A WebCore page installed as the thread's DOM host: Ranvier's view and <c>Fable.Ranvier</c> run against its document.
/// It owns the page's loop (native run-loop cycles, microtasks, timers and animation frames) and takes the app's window
/// input, paints the page and reports its dirty rectangle.
/// </summary>
/// <remarks>
/// Every member runs on the creating thread. A graph created on that thread after the host posts its drains to the
/// host's loop. Disposal removes the installation, releases every native reference and destroys the page.
/// </remarks>
[<Sealed>]
type WebCoreHost private (handle: nativeint, session: Session) =
    let thread = Environment.CurrentManagedThreadId
    let microtasks = Queue<unit -> unit>()
    let posted = ConcurrentQueue<struct (SendOrPostCallback * obj)>()
    let timers = SortedDictionary<struct (float * int), unit -> unit>()
    let timerKeys = Dictionary<int, struct (float * int)>()
    let frames = Dictionary<int, float -> unit>()
    let clock = Stopwatch.StartNew ()
    let mutable advanced = 0.0
    let mutable manual = false

    let context = HostContext (posted, thread)
    let previousContext = SynchronizationContext.Current
    let mutable nextId = 1
    let mutable inInput = 0
    let mutable frameRequested = 0
    let mutable installed: IDisposable list = []
    let mutable frameCallback: Native.FrameCallback = null
    let mutable document: Document = Unchecked.defaultof<_>
    let mutable disposed = false

    let record (error: exn) =
        session.ListenerErrors.Add error

    let runMicrotasks () =
        while microtasks.Count > 0 do
            let task = microtasks.Dequeue ()

            try
                task ()
            with error ->
                record error

    let now () =
        if manual then
            advanced
        else
            clock.Elapsed.TotalMilliseconds + advanced

    let setNow (time: float) =
        advanced <-
            if manual then
                time
            else
                time - clock.Elapsed.TotalMilliseconds

    let runDueTimers () =
        let mutable going = true

        while going && timers.Count > 0 do
            let first = Seq.head timers
            let struct (due, id) = first.Key

            if due <= now () then
                timers.Remove first.Key |> ignore
                timerKeys.Remove id |> ignore

                try
                    first.Value ()
                with error ->
                    record error

                runMicrotasks ()
            else
                going <- false

    // Runs a host operation as the DOM member it stands for, through the member's interceptions.
    let through (target: obj) (name: string) (args: obj array) (original: obj array -> obj) : obj =
        if session.Intercepting then
            Interop.intercept (target :?> NativeObject) name args original
        else
            original args

    /// <summary>Creates a page of <c>width</c> by <c>height</c> CSS pixels and installs it on the current thread.</summary>
    /// <exception cref="T:System.InvalidOperationException">The native library predates the generated DOM functions (ABI 20).</exception>
    static member Create(width: int, height: int) : WebCoreHost =
        if Native.rv_abi_has "dom-generated" <> 1 then
            raise (InvalidOperationException $"RanvierWebCoreHost ABI {Native.rv_abi_version ()} lacks the generated DOM functions (ABI 20)")

        let handle = Native.rv_host_create (width, height)

        if handle = 0n then
            raise (InvalidOperationException "rv_host_create returned NULL")

        Native.rv_host_load_html (handle, "<!DOCTYPE html><html><head></head><body></body></html>")
        |> Interop.check "rv_host_load_html"

        let session = new Session (handle)
        session.NodeFactory <- Func<_, _, _>(Factories.node)
        session.EventFactory <- Func<_, _, _>(Factories.event)
        let host = new WebCoreHost (handle, session)
        host.Install ()
        host

    member private this.Install() =
        document <- unbox<Document>(session.Node (Native.rv_host_document handle))
        session.Pin (box document :?> NativeObject)

        session.AfterListener <-
            Action (fun () ->
                if inInput > 0 then
                    runMicrotasks ())

        frameCallback <-
            Native.FrameCallback (fun _ ->
                Interlocked.Exchange (&frameRequested, 1)
                |> ignore)

        Native.rv_host_set_frame_callback (handle, Marshal.GetFunctionPointerForDelegate<Native.FrameCallback> frameCallback, 0n)
        |> Interop.check "rv_host_set_frame_callback"

        SynchronizationContext.SetSynchronizationContext context
        installed <- [ Native.install this; Fable.Ranvier.Global.Install this ]

    /// <summary>The <c>RvHost</c> handle, for ABI calls the host does not wrap.</summary>
    member _.Handle = handle

    /// <summary>The managed objects of the page: its node and event table and the release queue.</summary>
    member _.Session = session

    /// <summary>The page's document, an HTML document with an empty head and body at creation.</summary>
    member _.Document = document

    /// <summary>Exceptions raised by event listeners, microtasks, timers and animation frames, in raise order.</summary>
    member _.Errors = session.ListenerErrors

    // Loop

    /// <summary>Queues <c>callback</c> to run at the next microtask checkpoint.</summary>
    member _.QueueMicrotask(callback: unit -> unit) =
        microtasks.Enqueue callback

    /// <summary>Runs the queued microtasks, and those they queue, until none remain.</summary>
    member _.RunMicrotasks() =
        runMicrotasks ()

    /// <summary>Calls <c>action</c> after <c>milliseconds</c>, on a later <c>Cycle</c>; returns the timer's id.</summary>
    member _.SetTimeout(action: unit -> unit, milliseconds: float) : int =
        let id = nextId
        nextId <- nextId + 1

        let key = struct (now () + max 0.0 milliseconds, id)

        timers[key] <- action
        timerKeys[id] <- key
        id

    /// <summary>Cancels a timer; an unknown or finished id does nothing.</summary>
    member _.ClearTimeout(id: int) =
        match timerKeys.TryGetValue id with
        | true, key ->
            timers.Remove key |> ignore
            timerKeys.Remove id |> ignore
        | _ -> ()

    /// <summary>Runs <c>callback</c> with the frame time in milliseconds at the next <c>Frame</c>; returns its id.</summary>
    member _.RequestAnimationFrame(callback: float -> unit) : int =
        let id = nextId
        nextId <- nextId + 1
        frames[id] <- callback

        Interlocked.Exchange (&frameRequested, 1)
        |> ignore

        id

    /// <summary>Cancels an animation frame callback that has not run.</summary>
    member _.CancelAnimationFrame(id: int) =
        frames.Remove id |> ignore

    /// <summary>True when the page wants a frame: a rendering update, an invalidation or an animation frame callback.</summary>
    member _.FrameRequested = Volatile.Read &frameRequested = 1

    /// <summary>
    /// One turn of the loop: cycles the native run loop for up to <c>milliseconds</c>, releases the references of collected
    /// objects, runs posted work and due timers, each followed by the microtasks it queued.
    /// </summary>
    member _.Cycle(?milliseconds: int) =
        Native.rv_run_loop_cycle (defaultArg milliseconds 0)
        |> ignore

        session.Drain ()
        let mutable item = Unchecked.defaultof<_>

        while posted.TryDequeue (&item) do
            let struct (callback, state) = item

            try
                callback.Invoke state
            with error ->
                record error

            runMicrotasks ()

        runDueTimers ()
        runMicrotasks ()

    /// <summary>Moves the timer clock forward by <c>milliseconds</c> and runs the timers then due, as a test clock does.</summary>
    member internal this.AdvanceBy(milliseconds: float) =
        let target = now () + milliseconds
        let mutable going = true

        while going do
            let timer =
                if timers.Count = 0 then
                    ValueNone
                else
                    let struct (due, _) = (Seq.head timers).Key
                    if due <= target then ValueSome due else ValueNone

            // Animation frames fall on multiples of 16 ms, as the twin's clock runs them.
            let frame =
                if frames.Count = 0 then
                    ValueNone
                else
                    let boundary = (floor (now () / 16.0) + 1.0) * 16.0
                    if boundary <= target then ValueSome boundary else ValueNone

            match timer, frame with
            | ValueSome due, ValueSome boundary when boundary < due ->
                setNow boundary
                this.Frame ()
            | ValueSome due, _ ->
                setNow (max due (now ()))
                let first = Seq.head timers
                let struct (_, id) = first.Key
                timers.Remove first.Key |> ignore
                timerKeys.Remove id |> ignore

                try
                    first.Value ()
                with error ->
                    record error

                runMicrotasks ()
            | ValueNone, ValueSome boundary ->
                setNow boundary
                this.Frame ()
            | ValueNone, ValueNone -> going <- false

        setNow target

    /// <summary>Runs timers and animation frames only on <c>AdvanceBy</c>, from time zero, as a test clock does.</summary>
    member internal _.UseManualClock() =
        manual <- true
        advanced <- 0.0

    /// <summary>
    /// Makes calls of the DOM member <c>name</c> (a method name, or <c>get_</c>/<c>set_</c> and a property name) on the
    /// node <c>target</c>, or on any node for null, run through <c>wrap</c> until the returned function is called.
    /// <c>wrap</c> receives the inner call, the node and the arguments.
    /// </summary>
    /// <summary>Called with the parent of each node <c>DeleteBetween</c> removes, for a test's mutation records.</summary>
    member val internal DeletedFrom: Action<obj> = null with get, set

    member internal _.Intercept(target: obj, name: string, wrap: Func<Func<obj array, obj>, obj, obj array, obj>) : unit -> unit =
        let entry = struct (target, name, wrap)
        session.Interceptions.Add entry
        fun () -> session.Interceptions.Remove entry |> ignore

    /// <summary>
    /// Runs a frame: the animation frame callbacks queued before it, then WebCore's rendering update. The app paints
    /// afterwards.
    /// </summary>
    member _.Frame() =
        Volatile.Write (&frameRequested, 0)
        let now = now ()
        let callbacks = Seq.toArray frames.Values
        frames.Clear ()

        for callback in callbacks do
            try
                callback now
            with error ->
                record error

            runMicrotasks ()

        Native.rv_host_update handle
        |> Interop.check "rv_host_update"

    // Window

    /// <summary>Sets the viewport to <c>width</c> by <c>height</c> CSS pixels.</summary>
    member _.Resize(width: int, height: int) =
        Native.rv_host_resize (handle, width, height)
        |> Interop.check "rv_host_resize"

    /// <summary>Sets the device pixels per CSS pixel, from 0.25 to 8; <c>PaintSize</c> scales with it.</summary>
    member _.SetDeviceScale(scale: float32) =
        Native.rv_host_set_device_scale (handle, scale)
        |> Interop.check "rv_host_set_device_scale"

    /// <summary>The size in device pixels of the image <c>Paint</c> writes.</summary>
    member _.PaintSize =
        let mutable width = 0
        let mutable height = 0

        Native.rv_host_paint_size (handle, &width, &height)
        |> Interop.check "rv_host_paint_size"

        struct (width, height)

    /// <summary>Lays out and paints the page into <c>rgba</c>, unpremultiplied RGBA8 of <c>PaintSize</c>.</summary>
    member _.Paint(rgba: byte array) =
        Native.rv_host_paint_rgba (handle, rgba, rgba.Length)
        |> Interop.check "rv_host_paint_rgba"

    /// <summary>The viewport area invalidated since the last paint, in CSS pixels, or None.</summary>
    member _.DirtyRect: struct (int * int * int * int) voption =
        let mutable x = 0
        let mutable y = 0
        let mutable width = 0
        let mutable height = 0

        match Native.rv_host_dirty_rect (handle, &x, &y, &width, &height) with
        | 1 -> ValueSome (struct (x, y, width, height))
        | 0 -> ValueNone
        | status -> raise (Interop.failure "rv_host_dirty_rect" status)

    /// <summary>
    /// Paints the dirty rectangle into its place in <c>rgba</c>, an image a full <c>Paint</c> wrote, and returns it, or
    /// None when nothing was invalidated.
    /// </summary>
    member this.PaintDirty(rgba: byte array) =
        match this.DirtyRect with
        | ValueSome (struct (x, y, width, height)) as dirty ->
            Native.rv_host_paint_rgba_rect (handle, rgba, rgba.Length, x, y, width, height)
            |> Interop.check "rv_host_paint_rgba_rect"

            dirty
        | ValueNone -> ValueNone

    member private _.Input(name: string, call: unit -> int) : bool =
        inInput <- inInput + 1

        let status =
            try
                call ()
            finally
                inInput <- inInput - 1

        runMicrotasks ()

        if status < 0 then
            raise (Interop.failure name status)

        status = 1

    /// <summary>
    /// Native mouse input at <c>x</c>, <c>y</c> CSS pixels: an <c>RV_MOUSE_*</c> action, an <c>RV_BUTTON_*</c> button, the
    /// click count and <c>RV_MODIFIER_*</c> bits. Returns whether the page or WebCore handled it.
    /// </summary>
    member this.Mouse(action: int, x: int, y: int, button: int, clickCount: int, modifiers: int) =
        this.Input ("rv_host_mouse", fun () -> Native.rv_host_mouse (handle, action, x, y, button, clickCount, modifiers))

    /// <summary>A left-button press and release at <c>x</c>, <c>y</c>, as a user's click.</summary>
    member this.Click(x: int, y: int) =
        this.Mouse (0, x, y, -1, 0, 0) |> ignore
        this.Mouse (1, x, y, 0, 1, 0) |> ignore
        this.Mouse (2, x, y, 0, 1, 0)

    /// <summary>A wheel event at <c>x</c>, <c>y</c> that scrolls by the deltas in CSS pixels, positive right and down.</summary>
    member this.Wheel(x: int, y: int, deltaX: float32, deltaY: float32, modifiers: int) =
        this.Input ("rv_host_wheel", fun () -> Native.rv_host_wheel (handle, x, y, deltaX, deltaY, modifiers))

    /// <summary>Native key input: an <c>RV_KEY_*</c> action with <c>KeyboardEvent.key</c> and <c>code</c> values.</summary>
    member this.Key(action: int, key: string, code: string, text: string, modifiers: int, repeat: bool) =
        this.Input ("rv_host_key", fun () -> Native.rv_host_key (handle, action, key, code, text, modifiers, (if repeat then 1 else 0)))

    /// <summary>Presses and releases a key that types <c>text</c>.</summary>
    member this.Type(key: string, code: string, text: string) =
        this.Key (0, key, code, null, 0, false) |> ignore
        this.Key (2, key, code, text, 0, false) |> ignore
        this.Key (1, key, code, null, 0, false) |> ignore

    /// <summary>Sets whether the app's window has keyboard focus; a page starts unfocused.</summary>
    member _.SetFocused(focused: bool) =
        Native.rv_host_set_focused (handle, (if focused then 1 else 0))
        |> Interop.check "rv_host_set_focused"

    /// <summary>The <c>RV_CURSOR_*</c> value for the mouse's position.</summary>
    member _.Cursor = Native.rv_host_cursor handle

    interface Native.IHost with
        member _.Document = document

        member this.QueueMicrotask callback =
            this.QueueMicrotask callback

        member this.SetTimeout(action, milliseconds) =
            box (this.SetTimeout (action, float milliseconds))

        member this.ClearTimeout timer =
            match timer with
            | :? int as id -> this.ClearTimeout id
            | _ -> ()

        member this.RequestAnimationFrame callback =
            box (this.RequestAnimationFrame callback)

        member this.CancelAnimationFrame frame =
            match frame with
            | :? int as id -> this.CancelAnimationFrame id
            | _ -> ()

        member this.DeleteBetween(first, last) =
            let parent =
                if isNull this.DeletedFrom then
                    null
                else
                    first.parentNode |> Option.map box |> Option.toObj

            let last = Interop.handle last
            let mutable cursor = Native.rv_node_relative (Interop.handle first, 3)

            while cursor <> 0n && cursor <> last do
                let next = Native.rv_node_relative (cursor, 3)

                Native.rv_node_remove cursor
                |> Interop.check "Range.deleteContents"

                if not (isNull parent) then
                    this.DeletedFrom.Invoke parent

                Native.rv_node_release cursor
                cursor <- next

            if cursor <> 0n then
                Native.rv_node_release cursor

        member _.Listen(target, eventName, listener) =
            let registration =
                through target "addEventListener" [| eventName; box listener |] (fun args ->
                    box (Events.Listen (target :?> NativeObject, unbox<string> args[0], false, null, listener)))
                |> unbox<Registration>

            fun () ->
                through target "removeEventListener" [| eventName; box listener |] (fun _ ->
                    Events.Unlisten (session, registration)
                    null)
                |> ignore

        member _.OverrideCurrentTarget(event, read) =
            (event :?> NativeObject).CurrentTargetOverride <- read

    interface Native.IExpandoHost with
        member _.ExpandoSlots(target, create) =
            match target with
            | :? NativeObject as native when obj.ReferenceEquals (native.Session, session) ->
                if create && isNull native.Expandos then
                    native.Expandos <- Native.ExpandoSlots ()
                    session.Pin native

                ValueSome native.Expandos
            | _ -> ValueNone

    interface Fable.Ranvier.IGlobals with
        member _.Document = document

        member this.QueueMicrotask callback =
            this.QueueMicrotask callback

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                if Environment.CurrentManagedThreadId <> thread then
                    raise (InvalidOperationException "A WebCore host is disposed on the thread that created it")

                disposed <- true

                for item in installed do
                    item.Dispose ()

                installed <- []

                if obj.ReferenceEquals (SynchronizationContext.Current, context) then
                    SynchronizationContext.SetSynchronizationContext previousContext

                Native.rv_host_set_frame_callback (handle, 0n, 0n)
                |> ignore

                Events.RemoveAll session
                session.Dispose ()
                Native.rv_host_destroy handle
                GC.KeepAlive frameCallback
