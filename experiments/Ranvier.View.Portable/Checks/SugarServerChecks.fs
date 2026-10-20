/// <summary>Sugared and explicit twins compared by skeleton layout, structural hash and server HTML, on .NET and under Fable.</summary>
module SugarServerChecks

open System
open global.Ranvier
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
open SugarFixtures

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private sameLayouts (pairs: (string * TemplateComponent * TemplateComponent) array) () =
    for name, explicitDefinition, sugarDefinition in pairs do
        equal
            (name, layoutSignature explicitDefinition.Root, structuralHash explicitDefinition)
            (name, layoutSignature sugarDefinition.Root, structuralHash sugarDefinition)

let private sameServerHtml (pairs: unit -> (string * string * string) array) () =
    for name, explicitHtml, sugarHtml in pairs () do
        equal (name, explicitHtml) (name, sugarHtml)

let private semanticsOrder () =
    equal semanticsMarkup (skeletonLayout semantics.Root).Markup

let private ignoredParameterIsSlot () =
    equal (1, 0) ((skeletonLayout ignoredParameter.Root).Targets.Length, (skeletonLayout literalText.Root).Targets.Length)

let private definitionTimeReadIsStatic () =
    let layout = skeletonLayout definitionTimeRead.Root
    equal ("<span>1</span>", 0) (layout.Markup, layout.Targets.Length)

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private showOffMarkup () =
    let graph = new Graph ()
    let hidden = graph.Run (fun () -> toggle false)

    equal
        ("<!--[r--><div>before<!--[a:"
         + structuralHash (Ranvier.View.Foundation.Sugar.Builders.emptyArm ())
         + "--><!--ranvier:row--><!--]-->after</div><!--]-->")
        (Server.render omit showSugar hidden)

#if !FABLE_COMPILER
let private emptyArmField () =
    let holder =
        typeof<TemplateBuilder<unit>>.Assembly.GetType("Ranvier.View.Foundation.Sugar.Builders+EmptyArm", true)

    let field =
        holder.GetField (
            "shared",
            Reflection.BindingFlags.Static
            ||| Reflection.BindingFlags.NonPublic
            ||| Reflection.BindingFlags.Public
        )

    equal false (isNull field)
    field

/// <summary>Clears the shared empty arm, renders a hidden <c>show</c> built earlier, and restores the arm.</summary>
let private showTakesEmptyArmOnce () =
    let field = emptyArmField ()
    let original = field.GetValue null
    use graph = new Graph ()
    let hidden = graph.Run (fun () -> toggle false)

    try
        field.SetValue (null, null)
        Server.render omit showSugar hidden |> ignore
        equal true (isNull (field.GetValue null))
    finally
        field.SetValue (null, original)

/// <summary>Repeatedly clears the shared empty arm, then takes it from 16 threads released together.</summary>
let private emptyArmOnce () =
    let field = emptyArmField ()
    let original = field.GetValue null

    try
        for _ in 1..100 do
            field.SetValue (null, null)
            let arms = Array.zeroCreate<obj> 16
            use barrier = new Threading.Barrier 16

            let threads =
                [|
                    for index in 0..15 ->
                        Threading.Thread (fun () ->
                            barrier.SignalAndWait ()
                            arms[index] <- Ranvier.View.Foundation.Sugar.Builders.emptyArm () :> obj)
                |]

            for thread in threads do
                thread.Start ()

            for thread in threads do
                thread.Join ()

            equal
                true
                (arms
                 |> Array.forall (fun arm -> obj.ReferenceEquals (arm, arms[0])))
    finally
        field.SetValue (null, original)

let dotnetCases: (string * (unit -> unit)) array =
    [|
        "concurrent first uses of show share one empty arm", emptyArmOnce
        "show takes the empty arm once per call, outside the arms function", showTakesEmptyArmOnce
    |]
#endif

/// <summary>Renders both mixed trees three times; each static <c>View</c> ran once, at definition time.</summary>
let private viewsBuiltOnce () =
    for _ in 1..3 do
        Server.render omit mixedSugar () |> ignore
        Server.render omit mixedRendered () |> ignore

    equal (1, 1) (Builds.counterRecord, Builds.counterClass)

/// <summary>A mounted hook raises the browser-capability error under <c>Reject</c>; the motion kinds beside it do not.</summary>
let private mountedStillRejected () =
    let mixed =
        Component.template
            (fun _ () -> ())
            (div (Hooks.motion (fun _ _ _ -> ignore), Hooks.mounted (fun _ (_: Fable.Core.TS.Dom.HTMLDivElement) -> ignore)) { "x" })

    let message =
        try
            Server.render Server.Options.defaults mixed ()
            |> ignore

            ""
        with :? InvalidOperationException as error ->
            error.Message

    equal "Browser capability requires explicit Omit policy: <div> mounted hook" message

let cases: (string * (unit -> unit)) array =
    [|
        "sugared elements share their explicit twins' layouts and hashes", sameLayouts elementPairs
        "sugared elements render their explicit twins' server HTML", sameServerHtml elementServerPairs
        "a CE body keeps document order across for, if, yield! and adjacent texts", semanticsOrder
        "a body lambda that ignores its parameter is a text slot", ignoredParameterIsSlot
        "a body line reading a module-level signal is static text", definitionTimeReadIsStatic
        "control-flow sugar shares its explicit twins' layouts and hashes", sameLayouts flowPairs
        "control-flow sugar renders its explicit twins' server HTML", sameServerHtml flowServerPairs
        "a hidden show writes a zero-root arm marked with the shared empty arm's hash", showOffMarkup
        "declaration styles share their explicit twins' layouts and hashes", sameLayouts declPairs
        "declaration styles render their explicit twins' server HTML", sameServerHtml declServerPairs
        "a static View is read once per definition", viewsBuiltOnce
        "void and component tags as views share their explicit twins' layouts and hashes", sameLayouts tagViewPairs
        "void and component tags as views render their explicit twins' server HTML", sameServerHtml tagViewServerPairs
        "motion, exit and reflow hooks add no server HTML under Reject or Omit", sameServerHtml motionServerPairs
        "a mounted hook beside the motion kinds still requires Omit", mountedStillRejected
    |]
