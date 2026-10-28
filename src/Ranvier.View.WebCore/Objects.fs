namespace Ranvier.View.WebCore

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Runtime.CompilerServices
open Ranvier
open RanvierWebCoreAbi

/// <summary>The release function of a handle, by the root of its interface.</summary>
module internal Kind =
    [<Literal>]
    let Node = 0

    [<Literal>]
    let Event = 1

    [<Literal>]
    let Style = 2

    [<Literal>]
    let TokenList = 3

    [<Literal>]
    let StringMap = 4

    [<Literal>]
    let Range = 5

    [<Literal>]
    let Selection = 6

    [<Literal>]
    let Rect = 7

    [<Literal>]
    let NodeList = 8

    [<Literal>]
    let Collection = 9

    [<Literal>]
    let Validity = 10

/// <summary>A DOM exception WebCore raised; <c>name</c> is the DOM exception name, such as <c>NotFoundError</c>.</summary>
exception WebCoreDomException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>A native event listener: its target, type, phase, the callback it was added with, and its registration.</summary>
[<Sealed; AllowNullLiteral>]
type internal Registration(target: nativeint, eventType: string, capture: bool, callback: obj, invoke: obj -> unit) =
    member _.Target = target
    member _.Type = eventType
    member _.Capture = capture
    member _.Callback = callback
    member _.Invoke = invoke
    member val Listener = 0n with get, set
    member val Context = Runtime.InteropServices.GCHandle () with get, set

/// <summary>
/// A managed object over a native WebCore handle. The object holds one reference to the handle; a node or an event has
/// one object per handle.
/// </summary>
/// <remarks>
/// Collection of the object queues its reference's release, which the host thread drains on its next loop cycle. Host
/// disposal releases every reference and leaves <c>Handle</c> zero.
/// </remarks>
[<AbstractClass; AllowNullLiteral>]
type NativeObject(session: Session, handle: nativeint, kind: int) =
    let mutable handle = handle

    /// <summary>The session of the host the object belongs to.</summary>
    member _.Session = session

    /// <summary>The native handle, or zero after host disposal.</summary>
    member _.Handle = handle

    member internal _.Kind = kind

    member val internal Expandos: Ranvier.View.Native.ExpandoSlots = null with get, set

    /// <summary>The owner whose claim keeps the object alive, or null.</summary>
    member val internal ClaimedBy: Owner = Unchecked.defaultof<_> with get, set

    member val internal CurrentTargetOverride: unit -> obj = Unchecked.defaultof<_> with get, set

    /// <summary>The init fields of an event created by the host, which take precedence over the native values.</summary>
    member val internal Init: Dictionary<string, obj> = null with get, set

    /// <summary>Releases the reference now, on the host thread.</summary>
    member internal this.Detach() =
        if handle <> 0n then
            Session.Release (kind, handle)
            handle <- 0n
            GC.SuppressFinalize this

    override _.Finalize() =
        if handle <> 0n then
            session.QueueRelease (kind, handle)

    override _.ToString() =
        $"{session.KindName kind} 0x{handle:x}"

/// <summary>
/// The managed objects of one WebCore host: the node and event table, the owner claims that keep objects alive, and the
/// queue of releases from collected objects.
/// </summary>
and [<Sealed; AllowNullLiteral>] Session internal (host: nativeint) =
    let table = Dictionary<nativeint, WeakReference<NativeObject>>()
    let others = List<WeakReference<NativeObject>>()
    let releases = ConcurrentQueue<struct (int * nativeint)>()
    let held = HashSet<NativeObject>(HashIdentity.Reference)
    let claims = ConditionalWeakTable<Owner, HashSet<NativeObject>>()
    let errors = List<exn>()
    let mutable disposed = false
    let mutable released = 0

    static member internal Release(kind: int, handle: nativeint) =
        match kind with
        | Kind.Node -> Native.rv_node_release handle
        | Kind.Event -> Native.rv_event_release handle
        | Kind.Style -> NativeDom.rv_dom_css_style_declaration_release handle
        | Kind.TokenList -> NativeDom.rv_dom_dom_token_list_release handle
        | Kind.StringMap -> NativeDom.rv_dom_dom_string_map_release handle
        | Kind.Range -> NativeDom.rv_dom_abstract_range_release handle
        | Kind.Selection -> NativeDom.rv_dom_dom_selection_release handle
        | Kind.Rect -> NativeDom.rv_dom_dom_rect_read_only_release handle
        | Kind.NodeList -> NativeDom.rv_dom_node_list_release handle
        | Kind.Collection -> NativeDom.rv_dom_html_collection_release handle
        | Kind.Validity -> NativeDom.rv_dom_validity_state_release handle
        | _ -> ()

    member internal _.KindName(kind: int) =
        match kind with
        | Kind.Node -> "node"
        | Kind.Event -> "event"
        | _ -> "object"

    /// <summary>The <c>RvHost</c> handle.</summary>
    member _.Host = host

    member val internal Registrations = HashSet<Registration>(HashIdentity.Reference)

    /// <summary>The interceptions of members by name, in the order they were added; a null target is every node.</summary>
    member val internal Interceptions = List<struct (obj * string * Func<Func<obj array, obj>, obj, obj array, obj>)>()

    member internal this.Intercepting = this.Interceptions.Count > 0

    /// <summary>The listener calls and managed dispatches in progress.</summary>
    member val internal ScriptDepth = 0 with get, set

    /// <summary>Runs after a listener call that leaves no script on the stack.</summary>
    member val internal AfterListener: Action = null with get, set

    member val internal NodeFactory: Func<Session, nativeint, NativeObject> = null with get, set
    member val internal EventFactory: Func<Session, nativeint, NativeObject> = null with get, set

    /// <summary>The exceptions raised by event listeners, in raise order; each dispatch continues past them.</summary>
    member _.ListenerErrors = errors

    /// <summary>The live node and event objects.</summary>
    member _.LiveObjects =
        let mutable count = 0

        for weak in table.Values do
            match weak.TryGetTarget () with
            | true, _ -> count <- count + 1
            | _ -> ()

        count

    /// <summary>The references released for collected objects so far.</summary>
    member _.Released = released

    member internal _.Claim(target: NativeObject) =
        match Graph.TryCurrent with
        | ValueSome graph ->
            let owner =
                try
                    getOwner ()
                with :? InvalidOperationException ->
                    Unchecked.defaultof<_>

            if
                not (isNull (box owner))
                && not (obj.ReferenceEquals (owner, graph.Root))
                && not owner.IsDisposed
                && not (obj.ReferenceEquals (target.ClaimedBy, owner))
            then
                target.ClaimedBy <- owner

                let set =
                    match claims.TryGetValue owner with
                    | true, set -> set
                    | _ ->
                        let set = HashSet<NativeObject>(HashIdentity.Reference)
                        claims.Add (owner, set)

                        owner.OnCleanup (fun () ->
                            claims.Remove owner |> ignore

                            for o in set do
                                if obj.ReferenceEquals (o.ClaimedBy, owner) then
                                    o.ClaimedBy <- Unchecked.defaultof<_>

                            set.Clear ())

                        set

                set.Add target |> ignore

                true
            else
                not (isNull (box owner))
                && obj.ReferenceEquals (target.ClaimedBy, owner)
        | ValueNone -> false

    /// <summary>Keeps an object with expandos alive: under the current owner, else until host disposal.</summary>
    member internal this.Pin(target: NativeObject) =
        if target.Kind = Kind.Node && not (this.Claim target) then
            held.Add target |> ignore

    member private this.Wrap(handle: nativeint, factory: Func<Session, nativeint, NativeObject>, kind: int) : NativeObject =
        if handle = 0n then
            null
        else
            let mutable existing = null

            match table.TryGetValue handle with
            | true, weak when
                weak.TryGetTarget (&existing)
                && existing.Handle = handle
                ->
                Session.Release (kind, handle)

                if kind = Kind.Node then
                    this.Claim existing |> ignore

                existing
            | _ ->
                let created = factory.Invoke (this, handle)
                table[handle] <- WeakReference<NativeObject>(created)

                if kind = Kind.Node then
                    this.Claim created |> ignore

                created

    /// <summary>The object of a node handle the caller owns a reference to, or null for zero.</summary>
    member this.Node(handle: nativeint) : NativeObject =
        this.Wrap (handle, this.NodeFactory, Kind.Node)

    /// <summary>The object of an event handle the caller owns a reference to, or null for zero.</summary>
    member this.Event(handle: nativeint) : NativeObject =
        this.Wrap (handle, this.EventFactory, Kind.Event)

    /// <summary>The object of an event handle valid for the current listener call.</summary>
    /// <summary>Enters an object created for an owned node or event handle in the table.</summary>
    member internal _.Register(created: NativeObject) =
        table[created.Handle] <- WeakReference<NativeObject>(created)

    member internal this.BorrowedEvent(handle: nativeint) : NativeObject =
        let mutable existing = null

        match table.TryGetValue handle with
        | true, weak when
            weak.TryGetTarget (&existing)
            && existing.Handle = handle
            ->
            existing
        | _ -> this.Event (Native.rv_event_retain handle)

    /// <summary>Records an object outside the node and event table for release at host disposal.</summary>
    member internal _.Track<'T when 'T :> NativeObject>(target: 'T) : 'T =
        others.Add (WeakReference<NativeObject>(target))
        target

    member internal _.QueueRelease(kind: int, handle: nativeint) =
        if not disposed then
            releases.Enqueue (struct (kind, handle))

    /// <summary>Releases the references of collected objects; runs on the host thread.</summary>
    member _.Drain() =
        let mutable item = Unchecked.defaultof<_>

        while releases.TryDequeue (&item) do
            let struct (kind, handle) = item

            if kind = Kind.Node || kind = Kind.Event then
                match table.TryGetValue handle with
                | true, weak ->
                    match weak.TryGetTarget () with
                    | true, _ -> ()
                    | _ -> table.Remove handle |> ignore
                | _ -> ()

            Session.Release (kind, handle)
            released <- released + 1

        if others.Count > 64 then
            others.RemoveAll (fun weak -> not (fst (weak.TryGetTarget ())))
            |> ignore

    /// <summary>Releases every reference, live objects included; their handles read zero afterwards.</summary>
    member this.Dispose() =
        if not disposed then
            GC.Collect ()
            GC.WaitForPendingFinalizers ()
            this.Drain ()
            disposed <- true

            for weak in Seq.append table.Values others do
                match weak.TryGetTarget () with
                | true, target -> target.Detach ()
                | _ -> ()

            table.Clear ()
            others.Clear ()
            held.Clear ()

    interface IDisposable with
        member this.Dispose() =
            this.Dispose ()

/// <summary>Status handling, strings and handles of the generated members.</summary>
module internal Interop =
    [<Sealed; AbstractClass>]
    type private Buffers =
        [<ThreadStatic; DefaultValue>]
        static val mutable private text: byte array

        [<ThreadStatic; DefaultValue>]
        static val mutable private length: int array

        static member Text
            with get () =
                if isNull Buffers.text then
                    Buffers.text <- Array.zeroCreate 512

                Buffers.text
            and set (value: byte array) = Buffers.text <- value

        static member Length =
            if isNull Buffers.length then
                Buffers.length <- Array.zeroCreate 1

            Buffers.length

    [<Literal>]
    let private BufferTooSmall = -6

    let private lastException (part: int) =
        let buffer = Array.zeroCreate<byte> 256
        let length = [| 0 |]
        let mutable status = Native.rv_last_exception (part, buffer, buffer.Length, length)

        if status = BufferTooSmall then
            let larger = Array.zeroCreate<byte> length[0]
            status <- Native.rv_last_exception (part, larger, larger.Length, length)

            if status = 0 && length[0] >= 0 then
                Text.Encoding.UTF8.GetString (larger, 0, length[0])
            else
                ""
        elif status = 0 && length[0] >= 0 then
            Text.Encoding.UTF8.GetString (buffer, 0, length[0])
        else
            ""

    /// <summary>The exception for a failed status of the operation <c>name</c>.</summary>
    let failure (name: string) (status: int) : exn =
        match status with
        | -3 -> WebCoreDomException (lastException 0, lastException 1)
        | -11 -> InvalidCastException $"{name}: the object does not implement the member's interface"
        | -9 -> NotSupportedException $"{name} is not supported by this WebCore build"
        | -2 -> InvalidOperationException $"{name} was called off the host thread"
        | -1 -> ArgumentNullException (name, $"{name} received a null argument")
        | other -> InvalidOperationException $"{name} returned status {other}"

    /// <summary>Raises the status's exception unless it is <c>RV_OK</c>.</summary>
    let check (name: string) (status: int) =
        if status <> 0 then
            raise (failure name status)

    /// <summary>The string a getter copies out, or null for a null string.</summary>
    let text (name: string) (call: byte array -> int -> int array -> int) : string =
        let length = Buffers.Length
        let mutable buffer = Buffers.Text
        let mutable status = call buffer buffer.Length length

        if status = BufferTooSmall then
            buffer <- Array.zeroCreate (max length[0] (buffer.Length * 2))
            Buffers.Text <- buffer
            status <- call buffer buffer.Length length

        check name status

        if length[0] < 0 then
            null
        else
            Text.Encoding.UTF8.GetString (buffer, 0, length[0])

    /// <summary>Raises the <c>NotSupportedException</c> of a member outside the native backend.</summary>
    let unsupported<'T> (name: string) : 'T =
        raise (NotSupportedException $"{name} is not available in the native backend")

    /// <summary>The handle of a backend object, or zero for null.</summary>
    let handle (value: obj) : nativeint =
        match value with
        | null -> 0n
        | :? NativeObject as target ->
            if target.Handle = 0n then
                raise (ObjectDisposedException (target.ToString (), "The object's host was disposed"))

            target.Handle
        | other -> raise (ArgumentException $"A {other.GetType().Name} is not an object of the native backend")

    /// <summary>
    /// Runs <c>original</c> through the interceptions of <c>name</c> on <c>target</c>, the latest outermost, each given
    /// the inner call, the target and the arguments.
    /// </summary>
    let intercept (target: NativeObject) (name: string) (args: obj array) (original: obj array -> obj) : obj =
        let mutable call = original

        for struct (on, memberName, wrap) in target.Session.Interceptions do
            if
                memberName = name
                && (isNull on || obj.ReferenceEquals (on, target))
            then
                let inner = call
                call <- fun values -> wrap.Invoke (Func<_, _> inner, box target, values)

        call args

    /// <summary>The string, or empty for null: a DOMString result that WebCore reports as null.</summary>
    let nonNull (value: string) =
        if isNull value then "" else value

    let handleOr (value: 'T option) : nativeint =
        match value with
        | Some target -> handle (box target)
        | None -> 0n

    let optionOf<'T> (value: NativeObject) : 'T option =
        if isNull value then None else Some (unbox<'T> value)
