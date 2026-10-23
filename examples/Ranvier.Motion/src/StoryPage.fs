/// <summary>The story page's shared document declaration.</summary>
module StoryPage

open Ranvier.View

/// <summary>The story and its scroll observers.</summary>
let page =
    let capture () =
        ref Unchecked.defaultof<Story.Story>

    let view (state: Story.Story ref) =
        Component.define (fun _ () -> ()) (fun t -> t.render Story.view (fun () -> fun created -> state.Value <- created))

    PageLayout.declaration
        "story"
        (fun () -> ())
        (PageLayout.pageView "story" capture view (fun graph state -> StoryClient.activate graph state.Value))
        None
