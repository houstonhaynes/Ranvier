---
title: Analyzers
order: 1
---

:::info
`Ranvier.Analyzers` is available from this checkout and local package builds; it is not currently
published on nuget.org. After restoring a locally packed version, reference it and set `RanvierAnalyze`:

```xml
<PackageReference Include="Ranvier.Analyzers" Version="0.1.0-preview.1" PrivateAssets="all" />
<PropertyGroup>
  <RanvierAnalyze>true</RanvierAnalyze>
</PropertyGroup>
```

with the CLI in the tool manifest: `dotnet tool install fsharp-analyzers --version 0.41.0`.
:::

`Ranvier.Analyzers` reports mistakes at their source ranges during a build. Each rule below has a
triggering example and a corrected version.

| Code | Title | Severity | Scope |
|---|---|---|---|
| [RAN001](ran001.md) | Fable inlines a snapshot of a module mutable | Warning | Fable |
| [RAN002](ran002.md) | Generated union case tester in a Fable library | Warning | Fable |
| [RAN010](ran010.md) | Owned node created in a pure derivation | Error | Ranvier |
| [RAN011](ran011.md) | Async value created and read in the owner that replaces it | Warning | Ranvier |
| [RAN012](ran012.md) | Reactive read after the first await of an async memo | Warning | Ranvier |
| [RAN013](ran013.md) | Signal write or Pump on a thread-pool callback | Warning | Ranvier |
| [RAN015](ran015.md) | CancelPrevious with a body that ignores its token | Hint | Ranvier |
| [RAN020](ran020.md) | Reactive read at template build time | Warning | view |
| [RAN021](ran021.md) | Unstable list key | Warning / Info | view |
| [RAN022](ran022.md) | Definition created on every region read | Warning | view |
| [RAN023](ran023.md) | Invalid content parts on an element | Error | view |
| [RAN024](ran024.md) | Markup the server render refuses | Warning | view |
| [RAN025](ran025.md) | hydrateWith with queued commits | Error | view |

## Where the rules run

- **Ranvier** rules run in a project that references `Ranvier`.
- **view** rules run in a project that references `Ranvier.View` or `Ranvier.View.Dom`, or compiles
  their sources, such as the view projects of this repository.
- **Fable** rules run in a project that Fable compiles. Every Ranvier project references Fable.Core, so the rules
  read the project's own signals instead:
  - a tool manifest (`.config/dotnet-tools.json`, searched upward to the first root manifest) that declares `fable` or
    `fickl`;
  - a `Fable.*` package reference in the project file;
  - `<RanvierFableAnalyzers>true</RanvierFableAnalyzers>`, for a global, `--tool-path` or `dotnet dnx` install of
    Fable.

  A .NET-only project gets neither RAN001 nor RAN002. RAN002 also needs a declared `fable` before 5.22, or no
  declared compiler.

## Running the analyzers

In this repository, after `dotnet build Ranvier.slnx -c Release`:

```bash
dotnet fsi build.fsx -- analyze
```

The command builds the analyzers, treats findings as errors and writes reports to `artifacts/analyzers`.
Set `<RanvierAnalyze>true</RanvierAnalyze>` to include findings in a project's `dotnet build` output.

## Suppression

Write the reason on the line before the SDK's comment:

```fsharp
// The snapshot is read before any write, so Fable's inlining leaves the value unchanged.
// fsharpanalyzer: ignore-line-next RAN001
let setups = Loops.childSetups
```

`ignore-line`, `ignore-region-start`/`ignore-region-end` and `ignore-file` take the same codes.

## Columns

The CLI's console output, and the `dotnet build` warnings made from it, count columns from 0: `B.fs(4,8)` is the
identifier at the ninth character of line 4. The SARIF report, and the CI annotations made from it, count from 1.
