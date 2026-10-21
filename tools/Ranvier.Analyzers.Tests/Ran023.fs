module Ranvier.Analyzers.Tests.Ran023

open Expecto
open Ranvier.Analyzers
open Ranvier.Analyzers.Tests.Options

let private header =
    "module Fixture\nopen Ranvier\nopen Ranvier.View\nopen Ranvier.View.Sugar\nlet markup = UnsafeHtml.Trust \"<b>x</b>\"\nlet attach (_: unit) (_: 'native) = fun () -> ()\n"

let private findings (body: string) =
    run Analyzers.ran023Cli (context (ranvier.Force ()) (header + body))

let private codes body =
    findings body |> List.map _.Code

[<Tests>]
let tests =
    testList
        "RAN023"
        [
            test "two content parts, with its exact message" {
                Expect.equal
                    (findings "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup; t.Hooks.widget attach |] [||])\n"
                     |> List.map _.Message)
                    [
                        "This element has two content parts (`Unsafe.html` and `Hooks.widget`), which fails when the definition is created. Give each content part its own element."
                    ]
                    "one message"
            }

            test "the finding's range is the second content part" {
                let body =
                    "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup; t.Hooks.widget attach |] [||])\n"

                let range = (findings body).Head.Range
                Expect.equal (textAt (header + body) range) "t.Hooks.widget attach" "the widget part"
            }

            test "a content part and template children" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup |] [| t.text \"x\" |])\n")
                    [ "RAN023" ]
                    "children"
            }

            test "a content part and sugar children" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> div (t.Unsafe.html markup) { \"x\" })\n")
                    [ "RAN023" ]
                    "sugar children"
            }

            test "raw HTML on a void element" {
                Expect.equal (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.br [| t.Unsafe.html markup |])\n") [ "RAN023" ] "void"
            }

            test "raw HTML on textarea" {
                Expect.equal
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.textarea [| t.Unsafe.html markup |] [||])\n")
                    [ "RAN023" ]
                    "textarea"
            }

            test "Array.empty children are unknown" {
                Expect.isEmpty
                    (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup |] Array.empty)\n")
                    "Array.empty"
            }

            test "children passed as a variable are unknown" {
                Expect.isEmpty
                    (codes
                        "let none: TemplateView<unit> array = [||]\nlet c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup |] none)\n")
                    "a variable"
            }

            test "one content part" {
                Expect.isEmpty (codes "let c = Component.define (fun _ () -> ()) (fun t -> t.div [| t.Unsafe.html markup |] [||])\n") "valid"
            }

            test "content parts on separate elements" {
                Expect.isEmpty
                    (codes
                        "let c = Component.define (fun _ () -> ()) (fun t -> t.div [||] [| t.div [| t.Unsafe.html markup |] [||]; t.div [| t.Hooks.widget attach |] [||] |])\n")
                    "separate"
            }

            test "parts built by a helper are not inspected" {
                Expect.isEmpty
                    (codes
                        "let parts (t: TemplateBuilder<unit>) = [| t.Unsafe.html markup; t.Hooks.widget attach |]\nlet c = Component.define (fun _ () -> ()) (fun t -> t.div (parts t) [||])\n")
                    "false negative by design"
            }
        ]
