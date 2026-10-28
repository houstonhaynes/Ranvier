---
title: API reference
---

The site's generated API reference excludes view and server modules. Use the source links below
for their documentation comments, and the linked guides for examples.

## `Ranvier.View`

| Module | Contents | Page |
| --- | --- | --- |
| [`Ranvier.View.Template`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Template.fs) | Auto-opened with `Ranvier.View`. `Component.define`, `render`, `renderWith`; `TemplateBuilder`, `AttributeBuilder`, `PropertyBuilder`, `EventBuilder`, `HookBuilder`, `UnsafeBuilder`, `DocumentBuilder`; `TemplateView`, `Part`, `Source`, `Arm`, `UnsafeHtml`, `PropertyHtml`, `primitiveSame` | [Templates](templates.md), [Components](components.md) |
| [`Ranvier.View.HtmlTemplate`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Generated/HtmlTemplate.fs) | Generated: one `t.` member per tag, and the members of `Attrs`, `Props` and `Events`. Auto-opened with `Ranvier.View`. | [Templates](templates.md) |
| [`Ranvier.View.Sugar`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/TemplateSugar.fs) | `Builders`: `Element`, `VoidElement` (whose `View` is the element as a view), `ComponentTag`, `Attrs`, `Props`, `Events`, `Hooks`, `from`, `text`, `fragment`, the control flow, `render`, `renderOf`, and `Component.template`, `ofMembers`, `ofClass`. `Elements` ([generated](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Generated/HtmlTemplateSugar.fs)): one class per tag. | [Sugar](sugar.md) |
| [`Ranvier.View.Representation`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Representation.fs) | Auto-opened with `Ranvier.View`. `Setup`, `Tag`, `BrowserPolicy`, `BoundaryFailure`, `BoundaryStatus`, `NodeRef`, `Backend`, `Program`, `Html.escape`, `Html.render`, `Operations` | [Components](components.md), [Other authoring layers](authored.md) |
| [`Ranvier.View.Server`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Server.fs) | `Options`, `render`, `renderTo`, `renderPage`, `Page`, `DocumentShell`, `ModuleScript`, `renderDocument`, `renderDocumentTo`, `stateScript`, `ServerPendingException` | [Rendering](../server/rendering.md) |
| [`Lifetime.fs`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Lifetime.fs), [`Scope.fs`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Scope.fs), [`Identity.fs`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View/Identity.fs) | `ScopePhase`, `MountLifetime`; the scopes that route pending reads and failures; positional ids | [Mounts and lifetimes](lifetimes.md) |

## `Ranvier.View.Dom`

| Module | Contents | Page |
| --- | --- | --- |
| [`Mounting`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/Mounting.fs) | `mount`, `mountWith`, `Mounted`, `BuildContext`, `NativeOwnership` | [Mounts and lifetimes](lifetimes.md) |
| [`Hydration`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/Hydration.fs) | `hydrate`, `hydrateWith`, `HydrationOptions`, `readState` | [Hydration](../server/hydration.md) |
| [`Scheduling`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/Scheduling.fs), [`Microtask`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/Microtask.fs) | `CommitMode`, `CommitFailure`, `Queue`; `Microtask.mode` | [Mounts and lifetimes](lifetimes.md#commit-modes) |
| [`TemplateDom`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/TemplateDom.fs) | The DOM compiler of templates; `HydrationMismatchException` | [Hydration](../server/hydration.md#mismatches) |
| [`Authoring`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/Authoring.fs), [`Components`](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.View.Dom/Components.fs), `Generated.Html` | The authored layer | [Other authoring layers](authored.md) |

## `experiments/Ranvier.View.Arms`

| Module | Contents | Page |
| --- | --- | --- |
| [`Ranvier.View.Arms`](https://github.com/ranvier-org/Ranvier/blob/master/experiments/Ranvier.View.Arms/Arms.fs) | `Arms.Match`, `Arms.Prepare`; on .NET `BuildTimeReadException` and `ArmsMatchException` (both inherit `InvalidOperationException`, which shared code catches on both targets) | [Conditional arms](arms.md) |
| [`Ranvier.View.Arms.FablePlugin`](https://github.com/ranvier-org/Ranvier/tree/master/experiments/Ranvier.View.Arms.FablePlugin) | `CaseArmsAttribute`: the Fable plugin that lowers `Arms.Match` calls, referenced through `Ranvier.View.Arms.targets` | [Conditional arms](arms.md) |

## Other projects

| Project | Page |
| --- | --- |
| [`src/Fable.Ranvier`](https://github.com/ranvier-org/Ranvier/tree/master/src/Fable.Ranvier) | [Other authoring layers](authored.md) |
| [`src/Fable.Ranvier.Anime`](https://github.com/ranvier-org/Ranvier/tree/master/src/Fable.Ranvier.Anime) | [Animation](motion.md), [Owning and driving animations](motion-reactive.md), [Testing and costs](motion-testing.md) |
| [`src/Ranvier.View.Anime`](https://github.com/ranvier-org/Ranvier/tree/master/src/Ranvier.View.Anime) | [Motion parts](motion-parts.md) |
| [`examples/Ranvier.Motion`](https://github.com/ranvier-org/Ranvier/tree/master/examples/Ranvier.Motion) | [Motion parts](motion-parts.md#examples) |
| [`src/Ranvier.Wire`](https://github.com/ranvier-org/Ranvier/tree/master/src/Ranvier.Wire) | [State transfer](../server/state-transfer.md) |
| [`examples/Fable.Ranvier.Playground`](https://github.com/ranvier-org/Ranvier/tree/master/examples/Fable.Ranvier.Playground) | [Getting started](getting-started.md), [Testing views](testing.md) |
