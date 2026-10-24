/// <summary>The runtime shapes Arms.Match sites lower to: payload-direct under Fable, cells on .NET.</summary>
module internal Ranvier.View.Arms.Site

open Ranvier.View.Template
open Ranvier.View.Sugar

#if !FABLE_COMPILER
/// <summary>
/// One arm per body, built when the site is built. The same target keeps its arm and replaces its bound values;
/// another target remounts. A body's instance is the enclosing instance.
/// </summary>
let inline create (cells: Cells.Cells) (read: 'inst -> 'u) (select: 'u -> int * obj array) (bodies: TemplateView<'inst> array) : TemplateView<'inst> =
    let definitions =
        bodies
        |> Array.mapi (fun target body ->
            Component.define
                (fun setup (current: unit -> 'inst * obj array) ->
                    let props = current ()
                    let instance = fst props
                    Cells.enter cells setup (box instance) target (fun () -> snd (current ())) (snd props)
                    instance)
                (fun _ -> body))

    matchWith id (fun instance ->
        let target, values = select (read instance)
        arm definitions[target] (instance, values))
#else
/// <summary>The value of an optional argument, or undefined.</summary>
[<Fable.Core.Emit("$0")>]
let erased (value: obj option) : obj =
    Fable.Core.Util.jsNative

/// <summary>
/// The values array of an arm of the site tagged <c>tag</c>, read from its values thunk; any other value raises AM006.
/// A rewritten reader opens with it.
/// </summary>
let values (thunk: obj) (tag: obj) : obj array =
    let values: obj array =
        Fable.Core.JsInterop.emitJsExpr thunk "typeof $0 === \"function\" ? $0() : null"

    if
        not (isNull values)
        && obj.ReferenceEquals (values[1], tag)
    then
        values
    else
        Errors.noArm ()

/// <summary>
/// One <c>armOf</c> definition per body, whose instance is the arm's values thunk. The selector's array
/// <c>[target, site tag, pattern variables..., matched value, instance]</c> is the selected arm's payload; the same
/// target keeps its arm and receives the new array, and another target remounts.
/// </summary>
let inline direct
    (read: 'inst -> 'u)
    (select: System.Func<'inst, 'u, obj array>)
    (bodies: TemplateView<unit -> obj array> array)
    : TemplateView<'inst> =
    let definitions = Array.map armOf bodies

    matchWith id (fun instance ->
        let values = select.Invoke (instance, read instance)
        arm (Fable.Core.JsInterop.emitJsExpr (definitions, values) "$0[$1[0]]") values)

/// <summary>
/// A helper view over the enclosing instance inside a payload-direct body: an inlined child whose instance is
/// <c>instance</c> of the arm's values thunk. The child keeps the helper's structural hash and server HTML.
/// </summary>
let splice (instance: (unit -> obj array) -> obj) (view: TemplateView<obj>) : TemplateView<unit -> obj array> =
    render (Component.define (fun _ (current: obj) -> current) (fun _ -> view)) instance

/// <summary>A concrete-instance helper's mount in an arm: the arm's values thunk until its cleanup, then its last values.</summary>
[<AllowNullLiteral>]
type private Held(thunk: unit -> obj array, target: int) =
    member _.Target = target
    member val Thunk = thunk with get, set
    member val Last: obj array = null with get, set

/// <summary>The live mounts of an instance at a site, and each target's last withdrawn mount.</summary>
[<AllowNullLiteral>]
type private HeldSlots() =
    member val Live = ResizeArray<Held>()
    member val Withdrawn = ResizeArray<Held>()

[<Fable.Core.Emit("new WeakMap()")>]
let private weakMap () : obj =
    Fable.Core.Util.jsNative

[<Fable.Core.Emit("$0.get($1)")>]
let private weakGet (map: obj) (key: obj) : obj =
    Fable.Core.Util.jsNative

[<Fable.Core.Emit("$0.set($1, $2)")>]
let private weakSet (map: obj) (key: obj) (value: obj) : unit =
    Fable.Core.Util.jsNative

// Site tag to (instance to slots); created by the first concrete-instance helper mount.
let mutable private heldTable: obj = null

let private tryHeld (tag: obj) (instance: obj) : HeldSlots =
    if isNull heldTable then
        null
    else
        match weakGet heldTable tag with
        | null -> null
        | instances -> unbox<HeldSlots>(weakGet instances instance)

let private heldOf (tag: obj) (instance: obj) : HeldSlots =
    if isNull heldTable then
        heldTable <- weakMap ()

    let instances =
        match weakGet heldTable tag with
        | null ->
            let created = weakMap ()
            weakSet heldTable tag created
            created
        | existing -> existing

    match unbox<HeldSlots>(weakGet instances instance) with
    | null ->
        let created = HeldSlots ()
        weakSet instances instance created
        created
    | existing -> existing

let private find (entries: ResizeArray<Held>) (target: int) : Held =
    let mutable found = null
    let mutable index = 0

    while isNull found && index < entries.Count do
        if entries[index].Target = target then
            found <- entries[index]

        index <- index + 1

    found

/// <summary>
/// A helper view over the concrete instance type inside the arm of <c>target</c> at the site tagged <c>tag</c>: an inlined
/// child whose instance is the element at <c>last</c> of the arm's values. Each mount serves <c>heldValues</c> for the
/// instance until its cleanup, then serves its last values until another mount of the target is withdrawn.
/// </summary>
let spliceHeld (tag: obj) (target: int) (last: int) (view: TemplateView<obj>) : TemplateView<unit -> obj array> =
    render
        (Component.define
            (fun setup (thunk: unit -> obj array) ->
                let instance = (values (box thunk) tag)[last]
                let slots = heldOf tag instance
                let entry = Held (thunk, target)
                slots.Live.Add entry

                setup.OnCleanup (fun () ->
                    entry.Last <- values (box entry.Thunk) tag
                    entry.Thunk <- Unchecked.defaultof<_>

                    slots.Live.RemoveAll (fun other -> obj.ReferenceEquals (other, entry))
                    |> ignore

                    slots.Withdrawn.RemoveAll (fun other -> other.Target = target)
                    |> ignore

                    slots.Withdrawn.Add entry)

                instance)
            (fun _ -> view))
        id

/// <summary>
/// The values of the arm of <c>target</c> at the site tagged <c>tag</c> for an instance under a concrete-instance helper:
/// the first live mount's values, or else the last values of the target's last withdrawn mount; with neither, raises
/// AM006.
/// </summary>
let heldValues (instance: obj) (tag: obj) (target: int) : obj array =
    match tryHeld tag instance with
    | null -> Errors.noArm ()
    | slots ->
        match find slots.Live target with
        | null ->
            match find slots.Withdrawn target with
            | null -> Errors.noArm ()
            | held -> held.Last
        | live -> values (box live.Thunk) tag
#endif
