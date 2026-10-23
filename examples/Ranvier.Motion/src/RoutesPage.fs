/// <summary>The route shell's shared document declaration.</summary>
module RoutesPage

/// <summary>The shell hydrated at Home before the browser applies its hash.</summary>
let page =
    PageLayout.declaration "routes" (fun () -> Routes.Home) (PageLayout.pageView "routes" Routes.createShell Routes.view Routes.activate) None
