---
title: Boundaries
---

A boundary shows a **pending view** until its content has values, then reveals that content together.
A failure replaces the content with an **error view**. Pending reads come from an
[async source](../guide/async-sources.md) or [async memo](../guide/async-memos.md) throwing
`NotReadyException`; see [pending reads](../guide/pending.md).

Use the opens from [Templates and bindings](templates.md).

## A boundary

```fsharp
type ProfileStore = { Name: AsyncSource<string> }

let private waiting = Component.template (fun _ () -> ()) (p (Attrs.className "loading") { "Loading…" })

let private failed =
    Component.template (fun _ (failure: BoundaryFailure) -> failure) (
        p (Attrs.className "error") {
            fun f -> f.Error.Message
            button (Events.onClick (fun f _ -> f.Retry ())) { "Retry" }
        }
    )

/// Builds the profile definitions over one store; call it once.
let profileView (store: ProfileStore) =
    let card =
        Component.template (fun _ (status: BoundaryStatus) -> status) (
            article (Attrs.data ("refreshing", fun s -> if s.IsPending () then Some "true" else None)) {
                h2 () { fun _ -> store.Name.Value }
            }
        )

    Component.template (fun _ () -> ()) (section () { boundary waiting failed card })
```

`boundary pending failed content` takes three components:

| Component | Props | Shown |
| --- | --- | --- |
| `pending` | `unit` | From the first pending read under the content until the content's first reveal. |
| `failed` | `BoundaryFailure` | After a failure under the content: a binding read, a setup or a handler that throws. |
| `content` | `BoundaryStatus` | Once every binding, row and arm under it has a value. |

- `BoundaryFailure.Error` is the first exception of the failure. `BoundaryFailure.Retry ()` disposes
  the error view and mounts the content again with a new setup.
- `BoundaryStatus.IsPending ()` is a tracked reader: `true` after the first reveal while a binding,
  an arm's selection or a list's items under the boundary wait for a pending read. The content stays
  shown during that wait: a refetch keeps the previous data, arms and rows on screen.
- The content receives the status as its props. Data the content shows comes from state the content's
  definition can reach, such as the store captured above. Build the definitions once.

### Follow the example

1. With no name available, the section displays `Loading…`.
2. Settle the name as `Ada`: the article replaces the pending view.
3. During a later pending read, the article stays visible and gets `data-refreshing="true"`.
4. A failure shows its message and **Retry**; retry starts fresh content setup.

## Where failures go

A failure under a boundary goes to that boundary. A failure outside every boundary goes to the mount's
`report` (see [Mounts and lifetimes](lifetimes.md#failures)), and the failing binding keeps its last
value. A pending read outside every boundary keeps the binding's last value, or the region's last arm
or rows. A text binding that has not yet read a value shows empty text, and a region that has not yet
read its source shows no rows; a hydrating region keeps the server's nodes until its source settles.
On the server, a pending read outside every boundary fails the render (see below).

## On the server

The server renders the boundary's current state between markers that record it:

| State | Server HTML |
| --- | --- |
| Content ready | `<!--[c-->…<!--]-->` |
| Content pending | `<!--[p-->` and the pending view, `<!--]-->` |
| Content failed | `<!--[e-->` and the error view, `<!--]-->` |

```html
<section><!--[p--><p class="loading">Loading…</p><!--]--></section>
<section><!--[c--><article><h2>Ada</h2></article><!--]--></section>
<section><!--[e--><p class="error">offline<button>Retry</button></p><!--]--></section>
```

A server render is synchronous: the HTML holds the state of each read at the moment the render reaches it.
An error view rendered on the server receives a `Retry` that does nothing; hydration removes the
server's error view and mounts the boundary fresh (see [Hydration](../server/hydration.md#boundaries)).
A pending read outside every boundary raises `ServerPendingException` from `Server.render`. See
[Server rendering](../server/rendering.md#pending-reads) and, for how the client picks up each state,
[Hydration](../server/hydration.md#boundaries).

## The explicit form

`t.boundary pending failed content` takes the same three components. The authored layer's
`Authoring.boundary` is the [`Program.View`](authored.md) equivalent.
