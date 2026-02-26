# 03 — Layout Engine Strategy

## What the Layout Engine Does

The layout engine takes a parsed `MermaidGraph` (nodes, edges, subgraphs) and computes absolute x/y positions for everything. This is the most complex and critical component — it determines how the diagram looks.

beautiful-mermaid uses **ELK.js** (Eclipse Layout Kernel) with these specific settings:

- **Algorithm:** `layered` (Sugiyama-style)
- **Edge routing:** `ORTHOGONAL` (right-angle bends only)
- **Hierarchy handling:** `SEPARATE` (when subgraphs have direction overrides) or `INCLUDE_CHILDREN`
- **Node placement:** `BALANCED` (centered within layers)
- **Content alignment:** `H_CENTER V_CENTER`
- **Crossing minimization:** configurable `thoroughness` (1-7)

## Layout Engine Abstraction

```csharp
namespace Mermaid.Layout;

internal interface ILayoutEngine
{
    PositionedGraph Layout(MermaidGraph graph, LayoutOptions options);
}

internal sealed record LayoutOptions(
    double Padding = 40,
    double NodeSpacing = 28,
    double LayerSpacing = 48,
    int Thoroughness = 3
);
```

This abstraction lets us swap backends (MSAGL, Jint+ELK, future custom) without changing the rest of the pipeline.

## Option A: Microsoft.Msagl (Recommended Primary)

### What is MSAGL?

Microsoft Automatic Graph Layout — a .NET library originally from Microsoft Research. It supports:

- Layered (Sugiyama) layout ✓
- Orthogonal edge routing ✓
- Compound graphs (subgraphs) ✓ (via "clusters")
- Multiple layout directions ✓

### NuGet packages

```xml
<PackageReference Include="Microsoft.Msagl" />
<PackageReference Include="Microsoft.Msagl.Drawing" />
```

### Integration Plan

```csharp
internal sealed class MsaglLayoutEngine : ILayoutEngine
{
    public PositionedGraph Layout(MermaidGraph graph, LayoutOptions options)
    {
        // 1. Convert MermaidGraph → MSAGL GeometryGraph
        var geoGraph = ConvertToMsagl(graph, options);

        // 2. Configure layout settings
        var settings = new SugiyamaLayoutSettings
        {
            Transformation = DirectionToTransform(graph.Direction),
            NodeSeparation = options.NodeSpacing,
            LayerSeparation = options.LayerSpacing,
            EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.Rectilinear }
        };

        // 3. Run layout
        var layout = new LayeredLayout(geoGraph, settings);
        layout.Run();

        // 4. Convert back to PositionedGraph
        return ConvertFromMsagl(geoGraph, graph);
    }
}
```

### MSAGL Direction Mapping

| Mermaid | MSAGL `PlaneTransformation` |
|---|---|
| `TD` / `TB` | Identity (top-to-bottom is default) |
| `LR` | 90° rotation |
| `RL` | 270° rotation |
| `BT` | 180° rotation |

### MSAGL Compound Graphs (Subgraphs)

MSAGL supports "clusters" which are compound nodes containing child nodes. This maps directly to Mermaid subgraphs:

```csharp
var cluster = new Cluster(subgraph.Id);
foreach (var nodeId in subgraph.NodeIds)
    cluster.AddChild(nodeMap[nodeId]);
```

### Key Differences from ELK to Watch

| Feature | ELK | MSAGL | Mitigation |
|---|---|---|---|
| Edge bend style | Clean orthogonal with minimal bends | May produce more bends | Post-process to simplify |
| Layer alignment | Nodes can stagger (we fix in post) | Generally aligned | May not need alignment pass |
| Edge bundling | Not built-in (we add post) | Not built-in | Same post-processing code |
| Subgraph direction override | `SEPARATE` hierarchy mode | Per-cluster settings | Need to verify cluster layout supports this |
| Port-based edges | Hierarchical ports for cross-hierarchy | Cluster boundary routing | MSAGL handles this differently — test carefully |

## Option B: Jint + ELK.js (Fallback)

If MSAGL output quality is insufficient, we can run the actual ELK.js inside .NET using [Jint](https://github.com/nicollassilva/jint) (a JavaScript interpreter for .NET).

```csharp
internal sealed class ElkLayoutEngine : ILayoutEngine
{
    private readonly Engine _jint;

    public ElkLayoutEngine()
    {
        _jint = new Engine();
        // Load elk.bundled.js once
        var elkJs = EmbeddedResource.Read("elk.bundled.js");
        _jint.Execute(elkJs);
    }

    public PositionedGraph Layout(MermaidGraph graph, LayoutOptions options)
    {
        // 1. Convert MermaidGraph → ELK JSON (same format as TS version)
        var elkJson = ConvertToElkJson(graph, options);

        // 2. Run ELK layout via Jint
        _jint.SetValue("inputGraph", elkJson);
        var result = _jint.Evaluate("new ELK().layout(JSON.parse(inputGraph))");

        // 3. Parse result back to PositionedGraph
        return ConvertFromElkJson(result, graph);
    }
}
```

**Trade-offs:**
- Pro: Pixel-perfect match with beautiful-mermaid output
- Pro: No algorithm porting risk
- Con: JS interpreter overhead (~5-10x slower than native)
- Con: 1.6 MB embedded JS resource
- Con: Jint's `setTimeout` handling may need the same patches as beautiful-mermaid does

## Post-Layout Processing

Regardless of layout backend, these post-processing steps are needed. They are backend-agnostic and operate on `PositionedGraph`:

### 1. Layer Alignment (`LayerAlignment.cs`)

Snap nodes in the same logical layer to the same flow-axis position. beautiful-mermaid does this because ELK staggers nodes for edge routing channels.

- Group nodes by proximity on the flow axis (threshold: 60% of layer spacing)
- Exclude directly-connected nodes from same-layer grouping
- Snap to center of range
- Adjust edge endpoints proportionally

~130 lines in TS → ~100 lines in C#.

### 2. Edge Bundling (`EdgeBundling.cs`)

Merge fan-out / fan-in edge paths into shared trunks:

- Group edges by shared source (fan-out) or shared target (fan-in)
- Only bundle edges with same style, no labels, forward direction
- Compute junction point at midpoint between source and nearest target
- Adjust junction to avoid crossing subgraph boundaries
- Rebuild edge paths: exit → trunk → branch → enter

~240 lines in TS → ~200 lines in C#.

### 3. Shape Clipping (`ShapeClipping.cs`)

ELK/MSAGL treat all nodes as rectangles. For non-rectangular shapes, clip edge endpoints to actual shape boundaries:

- **Diamond**: Ray-polygon intersection with 4 edges
- **Circle** / **Hexagon** / **Cylinder**: Future — ray-shape intersection
- Skip for rectangle, rounded, stadium (already correct)

~180 lines in TS → ~150 lines in C#.

### 4. Edge Orthogonalization

Ensure all edge segments are purely horizontal or vertical. May be needed if MSAGL produces diagonal segments for cross-hierarchy edges.

~50 lines in TS → same in C#.

## Node Sizing

Before layout, each node needs width/height estimates based on its label text and shape:

```csharp
internal static (double Width, double Height) EstimateNodeSize(
    string label, NodeShape shape)
{
    var metrics = TextMetrics.MeasureMultiline(label, FontSizes.NodeLabel, FontWeights.NodeLabel);
    var width = metrics.Width + NodePadding.Horizontal * 2;
    var height = metrics.Height + NodePadding.Vertical * 2;

    return shape switch
    {
        NodeShape.Diamond => { var side = Math.Max(width, height) + 24; (side, side) },
        NodeShape.Circle or NodeShape.DoubleCircle => ...,
        NodeShape.Hexagon => (width + 20, height),
        NodeShape.StateStart or NodeShape.StateEnd => (28, 28),
        _ => (Math.Max(width, 60), Math.Max(height, 36))
    };
}
```

## Files to Create

| File | Lines (est.) | Complexity |
|---|---|---|
| `Layout/ILayoutEngine.cs` | ~15 | Low |
| `Layout/MsaglLayoutEngine.cs` | ~250 | High — MSAGL API integration |
| `Layout/LayoutConverter.cs` | ~150 | Medium — graph model conversion |
| `Layout/LayerAlignment.cs` | ~100 | Medium |
| `Layout/EdgeBundling.cs` | ~200 | Medium-High |
| `Layout/ShapeClipping.cs` | ~150 | Medium |
| `Layout/NodeSizing.cs` | ~60 | Low |
