/// <summary>The generated Motion site's document and browser entry catalogue.</summary>
module Site

/// <summary>A generated document with its optional browser entry.</summary>
type Output =
    {
        Name: string
        Path: string
        Entry: (string * string) option
        Render: unit -> string
    }

/// <summary>The complete output registry used by the server generator.</summary>
let outputs =
    let output name title declarationName definition =
        {
            Name = name
            Path = name + "/index.html"
            Entry = Some (title, declarationName)
            Render = fun () -> Ranvier.View.Page.render definition
        }

    [|
        {
            Name = "index"
            Path = "index.html"
            Entry = None
            Render = fun () -> Ranvier.View.Page.render PageLayout.index
        }
        output "kanban" "Kanban" "KanbanPage.page" KanbanPage.page
        output "dashboard" "Dashboard" "DashboardPage.page" DashboardPage.page
        output "story" "Story" "StoryPage.page" StoryPage.page
        output "physics" "Physics" "PhysicsPage.page" PhysicsPage.page
        output "routes" "Routes" "RoutesPage.page" RoutesPage.page
        output "graphs" "Graphs" "GraphsPage.page" GraphsPage.page
    |]
