# Claude Configuration

See AGENTS.md for project conventions and architecture.

## Key Design Decisions

- All regex patterns use `[GeneratedRegex]` with `matchTimeoutMilliseconds: 2000`
- SVG rendering uses `ObjectPool<StringBuilder>` — never allocate a new StringBuilder per render
- Text metrics operate on `ReadOnlySpan<char>` and use `SearchValues<char>` for SIMD character lookups
- Models use `FrozenDictionary` for immutable lookup tables after parsing
- Layout uses Microsoft.Msagl (Sugiyama/layered algorithm with rectilinear edge routing)
- Theming uses CSS custom properties with `color-mix()` fallbacks embedded in SVG

## Testing

Run tests with: `dotnet run --project tests/Mermaid.Tests/Mermaid.Tests.csproj`

Tests use TUnit (source-generated test runner) and AwesomeAssertions.

99 tests cover: flowchart parser (17), sequence parser (12), class parser (13), ER parser (7),
SVG renderer (7), sequence renderer (12), class renderer (7), ER renderer (9), text metrics (1),
golden file snapshots (14 — all diagram types, themes, transparency).

## Benchmarks

Run benchmarks with: `dotnet run -c Release --project tests/Mermaid.Benchmarks/Mermaid.Benchmarks.csproj`

## CLI

Test with: `printf 'graph TD\n  A-->B' | dotnet run --project src/Mermaid.Cli/Mermaid.Cli.csproj`
