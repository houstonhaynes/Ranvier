#!/usr/bin/env bash
# Clones ranvier-org/Ranvier.View and ranvier-org/Ranvier.Anime beside this repository, if missing, and builds both
# against this core checkout. Ranvier.View takes the anime.js bindings from the Ranvier.Anime checkout.
# Usage: .github/scripts/view-sibling.sh [views-root] [anime-root]   (defaults: ../Ranvier.View, ../Ranvier.Anime)
set -euo pipefail

# `pwd -W` gives the Windows form of the path under Git Bash, which MSBuild reads.
core="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
views="${1:-$core/../Ranvier.View}"
anime="${2:-${RanvierAnimeRoot:-$core/../Ranvier.Anime}}"

if [ ! -d "$views" ]; then
  git clone --depth 1 https://github.com/ranvier-org/Ranvier.View.git "$views"
fi
if [ ! -d "$anime" ]; then
  git clone --depth 1 https://github.com/ranvier-org/Ranvier.Anime.git "$anime"
fi

printf '<Project><PropertyGroup><UseLocalRanvier>true</UseLocalRanvier><RanvierRoot>%s</RanvierRoot></PropertyGroup></Project>\n' \
  "$core" > "$anime/Local.Build.props"
printf '<Project><PropertyGroup><UseLocalRanvier>true</UseLocalRanvier><RanvierRoot>%s</RanvierRoot><UseLocalRanvierAnime>true</UseLocalRanvierAnime><RanvierAnimeRoot>%s</RanvierAnimeRoot></PropertyGroup></Project>\n' \
  "$core" "$anime" > "$views/Local.Build.props"