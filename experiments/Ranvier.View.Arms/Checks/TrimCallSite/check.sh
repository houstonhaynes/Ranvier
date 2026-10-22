#!/usr/bin/env bash
# Publishes TrimCallSite trimmed for the runtime identifier in $1 (linux-x64 by default) and fails unless ILLink reports
# IL2026 at its Arms.Match call in Main.fs.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
log="$(mktemp)"
# An up-to-date publish skips ILLink and reports nothing, so each run starts from a clean output.
rm -rf "$here/bin" "$here/obj"
dotnet publish "$here/TrimCallSite.fsproj" -c Release -r "${1:-linux-x64}" -o "$here/bin/publish" 2>&1 | tee "$log"
if ! grep -E "Main\.fs\([0-9]+,[0-9]+\).*IL2026.*Ranvier\.View\.Arms\.Arms\.Match" "$log" > /dev/null; then
  echo "The trimmed publish reported no IL2026 at the Arms.Match call in Main.fs" >&2
  exit 1
fi
