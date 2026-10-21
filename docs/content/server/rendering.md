---
title: Rendering
---

`Ranvier.View.Foundation.Server` renders a template component to HTML. It compiles for .NET and Fable
and depends on the DOM types only, which compile on .NET. The samples use the opens from
[Templates and bindings](../view/templates.md).

## A render is one request

```fsharp
let options = { Server.Options.defaults with Policy = Omit }

let html = Server.render options counter { Start = 0; Label = "Clicks" }
```

Each call to `Server.render options definition props`:

1. creates a new [graph](../guide/graph.fsx) from `options.GraphOptions`, with one root owner for the
   request;
2. runs every instance's setup untracked, under its own owner, in template order;
3. reads every binding once and writes the HTML;
4. disposes the root and the graph, running every `Setup.OnCleanup` and the cleanups of everything
   setup created, before returning or raising.

A render reads each signal once, untracked. A signal that belongs to another graph, such
as application state shared between requests, is read as it is; a write to it from the request raises
under `ThreadAffinity.Guarded` (see [Threading](../guide/threading.md)). Under Fable, `render` is
synchronous.

## Options

| Field | Default | Meaning |
| --- | --- | --- |
| `Policy` | `Reject` | What a [browser-only part](#browser-only-parts) does: `Reject` raises, `Omit` leaves it out. |
| `IdPrefix` | `"r"` | The root prefix of the ids `Setup.CreateId` returns. Hydration's `IdPrefix` must match. |
| `GraphOptions` | `GraphOptions.Default` | The options of the request graph. |

## Entry points

| Function | Returns |
| --- | --- |
| `Server.render options definition props` | The body HTML as a `string`. |
| `Server.renderTo options definition props write` | Passes the same HTML to `write` in segments, in order. |
| `Server.renderPage options definition props` | A `Page` with `Body`, the same HTML, and `Head`, the [document head](../view/document-head.md) entries' HTML. |
| `Server.renderDocument options document definition props` | The whole HTML document around the page, as a `string`. See [Deployment](deployment.md#the-page). |
| `Server.renderDocumentTo options document definition props write` | Passes the same document to `write` in segments, in order. |

```fsharp
let writeCounter (output: System.IO.TextWriter) =
    Server.renderTo options counter { Start = 0; Label = "Clicks" } output.Write
```

`renderTo` and `renderDocumentTo` render the whole body before their first `write`, and `write` runs only after a
successful render. Under Fable `renderTo` writes one segment.

### Pages

`renderPage` collects the head entries of every instance in the request, whatever the policy. `renderDocument`
writes `Head` inside the document's `<head>` and `Body` inside the host element the client hydrates, with the state
elements and scripts after the host. See [Deployment](deployment.md#the-page) for a complete page.

## The HTML

The body is the instance between root markers, with markers wherever the client needs them to find its
nodes:

| Markup | Written for |
| --- | --- |
| `<!--[r-->` … `<!--]-->` | The root of the render. |
| `<!--!-->` | Between adjacent text nodes. |
| `<!--_-->` | A text binding whose value is empty. |
| `<!--ranvier:row-->` | A row or arm with no nodes. |
| `<!--[-->` … `<!--]-->` | A list region or a view hole. |
| `<!--[a:hash-->` … `<!--]-->` | An arm region; `hash` identifies the selected arm's structure. |
| `<!--[c-->`, `<!--[p-->`, `<!--[e-->` … `<!--]-->` | A boundary's ready content, pending view or error view. |

```html
<!--[r--><div><button>Details</button><!--[a:8fe1b6e4--><p>b</p><!--]--></div><!--]-->
```

- Text and attribute values are escaped: `<`, `>`, `&` and a carriage return (as `&#13;`) everywhere, `"` in
  attributes. `Html.escape`
  is the same escaper. `null` text renders as empty text.
- Event handlers and hooks are browser-only; see below.
- Generated properties write their attribute form: an input's `value` as `value="..."`, `checked` and
  `disabled` as presence attributes, a textarea's value as its text.
- A line feed at the start of a `pre`, `listing` or `textarea` binding is doubled; the HTML parser's
  removal of one leading line feed leaves the value intact.
- [Raw content](../view/templates.md#explicit-only-members) is written as given.

### Markup the parser would change

The server raises when the template holds structure that the browser's HTML parser rebuilds, because
hydration could not find the nodes afterwards:

```text
The HTML parser restructures this markup (table-row) at table/tr
```

A `tr` directly inside `table` (the parser inserts a `tbody`), a `p` inside a `p`, nested `a` or
`button` elements and similar cases raise this way. Write the structure the parser produces: `table`,
then `tbody`, then `tr`.

## Browser-only parts

Event handlers, hooks (`mounted`, `ref`, `widget`, `lend`) and properties without a server form exist
in the browser only. Under `Policy = Reject`, the default, the first one raises:

```text
Browser capability requires explicit Omit policy: <button> listener click
```

`Policy = Omit` leaves them out of the HTML; the client attaches them when it hydrates. Interactive
components render with `Omit`.

## Pending reads

A server render is synchronous: every read happens once, in the state it has when the render reaches
it.

- A pending read under a [boundary](../view/boundaries.md) renders the boundary's pending view between
  `<!--[p-->` and `<!--]-->`. A failed read renders its error view between `<!--[e-->` and `<!--]-->`.
- A pending read outside every boundary raises `Server.ServerPendingException`, whose `Pending` field is
  the core's `NotReadyException`.

```fsharp
let tryRender (definition: Component<unit>) =
    try
        Ok (Server.render options definition ())
    with Server.ServerPendingException pending ->
        Error pending.Message
```

To render data that loads asynchronously, settle it before the render (load it, then pass it in the
props or settle the source), or put the reads under a boundary and let the client finish them.

## Failures

A setup, binding or cleanup that throws fails the render. The request's root is disposed first, so
every cleanup still runs. When the render failed and its cleanup also failed, or the cleanup alone
failed, `render` raises an `AggregateException` whose inner exceptions are the render's failure, if
any, then the cleanup failures.
