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
11. Optional screenshot under `docs/screenshots/{type}.svg` (CLI → file; opaque for GH if needed)

## Conventions (non-negotiable)

- .NET 10, file-scoped ns, `var`, tabs, Allman  
- `[GeneratedRegex(..., matchTimeoutMilliseconds: 2000)]`  
- Theme text/chrome: `RenderConstants.FsVar.*`, `fill="var(--_text)"`, `stroke="var(--_line)"`  
- Box text center: `y = mid` + `dy="{RenderConstants.TextBaselineShift}"` (`0.35em`) — **not** `dominant-baseline` alone  
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
| **Brace stack** for nested blocks (C4 boundaries) | Flatten-only trees that lose nesting |
| **SplitArgs** with quote/escape awareness for call-style DSLs | Naive `Split(',')` on PlantUML-like args |
| Drop `$named=value` from positional lists; parse named for config | Mixing `$tags` into alias/label slots |

Header title: support both `type\ntitle X` and compact `type title X`.

### C4-specific

- Detector: `^C4(?:Context|Container|Component|Dynamic|Deployment)\b` (all five headers → one `DiagramType.C4`)
- Kind stored on model (`C4DiagramKind`) for future styling; v1 layout is shared
- Element shapes: Person / System* / Container* / Component* / Db / Queue / `_Ext` / Deployment_Node
- Boundaries: `Enterprise_Boundary` / `System_Boundary` / `Container_Boundary` / `Boundary` + `{ … }`
- Relations: `Rel`, `BiRel`, `Rel_*`, `RelIndex` (index skipped), `Rel_Back` (same as Rel for v1)
- Skip `UpdateElementStyle` / `UpdateRelStyle` in v1; honor `UpdateLayoutConfig($c4ShapeInRow, $c4BoundaryInRow)`
- Layout: **grid arithmetic in renderer** (shapeInRow / boundaryInRow), not Sugiyama
- accTitle must appear **after** the diagram header (detector reads first non-empty line of cleaned text)

## Render patterns (from Journey / C4)

When users demand **mermaid.ai parity**:

1. Read mermaid source: `packages/mermaid/src/diagrams/{type}/`  
2. Copy **defaults** from `config.schema.yaml` (`width`, `height`, margins, colours)  
3. Port geometry constants literally (e.g. journey face `cy = 300 + (5-score)*30`)  
4. Match draw order (e.g. dashed line under task rect; **C4: boundaries → relations → elements → labels**)  
5. Prefer mermaid palettes for type-specific chrome; keep title on theme vars  

When “good enough Mermaider chart” is fine: arithmetic layout + theme vars + fixed accents (pie/timeline style).

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
- Theme: title or labels use `var(--_text)` where themed  
- Edge: empty diagram, clamp/out-of-range values  
- Accessibility: `accTitle` **after** header; assert `aria-roledescription`  

Run: `dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj -c Release`  
TreatWarningsAsErrors: fix IDE00xx before ship.

## Worktree / PR

```text
git fetch origin main
git worktree add ../add-{type} -b feat/{type} origin/main
# implement → test → commit
git push -u fork HEAD   # tig/mermaider if no write on nullean
gh pr create --repo nullean/mermaider --head tig:feat/{type} --base main --draft
```

Issue first when tracking: `gh issue create --repo nullean/mermaider`.  
Title: `Fixes #N - Add {Type} support`. Body terse + example + test plan.

## Remaining types (priority)

| Priority | Type | Keyword | Notes |
|----------|------|---------|--------|
| Done / in flight | Gantt | `gantt` | dates, after, tags |
| Done / in flight | Journey | `journey` | mermaid geometry port |
| Done / in flight | C4 | `C4Context`… | nested boundaries; fixed palette |
| High | Sankey | `sankey` / `sankey-beta` | CSV links; flow widths |
| Medium | XY chart | `xychart` / `xychart-beta` | bar/line |
| Medium | Requirement | `requirementDiagram` | |
| Lower | Kanban, block, packet, architecture | `*-beta` | newer / niche |

Unsupported today must **not** crash host apps harder than `MermaidParseException` (flowchart fallback is OK).

## Anti-patterns

- Special-casing GH `<img>` with hardcoded px fills in the **library** renderer (use opaque example SVG only)  
- Parallel “support matrix” string lists in tests (drift)  
- Silent `DateTime.Today`  
- Giant single-file parsers without section/token structure  
- New public APIs per diagram type  
- Claiming pixel-perfect mermaid without reading upstream source  
- Putting `accTitle` **before** the type header in tests (detector only sees first line)  
- PowerShell `Set-Content` rewrites of C# (corrupts tabs / IDE0055) — use the write/strreplace tools  

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

```text
C4Context
  title Banking
  Person(c, "Customer")
  System(s, "Banking App")
  Rel(c, s, "Uses")
```

## Learnings log (append after each type)

### C4 (2026-07)

- One `DiagramType` for five headers; kind on model is enough for v1.
- PlantUML-call DSL needs quote-aware `SplitArgs` + `$named` filtering.
- Nested `{`/`}` → stack of boundary frames; deployment nodes with braces act as boundary-like frames.
- Grid layout (shapeInRow / boundaryInRow) is “good enough Mermaider”; not d3/elk.
- C4 is fixed-style upstream — keep shape fills hardcoded; theme only chrome/text.
- Detector keyword gate: do **not** require end-of-line only if headers never take inline options (C4 headers are bare).
- InternalsVisibleTo already covers parser unit tests calling internal helpers.

## Ref

- Project rules: `AGENTS.md`  
- Upstream: https://github.com/mermaid-js/mermaid  
- Spec/examples: mermaid.js.org/syntax/  
- Stress fixture: winprint `testfiles/mermaid.md`  
