# Mermaider

Pure-.NET Mermaid diagram rendering — no JavaScript, no headless browser, no Node.js subprocess.

![A flowchart with subgraphs rendered by Mermaider](images/diagrams/flowchart.svg)

## Quick start

```bash
dotnet add package Mermaider
```

```csharp
using Mermaider;

string svg = MermaidRenderer.RenderSvg("""
    graph TD
      A[Start] --> B{Check}
      B --> |yes| C[Done]
      B --> |no| D[Retry]
""");
```

`RenderSvg` returns a complete SVG string, sanitized and ready to embed directly in HTML.

## What it covers

- [24 diagram types](diagram-types/index.md), from flowcharts and sequence diagrams to sankey, treemap and packet diagrams.
- [One design system](theming/index.md) for every type: 15 built-in themes, three style presets (Quiet, Blueprint, Tonal) and colour roles for success, failure, warning and info.
- [Secure by default](security/index.md): an allowlist sanitizer on every SVG, optional strict styling, and resource limits that are on by default.
- [A built-in layered layout engine](layout/index.md) with no dependencies, also published on its own as the `Sugiyama` package.
