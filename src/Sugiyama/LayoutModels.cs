namespace Sugiyama;

/// <summary>A 2D point. Value type to avoid heap allocations.</summary>
public readonly record struct LayoutPoint(double X, double Y);

// ====================================================================
// Input models — what the caller provides
// ====================================================================

/// <summary>Direction for graph layout.</summary>
public enum LayoutDirection { TD, LR, BT, RL }

/// <summary>A graph to be laid out.</summary>
public sealed record LayoutGraph(
	LayoutDirection Direction,
	IReadOnlyList<LayoutNode> Nodes,
	IReadOnlyList<LayoutEdge> Edges,
	IReadOnlyList<LayoutSubgraph> Subgraphs);

/// <summary>A node with a pre-computed bounding box size.</summary>
public sealed record LayoutNode(string Id, double Width, double Height);

/// <summary>A directed edge between two nodes.</summary>
public sealed record LayoutEdge(string Source, string Target, double LabelWidth = 0, double LabelHeight = 0);

/// <summary>A subgraph grouping a set of node IDs with optional children.</summary>
public sealed record LayoutSubgraph(
	string Id,
	string Label,
	IReadOnlyList<string> NodeIds,
	IReadOnlyList<LayoutSubgraph> Children);

// ====================================================================
// Options
// ====================================================================

/// <summary>Layout algorithm configuration.</summary>
public sealed record LayoutOptions
{
	public static readonly LayoutOptions Default = new();

	/// <summary>Canvas padding in px. Default: 40.</summary>
	public double Padding { get; init; } = 40;

	/// <summary>Horizontal spacing between sibling nodes. Default: 36.</summary>
	public double NodeSpacing { get; init; } = 36;

	/// <summary>Vertical spacing between layers. Default: 72.</summary>
	public double LayerSpacing { get; init; } = 72;

	/// <summary>Number of barycenter sweep iterations for crossing minimization. Default: 4.</summary>
	public int CrossingIterations { get; init; } = 4;

	/// <summary>Spacing between disconnected graph components when <see cref="SeparateComponents"/> is true. Default: 48.</summary>
	public double ComponentSpacing { get; init; } = 48;

	/// <summary>
	/// When true (default), disconnected graph components are laid out independently and tiled.
	/// When false, all nodes share a unified layout grid.
	/// </summary>
	public bool SeparateComponents { get; init; } = true;
}

// ====================================================================
// Output models — what the layout produces
// ====================================================================

/// <summary>The complete layout result with absolute coordinates.</summary>
public sealed record LayoutResult(
	double Width,
	double Height,
	IReadOnlyList<LayoutNodeResult> Nodes,
	IReadOnlyList<LayoutEdgeResult> Edges,
	IReadOnlyList<LayoutGroupResult> Groups);

/// <summary>A positioned node.</summary>
public sealed record LayoutNodeResult(string Id, double X, double Y, double Width, double Height);

/// <summary>A positioned edge with a polyline path.</summary>
public sealed record LayoutEdgeResult(
	int OriginalIndex,
	IReadOnlyList<LayoutPoint> Points,
	LayoutPoint? LabelPosition);

/// <summary>A positioned subgraph group rectangle.</summary>
public sealed record LayoutGroupResult(
	string Id,
	string Label,
	double X, double Y,
	double Width, double Height,
	IReadOnlyList<LayoutGroupResult> Children);
