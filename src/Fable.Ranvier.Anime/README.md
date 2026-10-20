# Fable.Ranvier.Anime

Typed [anime.js](https://animejs.com) 4.5 bindings for Fable, with Ranvier integration. Install the
JavaScript package beside it:

```shell
npm install animejs@4.5.0
```

Parameters are constructors with named optional arguments. Each emits an object literal holding only
the arguments given, and every function is imported from `animejs` on its own, so a bundle carries
only the anime features it calls.

```fsharp
open Ranvier.Anime

let card (el: Fable.Core.TS.Dom.Element) =
    Anime.animate (el, Anim(x = 100, opacity = Value.fromTo (0, 1), duration = 400, ease = Ease.outQuart))
// animate(el, { x: 100, opacity: [0, 1], duration: 400, ease: "outQuart" })
```

`Ranvier.Anime.Reactive` ties anime objects to Ranvier's ownership tree and signals:

- `own` reverts an animation, timeline, draggable or layout when the current owner is disposed.
- `track` publishes a tickable's progress, running and completed state as memos.
- `scrub` seeks a tickable to a signal's progress; `drive` animates an animatable property to a signal's value.
- `policy` is a signal of the reduced-motion preference.

`Ranvier.Anime.Testing` steps anime's engine by hand: mock `Date` before `animejs` is imported, call
`Testing.manual ()`, then advance the mocked time and call `Testing.step ()`.

The assembly also compiles on .NET, so portable code can reference it; the bindings run only under Fable.
