module Ranvier.Persistence.Tests.Support

open System
open Expecto
open Ranvier
open Ranvier.Persistence

let record key version payload =
    {
        Key = key
        Version = version
        Payload = payload
        ETag = None
    }

/// <summary>An <c>int</c> codec at <c>version</c> whose payload is the decimal text.</summary>
let intCodec (version: int) : JournalCodec<int> =
    {
        Name = "int"
        Version = version
        Encode = string
        Decode =
            fun text ->
                match Int32.TryParse text with
                | true, value -> Result.Ok value
                | _ -> Result.Error ("not an int: " + text)
    }

let rec private innermost (error: exn) =
#if FABLE_COMPILER
    error
#else
    match error with
    | :? AggregateException as aggregate when not (isNull aggregate.InnerException) -> innermost aggregate.InnerException
    | _ -> error
#endif

/// <summary>The exception the work raises, unwrapped from task aggregation; fails the test when it completes.</summary>
let failureOf (work: Async<'T>) =
    async {
        try
            let! _ = work
            return failtest "the work completed"
        with error ->
            return innermost error
    }

/// <summary>Expects the load to fail with a <c>PersistLoadException</c> naming <c>key</c> and carrying <c>failure</c>.</summary>
let expectLoadFailure (key: string) (failure: PersistFailure) (load: Async<PersistSession>) =
    async {
        let! error = failureOf load

        match error with
        | :? PersistLoadException as error ->
            Expect.equal error.Key key "the failure names the key"
            Expect.equal error.Failure failure "the failure reason"
            Expect.stringContains error.Message ("\"" + key + "\"") "the message names the key"
        | other -> failtestf "expected a PersistLoadException, got %s: %s" (other.GetType().Name) other.Message
    }

/// <summary>A memory store that records the records of each <c>SaveMany</c> call, in call order.</summary>
type RecordingStore() =
    let inner = MemoryStateStore ()
    let saves = ResizeArray<StoredRecord list>()

    member _.Inner = inner
    member _.Saves = List.ofSeq saves

    interface IStateStore with
        member _.LoadMany keys =
            (inner :> IStateStore).LoadMany keys

        member _.SaveMany records =
            saves.Add records
            (inner :> IStateStore).SaveMany records

        member _.DeleteMany keys =
            (inner :> IStateStore).DeleteMany keys

        member _.Clear() =
            (inner :> IStateStore).Clear()

/// <summary>The key and payload of each record of each save.</summary>
let savedPayloads (store: RecordingStore) =
    store.Saves
    |> List.map (List.map (fun record -> record.Key, record.Payload))
