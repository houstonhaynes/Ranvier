# Fixed-dependency binding benchmark

A fixed-dependency `Binding` measured against the `Effect` it replaces: one consumer of one source, writing that
source's value. Source: `bench/Ranvier.Benchmarks/Bindings.fs`. Raw reports:
`docs/.ai/benchmarks/results/Ranvier.Benchmarks.Bindings.*-report-github.md`.

Run: `dotnet run --project bench/Ranvier.Benchmarks -c Release -- --anyCategories Binding`

- Machine: AMD Ryzen 9 9900X 4.40GHz, 1 CPU, 24 logical and 12 physical cores; Windows 11 (10.0.26200.9457/25H2)
- Runtime: .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4; .NET SDK 10.0.401; BenchmarkDotNet v0.15.8
- Job: UnrollFactor=16

## Gate

- Lifecycle, at `Nodes` 64 and 1024: each binding's mean is at most 0.85x its effect's, or its allocation is at most
  0.75x the effect's.
- Update: each binding kind's `WriteAndFlush` mean is at most 1.05x its effect kind's, at every `Nodes`.

The binding passes the gate.

## Lifecycle (create and dispose `Nodes` consumers under a root)

Ratios are binding / effect. `Nodes` 1 is outside the gate and listed for reference.

| Nodes | Method | Mean | Allocated | Mean ratio | Alloc ratio |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | SignalEffects | 146.8 ns | 624 B | 1.000 | 1.000 |
| 1 | SignalBindings | 124.7 ns | 472 B | 0.849 | 0.756 |
| 1 | MemoEffects | 155.1 ns | 624 B | 1.000 | 1.000 |
| 1 | MemoBindings | 123.8 ns | 472 B | 0.798 | 0.756 |
| 64 | SignalEffects | 7,736.2 ns | 21,288 B | 1.000 | 1.000 |
| 64 | SignalBindings | 5,588.6 ns | 11,560 B | 0.722 | 0.543 |
| 64 | MemoEffects | 7,386.9 ns | 21,288 B | 1.000 | 1.000 |
| 64 | MemoBindings | 5,506.9 ns | 11,560 B | 0.745 | 0.543 |
| 1024 | SignalEffects | 120,896.0 ns | 336,168 B | 1.000 | 1.000 |
| 1024 | SignalBindings | 83,838.6 ns | 180,520 B | 0.693 | 0.537 |
| 1024 | MemoEffects | 120,891.7 ns | 336,168 B | 1.000 | 1.000 |
| 1024 | MemoBindings | 93,561.4 ns | 180,520 B | 0.774 | 0.537 |

At 64 and 1024 both bindings meet the mean bound (at most 0.774x) and the allocation bound (at most 0.543x).

## Update (one write observed by `Nodes` consumers, flushed)

Ratios are binding / effect. No kind allocates per write.

| Nodes | Kind | Mean | Allocated | Mean ratio |
| --- | --- | ---: | ---: | ---: |
| 1 | signal-effect | 27.45 ns | 0 B | 1.000 |
| 1 | signal-binding | 21.73 ns | 0 B | 0.792 |
| 1 | memo-effect | 56.29 ns | 0 B | 1.000 |
| 1 | memo-binding | 48.05 ns | 0 B | 0.854 |
| 64 | signal-effect | 1,331.63 ns | 0 B | 1.000 |
| 64 | signal-binding | 913.76 ns | 0 B | 0.686 |
| 64 | memo-effect | 1,488.38 ns | 0 B | 1.000 |
| 64 | memo-binding | 1,123.43 ns | 0 B | 0.755 |
| 1024 | signal-effect | 21,198.52 ns | 0 B | 1.000 |
| 1024 | signal-binding | 14,720.96 ns | 0 B | 0.694 |
| 1024 | memo-effect | 23,302.72 ns | 0 B | 1.000 |
| 1024 | memo-binding | 17,798.70 ns | 0 B | 0.764 |

Every binding kind is at most 0.854x its effect kind, against a bound of 1.05x.
