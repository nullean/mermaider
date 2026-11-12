---
name: sugiyama
description: >-
  Use when .NET code needs to position the nodes of a directed graph in layers (top-down or left-right) and route
  its edges, for a diagram, a dependency view or a workflow editor. Sugiyama computes coordinates and edge polylines
  for boxes whose sizes you supply; it draws nothing.
---

# Sugiyama

`using Sugiyama;` Build a `LayoutGraph`, call a `Compute`, read back absolute coordinates. It is tuned for the
graphs diagrams produce (tens of nodes, not thousands), and knows nothing about Mermaid or SVG.

## The calls you need

```csharp
var graph = new LayoutGraph(
    LayoutDirection.TD,                                   // TD, LR, BT, RL
    Nodes: [new LayoutNode("a", 80, 40), new LayoutNode("b", 80, 40) { Outline = PortOutline.Diamond }],
    Edges: [new LayoutEdge("a", "b", LabelWidth: 40, LabelHeight: 16)],
    Subgraphs: []);

LayoutResult result = SugiyamaLayout.Compute(graph);      // or Compute(graph, options)

foreach (var n in result.Nodes)  Console.WriteLine($"{n.Id} {n.X},{n.Y} {n.Width}x{n.Height}");
foreach (var e in result.Edges)  Console.WriteLine($"{graph.Edges[e.OriginalIndex].Source}: {e.Points.Count} points, label at {e.LabelPosition}");
```

Nested groups: use `HierarchicalLayout`, which lays out every subgraph on its own so boxes never overlap, and
routes edges orthogonally around them.

```csharp
var grouped = graph with
{
    Subgraphs = [new LayoutSubgraph("g1", "Backend", NodeIds: ["a", "b"], Children: [])],
};
var laidOut = HierarchicalLayout.Compute(grouped, LayoutOptions.Default with { GroupPadding = 16 });
foreach (var g in laidOut.Groups) Console.WriteLine($"{g.Label}: {g.X},{g.Y} {g.Width}x{g.Height}");
```

Tuning and bounding the work:

```csharp
var options = new LayoutOptions
{
    NodeSpacing = 36, LayerSpacing = 72, Padding = 40,
    BalancedPlacement = true,                            // Brandes–Köpf placement, ports and label columns
    CancellationToken = ct,
    MaxNodeCount = 5_000,                                // InvalidOperationException past it, virtual nodes included
};
```

## Gotchas

- Node sizes are inputs: measure your text first. Node `X`/`Y` are the top-left corner, not the centre.
- An edge whose endpoint is not a node `Id` (ordinal, case-sensitive) is dropped without an error. Map results back
  through `LayoutEdgeResult.OriginalIndex` into `graph.Edges`, never by list position.
- `LayoutResult.Width`/`Height` already include `Padding`; size the canvas from them.
- `SugiyamaLayout.Compute` only fits group boxes around their members afterwards, so groups can overlap. Use
  `HierarchicalLayout.Compute` whenever subgraphs matter; it takes options without a default.
- Cycles are fine: back edges are reversed for layering and their points flipped back, so arrows keep your direction.

Docs: https://mermaider.nullean.net
