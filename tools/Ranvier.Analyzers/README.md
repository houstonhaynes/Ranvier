# Ranvier.Analyzers

F# analyzers that report Ranvier traps at build time. Built against FSharp.Analyzers.SDK 0.41.0.

```xml
<PackageReference Include="Ranvier.Analyzers" Version="0.1.0-preview.1" PrivateAssets="all" />
<PropertyGroup>
  <RanvierAnalyze>true</RanvierAnalyze>
</PropertyGroup>
```

Install the CLI in the tool manifest: `dotnet tool install fsharp-analyzers --version 0.41.0`. `dotnet build` then
prints `Foo.fs(12,9): warning RAN001: ...` after each build that compiles. See the
[analyzer rules and fixes](https://shayanhabibi.github.io/Ranvier/analyzers/).

The Fable rules, RAN001 and RAN002, run in a project that a tool manifest's `fable` or `fickl`, a `Fable.*` package
reference, or `<RanvierFableAnalyzers>true</RanvierFableAnalyzers>` marks as compiled by Fable.
