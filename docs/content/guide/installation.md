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

For runtime-specific suspension costs, see the
[pending-throw measurements](../concepts/suspension.md#what-a-throw-costs).

## From NuGet

Install `Ranvier` for F#, `Ranvier.CSharp` for C#, or `Ranvier.Elmish` for the MVU bridge.
Versions are previews, so pass `--prerelease`.

The guide describes the current source. The current versions are
[`Ranvier` 0.1.0-preview.7](https://www.nuget.org/packages/Ranvier/0.1.0-preview.7),
[`Ranvier.CSharp` 0.1.0-preview.7](https://www.nuget.org/packages/Ranvier.CSharp/0.1.0-preview.7),
[`Ranvier.Elmish` 0.1.0-preview.6](https://www.nuget.org/packages/Ranvier.Elmish/0.1.0-preview.6), and
[`Ranvier.Query` 0.1.0-preview.3](https://www.nuget.org/packages/Ranvier.Query/0.1.0-preview.3).

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
<PackageReference Include="Ranvier" Version="0.1.0-preview.7" />
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
<PackageReference Include="Ranvier.CSharp" Version="0.1.0-preview.7" />
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
<PackageReference Include="Ranvier.Elmish" Version="0.1.0-preview.6" />
```

:::
::::

The MVU bridge, `Mvu`, ships as its own package, `Ranvier.Elmish`, which depends on `Ranvier`; see
[Migrating from Elmish](elmish.md).

:::::
::::::

For typed reactive queries and mutation reconciliation, install `Ranvier.Query` with
`dotnet add package Ranvier.Query --prerelease`; see [Queries](queries.md).

## Views

The four view packages are published on NuGet at `0.1.0-alpha.1`, with matching dependency versions.

```shell
dotnet add package Ranvier.View.Dom --version 0.1.0-alpha.1
```

| Package | Holds | Depends on |
| --- | --- | --- |
| `Ranvier.View` | Template components, view sugar and server HTML rendering, on .NET and under Fable | `Ranvier`, `Xantham.Fable.Core.TS` |
| `Ranvier.View.Dom` | Mounting, hydration and the document head in the browser, under Fable | `Ranvier.View` |
| `Ranvier.View.Anime` | Motion parts for templates over anime.js | `Ranvier.View`, `Fable.Ranvier.Anime` |
| `Fable.Ranvier.Anime` | Typed anime.js 4.5 bindings | `Ranvier`, `Xantham.Fable.Core.TS` |

A browser app references `Ranvier.View.Dom`; server or shared component code references
`Ranvier.View`. See [Views](../view/index.md) for setup.

:::warning Additional dependency feed
`Xantham.Fable.Core.TS` 0.2.1 comes from the Xantham Cloudsmith feed. Add it beside nuget.org
and map `Xantham.*` to Cloudsmith:


```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="xantham-cloudsmith" value="https://nuget.cloudsmith.io/shayanhabibi/xantham/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="xantham-cloudsmith">
      <package pattern="Xantham.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```
:::

## Fable

Package builds include Fable sources and a standalone source project for `Ranvier`,
`Ranvier.Elmish`, `Ranvier.CSharp`, `Ranvier.Query` and the four [view packages](#views), including
their `.Traced` variants.
F# projects compiled with Fable use normal NuGet package references. CI compiles and runs
consumers restored from locally packed packages, so this path does not depend on repository
build imports. See [Fable](../fable/index.md) for package details, available APIs and JavaScript
timing and equality differences. The browser renderer is `Ranvier.View.Dom`.

The versions listed above and the view packages include standalone Fable project files.
See [package sources and CI](../fable/index.md#package-sources-and-ci).

## Native AOT and trimming

The `Ranvier`, `Ranvier.CSharp`, `Ranvier.Elmish` and `Ranvier.Query` packages, including their
`.Traced` variants, support trimming and Native AOT. CI publishes traced and untraced smoke
applications on .NET 10 for `linux-x64` and treats trim/AOT warnings as errors.
That validates this configuration; test your own deployment target and dependencies. Publish with
`<PublishAot>true</PublishAot>`. The view packages carry no trimming or Native AOT annotations.

Trace types print their fields with explicit formatters. On .NET, payload previews use the
application value's `ToString` implementation, which must also support Native AOT. Arrays and
collections preview up to 64 elements and four levels of nesting; previews fit on one line of
at most 60 characters. An application's records and unions can provide their own `ToString`
overrides to keep useful diagnostic text after trimming.

## Traced packages

Runtime libraries have a `.Traced` variant for diagnostics; `Ranvier.Analyzers` does not. See [Tracing](tracing.md#from-nuget) for
switching between traced and untraced builds.
