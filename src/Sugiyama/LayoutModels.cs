using System.Threading;

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
	IReadOnlyList<LayoutSubgraph> Subgraphs)
{
	/// <summary>
	/// Pairs of node IDs that must be placed on the same layer (rank).
	/// The first node in each pair is placed left of the second.
	/// Used by invisible edges (<c>~~~</c>).
	/// </summary>
	public IReadOnlyList<(string A, string B)> SameRankConstraints { get; init; } = [];
}

/// <summary>A node with a pre-computed bounding box size.</summary>
public sealed record LayoutNode(string Id, double Width, double Height);

/// <summary>A directed edge between two nodes.</summary>
public sealed record LayoutEdge(string Source, string Target, double LabelWidth = 0, double LabelHeight = 0, int MinLength = 1);

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
	/// When true (default), disconnected graph components are laid out independently (each its
	/// own local rank-0..N range) and then tiled into a grid by <c>ArrangeComponents</c>. When
	/// false, every node — across every weakly-connected component — goes through one shared
	/// <c>LayerAssigner</c>/network-simplex pass, matching dagre's actual behavior: dagre's
	/// <c>lib/nesting-graph.ts</c> unconditionally connects every top-level node to an implicit
	/// dummy root before ranking (not just for explicit subgraphs/clusters), so independent
	/// pieces land in the same layer range as the main component instead of being stacked
	/// underneath it or isolated in their own grid cell. ER (<c>LightweightErLayoutEngine</c>)
	/// sets this to <c>false</c> for exactly this reason — see
	/// <c>SkeletonComparisonTests</c>'s class remarks for the before/after IR-agreement numbers.
	/// Flowchart/class/state keep the default (<c>true</c>): their disconnected-component shapes
	/// differ enough (and are far less common) that switching needs its own calibration pass
	/// before being changed — see the <c>er-fix</c>/<c>final-verify-2</c> plan.
	/// </summary>
	public bool SeparateComponents { get; init; } = true;

	/// <summary>
	/// Token checked at phase boundaries within the layout engine.
	/// When cancelled, <see cref="OperationCanceledException"/> is thrown.
	/// Default: none.
	/// </summary>
	public CancellationToken CancellationToken { get; init; }

	/// <summary>
	/// Maximum node count after virtual-node insertion (Sugiyama amplification guard).
	/// If exceeded, <see cref="InvalidOperationException"/> is thrown.
	/// Default: int.MaxValue (no limit).
	/// </summary>
	public int MaxNodeCount { get; init; } = int.MaxValue;

	/// <summary>
	/// When true, fan-out nodes use bottom exit instead of side exit for direct
	/// left-facing connections. Prevents routing conflicts in ER diagrams where
	/// ancestor paths route through the same left-side corridor. Default: false.
	/// </summary>
	public bool StrictTopDownFanout { get; init; }

	/// <summary>
	/// When true, single-layer reversed back-edges (e.g. class inheritance) route
	/// straight up from child-top to parent-bottom instead of detouring around the
	/// right side. Default: false.
	/// </summary>
	public bool NaturalBackEdgeRouting { get; init; }

	/// <summary>
	/// When true, fan-out nodes always exit from the bottom center regardless of
	/// target direction (left or right). Produces the classic inheritance-tree look
	/// for class diagrams. Default: false.
	/// </summary>
	public bool ForceBottomExitFanOut { get; init; }

	/// <summary>
	/// Maximum number of components to tile in the primary direction before wrapping
	/// to a new row (for TD/BT) or column (for LR/RL). 0 means unlimited (default).
	/// Useful for ER diagrams with many disconnected entity pairs.
	/// </summary>
	public int MaxComponentsPerRow { get; init; }

	/// <summary>
	/// When true, each source node (no incoming edges) is pushed independently to sit
	/// directly above its nearest child, regardless of whether sibling sources exist.
	/// Keeps ER entities close to what they connect to. Default: false.
	/// </summary>
	public bool TightSourceLayering { get; init; }

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
