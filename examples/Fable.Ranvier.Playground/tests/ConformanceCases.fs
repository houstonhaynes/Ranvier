module ConformanceCases

open Ranvier.View.Testing

let modules: (string * Conformance.Case array) array =
    [|
        "RangeChecks", RangeChecks.allCases
        "TemplateChecks", TemplateChecks.cases
        "DelegationChecks", DelegationChecks.cases
        "TestingApiChecks", TestingApiChecks.cases
        "BoundaryChecks", BoundaryChecks.cases
        "PlacementChecks", PlacementChecks.cases
    |]
