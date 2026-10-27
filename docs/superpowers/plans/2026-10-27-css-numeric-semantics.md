# CSS numeric semantics

Implement the CSS Values numeric typing rules as an executable domain model, then embed the rules and each property's derived calculation contexts in the generated manifest. The grammar alone does not specify arithmetic: the normative prose algorithms must be encoded explicitly, with their source URLs retained.

Sources: https://drafts.csswg.org/css-values-4/#calc-type-checking and https://drafts.css-houdini.org/css-typed-om-1/#numeric-typing.

## Task 1: Numeric type algebra

Add `tools/Ranvier.Css.Generate/NumericSemantics.fs`, after Model.fs. Model dimensions, exponent maps, percentage hints, numeric contexts and function signatures. Implement addition, multiplication, inversion and function result inference. Preserve compound intermediate dimensions, reject incompatible sums and percentage hints, distinguish ordinary percentages from dimension-relative percentages, and reject unknown functions rather than guessing. Encode arity and argument restrictions for CSS Values math functions. Add pure Expecto tests covering valid and invalid examples, dimension cancellation, contextual percentages, and function typing. Use SageFs for the pure loop and F# XML documentation conventions for public declarations.

## Task 2: Derive generation metadata

Add a domain projection from resolved numeric grammar conversion paths to calculation contexts. Use the same checked `ergonomicValues` paths as property sugar; do not infer acceptance from arbitrary descendant tokens in sequences or function arguments. Retain ranges and distinguish integer rounding, number, dimension and percentage contexts. Embed a versioned `numericSemantics` entry and property calculation contexts in the manifest in both standalone and shared-runtime generation. Include provenance, dimension maps, operator policies and known function policies. Tests must verify width, opacity, integer properties, nonnumeric properties, unknown productions and deterministic output.

## Task 3: Verify and document

Regenerate the locked corpus manifest. Document that this change supplies executable semantic rules and derived generation inputs; ordinary grammar bindings remain available. Document how generated static operator signatures will use these rules, without claiming unimplemented runtime expression helpers. Run the complete CSS spec tests, generator determinism check, formatting/comment checks, and final branch review. Keep current release publication verification separate from this next feature.

## Constraints

Preserve existing runtime APIs and existing grammar information. Spec consumption must remain total for opaque or future productions: unknown numeric semantics stay unknown. Do not silently treat `<dimension>` as a particular dimension or infer percentage resolution from a property's name. Do not infer a length-percentage context merely because independent alternatives accept length and percentage: require the mixed production itself. The semantic metadata is additive and versioned independently of the existing manifest schema.

## Review focus

Check that percentage hints survive cancellation; number-percentage alternatives do not authorize mixed addition; nested numeric tokens do not authorize whole-property calculations; integer calculation results use CSS rounding rather than integer-only inputs; incomplete functions fail explicitly; and the metadata agrees with the executable algebra.
