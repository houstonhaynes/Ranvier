/// The F# samples of docs/content/view/motion-parts.md, in page order; a sample that is an excerpt is wrapped in a function.
module MotionParts

open global.Ranvier
open Ranvier.Anime
open Ranvier.View.Foundation
open Ranvier.View.Foundation.Representation
open Ranvier.View.Foundation.Template
open Ranvier.View.Foundation.Sugar
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
