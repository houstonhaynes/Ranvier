/// The F# samples of docs/content/view/testing.md, in page order; a sample that is an excerpt is wrapped in a function.
module ViewDumps

open Ranvier.View.Dom
open Ranvier.View.Twin
open Counter

let dumpCounter () =
    use session = Twin.mount counter { Start = 3; Label = "Clicks" }
    session.Dump { Debug.DumpOptions.defaults with MaxRows = 12; MaxChars = 4096 }
