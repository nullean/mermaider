/* Mermaider as a native library: Mermaid text in, SVG out. UTF-8, NUL-terminated strings throughout.
 *
 * Load libmermaider (mermaider.dylib / libmermaider.so / mermaider.dll) with the host's own dynamic loader and look the
 * functions up by name; nothing else is exported. Check mermaider_abi_version() first: it changes when a function or
 * the meaning of an option does, not when an option or a diagram type is added.
 *
 * Errors: a function that fails returns NULL, and mermaider_last_error() has the reason for the calling thread, until
 * that thread's next call into the library. The library never lets a failure cross into the host as an exception.
 * Every call is safe from any thread.
 */
#ifndef MERMAIDER_H
#define MERMAIDER_H

#ifdef __cplusplus
extern "C" {
#endif

#define MERMAIDER_ABI_VERSION 1

/* The version of this interface the library implements. */
int mermaider_abi_version(void);

/* Renders `source` (Mermaid text) to a self-contained SVG. `options_json` is a JSON object or NULL for the defaults:
 *   theme       a built-in theme name (the base of the colours; e.g. "tokyo-night")
 *   bg fg line accent muted surface border default success failure warning info    colours (CSS colours, "#rrggbb")
 *   dataPalette an array of colours for charts
 *   font monoFont fontSize                          strings ("Inter", "ui-monospace", "14px")
 *   style       "quiet" | "blueprint" | "tonal"
 *   gradient transparent roundedEdges               booleans
 *   tint padding nodeSpacing layerSpacing           numbers
 *   elevation   0 to 2
 * An unknown option or a value of the wrong type is an error. Returns the SVG, to be given back with mermaider_free, or
 * NULL (see mermaider_last_error). */
char *mermaider_render_svg(const char *source, const char *options_json);

/* The reason the last call on this thread failed, or NULL. Owned by the library: do not free it. */
const char *mermaider_last_error(void);

/* Gives back memory the library returned (the SVG). NULL is fine. */
void mermaider_free(void *memory);

#ifdef __cplusplus
}
#endif

#endif
