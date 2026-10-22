/// <summary>Arm sites keyed by match target: the lowering shared by the active-pattern, plugin and quotation forms.</summary>
namespace Ranvier.Arms.Probe

open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

/// <summary>
/// Per-site storage of each mounted arm's payload reader, keyed by the enclosing instance. A reader inside an arm
/// reads its own arm's payload, which changes only while the arm stays selected.
/// </summary>
module Cells =
#if FABLE_COMPILER
    open Fable.Core

    [<Emit("new WeakMap()")>]
    let create () : obj = jsNative

    [<Emit("$0.set($1, $2)")>]
    let set (cells: obj) (instance: obj) (values: unit -> obj array) : unit = jsNative

    /// <summary>The bound values of the arm mounted for <c>instance</c>: the pattern variables, then the matched value.</summary>
    [<Emit("$0.get($1)()")>]
    let values (cells: obj) (instance: obj) : obj array = jsNative
#else
    open System.Runtime.CompilerServices

    let create () : obj =
        ConditionalWeakTable<obj, unit -> obj array>() :> obj

    let set (cells: obj) (instance: obj) (values: unit -> obj array) : unit =
        (cells :?> ConditionalWeakTable<obj, unit -> obj array>).AddOrUpdate(instance, values)

    /// <summary>The bound values of the arm mounted for <c>instance</c>: the pattern variables, then the matched value.</summary>
    let values (cells: obj) (instance: obj) : obj array =
        match (cells :?> ConditionalWeakTable<obj, unit -> obj array>).TryGetValue instance with
        | true, read -> read ()
        | _ -> failwith "no arm is mounted for this instance"
#endif

/// <summary>The current union value of an arm site, seen from an arm built for case <c>Tag</c>.</summary>
[<Sealed>]
type Case<'inst, 'u> internal (cells: obj, tag: int) =
    /// <summary>The case the arm is built for.</summary>
    member _.Tag = tag

    /// <summary>A reader of one payload field of the arm's current value.</summary>
    member _.Field(extract: 'u -> 'field) : 'inst -> 'field =
        fun instance -> extract (unbox<'u> (Cells.values cells (box instance)).[0])

module Lowered =
    /// <summary>
    /// One arm per body, built when the site is built. <c>select</c> maps the instance's value to a body index and
    /// its bound values; an index change remounts, and an equal index keeps the arm and replaces its bound values.
    /// Readers in a body receive the enclosing instance and read the bound values through <c>Cells.values cells</c>.
    /// </summary>
    let site (cells: obj) (read: 'inst -> 'u) (select: 'u -> int * obj array) (bodies: TemplateView<'inst> array) : TemplateView<'inst> =
        let definitions =
            bodies
            |> Array.map (fun body ->
                Component.define
                    (fun _ (current: unit -> 'inst * obj array) ->
                        let instance, _ = current ()
                        Cells.set cells (box instance) (fun () -> snd (current ()))
                        instance)
                    (fun _ -> body))

        matchWith id (fun instance ->
            let index, values = select (read instance)
            arm definitions[index] (instance, values))

    /// <summary>One arm per case of a union with <c>count</c> cases; <c>arms</c> runs once per case, at build time.</summary>
    let cases (read: 'inst -> 'u) (tag: 'u -> int) (count: int) (arms: Case<'inst, 'u> -> TemplateView<'inst>) : TemplateView<'inst> =
        let cells = Cells.create ()
        site cells read (fun value -> tag value, [| box value |]) (Array.init count (fun index -> arms (Case (cells, index))))
