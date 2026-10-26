---
title: Examples
description: Complete F# and C# applications built on Ranvier, in the Ranvier.Examples repository.
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

To try interactive views directly in your browser, open the [browser examples](../examples/index.md):
the playground, Motion demos and a server-rendered hydration snapshot.

[Ranvier.Examples](https://github.com/shayanhabibi/Ranvier.Examples) holds complete applications
that use Ranvier for real workflows: remote data, file processing, data repair and Elmish-style
review. Each workflow runs as a console scenario that checks its own outcome, and all four open
together as tabs of one Avalonia desktop workbench.

The [shop replay recipe](shop-replay.md) adds a console example: save an async production incident and
reproduce it in a traced development build, including the pending states and completion order.

## Workflows

| Workflow | Core | Console | Desktop |
| --- | --- | --- | --- |
| Support workbench | F# | `run-fsharp-console` | Avalonia tab |
| File processing | C# | `run-csharp-console` | Avalonia tab |
| Data quality | F# | `run-data-console` | Avalonia tab |
| Elmish expense review | F# with `Ranvier.Elmish` | `run-elmish-console` | Avalonia tab |

**Support workbench** searches a local HTTP ticket service, edits drafts, reconciles server
versions, recovers from disconnection, and keeps saves running after the editor closes. Its core
is [SupportDesk.fs](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/src/Ranvier.Examples.FSharp/SupportDesk.fs);
the [support walkthrough](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/docs/workflows/support.md)
covers requests, drafts, conflicts and mutation ownership. Related guides: [Async memos](async-memos.md),
[Drafts](drafts.md) and [Queries and mutations](queries.md).

**File processing** streams real files through SHA-256 with bounded concurrency, reactive keyed
lists, cancellation, retry and throttled progress. Its core is
[FileJobMonitor.cs](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/src/Ranvier.Examples.CSharp/Jobs/FileJobMonitor.cs);
the [jobs walkthrough](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/docs/workflows/jobs.md)
covers worker admission, graph dispatch and terminal events. Related guides: [C#](csharp.md),
[Threading and dispatch](threading.md) and [Debounce and throttle](timing.md).

**Data quality** imports a CSV, repairs malformed measurements, inspects calibration certificates,
converts units together, keeps drafts across reloads, and exports valid rows. Its core is
[DataQuality.fs](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/src/Ranvier.Examples.FSharp/DataQuality.fs);
the [data walkthrough](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/docs/workflows/data.md)
covers incremental projections, raw drafts and asynchronous selection. Related guides:
[Collections](collections.fsx), [Projections](projections.fsx) and [Forms](forms.md).

**Elmish expense review** reviews claims with an immutable model, messages, file commands and
selectors, including validation, undo/redo and revision-aware saves. Its core is
[ExpenseReview.fs](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/src/Ranvier.Examples.FSharp/ExpenseReview.fs);
the [Elmish walkthrough](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/docs/workflows/elmish.md)
covers immutable updates, commands, undo/redo and selectors. Related guide: [Migrating from Elmish](elmish.md).

The [feature map](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/docs/workflows/features.md)
lists which reactive features each workflow uses and which object owns each node.

### Desktop workbench

The F# and C# desktop launchers open the same four-tab workbench. The shared host is a C#
Avalonia Desktop application; each tab binds to the workflow core listed above. The desktop
apps require a graphical session and Avalonia's
[platform prerequisites](https://docs.avaloniaui.net/docs/supported-platforms).

### Introductory order examples

Matching F# and C# order-pricing examples cover signals, conditional dependencies, batching and
accepted submission snapshots, each as a console and an Avalonia desktop app. Read the
[introductory walkthrough](https://github.com/shayanhabibi/Ranvier.Examples/blob/main/docs/walkthrough.md),
then run them with the `intro` argument:

```shell
dotnet run --project src/Ranvier.Examples.FSharp.Console -- intro
dotnet run --project src/Ranvier.Examples.CSharp.Console -- intro
dotnet run --project src/Ranvier.Examples.FSharp.Desktop -- --intro
dotnet run --project src/Ranvier.Examples.CSharp.Desktop -- --intro
```

## Clone and build

The examples reference the Ranvier source projects through a git submodule at `external/Ranvier`,
pinned to a Ranvier commit, so they build against current APIs without a package release. Clone
with the submodule, then build and test with the .NET 10 SDK:

```shell
git clone --recurse-submodules https://github.com/shayanhabibi/Ranvier.Examples.git
cd Ranvier.Examples
dotnet fsi build.fsx -- build
dotnet fsi build.fsx -- test
```

For a clone made without `--recurse-submodules`, fetch the submodule from the repository
directory:

```shell
git submodule update --init --recursive
```

## Run

```shell
# Desktop workbench with all four workflows
dotnet fsi build.fsx -- run-fsharp-desktop
dotnet fsi build.fsx -- run-csharp-desktop

# Console scenarios
dotnet fsi build.fsx -- run-fsharp-console
dotnet fsi build.fsx -- run-csharp-console
dotnet fsi build.fsx -- run-data-console
dotnet fsi build.fsx -- run-elmish-console
```

Console scenarios fail on unexpected behaviour. The repository README lists where each scenario
writes its files and how to move the submodule to a newer Ranvier revision.
