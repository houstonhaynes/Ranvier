module Ranvier.View.WebCore.Tests.InputTests

open System
open Expecto
open Fable.Core.TS.Dom
open Ranvier
open Ranvier.View
open Ranvier.View.WebCore.Tests.Support
open Ranvier.View.WebCore.Tests.MountTests

type Check = { Prevent: bool; Clicks: Signal<int> }

let private checkbox =
    Component.define (fun _ (state: Check) -> state) (fun t ->
        t.input
            [|
                t.Attrs.attribute ("type", "checkbox")
                t.Attrs.attribute ("style", "width:40px;height:40px")
                t.Events.on (
                    "click",
                    fun state (event: Event) ->
                        state.Clicks.Value <- state.Clicks.Value + 1

                        if state.Prevent then
                            event.preventDefault ()
                )
            |])

[<Tests>]
let tests =
    testList
        "Input"
        [
            hostCase "a native click reaches a component's handler"
            <| fun host ->
                use graph = new Graph ()
                use mounted = mount host graph counter 1
                clickOn host (mounted.Container.querySelector<Element> "button").Value
                Expect.equal mounted.Text "2" "the handler ran once"
                clickOn host (mounted.Container.querySelector<Element> "button").Value
                Expect.equal mounted.Text "3" "and again"

            hostCase "the event objects of handled clicks are released after collection"
            <| fun host ->
                use graph = new Graph ()
                use mounted = mount host graph counter 0
                let button = (mounted.Container.querySelector<Element> "button").Value

                for _ in 1..20 do
                    clickOn host button

                Expect.equal mounted.Text "20" "every click was handled"
                let released = host.Session.Released
                GC.Collect ()
                GC.WaitForPendingFinalizers ()
                host.Cycle ()
                Expect.isGreaterThanOrEqual (host.Session.Released - released) 20 "the events, at least one per click"

            hostCase "preventDefault in a handler cancels the native default action"
            <| fun host ->
                use graph = new Graph ()

                let prevented =
                    graph.Run (fun () ->
                        {
                            Prevent = true
                            Clicks = Api.createSignal 0
                        })

                use first = mount host graph checkbox prevented
                let box = (first.Container.querySelector<HTMLInputElement> "input").Value
                clickOn host box
                Expect.equal prevented.Clicks.Value 1 "the handler ran"
                Expect.isFalse box.``checked`` "the checkbox stays unchecked"

                let allowed =
                    graph.Run (fun () ->
                        {
                            Prevent = false
                            Clicks = Api.createSignal 0
                        })

                use second = mount host graph checkbox allowed
                let other = (second.Container.querySelector<HTMLInputElement> "input").Value
                clickOn host other
                Expect.equal allowed.Clicks.Value 1 "the handler ran"
                Expect.isTrue other.``checked`` "the default action checked it"
        ]
