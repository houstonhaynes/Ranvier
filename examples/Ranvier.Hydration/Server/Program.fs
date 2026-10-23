module Program

open System.IO
open Giraffe
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Ranvier.View

let private root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let private export () =
    Directory.CreateDirectory (Path.Combine (root, "generated-pages"))
    |> ignore

    File.WriteAllText (Path.Combine (root, "generated-pages", "Index.fs"), Page.clientSource "HydrationGenerated.Index" "HydrationPage.page")

    let developmentPage =
        { HydrationPage.page with
            Document =
                { HydrationPage.page.Document with
                    Scripts = [ Server.ModuleScript.Src "./generated/generated-pages/Index.js" ]
                }
        }

    File.WriteAllText (Path.Combine (root, "index.html"), Page.render developmentPage)

let private webApp: HttpHandler =
    choose
        [
            GET
            >=> route "/"
            >=> fun next ctx ->
                let props =
                    HydrationPage.fromQuery (string ctx.Request.Query["greeting"]) (string ctx.Request.Query["start"])

                htmlString (HydrationPage.render props) next ctx
            setStatusCode 404 >=> text "Not found"
        ]

[<EntryPoint>]
let main args =
    if args = [| "--write-pages" |] then
        export ()
    else
        let assets = Path.Combine (root, "dist")

        if not (File.Exists (Path.Combine (assets, "assets", "client.js"))) then
            failwith "Build the browser assets first: npm run build"

        let builder = WebApplication.CreateBuilder (args)
        builder.Services.AddGiraffe () |> ignore
        let app = builder.Build ()

        app.UseStaticFiles (StaticFileOptions (FileProvider = new PhysicalFileProvider (assets)))
        |> ignore

        app.UseGiraffe (webApp)
        app.Run ()

    0
