# Mermaider

Pure-.NET Mermaid diagram rendering — no JavaScript, no headless browser, no Node.js subprocess.

## Quick start

```bash
dotnet add package Mermaider
```

```csharp
using Mermaider;

var renderer = new MermaidRenderer();
string svg = renderer.Render("""
    graph TD
      A[Start] --> B{Check}
      B --> |yes| C[Done]
      B --> |no| D[Retry]
""");
```

The `Render` call returns a complete SVG string, sanitized and ready to embed directly in HTML.
