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

let slug (name: string) =
    let text =
        Text.RegularExpressions.Regex.Replace (name.ToLowerInvariant (), "[^a-z0-9]+", "-")

    let text = text.Trim '-'
    if text.Length > 80 then text.Substring (0, 80) else text

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
            Bridge.intercept host.Document (Bridge.nodeOf target) memberName wrap

        member _.Record(name, text) =
            count <- count + 1
            Directory.CreateDirectory directory |> ignore
            File.WriteAllText (Path.Combine (directory, $"%02d{count}-{slug name}.txt"), text)
    }
