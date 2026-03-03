# Diagram Implementation Plan

## Completed

### 1. Pie Chart ✅
- **Keyword**: `pie`
- **Status**: Stable in Mermaid
- **Syntax**: `"label" : value` pairs, optional `title`, optional `showData` flag
- **Architecture**: Parser → Renderer (no layout engine; arc math computed inline)
- **Tests**: 8 parser, 9 renderer, 2 snapshots

### 2. Quadrant Chart ✅
- **Keyword**: `quadrantChart`
- **Status**: Stable (v10.2.0+)
- **Syntax**:
  ```
  quadrantChart
    title Prioritization Matrix
    x-axis Low Effort --> High Effort
    y-axis Low Impact --> High Impact
    quadrant-1 Do First
    quadrant-2 Schedule
    quadrant-3 Delegate
    quadrant-4 Eliminate
    Point A: [0.8, 0.9]
    Point B: [0.3, 0.2]
  ```
- **Model**: `QuadrantChart` with title, axis labels (left/right, top/bottom), quadrant labels (1-4), list of `QuadrantPoint(label, x, y)` where x,y ∈ [0,1]
- **Parser**: Regex per line type: `x-axis`, `y-axis`, `quadrant-N`, point `Name: [x, y]`
- **Layout**: None needed — fixed 2×2 grid, points are normalized coordinates
- **Renderer**: Draw 4 quadrant rectangles with alternating fills, axis labels along edges, quadrant labels centered in each quadrant, scatter points as circles with text labels
- **Complexity**: Low
- **Estimated new tests**: ~8 parser, ~7 renderer, 2 snapshots

### 3. Timeline ✅
- **Keyword**: `timeline`
- **Status**: Experimental (syntax stable)
- **Syntax**:
  ```
  timeline
    title History of Social Media
    section Early Days
      2002 : LinkedIn
      2004 : Facebook : Google
    section Modern Era
      2010 : Instagram
      2019 : TikTok
  ```
- **Model**: `TimelineDiagram` with title, list of `TimelineSection(name, periods)`, each `TimelinePeriod(label, events[])`. If no sections, periods go into a default section.
- **Parser**: Line-by-line: `section X` starts a new section, `{period} : {event} [: {event}]` adds events to current section. Continuation lines starting with `: event` attach to the previous period.
- **Layout**: Linear horizontal. Each period gets a column. Sections group columns with a colored band. Events stack vertically under each period marker. Arithmetic layout (like sequence diagrams).
- **Renderer**: Horizontal timeline axis with period markers (circles on a line), event boxes below, section bands as colored rectangles behind groups, optional title at top.
- **Complexity**: Low-moderate (mostly sizing/spacing arithmetic)
- **Estimated new tests**: ~8 parser, ~7 renderer, 2 snapshots

### 4. GitGraph ✅
- **Keyword**: `gitGraph`
- **Status**: Stable (long-standing)
- **Syntax**:
  ```
  gitGraph
    commit
    commit
    branch develop
    checkout develop
    commit
    commit
    checkout main
    merge develop
    commit
  ```
- **Model**: `GitGraph` with ordered list of `GitAction` (commit/branch/checkout/merge/cherry-pick). Each commit has optional `id`, `type` (NORMAL/HIGHLIGHT/REVERSE), `tag`. Track branch creation order and head positions. Orientation (LR default, TB, BT).
- **Parser**: Keyword-based line parsing: `commit [id: "x"] [type: HIGHLIGHT] [tag: "v1"]`, `branch name [order: N]`, `checkout name`, `merge name [id/type/tag]`, `cherry-pick id: "x"`. No regex-heavy grammar — mostly keyword+attribute parsing.
- **Layout**: Lane-based. Each branch gets a horizontal lane (y-position). Commits are placed left-to-right (or top-to-bottom for TB). Merge commits connect lanes. Branch lines are colored per-branch. Need to track which branch each commit belongs to and compute merge paths.
- **Renderer**: Colored branch lanes as horizontal lines, commits as circles (filled for NORMAL, rectangle for HIGHLIGHT, crossed for REVERSE), merge lines connecting branches, tags as rounded labels, branch name labels on the left, optional commit ID labels.
- **Complexity**: Moderate (branch tracking state machine, merge path routing)
- **Estimated new tests**: ~12 parser, ~8 renderer, 3 snapshots

### 5. Radar Chart ✅
- **Keyword**: `radar-beta`
- **Status**: Beta (v11.6.0+)
- **Syntax**:
  ```
  radar-beta
    title Skills Assessment
    axis Design, Frontend, Backend, DevOps, Testing
    curve "Team A" { 4, 3, 5, 2, 4 }
    curve "Team B" { 3, 5, 2, 4, 3 }
    max 5
    graticule polygon
  ```
- **Model**: `RadarChart` with title, list of `RadarAxis(id, label)`, list of `RadarCurve(id, label, values[])`, options (ticks, graticule type, min, max, showLegend).
- **Parser**: `axis` line defines axes (comma-separated IDs with optional `["label"]`). `curve` lines define data series. Option lines for `ticks`, `graticule`, `min`, `max`, `showLegend`.
- **Layout**: Polar. Axes are evenly spaced around center (angle = 2π/N). Values are normalized to [min, max] and mapped to radius. Graticule is concentric circles or polygons.
- **Renderer**: Draw graticule (circles or polygons), axis lines radiating from center, axis labels at tips, filled semi-transparent polygons for each curve, legend box.
- **Complexity**: Moderate (polar coordinate math, polygon rendering)
- **Estimated new tests**: ~8 parser, ~7 renderer, 2 snapshots

### 6. Treemap ✅
- **Keyword**: `treemap-beta`
- **Status**: Beta (recent)
- **Syntax**:
  ```
  treemap-beta
    "Root"
      "Section A"
        "Item 1": 30
        "Item 2": 20
      "Section B"
        "Item 3": 50
  ```
- **Model**: `TreemapDiagram` with a tree of `TreemapNode(label, value?, children[])`. Leaf nodes have values; branch nodes derive value from sum of children.
- **Parser**: Indentation-based (like mindmap). Each line is `"label"[: value]`. Indentation depth determines parent-child relationship. Optional `:::className` for styling.
- **Layout**: Squarified treemap algorithm. Recursively subdivide a rectangle into sub-rectangles proportional to node values. Well-documented algorithm (Bruls, Huizing, van Wijk 2000).
- **Renderer**: Nested rectangles with fills from the color scale, labels centered in each rectangle (hidden if rectangle too small), optional value display.
- **Complexity**: Moderate (squarified algorithm is ~50 lines, but needs careful rectangle subdivision)
- **Estimated new tests**: ~8 parser, ~7 renderer, 2 snapshots

### 7. Venn Diagram ✅
- **Keyword**: `venn-beta`
- **Status**: Beta (v11.12.3+)
- **Syntax**:
  ```
  venn-beta
    set A["Frontend"]
    set B["Backend"]
    set C["DevOps"]
    union A, B ["Full Stack"]
    union B, C ["SRE"]
  ```
- **Model**: `VennDiagram` with list of `VennSet(id, label, size?)`, list of `VennUnion(setIds[], label?, size?)`, list of `VennText(parentId, label)`.
- **Parser**: Line-by-line: `set id["label"][: size]`, `union id1, id2[, ...]["label"][: size]`, indented `text ["label"]` attaches to previous set/union.
- **Layout**: 2-set: two overlapping circles. 3-set: three circles in triangular arrangement with pairwise overlaps. For N>3 or sized sets, need a circle-packing/overlap optimization (most complex layout of the batch). Can start with fixed geometric arrangements for 2-3 sets.
- **Renderer**: Semi-transparent filled circles, labels centered in exclusive regions and overlap regions, optional text nodes.
- **Complexity**: Moderate-high (circle positioning for proper overlap areas; can simplify with fixed geometry for common cases)
- **Estimated new tests**: ~8 parser, ~7 renderer, 2 snapshots

### 8. Mindmap ✅
- **Keyword**: `mindmap`
- **Status**: Experimental (syntax stable, icons experimental)
- **Syntax**:
  ```
  mindmap
    root((Central Idea))
      Topic A
        Subtopic 1
        Subtopic 2
      Topic B
        Subtopic 3
  ```
- **Model**: `MindmapDiagram` with root `MindmapNode(id, label, shape, children[])`. Shapes reuse flowchart syntax: `[square]`, `(rounded)`, `((circle))`, `))cloud((`, `{{hexagon}}`, etc.
- **Parser**: Indentation-based. First non-whitespace line is root. Subsequent lines' indentation level determines depth. Shape is determined by delimiter characters around the label text.
- **Layout**: Radial tree layout. Root at center, children fan out. Or tidy tree (top-down). The radial layout is the canonical mindmap look. Alternatively can use a simple left-right tree.
- **Renderer**: Nodes as shapes (circles, rounded rects, clouds, etc.), curved links from parent to child, root node emphasized (larger, different fill).
- **Complexity**: Moderate-high (radial tree layout algorithm; shape variety)
- **Estimated new tests**: ~10 parser, ~8 renderer, 3 snapshots

## Architecture Notes

Each diagram follows the same 3-file pattern:
1. `src/Mermaider/Models/{Type}.cs` — immutable record model
2. `src/Mermaider/Parsing/{Type}Parser.cs` — `[GeneratedRegex]` line-by-line parser
3. `src/Mermaider/Rendering/{Type}SvgRenderer.cs` — pooled StringBuilder SVG output

Diagrams without complex layout (pie, quadrant, radar) skip the Layout stage.
Diagrams with arithmetic layout (timeline, gitgraph) compute positions in the renderer or a dedicated static layout method.
Diagrams with tree/graph layout (treemap, mindmap) get a dedicated layout algorithm.

Wiring checklist for each:
- [ ] Add variant to `DiagramType` enum
- [ ] Add header regex to `DiagramDetector`
- [ ] Add case to `MermaidRenderer.RenderToBuilder` switch
- [ ] Parser tests, renderer tests, snapshot tests
- [ ] Accept `.verified.svg` golden files
