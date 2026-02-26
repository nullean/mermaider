# 01 — Project Structure & Dependencies

Follows the [Elastic .NET Build](https://improved-broccoli-v92811n.pages.github.io) standards.

## Solution Layout

```
mermaid-dotnet/
├── plans/                          # These planning docs
├── src/
│   ├── Mermaid/                    # Main library (net10.0)
│   │   ├── Mermaid.csproj
│   │   ├── Mermaid.cs              # Public API entry point
│   │   ├── Models/
│   │   │   ├── MermaidGraph.cs     # Parsed graph model (records)
│   │   │   ├── PositionedGraph.cs  # Laid-out graph model (records)
│   │   │   ├── RenderOptions.cs    # User-facing options
│   │   │   └── DiagramType.cs      # Diagram type enum
│   │   ├── Parsing/
│   │   │   ├── FlowchartParser.cs  # graph TD / flowchart LR
│   │   │   ├── StateParser.cs      # stateDiagram-v2
│   │   │   ├── SequenceParser.cs   # sequenceDiagram
│   │   │   ├── ClassParser.cs      # classDiagram
│   │   │   ├── ErParser.cs         # erDiagram
│   │   │   └── DiagramDetector.cs  # Auto-detect diagram type
│   │   ├── Layout/
│   │   │   ├── ILayoutEngine.cs    # Abstraction over layout backend
│   │   │   ├── MsaglLayoutEngine.cs# Microsoft.Msagl implementation
│   │   │   ├── LayoutConverter.cs  # MermaidGraph → layout input
│   │   │   ├── LayerAlignment.cs   # Post-layout node alignment
│   │   │   ├── EdgeBundling.cs     # Fan-out/fan-in bundling
│   │   │   └── ShapeClipping.cs    # Clip edges to non-rect shapes
│   │   ├── Rendering/
│   │   │   ├── SvgRenderer.cs      # Main SVG builder
│   │   │   ├── ShapeRenderer.cs    # Node shape SVG (rect, diamond, etc.)
│   │   │   ├── EdgeRenderer.cs     # Edge polyline SVG
│   │   │   ├── TextRenderer.cs     # Multi-line text with formatting
│   │   │   ├── MarkerDefs.cs       # Arrow head marker definitions
│   │   │   └── SvgHelpers.cs       # escapeAttr, escapeXml, etc.
│   │   ├── Theming/
│   │   │   ├── DiagramColors.cs    # Color configuration record
│   │   │   ├── Themes.cs           # Built-in theme palettes
│   │   │   ├── StyleBlock.cs       # CSS variable derivation system
│   │   │   └── ColorMix.cs         # color-mix() weight constants
│   │   └── Text/
│   │       ├── TextMetrics.cs      # Character-width text measurement
│   │       ├── CharWidths.cs       # Character class buckets
│   │       └── MultilineUtils.cs   # <br> normalization, inline format tags
│   │
│   └── Mermaid.Cli/                # Optional CLI tool (net10.0)
│       ├── Mermaid.Cli.csproj
│       └── Program.cs              # stdin/file → SVG output
│
├── tests/
│   ├── Mermaid.Tests/              # Unit + integration tests (net10.0)
│   │   ├── Mermaid.Tests.csproj
│   │   ├── Parsing/
│   │   │   ├── FlowchartParserTests.cs
│   │   │   ├── StateParserTests.cs
│   │   │   └── ...
│   │   ├── Layout/
│   │   │   └── LayoutEngineTests.cs
│   │   ├── Rendering/
│   │   │   ├── SvgRendererTests.cs
│   │   │   └── GoldenFileTests.cs  # Snapshot/golden SVG comparison
│   │   ├── Text/
│   │   │   └── TextMetricsTests.cs
│   │   └── TestData/
│   │       ├── inputs/             # .mmd files with mermaid source
│   │       └── golden/             # Expected .svg outputs
│   │
│   └── Mermaid.Benchmarks/         # BenchmarkDotNet (net10.0)
│       ├── Mermaid.Benchmarks.csproj
│       └── RenderBenchmarks.cs
│
├── build/
│   ├── build.fsproj                # F# build project (Bullseye + Argu)
│   ├── BuildInformation.fs         # Paths, Software, OS types
│   ├── CommandLine.fs              # Argu DU with Build.Step/Cmd/Ignore
│   ├── Targets.fs                  # Build target definitions
│   ├── Program.fs                  # Entry point
│   └── keys/
│       └── keypair.snk             # Strong name signing key
│
├── mermaid-dotnet.slnx             # .slnx format (NOT .sln)
├── Directory.Build.props           # MSBuild defaults (UseArtifactsOutput, etc.)
├── Directory.Packages.props        # Central Package Management + GlobalPackageReferences
├── .editorconfig                   # Elastic standard (tabs, var everywhere, Allman braces)
├── .gitignore
├── .gitattributes
├── global.json                     # SDK 10.0.100, rollForward: latestFeature
├── build.sh                        # Build entry point (unix)
├── build.bat                       # Build entry point (windows)
├── AGENTS.md                       # AI agent configuration
├── CLAUDE.md                       # Elastic .NET coding practices for AI assistants
├── LICENSE.txt                     # Apache 2.0
└── .github/
    └── workflows/
        ├── ci.yml                  # CI: build + test (ubuntu + windows matrix)
        ├── release-drafter.yml     # Auto-draft release notes
        └── license.yml             # License header check
```

## Key Elastic .NET Standards

### `.slnx` Solution Format

Must use `.slnx` — not legacy `.sln`.

### Central Package Management

All NuGet versions centralized in `Directory.Packages.props`. Individual `.csproj` files reference packages without versions:

```xml
<!-- In .csproj -->
<PackageReference Include="Microsoft.Msagl" />

<!-- In Directory.Packages.props -->
<PackageVersion Include="Microsoft.Msagl" Version="1.1.6" />
```

### `Directory.Packages.props`

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <GlobalPackageReference Include="MinVer" Version="6.0.0" PrivateAssets="All" />
  </ItemGroup>
  <ItemGroup>
    <!-- Runtime dependencies -->
    <PackageVersion Include="Microsoft.Msagl" Version="1.1.6" />
    <PackageVersion Include="Microsoft.Msagl.Drawing" Version="1.1.6" />
    <!-- Test dependencies -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="TUnit" Version="0.6.0" />
    <PackageVersion Include="AwesomeAssertions" Version="8.0.0" />
    <PackageVersion Include="Verify.TUnit" Version="28.0.0" />
    <!-- Benchmarks -->
    <PackageVersion Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>
</Project>
```

### `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <IsPackable>false</IsPackable>
    <UseArtifactsOutput>true</UseArtifactsOutput>
  </PropertyGroup>

  <!-- src/ projects: packable, strong-named, XML docs -->
  <PropertyGroup Condition="$(MSBuildProjectDirectory.Contains('/src/'))">
    <IsPackable>true</IsPackable>
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>$(MSBuildThisFileDirectory)build/keys/keypair.snk</AssemblyOriginatorKeyFile>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- test projects: relaxed warnings -->
  <PropertyGroup Condition="$(MSBuildProjectDirectory.Contains('/tests/'))">
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

### `global.json`

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

### License Headers

Every `.cs` file must start with:

```csharp
// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
```

### `.editorconfig` Key Rules

- **Tab indentation** for C# (`indent_style = tab`)
- **`var` everywhere** (`csharp_style_var_*: true:error`)
- **File-scoped namespaces** (`csharp_style_namespace_declarations = file_scoped`)
- **Allman braces** (`csharp_new_line_before_open_brace = all`)
- **No `this.` qualifier** (`dotnet_style_qualification_for_*: false:error`)
- **Private fields**: `_camelCase` prefix
- **Spaces** for JSON, YAML, XML, and `.fsproj`/`.csproj` files

### Versioning

MinVer-based, tag-driven:
- Pre-release identifier: `canary.0`
- No tag prefix (tags: `1.0.0`, not `v1.0.0`)
- `MinVerMinimumMajorMinor`: `0.1`

## Dependencies

### Main library (`Mermaid.csproj`)

| Package | Purpose |
|---|---|
| `Microsoft.Msagl` | Graph layout engine |

Minimal dependencies — parsing, rendering, text metrics, theming are all hand-written.

### Test project (`Mermaid.Tests.csproj`)

| Package | Purpose |
|---|---|
| `TUnit` | Test framework (Elastic standard) |
| `AwesomeAssertions` | Fluent assertions (Elastic standard) |
| `Verify.TUnit` | Snapshot/golden file testing |
| `Microsoft.NET.Test.Sdk` | Test SDK |

### Benchmarks (`Mermaid.Benchmarks.csproj`)

| Package | Purpose |
|---|---|
| `BenchmarkDotNet` | Performance measurement |

## Public API Design

```csharp
namespace Mermaid;

public static class MermaidRenderer
{
    public static string RenderSvg(string text, RenderOptions? options = null);
    public static MermaidGraph Parse(string text);
}
```

All internal types are `internal`. The public surface is:

- `MermaidRenderer` — static render methods
- `RenderOptions` — color, font, spacing configuration
- `DiagramColors` — theme color record
- `Themes` — built-in theme palettes
- `MermaidGraph` / `PositionedGraph` — for advanced users

## Naming Conventions

- **Namespaces** mirror folder structure: `Mermaid.Parsing`, `Mermaid.Layout`, etc.
- **Records** for data types: `MermaidNode`, `PositionedEdge`, `DiagramColors`
- **Static classes** for pure functions: `TextMetrics`, `SvgHelpers`
- **Interfaces** only where swappability matters: `ILayoutEngine`
- **Private fields**: `_camelCase` (enforced by `.editorconfig`)
- **`var`** everywhere (enforced by `.editorconfig`)
