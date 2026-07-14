# Claude Configuration

See AGENTS.md for project conventions and architecture.

## Key Design Decisions

- All regex patterns use `[GeneratedRegex]` with `matchTimeoutMilliseconds: 2000`
- SVG rendering uses `ObjectPool<StringBuilder>` — never allocate a new StringBuilder per render
- Text metrics operate on `ReadOnlySpan<char>` and use `SearchValues<char>` for SIMD character lookups
- Models use `FrozenDictionary` for immutable lookup tables after parsing
- Layout uses built-in Sugiyama engine (layered algorithm with rectilinear edge routing); optional MSAGL via `Mermaider.Layout.Msagl`
- Theming uses CSS custom properties with `color-mix()` fallbacks embedded in SVG
- Architecture diagrams (`architecture-beta`) use a bespoke directional-grid layout (`Layout/ArchitectureLayout.cs`), not Sugiyama — edges carry explicit L/R/T/B sides, which don't map onto a layered graph
- Icons (architecture services/groups) resolve via `Mermaider.Icons.IconRegistry`: a small built-in set (Mermaid defaults + curated AWS/GCP/Azure + full Elastic, all original placeholder glyphs, not official trademarked logos) plus user registrations via `IconRegistry.Register`. Icons render as `<image href="data:image/svg+xml;base64,...">`; `SvgSanitizer` has one narrow, regex-anchored exception permitting `href` only on `<image>` with a base64 `data:image/svg+xml|png` value — icons are also sanitized once at registration time

## Testing

Run tests with: `dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj`

Tests use TUnit (source-generated test runner) and AwesomeAssertions.

290 tests cover: flowchart parser (17), sequence parser (12), class parser (13), ER parser (7),
SVG renderer (7), sequence renderer (12), class renderer (7), ER renderer (9), text metrics (1),
golden file snapshots (14 — all diagram types, themes, transparency),
spec coverage snapshots (flowchart 22, sequence 20, class 13, ER 11, state 12).
(Counts above predate several diagram types added since; run the suite for the current total —
538 as of the `architecture-beta` addition.) Architecture diagrams add: parser tests, renderer
tests, `IconRegistry` tests (built-ins, vendor packs, custom registration, malicious-icon
stripping), sanitizer tests for the scoped `<image>` href exception, and spec-coverage snapshots
(groups, nested groups, junction routing, arrow directions, vendor icon packs, the reported
k8s/ECH diagram) under `Snapshots/ArchitectureSpecTests.cs`.

## Benchmarks

Run benchmarks with: `dotnet run -c Release --project tests/Mermaider.Benchmarks/Mermaider.Benchmarks.csproj`

## CLI

Test with: `printf 'graph TD\n  A-->B' | dotnet run --project src/Mermaider.Cli/Mermaider.Cli.csproj`
