---
title: Troubleshooting views
---

Each entry gives a message or symptom, its cause and the fix. Compile errors of the sugar are listed in
[Sugar](sugar.md) and the
[view sugar README](https://github.com/shayanhabibi/Ranvier/blob/master/experiments/Ranvier.View.Portable/README.md).

## Compile errors

**FS0001, FS0193 or FS0041 at an element.** A lambda inside it infers the wrong instance type. Apply
the [inference rule](sugar.md#the-inference-rule): annotate a lambda that reads a class member
(`Signal`, `Memo`, a class component) or a label also declared by a later record.

**FS0072 on a lambda that reads a class member.** No record declares the label, and the lambda has no
type to start from. Annotate it with the class type.

**FS0041 "No overloads match for method 'Yield'" with a tag type in the message.** A non-void element
is written without a body: `div (parts)`. Add `{ }`.

**FS0193: an element type is not compatible with `TemplateView`.** A void element or a component tag is
used outside an element body, for example as a template root or as `show` content. Write its `View`:
`(input (parts)).View`.

**FS0003 on a tag.** A local value has the tag's name, often `p`, `a` or `b`. Rename the value.

**FS0892 on `open Ranvier.View.Foundation.Generated.Html`.** The module requires qualified access.
Alias it: `module H = Ranvier.View.Foundation.Generated.Html`.

**FS0039: `TemplateBuilder` or `AttributeBuilder` does not define `div` or `className`.** The generated
members come from `open Ranvier.View.Foundation`. Add it to the file's opens.

## Runtime errors

**"Browser capability requires explicit Omit policy: &lt;button&gt; listener click"**: a server render
with the default `Reject` policy reached an event handler, hook or browser-only property. Render with
`{ Server.Options.defaults with Policy = Omit }`. See [Browser-only parts](../server/rendering.md#browser-only-parts).

**"A read outside every boundary is pending"** (`ServerPendingException`): a server render read an
async source or memo with no value, outside every boundary. Settle the data before rendering, or put
the read under a [boundary](boundaries.md).

**"The HTML parser restructures this markup (table-row) at table/tr"**: the template holds structure
the browser's parser rebuilds. Write the parser's structure, here `table`, `tbody`, `tr`. See
[Markup the parser would change](../server/rendering.md#markup-the-parser-would-change).

**"Hydration mismatch at 0/1: expected &lt;button&gt;, found span"** (`HydrationMismatchException`,
under `Strict`, or reported otherwise): the server HTML differs from the client's template. Check that
the client hydrates the same definition with the server's props, the same `IdPrefix`, and no `if` that
depends on the target. See [Mismatches](../server/hydration.md#mismatches).

**"a template definition was created while a region was read; bind it in the template's build
function"** (Fable Debug builds): a definition (`armOf`, a declaration helper, `Component.define`) is
created inside an arms function or a list read. Bind it with `let` before the template.

**"Hydration supports synchronous commits only"** (`NotSupportedException`): `hydrateWith` was given
`QueuedCommits`. Hydrate with `SynchronousCommits`.

**`InvalidOperationException` from `CreateId`**: the id was requested after the instance's setup
returned. Call `setup.CreateId ()` in setup and store the id in the instance.

**"Script elements are not created by templates or typed views; load scripts through the server
document shell"**: a template or typed view holds a `script` element. Load the script through the
server's [document shell](../server/deployment.md#the-page). See [Element names](templates.md#element-names)
for the other refused elements: **"Raw-text elements require an explicit HTML serialization
policy"**, **"Template elements are not supported in templates"** and **"A void element has
children"**.

**"Tag names must contain lowercase letters, digits or hyphens, or be a case-sensitive SVG name"**
or **"Attribute names must be lowercase or a case-sensitive SVG name, …"**: a tag name holds a character other than lowercase letters, digits and hyphens, an attribute name one other than those and underscores, or a name has uppercase
letters and is not one of the SVG names, or, under Fable, the module defining the template loads
before any module with an SVG tag. Write the name in lowercase, or import an SVG tag in that module.

**"An element takes viewBox and viewbox, which differ only by case"** (on .NET, when the template is
defined): give the element one spelling.

**"An exit hook requires a row or arm root element"** (Debug builds): a `Motion.exit`,
`Motion.presence` or `Hooks.exit` part sits on an element other than a list row's or an arm's root.
Move it to the root. See [Leaving](motion-parts.md#leaving).

**"An element takes at most one content part"** or **"An element with a content part has no template
children"**: an element has two of `Unsafe.html` and `Hooks.widget`, or one of them with children.
Give the content its own element.

## Symptoms

**An arm remounts on every update.** Its definition is created inside the arms function or read from a
`static member` property. Bind it with `let`. See [Conditional arms](arms.md).

**A row keeps showing an old value after its item was replaced.** The list uses `forEachFixed`, whose
row keeps the item it was created for. Use `forEach`, or hold the changing value in a signal on the
item. See [Lists and keyed rows](collections.md).

**A child does not follow a parent value.** `render`'s props reader runs once per instance. Pass the
child a `Signal`, a `Memo` or a reader.

**Several effects run for one click.** Handlers run unbatched. Wrap the handler's writes in
`Api.batch`. See [Events and hooks](events.md).

**A hydrated page re-renders a region and reports a mismatch.** The client's first state differs from
the server's in structure: another arm, another row count. Transfer the server's props with
[state transfer](../server/state-transfer.md).

**Text shows empty after mounting.** The binding's first read was pending outside every boundary. Put
the read under a boundary to show a pending view instead.
