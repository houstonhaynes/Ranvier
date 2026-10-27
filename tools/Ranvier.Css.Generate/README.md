# CSS specification consumer

Consumes consolidated and individual Webref CSS extracts into `Ranvier.Css.Spec`, resolves grammar references, and generates typed F# constructors and declarations.

From the repository root:

```powershell
rtk npm ci --prefix tools/Ranvier.Css.Generate --ignore-scripts
rtk node tools/Ranvier.Css.Generate/fetch-specs.mjs
rtk dotnet run --project tools/Ranvier.Css.Generate -c Release -- --specs-dir tools/Ranvier.Css.Generate/out/specs --bindings tools/Ranvier.Css.Generate/out/Css.Generated.fs --strict
rtk dotnet test tests/Ranvier.Css.Spec.Tests -c Release
rtk node tools/Ranvier.Css.Generate/verify-bindings.mjs
```

The command writes `tools/Ranvier.Css.Generate/out/css-model.json`. To consume another consolidated snapshot:

```powershell
rtk dotnet run --project tools/Ranvier.Css.Generate -c Release -- --input path/to/css.json --output path/to/model.json --label my-snapshot --strict
```

Custom input defaults to the label `custom`. The SHA-256 always identifies the exact consumed UTF-8 bytes. Input files are bounded at 32 MiB before allocation; grammars are bounded at 65,536 UTF-16 code units and 128 nested expressions. The command reads local data without network access.

## Model

`Model.fs` defines catalogue identities, definitions, grammar nodes and diagnostics. A feature identity consists of kind, name, sorted scope set and optional descriptor owner. Descriptors inherit the enclosing at-rule's scope. Definition URLs, extension URLs, original syntax and additional metadata are retained. Metadata is a map of property names to raw JSON values; `Snapshot.toJson` emits those values as JSON, preserving their value types.

`Grammar.fs` parses value-definition syntax into explicit nodes for sequences, alternatives (`|`), any-of (`||`), all-of (`&&`), groups, functions, literal blocks, type/property/function references, parameterized types, numeric ranges, required groups and repetition. Combined multipliers such as `+#?` and `{2}?` retain nested cardinalities. Nodes carry zero-based UTF-16 offsets and lengths into the source grammar. Symbolic references remain symbolic, including recursive references.

`Webref.consume label bytes` returns a catalogue or JSON-path diagnostics for malformed input. Unknown top-level categories, misplaced descriptors and duplicate feature identities fail consumption. Every definition has one of three grammar states: `Parsed`, `Rejected` (source plus diagnostic), or `Missing` (the upstream entry supplies no syntax). Freeform spec metadata such as inheritance and computed values stays in its upstream representation.

`Snapshot.toJson catalogue` exports schema version 1 with explicit node tags, metadata, provenance and coverage, in stable feature order with LF endings. The CLI writes through a temporary file and preserves an existing output if input validation fails. Exit codes: 0 success; 1 input or I/O error; 2 rejected grammar under `--strict`; 64 invalid arguments. Strict mode still writes the complete diagnostic model. Grammar-free definitions are counted separately and do not fail strict mode.

## Pinned coverage and boundaries

The pinned dataset contains 1,839 definitions: 822 properties, 529 types, 172 functions, 159 selectors, 55 at-rules and 102 descriptors. All 1,710 supplied grammars parse; the remaining 129 definitions have `Missing` grammar. Its `css.json` SHA-256 is `30cb8daa786d6c27a1f74c5d413311c785ee0eec1dd34c65e8a4e6681c86baca`. The corpus test pins these counts and input identity, and CI installs the lockfile before running the suite.

The source lock also pins all 122 individual `ed/css` extracts from Webref revision `4bb34a8d08cb9b9c8b73f7f175703941eef5c20c`, with SHA-256 checks for each file. These contain 4,785 definitions: all 4,595 supplied grammars parse and 190 are prose-defined. Consolidation with the canonical package yields 4,596 current definitions and 286 archived obsolete definitions. Together these produce 4,882 binding modules, including 160 grammar-free definitions. Every nested raw value is retained; raw source definitions are also preserved as metadata when canonical definitions merge them.

## Latest definitions and obsolete APIs

`SpecSources.consume` reads individual extracts. `SpecSources.consolidate` selects the highest available level within each specification series; `consolidateWith` also accepts a canonical merged catalogue. Newer raw definitions beat older canonical definitions from the same series. Earlier definitions absent from a delta spec stay current. Superseded definitions retain their original grammar and provenance in an obsolete catalogue, including replacement URLs. Explicit upstream obsolescence and legacy aliases emit `System.Obsolete` attributes in generated F#.

Cross-series extensions use canonical merged definitions. If a newer raw definition would replace an older canonical merge while dropping a distinct extension from another series, consolidation returns a diagnostic requiring a refreshed canonical source. It does not silently discard the extension or invent a grammar merge. The CLI includes both current and obsolete catalogues in its output.

The consumer is offline and reproducible. “Latest” means the newest available level in the supplied input set. Refresh the pinned npm package and Webref revision/lock together when updating upstream data; `fetch-specs.mjs --refresh-lock` regenerates hashes at the revision declared in the script. The default fetch verifies the committed lock and cached/downloaded bytes. It never silently changes the version being generated.

## Reference resolution and generated bindings

`Resolution.resolve` binds kind, normalized names, descriptor owners, lexical scopes, and source-document provenance. Equivalent aliases share a deterministic representative. Dependencies and recursive components are retained without recursively expanding grammar trees. Scoped forwarding definitions use their outer production when available. The complete corpus leaves two unresolved `anchored-feature` references from the upstream anchor-position extract; the manifest records these explicitly.

`Generation.generate namespaceName catalogue` returns source and a JSON manifest. Each production and intermediate grammar node has a distinct nominal marker inside `Css.Value<'syntax>`. Alternatives, sequences, functions, unordered groups, repetitions, ranges and generic boolean expressions generate typed constructors. The manifest maps CSS identities to F# module names, marker types, root/child syntax factories, references and recursive components. Normalized naming collisions receive deterministic suffixes. Complex intermediate nodes are available through `Css.Syntax`; their factory names are generated details described by the manifest.

For example, include the generated file before its consumers in an F# project:

```fsharp
open Ranvier.Css.Generated

let width =
    Css.TypeLength.px 42.0
    |> Css.TypeLengthPercentage.fromLength
    |> Css.TypeBoxSize.fromLengthPercentage
    |> Css.PropertyWidth.fromBoxSize
    |> Css.PropertyWidth.declaration

Css.renderDeclaration width // width: 42px;
```

`Css.renderDeclarations` returns declaration text that a view can use as a style attribute. Use `--runtime` to generate bindings sharing `Ranvier.Css` values and declarations, including the default unified helpers: with `open Ranvier.Css`, write `Css.width (Css.px 42.0)`, `Css.display Css.block`, or `Css.width (fun state -> Css.px state.Width.Value)`. Static values return declarations; readers return tracked style nodes. The detailed grammar API shown above remains under `Ranvier.Css.Generated.Css`, with `constant`, `reactive`, and `reactiveImportant` property methods. The checked-in library is `src/Ranvier.Css.Bindings`; `Ranvier.Css.View` supplies template adapters and `Ranvier.Css.Build` emits modular or lifted assets. CSS-wide keywords cannot leak into property-reference value grammars.

Sugar uses resolved grammar paths through alternatives, references, single branches of `||`, and repetitions permitting one item. Each conversion calls the canonical factories, preserving bounds and required/cardinality validation. Required multi-part structures remain explicit canonical values. The manifest records `ergonomicValues`, `ergonomicKeywords`, and `primitiveHelpers`. Inline static property extensions are grouped per property and opened automatically; callers see one `Css` helper surface. Normalized naming collisions receive stable suffixes. Archived versions remain in the canonical API; current deprecated definitions retain obsolete attributes on sugar helpers. A custom namespace ending in `.Generated` places sugar in its parent namespace; other custom namespaces use an adjacent `.Sugar` namespace to avoid colliding with their canonical `Css` module.

Numeric constructors require finite numbers. Repetition counts, required groups, unordered nonempty groups, and numeric reference bounds are checked at construction. String constructors escape CSS string newlines and quotes; identifier constructors accept a restricted unescaped subset. Numeric unit comparisons support matching units and basic absolute conversions; context-dependent conversion can fail until computed-value resolution is available.

Prose-only constraints, full computed-value semantics, property-specific identifier exclusions and browser support are not inferred from grammar. Missing/rejected productions expose explicitly named `unsafeFromTokens`; unresolved references require `Css.Unsafe.externalValue`. These calls accept unchecked text. Built-in prose primitives also provide typed numeric/string/identifier helpers, but the manifest keeps their opaque status. This is complete consumption of the pinned corpus, not a claim that every prose rule in CSS is enforced by F#.

The CLI emits a model, bindings and adjacent `.manifest.json`, writing each file atomically. Output paths must be distinct and cannot overwrite specification inputs. `--namespace` changes the generated namespace. `verify-bindings.mjs` checks the ordinary generated library source is current, compiles the entire corpus in Release, executes serialization and reactive-reader examples, checks rejected type mismatches and obsolete compiler warnings, and exercises range/cardinality/string contracts with a small fixture. `verify-assets.mjs` checks all three output modes with Lightning CSS. `verify-browser.mjs` runs the Fable DOM checks. CI runs the same acceptance path.

To refresh the ordinary shared-runtime library after a generator/spec update:

```powershell
rtk dotnet run --project tools/Ranvier.Css.Generate -c Release -- --runtime --specs-dir tools/Ranvier.Css.Generate/out/specs --bindings src/Ranvier.Css.Bindings/Generated/Css.fs --strict
```

Sources: [Webref consolidated format](https://github.com/w3c/webref/blob/main/packages/css/README.md), [CSS value-definition syntax](https://drafts.csswg.org/css-values-4/#value-defs).

Shared-runtime manifests also expose a versioned `sugar` map with actual property/primitive/keyword member names, accepted markers and canonical numeric guards. The [CSS source importer](../Ranvier.Css.Import/README.md) consumes this map to generate short F# calls from existing stylesheets. This keeps normalized naming and conversion constraints consistent with the generated library.
