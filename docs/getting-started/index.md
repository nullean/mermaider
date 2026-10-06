# Getting started

Mermaider renders Mermaid diagram syntax to sanitized SVG in pure .NET — no JavaScript runtime, no headless browser, no subprocess.

## Installation

```bash
dotnet add package Mermaider
```

Targets **net10.0**. No transitive native or JavaScript dependencies.

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
    Style = DiagramStyle.Quiet,   // or Blueprint / Tonal
    Transparent = true,
    Strict = new StrictStylingOptions()
};

string svg = MermaidRenderer.RenderSvg(diagram, options);
```

See [Theming](../theming/index.md) for the colour tokens, the built-in themes, the style presets and the colour roles, and [Security](../security/index.md) for strict mode and sanitization options.

## Text output

`RenderAscii` draws flowcharts, state diagrams and XY charts as text, for terminals and logs:

```csharp
string text = MermaidRenderer.RenderAscii(diagram, new AsciiOptions { Width = 80 });
```

Set `AsciiOptions.Ascii = true` for plain ASCII instead of box-drawing characters. Other diagram types throw `NotSupportedException`.

## Command line

The `Mermaider.Cli` tool renders a file or standard input to SVG:

```bash
dotnet tool install -g Mermaider.Cli

mermaid diagram.mmd -o diagram.svg
mermaid diagram.mmd -t github-dark --style blueprint -o diagram.svg
echo 'graph TD; A-->B' | mermaid > diagram.svg
```

| Flag | Effect |
|---|---|
| `-i`, `--input <file>` | Input file (or pass it as the first argument; stdin when omitted) |
| `-o`, `--output <file>` | Output file (stdout when omitted) |
| `-t`, `--theme <name>` | A built-in theme; `--list-themes` prints the names |
| `--no-transparent` | Fill the background with the theme's `Bg` (transparent is the default) |
| `--style <quiet\|blueprint\|tonal>` | Style preset |
| `--no-gradient` | Flat fills |
| `--tint <0.5-1.5>` | Strength of the derived tints |
| `--elevation <0\|1\|2>` | Shadow strength |
| `--ascii`, `--plain`, `--width <n>` | Text output; `--plain` keeps to ASCII characters |

## Exceptions

| Exception | Thrown when |
|---|---|
| `MermaidParseException` | Input cannot be parsed |
| `MermaidResourceLimitException` | A resource limit is exceeded (subtype of `MermaidParseException`) |
| `MermaidSvgException` | Sanitizer rejects generated output in `Block` mode |
| `OperationCanceledException` | The `CancellationToken` or the `RenderDeadline` fires |

`MermaidResourceLimitException` is a subtype of `MermaidParseException` — existing `catch (MermaidParseException)` blocks automatically cover limit violations.

## Thread safety

`MermaidRenderer` is a static class. `RenderSvg` is thread-safe — call it concurrently from as many threads as you like. Internal `ObjectPool<StringBuilder>` instances are shared across calls.
