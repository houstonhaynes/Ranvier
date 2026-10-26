# Equinox/FsCodec state-store sample

> **For agentic workers:** Use superpowers:executing-plans to implement the checked steps.

**Goal:** Complete #185's Equinox/FsCodec sample with a real backend, shared conformance tests and a runnable persisted-signal round trip.

**Spec:** [Accepted adapter scope](2026-10-26-issue-185.md).

**Architecture:** A source sample outside core implements IStateStore using an Equinox Decider.
FsCodec.SystemTextJson encodes versioned snapshot events. Equinox.EventStoreDb commits those events to
an adapter-owned stream per scope, tested against KurrentDB. Equinox retries stream conflicts; the
decision must revalidate explicit record ETags against fresh state on every attempt.

**Tech stack:** .NET 10, Equinox.EventStoreDb 4.1.0, FsCodec.SystemTextJson 3.1.2, real KurrentDB integration tests.

## Constraints and decisions

- Sample and backend dependencies stay outside src/Ranvier and src/Ranvier.Persistence.
- None ETags remain unconditional. Some ETags require a live matching record, including after delete/recreation.
- SaveMany is atomic across the scope. Unrelated records retain their ETags.
- Use the same small-snapshot scope as the KurrentDB slice: at most 1 MiB of encoded data and bounded append attempts.
- Save, delete and clear append snapshots; history persists. Reserve the sample's distinct stream category.
- Caller owns the backend connection/client. Use no stale application cache in the sample.
- Ordinary PersistSession writes remain unconditional. Journal transports remain separate.
- This is a source sample, with IsPackable=false, rather than an additional supported package.

Backend selection is an implementation decision: Equinox's supported gRPC EventStoreDb backend works
with the KurrentDB server already used for #185. The test proves this combination. Other Equinox
backends can replace the category wiring but are not claimed compatible by this sample's tests.

## Tasks

### 1. Sample implementation and storage contract

Files: samples/Ranvier.Persistence.Equinox/StateStore.fs and its fsproj;
tests/Ranvier.Persistence.Equinox.Tests/Tests.fs, Main.fs and fsproj.

- [ ] Run the shared optimistic cases RED against the existing memory store.
- [ ] Verify Equinox category, codec, fold and Decider APIs in SageFs.
- [ ] Run shared storage and ETag suites GREEN against a real KurrentDB stream.
- [ ] Implement public sample state store, versioned event contract and explicit setup helper.
- [ ] Test stale/deleted records, mixed atomic rollback, independent client races, concurrent unrelated writes,
  isolated scopes, invalid Unicode, duplicate keys, serialized size limit, corrupt/unknown events and empty operations.
- [ ] Restore a signal and recompute a memo over a new graph and client.
- [ ] Reload the persisted sample and re-run its full suite.

### 2. Runnable example, documentation and CI

Files: sample README and executable Program.fs; docs/content/guide/persistence-equinox.md;
docs/Site.fs; docs/content/guide/persistence.md; .github/workflows/ci.yml.

- [ ] Make the sample executable read a connection from the environment, run a save/reload example and print the result.
- [ ] Document which work belongs to Ranvier, Equinox, FsCodec and the backing store.
- [ ] Document setup, round trip, explicit ETags, limitations, stream ownership and retained history.
- [ ] Add an integration CI job with a pinned real server, both trace variants, executable round trip and aggregate CI dependency.
- [ ] Missing/unavailable server fails tests; integration projects remain excluded from ordinary build CLI tests.
- [ ] Run full adapter suite in both variants, persistence regressions, executable round trip, fresh semantic check,
  XML/comment checks, formatting, menu coverage and site build.
- [ ] Review the complete branch independently, resolve actionable findings, open a PR and clear CI.

## Review focus

- Equinox retries must recheck record tokens, rather than silently accepting a stale command.
- Decoding an unknown or malformed event must fail rather than produce an empty scope.
- Snapshot loading strategy must preserve current records after many writes, and unrelated records' tokens.
- Serialization must preserve empty keys, case-sensitive Unicode keys and payloads.
- Logical deletion and physical stream retention must be distinguished in the guide.
