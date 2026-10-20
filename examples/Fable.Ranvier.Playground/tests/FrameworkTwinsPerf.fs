/// <summary>Explicit twins of the record-declared and class-declared benchmark rows, written with <c>Component.define</c>. The <c>explicit-combined</c> bundle entry holds them beside <c>FrameworkPerf</c>'s row.</summary>
module FrameworkTwinsPerf

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open FrameworkPerf

type PerfRowMembersTwinProps =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
    }

type PerfRowMembersTwin =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
        Local: Signal<int>
    }

let private perfRowMembersTwin =
    Template.Component.define
        (fun _ (props: PerfRowMembersTwinProps) ->
            {
                Index = props.Index
                Source = props.Source
                Disabled = props.Disabled
                Local = Api.createSignal 0
            })
        (fun t ->
            t.div
                [|
                    t.Attrs.id (fun r -> "row-" + string r.Index)
                    t.Attrs.className "panel"
                    t.Attrs.title (t.from (fun r -> r.Source))
                |]
                [|
                    t.input [| t.Props.Input.value (t.from (fun r -> r.Source)) |]
                    t.button
                        [|
                            t.Props.Button.disabled (t.from (fun r -> r.Disabled))
                            t.Events.onClick (fun r _ -> r.Local.Value <- r.Local.Value + 1)
                        |]
                        [| t.text (fun r -> string r.Local.Value) |]
                |])

type PerfRowClassTwinProps =
    {
        Index: int
        Source: Signal<string>
        Disabled: Memo<bool>
    }

type PerfRowClassTwin(setup: Setup, props: PerfRowClassTwinProps) =
    member _.Index = props.Index
    member _.Source = props.Source
    member _.Disabled = props.Disabled
    member val Local = Api.createSignal 0

let private perfRowClassTwin =
    Template.Component.define (fun setup props -> PerfRowClassTwin (setup, props)) (fun t ->
        t.div
            [|
                t.Attrs.id (fun (r: PerfRowClassTwin) -> "row-" + string r.Index)
                t.Attrs.className "panel"
                t.Attrs.title (t.from (fun (r: PerfRowClassTwin) -> r.Source))
            |]
            [|
                t.input [| t.Props.Input.value (t.from (fun (r: PerfRowClassTwin) -> r.Source)) |]
                t.button
                    [|
                        t.Props.Button.disabled (t.from (fun (r: PerfRowClassTwin) -> r.Disabled))
                        t.Events.onClick (fun (r: PerfRowClassTwin) _ -> r.Local.Value <- r.Local.Value + 1)
                    |]
                    [| t.text (fun (r: PerfRowClassTwin) -> string r.Local.Value) |]
            |])

/// <summary>Mounts equivalent rows as instances of the record row's explicit twin.</summary>
let mountTemplateMembersTwin (host: HTMLElement) width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        perfRowMembersTwin
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width

/// <summary>Mounts equivalent rows as instances of the class row's explicit twin.</summary>
let mountTemplateClassTwin (host: HTMLElement) width =
    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        perfRowClassTwin
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width
