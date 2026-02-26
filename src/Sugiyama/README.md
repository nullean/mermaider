# Sugiyama

A lightweight [Sugiyama](https://en.wikipedia.org/wiki/Layered_graph_drawing) (layered/hierarchical) graph
layout engine for .NET.

- **Zero external dependencies** — no MSAGL, no ELK, no native binaries
- **Allocation-aware** — uses `ArrayPool<T>`, flat arrays, pooled buffers
- **AOT-friendly** — no reflection, no runtime code generation
- **Small** — ~500 lines of layout code across 6 phases

## Quick start

```bash
dotnet add package Sugiyama
```

```csharp
using Sugiyama;

var graph = new LayoutGraph(
    LayoutDirection.TD,
    Nodes: [
        new LayoutNode("A", Width: 100, Height: 40),
        new LayoutNode("B", Width: 100, Height: 40),
        new LayoutNode("C", Width: 100, Height: 40),
    ],
    Edges: [
        new LayoutEdge("A", "B"),
        new LayoutEdge("A", "C"),
    ],
    Subgraphs: []);

var result = SugiyamaLayout.Compute(graph);

foreach (var node in result.Nodes)
    Console.WriteLine($"{node.Id}: ({node.X:F0}, {node.Y:F0})");

foreach (var edge in result.Edges)
    Console.WriteLine($"Edge {edge.OriginalIndex}: {string.Join(" → ", edge.Points)}");
```

## How it works

The engine implements the classic Sugiyama framework in six phases:

| Phase | Class | Description |
|-------|-------|-------------|
| 1 | `CycleRemover` | Make the graph acyclic by reversing back-edges (DFS) |
| 2 | `LayerAssigner` | Assign nodes to layers via longest-path (Kahn's algorithm), insert virtual nodes |
| 3 | `CrossingMinimizer` | Reorder nodes within layers using barycenter heuristic |
| 4 | `CoordinateAssigner` | Assign X/Y coordinates with median-pull alignment |
| 5 | `EdgeRouter` | Generate rectilinear polyline edge paths with port spreading |
| 6 | `DirectionTransform` | Rotate layout for LR, RL, BT directions |

## Options

```csharp
var options = new LayoutOptions
{
    Padding = 40,          // Canvas padding (px)
    NodeSpacing = 28,      // Horizontal gap between sibling nodes
    LayerSpacing = 48,     // Vertical gap between layers
    CrossingIterations = 4 // Barycenter sweep iterations
};

var result = SugiyamaLayout.Compute(graph, options);
```

## Supported directions

`LayoutDirection.TD` (top-down), `LR` (left-right), `BT` (bottom-top), `RL` (right-left).

## Design goals

This engine is optimized for **small-to-medium directed graphs** (< 50 nodes) typical of
software diagrams, flowcharts, state machines, and org charts. It prioritizes:

1. Low allocations over asymptotic optimality
2. Predictable, readable layouts over pixel-perfect compactness
3. Simplicity over configurability

For very large or dense graphs, consider [Microsoft MSAGL](https://github.com/microsoft/automatic-graph-layout)
or [ELK](https://github.com/kieler/elkjs).

## License

Apache 2.0
