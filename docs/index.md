# Mermaider

Pure-.NET Mermaid diagram rendering — no JavaScript, no headless browser, no Node.js subprocess.

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
