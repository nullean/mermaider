#!/usr/bin/env bash
# What both builds (scripts/docs.sh and .github/workflows/docs.yml) do after docs-builder: the mermaider theme appended
# to the design system's stylesheet, the built pages patched (scripts/docs-pages.py), the landing page over the
# generated index.html, and the fonts docs-builder doesn't copy. Needs a build with FEATURE_NAVIGATION_PREVIEW=true:
# without it the pages load no ds.css and this fails.
#
#   scripts/docs-finish.sh .artifacts/docs/html [path prefix]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:?usage: docs-finish.sh <docs-builder output dir> [path prefix]}"
prefix="${2:-}"

# The theme goes into the ds.css the pages actually load. Its name comes from docs-builder (plain ds.css with ?v=, or a
# hashed file), so read it from a built page instead of guessing. Before the landing page replaces index.html.
probe="$out/getting-started/index.html"
[[ -f "$probe" ]] || { echo "docs: $probe is missing, cannot find the design-system stylesheet" >&2; exit 1; }
href="$(grep -o 'href="[^"]*/_static/ds[^"/]*\.css[^"]*"' "$probe" | grep -v 'ds-layers' | head -n1 | sed 's/^href="//; s/"$//; s/?.*$//')" || true
if [[ -z "$href" ]]; then
  echo "docs: $probe loads no design-system ds.css; build with FEATURE_NAVIGATION_PREVIEW=true" >&2
  exit 1
fi
css="$out/_static/${href##*/_static/}"
[[ -f "$css" ]] || { echo "docs: $css (from $href) does not exist" >&2; exit 1; }
# An incremental docs-builder build keeps the previous ds.css, theme and all: cut it at the marker before appending.
marker="/* mermaider-docs.css: appended by scripts/docs-finish.sh */"
awk -v m="$marker" '$0 == m { exit } { print }' "$css" > "$css.tmp"
{ printf '%s\n' "$marker"; cat "$root/docs/mermaider-docs.css"; } >> "$css.tmp"
mv "$css.tmp" "$css"

# Root links that skip htmx, the header logo, and canonical/social URLs on this site's host: see scripts/docs-pages.py.
# The header logo is the docset's branding.icon; the host is the one GitHub Pages serves (docs/CNAME).
logo="$(sed -n 's/^  icon:[[:space:]]*//p' "$root/docs/_docset.yml" | head -n1)"
[[ -n "$logo" ]] || { echo "docs: docs/_docset.yml has no branding icon: for the header logo" >&2; exit 1; }
host="$(tr -d '[:space:]' < "$root/docs/CNAME")"
[[ -n "$host" ]] || { echo "docs: docs/CNAME is empty" >&2; exit 1; }
python3 -I "$root/scripts/docs-pages.py" "$out" "$root/docs" "$prefix" "$logo" "https://$host"

cp "$root/docs/mermaider-landing.html" "$out/index.html"
# docs-builder copies images, not fonts; the theme needs these.
mkdir -p "$out/fonts" && cp "$root"/docs/fonts/*.woff2 "$out/fonts/"
echo "docs: finished $out (theme appended to ${css#"$out"/}, landing page applied)"
