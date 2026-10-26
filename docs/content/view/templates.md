---
title: Templates and bindings
---

A `TemplateView<'inst>` describes static HTML and dynamic **parts**: text, attributes, properties,
events and hooks. Build it once; mounting creates an instance and binds its readers to that instance.

The examples use the [sugar](sugar.md) vocabulary. The [explicit form](#the-explicit-form) at the
end of the page builds the same trees. Every sample on this page compiles with these opens:

```fsharp
open Fable.Core.TS
open global.Ranvier
open Ranvier.View
open Ranvier.View.Sugar
```

## Elements

A tag takes its parts as arguments and its children as a computation-expression body:

```fsharp
div (Attrs.className "panel", Attrs.id "main") {
    h2 () { "Totals" }
    input (Attrs.attribute ("type", "search"))
    p () { }
}
```

- `()` is an element with no parts. `{ }` is an element with no children; a non-void element needs
  it even when empty.
- A void element (`input`, `br`, `img`, `meta` and the other HTML void elements) takes parts and no
  body. Inside a body it is yielded as it is. In every other position that takes a view (a template
  root, the content of `show`, an `armOf` view, a `fragment` child, a row view) write its `View`:
  `(img (parts)).View`.
- An element's parts are typed by its DOM interface. `Props.Input.value` applies to `input` only, and
  any other tag fails to compile with it.

```fsharp
type Avatar = { Shown: Signal<bool> }

let avatar =
    Component.template (fun _ (shown: Signal<bool>) -> { Shown = shown }) (
        span (Attrs.className "avatar") { show (fun a -> a.Shown.Value) (img (Attrs.attribute ("alt", "avatar"))).View }
    )

let rule = Component.template (fun _ () -> ()) (hr (Attrs.className "rule")).View
```

## Element names

Tag names are lowercase letters, digits and hyphens, starting with a letter; attribute names also allow underscores. The
case-sensitive SVG names the HTML parser restores (`viewBox`, `preserveAspectRatio`,
`linearGradient`, `foreignObject`, `feGaussianBlur`, `refX` and the others) are written as given,
each matched as a whole name. Under Fable the module of the sugar's SVG tags registers them when it
loads, so a module defining a template with such a name imports an SVG tag or follows a module that
does; on .NET they are always accepted. Any other mixed-case name, such as `className`, raises, as
does an attribute name that is empty, null, starts with `on` or is `srcdoc`.

Some elements are refused when a template or typed view mounts, under both commit modes:

- a `script` element, with or without children, in either namespace (`t.element (HtmlTag "script")`,
  `t.script`, an SVG `script`): pages load scripts through the server
  [document shell](../server/deployment.md#the-page);
- a raw-text element with children (`style`, `iframe` and the others), and `plaintext`;
- a `template` element;
- a void element with children.

A childless `iframe` mounts. The server render refuses every raw-text element. The server's
definition path also refuses an RCDATA element (`textarea`, `title`) with element children, and one
element's attribute names that differ only by case, such as `viewBox` and `viewbox`; a Fable client
bundle accepts those last two.

## Text

An element body yields text in three forms:

```fsharp
type Greeting = { Name: Signal<string>; Shout: Memo<string> }

let greeting =
    Component.template
        (fun _ (name: Signal<string>) -> { Name = name; Shout = createMemo (fun _ -> name.Value.ToUpper ()) })
        (p (Attrs.className "greeting") {
            "Hello, "
            fun g -> g.Name.Value
            "! "
            from (fun g -> g.Shout)
        })
```

| Yield | Result |
| --- | --- |
| `"Hello, "` | A static text node. |
| `fun g -> g.Name.Value` | A text binding. It reads the instance and re-runs when a signal or memo it read changes. |
| `from (fun g -> g.Shout)` | A text binding over one signal or memo the instance holds. |

`text read` is the same node as a yielded reader. On the server, adjacent text nodes are separated by
`<!--!-->` so that hydration finds each one:

```html
<p class="greeting">Hello, <!--!-->&lt;Ada&gt;<!--!-->! <!--!-->&lt;ADA&gt;</p>
```

With `Name = "<Ada>"`, the greeting displays `Hello, <Ada>! <ADA>` as text, without creating tags.
Raw markup goes through [`Unsafe.html`](#explicit-only-members).

## Static structure

`for`, `if` and `yield!` inside a body run once, while the template is built. They repeat or omit
structure in the template itself:

```fsharp
let columns = [ "Name"; "Size"; "Modified" ]

let header =
    Component.template (fun _ () -> ()) (
        table () {
            thead () {
                tr () {
                    for column in columns do
                        th () { column }

                    if columns.Length > 2 then
                        th (Attrs.className "actions") { }
                }
            }
        }
    )
```

Content that depends on the instance uses a list or an arm: see [Lists and keyed rows](collections.md)
and [Conditional arms](arms.md). A body line that reads a signal while the template is built reads it
once, at definition time.

## Attributes

Each attribute accepts a fixed value, a reader of the instance, or a `from` source. Readers and
sources make the attribute a binding.

```fsharp
type Chore =
    { Title: Signal<string>
      Note: Signal<string option>
      Done: Signal<bool>
      Owner: string }

let chore =
    Component.template (fun _ (chore: Chore) -> chore) (
        li (
            Attrs.className "chore",
            Attrs.title (fun c -> c.Title.Value),
            Attrs.data ("owner", fun c -> Some c.Owner),
            Attrs.attribute ("aria-describedby", fun c -> c.Note.Value |> Option.map (fun _ -> "note")),
            Attrs.hidden (from (fun c -> c.Done))
        ) {
            fun c -> c.Title.Value
        }
    )
```

- **Named attributes** (`className`, `id`, `title`, `lang`, `dir`, `role`, `hidden`, `inert` and the
  `aria*` members) take a `string`, a `string option`, a reader returning either, or a source.
- **Optional values** remove the attribute when they are `None`. A reader returning `null` also
  removes it.
- **Boolean attributes** (`hidden`, `inert`, `Attrs.Button.disabled`, `Attrs.Input.required`, ...)
  take a `bool` and are present with an empty value while it is `true`. ARIA booleans
  (`ariaExpanded`, `ariaBusy`, ...) write `"true"` or `"false"`.
- **Element groups** hold the attributes of one element: `Attrs.Button`, `Attrs.Input`,
  `Attrs.Select`, `Attrs.TextArea` and `Attrs.Option`.
- **`data` and `aria`** take a suffix: `Attrs.data ("owner", ...)` writes `data-owner`.
- **Any other attribute** goes through `Attrs.attribute (name, value)`, where `value` is a `string`,
  a reader returning `string option`, or a source of `string option`.

## Properties

`Props` writes DOM properties: an input's current `value`, a button's `disabled` state, a checkbox's
`checked` state. `Attrs.Input.value` writes the `value` attribute, which is the input's default value;
`Props.Input.value` writes the live value.

```fsharp
type Field = { Text: Signal<string>; Busy: Signal<bool> }

let field =
    Component.template (fun _ (field: Field) -> field) (
        form () {
            input (
                Props.Input.value (from (fun f -> f.Text)),
                Events.onInput (fun f event -> f.Text.Value <- (unbox<Dom.HTMLInputElement> event.currentTarget.Value).value)
            )

            button (Props.Button.disabled (fun f -> f.Busy.Value || f.Text.Value = "")) { "Save" }
        }
    )
```

Each generated property has a server form: `value` is written as the `value` attribute, `disabled`
and `checked` as presence attributes, and a textarea's value as its child text. The field above
renders as `<form><input value=""><button disabled="">Save</button></form>` for an empty text.

A property outside the generated set goes through `Props.propertyStatic`, `Props.propertyWith` or
`Props.propertyFrom` with a writer of the native element:

```fsharp
type Selection = { Partial: Signal<bool> }

let selectAll =
    Component.template (fun _ (selection: Selection) -> selection) (
        label () {
            input (
                Attrs.attribute ("type", "checkbox"),
                Props.propertyWith ((fun (box: Dom.HTMLInputElement) partial -> box.indeterminate <- partial), (fun s -> s.Partial.Value), primitiveSame)
            )

            "Select all"
        }
    )
```

The third argument compares the previous and next value; `primitiveSame` compares strings, numbers,
booleans and options of them. Without a `PropertySerialization` argument the property is
**browser-only**: the server's `Reject` policy raises for it, and `Omit` leaves it out (see
[Server rendering](../server/rendering.md#browser-only-parts)). `PropertyHtml.text`,
`PropertyHtml.number`, `PropertyHtml.presence`, `PropertyHtml.optional` and `PropertyHtml.childText`
give a custom property a server form. `PropertyHtml.optional` omits the attribute for `None`; on .NET it
also omits it for `Some null`, which Fable erases to `None`. The `OptionalAttribute` and
`ReactiveAttribute` modifiers treat `Some null` the same way.

## How bindings re-run

Each instance has one **render effect**. It reads every binding of the instance in template order,
then writes each value that differs from the one already in the DOM. A change to any signal read by
any of the instance's bindings re-runs the effect, and the effect writes changed values only.

- A binding whose read is [pending](../guide/pending.md) keeps the value already written. The
  enclosing [boundary](boundaries.md) records the wait.
- A binding whose read throws keeps its value, and the failure goes to the enclosing boundary or,
  outside every boundary, to the mount's `report`.
- Reads in a binding are tracked; writes to the DOM are untracked.
- A binding with its own effect re-runs alone. Mark it with `t.isolated` (below) when one value
  changes much more often than the rest of the instance.

Static text and fixed attributes are written once.

![A signal change re-runs an instance render effect, which compares bindings and writes only changed DOM values.](/Ranvier/diagrams/view-bindings.svg)

## Explicit-only members

Four template members have no sugar form. Use them inside `Component.define setup (fun t -> ...)`,
where free tags and `t.` members mix in one body:

| Member | Result |
| --- | --- |
| `t.isolated part` / `t.isolated (t.text read)` | A binding with its own effect. |
| `t.view (fun i -> view)` | A view hole that renders a [`Program.View`](authored.md), read once per instance. |
| `t.Unsafe.html content` / `t.Unsafe.html (fun i -> content)` | The element's whole content, parsed from an `UnsafeHtml` value without escaping. |
| `t.Document.title`, `.meta`, `.link` | [Document head](document-head.md) entries. |

```fsharp
type Note = { Html: UnsafeHtml; Count: Signal<int> }

let note =
    Component.define (fun _ (note: Note) -> note) (fun t ->
        div () {
            div (t.Unsafe.html (fun n -> n.Html)) { }
            span () { t.isolated (t.text (fun n -> string n.Count.Value)) }
        })
```

`UnsafeHtml.Trust markup` wraps trusted or sanitised markup. Scripts, inline handlers and
`javascript:` URLs in it run in server HTML; on the client, script elements stay inert and inline
handlers and URLs run. An element with raw content declares no template children.

## The explicit form

`Component.define setup build` passes `build` a `TemplateBuilder<'inst>`. Its members carry the
instance type from `setup`'s result into every lambda. Elements take a parts array and a children
array, and text is `t.text`:

```fsharp
let greeting =
    Component.define
        (fun _ (name: Signal<string>) -> { Name = name; Shout = createMemo (fun _ -> name.Value.ToUpper ()) })
        (fun t ->
            t.p
                [| t.Attrs.className "greeting" |]
                [|
                    t.text "Hello, "
                    t.text (fun (g: Greeting) -> g.Name.Value)
                    t.text "! "
                    t.text (t.from (fun (g: Greeting) -> g.Shout))
                |])
```

| Sugar | Explicit |
| --- | --- |
| `div (parts) { children }` | `t.div [\| parts \|] [\| children \|]` |
| `input (parts)` | `t.input [\| parts \|]` |
| `"text"` / `fun i -> ...` / `from (...)` in a body | `t.text "text"` / `t.text (fun i -> ...)` / `t.text (t.from (...))` |
| `Attrs`, `Props`, `Events`, `Hooks` | `t.Attrs`, `t.Props`, `t.Events`, `t.Hooks` |
| `base'`, `option'` | `t.base` (a keyword, written in double backticks), `t.option` |
| `fragment [\| ... \|]` | `t.fragment [\| ... \|]` |

Both forms produce the same template tree, the same server HTML and the same mounted resources.
