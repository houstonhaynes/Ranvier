---
title: Document head
---

A component can own entries in the document's `<head>`: the title, `meta` elements and `link`
elements. An entry is shown once its instance is placed and withdrawn when the instance is disposed,
so the head follows whichever components are mounted.

Head entries are `t.Document` members, written inside `Component.define`; free tags mix with them in
the same body. The sample uses the opens from [Templates and bindings](templates.md).

```fsharp
type Article = { Title: Signal<string>; Summary: string; Slug: string }

let articlePage =
    Component.define (fun _ (article: Article) -> article) (fun t ->
        article () {
            t.Document.title (fun a -> a.Title.Value + " · Journal")
            t.Document.meta [| t.Attrs.attribute ("name", "description"); t.Attrs.attribute ("content", fun a -> Some a.Summary) |]
            t.Document.link [| t.Attrs.attribute ("rel", "canonical"); t.Attrs.attribute ("href", fun a -> Some ("/articles/" + a.Slug)) |]
            h1 () { fun a -> a.Title.Value }
        })
```

## Entries and keys

| Member | Key |
| --- | --- |
| `t.Document.title value` / `t.Document.title read` | `title` |
| `t.Document.meta parts` | Its static `name`, `property`, `http-equiv` or `charset` attribute. One of them is required. |
| `t.Document.link parts` | `canonical` for a static `rel="canonical"`; otherwise the link is unkeyed. |
| `t.Document.link (key, parts)` | `canonical` for a static `rel="canonical"`; otherwise `key`. |

- For each key, the head holds the most recently shown live entry. A nested page's title replaces its
  layout's title while the page is mounted, and the layout's title returns when the page is disposed.
- Every unkeyed link is in the head while its instance is live.
- Parts are attributes only. Key attributes are static; other attributes can be readers or sources.
- `data-ranvier-head` is reserved: the registry writes it on every element it manages.

## On the server

`Server.renderPage` returns the body and the head entries' HTML together:

```fsharp
let page = Server.renderPage options articlePage { Title = createSignal "Tides"; Summary = "On \"moons\""; Slug = "tides" }
// page.Body: <!--[r--><article><h1>Tides</h1></article><!--]-->
// page.Head:
```

```html
<title data-ranvier-head="title">Tides · Journal</title><meta data-ranvier-head="meta:name=description" name="description" content="On &quot;moons&quot;"><link data-ranvier-head="link:canonical" rel="canonical" href="/articles/tides">
```

The head lists one element per key, in the order the keys first activated, and one element per
unkeyed link. `Server.renderDocument` writes the head entries inside the document's `<head>`; see
[Server rendering](../server/rendering.md#pages).

## When hydrating

[Hydration](../server/hydration.md) adopts the head elements marked with each entry's key. A title
entry writes the document's title only when it differs. Once the mount is built, every marked head
element that no entry adopted is removed.
