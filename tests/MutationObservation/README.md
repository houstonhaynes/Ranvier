# Mutation observation

`Shared.fsproj` exercises the provider-backed service, copied option presence, cancellation,
reentrant disposal, record draining, and callback retention. Its Node proxy is a test value;
it does not simulate DOM mutation delivery.

The provider's second Create callback reports provider-originated automatic closure. It clears
the shared observer's user callback and abort registration before releasing the engine handle,
including when Release reenters that closure notification. The browser provider uses ordinary
explicit disconnect/release; it does not synthesize a document-close event.

`Browser.fsproj` uses the actual browser MutationObserver through the compiled Fable adapter.
The Chromium fixture covers child, attribute and text records; old values including empty,
NUL and lone-surrogate strings; option errors/defaults; filter snapshots; transient subtree
registrations; deferred delivery; callback identity/error isolation; takeRecords/disconnect;
accessible foreign-document and iframe nodes; adoption and retained records after teardown.
The observer/service disposal error is the portable `MutationObserverException` named
`InvalidStateError`. Existing Ranvier signal/effect behavior is unchanged.

Run the complete dedicated fixtures from the repository root:

```sh
dotnet run --project tests/MutationObservation/Shared.fsproj -c Release
dotnet tool restore
dotnet fickl tests/MutationObservation/Browser.fsproj -c Release --noCache -o artifacts/mutation-browser
npm ci --prefix tests/MutationObservation
npx --prefix tests/MutationObservation playwright install --with-deps chromium
node tests/MutationObservation/run-browser.mjs artifacts/mutation-browser
```

The last command accepts an optional existing Chromium executable as its second argument.
The runner starts a loopback server, closes both browser and server in `finally`, and rejects
incomplete fixtures and unexpected page errors. These fixtures qualify the shared/browser
path. Native engine callbacks, batch retention and managed host/session routing require the
corresponding native/View tests.

The package consumer gate also compiles the same browser fixture through `PackageBrowser.fsproj`
against each normal/traced packed source graph. This checks the package's standalone compile list;
the source-project Chromium run above checks actual delivery.

The observation/defaulting rules follow the [DOM MutationObserver contract](https://dom.spec.whatwg.org/#interface-mutationobserver).
