/// <summary>
/// The persistence workloads: a signal write with and without a persisted key, over a
/// <c>MemoryStateStore</c>, so the persisted row adds only the encode and the save to the plain row.
/// </summary>
module CounterBench.PersistWorkloads

open System
open Ranvier
open Ranvier.Persistence

let mutable private sink = 0

let private countCodec: PersistCodec<int> =
    PersistCodec.ofJournal
        {
            Name = "int"
            Version = 1
            Encode = string
            Decode =
                fun text ->
                    match Int32.TryParse text with
                    | true, value -> Result.Ok value
                    | _ -> Result.Error ("not an int: " + text)
        }

let private countKey = Persist.key "count" countCodec

/// <summary>
/// <c>n</c> writes of a new value to a signal read by one <c>createEffectOn</c>, each write its own flush. When
/// <c>persisted</c>, the signal is a persisted signal, and the teardown fails unless the store holds the last value.
/// </summary>
let write (persisted: bool) (n: int) : Workloads.Workload =
    let store = MemoryStateStore ()

    let session = (Persist.loadAsync store [ countKey ]).Result

    let graph = Workloads.newGraph ()
    let active = graph.Activate ()

    let owner, source =
        graph.CreateRoot (fun owner ->
            let source =
                if persisted then
                    Persist.signal session countKey 0
                else
                    Signal (graph, 0)

            createEffectOn (fun () -> source.Value) (fun value -> sink <- value)
            owner, source)

    active.Dispose ()

    {
        Run =
            fun () ->
                for i in 1..n do
                    source.Value <- i
        Teardown =
            fun () ->
                session.FlushAsync().Wait()

                if persisted then
                    match store.Records with
                    | [ record ] when record.Payload = string n -> ()
                    | records -> failwith $"persist-write stored %A{records}, not the last write %d{n}."

                owner.Dispose ()
                (graph :> IDisposable).Dispose()
    }
