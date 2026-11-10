#!/usr/bin/env bash
# Builds the HTML documentation of the proofs. Run as `bash proofs/docs.sh` from any directory and any shell.
#
#   1. builds the project, and fails when a .glob is older than its .v (its DIGEST line differs from the .v's MD5);
#   2. fails when a Theorem or Corollary has no (**md comment with a bold title directly above it;
#   3. builds rocqnavi at the pin of VERSIONS.md when its binary is missing, in its own opam switch;
#   4. generates the pages into docs/html, inserts ranvier.js, and fails when a page holds a theorem its outline lacks;
#   5. generates the guarantees overview and links it from the index.
#
# rocqnavi is a separate toolchain, so check.sh does not run this.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(uname -s)" != "Linux" ]; then
  distro="${RANVIER_WSL_DISTRO:-Ubuntu}"
  wsl_here="$(MSYS_NO_PATHCONV=1 wsl.exe -d "$distro" -- wslpath -u "$(cygpath -m "$here")" | tr -d '\r')"
  exec env MSYS_NO_PATHCONV=1 wsl.exe -d "$distro" -- bash "$wsl_here/$(basename "${BASH_SOURCE[0]}")" "$@"
fi

cd "$here"

rocqnavi_commit=95e370f
rocqnavi_switch=rocqnavi
rocqnavi_repo=https://github.com/affeldt-aist/rocqnavi.git
tool="docs/.rocqnavi/$rocqnavi_commit"
out=docs/html

command -v python3 >/dev/null 2>&1 || { echo "docs.sh: no python3 found" >&2; exit 1; }
export PATH="$HOME/.local/bin:$PATH"

log="$(mktemp)"
trap 'rm -f "$log"' EXIT
bash ./build.sh >"$log" 2>&1 || { cat "$log" >&2; echo "docs.sh: build failed" >&2; exit 1; }

# 1. A stale .glob gives rocqnavi the positions of an older file.
stale=0
while IFS= read -r f; do
  glob="${f%.v}.glob"
  digest="$(sed -n '1s/^DIGEST //p' "$glob" 2>/dev/null || true)"
  if [ "$digest" != "$(md5sum "$f" | cut -d' ' -f1)" ]; then
    echo "docs.sh: $glob is stale: its DIGEST is not the MD5 of $f. Recompile $f." >&2
    stale=1
  fi
done < <(grep -E '^theories/.*\.v$' _CoqProject)
[ "$stale" -eq 0 ] || exit 1

# 2.
python3 docs/guarantees.py comments

# 3. GPL-2.0, so the source stays out of this repository: it is cloned into an ignored folder.
rocqnavi_binary() { find "$tool/_build/default/bin" -maxdepth 1 -type f -name '*.exe' 2>/dev/null | head -1; }
if [ -z "$(rocqnavi_binary)" ]; then
  export OPAMYES=1 OPAMASSUMEDEPEXTS=1
  if ! opam switch list --short | grep -qx "$rocqnavi_switch"; then
    opam switch create "$rocqnavi_switch" ocaml-base-compiler.4.14.2 --no-switch
  fi
  opam install --switch="$rocqnavi_switch" dune ocamlfind dune-glob yojson
  rm -rf "$tool"
  mkdir -p "$(dirname "$tool")"
  git clone --quiet "$rocqnavi_repo" "$tool"
  git -C "$tool" checkout --quiet "$rocqnavi_commit"
  (eval "$(opam env --switch="$rocqnavi_switch" --set-switch)"; cd "$tool"; dune build --root . bin)
fi
binary="$(rocqnavi_binary)"
[ -n "$binary" ] || { echo "docs.sh: building rocqnavi gave no binary under $tool" >&2; exit 1; }

# 4. rocqnavi has no script option, so the tag is inserted here.
rm -rf "$out"
mkdir -p "$out"
mapfile -t inputs < <(find theories -name '*.v' -o -name '*.glob')
"$binary" -title "Ranvier reactive core proofs" -d "$out" -Q theories Ranvier \
  -external https://plv.mpi-sws.org/coqdoc/stdpp stdpp -custom-css docs/ranvier.css "${inputs[@]}" >"$log" 2>&1 \
  || { cat "$log" >&2; echo "docs.sh: rocqnavi failed" >&2; exit 1; }
cp docs/ranvier.js "$out/ranvier.js"
find "$out" -maxdepth 1 -name '*.html' -exec sed -i 's#</body>#<script src="ranvier.js"></script></body>#' {} +
python3 docs/guarantees.py outline "$out"

# 5.
python3 docs/guarantees.py overview "$out"

echo "docs.sh: wrote $here/$out/index.html"
if command -v wslpath >/dev/null 2>&1; then
  echo "docs.sh: open $(wslpath -w "$here/$out/index.html")"
fi
