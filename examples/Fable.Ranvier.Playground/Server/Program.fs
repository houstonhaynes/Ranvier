module Program

open System
open System.IO
open System.Text.Json
open Ranvier.View

let private root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let private write relative content =
    let path = Path.Combine (root, relative)

    if
        not (File.Exists path)
        || File.ReadAllText path <> content
    then
        Directory.CreateDirectory (Path.GetDirectoryName path)
        |> ignore

        let temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"

        try
            File.WriteAllText (temporary, content)
            File.Move (temporary, path, true)
        finally
            if File.Exists temporary then
                File.Delete temporary

[<EntryPoint>]
let main args =
    let html = Page.render Playground.page

    if html <> Page.render Playground.page then
        failwith "The playground document renders nondeterministically."

    if
        not (html.StartsWith "<!doctype html>")
        || not (html.Contains "<!--[r-->")
    then
        failwith "The playground document is missing its document or hydration root."

    match args with
    | [| "--write-pages" |] ->
        write "index.html" html

        write
            "generated-pages/Index.fs"
            (Page.clientSource "PlaygroundGenerated.Index" "Playground.page"
             + "\nApp.expose page\n")

        write
            "generated-pages/pages.json"
            (JsonSerializer.Serialize
                {|
                    pages =
                        [|
                            {|
                                name = "index"
                                path = "index.html"
                                entry = "generated-pages/Index.fs"
                            |}
                        |]
                |})
    | [||] -> ()
    | _ -> invalidArg "args" "Expected --write-pages or no arguments."

    printfn "ok: playground document is deterministic"
    0
