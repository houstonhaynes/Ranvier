module HydrationOxpecker

open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Oxpecker

let private page: EndpointHandler =
    fun ctx ->
        let props =
            HydrationPage.fromQuery
                (ctx.TryGetQueryValue "greeting" |> Option.defaultValue "")
                (ctx.TryGetQueryValue "start" |> Option.defaultValue "")

        ctx.Response.ContentType <- "text/html; charset=utf-8"
        ctx.Response.WriteAsync (HydrationPage.render props)

/// <summary>Serves the shared hydration example at port 5081; the optional argument is its built dist directory.</summary>
[<EntryPoint>]
let main args =
    let dist =
        (if args.Length > 0 then args[0] else Path.Combine (__SOURCE_DIRECTORY__, "../../../../examples/Ranvier.Hydration/dist"))
        |> Path.GetFullPath

    use assets = new PhysicalFileProvider (Path.Combine (dist, "assets"))
    let builder = WebApplication.CreateBuilder ()
    builder.Services.AddRouting().AddOxpecker () |> ignore
    use app = builder.Build ()
    app.Urls.Add "http://127.0.0.1:5081"

    app.UseStaticFiles (StaticFileOptions (FileProvider = assets, RequestPath = PathString "/assets"))
    |> ignore

    let stylesheet: EndpointHandler =
        fun ctx ->
            ctx.Response.ContentType <- "text/css; charset=utf-8"
            ctx.Response.SendFileAsync (Path.Combine (dist, "style.css"))

    app.UseRouting().UseOxpecker ([ GET [ route "/" page; route "/style.css" stylesheet ] ])
    |> ignore

    app.Run ()
    0
