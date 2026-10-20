/// <summary>The template benchmark row declared as a class with a static View. The module holds this row alone and is the <c>class</c> bundle entry's row.</summary>
module FrameworkClassPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open FrameworkPerf

/// <summary>The props of the class-declared row: the fields of <c>FrameworkPerf.PerfProps</c>.</summary>
type PerfRowClassProps =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
    }

type PerfRowClass(setup: Setup, props: PerfRowClassProps) =
    member _.Index = props.Index
    member _.Source = props.Source
    member _.Disabled = props.Disabled
    member val Local = Api.createSignal 0

    static member View: TemplateView<PerfRowClass> =
        div (
            Attrs.id (fun (r: PerfRowClass) -> "row-" + string r.Index),
            Attrs.className "panel",
            Attrs.title (from (fun (r: PerfRowClass) -> r.Source))
        ) {
            input (Props.Input.value (from (fun (r: PerfRowClass) -> r.Source)))

            button (
                Props.Button.disabled (from (fun (r: PerfRowClass) -> r.Disabled)),
                Events.onClick (fun (r: PerfRowClass) _ -> r.Local.Value <- r.Local.Value + 1)
            ) {
                fun (r: PerfRowClass) -> string r.Local.Value
            }
        }

let private perfRowClass = Component.ofClass PerfRowClass

/// <summary>Mounts equivalent rows as instances of the class-declared row.</summary>
let mountTemplateClass (host: HTMLElement) width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        perfRowClass
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width
