/// <summary>Nested Elmish loops: each component's setup owns its Mvu; props are read down, a callback reports up.</summary>
module ElmishNested

open global.Ranvier
open Ranvier.Elmish
open Ranvier.View
open Ranvier.View.Sugar

type ParentMsg =
    | Bump
    | Rename of string
    | Toggle
    | ChildReported of int

type ParentModel =
    {
        Step: int
        Label: string
        Shown: bool
        Last: int
    }

type ChildMsg =
    | Add
    | Tick

/// <summary>Counts and dispatch handles recorded by the fixtures.</summary>
module Loops =
    let mutable childSetups = 0
    let mutable stepReads = 0
    let mutable childUpdates = 0
    /// <summary>The dispatch kept by a child's initial command, standing in for a timer or subscription.</summary>
    let mutable held: ChildMsg -> unit = ignore
    let mutable parentDispatch: ParentMsg -> unit = ignore

type ChildModel = { Sum: int }

type ChildProps =
    { Step: Memo<int>; Report: int -> unit }

type MvuChild =
    {
        Loop: Mvu<ChildModel, ChildMsg>
        Total: Memo<int>
        StepOf: Memo<int>
    }

    static member Create(_: Setup, props: ChildProps) =
        Loops.childSetups <- Loops.childSetups + 1

        let update msg model =
            Loops.childUpdates <- Loops.childUpdates + 1

            match msg with
            | Add ->
                let sum = model.Sum + props.Step.Value
                { Sum = sum }, [ fun _ -> props.Report sum ]
            | Tick -> { Sum = model.Sum + 100 }, []

        let loop =
            Mvu.withCmd ({ Sum = 0 }, [ fun dispatch -> Loops.held <- dispatch ]) update

        {
            Loop = loop
            Total = loop.Select (fun m -> m.Sum)
            StepOf = props.Step
        }

    static member View =
        div (Attrs.className "child") {
            span (Attrs.className "step") {
                fun c ->
                    Loops.stepReads <- Loops.stepReads + 1
                    string c.StepOf.Value
            }

            span (Attrs.className "total") { fun c -> string c.Total.Value }

            button (Attrs.className "add", Attrs.attribute ("type", "button"), Events.onClick (fun c _ -> c.Loop.Dispatch Add)) { "Add" }
        }

let mvuChild = Component.ofMembers<MvuChild, _>()

type MvuParent =
    {
        Loop: Mvu<ParentModel, ParentMsg>
        CurrentStep: Memo<int>
        CurrentLabel: Memo<string>
        Visible: Memo<bool>
        LastReport: Memo<int>
    }

    static member Create(_: Setup, _: unit) =
        let loop =
            Mvu.create
                {
                    Step = 1
                    Label = "a"
                    Shown = true
                    Last = 0
                }
                (fun msg model ->
                    match msg with
                    | Bump -> { model with Step = model.Step + 1 }
                    | Rename label -> { model with Label = label }
                    | Toggle -> { model with Shown = not model.Shown }
                    | ChildReported sum -> { model with Last = sum })

        Loops.parentDispatch <- loop.Dispatch

        {
            Loop = loop
            CurrentStep = loop.Select (fun m -> m.Step)
            CurrentLabel = loop.Select (fun m -> m.Label)
            Visible = loop.Select (fun m -> m.Shown)
            LastReport = loop.Select (fun m -> m.Last)
        }

    static member View =
        section (Attrs.className "parent") {
            span (Attrs.className "label") { fun p -> p.CurrentLabel.Value }
            span (Attrs.className "last") { fun p -> string p.LastReport.Value }

            button (Attrs.className "bump", Attrs.attribute ("type", "button"), Events.onClick (fun p _ -> p.Loop.Dispatch Bump)) { "Bump" }

            show
                (fun p -> p.Visible.Value)
                (render mvuChild (fun p ->
                    {
                        Step = p.CurrentStep
                        Report = fun sum -> p.Loop.Dispatch (ChildReported sum)
                    }))
        }

let mvuParent = Component.ofMembers<MvuParent, _>()
