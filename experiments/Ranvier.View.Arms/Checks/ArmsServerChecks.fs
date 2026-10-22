/// <summary>Arms.Match fixtures compared with their explicit twins by layout, structural hash and server HTML.</summary>
module ArmsServerChecks

open global.Ranvier
open Ranvier.View
open Ranvier.View.Representation
open Ranvier.View.Template
open ArmsFixtures

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private sameLayouts () =
    for case in all do
        equal
            (case.Fixture, SugarFixtures.layoutSignature case.Twin.Root, structuralHash case.Twin)
            (case.Fixture, SugarFixtures.layoutSignature case.Site.Root, structuralHash case.Site)

/// <summary>Equal HTML for every fixture state; only the editor's listener under Reject may raise, on both sides alike.</summary>
let private sameHtml (options: Server.Options) () =
    for case in all do
        let site, twin = case.Render options
        let mayRaise = options.Policy = Reject && case.Fixture = "editor"

        if
            not mayRaise
            && (site.StartsWith "raised: "
                || twin.StartsWith "raised: ")
        then
            failwithf "%s %s raised: site %s; twin %s" case.Fixture case.State site twin

        equal (case.Fixture, case.State, twin) (case.Fixture, case.State, site)

/// <summary>A failwithf case body raises its message at the region read; another case renders.</summary>
let private failwithfRaisesAtRead () =
    let render state =
        try
            use graph = new Graph ()
            let props = graph.Run (fun () -> Api.createSignal state)
            Server.render omit failing props
        with error ->
            "raised: " + error.Message

    equal (true, "raised: bad state") ((render Idle).Contains "<span>Other</span>", render (Loaded ("x", 1)))

/// <summary>A case body's <c>let</c> of a raising expression raises when the site is built.</summary>
let private letThrowRaisesAtBuild () =
    let built =
        try
            letThrowing () |> ignore
            "built"
        with error ->
            "raised: " + error.Message

    equal "raised: a" built

let cases: (string * (unit -> unit)) array =
    [|
        "Arms.Match fixtures share their twins' layouts and hashes", sameLayouts
        "Arms.Match fixtures render their twins' server HTML under Reject", sameHtml Server.Options.defaults
        "Arms.Match fixtures render their twins' server HTML under Omit", sameHtml omit
        "an incomplete Arms.Match raises for its unmatched case", (fun () -> equal true (incompleteRaises ()))
        "a failwithf case body raises its message at the region read", failwithfRaisesAtRead
        "a case body's raising let raises when the site is built", letThrowRaisesAtBuild
    |]

/// <summary>One line per fixture state: fixture, state, the site's structural hash and its server HTML under Omit.</summary>
let goldenLines () : string array =
    [|
        for case in all -> sprintf "%s|%s|%s|%s" case.Fixture case.State (structuralHash case.Site) (fst (case.Render omit))
    |]

#if !FABLE_COMPILER
let private goldenPath =
    System.IO.Path.Combine (__SOURCE_DIRECTORY__, "arms-server.golden.txt")

/// <summary>Compares the .NET lines with the committed file; <c>--update</c> rewrites the file instead.</summary>
let private golden () =
    let lines = goldenLines ()

    if
        System.Environment.GetCommandLineArgs ()
        |> Array.contains "--update"
    then
        System.IO.File.WriteAllText (goldenPath, String.concat "\n" lines + "\n")
    else
        let committed =
            System.IO.File.ReadAllText(goldenPath).Replace("\r\n", "\n").TrimEnd('\n').Split '\n'

        equal (List.ofArray committed) (List.ofArray lines)

let dotnetCases: (string * (unit -> unit)) array =
    [| "the .NET server HTML of every fixture equals the golden file", golden |]
#endif
