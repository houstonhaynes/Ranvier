---
title: Elmish components
---

Create an [`Mvu`](../guide/elmish.md) in component setup and keep its dispatch and selectors in the
instance. Disposal stops the loop. Nest independent loops with two channels:

- **Props down.** The parent passes the child a memo or reader over its own model.
- **Callbacks up.** The child keeps its own `Msg` type and reports to the parent through a callback in
  its props. The parent handles one message for the report; child messages stay in the child.

The samples use the opens from [Templates and bindings](templates.md) and `open Ranvier.Elmish`.

## The child

```fsharp
type StepperMsg =
    | Add
    | Clear

type StepperProps = { Step: Memo<int>; Reported: int -> unit }

type Stepper =
    { Loop: Mvu<int, StepperMsg>; Total: Memo<int>; Step: Memo<int> }

    static member Create(_: Setup, props: StepperProps) =
        let update msg total =
            match msg with
            | Add ->
                let next = total + props.Step.Value
                next, [ fun _ -> props.Reported next ]
            | Clear -> 0, []

        let loop = Mvu.withCmd (0, []) update
        { Loop = loop; Total = loop.Select id; Step = props.Step }

    static member View =
        div (Attrs.className "stepper") {
            span () { fun s -> "+" + string s.Step.Value }
            span () { fun s -> string s.Total.Value }
            button (Events.onClick (fun s _ -> s.Loop.Dispatch Add)) { "Add" }
            button (Events.onClick (fun s _ -> s.Loop.Dispatch Clear)) { "Clear" }
        }

let stepper = Component.ofMembers<Stepper, _> ()
```

The `Add` command calls `props.Reported`, which is how the child tells the parent about a new total.

## The parent

```fsharp
type PageMsg =
    | Bigger
    | Toggle
    | StepperReported of int

type PageModel = { Step: int; Shown: bool; Last: int }

type Page =
    { Loop: Mvu<PageModel, PageMsg>
      CurrentStep: Memo<int>
      Shown: Memo<bool>
      Last: Memo<int> }

    static member Create(_: Setup, _: unit) =
        let loop =
            Mvu.create { Step = 1; Shown = true; Last = 0 } (fun msg model ->
                match msg with
                | Bigger -> { model with Step = model.Step + 1 }
                | Toggle -> { model with Shown = not model.Shown }
                | StepperReported total -> { model with Last = total })

        { Loop = loop
          CurrentStep = loop.Select _.Step
          Shown = loop.Select _.Shown
          Last = loop.Select _.Last }

    static member View =
        section () {
            p () { fun p -> "Last report: " + string p.Last.Value }
            button (Events.onClick (fun p _ -> p.Loop.Dispatch Bigger)) { "Bigger steps" }
            button (Events.onClick (fun p _ -> p.Loop.Dispatch Toggle)) { "Show or hide" }

            show
                (fun p -> p.Shown.Value)
                (render stepper (fun p -> { Step = p.CurrentStep; Reported = fun total -> p.Loop.Dispatch (StepperReported total) }))
        }

let page = Component.ofMembers<Page, _> ()
```

**Try it:** click **Add** to reach total 1 and report 1. Click **Bigger steps**, then **Add**: total
and report become 3. **Clear** resets the child only; the parent keeps its last report. Hiding and
showing the child resets its total to 0.

`CurrentStep` cuts off unchanged `Step` values, so `StepperReported` does not re-run the child's
bindings through that selector.

## Lifetime

- Create the loop in setup: `Create`, a class constructor or a `Component.template` setup.
  `Mvu.create` inside a pure body, such as a memo, raises `InvalidOperationException`.
- An `Mvu` belongs to the scope current when it was created. Once the instance is disposed, its
  `Dispatch`, and the `dispatch` its commands received, drop every message. Hiding the stepper above
  disposes its loop, and a message from a timer that still holds the stepper's `dispatch` is dropped.
- Showing the stepper again sets up a new instance with a new loop and a total of 0.

## On the server

Elmish components render on the server like any other: `Create` runs, `init`'s commands run, and the
template is written. A command that dispatches later leaves the HTML as written.
[Hydration](../server/hydration.md) runs `Create` again on the client.

[Migrating from Elmish](../guide/elmish.md) covers `Mvu`, commands and selector costs, and the view sugar
README's [Nested Elmish](https://github.com/shayanhabibi/Ranvier/blob/master/src/Ranvier.View/README.md#nested-elmish)
section states the pattern with its disposal rules.
