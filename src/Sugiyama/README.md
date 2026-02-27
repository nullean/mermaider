# Sugiyama — Layered Graph Layout for .NET

A lightweight, zero-dependency implementation of the
[Sugiyama framework](https://en.wikipedia.org/wiki/Layered_graph_drawing) for laying out directed graphs
in .NET. AOT-compatible, allocation-aware, and designed for small-to-medium graphs (&lt;50 nodes).

## What is the Sugiyama algorithm?

The Sugiyama algorithm (also called the "layered" or "hierarchical" layout algorithm) is the standard
approach for drawing directed graphs top-to-bottom (or left-to-right). It was introduced by Kozo Sugiyama,
Shôjirô Tagawa, and Mitsuhiko Toda in 1981 and remains the foundation of tools like Graphviz (`dot`),
Dagre, and ELK.

The algorithm works in five sequential phases:

| Phase | Purpose | This implementation |
|---|---|---|
| **1. Cycle removal** | Temporarily reverse back-edges so the graph is a DAG | `CycleRemover` — DFS-based, reverses minimum edges |
| **2. Layer assignment** | Assign each node to a horizontal layer (rank) | `LayerAssigner` — longest-path with virtual nodes for multi-layer edges |
| **3. Crossing minimization** | Reorder nodes within each layer to reduce edge crossings | `CrossingMinimizer` — barycenter heuristic with configurable sweep count |
| **4. Coordinate assignment** | Assign X/Y positions respecting spacing constraints | `CoordinateAssigner` — priority-based with median alignment |
| **5. Edge routing** | Generate polyline paths for edges | `EdgeRouter` — rectilinear routing with rounded corners, back-edge detours, shared trunks |

After these five phases, an optional **direction transform** rotates the result for LR, RL, or BT layouts.

## Usage

The Sugiyama package is independent of Mermaid and can be used for any directed graph layout.

### Install

```bash
dotnet add package Sugiyama
```

### Basic example

```csharp
using Sugiyama;

var graph = new LayoutGraph(
    LayoutDirection.TD,
    Nodes:
    [
        new LayoutNode("A", Width: 80, Height: 40),
        new LayoutNode("B", Width: 80, Height: 40),
        new LayoutNode("C", Width: 80, Height: 40),
        new LayoutNode("D", Width: 80, Height: 40),
    ],
    Edges:
    [
        new LayoutEdge("A", "B"),
        new LayoutEdge("A", "C"),
        new LayoutEdge("B", "D"),
        new LayoutEdge("C", "D"),
    ],
    Subgraphs: []
);

LayoutResult result = SugiyamaLayout.Compute(graph);

foreach (var node in result.Nodes)
    Console.WriteLine($"{node.Id}: ({node.X}, {node.Y}) {node.Width}x{node.Height}");

foreach (var edge in result.Edges)
{
    var path = string.Join(" -> ", edge.Points.Select(p => $"({p.X},{p.Y})"));
    Console.WriteLine($"Edge {edge.OriginalIndex}: {path}");
}
```

### Options

```csharp
var options = new LayoutOptions
{
    Padding = 40,           // canvas padding (px)
    NodeSpacing = 36,       // horizontal gap between sibling nodes
    LayerSpacing = 72,      // vertical gap between layers
    CrossingIterations = 4, // barycenter sweep count
    SeparateComponents = true, // tile disconnected components
};

var result = SugiyamaLayout.Compute(graph, options);
```

### Subgraphs

Nodes can be grouped into subgraphs (compound nodes). The layout engine computes bounding
boxes for each group and ensures child nodes are contained within their parent:

```csharp
var graph = new LayoutGraph(
    LayoutDirection.TD,
    Nodes:
    [
        new LayoutNode("web", 80, 40),
        new LayoutNode("api", 80, 40),
        new LayoutNode("db", 80, 40),
    ],
    Edges:
    [
        new LayoutEdge("web", "api"),
        new LayoutEdge("api", "db"),
    ],
    Subgraphs:
    [
        new LayoutSubgraph("frontend", "Frontend", ["web"], []),
        new LayoutSubgraph("backend", "Backend", ["api", "db"], []),
    ]
);

var result = SugiyamaLayout.Compute(graph);

foreach (var group in result.Groups)
    Console.WriteLine($"{group.Id}: ({group.X},{group.Y}) {group.Width}x{group.Height}");
```

### Edge labels

Supply label dimensions to reserve space along the edge path. The engine returns an optimal
label position on the longest straight segment:

```csharp
var edges = new[]
{
    new LayoutEdge("A", "B", LabelWidth: 40, LabelHeight: 16),
};

// After layout:
var labelPos = result.Edges[0].LabelPosition; // LayoutPoint?
```

### Direction

The engine supports all four Mermaid directions. Internally, LR/RL/BT layouts are transformed
to TD (top-down), laid out canonically, then rotated back:

```csharp
new LayoutGraph(LayoutDirection.LR, nodes, edges, subgraphs);
```

## Output

`SugiyamaLayout.Compute` returns a `LayoutResult` with:

- **`Nodes`** — positioned rectangles with `(X, Y, Width, Height)` in absolute coordinates
- **`Edges`** — polyline paths as `IReadOnlyList<LayoutPoint>`, plus optional `LabelPosition`
- **`Groups`** — bounding boxes for subgraphs, nested via `Children`
- **`Width` / `Height`** — total canvas dimensions including padding

All coordinates are in a top-left origin system. The caller is responsible for rendering
(SVG, Canvas, PDF, etc.).

## Performance

On a 6-node flowchart (Apple M2 Pro, .NET 10):

| | Time | Allocated |
|---|---:|---:|
| Sugiyama layout | **3.4 &micro;s** | **16 KB** |
| Microsoft MSAGL | 247 &micro;s | 558 KB |

**73&times; faster, 35&times; fewer allocations.**

The engine uses array-backed storage (`GraphBuffer`) instead of object graphs, minimizing
GC pressure. Virtual nodes for long edges are appended to flat arrays rather than creating
linked structures.

## Internals

The implementation lives in `Internal/`:

| File | Phase | Notes |
|---|---|---|
| `GraphBuffer.cs` | — | Flat array storage for node positions, layers, edges. Implements `IDisposable` for `ArrayPool` returns. |
| `CycleRemover.cs` | 1 | DFS to find and reverse back-edges. |
| `LayerAssigner.cs` | 2 | Longest-path layering. Inserts virtual nodes for edges spanning multiple layers. |
| `CrossingMinimizer.cs` | 3 | Barycenter heuristic with alternating up/down sweeps. |
| `CoordinateAssigner.cs` | 4 | Priority-based X assignment with median alignment to reduce edge length. |
| `EdgeRouter.cs` | 5 | Rectilinear polyline routing. Handles back-edge detours, shared trunk segments, and computes label positions. |
| `DirectionTransform.cs` | Post | Rotates TD coordinates to LR/RL/BT by swapping and mirroring axes. |
