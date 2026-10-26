# Ranvier examples for Nacara

`Nacara.Plugin.Ranvier` compiles Ranvier.View components and interactive signal maps in Markdown.
Examples render to HTML during the site build and hydrate in the browser. The scanner and renderer
have no Partas.Solid dependency.

Install Fickl in the site's tool manifest, then reference this project from the Nacara site:

```shell
dotnet new tool-manifest
dotnet tool install fickl --version 0.6.9
```

Register after HTML minification so it cannot remove the inserted hydration markers:

```fsharp
open Nacara.Plugins

site
|> RanvierExamples.registerWith
    { Project = "examples/Examples.fsproj"
      Repository = ".."
      Packages = [] }
```

`Project` is relative to the Nacara project root. It must reference `Ranvier.View.Dom` and include
the types the examples use. `Repository` locates shared build configuration. `Packages` lists
runtime npm dependencies to bundle, such as `"animejs", "4.5.0"`.

## Component fences

An expression fence evaluates to `Component<unit>`:

````markdown
```fsharp ranvier
MyExamples.counter
```
````

Use `setup` for declarations shared by later fences, or `render=counter` to render a component
declared in the fence. `show=both` is the default; `show=output`, `show=inline` and `show=code`
control the displayed code and preview. `id=counter` gives a stable host id within the page.
Code-only cells are typechecked with the page when it also contains a rendered example.

Both backtick and tilde fences work. Ordinary code fences stay ordinary code, including an outer
four-backtick fence containing a three-backtick example. Duplicate ids, invalid options and missing
closing fences produce source-line diagnostics.

## Signal maps

`fsharp map` fences use the documentation map runtime. The referenced project must provide
`Ranvier.Docs.Maps`; this repository uses `docs/maps/Ranvier.Docs.Maps.fsproj`.
See [Signal maps](../content/guide/signal-maps.md) for controls, replay, trace names and layout.

## Browser editing

The site combines this plugin with Nacara's `LiveExample` plugin, using the same project as an
editor preset. A `fsharp live` fence compiles F# in the browser. It needs an explicit mount, as in
the [live counter](../content/view/getting-started.md#try-a-component-in-the-live-editor).

The live editor excludes packages requiring compiler plugins, including `Ranvier.View.Arms`.
Standard Ranvier.View components and DOM bindings work without those packages.

When a preset references Xantham's DOM bindings, provide their checker metadata after registering
`LiveExample`. Use the site's restored assets file so custom NuGet package folders work too:

Declare both packages in the site project before restoring it; menu checks initialise the site
before the preset project is built. A package present only in a developer's cache is insufficient.

```xml
<PackageReference Include="Xantham.Fable.Core.TS" Version="0.2.1" />
<PackageReference Include="Xantham.Fable.Core" Version="0.2.0" />
```

```fsharp
let assets = Path.Combine (__SOURCE_DIRECTORY__, "obj", "project.assets.json")

site
|> LiveExample.registerWith configureLiveExamples
|> LiveExampleMetadata.register
    [ LiveExampleMetadata.nuget assets "Xantham.Fable.Core.TS" "0.2.1" "net8.0" "Xantham.Fable.Core.TS"
      LiveExampleMetadata.nuget assets "Xantham.Fable.Core" "0.2.0" "net8.0" "Xantham.Fable.Core" ]
```

The hook copies these exact restored DLLs into LiveExample's current reference directory as
`.dll.txt` files and adds their names to the browser checker. It fails if metadata is missing or
the upstream worker's reference-list format changes. Ordinary component and map fences need no hook.

## Checks

```shell
dotnet run --project docs/maps/tests/Ranvier.Docs.Maps.Tests.fsproj
dotnet build docs/plugin/Nacara.Plugin.Ranvier.fsproj
dotnet fsi docs/plugin/tests/MetadataTests.fsx
dotnet fsi build.fsx -- docs
```

The first command checks scanning, generation and map behavior. The site build compiles scenarios,
renders examples, bundles client entries and validates internal links. Browser tests under
`docs/maps/tests/browser` check hydration and editable component interaction.
