# Security

Mermaider is designed for untrusted input. Three independent defenses work together: allowlist SVG sanitization, strict styling mode, and resource limits.

## SVG sanitization

Every SVG Mermaider produces passes through `SvgSanitizer` before it reaches the caller. The sanitizer:

- Applies an **element and attribute allowlist**. Anything not explicitly permitted is stripped or throws. There is no blocklist: `<script>`, `<foreignObject>`, `on*` handlers and `href` are denied because they are not on the list.
- Checks attribute **values** too. Paint attributes accept colours, `color-mix()` and same-document `url(#id)` references only. The root `style` attribute accepts only the seven colour properties (`--bg`, `--fg`, `--line`, `--accent`, `--muted`, `--surface`, `--border`) with colour values, plus `background: var(--bg)`.
- Permits one `href`: on an `<image>` element, carrying a base64 `data:image/svg+xml` or `data:image/png` URI. Architecture diagram icons use it, and every icon is also sanitized when it is registered.
- Sets `DtdProcessing.Prohibit` and `XmlResolver = null` to block XXE and billion-laughs attacks.
- Strips `<style>` unconditionally from external SVG. Only the `<style>` emitted by Mermaider's own renderer can survive, and only after line-by-line validation against `RendererStylesheetAllowlist`.

### The generated stylesheet

`RendererStylesheetAllowlist` is a positive grammar for the one stylesheet the renderer writes. It accepts:

- The font rules, with the font names escaped.
- The derived colour tokens, compared line by line with the same source `StyleBlock` writes them from, so the two cannot drift.
- The four font-size tokens.
- Zero to two elevation (drop-shadow) rules, each exactly one that a style preset and `Elevation` level can produce. Shadow colours are mixed from the theme, never taken from input.
- Strict-mode `.cls-<name>` rules with hex colours only.

Gradients and markers live in `<defs>` and are referenced with `url(#id)`. Each id starts with a hash of the theme colours and design inputs, so several differently themed SVGs inlined in one page never pick up each other's definitions.

### Standalone use

`SvgSanitizer` is public and does not depend on the rendering pipeline. Use it to clean SVG from other sources:

```csharp
SvgSanitizeResult result = SvgSanitizer.Sanitize(untrustedSvg, SanitizeMode.Strip);
string clean = result.Svg;
```

### Sanitize mode

Control what happens when a violation is found in rendered output:

```csharp
// Strip violations silently (default)
var options = new RenderOptions { SanitizeMode = SanitizeMode.Strip };

// Throw MermaidSvgException on any violation
var options = new RenderOptions { SanitizeMode = SanitizeMode.Block };
```

In `Strip` mode, malformed XML returns `MermaidRenderer.FallbackSvg`, an empty SVG.

### Violation callbacks

In `Strip` mode, subscribe to violations without throwing:

```csharp
var options = new RenderOptions
{
    OnSanitized = violations =>
    {
        foreach (var v in violations)
            logger.LogWarning("SVG violation stripped: {Kind} {Name} on {Parent}", v.Kind, v.Name, v.ParentElement);
    }
};
```

The callback receives every violation found in a single render call. It is not called when there are no violations.

## Strict styling

By default Mermaider allows diagram-source styling directives (`classDef`, `style`, `linkStyle`, C4 `Update*Style`, and `theme` / `themeVariables` in `%%{init}%%`). In strict mode these are **rejected**: only caller-defined class names and colours reach the stylesheet.

```csharp
var options = new RenderOptions
{
    Strict = new StrictStylingOptions
    {
        AllowedClasses =
        [
            new DiagramClass { Name = "highlight", Fill = "#FEF3C7", Stroke = "#D97706" },
            new DiagramClass { Name = "success" },   // no colours: an external or role class
        ],
        Mode = StrictStylingMode.Strip,             // default; Block throws MermaidParseException
        OnStripped = stripped => { foreach (var s in stripped) logger.LogInformation("{Message}", s.Message); },
    }
};
```

Strict mode is a **visual uniformity** feature, not an addition to security. SVG output is always sanitized regardless. It prevents diagram authors from overriding your design system.

When `Strict` is set:

- `classDef`, `style`, `linkStyle`, C4 `Update*Style`, and `%%{init}%%` theme overrides in diagram source are dropped and reported through `OnStripped`, or throw in `Block` mode.
- Only classes listed in `AllowedClasses` may appear on nodes. A class with colours gets a `.cls-<name>` rule for light mode and a `prefers-color-scheme: dark` variant; a class without colours is passed through by name for your own stylesheet.
- The role classes `success`, `failure`, `warning` and `info` must be on the list too. Their colours always come from `RenderOptions` or the theme.
- All colour values in the stylesheet are caller-supplied (from `RenderOptions`): no diagram-source colour escapes.
- The design inputs (`Style`, `Gradient`, `Tint`, `Elevation`) are host-only options, so diagram source never sets them in any mode.

## Resource limits

`ResourceLimits.Default` is applied to every render call. Violations throw `MermaidResourceLimitException`, except the deadline, which throws `OperationCanceledException`:

| Limit | Default | Guards against |
|---|---|---|
| `MaxInputLength` | 512 KB | Memory exhaustion from oversized input |
| `MaxLines` | 10,000 | Aggregate regex-timeout budget |
| `MaxLineLength` | 8,000 chars | ReDoS on very long single lines |
| `MaxElements` | 5,000 | Pathological parse-time cost |
| `MaxNodesAfterLayout` | 20,000 | Virtual-node amplification in Sugiyama |
| `MaxRecursionDepth` | 64 | Stack exhaustion in tree renderers |
| `MaxOutputLength` | 8 MB | Unbounded SVG growth |
| `RenderDeadline` | 5 s | Cooperative wall-clock bound |

### Adjusting limits

Raise individual limits for legitimate large diagrams, or disable all checks for trusted server-side calls:

```csharp
// Raise one limit
var options = new RenderOptions
{
    Limits = ResourceLimits.Default with { MaxElements = 10_000 }
};

// Disable all limits for fully trusted input
var options = new RenderOptions { Limits = ResourceLimits.Unlimited };
```

### Cooperative deadline

`RenderDeadline` is a cancellation token that is checked between the pipeline phases and inside the Sugiyama layout (including the crossing-minimizer sweeps), not after every operation. It bounds the observed hotspots but is not a hard OS-level timer. `ResourceLimits.TimeProvider` sets the clock, so tests can use a fake one. To cancel from your side as well, pass a `CancellationToken`; Mermaider combines it with the deadline:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
await MermaidRenderer.RenderSvgAsync(diagram, stream, cancellationToken: cts.Token);
```

## ReDoS protection

Every regex pattern in the parser uses `[GeneratedRegex]` with `matchTimeoutMilliseconds: 2000`. The parsers turn a `RegexMatchTimeoutException` into a `MermaidParseException`.
