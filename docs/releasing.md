# Publish Ranvier packages

`Publish NuGet` (`.github/workflows/publish.yml`) publishes the exact package artifacts from successful
`master` CI. It includes every packable library under `src/` and their `.Traced` variants. Samples,
experiments and projects with `IsPackable=false` are excluded. The view, CSS and persistence packages publish from
[Ranvier.View](https://github.com/ranvier-org/Ranvier.View), [Ranvier.Css](https://github.com/ranvier-org/Ranvier.Css)
and [Ranvier.Persistence](https://github.com/ranvier-org/Ranvier.Persistence).

## Normal release

1. Bump the affected projects' `<Version>` values. Keep a library and its traced variant at the same
   version.
2. Merge after checks pass. CI builds and validates the packages, then uploads `nuget-packages`.
3. After successful `master` CI, publishing examines that run's package artifacts. Documentation-only CI is skipped.
   The workflow validates all artifacts and dependency feeds, lists missing NuGet versions, authenticates
   and pushes those versions in dependency order. Existing versions remain unchanged.
4. Check `Publish NuGet` for the release plan and successful uploads. The workflow completes when NuGet accepts the pushes.

Ordinary source edits do not mint new versions. Release them by bumping the relevant project versions.
NuGet package IDs and versions are immutable once published.

## Manual run and retry

Open **Actions → Publish NuGet → Run workflow** on `master`. Leave `dry_run` enabled to validate the
CI artifacts and inspect the plan without authentication or publishing. Disable it to publish.
The selected revision must have successful CI with an unexpired `nuget-packages` artifact.

A retry publishes versions still missing from NuGet. A failed dependency-feed query fails the release;
it is not treated as evidence that a package is absent.

## NuGet trusted publishing

The workflow exchanges GitHub's OIDC token for a short-lived NuGet key immediately before pushing.
Configure a policy on [NuGet Trusted Publishing](https://www.nuget.org/account/trustedpublishing):

- NuGet owner/user: `shayan.habibi` (or set the repository variable `NUGET_USER` for another owner).
- Repository owner: `ranvier-org`; repository: `Ranvier`.
- Workflow filename: **`publish.yml`**, without `.github/workflows/`.
- Environment: leave empty; this workflow does not declare an environment.
- Allow new packages and new versions for the `Ranvier*` and `Fable.Ranvier*` package patterns.

See [NuGet's trusted publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
for policy ownership and scope settings. The workflow stores no long-lived NuGet key.

Only the publishing job receives `id-token: write`. Pull requests run package validation with read
permissions. Actual publication requires successful CI from this repository's `master` branch.
