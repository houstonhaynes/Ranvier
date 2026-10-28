---
title: Ranvier in Nacara
description: Render components, draw signal maps and run editable F# examples in documentation.
---

The `Nacara.Plugin.Ranvier` project powers the live examples in these docs. It scans Markdown,
compiles Ranvier.View components, renders their initial HTML and bundles hydration code.

## Choose a fence

- **`fsharp ranvier`** renders a `Component<unit>`. Use it for a working UI example.
- **`fsharp map`** turns a scenario and its controls into a [signal map](signal-maps.md).
- **`fsharp live`**, supplied by Nacara's `LiveExample` plugin, opens an editor that compiles and
  evaluates code in the browser. Try the [editable counter](../view/getting-started.md#try-a-component-in-the-live-editor).

For a component already exposed by your example project:

````markdown
```fsharp ranvier
MyExamples.counter
```
````

Use `setup` for declarations shared by later component fences, `render=name` for a component
declared in the current fence, and `show=output` to display only its result.
The [plugin README](https://github.com/ranvier-org/Ranvier/tree/master/docs/plugin) covers registration
and the complete fence options.

## What runs where

The site build renders static HTML first. In the browser, hydration adopts that HTML and connects
event handlers and bindings. The live editor instead compiles the edited code when you press
**Run** and mounts it into a fresh output frame.

Standard Ranvier.View components need no Fable compiler plugin. The live editor excludes packages
that require one, including `Ranvier.View.Arms` and its `Arms.With` / `Arms.Match` forms.
Build those examples in a local project with their compiler plugin enabled.
