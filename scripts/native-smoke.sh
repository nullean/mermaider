#!/usr/bin/env bash
# Publishes Mermaider.Native ahead of time for this machine's runtime identifier and runs the C smoke test against the
# library it makes: what a host in any other language does (load, check the version, render, read an error, free).
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) rid=osx-arm64; ext=dylib ;;
  Darwin-x86_64) rid=osx-x64; ext=dylib ;;
  Linux-x86_64) rid=linux-x64; ext=so ;;
  Linux-aarch64) rid=linux-arm64; ext=so ;;
  *) echo "no runtime identifier for $(uname -sm)" >&2; exit 2 ;;
esac
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
dotnet publish "$here/src/Mermaider.Native/Mermaider.Native.csproj" -r "$rid" -c Release -o "$out/lib" >/dev/null
lib="$out/lib/mermaider.$ext"
[ "$ext" = so ] && lib="$out/lib/mermaider.so"
echo "library: $(wc -c <"$lib" | tr -d ' ') bytes ($rid)"
cc -std=c99 -Wall -Wextra -Werror -o "$out/smoke" "$here/src/Mermaider.Native/smoke/smoke.c" -ldl
"$out/smoke" "$lib"
