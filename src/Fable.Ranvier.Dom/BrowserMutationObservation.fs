namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>DOM mutation observation through the browser's MutationObserver.</summary>
module BrowserMutationObservation =
    [<Emit("$0.defaultView || globalThis")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("typeof $0.MutationObserver === 'function'")>]
    let private supported (realm: obj) : bool = jsNative

    [<Emit("new $0.MutationObserver($1)")>]
    let private construct (realm: obj, callback: obj array -> unit) : obj = jsNative

    [<Emit("({childList: $0, subtree: $1})")>]
    let private options (childList: bool, subtree: bool) : obj = jsNative

    [<Emit("$0[$1] = $2")>]
    let private set (options: obj, name: string, value: obj) : unit = jsNative

    [<Emit("$0.observe($1, $2)")>]
    let private observe (observer: obj, target: Node, options: obj) : unit = jsNative

    [<Emit("$0.disconnect()")>]
    let private disconnect (observer: obj) : unit = jsNative

    [<Emit("$0.takeRecords()")>]
    let private takeRecords (observer: obj) : obj array = jsNative

    [<Emit("$0[$1]")>]
    let private field<'T> (record: obj, name: string) : 'T = jsNative

    [<Emit("Array.from($0[$1])")>]
    let private nodes (record: obj, name: string) : Node array = jsNative

    [<Emit("$0 && $0.name === 'TypeError'")>]
    let private isTypeError (error: obj) : bool = jsNative

    let private optional (value: 'T) =
        if isNull (box value) then None else Some value

    let private copy (record: obj) : MutationRecord =
        {
            Type =
                match field<string>(record, "type") with
                | "childList" -> MutationType.ChildList
                | "attributes" -> MutationType.Attributes
                | "characterData" -> MutationType.CharacterData
                | value -> invalidOp ("Unknown mutation record type: " + value)
            Target = field (record, "target")
            AddedNodes = nodes (record, "addedNodes")
            RemovedNodes = nodes (record, "removedNodes")
            PreviousSibling = optional (field<Node>(record, "previousSibling"))
            NextSibling = optional (field<Node>(record, "nextSibling"))
            AttributeName = optional (field<string>(record, "attributeName"))
            AttributeNamespace = optional (field<string>(record, "attributeNamespace"))
            OldValue = optional (field<string>(record, "oldValue"))
        }

    /// <summary>The provider using document's realm, or the calling realm for a document without a Window.</summary>
    /// <remarks>Accessible foreign-document targets and adoption retain ordinary DOM semantics.</remarks>
    let provider (document: Document) : MutationObservationProvider =
        if isNull (box document) then
            nullArg "document"

        let realm = realm document

        if not (supported realm) then
            raise (NotSupportedException "Mutation observation requires a browser MutationObserver")

        {
            Create =
                fun deliver _onClosed ->
                    let observer = construct (realm, fun records -> deliver (Array.map copy records))

                    {
                        Observe =
                            fun target init ->
                                let value = options (init.ChildList, init.Subtree)

                                let boolean name setting =
                                    setting
                                    |> Option.iter (fun enabled -> set (value, name, box enabled))

                                boolean "attributes" init.Attributes
                                boolean "characterData" init.CharacterData
                                boolean "attributeOldValue" init.AttributeOldValue
                                boolean "characterDataOldValue" init.CharacterDataOldValue

                                init.AttributeFilter
                                |> Option.iter (fun filter -> set (value, "attributeFilter", box filter))

                                try
                                    observe (observer, target, value)
                                with error ->
                                    if isTypeError (box error) then
                                        raise (MutationObserverException ("TypeError", field<string>(box error, "message")))
                                    else
                                        reraise ()
                        Disconnect = fun () -> disconnect observer
                        TakeRecords = fun () -> Array.map copy (takeRecords observer)
                        Release = fun () -> disconnect observer
                    }
        }

    /// <summary>A mutation observation service using document's realm.</summary>
    let forDocument (document: Document) : MutationObservationService =
        MutationObservation.create (provider document)
