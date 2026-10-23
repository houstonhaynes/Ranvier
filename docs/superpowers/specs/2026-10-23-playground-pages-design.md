# Generated playground pages and modern examples

The main playground will use the shared F# page declaration introduced for Motion. Its HTML document and browser entry are generated before Fable compilation and bundling. Ordinary demo components use the current typed Sugar vocabulary and component declaration helpers. Handwritten HTML and JavaScript application startup are removed.

The user's existing uncommitted playground rewrite remains untouched in the main checkout and serves as a reference for authoring style. Work happens in the attached clean worktree on `codex/playground-pages`.

## Behavior and boundaries

- Preserve the visible demos, state changes, focus, node identity, remount controls and disposal behavior.
- Server-render ordinary content and hydrate it through `PageClient.start`. Browser-only effects start after placement. Avoid replacing useful server content with empty client-mounted containers merely to simplify migration.
- Keep explicit mounting/scheduling where the playground is intentionally demonstrating those mechanisms. Identify that teaching boundary in documentation.
- Generate documents and startup entries from one shared declaration. Reuse the existing build integration rather than duplicate the Motion build helper. Clean builds and development edits regenerate before serving; generation failures remain visible.
- Modernize ordinary View examples elsewhere, including Motion, after an inventory. Preserve low-level conformance fixtures, compiler diagnostics, and benchmark comparisons where changing syntax would change what they demonstrate or measure. Libraries located under experiments are implementations, not examples to mechanically rewrite.
- Do not change public library semantics or benchmark baselines merely to make examples look newer. Record deliberate exceptions by purpose.

## Validation

Verify clean production generation, nonempty server content, strict hydration node reuse, browser interactions, remount/disposal, and source-edit regeneration. Run the complete affected playground and Motion suites, representation/documentation checks as needed, formatting and XML audits. Review the final diff independently and verify CI and performance checks on the final PR commit.
