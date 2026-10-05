# Layout engine

Mermaider ships a complete, zero-dependency implementation of the Sugiyama layered layout algorithm. It lays out **flowchart**, **state**, **class**, **ER** and **requirement** diagrams. Architecture diagrams use a directional-grid layout, because their edges name explicit sides. Every other diagram type uses a purpose-built arithmetic layout.

![An ER diagram laid out by the layered engine](../images/diagrams/er.svg)

## What is Sugiyama?

The [Sugiyama framework](https://en.wikipedia.org/wiki/Layered_graph_drawing) (1981) is the standard algorithm for drawing directed graphs top-to-bottom or left-to-right. It is the foundation of Graphviz `dot`, Dagre, and ELK. The algorithm assigns nodes to horizontal layers (ranks), then minimizes edge crossings between layers, then assigns final coordinates.

## Five phases

| Phase | Class | What it does |
|---|---|---|
| **1. Cycle removal** | `CycleRemover` | A DFS finds back-edges and reverses them so the graph is a DAG, using the same two-pass order as dagre. Reversed edges are restored after layout to preserve the original arrow direction. |
| **2. Layer assignment** | `LayerAssigner` | Network simplex (`NetworkSimplexRanker`, a port of dagre's ranker) assigns each node a layer and keeps the total edge length short. With subgraphs, a nesting graph (`NestingGraphRanker`) keeps each subgraph's members in one contiguous band of layers. Edges that span several layers get virtual nodes, so every edge crosses exactly one layer boundary. |
| **3. Crossing minimization** | `CrossingMinimizer` | Barycenter heuristic: each node moves to the average position of its neighbours in the adjacent layer, in alternating top-down and bottom-up sweeps (default: 4 iterations). Optional deterministic restarts (`CrossingRestarts`) replace the result only when they find strictly fewer crossings. |
| **4. Coordinate assignment** | `CoordinateAssigner`, `BkCoordinateAssigner` | By default, children are spread under their parents by subtree width, then pulled toward the median of their neighbours. The Mermaider diagrams use Brandes–Köpf balanced placement instead: four aligned layouts combined by their median, with edge ports spread along each node side and a column reserved for every edge label. |
| **5. Edge routing** | `EdgeRouter`, `ErEdgeRouter`, `FlowEdgeRouter` | Rectilinear polylines. The default router handles back-edge detours and shared trunks for fan-out edges. ER edges run port → label column → port. Compound layouts route every edge last, on the final boxes, with an obstacle-aware A* search that avoids crossing foreign subgraphs. |

An optional **direction transform** rotates the canonical top-down result to `LR`, `RL`, or `BT` by swapping and mirroring axes. The renderer rounds the bends; the radius comes from the [style preset](../theming/index.md#style-presets).

### Compound graphs

Flowchart, state, class and requirement diagrams go through `HierarchicalLayout`. It lays out every subgraph on its own and then places it as **one node** of its parent, so subgraph boxes can never overlap each other or nodes that do not belong to them. Edges are routed afterwards on the final absolute boxes by `FlowEdgeRouter`.

## Performance

Benchmarked on a 6-node flowchart (Apple M2, .NET 10, BenchmarkDotNet short run), measuring the layout call alone with default options:

| | Time | Allocated |
|---|---:|---:|
| `SugiyamaLayout.Compute` | **6.1 µs** | **29 KB** |
| Microsoft MSAGL | 225 µs | 550 KB |

About **37× faster, with 19× less memory allocated** for the same graph. Reproduce it with:

```bash
dotnet run -c Release --project tests/Mermaider.Benchmarks/Mermaider.Benchmarks.csproj -- --filter '*PhaseBenchmarks.Layout_*'
```

The implementation uses flat array-backed storage (`GraphBuffer`, pooled via `ArrayPool<T>`) instead of object graphs, which keeps GC pressure low. Virtual nodes for long edges are appended to flat arrays rather than creating linked list structures.

## Supported features

- **All four directions:** `TD` (top-down), `LR` (left-right), `RL`, `BT`
- **Subgraphs:** nested compound nodes; each subgraph is laid out as a unit and its box contains only its own members
- **Disconnected components:** detected automatically and tiled side-by-side
- **Invisible edges:** `~~~` shapes the layout like an ordinary edge (the target goes below the source) but is not drawn
- **Edge labels:** each labelled edge gets its own column in the gap between layers, and the gap grows to fit the label
- **Back-edges:** cycles are broken for ranking and drawn with their original arrow direction

## Using the layout package standalone

The `Sugiyama` NuGet package has no dependency on Mermaider. Use it for any directed graph:

```bash
dotnet add package Sugiyama
```

```csharp
using Sugiyama;

var graph = new LayoutGraph(
    LayoutDirection.LR,
    Nodes:
    [
        new LayoutNode("A", Width: 80, Height: 40),
        new LayoutNode("B", Width: 80, Height: 40),
        new LayoutNode("C", Width: 80, Height: 40),
    ],
    Edges:
    [
        new LayoutEdge("A", "B"),
        new LayoutEdge("B", "C"),
    ],
    Subgraphs: []
);

LayoutResult result = SugiyamaLayout.Compute(graph);

foreach (var node in result.Nodes)
    Console.WriteLine($"{node.Id}: ({node.X}, {node.Y})");

foreach (var edge in result.Edges)
    Console.WriteLine($"Edge points: {string.Join(", ", edge.Points)}");
```

For graphs with nested subgraphs, call `HierarchicalLayout.Compute(graph, options)` instead. It returns the same `LayoutResult`. `SugiyamaLayout.Compute` also honours `LayoutGraph.SameRankConstraints`, pairs of nodes that must share a layer.

`LayoutResult` contains:

- **`Nodes`**: positioned rectangles with `(X, Y, Width, Height)` in absolute coordinates
- **`Edges`**: polyline paths as `IReadOnlyList<LayoutPoint>`, plus an optional `LabelPosition`
- **`Groups`**: subgraph bounding boxes, nested via `Children`
- **`Width` / `Height`**: total canvas dimensions including padding

### Layout options

```csharp
var options = new LayoutOptions
{
    Padding            = 40,    // canvas padding in px
    NodeSpacing        = 36,    // gap between siblings in a layer
    LayerSpacing       = 72,    // gap between layers
    CrossingIterations = 4,     // barycenter sweep passes
    CrossingRestarts   = 0,     // deterministic restarts; kept only with fewer crossings
    SeparateComponents = true,  // tile disconnected components
    BalancedPlacement  = false, // Brandes–Köpf coordinates with label columns
};

var result = SugiyamaLayout.Compute(graph, options);
```

## MSAGL alternative

For graphs where the built-in engine produces unsatisfactory results, swap in Microsoft MSAGL for flowchart, state, class and ER diagrams:

```bash
dotnet add package Mermaider.Layout.Msagl
```

```csharp
using Mermaider.Layout.Msagl;

// Set globally — all subsequent RenderSvg calls use MSAGL
MermaidRenderer.SetLayoutProvider(new MsaglLayoutProvider());

// Or per render call:
var options = new RenderOptions
{
    LayoutProvider = new MsaglLayoutProvider()
};
```

MSAGL is significantly slower (see the benchmark above). The built-in engine is the right choice for typical Mermaid diagrams.
