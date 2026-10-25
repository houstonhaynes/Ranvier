module Ranvier.Persistence.Tests.Restore

open Expecto
open Ranvier
open Ranvier.Persistence
open Ranvier.Persistence.Tests.Support

let private status =
    Persist.sourceKey "status" (PersistCodec.ofJournal (intCodec 1))

let private sourceKeys: IPersistKey list = [ status ]

/// <summary>The reading as text, with a failure as its kind and message.</summary>
let private describe (reading: Reading<int>) =
    match reading with
    | Reading.Ready value -> "ready " + string value
    | Reading.Pending -> "pending"
    | Reading.Failed error ->
        match error with
        | :? PersistedFailure -> "persisted failure " + error.Message
        | _ -> "failure " + error.Message

/// <summary>Loads <c>keys</c> from <c>store</c>.</summary>
let private load (store: IStateStore) (keys: IPersistKey list) =
    Persist.loadAsync store keys |> Async.AwaitTask

/// <summary>Runs <c>body</c> in a new graph whose journal is the session's wrapper over <c>journal</c>, then waits for every save.</summary>
let private inGraph (session: PersistSession) (journal: MemoryJournal) (body: Graph -> 'T) =
    async {
        let result =
            use graph = new Graph (GraphOptions.Default.WithJournal (session.Journal journal))
            use _active = graph.Activate ()
            body graph

        do! session.FlushAsync () |> Async.AwaitTask
        return result
    }

/// <summary>The reading of a source restored for <c>status</c> from <c>store</c>, in a new graph.</summary>
let private reload (store: IStateStore) =
    async {
        let! session = load store sourceKeys
        return! inGraph session (MemoryJournal "app") (fun _ -> describe (Persist.source session status).TryValue)
    }

let private sourceTests =
    testList
        "Persist.source"
        [
            testCaseAsync "a settled value round-trips"
            <| async {
                let store = MemoryStateStore () :> IStateStore
                let! session = load store sourceKeys
                do! inGraph session (MemoryJournal "app") (fun _ -> (Persist.source session status).Settle 7)
                let! restored = reload store
                Expect.equal restored "ready 7" "the restored source holds the settled value"
            }

            testCaseAsync "a failure round-trips as a PersistedFailure carrying its message"
            <| async {
                let store = MemoryStateStore () :> IStateStore
                let! session = load store sourceKeys
                do! inGraph session (MemoryJournal "app") (fun _ -> (Persist.source session status).Fail(exn "boom"))
                let! restored = reload store
                Expect.equal restored "persisted failure boom" "the restored source fails with the stored message"
            }

            testCaseAsync "a settle after a failure replaces the stored failure"
            <| async {
                let store = MemoryStateStore () :> IStateStore
                let! session = load store sourceKeys

                do!
                    inGraph session (MemoryJournal "app") (fun _ ->
                        let source = Persist.source session status
                        source.Fail (exn "boom")
                        source.Settle 3)

                let! restored = reload store
                Expect.equal restored "ready 3" "the last status"
            }

            testCaseAsync "a pending source stores no record and restores pending"
            <| async {
                let store = RecordingStore ()
                let! session = load store sourceKeys
                do! inGraph session (MemoryJournal "app") (fun _ -> Persist.source session status |> ignore)
                Expect.equal (savedPayloads store) [] "no save"
                let! restored = reload store
                Expect.equal restored "pending" "the restored source is pending"
            }

            testCaseAsync "a settle saves the status frames at the codec's version"
            <| async {
                let store = RecordingStore ()
                let! session = load store sourceKeys
                do! inGraph session (MemoryJournal "app") (fun _ -> (Persist.source session status).Settle 7)

                Expect.equal
                    (savedPayloads store |> List.map List.sort)
                    [ [ "$ranvier/position", "1"; "status", "5:ready1:7" ] ]
                    "the ready frame and the value frame"
            }

            testCaseAsync "a restored source records no journal entry and starts settled"
            <| async {
                let store = MemoryStateStore () :> IStateStore

                do!
                    store.SaveMany [ record "status" 1 "5:ready1:7" ]
                    |> Async.AwaitTask

                let! session = load store sourceKeys
                let journal = MemoryJournal "app"

                let observed =
                    use graph = new Graph (GraphOptions.Default.WithJournal (session.Journal journal))
                    use _active = graph.Activate ()
                    let source = Persist.source session status
                    let seen = ResizeArray<string>()
                    createEffectOn (fun () -> source.TryValue) (fun reading -> seen.Add (describe reading))
                    List.ofSeq seen

                Expect.equal observed [ "ready 7" ] "the first read sees the stored value"
                Expect.equal journal.Entries [] "the restore records no entry"
            }

            testCaseAsync "a stored value frame migrates with the codec"
            <| async {
                let store = MemoryStateStore () :> IStateStore

                do!
                    store.SaveMany [ record "status" 1 "5:ready1:7" ]
                    |> Async.AwaitTask

                let migrated =
                    Persist.sourceKey
                        "status"
                        (PersistCodec.ofJournal (intCodec 2)
                         |> PersistCodec.withMigration 1 (fun payload -> Result.Ok (payload + "0")))

                let! session = load store [ migrated ]

                let! restored =
                    inGraph session (MemoryJournal "app") (fun _ -> describe (Persist.source session migrated).TryValue)

                Expect.equal restored "ready 70" "the migrated value"
            }

            testCaseAsync "a malformed status record fails the load with DecodeFailed naming the key"
            <| async {
                let store = MemoryStateStore () :> IStateStore

                do!
                    store.SaveMany [ record "status" 1 "5:maybe1:7" ]
                    |> Async.AwaitTask

                let! error = failureOf (load store sourceKeys)

                match error with
                | :? PersistLoadException as error ->
                    Expect.equal error.Key "status" "the failure names the key"

                    match error.Failure with
                    | DecodeFailed _ -> ()
                    | other -> failtestf "expected DecodeFailed, got %A" other
                | other -> failtestf "expected a PersistLoadException, got: %s" other.Message
            }

            testCaseAsync "an unframed status record fails the load with DecodeFailed"
            <| async {
                let store = MemoryStateStore () :> IStateStore

                do!
                    store.SaveMany [ record "status" 1 "7" ]
                    |> Async.AwaitTask

                do! expectLoadFailure "status" (DecodeFailed "malformed frame at offset 0") (load store sourceKeys)
            }

            testCaseAsync "DiscardUndecodable restores a malformed status record as pending"
            <| async {
                let store = MemoryStateStore () :> IStateStore

                do!
                    store.SaveMany [ record "status" 1 "5:maybe1:7" ]
                    |> Async.AwaitTask

                let! session =
                    Persist.loadWith
                        { PersistOptions.defaults with
                            DiscardUndecodable = true
                        }
                        store
                        sourceKeys
                    |> Async.AwaitTask

                let! restored =
                    inGraph session (MemoryJournal "app") (fun _ -> describe (Persist.source session status).TryValue)

                Expect.equal restored "pending" "the discarded record"
            }

            testCase "a persisted failure carries its message"
            <| fun () ->
                let error = PersistedFailure "boom"
                Expect.equal error.Message "boom" "the message"
        ]

[<Tests>]
let tests = testList "Restore" [ sourceTests ]
