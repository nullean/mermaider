---
navigation_title: "Native library"
description: "Mermaider as a shared library with a C ABI: Mermaid text in, SVG out, from Swift, Rust, Go, Python, C and anything else with a C FFI."
---

# Native library

`Mermaider.Native` publishes Mermaider ahead of time as one shared library — `mermaider.dylib`, `mermaider.so` or
`mermaider.dll` — with a small C interface. A program in any language that can load a C library draws every diagram type
Mermaider has, with no .NET runtime and no browser.

```bash
dotnet publish src/Mermaider.Native -r osx-arm64 -c Release -o out     # osx-arm64, osx-x64, linux-x64, linux-arm64, win-x64
```

About 10 MB. `scripts/native-smoke.sh` publishes for the current machine and runs a C program against the result.

## The interface

Four functions, declared in [`mermaider.h`](https://github.com/nullean/mermaider/blob/main/src/Mermaider.Native/mermaider.h).
Text is UTF-8 and NUL-terminated.

| Function | |
|---|---|
| `int mermaider_abi_version(void)` | The version of this interface. Check it first; it changes when a function or the meaning of an option does, not when an option or a diagram type is added. |
| `char *mermaider_render_svg(const char *source, const char *options_json)` | The SVG for `source`, or `NULL`. Give the result back with `mermaider_free`. |
| `const char *mermaider_last_error(void)` | Why the last call on this thread returned `NULL`. Owned by the library; valid until the thread's next call. |
| `void mermaider_free(void *memory)` | Gives back what the library returned. |

Every call is safe from any thread, and a failure never crosses into the host as an exception.

## Options

`options_json` is a JSON object, or `NULL` for the defaults. An unknown key or a value of the wrong type is an error, so a
typo is never a silently unthemed diagram.

| Key | Value |
|---|---|
| `theme` | A built-in theme name (`mermaid --list-themes`): the base the colours below override. |
| `bg` `fg` `line` `accent` `muted` `surface` `border` `default` `success` `failure` `warning` `info` | CSS colours. |
| `dataPalette` | An array of colours for charts. |
| `font` `monoFont` `fontSize` | Strings: `"Inter"`, `"ui-monospace"`, `"14px"`. |
| `style` | `"quiet"`, `"blueprint"` or `"tonal"`. |
| `gradient` `transparent` `roundedEdges` | Booleans. |
| `tint` `padding` `nodeSpacing` `layerSpacing` | Numbers. `elevation` is 0 to 2. |
| `motion` `mode` | For a diagram with an [`animate` block](https://github.com/nullean/mermaider/tree/main/src/Mermaider.Animate): `"calm"`, `"crisp"` or `"still"`; `"loop"`, `"once"` or `"scrub"`. |

## From C

```c
void *library = dlopen("mermaider.dylib", RTLD_NOW);
char *(*render)(const char *, const char *) = dlsym(library, "mermaider_render_svg");
void (*release)(void *) = dlsym(library, "mermaider_free");

char *svg = render("graph TD\n  A-->B", "{\"theme\":\"tokyo-night\"}");
/* ... draw it ... */
release(svg);
```

## Animated diagrams

A source with an `animate` block comes back animated: the SVG plays it with CSS `@keyframes`, no script. The keyframes
animate only `opacity`, `transform` (`translate`, `scale`, `rotate`) and `stroke-dashoffset` (a line drawing itself), each
stop with a `cubic-bezier` easing, so a host that draws SVG itself can play it by evaluating them at a time. A source without
an `animate` block is exactly what it was.

## What the SVG has for a host that draws it itself

The SVG uses 19 element types (`svg g rect path circle ellipse line polyline polygon text tspan defs linearGradient
radialGradient stop marker image title style`), colours as CSS variables and `color-mix()`, and every diagram element carries
its identity: `data-id`, `data-from`, `data-to`, `data-shape`. A host that draws SVG itself can therefore hit-test nodes and
edges, which is how an editor makes a diagram's nodes jump to code.
