#!/usr/bin/env bash
# The differential check: `bash proofs/differential/run.sh [count] [seed]`, from any directory and any shell. Generates
# programs, runs each on the F# core (Generate.fsx) and on the extracted interpreter (driver.ml), and compares the traces
# and the end states. Agreement is evidence that the model matches the F#, not a proof.
#
# Exits 0 when every compared program agrees, when each program without a comparison diverged on both sides, when those
# stay under 5%, when excluded programs stay within 1%, and when a run of 200 or more reaches each floor Generate.fsx
# requires. Otherwise prints the first offending program, both sides, and the seed.
#
# A program that ends the F# process by a stack overflow, identified from the runtime's message, or by the watchdog, is
# `fatal`: this script records it with its cause and resumes at the next index. It diverged on both sides only when the
# model has no result either. Any other nonzero exit fails the check.
#
# Needs dotnet and ocamlopt. Under WSL it runs the Windows dotnet.exe through interop, as check.sh does, and takes OCaml
# from the opam switch of VERSIONS.md. RANVIER_DOTNET names another dotnet. A missing tool fails the check.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$(uname -s)" != "Linux" ]; then
  distro="${RANVIER_WSL_DISTRO:-Ubuntu}"
  wsl_here="$(MSYS_NO_PATHCONV=1 wsl.exe -d "$distro" -- wslpath -u "$(cygpath -m "$here")" | tr -d '\r')"
  # WSLENV carries the skip mode into the distribution.
  exec env MSYS_NO_PATHCONV=1 WSLENV="RANVIER_SKIP_MODE${WSLENV:+:$WSLENV}" \
    wsl.exe -d "$distro" -- bash "$wsl_here/$(basename "${BASH_SOURCE[0]}")" "$@"
fi

count="${1:-200}"
seed="${2:-1}"
fuel="${RANVIER_DIFF_FUEL:-2000}"
# The skip mode of the model side: off (the default, and CI's), leave-dirty, unchecked or all. See theories/Pure/Optimised.v.
mode="${RANVIER_SKIP_MODE:-off}"

if ! command -v ocamlopt >/dev/null 2>&1; then
  export PATH="$HOME/.local/bin:$PATH"
  if command -v opam >/dev/null 2>&1; then
    eval "$(opam env --switch=ranvier-proofs --set-switch)"
  fi
fi
command -v ocamlopt >/dev/null 2>&1 || { echo "run.sh: no ocamlopt found" >&2; exit 1; }

find_dotnet() {
  if [ -n "${RANVIER_DOTNET:-}" ]; then echo "$RANVIER_DOTNET"; return 0; fi
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return 0; fi
  if command -v dotnet.exe >/dev/null 2>&1; then command -v dotnet.exe; return 0; fi
  if [ -x "/mnt/c/Program Files/dotnet/dotnet.exe" ]; then echo "/mnt/c/Program Files/dotnet/dotnet.exe"; return 0; fi
  return 1
}
dotnet_cmd="$(find_dotnet)" || { echo "run.sh: no dotnet found" >&2; exit 1; }

# Windows dotnet resolves a relative path from the Windows view of this directory, so everything stays inside the checkout.
cd "$here/.."
work="$(mktemp -d -p differential .run.XXXXXX)"
trap 'rm -rf "$work"' EXIT

"$dotnet_cmd" build ../src/Ranvier -c Release -f net10.0 --nologo -v q >"$work/build.log" 2>&1 \
  || { cat "$work/build.log" >&2; echo "run.sh: dotnet build of src/Ranvier failed" >&2; exit 1; }

cp differential/engine.ml differential/engine.mli differential/driver.ml "$work/"
(cd "$work" && ocamlopt -w -a engine.mli engine.ml driver.ml -o driver) \
  || { echo "run.sh: building the driver failed" >&2; exit 1; }

# The extracted interpreter recurses as deep as its fuel.
ulimit -s unlimited 2>/dev/null || ulimit -s "$(ulimit -Hs)"

run_model() {
  [ -f "$work/ocaml.txt" ] && return 0
  "$work/driver" "$fuel" "$mode" <"$work/programs.txt" >"$work/ocaml.txt" \
    || { echo "run.sh: the driver failed at seed $seed" >&2; exit 1; }
}

# Runs the F# from an index. Each pass ends with every program recorded, or with one fatal program recorded.
next=0
fatal=0
touch "$work/fsharp.txt"
while :; do
  rm -f "$work/current.txt"
  if [ "$next" -eq 0 ]; then
    set -- generate "$count" "$seed" "$work"
  else
    set -- run "$work" "$next"
  fi
  rc=0
  "$dotnet_cmd" fsi differential/Generate.fsx "$@" >"$work/fsi.log" 2>&1 || rc=$?
  [ "$rc" -ne 0 ] || break
  if [ ! -s "$work/current.txt" ]; then
    tail -20 "$work/fsi.log" | tr -d '\r' | cut -c1-300 >&2
    echo "run.sh: Generate.fsx exited with $rc and no recorded program, at seed $seed" >&2
    exit 1
  fi
  i="$(tr -d '\r\n' <"$work/current.txt")"
  reason="$(grep -m1 -E '^(watchdog:|Stack overflow)' "$work/fsi.log" | tr -d '\r' || true)"
  program="$(sed -n "$((i + 1))p" "$work/programs.txt")"
  if [ -z "$reason" ]; then
    tail -20 "$work/fsi.log" | tr -d '\r' | cut -c1-300 >&2
    printf 'run.sh: Generate.fsx exited with %s, neither a stack overflow nor the watchdog, at seed %s, program %s\nprogram: %s\n' \
      "$rc" "$seed" "$i" "$program" >&2
    exit 1
  fi
  printf '%s\tfatal\t\t\t\t\n' "$i" >>"$work/fsharp.txt"
  run_model
  model="$(sed -n "$((i + 1))p" "$work/ocaml.txt" | cut -f2)"
  if [ "$model" != "none" ]; then
    printf 'run.sh: the F# process died with the model finishing, at seed %s, program %s (%s)\nprogram: %s\nmodel:   %s\n' \
      "$seed" "$i" "$reason" "$program" "$(sed -n "$((i + 1))p" "$work/ocaml.txt" | cut -f3)" >&2
    exit 1
  fi
  printf 'fatal on both sides: seed %s, program %s (%s)\nprogram: %s\n' "$seed" "$i" "$reason" "$program"
  fatal=$((fatal + 1))
  if [ $((fatal * 20)) -ge "$count" ]; then
    echo "run.sh: $fatal of $count programs fatal, 5% or more, at seed $seed" >&2
    exit 1
  fi
  next=$((i + 1))
  [ "$next" -lt "$count" ] || break
done
run_model

set +e
awk -F '\t' -v seed="$seed" -v count="$count" -v fuel="$fuel" -v programs="$work/programs.txt" -v floors="$work/floors.txt" '
function program(i,   line, n) {
  n = 0
  while ((getline line < programs) > 0) { if (n == i) { close(programs); return line } n++ }
  close(programs); return "<absent>"
}
function differ(i, why, f, o) {
  printf "run.sh: %s at seed %d, program %d\nprogram: %s\nF#:      %s\nmodel:   %s\n", why, seed, i, program(i), f, o > "/dev/stderr"
  failed = 1
  exit 1
}
FILENAME == ARGV[1] {
  gsub(/\r/, "")
  fstatus[$1] = $2; ftrace[$1] = $3; fended[$1] = $4; fclosed[$1] = $5; fcoverage[$1] = $6; fsharp++; next
}
{
  i = $1; ostatus = $2; ocaml++
  if (!(i in fstatus)) differ(i, "no F# result", "<absent>", $3)
  s = fstatus[i]
  if (s == "harness") differ(i, "the F# harness failed", ftrace[i], $3)
  if (s == "stuck") differ(i, "the F# is stuck, which the generator rules out", "<stuck>", ostatus " " $3)
  if (s == "excluded") { excluded++; next }
  if (s == "fatal") { fatals++; next }
  if (s == "budget") {
    if (ostatus != "none") differ(i, "the F# passed its budget and the model finished", "<budget>", $3)
    budget++; next
  }
  if (ostatus == "none") differ(i, "the model ran out of fuel or stuck and the F# finished", ftrace[i], "<none>")
  if (ftrace[i] != $3) differ(i, "the traces differ", ftrace[i], $3)
  if (fended[i] != $4) differ(i, "the end states differ", fended[i], $4)
  if (fclosed[i] != $5) differ(i, "the states after the closing differ", fclosed[i], $5)
  compared++
  n = split(fcoverage[i], items, ";")
  for (j = 1; j <= n; j++) {
    split(items[j], pair, "=")
    covered[pair[1]] += pair[2]
  }
}
END {
  if (failed) exit 1
  if (fsharp != count + 0 || ocaml != count + 0) {
    printf "run.sh: expected %d results, F# wrote %d and the driver %d\n", count, fsharp, ocaml > "/dev/stderr"
    exit 1
  }
  skipped = budget + fatals
  printf "seed %d, fuel %d: %d programs compared and agree on traces and end states (evidence, not a proof)\n", seed, fuel, compared
  printf "skipped: %d diverged on both sides, the F# past its budget and the model out of fuel\n", budget
  printf "fatal: %d ended the F# process, the model out of fuel\n", fatals
  printf "excluded: %d of a cap of %d programs raising more than one zero divisor\n", excluded, int(count / 100)
  verdict = 0
  if (skipped * 20 >= count) {
    printf "run.sh: %d of %d skipped, 5%% or more, at seed %d: the generator bounds are too loose\n", skipped, count, seed > "/dev/stderr"
    verdict = 1
  }
  if (excluded > int(count / 100)) {
    printf "run.sh: %d programs excluded at seed %d, above the cap of %d\n", excluded, seed, int(count / 100) > "/dev/stderr"
    verdict = 1
  }
  if (compared == 0) { print "run.sh: no program compared" > "/dev/stderr"; verdict = 1 }
  FS = "\t"
  while ((getline line < floors) > 0) {
    split(line, row, "\t")
    sub(/\r/, "", row[2])
    need = int(row[2] * count / 2000)
    if (count >= 200 && covered[row[1]] + 0 < need) {
      printf "run.sh: at seed %d the compared programs reached \"%s\" %d times, below the floor of %d\n", seed, row[1], covered[row[1]] + 0, need > "/dev/stderr"
      verdict = 1
    }
    printf "coverage: %s: %d (floor %d)\n", row[1], covered[row[1]] + 0, need
  }
  exit verdict
}' "$work/fsharp.txt" "$work/ocaml.txt"
verdict=$?
set -e

tr -d '\r' <"$work/stats.txt"
exit "$verdict"
