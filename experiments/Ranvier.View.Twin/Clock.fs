namespace Ranvier.View.Twin

open System
open System.Collections.Generic

type ClockPending =
    {
        Microtasks: int
        Timers: int
        Frames: int
        NextTimerAt: float option
    }

/// <summary>A queue that kept refilling past its limit; <c>origin</c> names its last callback.</summary>
exception TwinRunawayException of queue: string * origin: string with
    override this.Message =
        $"The twin's {this.queue} kept refilling; the last callback was {this.origin}"

type private Timer =
    {
        Id: int
        Due: float
        Sequence: int
        Callback: unit -> unit
    }

/// <summary>
/// The twin's event loop: microtasks, timers and animation frames, run only when the test asks. It belongs to the
/// thread that creates it; a call from another thread raises <c>InvalidOperationException</c>.
/// </summary>
[<Sealed>]
type TwinClock() as this =
    let microtasks = Queue<unit -> unit>()
    let timers = ResizeArray<Timer>()
    let frames = ResizeArray<int * (float -> unit)>()
    let ownerThread = Environment.CurrentManagedThreadId
    let mutable now = 0.0
    let mutable sequence = 0
    let mutable lastOrigin = ""

    let origin (callback: obj) =
        callback.GetType().FullName

    let check (operation: string) =
        let current = Environment.CurrentManagedThreadId

        if current <> ownerThread then
            raise (InvalidOperationException $"The twin clock of thread {ownerThread} was used on thread {current} ({operation})")

    let provider =
        { new TimeProvider() with
            override _.GetUtcNow() =
                DateTimeOffset.UnixEpoch.AddMilliseconds now

            override _.TimestampFrequency = 10_000_000L

            override _.GetTimestamp() =
                int64 (now * 10_000.0)

            override _.CreateTimer(callback, state, dueTime, period) =
                // The id of the scheduled run; a run reschedules only while it is still the scheduled one.
                let id = ref -1
                let periodMs = ref -1.0
                let disposed = ref false

                let rec schedule (delay: float) =
                    let mine = ref -1

                    mine.Value <-
                        this.SetTimeout (
                            (fun () ->
                                callback.Invoke state

                                if
                                    not disposed.Value
                                    && id.Value = mine.Value
                                    && periodMs.Value > 0.0
                                then
                                    schedule periodMs.Value),
                            delay
                        )

                    id.Value <- mine.Value

                let change (due: TimeSpan) (every: TimeSpan) =
                    if disposed.Value then
                        false
                    else
                        if id.Value >= 0 then
                            this.ClearTimeout id.Value

                        id.Value <- -1

                        periodMs.Value <-
                            (if every = Threading.Timeout.InfiniteTimeSpan then
                                 -1.0
                             else
                                 every.TotalMilliseconds)

                        if due <> Threading.Timeout.InfiniteTimeSpan then
                            schedule due.TotalMilliseconds

                        true

                let dispose () =
                    disposed.Value <- true

                    if id.Value >= 0 then
                        this.ClearTimeout id.Value

                    id.Value <- -1

                change dueTime period |> ignore

                { new Threading.ITimer with
                    member _.Change(due, every) =
                        change due every

                    member _.Dispose() =
                        dispose ()

                    member _.DisposeAsync() =
                        dispose ()
                        System.Threading.Tasks.ValueTask ()
                }
        }

    member _.Now = now
    member _.TimeProvider: TimeProvider = provider
    member val FrameMs = 16.0 with get, set

    /// <summary>
    /// The most microtasks one drain runs; a drain that reaches it with work left raises <c>TwinRunawayException</c>.
    /// </summary>
    member val MicrotaskLimit = 100_000 with get, set

    member _.Post(callback: unit -> unit) =
        check "Post"
        microtasks.Enqueue callback

    /// <summary>Runs queued microtasks, including those they queue, until none remain.</summary>
    member this.RunMicrotasks() : int =
        check "RunMicrotasks"
        let ran = this.Drain this.MicrotaskLimit

        if microtasks.Count > 0 then
            raise (TwinRunawayException ("microtasks", lastOrigin))

        ran

    /// <summary>Runs queued microtasks, including those they queue, until none remain or <c>limit</c> have run.</summary>
    member internal _.Drain(limit: int) : int =
        let mutable count = 0

        while microtasks.Count > 0 && count < limit do
            let callback = microtasks.Dequeue ()
            lastOrigin <- origin callback
            callback ()
            count <- count + 1

        count

    member _.SetTimeout(callback: unit -> unit, milliseconds: float) : int =
        check "SetTimeout"
        sequence <- sequence + 1

        timers.Add
            {
                Id = sequence
                Due = now + max 0.0 milliseconds
                Sequence = sequence
                Callback = callback
            }

        sequence

    member _.ClearTimeout(id: int) =
        check "ClearTimeout"
        timers.RemoveAll (fun t -> t.Id = id) |> ignore

    member _.RequestAnimationFrame(callback: float -> unit) : int =
        check "RequestAnimationFrame"
        sequence <- sequence + 1
        frames.Add ((sequence, callback))
        sequence

    member _.CancelAnimationFrame(id: int) =
        check "CancelAnimationFrame"
        frames.RemoveAll (fun (i, _) -> i = id) |> ignore

    member private this.RunTimer(timer: Timer) =
        timers.Remove timer |> ignore
        now <- max now timer.Due
        lastOrigin <- origin timer.Callback
        timer.Callback ()
        this.RunMicrotasks () |> ignore

    member this.Frame() =
        check "Frame"
        let due = List.ofSeq frames
        frames.Clear ()

        for _, callback in due do
            lastOrigin <- origin callback
            callback now
            this.RunMicrotasks () |> ignore

    member this.AdvanceBy(milliseconds: float) =
        check "AdvanceBy"

        if milliseconds < 0.0 then
            raise (ArgumentOutOfRangeException ("milliseconds", milliseconds, "The twin clock does not move backwards"))

        let target = now + milliseconds
        let mutable going = true

        while going do
            let timer =
                timers
                |> Seq.filter (fun t -> t.Due <= target)
                |> Seq.sortBy (fun t -> t.Due, t.Sequence)
                |> Seq.tryHead

            let frame =
                if frames.Count = 0 then
                    None
                else
                    let boundary = (floor (now / this.FrameMs) + 1.0) * this.FrameMs
                    if boundary <= target then Some boundary else None

            match timer, frame with
            | Some t, Some f when f < t.Due ->
                now <- f
                this.Frame ()
            | Some t, _ -> this.RunTimer t
            | None, Some f ->
                now <- f
                this.Frame ()
            | None, None -> going <- false

        now <- target

    member _.Pending: ClockPending =
        {
            Microtasks = microtasks.Count
            Timers = timers.Count
            Frames = frames.Count
            NextTimerAt =
                if timers.Count = 0 then
                    None
                else
                    Some (timers |> Seq.map (fun t -> t.Due) |> Seq.min)
        }

    /// <summary>
    /// Pumps the graph's inbox, drains microtasks and runs due timers until all three are empty. A round drains at most
    /// 1,000 microtasks and runs the due timers only when no microtask remains. After <c>maxRounds</c> busy rounds (default 1,000), or when a drain after a timer reaches
    /// <c>MicrotaskLimit</c>, it raises <c>TwinRunawayException</c> naming the busiest queue and its last callback.
    /// </summary>
    member this.Settle(graph: Ranvier.Graph, ?maxRounds: int) =
        check "Settle"
        let limit = defaultArg maxRounds 1_000
        let counts = Dictionary<string, int>()

        let bump name n =
            counts[name] <-
                (match counts.TryGetValue name with
                 | true, c -> c
                 | _ -> 0)
                + n

        let mutable rounds = 0
        let mutable busy = true

        while busy do
            let pumped = graph.Pump ()
            let ran = this.Drain 1_000

            // A due timer runs only once the microtask queue is empty, as a browser task does.
            let due =
                if microtasks.Count > 0 then
                    []
                else
                    timers
                    |> Seq.filter (fun t -> t.Due <= now)
                    |> Seq.sortBy (fun t -> t.Due, t.Sequence)
                    |> List.ofSeq

            for timer in due do
                if timers.Contains timer then
                    this.RunTimer timer

            bump "inbox" pumped
            bump "microtasks" ran
            bump "timers" due.Length
            busy <- pumped + ran + due.Length > 0
            rounds <- rounds + 1

            if busy && rounds >= limit then
                let busiest = counts |> Seq.maxBy (fun pair -> pair.Value)
                raise (TwinRunawayException (busiest.Key, lastOrigin))
