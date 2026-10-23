module Ranvier.View.Twin.Tests.TwinHarness

open System
open System.IO
open Ranvier.View.Twin
open Ranvier.View.Testing.Conformance

/// <summary>The repository root: <c>RANVIER_REPO</c> when set, else the directory four levels above this file.</summary>
let repository () =
    match Environment.GetEnvironmentVariable "RANVIER_REPO" with
    | null
    | "" -> Path.GetFullPath (Path.Combine (__SOURCE_DIRECTORY__, "..", ".."))
    | root -> root

// FNV-1a over the name's UTF-16 code units, as hex: conformance.test.mjs computes the same.
let private nameHash (name: string) =
    let mutable hash = 2166136261u

    for c in name do
        hash <- (hash ^^^ uint32 c) * 16777619u

    hash.ToString "x8"

/// <summary>A case's artefact directory: its name in lower-case words, cut to 71 characters plus the name's hash when longer than 80.</summary>
let slug (name: string) =
    let text =
        Text.RegularExpressions.Regex.Replace (name.ToLowerInvariant (), "[^a-z0-9]+", "-")

    let text = text.Trim '-'

    if text.Length > 80 then
        text.Substring(0, 71).TrimEnd '-'
        + "-"
        + nameHash name
    else
        text

let create (host: TwinHost) (directory: string) : Harness =
    let mutable count = 0

    { new Harness with
        member _.Platform = "twin"
        member _.Document = host.Document.Native :?> Fable.Core.TS.Dom.Document

        member _.Post callback =
            host.Clock.Post callback

        member _.RunMicrotasks() =
            host.Clock.RunMicrotasks () |> ignore

        member _.AdvanceBy milliseconds =
            host.Clock.AdvanceBy milliseconds

        member _.ListenerErrors() =
            let messages =
                [
                    for error in host.Document.ListenerErrors ->
                        match error with
                        | TwinListenerException (_, inner) -> inner.Message
                        | other -> other.Message
                ]

            host.Document.ListenerErrors.Clear ()
            messages

        member _.Intercept(target, memberName, wrap) =
            match target, Bridge.nodeOf target with
            | null, _ -> Bridge.intercept host.Document null memberName wrap
            | _, null -> raise (NotSupportedException $"The twin intercepts the members of nodes only, not of a {target.GetType().Name}")
            | _, node -> Bridge.intercept host.Document node memberName wrap

        member _.ObserveMutations root =
            let under = Bridge.nodeOf root
            let changes = ResizeArray<string * Fable.Core.TS.Dom.Node>()

            let observer =
                host.Document.ObserveMutations (fun (kind, target) ->
                    if under.Contains target then
                        changes.Add ((kind, unbox target.Native)))

            fun () ->
                observer.Dispose ()
                changes.ToArray ()

        member _.Record(name, text) =
            count <- count + 1
            Directory.CreateDirectory directory |> ignore
            File.WriteAllText (Path.Combine (directory, $"%02d{count}-{slug name}.txt"), text)
    }
