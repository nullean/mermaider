# Agent guide: add a Mermaid diagram type

Terse playbook for parallel agents implementing remaining Mermaid types in Mermaider.

## Pipeline (always)

```
Parse → Layout (optional) → Render SVG
```

| Stage | Where | Notes |
|-------|--------|--------|
| Detect | `Parsing/DiagramDetector.cs` | Keyword gate only: `^keyword(?:\s\|$)` |
| Parse | `Parsing/{Type}Parser.cs` | Owns header options (`title`, flags) |
| Model | `Models/{Type}Diagram.cs` | Records + small enums |
| Layout | `Layout/` only if graph (Sugiyama) | Charts: arithmetic **in renderer** |
| Render | `Rendering/{Type}SvgRenderer.cs` | Pooled `StringBuilder` |
| Wire | `MermaidRenderer` switch, `StyleBlock.GetRoleDescription` | |
| Docs | `AGENTS.md`, `README.md`, Gallery `DiagramExamples` | |
| Tests | `Parsing/*ParserTests`, `Rendering/*RendererTests` | |

Public API stays `MermaidRenderer.RenderSvg` / `Parse` — no new entry points.

## File checklist

1. `DiagramType` enum value  
2. Detector regex + branch  
3. Model records  
4. Parser (`internal static partial`, `[GeneratedRegex]` + **2s** timeout)  
5. Renderer (`Render` + `RenderToBuilder`, `SharedStringBuilderPool`)  
6. `MermaidRenderer` case  
7. `StyleBlock` role string  
8. Gallery category + 1–2 examples  
9. README section + `AGENTS.md` type list  
10. Parser + renderer tests  
11. Optional screenshot under `docs/screenshots/{type}.svg`

## Conventions (non-negotiable)

- .NET 10, file-scoped ns, `var`, tabs, Allman  
- `[GeneratedRegex(..., matchTimeoutMilliseconds: 2000)]`  
- Theme text/chrome: `RenderConstants.FsVar.*`, `fill="var(--_text)"`, `stroke="var(--_line)"`  
- Box text center: `y = mid` + `dy="{RenderConstants.TextBaselineShift}"` (`0.35em`)  
- Chart accents may use fixed palette (pie/timeline/C4/sankey/xy)  
- Escape via `MultilineUtils.AppendEscapedXml` / `AppendEscapedAttr`  
- No wall-clock in parse/layout  
- TreatWarningsAsErrors: fix **IDE00xx** / CA rules before ship  

## Parse patterns

| Do | Don't |
|----|--------|
| Keyword gate in detector; full header in parser | Duplicate full header regex in both |
| Quote-aware splits for CSV / call-style DSLs | Naive `Split(',')` |
| Reject NaN / Infinity numeric values | `value <= 0` alone (NaN slips through) |
| Compact header titles when applicable | Assume title only on following lines |

Header title: support both `type\ntitle X` and compact `type title X`.

## Worktree / PR

```text
git fetch origin main
git worktree add ../add-{type} -b feat/{type} origin/main
# implement → test → commit
git push -u fork HEAD   # tig/mermaider if no write on nullean
gh pr create --repo nullean/mermaider --head tig:feat/{type} --base main --draft
```

Issue first: `gh issue create --repo nullean/mermaider`.  
Title: `Fixes #N - Add {Type} support`.

## Remaining types (priority)

| Priority | Type | Keyword | Notes |
|----------|------|---------|--------|
| Done / in flight | Gantt | `gantt` | dates, after, tags |
| Done / in flight | Journey | `journey` | mermaid geometry port |
| Done / in flight | C4 | `C4Context`… | nested boundaries; fixed palette |
| Done / in flight | Sankey | `sankey` / `sankey-beta` | CSV links; flow widths |
| Done / in flight | XY chart | `xychart` / `xychart-beta` | bar/line |
| Done / in flight | Kanban | `kanban` | indent columns/tasks |
| Medium | Requirement | `requirementDiagram` | |
| Lower | block, packet, architecture | `*-beta` | newer / niche |

## Anti-patterns

- Special-casing GH `<img>` fills in the **library** renderer  
- Parallel “support matrix” string lists in tests (drift)  
- Silent `DateTime.Today`  
- New public APIs per diagram type  
- Claiming pixel-perfect mermaid without reading upstream source  
- Putting `accTitle` **before** the type header in tests  
- PowerShell `Set-Content` rewrites of C# (corrupts tabs) — use write/strreplace tools  

## Smoke snippets

```text
C4Context
  title Banking
  Person(c, "Customer")
  System(s, "Banking App")
  Rel(c, s, "Uses")
```

```text
sankey-beta
Electricity grid,Industry,342.165
Electricity grid,Losses,56.691
```

```text
xychart-beta
  title "Sales"
  x-axis [jan, feb, mar]
  y-axis "Rev" 0 --> 100
  bar [10, 20, 30]
  line [12, 18, 28]
```

## Learnings log

### C4 (2026-07)

- One `DiagramType` for five headers; kind on model is enough for v1.
- PlantUML-call DSL needs quote-aware `SplitArgs` + `$named` filtering.
- Nested braces → stack of boundary frames; mark deployment nodes (`IsDeploymentNode`) for solid chrome + relation anchors.
- Separate `placements` (drawn leaves) vs `relationAnchors` (leaves + nested deployment boxes).
- Layout must walk **source order** (do not partition leaves-then-boundaries).
- Header kind detection: capture group / `StartsWith`, not `Contains`.
- Self-relations need an explicit loop path.
- Primary-constructor private classes: **camelCase** parameter names (IDE1006).
- Add Verify snapshot early.

### Sankey (2026-07)

- Detector: `sankey` and `sankey-beta`.
- CSV 3 columns; quote-aware field split; skip non-positive / NaN / Infinity.
- Topo longest-path + capped edge relaxation for residual SCCs; compress vertical stack if overflow.
- Skip self-loop ribbons; cubic ribbons with source color + fill-opacity.
- No unused `[GeneratedRegex]` helpers.

### XY chart (2026-07)

- Detector: `xychart` and `xychart-beta`; optional `horizontal` / compact `title` on header.
- Axes: categorical `[a, b]` or numeric `min --> max` with optional quoted title; reject NaN/Infinity on **all** range paths.
- Series: `bar` / `line` with optional series name (legend); ignore per-point text labels (leading number only).
- Bad series tokens become **0** (keep category index alignment — do not drop).
- Render: themed axes/ticks; fixed plot palette; draw bars under lines but **color by declaration index** (legend must match).
- Auto Y for bars: include 0 from both sides (`min>0` → 0, `max<0` → 0).
- Horizontal flag stored; v1 vertical geometry only (numeric `XMin`/`XMax` parsed, plotting still categorical slots).
- Watch IDE: shadowing locals (`top`), CA2249 `Contains`, IDE0047 parens.

### Kanban (2026-07)

- Detector: `^kanban\b` (word boundary, not full-line `$` — header may grow).
- **Must** use `PreprocessLinesPreserveIndent(cleaned, accessibility)` — columns vs tasks are indent-driven.
- Two levels only: shallowest content indent = column; deeper = task under current column.
- `id[Title]` optional; bare text → id equals label.
- Task metadata: trailing `@{ assigned, ticket, priority }` with quoted or bare values; strip before id/title parse.
- Role string: `"kanban board"` (only emitted when acc* directives present).
- Theme via existing CSS vars: `--_node-fill`, `--_accent-fill`, `--_accent-text`, `--_text`, `--_text-muted`, `--_line` — no `--_surface` / `--_accent`.
- Layout arithmetic in renderer (horizontal columns, stacked cards, task-count badge).
- Prefer passing `accessibility` into preserve-indent preprocess so `accTitle`/`accDescr` are not parsed as columns.

## Ref

- Project rules: `AGENTS.md`  
- Upstream: https://github.com/mermaid-js/mermaid  
- Spec/examples: mermaid.js.org/syntax/  
