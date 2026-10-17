---
title: Installation
order: 2
---

:::info
Preview — Ranvier is pre-release; its APIs may change.
:::

Choose the package for your language, then continue with [Getting started](getting-started.md).

## Supported targets

:::center
|**Target**|Supported|Recommended|
|---:|:---:|:---:|
| **`net10`** | Yes | Yes |
| `netstandard2.1` | Yes | No |
:::

Applications on .NET 8 and .NET 9 use the `netstandard2.1` build. That build omits
`TimedClock.ofTimeProvider` and carries no trimming or Native AOT annotations.

:::tip Prefer .NET 10
The [pending-throw measurements](../concepts/suspension.md#what-a-throw-costs) document runtime-specific
exception costs. They are measurements of that path, not a guarantee of faster application workloads.
:::

## From NuGet

Install `Ranvier` for F#, `Ranvier.CSharp` for C#, or `Ranvier.Elmish` for the MVU bridge.
Versions are previews, so pass `--prerelease`.

The guide describes the current source. Published previews can lag newly merged features.
As checked on 2026-10-16, the latest listed versions are
[`Ranvier` 0.1.0-preview.5](https://www.nuget.org/packages/Ranvier/0.1.0-preview.5),
[`Ranvier.CSharp` 0.1.0-preview.5](https://www.nuget.org/packages/Ranvier.CSharp/0.1.0-preview.5),
[`Ranvier.Elmish` 0.1.0-preview.4](https://www.nuget.org/packages/Ranvier.Elmish/0.1.0-preview.4), and
[`Ranvier.Query` 0.1.0-preview.1](https://www.nuget.org/packages/Ranvier.Query/0.1.0-preview.1).

::::::tabs
:::::tab Core
::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier --prerelease
```

:::

:::tab Package reference

```xml
<PackageReference Include="Ranvier" Version="0.1.0-preview.5" />
```

:::

::::
:::::
:::::tab C#


::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier.CSharp --prerelease
```
:::
:::tab Package reference

```xml
<PackageReference Include="Ranvier.CSharp" Version="0.1.0-preview.5" />
```
:::
::::
C# projects use `Ranvier.CSharp`; see [C#](csharp.md).

:::::
:::::tab Elmish
::::tabs
:::tab NuGet

```bash
dotnet add package Ranvier.Elmish --prerelease
```

:::

:::tab Package reference

```xml
<PackageReference Include="Ranvier.Elmish" Version="0.1.0-preview.4" />
```

:::
::::

The MVU bridge, `Mvu`, ships as its own package, `Ranvier.Elmish`, which depends on `Ranvier`; see
[Migrating from Elmish](elmish.md).

:::::
::::::

For typed reactive queries and mutation reconciliation, install `Ranvier.Query` with
`dotnet add package Ranvier.Query --prerelease`; see [Queries](queries.md).

## Fable

Package builds include Fable sources and a standalone source project for `Ranvier`,
`Ranvier.Elmish`, `Ranvier.CSharp` and `Ranvier.Query`, including their `.Traced` variants.
F# projects compiled with Fable use normal NuGet package references. CI compiles and runs
consumers restored from locally packed packages, so this path does not depend on repository
build imports. See [Fable](../fable/index.md) for package details, available APIs and JavaScript
timing and equality differences. No browser renderer is included.

The published versions listed above do not contain those standalone Fable project files. Until a
release includes the packaging fix, build packages from the current checkout and restore them from a
local feed. The [Fable guide](../fable/index.md#package-sources-and-ci) explains this distinction.

## Native AOT and trimming

The untraced `Ranvier`, `Ranvier.CSharp` and `Ranvier.Elmish` packages support trimming and Native AOT.
CI publishes smoke applications on .NET 10 for `linux-x64` and treats trim/AOT warnings as errors.
That validates this configuration; test your own deployment target and dependencies. Publish with
`<PublishAot>true</PublishAot>`.

:::warning Traced builds and AOT
Traced packages retain reflection-based `ToString` implementations in their trace types. Their
trimming and AOT compatibility remains an outstanding issue.
:::

## Traced packages

Each package has a `.Traced` variant for diagnostics. See [Tracing](tracing.md#from-nuget) for
switching between traced and untraced builds.

## Next

Continue with [Getting started](getting-started.md).
