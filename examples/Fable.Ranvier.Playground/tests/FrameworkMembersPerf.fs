/// <summary>The template benchmark row declared as a record with static Create and View. The module holds this row alone and is the <c>members</c> bundle entry's row.</summary>
module FrameworkMembersPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Sugar
open FrameworkPerf

/// <summary>The props of the record-declared row: the fields of <c>FrameworkPerf.PerfProps</c>.</summary>
type PerfRowMembersProps =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
    }

type PerfRowMembers =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
        Local: Signal<int>
    }

    static member Create(_: Setup, props: PerfRowMembersProps) =
        {
            Index = props.Index
            Source = props.Source
            Disabled = props.Disabled
            Local = Api.createSignal 0
        }

    static member View =
        div (Attrs.id (fun r -> "row-" + string r.Index), Attrs.className "panel", Attrs.title (from (fun r -> r.Source))) {
            input (Props.Input.value (from (fun r -> r.Source)))

            button (Props.Button.disabled (from (fun r -> r.Disabled)), Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)) {
                fun r -> string r.Local.Value
            }
        }

let private perfRowMembers = Component.ofMembers<PerfRowMembers, _>()

/// <summary>Mounts equivalent rows as instances of the record-declared row.</summary>
let mountTemplateMembers (host: HTMLElement) width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        perfRowMembers
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width
