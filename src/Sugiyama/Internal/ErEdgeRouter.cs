namespace Sugiyama.Internal;

/// <summary>
/// Orthogonal routes for port-aware (ER) layouts: exit port → short jog next to the source → column
/// (the per-edge gap dummy computed by BK, which is where the label sits) → short jog next to the
/// target → entry port. Long edges run straight through their virtual-node column in between.
/// </summary>
internal static class ErEdgeRouter
{
	private const double Stub = 10;

	internal static List<EdgeRouter.RoutedEdge> Run(GraphBuffer graph, IReadOnlyList<LayoutEdge> inputEdges, bool useSideRouting)
	{
		_ = inputEdges;
		_ = useSideRouting;
		var edges = graph.Edges;
		var next = new Dictionary<(int From, int Orig), int>();
		var starts = new List<int>();
		for (var ei = 0; ei < edges.Count; ei++)
		{
			var e = edges[ei];
			if (e.From >= graph.RealNodeCount)
				next[(e.From, e.OriginalIndex)] = ei;
			else
				starts.Add(ei);
		}

		var results = new List<EdgeRouter.RoutedEdge>(starts.Count);
		foreach (var first in starts)
		{
			var chain = new List<int> { first };
			var cur = edges[first].To;
			while (cur >= graph.RealNodeCount && next.TryGetValue((cur, edges[first].OriginalIndex), out var ne))
			{
				chain.Add(ne);
				cur = edges[ne].To;
			}

			var points = Route(graph, chain, out var labelPos);
			if (edges[first].Reversed)
				points.Reverse();
			results.Add(new EdgeRouter.RoutedEdge(edges[first].OriginalIndex, edges[first].Reversed, points, labelPos));
		}

		return results;
	}

	private static double BottomOf(GraphBuffer g, int node) => node < g.RealNodeCount ? g.Y[node] + g.NodeHeights[node] : g.Y[node];

	private static double CentreX(GraphBuffer g, int node) => g.X[node] + (node < g.RealNodeCount ? g.NodeWidths[node] / 2.0 : 0);

	private static List<LayoutPoint> Route(GraphBuffer g, List<int> chain, out LayoutPoint? labelPos)
	{
		var edges = g.Edges;
		var src = edges[chain[0]].From;
		var tgt = edges[chain[^1]].To;
		labelPos = null;

		var srcPortX = g.X[src] + (g.PortOutOff.Length > chain[0] && g.PortOutOff[chain[0]] > 0 ? g.PortOutOff[chain[0]] : g.NodeWidths[src] / 2.0);
		var entryX = g.X[tgt] + (g.PortInOff.Length > chain[^1] && g.PortInOff[chain[^1]] > 0 ? g.PortInOff[chain[^1]] : g.NodeWidths[tgt] / 2.0);
		var srcBottom = BottomOf(g, src);
		var tgtTop = g.Y[tgt];

		var requests = new List<(double X, double Y)>();
		for (var i = 0; i < chain.Count; i++)
		{
			var e = edges[chain[i]];
			var a = e.From;
			var b = e.To;
			var bottomA = BottomOf(g, a);
			var topB = g.Y[b];
			var col = g.ColumnX.Length > chain[i] ? g.ColumnX[chain[i]] : double.NaN;
			var nextX = i == chain.Count - 1 ? entryX : CentreX(g, b);

			if (!double.IsNaN(col))
			{
				var yTop = bottomA + Stub;
				var yBot = topB - Stub;
				if (yTop > yBot)
					yTop = yBot = (bottomA + topB) / 2;
				requests.Add((col, yTop));
				requests.Add((nextX, yBot));
				labelPos ??= new LayoutPoint(col, (bottomA + topB) / 2);
			}
			else
			{
				requests.Add((nextX, (bottomA + topB) / 2));
			}
		}

		var pts = new List<LayoutPoint> { new(srcPortX, srcBottom) };
		var curX = srcPortX;
		foreach (var (x, y) in requests)
		{
			if (Math.Abs(x - curX) < 0.01)
				continue;
			pts.Add(new LayoutPoint(curX, y));
			pts.Add(new LayoutPoint(x, y));
			curX = x;
		}
		pts.Add(new LayoutPoint(entryX, tgtTop));

		return Simplify(pts);
	}

	/// <summary>Drop duplicate points and middle points of straight runs.</summary>
	private static List<LayoutPoint> Simplify(List<LayoutPoint> pts)
	{
		var dedup = new List<LayoutPoint>(pts.Count);
		foreach (var p in pts)
		{
			if (dedup.Count > 0 && Math.Abs(dedup[^1].X - p.X) < 0.01 && Math.Abs(dedup[^1].Y - p.Y) < 0.01)
				continue;
			dedup.Add(p);
		}

		var result = new List<LayoutPoint>(dedup.Count);
		for (var i = 0; i < dedup.Count; i++)
		{
			if (i > 0 && i < dedup.Count - 1)
			{
				var a = result[^1];
				var b = dedup[i];
				var c = dedup[i + 1];
				var vertical = Math.Abs(a.X - b.X) < 0.01 && Math.Abs(b.X - c.X) < 0.01;
				var horizontal = Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(b.Y - c.Y) < 0.01;
				if (vertical || horizontal)
					continue;
			}
			result.Add(dedup[i]);
		}
		return result;
	}
}
