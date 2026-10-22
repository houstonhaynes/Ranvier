/// <summary>The .NET lowering of Arms.Match: each rejection with its text and location, accepted forms, and the lowering cost.</summary>
module ArmsLoweringChecks

#nowarn "25"

open System
open System.IO
open global.Ranvier
open Ranvier.View
open Ranvier.View.Representation
open Ranvier.View.Template
open Ranvier.View.Sugar
open Ranvier.View.Arms

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of text: string * count: int

[<Struct>]
type Point = { X: int; Y: int }

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private here = Path.Combine (__SOURCE_DIRECTORY__, __SOURCE_FILE__)

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private read (state: Signal<Load>) =
    state.Value

let private sourceLines = File.ReadAllLines here

/// <summary>
/// What building a site raised: the kind and variable, and the message after the location prefix, which must name this
/// file and a line holding an <c>Arms.Match</c> call.
/// </summary>
let private raised (build: unit -> obj) =
    let unprefixed (message: string) =
        let close = message.IndexOf "): "

        let line =
            if message.StartsWith (here + "(") && close > 0 then
                match Int32.TryParse (message.Substring (here.Length + 1, close - here.Length - 1)) with
                | true, line -> line
                | _ -> 0
            else
                0

        if
            line > 0
            && sourceLines[line - 1].Contains "Arms.Match"
        then
            message.Substring (close + 3)
        else
            "unprefixed: " + message

    try
        build () |> ignore
        "none", ""
    with
    | :? BuildTimeReadException as error -> "BuildTimeRead " + error.Variable, unprefixed error.Message
    | :? ArmsMatchException as error -> "ArmsMatch", unprefixed error.Message

let private notObject (name: string) =
    "ArmsMatch",
    "Arms.Match: the instance type '"
    + name
    + "' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance."

let private am001 () =
    equal
        ("BuildTimeRead text",
         "Arms.Match: 'text' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> text).")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (state: Signal<Load>) -> state)
                    (div () {
                        Arms.Match (
                            read,
                            function
                            | Loaded (text, _) -> span () { text }
                            | _ -> span () { "Other" }
                        )
                    })
            )))

let private am002 () =
    equal
        ("BuildTimeRead text",
         "Arms.Match: 'text' is read inside a lambda over 'unit -> string', which is not the instance type 'Signal<Load>'. Pass the value to the nested view through its items or props.")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (state: Signal<Load>) -> state)
                    (div () {
                        Arms.Match (
                            read,
                            function
                            | Loaded (text, _) -> ul () { forEach id (fun _ -> [| "a" |]) (li () { fun (item: unit -> string) -> item () + text }) }
                            | _ -> span () { "Other" }
                        )
                    })
            )))

/// <summary>A wildcard row reader is still a lambda over another type (AM002).</summary>
let private am002Wildcard () =
    equal
        ("BuildTimeRead text",
         "Arms.Match: 'text' is read inside a lambda over 'unit -> string', which is not the instance type 'Signal<Load>'. Pass the value to the nested view through its items or props.")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (state: Signal<Load>) -> state)
                    (div () {
                        Arms.Match (
                            read,
                            function
                            | Loaded (text, _) -> ul () { forEach id (fun _ -> [| "a" |]) (li () { fun _ -> text }) }
                            | _ -> span () { "Other" }
                        )
                    })
            )))

let private namedArms: Load -> TemplateView<Signal<Load>> =
    function
    | Idle -> span () { "Idle" }
    | _ -> span () { "Other" }

/// <summary>A module-level arms function, quoted as an eta-expansion, raises AM003.</summary>
let private am003Named () =
    equal
        ("ArmsMatch", "Arms.Match takes a function literal as its second argument, such as (function | Case x -> ...).")
        (raised (fun () -> box (Component.template (fun _ (state: Signal<Load>) -> state) (div () { Arms.Match (read, namedArms) }))))

/// <summary>A failwithf leaf builds no arm; the selector raises it at the region read.</summary>
let private failwithfLeaf () =
    let built =
        try
            box (
                Component.template
                    (fun _ (state: Signal<Load>) -> state)
                    (div () {
                        Arms.Match (
                            read,
                            function
                            | Loaded _ -> failwithf "bad %s" "state"
                            | _ -> span () { "Other" }
                        )
                    })
            )
            |> Some
        with _ ->
            None

    equal true built.IsSome

let private am003 () =
    let arms: Load -> TemplateView<Signal<Load>> =
        function
        | Idle -> span () { "Idle" }
        | _ -> span () { "Other" }

    equal
        ("ArmsMatch", "Arms.Match takes a function literal as its second argument, such as (function | Case x -> ...).")
        (raised (fun () -> box (Component.template (fun _ (state: Signal<Load>) -> state) (div () { Arms.Match (read, arms) }))))

let private am004 () =
    equal
        (notObject "int")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (n: int) -> n)
                    (div () {
                        Arms.Match (
                            (fun (n: int) -> n),
                            function
                            | 0 -> span () { "zero" }
                            | _ -> span () { "other" }
                        )
                    })
            )))

    equal
        (notObject "string")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (s: string) -> s)
                    (div () {
                        Arms.Match (
                            (fun (s: string) -> s.Length),
                            function
                            | 0 -> span () { "zero" }
                            | _ -> span () { "other" }
                        )
                    })
            )))

    equal
        (notObject "unit")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (u: unit) -> u)
                    (div () {
                        Arms.Match (
                            (fun (_: unit) -> 0),
                            function
                            | 0 -> span () { "zero" }
                            | _ -> span () { "other" }
                        )
                    })
            )))

    equal
        (notObject "int option")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (o: int option) -> o)
                    (div () {
                        Arms.Match (
                            (fun (o: int option) -> defaultArg o 0),
                            function
                            | 0 -> span () { "zero" }
                            | _ -> span () { "other" }
                        )
                    })
            )))

    equal
        (notObject "Point")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (p: Point) -> p)
                    (div () {
                        Arms.Match (
                            (fun (p: Point) -> p.X),
                            function
                            | 0 -> span () { "zero" }
                            | _ -> span () { "other" }
                        )
                    })
            )))

let private am007 () =
    equal
        ("ArmsMatch", "Arms.Match: this pattern form is not supported.")
        (raised (fun () ->
            box (
                Component.template
                    (fun _ (state: Signal<Load>) -> state)
                    (div () {
                        Arms.Match (
                            read,
                            function
                            | Loaded (text, _) ->
                                (Console.Write ""

                                 if text = "" then
                                     span () { "empty" }
                                 else
                                     span () { "full" })
                            | _ -> span () { "Other" }
                        )
                    })
            )))

let private render (definition: Component<Signal<Load>>) (state: Load) =
    use graph = new Graph ()
    let signal = graph.Run (fun () -> Api.createSignal state)
    Server.render omit definition signal

/// <summary>An incomplete match builds; the unmatched case raises at the region read, as a plain match does.</summary>
let private incompleteRaisesAtRead () =
    let site =
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                Arms.Match (
                    read,
                    function
                    | Idle -> span () { "Not started" }
                    | Loading percent -> b () { fun _ -> string percent }
                )
            })

    equal "<!--[r--><div><!--[a:220727aa--><span>Not started</span><!--]--></div><!--]-->" (render site Idle)

    let unmatched =
        try
            render site (Loaded ("x", 1)) |> ignore
            false
        with :? MatchFailureException ->
            true

    equal true unmatched

/// <summary>A delegate over the instance reading a pattern variable is rewritten as a reader.</summary>
let private delegateReader () =
    let site =
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                Arms.Match (
                    read,
                    function
                    | Loaded (text, _) ->
                        span (
                            Hooks.exit (
                                Func<_, _, _, _>(fun (_: Signal<Load>) (_: Fable.Core.TS.Dom.HTMLSpanElement) finish ->
                                    if text = "" then
                                        finish ()

                                    ignore)
                            )
                        ) {
                            "held"
                        }
                    | _ -> span () { "Other" }
                )
            })

    let heldTwin =
        armOf (span (Hooks.exit (Func<_, _, _, _>(fun (_: unit -> string) (_: Fable.Core.TS.Dom.HTMLSpanElement) _ -> ignore))) { "held" })

    let otherTwin = armOf (span () { "Other" })

    let twin =
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                matchWith read (function
                    | Loaded (text, _) -> arm heldTwin text
                    | _ -> arm otherTwin ())
            })

    equal (render twin (Loaded ("a", 1))) (render site (Loaded ("a", 1)))

/// <summary>An if and a let at the top of case bodies, and a let of a view.</summary>
let private bodyForms () =
    let site =
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                Arms.Match (
                    read,
                    function
                    | Loaded (_, n) when n > 100 -> if n > 1000 then b () { "huge" } else i () { "big" }
                    | Loaded (t, 0) ->
                        let label = t + "!"
                        pre () { fun _ -> label }
                    | Loaded (t, _) ->
                        let view = pre () { fun _ -> t }
                        view
                    | _ -> span () { "Other" }
                )
            })

    let hugeArm = armOf (b () { "huge" })
    let bigArm = armOf (i () { "big" })
    let textArm = armOf (pre () { fun (p: unit -> string) -> p () })
    let otherArm = armOf (span () { "Other" })

    let twin =
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                matchWith read (function
                    | Loaded (_, n) when n > 1000 -> arm hugeArm ()
                    | Loaded (_, n) when n > 100 -> arm bigArm ()
                    | Loaded (t, 0) -> arm textArm (t + "!")
                    | Loaded (t, _) -> arm textArm t
                    | _ -> arm otherArm ())
            })

    for state in
        [
            Loaded ("x", 2000)
            Loaded ("x", 200)
            Loaded ("x", 0)
            Loaded ("y", 3)
            Idle
        ] do
        equal (state, render twin state) (state, render site state)

/// <summary>Milliseconds for the first and the mean of 200 further lowerings of a five-target site.</summary>
let private loweringCost () =
    let lower () =
        Arms.Match (
            read,
            function
            | Idle
            | Loading 0 -> span () { "Not started" }
            | Loading percent -> b () { fun _ -> string percent }
            | Loaded (_, 0) -> span () { "Empty" }
            | Loaded (text, count) when count > 99 -> b () { fun _ -> text }
            | Loaded (text, count) -> pre () { fun _ -> text + string count }
        )
        |> ignore

    let watch = Diagnostics.Stopwatch.StartNew ()
    lower ()
    let first = watch.Elapsed.TotalMilliseconds
    watch.Restart ()

    for _ in 1..200 do
        lower ()

    first, watch.Elapsed.TotalMilliseconds / 200.0

let private loweringBudget () =
    let first, mean = loweringCost ()
    printfn "lowering|warm-first=%.2fms mean=%.3fms" first mean

    if
        Environment.GetCommandLineArgs ()
        |> Array.contains "--timing"
    then
        equal true (mean <= 4.0)

let cases: (string * (unit -> unit)) array =
    [|
        "a pattern variable as static text raises AM001 with the call's location", am001
        "a pattern variable in a row reader raises AM002", am002
        "a pattern variable in a wildcard row reader raises AM002", am002Wildcard
        "a module-level arms function raises AM003", am003Named
        "a failwithf case body builds no arm", failwithfLeaf
        "a non-literal arms function raises AM003", am003
        "int, string, unit, option and struct instances raise AM004", am004
        "a decision hidden under a sequence raises AM007", am007
        "an incomplete match builds and raises MatchFailureException for the unmatched case", incompleteRaisesAtRead
        "a delegate over the instance reading a pattern variable is a reader", delegateReader
        "if, let and a let of a view at the top of case bodies", bodyForms
        "the lowering of a five-target site stays within its budget", loweringBudget
    |]

/// <summary>A reader with no arm of its site for its instance raises AM006.</summary>
let private noArm () =
    let message =
        try
            Ranvier.View.Arms.Cells.values (Ranvier.View.Arms.Cells.create ()) (obj ()) 0
            |> ignore

            ""
        with :? ArmsMatchException as error ->
            error.Message

    equal
        "Arms.Match: no arm of this site is mounted for the instance this reader received. A lambda over the instance type inside a case body runs with the arm's instance."
        message

/// <summary>The payload read by each thread's renders of the shared instance.</summary>
let private perThread = new Threading.ThreadLocal<int> ()

/// <summary>A site over a shared instance whose payload is the reading thread's number, and its <c>Hot</c> functions.</summary>
let private perThreadSite, private perThreadHots =
    Ranvier.View.Arms.Quoted.collectHots (fun () ->
        Component.template
            (fun _ (score: ArmsFixtures.Score) -> score)
            (div () {
                Arms.Match (
                    (fun (score: ArmsFixtures.Score) -> { score with Points = perThread.Value }),
                    function
                    | { Points = 0 } -> span () { "no points" }
                    | { Name = name; Points = points } -> pre () { fun _ -> name + ":" + string points }
                )
            }))

/// <summary>Waits up to ten seconds for the condition; whether it holds.</summary>
let private eventually (condition: unit -> bool) =
    Threading.SpinWait.SpinUntil ((fun () -> condition ()), 10000)

/// <summary>
/// Sixteen threads render one site for one shared instance, each under its own graph with a payload of its own; each
/// response shows its own thread's payload (Review Focus 1), and every <c>Hot</c> function of the site compiles.
/// </summary>
let private concurrentRenders () =
    let score: ArmsFixtures.Score = { Name = "ada"; Points = 5 }
    let failures = Collections.Concurrent.ConcurrentBag<string>()
    use barrier = new Threading.Barrier 16

    let threads =
        [|
            for index in 0..15 ->
                Threading.Thread (fun () ->
                    perThread.Value <- index

                    let expected =
                        if index = 0 then
                            "<span>no points</span>"
                        else
                            sprintf "<pre>ada:%d</pre>" index

                    barrier.SignalAndWait ()

                    for _ in 1..200 do
                        try
                            let html = Server.render omit perThreadSite score

                            if not (html.Contains expected) then
                                failures.Add (sprintf "thread %d: %s" index html)
                        with error ->
                            failures.Add (sprintf "thread %d: %s" index error.Message))
        |]

    for thread in threads do
        thread.Start ()

    for thread in threads do
        thread.Join ()

    equal [] (List.ofSeq failures |> List.truncate 3)

    equal
        (true, true, false)
        (not perThreadHots.IsEmpty,
         eventually (fun () ->
             perThreadHots
             |> List.forall (fun hot -> hot.Compiled)),
         perThreadHots
         |> List.exists (fun hot -> hot.Failed))

/// <summary>A site whose case body binds values with <c>let</c> and reads them in a reader, and its <c>Hot</c> functions.</summary>
let private letBoundSite, private letBoundHots =
    Ranvier.View.Arms.Quoted.collectHots (fun () ->
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                Arms.Match (
                    read,
                    function
                    | Loaded (text, count) ->
                        let suffix = "!"
                        let scale = 10
                        pre () { fun _ -> text + suffix + string (count * scale) }
                    | _ -> span () { "other" }
                )
            }))

/// <summary>A site whose case body holds readers inside a computation-expression <c>for</c>, and its <c>Hot</c> functions.</summary>
let private forBodySite, private forBodyHots =
    Ranvier.View.Arms.Quoted.collectHots (fun () ->
        Component.template
            (fun _ (state: Signal<Load>) -> state)
            (div () {
                Arms.Match (
                    read,
                    function
                    | Loaded (text, _) ->
                        ul () {
                            for label in [ "a"; "b" ] do
                                li () { fun _ -> text + label }
                        }
                    | _ -> span () { "other" }
                )
            }))

/// <summary>Readers inside a computation-expression <c>for</c> body compile after <c>hotCalls</c> renders and keep their output.</summary>
let private forBodyCompiles () =
    let html () =
        render forBodySite (Loaded ("t", 1))

    let before =
        [
            for _ in 1 .. Ranvier.View.Arms.Quoted.hotCalls + 1 -> (html ()).Contains "<li>ta</li><li>tb</li>"
        ]

    let compiled =
        eventually (fun () ->
            forBodyHots
            |> List.forall (fun hot -> hot.Compiled))

    equal (true, true, true, true) (forBodyHots.Length >= 3, List.forall id before, compiled, (html ()).Contains "<li>ta</li><li>tb</li>")

/// <summary>A reader over let-bound case-body values compiles after <c>hotCalls</c> renders and keeps its output.</summary>
let private letBoundCompiles () =
    let html () =
        render letBoundSite (Loaded ("a", 2))

    let before =
        [
            for _ in 1 .. Ranvier.View.Arms.Quoted.hotCalls + 1 -> (html ()).Contains "<pre>a!20</pre>"
        ]

    let compiled =
        eventually (fun () ->
            letBoundHots
            |> List.forall (fun hot -> hot.Compiled))

    equal (true, true, true, true) (letBoundHots.Length >= 2, List.forall id before, compiled, (html ()).Contains "<pre>a!20</pre>")

/// <summary>The 64th call of a hot lambda queues its compile and returns before the compile completes.</summary>
let private hotDoesNotBlock () =
    use release = new Threading.ManualResetEventSlim false

    let compile () =
        release.Wait 10000 |> ignore
        box (fun (x: int) -> x + 2)

    let hot = Ranvier.View.Arms.Quoted.Hot<int, int>((fun x -> x + 1), compile)
    let state = hot :> Ranvier.View.Arms.Quoted.IHot
    let call = hot |> box |> unbox<int -> int>
    let watch = Diagnostics.Stopwatch.StartNew ()

    for _ in 1 .. Ranvier.View.Arms.Quoted.hotCalls do
        call 1 |> ignore

    let elapsed = watch.ElapsedMilliseconds
    let interpreted = call 1
    let queued = state.Queued
    release.Set ()
    let compiled = eventually (fun () -> state.Compiled)
    equal (true, 2, 1, true, 3) (elapsed < 1000, interpreted, queued, compiled, call 1)

/// <summary>The messages written to it, in order.</summary>
type private Recorder() =
    inherit Diagnostics.TraceListener()
    member val Lines = Collections.Concurrent.ConcurrentQueue<string>()

    override this.Write(message: string) =
        this.Lines.Enqueue message

    override this.WriteLine(message: string) =
        this.Lines.Enqueue message

/// <summary>A hot lambda whose compile fails keeps running interpreted and writes one Trace warning naming the failure.</summary>
let private hotCompileFailure () =
    let recorder = new Recorder ()
    Diagnostics.Trace.Listeners.Add recorder |> ignore

    let compile () : obj =
        failwith "compile failed"

    let hot = Ranvier.View.Arms.Quoted.Hot<int, int>((fun x -> x + 1), compile)
    let state = hot :> Ranvier.View.Arms.Quoted.IHot
    let call = hot |> box |> unbox<int -> int>
    let results = [ for _ in 1..200 -> call 1 ]
    let failed = eventually (fun () -> state.Failed)
    Diagnostics.Trace.Listeners.Remove recorder

    let warnings =
        [
            for line in recorder.Lines do
                if line.Contains "Arms.Match: a per-call lambda stays interpreted: compile failed" then
                    line
        ]

    equal (true, true, false, 1, 2, 1) (results |> List.forall ((=) 2), failed, state.Compiled, state.Queued, call 1, warnings.Length)

/// <summary>Sixteen threads calling one hot lambda at once queue its compile exactly once.</summary>
let private hotCompilesOnce () =
    let compiles = ref 0

    let compile () =
        Threading.Interlocked.Increment &compiles.contents
        |> ignore

        box (fun (x: int) -> x + 2)

    let hot = Ranvier.View.Arms.Quoted.Hot<int, int>((fun x -> x + 1), compile)
    let state = hot :> Ranvier.View.Arms.Quoted.IHot
    let call = hot |> box |> unbox<int -> int>
    use barrier = new Threading.Barrier 16

    let threads =
        [|
            for _ in 1..16 ->
                Threading.Thread (fun () ->
                    barrier.SignalAndWait ()

                    for _ in 1..200 do
                        call 1 |> ignore)
        |]

    for thread in threads do
        thread.Start ()

    for thread in threads do
        thread.Join ()

    let compiled = eventually (fun () -> state.Compiled)
    equal (true, 1, 1, 3) (compiled, state.Queued, compiles.Value, call 1)

/// <summary>
/// <c>Arms.Prepare</c> runs twice, then on four threads at once, each also lowering and rendering a site; nothing raises
/// and each render shows its payload.
/// </summary>
let private prepares () =
    Arms.Prepare ()
    Arms.Prepare ()
    let failures = Collections.Concurrent.ConcurrentBag<string>()
    use barrier = new Threading.Barrier 4

    let threads =
        [|
            for index in 1..4 ->
                Threading.Thread (fun () ->
                    barrier.SignalAndWait ()

                    try
                        Arms.Prepare ()

                        let site =
                            Component.template
                                (fun _ (state: Signal<Load>) -> state)
                                (div () {
                                    Arms.Match (
                                        read,
                                        function
                                        | Loading percent -> b () { fun _ -> string percent }
                                        | _ -> span () { "other" }
                                    )
                                })

                        let html = render site (Loading index)

                        if not (html.Contains (sprintf "<b>%d</b>" index)) then
                            failures.Add html
                    with error ->
                        failures.Add error.Message)
        |]

    for thread in threads do
        thread.Start ()

    for thread in threads do
        thread.Join ()

    equal [] (List.ofSeq failures)

let runtimeCases: (string * (unit -> unit)) array =
    [|
        "a reader with no arm for its instance raises AM006", noArm
        "concurrent server renders of one shared instance read their own graph's payload", concurrentRenders
        "a reader over let-bound case-body values compiles", letBoundCompiles
        "readers inside a computation-expression for body compile", forBodyCompiles
        "the 64th call of a hot lambda does not wait for its compile", hotDoesNotBlock
        "a hot lambda whose compile fails keeps running interpreted", hotCompileFailure
        "concurrent calls of a hot lambda compile it once", hotCompilesOnce
        "Arms.Prepare runs twice and concurrently with site lowerings", prepares
    |]
