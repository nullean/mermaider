---
name: mermaider-native
description: >-
  Use when a program that is not .NET (Swift, Rust, Go, Python, C, a native app) needs to render a Mermaid diagram to SVG
  by loading a shared library with a C FFI. Mermaider.Native is the NativeAOT build of Mermaider with a four-function C ABI.
---

# Mermaider.Native

One shared library per platform (`mermaider.dylib`, `mermaider.so`, `mermaider.dll`), about 10 MB, no .NET runtime. Build it
with `dotnet publish src/Mermaider.Native -r <rid> -c Release`; the header is `src/Mermaider.Native/mermaider.h`.
For rendering inside a .NET program, reference the `Mermaider` package instead.

## The calls you need

```c
int   mermaider_abi_version(void);                                   /* check it equals the header's MERMAIDER_ABI_VERSION */
char *mermaider_render_svg(const char *source, const char *options_json);  /* the SVG, or NULL */
const char *mermaider_last_error(void);                              /* why it was NULL; the library's, do not free */
void  mermaider_free(void *memory);                                  /* give back what render_svg returned */
```

`options_json` is a JSON object or NULL. Keys: `theme` (a built-in theme name, the base), colours `bg fg line accent muted
surface border default success failure warning info`, `dataPalette`, `font monoFont fontSize`, `style`
(`quiet blueprint tonal`), `gradient transparent roundedEdges`, `tint padding nodeSpacing layerSpacing`, `elevation` 0 to 2, and for a diagram with an
`animate` block `motion` (`calm crisp still`) and `mode` (`loop once scrub`).

## Gotchas

- An unknown option, or a value of the wrong type, returns NULL with the reason in `mermaider_last_error`: it is not ignored.
- `mermaider_last_error` is per thread and valid until that thread's next call. Copy it if you keep it.
- The assembly is `Mermaider.Native` and the published file is renamed to `mermaider.*` by the publish step; a build that
  produces a different file name did not go through `dotnet publish -r`.
- A source with an `animate` block comes back animated with CSS keyframes (`opacity`, `transform`, `stroke-dashoffset` only); a host
  that draws SVG itself evaluates them at a time to play it. Without the block nothing moves.
- Output is the same SVG `MermaidRenderer.RenderSvg` makes: colours are CSS variables and `color-mix()`, so a host that draws
  SVG itself needs to resolve those. Every diagram element has `data-id` (and `data-from`/`data-to` on edges) for hit-testing.
