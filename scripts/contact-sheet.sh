#!/usr/bin/env bash
# Side-by-side PNGs (ours | mermaid.js) for the given gallery slugs, so layouts can be judged by eye.
# Usage: scripts/contact-sheet.sh OUTDIR slug [slug...]     (uses received snapshots when present, else verified)
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
SNAP="$REPO/tests/Mermaider.Tests/Snapshots"
# Nullean.Snapshots names a gallery case Gallery.<slug>.<argument hash>; received files live under .artifacts
VERIFIED="$REPO/tests/Mermaider.Tests/.snapshots/Snapshots/GallerySnapshotTests"
RECEIVED="$REPO/.artifacts/.snapshots/received/Mermaider.Tests/Snapshots/GallerySnapshotTests"
OUT="$1"; shift
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
mkdir -p "$OUT"
for slug in "$@"; do
  ours=""
  for f in "$RECEIVED/Gallery.$slug".????????.received.svg "$VERIFIED/Gallery.$slug".????????.verified.svg; do
    if [ -f "$f" ]; then ours="$f"; break; fi
  done
  [ -n "$ours" ] || { echo "no gallery snapshot for $slug" >&2; exit 1; }
  ref="$SNAP/Reference/mermaidjs/$slug.svg"
  html="$OUT/$slug.html"
  cat > "$html" <<HTML
<html><body style="margin:0;display:flex;background:#fff;font-family:sans-serif">
<div style="flex:1;padding:8px;border-right:1px solid #ccc"><div style="font-size:11px;color:#888">MERMAIDER $slug</div><img src="file://$ours" style="height:var(--h,680px);max-width:100%"></div>
<div style="flex:1;padding:8px"><div style="font-size:11px;color:#888">MERMAID.JS</div><img src="file://$ref" style="height:var(--h,680px);max-width:100%"></div>
</body></html>
HTML
  "$CHROME" --headless --disable-gpu --hide-scrollbars --screenshot="$OUT/$slug.png" --window-size=${SHEET_W:-1800},${SHEET_H:-760} "file://$html" >/dev/null 2>&1
done
