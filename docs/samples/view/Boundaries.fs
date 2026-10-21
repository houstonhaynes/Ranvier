/// The F# samples of docs/content/view/boundaries.md, in page order; a sample that is an excerpt is wrapped in a function.
module Boundaries

open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

type ProfileStore = { Name: AsyncSource<string> }

let private waiting = Component.template (fun _ () -> ()) (p (Attrs.className "loading") { "Loading…" })

let private failed =
    Component.template (fun _ (failure: BoundaryFailure) -> failure) (
        p (Attrs.className "error") {
            fun f -> f.Error.Message
            button (Events.onClick (fun f _ -> f.Retry ())) { "Retry" }
        }
    )

/// Builds the profile definitions over one store; call it once.
let profileView (store: ProfileStore) =
    let card =
        Component.template (fun _ (status: BoundaryStatus) -> status) (
            article (Attrs.data ("refreshing", fun s -> if s.IsPending () then Some "true" else None)) {
                h2 () { fun _ -> store.Name.Value }
            }
        )

    Component.template (fun _ () -> ()) (section () { boundary waiting failed card })
