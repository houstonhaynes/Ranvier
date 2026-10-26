/// <summary>The interactive graphs page's shared document declaration.</summary>
module GraphsPage

/// <summary>The full-day signal explorer and paused regional ranking.</summary>
let page =
    PageLayout.declaration "graphs" (fun () -> ()) (PageLayout.pageView "graphs" Graphs.createStore Graphs.view Graphs.activate) None
