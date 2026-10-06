# Layout engine

Mermaider ships its own zero-dependency layered layout engine, published on its own as the `Sugiyama` package. It lays out **flowchart**, **state**, **class**, **ER** and **requirement** diagrams. Architecture diagrams use a directional-grid layout, because their edges name explicit sides. Every other diagram type uses a purpose-built arithmetic layout.

![An ER diagram laid out by the layered engine](../images/diagrams/er.svg)

## A layered engine

The name refers to the [Sugiyama framework](https://en.wikipedia.org/wiki/Layered_graph_drawing) (1981) for drawing directed graphs top to bottom or left to right. Graphviz `dot`, dagre and ELK Layered belong to the same family. The framework breaks cycles, assigns nodes to layers, orders each layer to reduce crossings, assigns coordinates and routes the edges.

The engine keeps those phases, but most of them go well beyond the textbook versions. Compound layout, ports, label columns and the obstacle-aware orthogonal router are outside the classic framework altogether.

## Entry points

The `Sugiyama` package has two entry points:

- `SugiyamaLayout.Compute` runs one layered pipeline per connected component. It has a fast default mode, a balanced mode (`BalancedPlacement`) and a port-aware mode for ER diagrams (`PortAwareLayout`).
- `HierarchicalLayout.Compute` lays out each subgraph on its own, places it as one node of its parent, and routes every edge with the orthogonal router.

Mermaider uses them as follows:

| Diagram | Entry point |
|---|---|
| Flowchart, state, class, requirement | `HierarchicalLayout.Compute` |
| ER | `SugiyamaLayout.Compute` in port-aware mode, with 160 crossing restarts |
| Flowchart and state [text output](../getting-started/index.md#text-output) | `SugiyamaLayout.Compute` in the default mode |

## The phases

| Phase | Class | What it does |
|---|---|---|
| **1. Cycle removal** | `CycleRemover` | A depth-first search finds back edges and reverses them, in the same two-pass order as dagre. The reversed edges get their direction back after routing. |
| **2. Ranking** | `LayerAssigner`, `NetworkSimplexRanker`, `NestingGraphRanker` | Network simplex assigns each node a layer and keeps the total edge length short. With subgraphs, a nesting graph keeps each subgraph's members in one contiguous band of layers. Edges that span several layers get one virtual node per layer. |
| **3. Crossing minimisation** | `CrossingMinimizer` | Barycentre sweeps, alternating down and up, keep the best ordering found (default: 4 iterations). Pairwise swaps and insertion refinement then move nodes whenever that strictly lowers crossings. Optional deterministic restarts (`CrossingRestarts`) replace the result only with strictly fewer crossings. |
| **4. Coordinate assignment** | `CoordinateAssigner`, `BkCoordinateAssigner` | The default mode spreads children under their parents by subtree width, then pulls nodes towards their neighbours' median. The balanced and port-aware modes use Brandes–Köpf: four aligned layouts combined by their medians, with ports spread along each node side and a column reserved for every edge label. |
| **5. Edge routing** | `EdgeRouter`, `ErEdgeRouter`, `FlowEdgeRouter` | The default router draws rectilinear polylines with back-edge detours and shared trunks. In the balanced and port-aware modes, edges run port → label column → port. `HierarchicalLayout.Compute` routes every edge last, on the final boxes, with the orthogonal router. |

A **direction transform** rotates the canonical top-down result to `LR`, `RL` or `BT`. The renderer rounds the bends; the radius comes from the [style preset](../theming/index.md#style-presets).

### Compound graphs

Flowchart, state, class and requirement diagrams go through `HierarchicalLayout.Compute`. It lays out every subgraph on its own and places it as **one node** of its parent, so subgraph boxes can never overlap each other or nodes that do not belong to them. Each subgraph box leaves a free column beside its title, so edges can enter without crossing the title text.

Each level is ordered without seeing the edges that cross its border. After composition, the engine swaps same-layer siblings when that lowers crossings. It also slides a subgraph to centre a fan on its parent when the box stays clear of everything else.

### The orthogonal router

`FlowEdgeRouter` routes every edge with horizontal and vertical segments, on the final node and subgraph boxes.

- **Ports** are spread along the side that faces the other end. They slide onto the outline of diamonds and ellipses, and stay clear of subgraph titles.
- **Route candidates** are tried cheapest first: the level layout's own port → label column → port route, then a straight or single-Z route, then an A\* search on a grid built from the box borders.
- **The search** charges for length, bends, crossing earlier routes, running within 10px of an earlier route, entering a subgraph that holds neither end, and crossing a subgraph title.
- **Rip-up passes** re-route each edge against all the others after the first greedy pass.
- **Post-passes** straighten small jogs and give Z-bends between the same two layers one shared bend line. Labels go just before the arrowhead, so sibling labels line up.

Back edges loop around the outside of the drawing, where they cross the fewest forward edges. The [`Sugiyama` README](https://github.com/nullean/mermaider/blob/main/src/Sugiyama/README.md) lists the exact costs and every pass.

## Performance

Benchmarked on a 6-node flowchart (Apple M2, .NET 10, BenchmarkDotNet medium run), measuring the layout call alone:

| | Time | Allocated |
|---|---:|---:|
| Built-in flowchart layout (what renders use) | **24 µs** | **82 KB** |
| Microsoft MSAGL, same input | 226 µs | 549 KB |
| `SugiyamaLayout.Compute`, default mode, pre-sized nodes | 6.0 µs | 29 KB |

The first two rows run each layout provider on the same parsed flowchart, including node sizing and edge routing: the
built-in layout is about **9.6× faster, with 6.7× less memory allocated**. The last row is the standalone package's basic
entry point on unlabelled edges, without the compound layout or the orthogonal router. A full flowchart render, including the compound layout and the orthogonal router, takes about 109 µs and 209 KB, against 423 µs and 683 KB with MSAGL. Reproduce the layout numbers with:

```bash
dotnet run -c Release --project tests/Mermaider.Benchmarks/Mermaider.Benchmarks.csproj -- --filter '*PhaseBenchmarks.Layout_*'
```

The engine keeps the graph in flat arrays (`GraphBuffer`) with compressed adjacency lists, and rents its integer arrays from `ArrayPool<int>`. The router builds its search grid only when an edge needs a search, and indexes routed segments by grid row and column.

## Supported features

- **All four directions:** `TD` (top-down), `LR` (left-right), `RL`, `BT`
- **Subgraphs:** nested compound nodes; each subgraph is laid out as a unit and its box contains only its own members
- **Edges to subgraphs:** an edge written against a subgraph ends on the subgraph's border
- **Disconnected components:** detected automatically and tiled side by side
- **Invisible edges:** `~~~` shapes the layout like an ordinary edge (the target goes below the source) but is not drawn
- **Edge labels:** each labelled edge gets its own column in the gap between layers, and the gap grows to fit the label
- **Back edges:** cycles are broken for ranking and drawn with their original arrow direction

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

For subgraphs and the orthogonal router, call `HierarchicalLayout.Compute(graph, options)`. It requires an options instance; pass `LayoutOptions.Default` for the defaults. It returns the same `LayoutResult`. Only `SugiyamaLayout.Compute` honours `LayoutGraph.SameRankConstraints`, pairs of nodes that must share a layer.

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
    CrossingIterations = 4,     // barycentre sweep passes
    CrossingRestarts   = 0,     // deterministic restarts; kept only with fewer crossings
    SeparateComponents = true,  // tile disconnected components
    BalancedPlacement  = false, // Brandes–Köpf coordinates with label columns
    GroupPadding       = 24,    // HierarchicalLayout: space inside a subgraph border
    GroupHeaderHeight  = 32,    // HierarchicalLayout: space for a subgraph title
};

var result = SugiyamaLayout.Compute(graph, options);
```

The [`Sugiyama` README](https://github.com/nullean/mermaider/blob/main/src/Sugiyama/README.md) lists every option.

## MSAGL alternative

The optional `Mermaider.Layout.Msagl` package swaps in Microsoft MSAGL for flowchart, state, class and ER diagrams. It is kept for compatibility with output from earlier Mermaider versions. The built-in engine is the recommended choice.

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

MSAGL is much slower (see the benchmark above) and lacks several features of the built-in engine:

- It lays out nodes without their subgraphs, so subgraph boxes can overlap each other or unrelated nodes.
- It has no label columns and no ports on node sides or shape outlines, and edges to a subgraph end on a member node.
- Its class and ER layouts still use the box sizes from before the current design system.
- It ignores class namespaces, class notes, lollipop interface targets and state-diagram notes.

Requirement diagrams and text output always use the built-in engine.
