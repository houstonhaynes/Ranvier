# CSS spec consumer implementation plan

**Goal:** Consume pinned Webref CSS into an F# catalogue and parsed value-definition grammar for later code generation.

**Architecture:** Keep symbolic references and scoped feature identities. Preserve source text, spec URLs and metadata. Represent missing and rejected syntax explicitly.

**Stack:** .NET 10, System.Text.Json, curated @webref/css 8.7.8. No runtime network access.

**Scope:** The approved description in chat; implementation authorized with design questions skipped.

- [x] Grammar: Model.fs and Grammar.fs. Prove precedence, grouping, functions, references, ranges, multipliers, complete consumption and malformed-input diagnostics in SageFs, then persist.
- [x] Consumer: Webref.fs. Read all categories and at-rule descriptors, distinguish kind/name/scope/owner, retain metadata and exact input SHA-256, reject malformed JSON shape and duplicate identities.
- [x] CLI: Program.fs and Snapshot.fs. Export deterministic JSON with parsed trees and coverage; strict mode fails on rejected grammars while still reporting every definition.
- [x] Tests: tests/Ranvier.Css.Spec.Tests. Run grammar and consumer contracts, real corpus coverage, deterministic output, CLI failures and the complete new test executable.
- [x] Finish: document invocation and limits, audit XML/source comments, review the diff and run Release verification.

Review focus: scopes sharing a name; grammar-free prose definitions; malformed metadata; operator precedence and nested multipliers; output provenance and repeatability.

## Reference resolution and binding generation

User authorized implementation, all CSS spec consumption, latest-level consolidation, and obsolete API warnings without design questions.

- [x] Consume every nested definition from all 122 pinned individual extracts; verify exact source hashes and every supplied grammar.
- [x] Consolidate newer same-series definitions, retain delta-spec definitions and provenance, archive superseded definitions, and diagnose stale cross-series merges.
- [x] Resolve scoped references, equivalent aliases, descriptor owners, forwarding specializations, and recursive dependency components without expansion.
- [x] Generate nominal typed F# values, syntax constructors, declarations, runtime grammar constraints, explicit unsafe APIs, obsolescence attributes, and a naming/coverage manifest.
- [x] Compile the complete generated corpus, execute a width example, prove an angle is rejected as width, and prove obsolete bindings trigger FS0044.
- [x] Add regression coverage, offline reproducibility documentation, lock verification, CI generation checks, and independent code review.

Validation: full CSS regression suite plus generated runtime and compiler contracts. Remaining upstream gaps are two unresolved anchored-feature references; prose-only semantics remain explicit opaque/unsafe surfaces.
