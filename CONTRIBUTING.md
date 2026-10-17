# Contributing to Ranvier

## Before opening a pull request

Every repository task runs through the build CLI (see [Build CLI](README.md#build-cli)):

```shell
dotnet fsi build.fsx -- test            # F# (Expecto) and C# (xUnit) suites
dotnet fsi build.fsx -- test-fable      # the suites under Fable and Node.js, traced and untraced
dotnet fsi build.fsx -- format          # Fantomas over every source file
```

CI rejects unformatted F#. `dotnet fsi build.fsx -- format --dry-format` checks without writing.

## Commit messages

Commits follow [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): summary`,
for example `perf(core): trim Fable output while preserving .NET code`.

CI reads two trailers. Write them in the footer (the last paragraph of the message); `Perf-Ignore`
counts only there, and `Perf-Accepted` on any line of the message:

| Trailer | Effect |
|---------|--------|
| `Perf-Accepted: <names\|all> — <reason>` | Accepts an instruction-count regression in the listed scenarios |
| `Perf-Ignore: <reason>` | Keeps a `master` commit out of the performance history |

When a pull request is squash-merged, a trailer reaches `master` only if it is in the footer of the
squash commit message.

## Checks on a pull request

| Workflow | Job | Fails when |
|----------|-----|------------|
| CI | .NET tests | a test fails in `Ranvier.Tests` (untraced and traced) or `Ranvier.CSharp.Tests`, or the package compatibility check fails |
| CI | NativeAOT smoke | the AOT publish or the smoke executable fails |
| CI | Formatting | `fantomas --check` reports an unformatted `.fs` file |
| CI | Fable | the Fable suites fail under Node.js, or the Fable package compatibility check fails |
| Perf | verdict | a scenario's instruction count regresses on arm64 and the regression is not accepted |

## Performance checks

The Perf workflow builds the pull request's merge base and its head, then runs them alternately,
five processes each, for Node and for .NET, on two legs:

- **`arm64-counters`** counts hardware instructions per operation. A scenario regresses when the
  head median exceeds the base median by more than max(2%, 20 instructions per operation) *and*
  every head process exceeds every base process. An unaccepted regression fails the check.
- **`x64-timing`** measures thread CPU time with a 15% threshold. It only emits warnings.

The results arrive as one pull-request comment, updated on each run, with regressions,
improvements and library-counter changes.

### Accepting a regression

When a regression is intended, accept it with either:

- the `perf-accepted` label, which accepts every scenario; or
- a `Perf-Accepted` trailer in any commit between the merge base and the head:

  ```
  Perf-Accepted: app-detail, keyed-remove — the detail now disposes rows eagerly
  ```

  The value is a comma-separated list of scenario names (as written in the report), or `all`, then
  ` — ` or ` -- ` and the reason. A name matching no scenario fails the check.

Adding or removing the label reuses the previous measurement of the same head and base, so the
verdict updates without measuring again. An accepted regression stays in the report and in the
history, with its reason.

### Performance history

Each push to `master` is measured against its parent. The Perf report workflow then records the run
on the `perf-data` branch, and the Docs workflow rebuilds the site's benchmark charts from it.

A commit that changes nothing measured (documentation, workflows, repository tooling) can stay out
of that history with a `Perf-Ignore` trailer and a reason in its footer:

```
docs: fix typos in the getting started guide

Perf-Ignore: documentation only
```

CI then skips measuring and recording the commit, and skips redeploying the docs for it. The next
commit is measured against it as usual. `Perf-Ignore` applies to `master` pushes only: pull requests
are always measured, and `Perf-Accepted` is the way to pass an intended regression. An empty reason
fails the workflow.
