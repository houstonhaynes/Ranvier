module DomPackageConsumer

open Fable.Core
open Ranvier.Dom

[<Emit("({ nodeName: 'DIV' })")>]
let private node () : Node = jsNative

[<Emit("({ style: { cssText: '' } })")>]
let private styled () : ElementCSSInlineStyle = jsNative

/// <summary>Checks browser member access through the packaged DOM contracts.</summary>
[<EntryPoint>]
let main _ =
    if (node ()).nodeName <> "DIV" then
        failwith "The packaged DOM interface changed the browser member access"

    let inlineStyle = (styled ()).style
    inlineStyle.cssText <- "color: red"

    if inlineStyle.cssText <> "color: red" then
        failwith "The packaged inline style contract changed the browser property access"

    printfn "DOM_PACKAGE_BROWSER PASS"
    0
