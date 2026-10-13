module Ranvier.Benchmarks.Bindings

open BenchmarkDotNet.Attributes
open Ranvier

/// <summary>
/// A binding against the effect it replaces: one consumer of one source, writing that source's value.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Binding">]
type BindingLifecycleBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 1)
    let memo = Memo (graph, fun _ -> source.Value + 1)
    let mutable sink = 0
    let write = fun (_: obj) (value: int) -> sink <- value

    [<Params(1, 64, 1024)>]
    member val Nodes = 1 with get, set

    /// <summary>Create and dispose effects that each write one signal's value. Divide by <c>Nodes</c>.</summary>
    [<Benchmark(Baseline = true)>]
    member this.SignalEffects() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    new Effect (graph, fun () -> sink <- source.Value)
                    |> ignore

                owner)

        owner.Dispose ()

    /// <summary>Create and dispose bindings to one signal. Divide by <c>Nodes</c>.</summary>
    [<Benchmark>]
    member this.SignalBindings() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    Binding<obj, int>.OfSignal(graph, source, null, write)
                    |> ignore

                owner)

        owner.Dispose ()

    /// <summary>Create and dispose effects that each write one memo's value. Divide by <c>Nodes</c>.</summary>
    [<Benchmark>]
    member this.MemoEffects() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    new Effect (graph, fun () -> sink <- memo.Value)
                    |> ignore

                owner)

        owner.Dispose ()

    /// <summary>Create and dispose bindings to one memo. Divide by <c>Nodes</c>.</summary>
    [<Benchmark>]
    member this.MemoBindings() =
        let owner =
            graph.CreateRoot (fun owner ->
                for _ in 1 .. this.Nodes do
                    Binding<obj, int>.OfMemo(graph, memo, null, write)
                    |> ignore

                owner)

        owner.Dispose ()

/// <summary>
/// One write to a source observed by <c>Nodes</c> consumers, flushed. <c>Kind</c> chooses effects or bindings.
/// </summary>
[<MemoryDiagnoser; BenchmarkCategory "Binding">]
type BindingUpdateBenchmarks() =
    let graph = new Graph ()
    let source = Signal (graph, 0)
    let memo = Memo (graph, fun _ -> source.Value + 1)
    let mutable sink = 0
    let mutable counter = 0
    let write = fun (_: obj) (value: int) -> sink <- value

    [<Params(1, 64, 1024)>]
    member val Nodes = 1 with get, set

    [<Params("signal-effect", "signal-binding", "memo-effect", "memo-binding")>]
    member val Kind = "signal-effect" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        for _ in 1 .. this.Nodes do
            match this.Kind with
            | "signal-effect" ->
                new Effect (graph, fun () -> sink <- source.Value)
                |> ignore
            | "signal-binding" ->
                Binding<obj, int>.OfSignal(graph, source, null, write)
                |> ignore
            | "memo-effect" ->
                new Effect (graph, fun () -> sink <- memo.Value)
                |> ignore
            | _ ->
                Binding<obj, int>.OfMemo(graph, memo, null, write)
                |> ignore

    /// <summary>Write, and let the flush run every consumer the write invalidated.</summary>
    [<Benchmark>]
    member _.WriteAndFlush() =
        counter <- counter + 1
        source.Value <- counter
