# Layout engine

Mermaider ships a complete, zero-dependency implementation of the Sugiyama layered layout algorithm. It is used for **flowchart**, **class**, and **ER** diagrams. All other diagram types use purpose-built arithmetic layouts.

## What is Sugiyama?

The [Sugiyama framework](https://en.wikipedia.org/wiki/Layered_graph_drawing) (1981) is the standard algorithm for drawing directed graphs top-to-bottom or left-to-right. It is the foundation of Graphviz `dot`, Dagre, and ELK. The algorithm assigns nodes to horizontal layers (ranks), then minimizes edge crossings between layers, then assigns final coordinates.

## Five phases

| Phase | Class | What it does |
|---|---|---|
| **1. Cycle removal** | `CycleRemover` | DFS identifies back-edges and reverses them so the graph is a DAG. Reversed edges are restored after layout to preserve original arrow direction. |
| **2. Layer assignment** | `LayerAssigner` | Longest-path assigns each node to a layer. Edges spanning multiple layers get virtual nodes inserted so every edge covers exactly one layer boundary. |
| **3. Crossing minimization** | `CrossingMinimizer` | Barycenter heuristic: for each node, set its position to the average position of its neighbors in the adjacent layer. Alternating top-down and bottom-up sweeps. Configurable iteration count (default: 4). |
| **4. Coordinate assignment** | `CoordinateAssigner` | Priority-based X assignment with median alignment — nodes are shifted toward the median of their neighbors to shorten edges. |
| **5. Edge routing** | `EdgeRouter` | Rectilinear polyline paths with rounded corners. Handles back-edge detours (looping behind the source layer), shared trunk segments for fan-out edges, and computes optimal label positions on the longest straight segment. |

An optional **direction transform** rotates the canonical top-down result to LR, RL, or BT by swapping and mirroring axes.

## Performance

Benchmarked on a 6-node flowchart (Apple M2 Pro, .NET 10, BenchmarkDotNet):

| | Time | Allocated |
|---|---:|---:|
| Mermaider Sugiyama | **3.4 µs** | **16 KB** |
| Microsoft MSAGL | 247 µs | 558 KB |

**73× faster, 35× fewer allocations** for the same graph.

The implementation uses flat array-backed storage (`GraphBuffer`, pooled via `ArrayPool<T>`) instead of object graphs, minimising GC pressure. Virtual nodes for long edges are appended to flat arrays rather than creating linked list structures.

All three historically O(N³) phases — `CrossingMinimizer`, `LayerAssigner`, and `CycleRemover` — were rewritten to use a CSR (Compressed Sparse Row) adjacency index and run in **O(V + E)**.

## Supported features

- **All four directions:** TD (top-down), LR (left-right), RL, BT
- **Subgraphs:** compound nodes with nested children; bounding boxes are computed and child nodes stay inside their parent
- **Disconnected components:** detected automatically and tiled side-by-side
- **Same-rank constraints:** invisible edges (`~~~`) force two nodes into the same layer
- **Edge labels:** label dimensions are reserved in the coordinate pass; the router returns an optimal label position on the longest straight segment
- **Back-edges:** self-loops and cycles are drawn with a detour arc that clears the source layer

## Using the layout package standalone

The `Sugiyama` NuGet package has no dependency on Mermaider — you can use it for any directed graph rendering:

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

`LayoutResult` contains:

- **`Nodes`** — positioned rectangles with `(X, Y, Width, Height)` in absolute coordinates
- **`Edges`** — polyline paths as `IReadOnlyList<LayoutPoint>`, plus optional `LabelPosition`
- **`Groups`** — subgraph bounding boxes, nested via `Children`
- **`Width` / `Height`** — total canvas dimensions including padding

### Layout options

```csharp
var options = new LayoutOptions
{
    Padding           = 40,   // canvas padding in px
    NodeSpacing       = 36,   // horizontal gap between siblings
    LayerSpacing      = 72,   // vertical gap between layers
    CrossingIterations = 4,   // barycenter sweep passes
    SeparateComponents = true, // tile disconnected components
};

var result = SugiyamaLayout.Compute(graph, options);
```

## MSAGL alternative

For graphs where Sugiyama's heuristic crossing minimizer produces unsatisfactory results, swap in Microsoft MSAGL:

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

MSAGL produces higher-quality layouts for dense graphs but is significantly slower (see benchmark above). The Sugiyama engine is the right choice for typical Mermaid diagrams.
