// The F# half of the differential check: a seeded generator of command-language programs, a printer and a parser of
// their text form, and an interpreter of a program over real Signal, Memo and Effect objects. run.sh compares its output
// with that of driver.ml. Agreement is evidence that the model matches the F#, not a proof.
//
//   dotnet fsi differential/Generate.fsx generate <count> <seed> <directory>
//   dotnet fsi differential/Generate.fsx run <directory> <first index>
//   dotnet fsi differential/Generate.fsx replay <programs file> <directory>
//
// `generate` writes programs.txt, one S-expression per line, stats.txt and floors.txt, then acts as `run` from index 0.
// `replay` copies a file to programs.txt first. `run` appends one line per program to fsharp.txt, tab-separated:
//   index, status, trace, end state, closed state, coverage
// The status is one of
//   ok         the program returned, or failed at its top level, and every column is complete
//   stuck      the program reached a state with no F# meaning, where `exec` is stuck
//   budget     the program passed the step or depth budget, and has no complete trace
//   excluded   the program raised more than one zero divisor
//   harness    the harness itself failed, with the reason in the trace column
//
// current.txt holds the index of the running program, and is absent otherwise. A crash leaves it behind, and run.sh
// records that program as `fatal` and starts `run` at the next index. The watchdog ends the process the same way.
#load "../conformance/Harness.fsx"

open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Reflection
open System.Text
open System.Threading
open Ranvier
open Harness

// ---------------------------------------------------------------------------------------------------------------------
// The command language of theories/Base/Syntax.v
// ---------------------------------------------------------------------------------------------------------------------

type Expr =
    | K of int
    | V of int
    | Prev
    | Add of Expr * Expr
    | Sub of Expr * Expr
    | Mul of Expr * Expr
    | Div of Expr * Expr
    | Lt of Expr * Expr
    | Eq of Expr * Expr

type Comparer =
    | CmpEq
    | CmpNever
    | CmpThrows of int

type Cmd =
    | CRet of Expr
    | CThrow of int
    | CLet of int * Expr * Cmd
    | CIf of Expr * Cmd * Cmd
    | CRead of int * Expr * Cmd
    | CTryRead of int * int * Expr * Cmd
    | CReadUntracked of int * Expr * Cmd
    | CWrite of Expr * Expr * Cmd
    | CSignal of int * Expr * Cmd
    | CMemo of int * bool * Comparer * Cmd * Cmd
    | CEffect of int * Cmd * Cmd
    | CCleanup of Cmd * Cmd
    | CDispose of Expr * Cmd
    | CBatch of Cmd * Cmd
    | CLog of Expr * Cmd

/// The `prev_var` of theories/Pure/Engine.v. Programs use smaller variables.
[<Literal>]
let PrevVar = 4096

// ---------------------------------------------------------------------------------------------------------------------
// The text form
// ---------------------------------------------------------------------------------------------------------------------

let rec printExpr (e: Expr) =
    match e with
    | K z -> $"(k {z})"
    | V x -> $"(v {x})"
    | Prev -> "(prev)"
    | Add (a, b) -> $"(add {printExpr a} {printExpr b})"
    | Sub (a, b) -> $"(sub {printExpr a} {printExpr b})"
    | Mul (a, b) -> $"(mul {printExpr a} {printExpr b})"
    | Div (a, b) -> $"(div {printExpr a} {printExpr b})"
    | Lt (a, b) -> $"(lt {printExpr a} {printExpr b})"
    | Eq (a, b) -> $"(eq {printExpr a} {printExpr b})"

let printComparer (c: Comparer) =
    match c with
    | CmpEq -> "eq"
    | CmpNever -> "never"
    | CmpThrows f -> $"(throws {f})"

let rec printCmd (c: Cmd) =
    match c with
    | CRet e -> $"(ret {printExpr e})"
    | CThrow f -> $"(throw {f})"
    | CLet (x, e, k) -> $"(let {x} {printExpr e} {printCmd k})"
    | CIf (e, a, b) -> $"(if {printExpr e} {printCmd a} {printCmd b})"
    | CRead (x, n, k) -> $"(read {x} {printExpr n} {printCmd k})"
    | CTryRead (x, ok, n, k) -> $"(tryread {x} {ok} {printExpr n} {printCmd k})"
    | CReadUntracked (x, n, k) -> $"(readu {x} {printExpr n} {printCmd k})"
    | CWrite (n, e, k) -> $"(write {printExpr n} {printExpr e} {printCmd k})"
    | CSignal (x, e, k) -> $"(signal {x} {printExpr e} {printCmd k})"
    | CMemo (x, owning, q, b, k) ->
        let mode = if owning then "owning" else "pure"
        $"(memo {x} {mode} {printComparer q} {printCmd b} {printCmd k})"
    | CEffect (x, b, k) -> $"(effect {x} {printCmd b} {printCmd k})"
    | CCleanup (b, k) -> $"(cleanup {printCmd b} {printCmd k})"
    | CDispose (n, k) -> $"(dispose {printExpr n} {printCmd k})"
    | CBatch (b, k) -> $"(batch {printCmd b} {printCmd k})"
    | CLog (e, k) -> $"(log {printExpr e} {printCmd k})"

type Sexp =
    | Atom of string
    | Node of Sexp list

let parseSexp (text: string) : Sexp =
    let mutable i = 0

    let rec one () =
        while text[i] = ' ' do
            i <- i + 1

        if text[i] = '(' then
            i <- i + 1
            let items = ResizeArray<Sexp>()

            while (while text[i] = ' ' do
                       i <- i + 1

                   text[i] <> ')') do
                items.Add (one ())

            i <- i + 1
            Node (List.ofSeq items)
        else
            let start = i

            while text[i] <> ' ' && text[i] <> ')' && text[i] <> '(' do
                i <- i + 1

            Atom text[start .. i - 1]

    one ()

let private atomInt (s: Sexp) =
    match s with
    | Atom a -> int a
    | Node _ -> failwith "expected an integer"

let rec readExpr (s: Sexp) : Expr =
    match s with
    | Node [ Atom "k"; z ] -> K (atomInt z)
    | Node [ Atom "v"; x ] -> V (atomInt x)
    | Node [ Atom "prev" ] -> Prev
    | Node [ Atom "add"; a; b ] -> Add (readExpr a, readExpr b)
    | Node [ Atom "sub"; a; b ] -> Sub (readExpr a, readExpr b)
    | Node [ Atom "mul"; a; b ] -> Mul (readExpr a, readExpr b)
    | Node [ Atom "div"; a; b ] -> Div (readExpr a, readExpr b)
    | Node [ Atom "lt"; a; b ] -> Lt (readExpr a, readExpr b)
    | Node [ Atom "eq"; a; b ] -> Eq (readExpr a, readExpr b)
    | _ -> failwith $"not an expression: {s}"

let readComparer (s: Sexp) : Comparer =
    match s with
    | Atom "eq" -> CmpEq
    | Atom "never" -> CmpNever
    | Node [ Atom "throws"; f ] -> CmpThrows (atomInt f)
    | _ -> failwith $"not a comparer: {s}"

let rec readCmd (s: Sexp) : Cmd =
    match s with
    | Node [ Atom "ret"; e ] -> CRet (readExpr e)
    | Node [ Atom "throw"; f ] -> CThrow (atomInt f)
    | Node [ Atom "let"; x; e; k ] -> CLet (atomInt x, readExpr e, readCmd k)
    | Node [ Atom "if"; e; a; b ] -> CIf (readExpr e, readCmd a, readCmd b)
    | Node [ Atom "read"; x; n; k ] -> CRead (atomInt x, readExpr n, readCmd k)
    | Node [ Atom "tryread"; x; ok; n; k ] -> CTryRead (atomInt x, atomInt ok, readExpr n, readCmd k)
    | Node [ Atom "readu"; x; n; k ] -> CReadUntracked (atomInt x, readExpr n, readCmd k)
    | Node [ Atom "write"; n; e; k ] -> CWrite (readExpr n, readExpr e, readCmd k)
    | Node [ Atom "signal"; x; e; k ] -> CSignal (atomInt x, readExpr e, readCmd k)
    | Node [ Atom "memo"; x; Atom mode; q; b; k ] -> CMemo (atomInt x, (mode = "owning"), readComparer q, readCmd b, readCmd k)
    | Node [ Atom "effect"; x; b; k ] -> CEffect (atomInt x, readCmd b, readCmd k)
    | Node [ Atom "cleanup"; b; k ] -> CCleanup (readCmd b, readCmd k)
    | Node [ Atom "dispose"; n; k ] -> CDispose (readExpr n, readCmd k)
    | Node [ Atom "batch"; b; k ] -> CBatch (readCmd b, readCmd k)
    | Node [ Atom "log"; e; k ] -> CLog (readExpr e, readCmd k)
    | _ -> failwith $"not a command: {s}"

// ---------------------------------------------------------------------------------------------------------------------
// The generator
// ---------------------------------------------------------------------------------------------------------------------

type Site =
    | TopSite
    | PureBody
    | OwningBody
    | EffectBody
    | CleanupBody

/// What a body may refer to. A write-capable body reads signals below `ReadLimit` and writes signals at or above
/// `WriteFloor`, with `ReadLimit <= WriteFloor`, so every write goes to a signal of a higher creation index than any
/// signal its body reads, and writes from bodies form no cycle. The exceptions write a signal they read: `SelfWritingMemo`,
/// `ClimbingEffect`, `NestedSelfRun` and `RewritingCleanup` under a guard that stops changing the value, and
/// `CleanupWritesOwnSource` under the `inCleanup` guard of the core, which suppresses the mark.
type Scope =
    {
        Signals: (int * int) list
        /// Each memo with the highest creation index among the signals its body can read.
        Memos: (int * int) list
        Computations: int list
        Locals: int list
        ReadLimit: int
        WriteFloor: int
        Self: int option
        /// The highest creation index the enclosing memo's body has read so far.
        Reads: int ref
    }

let private unbounded = Int32.MaxValue

let private emptyScope =
    {
        Signals = []
        Memos = []
        Computations = []
        Locals = []
        ReadLimit = unbounded
        WriteFloor = 0
        Self = None
        Reads = ref -1
    }

// Each template logs its own constant on the path that makes its shape. The coverage gate counts the template by that
// event, so a template that stops reaching its shape fails the gate.
[<Literal>]
let SentinelDiamond = 9000

[<Literal>]
let SentinelStoppedWalk = 9001

[<Literal>]
let SentinelCleanupOwnSource = 9002

[<Literal>]
let SentinelRewritingCleanup = 9003

[<Literal>]
let SentinelRewritingThroughMemo = 9006

[<Literal>]
let SentinelPullWakesEffect = 9004

[<Literal>]
let SentinelSelfMarking = 9005

let sentinelNames =
    dict
        [
            SentinelDiamond, "template diamond"
            SentinelStoppedWalk, "template check walk stops at a moved source"
            SentinelCleanupOwnSource, "template effect cleanup writes its own source"
            SentinelRewritingCleanup, "template owning memo cleanup writes, reads and writes"
            SentinelRewritingThroughMemo, "template owning memo cleanup writes through a memo"
            SentinelPullWakesEffect, "template host pull wakes an effect"
            SentinelSelfMarking, "template self-marking cleanup"
        ]

type Generator(rng: Random) =
    let mutable nextVar = 0
    let mutable nextSignal = 0

    /// Draws of a node or a cleanup in a pure memo's body, which this generator drops.
    member val PureScopeDraws = 0 with get, set

    member private _.Fresh() =
        nextVar <- nextVar + 1
        nextVar

    member private _.SmallInt() =
        rng.Next (-2, 13)

    member private _.Pick(items: 'a list) : 'a =
        items[rng.Next items.Length]

    member private _.Chance(percent: int) =
        rng.Next 100 < percent

    member private this.Failure() =
        this.Pick [ 7; 8; 9 ]

    member private this.Atom(sc: Scope) =
        if not sc.Locals.IsEmpty && this.Chance 70 then
            V (this.Pick sc.Locals)
        else
            K (this.SmallInt ())

    /// Arithmetic whose values stay small over a run: the constant operands are small and a product has a small constant.
    member private this.Expr(sc: Scope) =
        let a = this.Atom sc

        match rng.Next 12 with
        | 0
        | 1 -> Add (a, this.Atom sc)
        | 2 -> Sub (a, this.Atom sc)
        | 3 -> Mul (a, K (rng.Next (0, 3)))
        | 4 -> Div (a, K (this.Pick [ 2; 3; -2 ]))
        | 5 -> Lt (a, K (this.SmallInt ()))
        | 6 -> Eq (a, K (this.SmallInt ()))
        // A divisor that can be zero.
        | 7 when not sc.Locals.IsEmpty && this.Chance 30 -> Div (a, V (this.Pick sc.Locals))
        | _ -> a

    member private this.ReadTarget(sc: Scope) =
        let signals =
            sc.Signals
            |> List.filter (fun (_, i) -> i < sc.ReadLimit)

        let memos =
            sc.Memos
            |> List.filter (fun (_, d) -> d < sc.ReadLimit)

        match signals @ memos with
        | [] -> None
        | all ->
            let v, d = this.Pick all
            sc.Reads.Value <- max sc.Reads.Value d
            Some v

    member private this.WriteTarget(sc: Scope) =
        match
            sc.Signals
            |> List.filter (fun (_, i) -> i >= sc.WriteFloor)
        with
        | [] -> None
        | all -> Some (fst (this.Pick all))

    member private this.Finish (site: Site) (sc: Scope) =
        match site with
        | PureBody
        | OwningBody when this.Chance 4 -> CThrow (this.Failure ())
        | PureBody
        | OwningBody -> CRet (this.Expr sc)
        | _ -> CRet (K 0)

    /// A body of at most `n` statements.
    member this.Body (site: Site) (sc: Scope) (n: int) : Cmd =
        if n <= 0 then
            this.Finish site sc
        else
            let rest (sc': Scope) =
                this.Body site sc' (n - 1)

            let isPure = (site = PureBody)

            let writes =
                (site = EffectBody
                 || site = CleanupBody
                 || site = TopSite)

            match rng.Next 100 with
            | r when r < 32 ->
                match this.ReadTarget sc with
                | Some t ->
                    let x = this.Fresh ()
                    CRead (x, V t, rest { sc with Locals = x :: sc.Locals })
                | None -> rest sc
            | r when r < 38 ->
                match this.ReadTarget sc with
                | Some t ->
                    let x = this.Fresh ()
                    let ok = this.Fresh ()

                    CTryRead (
                        x,
                        ok,
                        V t,
                        rest
                            { sc with
                                Locals = x :: ok :: sc.Locals
                            }
                    )
                | None -> rest sc
            | r when r < 42 ->
                match this.ReadTarget sc with
                | Some t ->
                    let x = this.Fresh ()
                    CReadUntracked (x, V t, rest { sc with Locals = x :: sc.Locals })
                | None -> rest sc
            | r when r < 56 -> CLog (this.Expr sc, rest sc)
            | r when r < 60 ->
                let x = this.Fresh ()
                CLet (x, this.Expr sc, rest { sc with Locals = x :: sc.Locals })
            | r when r < 72 -> CIf (this.Expr sc, this.Body site sc (n / 2), this.Body site sc (n / 2))
            | r when r < 80 && writes ->
                match this.WriteTarget sc with
                | Some t -> CWrite (V t, this.Expr sc, rest sc)
                | None -> rest sc
            | r when r < 84 -> CBatch (this.Body site sc (n / 2), rest sc)
            | r when r < 89 ->
                if isPure then
                    this.PureScopeDraws <- this.PureScopeDraws + 1
                    rest sc
                else
                    CCleanup (this.Body CleanupBody sc (min 3 (n / 2)), rest sc)
            | r when r < 92 ->
                if isPure then
                    this.PureScopeDraws <- this.PureScopeDraws + 1
                    rest sc
                else
                    let x = this.Fresh ()
                    let owning = this.Chance 40
                    let b, reads = this.MemoBody owning x sc (min 3 (n / 2))

                    CMemo (
                        x,
                        owning,
                        this.Comparer (),
                        b,
                        rest
                            { sc with
                                Memos = (x, reads) :: sc.Memos
                                Computations = x :: sc.Computations
                            }
                    )
            | r when r < 94 ->
                if isPure then
                    this.PureScopeDraws <- this.PureScopeDraws + 1
                    rest sc
                else
                    let x = this.Fresh ()

                    let b =
                        this.Body
                            EffectBody
                            { sc with
                                Computations = x :: sc.Computations
                                Self = Some x
                            }
                            (min 3 (n / 2))

                    CEffect (
                        x,
                        b,
                        rest
                            { sc with
                                Computations = x :: sc.Computations
                            }
                    )
            | r when r < 96 ->
                match sc.Computations with
                | [] -> rest sc
                | all -> CDispose (V (this.Pick all), rest sc)
            | r when r < 98 -> CIf (this.Expr sc, CThrow (this.Failure ()), rest sc)
            | _ ->
                // A memo reading itself.
                match sc.Self with
                | Some self when site = PureBody || site = OwningBody ->
                    let x = this.Fresh ()
                    CRead (x, V self, rest { sc with Locals = x :: sc.Locals })
                | _ -> rest sc

    member private this.Comparer() =
        match rng.Next 100 with
        | r when r < 70 -> CmpEq
        | r when r < 85 -> CmpNever
        | _ -> CmpThrows (this.Failure ())

    /// A memo's body and the highest creation index it can read. An owning memo's cleanups keep the write rule.
    member private this.MemoBody (owning: bool) (x: int) (sc: Scope) (n: int) =
        let reads = ref -1

        let floor, limit =
            if owning then
                let w = this.WriteSplit sc
                w, min sc.ReadLimit w
            else
                unbounded, sc.ReadLimit

        let inner =
            { sc with
                Computations = x :: sc.Computations
                Self = Some x
                Reads = reads
                ReadLimit = limit
                WriteFloor = max sc.WriteFloor floor
            }

        let b = this.Body (if owning then OwningBody else PureBody) inner n
        sc.Reads.Value <- max sc.Reads.Value reads.Value
        b, reads.Value

    /// The creation index dividing what a new write-capable body reads from what it writes.
    member private this.WriteSplit(sc: Scope) =
        match sc.Signals with
        | [] -> 0
        | signals ->
            let top = signals |> List.map snd |> List.max
            rng.Next (0, top + 2)

    member private this.NewMemo(sc: Scope) =
        let x = this.Fresh ()
        let owning = this.Chance 35
        let b, reads = this.MemoBody owning x sc (rng.Next (1, 6))

        (fun k -> CMemo (x, owning, this.Comparer (), b, k)),
        { sc with
            Memos = (x, reads) :: sc.Memos
            Computations = x :: sc.Computations
        }

    member private this.NewEffect(sc: Scope) =
        let x = this.Fresh ()
        let w = this.WriteSplit sc

        let inner =
            { sc with
                Computations = x :: sc.Computations
                Self = Some x
                ReadLimit = min sc.ReadLimit w
                WriteFloor = max sc.WriteFloor w
                Reads = ref -1
            }

        let b = this.Body EffectBody inner (rng.Next (1, 6))

        (fun k -> CEffect (x, b, k)),
        { sc with
            Computations = x :: sc.Computations
        }

    member private this.NewSignal(sc: Scope) =
        let x = this.Fresh ()
        let i = nextSignal
        nextSignal <- nextSignal + 1
        let v = this.SmallInt ()

        (fun k -> CSignal (x, K v, k)),
        { sc with
            Signals = (x, i) :: sc.Signals
        },
        x,
        i

    member private _.Nop = CRet (K 0)

    // Each template adds a shape the random bodies reach rarely.

    /// Two memos over a signal, and an effect reading both.
    member private this.Diamond(sc: Scope) =
        let addS, sc, s, i = this.NewSignal sc
        let a, b, e = this.Fresh (), this.Fresh (), this.Fresh ()
        let x, y, u, v = this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        (fun k ->
            addS (
                CMemo (
                    a,
                    false,
                    CmpEq,
                    CRead (x, V s, CLog (V x, CRet (Add (V x, K 1)))),
                    CMemo (
                        b,
                        false,
                        CmpEq,
                        CRead (y, V s, CLog (V y, CRet (Mul (V y, K 2)))),
                        CEffect (e, CRead (u, V a, CRead (v, V b, CLog (K SentinelDiamond, CLog (Add (V u, V v), this.Nop)))), k)
                    )
                )
            )),
        { sc with
            Memos = (a, i) :: (b, i) :: sc.Memos
            Computations = e :: a :: b :: sc.Computations
        }

    /// A memo whose value cuts off most writes, under the chosen comparer, with an effect reading it.
    member private this.Cutoff(sc: Scope) =
        let addS, sc, s, i = this.NewSignal sc
        let m, e = this.Fresh (), this.Fresh ()
        let x, y = this.Fresh (), this.Fresh ()
        let q = this.Comparer ()

        (fun k -> addS (CMemo (m, false, q, CRead (x, V s, CLog (V x, CRet (Lt (V x, K 5)))), CEffect (e, CRead (y, V m, CLog (V y, this.Nop)), k)))),
        { sc with
            Memos = (m, i) :: sc.Memos
            Computations = e :: m :: sc.Computations
        }

    /// A memo writes its own source, up to a ceiling, and an effect reads it.
    member private this.SelfWritingMemo(sc: Scope) =
        let addS, sc, s, i = this.NewSignal sc
        let m, e = this.Fresh (), this.Fresh ()
        let x, y = this.Fresh (), this.Fresh ()
        let ceiling = rng.Next (1, 5)

        let answer =
            if this.Chance 50 then
                CRet (K 0)
            else
                CIf (Lt (V x, K (ceiling - 1)), CRet (K 0), CRet (V x))

        (fun k ->
            addS (
                CMemo (
                    m,
                    false,
                    CmpEq,
                    CRead (x, V s, CLog (V x, CIf (Lt (V x, K ceiling), CWrite (V s, Add (V x, K 1), answer), answer))),
                    CEffect (e, CRead (y, V m, CLog (V y, this.Nop)), k)
                )
            )),
        { sc with
            Memos = (m, i) :: sc.Memos
            Computations = e :: m :: sc.Computations
        }

    /// An owning memo's cleanup writes a source its reader has read in the same run.
    member private this.SelfMarkingCleanup(sc: Scope) =
        let addH, sc, h, _ = this.NewSignal sc
        let addK, sc, kk, _ = this.NewSignal sc
        let addS, sc, s, i = this.NewSignal sc
        let y, x, e = this.Fresh (), this.Fresh (), this.Fresh ()

        let a, b, c, d, f =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        (fun k ->
            addH (
                addK (
                    addS (
                        CMemo (
                            y,
                            true,
                            CmpEq,
                            CRead (a, V kk, CCleanup (CLog (K SentinelSelfMarking, CWrite (V s, K 50, this.Nop)), CRet (Mul (V a, K 0)))),
                            CMemo (
                                x,
                                false,
                                CmpEq,
                                CRead (b, V h, CRead (c, V s, CRead (d, V y, CLog (V c, CRet (V c))))),
                                CEffect (e, CRead (f, V x, CLog (V f, this.Nop)), CBatch (CWrite (V h, K 1, CWrite (V kk, K 1, this.Nop)), k))
                            )
                        )
                    )
                )
            )),
        { sc with
            Memos = (x, i) :: (y, i - 1) :: sc.Memos
            Computations = e :: x :: y :: sc.Computations
        }

    /// A run reads a source, marks itself, and reads itself.
    member private this.NestedSelfRun(sc: Scope) =
        let addF, sc, flip, _ = this.NewSignal sc
        let addA, sc, a, _ = this.NewSignal sc
        let addB, sc, b, i = this.NewSignal sc
        let m, e = this.Fresh (), this.Fresh ()

        let f, u, v, w, y, z =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        (fun k ->
            addF (
                addA (
                    addB (
                        CMemo (
                            m,
                            false,
                            CmpEq,
                            CRead (
                                f,
                                V flip,
                                CIf (
                                    Eq (V f, K 2),
                                    CRead (u, V a, CWrite (V flip, K 3, CRead (v, V m, CRet (V u)))),
                                    CIf (Eq (V f, K 3), CRead (w, V b, CRet (V w)), CRead (y, V a, CRet (V y)))
                                )
                            ),
                            CEffect (e, CRead (z, V m, CLog (V z, this.Nop)), CWrite (V flip, K 2, CWrite (V a, K (this.SmallInt ()), k)))
                        )
                    )
                )
            )),
        { sc with
            Memos = (m, i) :: sc.Memos
            Computations = e :: m :: sc.Computations
        }

    /// A computation disposes itself, keeps running, and attaches children to its disposed scope.
    member private this.LateChild(sc: Scope) =
        let addS, sc, s, i = this.NewSignal sc
        let host = this.Fresh ()
        let x, child, y, z = this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()
        let readsSource = CRead (z, V s, CLog (V z, CRet (K 0)))

        let attach =
            match rng.Next 3 with
            | 0 -> CMemo (child, false, CmpEq, CRet (K 1), CRead (y, V child, CLog (V y, this.Nop)))
            | 1 -> CEffect (child, readsSource, this.Nop)
            | _ -> CCleanup (CLog (K 77, this.Nop), this.Nop)

        let body =
            CRead (x, V s, CLog (V x, CCleanup (CLog (K 66, this.Nop), CDispose (V host, attach))))

        if this.Chance 60 then
            (fun k -> addS (CEffect (host, body, k))),
            { sc with
                Computations = host :: sc.Computations
            }
        else
            let owned =
                CRead (x, V s, CLog (V x, CDispose (V host, CMemo (child, false, CmpEq, CRet (K 1), CRet (V x)))))

            (fun k -> addS (CMemo (host, true, CmpEq, owned, k))),
            { sc with
                Memos = (host, i) :: sc.Memos
                Computations = host :: sc.Computations
            }

    /// An effect reaches a fixpoint by writing a source it reads.
    member private this.ClimbingEffect(sc: Scope) =
        let addS, sc, s, _ = this.NewSignal sc
        let e, x = this.Fresh (), this.Fresh ()
        let ceiling = rng.Next (1, 5)

        (fun k -> addS (CEffect (e, CRead (x, V s, CLog (V x, CIf (Lt (V x, K ceiling), CWrite (V s, Add (V x, K 1), this.Nop), this.Nop))), k))),
        { sc with
            Computations = e :: sc.Computations
        }

    /// A memo's failure read through `CTryRead` and through a read that fails its effect.
    member private this.FailingMemo(sc: Scope) =
        let addS, sc, s, i = this.NewSignal sc
        let m, e, g = this.Fresh (), this.Fresh (), this.Fresh ()
        let x, y, ok, z = this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()
        let f = this.Failure ()

        (fun k ->
            addS (
                CMemo (
                    m,
                    false,
                    CmpEq,
                    CRead (x, V s, CIf (Lt (V x, K 3), CRet (V x), CThrow f)),
                    CEffect (
                        e,
                        CTryRead (y, ok, V m, CLog (Add (V y, Mul (V ok, K 100)), this.Nop)),
                        CEffect (g, CRead (z, V m, CLog (V z, this.Nop)), k)
                    )
                )
            )),
        { sc with
            Memos = (m, i) :: sc.Memos
            Computations = g :: e :: m :: sc.Computations
        }

    /// An effect whose cleanup fails.
    member private this.FailingCleanup(sc: Scope) =
        let addS, sc, s, _ = this.NewSignal sc
        let e, x = this.Fresh (), this.Fresh ()
        let f = this.Failure ()

        (fun k -> addS (CEffect (e, CRead (x, V s, CCleanup (CThrow f, CLog (V x, this.Nop))), k))),
        { sc with
            Computations = e :: sc.Computations
        }

    /// A memo answering from its previous value. The read at its creation publishes one before the second branch can run.
    member private this.PreviousValue(sc: Scope) =
        let addS, sc, s, i = this.NewSignal sc
        let m, x, y = this.Fresh (), this.Fresh (), this.Fresh ()
        let start = rng.Next (0, 3)

        let body =
            CRead (x, V s, CIf (Eq (V x, K start), CRet (K 1), CRet (Add (Prev, K 1))))

        (fun k -> addS (CWrite (V s, K start, CMemo (m, false, CmpEq, body, CRead (y, V m, k))))),
        { sc with
            Memos = (m, i) :: sc.Memos
            Computations = m :: sc.Computations
        }

    // The templates below add nodes after the program's other operations and take their choices from `tail`, so that a seed
    // and index that named a program before they existed keep naming the same operations.

    /// A memo's `Check` walk stops at the first source that moves: the second source is read only by the branch the
    /// re-run leaves behind, and stays unrun.
    member private this.StoppedWalk(tail: Random) : Cmd -> Cmd =
        let s, a, b, c, e =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let x, y, u, v, w =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let moved = tail.Next (1, 4)

        let reader =
            CRead (u, V a, CIf (Eq (V u, K 0), CRead (v, V b, CRet (V v)), CLog (K SentinelStoppedWalk, CRet (K 9))))

        fun k ->
            CSignal (
                s,
                K 0,
                CMemo (
                    a,
                    false,
                    CmpEq,
                    CRead (x, V s, CRet (V x)),
                    CMemo (
                        b,
                        false,
                        CmpEq,
                        CRead (y, V s, CLog (V y, CRet (V y))),
                        CMemo (c, false, CmpEq, reader, CEffect (e, CRead (w, V c, CLog (V w, this.Nop)), CWrite (V s, K moved, k)))
                    )
                )
            )

    /// An effect's cleanup writes the source the effect reads. The core's `inCleanup` guard suppresses the mark, and the
    /// body reads the written value.
    member private this.CleanupWritesOwnSource(tail: Random) : Cmd -> Cmd =
        let s, e, x = this.Fresh (), this.Fresh (), this.Fresh ()
        // Above 1, so that `x + 1` from the first run differs from the written value and the guard has a mark to suppress.
        let moved = tail.Next (2, 5)

        let written = if tail.Next 2 = 0 then Add (V x, K 1) else K 20

        fun k ->
            CSignal (
                s,
                K 0,
                CEffect (
                    e,
                    CRead (x, V s, CLog (V x, CCleanup (CLog (K SentinelCleanupOwnSource, CWrite (V s, written, this.Nop)), this.Nop))),
                    CWrite (V s, K moved, k)
                )
            )

    /// An owning memo's cleanup writes a source, reads the memo and writes again. The nested run leaves the memo dirty and
    /// `RecomputeScoped` goes round again, and the guard ends it at the next discharge. Through a memo over the source, the
    /// last write leaves the memo in `Check` instead, and `RecomputeScoped` stops there with the nested run's value.
    member private this.RewritingCleanup(tail: Random) : Cmd -> Cmd =
        let s, m, over = this.Fresh (), this.Fresh (), this.Fresh ()

        let x, y, z, a, b =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let c, d, f, g = this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()
        let h, j = this.Fresh (), this.Fresh ()
        let throughMemo = tail.Next 2 = 0

        let sentinel =
            if throughMemo then
                SentinelRewritingThroughMemo
            else
                SentinelRewritingCleanup

        let rewrite =
            CLog (K sentinel, CWrite (V s, K 2, CRead (y, V m, CLog (V y, CWrite (V s, K 3, this.Nop)))))

        let body =
            CRead (x, V (if throughMemo then over else s), CLog (V x, CCleanup (CIf (Eq (V x, K 1), rewrite, this.Nop), CRet (V x))))

        let reads k =
            CMemo (
                m,
                true,
                CmpEq,
                body,
                CTryRead (a, b, V m, CWrite (V s, K 1, CTryRead (c, d, V m, CWrite (V s, K 4, CTryRead (h, j, V m, CTryRead (f, g, V s, k))))))
            )

        fun k ->
            CSignal (
                s,
                K 0,
                if throughMemo then
                    CMemo (over, false, CmpEq, CRead (z, V s, CRet (V z)), reads k)
                else
                    reads k
            )

    /// A host read pulls an owning memo, whose discharge wakes an effect. The pull's end owes the flush, so the effect runs
    /// before the read returns.
    member private this.PullWakesEffect() : Cmd -> Cmd =
        let s, t, m, e = this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let x, y, a, b, c, d =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let woken =
            CRead (y, V t, CIf (Lt (K 0, V y), CLog (K SentinelPullWakesEffect, CLog (V y, this.Nop)), CLog (V y, this.Nop)))

        fun k ->
            CSignal (
                s,
                K 0,
                CSignal (
                    t,
                    K 0,
                    CMemo (
                        m,
                        true,
                        CmpEq,
                        CRead (x, V s, CCleanup (CWrite (V t, Add (V x, K 1), this.Nop), CRet (V x))),
                        CEffect (e, woken, CTryRead (a, b, V m, CWrite (V s, K 1, CTryRead (c, d, V m, CLog (K 99, k)))))
                    )
                )
            )

    /// A write sets a memo's divisor to zero, once. Its readers see the failure of one raise, through a try read and through
    /// a read that fails an effect, and a later write recovers the memo.
    member private this.ZeroDivisor(tail: Random) : Cmd -> Cmd =
        let s, m, e, g = this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let x, y, ok, z, a, b =
            this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh (), this.Fresh ()

        let start = tail.Next (1, 5)

        let recovery k =
            if tail.Next 2 = 0 then CWrite (V s, K start, k) else k

        fun k ->
            CSignal (
                s,
                K start,
                CMemo (
                    m,
                    false,
                    CmpEq,
                    CRead (x, V s, CRet (Div (K 12, V x))),
                    CEffect (
                        e,
                        CTryRead (y, ok, V m, CLog (Add (V y, Mul (V ok, K 100)), this.Nop)),
                        CEffect (g, CRead (z, V m, CLog (V z, this.Nop)), CWrite (V s, K 0, CTryRead (a, b, V m, recovery k)))
                    )
                )
            )

    /// One top-level operation over the nodes in scope.
    member private this.TopOp(sc: Scope) : (Cmd -> Cmd) =
        let writable = sc.Signals |> List.map fst
        let memos = sc.Memos |> List.map fst

        match rng.Next 100 with
        | r when r < 45 && not writable.IsEmpty ->
            let t = this.Pick writable
            let v = this.SmallInt ()
            fun k -> CWrite (V t, K v, k)
        | r when r < 65 && not writable.IsEmpty ->
            let writes =
                [ for _ in 1 .. rng.Next (2, 4) -> this.Pick writable, this.SmallInt () ]

            let b =
                writes
                |> List.foldBack (fun (t, v) k -> CWrite (V t, K v, k))
                <| this.Nop

            fun k -> CBatch (b, k)
        | r when r < 85 && not memos.IsEmpty ->
            let t = this.Pick memos
            let x, ok = this.Fresh (), this.Fresh ()
            fun k -> CTryRead (x, ok, V t, k)
        | r when r < 90 && not writable.IsEmpty ->
            let t = this.Pick writable
            let x = this.Fresh ()
            fun k -> CReadUntracked (x, V t, k)
        | r when r < 94 && not sc.Computations.IsEmpty ->
            let t = this.Pick sc.Computations
            fun k -> CDispose (V t, k)
        | _ ->
            let b = this.Body TopSite sc 3
            fun k -> CBatch (b, k)

    member this.Program(tail: Random) : Cmd =
        let mutable sc = { emptyScope with Reads = ref -1 }
        let steps = ResizeArray<Cmd -> Cmd>()

        let add (step, sc': Scope) =
            steps.Add step
            sc <- sc'

        for _ in 1 .. rng.Next (2, 5) do
            let step, sc', _, _ = this.NewSignal sc
            add (step, sc')

        for _ in 1 .. rng.Next (2, 7) do
            match rng.Next 100 with
            | r when r < 28 -> add (this.NewMemo sc)
            | r when r < 56 -> add (this.NewEffect sc)
            | r when r < 62 -> add (this.Diamond sc)
            | r when r < 67 -> add (this.Cutoff sc)
            | r when r < 72 -> add (this.SelfWritingMemo sc)
            | r when r < 77 -> add (this.SelfMarkingCleanup sc)
            | r when r < 81 -> add (this.NestedSelfRun sc)
            | r when r < 86 -> add (this.LateChild sc)
            | r when r < 90 -> add (this.ClimbingEffect sc)
            | r when r < 95 -> add (this.FailingMemo sc)
            | r when r < 98 -> add (this.FailingCleanup sc)
            | _ -> add (this.PreviousValue sc)

        for _ in 1 .. rng.Next (2, 8) do
            steps.Add (this.TopOp sc)

        for t in sc.Memos |> List.truncate 3 do
            let x, ok = this.Fresh (), this.Fresh ()
            steps.Add (fun k -> CTryRead (x, ok, V (fst t), k))

        if tail.Next 100 < 30 then
            steps.Add (this.StoppedWalk tail)

        if tail.Next 100 < 30 then
            steps.Add (this.CleanupWritesOwnSource tail)

        if tail.Next 100 < 30 then
            steps.Add (this.RewritingCleanup tail)

        if tail.Next 100 < 30 then
            steps.Add (this.PullWakesEffect ())

        if tail.Next 100 < 15 then
            steps.Add (this.ZeroDivisor tail)

        Seq.foldBack (fun step k -> step k) steps (CRet (K 0))

// ---------------------------------------------------------------------------------------------------------------------
// The interpreter over real objects
// ---------------------------------------------------------------------------------------------------------------------

[<Literal>]
let StepBudget = 400000

[<Literal>]
let DepthBudget = 500

/// The stack of the thread running programs, in bytes. A bound on how long a stack overflow takes to die, and well above
/// what `DepthBudget` nested bodies need.
[<Literal>]
let StackBytes = 67108864

/// The wall-clock limit of a program, in milliseconds. A failed step ends in a node's status and not in the flush
/// running it, so a flush can loop past the step budget.
[<Literal>]
let WatchdogMilliseconds = 20000

let private bump (coverage: Dictionary<string, int>) (name: string) =
    match coverage.TryGetValue name with
    | true, n -> coverage[name] <- n + 1
    | _ -> coverage[name] <- 1

let private memoCreate =
    lazy
        (typeof<Memo<int>>
            .GetMethod(
                "CreateWithComparer",
                BindingFlags.Static
                ||| BindingFlags.NonPublic
                ||| BindingFlags.Public
            ))

let private scopeMode (owning: bool) =
    let t = typeof<Graph>.Assembly.GetType "Ranvier.ScopeMode"
    let name = if owning then "Owning" else "Pure"
    Enum.Parse (t, name)

type Interpreter(c: Ctx, coverage: Dictionary<string, int>) =
    let g = c.Graph
    let nodes = Dictionary<int, obj>()
    let mutable lastId = 0
    let mutable steps = 0
    let mutable depth = 0
    let mutable runaway = false
    let mutable stuck = false
    let mutable zeroDivisors = 0

    let tick () =
        if stuck || runaway then
            raise Abandoned

        steps <- steps + 1

        if steps > StepBudget then
            runaway <- true
            raise Abandoned

    let stick () : 'a =
        stuck <- true
        raise Abandoned

    let isDisposed (node: obj) =
        privateField node "disposed" :?> bool

    /// A zero divisor raises a new exception each time, as `/` does, with the identity the trace reports.
    let rec eval (env: Map<int, int>) (e: Expr) : int =
        let inline bin (f: int -> int -> int) a b =
            let u = eval env a
            let v = eval env b
            f u v

        match e with
        | K z -> z
        | V x ->
            match env.TryFind x with
            | Some v -> v
            | None -> stick ()
        | Prev ->
            match env.TryFind PrevVar with
            | Some v -> v
            | None -> stick ()
        | Add (a, b) -> bin (fun u v -> Checked.(+) u v) a b
        | Sub (a, b) -> bin (fun u v -> Checked.(-) u v) a b
        | Mul (a, b) -> bin (fun u v -> Checked.(*) u v) a b
        | Div (a, b) ->
            bin
                (fun u v ->
                    if v = 0 then
                        zeroDivisors <- zeroDivisors + 1
                        raise (Boom -1)

                    u / v)
                a
                b
        | Lt (a, b) -> bin (fun u v -> if u < v then 1 else 0) a b
        | Eq (a, b) -> bin (fun u v -> if u = v then 1 else 0) a b

    let lookup (env: Map<int, int>) (n: Expr) =
        let t = eval env n

        match nodes.TryGetValue t with
        | true, node -> t, node
        | _ -> stick ()

    let readNode (node: obj) : int =
        match node with
        | :? Signal<int> as s -> s.Value
        | :? Memo<int> as m -> m.Value
        | _ -> stick ()

    /// The comparer of a memo's cutoff, whose events the coverage counts.
    let comparer (q: Comparer) : IEqualityComparer<int> =
        match q with
        | CmpEq -> EqualityComparer<int>.Default
        | CmpNever ->
            { new IEqualityComparer<int> with
                member _.Equals(_, _) =
                    bump coverage "comparer never consulted"
                    false

                member _.GetHashCode v = v
            }
        | CmpThrows f ->
            { new IEqualityComparer<int> with
                member _.Equals(_, _) =
                    bump coverage "comparer throws consulted"
                    c.Throw f

                member _.GetHashCode v = v
            }

    let entered (body: unit -> 'a) : 'a =
        tick ()
        depth <- depth + 1

        try
            if depth > DepthBudget then
                runaway <- true
                raise Abandoned

            body ()
        finally
            depth <- depth - 1

    /// Counts a node or cleanup created under a host already disposed.
    let noteLateChild (label: int) =
        match nodes.TryGetValue label with
        | true, host when isDisposed host -> bump coverage "late child attached"
        | _ -> ()

    /// Runs `c`, with `label` the node its events name and `top` set outside every computation and cleanup.
    let rec exec (env: Map<int, int>) (label: int) (top: bool) (cmd: Cmd) : int =
        tick ()

        match cmd with
        | CRet e -> eval env e
        | CThrow f ->
            bump coverage "failing body"
            c.Throw f
        | CLet (x, e, k) -> exec (env.Add (x, eval env e)) label top k
        | CIf (e, a, b) ->
            let taken = if eval env e = 0 then b else a

            match taken with
            | CRead _
            | CTryRead _
            | CReadUntracked _ -> bump coverage "conditional read"
            | _ -> ()

            exec env label top taken
        | CRead (x, n, k) ->
            let t, node = lookup env n

            let v =
                if top then
                    c.HostRead t (fun () -> readNode node)
                else
                    readNode node

            exec (env.Add (x, v)) label top k
        | CTryRead (x, ok, n, k) ->
            let t, node = lookup env n

            let v, flag =
                try
                    let v =
                        if top then
                            c.HostRead t (fun () -> readNode node)
                        else
                            readNode node

                    v, 1
                with Boom f ->
                    bump coverage "failure caught by a try read"
                    f, 0

            exec (env.Add(x, v).Add(ok, flag)) label top k
        | CReadUntracked (x, n, k) ->
            let t, node = lookup env n

            let read () =
                g.Untrack (Func<int>(fun () -> readNode node))

            let v = if top then c.HostRead t read else read ()
            exec (env.Add (x, v)) label top k
        | CWrite (n, e, k) ->
            let _, node = lookup env n
            let v = eval env e

            match node with
            | :? Signal<int> as s ->
                if
                    label <> 0
                    && not top
                    && nodes.ContainsKey label
                    && (nodes[label] :? Effect)
                then
                    bump coverage "write from an effect"

                s.Value <- v
            | _ -> stick ()

            exec env label top k
        | CSignal (x, e, k) ->
            let v = eval env e
            lastId <- lastId + 1
            let id = lastId
            nodes[id] <- c.Signal id v
            exec (env.Add (x, id)) label top k
        | CMemo (x, owning, q, b, k) ->
            noteLateChild label
            lastId <- lastId + 1
            let id = lastId
            let env' = env.Add (x, id)

            let body (prev: int voption) =
                entered (fun () ->
                    bump coverage (if owning then "owning memo run" else "pure memo run")

                    let bodyEnv =
                        match prev with
                        | ValueSome v -> env'.Add (PrevVar, v)
                        | ValueNone -> env'.Remove PrevVar

                    exec bodyEnv id false b)

            let guarded (prev: int voption) =
                let v = c.Guarded (fun () -> body prev)

                if prev = ValueSome v then
                    bump coverage "memo recomputed to its previous value"

                v

            let memo =
                match q with
                | CmpEq -> Memo<int>(g, Func<int voption, int>(guarded), owning)
                | _ -> memoCreate.Value.Invoke (null, [| box g; box guarded; scopeMode owning; box (comparer q) |]) :?> Memo<int>

            nodes[id] <- c.Labelled id memo
            exec env' label top k
        | CEffect (x, b, k) ->
            noteLateChild label
            lastId <- lastId + 1
            let id = lastId
            let env' = env.Add (x, id)

            let effect =
                c.Effect id (fun () ->
                    // The constructor runs the first body unless something defers it, before it returns the effect.
                    if not (nodes.ContainsKey id) then
                        nodes[id] <- internalProp g "CurrentComputation"

                    entered (fun () -> exec env' id false b |> ignore))

            nodes[id] <- effect
            exec env' label top k
        | CCleanup (b, k) ->
            noteLateChild label

            c.Cleanup label (fun () ->
                bump coverage "cleanup run"
                entered (fun () -> exec env label false b |> ignore))

            exec env label top k
        | CDispose (n, k) ->
            let t, node = lookup env n

            match node with
            | :? Effect as e -> c.Dispose t (fun () -> e.Dispose ()) (box e)
            | :? Memo<int> as m -> c.Dispose t (fun () -> m.Dispose ()) (box m)
            | _ -> stick ()

            bump coverage "dispose"
            exec env label top k
        | CBatch (b, k) ->
            bump coverage "batch"
            c.Batch (fun () -> exec env label top b |> ignore)
            exec env label top k
        | CLog (e, k) ->
            match e with
            | K v when sentinelNames.ContainsKey v -> bump coverage sentinelNames[v]
            | _ -> ()

            c.Log label (eval env e)
            exec env label top k

    member _.Run(program: Cmd) =
        exec Map.empty 0 true program |> ignore

    /// The closing of `observed` in theories/Extract.v: a host read of each memo existing now, then `Graph.Flush`.
    member _.Close() =
        let memos =
            [
                for id in 1..lastId do
                    if nodes[id] :? Memo<int> then
                        id
            ]

        for id in memos do
            exec Map.empty 0 true (CTryRead (0, 1, K id, CRet (K 0)))
            |> ignore

        g.Flush ()

    /// The state the differential check compares, as `render_view` in driver.ml writes the model's.
    member _.View() =
        let bit (b: bool) =
            if b then 1 else 0

        let boolField (node: obj) (name: string) =
            privateField node name :?> bool

        let intField (node: obj) (name: string) =
            privateField node name :?> int

        let sourceCount (node: obj) =
            internalProp (privateField node "sources") "Count" :?> int

        let failureOf (node: obj) =
            match privateField node "failure" with
            | null -> "-"
            | failure ->
                match internalProp failure "Error" with
                | :? exn as e ->
                    match e with
                    | Boom f -> string f
                    | other -> other.GetType().Name
                | _ -> "?"

        let common (node: obj) =
            let status = privateField node "status"
            let freshness = privateField node "freshness"
            $"st={status} fail={failureOf node} fr={freshness}"

        let observers (node: obj) =
            internalProp node "ObserverCount"

        let row (id: int) =
            match nodes[id] with
            | :? Signal<int> as s -> $"S{id} v={s.Peek} o={observers s}"
            | :? Memo<int> as m ->
                let value = privateField m "value"
                let unannounced = bit (boolField m "unannounced")
                let runs = intField m "runs"
                let disposed = bit (boolField m "disposed")
                $"M{id} v={value} {common m} u={unannounced} r={runs} d={disposed} s={sourceCount m} o={observers m}"
            | node ->
                let runs = intField node "runs"
                let disposed = bit (boolField node "disposed")
                let queued = bit (boolField node "queued")
                $"E{id} {common node} r={runs} d={disposed} q={queued} s={sourceCount node}"

        [
            for id in 1..lastId do
                row id
            $"queue={c.QueueLength} errors={g.Root.ErrorCount}"
        ]
        |> String.concat ", "

    member _.Runaway = runaway
    member _.Stuck = stuck
    member _.ZeroDivisors = zeroDivisors

    /// Counts the memos that ended a run stale, as a self-marking run does.
    member _.NoteSelfMarked() =
        for node in nodes.Values do
            match node with
            | :? Memo<int> as m when (privateField m "unannounced" :?> bool) -> bump coverage "memo left stale by its own run"
            | _ -> ()

let private renderOutcome (o: Outcome) =
    match o with
    | OkV v -> $"(Ok {v})"
    | FailV f -> $"(Fail {f})"

let private render (e: Ev) =
    match e with
    | Run n -> $"EvRun {n}"
    | Log (n, v) -> $"EvLog {n} {v}"
    | Cleanup n -> $"EvCleanup {n}"
    | FailE (n, f) -> $"EvFail {n} {f}"
    | Disposed n -> $"EvDispose {n}"
    | HostRead (n, o) -> $"EvHostRead {n} {renderOutcome o}"

/// The columns of a program after its index: status, trace, end state, closed state.
let runProgram (coverage: Dictionary<string, int>) (program: Cmd) : string list =
    let c = Ctx ()
    let interpreter = Interpreter (c, coverage)

    let guarded (step: unit -> unit) =
        try
            step ()
            None
        with
        | Boom _
        | Abandoned -> None
        | ex -> Some $"{ex.GetType().Name}: {ex.Message}"

    let usable () =
        not interpreter.Stuck && not interpreter.Runaway

    let ended = ref ""
    let closed = ref ""

    let escaped =
        guarded (fun () -> interpreter.Run program)
        |> Option.orElseWith (fun () ->
            guarded (fun () ->
                if usable () then
                    ended.Value <- interpreter.View ()
                    interpreter.Close ()

                    if usable () then
                        closed.Value <- interpreter.View ()))

    // A fresh failure identity moves a memo on every run where the model's one identity does not, so a self-reading memo
    // with a zero divisor runs away on the F# side alone. The exclusion comes before the budget.
    match escaped, c.Failures with
    | Some reason, _ -> [ "harness"; reason; ""; "" ]
    | None, (_ :: _ as failures) -> [ "harness"; String.concat " | " failures; ""; "" ]
    | None, [] ->
        if interpreter.Stuck then
            [ "stuck"; ""; ""; "" ]
        elif interpreter.ZeroDivisors > 1 then
            [ "excluded"; ""; ""; "" ]
        elif interpreter.Runaway then
            [ "budget"; ""; ""; "" ]
        else
            interpreter.NoteSelfMarked ()

            if interpreter.ZeroDivisors = 1 then
                bump coverage "single zero divisor"

            [
                "ok"
                c.Events |> List.map render |> String.concat "; "
                ended.Value
                closed.Value
            ]

/// Each shape a run has to reach in the programs it compares, with the count required of 2000 programs. Each floor is at
/// most half the lowest count measured over seeds 20001 to 20030.
let required =
    [
        "batch", 2000
        "cleanup run", 2000
        "comparer never consulted", 39
        "comparer throws consulted", 33
        "conditional read", 1200
        "dispose", 500
        "failing body", 470
        "failure caught by a try read", 1080
        "late child attached", 180
        "memo left stale by its own run", 32
        "memo recomputed to its previous value", 1120
        "owning memo run", 2750
        "pure memo run", 7100
        "single zero divisor", 140
        "write from an effect", 770
        "template diamond", 370
        "template check walk stops at a moved source", 285
        "template effect cleanup writes its own source", 285
        "template owning memo cleanup writes, reads and writes", 135
        "template owning memo cleanup writes through a memo", 135
        "template host pull wakes an effect", 275
        "template self-marking cleanup", 260
    ]

/// Runs the programs from `first` on, on a thread the watchdog can outlive.
let private runFrom (directory: string) (first: int) =
    let programs = File.ReadAllLines (Path.Combine (directory, "programs.txt"))

    let currentFile = Path.Combine (directory, "current.txt")

    use output =
        new StreamWriter (Path.Combine (directory, "fsharp.txt"), true, UTF8Encoding false, AutoFlush = true)

    let mutable current = -1
    let clock = Stopwatch ()

    let worker =
        Thread (
            (fun () ->
                for i in first .. programs.Length - 1 do
                    let program = readCmd (parseSexp programs[i])
                    File.WriteAllText (currentFile, string i)
                    current <- i
                    clock.Restart ()
                    let coverage = Dictionary<string, int>()
                    let columns = runProgram coverage program

                    let counted =
                        coverage
                        |> Seq.map (fun kv -> $"{kv.Key}={kv.Value}")
                        |> String.concat ";"

                    output.Write (
                        String.concat "\t" [ string i; yield! columns; counted ]
                        + "\n"
                    )

                File.Delete currentFile),
            StackBytes
        )

    worker.IsBackground <- true
    worker.Start ()

    while not (worker.Join 200) do
        if clock.ElapsedMilliseconds > int64 WatchdogMilliseconds then
            eprintfn "watchdog: program %d passed %d ms" current WatchdogMilliseconds
            Process.GetCurrentProcess().Kill()

match fsi.CommandLineArgs |> Array.toList |> List.tail with
| [ "generate"; count; seed; directory ] ->
    let count, seed = int count, int seed
    let mutable dropped = 0

    let programs =
        [|
            for i in 0 .. count - 1 do
                let generator = Generator (Random (seed * 100003 + i))
                let text = printCmd (generator.Program (Random (seed * 100019 + i)))
                dropped <- dropped + generator.PureScopeDraws
                text
        |]

    File.WriteAllText (Path.Combine (directory, "programs.txt"), (String.concat "\n" programs) + "\n")

    File.WriteAllText (
        Path.Combine (directory, "stats.txt"),
        $"excluded by construction: {dropped} draws of a node or cleanup in a pure memo's body, dropped\n"
    )

    File.WriteAllText (
        Path.Combine (directory, "floors.txt"),
        required
        |> List.map (fun (name, floor) -> $"{name}\t{floor}\n")
        |> String.concat ""
    )

    runFrom directory 0
| [ "run"; directory; first ] -> runFrom directory (int first)
| [ "replay"; file; directory ] ->
    File.WriteAllText (Path.Combine (directory, "programs.txt"), (File.ReadAllText file).Replace("\r\n", "\n"))
    runFrom directory 0
| _ ->
    eprintfn "usage: Generate.fsx generate <count> <seed> <directory> | run <directory> <first index> | replay <file> <directory>"
    exit 2
