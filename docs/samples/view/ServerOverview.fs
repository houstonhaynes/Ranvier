/// The F# samples of docs/content/server/index.md, in page order; a sample that is an excerpt is wrapped in a function.
module ServerOverview

open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Sugar
open Counter

let overview (graph: Graph) (report: exn list -> unit) host (props: CounterProps) =
    // Server
    let html = Server.render { Server.Options.defaults with Policy = Omit } counter { Start = 0; Label = "Clicks" }
    // <!--[r--><div class="counter"><span class="label">Clicks</span><button type="button">0</button></div><!--]-->

    // Client
    let mounted = Hydration.hydrate graph None report Hydration.HydrationOptions.defaults host counter props
    html, mounted
