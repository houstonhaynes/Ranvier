namespace Ranvier.View.Anime

open System
open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar

/// <summary>The instance signals a scroll-linked element writes; each is written while given.</summary>
[<NoEquality; NoComparison>]
type ScrollBinding =
    {
        /// <summary>The observer's progress between its enter and leave thresholds, in 0..1.</summary>
        Progress: Signal<float> voption
        /// <summary>Whether the element is between its thresholds.</summary>
        InView: Signal<bool> voption
    }

/// <summary>
/// The one animation an instance runs outside its parts, such as a spring a handler starts: playing another cancels the
/// running one, and the instance's disposal cancels it. A cancelled animation stops at its current values, its
/// callbacks muted.
/// </summary>
/// <remarks>Created by <c>Motion.slot</c>.</remarks>
[<AllowNullLiteral>]
type MotionSlot internal () =
    let mutable running: ITickable = null
    let mutable closed = false

    /// <summary>The latest animation played, or null before the first play and after a stop.</summary>
    member _.Running = running

    /// <summary>
    /// Whether the latest animation is live: played, and not yet completed or cancelled. A delayed or paused animation
    /// is live.
    /// </summary>
    /// <remarks>A WAAPI animation reports completion only, so it stays live after an outside cancel.</remarks>
    member _.Playing =
        not (isNull running)
        && not running.completed
        && not running.cancelled

    /// <summary>
    /// Cancels the running animation, then plays the one <c>start</c> creates while the instance is live; after its
    /// disposal the slot is closed and <c>Play</c> only cancels.
    /// </summary>
    /// <remarks>
    /// The slot holds the animation <c>start</c> returns; one that <c>start</c> plays through the same slot is
    /// cancelled.
    /// </remarks>
    member this.Play(start: unit -> #ITickable) =
        this.Stop ()

        if not closed then
            let next = start () :> ITickable

            if
                not (isNull running)
                && not (obj.ReferenceEquals (running, next))
            then
                running.cancel ()

            running <- next

    /// <summary>Cancels the running animation at its current values.</summary>
    member _.Stop() =
        if not (isNull running) then
            running.cancel ()
            running <- null

    member internal this.Close() =
        closed <- true
        this.Stop ()

/// <summary>The instance signals a draggable element shares with the rest of the instance.</summary>
[<NoEquality; NoComparison>]
type DragBinding =
    {
        /// <summary>The element's drag position in pixels, written by dragging and read to move the element.</summary>
        Position: Signal<struct (float * float)>
        /// <summary>True from grab to release.</summary>
        Dragging: Signal<bool> voption
        /// <summary>The slot whose running animation a grab cancels, such as the spring a release starts.</summary>
        Slot: MotionSlot voption
    }

/// <summary>
/// Template parts that animate elements with anime.js: lifecycle parts (<c>on</c>, <c>appear</c>, <c>enter</c>,
/// <c>exit</c>, <c>presence</c> and its presets, <c>flip</c>) and reactive parts (<c>follow</c>, <c>animateOn</c>,
/// <c>draw</c>, <c>morph</c>, <c>scroll</c>, <c>drag</c>, <c>split</c>), with <c>slot</c> for the animations a handler
/// starts and <c>flipFrom</c> for a row's FLIP start.
/// </summary>
/// <remarks>
/// Every part creates its anime objects once the element is placed and reverts them when the instance is disposed. On an
/// element whose exit, or whose ancestor's exit, follows the disposal, the part instead pauses its animations, so the exit
/// starts from the current values, and stops its observers and listeners. The server omits every part.
/// Under <c>Reactive.policy</c> <c>Reduced</c>, enter and flip do not play and an exit finishes at once.
/// </remarks>
[<RequireQualifiedAccess>]
module Motion =
#if FABLE_COMPILER
    // Sets, not WeakSets: flip scans their members, and every member leaves with its part's cleanup or its exit's end.
    [<Emit("new Set()")>]
    let private markSet () : obj = jsNative

    // Elements whose Motion exit runs.
    let private departing = markSet ()
    // Elements whose Motion enter started in a pass that has not yet been laid out.
    let private arriving = markSet ()
    // Elements whose Motion part animates them.
    let private animated = markSet ()
#else
    let private departing: obj = null
    let private arriving: obj = null
    let private animated: obj = null
#endif

    [<Emit("$0.size")>]
    let private sizeOf (set: obj) : int = jsNative

    /// <summary>The number of elements whose Motion exit runs.</summary>
    let internal departures () =
        sizeOf departing

    [<Emit("$0.add($1)")>]
    let private mark (set: obj) (element: obj) : unit = jsNative

    [<Emit("$0.delete($1)")>]
    let private forget (set: obj) (element: obj) : unit = jsNative

    [<Emit("(r => { if (typeof r.pause === 'function') r.pause(); else if (r.animations) for (const k in r.animations) r.animations[k].pause(); })($0)")>]
    let private pauseIfPlayable (revertible: obj) : unit = jsNative

    [<Emit("((t, k, f) => { const previous = t[k]; t[k] = self => { if (previous) previous(self); f(self); }; })($0, $1, $2)")>]
    let private chain (target: obj) (callback: string) (after: obj -> unit) : unit = jsNative

    [<Emit("Object.assign({}, $0, {[$1]: $2})")>]
    let private withKey (parameters: 'p) (key: string) (value: obj) : 'p = jsNative

    [<Emit("Object.assign({[$1]: $2}, $0)")>]
    let private withDefault (parameters: 'p) (key: string) (value: obj) : 'p = jsNative

    [<Emit("(e => { const top = e.offsetTop, left = e.offsetLeft, width = e.offsetWidth, height = e.offsetHeight, s = e.style; s.position = 'absolute'; s.top = top + 'px'; s.left = left + 'px'; s.width = width + 'px'; s.height = height + 'px'; s.margin = '0'; s.boxSizing = 'border-box'; })($0)")>]
    let private pinOutOfFlow (element: obj) : unit = jsNative

    [<Emit("Array.prototype.filter.call($0.children, c => !$1.has(c) && !$2.has(c))")>]
    let private liveChildren (root: obj) (leaving: obj) (entering: obj) : TS.Dom.Element array = jsNative

    // Takes the layout's visual properties (opacity, colours, borders, filters, clip paths, font size) that `parameters`
    // do not name out of its animated and recorded properties while an element under the root carries a Motion part, and
    // puts them back otherwise. anime 4.5.0 has no public way to remove them (AutoLayout's `properties` and
    // `recordedProperties` Sets).
    [<Emit("((l, p, r, a, b, c) => { if (!l.motionVisual) { const named = new Set([...(p.properties || []), ...Object.keys(p.enterFrom || {}), ...Object.keys(p.leaveTo || {}), ...Object.keys(p.swapAt || {})]); l.motionVisual = ['opacity', 'fontSize', 'color', 'backgroundColor', 'borderRadius', 'border', 'filter', 'clipPath'].filter(n => !named.has(n)); } const owned = [a, b, c].some(s => { for (const n of s) if (r.contains(n)) return true; return false; }); for (const n of l.motionVisual) { if (owned) { l.properties.delete(n); l.recordedProperties.delete(n); } else { l.properties.add(n); l.recordedProperties.add(n); } } })($0, $1, $2, $3, $4, $5)")>]
    let private leaveVisuals (layout: AutoLayout) (parameters: LayoutParams) (root: obj) (animated: obj) (departing: obj) (arriving: obj) : unit =
        jsNative

    [<Emit("(r => { for (const c of r.children) $1.delete(c); })($0)")>]
    let private laidOut (root: obj) (arriving: obj) : unit = jsNative

    [<Emit("$0.children = $1")>]
    let private measureChildren (layout: AutoLayout) (children: TS.Dom.Element array) : unit = jsNative

    [<Emit("$0.motionFrom = $0.getBoundingClientRect()")>]
    let private keepBox (element: TS.Dom.Element) : unit = jsNative

    // Moves the recorded box of each child carrying a `flipFrom` box to that box, and removes the box.
    [<Emit("((l, c) => { for (const e of c) { const r = e.motionFrom; if (r) { delete e.motionFrom; const p = l.oldState.getNode(e).properties; p.x += r.left - p.left; p.y += r.top - p.top; p.left = r.left; p.top = r.top; } } })($0, $1)")>]
    let private startFrom (layout: AutoLayout) (children: TS.Dom.Element array) : unit = jsNative

    // The parameters without the shared unit, which anime would read as a property, and with the property's own entry,
    // which takes the shared unit when it sets none.
    [<Emit("(({unit, ...p}, k) => { const e = p[k]; p[k] = Object.assign({unit}, typeof e === 'number' ? {duration: e} : e); return p; })($0, $1)")>]
    let private withUnit (parameters: AnimatableParams) (property: string) : AnimatableParams = jsNative

    [<Emit("$0.animations[$1].complete()")>]
    let private completeProperty (animatable: Animatable) (property: string) : unit = jsNative

    [<Emit("((el, f) => ({ get draw() { return f; }, set draw(v) { f = v; el.setAttribute('draw', '0 ' + v); } }))($0, $1)")>]
    let private strokeTarget (drawable: TS.Dom.Element) (initial: float) : obj = jsNative

    [<Emit("($0.tagName.toLowerCase() === 'path' ? 'd' : 'points')")>]
    let private shapeAttribute (element: obj) : string = jsNative

    [<Emit("((e, a, v) => { const shape = e.ownerDocument.createElementNS('http://www.w3.org/2000/svg', e.tagName.toLowerCase()); shape.setAttribute(a, v); return shape; })($0, $1, $2)")>]
    let private shapeLike (element: obj) (attribute: string) (shape: string) : TS.Dom.Element = jsNative

    [<Emit("$0.setAttribute($1, $2)")>]
    let private setShape (element: obj) (attribute: string) (shape: string) : unit = jsNative

    [<Emit("(d => { d.stop(); d.disable(); d.$target.classList.remove('is-disabled'); d.updateTicker.pause(); d.overshootTicker.pause(); d.resizeTicker.pause(); for (const k in d.animate.animations) d.animate.animations[k].pause(); d.resizeObserver.disconnect(); })($0)")>]
    let private freezeDraggable (draggable: Draggable) : unit = jsNative

    // Runs `after` once each of the draggable's position animations completes, after its last frame.
    [<Emit("((d, f) => { for (const k of [d.xProp, d.yProp]) { const a = d.animate.animations[k]; if (a) { const p = a.onComplete; a.onComplete = s => { if (p) p(s); f(s); }; } } })($0, $1)")>]
    let private whenPositioned (draggable: Draggable) (after: obj -> unit) : unit = jsNative

    [<Emit("(s => { clearTimeout(s.resizeTimeout); s.resizeObserver.disconnect(); })($0)")>]
    let private freezeSplitter (splitter: TextSplitter) : unit = jsNative

    [<Emit("((e, names) => { const kept = names.map(n => e.getAttribute(n)); return () => names.forEach((n, i) => kept[i] == null ? e.removeAttribute(n) : e.setAttribute(n, kept[i])); })($0, $1)")>]
    let private keepAttributes (element: obj) (names: string array) : unit -> unit = jsNative

    [<Emit("$0.scrollHeight")>]
    let private contentHeight (element: obj) : float = jsNative

    [<Emit("$0.style.overflow = $1")>]
    let private overflow (element: obj) (value: string) : unit = jsNative

    let inline private targetsOf (node: 'native) : Targets =
        Erased.cast node

    let inline private elementOf (node: 'native) : TS.Dom.Element =
        Erased.cast node

    let private nothing () = ()

    let private reduced () =
        match Api.untrack (fun () -> (Reactive.policy ()).Value) with
        | Reactive.Reduced -> true
        | Reactive.Full -> false

    // The cleanup of a part's anime object: paused while the element's exit follows, reverted otherwise.
    let private settle (context: MotionContext) (revertible: IRevertible) =
        let release () =
            if context.ExitPending () then
                pauseIfPlayable revertible
            else
                revertible.revert ()

        release

    // Marks the element as animated by a Motion part until `release` runs.
    let private owning (element: obj) (release: unit -> unit) =
        mark animated element

        let owned () =
            forget animated element
            release ()

        owned

    let private entering (play: 'inst -> TS.Dom.Element -> #ITickable) =
        Func<'inst, 'native, MotionContext, unit -> unit>(fun inst node context ->
            if context.Inserted && not (reduced ()) then
                let element = box node
                let tickable = play inst (elementOf node) :> ITickable
                mark arriving element
                let stop = settle context tickable

                let release () =
                    forget arriving element
                    stop ()

                owning element release
            else
                nothing)

    // Starts the exit, pinned out of flow when `pin`; the element leaves once the tickable completes, and at once under
    // Reduced. The returned cancel stops the tickable.
    let private departure (pin: bool) (play: 'inst -> TS.Dom.Element -> #ITickable) =
        Func<'inst, 'native, unit -> unit, unit -> unit>(fun inst node finish ->
            let element = box node

            let finished () =
                forget departing element
                finish ()

            if reduced () then
                finished ()
                nothing
            else
                mark departing element

                if pin then
                    pinOutOfFlow element

                let tickable =
                    try
                        play inst (elementOf node) :> ITickable
                    with _ ->
                        forget departing element
                        reraise ()

                chain tickable "onComplete" (fun _ -> finished ())

                let cancel () =
                    forget departing element
                    tickable.cancel ()

                cancel)

    /// <summary>Plays on every placement, hydration included; the cleanup reverts the animation.</summary>
    let on (play: 'inst -> TS.Dom.Element -> #IRevertible) : Part<'native, 'inst> =
        Hooks.motion (Func<_, _, _, _>(fun inst node context -> owning (box node) (settle context (play inst (elementOf node) :> IRevertible))))

    /// <summary>Plays on every placement of an element created on the client; an element adopted from server HTML stays as served.</summary>
    let appear (play: 'inst -> TS.Dom.Element -> #IRevertible) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                if context.Hydrated then
                    nothing
                else
                    owning (box node) (settle context (play inst (elementOf node) :> IRevertible)))
        )

    /// <summary>
    /// Plays when the element's row or arm is created by a region pass after the region's first; rows of the first pass,
    /// hydrated rows and rows nested inside an entering row stay still.
    /// </summary>
    let enter (play: 'inst -> TS.Dom.Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (entering play)

    /// <summary>
    /// On a row or arm root element: plays when the row is retired, and the element leaves when the tickable completes.
    /// </summary>
    /// <remarks>A forced removal, as when the mount is disposed, cancels the tickable and removes the element at once.</remarks>
    let exit (play: 'inst -> TS.Dom.Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.exit (departure false play)

    /// <summary>
    /// On a row or arm root element: <c>enter</c> and <c>exit</c> together. The leaving element is pinned out of flow,
    /// absolute at its current box, so later rows take its place while it plays.
    /// </summary>
    /// <remarks>The element's offset parent is the containing block of the pinned box; position the list's container.</remarks>
    let presence (enter: 'inst -> TS.Dom.Element -> #ITickable) (exit: 'inst -> TS.Dom.Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering enter), Hooks.exit (departure true exit))

    /// <summary>Presence that fades the element in over 240 ms and out over 180 ms.</summary>
    let fade<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) node -> Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 240, ease = Ease.outQuad)))
            (fun _ node -> Anime.animate (node, Anim (opacity = 0, duration = 180, ease = Ease.inQuad)))

    /// <summary>Presence that fades the element in while rising 12 px, and out while rising a further 12 px.</summary>
    let rise<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) node ->
                Anime.animate (node, Anim (y = Value.fromTo (12, 0), opacity = Value.fromTo (0, 1), duration = 240, ease = Ease.outQuad)))
            (fun _ node -> Anime.animate (node, Anim (y = -12, opacity = 0, duration = 180, ease = Ease.inQuad)))

    /// <summary>Presence that fades the element in from 92 % scale and out to it.</summary>
    let scale<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) node ->
                Anime.animate (node, Anim (scale = Value.fromTo (0.92, 1), opacity = Value.fromTo (0, 1), duration = 240, ease = Ease.outQuad)))
            (fun _ node -> Anime.animate (node, Anim (scale = 0.92, opacity = 0, duration = 180, ease = Ease.inQuad)))

    let private unfold (_: 'inst) (node: TS.Dom.Element) =
        let element = box node
        overflow element "hidden"

        Anime.animate (
            node,
            Anim (
                height = Value.fromTo (0, contentHeight element),
                duration = 240,
                ease = Ease.outQuad,
                onComplete =
                    fun animation ->
                        Utils.cleanInlineStyles animation
                        overflow element ""
            )
        )

    let private fold (_: 'inst) (node: TS.Dom.Element) =
        overflow (box node) "hidden"
        Anime.animate (node, Anim (height = 0, duration = 180, ease = Ease.inQuad))

    /// <summary>
    /// Presence that unfolds the element's height from 0 and folds it back to 0, in flow, with its overflow hidden while
    /// it plays.
    /// </summary>
    let collapse<'native, 'inst> : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering unfold), Hooks.exit (departure false fold))

    /// <summary>
    /// FLIP for the rows of every region under the element: anime's <c>createLayout</c> over the element's element
    /// children, recorded before each flush's region passes and animated once their nodes and commits are in place.
    /// </summary>
    /// <remarks>
    /// <c>parameters</c> apply to every animation; <c>children</c> is replaced. A row whose Motion exit runs, or whose Motion
    /// enter started in the pass, is left out of the pass's measurements. The layout's opacity, colours, borders, filters,
    /// clip paths and font size apply to a pass in which no element under the root carries a Motion part; in a pass where
    /// one does, they are left to the parts.
    /// </remarks>
    let flip (parameters: LayoutParams) : Part<'native, 'inst> =
        Hooks.reflow (fun _ node ->
            let root = box node
            let layout: AutoLayout ref = ref null
            let recorded = ref false

            Api.onCleanup (fun () ->
                if not (isNull layout.Value) then
                    layout.Value.revert ())

            {
                Before =
                    fun () ->
                        recorded.Value <- not (reduced ())

                        if recorded.Value then
                            let children = liveChildren root departing arriving

                            if isNull layout.Value then
                                layout.Value <- Anime.layout (elementOf node, withKey parameters "children" children)
                            else
                                measureChildren layout.Value children
                                leaveVisuals layout.Value parameters root animated departing arriving
                                layout.Value.record () |> ignore

                            startFrom layout.Value children
                After =
                    fun () ->
                        if recorded.Value then
                            recorded.Value <- false
                            measureChildren layout.Value (liveChildren root departing arriving)
                            leaveVisuals layout.Value parameters root animated departing arriving
                            layout.Value.animate () |> ignore
                            laidOut root arriving
            })

    /// <summary>
    /// Starts the element's next FLIP from its current visual box, drag transform included, instead of its recorded
    /// layout box: the next <c>flip</c> pass that measures the element as a row moves it from there to its new place.
    /// </summary>
    /// <remarks>
    /// Call before the change that moves the row, and before the drag position is reset, as for a card dropped into
    /// another lane. The FLIP keeps the recorded size; only the position moves. The box stays on the element until the
    /// next pass that measures it as a row; under the ambient graph's <c>Reactive.policy</c> <c>Reduced</c>, where flip
    /// stays still, the call returns at once.
    /// </remarks>
    let flipFrom (element: TS.Dom.Element) : unit =
        match Graph.TryCurrent with
        | ValueSome _ when reduced () -> ()
        | _ -> keepBox element

    /// <summary>
    /// A slot for the animations the current instance starts outside its parts, from a handler or an effect; call it in
    /// the instance's setup. The instance's disposal cancels the running animation and closes the slot.
    /// </summary>
    /// <remarks>Raises <c>InvalidOperationException</c> inside a pure body, as <c>Api.onCleanup</c> does.</remarks>
    let slot () : MotionSlot =
        let created = MotionSlot ()
        Api.onCleanup created.Close
        created

    /// <summary>
    /// Animates the element's <c>property</c> to <c>read</c>'s value whenever it changes, from wherever the property is;
    /// the first value is set at once.
    /// </summary>
    /// <remarks>
    /// <c>parameters</c> without an entry for <c>property</c> give it the shared defaults; the shared <c>unit</c> applies
    /// to <c>property</c> unless its entry sets its own <c>unit</c>.
    /// </remarks>
    let follow (property: string) (parameters: AnimatableParams) (read: 'inst -> float) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let animatable = Anime.animatable (targetsOf node, withUnit parameters property)

                let first = ref true

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    animatable.set (property, value)

                    if first.Value then
                        first.Value <- false
                        completeProperty animatable property)

                owning (box node) (settle context animatable))
        )

    /// <summary>
    /// Replays <c>play</c> with <c>read</c>'s value whenever it changes; anime's default composition replaces the running
    /// animation's tweens of the same properties. The first value is played to its end at once.
    /// </summary>
    /// <remarks>The cleanup reverts the latest animation.</remarks>
    let animateOn (read: 'inst -> 'v) (play: 'inst -> TS.Dom.Element -> 'v -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let current: ITickable ref = ref null

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    let tickable = play inst (elementOf node) value :> ITickable

                    if isNull current.Value then
                        tickable.complete ()

                    current.Value <- tickable)

                let release () =
                    if not (isNull current.Value) then
                        settle context current.Value ()

                owning (box node) release)
        )

    /// <summary>
    /// On an SVG geometry element: draws the stroke from its start to the fraction <c>read</c> returns, in 0..1, animating
    /// to each new fraction; the first fraction is drawn at once. The cleanup restores the element's stroke attributes.
    /// </summary>
    let draw (parameters: AnimatableParams) (read: 'inst -> float) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let initial = read inst

                let restore =
                    keepAttributes (box node) [| "pathLength"; "stroke-dasharray"; "stroke-dashoffset" |]

                let drawable = (Svg.drawable (targetsOf node, 0., initial))[0]

                let animatable =
                    Anime.animatable (Targets.object (strokeTarget drawable initial), withDefault parameters "draw" (AnimatableProp ()))

                Api.createEffectOn (fun () -> read inst) (fun fraction -> animatable.set ("draw", fraction))

                let release () =
                    if context.ExitPending () then
                        pauseIfPlayable animatable
                    else
                        animatable.revert ()
                        restore ()

                release)
        )

    /// <summary>
    /// On a <c>path</c>, <c>polygon</c> or <c>polyline</c>: morphs the shape to the <c>d</c> or <c>points</c> that
    /// <c>read</c> returns whenever it changes, with <c>parameters</c>' timing. The first shape is set at once, and each
    /// completed morph leaves exactly the shape read. The cleanup restores the shape the element had before the first.
    /// </summary>
    let morph (parameters: Anim) (read: 'inst -> string) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let element = box node
                let attribute = shapeAttribute element
                let restore = keepAttributes element [| attribute |]
                let current: JSAnimation ref = ref null
                let first = ref true

                Api.createEffectOn (fun () -> read inst) (fun shape ->
                    if first.Value then
                        first.Value <- false
                        setShape element attribute shape
                    else
                        let morphing =
                            Anime.animate (
                                targetsOf node,
                                withKey parameters attribute (Svg.morphTo (Erased.cast (shapeLike element attribute shape)))
                            )

                        chain morphing "onComplete" (fun _ -> setShape element attribute shape)
                        current.Value <- morphing)

                let release () =
                    if context.ExitPending () then
                        if not (isNull current.Value) then
                            current.Value.pause ()
                    else
                        if not (isNull current.Value) then
                            current.Value.cancel ()

                        restore ()

                release)
        )

    /// <summary>
    /// Links <c>play</c>'s tickable to an <c>onScroll</c> observer of the element, or of <c>parameters</c>' target, and
    /// writes the observer's progress and in-view state into the signals <c>bind</c> returns.
    /// </summary>
    /// <remarks>The observer requires <c>ResizeObserver</c>.</remarks>
    let scroll (parameters: ScrollParams) (bind: 'inst -> ScrollBinding) (play: 'inst -> TS.Dom.Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let binding = bind inst
                let graph = Graph.Current
                let observer = Anime.onScroll (withDefault parameters "target" (box node))
                let tickable = play inst (elementOf node) :> ITickable
                observer.link tickable |> ignore

                match binding.Progress with
                | ValueSome progress -> chain observer "onUpdate" (fun _ -> graph.Batch (fun () -> progress.Value <- observer.progress))
                | ValueNone -> ()

                match binding.InView with
                | ValueSome inView ->
                    chain observer "onEnter" (fun _ -> graph.Batch (fun () -> inView.Value <- true))
                    chain observer "onLeave" (fun _ -> graph.Batch (fun () -> inView.Value <- false))
                | ValueNone -> ()

                let stop = settle context tickable

                let release () =
                    stop ()
                    observer.revert ()

                release)
        )

    /// <summary>
    /// Makes the element draggable, two-way: dragging, and the end of a release's motion, write <c>Position</c>, and a write to
    /// <c>Position</c> from elsewhere moves the element without an update callback, so the write is not echoed back. A grab
    /// cancels the running animation of <c>Slot</c>.
    /// </summary>
    /// <remarks>The draggable requires <c>DOMPoint</c>, <c>DOMMatrix</c> and <c>ResizeObserver</c>.</remarks>
    let drag (parameters: DraggableParams) (bind: 'inst -> DragBinding) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let binding = bind inst
                let graph = Graph.Current
                let draggable = Anime.draggable (targetsOf node, parameters)

                let publish _ =
                    graph.Batch (fun () -> binding.Position.Value <- struct (draggable.x, draggable.y))

                chain draggable "onUpdate" publish
                whenPositioned draggable publish

                match binding.Slot with
                | ValueSome owned -> chain draggable "onGrab" (fun _ -> owned.Stop ())
                | ValueNone -> ()

                match binding.Dragging with
                | ValueSome dragging ->
                    chain draggable "onGrab" (fun _ -> graph.Batch (fun () -> dragging.Value <- true))
                    chain draggable "onRelease" (fun _ -> graph.Batch (fun () -> dragging.Value <- false))
                | ValueNone -> ()

                Api.createEffectOn (fun () -> binding.Position.Value) (fun (struct (x, y)) ->
                    if x <> draggable.x then
                        draggable.setX (x, true) |> ignore

                    if y <> draggable.y then
                        draggable.setY (y, true) |> ignore)

                let release () =
                    if context.ExitPending () then
                        freezeDraggable draggable
                    else
                        draggable.revert ()

                release)
        )

    /// <summary>
    /// Splits the element's static text with anime's <c>splitText</c> and plays <c>play</c> over the parts; the cleanup
    /// reverts the tickable, then the split.
    /// </summary>
    /// <remarks>The element's content must be static text: a text slot or region inside it stops updating once split.</remarks>
    let split (parameters: SplitParams) (play: 'inst -> TextSplitter -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let splitter = Anime.splitText (elementOf node, parameters)
                let tickable = play inst splitter :> ITickable

                let release () =
                    if context.ExitPending () then
                        tickable.pause ()
                        freezeSplitter splitter
                    else
                        tickable.revert ()
                        splitter.revert ()

                release)
        )
