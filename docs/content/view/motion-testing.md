---
title: Testing and costs
description: Deterministic animation tests on a manual anime clock under jsdom and Playwright, and the bundle cost of the bindings and Motion parts.
---

## A manual clock under jsdom

Install DOM globals and mock `Date` **before importing anime**: it captures `Date.now`,
`requestAnimationFrame` and browser detection at import time. Then load the compiled F# checks:

```js
import test, { mock } from "node:test";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><body></body>");
globalThis.window = dom.window;
globalThis.document = dom.window.document;
for (const name of ["Node", "Element", "HTMLElement", "SVGElement", "NodeList", "HTMLCollection", "getComputedStyle"])
  globalThis[name] = dom.window[name];
globalThis.requestAnimationFrame = dom.window.requestAnimationFrame = () => 0;
mock.timers.enable({ apis: ["Date"], now: 1000 });

const { checks } = await import("./generated/Checks.js");
for (const [name, check] of checks) test(name, () => check(ms => mock.timers.tick(ms)));
```

Each F# check advances mocked time, ticks the engine and asserts the resulting style:

```fsharp
open Fable.Core
open Ranvier.Anime

[<Emit("document.body.appendChild(document.createElement($0))")>]
let createElement (tag: string) : TS.Dom.Element = jsNative

[<Emit("$0.style.opacity")>]
let opacityOf (element: TS.Dom.Element) : string = jsNative

/// Advances the mocked clock by the given milliseconds.
type Clock = float -> unit

let linearFade (advance: Clock) =
    Testing.manual ()
    let card = createElement "div"
    Anime.animate (card, Anim (opacity = Value.fromTo (0, 1), duration = 1000, ease = Ease.linear)) |> ignore

    let samples =
        [
            for step in 0..4 do
                advance (if step = 0 then 0. else 250.)
                Testing.step ()
                opacityOf card
        ]

    if samples <> [ "0"; "0.262"; "0.512"; "0.762"; "1" ] then
        failwithf "Unexpected opacities %A" samples

let checks = [| "a linear fade reads the same values every run", linearFade |]
```

- `Testing.manual ()` sets `Engine.settings.useDefaultMainLoop` and `pauseOnDocumentHidden` to
  false, which leaves every tick to `Testing.step ()`.
- `Testing.step ()` advances every running tickable to the current mocked time.
- Each read runs 12 ms ahead of the mocked time: anime starts a resumed tickable 12 ms early to force
  a frame on the next tick, so the fade above reads 0.262 after 250 ms, every run.
- `Reactive.policy ()` is `Full` under jsdom, which has no `matchMedia`; a test writes the signal to
  check the `Reduced` paths.

jsdom has no layout and no Web Animations. `animate`, `Utils.set`, `revert`, animatables, timelines
and a layout's `record ()` run as in a browser. `splitText`, `onScroll` and `createDraggable` need
stubs of `ResizeObserver`, `DOMPoint` and `DOMMatrix`, and a FLIP layout's transforms, which run
through the Web Animations API, do not move.
[`examples/Ranvier.Motion/tests/pages.test.mjs`](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/tests/pages.test.mjs)
holds the full set of globals and stubs the example checks run under.

## Playwright

In a browser, `page.clock` controls the same two inputs. Install the clock, paused, before the page's
scripts run, so anime captures the controlled `Date` and frame callback:

```js
const start = new Date("2026-10-21T09:00:00Z").getTime();
await page.clock.install({ time: start });
await page.clock.pauseAt(start + 1);
await page.goto("/kanban/");
await page.clock.runFor(120);   // samples an animation 120 ms in
```

`page.clock.runFor (ms)` moves every anime animation by `ms`. The Web Animations API runs on the
browser's own timeline, outside the page clock: a FLIP test asserts that `element.getAnimations ()`
holds the layout's animation, calls `finish ()` on it, then asserts the final boxes.
[`examples/Ranvier.Motion/tests/support.mjs`](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/tests/support.mjs)
holds these steps.

## Server checks

A portable template with Motion parts renders on .NET with `Server.render`. Comparing that HTML with
the render of a twin without the parts checks that the parts leave the markup unchanged, as the
[server checks of the Motion parts](https://github.com/shayanhabibi/Ranvier/blob/master/fable/Ranvier.View.Anime.Tests/ServerChecks.fs)
do under the default `Reject` policy. A page with event handlers renders under `Omit`: the
[story example's check](https://github.com/shayanhabibi/Ranvier/blob/master/examples/Ranvier.Motion/Server/Program.fs)
renders the whole page and its twin with `Server.renderDocument` and fails when the documents differ.

## Bundle costs

Measured with rolldown (minified) and gzip level 9 on anime 4.5.0, importing from `animejs`. anime's
root imports tree-shake, so a bundle holds the features it calls:

| Entry | gzip |
| --- | --: |
| `animate` | 12.2 kB |
| `createTimeline` | 13.4 kB |
| `createAnimatable` | 12.6 kB |
| `createLayout` | 20.6 kB |
| `createDraggable` | 19.5 kB |
| `onScroll` + `animate` | 16.5 kB |
| `splitText` | 3.3 kB |
| `waapi.animate` | 4.5 kB |
| `spring` + `animate` | 13.1 kB |
| everything | 41.0 kB |

What Ranvier adds, from the committed measurements
([`bindings-bundle.json`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/research/motion/bindings-bundle.json),
[`bundle.json`](https://github.com/shayanhabibi/Ranvier/blob/master/docs/research/motion/bundle.json)),
each checked for drift in CI:

| Entry | Against | Δ gzip | Budget |
| --- | --- | --: | --: |
| `Anime.animate (el, Anim (...))` | the same call written in JavaScript | 0 B | 64 B |
| rows with `Motion.presence` | the same rows with `Hooks.motion` and `Hooks.exit` calling `animate` by hand | +732 B | 900 B |
| a list with `Motion.flip` | the same list with `Hooks.reflow` calling `createLayout` by hand | +580 B | 600 B |
| the view's motion, exit and reflow hook kinds | the view before them, in a bundle using none of them | +447 B | 450 B |

The last row is the Playground's `sugar-size.mjs` explicit entry, recorded in the
[motion seam note](https://github.com/shayanhabibi/Ranvier/blob/master/docs/research/2026-10-21-motion-seam.md#bundle-delta):
every view bundle carries it, with or without animation. SVG element creation, the element-name
checks and the client element refusals add a few hundred bytes more on the same entry; the
[motion research note](https://github.com/shayanhabibi/Ranvier/blob/master/docs/research/2026-10-21-motion.md#motion-parts-steps-89)
records each measurement.

- An entry using one Motion part declares exactly the anime feature classes of the functions that part calls
  (`flip` brings `AutoLayout`, `WAAPIAnimation` and `Timeline`; `scroll`
  `ScrollObserver`; `drag` `Draggable`; `split` `TextSplitter`; the lifecycle and reactive parts none).
- The emitted `Ranvier.View.Anime` and `Fable.Ranvier.Anime` modules hold no `curry`, `uncurry`,
  `partialApply` or `createObj`.
- Per instance, each part adds one placed hook and the anime objects it creates; `follow`,
  `animateOn`, `draw`, `morph` and `drag` add one core effect, and a slot adds one cleanup.
