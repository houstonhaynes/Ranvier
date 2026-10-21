---
title: Motion parts
description: Template parts that animate elements on placement, on entering and leaving lists and arms, on reorder, and from signals.
---

`Ranvier.View.Anime` animates the elements of a sugar template. Each `Motion.*` function returns a
part, given to a tag beside its attributes and events. A part creates its anime objects once the
element is placed, and the instance's disposal reverts them. The
[Motion parts reference](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Anime/README.md)
documents every part with a sample; this page covers the rules they share.

```fsharp
open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Ranvier.View.Anime

type Task = { Id: int; Title: string }

type TaskList =
    {
        Tasks: Signal<Task array>
        Done: Signal<float>
    }

let taskList =
    Component.template
        (fun _ (tasks: Task array) ->
            {
                Tasks = Api.createSignal tasks
                Done = Api.createSignal 0.
            })
        (section () {
            ul (Attrs.className "tasks", Motion.flip (LayoutParams (duration = 300, ease = Ease.outQuart))) {
                forEach
                    (fun task -> task.Id)
                    (fun l -> l.Tasks.Value)
                    (li (
                        Motion.presence
                            (fun _ el -> Anime.animate (el, Anim (opacity = Value.fromTo (0, 1), y = Value.fromTo (8, 0), duration = 220)))
                            (fun _ el -> Anime.animate (el, Anim (opacity = 0, duration = 160)))
                    ) {
                        fun current -> (current ()).Title
                    })
            }

            div (Attrs.className "progress", Motion.follow "scaleX" (AnimatableParams (duration = 400)) (fun l -> l.Done.Value)) { () }
        })
```

`Motion` requires qualified access: `Motion.exit` shadows FSharp.Core's `exit` when `Motion` is opened.

## Play functions

A play function receives the instance and the element, typed `TS.Dom.Element` on every tag, and
returns the anime object the part owns. `on` and `appear` take any `IRevertible`; `enter`, `exit`
and `presence` take a tickable, a `JSAnimation` or a `Timeline`.

Anime objects are created inside play functions. A template is built where its definition is bound,
on .NET as well, where `Anime.animate`, `Value.stagger`, `Ease.spring` and the other imported and
emitted members raise. The parameter constructors are inert on .NET and may appear at definition
time, as `LayoutParams` and `AnimatableParams` do above.

## The parts

| Part | Plays |
| --- | --- |
| `Motion.on` | on every placement, hydration included |
| `Motion.appear` | on every placement of an element created on the client |
| `Motion.enter` | when the element's row or arm is created by a region pass after the region's first |
| `Motion.exit` | when the element's row or arm is retired; the element leaves when the tickable completes |
| `Motion.presence` | `enter` and `exit` together, with the leaving element pinned out of flow |
| `Motion.fade`, `rise`, `scale`, `collapse` | presence presets |
| `Motion.flip` | FLIP for the rows of every region under the element |
| `Motion.follow`, `animateOn`, `draw`, `morph` | animations towards the values of a reader, re-run when the reader's value changes |
| `Motion.scroll`, `drag`, `split` | a scroll observer, a draggable and split text, bound to the instance's signals |

## Entering

An enter plays only for a row or arm created by a region pass after the region's first.

- Rows of a list's first pass stay still.
- Hydrated rows stay as the server rendered them.
- Rows nested inside an entering row stay still; the entering row animates as one.
- An arm shown by `show` turning true, or switched by `matchWith`, enters.

## Leaving

An exit applies to the root element of a list row or an arm.

- **The retired row's element is held in place**, with its content, until its exit's tickable
  completes, and then leaves. Rows inserted or moved while it plays are placed around it, and a key
  added again while its old element leaves gets a new row.
- **The exit starts from the current values.** On an element whose exit follows, and on every
  element inside it, the other parts pause their animations at the instance's disposal instead of
  reverting them, and anime's default `replace` composition hands each animated property to the
  exit. `drag` disables its draggable, and `scroll` and `split` stop their observers.
- **`presence` pins the leaving element** with `position: absolute` at its offset box, so the rows
  after it close the gap while it plays. Give the list's container `position: relative`. `collapse`
  and a separate `exit` stay in flow.
- **Disposing the mount ends every running exit at once**: the tickable is cancelled and the element
  leaves. An exit still running after 10 s is cancelled and its element leaves.
- **An exit whose play function raises finishes at once**: its element leaves, and the failure is
  raised once the row's other nodes are handled.
- A Debug build raises "An exit hook requires a row or arm root element" for an exit on any other
  element.

Arms switch simultaneously: the new arm is placed while the old one's exit plays.

## FLIP

`Motion.flip parameters` on a list's container wraps anime's `createLayout`. It records the layout of
the container's element children before the first region pass under the container in a flush, and
animates them from that layout to the new one once the pass's nodes and commits are in place.

- **The rows are the container's direct element children.** A row whose Motion exit has started, or
  whose Motion enter started in the pass, is left out of that pass's measurements.
- **Motion parts keep the visual properties.** In a pass where an element under the container carries
  a Motion part (`on`, `appear`, `follow`, `animateOn`, or a running enter or exit), the layout
  animates position and size only; opacity, colours, borders, filters, clip paths and font size stay
  with the parts. In a pass with no such element, anime's default fades apply. A property named in
  `properties`, `enterFrom`, `leaveTo` or `swapAt` is always the layout's.
- **The container is positioned.** anime animates moved rows with `position: absolute` and a
  translation through the Web Animations API.
- A pass with no recorded layout before it, such as the first, does not animate.
- **A dropped row starts from where it was dropped.** `Motion.flipFrom element` starts the element's
  next FLIP from its current visual box, drag transform included, in place of the transform-free box
  the recording measures. Call it before the change that moves the row and before the drag position
  is reset, as the kanban example does for a card dropped into another column. The box stays on the
  element until the next pass that measures the element as a row, which starts its FLIP there with
  the recorded size and drops the box.
- The parts mark their elements for `flip` until their cleanup, their exit's end or the pass's layout
  removes the mark. Each flip pass checks every marked element on the page, so its cost grows with
  the number of marked elements. A mount host emptied by other code without disposing the mount keeps
  its marked elements reachable: dispose the mount.

## Animations from handlers

An animation started by an event handler or an effect, such as a spring home after a release,
belongs to a `MotionSlot`. `Motion.slot ()`, called in the instance's setup, returns one owned by the
instance.

- `slot.Play (fun () -> animation)` cancels the running animation, then plays the one the function
  creates. A cancelled animation stops at its current values, its callbacks muted.
- `slot.Stop ()` cancels the running animation. `slot.Running` is the latest animation played (null
  after a stop), and `slot.Playing` is true while it is live: played, and not yet completed or
  cancelled.
- The instance's disposal cancels the running animation and closes the slot; a later `Play` only
  cancels.
- Given as a `DragBinding`'s `Slot`, a grab cancels the slot's running animation.

```fsharp
type Point = { mutable x: float; mutable y: float }

type Handle =
    {
        Offset: Signal<struct (float * float)>
        Spring: MotionSlot
    }

let springBack (handle: Handle) =
    let graph = Graph.Current
    let struct (x, y) = Api.untrack (fun () -> handle.Offset.Value)
    let point = { x = x; y = y }

    handle.Spring.Play (fun () ->
        Anime.animate (
            Targets.object point,
            Anim (
                x = 0,
                y = 0,
                ease = Ease.spring (stiffness = 200., damping = 14.),
                onUpdate = fun _ -> graph.Batch (fun () -> handle.Offset.Value <- struct (point.x, point.y))
            )
        ))

let handle =
    Component.template
        (fun _ () ->
            {
                Offset = Api.createSignal (struct (0., 0.))
                Spring = Motion.slot ()
            })
        (div () {
            div (
                Attrs.className "handle",
                Motion.drag (DraggableParams ()) (fun h ->
                    {
                        Position = h.Offset
                        Dragging = ValueNone
                        Slot = ValueSome h.Spring
                    })
            ) {
                "drag"
            }

            button (Events.onClick (fun (h: Handle) _ -> springBack h)) { "Back" }
        })
```

## Reduced motion

Under `Reactive.policy ()` = `Reduced`, enters and the presets' enters do not play, an exit finishes
at once (its element leaves in the pass that retires its row), `flip` neither records nor
animates, and `Motion.flipFrom` keeps no box. `on`, `appear` and the reactive parts play unchanged; a play function that honours the
policy reads `Reactive.policy ()` itself. See [policy](motion-reactive.md#policy).

## Server rendering and hydration

The parts leave the markup unchanged. `Server.render` omits them under both the `Reject` and `Omit` policies, so a
template with Motion parts renders the HTML of its twin without them. `taskList` above renders:

```html
<!--[r--><section><ul class="tasks"><!--[--><li>Write</li><li>Ship</li><!--]--></ul><div class="progress"></div></section><!--]-->
```

Hydration adopts the served elements. `appear` and `enter` leave them as served, and `on` plays.
`follow` sets its first value at once. `follow`'s `parameters` give the followed property their
shared duration, ease and `unit` unless its own entry sets them:
`Motion.follow "x" (AnimatableParams (unit = "%", duration = 420))` moves the element by percentages of
its width.

A page whose template holds event handlers renders under `Omit`; `Reject` refuses listeners, and
hydration attaches them. The story example renders its whole page that way with
`Server.renderDocument` (see [The page](../server/deployment.md#the-page)).

## SVG

`draw` and `morph` animate SVG geometry. The SVG tags of the sugar (`svg`, `g`, `path`, `circle`,
`ellipse`, `line`, `polyline`, `polygon`, `rect`, `text'`, `tspan`, `use'`, `defs`,
`linearGradient`, `radialGradient`, `stop`, `clipPath`, `mask`, `foreignObject` and `title'`) create
SVG elements under both commit modes, and a list row or arm rooted at one of them is an SVG element.
A row or arm rooted at `title'` is the exception: an SVG `title` under queued commits, and an HTML
`title` under synchronous commits.

Case-sensitive SVG names such as `viewBox`, `preserveAspectRatio` and `linearGradient` are written
as given; every other tag and attribute name is lowercase. Under Fable the module of the SVG tags
registers those names when it loads, so a module defining a template with them imports an SVG tag or
follows a module that does. [Element names](templates.md#element-names) has the full rules.

## Hooks behind the parts

The parts are written against three hooks of the view, usable without anime:

| Hook | Runs |
| --- | --- |
| `Hooks.motion (Func<_, _, _, _> (fun inst element context -> cleanup))` | once the element is placed, with `context.Hydrated`, `context.Inserted` and `context.ExitPending` |
| `Hooks.exit (Func<_, _, _, _> (fun inst element finish -> cancel))` | when the row or arm is retired; the element leaves when `finish ()` is called |
| `Hooks.reflow (fun inst element -> { Before = ...; After = ... })` | on a region's parent: `Before` ahead of the first region pass in a flush, `After` once the pass is in place |

The hooks take `System.Func` delegates. F# converts a lambda to a delegate that returns a function
only when written `Func<_, _, _, _> (fun ...)`; `reflow` returns a record and takes a plain lambda.
`Hooks.both (first, second)` combines two hook parts on one element, at most one of them other than
a motion hook. The server omits all three kinds.

## Examples

`examples/Ranvier.Motion` is a Vite app with five pages, each a sugar component tree run as a
CI test under jsdom and Playwright:

| Page | Shows |
| --- | --- |
| [kanban](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/src/Kanban.fs) | `presence` cards, `flip` across columns, `drag` with drop to a column and a spring back to the card's slot, undo |
| [dashboard](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/src/Dashboard.fs) | `follow` gauges, a `draw` and `morph` line chart, a number tween through `Reactive.track`, a boundary skeleton |
| [story](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/src/Story.fs) | `scroll` timelines, a `split` headline, a staggered grid, `appear` tiles; rendered on .NET as a whole document by `Server.renderDocument` under `Omit`, and hydrated |
| [physics](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/src/Physics.fs) | a thrown `drag` puck and a spring home, chained `follow`, a timeline scrubbed by `Reactive.scrub`, a policy toggle |
| [routes](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/src/Routes.fs) | hash routing with `presence` page arms and `flip` on the shell |

The kanban and physics springs run in a `Motion.slot` given to their draggables as the
`DragBinding`'s `Slot` (see [animations from handlers](#animations-from-handlers)), and a card
dropped into another column starts its FLIP at the drop with `Motion.flipFrom`.

Run them from `examples/Ranvier.Motion` with `npm install`, then `npm run dev`.
