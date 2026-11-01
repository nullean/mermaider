#!/usr/bin/env bash
# Build the documentation exactly as CI does, then serve it: docs-builder with the design-system layout, then
# scripts/docs-finish.sh (theme, page patches, landing page). `docs-builder serve` renders on demand and never applies
# any of that, so it would preview the wrong home page in the wrong theme.
#
#   scripts/docs.sh              build and serve on http://localhost:4000
#   scripts/docs.sh --noserve    build only
#
# The landing page gallery (docs/screenshots/<style>/) is generated and gitignored; run
# `dotnet run scripts/screenshots.cs` first if it is missing.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

# The sub-path GitHub Pages serves from; empty at the domain root (mermaider.nullean.net). The landing page's
# <base href> and the workflow's prefix: input must agree with it, and nothing but this check would notice if they
# drifted.
path_prefix=""
landing="docs/mermaider-landing.html"
workflow=".github/workflows/docs.yml"
expected_base="/"
[[ -n "$path_prefix" ]] && expected_base="/$path_prefix/"
actual_base="$(grep -o '<base href="[^"]*"' "$landing" | sed 's/<base href="//; s/"$//')"
if [[ "$actual_base" != "$expected_base" ]]; then
  echo "docs: $landing has <base href=\"$actual_base\"> but the site builds for \"$expected_base\"" >&2
  exit 1
fi
if [[ -z "$path_prefix" ]] && grep -q 'prefix:' "$workflow"; then
  echo "docs: $workflow has a prefix: input but path_prefix is empty" >&2
  exit 1
fi

# docs-builder is a native binary, not a dotnet tool; cached under .artifacts. Delete it to pick up a newer release,
# or set DOCS_BUILDER_VERSION to pin one. DOCS_BUILDER=<path> uses that binary instead (a local build or install).
tool="${DOCS_BUILDER:-.artifacts/tools/docs-builder}"
if [[ -z "${DOCS_BUILDER:-}" && ! -x "$tool" ]]; then
  case "$(uname -s)" in
    Darwin) os="mac" ;;
    *) os="linux" ;;
  esac
  arch="$(uname -m)"
  case "$arch" in
    x86_64) arch="x64" ;;
    aarch64) arch="arm64" ;;
  esac
  archive="docs-builder-$os-$arch.zip"
  version="${DOCS_BUILDER_VERSION:-latest}"
  if [[ "$version" == "latest" ]]; then
    url="https://github.com/elastic/docs-builder/releases/latest/download/$archive"
  else
    url="https://github.com/elastic/docs-builder/releases/download/$version/$archive"
  fi
  echo "docs: downloading $url"
  mkdir -p .artifacts/tools
  tmp="$(mktemp -d)"
  curl -fsSL "$url" -o "$tmp/$archive"
  unzip -o -q -j "$tmp/$archive" docs-builder -d .artifacts/tools
  rm -rf "$tmp"
  chmod +x "$tool"
fi

# The design-system layout is behind a feature flag, read from the environment only.
export FEATURE_NAVIGATION_PREVIEW=true
if [[ -n "$path_prefix" ]]; then
  "$tool" build --path docs --path-prefix "$path_prefix"
else
  "$tool" build --path docs
fi
out=".artifacts/docs/html"
scripts/docs-finish.sh "$out" "$path_prefix"

[[ "${1:-}" == "--noserve" ]] && exit 0
echo "docs: serving http://localhost:4000"
cd "$out" && exec python3 -m http.server 4000
