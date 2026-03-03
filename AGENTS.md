# Agent Configuration

## Project

Mermaider — Render Mermaid diagrams to SVG in pure .NET.

## Build & Test

```bash
dotnet build mermaid-dotnet.slnx
dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj
```

## Conventions

- .NET 10, C# latest, file-scoped namespaces, `var` everywhere
- Tab indentation, Allman braces, `_camelCase` private fields, `s_camelCase` static private fields
- `[GeneratedRegex]` with 2s timeout for all regex patterns (ReDoS protection)
- Minimize allocations: `ReadOnlySpan<char>`, `ObjectPool<StringBuilder>`, `SearchValues<char>`, `FrozenDictionary` (static/long-lived data only; prefer `IReadOnlyDictionary` for parse results)
- TUnit for tests, AwesomeAssertions for fluent assertions, Verify.TUnit for golden file snapshots
- MIT license (no per-file headers required)

## Architecture

Three-stage pipeline: **Parse** → **Layout** → **Render**

1. **Parsing** (`src/Mermaider/Parsing/`): Line-by-line regex parsers produce diagram models
2. **Layout** (`src/Mermaider/Layout/`): Sugiyama (flowchart/class/ER) or custom arithmetic (sequence) produces positioned models
3. **Rendering** (`src/Mermaider/Rendering/`): Pooled StringBuilder produces SVG string

Supported diagram types: flowchart, state, sequence, class, ER, pie, quadrant, timeline, gitgraph, radar, treemap, venn, mindmap.

## Public API

- Library: `MermaidRenderer.RenderSvg(text, options?)` and `MermaidRenderer.Parse(text)`
- CLI: `mermaid [options] [input-file]` — reads from stdin or file, writes SVG to stdout or file
  - `--theme <name>`, `--transparent`, `--list-themes`, `--output <file>`
