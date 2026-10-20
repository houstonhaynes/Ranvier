/// <summary>The allocation sampler's positive control: each mount defines the record-declared row afresh, so the sugar view is built inside the mount.</summary>
module SugarAllocControl

open global.Ranvier
open Fable.Core.TS.Dom
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open FrameworkPerf
open FrameworkMembersPerf

/// <summary>Mounts rows of a definition of <c>PerfRowMembers</c> created by this call.</summary>
let mountFreshDefinition (host: HTMLElement) width =
    let definition = Component.ofMembers<PerfRowMembers, _>()

    mountCore
        (fun width source disabled ->
            Program.fragment (
                Array.init width (fun index ->
                    Template.Component.render
                        definition
                        {
                            Index = index + 1
                            Source = source
                            Disabled = disabled
                        })
            ))
        host
        width
