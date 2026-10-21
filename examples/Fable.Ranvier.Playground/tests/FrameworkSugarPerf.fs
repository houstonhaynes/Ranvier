/// <summary>The template benchmark row of <c>FrameworkPerf</c> written with the template sugar. Kept apart from <c>FrameworkPerf</c>; bundles of the explicit fixtures hold no sugar code.</summary>
module FrameworkSugarPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Sugar
open FrameworkPerf

let private perfRowSugar =
    Component.template
        (fun _ (props: PerfProps) ->
            {
                Index = props.Index
                Source = props.Source
                Disabled = props.Disabled
                Local = Api.createSignal 0
            })
        (div (Attrs.id (fun r -> "row-" + string r.Index), Attrs.className "panel", Attrs.title (from (fun r -> r.Source))) {
            input (Props.Input.value (from (fun r -> r.Source)))

            button (Props.Button.disabled (from (fun r -> r.Disabled)), Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)) {
                fun r -> string r.Local.Value
            }
        })

/// <summary>Mounts equivalent rows as template component instances written with the template sugar.</summary>
let mountTemplateSugar (host: HTMLElement) width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        perfRowSugar
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width
