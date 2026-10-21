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

The guide describes the current source. The current versions are
[`Ranvier` 0.1.0-preview.6](https://www.nuget.org/packages/Ranvier/0.1.0-preview.6),
[`Ranvier.CSharp` 0.1.0-preview.6](https://www.nuget.org/packages/Ranvier.CSharp/0.1.0-preview.6),
[`Ranvier.Elmish` 0.1.0-preview.5](https://www.nuget.org/packages/Ranvier.Elmish/0.1.0-preview.5), and
[`Ranvier.Query` 0.1.0-preview.2](https://www.nuget.org/packages/Ranvier.Query/0.1.0-preview.2).

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
<PackageReference Include="Ranvier" Version="0.1.0-preview.6" />
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
<PackageReference Include="Ranvier.CSharp" Version="0.1.0-preview.6" />
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
<PackageReference Include="Ranvier.Elmish" Version="0.1.0-preview.5" />
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

The view layer ships as four packages that share one version, `0.1.0-alpha.1`. Each depends on the
others it needs at exactly that version.

| Package | Holds | Depends on |
| --- | --- | --- |
| `Ranvier.View` | Template components, view sugar and server HTML rendering, on .NET and under Fable | `Ranvier`, `Xantham.Fable.Core.TS` |
| `Ranvier.View.Dom` | Mounting, hydration and the document head in the browser, under Fable | `Ranvier.View` |
| `Ranvier.View.Anime` | Motion parts for templates over anime.js | `Ranvier.View`, `Fable.Ranvier.Anime` |
| `Fable.Ranvier.Anime` | Typed anime.js 4.5 bindings | `Ranvier`, `Xantham.Fable.Core.TS` |

A browser app references `Ranvier.View.Dom`; a server, or code shared between server and client,
references `Ranvier.View`. See [Views](../view/index.md).

```xml
<PackageReference Include="Ranvier.View.Dom" Version="0.1.0-alpha.1" />
```

:::warning Temporary: the Xantham feed
The view packages are on nuget.org. Their dependency `Xantham.Fable.Core.TS` 0.2.1 is served by the Xantham
Cloudsmith feed and is not on nuget.org, so a consumer adds that feed. The requirement ends when
`Xantham.Fable.Core.TS` 0.2.1 or later is on nuget.org.

The application's `nuget.config` adds the Xantham feed beside nuget.org and maps `Xantham.*` to it:

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

The published versions listed above do not contain those standalone Fable project files. Until a
release includes the packaging fix, build packages from the current checkout and restore them from a
local feed. The [Fable guide](../fable/index.md#package-sources-and-ci) explains this distinction.

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

Each package has a `.Traced` variant for diagnostics. See [Tracing](tracing.md#from-nuget) for
switching between traced and untraced builds.

## Next

Continue with [Getting started](getting-started.md).
