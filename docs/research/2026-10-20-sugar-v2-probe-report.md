> Probe report of 2026-10-20 at df3bb0b; the probe code was not committed. The decisions it informed are in the [view sugar v2 spec](../superpowers/specs/2026-10-20-view-sugar-v2-design.md).
# View sugar v2 probe: free tag functions and `match!` arms — report

Spike run on 2026-10-20 in `C:\Users\shaya\RiderProjects\Ranvier-sugar-probe2` (detached at `df3bb0b`), F# 10 / FSharp.Core 10.1.401, Fable 5.18.0 Release and Debug, net10.0 Release, jsdom for DOM and hydration checks, and Chromium 153.0.8010.12 on a Ryzen 9 9900X for timings. The probe code is in the worktree and is **not committed**: the repo's pre-commit hook rejects the unformatted probe files, and I did not bypass it. Everything is staged; `git status` lists it.

## Answers in one line

- **Free tags work.** `div (...) { ... }` compiles, renders the same tree, server HTML and `structuralHash` as the explicit twins, and mounts, updates and hydrates with identical owners, effects, edges, listener registrations and DOM writes. It costs +299 B gzip over today's sugar. The price is type inference: the instance type never flows down from `Component.define`, so a lambda whose body cannot infer its own instance type needs an annotation.
- **`match!` arms work only with strategy (a), rebuild on change**, which is correct but slower: mount 1.23×, switch 1.09× and payload update 1.34× the explicit fixture, with about 12× the allocation per payload update and +2.5 kB gzip. **Strategy (b), payload as a reader, is refuted:** `match!` binds values, not readers, so the first value the region sees is captured for every row.

## The prototype

- `experiments/Ranvier.View.Portable/FreeSugar.fs`, `namespace Ranvier.View.Foundation.Free`:
  - `Element<'native,'inst>`, an abstract CE builder: the same `Yield`, `Zero`, `Combine`, `Delay`, `For` and `Run` as `ElementBuilder`, plus `Yield(VoidElement)` and `Bind`.
  - `VoidElement<'inst>`.
  - Entry points without `t`: the type functions `Attrs<'inst>`, `Props<'inst>`, `Events<'inst>` and `Hooks<'inst>`, which reuse every generated member unchanged; a statically resolved `inline from`; and free `text`, `fragment`, `armOf`, `arm`, `matchWith`, `forEach`, `forEachFixed` and `show`.
  - `Component.template setup view`.
  - The `match!` runtime (`CaseArms.rebuild` and `CaseArms.once`, chosen with `watch` or `watchOnce`).
- `experiments/Ranvier.View.Portable/FreeElements.fs`: one generic class per tag for all 112 tags, as a generator would emit them.
- Fixtures: `tests/FreeFixtures.fs` (the demo screen and the `match!` twins of `SugarFixtures.armsExplicit`), `tests/KeyedRowsFreePerf.fs`, `tests/KeyedRowsMatchPerf.fs` and `tests/FrameworkFreePerf.fs`.
- Checks: `freeCases` appended to `tests/SugarChecks.fs`, run by `tests/free.test.mjs`, `free-counters.test.mjs` (core counters compiled in) and `free-debug.test.mjs` (Fable Debug compile).
- Compile probes for .NET: `probe/FreeProbe` and `probe/Collide`.
- Bench: three new adapters in `benchmarks/frameworks/keyed-page.mjs`, and `free-size.mjs`.
- One change to `Template.fs`: `regionRead` goes from `private` to `internal`.

**A tag is a class, because a free `let` function cannot take `[<ParamArray>]`.** The same reason is why Oxpecker.Solid's tags are types. The class is generic in the instance type: `type div<'i>`.

**Fable 5.18.0 miscompiles a ParamArray primary constructor.** It emits `constructor(...ps)` together with a helper `$ctor(...ps) { return new X(ps) }`, which nests the parts array one level deeper. The first jsdom run failed with `TypeError: Cannot read properties of undefined (reading '0')` in `elementNode`. The generator has to emit a private primary constructor and a public ParamArray constructor:

```fsharp
type div<'i> private (ps: Part<Dom.HTMLDivElement, 'i> array, primary: unit) =
    inherit Element<Dom.HTMLDivElement, 'i>(Div, ps)
    new([<ParamArray>] ps: Part<Dom.HTMLDivElement, 'i> array) = div<'i>(ps, ())
```

## Question 1: free tag functions

### 1.1 Does the instance type flow from `Component.define`, or from the parent's `Yield`, into children and parts?

**No, not anywhere.** Each lambda gets its type from its own body. The instance type is unified only after the whole expression has been checked.

`Records.fs` declares `KeyedRow = { Id; Label }` and then `ArmRow = { Id; Mode }`, so a bare `r.Id` resolves to `ArmRow`. With `keyedSetup : Setup -> KeyedRow -> KeyedRow`, every site below fails, under both `Component.template setup (view)` and `Component.define setup (fun _ -> view)`:

| Probe | Site | Result |
|---|---|---|
| F1 | root part `span (Attrs.title (fun r -> string r.Id)) {..}` | FS0193 `TemplateView<ArmRow>` is not compatible with `TemplateView<KeyedRow>` |
| F2 | root child `span () { fun r -> string r.Id }` | FS0001 expecting `KeyedRow -> 'a`, given `ArmRow -> string` |
| F3, F4 | nested part, nested child | FS0041 `Yield` with known argument `TemplateView<ArmRow>` |
| F5 | root part under `Component.define (fun _ -> ...)` | FS0193 |
| F6, F7 | `Events.onClick (fun r _ -> ... r.Id)`, a non-overloaded member, at the root and nested | FS0193 / FS0041 |
| F8, F9 | free non-overloaded `text (fun r -> ...)`, nested and at the root | FS0041 |
| F10 | `from (fun r -> r.Label)` (`Label` is declared by one record only) | compiles |

The cause is two orders of type checking:

- **The builder before the body.** In `span (args) { body }`, F# checks the builder expression `span (args)` before it relates the CE's `Run` type to the expected type. The arguments therefore see a fresh `'i`.
- **Implicit yields alone.** Each implicit-yield line in a body is type-checked on its own first, to decide whether it is a `unit` statement. A lambda yield therefore gets no expected type from `Yield` either.

With `t`, the instance type was carried by a value, `t`, so `t.Attrs.*` and `t.text` knew it before checking their lambda.

A class instance type is worse than a record. In G1 and G2, `fun c -> string c.Count.Value` on a class `CounterInstance` resolved `.Count` to the unrelated record `DemoState.Count` and failed with FS0001 `DemoState` vs `CounterInstance`. With no record label of that name in scope, it is FS0072.

### 1.2 Lambda annotations

Measured by deleting annotations until the compiler objected, for today's `t.` sugar and for free tags:

| Fixture | Annotations in today's fixture | Minimum with `t.` | Minimum with free tags |
|---|---|---|---|
| Demo screen | 9 | 0 | **0**: all 9 dropped, and `FreeFixtures.demoFree` has none |
| Keyed rows | 4 | 1 | 3 |
| Arm rows | 6 (2 of them on `TemplateBuilder`) | 0 | 1 |
| Benchmark row (`FrameworkFreePerf`) | 6 | not measured | 0 |

The annotations free tags cannot drop, and why:

1. **The keyed key lambda `fun (row: KeyedRow) -> row.Id`.** `Id` is shared with the later `ArmRow`, and `forEachFixed`'s `'item` is generic. Today's sugar needs this one as well: probe K1 failed with FS0193 `KeyedRow array` vs `ArmRow array`.
2. **`items`, `fun (rows: Signal<_ array>) -> rows.Value`, in both fixtures.** `Signal` is a class, so FS0072 "type ... could not be inferred before accessing its members". With `t.`, `t.forEachFixed` already knew `'inst`, so this is a regression.
3. **The keyed `Id` cell `fun (row: KeyedRow) -> string row.Id`.** The shared label resolves to `ArmRow`. Today `r.text` carries the row type, so this is a regression too.

The `armOf` annotations `(a: TemplateBuilder<unit -> int>)` disappear: there is no builder parameter, and `counted = armOf (b () { fun payload -> string (payload ()) })` gets its type from `arm counted count`.

Designs tried to bring back top-down typing. All are compiled in `probe/FreeProbe`; each row was re-run against the final constructor shape.

| Design | Result |
|---|---|
| A type argument on the tag, `span<KeyedRow> (...)` (G7–G10, H1, H3) | **Refuted.** FS0041 "No overloads match for method 'span`1'". A ParamArray constructor is resolved as two candidate forms (expanded and array), so it is overloaded and its arguments are checked without the expected type. A child yield fails as well (G8, G9). |
| A non-overloaded reader entry point, `Reader.title : ('inst -> string) -> Part<'native,'inst>` | **Works only after an annotated sibling** in the same ParamArray: H5 `span (Attrs.className (fun (r: KeyedRow) -> "a"), Reader.title (fun r -> string r.Id))` compiles, and G5 with `Events.onClick` compiles. It fails at the root without a sibling (H4) and with an overloaded entry after the sibling (G3, FS0193). It never reaches the body (G4, G6). |
| An explicit `yield (fun r -> ...)` or `yield text (...)` after `span<KeyedRow>` (H2, H6) | **Refuted**, FS0041. |

No design restores top-down typing without a value that carries `'inst`, and that value is `t`. Free tags infer the instance type only from lambda bodies. The rule for users becomes: **annotate a lambda whose body reads a label that a later record also declares, or a member of a class instance (including `Signal`).**

### 1.3 Rows: is mixing in an outer part still a compile error?

**Yes.** The row view's own instance type keeps the guarantee: `forEach … (row: TemplateView<unit -> 'item>)` and `forEachFixed … (row: TemplateView<'item>)`. No phantom type or row-builder parameter is needed. Every part and child inside the row unifies with that type, so an outer instance read cannot type-check.

Today's negative cases from `check-types.py`, re-run against the final free shape (each produced exactly one error):

| Case | Today | Free tags |
|---|---|---|
| input-only part inline on a div, `div (Props.Input.value (from ...)) {..}` | FS0041 | **FS0041** "No overloads match for method 'div`1'" |
| children on a void tag, `input () { "x" }` | FS0708 | **FS0708** "…defines a 'Zero' method" |
| another element's part, `Part<HTMLButtonElement,unit>` passed to `span` | FS0001 | **FS0001** |
| the outer instance inside a row through `text (fun (items: SugarItem array) -> ...)` | FS0041 | **FS0041** (`Yield`, known argument `TemplateView<SugarItem array>`) |
| the outer instance inside a row through a part (N4b), or a pre-bound outer part (N4c) | — | **FS0193** `TemplateView<SugarItem array>` vs `TemplateView<(unit -> SugarItem)>` |
| a `Program.View` in a body, `div () { Program.text "x" }` | FS0041 | **FS0041** |
| a generated part of another element, `span (Props.Input.value "x")` | FS0041 | **FS0041** |
| a bare lambda with a shared label | FS0001 | FS0001, FS0193 or FS0041, depending on the site (see 1.1) |

### 1.4 Name collisions (`probe/Collide`, built against `DomTests.fsproj` on .NET)

| Name | Collides with | Evidence | Oxpecker.Solid (`src/Oxpecker.Solid/Tags.fs`, develop) | Proposed |
|---|---|---|---|---|
| `base` | F# keyword | `type base<'i>` → FS0010 "Unexpected keyword 'base' in type name". ``` ``base`` ``` declares, but its own `new` fails (FS0717, FS0696), and with `Generated.Html` open, ``` ``base`` () ``` resolves to Html's ``` ``base`` ops modifiers ``` (FS0041/FS0001). | `base'` | **`base'`**: compiles, and wins over `Generated.Html`. |
| `option` | FSharp.Core `'T option` (arity 1) | The generic tag `option<'i>` has arity 1 and shadows it: `let c1: string option = Some "x"` → FS0001 "expected type 'option<string>' but here has type ''a option'". `defaultArg (x: int option)` fails the same way. | `option`, which is non-generic (arity 0), so it does not shadow | **`option'`** |
| `object` | an unreserved F# keyword (unreserved in F# 4.1) | compiles and is used | `object'` | `object` |
| `select` | the `query { select }` custom operation | `query { for x in [1;2] do select x }` compiles with the tags open | `select` | `select` |
| `map`, `data`, `title`, `time`, `label`, `source`, `link`, `style`, `script`, `var`, `main`, `menu`, `slot`, `template`, `search`, `form`, `input`, `output`, `a`, `b`, `i`, `p`, `q`, `s`, `u` | nothing in FSharp.Core, Fable.Core, Fable.Core.JsInterop, Fable.Core.TS, Fable.Core.TS.Dom, `Ranvier`, `Representation` or `Template` | module D opens the free tags first and every one of those after; all 28 suspicious tags compile. `Option.map`, `Map.empty` and `List.map` are unaffected (c2–c4). | same names | unchanged |
| all 112 tags, plus `Attrs`, `Props` and `Events` | Ranvier's authored `Generated.Html`: lowercase functions for every tag, and the `Attrs`, `Props` and `Events` modules in `Authoring` | Opened **after** the free tags, Html wins everywhere: `div (Attrs.className "x") {…}` → FS0001, FS0193 and FS0740 "Invalid record, sequence or computation expression". Opened **before**, the free tags win: all 112 compile, and `let c7: Part<HTMLDivElement, unit> = Attrs.className "x"` resolves to the free `Attrs`. | n/a | The two vocabularies must not be opened in the same file. Document it, or make `Generated.Html` `[<RequireQualifiedAccess>]`. |
| local values named like a tag, typically one-letter lambda parameters | ordinary shadowing | `let p = "note"` then `p () { "x" }` → FS0003 | same | Document it. |

**Proposed rule:** a tag whose lowercase name is an F# keyword, or an FSharp.Core type of arity 1, takes a trailing apostrophe. Among the 112 tags that is exactly `base'` and `option'`. Everything else keeps its raw name. Ranvier needs `option'` where Oxpecker does not, because Ranvier's tag types are generic.

Attribute spelling: `Attrs.className "x"`, the least change from `t.Attrs.className "x"`. `Attrs` is a type function, `let Attrs<'inst> : AttributeBuilder<'inst> = AttributeBuilder<'inst> ()`, so every generated member (`className`, `data`, `aria`, `Button.disabled`, `Props.Input.value`, `Events.onInput`, …) is reused without regeneration. The whole demo compiles with it, and the FS0041 and FS0001 element typing is unchanged (1.3). A module cannot stand in for it: module functions cannot be overloaded, and the overloads are what tell a value, a reader and a `Source` apart.

### 1.5 Which control-flow forms can be free?

All of them can be free. None needs an instance carrier, and all are compiled and tested:

- **`forEach`, `forEachFixed`, `show`:** they now take a view instead of a builder function, `forEachFixed key items (tr () {..})`.
- **`armOf`:** `armOf (span (Attrs.className "a") { "A" })`.
- **`arm`:** its own copy, which allocates nothing per selection.
- **`matchWith`.**
- **`text`:** non-overloaded. It adds nothing over a yielded lambda, because it cannot carry the instance type either (F8, F9).
- **`from`:** `let inline from (accessor: 'inst -> ^s) when ^s: (member Value: 'value)`, statically resolved because it has to accept both `Signal` and `Memo`. It emits `(instance) => Signal$1__get_Value(instance.Label)`, one call fewer than `t.from`, which emits `(instance) => Signal$1__get_Value(accessor(instance))`.

What they lose is the top-down instance type, which is what costs annotation 2 in 1.2.

### 1.6 Cost

**Runtime is unchanged.** For the demo screen, the keyed fixture and the arm fixture, the free twins match the explicit twins on `layoutSignature`, `structuralHash` and server HTML. Mounted side by side and driven with the same steps, they show identical markup, reports, core counter deltas (owners, effects, edges), `addEventListener` registrations and MutationObserver writes (`tests/free-counters.test.mjs`, 13 pass).

Template-build JS:

- One tag-class instance per element, where today there is one `ElementBuilder` per element.
- `Builders_Attrs()` allocates one `AttributeBuilder` per part, where `t.Attrs` returned a cached one.
- Both happen at definition time only.
- The emitted slot readers are byte-for-byte the same shape, for example `(row_1) => int32ToString(row_1.Id)`.

**0 `curry`, `uncurry` or `partialApply`** in `FreeSugar.js`, `FreeElements.js`, `KeyedRowsFreePerf.js` and `FreeFixtures.js`.

**Bundle** (`benchmarks/frameworks/free-size.mjs`; the same Vite config as `sugar-size.mjs`; each entry is the benchmark row plus the keyed and arm fixtures):

| Entry | raw | gzip | Brotli | Δ gzip vs explicit | Δ gzip vs today's sugar |
|---|---|---|---|---|---|
| explicit | 204,474 | 58,784 | 51,572 | — | — |
| today's sugar | 207,281 | 59,361 | 52,020 | +577 | — |
| free tags (`matchWith` arms) | 208,580 | 59,660 | 52,200 | **+876** | **+299** (raw +1,299, Brotli +180) |
| free tags + `match!` (a) | 215,409 | 61,252 | 53,596 | +2,468 | +1,891 |

Rollup kept only the 10 tag classes that are used. My first run measured +1,263 gzip for the free entry: a module-level `.NET` cache `Dictionary` in `CaseArms` pulled in `Reflection.js`. I moved it under `#if !FABLE_COMPILER`, and the table above is the second run. The free entry is within the 1 kB gzip budget of open question 8; the `match!` entry is not.

Arm timings for the free `matchWith` fixture against explicit `t.matchWith`: ratios 1.049, 1.001 and 0.946 (mount, switch, payload update), with overlapping interquartile ranges (2.3).

## Question 2: `match!` arms cached by case

### 2.1 The CE shape

**It compiles**, in `FreeSugar.fs` and the fixtures, under .NET and Fable. `Bind` sits next to the five `Yield` overloads:

```fsharp
type Watch<'inst, 'value> = internal { Read: 'inst -> 'value; Mode: CaseMode }
let watch (read: 'inst -> 'value) : Watch<'inst, 'value> = { Read = read; Mode = CaseMode.Rebuild }      // (a)
let watchOnce (read: 'inst -> 'value) : Watch<'inst, 'value> = { Read = read; Mode = CaseMode.Once }     // (b)

// on Element<'native,'inst>, next to Yield/Zero/Combine/Delay/For/Run:
member _.Bind(watched: Watch<'inst, 'value>, cases: 'value -> TemplateView<'inst> array) : TemplateView<'inst> array =
    match watched.Mode with
    | CaseMode.Once -> [| CaseArms.once watched.Read cases |]
    | _ -> [| CaseArms.rebuild watched.Read cases |]
```

The brief's example compiles as written:

```fsharp
td () {
    match! watch (fun (row: ArmRow) -> row.Mode.Value) with
    | Plain -> span (Attrs.className "a") { "A" }
    | Counted count -> b () { fun _ -> string count }
}
```

The `watch` lambda needs its annotation, as every other lambda does (1.1).

`Delay` and `Combine` behave: siblings before and after a `match!` stay outside the region. The skeleton of `td () { "before"; match! …; "after" }` is `<td>before<!--[--><!--]-->after</td>`, and its server HTML is `<td>before<!--[a:7f75a908--><b>1</b><!--]-->after</td>`. A case of `()` lowers through `Zero`.

### 2.2 The two payload strategies

**How (a) rebuild works.** On every item read the region does five things:

1. It runs the continuation, which builds a new `TemplateNode` tree.
2. It takes the case tag: `.tag` under Fable, `PreComputeUnionTagReader` on .NET.
3. It looks up a cached definition of that tag whose shape equals the tree. Shape equality is `sameShape`: markup, attributes, static property values, slot targets and modes, events, placed kinds and structure, but not closures.
4. On a miss, it builds a definition from the tree, with each closure replaced by an indirection to the same index in the current payload.
5. It yields the arm with payload `{ Outer = instance; Closures = <this tree's closures> }`.

The arm definition has the same tree as the explicit `armOf` arm, so the same hash.

- **Correctness:**
  - `templated "MATCH (a) rebuild arms switch and update their payload like their twins"` passes, synchronous and queued, against `SugarFixtures.armsExplicit`. It compares markup, reports, core counters, listeners and native writes after each step.
  - The production arm fixture renders the same after create, switch, payload, payload, switch and clear.
- **Payload-dependent shape:** detected. `| Counted count -> if count > 3 then b () { "many" } else i () { fun _ -> string count }` renders `<!--[a:e7bfd6f2--><i>1</i>` for count 1 and `<!--[a:bac0c2f3--><b>many</b>` for count 9 on .NET. The new shape gets a new definition and remounts.
- **Consequence of shape detection:** a case body that puts the payload into *static* text (`span () { string count }`, as opposed to `fun _ -> string count`) has a new shape for every value. Every value then adds a cache entry and remounts. The cache has no bound. This follows from `sameShape` comparing `TextNode` text; I did not measure it.
- **Debug check:** it does not fire for the case definitions, which are built with the internal `Component` constructor and are stable per tag and shape. All (a) cases pass under the Fable Debug compile. It **does** fire, correctly, for a definition nested in a case body: `FreeFixtures.nestedInCase`, a `forEachFixed` inside a `match!` case, fails in Debug with "a template definition was created while a region was read; bind it in the template's build function" and mounts in Release. It should fire: in Release, that nested row definition gets a new identity on every rebuild. `sameShape` then compares `ItemRow` definitions by reference, so every update remounts the arm and grows the cache.
- **Server render and hydration:**
  - On .NET, the fixture's `structuralHash` is `26552d7b` for explicit, (a) and (b) alike, and the arm hashes are `6581bb9d` and `7f75a908` for both forms.
  - Server HTML is equal on .NET and under Fable.
  - `MATCH (a) rebuild hydrates the explicit twin's HTML and its own under Strict` passes with no report, including two updates after hydration (Plain↔Counted 7).

**How (b) once works.** The continuation runs once per case tag, at first sight. The definition's instance is the enclosing instance. The pattern variables (`count`) are whatever value the first sighting had.

- **Correctness: broken.**
  - The site cache is shared by every instance of the template, so all rows render the first row's payload. With rows `[Plain; Counted 4]` and the write `row0 ← Counted 1`, row 0 showed `<b>4</b>` where the explicit fixture shows `<b>1</b>`.
  - The production fixture shows `<b>0</b>` after a payload update that should show `<b>1</b>`.
  - The shape is frozen: on .NET, the `if count > 3` body still showed `<i>1</i>` for count 9.
  - Four tests fail, each with the expected and actual markup: twins (synchronous and queued), the production fixture, and hydration ("Expected …`<b>7</b>`… got …`<b>4</b>`…").
- **"Payload as `unit -> 'p`" cannot come from `match!`.** `match! x with | Counted count ->` desugars to `Bind(x, fun v -> match v with Counted count -> …)`, and F# binds `count` to the `int` inside `v`. Turning each field of an arbitrary union into a reader would need either a reader-shaped user union or a different binding form.
- **The only correct (b):** the case body ignores its pattern variables and re-reads through the instance, `| Counted _ -> b () { fun (row: ArmRow) -> match row.Mode.Value with Counted c -> string c | Plain -> "" }`. That form passes (`MATCH (b) once, payload read through the instance…`). But it is no longer payload-as-reader, and the compiler accepts the broken form just as readily.
- **Debug check:** it does not fire for (b)'s own definitions. It should, since (b) creates them during a region read, and their identity is stable only by accident of the cache.
- **Hydration:** fails on content (above).

### 2.3 Arm benchmark

Command: `keyed-rows.mjs --modes=ranvier,ranvier-free,ranvier-match,ranvier-match-once --ops=armsCreate1k,armsSwitch1k,armsPayload1k`, with `--processes=3 --rounds=15` (3 warmups), modes rotated per round and process, under `bench.lock` and `browser.lock`, all in one session.

- `ranvier` is the production explicit fixture.
- `ranvier-free` is free tags with `matchWith`.
- `ranvier-match` is (a).
- `ranvier-match-once` is (b) in its only correct form, re-reading through the instance. The broken (b) fails the harness's row-text verification.

45 samples per cell. Times in ms; the ratio column is the paired same-round ratio to `ranvier`.

| Operation | Mode | Median | By process | Range | Q1–Q3 | Ratio median (Q1–Q3) |
|---|---|---|---|---|---|---|
| armsCreate1k (mount) | explicit | 16.51 | 18.45 / 16.04 / 15.83 | 12.99–33.71 | 15.45–18.28 | — |
| | free `matchWith` | 17.19 | 19.63 / 15.82 / 15.69 | 13.63–28.24 | 15.52–18.81 | 1.049 (0.945–1.117) |
| | **(a) rebuild** | **19.92** | 23.72 / 19.21 / 19.50 | 15.94–38.28 | 18.76–22.71 | **1.227 (1.132–1.293)** |
| | (b) once, re-read | 16.87 | 20.46 / 15.72 / 15.39 | 13.72–38.70 | 15.30–19.29 | 1.026 (0.925–1.143) |
| armsSwitch1k (switch every arm) | explicit | 8.88 | 10.55 / 8.67 / 8.29 | 6.06–13.79 | 8.05–9.99 | — |
| | free `matchWith` | 8.71 | 10.10 / 8.51 / 8.10 | 6.97–15.32 | 7.98–9.83 | 1.001 (0.910–1.131) |
| | **(a) rebuild** | **9.82** | 11.31 / 9.45 / 8.64 | 7.88–15.16 | 8.56–11.30 | **1.091 (0.960–1.214)** |
| | (b) once, re-read | 8.76 | 10.31 / 8.26 / 8.15 | 6.76–12.71 | 8.14–10.31 | 1.000 (0.914–1.154) |
| armsPayload1k (update every payload) | explicit | 1.47 | 1.70 / 1.51 / 1.28 | 1.05–3.08 | 1.28–1.81 | — |
| | free `matchWith` | 1.47 | 1.72 / 1.59 / 1.28 | 1.06–3.50 | 1.26–1.84 | 0.946 (0.863–1.185) |
| | **(a) rebuild** | **2.11** | 2.43 / 2.09 / 1.82 | 1.38–2.95 | 1.83–2.45 | **1.336 (1.196–1.614)** |
| | (b) once, re-read | 1.65 | 1.98 / 1.62 / 1.35 | 0.96–3.05 | 1.41–1.90 | 1.100 (0.872–1.294) |

Allocation, from `--profile` with CDP heap sampling at a 64-byte interval and 10 runs per operation. Bytes are per operation (1,000 rows; a payload update writes the counted rows):

| Operation | explicit | free `matchWith` | (a) rebuild | (b) once, re-read |
|---|---|---|---|---|
| armsCreate1k | 5.59 MB | 5.59 MB | **8.23 MB (1.47×)** | 5.75 MB (1.03×) |
| armsSwitch1k | 2.16 MB | 2.15 MB | **4.80 MB (2.22×)** | 2.32 MB (1.07×) |
| armsPayload1k | 122.5 kB | 121.6 kB | **1,445 kB (11.8×)** | 182.7 kB (1.49×) |

The top self-allocators in (a)'s update are `elementNode` (305 kB per operation), `CaseArms_sameArray` (291 kB) and the closure-collection arrays: the cost of rebuilding the case tree and comparing its shape on every change. (b)'s extra allocation comes from the re-reading text slot.

The raw results are in `C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/probe2-bench/match-timings.json` and `match-profile.json`. Each records the commit `df3bb0b` and the dirty file list.

### 2.4 Recommendation for `match!` arms

**Ship neither strategy.**

- **(b) is refuted.** It silently renders another instance's payload, and the correct use of it is a re-matching reader that is more verbose than `armOf`.
- **(a) is correct but not worth it:**
  - Time: +23 % on mount, +9 % on switch, +34 % on payload update.
  - Allocation: 11.8× per payload update.
  - Bundle: +1.9 kB gzip over the free-tag build.
  - Hazards: a cache with no bound for payload-dependent static text, and a full remount for any nested control flow in a case body.
  - Saving: 329 instead of 484 non-whitespace characters for the arm template, and 1 definition instead of 3.

**Keep `armOf` with free `matchWith`.** It costs nothing at runtime (ratios 0.95–1.05) and is already shorter than today's sugar.

The cost if this is wrong:

- **Adopting (a) when it should have been rejected:** every arm-heavy screen pays the rebuild on every payload change, invisibly, and a nested `forEach` in a case body remounts on every update in Release.
- **Rejecting (a) when it should have been adopted:** users keep writing two hoisted `armOf` lines per arm. The rejection can be revisited later without breaking anything, because `match!` would only add a `Bind`.

## Rewritten fixtures (free style, in full)

### The demo screen (`tests/FreeFixtures.fs`)

```fsharp
let demoFree =
    Component.template
        (fun _ (state: DemoState) ->
            {
                State = state
                Doubled = Api.createMemo (fun _ -> state.Count.Value * 2)
            })
        (section (
            Attrs.className "demo-grid",
            Attrs.data ("testid", "demo-root"),
            Attrs.data ("parity", (fun demo -> Some (if demo.State.Count.Value % 2 = 0 then "even" else "odd")))
         ) {
            article (Attrs.className "panel counter-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "01 / Counter" }
                    span (Attrs.className "tag") { "signal → text" }
                }

                output (Attrs.className "count", Attrs.data ("testid", "count"), Attrs.aria ("live", "polite")) {
                    fun demo -> string demo.State.Count.Value
                }

                div (Attrs.className "derived") {
                    span () { "Doubled" }
                    output (Attrs.data ("testid", "doubled")) { fun demo -> string demo.Doubled.Value }
                }

                div (Attrs.className "actions") {
                    button (
                        Attrs.className "primary",
                        Attrs.data ("testid", "increment"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- demo.State.Count.Value + 1)
                    ) {
                        "Increment +1"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "reset"),
                        Attrs.attribute ("type", "button"),
                        Props.Button.disabled (fun demo -> demo.State.Count.Value = 0),
                        Events.onClick (fun demo _ -> demo.State.Count.Value <- 0)
                    ) {
                        "Reset"
                    }

                    button (
                        Attrs.className "secondary",
                        Attrs.data ("testid", "burst"),
                        Attrs.attribute ("type", "button"),
                        Events.onClick (fun demo _ ->
                            Api.batch (fun () ->
                                for _ in 1..100 do
                                    demo.State.Count.Value <- demo.State.Count.Value + 1))
                    ) {
                        "Burst +100"
                    }
                }

                p (Attrs.className "note") { "The output changes. The DOM nodes stay." }
            }

            article (Attrs.className "panel name-panel") {
                div (Attrs.className "panel-heading") {
                    span (Attrs.className "eyebrow") { "02 / Input" }
                    span (Attrs.className "tag") { "event → signal" }
                }

                label (Attrs.attribute ("for", "name-field")) { "Your name" }

                input (
                    Attrs.id "name-field",
                    Attrs.attribute ("type", "text"),
                    Attrs.attribute ("autocomplete", "off"),
                    Attrs.data ("testid", "name"),
                    Props.Input.value (fun demo -> demo.State.Name.Value),
                    Events.onInput (fun demo event -> demo.State.Name.Value <- (unbox<HTMLInputElement> event.currentTarget.Value).value)
                )

                p (Attrs.className "greeting", Attrs.data ("testid", "greeting"), Attrs.aria ("live", "polite")) {
                    fun demo -> $"Hello, {demo.State.Name.Value}"
                }

                p (Attrs.className "note") { "Type anywhere in the field. Focus and selection are preserved." }
            }
        })
```

It has 0 annotations, against 9 today. The template and setup come to 2,135 non-whitespace characters, against 2,436 for today's sugar (2,323 for the template plus 113 for its setup), and about 81 lines against 83. It shares layout, hash and server HTML with `demoExplicit`, and clicks, typing and writes produce identical traces, synchronous and queued.

### The keyed fixture (`tests/KeyedRowsFreePerf.fs`)

```fsharp
let private keyedList =
    Component.template (fun _ (rows: Signal<KeyedRow array>) -> rows) (
        table (Attrs.className "table") {
            tbody () {
                forEachFixed (fun (row: KeyedRow) -> row.Id) (fun (rows: Signal<_ array>) -> rows.Value) (
                    tr () {
                        td (Attrs.className "col-md-1") { fun (row: KeyedRow) -> string row.Id }
                        td (Attrs.className "col-md-4") { a (Events.onClick (fun _ _ -> clicks <- clicks + 1)) { from (fun row -> row.Label) } }

                        td (Attrs.className "col-md-1") {
                            a (Events.onClick (fun _ _ -> clicks <- clicks - 1)) {
                                span (Attrs.className "glyphicon glyphicon-remove", Attrs.attribute ("aria-hidden", "true")) { }
                            }
                        }

                        td (Attrs.className "col-md-6") { }
                    }
                )
            }
        }
    )
```

559 non-whitespace characters, against 637 for today's sugar. Markup and handler sites equal the production fixture after create, append, update, remove, create, swap and clear.

### The arm fixture, recommended form: free `matchWith` with hoisted `armOf` (`tests/KeyedRowsFreePerf.fs`)

```fsharp
let private plain = armOf (span (Attrs.className "a") { "A" })
let private counted = armOf (b () { fun payload -> string (payload ()) })

let private armList =
    Component.template (fun _ (rows: Signal<ArmRow array>) -> rows) (
        table () {
            tbody () {
                forEachFixed (fun row -> row.Id) (fun (rows: Signal<_ array>) -> rows.Value) (
                    tr () {
                        td () { fun row -> string row.Id }

                        td () {
                            matchWith (fun row -> row.Mode.Value) (function
                                | Plain -> arm plain ()
                                | Counted count -> arm counted count)
                        }
                    }
                )
            }
        }
    )
```

484 characters against 537, with 3 definitions.

### The arm fixture, `match!` (a) (`tests/KeyedRowsMatchPerf.fs`; not recommended)

```fsharp
let private armListRebuild =
    Component.template (fun _ (rows: Signal<ArmRow array>) -> rows) (
        table () {
            tbody () {
                forEachFixed (fun row -> row.Id) (fun (rows: Signal<_ array>) -> rows.Value) (
                    tr () {
                        td () { fun row -> string row.Id }

                        td () {
                            match! watch (fun (row: ArmRow) -> row.Mode.Value) with
                            | Plain -> span (Attrs.className "a") { "A" }
                            | Counted count -> b () { fun _ -> string count }
                        }
                    }
                )
            }
        }
    )
```

329 characters, with 1 definition.

## Recommendation

1. **Adopt free tag types** as the sugar's element form. Today's `t.Div` members can stay for one release or be dropped. Conditions:
   - **Generator output.** One generic class per tag, with a private primary constructor and a ParamArray secondary constructor (the Fable workaround), and names `base'` and `option'`.
   - **Entry points.** `Attrs`, `Props`, `Events` and `Hooks` as type functions over the existing builders; `from` statically resolved; free `forEach`, `forEachFixed`, `show`, `showElse` (not prototyped; same shape as `show`), `armOf`, `arm` and `matchWith`, each taking views.
   - **Root.** `Component.template setup view` (name open), or `Component.define setup (fun _ -> view)`.
   - **Documented inference rule.** A lambda gets its instance type from its own body only. Annotate a reader of a label that a later record also declares, or of a member of a class instance (`Signal`, `Memo`, classes). This costs 2 extra annotations in the keyed fixture and 1 in the arm fixture, and saves all 9 in the demo screen.
   - **Documented exclusion.** The free tags and `Generated.Html` are not opened in one file.
2. **Reject `match!` arms** (2.4).

The cost if (1) is wrong is the inference regression above, which users meet as FS0001, FS0193 or FS0041 pointing at the element instead of the lambda. Going back means regenerating `t.` members, which this probe leaves untouched.

## DRAFT spec amendment (view-sugar spec, `docs/superpowers/specs/2026-10-19-view-sugar-design.md`)

> **DRAFT, not approved.** Amended after the sugar v2 probe (2026-10-20, report `2026-10-20-sugar-v2-probe-report.md`).

- **Header amendments list.** Add: "Elements are free generic tag types in `namespace Ranvier.View.Foundation.Free` (§4.1); arms stay hoisted `armOf` definitions (§4.2); inline `match!` arms were measured and rejected (§4.2, §8 Q12)."
- **§3 A, §8 Q2 and Q3.** Change the syntax from `t.Div(parts...) { }` to `div (parts...) { }`, with PascalCase `t.` members replaced by lowercase free types. Q3's naming answer becomes: raw lowercase tag names, except `base'` and `option'`.
- **§4 opening.** `open Ranvier.View.Foundation.Free` brings in `Builders` and `Elements`. It must not be combined with `Ranvier.View.Foundation.Generated.Html` in one file. Opening Html after it shadows every tag and `Attrs`/`Props`/`Events` (FS0001/FS0193/FS0740).
- **§4.1.** Replace the `TemplateBuilder` extension members with:
  - the class form, `type div<'i> private (ps, primary: unit) = inherit Element<Dom.HTMLDivElement,'i>(Div, ps); new([<ParamArray>] ps) = div<'i>(ps, ())`, giving the Fable 5.18.0 reason for the private primary constructor;
  - void tags inheriting `VoidElement<'i>`, yielded through `Yield(VoidElement<'inst>)`;
  - `Attrs`, `Props`, `Events` and `Hooks` as type functions over the existing builders;
  - `from` statically resolved;
  - root `Component.template setup view`.

  Rewrite the inference paragraph:

  > No instance type flows down: not from `Component.define`, not from an enclosing element, not into parts, not into children. A lambda gets its instance type from its own body only. Write the annotation on a lambda that reads a label also declared by a later record, or a member of a class instance (`Signal`, `Memo`, classes).

  Replace the benchmark-row example with the free row from `FrameworkFreePerf.fs`, which has 0 annotations.
- **§4.2.** `armOf`, `forEach`, `forEachFixed`, `show` and `showElse` take views instead of builder functions. Replace the keyed and arm examples with the free fixtures above. Add a paragraph on rejected inline `match!` arms, with the 2.3 numbers and the (b) refutation.
- **§4.3.** Change the table rows to the free spellings. The lowering is unchanged.
- **§4.4.** Bundle: free tags with arms add 876 B gzip over explicit, 299 B over the `t.` sugar. Per tag used: one class plus two constructor helpers.
- **§4.5.** Add these rows:
  - "Free tags parse as `div (args) { }`": proved.
  - "The instance type flows down": refuted.
  - "A type argument on a tag fixes the instance": refuted, FS0041, because a ParamArray constructor is an overloaded candidate set.
  - "Fable compiles a ParamArray primary constructor": refuted on 5.18.0; workaround above.
- **§5 table.** Codes: FS0041 (input part on div, generated part, outer `text` in a row, Program view), FS0001 (another element's part), FS0708 (void children), FS0193 (outer part or reader in a row; root lambda resolving a label to another record).
- **§6.5.** Port `check-types.py`'s sugar cases to the free spelling. Add negative cases for `base` (FS0010) and for `option` shadowing (FS0001 on `string option`).
- **§8.** Add Q12: "Inline `match!` arms?" Recommended: no (2.4).

## Not tried

- `showElse`, `isolated`, `boundary`, `view`, `render` and `Document` entries in free form. Each is a direct port; none is compiled.
- Timing the keyed benchmark (`keyed-rows.mjs` keyed operations), `keyed-heap.mjs` and `run.mjs --template-comparison` with the free fixtures. Equality of runtime trees, counters and writes was checked instead; free `matchWith` arm timings overlap explicit.
- An allocation sample of a free-tag mount, to show no `Element` or `Builders_` frames. The mount allocation totals above match explicit to within 0.1 %, but I did not search the frames.
- The unbounded (a) cache for payload-dependent static text: derived from the code, not measured.
- Running `check-types.py` itself against the free spelling. Its cases were reproduced in `probe/FreeProbe` instead.
- `option'` as an actual rename: the probe keeps `option` to show the collision.
- Making `Generated.Html` `[<RequireQualifiedAccess>]` to remove the open-order collision.
- A reflection-free (b) that turns union fields into readers through a user-declared reader union.
- Fable `--noReflection`: not needed once the module-level `Dictionary` was removed.
- `fantomas` formatting of the probe files, which blocks a commit.
