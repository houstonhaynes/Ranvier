/// <summary>The template benchmark row of <c>FrameworkPerf</c> written with the template sugar. Kept apart from <c>FrameworkPerf</c>; bundles of the explicit fixtures hold no sugar code.</summary>
module FrameworkSugarPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Sugar
open FrameworkPerf

let private perfRowSugar =
    Template.Component.define
        (fun _ (props: PerfProps) ->
            {
                Index = props.Index
                Source = props.Source
                Disabled = props.Disabled
                Local = Api.createSignal 0
            })
        (fun t ->
            t.Div (
                t.Attrs.id (fun (r: PerfRow) -> "row-" + string r.Index),
                t.Attrs.className "panel",
                t.Attrs.title (t.from (fun (r: PerfRow) -> r.Source))
            ) {
                t.Input (t.Props.Input.value (t.from (fun (r: PerfRow) -> r.Source)))

                t.Button (
                    t.Props.Button.disabled (t.from (fun (r: PerfRow) -> r.Disabled)),
                    t.Events.onClick (fun (r: PerfRow) _ -> r.Local.Value <- r.Local.Value + 1)
                ) {
                    fun (r: PerfRow) -> string r.Local.Value
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
