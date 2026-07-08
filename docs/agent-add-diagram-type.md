# Agent guide: add a Mermaid diagram type

Terse playbook for parallel agents implementing remaining Mermaid types in Mermaider.

## Pipeline (always)

```
Parse → Layout (optional) → Render SVG
Parse ΓåÆ Layout (optional) ΓåÆ Render SVG
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
Public API stays `MermaidRenderer.RenderSvg` / `Parse` ΓÇö no new entry points.

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
11. Optional screenshot under `docs/screenshots/{type}.svg` (CLI → file; opaque for GH if needed)
8. Gallery category + 1ΓÇô2 examples  
9. README section + `AGENTS.md` type list  
10. Parser + renderer tests  
11. Optional screenshot under `docs/screenshots/{type}.svg` (CLI ΓåÆ file; opaque for GH if needed)
11. Optional screenshot under `docs/screenshots/{type}.svg`

## Conventions (non-negotiable)

- .NET 10, file-scoped ns, `var`, tabs, Allman  
- `[GeneratedRegex(..., matchTimeoutMilliseconds: 2000)]`  
- Theme text/chrome: `RenderConstants.FsVar.*`, `fill="var(--_text)"`, `stroke="var(--_line)"`  
- Box text center: `y = mid` + `dy="{RenderConstants.TextBaselineShift}"` (`0.35em`) — **not** `dominant-baseline` alone  
- Chart accents may use fixed palette (pie/timeline/gantt/journey)  
- Escape via `MultilineUtils.AppendEscapedXml` / `AppendEscapedAttr`  
- No wall-clock in parse/layout (`DateTime.Today` banned); fixed synthetic origins if needed  

## Parse patterns (from Gantt)
- Box text center: `y = mid` + `dy="{RenderConstants.TextBaselineShift}"` (`0.35em`) ΓÇö **not** `dominant-baseline` alone  
- Chart accents may use fixed palette (pie/timeline/gantt/journey); **C4 uses fixed C4 palette** (mermaid parity)  
- Escape via `MultilineUtils.AppendEscapedXml` / `AppendEscapedAttr`  
- No wall-clock in parse/layout (`DateTime.Today` banned); fixed synthetic origins if needed  
- TreatWarningsAsErrors: fix **IDE00xx** (especially `IDE0007` var, `IDE0047`/`IDE0048` parens, `IDE0045` simplify if) before ship  

## Parse patterns (from Gantt / C4)

| Do | Don't |
|----|--------|
| Timeline-style **section flush** | Index vectors + `GetRange` |
| Typed tokens; **date = parses under `dateFormat`** | `LooksLikeDate` heuristics (breaks `task-1` ids) |
| Keyword gate in detector; full header in parser | Duplicate full header regex in both |
| One-pass resolve when deps are forward-only | Premature multi-pass complexity |

Header title: support both `type\ntitle X` and compact `type title X`.

## Render patterns (from Journey)
| **Brace stack** for nested blocks (C4 boundaries) | Flatten-only trees that lose nesting |
| **SplitArgs** with quote/escape awareness for call-style DSLs | Naive `Split(',')` on PlantUML-like args |
| Drop `$named=value` from positional lists; parse named for config | Mixing `$tags` into alias/label slots |

Header title: support both `type\ntitle X` and compact `type title X`.

### C4-specific

- Detector: `^C4(?:Context|Container|Component|Dynamic|Deployment)\b` (all five headers → one `DiagramType.C4`)
- Kind stored on model (`C4DiagramKind`) for future styling; v1 layout is shared
- Element shapes: Person / System* / Container* / Component* / Db / Queue / `_Ext` / Deployment_Node
- Boundaries: `Enterprise_Boundary` / `System_Boundary` / `Container_Boundary` / `Boundary` + `{ … }`
- Relations: `Rel`, `BiRel`, `RelIndex` (index skipped), `Rel_Back` (swaps from/to), `Rel_U`/`D`/`L`/`R` (accepted as plain Rel; layout direction ignored in v1)
- Detector: `^C4(?:Context|Container|Component|Dynamic|Deployment)\b` (all five headers ΓåÆ one `DiagramType.C4`)
- Kind stored on model (`C4DiagramKind`) for future styling; v1 layout is shared
- Element shapes: Person / System* / Container* / Component* / Db / Queue / `_Ext` / Deployment_Node
- Boundaries: `Enterprise_Boundary` / `System_Boundary` / `Container_Boundary` / `Boundary` + `{ ΓÇª }`
- Relations: `Rel`, `BiRel`, `Rel_*`, `RelIndex` (index skipped), `Rel_Back` (same as Rel for v1)
- Skip `UpdateElementStyle` / `UpdateRelStyle` in v1; honor `UpdateLayoutConfig($c4ShapeInRow, $c4BoundaryInRow)`
- Layout: **grid arithmetic in renderer** (shapeInRow / boundaryInRow), not Sugiyama
- accTitle must appear **after** the diagram header (detector reads first non-empty line of cleaned text)

## Render patterns (from Journey / C4)

When users demand **mermaid.ai parity**:

1. Read mermaid source: `packages/mermaid/src/diagrams/{type}/`  
2. Copy **defaults** from `config.schema.yaml` (`width`, `height`, margins, colours)  
3. Port geometry constants literally (e.g. journey face `cy = 300 + (5-score)*30`)  
4. Match draw order (e.g. dashed line under task rect)  
4. Match draw order (e.g. dashed line under task rect; **C4: boundaries → relations → elements → labels**)  
5. Prefer mermaid palettes for type-specific chrome; keep title on theme vars  

When “good enough Mermaider chart” is fine: arithmetic layout + theme vars + fixed accents (pie/timeline style).
4. Match draw order (e.g. dashed line under task rect; **C4: boundaries ΓåÆ relations ΓåÆ elements ΓåÆ labels**)  
5. Prefer mermaid palettes for type-specific chrome; keep title on theme vars  

When ΓÇ£good enough Mermaider chartΓÇ¥ is fine: arithmetic layout + theme vars + fixed accents (pie/timeline style).

### C4 render notes

- Fixed C4 fills (person `#08427B`, system `#1168BD`, container `#438DD5`, component `#85BBF0`, externals grey)
- Title / boundary labels / relation labels: `var(--_text*)` / `var(--_line)` / `var(--_arrow)`
- Clip relation endpoints to box edges; marker `#c4-arrow`
- Db = cylinder path; Queue = high `rx`; Person = circle + body path

## Visual QA

- Compare to mermaid live / mermaid.ai with the **same source**  
- Section/task labels: vertically centered (mid + `0.35em`)  
- PR example SVG: optional `Transparent = false` for dark GH pages  
- PR image: raw.githubusercontent.com from **fork branch**  

## Tests (minimum)

- Happy path parse (title, sections, core syntax)  
- Compact header title if applicable  
- Nested structure if type has blocks (C4 boundaries)  
- WinPrint / real-world fixture line if exists  
- `RenderSvg` → `<svg`…`</svg>`, key labels present  
- `RenderSvg` ΓåÆ `<svg`ΓÇª`</svg>`, key labels present  
- Theme: title or labels use `var(--_text)` where themed  
- Edge: empty diagram, clamp/out-of-range values  
- Accessibility: `accTitle` **after** header; assert `aria-roledescription`  

Run: `dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj -c Release`  
TreatWarningsAsErrors: fix IDE00xx before ship.

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
# implement ΓåÆ test ΓåÆ commit
git push -u fork HEAD   # tig/mermaider if no write on nullean
gh pr create --repo nullean/mermaider --head tig:feat/{type} --base main --draft
```

Issue first when tracking: `gh issue create --repo nullean/mermaider`.  
Title: `Fixes #N - Add {Type} support`. Body terse + example + test plan.
Issue first: `gh issue create --repo nullean/mermaider`.  
Title: `Fixes #N - Add {Type} support`.

## Remaining types (priority)

| Priority | Type | Keyword | Notes |
|----------|------|---------|--------|
| Done / in flight | Gantt | `gantt` | dates, after, tags |
| Done / in flight | Journey | `journey` | mermaid geometry port |
| Next | Journey polish / Gantt mermaid-parity | — | if needed |
| High | C4 | `C4Context`… | arch docs; heavier |
| High | Sankey | `sankey-beta` | flow widths |
| Medium | XY chart | `xychart-beta` | bar/line |
| Done / in flight | C4 | `C4Context`… | nested boundaries; fixed palette |
| Done / in flight | C4 | `C4Context`ΓÇª | nested boundaries; fixed palette |
| High | Sankey | `sankey` / `sankey-beta` | CSV links; flow widths |
| Medium | XY chart | `xychart` / `xychart-beta` | bar/line |
| Medium | Requirement | `requirementDiagram` | |
| Lower | Kanban, block, packet, architecture | `*-beta` | newer / niche |

Unsupported today must **not** crash host apps harder than `MermaidParseException` (flowchart fallback is OK).

## Anti-patterns

- Special-casing GH `<img>` with hardcoded px fills in the **library** renderer (use opaque example SVG only)  
- Parallel “support matrix” string lists in tests (drift)  
- Parallel ΓÇ£support matrixΓÇ¥ string lists in tests (drift)  
- Silent `DateTime.Today`  
- Giant single-file parsers without section/token structure  
- New public APIs per diagram type  
- Claiming pixel-perfect mermaid without reading upstream source  
- Putting `accTitle` **before** the type header in tests (detector only sees first line)  
- PowerShell `Set-Content` rewrites of C# (corrupts tabs / IDE0055) — use the write/strreplace tools  
- PowerShell `Set-Content` rewrites of C# (corrupts tabs / IDE0055) ΓÇö use the write/strreplace tools  

```text
sankey-beta
Electricity grid,Industry,342.165
Electricity grid,Losses,56.691
```

## Smoke source snippets

```text
gantt
  title Ship
  dateFormat YYYY-MM-DD
  section A
  Task :done, t1, 2026-01-01, 2d
  Next :active, after t1, 1d
```

```text
journey
  title Day
  section Work
  Make tea: 5: Me
  Do work: 1: Me, Cat
```
| Done / in flight | C4 | `C4Context`… | nested boundaries; fixed palette |
| Done / in flight | Sankey | `sankey` / `sankey-beta` | CSV links; flow widths |
| Done / in flight | XY chart | `xychart` / `xychart-beta` | bar/line |
| Medium | Requirement | `requirementDiagram` | |
| Done / in flight | C4 | `C4Context`… | nested boundaries; fixed palette |
| Done / in flight | Sankey | `sankey` / `sankey-beta` | CSV links; flow widths |
| Done / in flight | XY chart | `xychart` / `xychart-beta` | bar/line |
| Done / in flight | Requirement | `requirementDiagram` / `requirement` | SysML boxes + relations |
| Lower | Kanban, block, packet, architecture | `*-beta` | newer / niche |
| Done / in flight | Packet | `packet` / `packet-beta` | bit fields; arithmetic rows |
| Lower | Kanban, block, architecture | `*-beta` | newer / niche |

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

## Learnings log (append after each type)
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

```text
requirementDiagram
requirement test_req {
id: 1
text: the test text.
risk: high
verifymethod: test
}
element test_entity {
type: simulation
}
test_entity - satisfies -> test_req
```

## Learnings log

### C4 (2026-07)

- One `DiagramType` for five headers; kind on model is enough for v1.
- PlantUML-call DSL needs quote-aware `SplitArgs` + `$named` filtering.
- Nested `{`/`}` → stack of boundary frames; **mark deployment nodes** (`IsDeploymentNode`) so they get solid chrome + relation anchors without double-drawing leaves.
- Keep **separate** `placements` (drawn leaves) vs `relationAnchors` (leaves + nested deployment boxes).
- Layout must walk **source order** (do not partition leaves-then-boundaries) — Person → Boundary → System_Ext is the common case.
- Grid layout (shapeInRow / boundaryInRow) is “good enough Mermaider”; not d3/elk.
- C4 is fixed-style upstream — keep shape fills hardcoded; theme only chrome/text.
- Nested `{`/`}` ΓåÆ stack of boundary frames; **mark deployment nodes** (`IsDeploymentNode`) so they get solid chrome + relation anchors without double-drawing leaves.
- Keep **separate** `placements` (drawn leaves) vs `relationAnchors` (leaves + nested deployment boxes).
- Layout must walk **source order** (do not partition leaves-then-boundaries) ΓÇö Person ΓåÆ Boundary ΓåÆ System_Ext is the common case.
- Grid layout (shapeInRow / boundaryInRow) is ΓÇ£good enough MermaiderΓÇ¥; not d3/elk.
- C4 is fixed-style upstream ΓÇö keep shape fills hardcoded; theme only chrome/text.
- Header regex should capture kind + optional compact `title`; `DetectKind` must use `StartsWith` / capture group, not `Contains` (avoids `C4Context` matching `C4Container` substring myths and order bugs).
- Self-relations need an explicit loop path; edge clipping collapses zero-length segments.
- Primary-constructor private classes: **camelCase** parameter names (IDE1006).
- Add Verify snapshot for at least one happy-path SVG early.
- InternalsVisibleTo already covers parser unit tests calling internal helpers.

### Sankey (2026-07)

- Detector: `sankey` **and** `sankey-beta` (`^\s*sankey(-beta)?` upstream).
- Body is CSV (3 columns only): implement quote-aware field split once; share with tests via `internal`.
- Empty lines allowed; skip non-positive / unparsable / **NaN / Infinity** values quietly.
- Layout: topo longest-path + **capped edge relaxation** for residual SCCs; proportional stack with **compress if overflow**.
- Skip self-loops when building ribbons (zero-width path garbage).
- Links as cubic ribbons (`fill-opacity`, source color); labels `var(--_text)` + `TextBaselineShift`.
- Node palette fixed (pie-style); no frontmatter config in v1.
- Do not leave unused `[GeneratedRegex]` helpers (IDE / dead code).
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

### Requirement (2026-07)

- Detector: `requirementDiagram` **and** bare `requirement` (upstream `requirement(Diagram)?`).
- Block bodies are multi-line `{` … `}`; property keys case-insensitive (`id`, `text`, `risk`, `verifymethod`, `type`, `docref`).
- Six requirement kinds map to display labels with spaces (“Functional Requirement”, …).
- Relations: `A - type -> B` and reverse `B <- type - A` (src/dst flip); types: contains, copies, derives, satisfies, verifies, refines, traces.
- `direction TB|BT|LR|RL` — v1 layout is simple two-group grid (elements vs requirements), not Sugiyama.
- Draw edges **before** boxes so labels/rects sit under node chrome; side attachment along dominant axis.
- Requirements use accent fill/stroke; elements use node fill — distinguishes SysML stereotypes visually.
- StyleBlock role: `"requirement diagram"`.
- Screenshot: CLI `--output docs/screenshots/requirement.svg` for PR preview image.
### Packet (2026-07)

- Detector: `packet` and `packet-beta` (`^packet(?:-beta)?\b`).
- Field forms: range `0-15: "L"`, single bit `106: "URG"`, bit-count `+16: "Source Port"` (starts after previous end).
- Optional `title` on following line or compact `packet title X` on header.
- Labels must be double-quoted (mermaid grammar).
- Model is flat `Fields` list of `{Start, End, Label}`; renderer splits across 32-bit rows (mermaid parity).
- Arithmetic in renderer: `bitsPerRow=32`, `bitWidth=32`, `rowHeight=32`; bit numbers above blocks; fixed light fills; theme `var(--_text)` / `var(--_line)` for text/stroke.
- Zero `+count` skipped; invalid end &lt; start skipped (no throw for v1).

## Ref

- Project rules: `AGENTS.md`  
- Upstream: https://github.com/mermaid-js/mermaid  
- Spec/examples: mermaid.js.org/syntax/  
- Stress fixture: winprint `testfiles/mermaid.md`  
