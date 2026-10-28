module DomPackageConsumer

open Fable.Core
open Ranvier.Dom

[<Emit("({ nodeName: 'DIV' })")>]
let private node () : Node = jsNative

/// <summary>Checks browser member access through the packaged DOM contracts.</summary>
[<EntryPoint>]
let main _ =
    if (node ()).nodeName <> "DIV" then
        failwith "The packaged DOM interface changed the browser member access"

    printfn "DOM_PACKAGE_BROWSER PASS"
    0
