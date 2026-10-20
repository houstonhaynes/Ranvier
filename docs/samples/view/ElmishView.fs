/// The F# samples of docs/content/view/elmish.md, in page order; a sample that is an excerpt is wrapped in a function.
module ElmishView

open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open Ranvier.Elmish

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
