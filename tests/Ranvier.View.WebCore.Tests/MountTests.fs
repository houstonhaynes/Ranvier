module Ranvier.View.WebCore.Tests.MountTests

open Expecto
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.WebCore
open Ranvier.View.WebCore.Tests.Support

type Counter = { Count: Signal<int> }

let counter =
    Component.define (fun _ (start: int) -> { Count = Api.createSignal start }) (fun t ->
        t.button
            [|
                t.Attrs.attribute ("style", "width:120px;height:40px")
                t.Events.on ("click", fun state _ -> state.Count.Value <- state.Count.Value + 1)
            |]
            [| t.text (fun state -> string state.Count.Value) |])

let private row =
    Component.define (fun _ (item: unit -> int) -> item) (fun t -> t.li [||] [| t.text (fun item -> string (item ())) |])

let rows =
    Component.define (fun _ (items: Signal<int array>) -> items) (fun t ->
        t.ul
            [||]
            [|
                t.each (fun (n: int) -> n) (fun (items: Signal<int array>) -> items.Value) row
            |])

type Toggle = { On: Signal<bool>; Name: string }

let private shown =
    Component.define (fun _ (current: unit -> Toggle) -> current ()) (fun t -> t.span [||] [| t.text (fun (s: Toggle) -> s.Name) |])

let private hidden =
    Component.define (fun _ (_: unit -> Toggle) -> ()) (fun t -> t.em [||] [| t.text "off" |])

let toggle =
    Component.define (fun _ (state: Toggle) -> state) (fun t ->
        t.div
            [||]
            [|
                t.text "["
                t.matchWith (fun (s: Toggle) -> if s.On.Value then Choice1Of2 s else Choice2Of2 s) (function
                    | Choice1Of2 s -> t.arm shown s
                    | Choice2Of2 s -> t.arm hidden s)
                t.text "]"
            |])

let private items (mount: Mount) =
    let list = mount.Container.querySelectorAll ("li": string)
    [ for i in 0 .. int list.length - 1 -> list.item (float i) ]

[<Tests>]
let tests =
    testList
        "Mount"
        [
            hostCase "a sugar component renders and a click updates it"
            <| fun host ->
                use graph = new Graph ()
                use mounted = mount host graph counter 1
                Expect.equal mounted.Text "1" "first render"
                (mounted.Container.querySelector<HTMLElement> "button").Value.click()
                host.Cycle ()
                Expect.equal mounted.Text "2" "after the click"

            hostCase "a keyed list keeps the nodes of kept rows"
            <| fun host ->
                use graph = new Graph ()
                let source = graph.Run (fun () -> Api.createSignal [| 1; 2; 3; 4; 5 |])
                use mounted = mount host graph rows source

                Expect.equal
                    mounted.Html
                    "<!--ranvier:start--><ul><!--[--><li>1</li><li>2</li><li>3</li><li>4</li><li>5</li><!--]--></ul><!--ranvier:end-->"
                    "first render"

                let before = items mounted
                mounted.Act (host, fun () -> source.Value <- [| 1; 2; 5 |])
                Expect.equal mounted.Text "125" "after removing two rows"
                let after = items mounted
                Expect.isTrue (obj.ReferenceEquals (after[0], before[0])) "the first row's node"
                Expect.isTrue (obj.ReferenceEquals (after[2], before[4])) "the last row's node"
                mounted.Act (host, fun () -> source.Value <- [| 5; 1; 2; 6 |])
                Expect.equal mounted.Text "5126" "after a move and an insert"
                Expect.isTrue (obj.ReferenceEquals ((items mounted)[0], before[4])) "the moved row's node"

            hostCase "an arm switches with its signal"
            <| fun host ->
                use graph = new Graph ()

                let state =
                    graph.Run (fun () ->
                        {
                            On = Api.createSignal true
                            Name = "on"
                        })

                use mounted = mount host graph toggle state
                Expect.stringContains mounted.Html "<span>on</span>" "first arm"
                mounted.Act (host, fun () -> state.On.Value <- false)
                Expect.stringContains mounted.Html "<em>off</em>" "second arm"
                Expect.isFalse (mounted.Html.Contains "<span>") "first arm removed"
                Expect.equal mounted.Text "[off]" "text"
                mounted.Act (host, fun () -> state.On.Value <- true)
                Expect.equal mounted.Text "[on]" "back"

            hostCase "Fable.Ranvier mounts with Dom.element, Dom.on and Dom.reactiveText"
            <| fun host ->
                use graph = new Graph ()
                let count = graph.Run (fun () -> Api.createSignal 0)

                use mounted =
                    Fable.Ranvier.Mount.mount graph host.Document.body (fun () ->
                        Fable.Ranvier.Dom.element
                            "button"
                            [
                                Fable.Ranvier.Dom.attribute "id" "fable"
                                Fable.Ranvier.Dom.on "click" (fun _ -> count.Value <- count.Value + 1)
                            ]
                            [ Fable.Ranvier.Dom.reactiveText (fun () -> $"clicked {count.Value}") ]
                        :> Node)

                let button = host.Document.getElementById("fable").Value
                Expect.equal button.textContent "clicked 0" "first render"
                button.click ()
                host.Cycle ()
                Expect.equal button.textContent "clicked 1" "after a click"
                mounted.Dispose ()
                Expect.isNone (host.Document.getElementById "fable") "removed at disposal"
        ]
