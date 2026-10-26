/// <summary>Checks portable renders and generates the Motion site's documents and F# browser entries.</summary>
module Program

open System
open System.IO
open System.Text.Json
open Ranvier.View

let private root = Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, ".."))

let private outputPath relative =
    let path = Path.GetFullPath (Path.Combine (root, relative))

    if not (path.StartsWith (root + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) then
        invalidArg "relative" "An output must stay inside the Motion example directory."

    path

let private write path content =
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

let private rendered () =
    Site.outputs
    |> Array.map (fun page -> page, page.Render ())

let private verify (documents: (Site.Output * string) array) =
    let options =
        { Server.Options.defaults with
            Policy = Omit
        }

    let story = Server.render options Story.view ignore
    let twin = Server.render options StoryTwin.view ignore

    if story <> twin then
        failwith "The story's server render differs from its twin without Motion parts."

    for (page: Site.Output), html in documents do
        if
            not (html.StartsWith "<!doctype html>")
            || not (html.Contains "<!--[r-->")
        then
            failwithf "%s is missing its document or hydration root." page.Name

        if html <> page.Render () then
            failwithf "%s renders nondeterministically." page.Name

    let board =
        documents
        |> Array.find (fun (page, _) -> page.Name = "kanban")
        |> snd

    if
        not (board.Contains "kanban-state")
        || not (board.Contains "Sketch the board")
    then
        failwith "The board document must include its initial model and rendered cards."

    let dashboard =
        documents
        |> Array.find (fun (page, _) -> page.Name = "dashboard")
        |> snd

    if not (dashboard.Contains "skeleton") then
        failwith "The dashboard must render its pending feed."

let private writePages documents =
    let files =
        documents
        |> Array.collect (fun (page: Site.Output, html) ->
            let document = outputPath page.Path, html

            match page.Entry with
            | None -> [| document |]
            | Some (entry, declaration) ->
                [|
                    document
                    outputPath ("generated-pages/" + entry + ".fs"), Ranvier.View.Page.clientSource ("MotionGenerated." + entry) declaration
                |])

    let manifest =
        JsonSerializer.Serialize (
            {|
                pages =
                    documents
                    |> Array.map (fun (page, _) ->
                        {|
                            name = page.Name
                            path = page.Path
                            entry =
                                page.Entry
                                |> Option.map (fun (entry, _) -> "generated-pages/" + entry + ".fs")
                                |> Option.defaultValue null
                        |})
            |}
        )

    for path, content in files do
        write path content

    write (outputPath "generated-pages/pages.json") manifest
    printfn "generated %d documents and %d browser entries" documents.Length (files.Length - documents.Length)

[<EntryPoint>]
let main args =
    let documents = rendered ()
    verify documents

    match args with
    | [| "--write-pages" |] -> writePages documents
    | [| "--write"; path |] ->
        let html =
            documents
            |> Array.find (fun (page, _) -> page.Name = "story")
            |> snd

        write (Path.GetFullPath path) html
    | [||] -> ()
    | _ -> invalidArg "args" "Expected --write-pages, --write <story path>, or no arguments."

    printfn "ok: all %d page documents are deterministic; the story equals its Motion-free twin" documents.Length
    0
