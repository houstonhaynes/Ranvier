/// <summary>
/// The conformance suite every <c>IStateStore</c> passes. A store's tests call <c>StoreConformance.suite</c> with a
/// factory that returns an empty store on each call.
/// </summary>
module Ranvier.Persistence.Tests.StoreConformance

open Expecto
open Ranvier.Persistence

let private record key version payload =
    {
        Key = key
        Version = version
        Payload = payload
        ETag = None
    }

/// <summary>The key, version and payload of each record, ordered by key.</summary>
let private contents (records: StoredRecord list) =
    records
    |> List.map (fun record -> record.Key, record.Version, record.Payload)
    |> List.sortBy (fun (key, _, _) -> key)

let private loadMany (store: IStateStore) keys =
    store.LoadMany keys |> Async.AwaitTask

let private saveMany (store: IStateStore) records =
    store.SaveMany records |> Async.AwaitTask

let private deleteMany (store: IStateStore) keys =
    store.DeleteMany keys |> Async.AwaitTask

/// <summary>The load, save and delete cases, run against stores made by <c>create</c>.</summary>
let storeCases (create: unit -> IStateStore) =
    [
        testCaseAsync "an empty store loads no records"
        <| async {
            let store = create ()
            let! loaded = loadMany store [ "a"; "b" ]
            Expect.isEmpty loaded "absent keys load nothing"
        }

        testCaseAsync "saved records load back"
        <| async {
            let store = create ()
            do! saveMany store [ record "a" 1 "alpha"; record "b" 2 "beta" ]
            let! loaded = loadMany store [ "a"; "b" ]
            Expect.equal (contents loaded) [ "a", 1, "alpha"; "b", 2, "beta" ] "both records load"
        }

        testCaseAsync "a load returns the requested keys only"
        <| async {
            let store = create ()
            do! saveMany store [ record "a" 1 "alpha"; record "b" 1 "beta" ]
            let! loaded = loadMany store [ "b"; "missing" ]
            Expect.equal (contents loaded) [ "b", 1, "beta" ] "only the present requested key loads"
        }

        testCaseAsync "a later save of a key replaces its record"
        <| async {
            let store = create ()
            do! saveMany store [ record "a" 1 "first" ]
            do! saveMany store [ record "a" 2 "second" ]
            let! loaded = loadMany store [ "a" ]
            Expect.equal (contents loaded) [ "a", 2, "second" ] "the last save wins"
        }

        testCaseAsync "deleted keys load nothing and absent keys are skipped"
        <| async {
            let store = create ()
            do! saveMany store [ record "a" 1 "alpha"; record "b" 1 "beta" ]
            do! deleteMany store [ "a"; "missing" ]
            let! loaded = loadMany store [ "a"; "b"; "missing" ]
            Expect.equal (contents loaded) [ "b", 1, "beta" ] "only the kept record loads"
        }

        testCaseAsync "keys and payloads with separators, reserved prefixes and non-ASCII text round trip"
        <| async {
            let store = create ()

            let records =
                [
                    record "$ranvier/buster" 1 "v2"
                    record "todos/42" 1 "{\"title\":\"a/b\\\\c\"}"
                    record "Ünïcode: key?*<>|" 3 "line one\nline two\té"
                    record "TODOS/42" 1 "upper"
                ]

            do! saveMany store records
            let! loaded = loadMany store (records |> List.map _.Key)
            Expect.equal (contents loaded) (contents records) "every record loads unchanged"
        }
    ]

/// <summary>The store conformance cases under the store's name.</summary>
let suite (name: string) (create: unit -> IStateStore) =
    testList name (storeCases create)
