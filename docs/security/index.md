# Security

Mermaider is designed for untrusted input. Three independent defenses work together: allowlist SVG sanitization, strict styling mode, and resource limits.

## SVG sanitization

Every SVG Mermaider produces passes through `SvgSanitizer` before it reaches the caller. The sanitizer:

- Applies an **element and attribute allowlist** — anything not explicitly permitted is stripped or throws
- Sets `DtdProcessing.Prohibit` and `XmlResolver = null` to block XXE and billion-laughs attacks
- Strips `<style>` unconditionally from external SVG — only `<style>` emitted by Mermaider's own renderer can survive, and only after line-by-line validation against `RendererStylesheetAllowlist`

### Sanitize mode

Control what happens when a violation is found in rendered output:

```csharp
// Strip violations silently (default)
var options = new RenderOptions { SanitizeMode = SanitizeMode.Strip };

// Throw MermaidSvgException on any violation
var options = new RenderOptions { SanitizeMode = SanitizeMode.Block };
```

### Violation callbacks

In `Strip` mode, subscribe to violations without throwing:

```csharp
var options = new RenderOptions
{
    OnSanitized = violations =>
    {
        foreach (var v in violations)
            logger.LogWarning("SVG violation stripped: {Element} {Attribute}", v.Element, v.Attribute);
    }
};
```

The callback receives every violation found in a single render call. It is not called when there are no violations.

## Strict styling

By default Mermaider allows diagram-source styling directives (`classDef`, `style`, `linkStyle`, `%%{init}%%`). In strict mode these are **rejected** — only caller-defined class names and colors reach the stylesheet:

```csharp
var options = new RenderOptions
{
    Strict = new StrictStylingOptions
    {
        AllowedClasses = ["highlight", "warning", "error"]
    }
};
```

Strict mode is a **visual uniformity** feature, not an addition to security — SVG output is always sanitized regardless. It prevents diagram authors from overriding your design system.

When `Strict` is set:

- `classDef`, `style`, `linkStyle`, and `%%{init}%%` in diagram source are ignored and reported via diagnostic callbacks
- Only classes listed in `AllowedClasses` may appear on nodes
- All color values in the stylesheet are caller-supplied (from `RenderOptions`) — no diagram-source color escapes

## Resource limits

`ResourceLimits.Default` is applied to every render call. Violations throw `MermaidResourceLimitException`:

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

`RenderDeadline` is checked at phase transitions and in the Sugiyama crossing-minimizer sweep — not after every arbitrary native call. It bounds the observed hotspots but is not a hard OS-level timer. For hard time bounds, combine it with `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
await MermaidRenderer.RenderSvgAsync(diagram, stream, cancellationToken: cts.Token);
```

## ReDoS protection

Every regex pattern in the parser uses `[GeneratedRegex]` with `matchTimeoutMilliseconds: 2000`. A regex that exceeds its timeout throws `RegexMatchTimeoutException`, which surfaces as `MermaidParseException`.
