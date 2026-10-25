namespace Ranvier.Persistence

open System
open System.Collections.Generic
open System.Threading.Tasks
open Ranvier

/// <summary>Building and applying persist codecs.</summary>
module PersistCodec =
    /// <summary>The journal codec with no migrations.</summary>
    let ofJournal (codec: JournalCodec<'T>) : PersistCodec<'T> =
        { Codec = codec; Migrations = [] }

    /// <summary>
    /// Adds the migration from version <c>from</c> to <c>from + 1</c>. Migrations apply in version order, whatever
    /// order they are added in.
    /// </summary>
    let withMigration (from: int) (migrate: string -> Result<string, string>) (codec: PersistCodec<'T>) =
        { codec with
            Migrations =
                codec.Migrations
                @ [ { From = from; Migrate = migrate } ]
        }

    /// <summary>
    /// The value of <c>record</c>: its payload brought to the codec's version by the migrations, then decoded.
    /// </summary>
    let restore (codec: PersistCodec<'T>) (record: StoredRecord) : Result<'T, PersistFailure> =
        let target = codec.Codec.Version

        let rec migrate (version: int) (payload: string) =
            if version = target then
                Result.Ok payload
            else
                match
                    codec.Migrations
                    |> List.tryFind (fun migration -> migration.From = version)
                with
                | None -> Result.Error (MissingMigration version)
                | Some migration ->
                    match migration.Migrate payload with
                    | Result.Ok next -> migrate (version + 1) next
                    | Result.Error message -> Result.Error (MigrationFailed (version, message))

        if record.Version > target then
            Result.Error (NewerVersion (record.Version, target))
        else
            migrate record.Version record.Payload
            |> Result.bind (fun payload ->
                codec.Codec.Decode payload
                |> Result.mapError DecodeFailed)

    /// <summary>The record of <c>value</c> under <c>key</c>, at the codec's version.</summary>
    let encode (codec: PersistCodec<'T>) (key: string) (value: 'T) : StoredRecord =
        {
            Key = key
            Version = codec.Codec.Version
            Payload = codec.Codec.Encode value
            ETag = None
        }

/// <summary>A declared persisted key, as <c>Persist.loadAsync</c> reads it.</summary>
type IPersistKey =
    /// <summary>The store key.</summary>
    abstract Key: string
    /// <summary>The restored value of the key's stored record, boxed.</summary>
    abstract Restore: record: StoredRecord -> Result<obj, PersistFailure>

/// <summary>A persisted value's store key and codec. <c>Persist.key</c> declares one.</summary>
type PersistKey<'T> internal (key: string, codec: PersistCodec<'T>) =
    /// <summary>The store key.</summary>
    member _.Key = key

    /// <summary>The codec of the stored value.</summary>
    member _.Codec = codec

    interface IPersistKey with
        member _.Key = key

        member _.Restore record =
            PersistCodec.restore codec record
            |> Result.map box

/// <summary>The store keys of the package's own records.</summary>
module internal PersistReserved =
    [<Literal>]
    let Prefix = "$ranvier/"

    let busterKey = Prefix + "buster"
    let positionKey = Prefix + "position"

/// <summary>
/// The restored values of one load, and the graph whose persisted inputs save to its store. A session serves one graph.
/// </summary>
type PersistSession
    internal
    (store: IStateStore, options: PersistOptions, declared: HashSet<string>, values: Dictionary<string, obj>, position: int option, busterStale: bool)
    =
    let dirty = Dictionary<string, StoredRecord>()
    let live = HashSet<string>()
    let mutable graph: Graph option = None
    let mutable drain: Signal<int> option = None
    let mutable busterPending = busterStale
    let mutable saves = PersistTask.completed ()
    let mutable outcome = PersistTask.completed ()

    let enqueue (work: unit -> Task) =
        let save = PersistTask.after saves work
        saves <- save
        outcome <- PersistTask.both outcome save

    // Deletes the stale keys, then saves the records with the new buster. The bust stays pending until both succeed.
    let saveBusted (records: StoredRecord list) : Task =
        let written = HashSet<string>(records |> List.map _.Key)

        let stale =
            [
                yield! declared
                yield PersistReserved.positionKey
                if options.Buster.IsNone then
                    yield PersistReserved.busterKey
            ]
            |> List.filter (written.Contains >> not)

        let records =
            match options.Buster with
            | Some buster ->
                {
                    Key = PersistReserved.busterKey
                    Version = 1
                    Payload = buster
                    ETag = None
                }
                :: records
            | None -> records

        PersistTask.andThen (fun () -> PersistTask.andThen (fun () -> store.DeleteMany stale) (fun () -> store.SaveMany records)) (fun () ->
            busterPending <- false
            PersistTask.completed ())

    let drainDirty () =
        if dirty.Count > 0 then
            let records = List.ofSeq dirty.Values
            dirty.Clear ()

            enqueue (fun () ->
                if busterPending then
                    saveBusted records
                else
                    store.SaveMany records)

    /// <summary>
    /// The journal position stored under <c>$ranvier/position</c> at load, or <c>None</c> when the store holds none.
    /// </summary>
    member _.Position = position

    /// <summary>
    /// A task that completes once every save started before the call has completed. It faults with the exception of
    /// the first save to fault since the previous call; the saves after a faulted save still run.
    /// </summary>
    member _.FlushAsync() : Task =
        let reported = outcome
        outcome <- PersistTask.after saves PersistTask.completed
        reported

    member internal _.Store = store
    member internal _.Options = options

    /// <summary>The restored value of a declared key, or <c>None</c> when the store held no restorable record.</summary>
    member internal _.TryRestored(key: string) =
        if not (declared.Contains key) then
            raise (
                InvalidOperationException (
                    "The key \""
                    + key
                    + "\" was not declared to the load that made this session; pass it to Persist.loadAsync."
                )
            )

        match values.TryGetValue key with
        | true, value -> Some value
        | _ -> None

    /// <summary>
    /// Binds the session to <c>current</c> on first use and makes the drain: a signal written by the first change of
    /// a flush, and a root effect on it that saves every changed record in one <c>SaveMany</c>.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">The session already serves another graph.</exception>
    member internal _.Attach(current: Graph) =
        match graph with
        | Some bound when obj.ReferenceEquals (bound, current) -> ()
        | Some _ ->
            raise (InvalidOperationException "A persist session serves one graph, and this session already serves another; load a session per graph.")
        | None ->
            graph <- Some current

            runWithOwner current.Root (fun () ->
                let signal = createSignal 0
                drain <- Some signal
                createEffectOn (fun () -> signal.Value) (fun _ -> drainDirty ()))

    /// <summary>Claims <c>key</c> for one live persisted input until the current owner is disposed.</summary>
    /// <exception cref="T:System.InvalidOperationException">A live persisted input already holds the key.</exception>
    member internal _.Claim(key: string) =
        if not (live.Add key) then
            raise (
                InvalidOperationException (
                    "The key \""
                    + key
                    + "\" already belongs to a live persisted input."
                )
            )

        onCleanup (fun () -> live.Remove key |> ignore)

    /// <summary>Records <c>record</c> for the drain of the current flush; a later record of its key replaces it.</summary>
    member internal _.Mark(record: StoredRecord) =
        if dirty.Count = 0 then
            match drain with
            | Some signal -> signal.Value <- signal.Value + 1
            | None -> ()

        dirty[record.Key] <- record

/// <summary>Declaring, loading and restoring persisted inputs.</summary>
module Persist =
    /// <summary>The prefix of the keys the package stores its own records under.</summary>
    [<Literal>]
    let ReservedPrefix = PersistReserved.Prefix

    let internal busterKey = PersistReserved.busterKey
    let internal positionKey = PersistReserved.positionKey

    let internal checkKey (key: string) =
        if String.IsNullOrEmpty key then
            invalidArg (nameof key) "A persisted key cannot be empty."

        if key.StartsWith (ReservedPrefix, StringComparison.Ordinal) then
            invalidArg
                (nameof key)
                ("The key \""
                 + key
                 + "\" is under the reserved prefix "
                 + ReservedPrefix
                 + ".")

    /// <summary>Declares a persisted value stored under <c>key</c> with <c>codec</c>.</summary>
    /// <exception cref="T:System.ArgumentException">The key is empty or begins with <c>$ranvier/</c>.</exception>
    let key (key: string) (codec: PersistCodec<'T>) : PersistKey<'T> =
        checkKey key
        PersistKey (key, codec)

    /// <summary>
    /// Loads the stored records of <c>keys</c>. The task faults with a <c>PersistLoadException</c> naming the key when
    /// a record is newer than its codec, lacks a migration, fails a migration or fails to decode, unless
    /// <c>options.DiscardUndecodable</c> is set. A stored buster unequal to <c>options.Buster</c> discards every record, and the session's first successful save deletes
    /// the stored records of the unwritten keys and the journal position.
    /// </summary>
    /// <exception cref="T:System.ArgumentException">Two of the keys share a store key.</exception>
    let loadWith (options: PersistOptions) (store: IStateStore) (keys: IPersistKey list) : Task<PersistSession> =
        let declared = HashSet<string>()

        for key in keys do
            if not (declared.Add key.Key) then
                invalidArg
                    (nameof keys)
                    ("The key \""
                     + key.Key
                     + "\" is declared more than once.")

        let names =
            busterKey
            :: positionKey
            :: (keys |> List.map _.Key)

        PersistTask.map (store.LoadMany names) (fun records ->
            let byKey = Dictionary<string, StoredRecord>()

            for record in records do
                byKey[record.Key] <- record

            let stored name =
                match byKey.TryGetValue name with
                | true, record -> Some record
                | _ -> None

            let values = Dictionary<string, obj>()

            if
                (stored busterKey |> Option.map _.Payload)
                <> options.Buster
            then
                PersistSession (store, options, declared, values, None, true)
            else
                for key in keys do
                    match stored key.Key with
                    | None -> ()
                    | Some record ->
                        match key.Restore record with
                        | Result.Ok value -> values[key.Key] <- value
                        | Result.Error failure ->
                            if not options.DiscardUndecodable then
                                raise (PersistLoadException (key.Key, failure))

                let position =
                    stored positionKey
                    |> Option.bind (fun record ->
                        match Int32.TryParse record.Payload with
                        | true, position -> Some position
                        | _ -> None)

                PersistSession (store, options, declared, values, position, false))

    /// <summary>Loads the stored records of <c>keys</c> with <c>PersistOptions.defaults</c>.</summary>
    /// <exception cref="T:System.ArgumentException">Two of the keys share a store key.</exception>
    let loadAsync (store: IStateStore) (keys: IPersistKey list) : Task<PersistSession> =
        loadWith PersistOptions.defaults store keys

    /// <summary>The restored value of <c>key</c>, or <c>fallback</c> when the store held no restorable record.</summary>
    /// <exception cref="T:System.InvalidOperationException">The key was not passed to the load.</exception>
    let restore (session: PersistSession) (key: PersistKey<'T>) (fallback: 'T) : 'T =
        match session.TryRestored key.Key with
        | Some value -> unbox<'T> value
        | None -> fallback

    /// <summary>
    /// A signal holding the restored value of <c>key</c>, or <c>fallback</c>, made in the current graph and owner.
    /// After each flush that changes it, its value is saved to the session's store; the changes of one flush save in
    /// one <c>SaveMany</c>.
    /// </summary>
    /// <exception cref="T:System.InvalidOperationException">
    /// The key was not passed to the load, a live persisted input already holds it, or the session serves another graph.
    /// </exception>
    let signal (session: PersistSession) (key: PersistKey<'T>) (fallback: 'T) : Signal<'T> =
        let initial = restore session key fallback
        session.Attach Graph.Current
        session.Claim key.Key
        let signal = createSignal initial
        let mutable baseline = Some initial

        createEffectOn (fun () -> signal.Value) (fun value ->
            let record = PersistCodec.encode key.Codec key.Key value

            match baseline with
            | Some initial when key.Codec.Codec.Encode initial = record.Payload -> baseline <- None
            | _ ->
                baseline <- None
                session.Mark record)

        signal
