---
title: Hydration
---

Hydration mounts a component over the HTML that `Server.render` wrote for it. The client runs every
setup again and keeps the server's nodes: each binding writes its first value only where it differs from the
server's node. Elements, text nodes and their identity stay as the server wrote
them; the client adds listeners, hooks and browser-only properties.

## Hydrating a component

```fsharp
open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.View
open Ranvier.View.Dom

[<Global "document">]
let private document: Document = jsNative

let start () : unit -> unit =
    let props =
        match Hydration.readState stateId |> Option.map counterProps.Decode with
        | Some (Ok props) -> props
        | Some (Error error) -> failwithf "Unreadable props: %s" error.Message
        | None -> { Start = 0; Label = "Clicks" }

    let graph = new Graph ()
    let host = document.getElementById "app" |> Option.get

    let report (errors: exn list) =
        for error in errors do
            JS.console.warn ("Hydration", error)

    let mounted =
        Hydration.hydrate graph None report Hydration.HydrationOptions.defaults host counter props

    fun () ->
        mounted.Dispose ()
        graph.Dispose ()
```

`Hydration.hydrate graph parentOwner report options host definition props` takes the arguments of
[`Mounting.mount`](../view/lifetimes.md), with `options`, the definition and its props in place of
`before` and `build`. It returns a `Mounted` that behaves as a mount with `SynchronousCommits`.

- **Hydrate with the server's props.** The client's first values must equal the server's for the server
  nodes to be kept. [State transfer](state-transfer.md) carries the props from the server render into
  the page; `stateId` and `counterProps` above are defined there.
- **The root** is the first `<!--[r-->` child of `host` and its matching `<!--]-->`. Nodes outside that
  pair stay in place and outside the mount.
- **Disposal** removes the hydrated nodes and the root markers.
- `Hydration.hydrateWith mode ...` takes a commit mode first; it supports `SynchronousCommits` only and
  raises `NotSupportedException` for `QueuedCommits`.

## Options

| Field | Default | Meaning |
| --- | --- | --- |
| `IdPrefix` | `"r"` | The root prefix of `Setup.CreateId`'s ids. It must equal the server's `Server.Options.IdPrefix`. |
| `Strict` | `false` | Whether a structural mismatch raises from `hydrate` instead of re-rendering. |

## What the client adopts

| Server content | On the client |
| --- | --- |
| Elements and static text | Adopted. |
| Text, attribute and property bindings | Adopted; a differing first value is written. |
| A list region | Its rows are adopted in order. |
| An arm region | Adopted when the server's arm hash equals the hash of the arm the client selects. |
| A [boundary](#boundaries) | The view its start marker records. |
| A view hole (`t.view`) | Mounted fresh between the server's region markers. |
| [Document head](../view/document-head.md) entries | The head elements marked with each entry's key. A title is written only when it differs; marked elements that no entry adopted are removed once the mount is built. |

Ids continue: `Setup.CreateId` returns the server's ids during hydration, and rows created afterwards
continue their region's counter.

## Mismatches

A **structural mismatch** is a server node that differs from the node the template expects: another
tag, a missing element, a different arm hash or a different number of rows.

- **Inside a list, an arm or a boundary's view**, the innermost such region re-renders: its server nodes
  are removed and it mounts fresh. The mismatch goes to `report` once, and the rest of the hydration
  continues.
- **Outside every region**, the head is set back as the server wrote it, the mismatch goes to `report`,
  and the root mounts fresh between its markers. Without a root marker the root mounts fresh at the end
  of `host`.
- **Under `Strict = true`**, the first mismatch raises `HydrationMismatchException` and leaves the host
  and the head as the server wrote them:

```text
Hydration mismatch at 0/1: expected <button>, found span
```

`Path` is `root` for a missing or unclosed root marker, `#k` for an instance's k-th root, and otherwise
the dynamic node's child-index path. Use `Strict` in tests to find mismatches; use the default in
production, where a mismatch costs a re-render of the affected region.

A text or attribute value that differs is a value difference, written by the binding, and is not a
mismatch.

## Boundaries

A boundary hydrates the view the server recorded:

- **Ready content (`[c`)** hydrates in place. When one of its reads is still pending at the end of the
  build, its bound nodes are held detached and a copy of the server's nodes stays on screen as the
  pending view; the reveal replaces the copy with the bound nodes. An arm or list whose source is
  pending keeps the server's rows in place until its source settles, then mounts its rows fresh.
- **A pending view (`[p`)** hydrates while the content mounts detached. When the content is ready, it
  replaces the server's nodes at once.
- **An error view (`[e`)** is removed, and the boundary mounts fresh.

## Failures

A failure of a region's fresh mount is a failure of the hydration attempt: `hydrate` disposes what it
created and raises, with the host and the head as the server wrote them. A failure of a boundary view's
fresh mount goes to the boundary, as in a mount. After `hydrate` returns, failures follow
[the mount's rules](../view/lifetimes.md#failures).
