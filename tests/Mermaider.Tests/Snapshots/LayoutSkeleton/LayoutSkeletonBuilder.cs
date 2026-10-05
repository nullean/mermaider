using Mermaider.Models;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Turns raw entity boxes + edge polylines into a <see cref="LayoutSkeleton"/>. This is the
/// one piece of logic both <see cref="MermaiderSkeletonExtractor"/> and
/// <see cref="MermaidJsSkeletonExtractor"/> share — whatever renderer the boxes/edges came
/// from, the same clustering rules decide layers and order, so comparisons are apples-to-apples.
/// </summary>
internal static class LayoutSkeletonBuilder
{
	public static LayoutSkeleton Build(
		IReadOnlyList<(string Id, double X, double Y, double W, double H)> boxes,
		IReadOnlyList<(string From, string To, string Label, IReadOnlyList<Point> Points)> edges,
		IReadOnlyList<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)>? groups = null,
		Direction direction = Direction.TB)
	{
		var n = boxes.Count;
		var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var i = 0; i < n; i++)
			indexById[boxes[i].Id] = i;

		// A direct edge between two nodes forbids merging them into the same layer — a layered
		// graph engine (dagre or Sugiyama) never places an edge's two endpoints on the same
		// rank (minimum edge length is always >= 1 rank). Without this, an irregularly-shaped
		// node (a wide diamond, a tall hexagon) can spuriously box-overlap its very next,
		// directly-connected neighbor's band and get merged into one skeleton "layer" — found
		// via flowchart-shapes, where mermaid.js's own diamond (D) and circle (E) shapes
		// overlap enough in X to merge, even though D --> E is a direct edge in a plain linear
		// chain with zero real ordering ambiguity. That merge then manufactures a single
		// meaningless "sibling" pair whose near-tied secondary-axis position disagrees by
		// rounding noise, tanking WithinLayerOrderAgreement for a diagram that has no
		// real order question to agree or disagree on at all.
		var noMerge = new HashSet<(int, int)>();
		foreach (var e in edges)
		{
			if (indexById.TryGetValue(e.From, out var fi) && indexById.TryGetValue(e.To, out var ti) && fi != ti)
				noMerge.Add(fi < ti ? (fi, ti) : (ti, fi));
		}

		var (layerOf, orderOf) = AssignLayersAndOrder(boxes.Select(b => (b.X, b.Y, b.W, b.H)).ToList(), direction, noMerge);

		var nodes = new List<SkeletonNode>(n);
		for (var i = 0; i < n; i++)
		{
			var b = boxes[i];
			nodes.Add(new SkeletonNode(b.Id, layerOf[i], orderOf[i], b.X + (b.W / 2), b.Y + (b.H / 2), b.W, b.H));
		}

		var skeletonEdges = new List<SkeletonEdge>(edges.Count);
		foreach (var e in edges)
		{
			var isSelfLoop = string.Equals(e.From, e.To, StringComparison.Ordinal);
			var pts = e.Points;
			var fromSide = indexById.TryGetValue(e.From, out var fi2) && pts.Count >= 1
				? ClassifySide(pts[0], boxes[fi2])
				: EdgeSide.Bottom;
			var toSide = indexById.TryGetValue(e.To, out var ti2) && pts.Count >= 2
				? ClassifySide(pts[^1], boxes[ti2])
				: EdgeSide.Top;
			var routeSig = isSelfLoop ? "" : ComputeRouteSig(pts);
			skeletonEdges.Add(new SkeletonEdge(e.From, e.To, e.Label, fromSide, toSide, isSelfLoop, routeSig));
		}

		var skeletonGroups = BuildGroups(groups, direction);
		var ports = BuildPorts(skeletonEdges, edges.Select(e => e.Points).ToList());

		return new LayoutSkeleton(nodes, skeletonEdges, skeletonGroups, ports);
	}

	// Groups get their own independent layer/order assignment (same box-overlap clustering
	// algorithm as nodes, just run on group boxes) — a subgraph's Y-extent spans all of its
	// members plus padding, so comparing group boxes to each other (not mixed in with leaf
	// node boxes) is what makes "did the two engines place this whole cluster in the same
	// relative position as that other cluster" a well-defined question.
	private static List<SkeletonGroup> BuildGroups(
		IReadOnlyList<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)>? groups,
		Direction direction)
	{
		if (groups is null || groups.Count == 0)
			return [];

		var (layerOf, orderOf) = AssignLayersAndOrder(groups.Select(g => (g.X, g.Y, g.W, g.H)).ToList(), direction, []);

		var result = new List<SkeletonGroup>(groups.Count);
		for (var i = 0; i < groups.Count; i++)
		{
			var g = groups[i];
			result.Add(new SkeletonGroup(
				g.Id, g.ParentId, g.MemberNodeIds, layerOf[i], orderOf[i],
				g.X + (g.W / 2), g.Y + (g.H / 2), g.W, g.H));
		}
		return result;
	}

	// Compute port attachment order: for each (node, side) with ≥2 edges, order edges by
	// their attachment coordinate (X for Top/Bottom, Y for Left/Right). Edge indices reference
	// the stable-sorted position of each edge in the serialized edge list.
	private static List<SkeletonPort> BuildPorts(
		IReadOnlyList<SkeletonEdge> skeletonEdges,
		IReadOnlyList<IReadOnlyList<Point>> polylines)
	{
		// Build stable-sorted edge index map: maps original edge index → sorted position
		// matching Serialize() order (From, To, Label).
		var sortedEdgeMap = skeletonEdges
			.Select((e, i) => (e, i))
			.OrderBy(x => x.e.From, StringComparer.Ordinal)
			.ThenBy(x => x.e.To, StringComparer.Ordinal)
			.ThenBy(x => x.e.Label, StringComparer.Ordinal)
			.Select((x, sortedIdx) => (OrigIdx: x.i, SortedIdx: sortedIdx))
			.ToDictionary(x => x.OrigIdx, x => x.SortedIdx);

		// Bucket: (nodeId, side) → list of (sortedEdgeIndex, attachmentCoord)
		var buckets = new Dictionary<(string NodeId, EdgeSide Side), List<(int SortedIdx, double AttachCoord)>>();

		void AddToBucket(string nodeId, EdgeSide side, int sortedIdx, Point attachPoint)
		{
			var key = (nodeId, side);
			if (!buckets.TryGetValue(key, out var list))
				buckets[key] = list = [];
			var coord = side is EdgeSide.Left or EdgeSide.Right ? attachPoint.Y : attachPoint.X;
			list.Add((sortedIdx, coord));
		}

		for (var origIdx = 0; origIdx < skeletonEdges.Count; origIdx++)
		{
			var e = skeletonEdges[origIdx];
			if (e.IsSelfLoop) continue;
			var pts = polylines[origIdx];
			if (pts.Count < 2) continue;
			var sortedIdx = sortedEdgeMap[origIdx];
			AddToBucket(e.From, e.FromSide, sortedIdx, pts[0]);
			AddToBucket(e.To, e.ToSide, sortedIdx, pts[^1]);
		}

		var result = new List<SkeletonPort>();
		foreach (var kv in buckets
			.OrderBy(kv => kv.Key.NodeId, StringComparer.Ordinal)
			.ThenBy(kv => kv.Key.Side.ToString(), StringComparer.Ordinal))
		{
			if (kv.Value.Count < 2) continue; // single edge: no order to compare
			var sortedEdgeIndices = kv.Value.OrderBy(x => x.AttachCoord).Select(x => x.SortedIdx).ToList();
			result.Add(new SkeletonPort(kv.Key.NodeId, kv.Key.Side, sortedEdgeIndices));
		}
		return result;
	}

	// Computes a run-length direction sequence from a polyline. Each segment is classified as
	// D(own), U(p), R(ight), or L(eft) based on its dominant axis. Consecutive equal directions
	// are merged. Returns "" for degenerate inputs (< 2 points, or all near-zero segments).
	private static string ComputeRouteSig(IReadOnlyList<Point> points)
	{
		if (points.Count < 2)
			return "";

		const double Epsilon = 0.5;
		var dirs = new List<char>(points.Count - 1);
		for (var i = 0; i < points.Count - 1; i++)
		{
			var dx = points[i + 1].X - points[i].X;
			var dy = points[i + 1].Y - points[i].Y;
			if (Math.Abs(dy) >= Math.Abs(dx))
			{
				if (dy > Epsilon)       dirs.Add('D');
				else if (dy < -Epsilon) dirs.Add('U');
				// else: near-zero — skip degenerate segment
			}
			else
			{
				if (dx > Epsilon)       dirs.Add('R');
				else if (dx < -Epsilon) dirs.Add('L');
			}
		}

		if (dirs.Count == 0) return "";

		// Run-length encode: merge consecutive equal directions, join with spaces.
		var sb = new System.Text.StringBuilder(dirs.Count * 2);
		var cur = dirs[0];
		for (var i = 1; i < dirs.Count; i++)
		{
			if (dirs[i] == cur) continue;
			if (sb.Length > 0) sb.Append(' ');
			sb.Append(cur);
			cur = dirs[i];
		}
		if (sb.Length > 0) sb.Append(' ');
		sb.Append(cur);
		return sb.ToString();
	}

	// Union-find clustering: two entities belong to the same layer whenever their extents along
	// the *rank axis* overlap at all. This is deliberately size-agnostic along that axis — an
	// ER entity's height depends on its attribute count, so a fixed epsilon would misjudge a
	// short entity sitting beside a tall one in the same visual row. Layered graph engines
	// (dagre for mermaid.js, Sugiyama for Mermaider) both place same-rank nodes so their extents
	// along the rank axis coincide; different ranks are separated by real layer spacing, so a
	// simple "do the bands overlap" test cleanly tells layers apart.
	//
	// Which axis is the rank axis depends on the diagram's direction: TB/TD/BT diagrams rank
	// top-to-bottom (layer = Y-overlap, order = X-position, the original ER-only assumption),
	// but LR/RL diagrams rank left-to-right (layer = X-overlap, order = Y-position) — getting
	// this wrong doesn't just misorder siblings, it silently compares two structurally
	// unrelated axes and produces a garbage "layer agreement" score that looks like a real
	// layout defect. Found via db-flow-01-system (`flowchart LR`): every node landed in one of
	// only two Y-bands because Y-overlap is nearly meaningless for a left-to-right flow where
	// rank is expressed in X.
	private static (int[] LayerOf, int[] OrderOf) AssignLayersAndOrder(
		IReadOnlyList<(double X, double Y, double W, double H)> boxes, Direction direction, HashSet<(int, int)> noMerge)
	{
		var horizontal = direction is Direction.LR or Direction.RL;

		var n = boxes.Count;
		var layerOf = new int[n];
		var orderOf = new int[n];
		var parent = new int[n];
		for (var i = 0; i < n; i++)
			parent[i] = i;

		int Find(int i)
		{
			while (parent[i] != i)
			{
				parent[i] = parent[parent[i]];
				i = parent[i];
			}
			return i;
		}

		void Union(int a, int b)
		{
			a = Find(a);
			b = Find(b);
			if (a != b)
				parent[a] = b;
		}

		double RankStart((double X, double Y, double W, double H) b) => horizontal ? b.X : b.Y;
		double RankExtent((double X, double Y, double W, double H) b) => horizontal ? b.W : b.H;
		double OrderCenter((double X, double Y, double W, double H) b) => horizontal ? b.Y + (b.H / 2) : b.X + (b.W / 2);

		for (var i = 0; i < n; i++)
		{
			for (var j = i + 1; j < n; j++)
			{
				var start1 = RankStart(boxes[i]);
				var end1 = start1 + RankExtent(boxes[i]);
				var start2 = RankStart(boxes[j]);
				var end2 = start2 + RankExtent(boxes[j]);
				var overlap = Math.Min(end1, end2) - Math.Max(start1, start2);
				// Same-rank siblings placed by a real layered-graph engine share (near enough)
				// the same rank-axis band, so their extents overlap substantially regardless of
				// size mismatch (a short entity centered in a tall row's band is still mostly
				// contained in it). Adjacent *different* ranks can still show a sliver of overlap
				// when the engine reserves a thin, mostly-virtual-node rank for an edge label and
				// the inter-rank gap ends up smaller than a real node's own extent (confirmed via
				// flowchart-edges/long-edges: dagre's label-carrying rank sits close enough to the
				// next real rank that two real nodes on adjacent ranks touch by a few px). Require
				// the overlap to cover at least half of the smaller box's extent so a few-pixel
				// graze doesn't merge two genuinely different ranks into one skeleton layer.
				var minExtent = Math.Min(RankExtent(boxes[i]), RankExtent(boxes[j]));
				if (overlap > 0.5 * minExtent && !noMerge.Contains((i, j)))
					Union(i, j);
			}
		}

		var clusters = Enumerable.Range(0, n)
			.GroupBy(Find)
			.Select(g => (Indices: g.ToList(), MeanRank: g.Average(i => RankStart(boxes[i]) + (RankExtent(boxes[i]) / 2))))
			.OrderBy(c => c.MeanRank)
			.ToList();

		for (var layer = 0; layer < clusters.Count; layer++)
		{
			var ordered = clusters[layer].Indices.OrderBy(i => OrderCenter(boxes[i])).ToList();
			for (var pos = 0; pos < ordered.Count; pos++)
			{
				layerOf[ordered[pos]] = layer;
				orderOf[ordered[pos]] = pos;
			}
		}

		return (layerOf, orderOf);
	}

	// Classifies which side of `box` the point `p` is closest to. Used for both ends of an
	// edge independently — a self-loop's exit and re-entry point can land on different sides
	// of the same box.
	private static EdgeSide ClassifySide(Point p, (string Id, double X, double Y, double W, double H) box)
	{
		var distTop = Math.Abs(p.Y - box.Y);
		var distBottom = Math.Abs(p.Y - (box.Y + box.H));
		var distLeft = Math.Abs(p.X - box.X);
		var distRight = Math.Abs(p.X - (box.X + box.W));
		var min = Math.Min(Math.Min(distTop, distBottom), Math.Min(distLeft, distRight));

		if (min == distTop)
			return EdgeSide.Top;
		if (min == distBottom)
			return EdgeSide.Bottom;
		return min == distLeft ? EdgeSide.Left : EdgeSide.Right;
	}
}
