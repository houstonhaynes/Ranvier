/// <summary>Every page check, in the order pages.test.mjs runs them.</summary>
module Checks

open Harness

/// <summary>Hands anime's engine to the test clock; called once, after <c>animejs</c> is imported.</summary>
let prepare () =
    Harness.prepare ()

let cases: Case array =
    Array.ofList (
        List.concat
            [
                KanbanChecks.cases
                DashboardChecks.cases
                StoryChecks.cases
                PhysicsChecks.cases
                RoutesChecks.cases
            ]
    )
