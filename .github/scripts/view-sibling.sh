#!/usr/bin/env bash
# Clones ranvier-org/Ranvier.View beside this repository, if missing, and builds it against this core checkout.
# Usage: .github/scripts/view-sibling.sh [views-root]   (default: ../Ranvier.View)
set -euo pipefail

# `pwd -W` gives the Windows form of the path under Git Bash, which MSBuild reads.
core="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
views="${1:-$core/../Ranvier.View}"

if [ ! -d "$views" ]; then
  git clone --depth 1 https://github.com/ranvier-org/Ranvier.View.git "$views"
fi

printf '<Project><PropertyGroup><UseLocalRanvier>true</UseLocalRanvier><RanvierRoot>%s</RanvierRoot></PropertyGroup></Project>\n' \
  "$core" > "$views/Local.Build.props"
