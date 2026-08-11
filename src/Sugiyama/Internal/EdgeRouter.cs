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

	private const double MinGapFromNode = 42;

	internal static List<RoutedEdge> Run(
		GraphBuffer graph, bool useSideRouting = false,
		IReadOnlyList<LayoutEdge>? inputEdges = null)
	{
		var edgeChains = BuildEdgeChains(graph);
		var results = new List<RoutedEdge>(edgeChains.Count);

		foreach (var (origIdx, reversed, chain) in edgeChains)
		{
			var points = reversed && chain[0] < graph.RealNodeCount && chain[^1] < graph.RealNodeCount
				? RouteBackEdge(graph, chain[0], chain[^1])
				: RouteChain(graph, chain, useSideRouting);

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

		if (inputEdges is not null)
			ResolveOverlappingLabels(results, inputEdges);

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

	private static bool SameX(LayoutPoint a, LayoutPoint b) => Math.Abs(a.X - b.X) < 0.5;
	private static bool SameY(LayoutPoint a, LayoutPoint b) => Math.Abs(a.Y - b.Y) < 0.5;
	private static long Quantize(double value) => (long)Math.Round(value * 2, MidpointRounding.AwayFromZero);

	private static void ResolveOverlappingLabels(List<RoutedEdge> routes, IReadOnlyList<LayoutEdge> inputEdges)
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
			labeled.Add((i, lp.X, lp.Y, e.LabelWidth, e.LabelHeight));
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
		GraphBuffer graph, List<int> chain, bool useSideRouting)
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
				AddSourcePort(graph, points, chain, node, cx, cy, isReal, useSideRouting);
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

		if (isReal && (absDx >= SideEntryThreshold || srcAbsDx >= SideEntryThreshold) && HasConvergentIncoming(graph, node))
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

		var midY = (prevPoint.Y + portY) / 2.0;
		points.Add(new LayoutPoint(prevPoint.X, midY));
		points.Add(new LayoutPoint(cx, midY));
		points.Add(new LayoutPoint(cx, portY));
	}

	/// <summary>
	/// Determine the source exit point. For LR/RL layouts with side routing,
	/// edges to targets far above/below exit from the left/right side of the
	/// source node (which becomes top/bottom after direction transform).
	/// </summary>
	private static void AddSourcePort(
		GraphBuffer graph, List<LayoutPoint> points, List<int> chain,
		int node, double cx, double cy, bool isReal, bool useSideRouting)
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
			points.Add(new LayoutPoint(sideX, cy));
			points.Add(new LayoutPoint(tgtCX, cy));
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

		var bestStart = 0;
		var bestEnd = 1;
		var bestLen = 0.0;

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
				if (runLen > bestLen)
				{
					bestLen = runLen;
					bestStart = runStart;
					bestEnd = i;
				}
				runStart = i;
			}
		}

		const double t = 0.5;
		var x = points[bestStart].X + ((points[bestEnd].X - points[bestStart].X) * t);
		var y = (srcBottom + tgtTop) / 2.0;

		y = Math.Max(y, srcBottom + MinGapFromNode);
		y = Math.Min(y, tgtTop - MinGapFromNode);

		return new LayoutPoint(x, y);
	}
}
