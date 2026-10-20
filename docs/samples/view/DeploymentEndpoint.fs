/// The endpoint sample of docs/content/server/deployment.md, without its entry-point attribute, which types `args`.
module DeploymentEndpoint

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open ServerPage

let main (args: string array) =
    let app = WebApplication.CreateBuilder(args).Build ()

    app.UseStaticFiles () |> ignore

    app.MapGet ("/", Func<IResult> (fun () -> Results.Content (page { Start = 0; Label = "Clicks" }, "text/html")))
    |> ignore

    app.Run ()
    0
