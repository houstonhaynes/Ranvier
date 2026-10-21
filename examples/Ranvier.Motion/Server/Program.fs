/// <summary>
/// Renders the story page as a whole document and its twin without Motion parts, under <c>Omit</c>, and fails when they
/// differ. Given a path, writes the story's document there.
/// </summary>
module Program

open System.IO
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation

/// <summary>Browser-only parts, the listeners among them, are left to hydration.</summary>
let options =
    { Server.Options.defaults with
        Policy = Omit
    }

/// <summary>The story's document: its stylesheets, and the entry that hydrates the host.</summary>
let document =
    { Server.DocumentShell.defaults with
        Lang = Some "en"
        Stylesheets = [ "/styles/motion.css"; "/story/story.css" ]
        Scripts = [ Server.ModuleScript.Src "/story/main.js" ]
    }

[<EntryPoint>]
let main args =
    let render definition =
        Server.renderDocument options document definition ignore

    let story = render Story.view
    let twin = render StoryTwin.view

    match args with
    | [| "--write"; path |] ->
        Directory.CreateDirectory (Path.GetDirectoryName (Path.GetFullPath path))
        |> ignore

        File.WriteAllText (path, story)
        printfn "wrote %s (%d characters)" path story.Length
    | _ -> ()

    if story = twin then
        printfn "ok story: the server document equals its twin without Motion parts (%d characters)" story.Length
        0
    else
        printfn "FAIL story\n  motion: %s\n  twin:   %s" story twin
        1
