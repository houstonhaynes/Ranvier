---
title: API reference
---

The view and server modules are outside the site's generated API reference, which
covers the packaged libraries. Their documentation comments are in the source; this index lists each
module and the page that covers it.

## `experiments/Ranvier.View.Portable`

| Module | Contents | Page |
| --- | --- | --- |
| [`Ranvier.View.Foundation.Template`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Template.fs) | `Component.define`, `render`, `renderWith`; `TemplateBuilder`, `AttributeBuilder`, `PropertyBuilder`, `EventBuilder`, `HookBuilder`, `UnsafeBuilder`, `DocumentBuilder`; `TemplateView`, `Part`, `Source`, `Arm`, `UnsafeHtml`, `PropertyHtml`, `primitiveSame` | [Templates](templates.md), [Components](components.md) |
| [`Ranvier.View.Foundation.HtmlTemplate`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Generated/HtmlTemplate.fs) | Generated: one `t.` member per tag, and the members of `Attrs`, `Props` and `Events`. Auto-opened with `Ranvier.View.Foundation`. | [Templates](templates.md) |
| [`Ranvier.View.Foundation.Sugar`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/TemplateSugar.fs) | `Builders`: `Element`, `VoidElement` (whose `View` is the element as a view), `ComponentTag`, `Attrs`, `Props`, `Events`, `Hooks`, `from`, `text`, `fragment`, the control flow, `render`, `renderOf`, and `Component.template`, `ofMembers`, `ofClass`. `Elements` ([generated](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Generated/HtmlTemplateSugar.fs)): one class per tag. | [Sugar](sugar.md) |
| [`Ranvier.View.Foundation.Representation`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Representation.fs) | `Setup`, `Tag`, `BrowserPolicy`, `BoundaryFailure`, `BoundaryStatus`, `NodeRef`, `Backend`, `Program`, `Html.escape`, `Html.render`, `Operations` | [Components](components.md), [Other authoring layers](authored.md) |
| [`Ranvier.View.Foundation.Server`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Server.fs) | `Options`, `render`, `renderTo`, `renderPage`, `Page`, `DocumentShell`, `ModuleScript`, `renderDocument`, `renderDocumentTo`, `stateScript`, `ServerPendingException` | [Rendering](../server/rendering.md) |
| [`Lifetime.fs`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Lifetime.fs), [`Scope.fs`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Scope.fs), [`Identity.fs`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/Identity.fs) | `ScopePhase`, `MountLifetime`; the scopes that route pending reads and failures; positional ids | [Mounts and lifetimes](lifetimes.md) |

## `experiments/Ranvier.View.Foundation`

| Module | Contents | Page |
| --- | --- | --- |
| [`Mounting`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/Mounting.fs) | `mount`, `mountWith`, `Mounted`, `BuildContext`, `NativeOwnership` | [Mounts and lifetimes](lifetimes.md) |
| [`Hydration`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/Hydration.fs) | `hydrate`, `hydrateWith`, `HydrationOptions`, `readState` | [Hydration](../server/hydration.md) |
| [`Scheduling`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/Scheduling.fs), [`Microtask`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/Microtask.fs) | `CommitMode`, `CommitFailure`, `Queue`; `Microtask.mode` | [Mounts and lifetimes](lifetimes.md#commit-modes) |
| [`TemplateDom`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/TemplateDom.fs) | The DOM compiler of templates; `HydrationMismatchException` | [Hydration](../server/hydration.md#mismatches) |
| [`Authoring`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/Authoring.fs), [`Components`](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Foundation/Components.fs), `Generated.Html` | The authored layer | [Other authoring layers](authored.md) |

## Other projects

| Project | Page |
| --- | --- |
| [`src/Fable.Ranvier`](https://github.com/shayanhabibi/Ranvier/tree/master/src/Fable.Ranvier) | [Other authoring layers](authored.md) |
| [`src/Fable.Ranvier.Anime`](https://github.com/shayanhabibi/Ranvier/tree/master/src/Fable.Ranvier.Anime) | [Animation](motion.md), [Owning and driving animations](motion-reactive.md), [Testing and costs](motion-testing.md) |
| [`experiments/Ranvier.View.Anime`](https://github.com/shayanhabibi/Ranvier/tree/master/experiments/Ranvier.View.Anime) | [Motion parts](motion-parts.md) |
| [`examples/Ranvier.Motion`](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Ranvier.Motion) | [Motion parts](motion-parts.md#examples) |
| [`src/Ranvier.Wire`](https://github.com/shayanhabibi/Ranvier/tree/master/src/Ranvier.Wire) | [State transfer](../server/state-transfer.md) |
| [`examples/Fable.Ranvier.Playground`](https://github.com/shayanhabibi/Ranvier/tree/master/examples/Fable.Ranvier.Playground) | [Getting started](getting-started.md), [Testing views](testing.md) |
