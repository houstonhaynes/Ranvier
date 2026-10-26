---
title: Browser examples
description: Try Ranvier views, animation and server-rendered hydration in your browser.
---

Open these examples directly from the documentation. Each is built from the F# source in this
repository and published with the docs on GitHub Pages.

| Example | Try it | What it demonstrates |
| --- | --- | --- |
| Playground | [Open playground](/Ranvier/examples/playground/index.html) | Reactive views, collections, boundaries, events and component lifetimes. |
| Motion | [Open motion examples](/Ranvier/examples/motion/index.html) | Kanban, dashboard, story, physics and route transitions using anime.js. |
| Server hydration | [Open hydration snapshot](/Ranvier/examples/hydration/index.html) | F#-rendered HTML and transferred state becoming interactive in the browser. |

Start with the [browser editor](../view/getting-started.md) to edit a standard component and see its
result. It excludes compiler-plugin extensions such as `Arms.Match`; build those locally.

## Server hydration preview

The preview starts with HTML and state rendered by .NET during the documentation build. The
browser hydrates that output and handles counter interactions. GitHub Pages serves the saved
snapshot; it does not run the F# backend or produce new request-specific data.

<iframe src="/Ranvier/examples/hydration/index.html" title="Ranvier server hydration static snapshot" loading="lazy" style="width:100%;height:480px;border:1px solid currentColor;border-radius:8px"></iframe>

[Open the snapshot in its own tab](/Ranvier/examples/hydration/index.html) for more room.
Click the counter, then reload: it returns to the value saved during the docs build.

To run the real backend locally, follow the
[hydration example README](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Hydration).
It renders request-specific initial values on the server, transfers them to the browser and
hydrates the shared Ranvier component. The README includes the build and start commands and
URLs for changing those initial values.

The [F# HTTP backends guide](../server/backends.md) shows equivalent Giraffe, Oxpecker and Suave
handlers using the same shared component and browser bundle.

The [page declarations guide](../server/pages.md) explains the shared page definition and
generated browser entry. [State transfer](../server/state-transfer.md) covers the codec and
embedded data; [hydration](../server/hydration.md) describes how the browser reuses server nodes.

## Explore the source

- [Playground](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Fable.Ranvier.Playground):
  ordinary panels are rendered on .NET and hydrated; the scheduling panel demonstrates explicit mounting.
- [Motion](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Motion):
  shared page declarations generate the documents and browser entries. Read the
  [motion walkthrough](../view/motion-parts.md#examples) for each animation technique.
- [Server hydration](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Hydration):
  a shared component and page used by both the local HTTP server and the static exporter.

For console and Avalonia applications, see [application examples](../guide/examples.md).
