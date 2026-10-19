# Contributing to Ranvier

## Branches

Branch a change from `master` and open its pull request against `master`. Pull requests are
squash-merged, so each change lands as one conventional commit. `master` rejects direct pushes,
force pushes and deletion; pull requests merge only when the required checks pass. Repository
administrators bypass these rules, and push directly only changes that are not source code, such as
documentation.

GitHub Actions runs on a pull request from a fork only after a maintainer approves the run.

The rules are defined in [`.github/rulesets`](.github/rulesets) and applied with the GitHub CLI:

```shell
gh api -X POST repos/shayanhabibi/Ranvier/rulesets --input .github/rulesets/master.json
gh api -X PUT repos/shayanhabibi/Ranvier/rulesets/<id> --input .github/rulesets/master.json   # update
```

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
for example `perf(core): trim Fable output while preserving .NET code`. The type is one of `build`,
`bench`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style` or `test`; the
optional scope is lower case; `!` before the colon marks a breaking change. The **Conventional
commits** check applies this to the pull-request title, which becomes the squash commit's subject.
Pull requests opened by bots are exempt.

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
| CI | Fable tests | the Fable suites fail under Node.js, or the Fable package compatibility check fails |
| CI | Workflow scripts | a `node --test` suite under `.github/scripts` fails |
| CI | **CI result** | any CI job above failed or was cancelled |
| Perf | verdict | a scenario's instruction count regresses on arm64 and the regression is not accepted |
| Perf | **Perf result** | measuring or the verdict failed or was cancelled |
| Contribution | **Conventional commits** | the pull-request title is not a conventional commit header |

The branch rules require the three checks in bold. The Contribution check runs the scripts of the
target branch, not those of the pull request.

### Documentation-only changes

A change is documentation only when every changed path is under `docs/` (outside `docs/maps/`, the
signal map model and its tests), `brand/` or `.claude/`, is a Markdown file, a `LICENSE` file, or
`.github/FUNDING.yml` ([`changes.cjs`](.github/scripts/changes.cjs)). CI then runs only Formatting,
and Perf measures nothing; the required checks pass. A change under `docs/maps/` runs every CI job
and Perf measures it. A documentation-only push to `master` gets no history record, and
the docs are still redeployed for it.

## Performance checks

The Perf workflow builds the pull request's merge base and its head, then runs them alternately,
five processes each, for Node and for .NET, on two legs:

- **`arm64-counters`** counts hardware instructions per operation. A scenario regresses when the
  head median exceeds the base median by more than max(2%, 20 instructions per operation) *and*
  every head process exceeds every base process, and the lower modes of the two sides pass the
  same test. A side's lower mode is its lowest processes, up to the first gap between sorted
  values wider than that limit. When only the medians separate, the scenario is a **state shift**:
  some processes landed in a different discrete state, and the check passes. An unaccepted
  regression fails the check.
- Every worker process also measures `control`, a fixed workload with no Ranvier code. When the
  control regresses or improves by the same rule, the runtime's regressions on that leg are
  **inconclusive** and do not fail the check.
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
verdict updates without measuring again.

### Reusing measurements

A run measures nothing when a completed Perf run of this repository, among the 100 newest, has
measured a base and head with the same trees. The figures are restated for the new commits, with a
`reused` label naming the source run. When a pull request starts at the tip of `master`, the push to
`master` that squash-merges it reuses the pull request's measurement. A pull request behind `master`
is measured again on the push. An accepted regression stays in the report and in the
history, with its reason.

### Performance history

Each push to `master` that changes more than documentation is measured against its parent. The Perf
report workflow then records the run on the `perf-data` branch, and the Docs workflow rebuilds the
site's benchmark charts from it.

A commit that changes nothing measured beyond documentation (workflows, repository tooling) can
stay out of that history with a `Perf-Ignore` trailer and a reason in its footer:

```
ci: cache the .NET SDK

Perf-Ignore: workflow change
```

CI then skips measuring and recording the commit, and still redeploys the docs for it. The next
commit is measured against it as usual. `Perf-Ignore` applies to `master` pushes only: pull requests
are always measured, and `Perf-Accepted` is the way to pass an intended regression. An empty reason
fails the workflow.
