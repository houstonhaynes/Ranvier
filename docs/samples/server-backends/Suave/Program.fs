module HydrationSuave

open System.IO
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers

let private page: WebPart =
    fun ctx ->
        let query name =
            match ctx.request.queryParam name with
            | Choice1Of2 value -> value
            | Choice2Of2 _ -> ""

        let props = HydrationPage.fromQuery (query "greeting") (query "start")
        (OK (HydrationPage.render props) >=> setMimeType "text/html; charset=utf-8") ctx

/// <summary>Serves the shared hydration example at port 5082; the optional argument is its built dist directory.</summary>
[<EntryPoint>]
let main args =
    let dist =
        (if args.Length > 0 then args[0] else Path.Combine (__SOURCE_DIRECTORY__, "../../../../examples/Ranvier.Hydration/dist"))
        |> Path.GetFullPath

    let app =
        choose [
            GET
            >=> choose [
                path "/" >=> page
                path "/style.css" >=> Files.file (Path.Combine (dist, "style.css"))
                pathRegex "^/assets/" >=> Files.browse dist
            ]
            RequestErrors.NOT_FOUND "Not found"
        ]

    let config =
        { defaultConfig with
            bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" 5082 ] }

    startWebServer config app
    0
