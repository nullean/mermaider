namespace Sugiyama.Internal;

/// <summary>
/// Orthogonal routes for port-aware (ER) layouts: exit port → short jog next to the source → column
/// (the per-edge gap dummy computed by BK, which is where the label sits) → short jog next to the
/// target → entry port. Long edges run straight through their virtual-node column in between.
/// </summary>
internal static class ErEdgeRouter
{
	private const double Stub = 10;
	private const double SnapTolerance = 7;

	internal static List<EdgeRouter.RoutedEdge> Run(GraphBuffer graph, IReadOnlyList<LayoutEdge> inputEdges, bool useSideRouting)
	{
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
		var chains = new List<List<int>>(starts.Count);
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
			chains.Add(chain);
		}

		ReduceCrossingsByVariants(graph, results, chains, inputEdges, useSideRouting);
		SlotHorizontalRuns(graph, results);
		return results;
	}

	/// <summary>
	/// For short (single-gap) edges that cross another edge, try moving the single horizontal run to the top or bottom of the gap
	/// (vertical at the exit or entry port instead of a separate label column). Keep a variant only if it strictly reduces that edge's
	/// crossings and its label stays clear of every other edge and label.
	/// </summary>
	private static void ReduceCrossingsByVariants(
		GraphBuffer g, List<EdgeRouter.RoutedEdge> routes, List<List<int>> chains, IReadOnlyList<LayoutEdge> input, bool horizontalFlow)
	{
		bool Shares(int a, int b)
		{
			var ea = input[routes[a].OriginalIndex];
			var eb = input[routes[b].OriginalIndex];
			return ea.Source == eb.Source || ea.Target == eb.Target || ea.Source == eb.Target || ea.Target == eb.Source;
		}

		int CrossingsOf(int idx, List<LayoutPoint> pts)
		{
			var n = 0;
			for (var o = 0; o < routes.Count; o++)
			{
				if (o != idx && !Shares(idx, o) && PathsCross(pts, routes[o].Points))
					n++;
			}
			return n;
		}

		(double W, double H) LabelSize(int idx)
		{
			var e = input[routes[idx].OriginalIndex];
			return horizontalFlow ? (e.LabelHeight, e.LabelWidth) : (e.LabelWidth, e.LabelHeight);
		}

		bool LabelClear(int idx, LayoutPoint at)
		{
			var (w, h) = LabelSize(idx);
			double x0 = at.X - (w / 2), x1 = at.X + (w / 2), y0 = at.Y - (h / 2), y1 = at.Y + (h / 2);
			for (var o = 0; o < routes.Count; o++)
			{
				if (o == idx)
					continue;
				if (routes[o].LabelPosition is { } lp)
				{
					var (ow, oh) = LabelSize(o);
					if (x1 + 4 > lp.X - (ow / 2) && x0 - 4 < lp.X + (ow / 2) && y1 + 4 > lp.Y - (oh / 2) && y0 - 4 < lp.Y + (oh / 2))
						return false;
				}
				var pts = routes[o].Points;
				for (var k = 0; k < pts.Count - 1; k++)
				{
					double sx0 = Math.Min(pts[k].X, pts[k + 1].X), sx1 = Math.Max(pts[k].X, pts[k + 1].X);
					double sy0 = Math.Min(pts[k].Y, pts[k + 1].Y), sy1 = Math.Max(pts[k].Y, pts[k + 1].Y);
					if (sx1 > x0 + 1 && sx0 < x1 - 1 && sy1 > y0 + 1 && sy0 < y1 - 1)
						return false;
				}
			}
			return true;
		}

		for (var pass = 0; pass < 3; pass++)
		{
			var improved = false;
			for (var i = 0; i < routes.Count; i++)
			{
				if (chains[i].Count != 1 || routes[i].OriginalIndex >= input.Count)
					continue;
				var current = CrossingsOf(i, routes[i].Points);
				if (current == 0)
					continue;

				for (var variant = 1; variant <= 2; variant++)
				{
					var pts = Route(g, chains[i], out var label, variant);
					if (routes[i].Reversed)
						pts.Reverse();
					var hasLabel = input[routes[i].OriginalIndex].LabelWidth > 0;
					if (CrossingsOf(i, pts) >= current || (hasLabel && (label is null || !LabelClear(i, label.Value))))
						continue;
					routes[i].ReplacePoints(pts);
					if (hasLabel)
						routes[i].SetLabelPosition(label!.Value);
					improved = true;
					break;
				}
			}

			if (!improved)
				break;
		}
	}

	/// <summary>Number of edge pairs (sharing no endpoint) whose routes cross.</summary>
	internal static int CountCrossings(List<EdgeRouter.RoutedEdge> routes, IReadOnlyList<LayoutEdge> inputEdges)
	{
		var crossings = 0;
		for (var a = 0; a < routes.Count; a++)
		{
			for (var b = a + 1; b < routes.Count; b++)
			{
				if (routes[a].OriginalIndex >= inputEdges.Count || routes[b].OriginalIndex >= inputEdges.Count)
					continue;
				var ea = inputEdges[routes[a].OriginalIndex];
				var eb = inputEdges[routes[b].OriginalIndex];
				if (ea.Source == eb.Source || ea.Target == eb.Target || ea.Source == eb.Target || ea.Target == eb.Source)
					continue;
				if (PathsCross(routes[a].Points, routes[b].Points))
					crossings++;
			}
		}
		return crossings;
	}

	private static bool PathsCross(List<LayoutPoint> a, List<LayoutPoint> b)
	{
		for (var i = 0; i < a.Count - 1; i++)
		{
			for (var j = 0; j < b.Count - 1; j++)
			{
				if (SegmentsCross(a[i], a[i + 1], b[j], b[j + 1]))
					return true;
			}
		}
		return false;
	}

	private static bool SegmentsCross(LayoutPoint p, LayoutPoint q, LayoutPoint r, LayoutPoint s)
	{
		var d1x = q.X - p.X;
		var d1y = q.Y - p.Y;
		var d2x = s.X - r.X;
		var d2y = s.Y - r.Y;
		var den = (d1x * d2y) - (d1y * d2x);
		if (Math.Abs(den) < 1e-9)
			return false;
		var t = (((r.X - p.X) * d2y) - ((r.Y - p.Y) * d2x)) / den;
		var u = (((r.X - p.X) * d1y) - ((r.Y - p.Y) * d1x)) / den;
		return t is > 1e-3 and < 1 - 1e-3 && u is > 1e-3 and < 1 - 1e-3;
	}

	private sealed class HRun
	{
		internal List<LayoutPoint> Pts = [];
		internal int I;
		internal double Y;
		internal double Xl;
		internal double Xr;
		internal double XUp = double.NaN;
		internal double XDown = double.NaN;
		internal int Gap;
	}

	/// <summary>
	/// Horizontal runs of different edges that overlap in x and sit at (nearly) the same y would merge into one
	/// line. Give each run in such a cluster its own y slot, ordered so no run's vertical stub/column crosses
	/// another run of the cluster when that can be avoided.
	/// </summary>
	private static void SlotHorizontalRuns(GraphBuffer g, List<EdgeRouter.RoutedEdge> routes)
	{
		const double slotStep = 7;
		const double clusterDy = 12;

		var layerTop = new double[g.LayerCount];
		var layerBottom = new double[g.LayerCount];
		for (var li = 0; li < g.LayerCount; li++)
		{
			layerTop[li] = double.MaxValue;
			layerBottom[li] = double.MinValue;
			foreach (var v in g.LayerNodes[li])
			{
				layerTop[li] = Math.Min(layerTop[li], g.Y[v]);
				layerBottom[li] = Math.Max(layerBottom[li], v < g.RealNodeCount ? g.Y[v] + g.NodeHeights[v] : g.Y[v]);
			}
		}

		const int bandBase = 100000;
		var minHeight = new double[g.LayerCount];
		for (var li = 0; li < g.LayerCount; li++)
		{
			minHeight[li] = double.MaxValue;
			foreach (var v in g.LayerNodes[li])
			{
				if (v < g.RealNodeCount)
					minHeight[li] = Math.Min(minHeight[li], g.NodeHeights[v]);
			}
			if (minHeight[li] == double.MaxValue)
				minHeight[li] = 30;
		}

		int GapOf(double y)
		{
			for (var li = 0; li < g.LayerCount - 1; li++)
			{
				if (y >= layerBottom[li] - 0.5 && y <= layerTop[li + 1] + 0.5)
					return y >= layerTop[li + 1] - 0.5 ? bandBase + li + 1 : li;
			}

			// A long edge's jog between virtual columns sits a few px below its layer's top: slot it inside that band.
			for (var li = 0; li < g.LayerCount; li++)
			{
				if (y > layerTop[li] + 0.5 && y < layerTop[li] + 16)
					return bandBase + li;
			}
			return -1;
		}

		var runs = new List<HRun>();
		foreach (var r in routes)
		{
			var pts = r.Points;
			for (var i = 1; i + 2 < pts.Count; i++)
			{
				if (Math.Abs(pts[i].Y - pts[i + 1].Y) > 0.01 || Math.Abs(pts[i].X - pts[i + 1].X) < 2)
					continue;
				var y = pts[i].Y;
				var gap = GapOf(y);
				if (gap < 0)
					continue;
				var run = new HRun
				{
					Pts = pts,
					I = i,
					Y = y,
					Xl = Math.Min(pts[i].X, pts[i + 1].X),
					Xr = Math.Max(pts[i].X, pts[i + 1].X),
					Gap = gap,
				};
				if (pts[i - 1].Y < y)
					run.XUp = pts[i].X;
				else
					run.XDown = pts[i].X;
				if (pts[i + 2].Y < y)
					run.XUp = pts[i + 1].X;
				else
					run.XDown = pts[i + 1].X;
				runs.Add(run);
			}
		}

		var parent = Enumerable.Range(0, runs.Count).ToArray();
		int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
		for (var a = 0; a < runs.Count; a++)
		{
			for (var b = a + 1; b < runs.Count; b++)
			{
				if (runs[a].Gap != runs[b].Gap || ReferenceEquals(runs[a].Pts, runs[b].Pts))
					continue;
				if (Math.Abs(runs[a].Y - runs[b].Y) > clusterDy)
					continue;
				if (Math.Min(runs[a].Xr, runs[b].Xr) - Math.Max(runs[a].Xl, runs[b].Xl) < 2)
					continue;
				parent[Find(a)] = Find(b);
			}
		}

		foreach (var group in runs.Select((r, idx) => (r, idx)).GroupBy(t => Find(t.idx)))
		{
			var members = group.Select(t => t.r).ToList();
			if (members.Count < 2)
				continue;

			var n = members.Count;
			var before = new List<int>[n];
			var indeg = new int[n];
			for (var i = 0; i < n; i++)
				before[i] = [];

			// a above b (a.Y < b.Y) avoids: a's up-vertical crossing b (needs b below a) and a's down-vertical crossing b (needs b above a).
			for (var a = 0; a < n; a++)
			{
				for (var b = 0; b < n; b++)
				{
					if (a == b || Math.Min(members[a].Xr, members[b].Xr) - Math.Max(members[a].Xl, members[b].Xl) < 2)
						continue;
					var up = members[a].XUp;
					var down = members[a].XDown;
					if (!double.IsNaN(up) && up > members[b].Xl && up < members[b].Xr)
					{
						before[a].Add(b); // a above b
						indeg[b]++;
					}
					if (!double.IsNaN(down) && down > members[b].Xl && down < members[b].Xr)
					{
						before[b].Add(a); // b above a
						indeg[a]++;
					}
				}
			}

			var rank = new int[n];
			var placed = new bool[n];
			for (var k = 0; k < n; k++)
			{
				var pick = -1;
				for (var i = 0; i < n && pick < 0; i++)
				{
					if (!placed[i] && indeg[i] == 0)
						pick = i;
				}
				if (pick < 0)
				{
					for (var i = 0; i < n; i++)
					{
						if (!placed[i] && (pick < 0 || indeg[i] < indeg[pick]))
							pick = i;
					}
				}
				placed[pick] = true;
				rank[pick] = k;
				foreach (var t in before[pick])
					indeg[t]--;
			}

			var gap = members[0].Gap;
			double lo;
			double hi;
			if (gap >= bandBase)
			{
				var band = gap - bandBase;
				lo = layerTop[band] + 3;
				hi = layerTop[band] + Math.Max(12, Math.Min(34, minHeight[band] - 3));
			}
			else
			{
				lo = layerBottom[gap] + 4;
				hi = layerTop[gap + 1] - 4;
			}

			var baseY = members.Average(m => m.Y);
			var step = Math.Min(slotStep, Math.Max(2, (hi - lo) / (n + 1)));
			var first = baseY - (step * (n - 1) / 2);
			first = Math.Max(lo, Math.Min(first, hi - (step * (n - 1))));
			for (var i = 0; i < n; i++)
			{
				var y = first + (rank[i] * step);
				var m = members[i];
				m.Pts[m.I] = new LayoutPoint(m.Pts[m.I].X, y);
				m.Pts[m.I + 1] = new LayoutPoint(m.Pts[m.I + 1].X, y);
			}
		}
	}

	// A virtual node has no body: its "bottom" is the bottom of the row it passes through, so jogs happen in the gap below
	// the row instead of inside it (where they would run under the row's entities).
	private static double BottomOf(GraphBuffer g, int node)
	{
		if (node < g.RealNodeCount)
			return g.Y[node] + g.NodeHeights[node];
		var bottom = g.Y[node];
		foreach (var v in g.LayerNodes[g.Layers[node]])
		{
			if (v < g.RealNodeCount)
				bottom = Math.Max(bottom, g.Y[v] + g.NodeHeights[v]);
		}
		return bottom;
	}

	private static double CentreX(GraphBuffer g, int node) => g.X[node] + (node < g.RealNodeCount ? g.NodeWidths[node] / 2.0 : 0);

	private static List<LayoutPoint> Route(GraphBuffer g, List<int> chain, out LayoutPoint? labelPos, int variant = 0)
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

			// Variants for single-gap edges: 1 = vertical at the exit port (one run at the bottom), 2 = vertical at the entry port (one run at the top).
			if (variant != 0 && chain.Count == 1)
				col = variant == 1 ? srcPortX : entryX;

			// A jog of a few px reads as a slanted line once corners are rounded: run straight instead.
			if (!double.IsNaN(col))
			{
				if (i == 0 && Math.Abs(col - srcPortX) < SnapTolerance)
					col = srcPortX;
				else if (i == chain.Count - 1 && Math.Abs(col - nextX) < SnapTolerance)
					col = nextX;
			}

			var toVirtual = i < chain.Count - 1;
			if (!double.IsNaN(col))
			{
				var yTop = bottomA + Stub;
				var yBot = topB - Stub;
				if (yTop > yBot)
					yTop = yBot = (bottomA + topB) / 2;
				if (toVirtual)
				{
					// Long edge: jog straight to the virtual-node column right below the source (like ELK), not along the next
					// layer's tops, so the line never looks attached to a node it merely passes. The label rides that column.
					requests.Add((nextX, yTop));
					labelPos ??= new LayoutPoint(nextX, (bottomA + topB) / 2);
				}
				else
				{
					requests.Add((col, yTop));
					requests.Add((nextX, yBot));
					labelPos ??= new LayoutPoint(col, (bottomA + topB) / 2);
				}
			}
			else if (toVirtual)
			{
				requests.Add((nextX, bottomA + Stub));
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
