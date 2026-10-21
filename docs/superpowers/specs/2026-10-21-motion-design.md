# Motion design: anime.js 4.5.0 as Ranvier's animation framework

> **Status 2026-10-21:** accepted. Written against master at `642d889` (view sugar v2). No backwards compatibility is kept. Every anime.js fact below was read from the published 4.5.0 package (`dist/modules/**/*.d.ts` and `.js`) or measured in a scratch probe. The probe files are in `C:\Users\shaya\.claude\jobs\7fee9ca9\tmp\anime-{probe,size,clock}`; delivery step 1 copies them into `docs/research/`.

Date: 2026-10-21. Inputs: `experiments/Ranvier.View.Portable/README.md` (sugar reference), `Template.fs` (`HookBuilder`, `Part`), `Representation.fs` (`PlacedKind`, `HtmlBackend.Placed`), `experiments/Ranvier.View.Foundation/{Mounting,Placement,Scheduling}.fs`, `src/Ranvier/Api.fs`, the ad-hoc anime code in `docs/maps/SignalMap.fs`, `docs/content/index.md` (the `StateDial` setup, lines 70–200) and `docs/Site.fs:170`.

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

- `Owner`, `onCleanup`, `createRoot`: every animation, observer, draggable and layout is reverted by the cleanup of the owner that created it (an instance owner, a row owner, or the caller's owner for `Anime.own`).
- `createEffect` / `createEffectOnWithComparer`: signal-driven animation. A placed motion hook creates an effect under the instance owner that pushes a reader's value into an animatable, re-runs an animation on change, or seeks a timeline.
- `createSignal`: playback state (`Progress`, `Running`, `Completed`), scroll progress, in-view and drag position, written from anime callbacks.
- `createProjection` / the arm memo: unchanged. Enter and exit are decided from the region pass that creates or retires a row; no new keyed or selection state.
- The commit `Queue` (`Scheduling.fs`): a reflow's `After` is enqueued on a slot of the mount's queue under `QueuedCommits`, through the existing `Reserve`/`Enqueue` API. The queue gains no members.

**View-owned state (new):** all of it is DOM node and layout state, which the rule assigns to the view.

- `Departures.exits: WeakMap<Node, Exit>` — the exit hook registered on a row or arm root element.
- `Departures.leaving: WeakSet<Node>` and `MountState.leaving: ResizeArray<unit -> unit>` (each held root's end action: cancel, then remove) — nodes of retired rows held in the DOM until their exit finishes. The core has no DOM.
- `Departures.reflows: WeakMap<Node, Reflow>` — the before/after measurement pair registered on a region's parent element.
- `Placement.Inserted: bool` — whether the current region pass is not the region's first; read by placed motion hooks.
- `Departures.pending: WeakSet<Node>` — row roots whose instance is disposed and whose exit runs once the row retires; nested mounts under such a root leave their nodes in place.
- `Departures.seam` — a plain object whose `Enabled` and `ReflowEnabled` flags and registry operations are set on first registration; while the flags are unset the new code paths are a single property test.

## Goal

anime.js 4.5.0 is the animation framework of Ranvier. An author animates any element of a v2 sugar template with typed F# (no `obj`, no `createObj`, no `?`), every animation is owned and reverted by Ranvier's ownership tree, signals drive animations and animations publish their state as signals, keyed lists and arms get enter, exit and FLIP reorder transitions, and portable templates using motion still render on the .NET server and hydrate. Five example apps exercise all of it and run as CI tests.

## Non-goals

- **A Ranvier animation engine.** anime is the engine; Ranvier adds ownership, reactivity and DOM-lifecycle seams.
- **Sequential ("out-in") arm transitions.** The next arm would have to wait for the old arm's exit, which delays reactive setup behind a visual timer. Arms transition simultaneously; an exit can pin the leaving node out of flow.
- **Exit transitions on the authored path** (`Generated.Html` nested-mount rows, boundary content). Their nodes leave through `MountState.release`. Sugar templates under queued commits mount through `Expand`, whose list rows and arms are nested mounts: those do hold their roots (see Runtime changes).
- **Exits on non-root elements.** Only a row or arm root element can hold its node after the row is retired.
- **Reactive `splitText`.** `Motion.split` takes static text.
- **CSS-transition-based enter/exit.** `Hooks.exit` is generic and can be used that way, but nothing ships for it.

## Decisions

1. **Two projects.** `src/Fable.Ranvier.Anime` holds the typed bindings and the core-owner integration (it depends on `Ranvier` only); `experiments/Ranvier.View.Anime` holds the template parts and moves with the view when the view graduates. *The docs maps (Partas.Solid) and any non-view code need the bindings without the view.*
2. **The bindings compile on .NET.** Every binding is `[<Import>]`, `[<Emit>]` or `[<Erase>]` over `Fable.Core`; parameter types are `[<ParamObject>]` classes, which are inert on .NET. *Portable templates reference motion parts, and the server never runs a placed hook.*
3. **Parameters are `[<ParamObject; Emit("$0")>]` constructors with named optional arguments**, `Anim(x = 100, duration = 400, ease = Ease.outQuart)`, plus `.With(name, value)` for custom properties. *Typed, discoverable, and emitted as an object literal with only the given keys: 0 B over hand-written JS.*
4. **Values and eases are erased types with `op_Implicit`.** `Value` from `float`, `int` and `string`; `Ease` named members and functions. *`x = 100` type-checks with no wrapper; the fallback is `!^` if Fable 5.18 rejects the conversion (step 1).*
5. **Motion parts are free functions in `module Motion`, not members of a builder class.** *Fable keeps every prototype method of a used class, and one class would pull layout, draggable and scroll (41 kB gzip) into every bundle; free functions tree-shake one by one.*
6. **Three new placed-hook kinds in the view runtime: `Motion`, `Exit`, `Reflow`.** `Ranvier.View.Anime` is written against the public `Hooks.motion`, `Hooks.exit` and `Hooks.reflow` only. *The runtime seam is generic, tested without anime, and costs a boolean test when unused.*
7. **The server omits the three kinds under both `Reject` and `Omit`.** *They add no markup, so the server HTML of a template with motion equals that of its twin without it.*
8. **Enter runs only for rows and arms created by a region pass after the region's first.** First-pass rows, hydrated rows and rows nested inside an entering row do not enter. *Matches Vue/Solid transition-group semantics and prevents a hydration flash.*
9. **An exit holds the retired row's root element in place until the exit's `finish` is called**; region validation, the cleared-list fast path and mount release understand held nodes. *anime's `createLayout` treats only `display:none`/`visibility:hidden` as leaving; a detached node cannot animate.*
10. **FLIP is anime's `createLayout`**, driven by a `Reflow` registered on the list's parent: `record()` before the first region pass under that element, `animate()` after the pass (synchronous commits) or after the mount's queued commits. *anime 4.5.0 ships FLIP with enter/leave/swap states; Ranvier supplies the before/after moments, which only the runtime knows.*
11. **Disposing the mount ends every held exit synchronously** (cancel, then remove). *Disposal stays deterministic and leaves no stray nodes.*
12. **Reduced motion is a core signal, `Motion.policy`**, seeded from `prefers-reduced-motion` and tracking changes. Under `Reduced`, enter, exit and flip are skipped (exit finishes at once); other parts receive the flag. *One switch, testable without a media query.*
13. **Deterministic tests drive anime's engine by hand**: `engine.useDefaultMainLoop <- false`, a mocked `Date` (anime's clock is `const now = Date.now`, captured at import), and `engine.update ()`. Verified in the probe. *Exact values per step in jsdom; Playwright uses `page.clock` for the same control.*
14. **Examples live in one Vite app, `examples/Ranvier.Motion`**, with five pages and its own test project. *They share the compile, `node_modules`, the Playwright server and one CI job, and they keep anime out of the Playground's benchmark bundles.*

## API

### `src/Fable.Ranvier.Anime` (namespace `Ranvier.Anime`)

Files: `Values.fs` (Targets, Value, Ease, Stagger, Position), `Params.fs` (ParamObject types), `Tickables.fs` (interfaces), `Anime.fs` (the `Anime` entry type and `Svg`, `Text`, `Waapi`, `Utils`, `Engine`), `Reactive.fs` (owner and signal integration), `Testing.fs`.

```fsharp
namespace Ranvier.Anime

open Fable.Core
open Fable.Core.TS.Dom

[<Erase>]
type Targets =
    static member inline op_Implicit(e: Element) : Targets = unbox e
    static member inline op_Implicit(es: Element array) : Targets = unbox es
    static member inline selector(css: string) : Targets = unbox css
    static member inline object(o: obj) : Targets = unbox o   // JS-object tweening

[<Erase>]
type Value =
    static member inline op_Implicit(v: float) : Value = unbox v
    static member inline op_Implicit(v: int) : Value = unbox v
    static member inline op_Implicit(v: string) : Value = unbox v       // "50%", "+=20", "#f0f", "1rem"
    static member inline fromTo(a: Value, b: Value) : Value = unbox [| a; b |]
    [<Emit("{from:$0}")>] static member from(v: Value) : Value = jsNative
    [<Emit("{to:$0}")>] static member ``to``(v: Value) : Value = jsNative
    static member inline keys(frames: Value array) : Value = unbox frames
    static member inline per(f: Element -> int -> int -> Value) : Value = unbox (System.Func<_, _, _, _> f)
    [<Import("stagger", "animejs"); ParamObject(1)>]
    static member stagger(each: Value, ?start: Value, ?from: StaggerFrom, ?reversed: bool,
                          ?grid: int array, ?axis: string, ?ease: Ease) : Value = jsNative

[<Erase>]
type Ease =
    static member inline linear: Ease = unbox "linear"
    static member inline outQuart: Ease = unbox "outQuart"          // ... every EaseStringParamNames member
    [<Emit("'outBack(' + $0 + ')'")>] static member outBack(overshoot: float) : Ease = jsNative
    [<Import("spring", "animejs"); ParamObject>]
    static member spring(?mass: float, ?stiffness: float, ?damping: float, ?velocity: float,
                         ?bounce: float, ?duration: float) : Ease = jsNative
    [<Import("cubicBezier", "animejs")>] static member cubicBezier(x1: float, y1: float, x2: float, y2: float) : Ease = jsNative
    [<Import("steps", "animejs")>] static member steps(count: int, ?fromStart: bool) : Ease = jsNative
    static member inline fn(f: float -> float) : Ease = unbox f

/// Timeline positions: At.ms 200, At.after 50 ("+=50"), At.withPrevious ("<"), At.label "intro".
[<Erase>] type At = (* inline members emitting the anime position strings *)

/// Animation parameters. Named arguments not given are absent from the emitted object.
type Anim
    [<ParamObject; Emit("$0")>]
    (?x: Value, ?y: Value, ?z: Value, ?translateX: Value, ?translateY: Value, ?rotate: Value, ?scale: Value,
     ?scaleX: Value, ?scaleY: Value, ?skew: Value, ?opacity: Value, ?width: Value, ?height: Value,
     ?color: Value, ?backgroundColor: Value, ?borderRadius: Value, ?filter: Value,
     ?draw: Value, ?d: Value, ?points: Value, ?strokeDashoffset: Value,
     ?duration: Value, ?delay: Value, ?ease: Ease, ?playbackEase: Ease, ?loop: Loop, ?loopDelay: float,
     ?alternate: bool, ?reversed: bool, ?autoplay: Autoplay, ?composition: Composition,
     ?playbackRate: float, ?frameRate: float, ?keyframes: Keyframes, ?modifier: float -> Value,
     ?onBegin: JSAnimation -> unit, ?onUpdate: JSAnimation -> unit, ?onLoop: JSAnimation -> unit,
     ?onPause: JSAnimation -> unit, ?onComplete: JSAnimation -> unit, ?onRender: JSAnimation -> unit) =
    [<Emit("($0[$1]=$2,$0)")>] member _.With(property: string, value: Value) : Anim = jsNative

// Also ParamObject types: TimelineParams, TimerParams, AnimatableParams (+ AnimatableProp), DraggableParams,
// ScrollParams, LayoutParams (duration, delay, ease, children, properties, enterFrom: Anim, leaveTo: Anim,
// swapAt: Anim), SplitParams, ScrambleParams, WaapiParams, ScopeParams.

/// Every tickable: play, pause, resume, restart, reverse, alternate, seek, complete, cancel, revert, stretch,
/// Progress/CurrentTime/Duration (get/set), Paused, Began, Completed, and the callback properties.
[<AllowNullLiteral>] type ITickable = inherit IRevertible (* abstract members *)
[<AllowNullLiteral>] type IRevertible = abstract revert: unit -> unit
type JSAnimation = inherit ITickable  (* refresh *)
type Timeline = inherit ITickable    (* add/set/call/label/sync/remove, returning Timeline *)
type Animatable = inherit IRevertible
    (* [<Emit("$0[$1]($2,$3,$4)")>] Set(property, value: float, ?duration, ?ease); [<Emit("$0[$1]()")>] Get *)
type Draggable = (* X, Y, ProgressX, ProgressY, SetX(x, mute), SetY, Enable, Disable, Stop, Reset, Revert *)
type ScrollObserver = (* Progress, IsInView, Velocity, Backward, Link, Refresh, Revert *)
type TextSplitter = (* Chars, Words, Lines: Element array; AddEffect; Revert; Refresh *)
type AutoLayout = (* Record, Animate(LayoutParams): Timeline, Update(callback, LayoutParams), Revert *)
type WAAPIAnimation = (* play, pause, complete, cancel, revert, Then *)

[<Erase>]
type Anime =
    [<Import("animate", "animejs")>] static member animate(targets: Targets, parameters: Anim) : JSAnimation = jsNative
    [<Import("createTimeline", "animejs")>] static member timeline(?parameters: TimelineParams) : Timeline = jsNative
    [<Import("createTimer", "animejs")>] static member timer(parameters: TimerParams) : Timer = jsNative
    [<Import("createAnimatable", "animejs")>] static member animatable(targets: Targets, parameters: AnimatableParams) : Animatable = jsNative
    [<Import("createDraggable", "animejs")>] static member draggable(target: Targets, ?parameters: DraggableParams) : Draggable = jsNative
    [<Import("onScroll", "animejs")>] static member onScroll(?parameters: ScrollParams) : ScrollObserver = jsNative
    [<Import("createLayout", "animejs")>] static member layout(root: Element, ?parameters: LayoutParams) : AutoLayout = jsNative
    [<Import("createScope", "animejs")>] static member scope(?parameters: ScopeParams) : Scope = jsNative
    [<Import("splitText", "animejs")>] static member splitText(target: Element, ?parameters: SplitParams) : TextSplitter = jsNative
    [<Import("scrambleText", "animejs")>] static member scramble(?parameters: ScrambleParams) : Value = jsNative
    /// Resolves after the tickable completes. anime's `then` resolves with undefined, so this exposes completion only.
    [<Emit("$0.then()")>] static member completion(t: #ITickable) : JS.Promise<unit> = jsNative

// Svg.morphTo(path, ?precision) : Value; Svg.drawable(targets, ?start, ?finish) : Element array;
// Svg.motionPath(path, ?offset) : MotionPath { TranslateX; TranslateY; Rotate: Value }
// Waapi.animate(targets, WaapiParams) : WAAPIAnimation (4.5 kB gzip path)
// Utils.set, get, remove, cleanInlineStyles, random, clamp, mapRange, snap, lerp, round
// Engine.useDefaultMainLoop, pauseOnDocumentHidden, speed, timeUnit, update ()
```

`Reactive.fs` (core integration, no view dependency):

```fsharp
module Ranvier.Anime.Reactive

/// Reverts `revertible` when the current owner is disposed; returns it. Outside an owner it raises.
val own: revertible: 'r -> 'r when 'r :> IRevertible

type Playback = { Progress: Memo<float>; Running: Memo<bool>; Completed: Memo<bool> }
/// Signals updated from the tickable's callbacks; the callbacks it had are chained, not replaced.
val track: tickable: #ITickable -> Playback
/// An effect that seeks `tickable` to the progress returned by `read` (0..1) whenever it changes.
val scrub: read: (unit -> float) -> tickable: #ITickable -> unit
/// An effect that sends `read ()` to the animatable property whenever it changes.
val drive: read: (unit -> float) -> animatable: Animatable -> property: string -> unit

type MotionPolicy = Full | Reduced
/// A root signal seeded from `(prefers-reduced-motion: reduce)` on first read and tracking the media query; writable.
val policy: unit -> Signal<MotionPolicy>
```

`Testing.fs`: `Testing.manual ()` sets `engine.useDefaultMainLoop <- false` and `pauseOnDocumentHidden <- false`; `Testing.step ()` calls `engine.update ()`. Time is advanced by the test's mocked `Date`.

### Runtime hooks (`experiments/Ranvier.View.Portable/Template.fs`, `HookBuilder`)

```fsharp
type MotionContext = { Hydrated: bool; Inserted: bool; ExitPending: unit -> bool }
type Reflow = { Before: unit -> unit; After: unit -> unit }

/// Runs once the element is placed, with whether the element was adopted from server HTML (the mount's hydration
/// cursor, not the root flag), whether its row or arm was created by a pass after its region's first, and a reader of
/// whether an exit of the element has not started; read in the cleanup, true means the element stays for its exit.
/// The returned cleanup runs when the instance is disposed.
member Hooks.motion: Func<'inst, 'native, MotionContext, unit -> unit> -> Part<'native, 'inst>
/// On a row or arm root element: when the row is retired, its node stays in place and `exit` runs with `finish`;
/// the node leaves when `finish` is called, with its content kept until then. The returned function cancels the exit
/// when removal is forced. An exit applies once the element is placed.
member Hooks.exit: Func<'inst, 'native, unit -> unit, unit -> unit> -> Part<'native, 'inst>
/// On a region's parent element: `Before` runs before the first region pass under the element in a flush, `After`
/// once the pass's nodes and commits are in place.
member Hooks.reflow: Func<'inst, 'native, Reflow> -> Part<'native, 'inst>
/// Two hook parts on one element as one part, at most one of them other than a motion hook; the part takes that one's kind.
member Hooks.both: Part<'native, 'inst> * Part<'native, 'inst> -> Part<'native, 'inst>
```

### `experiments/Ranvier.View.Anime` (namespace `Ranvier.View.Anime`, `[<RequireQualifiedAccess>] module Motion`)

Every function returns `Part<'native, 'inst>` and is non-overloaded, so it infers like a non-overloaded `Events.*` entry. Play functions receive the element as `Element`, which `Targets` converts from. Animations are created inside the hook, never at definition time. The hook members take delegates: F# converts a lambda to a delegate returning a function only when written `Func<_, _, _, _>(fun ...)`, and `Motion` does so, so its emitted module holds no curry helper.

```fsharp
module Motion =
    /// Plays on every placement, hydration included; the animation is reverted on disposal.
    val on: play: ('inst -> Element -> #IRevertible) -> Part<'native, 'inst>
    /// As `on`, skipped when the mount hydrated server nodes.
    val appear: play: ('inst -> Element -> #IRevertible) -> Part<'native, 'inst>
    /// Plays when the element's row or arm is inserted after its region's first pass.
    val enter: play: ('inst -> Element -> #ITickable) -> Part<'native, 'inst>
    /// Plays when the element's row or arm is retired; the node leaves when the animation completes.
    val exit: play: ('inst -> Element -> #ITickable) -> Part<'native, 'inst>
    /// `enter` and `exit` together, with an exit that pins the node out of flow (absolute at its current box).
    val presence: enter: ('inst -> Element -> #ITickable) -> exit: ('inst -> Element -> #ITickable) -> Part<'native, 'inst>
    val fade: Part<'native, 'inst>          // presence presets: fade, rise, scale, collapse (height to 0)
    /// FLIP for the rows of every region under the element: anime createLayout, children = direct element
    /// children that are not leaving.
    val flip: parameters: LayoutParams -> Part<'native, 'inst>
    /// Pushes `read` into the element's animatable `property` whenever it changes.
    val follow: property: string -> parameters: AnimatableParams -> read: ('inst -> float) -> Part<'native, 'inst>
    /// Replays `play` with the new value whenever `read` changes (composition replace); the first value is set.
    val animateOn: read: ('inst -> 'v) -> play: ('inst -> Element -> 'v -> #ITickable) -> Part<'native, 'inst>
    /// SVG: draws the stroke to the fraction returned by `read`; morphs the path to the `d` returned by `read`.
    val draw: parameters: AnimatableParams -> read: ('inst -> float) -> Part<'native, 'inst>
    val morph: parameters: Anim -> read: ('inst -> string) -> Part<'native, 'inst>
    /// Links `play` to scroll; writes scroll progress and in-view into the instance's signals when given.
    val scroll: parameters: ScrollParams -> bind: ('inst -> ScrollBinding) -> play: ('inst -> Element -> #ITickable) -> Part<'native, 'inst>
    /// Two-way: drag writes `Position`, a write to `Position` from elsewhere moves the element (muted, no echo).
    val drag: parameters: DraggableParams -> bind: ('inst -> DragBinding) -> Part<'native, 'inst>
    /// Splits the element's static text (Lent hook; Debug raises when the element holds a text slot or region).
    val split: parameters: SplitParams -> play: ('inst -> TextSplitter -> #ITickable) -> Part<'native, 'inst>

type ScrollBinding = { Progress: Signal<float> voption; InView: Signal<bool> voption }
type DragBinding = { Position: Signal<struct (float * float)>; Dragging: Signal<bool> voption }
```

**Revert and exit interplay.** Cleanups run before a retired row's nodes leave. `Motion.on`/`appear`/`enter` cleanups check `Departures`-held state through a package-level `WeakSet` of elements with a Motion exit: such an element's animations are paused, not reverted, so the exit starts from the current values; a paused tickable leaves anime's engine on its next tick, so `finish` follows the exit's completion with no `Utils.remove` (ruled in step 8: it costs 427 B gzip).

**Sample (sugar v2).**

```fsharp
open Ranvier.Anime
open Ranvier.View.Anime

type Card = { Id: int; Title: string }
type Board = { Cards: Signal<Card array>; Load: Signal<float>; Pos: Signal<struct (float * float)> }

let board =
    Component.template
        (fun _ (cards: Card array) -> { Cards = Api.createSignal cards; Load = Api.createSignal 0.; Pos = Api.createSignal (struct (0., 0.)) })
        (section () {
            ul (Attrs.className "cards", Motion.flip (LayoutParams(duration = 350, ease = Ease.outQuart))) {
                forEach (fun c -> c.Id) (fun b -> b.Cards.Value) (
                    li (Motion.presence
                            (fun _ el -> Anime.animate (el, Anim(opacity = Value.fromTo (0, 1), y = Value.fromTo (12, 0), duration = 240)))
                            (fun _ el -> Anime.animate (el, Anim(opacity = 0, scale = 0.9, duration = 180)))) {
                        fun current -> (current ()).Title
                    })
            }

            div (Attrs.className "gauge", Motion.follow "scaleX" (AnimatableParams(duration = 400, ease = Ease.outQuart)) (fun b -> b.Load.Value))
            div (Attrs.className "puck", Motion.drag (DraggableParams(container = "section")) (fun b -> { Position = b.Pos; Dragging = ValueNone }))
            h1 (Motion.split (SplitParams(chars = true)) (fun _ s -> Anime.animate (s.Chars, Anim(y = Value.fromTo ("100%", "0%"), delay = Value.stagger 20)))) { "Ranvier" }
        })
```

## Runtime changes needed in Ranvier and the view

`src/Ranvier`: none.

`experiments/Ranvier.View.Portable`:

- `Representation.fs`: `PlacedKind` gains `Motion`, `Exit`, `Reflow`; `placedPart` names them. `HtmlBackend.Placed` returns before `browserCapability` for the three kinds (decision 7). Add `MotionContext` and `Reflow` (public, beside `NodeRef`).
- `Template.fs`: `Part.PlacedPart` payload becomes `Func<obj, obj, obj, obj>` (instance, node, context → cleanup, exit or reflow, by kind); `placedHook` and `HookBuilder.mounted/ref/widget/lend` adapt; `HookBuilder.motion/exit/reflow` added. The placed arrays of `TemplateNode` (line 175) and `TemplateDom.CompiledTemplate.Placed` change type to match.
- `Typed.fs`: the modifier path passes the new kinds through unchanged.

`experiments/Ranvier.View.Foundation`:

- **New `Departures.fs`** in `experiments/Ranvier.View.Portable` (after `Typed.fs`, before `Template.fs`), beside `HookBuilder`, which calls it from the exit and reflow payloads; the mount paths reach it only through `Departures.seam`, so a bundle without those hooks holds the seam object and its call sites alone. It holds `exits`, `leaving`, `pending`, `reflows` (as in Core and view state); `register (node, exit)`, which returns the cleanup that marks the node pending; `registerReflow (node, reflow)` returning its unregister; `hasExit`; and the seam operations `Retire (node, held)` (true when the node is held: marks it leaving, calls the exit with a `finish` that removes the node and drops it from its held list, and a 10 s safety timeout that forces `finish`), `NextLive`, `Holds (start, end)` for the cleared guard, `Within node` (the node or an ancestor is leaving or pending), `End (held, errors, target)` (end every held exit, or move them, still running, into `target`) and `Before (regionStart, post)` returning the post of `After` with per-flush coalescing per element.
- `Placement.fs`: `Placement.Inserted` and `Placement.Row` (the container is a region pass's), set by `PlacementPass.Container`; `PlacementPass.Begin` marks every pass after the first inserted. `PlacedHook.register` registers every kind the same way and passes each hook the flags of its `MotionContext` (1 hydrated, 2 inserted); the hydrated flag is the services' hydration cursor. The exit payload registers its exit once placed and returns the pending mark as its cleanup; the reflow payload registers its measurement and returns the withdrawal. A Debug build raises "An exit hook requires a row or arm root element" when an exit is registered on a node that is not a root at registration, or whose container is not a region pass's (a mount root).
- `Mounting.fs`:
  - `Row.Remove` takes the mount's held list, null when the removal is forced (the mount tears down, so release stays synchronous); each node from `First` to `Last` is offered to `seam.Retire` and removed when not held. A failure is raised after every node is handled.
  - `Row.ValidateWindow` steps with `seam.NextLive` when exits are enabled, so held nodes between rows do not read as external mutation.
  - `RegionSite.FlushReleased`: the `cleared` fast path (`Row.ClearBetween`) is taken only when exits are disabled or no node between the markers is leaving or has an exit; otherwise rows release one by one.
  - `ListRegion.reconcile` and `ArmRegion.reconcile`: begin the pass (`PlacementPass.Begin`) before building; when reflows are enabled and the pass is not hydrating, call `seam.Before (regionStart, this.Post)` at pass start and, after the pass settles, run the returned post, where `Post` creates an untracked core effect under `Synchronous` (it runs after the effects the pass created) and enqueues the action on a slot of the mount's `Queue` under `Queued`.
  - `MountState.release`: before `range.Release`, cancel and remove every node in `leaving` (decision 11); on a live release inside a live enclosing mount, move them, still running, into the enclosing mount's list instead. A nested mount whose first node is `Within` a leaving or pending root leaves its nodes in place, unclaimed, so a held root keeps its nested lists, arms, boundaries and holes until `finish`. A list row's or arm's nested mount (its inherited container is a region pass's) retires its roots into the enclosing mount's list on a live release.
- `Hydration.fs`: none; adopted rows retire through the same `removeRow`.

## Examples

All in `examples/Ranvier.Motion` (Vite multi-page app, `Motion.fsproj`, `tests/MotionTests.fsproj`, `package.json` pinning `animejs 4.5.0`, `jsdom`, `@playwright/test`, `vite` at the Playground's versions). Each page is a sugar v2 component tree, and each exposes `window.__motion` (its root instance's signals) for tests.

| Page | Exercises |
|---|---|
| `kanban/` — Elmish board, three columns, cards | `forEach` with `Motion.presence`, `Motion.flip` on the board root (moves between columns), `Motion.drag` on cards with drop-to-column, add/delete/shuffle/clear, undo |
| `dashboard/` — live metrics from `Api.createStream` | `Motion.follow` gauges, `Motion.draw` + `Motion.morph` SVG line chart updating per sample, number tween via `Reactive.track`, `boundary` pending skeleton with `Motion.on` loop, `show` panels with `Motion.fade` |
| `story/` — scroll-driven landing, server-rendered | `Motion.scroll` timelines with progress signals, `Motion.split` headline, staggered grid via `Value.stagger`, `Motion.appear`; rendered as a whole document by `Server.renderDocument` on .NET under `Omit` and hydrated `Strict` |
| `physics/` — spring playground | `Motion.drag` with `Ease.spring` release, chained `Motion.follow` followers, timeline scrubber via `Reactive.scrub` bound to a range input, `Motion.policy` toggle |
| `routes/` — Elmish multi-page app | hash routing, `matchWith` page arms with `Motion.presence` (pinned exit), nested `Mvu` per page, `Motion.flip` on the shell, rapid back/forward |

## Testing

1. **Runtime seam, no anime** (Playground, `tests/DepartureChecks.fs` in `DomTests.fsproj`, `tests/departures.test.mjs` added to `test:dom`): exit hooks as plain callbacks that store `finish`. Cases: node held until `finish`; reorder and insert while nodes are held validate; clearing a list with exits holds every root; a key re-added while its old node leaves gets a new row; arm switch with exit leaves old and new nodes in order; mount disposal mid-exit removes all nodes synchronously and calls cancel; the 10 s timeout; `Inserted` false on first pass, hydration and nested rows, true on later rows and `show` false→true; `Reflow` order (`Before` once per flush, `After` after the layout and after queued commits); server HTML of each fixture equals its twin without hooks under `Reject` (`SugarServerChecks.fs`); counters (`RANVIER_VIEW_COUNTERS`) show no added work when no hook is used.
2. **Bindings** (`examples/Ranvier.Motion/tests`, node --test + jsdom): globals (`window`, `document`, `Node`, `Element`, `HTMLElement`, `SVGElement`, `NodeList`, `HTMLCollection`, `getComputedStyle`, `requestAnimationFrame`) are set and `mock.timers.enable({ apis: ["Date"] })` is called **before** a dynamic `await import` of the compiled module, because anime evaluates `isBrowser` and captures `Date.now` at import. Then `Testing.manual ()` and `tick; Testing.step ()`. Probe result: `opacity [0,1]` linear over 1000 ms reads `0, .262, .512, .762, 1` at 250 ms steps, every run. Emit checks: `Anime.animate (el, Anim(x = 1))` emits `animate(el, {x: 1})` (no `createObj`, no `curry`).
3. **Motion parts** (same project, jsdom, manual clock): enter values at 0/half/end; exit removes the node exactly at completion; `Motion.on` reverts on disposal; paused-not-reverted when an exit follows; `follow`/`animateOn`/`draw` reach targets after a signal write; `drag` echo guard with stubbed `DOMPoint`/`ResizeObserver` (jsdom lacks both; `splitText`, `onScroll` and `createDraggable` throw without them, probe-verified); `Motion.policy Reduced` skips enter/exit.
4. **Real animation** (Playwright, chromium, `examples/Ranvier.Motion/playwright.config.mjs` on port 5179): `page.clock.install ()` controls `Date` and `requestAnimationFrame`, so `page.clock.runFor (ms)` samples JS-engine animations mid-flight. FLIP uses WAAPI for transforms, which the page clock does not control: tests assert `el.getAnimations ()` holds the layout's animation and its target transform, then `finish ()` it and assert final boxes. Pages: kanban (add, delete, shuffle, drag between columns), dashboard (path `d` and `draw` change per sample), story (scroll to fixed offsets, progress signal values; hydration reports nothing), physics (mouse drag and release settles; scrubber seeks), routes (10 rapid switches leave only the last page).
5. **.NET**: `examples/Ranvier.Motion/Server/ServerChecks.fsproj` renders the story page with `Server.renderDocument` and compares it with the twin without motion parts. The policy is `Omit`, not the default `Reject`: the page has an event listener, which the server refuses under `Reject`, and listeners attach on hydration.

**CI** (`.github/workflows/ci.yml`): a `motion` job mirroring `playground` (Node 26, `npm ci`, `dotnet tool restore`, `npx playwright install --with-deps chromium`, `npm test` in `examples/Ranvier.Motion`, which runs `ServerChecks` before the Chromium suite, then `npm run size:only`), added to `result.needs`. `.github/scripts/changes.cjs` needs no change: `examples/` paths are non-documentation. The `packages` job packs `src/Fable.Ranvier.Anime` with its Fable sources.

## Bundle budget

Measured with rolldown (minify) and gzip level 9 on anime 4.5.0, importing from `animejs`: `animate` 12.2 kB, `createTimeline` 13.4 kB, `createAnimatable` 12.6 kB, `createLayout` 20.6 kB, `createDraggable` 19.5 kB, `onScroll` + `animate` 16.5 kB, `splitText` 3.3 kB, `waapi.animate` 4.5 kB, `spring` + `animate` 13.1 kB, everything 41.0 kB. Root imports tree-shake.

`fable/Ranvier.View.Anime.Tests/motion-size.mjs` (modelled on `sugar-size.mjs`, writes `docs/research/motion/bundle.json`, exit 1 on a breach). The examples app reports per-page sizes with `examples/Ranvier.Motion/size.mjs`, without a budget:

| Check | Budget |
|---|---|
| Runtime seam: the Playground `sugar-size.mjs` explicit entry, before and after steps 3–6 | ≤ 450 B gzip (re-baselined from 150 B on review; measured +447 B, see `docs/research/2026-10-21-motion-seam.md`) |
| Bindings: `Anime.animate` entry vs hand-written JS calling `animate` | ≤ 64 B gzip |
| Glue: `Motion.presence` entry vs `Hooks.motion`/`Hooks.exit` calling `animate` by hand | ≤ 900 B gzip |
| Glue: `Motion.flip` entry vs `Hooks.reflow` calling `createLayout` by hand | ≤ 600 B gzip |
| Isolation: an entry using one Motion function contains no other feature's anime class (`AutoLayout`, `Draggable`, `ScrollObserver`, `TextSplitter`, `WAAPIAnimation`, `Timeline`, by unminified search) | 0 |
| Emitted `Ranvier.View.Anime` and `Fable.Ranvier.Anime` modules: `curry`, `uncurry`, `partialApply`, `createObj` | 0 |

Perf: Playground `keyed-rows.mjs` and `run.mjs --template-comparison` ranges overlap before and after steps 3–6 (the payload type change touches every placed hook).

## Docs

- `experiments/Ranvier.View.Anime/README.md`: the reference for `Motion.*`, in the shape of the Portable README (opening lines, each part with a sample, enter/exit rules, flip requirements: the container is positioned and its rows are direct element children, reduced motion, SSR rules, costs). Its samples compile as fixtures in `tests/MotionTests.fsproj`.
- A "Motion" section of the Views navigation, beside the other authoring pages: `view/motion.md` (the bindings), `view/motion-reactive.md` (`Reactive`: owning, playback signals, scrub, drive, policy), `view/motion-parts.md` (the rules the parts share, linking to the README and the examples) and `view/motion-testing.md` (deterministic testing, the bundle table). Their F# samples compile and are drift-checked in `docs/samples/view`.
- `docs/maps/SignalMap.fs`: replace the private `Anime` module and `createObj` options with `Ranvier.Anime`; the dot travel uses `Svg.motionPath` instead of `getPointAtLength` in `onUpdate`; node placement tweens an `Animatable` over `x`/`y` instead of a proxy object.
- `docs/content/index.md` (`StateDial`): typed `Anime.animate`/`Utils.set`; the `setTimeout` arrival sequencing becomes one `Anime.timeline` with `.call`; `reduced ()` becomes `Reactive.policy ()`.
- `docs/maps/Ranvier.Docs.Maps.fsproj` references `src/Fable.Ranvier.Anime`; `docs/Site.fs:170` keeps `npm "animejs" "4.5.0"`.
- `docs/research/2026-10-21-motion.md`: probe outputs, measurements, Fable emit samples.

## Risks

1. **Held nodes against Mounting's contiguity assumptions** (most likely to cost a phase). Validation, `ClearBetween`, `deleteBetween`, `clearAdopted`, `fillsFragment` and the boundary's `place` all assume that only rows and markers sit between region markers. The spec covers validation, the cleared path and release; any missed path either deletes a leaving node early (benign) or raises "externally mutated" (breaks the region). Step 5 greps every `nextOrNull`/sibling walk in `Mounting.fs` and gives each a case.
2. **`[<ParamObject>]` constructors with ~45 optional arguments and `op_Implicit` under Fable 5.18.0.** Fable 5.18 already miscompiles two constructor shapes (v2 spec §4.5). If the probe fails, parameters fall back to typed anonymous records cast with `!!`, keeping the names but losing optional-argument discovery.
3. **FLIP in tests.** `createLayout` animates transforms with WAAPI, which jsdom lacks and Playwright's clock does not drive; mid-flight FLIP positions are asserted only through `getAnimations ()`. The default `children: '*'` measures every descendant; `Motion.flip` narrows it to direct children, and lists over ~500 rows need a measured cost before the default is final.
4. **The placed-hook payload change** touches every hook in the hot mount path; a measurable regression in `keyed-rows.mjs` reverts to a separate `Part` case for the new kinds.
5. **anime callback semantics.** `then` resolves with `undefined`, and `composition: replace` interacts with a running animation on the same property in ways the probe did not settle (`complete ()` of a replacing animation read `opacity 0`). `animateOn` and the exit's pause-then-animate path are tested against both.

## Task breakdown

Each step lands with its tests green and is reviewable alone.

1. **Probe.** Fable 5.18: `[<ParamObject; Emit("$0")>]` constructor, `op_Implicit` on named optional arguments, `[<Import>]` static members on an `[<Erase>]` type, .NET compile of the same file; emit samples. Copy the anime probes and measurements into `docs/research/2026-10-21-motion.md`. Decide decision 3/4 fallbacks.
2. **`src/Fable.Ranvier.Anime`.** Values, params, tickables, `Anime`, `Svg`, `Text`, `Waapi`, `Utils`, `Engine`, `Reactive`, `Testing`. Tests: binding emit checks and manual-clock value checks under jsdom; `dotnet build` on .NET; package compatibility script.
3. **Portable hook kinds.** `PlacedKind` additions, payload type, `HookBuilder.motion/exit/reflow`, server omission. Tests: `RepresentationChecks`, `SugarServerChecks` twins under `Reject`.
4. **Placement context.** `Placement.Inserted`, `MotionContext` delivery, hydration flag. Tests: Playground `DepartureChecks` (Inserted and Hydrated cases).
5. **Departures.** `Departures.fs`, `Row.Remove`, validation stepping, cleared path, release. Tests: the exit cases of Testing §1; the sibling-walk audit of Risk 1.
6. **Reflow.** `before`/`after` in list and arm passes, synchronous and queued. Tests: Reflow order cases.
7. **Seam gate.** Bundle delta on the explicit entry, `keyed-rows.mjs`, `--template-comparison`, counters; record in the research note.
8. **`Ranvier.View.Anime`, lifecycle parts.** `on`, `appear`, `enter`, `exit`, `presence`, presets, `flip`, `Motion.policy` handling. The "paused, not reverted" interplay reads `MotionContext.ExitPending ()` in the motion hook's cleanup: true while an exit of the element is registered and not started. Tests: Testing §3 lifecycle cases; `motion-size.mjs` presence and flip rows.
9. **`Ranvier.View.Anime`, reactive parts.** `follow`, `animateOn`, `draw`, `morph`, `scroll`, `drag`, `split`. Tests: Testing §3 reactive cases; isolation row of the budget.
10. **`examples/Ranvier.Motion` scaffold and kanban**, Playwright config, CI `motion` job.
11. **Dashboard page.**
12. **Story page and .NET server check.**
13. **Physics page.**
14. **Routes page.**
15. **Docs.** View README with compiled samples, `guide/animation.md`, SignalMap and index migration, research note.
