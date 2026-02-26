# 00 — Project Overview & Architecture

## Goal

A pure .NET 10 library that renders Mermaid diagram text to SVG strings — no browser, no DOM, no native dependencies. The same "parse → layout → render" pipeline as [beautiful-mermaid](https://github.com/lukilabs/beautiful-mermaid), ported to idiomatic C#.

## Reference Implementation Analysis

beautiful-mermaid is ~5,500 lines of TypeScript across 18 source files. It has only two runtime dependencies:

| Dependency | Purpose | Size |
|---|---|---|
| `elkjs` | Graph layout (Eclipse Layout Kernel compiled to JS) | ~1.6 MB |
| `entities` | HTML entity decode/encode | Tiny |

Everything else — parsing, text measurement, SVG rendering, theming — is hand-written string manipulation and math.

## Architecture

```
Mermaid text
     │
     ▼
┌──────────┐     ┌──────────────┐     ┌──────────────┐
│  Parser  │────▶│ Layout Engine │────▶│ SVG Renderer │──▶ SVG string
└──────────┘     └──────────────┘     └──────────────┘
     │                  │                     │
 MermaidGraph    PositionedGraph         string (SVG)
```

### Stage 1: Parse

Regex-based line-by-line parsers convert Mermaid source text into a typed graph model (`MermaidGraph`). One parser per diagram type:

- **Flowchart / State** — `parser.ts` (~575 lines)
- **Sequence** — `sequence/parser.ts`
- **Class** — `class/parser.ts`
- **ER** — `er/parser.ts`

### Stage 2: Layout

The parsed graph is converted to ELK's JSON input format, laid out by ELK.js (layered algorithm, orthogonal edge routing), then converted back to a `PositionedGraph` with absolute x/y coordinates. Post-processing includes:

- Layer alignment (snap staggered nodes)
- Edge bundling (merge fan-out/fan-in)
- Shape-aware edge clipping (diamond vertices, etc.)

### Stage 3: Render

Pure string concatenation builds SVG markup from positioned data. No DOM API — just `StringBuilder` equivalent. Outputs `<svg>` with:

- CSS custom properties for theming (`--bg`, `--fg`, derived `--_text`, `--_line`, etc.)
- `<defs>` for arrow markers
- Groups, edges (polylines), nodes (shapes + labels)
- Text via `<text>` / `<tspan>` with inline formatting support

## Key Design Decisions for .NET Port

### 0. Allocation-awareness is a first-class concern

Every component is designed to minimize heap allocations. See [10-allocation-strategy.md](10-allocation-strategy.md) for the full playbook. Core techniques:

- **`[GeneratedRegex]` everywhere with 2s timeout** — compiles to `Span<T>`-optimized search routines, zero `Match` object allocations on the hot path; all patterns include `matchTimeoutMilliseconds: 2000` to guard against ReDoS from malicious input
- **`ReadOnlySpan<char>` as the default** — line iteration via `EnumerateLines()`, text measurement, regex matching all operate on spans
- **`ObjectPool<T>`** — pooled `StringBuilder` for rendering, pooled `Dictionary`/`List` for parser builder state
- **`SearchValues<char>`** — SIMD-optimized character set membership for text metrics
- **`ArrayPool<T>`** — for temporary buffers (subgraph stack, point arrays in edge processing)
- **`FrozenDictionary` / `FrozenSet`** — for all static lookup tables (themes, character widths)
- **Avoid LINQ in hot paths** — direct `for`/`foreach` loops over spans
- **Avoid string interpolation in hot loops** — `sb.Append()` chains instead

### 1. Layout engine strategy

ELK.js is a 1.6 MB JavaScript bundle transpiled from Java. Options:

| Option | Pros | Cons |
|---|---|---|
| **Microsoft.Msagl** | Native .NET, maintained by MS, supports layered layout | Different API, output may not match ELK pixel-for-pixel |
| **Jint + ELK.js** | Pixel-perfect ELK output, no algorithm porting | JS interpreter overhead, ~1.6 MB JS to load |
| **Port ELK from Java via IKVM** | Original algorithm | Heavy dependency, complex |
| **Custom layered layout** | Full control, no deps | Enormous effort, edge routing is hard |

**Recommendation:** Start with **Microsoft.Msagl** as the primary layout engine. It supports layered (Sugiyama) layout, orthogonal edge routing, and compound graphs. If output quality diverges too much, Jint + ELK.js is a solid fallback — the beautiful-mermaid wrapper around ELK is only ~300 lines.

### 2. String building

`ObjectPool<StringBuilder>` rented per render call. All SVG generation uses `sb.Append()` chains — no string interpolation in hot paths. The only unavoidable allocation is the final `sb.ToString()`.

### 3. Immutable data model

Use C# `record` types and `readonly record struct` for the graph model. The parsed graph and positioned graph are both immutable once created — records give us value equality, `with` expressions, and clean `ToString()` for free. `Point` is a `readonly record struct` (16 bytes, stack-allocated).

### 4. Parsing with minimal allocation

- **`[GeneratedRegex]`** for every regex pattern — compiles to `Span<T>` search routines
- **`text.AsSpan().EnumerateLines()`** — never `text.Split('\n')`
- **`ObjectPool<Dictionary<>>` / `ObjectPool<List<>>`** — pooled builder collections
- **`ArrayPool<T>`** for temporary buffers (subgraph stack, edge point arrays)
- **`FrozenDictionary`** for the output graph's node map
- **String interning** — node IDs allocated once, reused in edge references
- **`SearchValues<char>`** — SIMD-optimized character set membership in text metrics

### 5. API surface

```csharp
// Primary API — mirrors beautiful-mermaid's renderMermaidSVG()
public static class Mermaid
{
    public static string RenderSvg(string text, RenderOptions? options = null);
    public static string RenderSvg(ReadOnlySpan<char> text, RenderOptions? options = null);
}
```

### 6. No async in the core

Like beautiful-mermaid, the core rendering is **synchronous**. This is intentional — users can wrap in `Task.Run()` if they need async, but the default path avoids async overhead for what is fundamentally a CPU-bound computation.

## Elastic .NET Build Standards

This project follows the [Elastic .NET Build](https://improved-broccoli-v92811n.pages.github.io) conventions:

### Project Infrastructure
- **`.slnx`** solution format (not `.sln`)
- **Central Package Management** — all versions in `Directory.Packages.props`
- **MinVer** for tag-based semantic versioning (pre-release: `canary.0`)
- **F# build scripts** with Bullseye + Argu + Proc.Fs (`build/`)
- **Strong naming** — `build/keys/keypair.snk`
- **`UseArtifactsOutput: true`** — output to `.artifacts/`

### Code Style (`.editorconfig`)
- **Tab indentation** for C#
- **`var` everywhere** (`csharp_style_var_*: true:error`)
- **File-scoped namespaces**
- **Allman braces** (opening brace on new line)
- **No `this.` qualifier**
- **Private fields**: `_camelCase` prefix
- **Apache 2.0 license header** on every `.cs` file

### Testing
- **TUnit** test framework (not xUnit)
- **AwesomeAssertions** for fluent assertions (not FluentAssertions)
- **Verify.TUnit** for snapshot/golden file testing

### Build Properties
- `LangVersion: latest`
- `Nullable: enable`
- `ImplicitUsings: enable`
- `TreatWarningsAsErrors: true`
- `EnforceCodeStyleInBuild: true`
- SDK 10.0.100, `rollForward: latestFeature`

### CI/CD
- `ci.yml` — build + test on ubuntu + windows matrix
- `release-drafter.yml` — auto-draft release notes from PR labels
- `license.yml` — Apache 2.0 header enforcement

## C# Language Constructs to Use Throughout

- **File-scoped namespaces** — everywhere (enforced by `.editorconfig`)
- **Primary constructors** — for records and simple service classes
- **Collection expressions** — `[item1, item2]` syntax
- **`required` properties** — on options/config types
- **`ReadOnlySpan<char>`** — parsing hot paths
- **`FrozenDictionary` / `FrozenSet`** — static lookup tables
- **Pattern matching** — `switch` expressions for shape/style dispatch
- **Raw string literals** — SVG template fragments
- **`record` / `readonly record struct`** — all data model types
- **Target-typed `new`** — reduce type repetition
- **Source-generated regexes** — `[GeneratedRegex]` for all parser patterns
