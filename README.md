<p align="center">
  <img alt="ranvier: fine-grained reactive computation for .NET" src="brand/og/ranvier-og.png">
</p>

<p align="center">
  <a href="https://ranvier-org.github.io/Ranvier/">Documentation</a>
  &middot;
  <a href="https://ranvier-org.github.io/Ranvier/benchmarks/">Benchmarks</a>
  &middot;
  <a href="LICENSE">MIT License</a>
</p>

> The nodes of Ranvier are the short gaps in the myelin sheath along a nerve fibre, named after the
> French anatomist Louis-Antoine Ranvier. A signal jumps from node to node and is regenerated at each
> one, instead of flowing along every point of the fibre.

> **Preview.** Ranvier is pre-release. Its APIs may change before the first release. Install the preview from NuGet as
> [`Ranvier`](https://www.nuget.org/packages/Ranvier), or [`Ranvier.CSharp`](https://www.nuget.org/packages/Ranvier.CSharp) from C#.
> The Elmish-style MVU bridge ships separately as [`Ranvier.Elmish`](https://www.nuget.org/packages/Ranvier.Elmish).
> [`Ranvier.Query`](https://www.nuget.org/packages/Ranvier.Query) adds shared remote queries and mutation reconciliation.
> The guide tracks current source; published preview versions can lag merged features. See [Installation](docs/content/guide/installation.md) for checked package versions and Fable packaging status.

## Overview

Ranvier connects signals, memos and effects in a reactive graph. Signals hold writable state;
memos derive values and recompute on demand after their inputs change; effects run side effects
after changes. Async sources carry an explicit Pending state, and boundaries handle pending or failed reads.

From F#, through `Ranvier`:

```fsharp
open Ranvier

use graph = new Graph ()

graph.Run (fun () ->
    let count = createSignal 1
    let doubled = createMemo (fun _ -> count.Value * 2)
    createEffect (fun () -> printfn "doubled = %d" doubled.Value)
    count.Value <- 5)
```

From C#, through `Ranvier.CSharp`:

```csharp
using Ranvier;
using Ranvier.CSharp;
using static Ranvier.CSharp.Reactive;

using var graph = new Graph();

graph.Run(() =>
{
    var count = Signal(1);
    var doubled = Memo(() => count.Value * 2);
    Effect(() => Console.WriteLine($"doubled = {doubled.Value}"));
    count.Value = 5;
});
```

Read the [full documentation](https://ranvier-org.github.io/Ranvier/).

## Features

- [Owners and cleanup](docs/content/guide/roots.md) release child computations and cleanups together in reverse acquisition order.
- [Fixed-dependency bindings](docs/content/guide/bindings.md) copy one signal or memo into a target without dynamic dependency tracking.
- [Stream memos](docs/content/guide/streams.md) fold the items of a running stream into one node and restart the stream when its inputs change.
- [Debounce and throttle](docs/content/guide/timing.md) control when captured values reach downstream readers and async requests.
- [Collections](docs/content/guide/collections.fsx) provide editable keyed sources, per-row projections, change readers, views and aggregates.
- [Queries and mutations](docs/content/guide/queries.md) share acquired remote data, retain accepted data during refresh, and reconcile loaded queries in one batch.
- [Views](https://ranvier-org.github.io/Ranvier.View/) use typed F# components for browser rendering, server HTML and hydration. They are built in [ranvier-org/Ranvier.View](https://github.com/ranvier-org/Ranvier.View), with [typed CSS](https://github.com/ranvier-org/Ranvier.Css) and [persistence](https://github.com/ranvier-org/Ranvier.Persistence) in their own repositories.
- [Fable](docs/content/fable/index.md) compiles the same model to JavaScript. Package builds include standalone Fable source projects, and CI checks package consumers. The guide documents target differences.

## Examples

[Ranvier.Examples](https://github.com/shayanhabibi/Ranvier.Examples) holds complete F# and C# applications
built on Ranvier, each as a console scenario and as a tab of an Avalonia desktop workbench:

- **Support workbench** (F#): a local HTTP ticket service with drafts, version conflicts, reconnection and saves that outlive the editor.
- **File processing** (C#): SHA-256 hashing of real files with bounded concurrency, keyed lists, cancellation, retry and throttled progress.
- **Data quality** (F#): CSV import, repair of malformed measurements, unit conversion, drafts across reloads and export.
- **Elmish expense review** (F#, `Ranvier.Elmish`): an immutable model with commands, selectors, validation and undo/redo.

The repository references Ranvier through a git submodule, so clone it with `git clone --recurse-submodules`.
See [Examples](docs/content/guide/examples.md) in the guide for each workflow and how to run it.

## Origin

Ranvier began as experiments in a repository called Partas.Signals.

## Build CLI

Every repository task runs through `build.fsx`, a [Partas.Build](https://github.com/shayanhabibi/partas.build)
script, so the tasks are typed, composable and discoverable:

```shell
dotnet fsi build.fsx -- --help
```

| Command | What it does |
|---------|--------------|
| `build` | Restores and builds the source projects |
| `test` | Cleans, then runs the F# (Expecto) and C# (xUnit) suites (`--skip-tests` to skip them) |
| `test-fable` | Compiles and runs traced and untraced Fable suites, then writes the compatibility report |
| `format` | Formats every source file with Fantomas (`--dry-format` checks instead) |
| `compile` | Compiles the Fable projects to JavaScript (`--watch` to stay resident) |
| `publish` | Builds, tests, packs and pushes to NuGet (`--api-key`, or the `NUGET_API_KEY` env var; `--dry-publish` lists the packages instead of pushing) |
| `bump` | Bumps the version of a project |
| `docs` | Builds the Nacara site from `docs/docs.fsproj` (`--watch` to serve it) |

For automated releases, see [Publishing packages](docs/releasing.md).

Global flags: `--quick` skips restores and cleaning,
`--format` formats before building, `--dry-format` checks formatting before building,
`-c` picks the configuration (default `Release`).

## Git hooks

Enable the tracked pre-commit hook after cloning (requires Git, RTK, PowerShell 7 and the .NET SDK):

```powershell
rtk proxy pwsh -NoProfile -File scripts/setup-hooks.ps1
```

It checks staged whitespace, F# formatting with the pinned Fantomas version, and `FOR-REVIEW`
comments with the bundled comment-hygiene scanner. Whole staged F# files must pass; existing
review markers in a changed file also block its commit. String literals containing markers are
allowed. Checks use a temporary copy of index contents and never modify or stage your files.

Run it manually with `rtk proxy pwsh -NoProfile -File scripts/check-staged.ps1`.
Full test suites remain explicit verification commands and CI checks. `git commit --no-verify`
bypasses the local hook when needed.

Verify the hook itself with `rtk proxy pwsh -NoProfile -File scripts/test-hooks.ps1`.
Its commit-level tests run in a disposable repository.

See [CONTRIBUTING.md](CONTRIBUTING.md) for commit conventions and the CI checks, including performance.

## Layout

```
build.fsx                      the build CLI
src/Ranvier/                   the library
src/Ranvier.CSharp/            the C# façade
src/Ranvier.Elmish/            the MVU bridge (Mvu)
src/Ranvier.Query/             shared remote queries and mutation reconciliation
src/Ranvier.Wire/              the JSON wire codec (vendors Thoth.Json)
tests/Ranvier.Tests/           the Expecto suite
tests/Ranvier.Wire.Tests/      the wire suite, also run under Fable
tests/Ranvier.CSharp.Tests/    the xUnit suite, written in C#
docs/docs.fsproj               the Nacara site (net10.0)
docs/content/                  the pages
fable/                         Fable tests and counter scenarios
bench/                         BenchmarkDotNet suites
```

### Adding a project

The build CLI addresses the repository through `Partas.TypeProvider.BuildHelper`,
so projects are discovered at compile time: anything under `src/` is a source
project, anything under `tests/` is a test project. Add the project to the
solution and it is picked up by `build`, `test`
and `publish`
on the next run.

### Adding a step

A step is a stage of a command. A stage that needs a flag binds it in an
`input { }` block, which is also what puts the flag into `--help`:

```fsharp
let myStep = input {
    let! quick = Options.quick
    return stage "my step" {
        when' (not quick)
        run "dotnet ..."
    }
}
```

Add it to any `command "..." { }` block. Because the condition lives in the
stage, the command carries no flags of its own, and adding the stage to a
second command registers `--quick` there too.

## License

[MIT](LICENSE). Copyright (c) 2026 Shayan Habibi.
