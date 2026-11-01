"""Patch the site docs-builder wrote, for scripts/docs-finish.sh.

    python3 -I scripts/docs-pages.py <output dir> <docs dir> <path prefix> <logo path, relative to docs> <site url>

- Links to the site root load it whole (hx-boost="false"). The root is the landing page, which has none of the docs
  shell, so htmx's boosted swap of #main-container would find nothing to swap and leave the reader on the docs.
- The header shows the docset's branding.icon. The design-system header has a slot for it (site-header__logo) that
  docs-builder never fills.
- Canonical, og:url and og:image point at this site. docs-builder writes them against its own default host and has no
  setting to change it. Every file is rewritten, not only HTML (the .md copies and config carry it too), byte for byte,
  so this behaves the same on macOS and on the Linux runner, unlike sed -i.
"""

import re
import sys
from pathlib import Path

out, docs, prefix, logo, site = (
    Path(sys.argv[1]),
    Path(sys.argv[2]),
    sys.argv[3].strip("/"),
    sys.argv[4].strip("/"),
    sys.argv[5].rstrip("/"),
)
root = f"/{prefix}/" if prefix else "/"
default_host = b"https://docs-v3-preview.elastic.dev"

if not (docs / logo).is_file():
    print(f"docs: the logo {docs / logo} does not exist", file=sys.stderr)
    sys.exit(1)
# The header shows the logo at the size its file declares (the landing page's nav size), 28px square without one.
svg = (docs / logo).read_text(encoding="utf-8") if logo.endswith(".svg") else ""
size = re.search(r'<svg\b[^>]*?\bwidth="(\d+)"[^>]*?\bheight="(\d+)"', svg)
width, height = size.groups() if size else ("28", "28")
logo_img = f'<img class="site-header__logo" src="{root}{logo}" alt="" width="{width}" height="{height}">'
# An animated logo whose root is id="still" stops at #still (its own :target rule). Under reduced motion the page picks
# that: the SVG's own prefers-reduced-motion rule is not enough, as an image document does not always see the page's
# media (Chromium's emulation never reaches it).
if re.search(r'<svg\b[^>]*\bid="still"', svg):
    logo_img = (
        f'<picture><source media="(prefers-reduced-motion: reduce)" srcset="{root}{logo}#still">{logo_img}</picture>'
    )
# Replaces a logo already there (an incremental build keeps pages patched by an earlier run, maybe another logo).
brand = re.compile(
    r'(<a\b[^>]*\bclass="site-header__brand"[^>]*>)'
    r'(?:\s*(?:<picture><source [^>]*>)?<img class="site-header__logo"[^>]*>(?:</picture>)?)?'
)
root_link = re.compile(r'<a\b(?![^>]*\bhx-boost=)([^>]*\bhref="' + re.escape(root) + r'")')
any_root = re.compile(r'<a\b[^>]*\bhref="' + re.escape(root) + r'"')

hosts = 0
for file in out.rglob("*"):
    if not file.is_file():
        continue
    data = file.read_bytes()
    if default_host in data:
        file.write_bytes(data.replace(default_host, site.encode()))
        hosts += 1

pages = links = logos = 0
seen_root = seen_brand = False
for page in out.rglob("*.html"):
    html = page.read_text(encoding="utf-8")
    seen_root = seen_root or bool(any_root.search(html))
    seen_brand = seen_brand or 'class="site-header__brand"' in html
    new, n_links = root_link.subn(r'<a hx-boost="false"\1', html)
    new, n_logos = brand.subn(lambda m: m.group(1) + logo_img, new)
    if new != html:
        page.write_text(new, encoding="utf-8")
        pages += 1
        links += n_links
        logos += n_logos

if not seen_root:
    print(f'docs: no links to "{root}" found; the docs-builder markup changed', file=sys.stderr)
    sys.exit(1)
if not seen_brand:
    print("docs: no site-header__brand found for the logo; the docs-builder markup changed", file=sys.stderr)
    sys.exit(1)
print(f"docs: {pages} pages patched ({links} root links unboosted, {logos} logos); {hosts} files pointed at {site}")
