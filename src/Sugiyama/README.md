# Sugiyama

**A layered layout engine for .NET (network simplex, Brandes–Köpf, orthogonal routing)**

`Sugiyama` lays out directed graphs in layers, top to bottom or left to right. It has no dependencies, works with
Native AOT, and keeps allocations low. It is tuned for the small-to-medium graphs that diagrams produce, typically
fewer than 50 nodes.

The name refers to the [Sugiyama framework](https://en.wikipedia.org/wiki/Layered_graph_drawing) for layered drawing
(Sugiyama, Tagawa and Toda, 1981). This engine belongs to the same family as Graphviz `dot`, dagre and ELK Layered.
It keeps the framework's phases: break cycles, assign layers, order each layer, assign coordinates, route edges. Most
phases go well beyond the textbook versions, and some features are outside the classic framework altogether:

- compound layout, where nested subgraphs are laid out recursively and can never overlap
- ports spread along node sides, and a column reserved for every edge label
- an obstacle-aware orthogonal router that runs an A* search with rip-up passes

[Mermaider](https://github.com/nullean/mermaider) uses this engine for flowchart, state, class, ER and requirement
diagrams. The package itself knows nothing about Mermaid.

## Install

```bash
dotnet add package Sugiyama
```

## Quick start

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

## Choosing an entry point

The package has two entry points. Both take a `LayoutGraph` and return a `LayoutResult`.

| Entry point | Best for | Subgraphs | Router |
|---|---|---|---|
| `SugiyamaLayout.Compute(graph, options?)` | Flat graphs and dense graphs such as ER diagrams | Constrain ranking; boxes are fitted around members after placement | `EdgeRouter` in the default mode, `ErEdgeRouter` in balanced or port-aware mode |
| `HierarchicalLayout.Compute(graph, options)` | Nested subgraphs, and any graph that should get the orthogonal router | Each subgraph is laid out on its own and placed as one node of its parent | `FlowEdgeRouter` on the final boxes |

`SugiyamaLayout.Compute` itself has three modes:

- **Default.** Subtree-width placement and the classic rectilinear router. This is the fastest mode.
- **Balanced** (`BalancedPlacement = true`). Brandes–Köpf placement with ports and label columns.
- **Port-aware** (`PortAwareLayout = true` with `TightSourceLayering = true`). The balanced placement, plus a choice
  among equally good orderings by how they route. It suits entity-relationship diagrams.

`HierarchicalLayout.Compute` always uses the balanced mode for each level.

## How it works

### `SugiyamaLayout.Compute` step by step

`SugiyamaLayout.Compute` first splits the graph into weakly connected components. It lays out each component on its
own and tiles the results in a grid. Set `SeparateComponents = false` to lay out the whole graph in one pass.

Every phase works top-down. For `LR` and `RL`, the engine swaps each node's width and height before layout, and the
final direction transform swaps them back.

#### 1. Cycle removal

Layering needs an acyclic graph. `CycleRemover` runs a depth-first search and reverses every back edge it finds. The
search starts from the sources (nodes with no incoming edges) in declaration order, then from the remaining nodes.
This gives the same feedback set as dagre. After routing, each reversed edge gets its points flipped, so every arrow
keeps the direction the caller gave it.

#### 2. Ranking with network simplex

`NetworkSimplexRanker` assigns every node an integer layer (rank). It minimises the sum of edge lengths, measured in
layers, subject to each edge spanning at least its `MinLength`. This is the method of Gansner, Koutsofios, North and Vo
(1993), as used by `dot` and dagre. Unlike longest-path layering, it keeps graphs compact and pulls sources down next
to their children.

1. **Simplify.** Parallel edges merge into one with summed weight and the largest minimum length. Self-loops are
   dropped. Each weakly connected component is ranked on its own.
2. **Seed.** A longest-path pass gives a feasible starting ranking: every node sits exactly `MinLength` above its
   nearest successor.
3. **Feasible tree.** The ranker grows a spanning tree of tight edges, which are edges at exactly their minimum
   length. When no tight edge reaches a new node, it takes the edge with the least slack and shifts the tree's ranks
   to make that edge tight.
4. **Cut values.** Removing a tree edge splits the tree in two. That edge's cut value is the net weight of all graph
   edges that cross the split. Postorder low/lim numbers answer "is this node in that subtree?" in constant time.
5. **Exchange.** While a tree edge has a negative cut value, swapping it for a non-tree edge would shorten the
   drawing. The ranker swaps it for the minimum-slack edge that crosses the same cut, renumbers the tree, and
   recomputes ranks and cut values. It stops when no cut value is negative.

All traversals use explicit stacks rather than recursion, so long chains cannot overflow the call stack.

**Compound graphs: the nesting graph.** Plain network simplex knows nothing about subgraphs. Two sibling subgraphs can
then interleave their ranks, and a node ends up drawn inside the wrong box. When the graph has subgraphs,
`NestingGraphRanker` augments it before ranking, following dagre's nesting-graph technique:

- Each subgraph gets a top and a bottom border node, wired to its members with heavy weights. Every member must sit
  strictly between the two borders, and the weights keep the subgraph vertically compact.
- Every original edge's minimum length is scaled up, so border ranks fit between real ranks.
- A root node connects every top-level subgraph and loose node, so the augmented graph is connected.
- An edge from one top-level subgraph to another adds a border-to-border constraint, so the source subgraph ranks
  above the target. The constraint is skipped when the two subgraphs reference each other in both directions,
  because it would then contradict real edges.

After ranking, the border and root nodes are dropped and the ranks that real nodes occupy are compacted to `0..N-1`.

**After ranking.** `LayerAssigner` enforces `SameRankConstraints` and `MinLength`. It then splits every edge that
spans several layers into a chain of virtual nodes, one per layer, so every edge joins adjacent layers. Finally, a
depth-first pass from the sources sets the initial order inside each layer.

#### 3. Crossing minimisation

`CrossingMinimizer` reorders each layer to reduce edge crossings. It runs four stages:

1. **Barycentre sweeps.** A downward sweep sorts each layer by the average position of each node's neighbours in
   the layer above. An upward sweep does the same against the layer below. Each iteration runs one downward and one
   upward sweep, `CrossingIterations` times. The minimiser keeps the ordering with the fewest total crossings, not
   the last one. Ties can follow declaration order (`UseModelOrderForVirtualNodes`, `UseModelOrderForRealNodes`) or
   put real nodes before virtual ones (`UseRealFirstTiebreaker`).
2. **Pairwise swaps.** For every pair of real nodes in a layer, the minimiser tries a swap. It keeps the swap when
   neither adjoining layer pair gets worse and their total strictly drops. Pairs that are not adjacent matter: some
   improvements need a swap across nodes that must stay put.
3. **Insertion refinement.** Each node, virtual nodes included, is tried in every other slot of its layer. The best
   slot wins when it strictly lowers the crossings of the two adjoining layer pairs. This escapes local minima that
   swaps cannot.
4. **Restarts.** With `CrossingRestarts > 0`, the minimiser shuffles every layer with a fixed-seed random generator
   and runs the stages again. Odd restarts use plain barycentres without tie rules, because the tie rules pull every
   restart back to the same minimum. A restart replaces the result only with strictly fewer crossings, so the output
   is deterministic.

**Choosing among tied orders (port-aware mode).** Two orderings with the same crossing count can route very
differently. A jog across another edge's column is invisible to the count. With `PortAwareLayout` and restarts, the
minimiser keeps every ordering within three crossings of the best, up to 64 of them. Each candidate is placed and
routed. The deterministic first result wins unless another candidate has strictly fewer routed crossings.

#### 4. Coordinate assignment

**Default mode.** `CoordinateAssigner` gives each node a slot as wide as its subtree, so a parent sits centred over
its children. Each node is then pulled towards the median of its neighbours, and nodes stranded far from their layer
are compacted. Two more passes spread the children of a fan-out apart and push one branch of a fork aside, so the two
paths read as distinct.

**Balanced mode: Brandes–Köpf.** `BkCoordinateAssigner` implements U. Brandes and B. Köpf, "Fast and Simple Horizontal
Coordinate Assignment" (2001), extended with ports and label columns. The goal is to draw as many edges as possible
straight, without nodes overlapping.

1. **Ports.** The k edges on one side of a node get ports at `width · (i+1) / (k+1)`, ordered by the position of the
   node at the other end. The algorithm aligns ports, not node centres.
2. **Label columns.** In each gap between layers, every labelled edge gets a dummy node as wide as its label. The
   dummy is aligned like any long-edge virtual node, so each label gets a column of its own. Each gap is also as
   tall as its tallest label, plus room for the arrowheads. A node that fans many edges into one gap adds a lane per
   extra edge.
3. **Conflicts.** An inner segment joins two virtual nodes, meaning it is part of a long edge. A non-inner segment
   that crosses an inner segment is a type-1 conflict and is barred from alignment, so long edges stay straight.
4. **Vertical alignment.** Layer by layer, each node tries to join a block with the median neighbour in the previous
   layer. With an even number of neighbours, it tries both medians. An alignment is accepted only when it does not
   cross an alignment already made in this layer. A block is a vertical chain of nodes that will share one
   x-coordinate, adjusted by each member's port offset (its inner shift).
5. **Horizontal compaction.** Each block is placed as far towards one side as its left neighbours allow, with
   `NodeSpacing` between nodes. Blocks that touch form classes, each with a sink. Whole classes are then shifted
   against each other so they pack tightly.
6. **Four layouts.** Steps 4 and 5 run four times: aligning downwards or upwards, and compacting leftwards or
   rightwards. The rightward passes reuse the leftward code on a mirrored view of each layer.
7. **Balancing.** The four layouts are aligned to the narrowest one. Each node's final x is the average of its two
   median candidates out of the four. A sweep then restores the minimum spacing that the averaging can break.

Three post-passes polish the result. Each long-edge chain moves onto one x-coordinate where its neighbours leave
room. A source node is centred over the span of its children. A long edge's label column is kept in line with its
first virtual node.

#### 5. Edge routing

**Default mode: `EdgeRouter`.** Rectilinear polylines follow each edge's chain of virtual nodes. Fan-outs share
trunks. Back edges detour around the side, or, with `NaturalBackEdgeRouting`, run straight between the two nodes.
Opposing runs that would draw as one line are offset apart. Each label sits at the middle of the longest straight
segment, and overlapping labels are pushed apart.

**Balanced and port-aware mode: `ErEdgeRouter`.** Every edge runs exit port → short jog next to the source → label
column → short jog next to the target → entry port. A long edge runs straight through its virtual-node column in
between. Three passes clean up the routes:

- An edge between adjacent layers that crosses another edge tries putting its horizontal run at the top or bottom of
  the gap instead. The router keeps the variant only when that edge's crossings drop and its label stays clear.
- Horizontal runs of different edges that would merge into one line each get their own slot. The slots are ordered
  so the vertical runs of the cluster do not cross where that can be avoided.
- A sideways jog of a few pixels is removed by sliding the neighbouring vertical run.

#### 6. Direction and output

`DirectionTransform` rotates or mirrors the top-down result to `LR`, `RL` or `BT`, then shifts everything to the
canvas padding. In the default mode, subgraph boxes are fitted around their members at this point. Before routing,
the default mode also pushes apart layers and neighbours whose boxes would overlap, and edges that enter a subgraph
from above are rerouted around its sides, clear of the title.

### `HierarchicalLayout.Compute`

`HierarchicalLayout.Compute` builds compound layouts bottom-up, one level per subgraph. Subgraph boxes cannot overlap
each other or nodes they do not contain.

1. **Lay out each subgraph on its own.** Its box is the content plus `GroupPadding` on every side and
   `GroupHeaderHeight` at the top. The box is also widened so the title has a free column beside it, where edges can
   enter without crossing the title text.
2. **Treat each child subgraph as one node of its parent.** An edge between levels is mapped to the box that holds
   its end.
3. **Prepare each level's edges.**
   - A pair of unlabelled parallel edges lays out as one edge.
   - Items that reference each other follow the direction that carries more edges.
   - Inside a strongly connected component, a greedy feedback-arc-set order decides which edges point backwards, when
     it turns fewer edges backwards than declaration order.
   - A single edge that closes a cycle of three or more items leaves the level layout. The router draws it as a loop
     around the outside instead.
4. **Run `SugiyamaLayout.Compute` on each level** in balanced mode, with at least 16 crossing restarts. Its
   port → column → port routes become hints for edges between plain nodes of that level.
5. **Repair across levels.** Each level was ordered without seeing the edges that cross its border.
   - On the composed drawing, adjacent same-layer siblings swap when that lowers the crossings of straight
     centre-to-centre edges.
   - When a parent fans out to children that all sit in one top-level subgraph, that subgraph slides along the cross
     axis to centre the fan on the parent. It slides only when the moved box stays clear of every other box.
6. **Route every edge** on the final absolute boxes with `FlowEdgeRouter`.
7. **Fit the canvas** around nodes, subgraph boxes, route points and label pills, so nothing is clipped.

### The orthogonal router (`FlowEdgeRouter`)

`FlowEdgeRouter` routes every edge with horizontal and vertical segments, around node and subgraph boxes. It runs
after all placement is done, on absolute coordinates.

#### Sides and ports

- A forward edge leaves the side that faces the other end.
- A back edge goes straight up when the way is free. Otherwise it leaves and enters through the side facing the
  nearer outside edge, so it never shares ports with forward edges.
- A port sits where the edge can run straight: the middle of the stretch where the two boxes overlap. Ports are
  clamped to the usable part of the side and kept 14px apart.
- On diamonds and ellipses, ports slide onto the outline (`LayoutNode.Outline`). `PortOutline.Centre` attaches every
  edge at the middle of a side.
- When several arrows enter a tiny node, the outer ones come in from its sides.
- An edge declared against a subgraph (`SourceGroup`, `TargetGroup`) attaches to the subgraph's border, centred on
  the box.
- An edge from outside a subgraph keeps its port clear of the subgraph's title when the side is wide enough.
- Facing ports closer than two stubs apart share the gap rather than overshooting each other.

#### Route candidates, cheapest first

1. **The hint** from the level layout, when it is collision-free.
2. **A canonical route.** This is a straight line, or one Z with its jog in the middle of the gap. The router accepts
   it only when it avoids every box and neither overlaps nor runs within 7px of an existing route. A back edge's
   canonical route loops around the whole drawing on the outermost lane.
3. **An A\* search** on a grid. The grid lines come from the box borders and an 8px margin around them, the ports and
   their stub ends, the midlines between neighbouring lines, and up to ten extra lanes 8px apart in wide gaps. Grid
   points inside a node's margin are blocked, except at the target.
4. **A plain fallback shape**, when the search finds no path.

#### Search costs

The search state is a grid cell plus the direction of arrival, so bends can be charged. Each step adds these terms:

| Term | Cost |
|---|---|
| Length | 1 per px (0.6 for back-edge loops) |
| Bend | 45 |
| Crossing an earlier route | 140 (420 for back-edge loops) |
| Running within 10px of an earlier route, in parallel | 40 + length |
| Entering a subgraph that holds neither end | 450 + 2 × length |
| Running along the border of such a subgraph | 0.6 × length |
| Leaving a subgraph that holds both ends | 45 + length |
| Crossing a subgraph's title | 90 + length |

The near-parallel term treats a run a few pixels away like a run on top of another line, because the eye merges
them. Back-edge loops also count crossings that fall exactly on a grid line. Otherwise a loop could thread through
the drawing at grid points for free. The heuristic is the Manhattan distance to the target.

#### Rip-up and re-route

Edges without an accepted hint are routed greedily, shortest first. Two more passes then rip up each edge and route it
again against all the others, so an early edge cannot claim the best lane unfairly. Graphs with more than 60 routed
edges stop after the greedy pass.

#### Post-passes

- A short sideways jog between parallel ports becomes one straight line when either end can move its port.
- An unlabelled jog → column → jog route becomes one jog next to the source and a long straight run, when that stays
  clear.
- Z-bends between the same two layers share one bend coordinate, so fans and merges look uniform. Jogs whose runs
  overlap get adjacent lanes.
- Each label goes on its route, just before the final arrowhead stub, so sibling labels line up. Labels avoid subgraph
  borders and labels that are already placed.

## How it avoids work

Only what the code does:

- **Flat storage.** `GraphBuffer` keeps nodes, layers and positions in flat arrays indexed by dense integer IDs, not
  in an object graph. Virtual nodes are appended to the same arrays, which start at twice the node count so most
  graphs never grow them.
- **Pooled arrays.** The integer arrays come from `ArrayPool<int>.Shared` and go back on `Dispose`.
- **CSR adjacency.** In- and out-neighbours are stored in compressed sparse row form, as one offset array and one
  neighbour array. A barycentre sweep visits each node's neighbours in time proportional to its degree.
- **Skipped work.** Layers whose adjoining crossings are already zero skip the swap and insertion passes.
- **Guards against quadratic blow-up.**
  - A layer wider than 60 nodes falls back from all-pairs swaps to adjacent swaps and skips insertion refinement.
  - Insertion refinement stops after 8 rounds.
  - The cross-level swap pass in `HierarchicalLayout.Compute` runs only for 3 to 80 edges.
  - The router skips the rip-up passes for graphs with more than 60 routed edges.
  - One A\* search stops after 400,000 expansions.
- **A lazy routing grid.** Most edges take their hint or canonical route, so the router builds the grid only when an
  edge first needs a search.
- **Bucketed segment lookups.** Routed segments are indexed by grid row and column. A step's cost visits only the
  segments that could run parallel to it or cross it, merged back in routing order so the sum stays the same.
- **Reused searches.** A rip-up pass that asks the same question against exactly the same routed segments reuses
  the earlier answer.
- **Cached step costs.** A step's cost depends only on the cell, the direction and whether it bends. Within one
  search, it is computed once.
- **A specialised heap.** The priority queue is a 4-ary min-heap over integer states, with the same tie order as
  `PriorityQueue<TElement, TPriority>`. Stale queue entries are skipped without being expanded.
- **A reused workspace.** Search state lives in packed structs, in a workspace kept between renders. Each search
  stamps its entries with a version, so nothing is cleared between searches. The kept workspace is capped at 16,000
  cells, about 3 MB, and a concurrent render takes a fresh one.

## Usage

### Subgraphs and the orthogonal router

Pass subgraphs as `LayoutSubgraph` records, nested through `Children`. `HierarchicalLayout.Compute` requires an
options instance, so pass `LayoutOptions.Default` when the defaults suit you.

```csharp
var graph = new LayoutGraph(
    LayoutDirection.LR,
    Nodes:
    [
        new LayoutNode("web", 80, 40),
        new LayoutNode("api", 80, 40),
        new LayoutNode("db", 80, 40) { Outline = PortOutline.Ellipse },
    ],
    Edges:
    [
        new LayoutEdge("web", "api", LabelWidth: 48, LabelHeight: 16),
        new LayoutEdge("api", "db"),
    ],
    Subgraphs:
    [
        new LayoutSubgraph("frontend", "Frontend", ["web"], []),
        new LayoutSubgraph("backend", "Backend", ["api", "db"], []),
    ]
);

var result = HierarchicalLayout.Compute(graph, LayoutOptions.Default);

foreach (var group in result.Groups)
    Console.WriteLine($"{group.Id}: ({group.X},{group.Y}) {group.Width}x{group.Height}");
```

To end an edge on a subgraph's border rather than on a node inside it, set `TargetGroup` (or `SourceGroup`). The edge
still names a member node as its `Target`:

```csharp
new LayoutEdge("web", "api") { TargetGroup = "backend" }
```

Only `HierarchicalLayout.Compute` reads `SourceGroup`, `TargetGroup`, `LayoutNode.Outline`, `GroupPadding` and
`GroupHeaderHeight`. Only `SugiyamaLayout.Compute` honours `LayoutGraph.SameRankConstraints`.

### Edge labels

Give an edge its label size with `LabelWidth` and `LabelHeight`. `LayoutEdgeResult.LabelPosition` then holds the
label's centre.

```csharp
new LayoutEdge("A", "B", LabelWidth: 40, LabelHeight: 16)
```

In balanced mode, port-aware mode and `HierarchicalLayout.Compute`, every labelled edge gets its own column and the
layer gap grows to fit the label. The default mode places labels but reserves no space for them, so raise
`LayerSpacing` to make room.

### Direction

```csharp
new LayoutGraph(LayoutDirection.LR, nodes, edges, subgraphs);
```

All four directions are supported: `TD`, `LR`, `BT` and `RL`.

### Same-rank pairs and minimum lengths

```csharp
var graph = new LayoutGraph(LayoutDirection.TD, nodes, edges, [])
{
    SameRankConstraints = [("A", "B")], // A and B share a layer, A to the left
};

new LayoutEdge("A", "C", MinLength: 2); // C sits at least two layers below A
```

## Options

```csharp
var options = new LayoutOptions
{
    Padding            = 40,    // canvas padding in px
    NodeSpacing        = 36,    // gap between neighbours in a layer
    LayerSpacing       = 72,    // gap between layers
    CrossingIterations = 4,     // barycentre sweep passes
    CrossingRestarts   = 8,     // deterministic restarts; kept only with strictly fewer crossings
    SeparateComponents = true,  // lay out and tile disconnected components separately
    BalancedPlacement  = true,  // Brandes–Köpf placement with label columns
};

var result = SugiyamaLayout.Compute(graph, options);
```

| Option | Default | Effect |
|---|---|---|
| `Padding` | `40` | Canvas padding in px |
| `NodeSpacing` | `36` | Gap between neighbours in one layer |
| `LayerSpacing` | `72` | Gap between layers. Balanced mode grows gaps that carry labels |
| `CrossingIterations` | `4` | Barycentre sweep iterations |
| `CrossingRestarts` | `0` | Deterministic restarts of crossing minimisation. `HierarchicalLayout.Compute` uses at least 16 |
| `SeparateComponents` | `true` | Lay out disconnected components on their own and tile them |
| `ComponentSpacing` | `48` | Gap between tiled components |
| `MaxComponentsPerRow` | `0` | Components per row before wrapping. `0` means no limit |
| `BalancedPlacement` | `false` | Balanced mode: Brandes–Köpf placement, label columns and `ErEdgeRouter` |
| `PortAwareLayout` | `false` | Port-aware mode: label columns, `ErEdgeRouter`, and a choice among tied orders by routed crossings. Use with `TightSourceLayering` |
| `TightSourceLayering` | `false` | Selects Brandes–Köpf placement and skips the fork-spreading pass |
| `NaturalBackEdgeRouting` | `false` | A back edge across one layer gets no virtual nodes; in the default mode it runs straight between the two nodes |
| `StrictTopDownFanout` | `false` | Default mode only: fan-outs leave from the bottom instead of the side |
| `ForceBottomExitFanOut` | `false` | Default mode only: fan-outs always leave from the bottom centre |
| `UseModelOrderForVirtualNodes` | `false` | Crossing ties between real and virtual nodes follow declaration order |
| `UseModelOrderForRealNodes` | `false` | Crossing ties between real nodes follow declaration order |
| `UseRealFirstTiebreaker` | `false` | Crossing ties put real nodes before virtual nodes |
| `ReverseSourceOrder` | `false` | Seeds the initial layer order from the sources in reverse |
| `GroupPadding` | `24` | `HierarchicalLayout.Compute` only: space between a subgraph border and its content |
| `GroupHeaderHeight` | `32` | `HierarchicalLayout.Compute` only: space for a subgraph title |
| `MaxNodeCount` | `int.MaxValue` | Throws `InvalidOperationException` when virtual nodes push the node count past this |
| `CancellationToken` | none | Checked between phases and inside the crossing minimiser; throws `OperationCanceledException` |

## Output

Both entry points return a `LayoutResult`:

- **`Nodes`**: positioned rectangles with `(X, Y, Width, Height)` in absolute coordinates.
- **`Edges`**: polylines as `IReadOnlyList<LayoutPoint>`, with an optional `LabelPosition`. `OriginalIndex` is the
  edge's index in the input.
- **`Groups`**: subgraph boxes, nested through `Children`.
- **`Width`** and **`Height`**: the total canvas size, including padding.

The origin is the top left. Routes are orthogonal polylines with sharp corners; rounding them is up to the renderer.

## Performance

On a 6-node flowchart (Apple M2, .NET 10, BenchmarkDotNet medium run):

| | Time | Allocated |
|---|---:|---:|
| `SugiyamaLayout.Compute`, default mode | **6.0 &micro;s** | **29 KB** |
| Compound layout with orthogonal routing (as used by Mermaider) | 24 &micro;s | 82 KB |
| Microsoft MSAGL layered layout | 226 &micro;s | 549 KB |

The first row times `SugiyamaLayout.Compute` on pre-sized nodes and unlabelled edges. The last two rows lay out the
same parsed flowchart including node and label measurement: `HierarchicalLayout.Compute` with the orthogonal router is
about 9.6&times; faster than MSAGL and allocates 6.7&times; less.

## Credits and references

- **dagre** ([dagrejs/dagre](https://github.com/dagrejs/dagre), MIT). `NetworkSimplexRanker` and
  `NestingGraphRanker` are ports of dagre's network simplex and nesting-graph code. The cycle removal follows dagre's
  DFS order.
- E. R. Gansner, E. Koutsofios, S. C. North and K.-P. Vo, "A Technique for Drawing Directed Graphs", IEEE TSE, 1993.
  The network simplex ranking.
- U. Brandes and B. Köpf, "Fast and Simple Horizontal Coordinate Assignment", Graph Drawing 2001, LNCS 2265. The
  coordinate assignment.
- K. Sugiyama, S. Tagawa and M. Toda, "Methods for Visual Understanding of Hierarchical System Structures", IEEE
  SMC, 1981. The layered framework.

## Source files

The public API is in `SugiyamaLayout.cs`, `HierarchicalLayout.cs` and `LayoutModels.cs`. The phases live in
`Internal/`:

| File | Phase | What it does |
|---|---|---|
| `GraphBuffer.cs` | All | Flat array storage with CSR adjacency; integer arrays rented from `ArrayPool<int>` |
| `CycleRemover.cs` | Cycle removal | Two-pass DFS in dagre's order; reverses back edges |
| `LayerAssigner.cs` | Ranking | Calls the rankers, enforces same-rank pairs and minimum lengths, inserts virtual nodes, sets the initial order |
| `NetworkSimplexRanker.cs` | Ranking | Network simplex: feasible tree, cut values, edge exchange |
| `NestingGraphRanker.cs` | Ranking | Nesting-graph augmentation that keeps each subgraph in one band of ranks |
| `CrossingMinimizer.cs` | Ordering | Barycentre sweeps, pairwise swaps, insertion refinement, deterministic restarts |
| `CoordinateAssigner.cs` | Coordinates | Default placement by subtree width and median pull; dispatches to Brandes–Köpf |
| `BkCoordinateAssigner.cs` | Coordinates | Port-aware Brandes–Köpf with label columns |
| `EdgeRouter.cs` | Routing | Default-mode rectilinear router |
| `ErEdgeRouter.cs` | Routing | Port → column → port router for balanced and port-aware mode |
| `FlowEdgeRouter.cs` | Routing | Obstacle-aware orthogonal router for `HierarchicalLayout.Compute` |
| `DirectionTransform.cs` | Output | Rotates or mirrors the top-down result and normalises it to the padding |
