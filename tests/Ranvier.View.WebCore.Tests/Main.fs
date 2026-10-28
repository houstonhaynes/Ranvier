module Ranvier.View.WebCore.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    runTestsInAssemblyWithCLIArgs [ Sequenced ] argv
