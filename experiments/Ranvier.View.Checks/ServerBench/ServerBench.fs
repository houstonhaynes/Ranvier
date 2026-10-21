module ServerBench

open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running

[<MemoryDiagnoser>]
type ComponentPage() =
    [<Params(1, 16, 128)>]
    member val Rows = 0 with get, set

    [<Benchmark>]
    member this.Render() =
        ServerFixtures.renderComponentPage this.Rows

[<MemoryDiagnoser>]
type KeyedPage() =
    [<Params(1000, 10000)>]
    member val Rows = 0 with get, set

    [<Benchmark>]
    member this.Render() =
        ServerFixtures.renderKeyedPage this.Rows

[<EntryPoint>]
let main args =
    if Array.contains "--bytes" args then
        for fixture, render, rows in
            [
                "component", ServerFixtures.renderComponentPage, [ 1; 16; 128 ]
                "keyed", ServerFixtures.renderKeyedPage, [ 1000; 10000 ]
            ] do
            for count in rows do
                let bytes = System.Text.Encoding.UTF8.GetByteCount (render count)

                printfn "{\"fixture\":\"%s\",\"rows\":%d,\"bytes\":%d,\"bytesPerRow\":%f}" fixture count bytes (float bytes / float count)
    else
        BenchmarkSwitcher.FromTypes([| typeof<ComponentPage>; typeof<KeyedPage> |]).Run args
        |> ignore

    0
