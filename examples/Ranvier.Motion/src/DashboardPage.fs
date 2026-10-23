/// <summary>The dashboard page's shared document declaration.</summary>
module DashboardPage

/// <summary>The dashboard initialized with a pending feed until its browser timer supplies a sample.</summary>
let page =
    PageLayout.declaration "dashboard" (fun () -> ()) (PageLayout.pageView "dashboard" Dashboard.createStore Dashboard.view Dashboard.activate) None
