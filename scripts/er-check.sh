#!/usr/bin/env bash
# Fast ER metrics check — runs only the calibration report test.
# Use this after a layout change to see metric impact without the full ~3min suite.
#
# Usage:
#   ./scripts/er-check.sh            # show report
#   ./scripts/er-check.sh accept     # accept all received snapshots first, then report
#   ./scripts/er-check.sh diff SLUG  # diff our IR vs mjs IR for a slug

set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
SNAPSHOTS="$REPO/tests/Mermaider.Tests/Snapshots"
SKELETON="$SNAPSHOTS/LayoutSkeleton"
REFERENCE="$SKELETON/Reference"
REPORT="$SNAPSHOTS/Reference/mermaidjs/skeleton-comparison-report.md"

case "${1:-}" in
  accept)
    echo "Accepting received snapshots..."
    find "$SNAPSHOTS" -name "*.received.*" | while read -r f; do
      cp "$f" "${f//.received./.verified.}"
      echo "  accepted: $(basename "$f")"
    done
    echo "Done. Running calibration..."
    ;;
  diff)
    SLUG="${2:?Usage: er-check.sh diff SLUG}"
    MJS="$REFERENCE/${SLUG}.mjs.ir.txt"
    OURS="$SKELETON/LayoutSkeletonTests.Skeleton_snapshot_slug=${SLUG}.verified.txt"
    if [[ ! -f "$MJS" ]]; then echo "No mjs IR for '$SLUG'"; exit 1; fi
    if [[ ! -f "$OURS" ]]; then echo "No our IR for '$SLUG'"; exit 1; fi
    diff --color=always <(sed 's/^/mjs: /' "$MJS") <(sed 's/^/ours: /' "$OURS") || true
    exit 0
    ;;
  sidediff)
    # Side-by-side diff, easier to read
    SLUG="${2:?Usage: er-check.sh sidediff SLUG}"
    MJS="$REFERENCE/${SLUG}.mjs.ir.txt"
    OURS="$SKELETON/LayoutSkeletonTests.Skeleton_snapshot_slug=${SLUG}.verified.txt"
    if [[ ! -f "$MJS" ]]; then echo "No mjs IR for '$SLUG'"; exit 1; fi
    if [[ ! -f "$OURS" ]]; then echo "No our IR for '$SLUG'"; exit 1; fi
    diff -y --width=160 "$MJS" "$OURS" || true
    exit 0
    ;;
  routes)
    # Show only edge/route line diffs for a slug
    SLUG="${2:?Usage: er-check.sh routes SLUG}"
    MJS="$REFERENCE/${SLUG}.mjs.ir.txt"
    OURS="$SKELETON/LayoutSkeletonTests.Skeleton_snapshot_slug=${SLUG}.verified.txt"
    echo "=== mjs routes ==="
    grep -E "^\s+.*\[.*\].*\[.*\]" "$MJS" || true
    echo "=== ours routes ==="
    grep -E "^\s+.*\[.*\].*\[.*\]" "$OURS" || true
    exit 0
    ;;
esac

cd "$REPO"
dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj -- \
  --treenode-filter "/*/*/SkeletonComparisonTests/Calibration_report" 2>&1 | grep -v "^skipped\|Run manually"

echo ""
echo "=== Report ==="
cat "$REPORT"
