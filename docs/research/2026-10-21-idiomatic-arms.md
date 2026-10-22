# Idiomatic arm construction: a plain F# `match` as an arm site

Research of 2026-10-21 on `research/idiomatic-arms`, branched from `0a7fe326`. F# 10 / FSharp.Core 10.1.401, Fable 5.18.0
(Release), Fable.AST 5.0.0, net10.0, Node 26.7.0 with jsdom 30.1.1, Vite 8.3.2 for bundles. The probes are in
[`experiments/arms-probes`](../../experiments/arms-probes) and their raw output is in
[`experiments/arms-probes/results`](../../experiments/arms-probes/results).

## Answers in brief

- **A plain `match` works as an arm site** when a compile-time rewrite owns the case bodies. The probe's
  `Arms.Match (read, function ...)` renders the same server HTML and arm hashes as hand-written `armOf`/`matchWith`
  twins on .NET and under Fable, keeps the arm mounted while the target stays the same, updates the payload in place,
  and hydrates the .NET server HTML under `Strict` with every server node adopted.
- **Under Fable the rewrite is a plugin** (`MemberDeclarationPluginAttribute`). With the attribute on `Arms.Match`
  itself, every call site is lowered and user code carries no attribute. Cost: **+139 B gzip** over the explicit
  fixture.
- **On .NET the rewrite is a quotation.** `[<ReflectedDefinition>]` on the `arms` parameter turns the same call site
  into an `Expr`; the same lowering runs on it once per site, about **9 ms per site** with
  `LeafExpressionConverter`.
- **Pattern variables are reads, not values.** A pattern variable inside a lambda over the instance becomes a read of
  the arm's own payload; a pattern variable anywhere else (static text, an argument to a build-time helper) is a
  compile error under Fable and a `BuildTimeRead` exception on .NET when the site is built.
- **Readers must read the arm's payload, not the source signal.** The first lowering re-read the source signal and
  ran every outgoing arm's readers against the next case's value: wrong-case field reads, reported errors on every
  switch and a slower switch. Reading the payload the region hands its arm removes all three.
- **Fable cannot evaluate quotations of view code.** Fable 5.18 compiles `<@ ... @>` and `[<ReflectedDefinition>]`
  arguments to `quotation.js` (25,119 B raw, 4,996 B gzip), but its `evaluate` runs only operators.
- **The library-only forms cost boilerplate or speed.** Active-pattern lenses work (+252 B gzip) but need one
  hand-written or generated active pattern per union. Rebuilding the case body on every change was measured in the
  sugar v2 probe: +34 % payload-update time and 11.8× allocation.

## How an arm site works today

- `matchWith read arms` creates one region. Each read of the region calls `arms (read instance)` and receives an
  `Arm`: a definition and a payload.
- **The definition is the key.** The same definition keeps the arm mounted and replaces the payload its props read;
  another definition runs the old arm's cleanups, removes its nodes and sets up the new arm.
- **The arm's instance is `unit -> 'payload`**, a reader of the payload the region last handed it. That reader
  changes only while the arm stays selected.
- **The template is built once per definition** and shared by every instance, so a reader receives the instance as
  its argument and a closure holds only what is common to all instances.
- **Server HTML** marks the region with the selected definition's structural hash, `<!--[a:646a1db6-->`. Hydration
  adopts the server arm when the client selects a definition with the same hash.

A plain `match` meets two of these rules by default and breaks two. Its case bodies are expressions, so the body for
a case is rebuilt on every evaluation (a new definition, a remount). Its pattern variables are values bound from the
current union value, so a body built once would show the first value it saw. The sugar v2 probe measured both
failures: strategy (a) rebuilt the body on every change, strategy (b) built it once and showed another instance's
payload ([report](2026-10-20-sugar-v2-probe-report.md), Question 2).

An idiomatic site therefore needs:

1. **One definition per match target**, built when the site is built.
2. **Pattern variables that read the arm's payload** wherever a reader runs.
3. **A rejection of pattern variables used as build-time values**, since a shared template has no instance at build
   time.

Plain F# typing fixes the type of a pattern variable to the field's type. A library alone can deliver 1 and 2 only by
changing what the pattern binds (an active pattern returning readers); delivering them for an ordinary union pattern
takes a rewrite of the case bodies after type checking.

## The lowering

Every probe form lowers to one runtime function, `Lowered.site cells read select bodies`
([`CaseArms.fs`](../../experiments/arms-probes/Probe/CaseArms.fs)):

- `select : 'u -> int * obj array` is the match itself with each case body replaced by its target index and its bound
  values (the pattern variables, then the matched value).
- `bodies : TemplateView<'inst> array` holds one case body per target, built once. Each becomes a definition whose
  setup returns the enclosing instance, so a body's readers keep the enclosing instance type and helper views of that
  type work unchanged.
- `cells` maps an instance to the bound values of its mounted arm: a `WeakMap` under Fable, a
  `ConditionalWeakTable` on .NET. A definition's setup stores its payload reader there.
- A pattern variable inside a reader `fun s -> ...` becomes `(Cells.values cells s).[k]`.

`matchWith` then keys the region by target: the same target keeps the arm and replaces its bound values; a guard or
nested pattern that selects another target remounts.

A plugin-lowered site in the Fable output (from `matchUnmarked`, whitespace trimmed):

```js
(armsCells_4 = new WeakMap(), Arms_Match(read, null, [armsCells_4,
  (_arg_1) => (_arg_1.tag === 1) ? [1, [_arg_1.fields[0], _arg_1]] : (_arg_1.tag === 2) ? [2, [_arg_1]] : [0, [_arg_1]],
  [ /* span "Not started" */,
    /* progress */ ... AttributeBuilder$1__attribute(Attrs(), "value", (_arg_2) => {
        const armsValues_4 = armsCells_4.get(_arg_2)();
        const percent_arm4 = armsValues_4[0];
        const _arg_1_arm4 = armsValues_4[1];
        return int32ToString(percent_arm4); }) ...,
    /* pre */ ... ]]))
```

### Stale-case readers

The first lowering re-derived pattern variables by calling `read` and the match inside each reader. Each reader then
subscribed to the source signal directly. When the value moved to another case, the outgoing arm's readers ran
against the new value before the region removed that arm:

| Fixture (jsdom, 9 steps) | Reports after the trace |
|---|---|
| explicit `armOf` twins | none |
| first lowering, pattern variables inlined by FCS (`match`) | `Cannot read properties of undefined (reading 'toString')` |
| first lowering, bound pattern variables (`matchRich`) | 6 × `Cannot read properties of null (reading '0')` |
| cell lowering (all fixtures) | none |

The final markup was correct in every run. The rough jsdom timing showed the cost: switching 500 arms 20 times took
241–296 ms with the first lowering against 158–218 ms for explicit arms; with the cell lowering it takes 145–168 ms
against 135–169 ms (rounds 2–5). Readers that read the arm's payload follow the explicit arms' rule: the region updates a payload
only while its arm stays selected.

## Options and probes

### 1. Library only

| Form | Authoring | Payload in the arm | Probe result |
|---|---|---|---|
| Hoisted `armOf` + `matchWith` (today) | `\| Loaded t -> arm loadedArm t`, with `loadedArm = armOf (pre () { fun t -> t () })` bound outside | the arm's `unit -> 'payload` | baseline |
| Rebuild per change (`match!`, strategy (a)) | `match! watch read with \| Loaded t -> pre () { fun _ -> t }` | closures of the latest rebuild | sugar v2 report: correct; +23 % mount, +34 % payload update, 11.8× allocation per update, +1.9 kB gzip; a nested `forEach` remounts on every update |
| Active-pattern lenses (probed) | `\| LoadCase.Loaded (text, count) -> pre () { text; fun s -> string (count s) }` | `'inst -> 'field` readers from `Case.Field` | same HTML, hashes, reuse and hydration as explicit; +252 B gzip; needs one active pattern per union |
| Union reflection | `Arms.ofUnion<Load>` tag readers | — | `FSharpValue.PreComputeUnionTagReader` is rejected by Fable 5.18 ("not supported by Fable"); `.tag` through `Emit` works; F# code cannot read a union's `Tag` (FS0039 direct, FS0001 through SRTP) |
| Quotations under Fable | `Arms.Match (read, <@ function ... @>)` | — | construction compiles; `evaluate` handles `ExprCall` only for operators, so view code throws `Unknown method` |
| Caller-info keyed arms | `\| Loaded t -> Arm.at (pre () {...}) t`, keyed by `[<CallerFilePath; CallerLineNumber>]` | the arm's reader | caller info works under Fable 5.18 (`Caps.fs:47`); the view still sees snapshot pattern variables, which the compiler accepts |
| CE custom operations / SRTP | — | — | a custom operation cannot bind pattern variables; `match!` binds values (sugar v2 report) |
| Lambda-per-case records | `{ Idle = ...; Loaded = fun text count -> ... }` | readers as lambda parameters | not probed: exhaustiveness rests on field names matching case names, checked at run time or by a generator |

The lens form is the strongest library-only form. The active pattern maps a `Case<'inst, 'u>` to `Choice` cases whose
fields are readers, so the snapshot spelling is a type error:

```fsharp
module LoadCase =
    let tag = function Idle -> 0 | Loading _ -> 1 | Loaded _ -> 2

    let (|Idle|Loading|Loaded|) (case: Case<'inst, Load>) =
        match case.Tag with
        | 0 -> Choice1Of3 ()
        | 1 -> Choice2Of3 (case.Field (function Load.Loading p -> p | _ -> 0))
        | _ ->
            Choice3Of3 (
                case.Field (function Load.Loaded (t, _) -> t | _ -> ""),
                case.Field (function Load.Loaded (_, n) -> n | _ -> 0)
            )

Lowered.cases read LoadCase.tag 3 (function
    | LoadCase.Idle -> span () { "Not started" }
    | LoadCase.Loading percent -> progress (Attrs.attribute ("value", fun s -> Some (string (percent s)))) { }
    | LoadCase.Loaded (text, count) -> pre () { text; " x"; fun s -> string (count s) })
```

Its limits: the active pattern is 15 lines per union; nested patterns and guards on payload fields are impossible
(the fields are readers), so `Loaded (_, 0)` must become a reader-level condition; a union over 7 cases exceeds
`Choice`.

### 2. Compile-time hooks

| Hook | Runs on | What it can do here | Status |
|---|---|---|---|
| Fable plugin (`MemberDeclarationPluginAttribute`) | Fable only | rewrite the Fable AST of a member (`Transform`) or of every call to a member (`TransformCall`); log errors with ranges | probed: the full lowering, 340 lines |
| `[<ReflectedDefinition>]` parameter | .NET (Fable compiles it but cannot evaluate it) | hand the call site's lambda to the library as an `Expr` | probed: the full lowering, about 150 lines |
| Myriad / source generation | .NET build | generate the lens active pattern per union, or `armOf` definitions | not probed; Myriad generates in an MSBuild target before `CoreCompile`, and a Fable compile is assumed to need the generated file on disk beforehand (unverified) |
| FSharp.Analyzers.SDK | IDE and `dotnet fsharp-analyzers` | report a pattern variable read outside a reader, and an `Arms.Match` whose second argument is not a function literal | not probed; diagnostics only |
| Type providers | both | — | ruled out: static parameters are literals, so a provider cannot receive the user's union type |
| F# compiler plugins | — | — | ruled out: FCS has no plugin API |

**The Fable plugin.** [`Plugin.fs`](../../experiments/arms-probes/Plugin/Plugin.fs) finds `Arms.Match (read, arms)`
by the callee's full name. `arms` must be a lambda; its body is a decision tree whose leaves are case bodies. A
condition that reads the matched value or a pattern variable, a binding of a payload part (`Get` of a union field,
tuple item, record field, option value or list head/tail) and every `DecisionTree` target are decision; anything else
is a leaf. The plugin then:

1. numbers the leaves and builds `select` from a copy of the tree whose leaves return their index and bound values;
2. rewrites each leaf: a lambda whose parameter has the instance type and whose body reads a pattern variable gets a
   prologue that reads the arm's values from `cells`, with each pattern variable renamed to a fresh local;
3. reports any pattern variable left outside such a lambda as an error at the variable's range;
4. replaces the call's arguments with `read`, `null` and the packed `(cells, select, bodies)`, so the case bodies are
   emitted once.

FCS hands Fable two shapes, both handled: a simple match inlines pattern variables as `_arg_1.fields[k]` with no
binding, and a match with guards or literal patterns becomes a `DecisionTree` whose targets bind the variables.

With `[<CaseArms>]` on `Arms.Match`, Fable calls `TransformCall` for every call of it, so `matchUnmarked` (no
attribute) lowers the same as `matchLoader` (attribute on the member).

**The .NET quotation.** [`QuotedArms.fs`](../../experiments/arms-probes/Probe/QuotedArms.fs) gives `Arms.Match` a
different signature per target:

```fsharp
#if FABLE_COMPILER
    [<Arms.Plugin.CaseArms>]
    static member Match(read: 'inst -> 'u, arms: 'u -> TemplateView<'inst>, ?lowered: obj) : TemplateView<'inst>
#else
    static member Match(read: 'inst -> 'u, [<ReflectedDefinition>] arms: Expr<'u -> TemplateView<'inst>>) : TemplateView<'inst>
#endif
```

The call site is the same text on both targets. On .NET the lowering walks the quotation with the same decision and
leaf rules (`IfThenElse` on a controlled variable, `Let` of a `PropertyGet`/`TupleGet`), substitutes
`unbox (values.[k])` for each pattern variable inside a lambda over `'inst`, and evaluates each body and the selector
with `LeafExpressionConverter.EvaluateQuotation`. SRTP calls inside a quoted body evaluate (an `inline` member-constraint
call evaluated to the expected value); object expressions are rejected at compile time (FS0449 "Quotations cannot contain object
expressions").

### 3. Hybrid

The probe's `Arms.Match` is the hybrid: plugin under Fable, quotation on .NET, one lowering contract. Parity, checked
for every fixture and state:

- **Server HTML**: the 54 `server|` lines of the .NET run equal the Fable run byte for byte
  ([`net.txt`](../../experiments/arms-probes/results/net.txt), [`fable-dom.txt`](../../experiments/arms-probes/results/fable-dom.txt)).
- **Hashes**: each arm hash of a lowered site equals its explicit twin: `220727aa` (span), `a8ebc344` (progress),
  `646a1db6` (pre), `c054a3d7` (empty), `7f75a908` (b), `789182d0` (rows), `fddcd338` (other); the root hash is
  `3dc663a9` for all.
- **Hydration**: each Fable build of `explicit`, `explicitRich`, `lens`, `match` and `matchRich` hydrates the .NET
  server HTML of `match` under `Strict`, adopts the server `<pre>`, reports nothing and updates in place.

## Failure modes

Checked by the probe fixtures unless marked otherwise. "Same as explicit" means the mounted trace (markup after each
of 9 writes, whether the arm's element survived, whether the first row survived) equals the hand-written twin.

| Pattern or use | Fable plugin | .NET quotation |
|---|---|---|
| Union cases with one and two payload fields | same as explicit | same HTML and hashes |
| Or-pattern `\| Idle \| Loading 0 ->` | one target; same as explicit | same HTML |
| Literal nested pattern `\| Loaded (_, 0) ->` | own target; same as explicit | same HTML |
| Guard `when count > 99` | own target; the guard runs in `select`, a change remounts; same as explicit | same HTML |
| Wildcard `\| _ ->` | one target | same HTML |
| Tuple scrutinee `(state.Value, state.Value = Idle)` | works (no twin) | same HTML as Fable |
| `forEach` inside a case body | rows kept across payload updates; same as explicit | same HTML |
| Pattern variable as static text `span () { text }` | compile error at the variable: "a pattern variable is read outside a reader…"; `dotnet fable` exits 1 | `BuildTimeRead "text"` when the site is built |
| Pattern variable passed to a build-time helper `list items` with `list : Item array -> View` | same compile error | same exception |
| Helper view of the enclosing instance type yielded in a body | works: arm instance is the enclosing instance (not probed separately) | works |
| Event handler reading a pattern variable `fun s _ -> save text` | rewritten like a reader; reads the arm's values at event time (not probed) | same |
| Pattern variable read inside a row of a nested `forEach` | error: the row's reader parameter has the row type (not probed) | `BuildTimeRead` |
| Object expression in a body | compiles | FS0449 at compile time |
| Mutable local captured by a body | compiles | rejected by the quotation compiler (not probed) |
| Instance of a primitive or value type | `WeakMap.set` throws for a JS primitive at mount (not probed) | `ConditionalWeakTable` keys a fresh box per call, so reads miss (not probed) |
| `Arms.Match` with a non-literal `arms` | compile error "takes a function literal" | `failwith` when the site is built |
| `Arms.Match` under Fable without the plugin | `failwith` when the site is built | — |
| The same instance shown by one site twice | both arms share one cell; correct while both show the same target (not probed) | same |

## Measurements

### Bundle

[`size.mjs`](../../experiments/arms-probes/Probe/size.mjs): each entry is one loader (three arms) mounted through
`Mounting.mountWith`, compiled by Fable with `--define ENTRY_<form>`, bundled by Vite with minification, and measured
with gzip level 9 and Brotli quality 11.

| Entry | raw | gzip | Brotli | Δ gzip vs explicit |
|---|---|---|---|---|
| explicit `armOf` + `matchWith` | 206,568 | 59,733 | 52,502 | — |
| active-pattern lenses | 207,346 | 59,985 | 52,680 | +252 |
| `Arms.Match`, plugin-lowered | 206,860 | 59,872 | 52,553 | **+139** |

The plugin entry's 139 B are `Lowered.site`, `Cells`, `Arms.Match` and the per-reader prologues. The quotation
runtime is absent from it. Fable's `quotation.js` alone is 25,119 B raw and 4,996 B gzip.

### .NET lowering

Lowering a five-target site (or-pattern, literal pattern, guard) from its quotation: 9.06 ms for the first site of the
process and 8.55 ms mean over 200 further sites ([`net.txt`](../../experiments/arms-probes/results/net.txt),
`lowering|`). A site lowers once per process, when its template is built. Nearly all of it is
`LeafExpressionConverter` compiling one LINQ expression per body and one for the selector.

### Rough DOM timing

jsdom, one Node process, 5 rounds, 500 loaders; milliseconds to mount, to write 20 rounds of payload updates and to
write 20 rounds of case switches ([`bench.txt`](../../experiments/arms-probes/results/bench.txt)). Rounds 2–5:

| Form | mount | payload ×20 | switch ×20 |
|---|---|---|---|
| explicit | 10.6–13.0 | 11.4–17.8 | 135–169 |
| lenses | 9.9–12.0 | 10.2–12.6 | 134–159 |
| `Arms.Match` | 10.7–16.3 | 10.6–12.7 | 145–168 |

The ranges overlap. This is jsdom, not a browser; the browser keyed-rows benchmark was not run.

### Compile time

Full `dotnet fable` compiles of the probe project took 19.6–25.7 s with and without the plugin; run-to-run spread is
larger than any plugin cost. `Plugin.fs` does one walk per member and one per `Arms.Match` call.

## Comparison

| | Hoisted `armOf` | Rebuild (a) | Lenses | Plugin + quotation |
|---|---|---|---|---|
| Authoring | two places per arm | a plain `match!` | a plain `match` over generated patterns | a plain `match` |
| Pattern variables in a reader | `fun p -> p ()` | snapshot of the latest rebuild | `fun s -> count s` | `fun _ -> count` |
| Snapshot misuse | — | accepted, remounts per value | type error | compile error (Fable) / exception at build (.NET) |
| Nested patterns, guards | in the arms function | yes | no | yes |
| Reuse while the target holds | yes | yes, by shape | yes | yes |
| Nested control flow in a body | yes | remounts every update | yes | yes |
| SSR / hydration parity | yes | yes (sugar v2 report) | yes | yes |
| Bundle Δ gzip | 0 | +2,468 over explicit (sugar v2 report) | +252 | +139 |
| Runtime | baseline | +34 % payload update | overlaps explicit (jsdom) | overlaps explicit (jsdom) |
| .NET start-up | 0 | 0 | 0 | ~9 ms per site |
| Extra machinery | — | — | one active pattern per union | a Fable plugin package; quotations on .NET |
| IDE feedback | full | full | full | type errors only; build-time-read rule on Fable compile or at .NET run time |

## Recommendation

**Primary, in the standard library: keep `armOf` + `matchWith`, `branch`, `show` and `showElse` unchanged.** They cost
nothing, need no tooling and remain the documented form.

**Opt-in tier: `Arms.Match` with a Fable plugin package.** It gives the plain `match` with reactive payloads, keys by
match target, matches the explicit form's HTML, hashes and hydration, and costs 139 B gzip under Fable and about 9 ms
per site at .NET start-up. Readers in a case body read the payload the region hands their arm, never the source
signal.

Do not ship the lens form, the rebuild form or Fable quotations: the lens form needs per-union boilerplate for less
than the plugin gives, the rebuild form was rejected on speed and allocation in the sugar v2 probe, and Fable cannot
evaluate quoted views.

### API sketch

```fsharp
open Ranvier.View.Foundation.Sugar
open Ranvier.View.Arms            // opt-in package; references Ranvier.View.Arms.FablePlugin

type Load =
    | Idle
    | Loading of percent: int
    | Loaded of items: Item array

let loader =
    Component.template (fun _ (state: Signal<Load>) -> state) (
        div (Attrs.className "loader") {
            Arms.Match (
                (fun state -> state.Value),
                function
                | Idle -> spinner
                | Loading percent -> progress (Attrs.attribute ("value", fun _ -> Some (string percent))) { }
                | Loaded items when items.Length = 0 -> p () { "Nothing yet" }
                | Loaded items -> ul () { forEach _.Id (fun _ -> items) itemRow }
            )
        }
    )
```

- `items` inside `fun _ -> items` reads the mounted arm's payload; `Loaded` → `Loaded` keeps the `<ul>` and its rows.
- `| Loaded items -> list items` with `list : Item array -> TemplateView<_>` is rejected; the helper takes a reader,
  `list : ('inst -> Item array) -> TemplateView<'inst>`, and the call is `list (fun _ -> items)`.
- The guard is part of the selection: `items.Length` going from 0 to 3 remounts from the `p` arm to the `ul` arm.

### What implementation takes

1. **Runtime (`Ranvier.View.Arms`, both targets).** `Lowered.site` and `Cells` from the probe, moved next to
   `matchWith` so that `Cells.set` happens in the arm definition's setup without a public `Component.define` call. The
   region and hashing code stay as they are.
2. **Fable plugin (`Ranvier.View.Arms.FablePlugin`, netstandard2.0, Fable.AST 5.0.0).** The probe's `Shape` walker,
   the leaf rules and `TransformCall` on `Arms.Match`. Remaining work:
   - the full `Expr` shape set (`TryCatch`, `Extended`, `LetRec` bodies in leaves, `Delegate` readers over the
     instance);
   - error ranges for FCS-inlined pattern variables, which today name "a pattern variable" because FCS removed the
     binding;
   - source-map ranges on the inserted prologue;
   - plugin tests that compile fixture files with Fable and compare emitted JavaScript.
3. **.NET quotation lowering.** The probe's `Quoted.lower`. Options for the 9 ms per site: keep it (start-up only),
   compile all bodies of a site in one LINQ lambda, or interpret the body quotation instead of compiling it.
4. **Fallback under Fable without the plugin.** Fail at compile time rather than at site build: the package's
   `.targets` adds the plugin reference, and an analyzer rule reports `Arms.Match` in a project without it.
5. **Analyzer (FSharp.Analyzers.SDK, optional).** The build-time-read rule and the function-literal rule on the
   typed tree, so the IDE shows them on both targets.
6. **Checks.** Port the probe's twins to the playground suites: synchronous and queued commits, core counters,
   listener registrations, MutationObserver writes, Fable Debug ("a template definition was created while a region was
   read" must stay silent: definitions are created when the site is built), and the keyed-rows arm benchmark in
   Chromium against `armsCreate1k`, `armsSwitch1k` and `armsPayload1k`.
7. **Docs.** A section in `docs/content/view/arms.md` for the opt-in form, with the reader rule and the
   rejected-spelling error.

## Not measured

- Chromium timings and heap profiles of the lowered sites; the jsdom figures above are the only timings.
- Fable Debug builds of the probe.
- Myriad and FSharp.Analyzers.SDK.
- Event handlers, struct instances, rows reading pattern variables and one instance shown by one site twice, listed
  in the failure-mode table as not probed.

## Reproduce

```sh
cd experiments/arms-probes/Plugin && dotnet build -c Release
cd ../Probe && dotnet build -c Release && dotnet bin/Release/net10.0/Arms.Probe.dll > net.txt
dotnet fable Arms.Probe.fsproj --outDir out -c Release
grep 'server|match|Loaded ("done", 3)' net.txt | cut -d'|' -f5 > served.html
node run-dom.mjs              # server HTML, mounted traces, Strict hydration of served.html
node run-dom.mjs --bench      # rough jsdom timing
for e in explicit lens plugin; do dotnet fable Arms.Probe.fsproj --outDir out-$e -c Release --define ENTRY_${e^^}; done
node size.mjs                 # needs npm ci in examples/Fable.Ranvier.Playground
dotnet fable Arms.Probe.fsproj --outDir out-bad -c Release --define PROBE_BAD   # exits 1 with the plugin error
cd ../Caps && dotnet run -c Release && dotnet fable Caps.fsproj --outDir out -c Release --define CAPS_QUOTE
```
