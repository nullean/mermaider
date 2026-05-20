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
	private const double SnapEpsilon = 8;

	internal sealed class RoutedEdge(
		int originalIndex,
		bool reversed,
		List<LayoutPoint> points,
		LayoutPoint? labelPosition)
	{
		internal int OriginalIndex { get; } = originalIndex;
		internal bool Reversed { get; } = reversed;
		internal List<LayoutPoint> Points { get; } = points;
		internal LayoutPoint? LabelPosition { get; private set; } = labelPosition;

		internal void SetLabelPosition(LayoutPoint lp) => LabelPosition = lp;
	}

	internal static List<RoutedEdge> Run(GraphBuffer graph, bool useSideRouting = false)
	{
		var edgeChains = BuildEdgeChains(graph);
		var results = new List<RoutedEdge>(edgeChains.Count);

		foreach (var (origIdx, reversed, chain) in edgeChains)
		{
			var points = reversed && chain[0] < graph.RealNodeCount && chain[^1] < graph.RealNodeCount
				? RouteBackEdge(graph, chain[0], chain[^1])
				: RouteChain(graph, chain, useSideRouting);

			if (reversed)
				points.Reverse();

			results.Add(new RoutedEdge(origIdx, reversed, points, labelPosition: null));
		}

		CleanupRoutes(results);

		return results;
	}

	internal static void CleanupRoutes(List<RoutedEdge> edges)
	{
		SnapNearAlignedDoglegs(edges);
		SnapSharedHorizontalTrunks(edges);
		foreach (var edge in edges)
		{
			if (ComputeLabelPosition(edge.Points) is { } labelPosition)
				edge.SetLabelPosition(labelPosition);
		}
	}

	private static void SnapNearAlignedDoglegs(IEnumerable<RoutedEdge> edges)
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
			points.Clear();
			points.Add(new LayoutPoint(snappedX, start.Y));
			points.Add(new LayoutPoint(snappedX, end.Y));
		}
	}

	private static void SnapSharedHorizontalTrunks(IEnumerable<RoutedEdge> edges)
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
				var portX = cx;
				var portY = graph.Y[node];
				var prevPoint = points[^1];

				if (Math.Abs(prevPoint.X - portX) > 0.5)
				{
					var midY = (prevPoint.Y + portY) / 2.0;
					points.Add(new LayoutPoint(prevPoint.X, midY));
					points.Add(new LayoutPoint(portX, midY));
				}

				points.Add(new LayoutPoint(portX, portY));
			}
			else
			{
				if (points.Count > 0)
				{
					var prev = points[^1];
					if (Math.Abs(prev.X - cx) > 0.5)
					{
						var midY = (prev.Y + cy) / 2.0;
						points.Add(new LayoutPoint(prev.X, midY));
						points.Add(new LayoutPoint(cx, midY));
					}
				}
				points.Add(new LayoutPoint(cx, cy));
			}
		}

		return points;
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
		if (!useSideRouting || !isReal || chain.Count < 2)
		{
			var portX = cx;
			var portY = graph.Y[node] + (isReal ? graph.NodeHeights[node] : 0);
			points.Add(new LayoutPoint(portX, portY));
			return;
		}

		var target = chain[^1];
		var tgtCX = target < graph.RealNodeCount
			? graph.X[target] + (graph.NodeWidths[target] / 2.0)
			: graph.X[target];

		var deltaX = tgtCX - cx;
		var halfW = graph.NodeWidths[node] / 2.0;

		if (deltaX < -halfW * 0.3)
		{
			// Target is to the LEFT in canonical form → top side in LR
			points.Add(new LayoutPoint(graph.X[node], cy));
			points.Add(new LayoutPoint(tgtCX, cy));
		}
		else if (deltaX > halfW * 0.3)
		{
			// Target is to the RIGHT in canonical form → bottom side in LR
			points.Add(new LayoutPoint(graph.X[node] + graph.NodeWidths[node], cy));
			points.Add(new LayoutPoint(tgtCX, cy));
		}
		else
		{
			// Target roughly aligned → normal bottom exit (right side in LR)
			var portX = cx;
			var portY = graph.Y[node] + graph.NodeHeights[node];
			points.Add(new LayoutPoint(portX, portY));
		}
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
	/// </summary>
	private static LayoutPoint? ComputeLabelPosition(List<LayoutPoint> points)
	{
		if (points.Count < 2)
			return null;

		var bestIdx = 1;
		var bestLen = 0.0;

		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			var segLen = Math.Sqrt((dx * dx) + (dy * dy));
			if (segLen > bestLen)
			{
				bestLen = segLen;
				bestIdx = i;
			}
		}

		// Bias toward the start of the segment (t=0.4 instead of 0.5)
		// to keep labels away from arrowheads at the end
		const double t = 0.4;
		return new LayoutPoint(
			points[bestIdx - 1].X + ((points[bestIdx].X - points[bestIdx - 1].X) * t),
			points[bestIdx - 1].Y + ((points[bestIdx].Y - points[bestIdx - 1].Y) * t));
	}
}
