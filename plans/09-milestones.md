# 09 — Phased Delivery Milestones

Follows the [Elastic .NET Build](https://improved-broccoli-v92811n.pages.github.io) standards for scaffolding, testing, CI, and release.

## Phase 0: Scaffolding (Day 1)

**Goal:** Working solution structure following Elastic .NET conventions. Builds, tests run.

### Tasks
- [ ] Create `mermaid-dotnet.slnx` (`.slnx` format, not `.sln`)
- [ ] Create `global.json` — SDK 10.0.100, `rollForward: latestFeature`
- [ ] Create `Directory.Build.props`:
  - `TargetFramework: net10.0`
  - `LangVersion: latest`
  - `Nullable: enable`, `ImplicitUsings: enable`
  - `TreatWarningsAsErrors: true`
  - `EnforceCodeStyleInBuild: true`
  - `UseArtifactsOutput: true`
  - `src/` conditional: `IsPackable`, `SignAssembly`, `GenerateDocumentationFile`
  - `tests/` conditional: relaxed warnings, `IsTestProject: true`
- [ ] Create `Directory.Packages.props`:
  - `ManagePackageVersionsCentrally: true`
  - `GlobalPackageReference: MinVer`
  - All `PackageVersion` entries (Microsoft.Msagl, TUnit, AwesomeAssertions, Verify.TUnit, BenchmarkDotNet)
- [ ] Create `.editorconfig` (Elastic standard: tabs, `var` everywhere, Allman braces, `_camelCase`, file-scoped namespaces, Apache 2.0 header template)
- [ ] Create `src/Mermaid/Mermaid.csproj`
- [ ] Create `tests/Mermaid.Tests/Mermaid.Tests.csproj` (TUnit + AwesomeAssertions)
- [ ] Create `tests/Mermaid.Benchmarks/Mermaid.Benchmarks.csproj`
- [ ] Create `build/` F# build project (Bullseye + Argu + Proc.Fs):
  - `build.fsproj`, `BuildInformation.fs`, `CommandLine.fs`, `Targets.fs`, `Program.fs`
- [ ] Create `build.sh` / `build.bat` entry points
- [ ] Create `build/keys/keypair.snk` (strong name signing key)
- [ ] Create `LICENSE.txt` (Apache 2.0)
- [ ] Create `AGENTS.md` and `CLAUDE.md`
- [ ] Create `.gitignore` and `.gitattributes`
- [ ] Create `.github/workflows/ci.yml` (ubuntu + windows matrix)
- [ ] Create `.github/workflows/release-drafter.yml`
- [ ] Create `.github/workflows/license.yml` + `check-license-headers.sh`
- [ ] Create stub `MermaidRenderer.cs` with `RenderSvg()` returning `"<svg/>"`
- [ ] Create first TUnit test that calls `RenderSvg()` and passes
- [ ] Verify `dotnet build` and `dotnet test` work
- [ ] Verify `./build.sh build` works

### Deliverable
Green build with one passing test. Full Elastic .NET compliance from day one.

---

## Phase 1: Text Metrics + Theming (Days 2-3)

**Goal:** Character width estimation and CSS theming — zero-risk foundation code.

### Tasks
- [ ] Port `CharWidths` — character class lookup tables (use `FrozenSet`)
- [ ] Port `TextMetrics` — `MeasureTextWidth`, `MeasureMultiline`
- [ ] Port `MultilineUtils` — `<br>` normalization, formatting tag stripping
- [ ] Port `DiagramColors` record and `ColorMix` constants
- [ ] Port `Themes` — all 15 built-in palettes (use `FrozenDictionary`)
- [ ] Port `StyleBlock` — CSS variable derivation builder
- [ ] Port `FontConstants` — sizes, weights, paddings, stroke widths
- [ ] Write TUnit tests for text metrics (calibrate against TS reference values)
- [ ] Write TUnit tests for theming

### Deliverable
Text measurement and theming are complete and tested. Foundation for node sizing and SVG rendering.

---

## Phase 2: Flowchart Parser (Days 4-6)

**Goal:** Parse flowchart Mermaid text into `MermaidGraph`.

### Tasks
- [ ] Define model records: `MermaidGraph`, `MermaidNode`, `MermaidEdge`, `MermaidSubgraph`
- [ ] Define enums: `NodeShape`, `EdgeStyle`, `Direction`
- [ ] Implement `DiagramDetector` — first-line sniffing
- [ ] Implement `FlowchartParser` (source-generated regexes):
  - [ ] Header parsing (`graph TD`, `flowchart LR`)
  - [ ] Node shape detection (all 14 patterns, correct ordering)
  - [ ] Arrow parsing (all 6 operators + bidirectional)
  - [ ] Edge labels (`-->|text|`)
  - [ ] Chained edges (`A --> B --> C`)
  - [ ] `&` parallel links
  - [ ] Subgraph start/end with nesting
  - [ ] `classDef`, `class`, `style` statements
  - [ ] `direction` override inside subgraphs
  - [ ] `:::className` shorthand
- [ ] Implement `StateParser`:
  - [ ] `stateDiagram-v2` header
  - [ ] State descriptions, aliases, transitions
  - [ ] `[*]` pseudostates
  - [ ] Composite states
- [ ] Write comprehensive TUnit parser tests

### Deliverable
Can parse any flowchart/state diagram into a typed graph model. Fully tested.

---

## Phase 3: Layout Engine Integration (Days 7-10)

**Goal:** Compute x/y positions for all graph elements.

### Tasks
- [ ] Define `PositionedGraph`, `PositionedNode`, `PositionedEdge`, `PositionedGroup` records
- [ ] Define `ILayoutEngine` interface
- [ ] Implement `NodeSizing` — estimate node width/height from label + shape
- [ ] Implement `MsaglLayoutEngine`:
  - [ ] Convert `MermaidGraph` → MSAGL `GeometryGraph`
  - [ ] Configure `SugiyamaLayoutSettings` (direction, spacing, edge routing)
  - [ ] Handle subgraphs as MSAGL clusters
  - [ ] Run layout, convert result → `PositionedGraph`
- [ ] Implement `LayerAlignment` — snap staggered nodes
- [ ] Implement `EdgeBundling` — merge fan-out/fan-in paths
- [ ] Implement `ShapeClipping` — clip edges to diamond boundaries
- [ ] Write layout tests
- [ ] **Evaluate MSAGL output quality** — compare with beautiful-mermaid
  - If insufficient: implement Jint + ELK.js fallback

### Deliverable
Parsed graphs get positioned with reasonable layouts.

### Risk Mitigation
Highest-risk phase. If MSAGL doesn't produce good layouts:
1. Tune MSAGL settings extensively
2. Add post-processing to improve output
3. Fall back to Jint + ELK.js (known-good output)

---

## Phase 4: SVG Renderer (Days 11-13)

**Goal:** Render positioned graphs to complete SVG strings.

### Tasks
- [ ] Implement `SvgHelpers` — escape functions, SVG open tag
- [ ] Implement `MarkerDefs` — arrow head markers
- [ ] Implement `ShapeRenderer` — all 14 node shapes
- [ ] Implement `EdgeRenderer` — polyline edges with markers
- [ ] Implement `TextRenderer` — multi-line text with inline formatting
- [ ] Implement `SvgRenderer` — orchestrate the full render pipeline
- [ ] Wire up `MermaidRenderer.RenderSvg()` end-to-end
- [ ] Create Verify golden file test suite:
  - [ ] Simple 2-node flow
  - [ ] All node shapes
  - [ ] All edge styles
  - [ ] Subgraph nesting
  - [ ] State diagram
  - [ ] Edge labels / multi-line labels / inline formatting
  - [ ] All 15 themes
- [ ] Visual review of all golden files

### Deliverable
**End-to-end flowchart/state SVG rendering works.** This is the MVP.

---

## Phase 5: Sequence Diagrams (Days 14-17)

**Goal:** Add sequence diagram support (independent pipeline).

### Tasks
- [ ] Define `SequenceDiagram` model types
- [ ] Implement `SequenceParser`
- [ ] Implement `SequenceLayout` (custom, no MSAGL)
- [ ] Implement `SequenceRenderer` (custom SVG)
- [ ] Wire into `MermaidRenderer.RenderSvg()` via diagram type detection
- [ ] Add Verify golden file tests
- [ ] Visual review

### Deliverable
Sequence diagrams render to SVG.

---

## Phase 6: Class + ER Diagrams (Days 18-22)

**Goal:** Add remaining diagram types.

### Tasks
- [ ] Class diagrams: model, parser, layout, renderer, tests
- [ ] ER diagrams: model, parser, layout, renderer, tests

### Deliverable
All 5 diagram types render to SVG.

---

## Phase 7: Polish & CLI (Days 23-25)

**Goal:** Production readiness.

### Tasks
- [ ] Performance optimization pass (BenchmarkDotNet, profile, optimize)
- [ ] API review — ensure public surface is minimal and clean
- [ ] XML doc comments on all public types
- [ ] `Mermaid.Cli` tool:
  - [ ] Read from stdin or file argument
  - [ ] Write SVG to stdout or file
  - [ ] `--theme` flag, `--transparent` flag
- [ ] NuGet package metadata in `Directory.Build.props`:
  - Authors, Copyright, PackageLicenseExpression, PackageIcon
- [ ] README.md with usage examples
- [ ] Ensure `./build.sh release` produces NuGet packages

### Deliverable
Ship-ready library + CLI tool.

---

## Summary Timeline

| Phase | Duration | Deliverable |
|---|---|---|
| 0: Scaffolding | 1 day | Green build, full Elastic .NET compliance |
| 1: Text + Theming | 2 days | Foundation components |
| 2: Parser | 3 days | Flowchart/state parsing |
| 3: Layout Engine | 4 days | Graph positioning (highest risk) |
| 4: SVG Renderer | 3 days | **MVP — end-to-end flowcharts** |
| 5: Sequence | 4 days | Sequence diagrams |
| 6: Class + ER | 5 days | All diagram types |
| 7: Polish + CLI | 3 days | Production ready |
| **Total** | **~25 days** | |

## Estimated Line Counts

| Component | C# Lines (est.) |
|---|---|
| Models (all records/enums) | ~300 |
| Parsers (all 5 types) | ~1,200 |
| Layout (MSAGL integration + post-processing) | ~800 |
| Rendering (SVG + shapes + text) | ~1,000 |
| Theming | ~200 |
| Text metrics | ~200 |
| CLI tool | ~100 |
| Tests | ~1,500 |
| Build scripts (F#) | ~200 |
| **Total** | **~5,500** |
