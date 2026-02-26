# Plan: Lightweight Sugiyama Layout Engine

## Motivation

BenchmarkDotNet profiling reveals that **Microsoft MSAGL accounts for ~95% of both
time and memory** across the entire parse → layout → render pipeline:

```
Phase-isolated benchmarks (simple flowchart: 6 nodes, 6 edges)

| Phase              | Mean       | Allocated  | % of total |
|--------------------|------------|------------|------------|
| Parse              |   7.20 us  |  11.66 KB  |     2%     |
| Layout (MSAGL)     | 225.40 us  | 554.45 KB  |    95%     |
| Render (SVG)       |   9.00 us  |  15.60 KB  |     3%     |
| EndToEnd           | 455.80 us  | 582.64 KB  |   100%     |
```

MSAGL is a research-grade library designed for interactive WPF/WinForms viewers with
full spline edge routing, comprehensive crossing minimization, and geometry curve
abstractions. Every `GeometryGraph`, `Node`, and `Edge` allocates deep object trees
(`ICurve`, `BoundingBox`, `EdgeGeometry`, `Arrowhead`, visibility graphs, etc.).
For a 6-node flowchart, it allocates **~92 KB per node**.

Our parse + render phases together are ~27 KB. A purpose-built layout engine for
Mermaid's constrained use case (typically <50 nodes, 4 direction modes, rectilinear
edges only) should be able to match that order of magnitude.

**Target:** reduce layout for the simple flowchart from 554 KB → <40 KB and from
225 us → <50 us, bringing total end-to-end under 70 KB / 70 us.

## Scope

A new project `src/Mermaid.Layout` that:

1. Replaces MSAGL for Flowchart and State diagrams (the two `MermaidGraph`-based types)
2. Replaces MSAGL for Class and ER diagrams (same Sugiyama algorithm, different models)
3. Leaves Sequence diagrams untouched (already uses arithmetic layout at 12 us / 30 KB)

The MSAGL dependency moves from `Mermaid.csproj` to a compatibility/fallback package
(or is removed entirely once parity is reached).

## Algorithm: Sugiyama Layered Layout

The Sugiyama method is a 5-phase pipeline, each phase well-defined and independently
testable. Below is the planned implementation for each phase.

### Phase 1: Cycle Removal

**Goal:** Make the graph acyclic by reversing a minimal set of back-edges.

**Algorithm:** Depth-first search. Mark edges to nodes currently on the DFS stack as
"reversed". After layout, restore original direction and flip the route points.

**Allocation strategy:**
- `Span<NodeState>` enum array (stackalloc for ≤64 nodes, ArrayPool beyond)
- Reversed edge set as a `BitArray` indexed by edge ordinal

**Complexity:** O(V + E)

### Phase 2: Layer Assignment (Longest Path)

**Goal:** Assign each node to a discrete integer layer such that all edges point
downward (in TD mode).

**Algorithm:** Longest-path layering. Topological sort, then assign each node to
`max(predecessor layers) + 1`. This is simpler than the network-simplex approach MSAGL
uses but produces good results for Mermaid-sized graphs.

**Allocation strategy:**
- `int[]` layer assignment, one per node (stackalloc for ≤64 nodes)
- Topological order via in-degree counting (Kahn's algorithm) with an `int[]` queue

For edges spanning multiple layers, insert virtual (dummy) nodes to maintain the
single-layer-step invariant. Virtual nodes are lightweight structs in a pooled list.

**Complexity:** O(V + E)

### Phase 3: Crossing Minimization (Barycenter Heuristic)

**Goal:** Order nodes within each layer to minimize edge crossings.

**Algorithm:** Layer-by-layer sweep using the barycenter heuristic. For each layer,
compute each node's barycenter (average position of its neighbors in the adjacent
layer), then sort by that value. Repeat top-down and bottom-up for a configurable
number of iterations (default: 4, matching `thoroughness` in `beautiful-mermaid`).

**Allocation strategy:**
- `double[]` barycenter values per layer (reuse across sweeps)
- `int[]` order permutation per layer
- No LINQ, no temporary lists — sort in-place

**Complexity:** O(iterations × E) per sweep

### Phase 4: Coordinate Assignment (Brandes-Köpf)

**Goal:** Assign X/Y coordinates to nodes respecting layer assignment and ordering,
while keeping the graph compact and edges short.

**Algorithm:** Brandes-Köpf (used in `dagre` and `ELK.js`). Four passes (up-left,
up-right, down-left, down-right) to compute median-aligned coordinates, then average.
This is what `beautiful-mermaid`'s upstream ELK engine uses.

**Allocation strategy:**
- Four `double[]` arrays for the four alignment passes
- `int[]` arrays for block/root tracking
- All arrays sized to `nodeCount` (including virtual nodes)

**Complexity:** O(V + E) per pass

### Phase 5: Edge Routing (Rectilinear)

**Goal:** Generate polyline edge paths with orthogonal (rectilinear) segments.

**Algorithm:** For each edge, route from source node's port to target node's port
using simple orthogonal connectors. For edges that cross layers, follow the virtual
node chain. Each segment is either horizontal (within a layer gap) or vertical (between
layers).

This is much simpler than MSAGL's full visibility-graph-based rectilinear router,
which is the main source of its allocation overhead.

**Allocation strategy:**
- Edge points written directly to a pooled `List<Point>` (where `Point` is an
  existing `readonly record struct` — zero heap allocation per point)
- No spline fitting, no curve objects

**Complexity:** O(E × layers)

### Phase 6: Direction Transform

**Goal:** Support TD, LR, BT, RL by rotating coordinates.

**Implementation:** A single pass that swaps/negates X/Y based on `Direction`.
No matrix allocation — just a `switch` over the 4 cases.

### Phase 7: Subgraph (Group) Layout

**Goal:** Compute bounding boxes for `subgraph` regions.

**Implementation:** After coordinate assignment, iterate subgraph node sets and
compute min/max bounds (same logic as current `ExtractGroups`, already lean).

## Project Structure

```
src/
  Mermaid.Layout/
    Mermaid.Layout.csproj          # no external dependencies
    SugiyamaLayout.cs              # public entry point
    Internal/
      CycleRemover.cs              # Phase 1
      LayerAssigner.cs             # Phase 2
      CrossingMinimizer.cs         # Phase 3
      CoordinateAssigner.cs        # Phase 4
      EdgeRouter.cs                # Phase 5
      DirectionTransform.cs        # Phase 6
      GraphBuffer.cs               # Pooled working memory
tests/
  Mermaid.Layout.Tests/
    CycleRemoverTests.cs
    LayerAssignerTests.cs
    CrossingMinimizerTests.cs
    CoordinateAssignerTests.cs
    EdgeRouterTests.cs
    IntegrationTests.cs            # Compare output vs MSAGL baseline
    LayoutBenchmarks.cs            # Side-by-side benchmark vs MSAGL
```

## Integration Plan

### Step 1: New project, side-by-side

- Create `Mermaid.Layout` with the same `ILayoutEngine` contract:
  `PositionedGraph Layout(MermaidGraph graph, LayoutOptions options)`
- Wire it up behind a feature flag (e.g. `RenderOptions.LayoutEngine = LayoutEngine.Lightweight`)
- Keep MSAGL as default during development

### Step 2: Golden-file comparison tests

- For every diagram in the gallery, render with both engines
- Assert that node positions differ by ≤ configurable tolerance (exact pixel match
  is not expected — different algorithms produce different-but-valid layouts)
- Visually compare in the gallery web server

### Step 3: Benchmark parity gate

Before switching the default, the new engine must beat MSAGL on:
- **Allocated memory** (target: >10x reduction)
- **Mean time** (target: >3x reduction)
- **Identical SVG output structure** (same nodes, edges, groups — coordinates may differ)

### Step 4: Switch default, deprecate MSAGL path

- Make `LayoutEngine.Lightweight` the default
- Move `Microsoft.Msagl` dependency to an optional compatibility package or remove it
- This also eliminates the AOT trim warnings from MSAGL's internal `BinaryFormatter` usage

## Allocation Budget

Target allocation budget for simple flowchart (6 nodes, 6 edges):

| Component                    | Budget   |
|------------------------------|----------|
| Layer assignment arrays      |  ~0.5 KB |
| Virtual nodes (if any)       |  ~0.5 KB |
| Crossing minimization arrays |  ~1.0 KB |
| Coordinate assignment arrays |  ~2.0 KB |
| Edge routing points          |  ~1.0 KB |
| PositionedGraph output       | ~12.0 KB |
| **Total layout**             | **~17 KB** |

Compared to current MSAGL: **554 KB → ~17 KB (32x reduction)**

## .NET Techniques

- `stackalloc` / `ArrayPool<T>` for all working arrays ≤ 64 elements
- `readonly record struct` for internal node/edge representations
- `Span<T>` for in-place sorting during crossing minimization
- No LINQ in hot paths
- No `Dictionary<K,V>` — use flat arrays indexed by node ordinal
- `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for coordinate math

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Barycenter heuristic produces worse crossing counts than MSAGL's median | Allow configurable iteration count; for Mermaid-sized graphs (<50 nodes) the difference is negligible |
| Simple rectilinear router produces overlapping edges | Add port assignment logic to offset edges at shared nodes |
| Subgraph layout changes visually | Subgraph bounding box logic is the same — only node positions change |
| Edge labels positioned differently | Use same midpoint-of-polyline logic as current renderer |
| Regression in large graphs (50+ nodes) | Keep MSAGL as opt-in fallback via `LayoutEngine.Msagl` |

## Non-Goals

- Spline/bezier edge routing (rectilinear is sufficient for Mermaid)
- Force-directed layout (not suitable for hierarchical diagrams)
- Incremental/animated layout (single-shot rendering only)
- General-purpose graph layout library (this is purpose-built for Mermaid)

## Implementation Status: COMPLETE

The lightweight Sugiyama engine has been implemented in `src/Mermaid.Layout/` and is now
the **default layout engine**. MSAGL is retained as an opt-in fallback via
`RenderOptions.LayoutEngine = LayoutEngine.Msagl`.

### Benchmark Results (Simple Flowchart: 6 nodes, 6 edges)

```
| Method               | Mean       | Allocated |
|--------------------- |-----------:|----------:|
| Layout_Msagl         | 227.479 us | 554.37 KB |
| Layout_Lightweight   |   1.865 us |   7.95 KB |
| EndToEnd_Msagl       | 355.676 us | 582.48 KB |
| EndToEnd_Lightweight |  20.939 us |  38.29 KB |
```

**Layout phase: 122x faster, 70x fewer allocations.**
**End-to-end: 17x faster, 15x fewer allocations.**

### Success Criteria — Results

1. ✅ All 212 existing tests pass with the new engine (snapshots updated)
2. ✅ Layout dropped from 225 us / 554 KB to **1.9 us / 8 KB** (far exceeds target)
3. ✅ 12 dedicated layout unit tests + 78 mermaid-js compatibility tests pass
4. ✅ AOT smoke test — MSAGL dependency removed from core `Mermaid` package
5. ✅ `Microsoft.Msagl` moved to optional `Mermaid.Layout.Msagl` package with pluggable `IGraphLayoutProvider` API
