# Agent Configuration

## Project

Mermaider — Render Mermaid diagrams to SVG in pure .NET.

## Build & Test

`mise install` provides the toolchain: the .NET 11 SDK pinned in `global.json`, plus the net10.0 runtime Mermaider
and its tests target. The everyday tasks have the same names as in every nullean repository:

```bash
mise run build            # build the solution
mise run test             # build, then run the tests
mise run pack             # build, pack the NuGet packages and validate them
mise run ci               # what CI's build job runs: test, pack, validate, diff the public API
mise run docs             # build the docs and serve them
mise run check            # workflow security checks (zizmor)
mise run clean            # delete build output
mise run release          # show the release draft; `mise run release publish` releases
```

`./build.sh <target>` runs the same build targets directly (`./build.sh --help` lists them), from the F# build in
`build/scripts` ([Nullean.Make.Fs](https://www.nuget.org/packages/Nullean.Make.Fs), net11.0). Without mise:

```bash
dotnet build mermaid-dotnet.slnx
dotnet test                # every test project (global.json selects Microsoft.Testing.Platform)
dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj   # one project
```

## Releasing

release-drafter keeps a draft release current from the labels of merged pull requests; every pull request carries
exactly one release label (`pr-labels.yml` enforces it). **Publishing the draft** releases: it creates the tag and runs
`ci.yml` on it, whose `build` job tests, packs at the tag's version, pushes the packages to nuget.org with trusted
publishing and appends the public API changes to the release notes. Pushing a tag on its own does nothing. In 0.x a
`breaking` label bumps the minor version; 1.0 is the maintainer's call. See the
[releasing](.claude/skills/releasing/SKILL.md) and [breaking-changes](.claude/skills/breaking-changes/SKILL.md)
skills, and `mise run release` for the current draft.

## Conventions

- .NET 10, C# latest, file-scoped namespaces, `var` everywhere
- Tab indentation, Allman braces, `_camelCase` private fields, `s_camelCase` static private fields
- `[GeneratedRegex]` with 2s timeout for all regex patterns (ReDoS protection)
- Minimize allocations: `ReadOnlySpan<char>`, `ObjectPool<StringBuilder>`, `SearchValues<char>`, `FrozenDictionary` (static/long-lived data only; prefer `IReadOnlyDictionary` for parse results)
- TUnit for tests, AwesomeAssertions for fluent assertions, CsCheck for property-based tests and fuzzing, Verify.TUnit
  for golden file snapshots
- Package versions live only in `Directory.Packages.props`; a `PackageReference` never carries a `Version`
- Every package ships an agent skill at `src/<Project>/skills/<name>/SKILL.md`, written from the code. Change it with
  the public API it describes, and compile its snippets before committing
- MIT license (no per-file headers required)
- Build targets are Make.Fs `target { }` blocks in `build/scripts/Program.fs` whose commands are Proc.Fs `sh $"…"`
  lines; values come from `text`/`lines` inside `shell { }`. Do not call the C# `ProcNet` API from the build

## Architecture

Three-stage pipeline: **Parse** → **Layout** → **Render**

1. **Parsing** (`src/Mermaider/Parsing/`): Line-by-line regex parsers produce diagram models
2. **Layout** (`src/Mermaider/Layout/`): Sugiyama (flowchart/class/ER) or custom arithmetic (sequence) produces positioned models
3. **Rendering** (`src/Mermaider/Rendering/`): Pooled StringBuilder produces SVG string

Flowchart node shapes include the two parallelograms (`[/text/]`, `[\text\]`) as well as the two trapezoids.

Supported diagram types: flowchart, state, sequence, class, ER, pie, quadrant, timeline, gitgraph, radar, treemap, venn, mindmap, gantt, journey, C4, sankey, xychart, requirement, packet, kanban, architecture, block, treeview.

Adding a type: see `contributing/agent-add-diagram-type.md` (playbook for parallel agents).

## Design system

See `DESIGN.md` for the enforced rules. In short: three **style presets** (`DiagramStyle.Quiet` /
`Blueprint` / `Tonal`, every knob in `Theming/StyleSpec.cs`) plus the `Gradient`, `Tint` and `Elevation`
design inputs restyle all diagram types without changing layout. Every colour resolves through a
**colour family** (`Theming/ColorFamily.cs`: cluster `p<i>`, chart series, neutral, accent, roles; eleven
derived stages), every text through a `TypeRole`, and every renderer draws with the shared components in
`Rendering/DesignSystem*.cs` (`var ds = DesignSystem.For(context);` … `ds.Close(sb);`). No literal hex
colours, ad-hoc `color-mix` ratios or font sizes in renderers. `DESIGN.md` ends with the "adding a
diagram type" checklist.

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

- Library: `MermaidRenderer.RenderSvg(text, options?)`, `MermaidRenderer.RenderAscii(text, options?)` (flowchart, state and xychart, drawn as characters on a grid the Sugiyama layout is run in) and `MermaidRenderer.Parse(text)`
- CLI: `mermaid [options] [input-file]` — reads from stdin or file, writes SVG to stdout or file
  - `--theme <name>`, `--transparent`, `--list-themes`, `--output <file>`
  - `--style <quiet|blueprint|tonal>`, `--no-gradient`, `--tint <0.5-1.5>`, `--elevation <0|1|2>`
