<p align="center">
  <img src="nuget-icon.png" alt="Mermaid.NET" width="96" />
</p>

<h1 align="center">Mermaid.NET</h1>

<p align="center">
  Render <a href="https://mermaid.js.org/">Mermaid</a> diagrams to SVG in pure .NET.<br/>
  No browser. No DOM. No JavaScript runtime. AOT-ready.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Mermaid"><img src="https://img.shields.io/nuget/v/Mermaid.svg" alt="NuGet" /></a>
  <a href="https://github.com/nullean/mermaid-dotnet/actions"><img src="https://github.com/nullean/mermaid-dotnet/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
</p>

---

## Attribution

This project is a **.NET port** of [**beautiful-mermaid**](https://github.com/lukilabs/beautiful-mermaid) by
[Craft Docs](https://craft.do) (lukilabs). Their TypeScript library pioneered the idea of rendering Mermaid
diagrams without a browser or DOM&mdash;fast, themeable, and synchronous.

`beautiful-mermaid` itself credits [**mermaid-ascii**](https://github.com/AlexanderGrooff/mermaid-ascii) by
Alexander Grooff for its ASCII rendering engine, which was ported from Go to TypeScript and extended.

We owe a huge thank-you to both projects for the excellent foundation.

### A note on how this was built

This codebase was written with a coding agent (Claude). That said, care was taken to follow modern .NET 10
idioms and keep allocations low: `ReadOnlySpan<char>` parsing, `[GeneratedRegex]` with ReDoS timeout guards,
`FrozenDictionary` / `FrozenSet` for hot-path lookups, `SearchValues<char>` for character classification,
object pooling, and file-scoped namespaces throughout. The benchmark numbers below reflect the result.

## Why This Fork?

**`beautiful-mermaid` is excellent&mdash;but it requires a JavaScript runtime** (Node.js / Bun). If you're
building in .NET, that means shelling out to a JS process, bundling a V8 engine, or running a headless browser.
All of these add latency, memory overhead, and deployment complexity.

**Mermaid.NET** is a from-scratch .NET implementation that:

| | beautiful-mermaid (TS) | Mermaid.NET (C#) |
|---|---|---|
| Runtime | Node.js / Bun | .NET 10+ |
| Layout engine | [ELK.js](https://github.com/kieler/elkjs) | Built-in [`Sugiyama`](src/Sugiyama/) (zero deps) |
| AOT compilation | N/A | Native AOT via `dotnet publish` |
| Strict mode | &mdash; | Forbid `classDef`/`style`, enforce class allowlist |
| SVG sanitization | &mdash; | Built-in allowlist-based sanitizer |
| Deployment | npm package | NuGet package / `dotnet tool` |

### Layout engine

Mermaid.NET ships a **lightweight, allocation-aware Sugiyama layout engine** with zero external dependencies.
It replaced [Microsoft MSAGL](https://github.com/microsoft/automatic-graph-layout) (Automatic Graph Layout),
which was the original engine used during initial development.

MSAGL is an excellent piece of research-grade software, but it was designed for a different era of .NET:

- **High allocations** &mdash; 554 KB per layout of a simple 6-node flowchart
- **Not AOT-friendly** &mdash; trim warnings, reflection-based internals
- **Opaque API** &mdash; limited ability to tune for diagram-specific use cases

The built-in Sugiyama engine is **122&times; faster** and uses **70&times; fewer allocations** for the
layout phase:

| Phase | MSAGL | Built-in | Improvement |
|---|---:|---:|---|
| Layout only | 227 &micro;s / 554 KB | 1.9 &micro;s / 8 KB | 122&times; faster, 70&times; less memory |
| End-to-end render | 356 &micro;s / 582 KB | 21 &micro;s / 38 KB | 17&times; faster, 15&times; less memory |

If you still need MSAGL for its higher-fidelity edge routing on complex graphs, install the optional package:

```bash
dotnet add package Mermaid.Layout.Msagl
```

```csharp
using Mermaid.Layout.Msagl;

// Global — all subsequent renders use MSAGL:
MermaidRenderer.SetLayoutProvider(new MsaglLayoutProvider());

// Or per-call:
var svg = MermaidRenderer.RenderSvg(input, new RenderOptions
{
    LayoutProvider = new MsaglLayoutProvider(),
});
```

### AOT-ready

Every public API is compatible with Native AOT. The CI pipeline publishes and invokes a native binary on
Linux, macOS, and Windows to prove it. No reflection, no runtime code generation, no surprises.

## Quick Start

```bash
dotnet add package Mermaid
```

```csharp
using Mermaid;

var svg = MermaidRenderer.RenderSvg("""
    graph TD
      A[Start] --> B{Decision}
      B -->|Yes| C[OK]
      B -->|No| D[End]
    """);
```

## Supported Diagrams

### Flowchart

```csharp
MermaidRenderer.RenderSvg("""
    graph TD
      A[Start] --> B{Decision}
      B -->|Yes| C[OK]
      B -->|No| D[Cancel]
      C --> E[End]
      D --> E
    """);
```

<p align="center"><img src="docs/screenshots/flowchart.svg" alt="Flowchart" /></p>

### Sequence

```csharp
MermaidRenderer.RenderSvg("""
    sequenceDiagram
      participant A as Alice
      participant B as Bob
      A->>B: Hello Bob!
      B-->>A: Hi Alice!
      A->>B: How are you?
      B-->>A: Great, thanks!
    """);
```

<p align="center"><img src="docs/screenshots/sequence.svg" alt="Sequence diagram" /></p>

### State

```csharp
MermaidRenderer.RenderSvg("""
    stateDiagram-v2
      [*] --> Idle
      Idle --> Processing : submit
      Processing --> Success : ok
      Processing --> Failed : error
      Success --> [*]
      Failed --> Idle : retry
    """);
```

<p align="center"><img src="docs/screenshots/state.svg" alt="State diagram" /></p>

### Class

```csharp
MermaidRenderer.RenderSvg("""
    classDiagram
      class Animal {
        <<abstract>>
        +String name
        +eat() void
      }
      class Dog { +bark() void }
      class Cat { +purr() void }
      Animal <|-- Dog
      Animal <|-- Cat
    """);
```

<p align="center"><img src="docs/screenshots/class.svg" alt="Class diagram" /></p>

### ER (Entity-Relationship)

```csharp
MermaidRenderer.RenderSvg("""
    erDiagram
      CUSTOMER ||--o{ ORDER : places
      ORDER ||--|{ LINE_ITEM : contains
      CUSTOMER {
        string name PK
        string email UK
      }
      ORDER {
        int id PK
        date created
      }
    """);
```

<p align="center"><img src="docs/screenshots/er.svg" alt="ER diagram" /></p>

## Theming

Every diagram derives its palette from just two colors&mdash;background and foreground&mdash;using
`color-mix()` CSS functions embedded in the SVG. Override individual roles for richer themes:

```csharp
var svg = MermaidRenderer.RenderSvg(input, new RenderOptions
{
    Bg = "#1E1E2E",
    Fg = "#CDD6F4",
    Accent = "#CBA6F7",    // arrow heads, highlights
    Muted  = "#6C7086",    // secondary text, labels
});
```

Because the SVG uses CSS custom properties, themes switch live without re-rendering&mdash;just update the
`--bg` / `--fg` properties on the root `<svg>` element.

## Strict Mode

When you embed user-authored Mermaid in a product, you typically want **uniform styling** controlled by your
design system&mdash;not arbitrary colors injected via `classDef` or `style` directives.

Strict mode:

- **Rejects** `classDef` and `style` directives at parse time (throws `MermaidParseException`)
- **Enforces** a pre-approved class allowlist with theme-aware colors
- **Generates** `@media (prefers-color-scheme: dark)` CSS for automatic light/dark switching
- **Auto-derives** dark mode colors by inverting HSL lightness (or use explicit overrides)

```csharp
var svg = MermaidRenderer.RenderSvg(input, new RenderOptions
{
    Strict = new StrictModeOptions
    {
        AllowedClasses =
        [
            new DiagramClass
            {
                Name = "ok",
                Fill = "#D4EDDA", Stroke = "#28A745", Color = "#155724",
                // dark variants auto-derived, or set explicitly:
                // DarkFill = "#1B4332", DarkStroke = "#2DD55B", DarkColor = "#A7F3D0",
            },
            new DiagramClass
            {
                Name = "warn",
                Fill = "#FFF3CD", Stroke = "#FFC107", Color = "#856404",
            },
            // External class — no colors, styling from your own CSS:
            new DiagramClass { Name = "custom-highlight" },
        ],
        RejectUnknownClasses = true,
        Sanitize = SvgSanitizeMode.Strip,   // also sanitize the final SVG output
    }
});
```

Nodes reference classes via Mermaid's `:::` shorthand or `class` directive:

```
graph TD
  A[Healthy]:::ok --> B[Warning]:::warn --> C[Custom]:::custom-highlight
```

## SVG Sanitization

A standalone, general-purpose SVG sanitizer is included&mdash;useful beyond Mermaid for any untrusted SVG content.

It enforces element and attribute allowlists, and **always** blocks the main XSS vectors regardless of the
allowlist: `<script>`, `<foreignObject>`, `on*` event handlers, `href`/`xlink:href` with `javascript:` URIs.

```csharp
var result = SvgSanitizer.Sanitize(untrustedSvg);

if (result.HasViolations)
    Console.WriteLine($"Stripped {result.Violations.Count} violations");

var cleanSvg = result.Svg;
```

Supply custom allowlists to restrict further:

```csharp
var result = SvgSanitizer.Sanitize(svg, myAllowedElements, myAllowedAttributes);
```

## CLI

```bash
dotnet tool install -g Mermaid.Cli

echo 'graph TD
  A --> B' | mermaid > diagram.svg

mermaid input.mmd -o output.svg --theme github-dark
mermaid --list-themes
```

## AOT Support

Mermaid.NET is fully compatible with .NET Native AOT. The CI pipeline validates this on every commit by
publishing and invoking a native binary across Linux, macOS, and Windows.

To publish your own AOT app that uses Mermaid.NET:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Mermaid" />
</ItemGroup>
```

```bash
dotnet publish -c Release
```

## Benchmarks

All five diagram types use the built-in lightweight Sugiyama engine (no MSAGL). Measured with
`[MemoryDiagnoser]` on .NET 10 (Apple M2 Pro):

| Method | Mean | Allocated |
|---|---:|---:|
| Flowchart (simple) | ~21 &micro;s | ~38 KB |
| Flowchart (large) | ~80 &micro;s | ~120 KB |
| Sequence | ~13 &micro;s | ~30 KB |
| State | ~25 &micro;s | ~45 KB |
| Class | ~18 &micro;s | ~35 KB |
| ER | ~20 &micro;s | ~40 KB |

Compared to the previous MSAGL-based layout, graph-based diagrams (flowchart, state, class, ER) are
**10&ndash;20&times; faster** end-to-end with **10&ndash;15&times; fewer allocations**.

Run the benchmarks yourself:

```bash
dotnet run --project tests/Mermaid.Benchmarks -c Release
```

## Building from Source

```bash
git clone https://github.com/nullean/mermaid-dotnet.git
cd mermaid-dotnet
./build.sh build
./build.sh test
```

## License

Apache 2.0 &mdash; see [LICENSE.txt](LICENSE.txt).
