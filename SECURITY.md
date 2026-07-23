# Security

Mermaider converts diagram source text into SVG. Because SVG is a powerful XML vocabulary capable of executing JavaScript, loading remote resources, and embedding arbitrary HTML, the library treats all SVG production as a security boundary and subjects every output — and every externally-supplied SVG passed to the sanitizer — to a mandatory sanitization pass before returning it to callers.

The sections below document the concrete attack classes present in weaponized-SVG campaigns and how Mermaider mitigates each one.

## Threat model

Mermaider accepts untrusted diagram source text (e.g. user-authored Mermaid markup) and produces SVG. The SVG is typically embedded in a web page or document viewer. An attacker who controls the diagram source must not be able to:

- execute JavaScript in the viewer's origin
- load or exfiltrate data from remote servers
- redirect the user to an attacker-controlled page
- embed an HTML phishing form
- deliver a payload via a `data:` URI

Mermaider also exposes `SvgSanitizer.Sanitize()` as a public API for callers who want to sanitize externally-produced SVG. The same threat model applies there.

## Architecture: pure allowlist

The sanitizer (`SvgSanitizer.cs`) is built on a **pure allowlist**. An element, attribute, or attribute value must be explicitly permitted by a positive rule to survive. There is no blocklist of "bad" things to block — unknown constructs are rejected by default. This means novel attack techniques that have not been specifically anticipated are denied without requiring a rule update.

Every render output passes through a mandatory `SvgSanitizationStage` before being returned. This is not optional and cannot be bypassed by callers. Renderer-side XML escaping of user text is a first line of defense; the sanitizer is the unconditional backstop.

## Attack mitigations

### JavaScript execution

**Vectors:** `<script>` elements, `on*` event-handler attributes (`onload`, `onclick`, `onmouseover`, …), `javascript:` URIs in `href`.

**Mitigation:**

- `script` is absent from `DefaultAllowedElements`. Any `<script>` element is removed.
- All `on*` attributes are absent from `DefaultAllowedAttributes`. The check is a pure positive lookup — no novel event-attribute name can pass.
- `href` is absent from `DefaultAllowedAttributes`. The only path that allows `href` is a scoped rule on `<image>` elements that validates the full value against a strict regex anchored to `data:image/(svg+xml|png);base64,...`. A `javascript:` URI does not match and is stripped.

### External resource loading

**Vectors:** `<image href="https://...">` (tracking pixel, exfiltration), `<use href="...">`, `xlink:href` pointing to an external host, `<a href="...">` redirects.

**Mitigation:**

- `href` on any element other than `<image>` is stripped unconditionally.
- `href` on `<image>` must match `^data:image/(svg+xml|png);base64,...$`. Any `http://`, `https://`, protocol-relative `//host`, or relative URL fails the regex and is removed.
- `use` is absent from `DefaultAllowedElements` and is always stripped, eliminating `<use href="#...">` shadow-root tricks entirely.
- `a` (anchor) is absent from `DefaultAllowedElements`. No hyperlinks survive.
- `src` does not appear in `DefaultAllowedAttributes`.

### Embedded HTML (`foreignObject`, `iframe`)

**Vectors:** `<foreignObject><body xmlns="http://www.w3.org/1999/xhtml">` containing a phishing form, redirect, or full HTML page (seen in the Amatera Stealer / PureMiner campaign targeting Ukraine). `<iframe>` embedded in SVG to load external content. Both elements also enable a secondary attack: drawing the SVG to a `<canvas>` and calling `getImageData()` to extract rendered pixel data from sensitive DOM content (WHATWG HTML issue #12350).

**Mitigation:**

- `foreignObject` is absent from `DefaultAllowedElements`. The element and its entire subtree are removed.
- `iframe` is absent from `DefaultAllowedElements` and is always stripped. `object` and `embed` are likewise absent.
- Because `foreignObject` is unconditionally removed, SVG produced by Mermaider cannot be used to draw sensitive HTML content to canvas and extract it via `getImageData()`.

### Obfuscation — DTD entities, CDATA, namespace tricks, encrypted/encoded payloads

**Vectors:** `<!DOCTYPE` entity expansion to smuggle payloads; CDATA wrapping (`<script><![CDATA[...]]></script>`) to hide code from naive parsers; prefixed element names (`<s:script xmlns:s="http://www.w3.org/2000/svg">`); foreign-namespace lookalikes. Campaigns additionally obfuscate JavaScript payloads with base64 (`atob()`), XOR encryption, AES encryption, `eval()`, the `Function()` constructor, `fromCharCode()`, reverse/replace string transforms, and dummy decoy code — making the payload unreadable to signature scanners.

**Mitigation:**

- The XML reader is configured with `DtdProcessing = DtdProcessing.Prohibit` and `XmlResolver = null`. Any document containing a DOCTYPE declaration throws `XmlException`; the sanitizer catches it and returns the canonical safe empty document `<svg xmlns="http://www.w3.org/2000/svg"></svg>`. No content survives.
- Foreign-namespace elements whose local name matches an allowlisted element are rejected: elements must reside in the SVG namespace (`http://www.w3.org/2000/svg`).
- Prefixed SVG roots (`<s:svg xmlns:s="...">`) fail root validation.
- Namespace declarations on child elements are stripped; only `xmlns` and `xlink` declarations on the root element are permitted, and only with their canonical values.
- JavaScript obfuscation techniques are irrelevant here: they exist to hide payloads from signature scanners, but Mermaider's defence is structural — `<script>` is stripped by the element allowlist regardless of how the code inside it is encoded. A `<script>` whose content is AES-encrypted or CDATA-wrapped is still a `<script>` element; it is removed before any content is read.

### SMIL animation abuse

**Vectors:** `<animate>`, `<animateTransform>`, `<animateMotion>`, `<set>` used to modify `href` or inject script after load.

**Mitigation:**

- None of these elements appear in `DefaultAllowedElements`. All SMIL animation elements are removed unconditionally.

### CSS external loading and style injection

**Vectors:** `<style>@import url("https://...")</style>` to load remote resources or exfiltrate data; `background: url(...)` in a `style` attribute; injecting arbitrary CSS rules via user-authored styling directives (`classDef`, `style`, `linkStyle`).

**Mitigation — `<style>` elements:**

`style` is absent from `DefaultAllowedElements`. The public `SvgSanitizer.Sanitize()` API strips every `<style>` element unconditionally, with no exceptions and no way for a caller to re-enable it via a custom allowlist. A `<style>` element can only survive inside an SVG produced by Mermaider's own render pipeline, via the internal `SanitizeRendererOutput` path — there is no mechanism for externally-supplied SVG to retain CSS.

Even on the internal path, a `<style>` element only survives if `RendererStylesheetAllowlist.IsAllowed()` confirms it matches the exact structural grammar of what the renderer produces. This is a line-by-line positive grammar, not a filter of dangerous constructs:

- The first two lines must be exact `font-family` rules for `text` and `.mono` selectors with a fixed system-font fallback stack. Font names come from `RenderOptions.Font` (a caller API parameter, not diagram source) and are hex-escaped through `AppendCssString` so only `[A-Za-z0-9 _-]` characters appear literally — anything else becomes a `\HEX ` escape sequence that cannot break CSS syntax.
- The next ~17 lines are compared character-for-character against fixed constant strings (the theme custom-property block). A single byte of deviation fails validation.
- The font-size custom properties (`--fs-xs`, `--fs-s`, `--fs-m`, `--fs-l`) must match a strict numeric-with-unit regex.
- Two fixed drop-shadow lines are compared verbatim.
- Any remaining lines must each match one of two narrow patterns: a shape rule (`selector rect, polygon, circle, ellipse { fill: #hex; stroke: #hex; }`) or a text rule (`selector text { fill: #hex; }`), both with hex colors validated by `IsAllowedHexColor`. Selectors must begin with `.cls-`.
- No `@import`, `@charset`, `url()`, arbitrary properties, or unrecognised structure can pass this grammar.

This validation runs unconditionally on every render — including when `StrictStylingOptions` is active. If the renderer produced a stylesheet that fails the check (which would indicate a renderer bug), the output is rejected rather than passed through.

**Mitigation — style injection via diagram source:**

Without `StrictStylingOptions`, users can author `classDef`, `style`, and `linkStyle` directives. The only values from these directives that reach the stylesheet are colors, and each passes through `SvgValueAllowlist.IsAllowedColor()` / `IsAllowedHexColor()` before being written. A value like `red; } body { background: url(https://attacker.invalid/c2) }` fails the check and is never written.

When `StrictStylingOptions` is active, diagram-source user values are eliminated from the stylesheet entirely. `AllowedClasses` and their colors are defined by the caller in code; any `classDef`, `style`, `linkStyle`, or `%%{init}%%` theme directive in the diagram source is stripped before rendering. The only values that appear in the stylesheet are the fixed constant block, caller-configured font names, and caller-defined class colors — none of which come from diagram source. The line-by-line stylesheet validation still runs unconditionally regardless.

**Mitigation — `style` attributes and paint values:**

- `style` attributes are absent from `DefaultAllowedAttributes` and are stripped from all elements in external SVG. In renderer output, the SVG root's `style` attribute is validated by `SvgValueAllowlist.IsAllowedRootStyle()`, which permits only named CSS custom properties (`--bg`, `--fg`, `--line`, `--accent`, `--muted`, `--surface`, `--border`) set to allowed color values, plus optionally `background: var(--bg)`. `background: url(...)` is rejected.
- Paint attributes (`fill`, `stroke`, `stop-color`, etc.) permit `url()` only for same-document fragment references (`url(#id)`). External URLs and `data:` URIs in paint values are rejected by `SvgValueAllowlist.IsAllowedPaint()`.

### Malicious `data:` URIs

**Vectors:** `data:text/html;base64,...` to deliver an HTML payload, `data:application/javascript;base64,...`, or a `data:image/svg+xml` URI containing active content.

**Mitigation:**

- Only `data:image/svg+xml;base64,...` and `data:image/png;base64,...` can survive.
- PNG: the decoded bytes must begin with the 8-byte PNG magic signature (`\x89PNG\r\n\x1a\n`). Anything else is rejected.
- SVG: the base64-decoded bytes are re-parsed as a fresh SVG string and passed through `SvgSanitizer.Sanitize()` recursively. If the nested SVG contains any violation (script, foreign element, style, event handler, etc.) the outer `href` is rejected. Recursion is depth-capped at 4 to prevent stack exhaustion.
- All other MIME types (`text/html`, `application/javascript`, `text/css`, …) do not match the pattern and are stripped.

### Injection via user text (node labels, edge labels, titles)

**Vectors:** A diagram node label of `</text><script>alert(1)</script>` or `" onmouseover="alert(1)` attempting to break out of an XML text node or attribute.

**Mitigation:**

- Every renderer writes user text through `MultilineUtils.AppendEscapedXml()` (text content) and `AppendEscapedAttr()` / `EscapeAttr()` (attribute values), which entity-encode `&`, `<`, `>`, `"`, and `'` before writing to the output buffer.
- Color values from diagram source and `%%{init}%%` themeVariables pass through `SvgValueAllowlist.IsAllowedColor()` before use; invalid values are silently replaced with the default.
- The mandatory final sanitization pass is defense-in-depth: a renderer bug that omits escaping would still be caught before output is returned.

## Icons and nested SVGs

Architecture and tree-view diagrams embed icons as `<image href="data:image/svg+xml;base64,..."/>`. Nested SVG support exists specifically for this feature and is intentional.

Icons are sanitized **twice**:

1. At registration time — `IconRegistry.Register()` runs the raw SVG through `SvgSanitizer.Sanitize()` and rejects icons containing any violation before they are stored.
2. At sanitization time — when the sanitizer encounters a `data:image/svg+xml` URI (e.g. in externally-supplied SVG), it decodes and re-sanitizes the nested SVG recursively, rejecting the `href` if the nested content has any violation.

This double sanitization means a malicious icon payload cannot survive even if one layer is somehow bypassed.

## Allowed elements and attributes

The complete positive allowlists are defined in `SvgSanitizer.cs` (`DefaultAllowedElements`, `DefaultAllowedAttributes`).

**Allowed elements (representative — structural, shape, text, gradient, filter, clip/mask):**
`svg` `g` `defs` `title` `desc` `rect` `circle` `ellipse` `polygon` `polyline` `line` `path` `text` `tspan` `marker` `image` `clipPath` `mask` `linearGradient` `radialGradient` `stop` `filter` `feGaussianBlur` `feOffset` `feBlend` `feFlood` `feComposite` `feMerge` `feMergeNode` `feDropShadow` `feColorMatrix` `feMorphology`

**Notably absent:** `script` `foreignObject` `use` `a` `animate` `animateTransform` `animateMotion` `set` `iframe` `object` `embed` `template` `link` `meta` `style`

**Allowed attributes (representative):**
Structural: `id` `class` `transform` `role` `viewBox` `preserveAspectRatio` `width` `height` geometric coordinates and dimensions, path data
Paint: `fill` `stroke` `stroke-width` `opacity` and related presentation attributes (values validated)
Text: `font-family` `font-size` `font-weight` `text-anchor` `dominant-baseline` and related
Gradient/filter/clip: gradient geometry, filter primitive attributes, clip/mask units
Prefixes: `data-*` and `aria-*`

**Notably absent:** any `on*` event handler `href` (except scoped image rule) `src` `action` `formaction` `ping` `xlink:href` (except scoped image rule)

## Test coverage

| Area | File | Scope |
|---|---|---|
| Element/attribute blocking | `SvgSanitizerTests.cs` | ~50 named tests covering each attack class |
| Value grammar allowlists | `SvgValueAllowlistTests.cs` | Positive and negative cases per grammar rule |
| Renderer stylesheet allowlist | `RendererStylesheetAllowlistTests.cs` | Line-by-line grammar validation |
| Deterministic mutation fuzz | `SvgSanitizerFuzzTests.cs` | 2 500 mutated payloads |
| Deterministic structured fuzz | `SvgSanitizerFuzzTests.cs` | 1 500 cross-product element × attribute cases |
| Sanitizer idempotency oracle | `SvgSanitizerFuzzTests.cs` | Every fuzz output re-sanitized; verified exact no-op |
| End-to-end injection resistance | `InjectionTests.cs` | ~75 cases across all diagram types |

The idempotency oracle independently re-parses every sanitizer output and asserts: no `<style>` element, no element outside the allowlist, no `href` except a validated `data:` URI on `<image>`, no namespaced attributes except `xmlns`/`xlink`, no processing instructions, `<image>` is empty, metadata elements contain only text nodes. Running the sanitizer a second time on its own output is verified to be an exact no-op, proving convergence.
