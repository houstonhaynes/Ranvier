---
title: State transfer
---

Encode the server's initial props in a `<script type="application/json">` element. Decode them
before hydration so the client starts from the same data. `Ranvier.Wire` shares a JSON contract
between .NET and Fable.

## A codec shared by both sides

```fsharp
open Ranvier.Wire
open Ranvier.Wire.Thoth.Json.Core

let counterPropsCodec: Codec<CounterProps> =
    objectCodec {
        let! start = Codec.field "start" _.Start Codec.int
        and! label = Codec.field "label" _.Label Codec.string
        return { Start = start; Label = label }
    }

let counterProps = WireCodec.ofCodec counterPropsCodec

/// The id of the script element that carries the props from server to client.
let stateId = "counter-props"
```

Compile this file into both the server and the client projects. `WireCodec.ofCodec` wraps a
[Thoth.Json](https://github.com/thoth-org/Thoth.Json) codec, vendored under
`Ranvier.Wire.Thoth.Json.Core`; write the contract with `objectCodec` and `variantCodec`.

```bash
dotnet add package Ranvier.Wire --prerelease
```

## Writing the state

```fsharp
Server.stateScript stateId (counterProps.Encode props)
```

```html
<script type="application/json" id="counter-props">{"start":2,"label":"\u003c/script>"}</script>
```

`Server.stateScript id json` escapes the id as an attribute and writes every `<`, U+2028 and U+2029 in
the JSON as its six-character escape. The element's text then runs to its own end tag, and
`JSON.parse` of its `textContent` restores the original value. The escaped sample decodes to
`{ Start = 2; Label = "</script>" }`, without closing the state element early.

## Reading the state

```fsharp
let props =
    match Hydration.readState stateId |> Option.map counterProps.Decode with
    | Some (Ok props) -> props
    | Some (Error error) -> failwithf "Unreadable props: %s" error.Message
    | None -> { Start = 0; Label = "Clicks" }
```

`Hydration.readState id` returns the `textContent` of the document's element with that id, or `None`
when the document has none. `Decode` returns every failure as an `Error`:

| `WireError` | Cause |
| --- | --- |
| `Syntax` | The text is null, not JSON, or holds a lone surrogate. |
| `Limit` | The text exceeds the codec's `WireLimits`: by default depth 64 and 100,000 items, checked before parsing, and 4,300 digits for a `Codecs.bigint` value. |
| `Shape` | The JSON does not fit the type. |

## The wire contract

A codec reads the text that the other target wrote: dates, 64-bit integers, decimals and floats have
one wire form on both targets. Use `Codecs.dateTimeOffset`, `Codecs.bigint` and `Codecs.decimal` for
those types; Thoth's own members for them write differently on .NET and Fable and are marked obsolete.
The [Ranvier.Wire README](https://github.com/ranvier-org/Ranvier/blob/master/src/Ranvier.Wire/README.md)
lists the form of every type and the remaining differences between the targets.

## What to transfer

- **Props, not signals.** Transfer the values the root's props are built from; the client's setup
  creates its signals from them, as the server's did.
- **Settled data.** A value loaded before the render can travel in the props or in its own script
  element; the client then starts with it settled, and a [boundary](../view/boundaries.md) over the
  same read hydrates its ready content.
- **One element per value.** Several `stateScript` elements with distinct ids can share a page.
  `Server.renderDocument` writes one per entry of `DocumentShell.State`, after the host element; see
  [Deployment](deployment.md#the-page).
