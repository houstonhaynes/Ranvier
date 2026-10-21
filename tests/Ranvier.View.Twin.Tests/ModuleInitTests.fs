module Ranvier.View.Twin.Tests.ModuleInitTests

open System
open System.Runtime.CompilerServices
open Expecto
open Ranvier.View.Foundation

let private startupTypes (assembly: Reflection.Assembly) =
    assembly.GetTypes ()
    |> Array.filter (fun t -> t.FullName.StartsWith "<StartupCode$")

let private recordingHost (posted: ResizeArray<unit -> unit>) =
    { new Native.IHost with
        member _.Document = Unchecked.defaultof<_>

        member _.QueueMicrotask callback =
            posted.Add callback

        member _.SetTimeout(_, _) = null
        member _.ClearTimeout _ = ()
        member _.RequestAnimationFrame _ = null
        member _.CancelAnimationFrame _ = ()
        member _.DeleteBetween(_, _) = ()
        member _.Listen(_, _, _) = ignore
        member _.OverrideCurrentTarget(_, _) = ()
    }

[<Tests>]
let tests =
    testList
        "Module initialisation"
        [
            testCase "Portable and Foundation initialise on .NET with no host"
            <| fun () ->
                for assembly in [ typeof<Scheduling.Queue>.Assembly; typeof<ScopePhase>.Assembly ] do
                    for t in startupTypes assembly do
                        try
                            RuntimeHelpers.RunClassConstructor t.TypeHandle
                        with :? TypeInitializationException as e ->
                            failtestf "%s failed to initialise: %s" t.FullName (e.GetBaseException ()).Message

            testCase "reading the document with no host names TwinDocument"
            <| fun () ->
                let e = Expect.throwsC (fun () -> Native.document () |> ignore) id
                Expect.isTrue (e :? InvalidOperationException) "InvalidOperationException"
                Expect.stringContains e.Message "create a TwinDocument first" "message"

            testCase "an installed host serves the thread until it is disposed"
            <| fun () ->
                let posted = ResizeArray<unit -> unit>()

                do
                    use _ = Native.install (recordingHost posted)
                    Native.queueMicrotask ignore
                    Expect.equal posted.Count 1 "posted through the host"

                Expect.throwsT<InvalidOperationException> (fun () -> Native.queueMicrotask ignore) "uninstalled"

            testCase "disposing an installation on another thread or out of order raises"
            <| fun () ->
                let first = Native.install (recordingHost (ResizeArray ()))
                let second = Native.install (recordingHost (ResizeArray ()))
                Expect.throwsT<InvalidOperationException> (fun () -> first.Dispose ()) "out of order"
                let failure = ref null

                let thread =
                    Threading.Thread (fun () ->
                        try
                            second.Dispose ()
                        with e ->
                            failure.Value <- e)

                thread.Start ()
                thread.Join ()
                Expect.isTrue (failure.Value :? InvalidOperationException) "another thread"
                second.Dispose ()
                first.Dispose ()
                Expect.throwsT<InvalidOperationException> (fun () -> Native.queueMicrotask ignore) "both uninstalled"

            testCase "one host installed twice is disposed innermost first"
            <| fun () ->
                let host = recordingHost (ResizeArray ())
                let outer = Native.install host
                let inner = Native.install host
                Expect.throwsT<InvalidOperationException> (fun () -> outer.Dispose ()) "the outer installation first"
                inner.Dispose ()
                outer.Dispose ()
                Expect.throwsT<InvalidOperationException> (fun () -> Native.queueMicrotask ignore) "uninstalled"
        ]
