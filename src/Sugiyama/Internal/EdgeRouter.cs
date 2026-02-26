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
			List<LayoutPoint> points;
			if (reversed && chain.Count == 2 && chain[0] < graph.RealNodeCount && chain[1] < graph.RealNodeCount)
				points = RouteBackEdge(graph, chain[0], chain[1]);
			else
				points = RouteChain(graph, chain, useSideRouting);

			var labelPos = ComputeLabelPosition(points);

			if (reversed)
				points.Reverse();

			results.Add(new RoutedEdge(origIdx, reversed, points, labelPos));
		}

		return results;
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
			if (chains.ContainsKey(origIdx)) continue;

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
			var cx = isReal ? graph.X[node] + graph.NodeWidths[node] / 2.0 : graph.X[node];
			var cy = isReal ? graph.Y[node] + graph.NodeHeights[node] / 2.0 : graph.Y[node];

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
			? graph.X[target] + graph.NodeWidths[target] / 2.0
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
		const double detourGap = 20;

		var srcRight = graph.X[source] + graph.NodeWidths[source];
		var tgtRight = graph.X[target] + graph.NodeWidths[target];
		var srcCY = graph.Y[source] + graph.NodeHeights[source] / 2.0;
		var tgtCY = graph.Y[target] + graph.NodeHeights[target] / 2.0;

		var detourX = Math.Max(srcRight, tgtRight) + detourGap;

		return
		[
			new(srcRight, srcCY),
			new(detourX, srcCY),
			new(detourX, tgtCY),
			new(tgtRight, tgtCY),
		];
	}

	private static LayoutPoint? ComputeLabelPosition(List<LayoutPoint> points)
	{
		if (points.Count < 2) return null;

		var totalLength = 0.0;
		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			totalLength += Math.Sqrt(dx * dx + dy * dy);
		}

		var halfLength = totalLength / 2.0;
		var accumulated = 0.0;

		for (var i = 1; i < points.Count; i++)
		{
			var dx = points[i].X - points[i - 1].X;
			var dy = points[i].Y - points[i - 1].Y;
			var segLen = Math.Sqrt(dx * dx + dy * dy);

			if (accumulated + segLen >= halfLength)
			{
				var t = segLen > 0 ? (halfLength - accumulated) / segLen : 0;
				return new LayoutPoint(
					points[i - 1].X + dx * t,
					points[i - 1].Y + dy * t);
			}

			accumulated += segLen;
		}

		return new LayoutPoint(
			(points[0].X + points[^1].X) / 2.0,
			(points[0].Y + points[^1].Y) / 2.0);
	}
}
