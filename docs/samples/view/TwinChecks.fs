/// The F# samples of docs/content/view/testing.md, in page order; a sample that is an excerpt is wrapped in a function.
module TwinChecks

open Ranvier.View.Twin
open Ranvier.View.Testing
open Counter

let twinClickIncrements () =
    use session = Twin.mount counter { Start = 0; Label = "Clicks" }
    Interact.click session.Host "button"
    Interact.click session.Host "button"
    Expect.text session.Host "button" "2"
