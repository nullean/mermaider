namespace Sugiyama.Internal;

/// <summary>
/// Phase 5: Generate rectilinear polyline paths for each edge.
/// For edges that span multiple layers (via virtual nodes), follow the
/// virtual node chain.
/// When <c>useSideRouting</c> is enabled (LR/RL layouts), edges to targets
/// significantly above or below the source exit from the source's left/right
/// side (which becomes top/bottom after direction transform).
/// </summary>
internal static class EdgeRouter
{
	internal sealed class RoutedEdge(
		int originalIndex,
		bool reversed,
		List<LayoutPoint> points,
		LayoutPoint? labelPosition)
	{
		internal int OriginalIndex { get; } = originalIndex;
		internal bool Reversed { get; } = reversed;
		internal List<LayoutPoint> Points { get; private set; } = points;
		internal LayoutPoint? LabelPosition { get; private set; } = labelPosition;

		internal void SetLabelPosition(LayoutPoint lp) => LabelPosition = lp;

		internal void ReplacePoints(List<LayoutPoint> newPoints)
		{
			Points = newPoints;
			LabelPosition = null;
		}
	}

	private const double MinGapFromNode = 22;

	internal static List<RoutedEdge> Run(
		GraphBuffer graph, bool useSideRouting = false,
		IReadOnlyList<LayoutEdge>? inputEdges = null,
		bool strictTopDownFanout = false,
		bool naturalBackEdgeRouting = false,
		bool forceBottomExitFanOut = false)
	{
		var edgeChains = BuildEdgeChains(graph);
		var results = new List<RoutedEdge>(edgeChains.Count);

		foreach (var (origIdx, reversed, chain) in edgeChains)
		{
			var isDirectBackEdge = reversed && chain[0] < graph.RealNodeCount && chain[^1] < graph.RealNodeCount;
			var points = isDirectBackEdge && naturalBackEdgeRouting
				? RouteNaturalBackEdge(graph, chain[0], chain[^1])
				: isDirectBackEdge
					? RouteBackEdge(graph, chain[0], chain[^1])
					: RouteChain(graph, chain, useSideRouting, strictTopDownFanout, forceBottomExitFanOut);

			var src = chain[0];
			var tgt = chain[^1];
			var srcBottom = src < graph.RealNodeCount ? graph.Y[src] + graph.NodeHeights[src] : graph.Y[src];
			var tgtTop = tgt < graph.RealNodeCount ? graph.Y[tgt] : graph.Y[tgt];
			var labelPos = ComputeLabelPosition(points, srcBottom, tgtTop);

			if (reversed)
				points.Reverse();

			results.Add(new RoutedEdge(origIdx, reversed, points, labelPos));
		}

		SnapNearAlignedDoglegs(results);
		SnapSharedHorizontalTrunks(results);
		OffsetCrossingTrunks(results);

		if (inputEdges is not null)
			ResolveOverlappingLabels(results, inputEdges, useSideRouting);

		return results;
	}

	private const double SnapEpsilon = 8;

	private static void SnapNearAlignedDoglegs(List<RoutedEdge> edges)
	{
		foreach (var edge in edges)
		{
			var points = edge.Points;
			if (points.Count != 4)
				continue;

			var (start, firstBend, secondBend, end) = (points[0], points[1], points[2], points[3]);
			if (!SameX(start, firstBend) || !SameY(firstBend, secondBend) || !SameX(secondBend, end))
				continue;

			if (Math.Abs(start.X - end.X) > SnapEpsilon)
				continue;

			var snappedX = (start.X + end.X) / 2.0;
			edge.ReplacePoints([new LayoutPoint(snappedX, start.Y), new LayoutPoint(snappedX, end.Y)]);
		}
	}

	private static void SnapSharedHorizontalTrunks(List<RoutedEdge> edges)
	{
		var groups = edges
			.Where(e => e.Points.Count == 3)
			.Where(e => SameX(e.Points[0], e.Points[1]) && SameY(e.Points[1], e.Points[2]))
			.GroupBy(e => (Y: Quantize(e.Points[1].Y), TargetX: Quantize(e.Points[2].X), TargetY: Quantize(e.Points[2].Y)));

		foreach (var group in groups)
		{
			var candidates = group.ToList();
			if (candidates.Count < 2)
				continue;

			var minX = candidates.Min(e => e.Points[0].X);
			var maxX = candidates.Max(e => e.Points[0].X);
			if (maxX - minX > SnapEpsilon)
				continue;

			var snappedX = candidates.Average(e => e.Points[0].X);
			foreach (var edge in candidates)
			{
				var points = edge.Points;
				points[0] = new LayoutPoint(snappedX, points[0].Y);
				points[1] = new LayoutPoint(snappedX, points[1].Y);
			}
		}
	}

	/// <summary>
	/// When two edges share the same horizontal bus segment but travel in opposite horizontal
	/// directions (e.g., E→H going right and F→G going left across the same Y level), they
	/// render as one indistinguishable line. Offset each pair by ±CrossingOffset so both
	/// crossings are visible as distinct paths.
	/// </summary>
	private static void OffsetCrossingTrunks(List<RoutedEdge> edges)
	{
		const double crossingOffset = 15.0;

		// Collect edges that have a Z/S bend: [src, hBend1, hBend2, tgt]
		// where src.X == hBend1.X (vertical start), hBend1.Y == hBend2.Y (horizontal bus),
		// hBend2.X == tgt.X (vertical end).
		var bent = new List<(int Idx, double BusY, double XA, double XB)>();
		for (var i = 0; i < edges.Count; i++)
		{
			var pts = edges[i].Points;
			if (pts.Count != 4)
				continue;
			if (!SameX(pts[0], pts[1]) || !SameY(pts[1], pts[2]) || !SameX(pts[2], pts[3]))
				continue;
			if (Math.Abs(pts[1].X - pts[2].X) < 1)
				continue;
			bent.Add((i, pts[1].Y, pts[1].X, pts[2].X));
		}

		// Find counter-pairs: same busY, opposite direction, overlapping X span.
		var paired = new HashSet<int>();
		for (var i = 0; i < bent.Count; i++)
		{
			if (paired.Contains(bent[i].Idx))
				continue;
			for (var j = i + 1; j < bent.Count; j++)
			{
				if (paired.Contains(bent[j].Idx))
					continue;
				var a = bent[i];
				var b = bent[j];
				if (!SameY(new LayoutPoint(0, a.BusY), new LayoutPoint(0, b.BusY)))
					continue;
				var aGoesRight = a.XB > a.XA;
				var bGoesRight = b.XB > b.XA;
				if (aGoesRight == bGoesRight)
					continue;
				var overlapLeft = Math.Max(Math.Min(a.XA, a.XB), Math.Min(b.XA, b.XB));
				var overlapRight = Math.Min(Math.Max(a.XA, a.XB), Math.Max(b.XA, b.XB));
				if (overlapRight - overlapLeft < 1)
					continue;

				var ptsA = edges[a.Idx].Points;
				ptsA[1] = new LayoutPoint(ptsA[1].X, ptsA[1].Y - crossingOffset);
				ptsA[2] = new LayoutPoint(ptsA[2].X, ptsA[2].Y - crossingOffset);

				var ptsB = edges[b.Idx].Points;
				ptsB[1] = new LayoutPoint(ptsB[1].X, ptsB[1].Y + crossingOffset);
				ptsB[2] = new LayoutPoint(ptsB[2].X, ptsB[2].Y + crossingOffset);

				_ = paired.Add(a.Idx);
				_ = paired.Add(b.Idx);
				break;
			}
		}
	}

	private static bool SameX(LayoutPoint a, LayoutPoint b) => Math.Abs(a.X - b.X) < 0.5;
	private static bool SameY(LayoutPoint a, LayoutPoint b) => Math.Abs(a.Y - b.Y) < 0.5;
	private static long Quantize(double value) => (long)Math.Round(value * 2, MidpointRounding.AwayFromZero);

	private static void ResolveOverlappingLabels(List<RoutedEdge> routes, IReadOnlyList<LayoutEdge> inputEdges, bool useSideRouting)
	{
		const double labelGap = 4;
		var labeled = new List<(int Index, double X, double Y, double W, double H)>();

		for (var i = 0; i < routes.Count; i++)
		{
			var r = routes[i];
			if (r.LabelPosition is not { } lp || r.OriginalIndex >= inputEdges.Count)
				continue;
			var e = inputEdges[r.OriginalIndex];
			if (e.LabelWidth <= 0 || e.LabelHeight <= 0)
				continue;
			// This runs pre-DirectionTransform, in canonical (TD) space, where the X axis is
			// perpendicular to flow and Y runs along it. For LR/RL, DirectionTransform later
			// maps canonical Y -> visual X and canonical X -> visual Y, so the label's width
			// and height have to swap roles here to still compare against the right axis.
			var extentX = useSideRouting ? e.LabelHeight : e.LabelWidth;
			var extentY = useSideRouting ? e.LabelWidth : e.LabelHeight;
			labeled.Add((i, lp.X, lp.Y, extentX, extentY));
		}

		if (labeled.Count < 2)
			return;

		labeled.Sort((a, b) => a.Y.CompareTo(b.Y));

		for (var i = 1; i < labeled.Count; i++)
		{
			var prev = labeled[i - 1];
			var curr = labeled[i];

			var prevBottom = prev.Y + (prev.H / 2);
			var currTop = curr.Y - (curr.H / 2);

			if (currTop >= prevBottom + labelGap)
				continue;

			var prevRight = prev.X + (prev.W / 2);
			var prevLeft = prev.X - (prev.W / 2);
			var currRight = curr.X + (curr.W / 2);
			var currLeft = curr.X - (curr.W / 2);

			if (currLeft > prevRight || currRight < prevLeft)
				continue;

			var shift = prevBottom - currTop + labelGap;
			var newY = curr.Y + shift;
			routes[curr.Index].SetLabelPosition(new LayoutPoint(curr.X, newY));
			labeled[i] = (curr.Index, curr.X, newY, curr.W, curr.H);
		}
	}

	private static List<(int OriginalIndex, bool Reversed, List<int> Chain)> BuildEdgeChains(GraphBuffer graph)
	{
		var chains = new Dictionary<int, (bool Reversed, List<int> Chain)>();

		var virtualOutgoing = new Dictionary<int, (int To, int OriginalIndex, bool Reversed)>();
		var edgeStarts = new List<(int From, int To, int OriginalIndex, bool Reversed)>();

		foreach (var e in graph.Edges)
		{
			if (e.From < graph.RealNodeCount && !e.IsVirtual)
			{
				edgeStarts.Add((e.From, e.To, e.OriginalIndex, e.Reversed));
			}
			else if (e.IsVirtual && e.From >= graph.RealNodeCount)
			{
				virtualOutgoing[e.From] = (e.To, e.OriginalIndex, e.Reversed);
			}
			else if (e.IsVirtual && e.From < graph.RealNodeCount)
			{
				edgeStarts.Add((e.From, e.To, e.OriginalIndex, e.Reversed));
			}
		}

		foreach (var (from, to, origIdx, reversed) in edgeStarts)
		{
			if (chains.ContainsKey(origIdx))
				continue;

			var chain = new List<int> { from, to };
			var current = to;

			while (current >= graph.RealNodeCount && virtualOutgoing.TryGetValue(current, out var next))
			{
				chain.Add(next.To);
				current = next.To;
			}

			chains[origIdx] = (reversed, chain);
		}

		foreach (var e in graph.Edges)
		{
			if (!chains.ContainsKey(e.OriginalIndex) && !e.IsVirtual)
				chains[e.OriginalIndex] = (e.Reversed, [e.From, e.To]);
		}

		return chains.Select(kvp => (kvp.Key, kvp.Value.Reversed, kvp.Value.Chain)).ToList();
	}

	private const double SnapThreshold = 16;

	private static List<LayoutPoint> RouteChain(
		GraphBuffer graph, List<int> chain, bool useSideRouting, bool strictTopDownFanout = false, bool forceBottomExitFanOut = false)
	{
		var points = new List<LayoutPoint>(chain.Count * 2);

		for (var i = 0; i < chain.Count; i++)
		{
			var node = chain[i];
			var isReal = node < graph.RealNodeCount;
			var cx = isReal ? graph.X[node] + (graph.NodeWidths[node] / 2.0) : graph.X[node];
			var cy = isReal ? graph.Y[node] + (graph.NodeHeights[node] / 2.0) : graph.Y[node];

			if (i == 0)
			{
				AddSourcePort(graph, points, chain, node, cx, cy, isReal, useSideRouting, strictTopDownFanout, forceBottomExitFanOut);
			}
			else if (i == chain.Count - 1)
			{
				var chainSource = chain[0];
				var srcCX = chainSource < graph.RealNodeCount
					? graph.X[chainSource] + (graph.NodeWidths[chainSource] / 2.0)
					: graph.X[chainSource];
				AddTargetPort(graph, points, node, cx, cy, isReal, srcCX);
			}
			else
			{
				if (points.Count > 0)
				{
					var prev = points[^1];
					var dx = Math.Abs(prev.X - cx);
					if (dx is > 0.5 and <= SnapThreshold)
					{
						points.Add(new LayoutPoint(cx, cy));
					}
					else if (dx > SnapThreshold)
					{
						var midY = (prev.Y + cy) / 2.0;
						points.Add(new LayoutPoint(prev.X, midY));
						points.Add(new LayoutPoint(cx, midY));
						points.Add(new LayoutPoint(cx, cy));
					}
					else
					{
						points.Add(new LayoutPoint(cx, cy));
					}
				}
				else
				{
					points.Add(new LayoutPoint(cx, cy));
				}
			}
		}

		return points;
	}

	private const double SideEntryThreshold = 20;

	private static void AddTargetPort(
		GraphBuffer graph, List<LayoutPoint> points,
		int node, double cx, double cy, bool isReal, double sourceCX = double.NaN)
	{
		var prevPoint = points[^1];
		var portY = graph.Y[node];
		var deltaX = prevPoint.X - cx;
		var absDx = Math.Abs(deltaX);

		var srcDeltaX = double.IsNaN(sourceCX) ? deltaX : sourceCX - cx;
		var srcAbsDx = Math.Abs(srcDeltaX);

		if (absDx <= SnapThreshold && srcAbsDx <= SideEntryThreshold)
		{
			if (points.Count > 0)
				points[^1] = new LayoutPoint(cx, points[^1].Y);
			points.Add(new LayoutPoint(cx, portY));
			return;
		}

		// Side-entry only makes sense when the edge is approaching horizontally (LR-style).
		// When approaching from above (TD: prevPoint.Y < portY), mid-Y routing must be used
		// instead — side-entry would create a long vertical segment through sibling nodes.
		if (isReal && prevPoint.Y > portY - 2 && (absDx >= SideEntryThreshold || srcAbsDx >= SideEntryThreshold) && HasConvergentIncoming(graph, node))
		{
			var nodeLeft = graph.X[node];
			var nodeRight = nodeLeft + graph.NodeWidths[node];
			var effectiveDelta = srcAbsDx > absDx ? srcDeltaX : deltaX;
			if (effectiveDelta > 0)
			{
				points.Add(new LayoutPoint(prevPoint.X, cy));
				points.Add(new LayoutPoint(nodeRight, cy));
			}
			else
			{
				points.Add(new LayoutPoint(prevPoint.X, cy));
				points.Add(new LayoutPoint(nodeLeft, cy));
			}
			return;
		}

		var midY = FindClearBusY(graph, prevPoint.Y, portY, prevPoint.X, cx, node);
		points.Add(new LayoutPoint(prevPoint.X, midY));
		points.Add(new LayoutPoint(cx, midY));
		points.Add(new LayoutPoint(cx, portY));
	}

	/// <summary>
	/// Find a horizontal bus Y between yStart and yEnd that doesn't pass through
	/// any real node whose X range overlaps [xA, xB] (excluding the target node).
	/// Falls back to the geometric midpoint when no obstacle is found.
	/// </summary>
	private static double FindClearBusY(GraphBuffer graph, double yStart, double yEnd, double xA, double xB, int excludeNode)
	{
		var xMin = Math.Min(xA, xB);
		var xMax = Math.Max(xA, xB);
		var midY = (yStart + yEnd) / 2.0;

		// Collect Y ranges of real nodes that the horizontal bus would cross.
		var blockers = new List<(double Top, double Bottom)>();
		for (var i = 0; i < graph.RealNodeCount; i++)
		{
			if (i == excludeNode)
				continue;
			var nodeLeft = graph.X[i];
			var nodeRight = nodeLeft + graph.NodeWidths[i];
			if (nodeRight <= xMin || nodeLeft >= xMax)
				continue;
			var nodeTop = graph.Y[i];
			var nodeBottom = nodeTop + graph.NodeHeights[i];
			if (nodeTop >= yEnd || nodeBottom <= yStart)
				continue;
			if (midY > nodeTop && midY < nodeBottom)
				blockers.Add((nodeTop, nodeBottom));
		}

		if (blockers.Count == 0)
			return midY;

		// Try a gap below the last blocker (between blocker.bottom and yEnd).
		blockers.Sort((a, b) => a.Top.CompareTo(b.Top));
		var lastBottom = blockers[^1].Bottom;
		if (lastBottom < yEnd - 1)
			return (lastBottom + yEnd) / 2.0;

		// Try a gap above the first blocker (between yStart and blocker.top).
		var firstTop = blockers[0].Top;
		if (firstTop > yStart + 1)
			return (yStart + firstTop) / 2.0;

		return midY;
	}

	/// <summary>
	/// Determine the source exit point. For LR/RL layouts with side routing,
	/// edges to targets far above/below exit from the left/right side of the
	/// source node (which becomes top/bottom after direction transform).
	/// </summary>
	private static void AddSourcePort(
		GraphBuffer graph, List<LayoutPoint> points, List<int> chain,
		int node, double cx, double cy, bool isReal, bool useSideRouting,
		bool strictTopDownFanout = false, bool forceBottomExitFanOut = false)
	{
		if (!isReal || chain.Count < 2)
		{
			var portX = cx;
			var portY = graph.Y[node] + (isReal ? graph.NodeHeights[node] : 0);
			points.Add(new LayoutPoint(portX, portY));
			return;
		}

		var nextNode = chain[1];
		var nextCX = nextNode < graph.RealNodeCount
			? graph.X[nextNode] + (graph.NodeWidths[nextNode] / 2.0)
			: graph.X[nextNode];

		var deltaX = nextCX - cx;
		var halfW = graph.NodeWidths[node] / 2.0;

		if (useSideRouting)
		{
			if (deltaX < -halfW * 0.3)
			{
				points.Add(new LayoutPoint(graph.X[node], cy));
				points.Add(new LayoutPoint(nextCX, cy));
			}
			else if (deltaX > halfW * 0.3)
			{
				points.Add(new LayoutPoint(graph.X[node] + graph.NodeWidths[node], cy));
				points.Add(new LayoutPoint(nextCX, cy));
			}
			else
			{
				points.Add(new LayoutPoint(cx, graph.Y[node] + graph.NodeHeights[node]));
			}
		}
		else if (HasFanOut(graph, node))
		{
			var target = chain[^1];
			var tgtCX = target < graph.RealNodeCount
				? graph.X[target] + (graph.NodeWidths[target] / 2.0)
				: graph.X[target];

			var goRight = Math.Abs(deltaX) > SideEntryThreshold
				? deltaX > 0
				: FanOutSide(graph, node, target);

			var sideX = goRight
				? graph.X[node] + graph.NodeWidths[node]
				: graph.X[node];

			// Guard: fall back to bottom-center if the horizontal side-exit segment
			// would pass through a same-layer sibling, or if the exit point overshoots
			// the target (node right-edge is already past the target center, or vice-versa).
			// Both cases mean side-routing would draw across another node or produce a
			// hairpin jog; a straight bottom-exit and corridor route is cleaner.
			// Also fall back for direct (no-virtual-node) left-facing connections: the
			// horizontal segment at center-y crosses ancestor paths that also route through
			// the same left-side corridor.
			var exitOvershoot = goRight ? sideX > tgtCX : sideX < tgtCX;
			// In strict top-down mode (e.g. ER diagrams), use bottom exit for direct
			// left-facing connections to avoid crossing ancestor paths that route through
			// the same left-side corridor.
			var directLeftCrossing = strictTopDownFanout && !goRight && nextNode < graph.RealNodeCount;
			if (forceBottomExitFanOut || exitOvershoot || ExitCrossesSibling(graph, node, sideX, tgtCX) || directLeftCrossing)
			{
				points.Add(new LayoutPoint(cx, graph.Y[node] + graph.NodeHeights[node]));
			}
			else
			{
				points.Add(new LayoutPoint(sideX, cy));
				points.Add(new LayoutPoint(tgtCX, cy));
			}
		}
		else
		{
			points.Add(new LayoutPoint(cx, graph.Y[node] + graph.NodeHeights[node]));
		}
	}

	private static bool FanOutSide(GraphBuffer graph, int source, int target)
	{
		var targets = new List<int>();
		foreach (var e in graph.Edges)
		{
			if (e.From != source)
				continue;
			var finalTarget = ResolveVirtualChain(graph, e);
			if (finalTarget >= graph.RealNodeCount || targets.Contains(finalTarget))
				continue;
			targets.Add(finalTarget);
		}
		if (targets.Count < 2)
			return true;

		targets.Sort((a, b) => graph.X[a].CompareTo(graph.X[b]));
		var chainTarget = ResolveVirtualChain(graph, target);
		var idx = targets.IndexOf(chainTarget >= 0 ? chainTarget : target);
		return idx >= targets.Count / 2.0;
	}

	private static int ResolveVirtualChain(GraphBuffer graph, GraphEdge edge)
	{
		var current = edge.To;
		while (current >= graph.RealNodeCount)
		{
			var found = false;
			foreach (var ve in graph.Edges)
			{
				if (ve.From == current && ve.OriginalIndex == edge.OriginalIndex)
				{
					current = ve.To;
					found = true;
					break;
				}
			}
			if (!found)
				break;
		}
		return current;
	}

	private static int ResolveVirtualChain(GraphBuffer graph, int target)
	{
		if (target < graph.RealNodeCount)
			return target;
		return -1;
	}

	private static bool HasFanOut(GraphBuffer graph, int node)
	{
		if (node >= graph.RealNodeCount)
			return false;

		var cx = graph.X[node] + (graph.NodeWidths[node] / 2.0);
		var hasLeft = false;
		var hasRight = false;
		var distinctTargets = new HashSet<int>();

		foreach (var e in graph.Edges)
		{
			if (e.From != node)
				continue;

			var finalTarget = e.To;
			if (finalTarget >= graph.RealNodeCount)
			{
				var current = finalTarget;
				while (current >= graph.RealNodeCount)
				{
					var found = false;
					foreach (var ve in graph.Edges)
					{
						if (ve.From == current && ve.OriginalIndex == e.OriginalIndex)
						{
							current = ve.To;
							found = true;
							break;
						}
					}
					if (!found)
						break;
				}
				finalTarget = current;
			}

			if (finalTarget >= graph.RealNodeCount)
				continue;

			_ = distinctTargets.Add(finalTarget);
			var tgtCX = graph.X[finalTarget] + (graph.NodeWidths[finalTarget] / 2.0);
			if (tgtCX < cx - 10)
				hasLeft = true;
			if (tgtCX > cx + 10)
				hasRight = true;
		}
		return hasLeft && hasRight;
	}

	/// <summary>
	/// Returns true if the horizontal segment [segFromX, segToX] at source center-Y
	/// would pass through any real same-layer sibling node.
	/// </summary>
	private static bool ExitCrossesSibling(GraphBuffer graph, int node, double segFromX, double segToX)
	{
		var layer = graph.Layers[node];
		var segMin = Math.Min(segFromX, segToX);
		var segMax = Math.Max(segFromX, segToX);

		foreach (var sibling in graph.LayerNodes[layer])
		{
			if (sibling == node || sibling >= graph.RealNodeCount)
				continue;
			var sibLeft = graph.X[sibling];
			var sibRight = sibLeft + graph.NodeWidths[sibling];
			if (sibRight > segMin + 1 && sibLeft < segMax - 1)
				return true;
		}
		return false;
	}

	private static bool HasConvergentIncoming(GraphBuffer graph, int node)
	{
		var distinctSources = new HashSet<int>();
		foreach (var e in graph.Edges)
		{
			if (e.To != node)
				continue;

			var src = e.From;
			while (src >= graph.RealNodeCount)
			{
				var found = false;
				foreach (var ve in graph.Edges)
				{
					if (ve.To == src)
					{
						src = ve.From;
						found = true;
						break;
					}
				}
				if (!found)
					break;
			}
			if (src < graph.RealNodeCount)
				_ = distinctSources.Add(src);
		}
		return distinctSources.Count >= 2;
	}

	/// <summary>
	/// Route a reversed (back) edge with a detour to the right so it doesn't
	/// overlap with the forward edge on the same path.
	/// In canonical TD form: exits source right side, jogs right, goes down,
	/// enters target right side.
	/// </summary>
	/// <summary>
	/// Routes a single-layer reversed back-edge straight between the two nodes.
	/// In TD layout: exits source bottom-center, enters target top-center, with an
	/// L-bend at the midpoint when the nodes are horizontally offset.
	/// After <c>points.Reverse()</c> in <see cref="Run"/>, this produces an upward
	/// inheritance arrow from child-top to parent-bottom.
	/// </summary>
	private static List<LayoutPoint> RouteNaturalBackEdge(GraphBuffer graph, int source, int target)
	{
		var srcCX = graph.X[source] + (graph.NodeWidths[source] / 2.0);
		var srcBottom = graph.Y[source] + graph.NodeHeights[source];
		var tgtCX = graph.X[target] + (graph.NodeWidths[target] / 2.0);
		var tgtTop = graph.Y[target];

		if (Math.Abs(srcCX - tgtCX) < 1.0)
			return [new(srcCX, srcBottom), new(srcCX, tgtTop)];

		var midY = (srcBottom + tgtTop) / 2.0;

		// If the midY horizontal bus would pass through any intermediate node's Y range,
		// fall back to side routing which detours around the right edge of the diagram.
		for (var i = 0; i < graph.RealNodeCount; i++)
		{
			if (i == source || i == target)
				continue;
			var nodeTop = graph.Y[i];
			var nodeBottom = nodeTop + graph.NodeHeights[i];
			if (midY > nodeTop && midY < nodeBottom)
				return RouteBackEdge(graph, source, target);
		}

		return
		[
			new(srcCX, srcBottom),
			new(srcCX, midY),
			new(tgtCX, midY),
			new(tgtCX, tgtTop),
		];
	}

	private static List<LayoutPoint> RouteBackEdge(GraphBuffer graph, int source, int target)
	{
		const double detourGap = 36;

		var srcCY = graph.Y[source] + (graph.NodeHeights[source] / 2.0);
		var tgtCY = graph.Y[target] + (graph.NodeHeights[target] / 2.0);

		var maxRight = 0.0;
		for (var i = 0; i < graph.RealNodeCount; i++)
		{
			var right = graph.X[i] + graph.NodeWidths[i];
			if (right > maxRight)
				maxRight = right;
		}
		var detourX = maxRight + detourGap;

		var srcRight = graph.X[source] + graph.NodeWidths[source];
		var tgtRight = graph.X[target] + graph.NodeWidths[target];

		return
		[
			new(srcRight, srcCY),
			new(detourX, srcCY),
			new(detourX, tgtCY),
			new(tgtRight, tgtCY),
		];
	}

	/// <summary>
	/// Place the label at the midpoint of the longest straight segment,
	/// biased toward the start so it doesn't overlap arrowheads at the end.
	/// Clamps the Y position to keep a minimum visible gap from source/target nodes.
	/// </summary>
	private static LayoutPoint? ComputeLabelPosition(List<LayoutPoint> points, double srcBottom, double tgtTop)
	{
		if (points.Count < 2)
			return null;

		// Collect all straight-line segments from the edge path.
		var segments = new List<(int Start, int End, double Len, bool IsVertical)>();
		var runStart = 0;
		for (var i = 1; i < points.Count; i++)
		{
			var isCollinear = i < points.Count - 1 &&
				Math.Abs(points[i].X - points[runStart].X) < 0.5 &&
				Math.Abs(points[i + 1].X - points[runStart].X) < 0.5;

			if (!isCollinear || i == points.Count - 1)
			{
				var rdx = points[i].X - points[runStart].X;
				var rdy = points[i].Y - points[runStart].Y;
				var runLen = Math.Sqrt((rdx * rdx) + (rdy * rdy));
				segments.Add((runStart, i, runLen, Math.Abs(rdy) >= Math.Abs(rdx)));
				runStart = i;
			}
		}

		// Prefer placing the label on a vertical (flow-direction) segment so that fan-out
		// edges from a shared source each get a label on their own descent column at unique
		// X positions, rather than all clustering on the shared horizontal spread.
		// Among vertical segments use the last one (nearest the target) which is always
		// unique per edge in a fan-out. Fall back to the longest segment if there are none.
		int bestStart, bestEnd;
		var verticals = segments.Where(s => s.IsVertical && s.Len > 1.0).ToList();
		if (verticals.Count > 0)
		{
			var last = verticals[^1];
			bestStart = last.Start;
			bestEnd = last.End;
		}
		else
		{
			var best = segments.MaxBy(s => s.Len);
			bestStart = best.Start;
			bestEnd = best.End;
		}

		const double t = 0.5;
		var x = points[bestStart].X + ((points[bestEnd].X - points[bestStart].X) * t);
		var y = points[bestStart].Y + ((points[bestEnd].Y - points[bestStart].Y) * t);

		// For non-bent edges, clamp Y between the source and target nodes.
		// Back-edges route around the diagram so clamping is only valid when the
		// label Y already falls within the src→tgt band.
		var yLo = Math.Min(srcBottom, tgtTop);
		var yHi = Math.Max(srcBottom, tgtTop);
		if (y >= yLo && y <= yHi)
		{
			y = Math.Max(y, srcBottom + MinGapFromNode);
			y = Math.Min(y, tgtTop - MinGapFromNode);
		}

		return new LayoutPoint(x, y);
	}
}
