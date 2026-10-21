/// The F# samples of docs/content/view/testing.md, in page order; a sample that is an excerpt is wrapped in a function.
module CounterHtml

open Ranvier.View
open Counter

let private omit = { Server.Options.defaults with Policy = Omit }

let rendersStartValue () =
    let html = Server.render omit counter { Start = 3; Label = "Cups" }

    let expected =
        """<!--[r--><div class="counter"><span class="label">Cups</span><button type="button">3</button></div><!--]-->"""

    if html <> expected then
        failwithf "Expected %s; got %s" expected html
