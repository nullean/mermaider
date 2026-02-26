# Mermaid.NET

Render [Mermaid](https://mermaid.js.org/) diagrams to SVG in pure .NET. No browser, no DOM, no JavaScript runtime.

## Quick Start

```csharp
using Mermaid;

var svg = MermaidRenderer.RenderSvg("""
    graph TD
      A[Start] --> B{Decision}
      B -->|Yes| C[OK]
      B -->|No| D[End]
    """);
```

## Supported Diagram Types

| Type | Header |
|---|---|
| Flowchart | `graph TD`, `flowchart LR` |
| State | `stateDiagram-v2` |
| Sequence | `sequenceDiagram` |
| Class | `classDiagram` |
| ER | `erDiagram` |

## Theming

Pass colors via `RenderOptions` or use one of the 15 built-in themes:

```csharp
var svg = MermaidRenderer.RenderSvg(input, new RenderOptions
{
    Bg = "#1E1E2E", Fg = "#CDD6F4"  // catppuccin mocha
});
```

## Strict Mode

Reject `classDef`/`style` directives and enforce a pre-approved class allowlist:

```csharp
var svg = MermaidRenderer.RenderSvg(input, new RenderOptions
{
    Strict = new StrictModeOptions
    {
        AllowedClasses =
        [
            new DiagramClass { Name = "ok", Fill = "#D4EDDA", Stroke = "#28A745" },
        ]
    }
});
```

Dark mode variants are auto-derived or can be set explicitly. The SVG includes
`@media (prefers-color-scheme: dark)` rules.

## SVG Sanitization

General-purpose sanitizer with element/attribute allowlists:

```csharp
var result = SvgSanitizer.Sanitize(untrustedSvg);
if (result.HasViolations)
    Console.WriteLine($"Stripped {result.Violations.Count} violations");
var clean = result.Svg;
```

## CLI

```bash
dotnet tool install -g Mermaid.Cli

echo 'graph TD
  A --> B' | mermaid > diagram.svg

mermaid input.mmd -o output.svg --theme github-dark
mermaid --list-themes
```

## License

Apache 2.0 — see [LICENSE.txt](LICENSE.txt).
