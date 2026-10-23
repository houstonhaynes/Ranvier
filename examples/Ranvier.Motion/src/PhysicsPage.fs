/// <summary>The physics page's shared document declaration.</summary>
module PhysicsPage

open Ranvier.View
open Ranvier.View.Sugar

/// <summary>The physics playground and its browser controls.</summary>
let page =
    let capture () =
        ref Unchecked.defaultof<Physics.Playground>

    let view (state: Physics.Playground ref) =
        Component.template (fun _ () -> ()) (render Physics.view (fun () -> fun created -> state.Value <- created))

    PageLayout.declaration
        "physics"
        (fun () -> ())
        (PageLayout.pageView "physics" capture view (fun graph state -> Physics.activate graph state.Value))
        None
