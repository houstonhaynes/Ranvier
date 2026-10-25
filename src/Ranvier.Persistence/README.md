# Ranvier.Persistence

Keyed persisted inputs for Ranvier graphs. A persisted signal or keyed collection is restored from an `IStateStore`
when the graph is built, and saved back to it after each flush that changes it. Derived nodes are not stored: they
recompute from the restored inputs.

Preview API. The package holds three stores: an in-memory store, a file store (.NET, written to a temporary file and
renamed) and a `localStorage` store (Fable).
