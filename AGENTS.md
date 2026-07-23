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

Supported diagram types: flowchart, state, sequence, class, ER, pie, quadrant, timeline, gitgraph, radar, treemap, venn, mindmap, gantt, journey, C4, sankey, xychart, requirement, packet, kanban, architecture, block, treeview.

Adding a type: see `docs/agent-add-diagram-type.md` (playbook for parallel agents).

## Design system

See `DESIGN.md` for the enforced uniformity rules: token derivation, font scale, geometry
constants, drop-shadow classes, and the "adding a diagram" checklist.

## Security

### Injection / XSS (already hardened)
- `SvgSanitizer` applies an allowlist-only pass before every output; `DtdProcessing.Prohibit` + `XmlResolver = null` blocks XXE and billion-laughs
- `[GeneratedRegex]` with `matchTimeoutMilliseconds: 2000` on every regex pattern (ReDoS)
- `<style>` is stripped unconditionally by the public `SvgSanitizer.Sanitize()` API — it can only survive in SVG produced by Mermaider's own render pipeline, and only after `RendererStylesheetAllowlist.IsAllowed()` validates it line-by-line against the exact grammar the renderer produces
- **`StrictStylingOptions` eliminates all diagram-source user values from the stylesheet.** When strict styling is active, `AllowedClasses` and colors are caller-defined; no `classDef`, `style`, `linkStyle`, or `%%{init}%%` value from the diagram source reaches the stylesheet. New stylesheet-writing code paths must preserve this invariant: either gate diagram-source values behind `strict is null`, or validate with `IsAllowedColor`/`IsAllowedHexColor` and document that the value is caller-supplied. `RendererStylesheetAllowlist` must be updated whenever the renderer emits new CSS — the sanitizer rejects output that doesn't match.

### Resource-exhaustion / DoS (added in `feature/validations`)
Mermaider accepts untrusted input. `ResourceLimits` (on by default; opt-out via `ResourceLimits.Unlimited`) enforces:

| Limit | Default | Guarded at |
|---|---|---|
| `MaxInputLength` | 512 KB | Before any split/regex |
| `MaxLines` | 10 000 | After preprocessing |
| `MaxLineLength` | 8 000 chars | After preprocessing |
| `MaxElements` | 5 000 | Per-type boundary in render pipeline |
| `MaxNodesAfterLayout` | 20 000 | After Sugiyama virtual-node insertion |
| `MaxRecursionDepth` | 64 | Mindmap / TreeView / Treemap recursive layout |
| `MaxOutputLength` | 8 MB | SVG StringBuilder before return |
| `RenderDeadline` | 5 s | Cooperative — checked at inner loop boundaries |

Violations throw `MermaidResourceLimitException : MermaidParseException` (existing catch blocks keep working).

**Cooperative deadline caveat**: `RenderDeadline` is checked at phase transitions and in the Sugiyama crossing-minimizer sweep, not after arbitrary native calls (e.g. MSAGL layout). It bounds the observed hotspots but is not a hard OS-level timer.

**Sugiyama layout O(V+E)**: The built-in Sugiyama engine was rewritten to use a CSR adjacency index in `GraphBuffer`; all three O(N³) hotspots in `CrossingMinimizer`, `LayerAssigner`, and `CycleRemover` are now O(V+E). The output is byte-identical for all golden snapshots.

Hosts may raise limits selectively or set `Limits = ResourceLimits.Unlimited` for fully trusted callers.

## Public API

- Library: `MermaidRenderer.RenderSvg(text, options?)` and `MermaidRenderer.Parse(text)`
- CLI: `mermaid [options] [input-file]` — reads from stdin or file, writes SVG to stdout or file
  - `--theme <name>`, `--transparent`, `--list-themes`, `--output <file>`
