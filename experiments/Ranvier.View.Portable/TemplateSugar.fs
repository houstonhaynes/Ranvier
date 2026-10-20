namespace Ranvier.View.Foundation.Sugar

open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template

/// <summary>Opt-in template sugar: element computation expressions and control flow with inline definitions.</summary>
[<AutoOpen>]
module Builders =
    /// <summary>An element's children, built with its template; <c>Run</c> returns the element.</summary>
    /// <remarks>
    /// A string yields static text, an <c>'inst -> string</c> a text slot and a <c>Source</c> a direct text slot, as the
    /// three <c>t.text</c> overloads do. A yielded lambda infers its parameter type from its own body; write
    /// <c>t.text</c> when the body needs the instance type.
    /// </remarks>
    [<Sealed>]
    type ElementBuilder<'native, 'inst> internal (tag: Tag, parts: Part<'native, 'inst> array) =
        member _.Yield(view: TemplateView<'inst>) : TemplateView<'inst> array =
            [| view |]

        member _.Yield(value: string) : TemplateView<'inst> array =
            [| TemplateParts.text value |]

        member _.Yield(read: 'inst -> string) : TemplateView<'inst> array =
            [| TemplateParts.readText read |]

        member _.Yield(source: Source<'inst, string>) : TemplateView<'inst> array =
            [| TemplateParts.sourceText source |]

        member _.YieldFrom(views: TemplateView<'inst> array) : TemplateView<'inst> array = views
        member _.Zero() : TemplateView<'inst> array = [||]

        member _.Combine(first: TemplateView<'inst> array, rest: TemplateView<'inst> array) : TemplateView<'inst> array =
            Array.append first rest

        member _.Delay(body: unit -> TemplateView<'inst> array) : TemplateView<'inst> array = body ()

        /// <summary>Repeats the body for each item while the template is built; the result is static structure.</summary>
        member _.For(items: seq<'item>, body: 'item -> TemplateView<'inst> array) : TemplateView<'inst> array =
            items |> Seq.collect body |> Array.ofSeq

        member _.Run(children: TemplateView<'inst> array) : TemplateView<'inst> =
            TemplateParts.element tag parts children

    [<Sealed>]
    type private EmptyArm private () =
        static let gate = obj ()
        static let mutable shared: Component<unit -> unit> = Unchecked.defaultof<_>

        static member Get() =
            // Reads shared without the lock; null re-checks under the lock.
            if isNull (box shared) then
#if FABLE_COMPILER
                shared <- Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||])
#else
                lock gate (fun () ->
                    if isNull (box shared) then
                        shared <- Component.define (fun _ (_: unit -> unit) -> ()) (fun t -> t.fragment [||]))
#endif
            shared

    /// <summary>The zero-root arm every <c>show</c> selects while its condition is false, created on first use.</summary>
    let internal emptyArm () : Component<unit -> unit> =
        EmptyArm.Get ()

    type TemplateBuilder<'inst> with
        /// <summary>An arm definition whose instance is its props.</summary>
        member _.armOf(build: TemplateBuilder<unit -> 'payload> -> TemplateView<unit -> 'payload>) : Component<unit -> 'payload> =
            Component.define (fun _ payload -> payload) build

        /// <summary>One row per item; a row reads its current item, and items with equal keys keep their row.</summary>
        member t.forEach<'item, 'key when 'key: equality>
            (key: 'item -> 'key)
            (items: 'inst -> 'item array)
            (row: TemplateBuilder<unit -> 'item> -> TemplateView<unit -> 'item>)
            : TemplateView<'inst> =
            t.each key items (Component.define (fun _ (current: unit -> 'item) -> current) row)

        /// <summary>One row per item; a row's instance is the item it was created for, and items with equal keys keep their row.</summary>
        member t.forEachFixed<'item, 'key when 'key: equality>
            (key: 'item -> 'key)
            (items: 'inst -> 'item array)
            (row: TemplateBuilder<'item> -> TemplateView<'item>)
            : TemplateView<'inst> =
            t.each key items (Component.define (fun _ (current: unit -> 'item) -> current ()) row)

        /// <summary>Renders <c>content</c> while <c>condition</c> holds; a change to false runs its cleanups and removes its nodes.</summary>
        member t.show (condition: 'inst -> bool) (content: TemplateBuilder<'inst> -> TemplateView<'inst>) : TemplateView<'inst> =
            let shown = Component.define (fun _ (current: unit -> 'inst) -> current ()) content
            let hidden = emptyArm ()

            t.matchWith (fun instance -> if condition instance then ValueSome instance else ValueNone) (function
                | ValueSome instance -> t.arm shown instance
                | ValueNone -> t.arm hidden ())

        /// <summary>Renders <c>content</c> while <c>condition</c> holds and <c>fallback</c> otherwise; each change of the condition remounts.</summary>
        member t.showElse
            (condition: 'inst -> bool)
            (content: TemplateBuilder<'inst> -> TemplateView<'inst>)
            (fallback: TemplateBuilder<'inst> -> TemplateView<'inst>)
            : TemplateView<'inst> =
            let shown = Component.define (fun _ (current: unit -> 'inst) -> current ()) content
            let other = Component.define (fun _ (current: unit -> 'inst) -> current ()) fallback

            t.matchWith
                (fun instance ->
                    if condition instance then
                        Choice1Of2 instance
                    else
                        Choice2Of2 instance)
                (function
                 | Choice1Of2 instance -> t.arm shown instance
                 | Choice2Of2 instance -> t.arm other instance)
