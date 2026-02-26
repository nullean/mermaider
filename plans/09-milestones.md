# 09 — Phased Delivery Milestones

Follows the [Nullean](https://github.com/nullean) org conventions as seen in
[xunit-partitions](https://github.com/nullean/xunit-partitions).

---

## Phases 0–7: Core Implementation ✅

All core phases are **complete**:

| Phase | Status |
|---|---|
| 0: Scaffolding | ✅ `.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `LICENSE.txt` |
| 1: Text Metrics + Theming | ✅ `CharWidths`, `TextMetrics`, `MultilineUtils`, 15 themes, `StyleBlock` |
| 2: Flowchart + State Parser | ✅ `FlowchartParser`, `StateParser`, `DiagramDetector`, all node shapes/edge styles |
| 3: Layout Engine | ✅ `MsaglLayoutEngine`, `NodeSizing`, subgraph positioning |
| 4: SVG Renderer | ✅ `SvgRenderer`, all 14 node shapes, edge routing, Verify snapshot tests |
| 5: Sequence Diagrams | ✅ `SequenceParser`, `SequenceLayout`, `SequenceSvgRenderer` |
| 6: Class + ER Diagrams | ✅ `ClassParser`/`ErParser`, MSAGL layout, SVG renderers |
| 7: Polish & CLI | ✅ `Mermaid.Cli`, visual styling, strict mode, SVG sanitization |

**Current state:** 134 tests, 5 diagram types, CLI tool, gallery web server.

---

## Phase 8: Nullean Build Infrastructure

**Goal:** Match the nullean org build/release conventions exactly.
Reference: https://github.com/nullean/xunit-partitions

### 8.1 — dotnet-tools.json

- [ ] Create `.config/dotnet-tools.json` with:
  - `minver-cli` 6.0.0
  - `release-notes` 0.10.0
  - `nupkg-validator` 0.10.1
  - `assembly-differ` 0.16.0

### 8.2 — Build Scripts (F# + Bullseye)

- [ ] Create `build/scripts/scripts.fsproj`:
  - `OutputType: Exe`, `TargetFramework: net10.0`, `IsPackable: false`
  - Dependencies: `Argu 6.0.0`, `Bullseye 6.0.0`, `Proc 0.9.1`, `Fake.Tools.Git 5.15.0`
  - `<PackageReference Update="MinVer" Version="6.0.0" />`
- [ ] Create `build/scripts/Paths.fs`:
  - `ToolName = "Mermaid"`
  - `Repository = "nullean/mermaid-dotnet"`
  - `MainTFM = "net10.0"`
  - `SignKey` = sign key token
  - Output dir: `build/output`
- [ ] Create `build/scripts/CommandLine.fs`:
  - Argu subcommands: `Clean`, `Build`, `Test`, `PristineCheck`,
    `GeneratePackages`, `ValidatePackages`, `GenerateReleaseNotes`,
    `GenerateApiChanges`, `Release`, `CreateReleaseOnGithub`, `Publish`
  - Flags: `SingleTarget`, `Token`, `CleanCheckout`
- [ ] Create `build/scripts/Targets.fs`:
  - `clean` → delete `build/output`, `dotnet clean`
  - `build` → `dotnet build -c Release` (depends on clean)
  - `test` → `dotnet test -c Release` with loggers (depends on build)
  - `pristinecheck` → verify clean git working copy
  - `generatepackages` → `dotnet pack -c Release -o build/output`
  - `validatepackages` → `nupkg-validator` per .nupkg
  - `generatereleasenotes` → `release-notes` tool
  - `generateapichanges` → `assembly-differ` tool
  - `release` → pristinecheck + test + packages + validation + notes + api changes
  - `createreleaseongithub` → `release-notes create-release`
  - `publish` → release + create github release
- [ ] Create `build/scripts/Program.fs`:
  - Argu parsing, `Targets.Setup`, `RunTargetsAndExitAsync`
- [ ] Create `build.sh`:
  ```bash
  #!/usr/bin/env bash
  set -euo pipefail
  dotnet run --project build/scripts -- "$@"
  ```
- [ ] Create `build.bat`:
  ```bat
  @echo off
  dotnet run --project build/scripts -- %*
  ```

### 8.3 — Strong-Name Signing

- [ ] Generate `build/keys/keypair.snk` (2048-bit RSA)
- [ ] Extract `build/keys/public.snk`
- [ ] Add to `Directory.Build.props` (src/ conditional):
  ```xml
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>$(MSBuildThisFileDirectory)build/keys/keypair.snk</AssemblyOriginatorKeyFile>
  ```

### 8.4 — NuGet Package Metadata

- [ ] Create/add `nuget-icon.png` (simple diagram icon)
- [ ] Update `Directory.Build.props` src/ conditional:
  - `<PackageIcon>nuget-icon.png</PackageIcon>`
- [ ] Add to `src/Mermaid/Mermaid.csproj`:
  ```xml
  <Content Include="..\..\nuget-icon.png" Pack="true" PackagePath="nuget-icon.png" />
  <Content Include="..\..\README.md" Pack="true" PackagePath="README.md" />
  ```
- [ ] Same for `src/Mermaid.Cli/Mermaid.Cli.csproj`

### 8.5 — Test Infrastructure

- [ ] Create `tests/Directory.Build.props`:
  - Import parent `Directory.Build.props`
  - Add `Nullean.VsTest.Pretty.TestLogger` (or TUnit equivalent)
  - Add `GitHubActionsTestLogger`

### 8.6 — Solution File Update

- [ ] Update `mermaid-dotnet.slnx` to include `/build/` folder:
  ```xml
  <Folder Name="/build/">
    <File Path=".editorconfig" />
    <File Path=".github/workflows/ci.yml" />
    <File Path=".gitignore" />
    <File Path="build.bat" />
    <File Path="build.sh" />
    <File Path="Directory.Build.props" />
    <File Path="dotnet-tools.json" />
    <File Path="global.json" />
    <File Path="readme.md" />
    <Project Path="build/scripts/scripts.fsproj" />
  </Folder>
  ```

### 8.7 — CI Workflow

- [ ] Create `.github/workflows/ci.yml`:
  - Name: "Always be deploying"
  - Triggers: push to `main` + tags `*.*.*`, pull_request
  - `paths-ignore`: README.md, .editorconfig
  - Steps:
    1. `actions/checkout@v5` (fetch-depth: 1) + `git fetch --prune --unshallow --tags`
    2. `actions/setup-dotnet@v5` with `10.0.x`, source-url: `https://nuget.pkg.github.com/nullean/index.json`
    3. `./build.sh build -s true`
    4. `./build.sh test -s true`
    5. `./build.sh generatepackages -s true`
    6. `./build.sh validatepackages -s true`
    7. `./build.sh generateapichanges -s true`
    8. On push to branch → publish to GitHub Packages (`dotnet nuget push`)
    9. On tag push → generate release notes, create GitHub release, push to nuget.org

### Deliverable

`./build.sh build`, `./build.sh test`, `./build.sh release` all work.
CI produces and validates NuGet packages on every push.
Tag pushes auto-release to nuget.org.

---

## Phase 9: README & Documentation

- [ ] Create `README.md` with:
  - Package badges (NuGet, CI, license)
  - Quick-start code example
  - Supported diagram types
  - Theming / custom colors
  - Strict mode usage
  - SVG sanitization API
  - CLI usage
  - Contributing / build instructions

---

## Phase 10: Optional Quality Improvements

Low-priority items that can be tackled incrementally:

- [ ] Performance profiling pass with BenchmarkDotNet results
- [ ] Public API review — trim surface to essential types only
- [ ] Subgraph → MSAGL cluster mapping (true nested layout)
- [ ] Edge bundling / fan-out merging post-processor
- [ ] Shape clipping (clip edge endpoints to diamond/hexagon boundaries)
- [ ] CSS content sanitization inside `<style>` elements (block `url()`, `expression()`)

---

## Summary

| Phase | Status | Description |
|---|---|---|
| 0–7 | ✅ | Core library, all diagram types, CLI, strict mode, sanitization |
| 8 | ⬜ | Nullean build infrastructure (F# scripts, CI, signing, packages) |
| 9 | ⬜ | README & documentation |
| 10 | ⬜ | Optional quality improvements |
