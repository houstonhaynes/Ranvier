# Toolchain

Platform: WSL, Ubuntu 24.04.4 LTS (distribution `Ubuntu`), opam 2.3.0 switch `ranvier-proofs`.
`build.sh` and `check.sh` re-execute themselves in that distribution when started from Git Bash;
set `RANVIER_WSL_DISTRO` to use another distribution name.

| Component | Version |
|---|---|
| OCaml | 4.14.2 (`ocaml-base-compiler`) |
| Rocq (`rocq-core`, `rocq-runtime`) | 9.1.0 |
| `rocq-stdlib` | 9.2.0 |
| stdpp (`rocq-stdpp`) | 1.13.0 |
| Iris (`rocq-iris`, `rocq-iris-heap-lang`) | 4.5.0 |
| zarith | 1.14 |

## Why WSL and not the Windows installer

Rocq Platform 2026.07.0 for Windows (Rocq 9.1.0, signed by INRIA) installs per user without
elevation, and the HeapLang smoke theorem compiles with it. It ships no `make`, which
`rocq makefile` requires, so `build.sh` cannot run there. WSL is the fallback the plan names.

## Reproducing the switch

The WSL account has no `sudo`. Three prerequisites were therefore installed in user space:
the `opam` binary, GMP 6.3.0 (headers for zarith, prefix `~/.local/gmp`), and a Python `unzip`
shim in `~/.local/bin` (opam requires an `unzip` command). `build.sh` exports the GMP paths
when `~/.local/gmp` exists.

```sh
export PATH=$HOME/.local/bin:$PATH
export CPATH=$HOME/.local/gmp/include LIBRARY_PATH=$HOME/.local/gmp/lib LD_LIBRARY_PATH=$HOME/.local/gmp/lib
export OPAMASSUMEDEPEXTS=1 OPAMYES=1
opam init --bare --disable-sandboxing --no-setup
opam switch create ranvier-proofs ocaml-base-compiler.4.14.2
eval "$(opam env --switch=ranvier-proofs)"
opam repo add rocq-released https://rocq-prover.org/opam/released
opam repo add iris-dev https://gitlab.mpi-sws.org/iris/opam.git
opam update
opam install -j16 rocq-core.9.1.0 rocq-stdlib rocq-iris-heap-lang.4.5.0
```

On a machine with `sudo`, install `libgmp-dev`, `unzip` and `bubblewrap` with apt and drop the
user-space workarounds and `--disable-sandboxing`.

## Differential check

`differential/run.sh 2000` takes about 15 seconds on the development machine, plus a process restart of about 5 seconds per
fatal program. `differential/run.sh` needs OCaml and the .NET SDK, and no Rocq, stdpp or Iris: it compiles the committed
`differential/engine.ml` with `ocamlopt` alone, with no opam package beyond the compiler. Locally it takes OCaml from the
switch above and the Windows `dotnet.exe` through interop. The workflow `proofs-differential.yml` installs OCaml 4.14.2 with
`ocaml/setup-ocaml@v3` and the SDK from `global.json` with `actions/setup-dotnet@v6`, on `ubuntu-latest`.

## Documentation

`docs.sh` renders the theories with [rocqnavi](https://github.com/affeldt-aist/rocqnavi), pinned at commit `95e370f`
(2026-11-04). Rocqnavi is GPL-2.0, so its source stays out of this repository: when the binary is missing, `docs.sh` clones it
at the pin into the ignored folder `docs/.rocqnavi/` and builds it. It has its own opam switch, and the project's switch stays
untouched. `docs.sh` also needs `python3`, and the pages load Markdown, syntax highlighting and fonts from CDNs when viewed.

```sh
opam switch create rocqnavi ocaml-base-compiler.4.14.2 --no-switch
opam install --switch=rocqnavi dune ocamlfind dune-glob yojson
```

`-external https://plv.mpi-sws.org/coqdoc/stdpp stdpp` links stdpp's names to its published documentation. Rocqnavi has no
script option, so `docs.sh` inserts the tag for `docs/ranvier.js` before `</body>`.

## Navigator site

`site.sh` builds a second rendering of the theories with `Nacara.Plugin.Rocq` and `FSharp.Data.Rocq` from
[FSharp.Data.Rocq](https://github.com/ranvier-org/FSharp.Data.Rocq), at the version in `site/Ranvier.Proofs.Site.fsproj`. It needs
this toolchain, `python3` and the .NET SDK, and no second opam switch.

The workflow `proofs-site.yml` runs `check.sh` and `site.sh` on `ubuntu-latest`, and uploads the pages as the artifact
`proofs-site`. The workflow `docs.yml` places the newest artifact from `master` at `/proofs/` of the Pages deployment. Its opam
cache key is the hash of this file, so a version change here rebuilds the toolchain. A version change also needs the matching
change to the install command in `proofs-site.yml`.

## Commands

- Build: `bash proofs/build.sh`
- Audit: `bash proofs/check.sh`
- Differential check: `bash proofs/differential/run.sh [count] [seed]`
- Documentation: `bash proofs/docs.sh`
- Navigator site: `bash proofs/site.sh`
- Run a command in the toolchain environment: `bash proofs/build.sh --run CMD ARGS`

## Smoke theorem

Compiles under both the Windows install and the WSL switch:

```rocq
From iris.heap_lang Require Import proofmode notation.
Lemma smoke `{!heapGS Σ} (l : loc) : l ↦ #1 ⊢ WP !#l {{ v, ⌜v = #1⌝ ∗ l ↦ #1 }}.
Proof. iIntros "H". wp_load. iFrame. done. Qed.
```
