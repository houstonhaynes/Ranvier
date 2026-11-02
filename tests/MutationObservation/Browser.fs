module MutationObservationBrowserTests

open System
open Fable.Core
open Ranvier.Dom

[<Emit("document")>]
let document: Document = jsNative

[<Emit("$0.createElement($1)")>]
let element (document: Document, tag: string) : Node = jsNative

[<Emit("$0.createTextNode($1)")>]
let text (document: Document, value: string) : Node = jsNative

[<Emit("$0.appendChild($1)")>]
let append (parent: Node, child: Node) : unit = jsNative

[<Emit("$0.removeChild($1)")>]
let remove (parent: Node, child: Node) : unit = jsNative

[<Emit("$0.setAttribute($1, $2)")>]
let attribute (node: Node, name: string, value: string) : unit = jsNative

[<Emit("$0.setAttributeNS($1, $2, $3)")>]
let attributeNS (node: Node, ns: string, name: string, value: string) : unit = jsNative

[<Emit("$0.data = $1")>]
let data (node: Node, value: string) : unit = jsNative

[<Emit("String.fromCharCode(0, 0xD800)")>]
let rawUtf16 () : string = jsNative

[<Emit("$0.implementation.createHTMLDocument('foreign')")>]
let detachedDocument (document: Document) : Document = jsNative

[<Emit("$0.adoptNode($1)")>]
let adopt (document: Document, node: Node) : unit = jsNative

[<Emit("(() => {const f=document.createElement('iframe');document.body.appendChild(f);return f;})()")>]
let frame () : obj = jsNative

[<Emit("$0.contentDocument")>]
let frameDocument (frame: obj) : Document = jsNative

[<Emit("$0.remove()")>]
let removeFrame (frame: obj) : unit = jsNative

[<Emit("setTimeout($0, 0)")>]
let later (callback: unit -> unit) : unit = jsNative

[<Emit("globalThis.__mutationProof = {checks:$0, error:$1}")>]
let complete (checks: int, error: string) : unit = jsNative

[<Emit("(() => {const h=e=>{if(String(e.message).includes('mutation intentional')){e.preventDefault();$0();}};window.addEventListener('error',h);return ()=>window.removeEventListener('error',h);})()")>]
let captureErrors (callback: unit -> unit) : (unit -> unit) = jsNative

let mutable checks = 0

let check message condition =
    checks <- checks + 1

    if not condition then
        failwith message

let named expected action =
    let mutable name = None

    try
        action ()
    with MutationObserverException (value, _) ->
        name <- Some value

    check ("Expected " + expected) (name = Some expected)

let cleanup = ResizeArray<unit -> unit>()

let own (service: MutationObservationService) =
    cleanup.Add service.Dispose
    service

let phases = Collections.Generic.Queue<unit -> unit>()

let rec advance () =
    try
        if phases.Count = 0 then
            for release in cleanup do
                release ()

            complete (checks, null)
        else
            phases.Dequeue () ()
            later advance
    with error ->
        for release in cleanup do
            release ()

        complete (checks, error.ToString ())

let service = own (BrowserMutationObservation.forDocument document)
let root = element (document, "div")
let child = element (document, "span")
let content = text (document, "")
append (root, child)
append (child, content)
let mutable calls = 0
let mutable batch = [||]
let mutable callbackSelf = None

let observer =
    service.Create (fun records self ->
        calls <- calls + 1
        batch <- records
        callbackSelf <- Some self)

let all =
    { MutationObserverOptions.Default with
        ChildList = true
        Subtree = true
        Attributes = Some true
        CharacterData = Some true
        AttributeOldValue = Some true
        CharacterDataOldValue = Some true
    }

phases.Enqueue (fun () ->
    observer.Observe (root, all)
    attribute (child, "data-x", "")
    attribute (child, "data-x", rawUtf16 ())
    attribute (child, "data-x", "new")
    attributeNS (child, "urn:mutation", "t:attr", "value")
    data (content, rawUtf16 ())
    data (content, "new")
    let sibling = element (document, "b")
    append (root, sibling)
    remove (root, sibling)
    check "No inline callbacks" (calls = 0)
    let records = observer.TakeRecords ()
    check "All record kinds queued in order" (records.Length = 8)
    check "Absent old attribute" (records[0].OldValue = None)
    check "Empty old attribute" (records[1].OldValue = Some "")
    check "UTF16 old attribute" (records[2].OldValue = Some (rawUtf16 ()))

    check
        "Namespace and local name"
        (records[3].AttributeNamespace = Some "urn:mutation"
         && records[3].AttributeName = Some "attr")

    check "Character category" (records[4].Type = MutationType.CharacterData)
    check "Empty old text" (records[4].OldValue = Some "")
    check "UTF16 old text" (records[5].OldValue = Some (rawUtf16 ()))
    check "Child addition node identity" (obj.ReferenceEquals (records[6].AddedNodes[0], sibling))
    check "Child removal node identity" (obj.ReferenceEquals (records[7].RemovedNodes[0], sibling))

    check
        "Child sibling identity"
        (records[6].PreviousSibling
         |> Option.exists (fun node -> obj.ReferenceEquals (node, child)))

    check
        "Nullable child fields"
        (records[6].NextSibling.IsNone
         && records[6].AttributeName.IsNone
         && records[6].OldValue.IsNone)

    check "Drained queue empty" (observer.TakeRecords().Length = 0)
    attribute (child, "data-x", "delivery"))

phases.Enqueue (fun () ->
    check "One deferred callback" (calls = 1 && batch.Length = 1)

    check
        "Callback same observer"
        (callbackSelf
         |> Option.exists (fun self -> obj.ReferenceEquals (self, observer)))

    check "Record exact target" (obj.ReferenceEquals (batch[0].Target, child))
    named "TypeError" (fun () -> observer.Observe (root, MutationObserverOptions.Default))
    named "TypeError" (fun () -> observer.Observe (root, { all with Attributes = Some false }))
    named "TypeError" (fun () -> observer.Observe (root, { all with CharacterData = Some false }))

    named "TypeError" (fun () ->
        observer.Observe (
            root,
            { MutationObserverOptions.Default with
                Attributes = Some false
                AttributeFilter = Some [||]
            }
        ))

    observer.Observe (
        root,
        { MutationObserverOptions.Default with
            AttributeOldValue = Some false
        }
    )

    attribute (root, "data-implied", "value")
    let implied = observer.TakeRecords ()
    check "Present false derives attributes" (implied.Length = 1 && implied[0].OldValue.IsNone)

    observer.Observe (
        root,
        { MutationObserverOptions.Default with
            CharacterDataOldValue = Some false
            Subtree = true
        }
    )

    data (content, "implied")
    check "Present false derives character data" (observer.TakeRecords().Length = 1)

    observer.Observe (
        root,
        { MutationObserverOptions.Default with
            AttributeFilter = Some [||]
        }
    )

    attribute (root, "data-empty", "x")
    check "Present empty filter matches nothing" (observer.TakeRecords().Length = 0)
    let filter = [| "data-kept" |]

    observer.Observe (
        root,
        { MutationObserverOptions.Default with
            AttributeFilter = Some filter
        }
    )

    filter[0] <- "data-other"
    attribute (root, "data-kept", "x")
    attribute (root, "data-other", "x")
    check "Filter snapshot and replacement" (observer.TakeRecords().Length = 1)
    attribute (root, "data-kept", "queued")
    observer.Disconnect ()
    check "Disconnect discards records" (observer.TakeRecords().Length = 0)
    observer.Observe (root, all)
    remove (root, child)
    attribute (child, "data-transient", "before checkpoint"))

phases.Enqueue (fun () ->
    check
        "Removed subtree observed until notification"
        (batch.Length = 2
         && batch[0].Type = MutationType.ChildList
         && batch[1].Type = MutationType.Attributes)

    check "Removed subtree retained node" (obj.ReferenceEquals (batch[0].RemovedNodes[0], child))
    let before = calls
    attribute (child, "data-transient", "after checkpoint")
    check "Transient registration removed" (observer.TakeRecords().Length = 0)
    check "Drain does not callback" (calls = before)
    let foreign = detachedDocument document
    let foreignNode = element (foreign, "div")

    observer.Observe (
        foreignNode,
        { MutationObserverOptions.Default with
            Attributes = Some true
        }
    )

    attribute (foreignNode, "data-before-adoption", "a")
    adopt (document, foreignNode)
    attribute (foreignNode, "data-after-adoption", "b")
    let records = observer.TakeRecords ()
    check "Cross-document and adoption records" (records.Length = 2)

    check
        "Adoption preserves target identity"
        (records
         |> Array.forall (fun record -> obj.ReferenceEquals (record.Target, foreignNode)))

    let detachedService = own (BrowserMutationObservation.forDocument foreign)
    let detachedObserver = detachedService.Create (fun _ _ -> ())

    detachedObserver.Observe (
        foreignNode,
        { MutationObserverOptions.Default with
            Attributes = Some true
        }
    )

    attribute (foreignNode, "data-detached-realm", "c")
    check "Document without Window uses calling realm" (detachedObserver.TakeRecords().Length = 1)
    detachedService.Dispose ()
    let iframe = frame ()
    let foreignWindowNode = element (frameDocument iframe, "div")

    observer.Observe (
        foreignWindowNode,
        { MutationObserverOptions.Default with
            Attributes = Some true
        }
    )

    attribute (foreignWindowNode, "data-iframe", "x")
    let retained = observer.TakeRecords ()

    check
        "Cross-window same-origin target"
        (retained.Length >= 1
         && obj.ReferenceEquals (retained[retained.Length - 1].Target, foreignWindowNode))

    removeFrame iframe
    observer.Disconnect ()
    observer.Dispose ()
    check "Retained record after observer/frame teardown" (retained[retained.Length - 1].AttributeName = Some "data-iframe")
    named "InvalidStateError" observer.Disconnect
    check "Disposed takeRecords empty" (observer.TakeRecords().Length = 0))

let mutable errorCalls = 0
let mutable healthyCalls = 0
let stopErrors = captureErrors (fun () -> errorCalls <- errorCalls + 1)
cleanup.Add stopErrors
let failing = service.Create (fun _ _ -> failwith "mutation intentional")

let healthy =
    service.Create (fun _ self ->
        healthyCalls <- healthyCalls + 1
        self.Dispose ())

let mutable reentrantCalls = 0
let mutable reentrantDrained = 0

let reentrant =
    service.Create (fun _ self ->
        reentrantCalls <- reentrantCalls + 1
        attribute (root, "data-nested", "inside callback")
        reentrantDrained <- self.TakeRecords().Length
        self.Disconnect ())

phases.Enqueue (fun () ->
    failing.Observe (
        root,
        { MutationObserverOptions.Default with
            AttributeFilter = Some [| "data-errors" |]
        }
    )

    healthy.Observe (
        root,
        { MutationObserverOptions.Default with
            AttributeFilter = Some [| "data-errors" |]
        }
    )

    reentrant.Observe (
        root,
        { MutationObserverOptions.Default with
            AttributeFilter = Some [| "data-errors"; "data-nested" |]
        }
    )

    attribute (root, "data-errors", "one"))

phases.Enqueue (fun () ->
    check "Callback exception reported" (errorCalls = 1)
    check "Unrelated observer continues" (healthyCalls = 1)
    check "Reentrant mutation can drain inside callback" (reentrantCalls = 1 && reentrantDrained = 1)
    failing.Dispose ()
    attribute (root, "data-errors", "two"))

phases.Enqueue (fun () ->
    check "Self close prevents second delivery" (healthyCalls = 1 && errorCalls = 1)
    check "Reentrant drain prevents second delivery" (reentrantCalls = 1)
    service.Dispose ()
    named "InvalidStateError" (fun () -> service.Create (fun _ _ -> ()) |> ignore))

let mutable faultCalls = 0
let faultReleases = ResizeArray<int>()
let mutable faultIndex = 0

let releaseFailure =
    InvalidOperationException ("controlled mutation release failure")

let browserProvider = BrowserMutationObservation.provider document

let faultService =
    own (
        Ranvier.Dom.MutationObservation.create
            {
                Create =
                    fun deliver closed ->
                        let engine = browserProvider.Create deliver closed
                        faultIndex <- faultIndex + 1
                        let id = faultIndex

                        { engine with
                            Release =
                                fun () ->
                                    engine.Release ()
                                    faultReleases.Add id

                                    if id = 1 then
                                        raise releaseFailure
                        }
            }
    )

phases.Enqueue (fun () ->
    let first = faultService.Create (fun _ _ -> faultCalls <- faultCalls + 1)
    let second = faultService.Create (fun _ _ -> faultCalls <- faultCalls + 1)

    first.Observe (
        root,
        { MutationObserverOptions.Default with
            Attributes = Some true
        }
    )

    second.Observe (
        root,
        { MutationObserverOptions.Default with
            Attributes = Some true
        }
    )

    attribute (root, "data-fault", "queued")
    let mutable actual = None

    try
        faultService.Dispose ()
    with error ->
        actual <- Some error

    check
        "Service disposal preserves first provider failure"
        (actual
         |> Option.exists (fun error -> obj.ReferenceEquals (error, releaseFailure)))

    check "Service disposal continues real browser releases" (faultReleases.ToArray () = [| 1; 2 |])
    check "Service failure still closes later observer" (second.TakeRecords().Length = 0)
    faultService.Dispose ())

phases.Enqueue (fun () -> check "Faulted service disposal suppresses queued callbacks" (faultCalls = 0))

phases.Enqueue (fun () ->
    let mutable lateService = Unchecked.defaultof<MutationObservationService>
    let mutable lateDeliver = ignore
    let mutable lateReleases = 0
    let mutable lateCalls = 0

    lateService <-
        Ranvier.Dom.MutationObservation.create
            {
                Create =
                    fun deliver _ ->
                        lateDeliver <- deliver
                        lateService.Dispose ()

                        {
                            Observe = (fun _ _ -> ())
                            Disconnect = ignore
                            TakeRecords = (fun () -> [||])
                            Release = (fun () -> lateReleases <- lateReleases + 1)
                        }
            }

    let lateObserver = lateService.Create (fun _ _ -> lateCalls <- lateCalls + 1)
    lateDeliver [||]
    check "Synchronous service close releases late handle" (lateReleases = 1)
    check "Synchronous service close disables late callback" (lateCalls = 0)
    check "Late factory observer is closed" (lateObserver.TakeRecords().Length = 0)
    lateObserver.Dispose ())

advance ()
