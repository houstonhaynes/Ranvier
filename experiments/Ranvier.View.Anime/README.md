# Motion parts

`Ranvier.View.Anime` animates the elements of a sugar template with [anime.js](https://animejs.com) 4.5.0 through the typed bindings of `src/Fable.Ranvier.Anime`. Each `Motion.*` function returns a `Part<'native, 'inst>`, given to a tag beside its attributes and events. Every part creates its anime objects once the element is placed, and the instance's disposal reverts them. Enter and exit follow the region passes of lists and arms, FLIP follows the reflow of a list's container, and signals drive the reactive parts. The server omits every part, so a portable template with motion renders the same HTML as its twin without it. The design is in the [motion spec](../../docs/superpowers/specs/2026-10-21-motion-design.md); the runtime seam the parts use is in the [seam note](../../docs/research/2026-10-21-motion-seam.md).

## Opening the parts

```fsharp
open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Ranvier.View.Anime
```

Every sample on this page assumes these lines; the [view sugar reference](../Ranvier.View.Portable/README.md) covers the first six. `Motion` requires qualified access: its `exit` would otherwise shadow FSharp.Core's.

**A play function receives the element as `Element`.** `Targets` converts from `Element` and `Element array` only, so a play function written `fun _ el -> Anime.animate (el, Anim (...))` type-checks on every tag.

**Anime objects are created inside play functions.** A template is built where its definition is bound, also on .NET, where the `[<Emit>]` and `[<Import>]` members of the bindings raise. The parameter constructors (`Anim`, `LayoutParams`, `AnimatableParams` and the others) are inert on .NET and may appear at definition time; `Anime.animate`, `Value.stagger`, `Ease.spring` and `Svg.morphTo` belong inside a play function.

## Lifecycle parts

| Part | Plays |
|---|---|
| `Motion.on play` | on every placement, hydration included |
| `Motion.appear play` | on every placement of an element created on the client; an element adopted from server HTML stays as served |
| `Motion.enter play` | when the element's row or arm is created by a region pass after the region's first |
| `Motion.exit play` | when the element's row or arm is retired; the element leaves when the tickable completes |
| `Motion.presence enter exit` | `enter` and `exit` on one element, with the leaving element pinned out of flow |
| `Motion.fade`, `Motion.rise`, `Motion.scale` | presence presets: opacity, opacity with a 12 px rise, opacity with a 92 % scale; 240 ms in, 180 ms out |
| `Motion.collapse` | presence preset: the height unfolds from 0 and folds back to 0, in flow, with the overflow hidden while it plays |
| `Motion.flip parameters` | FLIP for the rows of every region under the element |

```fsharp
type Card = { Id: int; Title: string }

type Board =
    {
        Cards: Signal<Card array>
        Selected: Signal<bool>
    }

let board =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal false
            })
        (section () {
            ul (Attrs.className "cards", Motion.flip (LayoutParams (duration = 350, ease = Ease.outQuart))) {
                forEach
                    (fun card -> card.Id)
                    (fun b -> b.Cards.Value)
                    (li (
                        Motion.presence
                            (fun _ el -> Anime.animate (el, Anim (opacity = Value.fromTo (0, 1), y = Value.fromTo (12, 0), duration = 240)))
                            (fun _ el -> Anime.animate (el, Anim (opacity = 0, scale = 0.9, duration = 180)))
                    ) {
                        fun current -> (current ()).Title
                    })
            }

            show (fun b -> b.Selected.Value) (p (Motion.fade) { "Selected" })
        })
```

`on` and `appear` take any `IRevertible`, such as an `Animatable`; `enter`, `exit` and `presence` take a tickable, `JSAnimation` or `Timeline`:

```fsharp
type Banner = { Text: string }

let banner =
    Component.template
        (fun _ (text: string) -> { Text = text })
        (header () {
            h1 (Motion.appear (fun _ el -> Anime.animate (el, Anim (opacity = Value.fromTo (0, 1), duration = 400)))) { fun b -> b.Text }

            span (Motion.on (fun _ el -> Anime.animate (el, Anim (rotate = 360, duration = 1200, loop = Loop.forever, ease = Ease.linear)))) { "*" }
        })
```

`enter` and `exit` are separate parts, and an element takes both, in flow:

```fsharp
type Toast = { Messages: Signal<string array> }

let toasts =
    Component.template
        (fun _ () -> { Messages = Api.createSignal [||] })
        (ol () {
            forEach
                id
                (fun (t: Toast) -> t.Messages.Value)
                (li (
                    Motion.enter (fun _ el -> Anime.animate (el, Anim (x = Value.fromTo (40, 0), duration = 200))),
                    Motion.exit (fun _ el -> Anime.animate (el, Anim (x = 40, opacity = 0, duration = 200)))
                ) {
                    fun message -> message ()
                })
        })
```

## Enter and exit rules

- **Enter plays for rows and arms created after the region's first pass.** Rows of the first pass, hydrated rows and rows nested inside an entering row stay still; an arm shown by `show` turning true enters.
- **An exit applies to a row or arm root element.** The retired row's element stays in place, with its content, until the exit's tickable completes, and then leaves. Reorders and inserts while it plays place the live rows around it. A Debug build raises "An exit hook requires a row or arm root element" for an exit elsewhere.
- **The exit starts from the current values.** The instance is disposed before its row's nodes leave. On an element whose exit follows, and on every element inside it, `on`, `appear`, `enter` and the reactive parts pause their animations instead of reverting them; anime's default composition, `replace`, then hands each animated property to the exit. `drag` disables its draggable and `split` and `scroll` stop their observers, so nothing of the row stays live once it leaves.
- **Disposing the mount ends every running exit** at once: the exit's tickable is cancelled and the element leaves. An exit that has not completed after 10 s is cancelled and its element leaves.
- **`presence` pins the leaving element** with `position: absolute` at its current box, its offset box, with no margin, so the rows after it close the gap while it plays. The box is relative to the element's offset parent: give the list's container `position: relative`. `collapse` and a separate `exit` stay in flow.

## FLIP

`Motion.flip parameters` on a list's container wraps anime's `createLayout`. Before the first region pass under the container in a flush it records the layout of the container's element children, and once the pass's nodes and commits are in place it animates them from the recorded layout to the new one. A pass with no recorded layout before it, such as the first, does not animate.

- **The rows are the container's direct element children.** `parameters`' `children` is replaced by those children, measured at both moments. A row whose Motion exit has started, or whose Motion enter started in the pass, is left out of that pass's measurements.
- **Motion parts keep the visual properties.** In a pass where an element under the container carries a Motion part, the layout animates position and size only, and the opacity, colours, borders, filters, clip paths and font size stay with the parts (an opacity `enter` or `presence` under `flip` ends at its last frame). In a pass with no such element, anime's default fades apply. A property named in `properties`, `enterFrom`, `leaveTo` or `swapAt` is always the layout's.
- **The container is positioned.** anime animates moved rows with `position: absolute` and a translation through the Web Animations API during the animation.
- **The layout is created on the first recording,** and reverted with the instance.

## Reactive parts

| Part | Drives |
|---|---|
| `Motion.follow property parameters read` | an `Animatable` property of the element towards `read`'s value whenever it changes; the first value is set at once |
| `Motion.animateOn read play` | a new tickable from `play` with `read`'s value whenever it changes, replacing the running one's tweens of the same properties; the first plays to its end at once |
| `Motion.draw parameters read` | the stroke of an SVG geometry element, drawn from its start to the fraction `read` returns |
| `Motion.morph parameters read` | the `d` (path) or `points` (polygon, polyline) of an SVG shape towards the shape `read` returns; each completed morph leaves exactly that shape |
| `Motion.scroll parameters bind play` | `play`'s tickable linked to an `onScroll` observer of the element (or of `parameters`' target); the observer's progress and in-view state written into the signals of a `ScrollBinding` |
| `Motion.drag parameters bind` | a draggable element, two-way through a `DragBinding`: dragging, and the end of a release's motion, write `Position`, and a write to `Position` from elsewhere moves the element with its update callback muted, so the write is not echoed |
| `Motion.split parameters play` | anime's `splitText` over the element's static text, and `play` over its lines, words or characters |

```fsharp
type Gauge =
    {
        Load: Signal<float>
        Label: Signal<string>
    }

let gauge =
    Component.template
        (fun _ (load: float) ->
            {
                Load = Api.createSignal load
                Label = Api.createSignal "idle"
            })
        (div () {
            div (Attrs.className "bar", Motion.follow "scaleX" (AnimatableParams (duration = 400, ease = Ease.outQuart)) (fun g -> g.Load.Value)) {
                ()
            }

            span (Motion.animateOn (fun g -> g.Label.Value) (fun _ el _ -> Anime.animate (el, Anim (opacity = Value.fromTo (0, 1), duration = 200)))) {
                fun g -> g.Label.Value
            }
        })
```

`follow`'s `parameters` without an entry for `property` give the property the shared duration and ease.

```fsharp
type Story =
    {
        Progress: Signal<float>
        Seen: Signal<bool>
        Puck: Signal<struct (float * float)>
    }

let story =
    Component.template
        (fun _ () ->
            {
                Progress = Api.createSignal 0.
                Seen = Api.createSignal false
                Puck = Api.createSignal (struct (0., 0.))
            })
        (article () {
            h2 (
                Motion.split (SplitParams (chars = true)) (fun _ split ->
                    Anime.animate (split.chars, Anim (y = Value.fromTo ("100%", "0%"), delay = Value.stagger 20)))
            ) {
                "Ranvier"
            }

            section (
                Motion.scroll
                    (ScrollParams (enter = "bottom top", leave = "top bottom", sync = true))
                    (fun s ->
                        {
                            Progress = ValueSome s.Progress
                            InView = ValueSome s.Seen
                        })
                    (fun _ el -> Anime.animate (el, Anim (opacity = Value.fromTo (0.2, 1), autoplay = false)))
            ) {
                fun s -> sprintf "%.0f %%" (s.Progress.Value * 100.)
            }

            div (
                Attrs.className "puck",
                Motion.drag (DraggableParams (container = "article")) (fun s ->
                    {
                        Position = s.Puck
                        Dragging = ValueNone
                    })
            ) {
                "drag"
            }
        })
```

**`split` takes static text.** A text slot or region inside the split element stops updating once its text is split. `splitText`, `onScroll` and `createDraggable` require `ResizeObserver`, and the draggable `DOMPoint` and `DOMMatrix`.

**`draw` and `morph` need SVG elements.** The SVG tags of the sugar (`svg`, `g`, `path`, `circle` and the others listed in the [sugar reference](../Ranvier.View.Portable/README.md#tags)) create SVG elements under both commit modes:

```fsharp
type Chart =
    {
        Fraction: Signal<float>
        Line: Signal<string>
    }

let chart =
    Component.template
        (fun _ () ->
            {
                Fraction = Api.createSignal 0.
                Line = Api.createSignal "M0 0 L100 0"
            })
        (svg () {
            path (Motion.draw (AnimatableParams (duration = 300)) (fun c -> c.Fraction.Value)) { () }
            path (Motion.morph (Anim (duration = 300)) (fun c -> c.Line.Value)) { () }
        })
```

A list row or arm rooted at an SVG tag is an SVG element too, under both commit modes, and SVG names such as `viewBox` are written as given.

## Reduced motion

`Reactive.policy ()` is the graph's motion policy, seeded from `prefers-reduced-motion` and writable. Under `Reduced`, `enter` and the presets' enters do not play, an exit finishes at once (its element leaves in the pass that retires its row), and `flip` neither records nor animates. `on`, `appear` and the reactive parts play unchanged; a play function that should honour the policy reads `Reactive.policy ()` itself.

## Server rendering and hydration

The three hook kinds behind the parts (motion, exit and reflow) add no markup, and `Server.render` omits them under both `Reject` and `Omit`: the server HTML of a template with Motion parts equals that of its twin without them (`fable/Ranvier.View.Anime.Tests/ServerChecks.fs`, run with `dotnet run`). Hydration adopts the served elements; `appear` and `enter` leave them as served, and `on` plays.

## Costs

**Bundle,** rolldown (minified) and gzip level 9, each entry mounting one keyed list (`docs/research/motion/bundle.json`, regenerated by `fable/Ranvier.View.Anime.Tests/motion-size.mjs`):

| Entry | Against | Δ gzip | Budget |
|---|---|--:|--:|
| rows with `Motion.presence` | the same rows with `Hooks.motion` and `Hooks.exit` calling `animate` by hand | +679 B | 900 B |
| a list with `Motion.flip` | the same list with `Hooks.reflow` calling `createLayout` by hand | +416 B | 600 B |

An entry using one Motion part declares no anime feature class (`AutoLayout`, `Draggable`, `ScrollObserver`, `TextSplitter`, `WAAPIAnimation`, `Timeline`) beyond those of the anime functions that part calls. The emitted module holds no `curry`, `uncurry`, `partialApply` or `createObj`.

**Per instance:** each part adds one placed hook and the anime objects it creates; `follow`, `animateOn`, `draw`, `morph` and `drag` add one core effect.
