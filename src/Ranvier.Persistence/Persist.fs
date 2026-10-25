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

/// <summary>The restored values of one load, and the store later writes go to.</summary>
type PersistSession
    internal
    (store: IStateStore, options: PersistOptions, declared: HashSet<string>, values: Dictionary<string, obj>, position: int option, busterStale: bool)
    =

    /// <summary>
    /// The journal position recorded with the last saved batch, or <c>None</c> when the store holds none.
    /// </summary>
    member _.Position = position

    member internal _.Store = store
    member internal _.Options = options

    /// <summary>True when the stored buster differs from <c>Options.Buster</c>; the next save rewrites it.</summary>
    member internal _.BusterStale = busterStale

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

/// <summary>Declaring, loading and restoring persisted inputs.</summary>
module Persist =
    /// <summary>The prefix of the keys the package stores its own records under.</summary>
    [<Literal>]
    let ReservedPrefix = "$ranvier/"

    let internal busterKey = ReservedPrefix + "buster"
    let internal positionKey = ReservedPrefix + "position"

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
    /// <c>options.DiscardUndecodable</c> is set. A stored buster unequal to <c>options.Buster</c> discards every record.
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
