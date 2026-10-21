/// The F# samples of docs/content/view/motion-parts.md, in page order; a sample that is an excerpt is wrapped in a function.
module MotionParts

open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

type Task = { Id: int; Title: string }

type TaskList =
    {
        Tasks: Signal<Task array>
        Done: Signal<float>
    }

let taskList =
    Component.template
        (fun _ (tasks: Task array) ->
            {
                Tasks = Api.createSignal tasks
                Done = Api.createSignal 0.
            })
        (section () {
            ul (Attrs.className "tasks", Motion.flip (LayoutParams (duration = 300, ease = Ease.outQuart))) {
                forEach
                    (fun task -> task.Id)
                    (fun l -> l.Tasks.Value)
                    (li (
                        Motion.presence
                            (fun _ el -> Anime.animate (el, Anim (opacity = Value.fromTo (0, 1), y = Value.fromTo (8, 0), duration = 220)))
                            (fun _ el -> Anime.animate (el, Anim (opacity = 0, duration = 160)))
                    ) {
                        fun current -> (current ()).Title
                    })
            }

            div (Attrs.className "progress", Motion.follow "scaleX" (AnimatableParams (duration = 400)) (fun l -> l.Done.Value)) { () }
        })

type Point = { mutable x: float; mutable y: float }

type Handle =
    {
        Offset: Signal<struct (float * float)>
        Spring: MotionSlot
    }

let springBack (handle: Handle) =
    let graph = Graph.Current
    let struct (x, y) = Api.untrack (fun () -> handle.Offset.Value)
    let point = { x = x; y = y }

    handle.Spring.Play (fun () ->
        Anime.animate (
            Targets.object point,
            Anim (
                x = 0,
                y = 0,
                ease = Ease.spring (stiffness = 200., damping = 14.),
                onUpdate = fun _ -> graph.Batch (fun () -> handle.Offset.Value <- struct (point.x, point.y))
            )
        ))

let handle =
    Component.template
        (fun _ () ->
            {
                Offset = Api.createSignal (struct (0., 0.))
                Spring = Motion.slot ()
            })
        (div () {
            div (
                Attrs.className "handle",
                Motion.drag (DraggableParams ()) (fun h ->
                    {
                        Position = h.Offset
                        Dragging = ValueNone
                        Slot = ValueSome h.Spring
                    })
            ) {
                "drag"
            }

            button (Events.onClick (fun (h: Handle) _ -> springBack h)) { "Back" }
        })
