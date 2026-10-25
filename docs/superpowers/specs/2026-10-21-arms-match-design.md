# Arms.Match design: a plain F# `match` as an arm site, opt-in

> **Status 2026-10-21:** approved, with the eight open questions decided as recommended (Decisions 11-18). Written
> against master at `bcdb561`; citations refreshed against master at `0ba119a` (PR #140, motion parts and client
> element refusals). Inputs: the research note
> [`docs/research/2026-10-21-idiomatic-arms.md`](../../research/2026-10-21-idiomatic-arms.md) and its probes in
> [`experiments/arms-probes`](../../../experiments/arms-probes), plus one probe added for this spec
> (`experiments/arms-probes/Twice`, result in `results/twice.txt`). The implementation plan is
> [`docs/superpowers/plans/2026-10-21-arms-match.md`](../plans/2026-10-21-arms-match.md).

Date: 2026-10-21. Toolchain facts: Fickl 0.3.0 (`.config/dotnet-tools.json` since #152, `dotnet fickl`; it reports
Fable 5.22.0), whose tool runs on net10.0 and loads `Fable.AST` 5.1.0 and FSharp.Core 10.1.203 (assembly version
10.1.0.0) from its own folder, as Fable 5.18.0, the probe's compiler, did; the plugin compiled against `Fable.AST`
5.0.0 and FSharp.Core 10.1.203 loads into both.

## Core and view state

Required by `.claude/rules/view-uses-core.md`.

**Core primitives used:**

- The arm memo behind `matchWith` (`Template.fs:2721-2734`): `Arms.Match` lowers to `matchWith` over one definition
  per match target. Selection, the owner per selected arm, and remount-versus-update stay with the existing region; the
  key is the definition (`Template.fs:2731`, `KeyedItems.pairKey`).
- `Owner` cleanups through `Setup.OnCleanup` (`Representation.fs:15-17`): an arm withdraws its payload reader from the
  site's cells when its owner is disposed.
- `Graph.TryCurrent` (`src/Ranvier/Core.fs:1741`): a reader picks the payload readers registered under its own graph,
  so concurrent server renders (one request graph each, `Server.fs:136`, `Server.fs:217`) stay apart.

**View-owned state (new):** under Fable, the site's definitions array, one `armOf` per target, built once per evaluation
of the call: a template table, holding no lookup state (Phased implementation 8). A site that passes a view to a helper
over the concrete instance type also holds the `Cells` lookup below, as a `WeakMap` from the site's tag and the instance to
that helper's mounts per target (`Site.spliceHeld`, plan Decision P8, an exception to `.claude/rules/view-uses-core.md` accepted by the user on 2026-10-25); other sites carry none of it in their bundle. On
.NET, one table per site, `Cells`, a
`ConditionalWeakTable` from an instance to the entries of that instance's mounted arms, per target. It is a lookup, not a selection cache: the
payload itself stays in the arm memo; an entry holds the arm's props accessor, which the region already hands the arm
(`CaseArms.fs:61-64`), its graph and its last values, seeded at mount (plan Rulings 28, 32 and 37). The core cannot hold it because a reader receives only the
instance (`TemplateView<'inst>` readers are `'inst -> 'v`), and the core has no owner-scoped lookup from which a reader
could find its arm. Decision 13 keeps the lookup in the view.

## Goal

An author writes an arm site as an ordinary F# `match` over a union, with pattern variables, nested patterns, guards
and or-patterns, and gets the arms, layouts, structural hashes, server HTML and hydration of the hand-written
`armOf` + `matchWith` form, on .NET and under Fable, from one source text:

```fsharp
open Ranvier.View.Foundation.Sugar
open Ranvier.View.Arms

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

Inside a reader, a pattern variable is the mounted arm's current payload; `Loaded` to `Loaded` keeps the `<ul>` and
its rows and updates them.

## Non-goals

- **Changing the standard set.** `armOf`, `arm`, `matchWith`, `branch`, `show` and `showElse`
  (`TemplateSugar.fs:101-171`) keep their signatures, behaviour, docs and bundle bytes. `Arms.Match` is built on their
  public surface.
- **Pattern variables as build-time values.** A case body is built once per target and shared by every instance;
  a pattern variable has a value only per instance, inside a reader.
- **Snapshot semantics.** The rebuild-per-change form (sugar v2 strategy (a)) is not offered.
- **The lens form, Fable quotations and Myriad generation.** Rejected by the research (Recommendation).
- **NativeAOT and trimmed .NET apps on the quotation path.** The .NET path is reflection-based; such apps use
  `matchWith` (decision 9).
- **Keyed remount by value** (`branch` semantics). An equal target keeps its arm; `branch` stays the tool for that.
- **IDE squiggles for the payload rule in this spec.** The analyzer is a follow-up (IDE experience).

## Decisions

1. **One call shape on both targets: `Arms.Match (read, function ...)`.** A static member with a tupled call, because
   `[<ReflectedDefinition>]` applies only to method parameters. *The same source compiles on .NET and under Fable.*
2. **Under Fable a compiler plugin rewrites every call.** `CaseArmsAttribute` (a `MemberDeclarationPluginAttribute`)
   sits on `Arms.Match` itself, so Fable runs its `TransformCall` on each call site (`Plugin.fs:338-340`); user code
   carries no attribute (probe fixture `matchUnmarked`, `Fixtures.fs:184-200`).
3. **On .NET the argument is a quotation.** The .NET overload takes `[<ReflectedDefinition>] arms: Expr<...>`
   (`QuotedArms.fs:178`); the lowering runs on it when the call is evaluated.
4. **One lowering contract, two producers.** Both produce `select : 'u -> int * obj array` (target index and bound
   values) and `bodies : TemplateView<'inst> array` (one per target, built once), consumed by one runtime function,
   `Site.create` (the probe's `Lowered.site`, `CaseArms.fs:56-69`). *Parity rests on one runtime path.*
   **Amended by #157** (plan `2026-10-24-issue-157.md`, Decision P1): two runtime shapes behind one parity contract.
   Under Fable the plugin emits a selector returning `[target, site tag, bound values..., matched value, instance]` and
   bodies over the arm's values thunk, consumed by `Site.direct`; on .NET the quotation path, `Site.create` and the
   cells stay. Parity is observable output: server HTML and structural hashes on both targets (`ArmsServerChecks.fs`,
   `arms-server.test.mjs`), Strict hydration of .NET HTML by the Fable client (`arms-hydration.test.mjs`), and the
   diagnostic texts and ranges.
5. **The key is the match target.** Each target owns one arm definition; the same target keeps its arm and replaces its
   bound values, another target remounts. A guard, literal or nested pattern that routes to another target remounts.
6. **Readers read their own arm's payload.** A pattern variable inside a lambda over the instance type becomes a read
   of the bound values that the region last handed that reader's arm, through the site's cells. The source signal is
   never re-read from a body. *The first probe lowering re-read it and produced wrong-case reads on every switch
   (research, Stale-case readers).*
7. **Cells hold a list of live arms per instance and target**, each tagged with its graph; an arm's cleanup withdraws
   its entry. *The probe's single slot per instance shows a stale payload once a second mount of the same instance is
   disposed (`results/twice.txt`, line `afterDisposeB`); the list removes that failure.*
8. **The runtime lives in its own project and uses only public view API** (`Component.define`, `matchWith`, `arm`,
   `Setup.OnCleanup`). *An app that does not reference the package gets 0 B and 0 code-path changes.*
9. **The .NET overload is annotated `RequiresUnreferencedCode` and `RequiresDynamicCode`.** *FSharp.Core's quotation
   deserializer and `LeafExpressionConverter` carry no trim annotations (checked by reflection on FSharp.Core
   10.1/11.0: no attributes on `EvaluateQuotation`), so a trimmed app would fail at run time with no warning unless
   `Arms.Match` itself warns.*
10. **Rejections are errors with fixed texts**, compile-time under Fable (plugin `LogError` at the variable's range),
    site-build-time on .NET (an exception from the `Arms.Match` call). Both use the same sentence; .NET prefixes the
    caller's file and line.
11. **An unsupported tree shape in a decision position is an error, AM007.** A leaf whose root, under a type cast,
    sequence, `try` or a `let` over an unrelated value, is an `if`, `match` or `let` over the matched value raises
    "Arms.Match: this pattern form is not supported." at that node's range, so a gap in the walker fails loudly instead
    of as AM001.
12. **`if` and `match` at the top of a case body are part of the selection.** Each branch is a target, with the remount
    rule of guards; the docs state it.
13. **The core gains no owner-scoped lookup.** The per-target lists meet guarantee 3, and readers also run in event
    handlers and server renders, where the owner chain is not the arm's. A second view feature that needs owner context
    reopens it.
14. **The .NET path caches no lowering.** A call-site cache would serve the first call's captured locals to later calls,
    and module-level sites lower once already. The one-expression lowering (≤ 4 ms mean) and the binding rule in the
    docs ship instead.
15. **No NativeAOT-compatible .NET path.** It would need a build-time lowering, the machinery the plugin avoids. Trimmed
    and NativeAOT apps use `matchWith`, and the annotation says so at publish. A build-time lowering is also the
    follow-on that removes the remaining cold cost: emitting each site as `armOf`/`matchWith` code before compilation
    (a source generator over the typed tree, as the analyzer of the follow-up spec reads it) drops the quotation
    deserialiser, `LeafExpressionConverter` and the interpreter from the .NET path. Estimate: the first site falls to
    the view's own cold start (29-32 ms, 8-11 ms of it for a twin site after R2R), each lowering to 0 ms, and the
    per-call lambdas are ordinary compiled code; about two weeks of work, most of it the typed-tree walk the plugin
    already does on the Fable AST.
16. **One package at graduation.** `Ranvier.View.Arms` carries the plugin assembly and an auto-imported `.targets`, so
    an app adds one reference and always gets the runtime with the plugin. The package declares `Fable.AST` as a NuGet
    dependency, so NuGet unifies it with the `Fable.AST` of other plugin packages (a .NET build carries an unused
    compile reference), and ships only `Ranvier.View.Arms.FablePlugin.dll` under `fable-plugin/`, from an explicit file
    list: the `FSharp.Core.dll` the plugin build copies stays out. The experiment references the copied
    `Fable.AST.dll` directly until the package exists; the pack work belongs to graduation. Fable needs the runtime's F#
    sources in the package (`fable/` with `Diagnostics.fs`, `Errors.fs`, `Cells.fs`, `Site.fs`, `Quoted.fs`, `Arms.fs`
    and the project file). The `Fable.AST` dependency is declared with `ExcludeAssets="runtime"` (nuspec
    `exclude="Runtime"`), so a .NET app's output holds no `Fable.AST.dll`. The packaged `.targets` sets
    `RanvierArmsPluginDir` to `$(MSBuildThisFileDirectory)../fable-plugin/` and drops its `Fable.AST.dll` reference,
    which the dependency supplies.
17. **AM005 is a compile-time error at the call.** The `.targets` defines `RANVIER_ARMS_PLUGIN` under Fable; without it,
    Fable compiles an `Arms.Match` marked `[<Obsolete(..., true)>]`, so a project that references `Ranvier.View.Arms`
    without importing the `.targets` fails with FS0101 "This construct is deprecated. Arms.Match needs the
    Ranvier.View.Arms Fable plugin; ..." at each call. A project that defines the symbol but whose compilation lacks
    the plugin assembly fails with FS0039 naming `FablePlugin` in `Arms.fs`. The runtime holds no AM005 check: a site
    whose plugin does not run fails at site build with a JavaScript `TypeError`. The follow-up analyzer reports AM005 in
    the IDE.
18. **The instance-type rule rejects `string`.** A JavaScript string is a primitive that `WeakMap` rejects, so accepting
    it on .NET alone would break "builds on one, builds on the other".

## Package layout

| Project | Target | Contents |
|---|---|---|
| `experiments/Ranvier.View.Arms/Ranvier.View.Arms.fsproj` | net10.0;net8.0, Fable sources | `Cells.fs`, `Site.fs`, `Quoted.fs` (.NET only), `Arms.fs`, `Diagnostics.fs`, `Errors.fs`; references `src/Ranvier.View` |
| `experiments/Ranvier.View.Arms.FablePlugin/Ranvier.View.Arms.FablePlugin.fsproj` | netstandard2.0 | `Shape.fs`, `Lower.fs`, `Plugin.fs`: the probe's `Plugin.fs` split by role; `[<assembly: ScanForPlugins>]` |
| `experiments/Ranvier.View.Arms/Ranvier.View.Arms.targets` | MSBuild | when `'$(FABLE_COMPILER)' == 'true'`: defines `RANVIER_ARMS_PLUGIN`, adds the plugin assembly and `Fable.AST.dll` (the base type of the plugin attribute) as `Reference`s, builds the plugin in Release when its assembly is missing from the default folder, and fails the build when it is still missing; `RanvierArmsPluginDir` overrides its folder |
| `experiments/Ranvier.View.Arms/Checks/` | both | fixtures, explicit twins, server checks, the golden server HTML |

Versions:

| Package | Version | Reason |
|---|---|---|
| `Fable.AST` (plugin, compile reference) | 5.0.0 | the lowest 5.x; Fickl 0.3.0 and Fable 5.18 load their own 5.1.0, verified |
| `FSharp.Core` (plugin) | 10.1.203 | the version inside the Fickl 0.3.0 (and Fable 5.18) tool folder; the plugin runs in that process |
| `FSharp.Core` (library) | 8.0.100 floor (`FSharpCoreImplicitPackageVersion`), targets net10.0;net8.0 | the view projects' floor since #147 (`Ranvier.View.Portable.fsproj:2-6`) |
| `FableMinimumVersion` | `"5.0"` | the probe declared `"4.0"` (`Plugin.fs:331`); 4.x is untested |

**Referencing.** The view is unpackaged (`Ranvier.View.Portable.fsproj:5`, `IsPackable=false`), so an app opts in with
two lines:

```xml
<ProjectReference Include="../../experiments/Ranvier.View.Arms/Ranvier.View.Arms.fsproj" />
<Import Project="../../experiments/Ranvier.View.Arms/Ranvier.View.Arms.targets" />
```

Fable discovers plugins among the compilation's assembly references, and it compiles `Ranvier.View.Arms` from source
inside the app's compilation with the app's references and defines; the `.targets` adds the plugin assembly,
`Fable.AST.dll` and the `RANVIER_ARMS_PLUGIN` define.
`dotnet fickl` evaluates the project without building it; the `.targets` builds the plugin in Release during that
evaluation when its assembly is missing (plan Ruling 37).
Fable sets the `FABLE_COMPILER` MSBuild property during project cracking (verified in phase 1), so .NET builds carry
no plugin assembly. When the view graduates to a package, one `Ranvier.View.Arms` package ships the plugin assembly
under `fable-plugin/` with a NuGet dependency on `Fable.AST`, and the `.targets` under both `build/` and
`buildTransitive/`, so an app that references a component library built on `Ranvier.View.Arms` also receives the define
and the plugin reference; the project passed to `dotnet fickl` is the one whose imports count (decision 16).

## Public API

```fsharp
namespace Ranvier.View.Arms

open System.Diagnostics.CodeAnalysis
open Ranvier.View.Foundation.Template

/// <summary>Arm sites written as a plain F# match: one arm per match target.</summary>
[<AbstractClass; Sealed>]
type Arms =
#if FABLE_COMPILER && RANVIER_ARMS_PLUGIN
    [<Ranvier.View.Arms.FablePlugin.CaseArms>]
    static member Match:
        read: ('inst -> 'u) *
        arms: ('u -> TemplateView<'inst>) *
        ?lowered: obj
            -> TemplateView<'inst>
#else
#if FABLE_COMPILER
    [<Obsolete(Diagnostics.noPlugin, true)>]
    static member Match: read: ('inst -> 'u) * arms: ('u -> TemplateView<'inst>) -> TemplateView<'inst>
#else
    [<RequiresUnreferencedCode(Diagnostics.reflection); RequiresDynamicCode(Diagnostics.reflection)>]
    static member Match:
        read: ('inst -> 'u) *
        [<ReflectedDefinition>] arms: Expr<'u -> TemplateView<'inst>> *
        [<CallerFilePath; Optional>] ?file: string *
        [<CallerLineNumber; Optional>] ?line: int
            -> TemplateView<'inst>
#endif
#endif
    /// <summary>Warms the .NET lowering on a built-in site; does nothing under Fable.</summary>
    static member Prepare: unit -> unit

// .NET only: shared code catches InvalidOperationException, which both types inherit and Fable raises.
#if !FABLE_COMPILER
/// <summary>A pattern variable read outside a reader of the instance, raised when a .NET site is built.</summary>
type BuildTimeReadException =
    inherit System.InvalidOperationException
    /// <summary>The pattern variable, or the union field when the compiler removed the binding.</summary>
    member Variable: string

/// <summary>A site rejected by the .NET lowering: AM003, AM004, AM006 or AM007.</summary>
type ArmsMatchException =
    inherit System.InvalidOperationException
#endif
```

`?lowered` is filled by the plugin with a function from the cells reader to `(select, bodies)` (plan, Ruling 4); a call
that reaches the runtime without it fails at site build (Decision 17). Two internal modules complete the runtime:

```fsharp
module internal Cells =
    type Cells
    val create: unit -> Cells
    /// Registers the arm's payload reader for (instance, target) and withdraws it on the arm's cleanup.
    val enter: cells: Cells -> setup: Setup -> instance: obj -> target: int -> values: (unit -> obj array) -> unit
    /// The bound values of the reader's own arm: the pattern variables, then the matched value.
    val values: cells: Cells -> instance: obj -> target: int -> obj array

module Site =
    /// One arm per body, built when the site is built; called by Arms.Match on both targets.
    val create:
        cells: Cells.Cells -> read: ('inst -> 'u) -> select: ('u -> int * obj array) -> bodies: TemplateView<'inst> array
            -> TemplateView<'inst>
```

`Site.create` defines body `k` as `Component.define (fun setup current -> Cells.enter cells setup (fst (current ())) k
(fun () -> snd (current ())); fst (current ())) (fun _ -> bodies[k])` and returns
`matchWith id (fun instance -> let k, values = select (read instance) in arm definitions[k] (instance, values))`, the
probe's shape (`CaseArms.fs:56-69`) with the target passed to `enter`. A body's instance is the enclosing instance, so
helper views typed `TemplateView<'inst>` work unchanged inside a body.

`Cells.values cells s k` reads every live entry of `(s, k)` registered under the ambient graph (all entries when no
graph is ambient), which subscribes the reader to each of those arms' payloads, and returns the first entry's values.
With no live entry it returns the last values of a withdrawn entry of `(s, k)` (under Fable the first in mount order serving the graph; on .NET the last withdrawn), so a reader that runs while
its arm's nodes are held by an exit (`Departures.fs`) still reads its own case. Entries of one site, instance and target
belong to arms of the same case of the same instance, so they hold equal values once a flush settles. The .NET table
changes an instance's entry lists under its lock and reads them without it (plan Ruling 28).

## The lowering

### Decision and leaf

The `arms` lambda's body is split into a **decision part**, which becomes `select`, and **leaves**, which become
targets. Walking from the top:

- a `DecisionTree` (Fable) is decision; each of its targets is walked with its bound identifiers added;
- an `if` whose condition refers to the matched value or a bound identifier is decision;
- a `let` whose value refers to the matched value or a bound identifier is decision, and its identifier becomes a
  bound identifier;
- anything else is a leaf.

The probe accepted only payload extractions (`Get` of a union field, tuple item, record field, option value, list
head or tail) as decision bindings (`Plugin.fs:126-131`, `QuotedArms.fs:24-30`). The spec widens the rule to any
value that refers to a controlled identifier, so active-pattern results and user `let`s over pattern variables are part
of the selection. Both producers apply the same rule; phase 2 checks that FCS hands both the same tree for each fixture.

A leaf that is a throw (`raise`, `failwith`, the compiler's `MatchFailureException` for an incomplete match) builds no
definition: `select` raises it when that target is selected, so an incomplete match fails at the region read, as a
plain `match` does, and site build succeeds.

### Pattern forms

| Form | Example | Targets and keys | Status |
|---|---|---|---|
| Union case without payload | `\| Idle ->` | one target | probed |
| Union case with payload | `\| Loading percent ->`, `\| Loaded (text, count) ->` | one target; fields are bound values | probed |
| Or-pattern | `\| Idle \| Loading 0 ->` | one target under Fable; the .NET quotation may hold the body once per alternative, each with the same hash and HTML | probed (same HTML) |
| Literal nested pattern | `\| Loaded (_, 0) ->` | its own target | probed |
| Guard | `\| Loaded (t, n) when n > 99 ->` | its own target; the guard runs in `select`, so a guard change remounts | probed |
| Wildcard | `\| _ ->` | one target | probed |
| Tuple scrutinee | `(s.Value, s.Value = Idle)` with `\| Loaded (t, _), _ ->` | one target per row | probed (no twin) |
| Record, list, option patterns | `\| { Name = n } ->`, `\| [x] ->`, `\| Some v ->` | one target per row; extracted parts are bound values | fixtures (phase 2) |
| Array pattern | `\| [\| x \|] ->` | FS0452 "Quotations cannot contain inline assembly code or pattern matching on arrays" in the IDE and on .NET; a Fable-only build accepts it, which breaks "builds on one, builds on the other"; use a list pattern or a guard | unsupported (phase 2) |
| `as` pattern | `\| Loaded _ as whole ->` | one target; `whole` is a bound value | spec; fixture in phase 2 |
| Type test | `\| :? Item as item ->` (scrutinee `obj`) | one target; `item` is a bound value | spec; fixture in phase 2 |
| Active pattern, total or partial | `\| Even -> \| Odd ->`, `\| Parse n ->` | the active pattern runs in `select`; one target per row | spec; fixtures in phase 2 |
| `if` or `match` on a pattern variable at the top of a body | `\| Loaded (_, n) -> if n > 5 then a else b` | each branch is a target | spec; fixture in phase 2 |
| `let` over a pattern variable at the top of a body | `\| Loaded (t, _) -> let label = t + "!" in ...` | `label` is a bound value, computed per region read | spec; fixture in phase 2 |
| Nested `Arms.Match` in a body | inner site reading an outer pattern variable in a reader | inner and outer sites each own their cells | spec; fixture in phase 2 |
| Incomplete match | missing case, FS0025 warning | `select` raises `MatchFailureException` for unmatched values | spec |
| Object expression in a body | `{ new IDisposable with ... }` | FS0449 "Quotations cannot contain object expressions" in the IDE and on .NET; a Fable-only build accepts it | probed; unsupported |
| Assignment in a reader | `fun (e: Editor) _ -> e.Saved.Value <- text` | the .NET lowering follows each assignment with `()`, since `LeafExpressionConverter` types an assignment by its value | fixture (phase 2) |
| `Arms.Match` with a non-literal second argument | `Arms.Match (read, arms)` | AM003 | probed |

### Readers

In a leaf, every lambda whose first parameter has the instance type and whose body refers to a bound identifier gets a
prologue `let values = Cells.values cells s k` and reads bound identifier `i` as `unbox values[i]`
(`Plugin.fs:215-242`, `QuotedArms.fs:114-135`). This covers text and attribute readers, `Hooks` callbacks, and event
handlers `fun s e -> ...` (a curried lambda over the instance). Under Fable a `Delegate` whose first argument has the
instance type is rewritten the same way (the probe handled `Lambda` only). An event handler reads the bound values at
event time, which are the arm's current payload.

Each bound identifier keeps its type: `percent` in `fun _ -> string percent` is an `int`. Value-type payloads are boxed
into the bound-values array and unboxed in the prologue, on both targets.

## The payload rule

**Guarantees.**

1. A reader in a case body reads the bound values of its own arm, as the region last handed them to that arm. It never
   reads the values of another target, so a field read always meets the case its pattern matched.
2. A reader's bound values change only while its arm stays selected; a switch to another target disposes the arm before
   the new arm's setup (`TemplateSugar.fs:109` remarks), and the outgoing arm's readers keep their own case's values.
3. Two arms of one site showing one instance (the same component rendered twice with the same props, as in
   `results/twice.txt`) each read live values, through the disposal of either.
4. Case bodies, the selector and the per-target definitions are built once per evaluation of the `Arms.Match` call.
   A Fable Debug build stays silent about "a template definition was created while a region was read"
   (`Template.fs:2776-2780`), because the definitions are created when the site is built.

**Enforcement.** A bound identifier left outside every reader is a build-time read. The plugin replaces each reader
over the instance by a placeholder and searches the rest of the leaf (`Plugin.fs:245-252`); the .NET lowering looks
for free bound variables after substitution (`QuotedArms.fs:142-147`).

| Code | Raised | Fable (compile error at the range) | .NET (exception from the `Arms.Match` call) |
|---|---|---|---|
| AM001 | pattern variable outside a reader, e.g. `span () { text }`, `list items` | `Arms.Match: 'text' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> text).` | `BuildTimeReadException`, message `<file>(<line>): ` + the same sentence |
| AM001b | the same, when FCS inlined the variable as `matched.fields[k]` | `Arms.Match: the field 'text' of 'Loaded' is read outside a reader of the instance. Read it inside a lambda over the instance, such as (fun _ -> text).` | as AM001; the quotation keeps the binding, so .NET names the variable |
| AM002 | pattern variable inside a lambda over another type, e.g. a `forEach` row reader | `Arms.Match: 'text' is read inside a lambda over 'unit -> string', which is not the instance type 'Signal<Load>'. Pass the value to the nested view through its items or props.` | `BuildTimeReadException` with the same sentence |
| AM003 | second argument not a function literal | `Arms.Match takes a function literal as its second argument, such as (function \| Case x -> ...).` | `ArmsMatchException`, same sentence |
| AM004 | instance type is a value type, `string`, `unit`, an option or an enum | `Arms.Match: the instance type 'int' is not an object type. An arm site keys each arm's payload by its instance; use a record, class, union or function instance.` | `ArmsMatchException`, same sentence |
| AM005 | Fable build without the `.targets` (no `RANVIER_ARMS_PLUGIN`), or with a plugin that does not run | compile error FS0101 at the call: `This construct is deprecated. Arms.Match needs the Ranvier.View.Arms Fable plugin; import Ranvier.View.Arms.targets in the project compiled by Fable.`; a site whose plugin is referenced but does not run fails at site build with a `TypeError` | — |
| AM007 | a decision node under a wrapper the walker does not decompose (decision 11) | `Arms.Match: this pattern form is not supported.` | `ArmsMatchException`, `<file>(<line>): ` + the same sentence |
| AM006 | a reader over the instance type receives an instance with no arm of this site, e.g. a row whose item type equals the instance type | `Arms.Match: no arm of this site is mounted for the instance this reader received. A lambda over the instance type inside a case body runs with the arm's instance.` (a Release Fable build raises `Ranvier AM006: shayanhabibi.github.io/Ranvier/view/arms/#am006`, the docs entry holding this text) | same, as `ArmsMatchException` at read time |

AM004 is checked from the type, before any instance exists: `WeakMap.set` throws for a JavaScript primitive and a
`ConditionalWeakTable` keys a fresh box on each .NET call, so either target would fail at mount with a less useful
message. Both targets reject the same types, so a site that builds on one builds on the other.

**Rows.** A `forEach` row template is built once and shared by every row of every instance; a row reader receives the
row's `unit -> item`, so a pattern variable inside it has no arm to read (AM002). The pattern variable is passed
through the items reader, which runs over the instance:

```fsharp
| Loaded (owner, items) -> ul () { forEach _.Id (fun _ -> items |> Array.map (fun i -> owner, i)) row }
```

## The .NET quotation path

**Cost.** 9.06 ms for the first five-target site of a process and 8.55 ms mean over 200 more
(`results/net.txt:1`); nearly all of it is `LeafExpressionConverter` compiling one LINQ expression per body and one for
the selector. The shipped lowering forms one LINQ expression per site, runs its construction once in the LINQ
interpreter, and compiles each per-call lambda (selector, readers, row readers, handlers) after 64 calls; the budget is
≤ 4 ms mean per five-target site, measured by `Checks/DotnetCost` and the `lowering|` line in the checks. The first site
of a fresh process costs 125-146 ms: JIT of FSharp.Core's quotation deserialiser (about 30 ms) and
`LeafExpressionConverter` (about 50 ms) and of the Arms lowering and view generics (about 38 ms); it is measured and
recorded, not budgeted. Two measures cut it. `Arms.Prepare ()` at start-up (on the calling thread or the thread pool)
lowers, renders and compiles a built-in site, after which the app's first five-target site costs 9-12 ms. A ReadyToRun
publish (`-p:PublishReadyToRun=true`) precompiles FSharp.Core and the view: 56-57 ms for the first site without
`Prepare`, 7 ms after it (`Prepare` itself 68-75 ms). Module-level sites and one render at start-up keep the rest out
of the first request (research note, ".NET lowering cost").

**When it runs.** Once per evaluation of the call. A site bound at module level, or inside a template's build
function, lowers once per process, on the first access of its file's values; a server that calls `Server.render` once
at start-up moves the cost out of the first request. A site inside a function called per render lowers per call; the
docs page states the binding rule that `armOf` already has (`docs/content/view/arms.md`, "Bind arm definitions with
`let`").

**Caching.** None in v1 (decision 14). A quotation captures the call's free locals as values, so a cache keyed by
call site would serve the first call's captures to later calls.

**Thread safety.** Each call creates its own cells and definitions, so concurrent calls of `Arms.Match` are independent.
A site's per-call lambdas are shared by every render of the site: each counts its calls with `Interlocked`, and exactly
one call queues its compilation on the thread pool; renders keep running the interpreted delegate until the compiled one
is published, and a failed compile leaves it interpreted. The cells' entry arrays are replaced under the instance's
lock and read without it. Arm readers are installed
with `Interlocked.CompareExchange` by the existing `arm` (`Template.fs:2307-2319`).

**Trimming and NativeAOT.** `[<ReflectedDefinition>]` stores the argument as a serialized quotation that FSharp.Core
resolves by reflection on type and member names, and `LeafExpressionConverter` builds `System.Linq.Expressions` trees
through `MakeGenericMethod`. ILLink cannot see either. The .NET `Arms.Match` therefore carries
`[<RequiresUnreferencedCode>]` and `[<RequiresDynamicCode>]` with the text
`"Arms.Match lowers a quotation through reflection and System.Linq.Expressions. Use matchWith with armOf in trimmed or NativeAOT apps."`,
and a trimmed or NativeAOT publish reports IL2026 and IL3050 at each call site. The repo's NativeAOT smoke job
(`.github/workflows/ci.yml:86-102`) publishes `tests/Ranvier.AotSmoke`, which references only `src/` libraries
(`Ranvier.AotSmoke.fsproj:27-31`) with `TreatWarningsAsErrors`; it is unchanged and unaffected. The arms CI job adds a
check that `dotnet publish -p:PublishTrimmed=true` of `Checks/TrimCallSite` (one `Arms.Match` call) reports IL2026
naming `Arms.Match`, so the annotation stays in place.

## SSR and hydration parity

**Guarantees.** For every fixture and state:

1. **Server HTML is byte-identical** between the .NET render (quotation path) and the Fable render (plugin path), and
   equal to the explicit twin's on each target. The probe showed 54 of 54 lines equal (research, Hybrid).
2. **Each target's definition has its explicit twin's structural hash and skeleton layout.** The arm marker carries the
   selected definition's hash (`Template.fs:1860-1865`, `<!--[a:646a1db6-->`); the probe's hashes match for every arm
   (`220727aa`, `a8ebc344`, `646a1db6`, `c054a3d7`, `7f75a908`, `789182d0`, `fddcd338`).
3. **A Fable client hydrates .NET server HTML under `Strict`**, adopts every server node, reports nothing, and updates
   in place (probe `hydrate|` lines, `results/fable-dom.txt:145-149`).
4. Hashes and layouts are equal between `Arms.Match` on .NET and under Fable, because both build the same bodies through
   the same `Site.create`.

**Tests.**

- `Checks/ArmsFixtures.fs`: each fixture of the probe (`Fixtures.fs:35-215`) plus the phase-2 forms of the pattern
  table, each with an explicit `armOf` twin.
- `Checks/ArmsServerChecks.fs`, compiled into `RepresentationChecks` (.NET) and into the Playground `DomTests.fsproj`
  (Fable), as `SugarServerChecks.fs` is (`DomTests.fsproj:83-84`, `RepresentationChecks.fsproj:14`): twin layouts and
  hashes (`layoutSignature`, `structuralHash`), and twin server HTML under the default `Reject` policy and `Omit`.
- **Cross-runtime golden file** `Checks/arms-server.golden.txt`: the .NET checks write `name|state|hash|html` lines with
  `--update` and compare against the committed file otherwise; `tests/arms-server.test.mjs` compares the Fable lines
  with the same file. Two runtimes, one file, so a divergence fails on whichever side changed.
- `tests/arms-hydration.test.mjs`: each Fable fixture hydrates the golden file's HTML for its state under `Strict`, with
  the report handler failing the test, then writes three steps and compares markup with the explicit twin's.

## Bundle budget

Measured by `experiments/Ranvier.View.Arms/Checks/size.mjs`, modelled on the probe's `size.mjs` and the Playground's
`sugar-size.mjs`: Vite with minification, gzip level 9 and Brotli quality 11, one loader with three arms mounted
through `Mounting.mountWith`.

| Check | Budget |
|---|---|
| `Arms.Match` entry vs its explicit `armOf` + `matchWith` twin | ≤ 400 B gzip (measured 189 B with the payload-direct lowering of #157: the definitions array, the selector, the AM006 guard and its Release text, and the splice adapter; 374 B with the cells; the method resolves about ±20 B) |
| Each further site in the same bundle, against its twin | ≤ 60 B gzip per site |
| An app that references `Ranvier.View.Arms` and calls no `Arms.Match`, vs the same app without the reference | 0 B (byte-identical bundle) |
| The Playground `sugar-size.mjs` explicit entry before and after this work | 0 B |
| `quotation.js` or `Expr` code in any `Arms.Match` entry (unminified search for `quotation`) | absent |
| `curry`, `uncurry`, `partialApply` in the emitted `Ranvier.View.Arms` module and the prologues | 0 |

The 0 B rules follow from decision 8: no standard file changes, and Vite drops an unreferenced module.

## IDE experience

- The IDE type-checks without `FABLE_COMPILER`, so it sees the .NET overload. A lambda passed where an `Expr` is
  expected is quoted automatically, so the call types and completes as a plain function in every project.
- Pattern variables have their field types (`text: string`), and hover, rename and go-to-definition work on them.
- Quotation restrictions (FS0449 for object expressions) appear in the IDE for Fable-only projects too.
- AM001-AM004 appear in `dotnet fickl` output with file and range, and on .NET when the site is built. The IDE shows
  none of them in this spec; `span () { text }` type-checks because a string yields static text.
- Source maps: the plugin gives each prologue the range of the reader it opens, so a breakpoint in `fun _ -> string
  percent` stops in the reader.

**Follow-up analyzer** (FSharp.Analyzers.SDK, separate spec): AM001-AM004 on the typed tree, reported by Ionide and
`dotnet fsharp-analyzers` on both targets, plus AM005 for a Fable project that references `Ranvier.View.Arms` without
the `.targets`. Rider does not host FSharp.Analyzers.SDK analyzers, so Rider users get the CLI report only.

## Testing

Each item names its suite; the research's unprobed items are marked **(new)**.

1. **Plugin unit tests** (`experiments/Ranvier.View.Arms.FablePlugin/Tests`, .NET): compile fixture files with
   `dotnet fickl` into a scratch folder and compare the emitted JavaScript of each site with a committed expectation
   (names normalised); one bad-fixture file per diagnostic, asserting exit code 1 and the exact message and range.
   Emitted names are derived from the call's source range (`L<line>C<col>`), and the tests normalise them to per-file
   ordinals, so the expectation changes only when the lowering does (the probe's global counter, `Plugin.fs:161`, is
   not stable).
2. **.NET lowering tests** (`RepresentationChecks`): the pattern table's forms lower, AM001-AM004 raise with the exact
   texts, and the `lowering|` timing stays within budget.
3. **Mounted behaviour** (Playground `tests/arms.test.mjs`, jsdom, `RANVIER_VIEW_COUNTERS`): the probe's nine-step trace
   (`Main.fs:81-130`) for every fixture equals its twin's markup, arm-kept and row-kept flags; reports stay empty;
   synchronous and queued commits; core counters, listener registrations and MutationObserver writes equal the twin's.
4. **Twice-shown (new, probe `Twice`)**: two mounts of one site with one instance; dispose either; later writes reach
   the other (the probe's `afterDisposeB` line reads `b x2` today and must read `c x3`); a switch while both are
   mounted leaves no reports.
5. **Event handlers in arms (new)**: `button (Events.onClick (fun s _ -> saved.Value <- text)) { ... }` in a case
   body; click after two payload updates stores the latest `text`; click after a switch and back stores the new arm's
   value.
6. **Primitive and value-type payloads (new)**: `int`, `float`, `bool`, `char`, `decimal`, a struct record and a
   `ValueOption` as payload fields, on both targets; AM004 for `int`, `string`, `unit`, `int option` and a struct
   record as the instance type, on both targets.
7. **Rows reading pattern variables (new)**: AM002 for a pattern variable inside a `forEach` row reader; the
   items-tuple form above keeps rows across payload updates and updates their text.
8. **Fable Debug build (new)**: the arms fixtures join `test:sugar-debug` (`package.json`), which compiles
   `DomTests.fsproj` with `--configuration Debug`; the region-read definition check stays silent and the traces equal
   the Release build's.
9. **Held exits**: a `Motion.presence`-style exit hook (`Hooks.exit`, from the motion seam) on a case body root holds
   the outgoing arm's node after a switch; its readers keep their own case's values until `finish`.
10. **Pending sources**: a `read` over an async value inside a `boundary` waits with the boundary, as `matchWith` does
    since #137.
11. **Chromium benchmark (new)**: a `ranvier-arms-match` mode beside `ranvier-sugar` in
    `benchmarks/frameworks/keyed-page.mjs` (`armAdapters`, line 26), with the arm list of `KeyedRowsSugarPerf.fs:98-119`
    written as `Arms.Match`; run `keyed-rows.mjs --modes=ranvier-sugar,ranvier-arms-match
    --ops=armsCreate1k,armsSwitch1k,armsPayload1k,armsClear1k --processes=3 --rounds=15 --pairs=ranvier-arms-match:ranvier-sugar` under `bench.lock`
    (`mkdir C:/Users/shaya/.claude/jobs/7fee9ca9/tmp/bench.lock` before, `rmdir` after) and the browser lock. Gate, per
    operation: the paired ratio (each `ranvier-arms-match` sample over the `ranvier-sugar` sample of the same process and round, `--pairs=ranvier-arms-match:ranvier-sugar`) pooled over the committed runs of the benchmark whose recorded source hashes match the tree (`Checks/pooled-interval.mjs`), has a median of at most the operation's budget; its pooled 95 % bootstrap interval is reported as below, above or straddling the budget. `Checks/chromium-gate.mjs` checks the committed results. Every committed run with matching sources stays in the pool. Runs record the CPU load in `notes`, source hashes and tool versions.
    The budgets are create 1.05, switch 1.04, payload 1.07 and clear 1.05. The first release shipped under a 1.12
    allowance the user accepted on 2026-10-22 (plan Ruling 38, superseded); the payload-direct lowering
    ([#157](https://github.com/shayanhabibi/Ranvier/issues/157), Phased implementation 8) targeted 1.05 on every
    operation. **Concession (user ruling, 2026-10-24; plan `2026-10-24-issue-157.md`, Decision P9):** create and clear
    keep 1.05; switch takes 1.04 and payload 1.07, because the plugin's output is indistinguishable from the hand-written
    payload-direct shape (`arms-match-shape.json`). The budgets are fixed and are not refitted when a run joins the pool;
    an operation whose pooled interval straddles its budget passes when its pooled median is within it. Paired medians and intervals per run, at the CPU load recorded in each:

    | Run (CPU load before / after, %) | create | switch | payload | clear |
    |---|---|---|---|---|
    | `arms-match-22d86d44-1` (38, 83, 96 / 54, 43, 72) | 0.957 (0.902-1.010) | 0.975 (0.950-1.027) | 1.040 (0.979-1.121) | 1.000 (0.864-1.116) |
    | `arms-match-22d86d44-2` (43, 84, 96 / 96, 93, 79) | 1.087 (1.005-1.121) | 1.015 (0.948-1.141) | 1.074 (1.009-1.146) | 0.985 (0.853-1.010) |
    | `arms-match-22d86d44-3` (44, 57, 79 / 73, 85, 64) | 0.999 (0.923-1.050) | 1.050 (0.967-1.100) | 1.149 (1.028-1.263) | 0.964 (0.890-1.059) |
    | `arms-match-22d86d44-4` (52, 59, 68 / 57, 82, 76) | 1.024 (0.989-1.057) | 1.003 (0.951-1.048) | 0.994 (0.946-1.084) | 0.955 (0.869-1.030) |
    | `arms-match-22d86d44-5` (71, 88, 59 / 66, 71, 63) | 1.002 (0.952-1.053) | 1.018 (0.981-1.077) | 1.013 (0.939-1.128) | 1.035 (0.893-1.156) |
    | `arms-match-22d86d44-6` (65, 64, 59 / 81, 68, 68) | 0.977 (0.917-1.041) | 1.051 (0.997-1.101) | 1.017 (0.929-1.144) | 1.015 (0.922-1.107) |
    | `arms-match-2dc2835b-1` (54, 48, 38 / 45, 51, 65) | 1.009 (0.979-1.036) | 1.005 (0.969-1.026) | 1.094 (0.979-1.158) | 0.970 (0.880-1.064) |
    | `arms-match-2dc2835b-2` (54, 48, 37 / 77, 26, 44) | 0.983 (0.962-1.004) | 1.016 (0.991-1.047) | 1.024 (0.967-1.088) | 1.004 (0.891-1.133) |
    | `arms-match-2dc2835b-3` (57, 20, 34 / 46, 46, 62) | 1.014 (0.971-1.057) | 0.975 (0.937-1.041) | 0.955 (0.895-1.090) | 1.119 (0.931-1.192) |
    | `arms-match-2dc2835b-4` (48, 32, 58 / 42, 35, 29) | 0.992 (0.975-1.018) | 1.012 (0.981-1.041) | 1.033 (0.972-1.095) | 0.983 (0.902-1.096) |
    | `arms-match-2dc2835b-5` (33, 46, 23 / 35, 54, 38) | 0.993 (0.975-1.043) | 1.014 (0.977-1.049) | 0.970 (0.892-1.011) | 0.863 (0.797-1.132) |
    | `arms-match-2dc2835b-6` (40, 51, 63 / 18, 12, 28) | 1.046 (1.022-1.058) | 0.998 (0.965-1.014) | 1.048 (0.987-1.096) | 1.040 (0.913-1.141) |
    | pooled, 540 pairs | 1.006 (0.993-1.018) | 1.011 (0.999-1.022) | 1.033 (1.010-1.060) | 0.991 (0.957-1.020) |

    Every pooled median is within its budget, and every pooled interval lies below its budget. The runs' switch medians
    spread from 0.975 to 1.051 and their payload medians from 0.955 to 1.149.
    `Checks/pooled-interval.mjs` prints the pool, each pooled run, and the sources that keep each other run out;
    `pooled-interval.test.mjs` checks the fixed budgets, the pooled medians against them, and the pool against the
    source rule; `keyed-rows.mjs` writes a run into `docs/research/arms-match/` only from committed sources unchanged
    during the run, and only with `--build`: the run builds its own bundle into `generated-keyed-pooled/<label>` under a
    lock per checkout, records `built` and the served bundle's `bundleHash`, and refuses the write when the bundle
    changed during the run. The hashed sources (`benchmarks/frameworks/run-sources.mjs`) are the Arms.Match lowering
    (`experiments/Ranvier.View.Arms` and the Fable plugin project with each file it compiles), the keyed-rows benchmark
    with its page and build config, and the keyed-rows fixtures `tests/KeyedRows{,Sugar,ArmsMatch}Perf.fs`; a run
    matches when it records each with the tree's hash, up to line endings, and is compared on these paths alone. The
    core (`src/Ranvier`), the view libraries and the Playground lock file are not hashed (user decision, 2026-10-25,
    plan Decision P9): a change to them leaves the pool unchanged, and a core change that slows Arms.Match shows in the
    gate only once the lowering changes or a run is recorded. The twelve pooled runs were recorded six at `22d86d44` and
    six at `2dc2835b`, two at a time under `gate-lock.ps1 bench`; each built its own bundle, and the six runs at each
    commit record the same `bundleHash`. The five runs at
    `da6d32e6` stay committed and out of the pool: they predate the `--build` requirement, four of them served a bundle
    they did not build, and their hashes differ from the tree in `keyed-rows.mjs`. The earlier runs `8ee10018` (`docs/.ai/benchmarks/arms-157-noisy-probe.json`),
    `299ab276`, `fccc740c` (`arms-match-shape.json`), `dc8b3d5b`, `5369d6a0` and `4d00179e` (`arms-match.json`) stay
    committed and out of the pool: their hashes differ from the tree in `keyed-rows.mjs`, and they record none for the
    plugin project, `Shape.fs` or `Plugin.fs`; the first three also differ in `Site.fs` and `Lower.fs`. A change to any
    hashed source takes every run out of the pool, and the gate fails until runs are recorded on the new sources. CI
    runs the gate only for a change to the lowering, its benchmark, its runs or the workflow
    (`.github/scripts/changes.cjs`, `armsRuns`); a core, view or Playground lock-file change alone skips it.
12. **Heap profile (new)**: `keyed-heap.mjs --fixture=arms` with the new mode at 1,000 and 10,000 rows; retained bytes
    per row after create within 10 % of `ranvier-sugar`, and the heap after one switch of every row (`--after=switch`)
    reported at both counts. `Checks/chromium-gate.mjs` checks the first and prints the second.
13. **Docs samples**: the `arms.md` samples compile in `docs/samples/view` (.NET, quotation path) and their quoted HTML
    is checked, as the Playground CI job runs today.
14. **Trim annotation**: the `TrimCallSite` publish reports IL2026 naming `Arms.Match`.

**Myriad is not needed.** It would generate the lens active patterns or hoisted `armOf` definitions, both of which the
recommendation rejects, and the plugin and quotation cover both targets from the user's source alone. Myriad also
generates in an MSBuild target before `CoreCompile`, which `dotnet fickl` does not run, so generated files would have to
be committed or produced by a separate step.

**CI.** A new `arms` job mirroring `playground` (`ci.yml:172-212`): build the plugin, run `RepresentationChecks` with
the arms checks, `npm run test:arms` and the arms size script in the Playground, and the trim check; added to
`result.needs` (`ci.yml:367`). The benchmark and heap profile run on demand, with results recorded in
`docs/research/2026-10-21-arms-match.md`.

## Docs

- `docs/content/view/arms.md`: a section "`Arms.Match`: a plain match (opt-in)" after "`branch` over a key": the
  two-line opt-in, the loader sample, the reader rule ("a pattern variable is read inside `fun _ -> ...`"), the
  rejected spelling with the AM001 text, rows (the items-tuple form), what remounts (another target, including guards
  and literal patterns), costs (bundle, .NET first-call lowering, NativeAOT unsupported), and a link to the reference.
- `docs/content/view/reference.md`: a table for `experiments/Ranvier.View.Arms` (`Arms.Match`,
  `BuildTimeReadException`, `ArmsMatchException`) and the plugin project.
- `docs/content/view/troubleshooting.md`: AM001-AM006 with each exact text and its fix.
- `docs/samples/view`: the new samples, generated by `Generate.fsx` as the existing pages are.
- `docs/research/2026-10-21-arms-match.md`: benchmark, heap, bundle and lowering measurements of phases 3-5.

## Risks

1. **FCS tree shapes the probe did not meet** (most likely to cost a phase). The plugin saw two shapes
   (inlined `fields[k]` and `DecisionTree` targets); active patterns, `as`, type tests and nested sites may produce
   others, and the quotation may differ from the Fable AST for the same source. A shape the decision walk does not
   recognise becomes one leaf, which surfaces as AM001 on a valid program. Phase 2 adds a fixture per form on both
   targets before the docs list it.
2. **Plugin discovery through a project reference.** The probe referenced the plugin assembly from the same project
   that called `Arms.Match`. Discovery through an imported `.targets` and a project reference to the library is
   unverified; phase 1 settles it before any other work.
3. **Rows whose item type equals the instance type.** Their readers look like readers of the instance and are
   rewritten; they then read cells keyed by the row item and raise AM006. The diagnostic is clear; the form is rare.
4. **Divergence between target identity on .NET and under Fable.** If the quotation duplicates an or-pattern body, the
   .NET site has more definitions than the Fable site. Server HTML and hashes are unaffected (guarantee 1-2), and .NET
   renders only on the server, where target identity shows nowhere.
5. **Plugin compatibility with later Fable 5.x.** The plugin reads `Fable.AST` union cases directly. A new case in a
   minor version compiles against 5.0.0 but is skipped by `Shape.children`, which can hide a pattern variable from the
   search. The arms CI job runs on the repo's pinned Fable; a Fable bump re-runs the plugin tests.

## Phased implementation

Each phase lands with its tests green and is reviewable alone.

1. **Scaffold and discovery.** Create both projects from the probe (`CaseArms.fs`, `QuotedArms.fs`, `Plugin.fs`),
   rename namespaces to `Ranvier.View.Arms`, add the `.targets`. Verify with a Playground fixture compiled by
   `dotnet fickl`: the plugin is found through the `.targets` and a project reference; `FABLE_COMPILER` as an MSBuild
   property during cracking; netstandard2.0 plugin loads in Fable 5.18. Record results in the research note.
2. **Lowering rules.** The widened decision rule on both producers; throw leaves into `select`; `Delegate` readers;
   AM001b field names; AM002-AM004; stable emitted names; the pattern-table fixtures and their twins on both targets.
3. **Runtime.** `Cells` with per-target live lists, graph tags, withdrawal, locking on .NET, AM006; `Site.create`; the
   .NET one-expression lowering. Tests: Testing 2-7, the `Twice` scenario, `lowering|` budget.
4. **Parity.** `ArmsServerChecks.fs`, the golden file, `arms-server.test.mjs`, `arms-hydration.test.mjs`, Debug build,
   held exits, pending sources (Testing 8-10).
5. **Budgets.** `size.mjs` with the 0 B and per-site rules; the Chromium benchmark mode and heap profile (Testing
   11-12); the trim check; the CI `arms` job.
6. **Docs.** `arms.md` section, reference table, troubleshooting entries, samples, research note.
7. **Analyzer** (separate spec): AM001-AM005 under FSharp.Analyzers.SDK.
8. **Payload-direct lowering (implemented, #157; plan `2026-10-24-issue-157.md`; Chromium gate met under the
   per-operation budgets of the 2026-10-24 ruling).** Each target becomes an `armOf` definition whose instance is the
   arm's values thunk, the site selects with one array `[target, pattern variables..., matched value, instance]`, and
   the plugin rewrites every instance lambda to read `p ()[k]`; under Fable the cells, `enter`, withdrawal and graph
   tags go, and a held reader reads its own row's props, as the twin does. A helper view of the instance type is
   spliced through an adapter child, which keeps the structural hash and server HTML (probe in the research note).
   The spike of the shape (research note, "Payload-direct spike"; quiet run) measured it against the sugared twin at
   create 1.019, switch 1.031, payload 1.007 and clear 1.026 (paired medians), the heap after a switch at +1.1 % (today
   +12.8 %), and its loader at 31 B gzip (today 371 B). The final review estimates 1-1.5 weeks for Fable with the .NET
   quotation and cells path kept, about 3 weeks with .NET parity; the AM006 guard (15-25 B) and splice adapters
   (30-50 B) remain to be measured. The lowering targeted the original median of 1.05.
   Measured on the shipped lowering: the first site 189 B gzip over its twin and each later site 8 B (`bundle.json`),
   the Fable output holding no cells module; the heap at +1.2 % over the twin after create and after a switch, at both
   row counts. The Step 11 paired medians on the shared machine were create 1.003, switch 1.078, payload 1.073 and
   clear 1.011, every 95 % interval straddling 1.05. A paired run against the spike's hand-written shape finds the
   plugin's output indistinguishable from it (research note, "Chromium"). Under the user's ruling of 2026-10-24 the
   Chromium budgets are per operation (Testing 11): create and clear 1.05, switch 1.04 and payload 1.07, fixed, judged by
   the pooled paired median over the twelve runs whose sources match the tree and which built their own bundle: create
   1.006, switch 1.011, payload 1.033 and clear 0.991, and `chromium-gate.mjs` passes; every pooled interval lies below
   its budget.
