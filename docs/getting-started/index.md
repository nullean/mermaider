# Getting started

Mermaider renders Mermaid diagram syntax to sanitized SVG in pure .NET — no JavaScript runtime, no headless browser, no subprocess.

## Installation

```bash
dotnet add package Mermaider
```

Targets **net8.0** and later. No transitive native or JavaScript dependencies.

## Basic usage

```csharp
using Mermaider;

string svg = MermaidRenderer.RenderSvg("""
    flowchart LR
      A[Parse] --> B[Layout] --> C[Render SVG]
""");

// svg is a complete, sanitized SVG string — embed it directly in HTML
Response.Write(svg);
```

`RenderSvg` returns a self-contained SVG string. Write it into your HTML response, save it to disk, or pipe it anywhere — no further processing needed.

## Async and streaming

```csharp
// Write SVG bytes directly to an HTTP response stream
await MermaidRenderer.RenderSvgAsync(diagram, Response.Body, cancellationToken: ct);

// Or to a PipeWriter for zero-copy scenarios
await MermaidRenderer.RenderSvgAsync(diagram, pipeWriter, cancellationToken: ct);
```

The `CancellationToken` is honored throughout the full parse → layout → render pipeline, not only on the final write.

## Configuring the renderer

Pass a `RenderOptions` record to control colors, fonts, layout, and security behavior:

```csharp
var options = new RenderOptions
{
    Bg = "#0D1117",
    Fg = "#E6EDF3",
    Accent = "#58A6FF",
    Font = "Inter",
    Transparent = true,
    Strict = new StrictStylingOptions()
};

string svg = MermaidRenderer.RenderSvg(diagram, options);
```

See [Theming](../theming/) for the full color token reference, and [Security](../security/) for strict mode and sanitization options.

## Exceptions

| Exception | Thrown when |
|---|---|
| `MermaidParseException` | Input cannot be parsed |
| `MermaidResourceLimitException` | A resource limit is exceeded (subtype of `MermaidParseException`) |
| `MermaidSvgException` | Sanitizer rejects generated output in `Block` mode |

`MermaidResourceLimitException` is a subtype of `MermaidParseException` — existing `catch (MermaidParseException)` blocks automatically cover limit violations.

## Thread safety

`MermaidRenderer` is a static class. `RenderSvg` is thread-safe — call it concurrently from as many threads as you like. Internal `ObjectPool<StringBuilder>` instances are shared across calls.
