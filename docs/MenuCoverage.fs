module Docs.MenuCoverage

open System.IO
open Partas.Nacara.Theme

/// The source paths of the pages an entry references, nested sections and groups included.
let rec private entryPages (item: MenuItem) : string list =
    match item.Entry with
    | MenuEntry.MenuPage path -> [ path ]
    | MenuEntry.MenuSection (_, items) -> List.collect entryPages items
    | MenuEntry.MenuGroup (page, items) -> page :: List.collect entryPages items
    | MenuEntry.MenuLink _ -> []

/// The pages under <c>contentRoot</c>, as paths relative to it with <c>/</c> separators: the files the docs collection
/// builds into pages, Markdown and literate scripts.
let private contentPages (contentRoot: string) : Set<string> =
    [ "*.md"; "*.fsx" ]
    |> List.collect (fun pattern ->
        Directory.GetFiles (contentRoot, pattern, SearchOption.AllDirectories)
        |> List.ofArray)
    |> List.map (fun file -> Path.GetRelativePath(contentRoot, file).Replace('\\', '/'))
    |> Set.ofList

/// <summary>
/// A message per page under <c>contentRoot</c> held by none of <c>menus</c> and absent from <c>exempt</c>, and per menu
/// entry whose source file is missing. An empty list means every page is reachable from a sidebar.
/// </summary>
let check (contentRoot: string) (exempt: Set<string>) (menus: Map<string, MenuItem list>) : string list =
    let pages = contentPages contentRoot

    let listed =
        menus
        |> Map.values
        |> Seq.collect (List.collect entryPages)
        |> Set.ofSeq

    [
        for page in Set.difference (Set.difference pages listed) exempt do
            $"docs/content/%s{page} is in no Theme.menu in docs/Site.fs. Add it to its section's menu, or to unlistedPages in docs/Site.fs with the reason it has none."
        for entry in Set.difference listed pages do
            $"docs/Site.fs lists %s{entry} in a Theme.menu, but docs/content/%s{entry} does not exist."
    ]
