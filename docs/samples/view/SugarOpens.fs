/// The F# samples of docs/content/view/sugar.md, in page order; a sample that is an excerpt is wrapped in a function.
module Sugar

open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar

let note = Component.template (fun _ () -> ()) (div () { "opened" })
