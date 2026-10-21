/// <summary>The nested Elmish pattern mounted, updated, disposed, server-rendered and hydrated.</summary>
module ElmishNestedChecks

open global.Ranvier
open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.View
open Ranvier.View.Dom
open CheckSupport
open ElmishNested

[<Emit("($1.querySelector($0)?.textContent ?? null)")>]
let private textOf (selector: string) (root: HTMLElement) : string = jsNative

let private click (selector: string) (root: HTMLElement) =
    (root.querySelector selector |> unbox<HTMLElement>).click()

let private omit =
    { Server.Options.defaults with
        Policy = Omit
    }

let private strictOptions: Hydration.HydrationOptions =
    { IdPrefix = "r"; Strict = true }

let private loops () =
    use graph = new Graph ()
    // Snapshots of module mutables are `let mutable`: Fable 5.18 inlines a single-use immutable binding at its use site.
    let mutable setups = Loops.childSetups
    let target = host ()
    let reports = ResizeArray<exn list>()

    let mounted =
        mountTemplate false graph reports.Add target (Component.render mvuParent ())

    mounted.Flush ()

    let text selector =
        textOf selector target

    let act (step: unit -> unit) =
        graph.Run step
        mounted.Flush ()

    equal (1, "1", "0", "0") (Loops.childSetups - setups, text ".step", text ".total", text ".last")

    // A parent change that leaves the selected step equal causes 0 child step reads.
    let mutable reads = Loops.stepReads
    act (fun () -> Loops.parentDispatch (Rename "b"))
    equal ("b", 0, 1) (text ".label", Loops.stepReads - reads, Loops.childSetups - setups)

    // Props down, through the reader over the parent's selector.
    click ".bump" target
    mounted.Flush ()
    equal ("2", 1, 1) (text ".step", Loops.stepReads - reads, Loops.childSetups - setups)

    // Callback up.
    click ".add" target
    mounted.Flush ()
    equal ("2", "2") (text ".total", text ".last")

    // The held dispatch while the child is mounted.
    act (fun () -> Loops.held Tick)
    equal "102" (text ".total")

    // Hiding the child disposes it; its held dispatch is dropped (PR #122).
    act (fun () -> Loops.parentDispatch Toggle)
    equal null (text ".child")
    let mutable updates = Loops.childUpdates
    act (fun () -> Loops.held Tick)
    act (fun () -> Loops.held Add)
    equal (0, "2") (Loops.childUpdates - updates, text ".last")

    // The parent's loop is unaffected; showing again sets up a fresh child.
    act (fun () -> Loops.parentDispatch (Rename "c"))
    equal "c" (text ".label")
    act (fun () -> Loops.parentDispatch Toggle)
    equal (2, "0", "2") (Loops.childSetups - setups, text ".total", text ".step")
    mounted.Dispose ()
    equal [] (messages reports)

let private hydrated () =
    use graph = new Graph ()
    let html = Server.render omit mvuParent ()
    let target = host ()
    target.innerHTML <- html

    let mounted = Hydration.hydrate graph None strict strictOptions target mvuParent ()

    equal html target.innerHTML
    click ".add" target
    equal ("1", "1") (textOf ".total" target, textOf ".last" target)
    click ".bump" target
    click ".add" target
    equal ("2", "3", "3") (textOf ".step" target, textOf ".total" target, textOf ".last" target)
    mounted.Dispose ()

let cases: (string * (unit -> unit)) array =
    strictCases
        [|
            "nested Elmish: props down, callback up, and a disposed child's held dispatch reaches nothing", loops
            "nested Elmish server-renders and hydrates Strict", hydrated
        |]
