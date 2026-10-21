module Ranvier.Analyzers.Tests.Ran022

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View.Foundation\nopen Ranvier.View.Foundation.Representation\nopen Ranvier.View.Foundation.Template\nopen Ranvier.View.Foundation.Sugar\ntype Page =\n    | Home\n    | About\nlet page = createSignal Home\n"

let private findings (body: string) =
    run Analyzers.ran022Cli (context (ranvier.Force ()) (header + body))

let private codes body =
    findings body |> List.map _.Code

let private viewOf (arms: string) =
    $"let view = Component.template (fun _ () -> page) (div () {{ matchWith (fun (p: Signal<Page>) -> p.Value) ({arms}) }})\n"

[<Tests>]
let tests =
    testList
        "RAN022"
        [
            test "armOf in function arms, with its exact message" {
                Expect.equal
                    (findings (viewOf "function Home -> arm (armOf (span () { \"home\" })) () | About -> arm (armOf (span () { \"about\" })) ()")
                     |> List.map _.Message
                     |> List.distinct)
                    [
                        "`armOf` runs on every read of this region, so the definition is new each time and remounts. Bind the definition with `let` outside the template."
                    ]
                    "the message"
            }

            test "the finding's range is the definition call" {
                let body =
                    viewOf "function Home -> arm (armOf (span () { \"home\" })) () | About -> arm (armOf (span () { \"about\" })) ()"

                let range = (findings body).Head.Range
                Expect.equal (textAt (header + body) range) "armOf (span () { \"home\" })" "the armOf call"
            }

            test "show inside arms" {
                Expect.contains
                    (codes (
                        viewOf
                            "function Home -> arm (armOf (show (fun _ -> true) (span () { \"x\" }))) () | About -> arm (armOf (span () { \"y\" })) ()"
                    ))
                    "RAN022"
                    "show"
            }

            test "a definition in an items reader" {
                let body =
                    "let row = li () { \"x\" }\nlet view = Component.template (fun _ () -> page) (ul () { forEach id (fun (_: Signal<Page>) -> (Component.template (fun _ () -> ()) (span () { \"x\" }) |> ignore; [| 1 |])) row })\n"

                Expect.equal (codes body) [ "RAN022" ] "items reader"
            }

            test "a static property read in arms" {
                let body =
                    "type Arms =\n    static member Home: Component<unit -> unit> = armOf (span () { \"home\" })\n"
                    + viewOf "function Home -> arm Arms.Home () | About -> arm Arms.Home ()"

                Expect.contains (codes body) "RAN022" "non-auto static property"
            }

            test "an arms function bound with let" {
                let body =
                    "let arms = function\n    | Home -> arm (armOf (span () { \"home\" })) ()\n    | About -> arm (armOf (span () { \"about\" })) ()\n"
                    + "let view = Component.template (fun _ () -> page) (div () { matchWith (fun (p: Signal<Page>) -> p.Value) arms })\n"

                Expect.equal (codes body) [ "RAN022"; "RAN022" ] "hoisted arms"
            }

            test "a same-file helper that creates the definition" {
                let body =
                    "let mkArm view = armOf view\n"
                    + viewOf "function Home -> arm (mkArm (span () { \"home\" })) () | About -> arm (mkArm (span () { \"about\" })) ()"

                let messages = findings body
                Expect.equal (messages |> List.map _.Code) [ "RAN022"; "RAN022" ] "helper"
                Expect.stringContains messages.Head.Message "`mkArm`" "names the helper"
            }

            test "the message names a definition creator as written" {
                let body =
                    "let row = li () { \"x\" }\nlet view = Component.template (fun _ () -> page) (ul () { forEach id (fun (_: Signal<Page>) -> (Component.template (fun _ () -> ()) (span () { \"x\" }) |> ignore; [| 1 |])) row })\n"

                Expect.stringContains (findings body).Head.Message "`Component.template`" "qualified"
            }

            test "a same-file helper that returns a hoisted definition" {
                let body =
                    "let home = armOf (span () { \"home\" })\nlet homeOf () = home\n"
                    + viewOf "function Home -> arm (homeOf ()) () | About -> arm home ()"

                Expect.isEmpty (codes body) "hoisted behind a helper"
            }

            test "hoisted let definitions" {
                let body =
                    "let home = armOf (span () { \"home\" })\nlet about = armOf (span () { \"about\" })\n"
                    + viewOf "function Home -> arm home () | About -> arm about ()"

                Expect.isEmpty (codes body) "hoisted"
            }

            test "static member val" {
                let body =
                    "type Arms =\n    static member val Home: Component<unit -> unit> = armOf (span () { \"home\" })\n"
                    + viewOf "function Home -> arm Arms.Home () | About -> arm Arms.Home ()"

                Expect.isEmpty (codes body) "auto property"
            }

            test "a definition in a template build function outside regions" {
                Expect.isEmpty
                    (codes
                        "let view = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.render (Component.template (fun _ () -> ()) (span () { \"x\" })) (fun _ -> ()) |])\n")
                    "build function"
            }
        ]
